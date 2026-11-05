using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    public enum Ar6dofNoteKind { Alarm, Event, Error, Info }

    /// <summary>Une notification : une alarme ou un minuteur qui a sonne, un rappel d'evenement, un probleme, une information.</summary>
    public class Ar6dofNote
    {
        public int id;
        public Ar6dofNoteKind kind;
        public string title = "", text = "";
        public string key = "";    // meme cle = meme probleme : une seule ligne, mise a jour (et retiree quand le probleme disparait)
        public string app = "";    // application a ouvrir en touchant la ligne (id du catalogue : "clock", "calendar", "mixer", "audio"...)
        public DateTime time;
        public bool read;
    }

    /// <summary>
    /// Centre de notifications : une liste (40 au plus, la plus recente d'abord) alimentee par l'horloge (alarmes, minuteur), le calendrier (rappels)
    /// et une surveillance legere des problemes (moteur audio, musique, liaison avec le casque, suivi). Vit sur l'objet du dock.
    /// Cout : une lecture de quelques variables par SECONDE ; rien d'autre. Le panneau (Ar6dofNotifyApp) et la pastille du dock ne se
    /// mettent a jour que quand 'Version' change. Les notifications ne sont pas gardees apres la fermeture de l'application.
    /// </summary>
    public class Ar6dofNotifyService : MonoBehaviour
    {
        public static Ar6dofNotifyService Instance { get; private set; }
        public readonly List<Ar6dofNote> notes = new List<Ar6dofNote>();
        public int Version { get; private set; }
        public int Unread { get; private set; }
        public int UnreadErrors { get; private set; }
        const int Max = 40;
        int nextId = 1;
        float nextPoll;
        // etat de la surveillance
        bool audioFailed, linkLost, trackBad;
        string musicErr = "";
        float trackBadSince = -1f;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Ajoute une notification (sans effet si le centre n'existe pas encore). 'title' et 'text' sont traduits ici.</summary>
        public static void Post(Ar6dofNoteKind kind, string title, string text, string app = "", string key = "")
        {
            if (Instance != null) Instance.Add(kind, title, text, app, key);
        }

        /// <summary>Retire le probleme qui portait cette cle (il est resolu).</summary>
        public static void Resolve(string key)
        {
            if (Instance != null) Instance.RemoveKey(key);
        }

        public void Add(Ar6dofNoteKind kind, string title, string text, string app, string key)
        {
            if (!string.IsNullOrEmpty(key)) RemoveKeyQuiet(key);
            var n = new Ar6dofNote
            {
                id = nextId++, kind = kind, title = Ar6dofLoc.T(title ?? ""), text = text ?? "", app = app ?? "", key = key ?? "", time = DateTime.Now,
            };
            notes.Insert(0, n);
            if (notes.Count > Max) notes.RemoveRange(Max, notes.Count - Max);
            Changed();
        }

        public void Remove(Ar6dofNote n)
        {
            if (notes.Remove(n)) Changed();
        }

        public void RemoveKey(string key)
        {
            if (RemoveKeyQuiet(key)) Changed();
        }

        bool RemoveKeyQuiet(string key)
        {
            bool any = false;
            for (int i = notes.Count - 1; i >= 0; i--)
                if (notes[i].key == key) { notes.RemoveAt(i); any = true; }
            return any;
        }

        public void Clear()
        {
            if (notes.Count == 0) return;
            notes.Clear();
            Changed();
        }

        public void MarkRead(Ar6dofNote n)
        {
            if (n == null || n.read) return;
            n.read = true;
            Changed();
        }

        public void MarkAllRead()
        {
            bool any = false;
            foreach (var n in notes) if (!n.read) { n.read = true; any = true; }
            if (any) Changed();
        }

        void Changed()
        {
            Version++;
            int u = 0, e = 0;
            foreach (var n in notes)
                if (!n.read) { u++; if (n.kind == Ar6dofNoteKind.Error) e++; }
            Unread = u;
            UnreadErrors = e;
        }

        // ------------------------------------------------------------------ surveillance
        void Update()
        {
            float now = Time.unscaledTime;
            if (now < nextPoll) return;
            nextPoll = now + 1f;
            Watch(now);
        }

        void Watch(float now)
        {
            var a = Ar6dofAudio.Instance;
            if (a != null)
            {
                bool failed = a.State == Ar6dofAudio.EngineState.Failed;
                if (failed != audioFailed)
                {
                    audioFailed = failed;
                    if (failed) Add(Ar6dofNoteKind.Error, "Moteur audio", string.IsNullOrEmpty(a.Message) ? Ar6dofLoc.T("Erreur") : a.Message, "audio", "audio");
                    else RemoveKey("audio");
                }
                string me = a.musicError ?? "";
                if (me != musicErr)
                {
                    musicErr = me;
                    if (me.Length > 0) Add(Ar6dofNoteKind.Error, "Musique", me, "mixer", "music");
                    else RemoveKey("music");
                }
            }
            var d = Ar6dofDock.Instance;
            var rig = d != null ? d.rig : null;
            if (rig != null && rig.HasPose)
            {
                bool lost = rig.ConnectionLost;
                if (lost != linkLost)
                {
                    linkLost = lost;
                    if (lost) Add(Ar6dofNoteKind.Error, "Connexion perdue", Ar6dofLoc.T("Plus de donnees du casque (ar6dof_probe lance ?)"), "", "link");
                    else RemoveKey("link");
                }
                bool bad = !lost && !rig.TrackingOk;
                if (!bad) trackBadSince = -1f;
                else if (trackBadSince < 0f) trackBadSince = now;
                bool badLong = trackBadSince >= 0f && now - trackBadSince > 3f;   // un trou de suivi de moins de 3 s n'est pas un probleme a signaler
                if (badLong != trackBad)
                {
                    trackBad = badLong;
                    if (badLong) Add(Ar6dofNoteKind.Error, "Suivi perdu", Ar6dofLoc.T("Le suivi de la tete ne fonctionne plus"), "", "track");
                    else RemoveKey("track");
                }
            }
        }
    }

    /// <summary>
    /// Panneau des notifications, ouvert par la cloche du dock (juste a cote d'Apps) et pose au-dessus du dock : musique en cours avec ses boutons
    /// (precedent, lecture / pause, stop, suivant), puis la liste des notifications. Toucher une ligne ouvre l'application concernee ; la croix l'efface.
    /// Ferme, le panneau ne calcule rien ; ouvert, il ne se reconstruit que quand la liste ou la piste change.
    /// </summary>
    public class Ar6dofNotifyApp : Ar6dofAppWindow
    {
        const int PerPage = 5;
        int page, seenVersion = -1, musicSig;
        Image badgeBg;
        Text badgeTxt;
        int badgeVersion = -1;
        Vector3 bottomBefore;
        bool hadLayout;
        static Sprite dotSprite;

        public Ar6dofNotifyApp(Ar6dofDock d) : base(d, "notif", "Notifications", "bell", "Alarmes, evenements, problemes et musique en cours", 660f) { }

        public override string TooltipText()
        {
            var s = Ar6dofNotifyService.Instance;
            int u = s != null ? s.Unread : 0;
            return Ar6dofLoc.T("Notifications") + (u > 0 ? " (" + u + ")" : "");
        }

        public override void Activate(bool on)
        {
            base.Activate(on);
            if (on) Replace();   // a chaque ouverture, le panneau se pose de nouveau au-dessus du dock (qui a pu bouger)
        }

        protected override void OnVisible(bool on)
        {
            if (on) { page = 0; return; }
            var s = Ar6dofNotifyService.Instance;
            if (s != null) s.MarkAllRead();   // tout ce qui etait affiche est considere comme vu
        }

        // la hauteur change avec le contenu : le bas du panneau reste au-dessus du dock, il grandit vers le haut
        protected override void OnRebuildStart()
        {
            hadLayout = false;
            if (canvas == null || !canvas.gameObject.activeInHierarchy) return;
            var rt = (RectTransform)canvas.transform;
            bottomBefore = canvas.transform.position - canvas.transform.up * (rt.sizeDelta.y * Mathf.Abs(rt.lossyScale.y));
            hadLayout = rt.sizeDelta.y > 20f;
        }

        protected override void OnRebuilt()
        {
            if (!hadLayout || canvas == null) return;
            var rt = (RectTransform)canvas.transform;
            canvas.transform.position = bottomBefore + canvas.transform.up * (rt.sizeDelta.y * Mathf.Abs(rt.lossyScale.y));
        }

        int MusicSignature()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null || !a.MusicActive) return 0;
            return 1 + (a.musicVersion << 1);
        }

        protected override void OnTick()
        {
            var s = Ar6dofNotifyService.Instance;
            if ((s != null && s.Version != seenVersion) || MusicSignature() != musicSig) Rebuild();
        }

        static string Mmss(float s)
        {
            if (s < 0f || float.IsNaN(s)) s = 0f;
            int t = Mathf.RoundToInt(s);
            return (t / 60) + ":" + (t % 60).ToString("00");
        }

        protected override void BuildMain()
        {
            var svc = Ar6dofNotifyService.Instance;
            seenVersion = svc != null ? svc.Version : 0;
            musicSig = MusicSignature();
            BuildMusic();
            BuildList(svc);
        }

        void BuildMusic()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null || !a.MusicActive) return;
            Section("Musique en cours");
            var nameLbl = Lbl("", 34, 22);
            nameLbl.gameObject.AddComponent<Ar6dofFitText>().Init(nameLbl, 22);
            var timeLbl = Lbl("", 28, 18);
            var pb = BtnRow(56f, 22, "", (Action)(() => { a.PrevTrack(); }),
                                     "", (Action)(() => { if (a.MusicActive) a.PauseMusic(!a.Meters.MusicPaused); }),
                                     "", (Action)(() => { a.StopMusic(); }),
                                     "", (Action)(() => { a.NextTrack(false); }));
            GlyphOn(pb[0], "prev", "Piste precedente");
            var playImg = GlyphOn(pb[1], "pause", "Lire / Pause");
            GlyphOn(pb[2], "stop", "Stop");
            GlyphOn(pb[3], "next", "Piste suivante");
            bool shownPause = true;
            refreshers.Add(() =>
            {
                nameLbl.text = a.TrackName;
                var m = a.Meters;
                timeLbl.text = Mmss(m.musicPos) + " / " + Mmss(m.musicDur);
                bool wantPause = !m.MusicPaused;
                if (wantPause == shownPause) return;
                shownPause = wantPause;
                playImg.sprite = Ar6dofDock.Glyph(wantPause ? "pause" : "play");
            });
        }

        static string KindGlyph(Ar6dofNoteKind k)
        {
            switch (k)
            {
                case Ar6dofNoteKind.Alarm: return "clock";
                case Ar6dofNoteKind.Event: return "calendar";
                case Ar6dofNoteKind.Error: return "alert";
                default: return "bell";
            }
        }

        void BuildList(Ar6dofNotifyService svc)
        {
            Section("Notifications");
            if (svc == null || svc.notes.Count == 0)
            {
                Para("Rien a signaler.", 40, 20);
                return;
            }
            int total = svc.notes.Count;
            int pages = Mathf.Max(1, (total + PerPage - 1) / PerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            bool h24 = Ar6dofSettings.Current.clock24h;
            float rowW = IW - 64f;
            for (int i = page * PerPage; i < Mathf.Min(total, (page + 1) * PerPage); i++)
            {
                var n = svc.notes[i];
                float cy = RowY(66f);
                Text l0;
                var rb = BtnAt(-IW * 0.5f + rowW * 0.5f, cy, rowW, 66f, "", () => OnNote(n), out l0, 20);
                Ar6dofTheme.SetSelected(rb, !n.read);
                var rrt = (RectTransform)rb.transform;
                var gi = new GameObject("Glyphe", typeof(RectTransform));
                gi.transform.SetParent(rrt, false);
                var grt = (RectTransform)gi.transform;
                grt.anchoredPosition = new Vector2(-rowW * 0.5f + 36f, 0f);
                grt.sizeDelta = new Vector2(44f, 44f);
                var img = gi.AddComponent<Image>();
                img.sprite = Ar6dofDock.Glyph(KindGlyph(n.kind));
                img.raycastTarget = false;
                Ar6dofTheme.Attach(gi, Ar6dofRole.Text);
                float tw = rowW - 84f;
                var t1 = Ar6dofVrUi.Label(rrt, n.title, new Vector2(30f, 15f), new Vector2(tw, 30f), 21, TextAnchor.MiddleLeft);
                t1.fontStyle = FontStyle.Bold;
                t1.gameObject.AddComponent<Ar6dofFitText>().Init(t1, 21);
                string sub = Ar6dofClockFmt.Time(n.time, h24, false) + (n.text.Length > 0 ? "  -  " + n.text : "");
                var t2 = Ar6dofVrUi.Label(rrt, sub, new Vector2(30f, -15f), new Vector2(tw, 26f), 17, TextAnchor.MiddleLeft);
                t2.gameObject.AddComponent<Ar6dofFitText>().Init(t2, 17);
                Text l1;
                var xb = BtnAt(IW * 0.5f - 28f, cy, 56f, 66f, "", () => { svc.Remove(n); Rebuild(); }, out l1, 20);
                GlyphOn(xb, "close", "Effacer", 0.5f);
            }
            if (pages > 1)
            {
                BtnRow(46f, 22, "<", (Action)(() => { page = (page + pages - 1) % pages; Rebuild(); }),
                                Ar6dofLoc.F("Page {0}/{1}", page + 1, pages), (Action)(() => { }),
                                ">", (Action)(() => { page = (page + 1) % pages; Rebuild(); }));
            }
            Btn("Tout effacer", () => { svc.Clear(); page = 0; Rebuild(); }, 46f, 21);
        }

        void OnNote(Ar6dofNote n)
        {
            var svc = Ar6dofNotifyService.Instance;
            if (svc != null) svc.MarkRead(n);
            if (!string.IsNullOrEmpty(n.app) && !dock.IsOpen(n.app)) dock.OpenApp(n.app);
            if (canvas != null && canvas.gameObject.activeSelf) Rebuild();
        }

        // ------------------------------------------------------------------ pastille du dock
        static Sprite Dot()
        {
            if (dotSprite != null) return dotSprite;
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(c + 0.5f - d) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            dotSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            return dotSprite;
        }

        /// <summary>Pastille rouge (probleme) ou orange avec le nombre de notifications non vues, sur le coin de la cloche. Appelee a chaque reconstruction des icones.</summary>
        public void BuildBadge(RectTransform icon, float size)
        {
            var go = new GameObject("Pastille", typeof(RectTransform));
            go.transform.SetParent(icon, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = new Vector2(size * 0.5f - 18f, size * 0.5f - 18f);
            rt.sizeDelta = new Vector2(36f, 36f);
            badgeBg = go.AddComponent<Image>();
            badgeBg.sprite = Dot();
            badgeBg.raycastTarget = false;
            badgeTxt = Ar6dofVrUi.Label(rt, "", Vector2.zero, new Vector2(36f, 36f), 22, TextAnchor.MiddleCenter);
            badgeTxt.fontStyle = FontStyle.Bold;
            badgeTxt.color = Color.white;
            badgeVersion = -1;
            ApplyBadge();
        }

        void ApplyBadge()
        {
            if (badgeBg == null) return;
            var s = Ar6dofNotifyService.Instance;
            int u = s != null ? s.Unread : 0;
            badgeVersion = s != null ? s.Version : 0;
            badgeBg.gameObject.SetActive(u > 0);
            if (u <= 0) return;
            badgeTxt.text = u > 9 ? "9+" : u.ToString();
            badgeBg.color = s.UnreadErrors > 0 ? new Color(0.88f, 0.16f, 0.16f) : new Color(0.95f, 0.50f, 0.08f);
        }

        /// <summary>Appelee a chaque image par le dock : ne fait rien tant que la liste n'a pas change.</summary>
        public void DockTick()
        {
            var s = Ar6dofNotifyService.Instance;
            if (s != null && badgeBg != null && s.Version != badgeVersion) ApplyBadge();
            if (open) Tick();
        }
    }
}
