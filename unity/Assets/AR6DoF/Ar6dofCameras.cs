using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Rôle d'une caméra supplémentaire.</summary>
    public static class Ar6dofCamRole
    {
        public const int Night = 0, Thermal = 1, Other = 2;
        public static readonly string[] Names = { "Vision nocturne", "Thermique", "Autre (retroviseur, miroir...)" };
        public static readonly string[] Palettes = { "Chaud = blanc", "Chaud = noir", "Fer (noir, rouge, jaune)", "Arc-en-ciel" };
        public static readonly string[] Tints = { "Vert", "Blanc", "Couleurs d'origine" };
        public static readonly string[] Layouts = { "Une seule image (faux stereo)", "Deux images cote a cote", "Deux images haut / bas" };
    }

    /// <summary>Réglages d'une caméra supplémentaire. Tout est enregistré dans ar6dof_cameras.json (voir Ar6dofCameras).</summary>
    [Serializable]
    public class Ar6dofCamCfg
    {
        public string name = "Camera";
        public bool enabled = true;          // disponible (bouton « Vue » du dock, application Miroir) ; une caméra désactivée ne tourne jamais
        public int role;                     // Ar6dofCamRole
        public int source;                   // 0 = caméra USB, 1 = flux réseau (adresse http, MJPEG)
        public string device = "";           // nom de la caméra USB
        public string url = "http://";
        public int width = 640, height = 480, fps = 30;
        public int stereo;                   // 0 = une image (faux stéréo : la même aux deux yeux), 1 = côte à côte, 2 = haut / bas
        public bool swapEyes;
        public float hfov = 90f;             // champ horizontal de la caméra (degrés) : sert à poser l'image à la bonne échelle
        public float ipd;                    // décalage de l'image entre les deux yeux (m / 1000 comme l'IPD logiciel) : + = écarte
        public float panY;
        public int rotate;                   // quarts de tour horaires (0..3)
        public bool mirrorX, mirrorY;
        public float delayMs = 60f;          // délai entre la prise de vue et l'arrivée de l'image (pour la reprojection)
        public float gain = 1f;              // vision nocturne : amplification ; thermique : contraste
        public float bright = 0f;            // luminosité (-0.5 .. 0.5)
        public int palette;                  // thermique : palette
        public int tint;                     // vision nocturne : teinte
        public bool ledOn;                   // allume une LED infrarouge avec cette caméra
        public string ledMenu = "";          // menu d'appareil Bluetooth (nom) qui pilote la LED
        public string ledControl = "";       // contrôle de ce menu (étiquette)
        public int ledPower = 100;           // puissance (0..100 %)
    }

    [Serializable] class CamFile { public int version = 1; public List<Ar6dofCamCfg> cams = new List<Ar6dofCamCfg>(); }

    /// <summary>
    /// Une source d'image : caméra USB (WebCamTexture) ou flux réseau MJPEG (un fil d'exécution lit le flux, le fil principal décode la dernière image seulement).
    /// N'existe que tant qu'au moins une vue s'en sert (voir Ar6dofCameras.Acquire).
    /// </summary>
    public class Ar6dofCamFeed
    {
        public readonly Ar6dofCamCfg cfg;
        public int refs;
        public Texture Tex { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool Ready { get; private set; }
        public bool FlipY { get; private set; }
        public double LastFrameQpc { get; private set; }
        public float Fps { get; private set; }
        public string Error { get; private set; }

        WebCamTexture wct;
        Texture2D jpg;
        Thread th;
        sealed class Run { public volatile bool stop; public HttpWebRequest req; }
        Run run;
        string sig = "";
        readonly object sync = new object();
        byte[] pending;
        string netErr = "";
        int frames;
        float fpsT;

        public Ar6dofCamFeed(Ar6dofCamCfg c) { cfg = c; Error = ""; }

        /// <summary>Ce qui oblige à rouvrir la source quand ça change.</summary>
        public static string SigOf(Ar6dofCamCfg c) { return c.source + "|" + c.device + "|" + c.url + "|" + c.width + "x" + c.height + "@" + c.fps; }
        public string Sig { get { return sig; } }

        public void Start()
        {
            sig = SigOf(cfg);
            Error = "";
            Ready = false;
            if (cfg.source == 0) StartUsb(); else StartNet();
        }

        void StartUsb()
        {
            try
            {
                var devs = WebCamTexture.devices;
                string name = cfg.device;
                bool found = false;
                for (int i = 0; i < devs.Length; i++) if (devs[i].name == name) { found = true; break; }
                if (!found)
                {
                    if (devs.Length == 0) { Error = "Aucune camera USB trouvee"; return; }
                    if (!string.IsNullOrEmpty(name)) { Error = "Camera USB introuvable : " + name; return; }
                    name = devs[0].name;
                }
                wct = new WebCamTexture(name, Mathf.Max(16, cfg.width), Mathf.Max(16, cfg.height), Mathf.Max(5, cfg.fps));
                wct.wrapMode = TextureWrapMode.Clamp;
                wct.filterMode = FilterMode.Bilinear;
                wct.Play();
                Tex = wct;
            }
            catch (Exception e) { Error = e.Message; }
        }

        void StartNet()
        {
            string u = (cfg.url ?? "").Trim();
            if (u.Length < 8 || !(u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) || u == "http://")
            {
                Error = "Adresse du flux manquante";
                return;
            }
            var r = new Run();
            run = r;
            string url = u;
            th = new Thread(() => NetLoop(url, r)) { IsBackground = true, Name = "AR6DoF flux camera" };
            th.Start();
        }

        // Lit un flux d'images JPEG (MJPEG multipart ou simples images l'une après l'autre) : on cherche les marqueurs de début (FFD8) et de fin (FFD9).
        void NetLoop(string url, Run r)
        {
            var buf = new byte[1 << 16];
            var acc = new byte[1 << 18];
            while (!r.stop)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 6000;
                    req.ReadWriteTimeout = 6000;
                    req.KeepAlive = true;
                    req.UserAgent = "AR6DoF";
                    var uri = new Uri(url);
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        string[] up = Uri.UnescapeDataString(uri.UserInfo).Split(new[] { ':' }, 2);
                        req.Credentials = new NetworkCredential(up[0], up.Length > 1 ? up[1] : "");
                    }
                    r.req = req;
                    using (var resp = req.GetResponse())
                    using (var st = resp.GetResponseStream())
                    {
                        lock (sync) netErr = "";
                        int len = 0;
                        while (!r.stop)
                        {
                            int n = st.Read(buf, 0, buf.Length);
                            if (n <= 0) break;
                            if (len + n > acc.Length)
                            {
                                if (acc.Length >= (8 << 20)) len = 0;   // pas de fin d'image en vue : on repart de zéro
                                else Array.Resize(ref acc, acc.Length * 2);
                            }
                            Buffer.BlockCopy(buf, 0, acc, len, n);
                            len += n;
                            while (true)
                            {
                                int s = IndexOf(acc, len, 0, 0xD8);
                                if (s < 0) { if (len > 1) { acc[0] = acc[len - 1]; len = 1; } break; }
                                int e = IndexOf(acc, len, s + 2, 0xD9);
                                if (e < 0)
                                {
                                    if (s > 0) { Buffer.BlockCopy(acc, s, acc, 0, len - s); len -= s; }
                                    break;
                                }
                                int fl = e + 2 - s;
                                var frame = new byte[fl];
                                Buffer.BlockCopy(acc, s, frame, 0, fl);
                                lock (sync) pending = frame;   // seule la dernière image compte
                                int rest = len - (e + 2);
                                if (rest > 0) Buffer.BlockCopy(acc, e + 2, acc, 0, rest);
                                len = rest;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!r.stop) lock (sync) netErr = ex.Message;
                }
                r.req = null;
                for (int i = 0; i < 10 && !r.stop; i++) Thread.Sleep(50);   // on réessaie après une demi-seconde
            }
        }

        // cherche l'octet 0xFF suivi de 'second' à partir de 'from'
        static int IndexOf(byte[] a, int len, int from, byte second)
        {
            for (int i = Math.Max(0, from); i < len - 1; i++)
                if (a[i] == 0xFF && a[i + 1] == second) return i;
            return -1;
        }

        /// <summary>Appelé à chaque image par le gestionnaire tant que la source est utilisée.</summary>
        public void Pump()
        {
            if (wct != null)
            {
                if (wct.width > 16 && wct.height > 16)
                {
                    Width = wct.width; Height = wct.height; Ready = true;
                    FlipY = wct.videoVerticallyMirrored;
                }
                if (wct.didUpdateThisFrame) { LastFrameQpc = Ar6dofClock.Now(); frames++; }
            }
            else if (th != null)
            {
                byte[] f = null;
                string err;
                lock (sync) { f = pending; pending = null; err = netErr; }
                if (!string.IsNullOrEmpty(err)) Error = err; else if (Error.Length > 0 && Ready) Error = "";
                if (f != null)
                {
                    if (jpg == null)
                    {
                        jpg = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        jpg.wrapMode = TextureWrapMode.Clamp;
                        jpg.filterMode = FilterMode.Bilinear;
                    }
                    if (jpg.LoadImage(f, false))
                    {
                        Tex = jpg; Width = jpg.width; Height = jpg.height; Ready = true; FlipY = false;
                        LastFrameQpc = Ar6dofClock.Now();
                        frames++;
                        Error = "";
                    }
                }
            }
            fpsT += Time.unscaledDeltaTime;
            if (fpsT >= 1f) { Fps = frames / fpsT; frames = 0; fpsT = 0f; }
        }

        public void Stop()
        {
            if (run != null) { run.stop = true; try { if (run.req != null) run.req.Abort(); } catch (Exception) { } run = null; }
            lock (sync) { pending = null; netErr = ""; }
            th = null;
            if (wct != null) { try { wct.Stop(); } catch (Exception) { } UnityEngine.Object.Destroy(wct); wct = null; }
            if (jpg != null) { UnityEngine.Object.Destroy(jpg); jpg = null; }
            Tex = null;
            Ready = false;
            Width = Height = 0;
        }
    }

    /// <summary>
    /// Caméras supplémentaires : vision nocturne, thermique, caméras « autre » (par exemple derrière la tête). Aucune ne tourne tant qu'elle n'est pas utilisée :
    /// elle démarre quand la vue du dock la choisit ou qu'une fenêtre Miroir l'affiche, et s'arrête dès que plus personne ne s'en sert (LED infrarouge éteinte aussi).
    /// Le suivi de position reste TOUJOURS sur la caméra normale (ar6dof_probe) : ces caméras ne servent qu'à l'image.
    /// </summary>
    public class Ar6dofCameras : MonoBehaviour
    {
        public static Ar6dofCameras Instance;
        public readonly List<Ar6dofCamCfg> cams = new List<Ar6dofCamCfg>();
        public event Action Changed;                 // liste ou vue modifiée (le dock affiche ou cache son bouton)

        readonly Dictionary<Ar6dofCamCfg, Ar6dofCamFeed> feeds = new Dictionary<Ar6dofCamCfg, Ar6dofCamFeed>();
        readonly List<Ar6dofCamCfg> pumpList = new List<Ar6dofCamCfg>();
        readonly Dictionary<Ar6dofCamCfg, bool> ledWant = new Dictionary<Ar6dofCamCfg, bool>();
        readonly Dictionary<Ar6dofCamCfg, bool> ledSent = new Dictionary<Ar6dofCamCfg, bool>();
        float nextLed, saveAt;
        bool dirty;

        /// <summary>Caméra dont l'image remplace le passthrough (null = caméra normale).</summary>
        public Ar6dofCamCfg ViewCam { get; private set; }

        public static string FilePath() { return Path.Combine(Application.persistentDataPath, "ar6dof_cameras.json"); }

        void Awake() { Instance = this; Load(); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Load()
        {
            try
            {
                string p = FilePath();
                if (!File.Exists(p)) return;
                var f = JsonUtility.FromJson<CamFile>(File.ReadAllText(p));
                if (f != null && f.cams != null) foreach (var c in f.cams) { Fix(c); cams.Add(c); }
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : ar6dof_cameras.json illisible : " + e.Message); }
        }

        static void Fix(Ar6dofCamCfg c)
        {
            if (c.name == null) c.name = "Camera";
            if (c.device == null) c.device = "";
            if (c.url == null) c.url = "http://";
            if (c.ledMenu == null) c.ledMenu = "";
            if (c.ledControl == null) c.ledControl = "";
            c.role = Mathf.Clamp(c.role, 0, 2);
            c.source = Mathf.Clamp(c.source, 0, 1);
            c.stereo = Mathf.Clamp(c.stereo, 0, 2);
            c.rotate = ((c.rotate % 4) + 4) % 4;
            c.width = Mathf.Clamp(c.width, 16, 4096);
            c.height = Mathf.Clamp(c.height, 16, 4096);
            c.fps = Mathf.Clamp(c.fps, 5, 120);
            c.hfov = Mathf.Clamp(c.hfov, 20f, 200f);
            c.ipd = Mathf.Clamp(c.ipd, -0.5f, 0.5f);
            c.panY = Mathf.Clamp(c.panY, -0.5f, 0.5f);
            c.delayMs = Mathf.Clamp(c.delayMs, 0f, 400f);
            c.gain = Mathf.Clamp(c.gain, 0.2f, 8f);
            c.bright = Mathf.Clamp(c.bright, -0.5f, 0.5f);
            c.palette = Mathf.Clamp(c.palette, 0, Ar6dofCamRole.Palettes.Length - 1);
            c.tint = Mathf.Clamp(c.tint, 0, Ar6dofCamRole.Tints.Length - 1);
            c.ledPower = Mathf.Clamp(c.ledPower, 0, 100);
        }

        /// <summary>Appelé après chaque modification d'un réglage : enregistre un peu plus tard et prévient ceux qui écoutent.</summary>
        public void MarkChanged(Ar6dofCamCfg c = null)
        {
            dirty = true;
            saveAt = Time.unscaledTime + 0.6f;
            if (c != null)
            {
                Fix(c);
                Ar6dofCamFeed rf;
                if (feeds.TryGetValue(c, out rf))
                {
                    if (rf.Sig != Ar6dofCamFeed.SigOf(c)) { rf.Stop(); rf.Start(); }   // source, adresse ou résolution changées : on rouvre
                    RefreshLed(c);
                }
            }
            if (ViewCam != null && (!cams.Contains(ViewCam) || !ViewCam.enabled || ViewCam.role == Ar6dofCamRole.Other)) SetView(null);
            if (Changed != null) Changed();
        }

        public Ar6dofCamCfg Add()
        {
            var c = new Ar6dofCamCfg { name = Ar6dofLoc.T("Camera") + " " + (cams.Count + 1) };
            cams.Add(c);
            MarkChanged();
            return c;
        }

        public void Remove(Ar6dofCamCfg c)
        {
            if (ViewCam == c) SetView(null);
            Ar6dofCamFeed f;
            if (feeds.TryGetValue(c, out f)) { f.refs = 0; DropFeed(c); }
            cams.Remove(c);
            MarkChanged();
        }

        public void Save()
        {
            dirty = false;
            try
            {
                var f = new CamFile();
                f.cams.AddRange(cams);
                File.WriteAllText(FilePath(), JsonUtility.ToJson(f, true));
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : sauvegarde des cameras impossible : " + e.Message); }
        }

        // ------------------------------------------------------------------ vue du dock
        /// <summary>Vrai s'il existe au moins une caméra activée de vision nocturne ou thermique : le bouton « Vue » du dock n'apparaît que dans ce cas.</summary>
        public bool HasSwitchable
        {
            get { foreach (var c in cams) if (c.enabled && c.role != Ar6dofCamRole.Other) return true; return false; }
        }

        public List<Ar6dofCamCfg> Switchable()
        {
            var l = new List<Ar6dofCamCfg>();
            foreach (var c in cams) if (c.enabled && c.role != Ar6dofCamRole.Other) l.Add(c);
            return l;
        }

        /// <summary>Normale → première caméra → deuxième... → normale.</summary>
        public void CycleView()
        {
            var l = Switchable();
            if (l.Count == 0) { SetView(null); return; }
            int i = ViewCam == null ? -1 : l.IndexOf(ViewCam);
            if (i + 1 >= l.Count) SetView(null); else SetView(l[i + 1]);
        }

        public void SetView(Ar6dofCamCfg c)
        {
            if (c == ViewCam) return;
            var old = ViewCam;
            ViewCam = c;
            if (c != null) Acquire(c);
            if (old != null) Release(old);
            if (Changed != null) Changed();
        }

        /// <summary>Source prête de la vue choisie (null = vue normale ou image pas encore arrivée).</summary>
        public Ar6dofCamFeed ActiveFeed
        {
            get
            {
                Ar6dofCamFeed f;
                if (ViewCam != null && feeds.TryGetValue(ViewCam, out f) && f.Ready && f.Tex != null) return f;
                return null;
            }
        }

        public Ar6dofCamFeed FeedOf(Ar6dofCamCfg c)
        {
            Ar6dofCamFeed f;
            return c != null && feeds.TryGetValue(c, out f) ? f : null;
        }

        public string ViewLabel()
        {
            if (ViewCam == null) return Ar6dofLoc.T("Vue : Normale");
            return Ar6dofLoc.T("Vue") + " : " + ViewCam.name;
        }

        // ------------------------------------------------------------------ prise / rendu d'une source
        public Ar6dofCamFeed Acquire(Ar6dofCamCfg c)
        {
            Ar6dofCamFeed f;
            if (!feeds.TryGetValue(c, out f))
            {
                f = new Ar6dofCamFeed(c);
                feeds[c] = f;
                f.Start();
                SetLed(c, true);
            }
            f.refs++;
            return f;
        }

        public void Release(Ar6dofCamCfg c)
        {
            Ar6dofCamFeed f;
            if (!feeds.TryGetValue(c, out f)) return;
            f.refs--;
            if (f.refs <= 0) DropFeed(c);
        }

        void DropFeed(Ar6dofCamCfg c)
        {
            Ar6dofCamFeed f;
            if (!feeds.TryGetValue(c, out f)) return;
            f.Stop();
            feeds.Remove(c);
            SetLed(c, false);
        }

        void Update()
        {
            if (feeds.Count > 0)
            {
                pumpList.Clear();
                foreach (var kv in feeds) pumpList.Add(kv.Key);
                for (int i = 0; i < pumpList.Count; i++) { Ar6dofCamFeed f; if (feeds.TryGetValue(pumpList[i], out f)) f.Pump(); }
            }
            if (ledWant.Count > 0 && Time.unscaledTime >= nextLed) { nextLed = Time.unscaledTime + 1f; TryLeds(); }
            if (dirty && Time.unscaledTime >= saveAt) Save();
        }

        // ------------------------------------------------------------------ LED infrarouge (via un menu d'appareil Bluetooth)
        DeviceMenu FindMenu(string name)
        {
            var d = Ar6dofDock.Instance;
            if (d == null || string.IsNullOrEmpty(name)) return null;
            foreach (var m in d.DeviceMenus) if (m.DisplayName == name) return m;
            return null;
        }

        void SetLed(Ar6dofCamCfg c, bool on)
        {
            if (!c.ledOn && !(ledSent.ContainsKey(c) && ledSent[c])) return;   // pas de LED, sauf s'il faut éteindre une LED déjà allumée
            ledWant[c] = on;
            nextLed = 0f;
        }

        /// <summary>La puissance ou le contrôle a changé alors que la caméra tourne : renvoie la valeur à la LED.</summary>
        public void RefreshLed(Ar6dofCamCfg c)
        {
            if (!feeds.ContainsKey(c)) return;
            ledSent.Remove(c);
            ledWant[c] = c.ledOn;
            nextLed = 0f;
        }

        void TryLeds()
        {
            var keys = new List<Ar6dofCamCfg>(ledWant.Keys);
            foreach (var c in keys)
            {
                bool want = ledWant[c];
                bool sent;
                if (ledSent.TryGetValue(c, out sent) && sent == want) { ledWant.Remove(c); if (!want) ledSent.Remove(c); continue; }
                if (!want && !ledSent.ContainsKey(c)) { ledWant.Remove(c); continue; }
                var m = FindMenu(c.ledMenu);
                if (m == null || !m.IsConnectedNow || string.IsNullOrEmpty(c.ledControl)) continue;   // on réessaie chaque seconde, le temps que l'appareil se connecte
                if (m.SetControlFraction(c.ledControl, want ? c.ledPower / 100f : 0f)) ledSent[c] = want;
                else ledWant.Remove(c);   // ce contrôle n'existe pas : inutile d'insister
            }
        }

        /// <summary>Allume ou éteint la LED de cette caméra pour un essai (même si la caméra ne tourne pas). Faux si le menu n'est pas prêt.</summary>
        public bool TestLed(Ar6dofCamCfg c, bool on)
        {
            var m = FindMenu(c.ledMenu);
            if (m == null || !m.IsConnectedNow || string.IsNullOrEmpty(c.ledControl)) return false;
            return m.SetControlFraction(c.ledControl, on ? c.ledPower / 100f : 0f);
        }

        void OnApplicationQuit()
        {
            foreach (var kv in new List<KeyValuePair<Ar6dofCamCfg, bool>>(ledSent))
            {
                if (!kv.Value) continue;
                var m = FindMenu(kv.Key.ledMenu);
                if (m != null && m.IsConnectedNow) m.SetControlFraction(kv.Key.ledControl, 0f);
            }
            if (dirty) Save();
        }

        /// <summary>État lisible d'une caméra pour les réglages.</summary>
        public string StatusOf(Ar6dofCamCfg c)
        {
            var f = FeedOf(c);
            if (f == null) return Ar6dofLoc.T("Arretee (elle ne tourne que quand on s'en sert)");
            if (!string.IsNullOrEmpty(f.Error)) return Ar6dofLoc.T("Erreur : ") + f.Error;
            if (!f.Ready) return Ar6dofLoc.T("Demarrage...");
            return Ar6dofLoc.F("En marche : {0} x {1}, {2} img/s", f.Width, f.Height, f.Fps.ToString("0"));
        }

        public string LedStatusOf(Ar6dofCamCfg c)
        {
            if (!c.ledOn) return Ar6dofLoc.T("LED infrarouge : non utilisee");
            var m = FindMenu(c.ledMenu);
            if (m == null) return Ar6dofLoc.T("LED : choisis un menu d'appareil Bluetooth");
            if (string.IsNullOrEmpty(c.ledControl)) return Ar6dofLoc.T("LED : choisis le controle de ce menu");
            if (!m.IsConnectedNow) return Ar6dofLoc.T("LED : appareil Bluetooth pas connecte");
            bool sent;
            return Ar6dofLoc.T(ledSent.TryGetValue(c, out sent) && sent ? "LED : allumee" : "LED : eteinte");
        }
    }
}
