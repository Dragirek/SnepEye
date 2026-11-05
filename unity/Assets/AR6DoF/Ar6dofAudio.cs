using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AR6DoF
{
    [Serializable] public class AudioKV { public string k = ""; public float v; }

    /// <summary>Un bouton du soundboard.</summary>
    [Serializable]
    public class PadDef
    {
        public int slot;                 // identifiant stable côté ar6dof_audio (ne change jamais, même si on réordonne)
        public string label = "";
        public string path = "";
        public float r = 0.2f, g = 0.45f, b = 0.9f;
        public float vol = 1f, rate = 1f, pan = 0f;
        public bool loop;                // vrai : un clic lance en boucle, le clic suivant l'arrête
        public int w = 1, h = 1;         // taille en cases
    }

    [Serializable]
    public class AudioData
    {
        public bool enabled;
        public string inDev = "off", outDev = "default";
        public string inName = "", outName = "";
        public List<AudioKV> voice = new List<AudioKV>();
        public float[] vol = { 1f, 1f, 1f, 1f };      // voix, soundboard, musique, général
        public bool[] mute = new bool[4];
        // table de mixage avancée : tranches voix, soundboard, musique, général
        public bool[] solo = new bool[4];
        public float[] pan = new float[4];
        public float[] eq = new float[12];            // graves, medium, aigus (dB) pour chaque tranche
        public bool limiter = true;
        public int viz = 0;                           // visualisation choisie dans la table de mixage
        public List<PadDef> pads = new List<PadDef>();
        public int nextSlot = 1;
        public int layout = 0;                        // 0 grille, 1 liste, 2 flux, 3 cercle
        public int cols = 4;
        public float cell = 150f;
        public List<string> playlist = new List<string>();
        public int track = -1;
        public int repeat = 1;                        // 0 non, 1 tout, 2 une piste
        public bool shuffle;
    }

    public class AudioDevice { public string id = "", name = ""; public bool isDefault; }

    /// <summary>Une platine DJ vue d'Unity : l'état (position, lecture, tempo...) vient du moteur ~20 fois par seconde, la forme d'onde arrive par morceaux après le chargement.
    /// Les repères (cue, hot cues) ne sont gardés qu'ici et ne survivent pas au changement de piste ni à la fermeture.</summary>
    public class DjDeck
    {
        public string path = "", name = "";
        public int state;                  // 0 vide, 1 chargement, 2 prête, 3 illisible
        public float dur, bpm, firstBeat;  // secondes, BPM (0 = inconnu), premier temps de la grille (secondes)
        public byte[] peak = new byte[0], low = new byte[0];   // forme d'onde : 50 points par seconde (crête, part de graves)
        public int waveGot, waveTotal, waveVersion;
        public float pos, tempo = 1f, vu;
        public bool playing, loop, loaded;
        public float cue;                  // repère principal (secondes)
        public readonly float[] hot = { -1f, -1f, -1f, -1f };
        public float loopIn = -1f, loopOut = -1f;   // zone de boucle affichée (calculée comme le moteur, au temps le plus proche)
        public readonly float[] eq = new float[3];  // graves, medium, aigus (dB)
        public float filter, vol = 1f;
        public int range = 0;              // plage du tempo : 0 = 8 %, 1 = 16 %, 2 = 50 %
        public float loopLock;             // jusqu'à cet instant, l'état de boucle reçu du moteur est ignoré (la commande vient d'être envoyée)
        public int cueVersion;             // change quand un repère ou la boucle change (les formes d'onde se redessinent)

        public void Reset()
        {
            path = ""; name = ""; state = 0; dur = 0f; bpm = 0f; firstBeat = 0f;
            peak = new byte[0]; low = new byte[0]; waveGot = waveTotal = 0; waveVersion++;
            pos = 0f; tempo = 1f; vu = 0f; playing = false; loop = false; loaded = false; cue = 0f;
            for (int i = 0; i < 4; i++) hot[i] = -1f;
            loopIn = loopOut = -1f;
            cueVersion++;   // l'égaliseur, le filtre et le volume restent comme réglés (le moteur les garde d'une piste à l'autre)
        }
        public float BeatLen { get { return bpm > 0f ? 60f / bpm : 0.5f; } }
    }

    /// <summary>Niveaux reçus d'ar6dof_audio (micro, voix, soundboard, musique, gauche, droite), position de la musique et spectres (32 bandes de 60 Hz à 12 kHz).</summary>
    public class AudioMeters
    {
        public readonly float[] peak = new float[6];
        public readonly float[] specIn = new float[32], specOut = new float[32];
        public readonly float[] wave = new float[128];   // forme d'onde de la sortie (oscilloscope)
        public float musicPos, musicDur;
        public int voices, flags;
        public bool GateOpen { get { return (flags & 4) != 0; } }
        public bool MusicPlaying { get { return (flags & 8) != 0; } }
        public bool MusicPaused { get { return (flags & 16) != 0; } }
    }

    /// <summary>Description d'un réglage de la voix (curseur dans la fenêtre Voix).</summary>
    public class VoiceKey
    {
        public string key, label, tab, fmt;
        public float min, max, def;
        public bool toggle;
        public VoiceKey(string tab, string key, string label, float min, float max, float def, string fmt, bool toggle = false)
        { this.tab = tab; this.key = key; this.label = label; this.min = min; this.max = max; this.def = def; this.fmt = fmt; this.toggle = toggle; }
    }

    /// <summary>
    /// Client du moteur audio (ar6dof_audio.exe) : micro -> voix modifiée -> enceintes externes, soundboard, musique.
    /// Le moteur est un petit programme séparé, lancé SEULEMENT quand le son est activé (fenêtre Voix, bouton « Moteur audio ») : désactivé,
    /// aucun processus, aucun thread, aucun port ouvert. Ports : commandes 9890, niveaux 9891, événements 9892 (127.0.0.1).
    /// </summary>
    public class Ar6dofAudio : MonoBehaviour
    {
        public static Ar6dofAudio Instance;
        public int commandPort = 9890, meterPort = 9891, eventPort = 9892;
        public enum EngineState { Off, Starting, Online, Failed }

        public AudioData data = new AudioData();
        public EngineState State { get; private set; }
        public string Message { get; private set; } = "";
        public string InStatus { get; private set; } = "";
        public string OutStatus { get; private set; } = "";
        public readonly List<AudioDevice> inputs = new List<AudioDevice>(), outputs = new List<AudioDevice>();
        public int deviceVersion;            // augmente à chaque nouvelle liste de périphériques (les fenêtres se rafraîchissent)
        public int soundVersion;             // augmente quand l'état d'un son change
        public int musicVersion;             // augmente quand la piste change
        public readonly Dictionary<int, int> soundState = new Dictionary<int, int>();       // 1 prêt, 2 erreur
        public readonly Dictionary<int, int> soundMs = new Dictionary<int, int>();
        public readonly HashSet<int> loopingSlots = new HashSet<int>();
        public string musicError = "";

        // ---- réglages de la voix
        public static readonly VoiceKey[] VoiceKeys =
        {
            new VoiceKey("base", "in", "Gain du micro", -20, 24, 0, "{0:+0.#;-0.#;0} dB"),
            new VoiceKey("base", "gthr", "Seuil de la porte de bruit", -80, -10, -50, "{0:0} dB"),
            new VoiceKey("base", "grel", "Fermeture de la porte", 20, 500, 120, "{0:0} ms"),
            new VoiceKey("base", "cthr", "Seuil du compresseur", -40, 0, -18, "{0:0} dB"),
            new VoiceKey("base", "cratio", "Force du compresseur", 1, 10, 3, "{0:0.#} : 1"),
            new VoiceKey("base", "out", "Volume de la voix", -20, 12, 0, "{0:+0.#;-0.#;0} dB"),
            new VoiceKey("fx", "pitch", "Hauteur (demi-tons)", -12, 12, 0, "{0:+0.#;-0.#;0}"),
            new VoiceKey("fx", "robot", "Robot", 0, 1, 0, "{0:0}%"),
            new VoiceKey("fx", "robothz", "Frequence du robot", 20, 300, 60, "{0:0} Hz"),
            new VoiceKey("fx", "drive", "Distorsion", 0, 1, 0, "{0:0}%"),
            new VoiceKey("fx", "crush", "Qualite reduite (bits)", 4, 16, 16, "{0:0} bits"),
            new VoiceKey("space", "echo", "Echo", 0, 1, 0, "{0:0}%"),
            new VoiceKey("space", "echoms", "Delai de l'echo", 50, 800, 220, "{0:0} ms"),
            new VoiceKey("space", "echofb", "Repetitions de l'echo", 0, 0.85f, 0.35f, "{0:0}%"),
            new VoiceKey("space", "rev", "Reverberation", 0, 1, 0, "{0:0}%"),
            new VoiceKey("space", "room", "Taille de la salle", 0, 1, 0.5f, "{0:0}%"),
            new VoiceKey("space", "damp", "Sons aigus absorbes", 0, 1, 0.4f, "{0:0}%"),
            new VoiceKey("filter", "hp", "Coupe-bas", 0, 400, 80, "{0:0} Hz"),
            new VoiceKey("filter", "lp", "Coupe-haut", 1000, 20000, 20000, "{0:0} Hz"),
            new VoiceKey("filter", "eql", "Graves", -12, 12, 0, "{0:+0.#;-0.#;0} dB"),
            new VoiceKey("filter", "eqm", "Medium", -12, 12, 0, "{0:+0.#;-0.#;0} dB"),
            new VoiceKey("filter", "eqh", "Aigus", -12, 12, 0, "{0:+0.#;-0.#;0} dB"),
        };
        public static readonly VoiceKey[] VoiceToggles =
        {
            new VoiceKey("general", "on", "Effets de voix", 0, 1, 1, "", true),
            new VoiceKey("base", "gate", "Porte de bruit", 0, 1, 1, "", true),
            new VoiceKey("base", "comp", "Compresseur", 0, 1, 1, "", true),
        };

        /// <summary>Préréglages : paires (clé, valeur) ; tout le reste revient à la valeur par défaut.</summary>
        public static readonly string[] PresetNames = { "Normal", "Grave", "Aigu", "Robot", "Demon", "Radio", "Grotte", "Cassette" };
        static readonly object[][] PresetValues =
        {
            new object[] { },
            new object[] { "pitch", -4f, "eql", 4f, "eqh", -3f },
            new object[] { "pitch", 5f, "eqh", 3f, "hp", 150f },
            new object[] { "robot", 0.9f, "robothz", 70f, "pitch", -1f, "drive", 0.1f, "echo", 0.15f, "echoms", 40f, "echofb", 0.25f },
            new object[] { "pitch", -9f, "eql", 6f, "rev", 0.35f, "room", 0.75f, "drive", 0.2f, "echo", 0.2f, "echoms", 120f },
            new object[] { "hp", 400f, "lp", 3500f, "drive", 0.12f, "eqm", 5f, "crush", 12f },
            new object[] { "rev", 0.7f, "room", 0.95f, "damp", 0.25f, "echo", 0.25f, "echoms", 340f, "echofb", 0.5f },
            new object[] { "lp", 6000f, "hp", 200f, "crush", 9f, "drive", 0.08f, "eqh", -4f }
        };

        public static string FilePath()
        {
            string dir;
            try { dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } catch (Exception) { dir = Application.persistentDataPath; }
            return Path.Combine(dir, "ar6dof_audio.json");
        }

        // ---------------------------------------------------------------- état interne
        readonly object lockObj = new object();
        readonly Queue<string> events = new Queue<string>();
        AudioMeters front = new AudioMeters(), back = new AudioMeters();
        UdpClient meterUdp, eventUdp, sender;
        Thread meterThread, eventThread;
        volatile bool running;
        Process proc;
        float suspendAt = -1f;        // arret differe du moteur (voir Suspend)
        int gen;                      // numero du lancement en cours : un lancement devenu inutile se tue lui-meme
        volatile string launchFail;   // erreur de lancement (le processus est lance sur un autre fil)
        float stateSince, lastPing, lastPong, meterWantedUntil, lastMeterPoke;
        bool dirty;
        float saveAt;
        bool applied;

        public AudioMeters Meters { get { return front; } }

        // ---------------------------------------------------------------- platines DJ (le moteur fait tout le son ; ici seulement les commandes et l'état reçu)
        public readonly DjDeck[] decks = { new DjDeck(), new DjDeck() };
        public float xfade = 0.5f;       // 0 = platine A, 1 = platine B
        public int xfCurve;              // 0 mix, 1 coupe
        public int djVersion;            // change quand une piste est chargée / éjectée / illisible
        float djWantedUntil, lastDjPoke;

        /// <summary>À appeler à chaque image par la fenêtre qui affiche les platines : le moteur n'envoie leur état que tant que quelqu'un regarde.</summary>
        public void WantDj() { djWantedUntil = Time.unscaledTime + 0.4f; }
        static string DL(int d) { return d == 0 ? "A" : "B"; }

        public void DjLoad(int d, string path)
        {
            if (d < 0 || d > 1 || string.IsNullOrEmpty(path)) return;
            var k = decks[d];
            k.Reset();
            k.path = path; k.name = Path.GetFileNameWithoutExtension(path); k.state = 1;
            djVersion++;
            EnsureOutput();
            WhenOnline(() => Send("DLOAD " + DL(d) + " " + path));
        }
        public void DjEject(int d) { Send("DEJECT " + DL(d)); decks[d].Reset(); djVersion++; }
        public void DjPlay(int d, bool on) { decks[d].playing = on; Send("DPLAY " + DL(d) + " " + (on ? "1" : "0")); }
        public void DjSeek(int d, float sec) { decks[d].pos = sec; Send("DSEEK " + DL(d) + " " + N(sec)); }
        public void DjTempo(int d, float t) { decks[d].tempo = t; Send("DTEMPO " + DL(d) + " " + N(t)); }
        public void DjBend(int d, float v) { Send("DBEND " + DL(d) + " " + N(v)); }
        public void DjVol(int d, float v) { decks[d].vol = v; Send("DVOL " + DL(d) + " " + N(v)); }
        public void DjEq(int d, int band, float db)
        {
            var k = decks[d];
            k.eq[band] = db;
            Send("DEQ " + DL(d) + " " + N(k.eq[0]) + " " + N(k.eq[1]) + " " + N(k.eq[2]));
        }
        public void DjFilter(int d, float v) { decks[d].filter = v; Send("DFILT " + DL(d) + " " + N(v)); }
        public void DjLoop(int d, float beats) { Send("DLOOP " + DL(d) + " " + N(beats)); }
        public void DjSync(int d) { Send("DSYNC " + DL(d)); }
        public void DjXfade(float v) { xfade = v; Send("XFADE " + N(v)); }
        public void DjCurve(int c) { xfCurve = c; Send("XFCURVE " + c); }

        static float PF(string t) { float v; return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f; }

        // DST <pos> <lecture> <tempo> <vu> <boucle> <chargee>  x2 (A puis B)  <crossfader>
        void ParseDjStatus(string s)
        {
            var p = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 14) return;
            for (int d = 0; d < 2; d++)
            {
                int o = 1 + d * 6;
                var k = decks[d];
                k.pos = PF(p[o]); k.playing = p[o + 1] == "1"; k.tempo = PF(p[o + 2]); k.vu = PF(p[o + 3]);
                bool lp = p[o + 4] == "1";
                if (lp != k.loop && Time.unscaledTime > k.loopLock) { k.loop = lp; if (!lp) { k.loopIn = k.loopOut = -1f; } k.cueVersion++; }
                k.loaded = p[o + 5] == "1";
                if (!k.loaded && k.state == 2) { k.state = 0; djVersion++; }
            }
        }

        // DECK <platine> loading | err | ok <durée ms> <bpm x100> <premier temps ms> <nombre de points>
        void ParseDeck(string s)
        {
            var p = s.Split(' ');
            int d;
            if (p.Length < 3 || !int.TryParse(p[1], out d) || d < 0 || d > 1) return;
            var k = decks[d];
            if (p[2] == "loading") { k.state = 1; }
            else if (p[2] == "err") { k.state = 3; k.loaded = false; }
            else if (p[2] == "ok" && p.Length >= 7)
            {
                k.dur = PF(p[3]) / 1000f; k.bpm = PF(p[4]) / 100f; k.firstBeat = PF(p[5]) / 1000f;
                int n; int.TryParse(p[6], out n);
                n = Mathf.Clamp(n, 0, 200000);
                k.peak = new byte[n]; k.low = new byte[n]; k.waveGot = 0; k.waveTotal = n; k.waveVersion++;
                k.state = 2; k.loaded = true; k.pos = 0f; k.cue = 0f;
                k.cueVersion++;
            }
            djVersion++;
        }

        static int Hx(char c) { return c >= '0' && c <= '9' ? c - '0' : (c >= 'a' && c <= 'f' ? c - 'a' + 10 : (c >= 'A' && c <= 'F' ? c - 'A' + 10 : 0)); }

        // DWAVE <platine> <total> <début> <hex : (crête, graves) par point>
        void ParseWave(string s)
        {
            var p = s.Split(' ');
            int d, total, start;
            if (p.Length < 5 || !int.TryParse(p[1], out d) || d < 0 || d > 1 || !int.TryParse(p[2], out total) || !int.TryParse(p[3], out start)) return;
            var k = decks[d];
            if (k.peak.Length != total) return;
            string h = p[4];
            int n = h.Length / 4;
            for (int i = 0; i < n && start + i < total; i++)
            {
                k.peak[start + i] = (byte)(Hx(h[4 * i]) * 16 + Hx(h[4 * i + 1]));
                k.low[start + i] = (byte)(Hx(h[4 * i + 2]) * 16 + Hx(h[4 * i + 3]));
                k.waveGot++;
            }
            k.waveVersion++;
        }
        public bool Online { get { return State == EngineState.Online; } }
        public bool Enabled { get { return data.enabled; } }

        void Awake()
        {
            Instance = this;
            Load();
        }

        // Le moteur ne demarre plus tout seul : Ar6dofDock le lance (Resume) quand une application audio (Voix, Soundboard, Table de mixage) est dans la barre
        // et l'arrete (Suspend) quand la derniere est fermee. Tant qu'aucune n'est ouverte : aucun processus, aucun port.
        void Start() { }

        /// <summary>Redemarre le moteur s'il etait active (sans changer ce reglage).</summary>
        public void Resume()
        {
            suspendAt = -1f;   // rouvrir une application audio juste apres l'avoir fermee : le moteur tourne encore, rien a relancer
            if (data.enabled && State == EngineState.Off) StartEngine();
        }

        /// <summary>Arrete le moteur sans changer le reglage « active » (il repart a la prochaine ouverture d'une application audio).</summary>
        public void Suspend()
        {
            // Arret differe de 30 s : fermer puis rouvrir une application ne relance pas le processus (lancer ou arreter un exe fige l'affichage quelques centaines de ms).
            if (State != EngineState.Off && suspendAt < 0f) suspendAt = Time.unscaledTime + 30f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (dirty) Save();
            StopEngine(false);
            try { sender?.Close(); } catch (Exception) { }
        }

        void OnApplicationQuit()
        {
            if (dirty) Save();
            StopEngine(true);
        }

        // ---------------------------------------------------------------- fichier
        public void Load()
        {
            try
            {
                string p = FilePath();
                if (File.Exists(p))
                {
                    var d = JsonUtility.FromJson<AudioData>(File.ReadAllText(p, Encoding.UTF8));
                    if (d != null)
                    {
                        data = d;
                        if (data.vol == null || data.vol.Length < 4) data.vol = new[] { 1f, 1f, 1f, 1f };
                        if (data.mute == null || data.mute.Length < 4) data.mute = new bool[4];
                        if (data.solo == null || data.solo.Length < 4) data.solo = new bool[4];
                        if (data.pan == null || data.pan.Length < 4) data.pan = new float[4];
                        if (data.eq == null || data.eq.Length < 12) data.eq = new float[12];
                        if (data.voice == null) data.voice = new List<AudioKV>();
                        if (data.pads == null) data.pads = new List<PadDef>();
                        if (data.playlist == null) data.playlist = new List<string>();
                    }
                }
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("AR6DoF : ar6dof_audio.json illisible : " + e.Message); }
        }

        public void MarkDirty() { dirty = true; saveAt = Time.unscaledTime + 1f; }

        public void Save()
        {
            dirty = false;
            try { File.WriteAllText(FilePath(), JsonUtility.ToJson(data, true), new UTF8Encoding(false)); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("AR6DoF : sauvegarde audio impossible : " + e.Message); }
        }

        // ---------------------------------------------------------------- moteur : démarrage / arrêt
        string ExePath()
        {
            string dir = "";
            var l = Ar6dofDock.Instance != null ? Ar6dofDock.Instance.launcher : null;
            if (l != null) dir = l.ExeDirectory();
            if (string.IsNullOrEmpty(dir)) dir = Ar6dofProbePaths.Dir("");
            return Path.Combine(dir, "ar6dof_audio.exe");
        }

        public void SetEnabled(bool on)
        {
            if (on == data.enabled && (on == (State != EngineState.Off))) return;
            data.enabled = on;
            Save();
            if (on) StartEngine(); else StopEngine(true);
        }

        void StartEngine()
        {
            if (State == EngineState.Starting || State == EngineState.Online) return;
            applied = false;
            Message = "";
            if (!OpenSockets()) { State = EngineState.Failed; Message = Ar6dofLoc.T("Ports audio utilises par un autre programme"); return; }
            State = EngineState.Starting;
            stateSince = Time.unscaledTime;
            lastPing = -10f;
            string exe = ExePath();
            if (!File.Exists(exe))
            {
                State = EngineState.Failed;
                Message = Ar6dofLoc.T("ar6dof_audio.exe introuvable (a cote d'ar6dof_probe.exe)");
                return;
            }
            // Process.Start peut prendre plusieurs centaines de ms (antivirus...) : sur un autre fil, sinon l'affichage du casque se fige a l'ouverture.
            int myGen;
            lock (lockObj) { myGen = ++gen; }
            int pid = Process.GetCurrentProcess().Id;
            launchFail = null;
            ThreadPool.QueueUserWorkItem(_ => LaunchProcess(exe, pid, myGen));
        }

        void LaunchProcess(string exe, int parentPid, int myGen)
        {
            Process p = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "--parent " + parentPid)
                {
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                p = new Process { StartInfo = psi };
                p.OutputDataReceived += (s, e) => { };
                p.ErrorDataReceived += (s, e) => { };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                launchFail = e.Message;
                return;
            }
            lock (lockObj) { if (myGen == gen) { proc = p; return; } }
            try { p.Kill(); } catch (Exception) { }   // le moteur a ete arrete avant la fin du lancement
        }

        void StopEngine(bool polite)
        {
            if (State != EngineState.Off && polite) Send("QUIT");
            State = EngineState.Off;
            applied = false;
            loopingSlots.Clear();
            whenOnline.Clear();
            lock (lockObj) events.Clear();
            CloseSockets();
            Process p;
            lock (lockObj) { p = proc; proc = null; gen++; }
            if (p == null) return;
            if (!polite)
            {
                try { if (!p.HasExited && !p.WaitForExit(50)) p.Kill(); } catch (Exception) { }
                return;
            }
            // attendre sa sortie (jusqu'a 600 ms) sur un autre fil : fermer une application ne fige pas l'affichage
            ThreadPool.QueueUserWorkItem(_ => { try { if (!p.HasExited && !p.WaitForExit(600)) p.Kill(); } catch (Exception) { } });
        }

        bool OpenSockets()
        {
            if (running) return true;
            try
            {
                meterUdp = Ar6dofSocket.NoInherit(new UdpClient(meterPort));
                eventUdp = Ar6dofSocket.NoInherit(new UdpClient(eventPort));
                try { eventUdp.Client.ReceiveBufferSize = 1 << 20; } catch (Exception) { }   // les formes d'onde des platines arrivent en rafale
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("AR6DoF : audio, ports UDP " + meterPort + "/" + eventPort + " inutilisables : " + e.Message);
                CloseSockets();
                return false;
            }
            running = true;
            meterThread = new Thread(MeterLoop) { IsBackground = true, Name = "Ar6dofAudioMeters" };
            eventThread = new Thread(EventLoop) { IsBackground = true, Name = "Ar6dofAudioEvents" };
            meterThread.Start();
            eventThread.Start();
            return true;
        }

        void CloseSockets()
        {
            running = false;
            try { meterUdp?.Close(); } catch (Exception) { }
            try { eventUdp?.Close(); } catch (Exception) { }
            meterUdp = null;
            eventUdp = null;
        }

        // ---------------------------------------------------------------- réception
        void MeterLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            var udp = meterUdp;
            while (running)
            {
                byte[] d;
                try { d = udp.Receive(ref any); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                if ((d.Length != 308 && d.Length != 820) || BitConverter.ToUInt32(d, 0) != 0x31445541u) continue;
                var m = back;
                for (int i = 0; i < 6; i++) m.peak[i] = BitConverter.ToSingle(d, 12 + 4 * i);
                m.musicPos = BitConverter.ToSingle(d, 36);
                m.musicDur = BitConverter.ToSingle(d, 40);
                m.voices = (int)BitConverter.ToUInt32(d, 44);
                m.flags = BitConverter.ToUInt16(d, 6);
                Buffer.BlockCopy(d, 52, m.specIn, 0, 128);
                Buffer.BlockCopy(d, 180, m.specOut, 0, 128);
                if (d.Length == 820) Buffer.BlockCopy(d, 308, m.wave, 0, 512);
                lock (lockObj) { var t = front; front = back; back = t; }
            }
        }

        void EventLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            var udp = eventUdp;
            while (running)
            {
                byte[] d;
                try { d = udp.Receive(ref any); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                string s = Encoding.UTF8.GetString(d).TrimEnd('\r', '\n', ' ');
                lock (lockObj) { if (events.Count < 4096) events.Enqueue(s); }
            }
        }

        // ---------------------------------------------------------------- commandes
        public void Send(string line)
        {
            try
            {
                if (sender == null) sender = Ar6dofSocket.NoInherit(new UdpClient());
                byte[] d = Encoding.UTF8.GetBytes(line);
                sender.Send(d, d.Length, "127.0.0.1", commandPort);
            }
            catch (Exception) { }
        }

        static string N(float v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }

        /// <summary>À appeler à chaque image par une fenêtre qui affiche les niveaux : le moteur n'envoie rien tant que personne ne regarde.</summary>
        public void WantMeters() { meterWantedUntil = Time.unscaledTime + 0.4f; }

        public void RequestDevices() { Send("DEVLIST"); }

        // ---- périphériques
        public void SetInput(string id, string name) { data.inDev = id; data.inName = name; Save(); Send("IN " + id); }
        public void SetOutput(string id, string name) { data.outDev = id; data.outName = name; Save(); Send("OUT " + id); }

        public string InputLabel()
        {
            if (data.inDev == "off") return Ar6dofLoc.T("Desactive");
            if (data.inDev == "default") return Ar6dofLoc.T("Par defaut de Windows");
            foreach (var d in inputs) if (d.id == data.inDev) return d.name;
            return string.IsNullOrEmpty(data.inName) ? data.inDev : data.inName;
        }

        public string OutputLabel()
        {
            if (data.outDev == "off") return Ar6dofLoc.T("Desactive");
            if (data.outDev == "default") return Ar6dofLoc.T("Par defaut de Windows");
            foreach (var d in outputs) if (d.id == data.outDev) return d.name;
            return string.IsNullOrEmpty(data.outName) ? data.outDev : data.outName;
        }

        // ---- voix
        public float GetVoice(string key, float def)
        {
            foreach (var kv in data.voice) if (kv.k == key) return kv.v;
            return def;
        }

        public void SetVoice(string key, float v)
        {
            AudioKV f = null;
            foreach (var kv in data.voice) if (kv.k == key) { f = kv; break; }
            if (f == null) { f = new AudioKV { k = key }; data.voice.Add(f); }
            f.v = v;
            MarkDirty();
            Send("V " + key + " " + N(v));
        }

        public void ApplyPreset(int index)
        {
            if (index < 0 || index >= PresetValues.Length) return;
            var sb = new StringBuilder("V");
            var all = new Dictionary<string, float>();
            foreach (var k in VoiceKeys) all[k.key] = k.def;
            var vals = PresetValues[index];
            for (int i = 0; i + 1 < vals.Length; i += 2) all[(string)vals[i]] = (float)vals[i + 1];
            foreach (var kv in all)
            {
                AudioKV f = null;
                foreach (var e in data.voice) if (e.k == kv.Key) { f = e; break; }
                if (f == null) { f = new AudioKV { k = kv.Key }; data.voice.Add(f); }
                f.v = kv.Value;
                sb.Append(' ').Append(kv.Key).Append(' ').Append(N(kv.Value));
            }
            MarkDirty();
            Send(sb.ToString());
        }

        // ---- canaux
        public static readonly string[] ChannelNames = { "voice", "sfx", "music", "master" };

        public void SetVolume(int ch, float v) { data.vol[ch] = v; MarkDirty(); Send("VOL " + ChannelNames[ch] + " " + N(v)); }
        public void SetMute(int ch, bool m) { data.mute[ch] = m; MarkDirty(); Send("MUTE " + ChannelNames[ch] + " " + (m ? "1" : "0")); }
        public void SetSolo(int ch, bool s) { if (ch >= 3) return; data.solo[ch] = s; MarkDirty(); Send("SOLO " + ChannelNames[ch] + " " + (s ? "1" : "0")); }
        public void SetPan(int ch, float p) { data.pan[ch] = p; MarkDirty(); Send("PAN " + ChannelNames[ch] + " " + N(p)); }
        /// <summary>Égaliseur d'une tranche : band 0 graves, 1 medium, 2 aigus, en dB (-12 à +12).</summary>
        public void SetEq(int ch, int band, float db)
        {
            data.eq[ch * 3 + band] = db;
            MarkDirty();
            Send("EQ " + ChannelNames[ch] + " " + N(data.eq[ch * 3]) + " " + N(data.eq[ch * 3 + 1]) + " " + N(data.eq[ch * 3 + 2]));
        }
        public void SetLimiter(bool on) { data.limiter = on; MarkDirty(); Send("LIMIT " + (on ? "1" : "0")); }
        public void SetViz(int v) { data.viz = v; MarkDirty(); }
        /// <summary>Remet une tranche à zéro (balance au centre, égaliseur à plat, ni muet ni solo) sans toucher à son fader.</summary>
        public void ResetStrip(int ch)
        {
            SetPan(ch, 0f);
            data.eq[ch * 3] = data.eq[ch * 3 + 1] = data.eq[ch * 3 + 2] = 0f;
            SetEq(ch, 0, 0f);
            SetMute(ch, false);
            SetSolo(ch, false);
        }

        // Lancer un son ou une musique doit toujours faire du bruit : moteur démarré et sortie choisie (sinon rien ne consomme le son
        // et la position de la musique reste à 0). La commande attend que le moteur réponde s'il démarre.
        readonly List<Action> whenOnline = new List<Action>();

        void EnsureOutput()
        {
            if (data.outDev == "off") { data.outDev = "default"; data.outName = ""; Save(); Send("OUT default"); }
            if (!data.enabled) SetEnabled(true);
            else if (State == EngineState.Off || State == EngineState.Failed) StartEngine();
        }

        void WhenOnline(Action a)
        {
            if (State == EngineState.Online) a();
            else if (State == EngineState.Starting && whenOnline.Count < 64) whenOnline.Add(a);
        }

        // ---- soundboard
        public PadDef AddPad(string path)
        {
            var p = new PadDef { slot = data.nextSlot++, path = path };
            string n = Path.GetFileNameWithoutExtension(path);
            p.label = n.Length > 24 ? n.Substring(0, 24) : n;
            Color c = Color.HSVToRGB((data.pads.Count * 0.13f) % 1f, 0.55f, 0.85f);
            p.r = c.r; p.g = c.g; p.b = c.b;
            data.pads.Add(p);
            MarkDirty();
            LoadPad(p);
            return p;
        }

        public PadDef DuplicatePad(PadDef src)
        {
            var p = JsonUtility.FromJson<PadDef>(JsonUtility.ToJson(src));
            p.slot = data.nextSlot++;
            data.pads.Insert(data.pads.IndexOf(src) + 1, p);
            MarkDirty();
            LoadPad(p);
            return p;
        }

        public void RemovePad(PadDef p)
        {
            data.pads.Remove(p);
            Send("UNLOAD " + p.slot);
            soundState.Remove(p.slot); soundMs.Remove(p.slot); loopingSlots.Remove(p.slot);
            MarkDirty();
        }

        public void LoadPad(PadDef p)
        {
            if (string.IsNullOrEmpty(p.path)) return;
            soundState.Remove(p.slot);
            if (State == EngineState.Online) Send("LOAD " + p.slot + " " + p.path);
        }

        public void PlayPad(PadDef p)
        {
            EnsureOutput();
            if (p.loop && loopingSlots.Contains(p.slot)) { Send("STOP " + p.slot); loopingSlots.Remove(p.slot); soundVersion++; return; }
            string cmd = "PLAY " + p.slot + " " + N(p.vol) + " " + N(p.pan) + " " + N(p.rate) + " " + (p.loop ? "1" : "0");
            WhenOnline(() => Send(cmd));
            if (p.loop) { loopingSlots.Add(p.slot); soundVersion++; }
        }

        public void StopAllSounds() { Send("STOPALL"); loopingSlots.Clear(); soundVersion++; }

        // ---- musique
        public bool MusicActive { get; private set; }
        public string TrackName
        {
            get
            {
                if (data.track < 0 || data.track >= data.playlist.Count) return "";
                return Path.GetFileNameWithoutExtension(data.playlist[data.track]);
            }
        }

        public void PlayTrack(int index)
        {
            if (index < 0 || index >= data.playlist.Count) return;
            data.track = index;
            musicError = "";
            MarkDirty();
            EnsureOutput();
            string loop = "MLOOP " + (data.repeat == 2 ? "1" : "0"), file = "MUSIC " + data.playlist[index];
            WhenOnline(() => { Send(loop); Send(file); });
            MusicActive = true;
            musicVersion++;
        }

        public void StopMusic() { Send("MSTOP"); MusicActive = false; musicVersion++; }
        public void PauseMusic(bool p) { Send("MPAUSE " + (p ? "1" : "0")); }
        public void SeekMusic(float sec) { Send("MSEEK " + N(sec)); }

        public void SetRepeat(int r) { data.repeat = r; MarkDirty(); Send("MLOOP " + (r == 2 ? "1" : "0")); }

        public void NextTrack(bool auto)
        {
            int n = data.playlist.Count;
            if (n == 0) return;
            int t = data.track;
            if (data.shuffle && n > 1) { int r = UnityEngine.Random.Range(0, n - 1); t = r >= t ? r + 1 : r; }
            else t = t + 1;
            if (t >= n) { if (auto && data.repeat == 0) { StopMusic(); return; } t = 0; }
            PlayTrack(t);
        }

        public void PrevTrack()
        {
            int n = data.playlist.Count;
            if (n == 0) return;
            PlayTrack(data.track <= 0 ? n - 1 : data.track - 1);
        }

        // ---------------------------------------------------------------- boucle principale
        void ApplyAll()
        {
            applied = true;
            Send("IN " + data.inDev);
            Send("OUT " + data.outDev);
            var sb = new StringBuilder("V");
            foreach (var kv in data.voice) sb.Append(' ').Append(kv.k).Append(' ').Append(N(kv.v));
            if (data.voice.Count > 0) Send(sb.ToString());
            for (int i = 0; i < 4; i++)
            {
                Send("VOL " + ChannelNames[i] + " " + N(data.vol[i]));
                Send("MUTE " + ChannelNames[i] + " " + (data.mute[i] ? "1" : "0"));
                if (i < 3 && data.solo[i]) Send("SOLO " + ChannelNames[i] + " 1");
                if (data.pan[i] != 0f) Send("PAN " + ChannelNames[i] + " " + N(data.pan[i]));
                if (data.eq[i * 3] != 0f || data.eq[i * 3 + 1] != 0f || data.eq[i * 3 + 2] != 0f)
                    Send("EQ " + ChannelNames[i] + " " + N(data.eq[i * 3]) + " " + N(data.eq[i * 3 + 1]) + " " + N(data.eq[i * 3 + 2]));
            }
            if (!data.limiter) Send("LIMIT 0");
            foreach (var p in data.pads) LoadPad(p);
            Send("DEVLIST");
        }

        void Update()
        {
            if (dirty && Time.unscaledTime > saveAt) Save();
            if (suspendAt > 0f && Time.unscaledTime >= suspendAt) { suspendAt = -1f; StopEngine(true); }
            string lf = launchFail;
            if (lf != null) { launchFail = null; State = EngineState.Failed; Message = Ar6dofLoc.T("Lancement impossible : ") + lf; return; }
            if (State == EngineState.Off || State == EngineState.Failed) return;

            // événements du moteur
            while (true)
            {
                string s;
                lock (lockObj) { if (events.Count == 0) break; s = events.Dequeue(); }
                Handle(s);
            }

            float now = Time.unscaledTime;
            if (State == EngineState.Starting)
            {
                if (now - lastPing > 0.4f) { Send("PING"); lastPing = now; }
                bool dead = false;
                try { dead = proc != null && proc.HasExited; } catch (Exception) { }
                if (now - stateSince > 8f || (dead && now - stateSince > 1.5f && proc != null && ExitCode() != 2))
                {
                    State = EngineState.Failed;
                    Message = Ar6dofLoc.T("Le moteur audio ne repond pas");
                }
            }
            else
            {
                // moteur en ligne : on vérifie qu'il est toujours là, et on lui dit si quelqu'un regarde les niveaux
                if (now - lastPing > 2f) { Send("PING"); lastPing = now; }
                if (now - lastPong > 7f)
                {
                    State = EngineState.Starting; stateSince = now; applied = false;
                    try { if (proc == null || proc.HasExited) { proc = null; RelaunchProcess(); } } catch (Exception) { }
                }
                if (now < meterWantedUntil && now - lastMeterPoke > 0.5f) { Send("METER"); lastMeterPoke = now; }
                if (now < djWantedUntil && now - lastDjPoke > 0.5f) { Send("DJWATCH"); lastDjPoke = now; }
            }
        }

        int ExitCode() { try { return proc.ExitCode; } catch (Exception) { return -1; } }

        void RelaunchProcess()
        {
            bool open = running;
            if (!open) return;
            string exe = ExePath();
            if (!File.Exists(exe)) return;
            try
            {
                var psi = new ProcessStartInfo(exe, "--parent " + Process.GetCurrentProcess().Id)
                {
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (s, e) => { };
                proc.ErrorDataReceived += (s, e) => { };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception) { }
        }

        void Handle(string s)
        {
            if (s.Length == 0) return;
            if (s == "READY" || s == "PONG")
            {
                if (s == "READY") { for (int i = 0; i < 2; i++) { decks[i].Reset(); decks[i].eq[0] = decks[i].eq[1] = decks[i].eq[2] = 0f; decks[i].filter = 0f; decks[i].vol = 1f; decks[i].range = 0; } xfade = 0.5f; xfCurve = 0; djVersion++; }   // moteur (re)lance : platines vides
                lastPong = Time.unscaledTime;
                if (State != EngineState.Online) { State = EngineState.Online; Message = ""; }
                if (!applied) ApplyAll();
                if (whenOnline.Count > 0)
                {
                    var todo = whenOnline.ToArray();
                    whenOnline.Clear();
                    foreach (var a in todo) a();
                }
                return;
            }
            if (s == "DEVBEGIN") { inputs.Clear(); outputs.Clear(); return; }
            if (s == "DEVEND") { deviceVersion++; return; }
            if (s.StartsWith("DEV\t", StringComparison.Ordinal))
            {
                var p = s.Split('\t');
                if (p.Length >= 5)
                {
                    var d = new AudioDevice { isDefault = p[2] == "1", id = p[3], name = p[4] };
                    (p[1] == "i" ? inputs : outputs).Add(d);
                }
                return;
            }
            if (s.StartsWith("IN ", StringComparison.Ordinal)) { InStatus = s.Substring(3); deviceVersion++; return; }
            if (s.StartsWith("OUT ", StringComparison.Ordinal)) { OutStatus = s.Substring(4); deviceVersion++; return; }
            if (s.StartsWith("SND ", StringComparison.Ordinal))
            {
                var p = s.Split(' ');
                int slot;
                if (p.Length >= 3 && int.TryParse(p[1], out slot))
                {
                    soundState[slot] = p[2] == "ok" ? 1 : 2;
                    int ms;
                    if (p.Length >= 4 && int.TryParse(p[3], out ms)) soundMs[slot] = ms;
                    soundVersion++;
                }
                return;
            }
            if (s.StartsWith("DST ", StringComparison.Ordinal)) { ParseDjStatus(s); return; }
            if (s.StartsWith("DECK ", StringComparison.Ordinal)) { ParseDeck(s); return; }
            if (s.StartsWith("DWAVE ", StringComparison.Ordinal)) { ParseWave(s); return; }
            if (s.StartsWith("MUSIC", StringComparison.Ordinal))
            {
                if (s == "MUSIC end") { MusicActive = false; musicVersion++; NextTrack(true); }
                else if (s == "MUSIC err") { MusicActive = false; musicError = Ar6dofLoc.T("Fichier illisible"); musicVersion++; }
                return;
            }
        }
    }
}
