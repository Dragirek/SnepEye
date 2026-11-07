using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace AR6DoF
{
    public enum Ar6dofScanKind { Text, Url, Wifi, Contact, Event, Mail, Phone, Geo, Sms, Otp, Scheme }

    /// <summary>
    /// Ce que contient un QR code : classement (lien, Wi-Fi, contact vCard ou MECARD, evenement, e-mail, telephone, SMS, position GPS, code d'authentification...) et lecture
    /// des formats courants en clair. Pur texte, aucun cout. Seuls les liens http et https peuvent etre ouverts (et seulement au clic) : les autres adresses sont montrees, jamais lancees.
    /// </summary>
    public static class Ar6dofScanText
    {
        static readonly string[] SchemeNames = { "bitcoin:", "litecoin:", "ethereum:", "monero:", "upi:", "paypal:", "market:", "intent:", "whatsapp:", "tg:", "spotify:", "zoommtg:", "skype:", "facetime:" };

        public static Ar6dofScanKind Classify(string s)
        {
            if (string.IsNullOrEmpty(s)) return Ar6dofScanKind.Text;
            string t = s.TrimStart();
            if (StartsWith(t, "http://") || StartsWith(t, "https://") || (StartsWith(t, "www.") && t.IndexOf(' ') < 0 && t.IndexOf('.', 4) > 4)) return Ar6dofScanKind.Url;
            if (StartsWith(t, "WIFI:")) return Ar6dofScanKind.Wifi;
            if (StartsWith(t, "BEGIN:VEVENT") || (StartsWith(t, "BEGIN:VCALENDAR") && t.IndexOf("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase) >= 0)) return Ar6dofScanKind.Event;
            if (StartsWith(t, "BEGIN:VCARD") || StartsWith(t, "MECARD:") || StartsWith(t, "BIZCARD:")) return Ar6dofScanKind.Contact;
            if (StartsWith(t, "mailto:") || StartsWith(t, "MATMSG:")) return Ar6dofScanKind.Mail;
            if (StartsWith(t, "tel:")) return Ar6dofScanKind.Phone;
            if (StartsWith(t, "SMSTO:") || StartsWith(t, "sms:") || StartsWith(t, "MMSTO:") || StartsWith(t, "mms:")) return Ar6dofScanKind.Sms;
            if (StartsWith(t, "geo:")) return Ar6dofScanKind.Geo;
            if (StartsWith(t, "otpauth:") || StartsWith(t, "otpauth-migration:")) return Ar6dofScanKind.Otp;
            int sch = t.IndexOf("://", StringComparison.Ordinal);
            if (sch > 1 && sch <= 21 && IsSchemeName(t, sch)) return Ar6dofScanKind.Scheme;
            for (int i = 0; i < SchemeNames.Length; i++) if (StartsWith(t, SchemeNames[i])) return Ar6dofScanKind.Scheme;
            return Ar6dofScanKind.Text;
        }

        static bool IsSchemeName(string t, int len)
        {
            if (!char.IsLetter(t[0])) return false;
            for (int i = 0; i < len; i++)
            {
                char c = t[i];
                if (!(char.IsLetterOrDigit(c) || c == '+' || c == '.' || c == '-')) return false;
            }
            return true;
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
                case Ar6dofScanKind.Sms: return "SMS";
                case Ar6dofScanKind.Geo: return "Position GPS";
                case Ar6dofScanKind.Otp: return "Code d'authentification";
                case Ar6dofScanKind.Scheme: return "Adresse speciale (non ouverte)";
                default: return "Texte";
            }
        }

        static bool StartsWith(string s, string p) { return s.StartsWith(p, StringComparison.OrdinalIgnoreCase); }

        /// <summary>Adresse a ouvrir pour un lien (http ou https seulement ; « www.x.y » devient https://www.x.y), sinon une chaine vide.</summary>
        public static string OpenTarget(string raw, Ar6dofScanKind k)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string t = raw.Trim();
            if (k == Ar6dofScanKind.Url)
            {
                if (StartsWith(t, "www.")) return "https://" + t;
                return t;
            }
            if (k == Ar6dofScanKind.Geo)
            {
                double lat, lon;
                if (GeoPoint(t, out lat, out lon))
                    return "https://www.openstreetmap.org/?mlat=" + lat.ToString("0.#####", CultureInfo.InvariantCulture) + "&mlon=" + lon.ToString("0.#####", CultureInfo.InvariantCulture)
                         + "#map=17/" + lat.ToString("0.#####", CultureInfo.InvariantCulture) + "/" + lon.ToString("0.#####", CultureInfo.InvariantCulture);
            }
            return "";
        }

        static bool GeoPoint(string t, out double lat, out double lon)
        {
            lat = 0; lon = 0;
            string body = t.Length > 4 ? t.Substring(4) : "";
            int cut = body.IndexOfAny(new[] { ';', '?' });
            if (cut >= 0) body = body.Substring(0, cut);
            string[] p = body.Split(',');
            if (p.Length < 2) return false;
            if (!double.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat)) return false;
            if (!double.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lon)) return false;
            return Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
        }

        /// <summary>Texte a afficher : les formats courants sont decrits en clair, le reste est montre tel quel.</summary>
        public static string Display(string s, Ar6dofScanKind k)
        {
            switch (k)
            {
                case Ar6dofScanKind.Wifi: return DisplayWifi(s);
                case Ar6dofScanKind.Contact: return DisplayContact(s);
                case Ar6dofScanKind.Mail: return DisplayMail(s);
                case Ar6dofScanKind.Phone: return DisplayPhone(s);
                case Ar6dofScanKind.Sms: return DisplaySms(s);
                case Ar6dofScanKind.Geo: return DisplayGeo(s);
                case Ar6dofScanKind.Otp: return DisplayOtp(s);
            }
            return s.Trim();
        }

        static void Add(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Ar6dofLoc.T(label)).Append(" : ").Append(value.Trim());
        }

        static string DisplayWifi(string s)
        {
            string ssid = "", pass = "", type = "";
            foreach (string f in SplitFields(s.Trim().Substring(5)))
            {
                if (f.StartsWith("S:")) ssid = f.Substring(2);
                else if (f.StartsWith("P:")) pass = f.Substring(2);
                else if (f.StartsWith("T:")) type = f.Substring(2);
            }
            var sb = new StringBuilder();
            Add(sb, "Reseau", ssid);
            Add(sb, "Securite", type);
            Add(sb, "Mot de passe", pass);
            return sb.ToString();
        }

        // « %20 », « + » : texte d'un parametre d'adresse
        static string Decode(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            try { return Uri.UnescapeDataString(v.Replace('+', ' ')); } catch (Exception) { return v; }
        }

        static string Query(string q, string key)
        {
            foreach (string part in q.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (part.Substring(0, eq).Equals(key, StringComparison.OrdinalIgnoreCase)) return Decode(part.Substring(eq + 1));
            }
            return "";
        }

        static string DisplayMail(string s)
        {
            string t = s.Trim();
            var sb = new StringBuilder();
            if (StartsWith(t, "MATMSG:"))
            {
                foreach (string f in SplitFields(t.Substring(7)))
                {
                    if (StartsWith(f, "TO:")) Add(sb, "Destinataire", f.Substring(3));
                    else if (StartsWith(f, "SUB:")) Add(sb, "Objet", f.Substring(4));
                    else if (StartsWith(f, "BODY:")) Add(sb, "Message", f.Substring(5));
                }
                return sb.Length > 0 ? sb.ToString() : t;
            }
            string rest = t.Substring(7);
            int qm = rest.IndexOf('?');
            string to = Decode(qm >= 0 ? rest.Substring(0, qm) : rest);
            string q = qm >= 0 ? rest.Substring(qm + 1) : "";
            Add(sb, "Destinataire", to);
            Add(sb, "Objet", Query(q, "subject"));
            Add(sb, "Message", Query(q, "body"));
            return sb.Length > 0 ? sb.ToString() : t;
        }

        static string DisplayPhone(string s)
        {
            string t = s.Trim();
            var sb = new StringBuilder();
            Add(sb, "Numero", Decode(t.Length > 4 ? t.Substring(4) : ""));
            return sb.Length > 0 ? sb.ToString() : t;
        }

        static string DisplaySms(string s)
        {
            string t = s.Trim();
            int colon = t.IndexOf(':');
            string rest = colon >= 0 ? t.Substring(colon + 1) : t;
            string num, msg = "";
            if (StartsWith(t, "SMSTO:") || StartsWith(t, "MMSTO:"))
            {
                int c2 = rest.IndexOf(':');
                num = c2 >= 0 ? rest.Substring(0, c2) : rest;
                msg = c2 >= 0 ? rest.Substring(c2 + 1) : "";
            }
            else
            {
                int qm = rest.IndexOf('?');
                num = qm >= 0 ? rest.Substring(0, qm) : rest;
                if (qm >= 0) msg = Query(rest.Substring(qm + 1), "body");
            }
            var sb = new StringBuilder();
            Add(sb, "Numero", Decode(num));
            Add(sb, "Message", msg);
            return sb.Length > 0 ? sb.ToString() : t;
        }

        static string DisplayGeo(string s)
        {
            double lat, lon;
            var sb = new StringBuilder();
            if (GeoPoint(s.Trim(), out lat, out lon)) Add(sb, "Position", lat.ToString("0.#####", CultureInfo.InvariantCulture) + ", " + lon.ToString("0.#####", CultureInfo.InvariantCulture));
            return sb.Length > 0 ? sb.ToString() : s.Trim();
        }

        static string DisplayOtp(string s)
        {
            string t = s.Trim();
            var sb = new StringBuilder();
            if (!StartsWith(t, "otpauth://")) return t;   // lot de comptes exporte (otpauth-migration) : illisible, montre tel quel
            string rest = t.Substring(10);
            int qm = rest.IndexOf('?');
            string path = Decode(qm >= 0 ? rest.Substring(0, qm) : rest);
            string q = qm >= 0 ? rest.Substring(qm + 1) : "";
            int sl = path.IndexOf('/');
            Add(sb, "Compte", sl >= 0 ? path.Substring(sl + 1) : path);
            Add(sb, "Emetteur", Query(q, "issuer"));
            Add(sb, "Cle secrete", Query(q, "secret"));
            return sb.Length > 0 ? sb.ToString() : t;
        }

        // ------------------------------------------------------------------ carte de visite (texte lu par l'OCR)
        static readonly Regex MailRx = new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant);
        static readonly Regex UrlRx = new Regex(@"(https?://\S+|www\.[A-Za-z0-9\-]+\.[A-Za-z.]+\S*)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex PhoneRx = new Regex(@"\+?\d[\d .\-/()]{6,}\d", RegexOptions.CultureInvariant);

        /// <summary>Classe les lignes lues sur une carte de visite : e-mails, telephones, sites, puis le reste (nom, societe, adresse...) tel quel.</summary>
        public static string CardText(List<string> lines)
        {
            var mails = new List<string>(); var phones = new List<string>(); var urls = new List<string>(); var rest = new List<string>();
            foreach (string raw in lines)
            {
                string l = raw;
                foreach (Match m in MailRx.Matches(l)) mails.Add(m.Value);
                l = MailRx.Replace(l, " ");
                foreach (Match m in UrlRx.Matches(l)) urls.Add(m.Value.TrimEnd('.', ',', ';'));
                l = UrlRx.Replace(l, " ");
                foreach (Match m in PhoneRx.Matches(l))
                {
                    int digits = 0;
                    foreach (char c in m.Value) if (char.IsDigit(c)) digits++;
                    if (digits >= 8) phones.Add(m.Value.Trim());
                }
                l = PhoneRx.Replace(l, " ");
                l = Regex.Replace(l, @"\s+", " ").Trim(' ', '-', ':', ';', ',', '|');
                if (l.Length >= 2) rest.Add(l);
            }
            var sb = new StringBuilder();
            foreach (string v in rest) { if (sb.Length > 0) sb.Append('\n'); sb.Append(v); }
            foreach (string v in phones) Add(sb, "Telephone", v);
            foreach (string v in mails) Add(sb, "E-mail", v);
            foreach (string v in urls) Add(sb, "Site", v);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ contacts vCard et MECARD
        static string DisplayContact(string s)
        {
            string t = s.Trim();
            var sb = new StringBuilder();
            if (StartsWith(t, "MECARD:") || StartsWith(t, "BIZCARD:"))
            {
                string name = "", org = "", job = "", adr = "", note = "";
                var tels = new List<string>(); var mails = new List<string>(); var urls = new List<string>();
                foreach (string f in SplitFields(t.Substring(t.IndexOf(':') + 1)))
                {
                    int c = f.IndexOf(':');
                    if (c <= 0) continue;
                    string key = f.Substring(0, c).ToUpperInvariant(), val = f.Substring(c + 1);
                    if (key == "N") { string[] nm = val.Split(','); name = nm.Length >= 2 ? (nm[1].Trim() + " " + nm[0].Trim()).Trim() : val; }
                    else if (key == "ORG" || key == "C") org = val;
                    else if (key == "TITLE" || key == "T") job = val;
                    else if (key == "TEL") tels.Add(val);
                    else if (key == "EMAIL") mails.Add(val);
                    else if (key == "URL") urls.Add(val);
                    else if (key == "ADR") adr = val.Replace(",", ", ").Trim().Trim(',').Trim();
                    else if (key == "NOTE") note = val;
                }
                Add(sb, "Nom", name); Add(sb, "Societe", org); Add(sb, "Fonction", job);
                foreach (string v in tels) Add(sb, "Telephone", v);
                foreach (string v in mails) Add(sb, "E-mail", v);
                foreach (string v in urls) Add(sb, "Site", v);
                Add(sb, "Adresse", adr); Add(sb, "Note", note);
                return sb.Length > 0 ? sb.ToString() : t;
            }
            // vCard : lignes « pliees » recollees, parametres (TYPE=...) ignores
            var lines = new List<string>();
            foreach (string raw in t.Replace("\r", "").Split('\n'))
            {
                if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0) lines[lines.Count - 1] += raw.Substring(1);
                else lines.Add(raw);
            }
            string fn = "", n = "", org2 = "", title = "", adr2 = "", note2 = "";
            var tel2 = new List<string>(); var mail2 = new List<string>(); var url2 = new List<string>();
            foreach (string l in lines)
            {
                int colon = l.IndexOf(':');
                if (colon <= 0) continue;
                string key = l.Substring(0, colon);
                int semi = key.IndexOf(';');
                if (semi >= 0) key = key.Substring(0, semi);
                key = key.ToUpperInvariant();
                int dot = key.IndexOf('.');
                if (dot >= 0) key = key.Substring(dot + 1);   // « item1.TEL »
                string val = l.Substring(colon + 1).Trim();
                if (key == "FN") fn = val;
                else if (key == "N") { string[] nm = val.Split(';'); n = nm.Length >= 2 ? (nm[1].Trim() + " " + nm[0].Trim()).Trim() : val.Replace(";", " ").Trim(); }
                else if (key == "ORG") org2 = val.Replace(";", ", ").Trim().Trim(',').Trim();
                else if (key == "TITLE") title = val;
                else if (key == "TEL") tel2.Add(val);
                else if (key == "EMAIL") mail2.Add(val);
                else if (key == "URL") url2.Add(val);
                else if (key == "ADR") adr2 = val.Replace(";", " ").Replace("  ", " ").Trim();
                else if (key == "NOTE") note2 = val.Replace("\\n", " ").Replace("\\N", " ");
            }
            Add(sb, "Nom", fn.Length > 0 ? fn : n); Add(sb, "Societe", org2); Add(sb, "Fonction", title);
            foreach (string v in tel2) Add(sb, "Telephone", v);
            foreach (string v in mail2) Add(sb, "E-mail", v);
            foreach (string v in url2) Add(sb, "Site", v);
            Add(sb, "Adresse", adr2); Add(sb, "Note", note2);
            return sb.Length > 0 ? sb.ToString() : t;
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
        string pathA, pathB, pathK;   // pathK : copie gardee jusqu'a la fin de la lecture ; si rien n'est lu elle est rangee dans le dossier Photos pour comprendre pourquoi
        public string KeptImage = "";
        Task<string> qrTask;
        Ar6dofOcr.Job job;
        bool wantOcr, wantQr = true;
        List<Ar6dofParsedEvent> evCache;
        /// <summary>Recoit la photo du scan (l'appelant la garde quelques secondes puis la detruit) ; sans cela elle est detruite tout de suite.</summary>
        public Action<Texture2D> shotCb;
        /// <summary>Carte de visite : le texte lu est classe en e-mail, telephone, site, reste.</summary>
        public bool card;
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
            if (card) return Ar6dofScanText.CardText(lines);
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
            phase = Phase.Idle; status = ""; error = ""; info = ""; qr = null; countSec = 0; lastCount = -1; KeptImage = "";
            lines.Clear(); evCache = null; kind = Ar6dofScanKind.Text;
            job = null; qrTask = null;
        }

        void DeleteTemp()
        {
            if (pathA != null) { try { File.Delete(pathA); } catch (Exception) { } pathA = null; }
            if (pathB != null) { try { File.Delete(pathB); } catch (Exception) { } pathB = null; }
        }

        void DropKept()
        {
            if (pathK != null) { try { File.Delete(pathK); } catch (Exception) { } pathK = null; }
        }

        /// <summary>Fin de lecture : si quelque chose a ete lu, la copie est supprimee ; sinon elle est rangee dans Images\AR6DoF\Photos\scan_echec.png (ecrasee a chaque echec).</summary>
        void KeepOrDrop(bool found)
        {
            string k = pathK;
            pathK = null;
            if (k == null) return;
            try
            {
                if (!found)
                {
                    string dir = Ar6dofPassthrough.PhotosFolder;
                    Directory.CreateDirectory(dir);
                    string dest = Path.Combine(dir, "scan_echec.png");
                    File.Copy(k, dest, true);
                    KeptImage = dest;
                }
            }
            catch (Exception) { KeptImage = ""; }
            try { File.Delete(k); } catch (Exception) { }
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
            DropKept();
            Clear();
        }

        /// <summary>Bouton Annuler.</summary>
        public void Cancel()
        {
            var pt = dock != null ? dock.passthrough : null;
            if (phase == Phase.Countdown && pt != null && pt.PhotoBusy) { pt.CancelPhoto(); return; }   // la photo repond « Annule » et remet tout a zero
            gen++;
            DeleteTemp();
            DropKept();
            Clear();
            if (changed != null) changed();
        }

        /// <summary>Un QR lu en direct sur l'apercu (sans photo) : le resultat s'affiche tout de suite, comme apres un scan.</summary>
        public void AcceptLive(string text)
        {
            var pt = dock != null ? dock.passthrough : null;
            if (pt != null && pt.PhotoBusy) pt.CancelPhoto();
            gen++;
            DeleteTemp();
            DropKept();
            Clear();
            Ar6dofNotifyService.Resolve("scan");
            qr = text;
            kind = Ar6dofScanText.Classify(text);
            phase = Phase.Done;
            if (changed != null) changed();
        }

        public void Start(bool ocr) { Start(true, ocr); }

        /// <summary>'qr' : chercher un code QR ; 'ocr' : lire le texte (apres le QR s'il est cherche, sinon seul).</summary>
        public void Start(bool qr, bool ocr)
        {
            var pt = dock != null ? dock.passthrough : null;
            gen++;
            DeleteTemp();
            DropKept();
            Clear();
            Ar6dofNotifyService.Resolve("scan");
            if (pt == null || pt.PhotoBusy) { Fail("Camera indisponible : le passthrough n'est pas pret"); return; }
            wantOcr = ocr;
            wantQr = qr || !ocr;
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
            if (tex != null)
            {
                if (my == gen && shotCb != null && string.IsNullOrEmpty(err)) shotCb(tex);   // la fenetre montre la photo quelques secondes
                else UnityEngine.Object.Destroy(tex);
            }
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
            try { pathK = Path.Combine(Path.GetDirectoryName(pathA), "keep_" + Path.GetFileName(pathA)); File.Copy(pathA, pathK, true); }
            catch (Exception) { pathK = null; }
            var pt = dock != null ? dock.passthrough : null;
            info = pt != null ? pt.LastStackInfo : "";
            phase = Phase.Reading;
            status = Ar6dofLoc.T(wantQr ? "Lecture du code QR..." : "Lecture du texte...");
            // l'OCR supprime l'image qu'on lui donne : on lui prepare une copie avant tout
            if (wantOcr)
            {
                try { pathB = Path.Combine(Path.GetDirectoryName(pathA), Path.GetFileNameWithoutExtension(pathA) + "_ocr.png"); File.Copy(pathA, pathB, true); }
                catch (Exception) { pathB = null; }
            }
            if (!wantQr)   // texte seul : pas de recherche de QR
            {
                StartOcr();
                if (changed != null) changed();
                return;
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
            DropKept();
            error = msg;
            phase = Phase.Done;
            Ar6dofNotifyService.Post(Ar6dofNoteKind.Error, Ar6dofLoc.T(appTitle), Ar6dofLoc.T(msg), appId, "scan");
            if (changed != null) changed();
        }

        void Finish()
        {
            phase = Phase.Done;
            KeepOrDrop(qr != null || lines.Count > 0);
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
        /// <summary>Mode Traduction : le texte lu est envoye tout seul au service de traduction (et seulement dans ce mode).</summary>
        protected bool autoTranslate;
        bool autoTried;

        protected Ar6dofScanWindow(Ar6dofDock d, string id, string name, string glyph, string desc, float width) : base(d, id, name, glyph, desc, width)
        {
            scan = new Ar6dofScanSession(d, id, name, OnScanChanged, OnScanStatus);
            tr = new Ar6dofTranslator(OnScanChanged);
        }

        protected bool IsOpen { get { return canvas != null && canvas.gameObject.activeSelf; } }

        protected virtual void OnScanChanged()
        {
            if (autoTranslate && !autoTried && scan.phase == Ar6dofScanSession.Phase.Done && scan.error.Length == 0)
            {
                string raw = scan.RawText();
                if (raw.Length > 0) { autoTried = true; tr.Start(raw, TargetLang); }
            }
            if (IsOpen) Rebuild();
        }
        void OnScanStatus() { if (IsOpen) Refresh(); }

        protected void TickScan() { scan.Tick(); tr.Tick(); }

        protected void AbortScan()
        {
            scan.Abort();
            tr.Clear();
            scanNote = "";
            autoTried = false;
        }

        protected void StartScan(bool ocr) { StartScan(true, ocr, false); }

        protected void StartScan(bool qr, bool ocr, bool card)
        {
            tr.Clear();
            scanNote = "";
            autoTried = false;
            scan.card = card;
            scan.Start(qr, ocr);
        }

        /// <summary>Bouton « Nouveau scan » du resultat : par defaut le scan complet (QR puis texte) ; l'appareil photo relance le mode choisi.</summary>
        protected virtual void Rescan() { StartScan(true); }

        public override void OnClosed()
        {
            AbortScan();
            base.OnClosed();
        }

        protected int TargetLang
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

        /// <summary>Choix de la langue + traduction (en ligne) du texte 'raw', avec son resultat. En mode Traduction la requete part toute seule apres la lecture ; sinon seulement au clic.</summary>
        void BuildTranslateBlock(string raw)
        {
            Section("Traduction");
            var tb = BtnRow(52f, 20, "", (Action)(() =>
                                    {
                                        TargetLang = (TargetLang + 1) % Ar6dofTranslator.Codes.Length;
                                        if (autoTranslate) { tr.Start(raw, TargetLang); Rebuild(); }   // mode Traduction : la nouvelle langue est appliquee tout de suite
                                        else Refresh();
                                    }),
                                     "", (Action)(() => { if (tr.Busy) tr.Cancel(); else tr.Start(raw, TargetLang); Rebuild(); }));
            var t0 = tb[0].GetComponentInChildren<Text>();
            var t1 = tb[1].GetComponentInChildren<Text>();
            refreshers.Add(() =>
            {
                t0.text = Ar6dofLoc.T("Vers") + " : " + Ar6dofTranslator.Names[TargetLang];
                t1.text = Ar6dofLoc.T(tr.Busy ? "Annuler" : (autoTranslate ? "Traduire de nouveau" : "Traduire (en ligne)"));
            });
            Para(autoTranslate ? "Mode Traduction : le texte lu est envoye au service en ligne MyMemory pour etre traduit." : "Traduire envoie ce texte au service en ligne MyMemory. Rien n'est envoye avant ton clic.", 40, 15);
            if (tr.Busy) Para("Traduction en cours...", 30, 18);
            else if (tr.error.Length > 0) Para(tr.error, 56, 18);
            else if (tr.result.Length > 0)
            {
                ParaFit(tr.result, 20, 10);
                if (tr.truncated) Para("Texte long : seuls les 2500 premiers caracteres ont ete traduits.", 30, 15);
                string trr = tr.result;
                var cb = BtnRow(52f, 21, "Copier la traduction", (Action)(() => CopyTo(trr, 1)));
                copyBtn2 = cb[0].GetComponentInChildren<Text>();
            }
        }

        /// <summary>Resultat d'un scan : QR decrit ou texte lu, Copier, Ouvrir le lien / la carte (seulement au clic), Traduire (en ligne), calendrier. 'translationFirst' : la traduction passe avant le texte lu.</summary>
        protected void BuildScanResult(bool allowTranslate, bool allowCalendar, bool translationFirst = false)
        {
            var s = scan;
            if (scanNote.Length > 0) Lbl(scanNote, 30, 19).MakeAccent();
            string body = s.BodyText();
            if (s.error.Length > 0) Para(s.error, 80, 19);
            else if (body.Length == 0)
            {
                Para("Rien de lisible. Rapproche-toi, ameliore la lumiere et ne bouge pas pendant la photo.", 76, 18);
                if (s.KeptImage.Length > 0) Para(Ar6dofLoc.F("Image du scan gardee : {0}", s.KeptImage), 60, 15);
            }
            else
            {
                string raw = s.RawText();
                bool textual = s.qr == null || s.Kind == Ar6dofScanKind.Text;
                bool tBlock = allowTranslate && textual;
                if (tBlock && translationFirst) BuildTranslateBlock(raw);
                string head = s.qr != null ? Ar6dofLoc.F("Code QR : {0}", Ar6dofLoc.T(Ar6dofScanText.KindName(s.Kind))) : Ar6dofLoc.T(s.card ? "Carte de visite" : "Texte lu");
                Lbl(head, 30, 20).MakeAccent();
                ParaFit(body, 18, translationFirst ? 6 : 9);
                // « Copier » : le texte lisible pour les formats decrits en clair (contact, e-mail...), le contenu brut pour le reste
                bool readable = s.qr != null && (s.Kind == Ar6dofScanKind.Contact || s.Kind == Ar6dofScanKind.Mail || s.Kind == Ar6dofScanKind.Phone || s.Kind == Ar6dofScanKind.Sms || s.Kind == Ar6dofScanKind.Geo || s.Kind == Ar6dofScanKind.Otp);
                string cp = readable || s.card ? body : raw;
                string open = s.qr != null ? Ar6dofScanText.OpenTarget(raw, s.Kind) : "";
                List<Button> bs;
                if (open.Length > 0)
                    bs = BtnRow(52f, 21, "Copier", (Action)(() => CopyTo(cp, 0)),
                                         s.Kind == Ar6dofScanKind.Geo ? "Ouvrir la carte" : "Ouvrir le lien", (Action)(() => { try { Application.OpenURL(open); } catch (Exception) { } }));
                else
                    bs = BtnRow(52f, 21, "Copier", (Action)(() => CopyTo(cp, 0)));
                copyBtn = bs[0].GetComponentInChildren<Text>();

                if (tBlock && !translationFirst) BuildTranslateBlock(raw);

                if (allowCalendar)
                {
                    var evs = s.Events(DateTime.Now);
                    int n = 0;
                    foreach (var e in evs) if (e.start > DateTime.Now) n++;
                    if (n > 0) Btn(Ar6dofLoc.F("Ajouter au calendrier ({0})", n), AddToCalendar, 52f, 21);
                }
            }
            Btn("Nouveau scan", Rescan, 52f, 22);
        }

        Text copyBtn, copyBtn2;

        protected override void OnRebuildStart() { copyBtn = null; copyBtn2 = null; }

        protected void CopyTo(string text, int which)
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
