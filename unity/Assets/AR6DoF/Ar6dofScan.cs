using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace AR6DoF
{
    public enum Ar6dofScanKind { Text, Url, Wifi, Contact, Event, Mail, Phone }

    /// <summary>Ce que contient un QR code : classement (lien, Wi-Fi, contact, evenement...) et lecture des formats courants. Pur texte, aucun cout.</summary>
    public static class Ar6dofScanText
    {
        public static Ar6dofScanKind Classify(string s)
        {
            if (string.IsNullOrEmpty(s)) return Ar6dofScanKind.Text;
            string t = s.TrimStart();
            if (StartsWith(t, "http://") || StartsWith(t, "https://")) return Ar6dofScanKind.Url;
            if (StartsWith(t, "WIFI:")) return Ar6dofScanKind.Wifi;
            if (StartsWith(t, "BEGIN:VEVENT") || (StartsWith(t, "BEGIN:VCALENDAR") && t.IndexOf("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase) >= 0)) return Ar6dofScanKind.Event;
            if (StartsWith(t, "BEGIN:VCARD") || StartsWith(t, "MECARD:")) return Ar6dofScanKind.Contact;
            if (StartsWith(t, "mailto:") || StartsWith(t, "MATMSG:")) return Ar6dofScanKind.Mail;
            if (StartsWith(t, "tel:") || StartsWith(t, "SMSTO:") || StartsWith(t, "sms:")) return Ar6dofScanKind.Phone;
            return Ar6dofScanKind.Text;
        }

        public static string KindName(Ar6dofScanKind k)
        {
            switch (k)
            {
                case Ar6dofScanKind.Url: return "Lien";
                case Ar6dofScanKind.Wifi: return "Reseau Wi-Fi";
                case Ar6dofScanKind.Contact: return "Contact";
                case Ar6dofScanKind.Event: return "Evenement";
                case Ar6dofScanKind.Mail: return "E-mail";
                case Ar6dofScanKind.Phone: return "Telephone";
                default: return "Texte";
            }
        }

        static bool StartsWith(string s, string p) { return s.StartsWith(p, StringComparison.OrdinalIgnoreCase); }

        /// <summary>Texte a afficher : le Wi-Fi est decrit en clair, le reste est montre tel quel.</summary>
        public static string Display(string s, Ar6dofScanKind k)
        {
            if (k == Ar6dofScanKind.Wifi)
            {
                string ssid = "", pass = "", type = "";
                foreach (string f in SplitFields(s.Trim().Substring(5)))
                {
                    if (f.StartsWith("S:")) ssid = f.Substring(2);
                    else if (f.StartsWith("P:")) pass = f.Substring(2);
                    else if (f.StartsWith("T:")) type = f.Substring(2);
                }
                var sb = new StringBuilder();
                sb.Append(Ar6dofLoc.T("Reseau")).Append(" : ").Append(ssid);
                if (type.Length > 0) sb.Append('\n').Append(Ar6dofLoc.T("Securite")).Append(" : ").Append(type);
                if (pass.Length > 0) sb.Append('\n').Append(Ar6dofLoc.T("Mot de passe")).Append(" : ").Append(pass);
                return sb.ToString();
            }
            return s.Trim();
        }

        // champs separes par ';' (l'antislash protege ; , : \)
        static List<string> SplitFields(string s)
        {
            var res = new List<string>();
            var cur = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length) { cur.Append(s[++i]); continue; }
                if (c == ';') { if (cur.Length > 0) res.Add(cur.ToString()); cur.Length = 0; continue; }
                cur.Append(c);
            }
            if (cur.Length > 0) res.Add(cur.ToString());
            return res;
        }

        // ------------------------------------------------------------------ evenements iCalendar (QR de convention, billets...)
        public static List<Ar6dofParsedEvent> ParseEvents(string text)
        {
            var res = new List<Ar6dofParsedEvent>();
            if (string.IsNullOrEmpty(text)) return res;
            // lignes « pliees » : une ligne qui commence par un espace ou une tabulation continue la precedente
            var lines = new List<string>();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0) lines[lines.Count - 1] += raw.Substring(1);
                else lines.Add(raw);
            }
            Ar6dofParsedEvent cur = null;
            bool hasStart = false;
            DateTime endT = DateTime.MinValue;
            bool hasEnd = false;
            foreach (string l0 in lines)
            {
                string l = l0.Trim();
                if (l.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { cur = new Ar6dofParsedEvent(); hasStart = false; hasEnd = false; continue; }
                if (l.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    if (cur != null && hasStart)
                    {
                        if (hasEnd && endT > cur.start) cur.durMin = (int)Math.Min(24 * 60, (endT - cur.start).TotalMinutes);
                        res.Add(cur);
                    }
                    cur = null;
                    continue;
                }
                if (cur == null) continue;
                int colon = l.IndexOf(':');
                if (colon <= 0) continue;
                string key = l.Substring(0, colon);
                int semi = key.IndexOf(';');
                if (semi >= 0) key = key.Substring(0, semi);
                key = key.ToUpperInvariant();
                string val = l.Substring(colon + 1);
                DateTime dt;
                if (key == "SUMMARY") cur.title = Unescape(val);
                else if (key == "LOCATION") cur.place = Unescape(val);
                else if (key == "DTSTART" && ParseTime(val, out dt)) { cur.start = dt; hasStart = true; }
                else if (key == "DTEND" && ParseTime(val, out dt)) { endT = dt; hasEnd = true; }
            }
            return res;
        }

        static string Unescape(string v)
        {
            return v.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
        }

        static bool ParseTime(string v, out DateTime dt)
        {
            v = v.Trim();
            bool utc = v.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            if (utc) v = v.Substring(0, v.Length - 1);
            if (DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                if (utc) dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
                return true;
            }
            if (DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return true;
            dt = DateTime.MinValue;
            return false;
        }
    }

    /// <summary>
    /// Traduction EN LIGNE d'un texte (service gratuit MyMemory, https://api.mymemory.translated.net). Le texte part sur Internet uniquement quand on touche « Traduire » :
    /// l'interface le dit en clair. Decoupe en morceaux de 450 octets maximum, un par requete (UnityWebRequest, sans fil ni coroutine : on interroge dans Tick),
    /// langue d'origine detectee par le service. Limite gratuite du service : environ 5 000 caracteres par jour sans compte. Rien ne tourne hors traduction.
    /// </summary>
    public class Ar6dofTranslator
    {
        public static readonly string[] Codes = { "fr", "en", "es", "de", "it", "pt" };
        public static readonly string[] Names = { "Francais", "English", "Espanol", "Deutsch", "Italiano", "Portugues" };
        const int MaxBytes = 450;
        const int MaxChars = 2500;

        [Serializable] public class MmData { public string translatedText = ""; }
        [Serializable] public class MmResp { public MmData responseData = null; }

        public string result = "", error = "";
        public bool truncated;
        public bool Busy { get { return req != null; } }

        readonly Action done;
        readonly List<string> chunks = new List<string>();
        readonly StringBuilder acc = new StringBuilder();
        UnityWebRequest req;
        int idx;
        string target = "en";

        public Ar6dofTranslator(Action onDone) { done = onDone; }

        public static int DefaultIndex()
        {
            int i = Array.IndexOf(Codes, Ar6dofLoc.Code);
            return i >= 0 ? i : 1;
        }

        public void Clear() { Cancel(); result = ""; error = ""; truncated = false; }

        public void Cancel()
        {
            if (req != null) { try { req.Abort(); req.Dispose(); } catch (Exception) { } req = null; }
            chunks.Clear(); acc.Length = 0; idx = 0;
        }

        public void Start(string text, int targetIndex)
        {
            Clear();
            if (string.IsNullOrWhiteSpace(text)) return;
            target = Codes[Mathf.Clamp(targetIndex, 0, Codes.Length - 1)];
            if (text.Length > MaxChars) { text = text.Substring(0, MaxChars); truncated = true; }
            Split(text);
            if (chunks.Count == 0) return;
            idx = 0;
            Send();
        }

        void Split(string text)
        {
            var cur = new StringBuilder();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                while (Encoding.UTF8.GetByteCount(line) > MaxBytes)
                {
                    int cut = Math.Min(line.Length, MaxBytes / 3);
                    while (cut < line.Length - 1 && Encoding.UTF8.GetByteCount(line.Substring(0, cut + 1)) <= MaxBytes) cut++;
                    int sp = line.LastIndexOf(' ', cut - 1, Math.Min(cut, 80));
                    if (sp > 0) cut = sp;
                    Flush(cur);
                    chunks.Add(line.Substring(0, cut).Trim());
                    line = line.Substring(cut).Trim();
                }
                if (line.Length == 0) continue;
                if (cur.Length > 0 && Encoding.UTF8.GetByteCount(cur.ToString()) + 1 + Encoding.UTF8.GetByteCount(line) > MaxBytes) Flush(cur);
                if (cur.Length > 0) cur.Append('\n');
                cur.Append(line);
            }
            Flush(cur);
        }

        void Flush(StringBuilder cur)
        {
            if (cur.Length > 0) chunks.Add(cur.ToString());
            cur.Length = 0;
        }

        void Send()
        {
            try
            {
                string url = "https://api.mymemory.translated.net/get?q=" + UnityWebRequest.EscapeURL(chunks[idx]) + "&langpair=Autodetect%7C" + target;
                req = UnityWebRequest.Get(url);
                req.timeout = 12;
                req.SendWebRequest();
            }
            catch (Exception e) { Fail(e.Message); }
        }

        void Fail(string msg)
        {
            error = Ar6dofLoc.F("Traduction impossible : {0}", Ar6dofLoc.T(string.IsNullOrEmpty(msg) ? "reseau" : (msg.Length > 100 ? msg.Substring(0, 100) : msg)));
            Cancel();
            if (done != null) done();
        }

        /// <summary>A appeler a chaque image par la fenetre ouverte : ne fait rien sans requete en cours.</summary>
        public void Tick()
        {
            if (req == null || !req.isDone) return;
            var rq = req;
            req = null;
            string text = null, err = null;
            if (rq.result == UnityWebRequest.Result.Success) text = rq.downloadHandler.text;
            else err = string.IsNullOrEmpty(rq.error) ? "reseau" : rq.error;
            try { rq.Dispose(); } catch (Exception) { }
            if (text == null) { Fail(err); return; }
            string tr = null;
            try { var r = JsonUtility.FromJson<MmResp>(text); if (r != null && r.responseData != null) tr = r.responseData.translatedText; } catch (Exception) { }
            if (string.IsNullOrEmpty(tr)) { Fail("reponse illisible"); return; }
            if (tr.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) >= 0) { Fail("limite du jour atteinte sur le service"); return; }
            if (tr.IndexOf("PLEASE SELECT TWO DISTINCT LANGUAGES", StringComparison.OrdinalIgnoreCase) >= 0) tr = chunks[idx];   // deja dans la langue voulue
            else if (tr.IndexOf("INVALID", StringComparison.OrdinalIgnoreCase) >= 0 && tr.ToUpperInvariant() == tr) { Fail("langue non reconnue"); return; }
            if (acc.Length > 0) acc.Append('\n');
            acc.Append(WebUtility.HtmlDecode(tr).Trim());
            idx++;
            if (idx < chunks.Count) { Send(); return; }
            result = acc.ToString();
            chunks.Clear(); acc.Length = 0;
            if (done != null) done();
        }
    }

    /// <summary>
    /// Une session de scan, PARTAGEE par l'appareil photo et le calendrier : photo apres 3 s (camera gauche, 8 images accumulees, mode texte), lecture d'un QR code
    /// (Ar6dofQr, dans un autre fil), et, si aucun QR n'est trouve et que l'appelant le veut, lecture du texte par l'OCR de Windows. Rien ne tourne hors scan.
    /// Les images sont temporaires et supprimees des que la lecture est finie (ou annulee).
    /// </summary>
    public class Ar6dofScanSession
    {
        public enum Phase { Idle, Countdown, Reading, Done }

        public const float Delay = 3f;
        public Phase phase = Phase.Idle;
        public string status = "", error = "", info = "", qr;
        public int countSec;
        public readonly List<string> lines = new List<string>();

        readonly Ar6dofDock dock;
        readonly string appId, appTitle;
        readonly Action changed, statusChanged;
        int gen, lastCount = -1;
        string pathA, pathB;
        Task<string> qrTask;
        Ar6dofOcr.Job job;
        bool wantOcr;
        List<Ar6dofParsedEvent> evCache;
        Ar6dofScanKind kind;

        public static string TempFolder { get { return Path.Combine(Path.GetTempPath(), "AR6DoF_scan"); } }

        /// <param name="changed">appele quand la phase change (la fenetre se reconstruit)</param>
        /// <param name="statusChanged">appele quand seul le texte d'etat change (compte a rebours)</param>
        public Ar6dofScanSession(Ar6dofDock d, string appId, string appTitle, Action changed, Action statusChanged)
        {
            dock = d; this.appId = appId; this.appTitle = appTitle; this.changed = changed; this.statusChanged = statusChanged;
        }

        public Ar6dofScanKind Kind { get { return kind; } }
        public bool Busy { get { return phase == Phase.Countdown || phase == Phase.Reading; } }

        /// <summary>Texte a montrer : le QR (decrit) ou les lignes lues par l'OCR.</summary>
        public string BodyText()
        {
            if (qr != null) return Ar6dofScanText.Display(qr, kind);
            if (lines.Count == 0) return "";
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Texte brut (pour copier, ouvrir, traduire).</summary>
        public string RawText() { return qr != null ? qr.Trim() : BodyText(); }

        /// <summary>Evenements trouves : ceux d'un QR iCalendar, sinon ceux que l'analyseur d'horaires tire du texte lu.</summary>
        public List<Ar6dofParsedEvent> Events(DateTime now)
        {
            if (evCache != null) return evCache;
            if (qr != null) evCache = kind == Ar6dofScanKind.Event ? Ar6dofScanText.ParseEvents(qr) : new List<Ar6dofParsedEvent>();
            else evCache = lines.Count > 0 ? Ar6dofCalParser.Parse(lines, now) : new List<Ar6dofParsedEvent>();
            return evCache;
        }

        void Clear()
        {
            phase = Phase.Idle; status = ""; error = ""; info = ""; qr = null; countSec = 0; lastCount = -1;
            lines.Clear(); evCache = null; kind = Ar6dofScanKind.Text;
            job = null; qrTask = null;
        }

        void DeleteTemp()
        {
            if (pathA != null) { try { File.Delete(pathA); } catch (Exception) { } pathA = null; }
            if (pathB != null) { try { File.Delete(pathB); } catch (Exception) { } pathB = null; }
        }

        static void DeleteAll(List<string> files)
        {
            if (files == null) return;
            foreach (string f in files) { try { File.Delete(f); } catch (Exception) { } }
        }

        /// <summary>Arret sans bruit (fenetre fermee ou reduite) : annule la photo, oublie tout, supprime les images temporaires.</summary>
        public void Abort()
        {
            var pt = dock != null ? dock.passthrough : null;
            if (pt != null && pt.PhotoBusy) pt.CancelPhoto();
            gen++;
            DeleteTemp();
            Clear();
        }

        /// <summary>Bouton Annuler.</summary>
        public void Cancel()
        {
            var pt = dock != null ? dock.passthrough : null;
            if (phase == Phase.Countdown && pt != null && pt.PhotoBusy) { pt.CancelPhoto(); return; }   // la photo repond « Annule » et remet tout a zero
            gen++;
            DeleteTemp();
            Clear();
            if (changed != null) changed();
        }

        public void Start(bool ocr)
        {
            var pt = dock != null ? dock.passthrough : null;
            gen++;
            DeleteTemp();
            Clear();
            Ar6dofNotifyService.Resolve("scan");
            if (pt == null || pt.PhotoBusy) { Fail("Camera indisponible : le passthrough n'est pas pret"); return; }
            wantOcr = ocr;
            int my = gen;
            phase = Phase.Countdown;
            status = Ar6dofLoc.F("Scan dans {0} s...", Mathf.CeilToInt(Delay));
            var es = pt.EyeSize;
            // 8 images accumulees (bruit reduit, grille x2), niveaux de gris, contraste etire : la lecture du texte et du QR en profitent
            var o = new Ar6dofPassthrough.PhotoOptions { withUi = false, eye = 0, width = es.x, height = es.y, folder = TempFolder, burst = 8, upscale = 2, forText = true };
            pt.TakePhoto(o, Delay, s => OnCount(my, s), null, (f, t, e) => OnPhoto(my, f, t, e));
            if (changed != null) changed();
        }

        void OnCount(int my, int sec)
        {
            if (my != gen || sec == lastCount) return;   // appele a chaque image : on ne touche au texte que quand le chiffre change
            lastCount = sec;
            countSec = sec;
            status = sec > 0 ? Ar6dofLoc.F("Scan dans {0} s...", sec) : Ar6dofLoc.T("Photo...");
            if (statusChanged != null) statusChanged();
        }

        void OnPhoto(int my, List<string> files, Texture2D tex, string err)
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
            if (my != gen) { DeleteAll(files); return; }
            countSec = 0;
            if (!string.IsNullOrEmpty(err))
            {
                if (err == "Annule") { Clear(); if (changed != null) changed(); }
                else Fail(Ar6dofLoc.F("Photo impossible : {0}", err));
                return;
            }
            if (files == null || files.Count == 0) { Fail("Photo impossible : rien n'a ete enregistre"); return; }
            for (int i = 1; i < files.Count; i++) { try { File.Delete(files[i]); } catch (Exception) { } }
            pathA = files[0];
            var pt = dock != null ? dock.passthrough : null;
            info = pt != null ? pt.LastStackInfo : "";
            phase = Phase.Reading;
            status = Ar6dofLoc.T("Lecture du code QR...");
            // l'OCR supprime l'image qu'on lui donne : on lui prepare une copie avant tout
            if (wantOcr)
            {
                try { pathB = Path.Combine(Path.GetDirectoryName(pathA), Path.GetFileNameWithoutExtension(pathA) + "_ocr.png"); File.Copy(pathA, pathB, true); }
                catch (Exception) { pathB = null; }
            }
            // decodage de l'image sur le fil principal (une fois), calcul du QR dans un autre fil
            try
            {
                byte[] bytes = File.ReadAllBytes(pathA);
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool ok = t.LoadImage(bytes);
                int w = t.width, h = t.height;
                Color32[] px = ok ? t.GetPixels32() : null;
                UnityEngine.Object.Destroy(t);
                if (px == null) throw new Exception("image illisible");
                qrTask = Task.Run(() => Ar6dofQr.DecodePixels(px, w, h));
            }
            catch (Exception)
            {
                qrTask = null;
                if (wantOcr) StartOcr(); else { DeleteTemp(); Finish(); }
                return;
            }
            if (changed != null) changed();
        }

        void StartOcr()
        {
            if (pathB == null) { DeleteTemp(); Finish(); return; }
            status = Ar6dofLoc.T("Lecture du texte...");
            string p = pathB;
            pathB = null;
            if (pathA != null) { try { File.Delete(pathA); } catch (Exception) { } pathA = null; }
            job = Ar6dofOcr.Start(p);   // l'OCR supprime lui-meme la copie
            if (statusChanged != null) statusChanged();
        }

        void Fail(string msg)
        {
            DeleteTemp();
            error = msg;
            phase = Phase.Done;
            Ar6dofNotifyService.Post(Ar6dofNoteKind.Error, Ar6dofLoc.T(appTitle), Ar6dofLoc.T(msg), appId, "scan");
            if (changed != null) changed();
        }

        void Finish()
        {
            phase = Phase.Done;
            if (changed != null) changed();
        }

        /// <summary>A appeler a chaque image par la fenetre ouverte (ne fait rien hors lecture).</summary>
        public void Tick()
        {
            if (phase != Phase.Reading) return;
            if (qrTask != null)
            {
                if (!qrTask.IsCompleted) return;
                string r = qrTask.IsFaulted ? null : qrTask.Result;
                qrTask = null;
                if (!string.IsNullOrEmpty(r))
                {
                    qr = r;
                    kind = Ar6dofScanText.Classify(r);
                    DeleteTemp();
                    Finish();
                    return;
                }
                if (wantOcr) { StartOcr(); return; }
                DeleteTemp();
                Finish();
                return;
            }
            if (job != null && job.done)
            {
                var j = job;
                job = null;
                if (j.error.Length > 0) { Fail(j.error); return; }
                lines.AddRange(j.lines);
                if (j.info.Length > 0) info = (info.Length > 0 ? info + " ; " : "") + "OCR " + j.info;
                Finish();
            }
        }
    }

    /// <summary>
    /// Base commune de l'appareil photo et du calendrier : la meme session de scan, les memes boutons de resultat (copier, ouvrir un lien, traduire, ajouter au calendrier).
    /// Les fenetres appellent TickScan() dans OnTick et AbortScan() quand elles se ferment ou se reduisent.
    /// </summary>
    public abstract class Ar6dofScanWindow : Ar6dofAppWindow
    {
        protected readonly Ar6dofScanSession scan;
        protected readonly Ar6dofTranslator tr;
        protected string scanNote = "";
        int tlang = -1;

        protected Ar6dofScanWindow(Ar6dofDock d, string id, string name, string glyph, string desc, float width) : base(d, id, name, glyph, desc, width)
        {
            scan = new Ar6dofScanSession(d, id, name, OnScanChanged, OnScanStatus);
            tr = new Ar6dofTranslator(OnScanChanged);
        }

        protected bool IsOpen { get { return canvas != null && canvas.gameObject.activeSelf; } }

        protected virtual void OnScanChanged() { if (IsOpen) Rebuild(); }
        void OnScanStatus() { if (IsOpen) Refresh(); }

        protected void TickScan() { scan.Tick(); tr.Tick(); }

        protected void AbortScan()
        {
            scan.Abort();
            tr.Clear();
            scanNote = "";
        }

        protected void StartScan(bool ocr)
        {
            tr.Clear();
            scanNote = "";
            scan.Start(ocr);
        }

        public override void OnClosed()
        {
            AbortScan();
            base.OnClosed();
        }

        int TargetLang
        {
            get
            {
                if (tlang < 0)
                {
                    try { tlang = PlayerPrefs.GetInt("ar6dof_scan_tl", -1); } catch (Exception) { tlang = -1; }
                    if (tlang < 0 || tlang >= Ar6dofTranslator.Codes.Length) tlang = Ar6dofTranslator.DefaultIndex();
                }
                return tlang;
            }
            set
            {
                tlang = value;
                try { PlayerPrefs.SetInt("ar6dof_scan_tl", value); } catch (Exception) { }
            }
        }

        /// <summary>Paragraphe qui s'ajuste a la quantite de texte (au plus 'maxLines' lignes, le reste est remplace par « ... »).</summary>
        protected Text ParaFit(string text, int fs, int maxLines)
        {
            int cpl = Mathf.Max(20, Mathf.FloorToInt(IW / (fs * 0.52f)));
            var sb = new StringBuilder();
            int used = 0;
            bool cut = false;
            string[] parts = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                string ln = parts[i];
                int n = Mathf.Max(1, (ln.Length + cpl - 1) / cpl);
                if (used + n > maxLines)
                {
                    int room = maxLines - used;
                    if (room > 0 && ln.Length > 0) { sb.Append(ln.Substring(0, Mathf.Min(ln.Length, room * cpl))); used = maxLines; }
                    cut = true;
                    break;
                }
                sb.Append(ln);
                used += n;
                if (i < parts.Length - 1) sb.Append('\n');
            }
            if (cut) { sb.Append("..."); used = maxLines; }
            return Para(sb.ToString(), 24f * Mathf.Max(1, used) + 8f, fs);
        }

        /// <summary>Compte a rebours / lecture en cours : un texte d'etat et un bouton Annuler.</summary>
        protected void BuildScanBusy()
        {
            var st = Lbl("", 44, 24);
            refreshers.Add(() => st.text = scan.status);
            Btn("Annuler", scan.Cancel, 52f, 22);
        }

        /// <summary>Resultat d'un scan : QR decrit ou texte lu, Copier, Ouvrir le lien (QR http/https, seulement au clic), Traduire (en ligne, seulement au clic), calendrier.</summary>
        protected void BuildScanResult(bool allowTranslate, bool allowCalendar)
        {
            var s = scan;
            if (scanNote.Length > 0) Lbl(scanNote, 30, 19).MakeAccent();
            string body = s.BodyText();
            if (s.error.Length > 0) Para(s.error, 80, 19);
            else if (body.Length == 0) Para("Rien de lisible. Rapproche-toi, ameliore la lumiere et ne bouge pas pendant la photo.", 56, 18);
            else
            {
                string head = s.qr != null ? Ar6dofLoc.F("Code QR : {0}", Ar6dofLoc.T(Ar6dofScanText.KindName(s.Kind))) : Ar6dofLoc.T("Texte lu");
                Lbl(head, 30, 20).MakeAccent();
                ParaFit(body, 18, 9);
                string raw = s.RawText();
                bool url = s.qr != null && s.Kind == Ar6dofScanKind.Url;
                List<Button> bs;
                if (url)
                    bs = BtnRow(52f, 21, "Copier", (Action)(() => CopyTo(raw, 0)),
                                         "Ouvrir le lien", (Action)(() => { try { Application.OpenURL(raw); } catch (Exception) { } }));
                else
                    bs = BtnRow(52f, 21, "Copier", (Action)(() => CopyTo(raw, 0)));
                copyBtn = bs[0].GetComponentInChildren<Text>();

                if (allowTranslate && (s.qr == null || s.Kind == Ar6dofScanKind.Text))
                {
                    Section("Traduction");
                    var tb = BtnRow(52f, 20, "", (Action)(() => { TargetLang = (TargetLang + 1) % Ar6dofTranslator.Codes.Length; Refresh(); }),
                                             "", (Action)(() => { if (tr.Busy) tr.Cancel(); else tr.Start(raw, TargetLang); Rebuild(); }));
                    var t0 = tb[0].GetComponentInChildren<Text>();
                    var t1 = tb[1].GetComponentInChildren<Text>();
                    refreshers.Add(() =>
                    {
                        t0.text = Ar6dofLoc.T("Vers") + " : " + Ar6dofTranslator.Names[TargetLang];
                        t1.text = Ar6dofLoc.T(tr.Busy ? "Annuler" : "Traduire (en ligne)");
                    });
                    Para("Traduire envoie ce texte au service en ligne MyMemory. Rien n'est envoye avant ton clic.", 40, 15);
                    if (tr.Busy) Para("Traduction en cours...", 30, 18);
                    else if (tr.error.Length > 0) Para(tr.error, 56, 18);
                    else if (tr.result.Length > 0)
                    {
                        ParaFit(tr.result, 18, 9);
                        if (tr.truncated) Para("Texte long : seuls les 2500 premiers caracteres ont ete traduits.", 30, 15);
                        string trr = tr.result;
                        var cb = BtnRow(52f, 21, "Copier la traduction", (Action)(() => CopyTo(trr, 1)));
                        copyBtn2 = cb[0].GetComponentInChildren<Text>();
                    }
                }

                if (allowCalendar)
                {
                    var evs = s.Events(DateTime.Now);
                    int n = 0;
                    foreach (var e in evs) if (e.start > DateTime.Now) n++;
                    if (n > 0) Btn(Ar6dofLoc.F("Ajouter au calendrier ({0})", n), AddToCalendar, 52f, 21);
                }
            }
            Btn("Nouveau scan", () => StartScan(true), 52f, 22);
        }

        Text copyBtn, copyBtn2;

        protected override void OnRebuildStart() { copyBtn = null; copyBtn2 = null; }

        void CopyTo(string text, int which)
        {
            try { GUIUtility.systemCopyBuffer = text; } catch (Exception) { }
            var t = which == 0 ? copyBtn : copyBtn2;
            if (t != null) t.text = Ar6dofLoc.T("Copie !");
        }

        void AddToCalendar()
        {
            var svc = Ar6dofCalendarService.Instance;
            if (svc == null) { scanNote = Ar6dofLoc.T("Calendrier indisponible."); Rebuild(); return; }
            var list = new List<Ar6dofCalEvent>();
            DateTime now = DateTime.Now;
            foreach (var p in scan.Events(now))
            {
                if (p.start <= now) continue;
                var ev = new Ar6dofCalEvent { title = p.title, place = p.place, durMin = p.durMin, remind = true };
                ev.Start = p.start;
                list.Add(ev);
            }
            int added = svc.AddRange(list);
            scanNote = added == 0 ? Ar6dofLoc.T("Rien de nouveau a enregistrer") : Ar6dofLoc.F("{0} evenement(s) ajoute(s)", added);
            Rebuild();
        }
    }
}
