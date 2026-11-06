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

    /// <summary>
    /// Vumètre (horizontal ou vertical) comme sur une console : vert jusqu'à -12 dB, jaune jusqu'à -3 dB, orange jusqu'à 0 dB, trait de crête retenu 1,2 s,
    /// et voyant rouge au bout qui s'allume quand le signal atteint 0 dB (retenu 2 s) : on voit quand ça sature.
    /// </summary>
    public class Ar6dofMeterBar
    {
        static readonly Color Green = new Color(0.20f, 0.85f, 0.35f), Yellow = new Color(0.96f, 0.86f, 0.20f), Orange = new Color(1f, 0.55f, 0.12f);
        static readonly Color ClipOn = new Color(0.95f, 0.18f, 0.16f), ClipOff = new Color(0.34f, 0.09f, 0.09f);
        readonly RectTransform[] seg = new RectTransform[3];
        RectTransform hold, led;
        Image ledImg;
        float len;
        bool vertical;
        float holdV, holdT, clipT, lastT = -1f;

        static RectTransform Seg(Transform parent, bool vertical, Color c, out Image img)
        {
            var rt = Ar6dofVrUi.Rect(parent, "Niveau", Vector2.zero, Vector2.zero);
            if (vertical) { rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f); }
            else { rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 0.5f); }
            img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = c;
            return rt;
        }

        public static Ar6dofMeterBar Make(Transform parent, Vector2 pos, Vector2 size, bool vertical = false)
        {
            var m = new Ar6dofMeterBar { vertical = vertical, len = (vertical ? size.y : size.x) - 8f - 12f };
            var root = Ar6dofVrUi.Rect(parent, "Niveau", pos, size);
            var bg = root.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            Ar6dofTheme.Attach(root.gameObject, Ar6dofRole.Track, 8);
            Image im;
            m.seg[0] = Seg(root, vertical, Green, out im);
            m.seg[1] = Seg(root, vertical, Yellow, out im);
            m.seg[2] = Seg(root, vertical, Orange, out im);
            m.hold = Seg(root, vertical, Color.white, out im);
            m.led = Seg(root, vertical, ClipOff, out m.ledImg);
            if (vertical) { m.led.anchoredPosition = new Vector2(0f, 4f + m.len + 2f); m.led.sizeDelta = new Vector2(-8f, 10f); }
            else { m.led.anchoredPosition = new Vector2(4f + m.len + 2f, 0f); m.led.sizeDelta = new Vector2(10f, -8f); }
            return m;
        }

        void Place(RectTransform rt, float from, float to)
        {
            float a = len * Mathf.Clamp01(from), b = len * Mathf.Clamp01(to);
            bool on = b - a > 0.6f;
            if (rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
            if (!on) return;
            if (vertical) { rt.anchoredPosition = new Vector2(0f, 4f + a); rt.sizeDelta = new Vector2(-8f, b - a); }
            else { rt.anchoredPosition = new Vector2(4f + a, 0f); rt.sizeDelta = new Vector2(b - a, -8f); }
        }

        /// <summary>Niveau de crête linéaire (0..1+) affiché en dB : -60 dB au début, 0 dB à la fin. Au-dessus de 1.0 : saturation (voyant rouge).</summary>
        public void SetPeak(float peak)
        {
            float now = Time.unscaledTime;
            float dt = lastT < 0f ? 0f : Mathf.Min(0.2f, now - lastT);
            lastT = now;
            float v = peak <= 0.0003f ? 0f : Mathf.Clamp01((20f * Mathf.Log10(peak) + 60f) / 60f);
            Place(seg[0], 0f, Mathf.Min(v, 0.8f));
            Place(seg[1], 0.8f, Mathf.Min(v, 0.95f));
            Place(seg[2], 0.95f, Mathf.Min(v, 1f));
            if (v >= holdV) { holdV = v; holdT = 1.2f; }
            else { holdT -= dt; if (holdT <= 0f) holdV = Mathf.Max(v, holdV - dt * 0.5f); }
            float th = 2.5f / Mathf.Max(1f, len);
            if (holdV > 0.03f) Place(hold, holdV - th, holdV + th); else Place(hold, 0f, 0f);
            clipT = peak >= 0.99f ? 2f : Mathf.Max(0f, clipT - dt);
            ledImg.color = clipT > 0f ? ClipOn : ClipOff;
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
        protected float W;
        readonly float baseW;
        protected const float Gap = 8f;
        protected readonly List<Action> refreshers = new List<Action>();
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        bool placed;
        float nextRefresh;

        enum View { Main, Pick, Browse }
        View view = View.Main;

        // ---- reglages audio propres a l'application (roue de la fenetre) : micro et / ou enceintes, seulement si l'application s'en sert
        /// <summary>Vrai si l'application utilise le micro : la roue propose alors de choisir le micro.</summary>
        public virtual bool UsesMic { get { return false; } }
        /// <summary>Vrai si l'application joue du son sur les enceintes : la roue propose alors de choisir la sortie.</summary>
        public virtual bool UsesOutput { get { return false; } }
        public bool HasSettings { get { return UsesMic || UsesOutput; } }
        bool settingsOpen;
        public bool SettingsOpen { get { return settingsOpen; } }
        public void ToggleSettings()
        {
            if (!HasSettings || canvas == null) return;
            settingsOpen = !settingsOpen;
            view = View.Main;
            Rebuild();
        }

        protected Ar6dofAppWindow(Ar6dofDock d, string id, string name, string glyph, string desc, float width)
        {
            dock = d;
            this.id = id;
            title = name;
            this.glyph = glyph;
            description = desc;
            W = width;
            baseW = width;
            canvas = Ar6dofVrUi.NewCanvas(name, new Vector2(width, 300f));
            ((RectTransform)canvas.transform).pivot = new Vector2(0.5f, 1f);
            canvas.gameObject.SetActive(false);
        }

        protected float IW { get { return W - 40f; } }

        /// <summary>Largeur de la vue principale ; une fenêtre dont une page a besoin de plus de place la redéfinit (la liste de choix et le navigateur de fichiers gardent la largeur de base).</summary>
        protected virtual float MainWidth() { return baseW; }

        void SetWidth(float w)
        {
            if (Mathf.Abs(w - W) < 0.5f) return;
            W = w;
            if (canvas != null) { var rt = (RectTransform)canvas.transform; rt.sizeDelta = new Vector2(w, rt.sizeDelta.y); }
        }

        // ------------------------------------------------------------------ Ar6dofDockApp
        public override Transform AnimRoot { get { return canvas != null ? canvas.transform : null; } }
        public override string TooltipText() { return Ar6dofLoc.T(title) + (string.IsNullOrEmpty(description) ? "" : " - " + Ar6dofLoc.T(description)); }

        public override void Activate(bool on)
        {
            if (canvas == null) return;
            canvas.gameObject.SetActive(on);
            if (on) { view = View.Main; settingsOpen = false; Rebuild(); }
            OnVisible(on);
        }

        public override void BeforeFirstShow() { if (!placed && place != null && canvas != null) { place(canvas.transform); placed = true; } }
        public void Replace() { if (place != null && canvas != null) place(canvas.transform); }
        public virtual void Destroy() { if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject); }

        protected virtual void OnVisible(bool on) { }
        /// <summary>Appele quand l'application est fermee (pas seulement reduite) : arreter ici ce qu'elle faisait.</summary>
        public virtual void OnClosed() { }
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
            SetWidth(view == View.Main ? MainWidth() : baseW);
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
                    if (settingsOpen && HasSettings) BuildAudioSettings();
                    else BuildMain();
                    break;
            }
            Ar6dofVrUi.FitHeight(canvas, y + 10f);
            OnRebuilt();
            Refresh();
        }

        /// <summary>Appele apres chaque reconstruction de la fenetre (tous ses elements sont crees).</summary>
        protected virtual void OnRebuilt() { }

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

        /// <summary>
        /// Remplace le texte d'un bouton par un dessin (lecture, pause, stop...), teinté comme le texte du thème. 'tip' : nom affiché au survol (traduit).
        /// Renvoie l'image : changer son 'sprite' (Ar6dofDock.Glyph("pause")) change le dessin sans rien reconstruire.
        /// </summary>
        protected static Image GlyphOn(Button b, string glyph, string tip = null, float frac = 0.58f)
        {
            var rt = (RectTransform)b.transform;
            var lbl = b.GetComponentInChildren<Text>();
            if (lbl != null) lbl.text = "";
            var g = new GameObject("Glyphe", typeof(RectTransform));
            g.transform.SetParent(rt, false);
            var grt = (RectTransform)g.transform;
            grt.anchoredPosition = Vector2.zero;
            float s = Mathf.Min(rt.sizeDelta.x, rt.sizeDelta.y) * frac;
            grt.sizeDelta = new Vector2(s, s);
            var gi = g.AddComponent<Image>();
            gi.sprite = Ar6dofDock.Glyph(glyph);
            gi.raycastTarget = false;
            Ar6dofTheme.Attach(g, Ar6dofRole.Text);
            if (tip != null)
            {
                var tt = b.gameObject.AddComponent<Ar6dofVrTooltip>();
                tt.text = () => Ar6dofLoc.T(tip);
            }
            return gi;
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

        // ------------------------------------------------------------------ reglages audio de l'application
        void BuildAudioSettings()
        {
            var a = Ar6dofAudio.Instance;
            if (a == null) { Lbl("Audio indisponible", 40, 22); return; }
            Section("Audio de cette application");
            EngineRow();
            Text t1 = null, t2 = null;
            if (UsesMic) BtnT("", () => PickAudioDevice(a, true), out t1, 52f, 22);
            if (UsesOutput) BtnT("", () => PickAudioDevice(a, false), out t2, 52f, 22);
            refreshers.Add(() =>
            {
                if (t1 != null) t1.text = Ar6dofLoc.T("Micro : ") + a.InputLabel();
                if (t2 != null) t2.text = Ar6dofLoc.T("Enceintes : ") + a.OutputLabel();
            });
            var st = Lbl("", 30, 18);
            refreshers.Add(() => st.text = (UsesMic ? Ar6dofLoc.T("Micro ") + StatusWord(a.InStatus) : "") + (UsesMic && UsesOutput ? "  -  " : "") + (UsesOutput ? Ar6dofLoc.T("Enceintes ") + StatusWord(a.OutStatus) : ""));
            Para(Ar6dofLoc.T(a.Online ? "Le casque garde le son de Windows et de la realite augmentee : seuls ta voix, les sons et la musique sortent sur l'appareil choisi." : "Active le moteur audio pour lister tes micros et tes enceintes."), 56, 18);
            Btn("Retour", ToggleSettings, 50f, 22);
        }

        public static string StatusWord(string s)
        {
            if (string.IsNullOrEmpty(s)) return "...";
            if (s == "ok") return Ar6dofLoc.T("ok");
            return Ar6dofLoc.T("erreur ") + s.Replace("err ", "");
        }

        void PickAudioDevice(Ar6dofAudio a, bool input)
        {
            a.RequestDevices();
            var items = new List<string> { Ar6dofLoc.T("Desactive"), Ar6dofLoc.T("Par defaut de Windows") };
            var list = input ? a.inputs : a.outputs;
            foreach (var d in list) items.Add(d.name + (d.isDefault ? "  (" + Ar6dofLoc.T("defaut") + ")" : ""));
            string cur = input ? a.data.inDev : a.data.outDev;
            int ci = cur == "off" ? 0 : cur == "default" ? 1 : -1;
            if (ci < 0) for (int i = 0; i < list.Count; i++) if (list[i].id == cur) ci = i + 2;
            var snapshot = new List<AudioDevice>(list);
            Pick(input ? "Choisir le micro" : "Choisir les enceintes externes", items, ci, i =>
            {
                string id = i == 0 ? "off" : i == 1 ? "default" : snapshot[i - 2].id;
                string name = i < 2 ? "" : snapshot[i - 2].name;
                if (input) a.SetInput(id, name); else a.SetOutput(id, name);
                if (id != "off" && !a.Enabled) a.SetEnabled(true);
                Refresh();
            });
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
        string brPath = "", brTitle = "", brLib = "", brRoot = "";
        string[] brExt;
        bool brMulti;
        int brPage;
        Action<List<string>> brDone;
        const int BrPerPage = 8;

        /// <summary>
        /// Remplace la fenêtre par la liste des fichiers du dossier 'library' de l'application (Documents\AR6DoF\&lt;library&gt;, voir Ar6dofLibrary) :
        /// pas de navigation dans le disque. Les sous-dossiers de ce dossier restent accessibles. 'done' reçoit les fichiers choisis.
        /// </summary>
        protected void Browse(string titleText, string[] extensions, bool multi, Action<List<string>> done, string library)
        {
            brTitle = titleText; brExt = extensions; brMulti = multi; brDone = done;
            brLib = library;
            brRoot = Ar6dofLibrary.Folder(library);
            brSel.Clear();
            brPath = brRoot;
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
            if (string.IsNullOrEmpty(brPath)) brPath = brRoot;
            bool atRoot = string.Equals(brPath.TrimEnd('\\', '/'), brRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            if (!atRoot)
            {
                string parent = brRoot;
                try { var p = Directory.GetParent(brPath.TrimEnd('\\', '/')); if (p != null) parent = p.FullName; } catch (Exception) { }
                brEntries.Add(new BrEntry { name = Ar6dofLoc.T("Dossier parent"), path = parent, dir = true, up = true });
            }
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

        void BuildBrowse()
        {
            var t = Ar6dofVrUi.Label(content, brTitle, new Vector2(0f, -(y + 24f)), new Vector2(IW, 48f), 28, TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            y += 48f + Gap;
            string shown = Ar6dofLibrary.Pretty(brLib);
            if (!string.Equals(brPath.TrimEnd('\\', '/'), brRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                shown += "\\" + brPath.Substring(Mathf.Min(brPath.Length, brRoot.Length)).Trim('\\', '/');
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
            if (brEntries.Count == 0 || (brEntries.Count == 1 && brEntries[0].up))
                Para("Ce dossier est vide. Touche « Ouvrir le dossier », depose tes fichiers dedans, puis « Actualiser ».", 56, 19);
            if (pages > 1)
            {
                BtnRow(46f, 22, "<", (Action)(() => { brPage = (brPage + pages - 1) % pages; Rebuild(); }),
                                Ar6dofLoc.F("Page {0}/{1}", brPage + 1, pages), (Action)(() => { }),
                                ">", (Action)(() => { brPage = (brPage + 1) % pages; Rebuild(); }));
            }
            BtnRow(48f, 21, "Ouvrir le dossier", (Action)(() => Ar6dofLibrary.OpenInExplorer(brRoot)),
                            "Actualiser", (Action)(() => { LoadEntries(); Rebuild(); }));
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
