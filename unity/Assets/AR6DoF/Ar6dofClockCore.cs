using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Formats d'heure et de date, communs au dock et à l'application Horloge (réglés dans Réglages &gt; Interface &gt; Dock).</summary>
    public static class Ar6dofClockFmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Modèles de date (.NET). Les exemples sont calculés avec la date du jour dans les réglages.</summary>
        public static readonly string[] DateFormats =
        {
            "dddd d MMMM yyyy",          // jeudi 5 novembre 2026
            "ddd d MMM",                 // jeu. 5 nov.
            "dd'/'MM'/'yyyy",            // 05/11/2026
            "MM'/'dd'/'yyyy",            // 11/05/2026
            "yyyy'-'MM'-'dd",            // 2026-11-05
            "d MMMM",                    // 5 novembre
            "dd'.'MM'.'yyyy",            // 05.11.2026
            "MMM d',' yyyy",             // nov. 5, 2026
            "dddd",                      // jeudi
            "dd'/'MM",                   // 05/11
        };

        public static bool CultureIs24h()
        {
            try { return CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.IndexOf('H') >= 0; }
            catch (Exception) { return true; }
        }

        /// <summary>Culture de la langue choisie dans AR6DoF (jours et mois dans cette langue).</summary>
        public static CultureInfo Culture()
        {
            try { return CultureInfo.GetCultureInfo(Ar6dofLoc.Code); }
            catch (Exception) { return Inv; }
        }

        static string Cap(string s, CultureInfo ci)
        {
            return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], ci) + s.Substring(1);
        }

        public static string Date(DateTime t, int fmt)
        {
            var ci = Culture();
            if (fmt < 0 || fmt >= DateFormats.Length) fmt = 0;
            return Cap(t.ToString(DateFormats[fmt], ci), ci);
        }

        /// <summary>« 14:05 » / « 14:05:09 » ou « 2:05 PM » / « 2:05:09 PM ».</summary>
        public static string Time(DateTime t, bool h24, bool seconds)
        {
            string s = seconds ? ":" + t.Second.ToString("00", Inv) : "";
            if (h24) return t.Hour.ToString("00", Inv) + ":" + t.Minute.ToString("00", Inv) + s;
            int h = t.Hour % 12;
            if (h == 0) h = 12;
            return h.ToString(Inv) + ":" + t.Minute.ToString("00", Inv) + s + " " + (t.Hour < 12 ? "AM" : "PM");
        }

        /// <summary>Heure et minute d'une alarme (même format que l'heure du jour, sans secondes).</summary>
        public static string HourMinute(int hour, int minute, bool h24)
        {
            return Time(new DateTime(2000, 1, 1, Mathf.Clamp(hour, 0, 23), Mathf.Clamp(minute, 0, 59), 0), h24, false);
        }

        /// <summary>Durée du chronomètre : « 01:23.45 » ou « 1:02:03.45 ».</summary>
        public static string Stopwatch(double sec, bool hundredths = true)
        {
            if (sec < 0) sec = 0;
            int cs = (int)(sec * 100.0) % 100;
            int total = (int)sec;
            int h = total / 3600, m = (total / 60) % 60, s = total % 60;
            string tail = hundredths ? "." + cs.ToString("00", Inv) : "";
            return h > 0 ? h.ToString(Inv) + ":" + m.ToString("00", Inv) + ":" + s.ToString("00", Inv) + tail : m.ToString("00", Inv) + ":" + s.ToString("00", Inv) + tail;
        }

        /// <summary>Temps restant d'un minuteur, arrondi à la seconde supérieure : « 04:59 » ou « 1:00:00 ».</summary>
        public static string Countdown(double sec)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt((float)sec));
            int h = total / 3600, m = (total / 60) % 60, s = total % 60;
            return h > 0 ? h.ToString(Inv) + ":" + m.ToString("00", Inv) + ":" + s.ToString("00", Inv) : m.ToString("00", Inv) + ":" + s.ToString("00", Inv);
        }

        /// <summary>Durée en mots courts : « 3 h 20 min », « 12 min », « 1 j 3 h ».</summary>
        public static string Span(TimeSpan d)
        {
            int mins = Mathf.Max(0, (int)Math.Ceiling(d.TotalMinutes));
            if (mins >= 1440) return Ar6dofLoc.F("{0} j {1} h", mins / 1440, (mins % 1440) / 60);
            if (mins >= 60) return Ar6dofLoc.F("{0} h {1} min", mins / 60, mins % 60);
            return Ar6dofLoc.F("{0} min", mins);
        }

        /// <summary>Initiales des jours, lundi d'abord.</summary>
        public static string DayShort(int mondayFirstIndex)
        {
            var ci = Culture();
            string n = ci.DateTimeFormat.AbbreviatedDayNames[(mondayFirstIndex + 1) % 7];
            return Cap(n, ci);
        }
    }

    // ------------------------------------------------------------------ données enregistrées (clock.json)
    [Serializable]
    public class Ar6dofAlarm
    {
        public int id;
        public string name = "";
        public int hour = 7, minute = 0;
        public int days = 0;          // bits 0..6 = lundi..dimanche ; 0 = une seule fois
        public bool enabled = true;
        public int snooze = 5;        // minutes
        public int tone = 0;          // 0 bip, 1 cloche, 2 carillon
        [NonSerialized] public long firedMinute = -1;

        public bool RepeatsOn(int mondayFirstIndex) { return (days & (1 << mondayFirstIndex)) != 0; }

        public string DisplayName { get { return string.IsNullOrEmpty(name) ? Ar6dofLoc.T("Alarme") : name; } }

        /// <summary>Prochain déclenchement strictement après 'from' ; null si l'alarme est coupée.</summary>
        public DateTime? Next(DateTime from)
        {
            if (!enabled) return null;
            for (int d = 0; d <= 7; d++)
            {
                DateTime c = from.Date.AddDays(d).AddHours(hour).AddMinutes(minute);
                if (c <= from) continue;
                int idx = ((int)c.DayOfWeek + 6) % 7;
                if (days == 0 || RepeatsOn(idx)) return c;
            }
            return null;
        }
    }

    [Serializable]
    public class Ar6dofClockData
    {
        public List<Ar6dofAlarm> alarms = new List<Ar6dofAlarm>();
        public int nextId = 1;
        public int faceStyle = 0;         // visuel de l'horloge
        public int timerStyle = 0;        // visuel du minuteur
        public int stopwatchStyle = 0;    // visuel du chronomètre
        public int timerSeconds = 300;    // durée choisie du minuteur
        public string timerName = "";
        public int timerTone = 1;
        public float volume = 0.8f;
    }

    /// <summary>Une sonnerie à annoncer (alarme ou minuteur).</summary>
    public class Ar6dofRingEvent
    {
        public bool isTimer;
        public bool isEvent;      // rappel d'un evenement du calendrier : pas de « Repeter »
        public int alarmId;
        public string title = "", text = "";
        public int tone;
        public int snooze;
    }

    /// <summary>
    /// Alarmes, minuteur et chronomètre. Vit sur l'objet du dock mais ne fait RIEN tant qu'aucune alarme n'est active, qu'aucun minuteur ne tourne
    /// et qu'aucune sonnerie n'est en cours (une seule comparaison par image). Les alarmes sont enregistrées dans %APPDATA%\AR6DoF\clock.json ;
    /// le minuteur et le chronomètre ne survivent pas à la fermeture de l'application. Une sonnerie joue un son (généré par code, aucun fichier)
    /// et affiche une petite fenêtre devant toi avec « Arrêter » (et « Répéter » pour une alarme).
    /// </summary>
    public class Ar6dofClockService : MonoBehaviour
    {
        public static Ar6dofClockService Instance { get; private set; }
        public Ar6dofClockData data = new Ar6dofClockData();

        public enum TimerPhase { Idle, Running, Paused, Done }

        // chronomètre
        bool swRunning;
        double swAccum, swStartT;
        public readonly List<double> laps = new List<double>();   // temps total à chaque tour
        // minuteur
        TimerPhase timerPhase = TimerPhase.Idle;
        double timerEndT, timerLeftPaused;
        // sonnerie
        Ar6dofRingEvent current;
        readonly Queue<Ar6dofRingEvent> pending = new Queue<Ar6dofRingEvent>();
        readonly List<KeyValuePair<Ar6dofAlarm, DateTime>> snoozes = new List<KeyValuePair<Ar6dofAlarm, DateTime>>();
        float ringStart;
        AudioSource src;
        static AudioClip[] tones = new AudioClip[3];
        Ar6dofRingPopup popup;
        // état
        bool anyAlarm, dirty;
        float nextCheck, saveAt;

        public static readonly string[] ToneNames = { "Bip", "Cloche", "Carillon" };

        static string FilePath { get { return Path.Combine(Ar6dofSettings.FolderPath, "clock.json"); } }
        static double Now() { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; }

        void Awake()
        {
            Instance = this;
            Load();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() { if (dirty) Save(); }

        // ------------------------------------------------------------------ fichier
        void Load()
        {
            try
            {
                if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath, Encoding.UTF8), data);
            }
            catch (Exception) { data = new Ar6dofClockData(); }
            if (data.alarms == null) data.alarms = new List<Ar6dofAlarm>();
            foreach (var a in data.alarms) { a.hour = Mathf.Clamp(a.hour, 0, 23); a.minute = Mathf.Clamp(a.minute, 0, 59); a.tone = Mathf.Clamp(a.tone, 0, 2); a.snooze = Mathf.Clamp(a.snooze, 1, 30); a.firedMinute = -1; if (a.id >= data.nextId) data.nextId = a.id + 1; }
            data.timerSeconds = Mathf.Clamp(data.timerSeconds, 1, 99 * 3600 + 59 * 60 + 59);
            data.volume = Mathf.Clamp01(data.volume);
            data.faceStyle = Mathf.Clamp(data.faceStyle, 0, 5);
            data.timerStyle = Mathf.Clamp(data.timerStyle, 0, 4);
            data.stopwatchStyle = Mathf.Clamp(data.stopwatchStyle, 0, 2);
            data.timerTone = Mathf.Clamp(data.timerTone, 0, 2);
            anyAlarm = AnyAlarmOn();
        }

        public void MarkDirty()
        {
            dirty = true;
            saveAt = Time.unscaledTime + 0.8f;
            anyAlarm = AnyAlarmOn();
        }

        void Save()
        {
            dirty = false;
            try
            {
                Directory.CreateDirectory(Ar6dofSettings.FolderPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true), Encoding.UTF8);
            }
            catch (Exception) { }
        }

        bool AnyAlarmOn()
        {
            foreach (var a in data.alarms) if (a.enabled) return true;
            return false;
        }

        // ------------------------------------------------------------------ alarmes
        public Ar6dofAlarm AddAlarm()
        {
            var t = DateTime.Now.AddHours(1);
            var a = new Ar6dofAlarm { id = data.nextId++, hour = t.Hour, minute = 0, enabled = true };
            data.alarms.Add(a);
            MarkDirty();
            return a;
        }

        public void DeleteAlarm(Ar6dofAlarm a)
        {
            data.alarms.Remove(a);
            for (int i = snoozes.Count - 1; i >= 0; i--) if (snoozes[i].Key == a) snoozes.RemoveAt(i);
            MarkDirty();
        }

        // ------------------------------------------------------------------ chronomètre
        public bool SwRunning { get { return swRunning; } }
        public double SwElapsed { get { return swRunning ? swAccum + (Now() - swStartT) : swAccum; } }

        public void SwToggle()
        {
            if (swRunning) { swAccum += Now() - swStartT; swRunning = false; }
            else { swStartT = Now(); swRunning = true; }
        }

        public void SwLap() { if (swRunning || swAccum > 0) laps.Add(SwElapsed); }

        public void SwReset() { swRunning = false; swAccum = 0; laps.Clear(); }

        // ------------------------------------------------------------------ minuteur
        public TimerPhase Phase { get { return timerPhase; } }

        public double TimerLeft
        {
            get
            {
                switch (timerPhase)
                {
                    case TimerPhase.Running: return Math.Max(0, timerEndT - Now());
                    case TimerPhase.Paused: return timerLeftPaused;
                    case TimerPhase.Done: return 0;
                    default: return data.timerSeconds;
                }
            }
        }

        public void TimerStartPause()
        {
            switch (timerPhase)
            {
                case TimerPhase.Running:
                    timerLeftPaused = Math.Max(0, timerEndT - Now());
                    timerPhase = TimerPhase.Paused;
                    break;
                case TimerPhase.Paused:
                    timerEndT = Now() + timerLeftPaused;
                    timerPhase = TimerPhase.Running;
                    break;
                default:
                    timerEndT = Now() + data.timerSeconds;
                    timerPhase = TimerPhase.Running;
                    break;
            }
        }

        public void TimerReset() { timerPhase = TimerPhase.Idle; }

        /// <summary>Change la durée choisie (remet le minuteur à zéro s'il ne tournait pas).</summary>
        public void SetTimerSeconds(int s)
        {
            data.timerSeconds = Mathf.Clamp(s, 1, 99 * 3600 + 59 * 60 + 59);
            if (timerPhase != TimerPhase.Running && timerPhase != TimerPhase.Paused) timerPhase = TimerPhase.Idle;
            MarkDirty();
        }

        // ------------------------------------------------------------------ boucle
        void Update()
        {
            float now = Time.unscaledTime;
            if (dirty && now >= saveAt) Save();
            if (current != null)
            {
                if (now - ringStart > 90f) StopRing();   // la sonnerie s'arrête seule au bout d'1 min 30
            }
            if (!anyAlarm && timerPhase != TimerPhase.Running && snoozes.Count == 0) return;
            if (now < nextCheck) return;
            nextCheck = now + 0.25f;

            if (timerPhase == TimerPhase.Running && Now() >= timerEndT)
            {
                timerPhase = TimerPhase.Done;
                Ring(new Ar6dofRingEvent { isTimer = true, title = string.IsNullOrEmpty(data.timerName) ? Ar6dofLoc.T("Minuteur") : data.timerName, text = Ar6dofLoc.T("Temps ecoule"), tone = data.timerTone });
            }

            DateTime t = DateTime.Now;
            long minute = t.Ticks / TimeSpan.TicksPerMinute;
            if (anyAlarm)
            {
                int idx = ((int)t.DayOfWeek + 6) % 7;
                foreach (var a in data.alarms)
                {
                    if (!a.enabled || a.hour != t.Hour || a.minute != t.Minute || a.firedMinute == minute) continue;
                    if (a.days != 0 && !a.RepeatsOn(idx)) continue;
                    a.firedMinute = minute;
                    if (a.days == 0) { a.enabled = false; MarkDirty(); }   // alarme unique : elle se coupe après avoir sonné
                    Ring(MakeAlarmEvent(a));
                }
            }
            for (int i = snoozes.Count - 1; i >= 0; i--)
            {
                if (t < snoozes[i].Value) continue;
                var a = snoozes[i].Key;
                snoozes.RemoveAt(i);
                Ring(MakeAlarmEvent(a));
            }
        }

        static Ar6dofRingEvent MakeAlarmEvent(Ar6dofAlarm a)
        {
            return new Ar6dofRingEvent
            {
                alarmId = a.id,
                title = a.DisplayName,
                text = Ar6dofClockFmt.HourMinute(a.hour, a.minute, Ar6dofSettings.Current.clock24h),
                tone = a.tone,
                snooze = a.snooze,
            };
        }

        // ------------------------------------------------------------------ sonnerie
        public bool Ringing { get { return current != null; } }

        /// <summary>Fait sonner un rappel venu d'ailleurs (calendrier) : meme son, meme fenetre, et une ligne dans les notifications.</summary>
        public void RingNow(Ar6dofRingEvent e) { Ring(e); }

        void Ring(Ar6dofRingEvent e)
        {
            // trace dans les notifications, meme si la sonnerie est arretee tout de suite ou mise en attente
            Ar6dofNotifyService.Post(e.isEvent ? Ar6dofNoteKind.Event : Ar6dofNoteKind.Alarm, e.title, e.isTimer ? Ar6dofLoc.T(e.text) : e.text, e.isEvent ? "calendar" : "clock");
            if (current != null) { pending.Enqueue(e); return; }
            current = e;
            ringStart = Time.unscaledTime;
            PlayLoop(e.tone);
            if (popup == null) popup = new Ar6dofRingPopup(this);
            popup.Show(e);
        }

        public void StopRing()
        {
            if (src != null) src.Stop();
            current = null;
            if (popup != null) popup.Hide();
            if (pending.Count > 0) Ring(pending.Dequeue());
        }

        /// <summary>« Répéter » : l'alarme sonnera de nouveau dans quelques minutes.</summary>
        public void SnoozeRing()
        {
            var e = current;
            if (e != null && !e.isTimer)
            {
                foreach (var a in data.alarms)
                    if (a.id == e.alarmId) { snoozes.Add(new KeyValuePair<Ar6dofAlarm, DateTime>(a, DateTime.Now.AddMinutes(Math.Max(1, e.snooze)))); break; }
            }
            StopRing();
        }

        public void TestTone(int tone)
        {
            EnsureSource();
            src.Stop();
            src.loop = false;
            src.clip = Tone(tone);
            src.volume = data.volume;
            src.Play();
        }

        void EnsureSource()
        {
            if (src != null) return;
            // Unity ne joue rien sans « oreille » dans la scène : on en ajoute une seulement s'il n'y en a aucune
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
        }

        void PlayLoop(int tone)
        {
            EnsureSource();
            src.Stop();
            src.clip = Tone(tone);
            src.loop = true;
            src.volume = data.volume;
            src.Play();
        }

        static AudioClip Tone(int tone)
        {
            tone = Mathf.Clamp(tone, 0, 2);
            if (tones[tone] != null) return tones[tone];
            const int sr = 44100;
            float len = tone == 1 ? 2.4f : 1.6f;
            int n = (int)(sr * len);
            var d = new float[n];
            switch (tone)
            {
                case 0:   // trois bips
                {
                    float[] starts = { 0f, 0.22f, 0.44f };
                    for (int k = 0; k < starts.Length; k++)
                    {
                        int i0 = (int)(starts[k] * sr), nn = (int)(0.12f * sr);
                        for (int i = 0; i < nn && i0 + i < n; i++)
                        {
                            float env = Mathf.Sin(Mathf.PI * i / nn);
                            d[i0 + i] += Mathf.Sin(2f * Mathf.PI * 1046f * i / sr) * env * 0.6f;
                        }
                    }
                    break;
                }
                case 1:   // cloche : plusieurs harmoniques qui s'éteignent
                {
                    float[] f = { 523f, 1046f, 1569f, 2200f }, dec = { 1.6f, 2.4f, 3.4f, 5f }, amp = { 0.5f, 0.28f, 0.16f, 0.09f };
                    for (int i = 0; i < n; i++)
                    {
                        float t = (float)i / sr, v = 0f;
                        for (int k = 0; k < f.Length; k++) v += Mathf.Sin(2f * Mathf.PI * f[k] * t) * Mathf.Exp(-t * dec[k]) * amp[k];
                        d[i] = v;
                    }
                    break;
                }
                default:  // carillon : do mi sol do
                {
                    float[] f = { 523.25f, 659.25f, 783.99f, 1046.5f };
                    for (int k = 0; k < f.Length; k++)
                    {
                        int i0 = (int)(k * 0.25f * sr);
                        for (int i = 0; i0 + i < n && i < sr; i++)
                        {
                            float t = (float)i / sr;
                            d[i0 + i] += (Mathf.Sin(2f * Mathf.PI * f[k] * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * f[k] * 2f * t)) * Mathf.Exp(-t * 4f) * 0.4f;
                        }
                    }
                    break;
                }
            }
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i], -0.95f, 0.95f);
            var clip = AudioClip.Create("Ar6dofTone" + tone, n, 1, sr, false);
            clip.SetData(d, 0);
            tones[tone] = clip;
            return clip;
        }
    }

    /// <summary>Petite fenêtre qui s'affiche devant toi quand une alarme ou un minuteur sonne. Créée une seule fois, ensuite cachée / montrée.</summary>
    public class Ar6dofRingPopup
    {
        readonly Ar6dofClockService svc;
        Canvas canvas;
        Text title, body;
        Button snoozeBtn;
        Text snoozeLbl;
        bool placedOnce;

        public Ar6dofRingPopup(Ar6dofClockService s) { svc = s; }

        void Build()
        {
            canvas = Ar6dofVrUi.NewCanvas("Sonnerie", new Vector2(600f, 330f));
            var content = Ar6dofVrUi.NewContent(canvas);
            title = Ar6dofVrUi.Label(content, "", new Vector2(0f, -58f), new Vector2(560f, 60f), 42, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            body = Ar6dofVrUi.Label(content, "", new Vector2(0f, -128f), new Vector2(560f, 50f), 34, TextAnchor.MiddleCenter);
            Text t;
            Ar6dofVrUi.MakeButton(content, "Arreter", new Vector2(-150f, -250f), new Vector2(260f, 84f), () => svc.StopRing(), out t, 32);
            snoozeBtn = Ar6dofVrUi.MakeButton(content, "Repeter", new Vector2(150f, -250f), new Vector2(260f, 84f), () => svc.SnoozeRing(), out snoozeLbl, 32);
            var d = Ar6dofDock.Instance;
            if (d != null && d.pointer != null) d.pointer.AddCanvas(canvas);
            canvas.gameObject.SetActive(false);
        }

        public void Show(Ar6dofRingEvent e)
        {
            if (canvas == null) Build();
            title.text = e.title;
            body.text = e.isTimer ? Ar6dofLoc.T(e.text) : e.text;
            bool canSnooze = !e.isTimer && !e.isEvent;
            snoozeBtn.gameObject.SetActive(canSnooze);
            if (canSnooze) snoozeLbl.text = Ar6dofLoc.F("Repeter ({0} min)", e.snooze);
            var d = Ar6dofDock.Instance;
            Transform h = d != null ? d.HeadTransform : null;
            if (h != null)
            {
                Vector3 f = h.forward; f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                Vector3 pos = h.position + f * 0.85f;
                pos.y = h.position.y - 0.05f;
                canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(f));
                placedOnce = true;
            }
            else if (!placedOnce)
            {
                canvas.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, 1f), Quaternion.identity);
            }
            canvas.gameObject.SetActive(true);
        }

        public void Hide() { if (canvas != null) canvas.gameObject.SetActive(false); }
    }
}
