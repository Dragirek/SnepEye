using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Barres de spectre (une barre par bande), mises à jour sans aucune allocation.</summary>
    public class Ar6dofSpectrum
    {
        RectTransform[] bars;
        float h;

        public static Ar6dofSpectrum Make(Transform parent, Vector2 pos, Vector2 size, int count)
        {
            var s = new Ar6dofSpectrum { h = size.y, bars = new RectTransform[count] };
            var root = Ar6dofVrUi.Rect(parent, "Spectre", pos, size);
            var bg = root.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            Ar6dofTheme.Attach(root.gameObject, Ar6dofRole.Track, 10);
            float slot = (size.x - 12f) / count;
            for (int i = 0; i < count; i++)
            {
                var b = Ar6dofVrUi.Rect(root, "Barre", new Vector2(6f + slot * (i + 0.5f), 4f), new Vector2(Mathf.Max(2f, slot - 3f), 2f));
                b.anchorMin = b.anchorMax = new Vector2(0f, 0f);
                b.pivot = new Vector2(0.5f, 0f);
                b.anchoredPosition = new Vector2(6f + slot * (i + 0.5f), 4f);
                var im = b.gameObject.AddComponent<Image>();
                im.raycastTarget = false;
                Ar6dofTheme.Attach(b.gameObject, Ar6dofRole.Accent, 3);
                s.bars[i] = b;
            }
            return s;
        }

        public void Set(float[] v)
        {
            if (bars == null || v == null) return;
            float max = h - 8f;
            for (int i = 0; i < bars.Length && i < v.Length; i++)
            {
                var sd = bars[i].sizeDelta;
                sd.y = Mathf.Max(2f, Mathf.Clamp01(v[i]) * max);
                bars[i].sizeDelta = sd;
            }
        }
    }

    /// <summary>Barre de niveau horizontale.</summary>
    public class Ar6dofMeterBar
    {
        RectTransform fill;
        float w;

        public static Ar6dofMeterBar Make(Transform parent, Vector2 pos, Vector2 size)
        {
            var m = new Ar6dofMeterBar { w = size.x - 8f };
            var root = Ar6dofVrUi.Rect(parent, "Niveau", pos, size);
            var bg = root.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            Ar6dofTheme.Attach(root.gameObject, Ar6dofRole.Track, 8);
            var f = Ar6dofVrUi.Rect(root, "Remplissage", Vector2.zero, new Vector2(2f, -8f));
            f.anchorMin = new Vector2(0f, 0f); f.anchorMax = new Vector2(0f, 1f);
            f.pivot = new Vector2(0f, 0.5f);
            f.anchoredPosition = new Vector2(4f, 0f);
            f.sizeDelta = new Vector2(2f, -8f);
            var im = f.gameObject.AddComponent<Image>();
            im.raycastTarget = false;
            Ar6dofTheme.Attach(f.gameObject, Ar6dofRole.Accent, 6);
            m.fill = f;
            return m;
        }

        /// <summary>Niveau de crête linéaire (0..1+) affiché en dB : -60 dB à gauche, 0 dB à droite.</summary>
        public void SetPeak(float peak)
        {
            float v = peak <= 0.0003f ? 0f : Mathf.Clamp01((20f * Mathf.Log10(peak) + 60f) / 60f);
            fill.sizeDelta = new Vector2(Mathf.Max(2f, w * v), -8f);
        }
    }

    /// <summary>
    /// Une application de la barre de tâches qui vit dans sa propre fenêtre (Canvas) : Voix, Soundboard, Table de mixage, Appareil photo...
    /// Fournit tout ce qu'il faut pour construire une fenêtre : lignes de texte, boutons, interrupteurs, curseurs, liste de choix, navigateur de fichiers.
    /// Fermée, la fenêtre est désactivée : aucun calcul. Les classes dérivées remplissent BuildMain() et, si besoin, OnTick().
    /// </summary>
    public abstract class Ar6dofAppWindow : Ar6dofDockApp
    {
        public string id = "", glyph = "", description = "";
        public Canvas canvas;
        public Action<Transform> place;
        public float yaw = 35f;
        protected readonly Ar6dofDock dock;
        protected RectTransform content;
        protected float y;
        protected readonly float W;
        protected const float Gap = 8f;
        protected readonly List<Action> refreshers = new List<Action>();
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        bool placed;
        float nextRefresh;

        enum View { Main, Pick, Browse }
        View view = View.Main;

        protected Ar6dofAppWindow(Ar6dofDock d, string id, string name, string glyph, string desc, float width)
        {
            dock = d;
            this.id = id;
            title = name;
            this.glyph = glyph;
            description = desc;
            W = width;
            canvas = Ar6dofVrUi.NewCanvas(name, new Vector2(width, 300f));
            ((RectTransform)canvas.transform).pivot = new Vector2(0.5f, 1f);
            canvas.gameObject.SetActive(false);
        }

        protected float IW { get { return W - 40f; } }

        // ------------------------------------------------------------------ Ar6dofDockApp
        public override Transform AnimRoot { get { return canvas != null ? canvas.transform : null; } }
        public override string TooltipText() { return Ar6dofLoc.T(title) + (string.IsNullOrEmpty(description) ? "" : " - " + Ar6dofLoc.T(description)); }

        public override void Activate(bool on)
        {
            if (canvas == null) return;
            canvas.gameObject.SetActive(on);
            if (on) { view = View.Main; Rebuild(); }
            OnVisible(on);
        }

        public override void BeforeFirstShow() { if (!placed && place != null && canvas != null) { place(canvas.transform); placed = true; } }
        public void Replace() { if (place != null && canvas != null) place(canvas.transform); }
        public virtual void Destroy() { if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject); }

        protected virtual void OnVisible(bool on) { }
        protected virtual void OnTick() { }
        protected abstract void BuildMain();

        /// <summary>Appelé à chaque image par le dock, seulement quand la fenêtre est ouverte.</summary>
        public void Tick()
        {
            if (canvas == null || !canvas.gameObject.activeSelf) return;
            if (Time.unscaledTime > nextRefresh) { nextRefresh = Time.unscaledTime + 0.25f; Refresh(); }
            OnTick();
        }

        protected void Refresh() { for (int i = 0; i < refreshers.Count; i++) refreshers[i](); }

        public void Rebuild()
        {
            if (canvas == null) return;
            var cur = Ar6dofVrTextField.Current;
            if (cur != null && cur.transform.IsChildOf(canvas.transform)) cur.Blur();
            if (content != null) UnityEngine.Object.Destroy(content.gameObject);
            refreshers.Clear();
            OnRebuildStart();
            content = Ar6dofVrUi.NewContent(canvas);
            y = 14f;
            switch (view)
            {
                case View.Pick: BuildPick(); break;
                case View.Browse: BuildBrowse(); break;
                default:
                    var t = Ar6dofVrUi.Label(content, title, new Vector2(0f, -(y + 24f)), new Vector2(IW, 48f), 32, TextAnchor.MiddleLeft);
                    t.fontStyle = FontStyle.Bold;
                    y += 48f + Gap;
                    BuildMain();
                    break;
            }
            Ar6dofVrUi.FitHeight(canvas, y + 10f);
            Refresh();
        }

        /// <summary>Appelé juste avant la reconstruction : les classes dérivées oublient leurs références vers l'ancienne vue.</summary>
        protected virtual void OnRebuildStart() { }

        // ------------------------------------------------------------------ éléments
        protected float RowY(float h) { float cy = -(y + h * 0.5f); y += h + Gap; return cy; }
        protected void Space(float h) { y += h; }

        protected Text Lbl(string text, float h = 34f, int fs = 24, TextAnchor a = TextAnchor.MiddleLeft)
        {
            return Ar6dofVrUi.Label(content, text, new Vector2(0f, RowY(h)), new Vector2(IW, h), fs, a);
        }

        /// <summary>Paragraphe qui passe à la ligne (les textes de Lbl restent sur une seule ligne).</summary>
        protected Text Para(string text, float h = 56f, int fs = 18)
        {
            var t = Lbl(text, h, fs);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        protected Text Section(string text)
        {
            Space(6f);
            var t = Lbl(text, 34f, 24);
            t.fontStyle = FontStyle.Bold;
            t.MakeAccent();
            return t;
        }

        protected Button Btn(string text, Action click, float h = 52f, int fs = 24)
        {
            Text t;
            return BtnT(text, click, out t, h, fs);
        }

        protected Button BtnT(string text, Action click, out Text label, float h = 52f, int fs = 24)
        {
            return Ar6dofVrUi.MakeButton(content, text, new Vector2(0f, RowY(h)), new Vector2(IW, h), () => click(), out label, fs);
        }

        protected Button BtnAt(float cx, float cy, float w, float h, string text, Action click, out Text label, int fs = 24)
        {
            return Ar6dofVrUi.MakeButton(content, text, new Vector2(cx, cy), new Vector2(w, h), () => click(), out label, fs);
        }

        /// <summary>Plusieurs boutons de même largeur sur une ligne : paires (texte, Action).</summary>
        protected List<Button> BtnRow(float h, int fs, params object[] items)
        {
            var res = new List<Button>();
            float cy = RowY(h);
            int n = items.Length / 2;
            float w = (IW - (n - 1) * 8f) / n;
            for (int i = 0; i < n; i++)
            {
                string text = (string)items[2 * i];
                Action act = (Action)items[2 * i + 1];
                Text t;
                float x = -IW * 0.5f + w * 0.5f + i * (w + 8f);
                res.Add(Ar6dofVrUi.MakeButton(content, text, new Vector2(x, cy), new Vector2(w, h), () => act(), out t, fs));
            }
            return res;
        }

        /// <summary>Onglets : le bouton de l'onglet actif est teinté. Un clic change d'onglet et reconstruit la fenêtre.</summary>
        protected void Tabs(string[] names, int current, Action<int> choose)
        {
            var items = new object[names.Length * 2];
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                items[2 * i] = names[i];
                items[2 * i + 1] = (Action)(() => { choose(k); Rebuild(); });
            }
            var bs = BtnRow(50f, 21, items);
            for (int i = 0; i < bs.Count; i++) Ar6dofTheme.SetSelected(bs[i], i == current);
        }

        protected Button ToggleRow(string label, Func<bool> get, Action<bool> set, float h = 52f, int fs = 24)
        {
            Text t;
            Button b = null;
            string lab = label;
            b = BtnT(label, () => { set(!get()); Refresh(); }, out t, h, fs);
            Text tt = t;
            Button bb = b;
            refreshers.Add(() => { tt.text = Ar6dofLoc.T(lab) + " : " + Ar6dofLoc.T(get() ? "Oui" : "Non"); Ar6dofTheme.SetSelected(bb, get()); });
            return b;
        }

        protected Slider SliderRow(string label, float min, float max, Func<float> get, Action<float> set, Func<float, string> fmt, float h = 54f, bool whole = false)
        {
            float cy = RowY(h);
            float lw = IW * 0.34f, vw = IW * 0.17f, sw = IW - lw - vw - 16f;
            float x0 = -IW * 0.5f;
            var lab = Ar6dofVrUi.Label(content, label, new Vector2(x0 + lw * 0.5f, cy), new Vector2(lw, h), 22, TextAnchor.MiddleLeft);
            lab.gameObject.AddComponent<Ar6dofFitText>().Init(lab, 22);
            var val = Ar6dofVrUi.Label(content, "", new Vector2(IW * 0.5f - vw * 0.5f, cy), new Vector2(vw, h), 21, TextAnchor.MiddleRight);
            val.gameObject.AddComponent<Ar6dofFitText>().Init(val, 21);
            Slider s = null;
            s = Ar6dofVrUi.MakeSlider(content, new Vector2(x0 + lw + 8f + sw * 0.5f, cy), new Vector2(sw, 40f), min, max, get(), v =>
            {
                if (whole) v = Mathf.Round(v);
                set(v);
                val.text = fmt(v);
            });
            s.wholeNumbers = whole;
            Slider ss = s;
            refreshers.Add(() => { float v = get(); ss.SetValueWithoutNotify(Mathf.Clamp(v, min, max)); val.text = fmt(v); });
            return s;
        }

        protected static string Fmt(string f, float v) { return string.Format(Ci, f, v); }
        protected static string Pct(float v) { return Mathf.RoundToInt(v * 100f).ToString(Ci) + "%"; }

        /// <summary>Ligne « Moteur audio » commune aux fenêtres de son : interrupteur + état.</summary>
        protected void EngineRow()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null) { Lbl("Audio indisponible", 34, 22); return; }
            ToggleRow("Moteur audio", () => a.Enabled, v => a.SetEnabled(v));
            var st = Lbl("", 30, 19);
            refreshers.Add(() => st.text = EngineText(a));
        }

        protected static string EngineText(Ar6dofAudio a)
        {
            switch (a.State)
            {
                case Ar6dofAudio.EngineState.Off: return Ar6dofLoc.T("Eteint : aucun programme lance, aucun calcul");
                case Ar6dofAudio.EngineState.Starting: return Ar6dofLoc.T("Demarrage...");
                case Ar6dofAudio.EngineState.Online: return Ar6dofLoc.T("Actif");
                default: return string.IsNullOrEmpty(a.Message) ? Ar6dofLoc.T("Erreur") : a.Message;
            }
        }

        // ------------------------------------------------------------------ liste de choix
        List<string> pickItems;
        string pickTitle = "";
        int pickCur, pickPage;
        Action<int> pickDone;
        const int PerPage = 7;

        /// <summary>Remplace la fenêtre par une liste de choix (un bouton par ligne). 'done' reçoit l'indice choisi.</summary>
        protected void Pick(string titleText, List<string> items, int current, Action<int> done)
        {
            pickItems = items; pickTitle = titleText; pickCur = current; pickDone = done;
            pickPage = Mathf.Max(0, current) / PerPage;
            view = View.Pick;
            Rebuild();
        }

        void BuildPick()
        {
            var t = Ar6dofVrUi.Label(content, pickTitle, new Vector2(0f, -(y + 24f)), new Vector2(IW, 48f), 28, TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            y += 48f + Gap;
            int pages = Mathf.Max(1, (pickItems.Count + PerPage - 1) / PerPage);
            pickPage = Mathf.Clamp(pickPage, 0, pages - 1);
            for (int i = pickPage * PerPage; i < Mathf.Min(pickItems.Count, (pickPage + 1) * PerPage); i++)
            {
                int k = i;
                var b = Btn(pickItems[i], () => { view = View.Main; var d = pickDone; pickDone = null; Rebuild(); if (d != null) d(k); }, 52f, 22);
                Ar6dofTheme.SetSelected(b, i == pickCur);
            }
            if (pages > 1)
            {
                BtnRow(48f, 22, "<", (Action)(() => { pickPage = (pickPage + pages - 1) % pages; Rebuild(); }),
                                Ar6dofLoc.F("Page {0}/{1}", pickPage + 1, pages), (Action)(() => { }),
                                ">", (Action)(() => { pickPage = (pickPage + 1) % pages; Rebuild(); }));
            }
            Btn("Retour", () => { view = View.Main; Rebuild(); }, 50f, 22);
        }

        // ------------------------------------------------------------------ navigateur de fichiers intégré
        class BrEntry { public string name = "", path = ""; public bool dir, up; }
        readonly List<BrEntry> brEntries = new List<BrEntry>();
        readonly List<string> brSel = new List<string>();
        string brPath = "", brTitle = "";
        string[] brExt;
        bool brMulti;
        int brPage;
        Action<List<string>> brDone;
        const int BrPerPage = 8;

        /// <summary>Remplace la fenêtre par un navigateur de fichiers (dossiers, disques, raccourcis). 'done' reçoit les fichiers choisis.</summary>
        protected void Browse(string titleText, string[] extensions, bool multi, Action<List<string>> done)
        {
            brTitle = titleText; brExt = extensions; brMulti = multi; brDone = done;
            brSel.Clear();
            string last = "";
            try { last = PlayerPrefs.GetString("ar6dof_browse_last", ""); } catch (Exception) { }
            brPath = (!string.IsNullOrEmpty(last) && Directory.Exists(last)) ? last : "";
            brPage = 0;
            LoadEntries();
            view = View.Browse;
            Rebuild();
        }

        static bool ExtOk(string file, string[] exts)
        {
            if (exts == null || exts.Length == 0) return true;
            string e = Path.GetExtension(file).ToLowerInvariant();
            foreach (var x in exts) if (e == x) return true;
            return false;
        }

        void LoadEntries()
        {
            brEntries.Clear();
            if (string.IsNullOrEmpty(brPath))
            {
                try
                {
                    string own = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ar6dof_sounds"));
                    if (Directory.Exists(own)) brEntries.Add(new BrEntry { name = "ar6dof_sounds", path = own, dir = true });
                }
                catch (Exception) { }
                AddSpecial(Environment.SpecialFolder.MyMusic, "Musique");
                AddSpecial(Environment.SpecialFolder.Desktop, "Bureau");
                AddSpecial(Environment.SpecialFolder.MyDocuments, "Documents");
                try
                {
                    string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (Directory.Exists(dl)) brEntries.Add(new BrEntry { name = Ar6dofLoc.T("Telechargements"), path = dl, dir = true });
                }
                catch (Exception) { }
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                        if (d.IsReady) brEntries.Add(new BrEntry { name = Ar6dofLoc.T("Disque ") + d.Name, path = d.Name, dir = true });
                }
                catch (Exception) { }
                return;
            }
            string parent = "";
            try { var p = Directory.GetParent(brPath.TrimEnd('\\', '/')); parent = p != null ? p.FullName : ""; } catch (Exception) { }
            brEntries.Add(new BrEntry { name = Ar6dofLoc.T("Dossier parent"), path = parent, dir = true, up = true });
            try
            {
                var dirs = Directory.GetDirectories(brPath);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (var d in dirs)
                {
                    try
                    {
                        var at = File.GetAttributes(d);
                        if ((at & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch (Exception) { continue; }
                    brEntries.Add(new BrEntry { name = Path.GetFileName(d), path = d, dir = true });
                }
                var files = Directory.GetFiles(brPath);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files) if (ExtOk(f, brExt)) brEntries.Add(new BrEntry { name = Path.GetFileName(f), path = f });
            }
            catch (Exception) { }
        }

        void AddSpecial(Environment.SpecialFolder sf, string label)
        {
            try
            {
                string p = Environment.GetFolderPath(sf);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) brEntries.Add(new BrEntry { name = Ar6dofLoc.T(label), path = p, dir = true });
            }
            catch (Exception) { }
        }

        void BuildBrowse()
        {
            var t = Ar6dofVrUi.Label(content, brTitle, new Vector2(0f, -(y + 24f)), new Vector2(IW, 48f), 28, TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            y += 48f + Gap;
            string shown = string.IsNullOrEmpty(brPath) ? Ar6dofLoc.T("Ce PC") : brPath;
            if (shown.Length > 52) shown = "..." + shown.Substring(shown.Length - 49);
            Lbl(shown, 28, 18);
            int pages = Mathf.Max(1, (brEntries.Count + BrPerPage - 1) / BrPerPage);
            brPage = Mathf.Clamp(brPage, 0, pages - 1);
            for (int i = brPage * BrPerPage; i < Mathf.Min(brEntries.Count, (brPage + 1) * BrPerPage); i++)
            {
                var e = brEntries[i];
                string text = e.up ? "<  " + e.name : (e.dir ? ">  " + e.name : (brMulti ? (brSel.Contains(e.path) ? "[x]  " : "[  ]  ") + e.name : e.name));
                var en = e;
                var b = Btn(text, () => OnEntry(en), 46f, 21);
                if (!e.dir && brSel.Contains(e.path)) Ar6dofTheme.SetSelected(b, true);
            }
            if (brEntries.Count == 0 || (brEntries.Count == 1 && brEntries[0].up)) Para("Aucun fichier compatible dans ce dossier", 34, 20);
            if (pages > 1)
            {
                BtnRow(46f, 22, "<", (Action)(() => { brPage = (brPage + pages - 1) % pages; Rebuild(); }),
                                Ar6dofLoc.F("Page {0}/{1}", brPage + 1, pages), (Action)(() => { }),
                                ">", (Action)(() => { brPage = (brPage + 1) % pages; Rebuild(); }));
            }
            if (brMulti && brSel.Count > 0)
                BtnRow(52f, 22, Ar6dofLoc.F("Valider ({0})", brSel.Count), (Action)FinishBrowse, "Annuler", (Action)(() => { view = View.Main; Rebuild(); }));
            else
                Btn("Annuler", () => { view = View.Main; Rebuild(); }, 50f, 22);
        }

        void OnEntry(BrEntry e)
        {
            if (e.dir)
            {
                brPath = e.path;
                brPage = 0;
                if (!string.IsNullOrEmpty(brPath)) { try { PlayerPrefs.SetString("ar6dof_browse_last", brPath); } catch (Exception) { } }
                LoadEntries();
                Rebuild();
                return;
            }
            if (brMulti)
            {
                if (brSel.Contains(e.path)) brSel.Remove(e.path); else brSel.Add(e.path);
                Rebuild();
                return;
            }
            brSel.Clear();
            brSel.Add(e.path);
            FinishBrowse();
        }

        void FinishBrowse()
        {
            var done = brDone;
            var res = new List<string>(brSel);
            brDone = null;
            view = View.Main;
            Rebuild();
            if (done != null && res.Count > 0) done(res);
        }
    }
}
