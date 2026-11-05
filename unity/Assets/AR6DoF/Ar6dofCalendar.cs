using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Un evenement du calendrier (enregistre dans %APPDATA%\AR6DoF\calendar.json).</summary>
    [Serializable]
    public class Ar6dofCalEvent
    {
        public int id;
        public string title = "";
        public string place = "";
        public long startTicks;      // heure locale de debut
        public int durMin;           // 0 = duree inconnue
        public bool remind = true;   // rappel avant le debut
        public bool reminded;        // le rappel a deja eu lieu (ou n'a plus lieu d'etre)

        public DateTime Start { get { return new DateTime(startTicks); } set { startTicks = value.Ticks; } }
        public string DisplayTitle { get { return string.IsNullOrEmpty(title) ? Ar6dofLoc.T("Evenement") : title; } }

        public Ar6dofCalEvent Clone() { return (Ar6dofCalEvent)MemberwiseClone(); }
    }

    [Serializable]
    public class Ar6dofCalendarData
    {
        public List<Ar6dofCalEvent> events = new List<Ar6dofCalEvent>();
        public int nextId = 1;
        public int leadMin = 10;     // le rappel sonne ce nombre de minutes avant le debut
    }

    /// <summary>
    /// Calendrier et rappels. Vit sur l'objet du dock mais ne fait RIEN tant qu'aucun rappel n'est attendu : l'instant du prochain rappel est calcule une fois
    /// (a chaque changement), puis on le compare a l'heure une fois par seconde. Le rappel passe par la sonnerie de l'horloge (son et petite fenetre devant toi,
    /// sans « Repeter ») et laisse une ligne dans les notifications. Un rappel manque de plus de 30 minutes (application fermee) est ignore.
    /// </summary>
    public class Ar6dofCalendarService : MonoBehaviour
    {
        public static Ar6dofCalendarService Instance { get; private set; }
        public Ar6dofCalendarData data = new Ar6dofCalendarData();
        public const int MaxEvents = 300;
        public static readonly int[] LeadChoices = { 5, 10, 15, 30 };

        bool dirty, pending;
        float saveAt, nextCheck;
        DateTime nextDue = DateTime.MaxValue;

        static string FilePath { get { return Path.Combine(Ar6dofSettings.FolderPath, "calendar.json"); } }

        void Awake()
        {
            Instance = this;
            Load();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnApplicationQuit() { if (dirty) Save(); }

        // ------------------------------------------------------------------ fichier
        void Load()
        {
            try
            {
                if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath, Encoding.UTF8), data);
            }
            catch (Exception) { data = new Ar6dofCalendarData(); }
            if (data.events == null) data.events = new List<Ar6dofCalEvent>();
            data.leadMin = Mathf.Clamp(data.leadMin, 1, 120);
            // on garde les evenements des deux derniers jours : le reste n'est que du bruit
            DateTime limit = DateTime.Now.AddDays(-2);
            data.events.RemoveAll(e => e == null || e.Start < limit);
            foreach (var e in data.events) if (e.id >= data.nextId) data.nextId = e.id + 1;
            Recompute();
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

        /// <summary>A appeler apres tout changement des evenements ou du delai : enregistre bientot et recalcule le prochain rappel.</summary>
        public void MarkDirty()
        {
            dirty = true;
            saveAt = Time.unscaledTime + 0.8f;
            Recompute();
        }

        // ------------------------------------------------------------------ evenements
        public Ar6dofCalEvent Add(Ar6dofCalEvent e)
        {
            e.id = data.nextId++;
            data.events.Add(e);
            if (data.events.Count > MaxEvents)
            {
                data.events.Sort((a, b) => a.startTicks.CompareTo(b.startTicks));
                data.events.RemoveRange(0, data.events.Count - MaxEvents);   // les plus anciens partent d'abord
            }
            MarkDirty();
            return e;
        }

        /// <summary>Ajoute des evenements trouves par un scan, sans doublon (meme debut et meme titre). Renvoie le nombre ajoute.</summary>
        public int AddRange(IEnumerable<Ar6dofCalEvent> list)
        {
            int n = 0;
            foreach (var e in list)
            {
                bool dup = false;
                string f = Ar6dofCalParser.Fold(e.title);
                foreach (var x in data.events)
                    if (x.startTicks == e.startTicks && Ar6dofCalParser.Fold(x.title) == f) { dup = true; break; }
                if (dup) continue;
                e.id = data.nextId++;
                e.reminded = false;
                data.events.Add(e);
                n++;
            }
            if (n > 0) MarkDirty();
            return n;
        }

        public void Delete(Ar6dofCalEvent e)
        {
            if (data.events.Remove(e)) MarkDirty();
        }

        public void Clear()
        {
            data.events.Clear();
            MarkDirty();
        }

        /// <summary>Les evenements pas encore finis (ou commences depuis moins d'une heure), du plus proche au plus lointain.</summary>
        public List<Ar6dofCalEvent> Upcoming()
        {
            var l = new List<Ar6dofCalEvent>();
            DateTime from = DateTime.Now.AddHours(-1);
            foreach (var e in data.events)
            {
                DateTime end = e.Start.AddMinutes(Math.Max(60, e.durMin));
                if (e.Start >= from || end >= DateTime.Now) l.Add(e);
            }
            l.Sort((a, b) => a.startTicks.CompareTo(b.startTicks));
            return l;
        }

        // ------------------------------------------------------------------ rappels
        void Recompute()
        {
            nextDue = DateTime.MaxValue;
            pending = false;
            foreach (var e in data.events)
            {
                if (!e.remind || e.reminded) continue;
                DateTime due = e.Start.AddMinutes(-data.leadMin);
                if (due < nextDue) nextDue = due;
                pending = true;
            }
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (dirty && now >= saveAt) Save();
            if (!pending) return;                  // aucun rappel attendu : rien d'autre a faire
            if (now < nextCheck) return;
            nextCheck = now + 1f;
            DateTime t = DateTime.Now;
            if (t < nextDue) return;
            Fire(t);
        }

        void Fire(DateTime t)
        {
            foreach (var e in data.events)
            {
                if (!e.remind || e.reminded) continue;
                DateTime start = e.Start;
                if (t < start.AddMinutes(-data.leadMin)) continue;
                e.reminded = true;
                if (t > start.AddMinutes(30)) continue;   // trop tard pour prevenir : on l'ignore sans bruit
                Remind(e, start, t);
            }
            MarkDirty();   // enregistre « deja rappele » et cherche le rappel suivant
        }

        void Remind(Ar6dofCalEvent e, DateTime start, DateTime now)
        {
            int mins = Mathf.CeilToInt((float)(start - now).TotalMinutes);
            string when = mins > 0 ? Ar6dofLoc.F("Dans {0} min", mins) : Ar6dofLoc.T("Maintenant");
            string text = when + " - " + Ar6dofClockFmt.Time(start, Ar6dofSettings.Current.clock24h, false) + (string.IsNullOrEmpty(e.place) ? "" : " - " + e.place);
            string name = e.DisplayTitle;
            var clock = Ar6dofClockService.Instance;
            if (clock != null) clock.RingNow(new Ar6dofRingEvent { isEvent = true, title = name, text = text, tone = 1 });
            else Ar6dofNotifyService.Post(Ar6dofNoteKind.Event, name, text, "calendar");
        }
    }
}
