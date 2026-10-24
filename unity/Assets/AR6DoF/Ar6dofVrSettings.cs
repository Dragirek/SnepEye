using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Le panneau de réglages en réalité virtuelle (roue du dock) : image, optique, écrans, système, yeux, style, dock et langue.
    /// Tout ce qui s'affichait avant dans le panneau F1 est ici, dans l'onglet de l'objet concerné.
    /// Style / Dock / Langue sont des paramètres GLOBAUX (Ar6dofSettings, fichier settings.json) : ils s'appliquent tout de suite en aperçu
    /// et ne sont gardés qu'avec le bouton Enregistrer. Annuler = retour au dernier enregistrement, Par defaut = valeurs d'usine.
    /// </summary>
    public class Ar6dofVrSettings
    {
        public Canvas canvas;
        readonly Ar6dofDock dock;
        readonly List<RectTransform> pages = new List<RectTransform>();
        readonly List<Button> tabButtons = new List<Button>();
        readonly List<Action> refreshers = new List<Action>();
        readonly List<KeyValuePair<int, Action>> live = new List<KeyValuePair<int, Action>>();   // textes qui changent tout seuls (onglet, action)
        int curTab;
        float nextLive;

        // onglets
        const int TabImage = 0, TabOptics = 1, TabScreens = 2, TabSystem = 3, TabEyes = 4, TabStyle = 5, TabDock = 6, TabLang = 7;
        static readonly string[] TabNames = { "Image", "Optique", "Ecrans", "Systeme", "Yeux", "Style", "Dock", "Langue" };

        // bas du panneau
        Text statusLbl, resetLbl;
        Button saveBtn;
        bool resetArmed;
        float resetUntil, statusUntil;
        string statusMsg = "";

        // onglet Style
        static readonly Ar6dofElem[] Elems = { Ar6dofElem.Global, Ar6dofElem.Dock, Ar6dofElem.Menus, Ar6dofElem.Keyboard, Ar6dofElem.Screens, Ar6dofElem.Laser };
        static readonly string[] ElemNames = { "Tout", "Dock", "Menus", "Clavier", "Ecrans", "Rayon" };
        Ar6dofElem selElem = Ar6dofElem.Global;
        int colorSlot = 2;          // 0 fond, 1 boutons, 2 accent, 3 texte
        int styleSub;               // 0 couleurs, 1 forme, 2 fenetres
        float hue, sat, val = 1f;
        readonly List<Button> elemButtons = new List<Button>();
        readonly List<Button> chipButtons = new List<Button>();
        readonly List<Image> chipSwatches = new List<Image>();
        readonly List<Button> subButtons = new List<Button>();
        readonly List<Button> chromeStyleButtons = new List<Button>();
        readonly List<Button> chromeCornerButtons = new List<Button>();
        RectTransform colorsBox, formBox, shapeBox, texBox, laserBox, separateBox, windowsBox;
        Text scopeLbl, texNameLbl;

        // onglet Optique
        int optEye;                 // 0 gauche, 1 droit (quand les yeux ne sont pas liés)
        readonly List<Button> eyeButtons = new List<Button>();
        RectTransform eyeSelBox;
        Text eyeHint;

        // onglet Système : calibration de la télécommande avec compte à rebours (pour avoir le temps de poser la manette)
        Text calMsg;
        string calMsgFr = "";
        int calStep;                // 0 = rien, 1 ou 2 = étape en attente
        float calAt;
        const float CalDelay = 3f;

        // défilement des pages
        readonly List<GameObject> views = new List<GameObject>();
        readonly List<Ar6dofVrScrollArea> scrolls = new List<Ar6dofVrScrollArea>();
        Button scrollUp, scrollDown;
        Image scrollTrack, scrollHandle;

        // sélecteur de couleur
        Ar6dofVrColorPicker picker;
        Image preview;
        Ar6dofVrTextField fHex;
        readonly Ar6dofVrTextField[] fRgb = new Ar6dofVrTextField[3];
        readonly Ar6dofVrTextField[] fHsv = new Ar6dofVrTextField[3];

        // onglet Langue
        readonly List<Button> langButtons = new List<Button>();
        readonly List<string> langCodes = new List<string>();

        const float W = 720f, H = 940f;
        const float PageH = H - 250f;

        public Ar6dofVrSettings(Ar6dofDock d)
        {
            dock = d;
            canvas = Ar6dofVrUi.NewCanvas("Reglages VR", new Vector2(W, H));
            Ar6dofVrUi.Label(canvas.transform, "Reglages", new Vector2(0f, H * 0.5f - 36f), new Vector2(W - 40f, 50f), 34);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int idx = i;
                Text t;
                var b = Ar6dofVrUi.MakeButton(canvas.transform, TabNames[i], new Vector2((i - (TabNames.Length - 1) * 0.5f) * 88f, H * 0.5f - 92f), new Vector2(84f, 46f), () => ShowTab(idx), out t, 17);
                tabButtons.Add(b);
            }
            for (int i = 0; i < TabNames.Length; i++)
            {
                // fenêtre (découpe + zone vide qui capte le glissement) contenant la page, que le défilement déplace
                var vp = new GameObject("Vue" + i, typeof(RectTransform));
                vp.transform.SetParent(canvas.transform, false);
                var vrt = (RectTransform)vp.transform;
                vrt.sizeDelta = new Vector2(W, PageH);
                vrt.anchoredPosition = Vector2.zero;
                var vimg = vp.AddComponent<Image>();
                vimg.color = new Color(0f, 0f, 0f, 0f);
                vp.AddComponent<RectMask2D>();
                var go = new GameObject("Page" + i, typeof(RectTransform));
                go.transform.SetParent(vrt, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(W, PageH);
                rt.anchoredPosition = Vector2.zero;
                pages.Add(rt);
                var sa = vp.AddComponent<Ar6dofVrScrollArea>();
                int tabIdx = i;
                sa.content = rt;
                sa.viewport = vrt;
                sa.maxScroll = () => MaxScroll(tabIdx);
                sa.onChanged = UpdateScrollUi;
                views.Add(vp);
                scrolls.Add(sa);
            }
            BuildPassthrough(pages[TabImage]);
            BuildOptics(pages[TabOptics]);
            BuildScreens(pages[TabScreens]);
            BuildSystem(pages[TabSystem]);
            BuildEyes(pages[TabEyes]);
            BuildStyle(pages[TabStyle]);
            BuildDock(pages[TabDock]);
            BuildLanguage(pages[TabLang]);
            BuildBottom();
            BuildScrollControls();
            Ar6dofSettings.Changed += OnSettingsChanged;
            Ar6dofLoc.Changed += OnSettingsChanged;
            ShowTab(0);
        }

        void OnSettingsChanged()
        {
            if (canvas == null)
            {
                Ar6dofSettings.Changed -= OnSettingsChanged;
                Ar6dofLoc.Changed -= OnSettingsChanged;
                return;
            }
            RefreshAll();
        }

        void RefreshAll()
        {
            foreach (var r in refreshers) r();
            foreach (var kv in live) kv.Value();
            UpdateStatus();
            if (scrollUp != null && curTab < scrolls.Count) scrolls[curTab].SetOffset(scrolls[curTab].offset);   // recale + met à jour l'ascenseur
        }

        // ---- défilement ----
        /// <summary>Hauteur à découvrir en plus pour un onglet (0 = tout tient).</summary>
        float MaxScroll(int tab)
        {
            if (tab == TabStyle && styleSub == 0) return 190f;
            return 0f;
        }

        void ScrollBy(float d) { if (curTab < scrolls.Count) scrolls[curTab].By(d); }

        void BuildScrollControls()
        {
            float x = W * 0.5f - 22f, top = PageH * 0.5f;
            Text t;
            scrollUp = Ar6dofVrUi.MakeButton(canvas.transform, "^", new Vector2(x, top - 24f), new Vector2(38f, 38f), () => ScrollBy(-150f), out t, 22);
            scrollDown = Ar6dofVrUi.MakeButton(canvas.transform, "v", new Vector2(x, -top + 24f), new Vector2(38f, 38f), () => ScrollBy(150f), out t, 22);
            var tr = Ar6dofVrUi.Rect(canvas.transform, "PisteDefilement", new Vector2(x, 0f), new Vector2(6f, PageH - 110f));
            scrollTrack = tr.gameObject.AddComponent<Image>();
            scrollTrack.raycastTarget = false;
            Ar6dofTheme.Attach(tr.gameObject, Ar6dofRole.Track, 3);
            var hd = Ar6dofVrUi.Rect(tr, "Poignee", Vector2.zero, new Vector2(10f, 40f));
            scrollHandle = hd.gameObject.AddComponent<Image>();
            scrollHandle.raycastTarget = false;
            Ar6dofTheme.Attach(hd.gameObject, Ar6dofRole.Accent, 5);
            UpdateScrollUi();
        }

        void UpdateScrollUi()
        {
            if (scrollUp == null || curTab >= scrolls.Count) return;
            var sa = scrolls[curTab];
            float max = sa.Max;
            bool on = max > 0.5f;
            scrollUp.gameObject.SetActive(on);
            scrollDown.gameObject.SetActive(on);
            scrollTrack.gameObject.SetActive(on);
            if (!on) return;
            float trackH = PageH - 110f;
            float hh = Mathf.Clamp(trackH * PageH / (PageH + max), 30f, trackH);
            var hr = (RectTransform)scrollHandle.transform;
            hr.sizeDelta = new Vector2(10f, hh);
            float frac = Mathf.Clamp01(sa.offset / max);
            hr.anchoredPosition = new Vector2(0f, (trackH - hh) * 0.5f - frac * (trackH - hh));
        }

        void Live(int tab, Action a) { live.Add(new KeyValuePair<int, Action>(tab, a)); a(); }

        /// <summary>Appelé à chaque image par la barre de tâches : textes vivants de l'onglet visible, état Enregistré / Aperçu.</summary>
        public void Tick()
        {
            if (canvas == null || !canvas.gameObject.activeInHierarchy) return;
            float now = Time.unscaledTime;
            if (resetArmed && now > resetUntil) { resetArmed = false; UpdateResetLabel(); }
            // molette d'une vraie souris : fait défiler la page visée par le laser
            float wheel = Ar6dofInput.ScrollY();
            if (wheel != 0f && dock.pointer != null)
            {
                var hov = dock.pointer.HoverUi;
                if (hov != null && hov.transform.IsChildOf(canvas.transform)) ScrollBy(-wheel * 90f);
            }
            // calibration de la télécommande : l'étape part quand le compte à rebours est fini
            if (calStep != 0)
            {
                if (now >= calAt)
                {
                    var r = Remote;
                    calMsgFr = r == null ? "telecommande introuvable" : (calStep == 1 ? r.CalibrateStep1() : r.CalibrateStep2());
                    calStep = 0;
                }
                UpdateCalMsg();
            }
            if (now < nextLive) return;
            nextLive = now + 0.25f;
            foreach (var kv in live) if (kv.Key == curTab) kv.Value();
            UpdateStatus();
        }

        public void ShowTab(int i)
        {
            curTab = i;
            for (int k = 0; k < pages.Count; k++)
            {
                views[k].SetActive(k == i);
                if (k != i) scrolls[k].SetOffset(0f);
                Ar6dofTheme.SetSelected(tabButtons[k], k == i);
            }
            RefreshAll();
        }

        // ---- éléments ----
        float TopY { get { return PageH * 0.5f - 30f; } }

        static RectTransform NewBox(RectTransform parent, string name)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var rt = (RectTransform)g.transform;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        /// <summary>Curseur avec son texte au-dessus, à l'endroit (x, y) et de largeur w. Le texte est aligné à gauche, au même bord que le curseur.</summary>
        Slider SliderAt(RectTransform page, float x, float y, float w, string label, float min, float max, Func<float> get, Action<float> set, string fmt = "0.##")
        {
            Text lbl = Ar6dofVrUi.Label(page, "", new Vector2(x, y + 22f), new Vector2(w, 30f), 22, TextAnchor.MiddleLeft);
            Action upd = () => lbl.text = Ar6dofLoc.T(label) + " : " + get().ToString(fmt);
            var s = Ar6dofVrUi.MakeSlider(page, new Vector2(x, y - 16f), new Vector2(w, 40f), min, max, get(), v => { set(v); upd(); });
            refreshers.Add(() => { s.SetValueWithoutNotify(Mathf.Clamp(get(), min, max)); upd(); });
            upd();
            return s;
        }

        void Slider(RectTransform page, ref float y, string label, float min, float max, Func<float> get, Action<float> set, string fmt = "0.##", float step = 78f)
        {
            SliderAt(page, 0f, y, W - 90f, label, min, max, get, set, fmt);
            y -= step;
        }

        /// <summary>Case à cocher : un petit carré SUR LE CÔTÉ, le texte à droite du carré (aligné à gauche, jamais dans le carré).</summary>
        void Toggle(RectTransform page, float x, float y, string label, Func<bool> get, Action<bool> set, float width = 290f)
        {
            Text t = null;
            GameObject tick = null;
            Action upd = () =>
            {
                if (t != null) t.text = Ar6dofLoc.T(label);
                if (tick != null) tick.SetActive(get());
            };
            var b = Ar6dofVrUi.MakeButton(page, "", new Vector2(x, y), new Vector2(width, 48f), () => { set(!get()); upd(); }, out t, 20);
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.rectTransform.sizeDelta = new Vector2(width - 70f, 48f);
            t.rectTransform.anchoredPosition = new Vector2(25f, 0f);
            var box = Ar6dofVrUi.Rect(b.transform, "Case", new Vector2(-width * 0.5f + 32f, 0f), new Vector2(30f, 30f));
            var bi = box.gameObject.AddComponent<Image>();
            bi.raycastTarget = false;
            Ar6dofTheme.Attach(box.gameObject, Ar6dofRole.Track, 7);
            var ck = Ar6dofVrUi.Rect(box, "Coche", Vector2.zero, new Vector2(18f, 18f));
            var ci = ck.gameObject.AddComponent<Image>();
            ci.raycastTarget = false;
            Ar6dofTheme.Attach(ck.gameObject, Ar6dofRole.Accent, 4);
            tick = ck.gameObject;
            refreshers.Add(upd);
            upd();
        }

        Button Btn(RectTransform page, float x, float y, float w, string label, Action click, int fs = 24, float h = 48f)
        {
            Text t;
            return Ar6dofVrUi.MakeButton(page, label, new Vector2(x, y), new Vector2(w, h), () => click(), out t, fs);
        }

        Text ButtonText(RectTransform page, float x, float y, float w, string label, Action click, int fs = 24)
        {
            Text t;
            Ar6dofVrUi.MakeButton(page, label, new Vector2(x, y), new Vector2(w, 48f), () => click(), out t, fs);
            return t;
        }

        /// <summary>Ligne d'information (texte aligné à gauche) qui se met à jour toute seule.</summary>
        Text InfoLine(RectTransform page, float y, int tab, Func<string> text, int fs = 19)
        {
            Text l = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(W - 70f, 28f), fs, TextAnchor.MiddleLeft);
            Live(tab, () => l.text = text());
            return l;
        }

        Text Heading(RectTransform page, float y, string text)
        {
            return Ar6dofVrUi.Label(page, text, new Vector2(0f, y), new Vector2(W - 70f, 34f), 24, TextAnchor.MiddleLeft).MakeAccent();
        }

        static void Touch() { Ar6dofSettings.Notify(); }

        // ---- bas du panneau : Enregistrer / Annuler / Par defaut ----
        void BuildBottom()
        {
            float by = -H * 0.5f + 50f;
            Text t;
            saveBtn = Ar6dofVrUi.MakeButton(canvas.transform, "Enregistrer", new Vector2(-220f, by), new Vector2(200f, 56f), OnSave, out t, 22);
            Ar6dofVrUi.MakeButton(canvas.transform, "Annuler", new Vector2(0f, by), new Vector2(200f, 56f), OnCancel, out t, 22);
            Ar6dofVrUi.MakeButton(canvas.transform, "Par defaut", new Vector2(220f, by), new Vector2(200f, 56f), OnReset, out resetLbl, 22);
            statusLbl = Ar6dofVrUi.Label(canvas.transform, "", new Vector2(0f, -H * 0.5f + 112f), new Vector2(W - 40f, 28f), 18);
        }

        void UpdateResetLabel()
        {
            if (resetLbl != null) resetLbl.text = Ar6dofLoc.T(resetArmed ? "Confirmer ?" : "Par defaut");
        }

        void UpdateStatus()
        {
            if (statusLbl == null) return;
            bool dirty = Ar6dofSettings.Dirty;
            string msg;
            if (Time.unscaledTime < statusUntil) msg = statusMsg;
            else msg = Ar6dofLoc.T(dirty ? "Apercu : pas encore enregistre" : "Tout est enregistre");
            statusLbl.text = msg;
            Ar6dofTheme.SetSelected(saveBtn, dirty);
            UpdateResetLabel();
        }

        void Say(string fr)
        {
            statusMsg = Ar6dofLoc.T(fr);
            statusUntil = Time.unscaledTime + 3f;
            UpdateStatus();
        }

        void OnSave()
        {
            bool ok = Ar6dofSettings.Save();
            Say(ok ? "Enregistre" : "Echec de l'enregistrement");
        }

        void OnCancel()
        {
            Ar6dofSettings.Revert();
            Say("Modifications annulees");
        }

        void OnReset()
        {
            if (!resetArmed)
            {
                resetArmed = true;
                resetUntil = Time.unscaledTime + 3f;
                UpdateResetLabel();
                return;
            }
            resetArmed = false;
            Ar6dofSettings.ResetDefaults();
            Say("Valeurs d'usine en apercu : clique Enregistrer pour les garder");
        }

        // ================================================================== IMAGE
        void BuildPassthrough(RectTransform page)
        {
            var pt = dock.passthrough;
            float y = TopY;
            if (pt == null) { Ar6dofVrUi.Label(page, "Passthrough introuvable dans la scene", new Vector2(0f, y), new Vector2(W - 60f, 40f), 24); return; }
            // infos de l'image (avant : panneau F1)
            InfoLine(page, y, TabImage, () => pt.SourceOpen
                ? Ar6dofLoc.F("Source : {0} x {1}   -   {2} img/s   -   age {3} ms", pt.SourceWidth, pt.SourceHeight, pt.UploadsPerSec, pt.FrameAgeMs.ToString("0"))
                : Ar6dofLoc.T("Source : camera non ouverte"), 18);
            InfoLine(page, y - 26f, TabImage, () => Ar6dofLoc.F("Envoi GPU : {0} ms   -   reprojection {1} deg   -   delai {2} ms",
                pt.UploadMs.ToString("0.0"), pt.ReprojDeg.ToString("0.0"), pt.DelayUsedMs.ToString("0")), 18);
            InfoLine(page, y - 52f, TabImage, () => Ar6dofLoc.T(pt.HasCalibration ? "Calibration camera : chargee" : "Calibration camera : ABSENTE (camera_model.json)"), 18);
            y -= 120f;
            Slider(page, ref y, "FOV affiche (deg)", 40f, 130f, () => pt.settings.displayFovDeg, v => pt.settings.displayFovDeg = v, "0");
            Slider(page, ref y, "IPD logiciel (ecarte +)", -0.4f, 0.4f, () => pt.settings.ipdShift, v => pt.settings.ipdShift = v, "0.00");
            Slider(page, ref y, "Delai manuel (ms)", 0f, 120f, () => pt.settings.captureDelayMs, v => pt.settings.captureDelayMs = v, "0");
            y -= 6f;
            Toggle(page, -178f, y, "Calibration camera", () => pt.settings.useCalibration, v => pt.settings.useCalibration = v, 340f);
            Toggle(page, 178f, y, "Reprojection", () => pt.settings.reproject, v => pt.settings.reproject = v, 340f); y -= 58f;
            Toggle(page, -178f, y, "Delai auto", () => pt.settings.autoDelay, v => pt.settings.autoDelay = v, 340f);
            Toggle(page, 178f, y, "Etirer les bords", () => pt.settings.clampEdges, v => pt.settings.clampEdges = v, 340f); y -= 58f;
            Toggle(page, -178f, y, "Rendu a l'envers", () => pt.settings.flipComposite, v => pt.settings.flipComposite = v, 340f); y -= 72f;
            Btn(page, 0f, y, 400f, "Sauver ces reglages d'image", () => { pt.SaveSettings(); Say("Reglages d'image sauves"); });
        }

        // ================================================================== OPTIQUE
        Ar6dofEyeSettings Eye(Ar6dofPassthrough pt)
        {
            return (!pt.settings.linkEyes && optEye == 1) ? pt.settings.right : pt.settings.left;
        }

        void BuildOptics(RectTransform page)
        {
            var pt = dock.passthrough;
            float y = TopY;
            if (pt == null) { Ar6dofVrUi.Label(page, "Passthrough introuvable dans la scene", new Vector2(0f, y), new Vector2(W - 60f, 40f), 24); return; }
            const float cx = 178f, cw = 330f;
            Toggle(page, -cx, y, "Yeux lies (droit = miroir du gauche)", () => pt.settings.linkEyes, v => pt.settings.linkEyes = v, 340f);
            Toggle(page, cx, y, "Echanger G / D", () => pt.settings.swapEyes, v => pt.settings.swapEyes = v, 340f); y -= 58f;

            // quel oeil : visible seulement quand les yeux ne sont pas liés
            eyeSelBox = NewBox(page, "Oeil");
            eyeButtons.Add(Btn(eyeSelBox, -cx, y, 340f, "Oeil gauche", () => { optEye = 0; RefreshAll(); }, 22));
            eyeButtons.Add(Btn(eyeSelBox, cx, y, 340f, "Oeil droit", () => { optEye = 1; RefreshAll(); }, 22));
            eyeHint = Ar6dofVrUi.Label(page, "Les reglages ci-dessous s'appliquent aux deux yeux", new Vector2(0f, y), new Vector2(W - 70f, 30f), 19, TextAnchor.MiddleLeft);
            y -= 28f;
            refreshers.Add(() =>
            {
                bool linked = pt.settings.linkEyes;
                eyeSelBox.gameObject.SetActive(!linked);
                eyeHint.gameObject.SetActive(linked);
                for (int i = 0; i < eyeButtons.Count; i++) Ar6dofTheme.SetSelected(eyeButtons[i], i == optEye);
            });

            // deux colonnes de curseurs
            y -= 40f;
            SliderAt(page, -cx, y, cw, "Decalage horizontal", -0.6f, 0.6f, () => Eye(pt).panX, v => Eye(pt).panX = v, "0.00");
            SliderAt(page, cx, y, cw, "Decalage vertical", -0.6f, 0.6f, () => Eye(pt).panY, v => Eye(pt).panY = v, "0.00"); y -= 72f;
            SliderAt(page, -cx, y, cw, "Centre lentille X", -0.5f, 0.5f, () => Eye(pt).lensCenterX, v => Eye(pt).lensCenterX = v, "0.000");
            SliderAt(page, cx, y, cw, "Centre lentille Y", -0.5f, 0.5f, () => Eye(pt).lensCenterY, v => Eye(pt).lensCenterY = v, "0.000"); y -= 72f;
            SliderAt(page, -cx, y, cw, "Distorsion k1", -0.8f, 0.8f, () => Eye(pt).k1, v => Eye(pt).k1 = v, "0.000");
            SliderAt(page, cx, y, cw, "Distorsion k2", -0.8f, 0.8f, () => Eye(pt).k2, v => Eye(pt).k2 = v, "0.000"); y -= 72f;
            SliderAt(page, -cx, y, cw, "Distorsion k3", -0.8f, 0.8f, () => Eye(pt).k3, v => Eye(pt).k3 = v, "0.000");
            Text rot = ButtonText(page, cx, y - 8f, 340f, "", () => { var e = Eye(pt); e.rotateQuarterTurns = (e.rotateQuarterTurns + 1) & 3; RefreshAll(); }, 20);
            refreshers.Add(() => rot.text = Ar6dofLoc.F("Rotation du flux : {0} deg", (Eye(pt).rotateQuarterTurns & 3) * 90));
            y -= 72f;
            Toggle(page, -cx, y, "Miroir horizontal", () => Eye(pt).mirrorX, v => Eye(pt).mirrorX = v, 340f);
            Toggle(page, cx, y, "Miroir vertical", () => Eye(pt).mirrorY, v => Eye(pt).mirrorY = v, 340f); y -= 58f;
            Text lay = ButtonText(page, -cx, y, 340f, "", () => { pt.settings.layout = pt.settings.layout == Ar6dofStereoLayout.SideBySide ? Ar6dofStereoLayout.TopBottom : Ar6dofStereoLayout.SideBySide; RefreshAll(); }, 20);
            refreshers.Add(() => lay.text = Ar6dofLoc.T(pt.settings.layout == Ar6dofStereoLayout.SideBySide ? "Flux : cote a cote" : "Flux : haut / bas"));
            Text fov = ButtonText(page, cx, y, 340f, "", () => { pt.settings.displayFovDeg = Mathf.Clamp(pt.MaxFovNoBlackDeg, 40f, 130f); RefreshAll(); }, 20);
            refreshers.Add(() => fov.text = Ar6dofLoc.F("FOV max sans bord noir : {0} deg", pt.MaxFovNoBlackDeg.ToString("0")));
            y -= 58f;
            Toggle(page, 0f, y, "Recentrer sur le milieu de l'image (decale tout : deconseille)", () => pt.settings.centerOnImage, v => pt.settings.centerOnImage = v, 640f); y -= 62f;
            Btn(page, -235f, y, 225f, "Recharger", () => { pt.ReloadSettings(); Say("Reglages d'image recharges"); RefreshAll(); }, 20);
            Btn(page, 0f, y, 225f, "Sauver", () => { pt.SaveSettings(); Say("Reglages d'image sauves"); }, 20);
            Btn(page, 235f, y, 225f, "Remettre a zero", () => { pt.ResetSettings(); Say("Reglages d'image remis a zero (Sauver pour garder)"); RefreshAll(); }, 20);
        }

        // ================================================================== ECRANS
        void BuildScreens(RectTransform page)
        {
            float y = TopY;
            Heading(page, y, "Images / s de tous les ecrans"); y -= 52f;
            Btn(page, -210f, y, 190f, "30", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(30); });
            Btn(page, 0f, y, 190f, "60", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(60); });
            Btn(page, 210f, y, 190f, "90", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(90); }); y -= 70f;
            Toggle(page, 0f, y, "Coller les ecrans aux bords", () => Ar6dofScreens.Instance != null && Ar6dofScreens.Instance.snapEnabled,
                   v => { if (Ar6dofScreens.Instance != null) { Ar6dofScreens.Instance.snapEnabled = v; Ar6dofScreens.Instance.Save(); } }, 440f); y -= 64f;
            Text hinge = ButtonText(page, 0f, y, 440f, "", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.CycleHinge(); });
            refreshers.Add(() => hinge.text = Ar6dofLoc.F("Courbure : {0} deg", Ar6dofScreens.Instance != null ? Mathf.RoundToInt(Ar6dofScreens.Instance.hingeDegrees) : 0));
            y -= 64f;
            Btn(page, 0f, y, 440f, "+ Ajouter un ecran", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.AddScreen(); }); y -= 64f;
            Btn(page, 0f, y, 440f, "Recentrer les ecrans devant moi", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.RecenterAll(); }); y -= 64f;
            InfoLine(page, y, TabScreens, () => Ar6dofLoc.F("Ecrans actifs : {0}", Ar6dofScreens.Instance != null ? Ar6dofScreens.Instance.panels.Count : 0), 20); y -= 60f;
            Ar6dofVrUi.Label(page, "La roue sous un ecran ouvre ses reglages (source, images/s...) ; clique-la encore pour les fermer. Le coin bas droit redimensionne, les boutons du coin reduisent ou ferment. La resolution est celle du moniteur Windows capture.", new Vector2(0f, y - 50f), new Vector2(W - 80f, 140f), 19, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // ================================================================== SYSTEME
        Ar6dofRemote Remote
        {
            get
            {
                if (dock.pointer != null && dock.pointer.remote != null) return dock.pointer.remote;
                return UnityEngine.Object.FindFirstObjectByType<Ar6dofRemote>();
            }
        }

        void StartCal(int step)
        {
            calStep = step;
            calAt = Time.unscaledTime + CalDelay;
            calMsgFr = "";
            UpdateCalMsg();
        }

        void UpdateCalMsg()
        {
            if (calMsg == null) return;
            if (calStep != 0)
            {
                int left = Mathf.CeilToInt(Mathf.Max(0f, calAt - Time.unscaledTime));
                calMsg.text = Ar6dofLoc.F(calStep == 1 ? "Calibration 1/2 dans {0} s : pose la telecommande a plat, immobile" : "Calibration 2/2 dans {0} s : releve la pointe de ~30 deg, immobile", left);
            }
            else calMsg.text = calMsgFr.Length > 0 ? Ar6dofLoc.T(calMsgFr) : "";
        }

        void BuildSystem(RectTransform page)
        {
            float y = TopY;
            Heading(page, y, "Suivi de la tete"); y -= 34f;
            InfoLine(page, y, TabSystem, () =>
            {
                var rig = dock.rig;
                if (rig == null) return Ar6dofLoc.T("Tete : casque introuvable");
                if (!rig.HasPose) return Ar6dofLoc.T("Tete : en attente du casque");
                if (rig.ConnectionLost) return Ar6dofLoc.T("Tete : liaison perdue");
                return Ar6dofLoc.T(rig.TrackingOk ? "Tete : suivi OK" : "Tete : suivi degrade");
            }); y -= 28f;
            InfoLine(page, y, TabSystem, () =>
            {
                var rig = dock.rig;
                if (rig == null || !rig.HasPose) return "";
                return Ar6dofLoc.F("Sol : {0}   -   hauteur des yeux : {1} cm", Ar6dofLoc.T(rig.FloorDetected ? "detecte" : "estime"), Mathf.RoundToInt(rig.HeadHeight * 100f));
            }); y -= 46f;
            if (dock.launcher != null)
            {
                Btn(page, -235f, y, 225f, "Recentrer le tracking", () => dock.launcher.Send('T'), 17);
                Btn(page, 0f, y, 225f, "Verrouiller le sol", () => dock.launcher.Send('V'), 17);
                Btn(page, 235f, y, 225f, "Remettre le cap a zero", () => dock.launcher.Send('Z'), 17);
                y -= 62f;
            }
            else y -= 6f;

            Heading(page, y, "Telecommande"); y -= 34f;
            InfoLine(page, y, TabSystem, () =>
            {
                var r = Remote;
                if (r == null) return Ar6dofLoc.T("Telecommande : composant introuvable");
                if (!r.Connected) return Ar6dofLoc.T("Telecommande : non connectee");
                string b = (r.Left ? Ar6dofLoc.T("G") : "-") + " " + (r.Right ? Ar6dofLoc.T("D") : "-");
                return Ar6dofLoc.F("Telecommande : connectee   -   {0} paquets/s   -   boutons {1}", r.PacketsPerSec, b);
            }); y -= 30f;
            Ar6dofVrUi.Label(page, "Recentrer le rayon : maintenir les 2 boutons 0,6 s", new Vector2(0f, y), new Vector2(W - 70f, 26f), 17, TextAnchor.MiddleLeft); y -= 28f;
            Ar6dofVrUi.Label(page, "Un compte a rebours de 3 s laisse le temps de poser la telecommande", new Vector2(0f, y), new Vector2(W - 70f, 26f), 17, TextAnchor.MiddleLeft); y -= 40f;
            Btn(page, -178f, y, 340f, "Calibrer 1/2 (a plat)", () => StartCal(1), 19);
            Btn(page, 178f, y, 340f, "Calibrer 2/2 (pointe relevee)", () => StartCal(2), 19);
            y -= 38f;
            calMsg = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(W - 70f, 28f), 17, TextAnchor.MiddleLeft);
            refreshers.Add(UpdateCalMsg);
            y -= 54f;
            Heading(page, y + 16f, "Telecommande : filtre du rayon"); y -= 40f;
            Slider(page, ref y, "Stabilite (plus bas = plus stable)", 0.5f, 8f, () => Ar6dofSettings.Current.remoteMinCutoff, v => { Ar6dofSettings.Current.remoteMinCutoff = v; Touch(); }, "0.0", 70f);
            Slider(page, ref y, "Reactivite en mouvement", 0f, 1f, () => Ar6dofSettings.Current.remoteBeta, v => { Ar6dofSettings.Current.remoteBeta = v; Touch(); }, "0.00", 70f);
            Slider(page, ref y, "Anticipation (ms)", 0f, 40f, () => Ar6dofSettings.Current.remotePredictMs, v => { Ar6dofSettings.Current.remotePredictMs = v; Touch(); }, "0", 70f);
        }

        static string CamTxt(int state, float fps)
        {
            if (state < 0) return Ar6dofLoc.T("aucune");
            return state == 0 ? Ar6dofLoc.T("arretee") : Ar6dofLoc.F("{0} fps", Mathf.RoundToInt(fps));
        }

        void BuildEyes(RectTransform page)
        {
            var et = dock.eyes;
            float y = TopY;
            if (et == null) { Ar6dofVrUi.Label(page, "Eye tracking introuvable", new Vector2(0f, y), new Vector2(W - 60f, 40f), 24); return; }
            Toggle(page, 0f, y, "Eye tracking actif", () => et.Enabled, v => et.SetEnabled(v), 400f); y -= 70f;
            Text l1 = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(W - 60f, 32f), 21, TextAnchor.MiddleLeft); y -= 36f;
            Text l2 = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(W - 60f, 32f), 21, TextAnchor.MiddleLeft); y -= 36f;
            Text l3 = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(W - 60f, 32f), 21, TextAnchor.MiddleLeft); y -= 36f;
            Text l4 = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(W - 60f, 32f), 21, TextAnchor.MiddleLeft); y -= 56f;
            Live(4, () =>
            {
                var st = et.Status;
                var g = et.Gaze;
                if (!et.Enabled) l1.text = Ar6dofLoc.T("Desactive : aucune camera, aucun port, aucune charge");
                else if (!et.EngineOnline) l1.text = Ar6dofLoc.T("ar6dof_probe ne repond pas (lance-le avec Eye tracking = 1)");
                else l1.text = Ar6dofLoc.T("ar6dof_probe : en ligne") + (st.on ? "" : Ar6dofLoc.T(" (cameras arretees)"));
                bool on = et.Enabled && et.EngineOnline;
                l2.text = on ? Ar6dofLoc.F("Cameras : gauche {0}   droite {1}", CamTxt(st.camL, st.fpsL), CamTxt(st.camR, st.fpsR)) : "";
                l3.text = on ? Ar6dofLoc.F("Etalonnage : {0}   pupille : gauche {1}, droite {2}", Ar6dofLoc.T((st.calL || st.calR) ? "fait" : "a faire"),
                    Ar6dofLoc.T(st.seeL ? "vue" : "non"), Ar6dofLoc.T(st.seeR ? "vue" : "non")) : "";
                if (on && et.Receiving)
                {
                    if (g.valid)
                        l4.text = Ar6dofLoc.F("Regard : {0} deg (horizontal)   {1} deg (vertical)", g.yaw.ToString("+0.0;-0.0;0.0"), g.pitch.ToString("+0.0;-0.0;0.0"))
                            + ((g.blinkL || g.blinkR) ? Ar6dofLoc.T("   clignement") : "");
                    else
                        l4.text = Ar6dofLoc.T(g.calibrated ? "Regard : oeil non detecte" : "Regard : pas d'etalonnage");
                }
                else l4.text = "";
            });
            Btn(page, 0f, y, 440f, "Etalonner (9 points)", () =>
            {
                et.SetEnabled(true);
                if (dock.eyeCal != null) dock.eyeCal.Begin(et);
            }); y -= 64f;
            Btn(page, 0f, y, 440f, "Recentrer (regarde droit devant)", () => et.Recenter(), 22); y -= 64f;
            Btn(page, 0f, y, 440f, "Effacer l'etalonnage", () => et.ClearCalibration()); y -= 84f;
            Ar6dofVrUi.Label(page, "Pendant l'etalonnage, garde la tete immobile et suis la cible\ndes yeux. Les donnees du regard sont lues par Unity dans\nAr6dofEyeTracker.Instance.Gaze (pour tes animations).", new Vector2(0f, y - 30f), new Vector2(W - 60f, 100f), 19, TextAnchor.UpperLeft);
        }

        // ================================================================== STYLE
        static Ar6dofStyle CurStyle(Ar6dofElem e) { return Ar6dofSettings.StyleFor(e); }

        Color GetC(int slot)
        {
            var s = CurStyle(selElem);
            switch (slot)
            {
                case 0: return s.bg;
                case 1: return s.button;
                case 2: return s.accent;
                default: return s.text;
            }
        }

        void SetC(int slot, Color c)
        {
            c.a = 1f;
            var s = CurStyle(selElem);
            switch (slot)
            {
                case 0: s.bg = c; break;
                case 1: s.button = c; break;
                case 2: s.accent = c; break;
                default: s.text = c; break;
            }
            Touch();
        }

        /// <summary>Garde teinte / saturation / luminosité stables pendant qu'on glisse un curseur (le gris perd sa teinte sinon).</summary>
        void SyncHsv()
        {
            Color c = GetC(colorSlot);
            Color back = Color.HSVToRGB(hue, sat, val);
            if (Mathf.Abs(back.r - c.r) > 0.006f || Mathf.Abs(back.g - c.g) > 0.006f || Mathf.Abs(back.b - c.b) > 0.006f)
            {
                float h, s, v;
                Color.RGBToHSV(c, out h, out s, out v);
                if (s > 0.01f && v > 0.01f) hue = h;
                sat = s;
                val = v;
            }
        }

        void SetHsv()
        {
            SetC(colorSlot, Color.HSVToRGB(Mathf.Clamp01(hue), Mathf.Clamp01(sat), Mathf.Clamp01(val)));
        }

        void BuildStyle(RectTransform page)
        {
            // 1) quel élément : Tout (commun) ou un élément à part
            for (int i = 0; i < Elems.Length; i++)
            {
                int idx = i;
                elemButtons.Add(Btn(page, -250f + 100f * i, 290f, 96f, ElemNames[i], () => { selElem = Elems[idx]; RefreshAll(); }, 18));
            }
            scopeLbl = Ar6dofVrUi.Label(page, "", new Vector2(0f, 244f), new Vector2(W - 40f, 30f), 19);
            separateBox = NewBox(page, "Separe");
            Toggle(separateBox, 0f, 198f, "Style a part pour cet element", () =>
            {
                var es = Ar6dofSettings.ElemOf(selElem);
                return es != null && es.separate;
            }, v =>
            {
                var es = Ar6dofSettings.ElemOf(selElem);
                if (es == null) return;
                if (v && !es.separate) { es.style = Ar6dofSettings.Current.global.Clone(); es.separate = true; }
                else if (!v) es.separate = false;
                Touch();
            }, 470f);

            // 2) Couleurs / Forme
            subButtons.Add(Btn(page, -220f, 140f, 210f, "Couleurs", () => { styleSub = 0; scrolls[TabStyle].SetOffset(0f); RefreshAll(); }));
            subButtons.Add(Btn(page, 0f, 140f, 210f, "Forme", () => { styleSub = 1; scrolls[TabStyle].SetOffset(0f); RefreshAll(); }));
            subButtons.Add(Btn(page, 220f, 140f, 210f, "Fenetres", () => { styleSub = 2; scrolls[TabStyle].SetOffset(0f); RefreshAll(); }));

            // ---- Couleurs
            colorsBox = NewBox(page, "Couleurs");
            string[] chipNames = { "Fond", "Boutons", "Accent", "Texte" };
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                var b = Btn(colorsBox, -225f + 150f * i, 84f, 144f, chipNames[i], () => { colorSlot = slot; RefreshAll(); }, 18);
                var sw = new GameObject("Echantillon", typeof(RectTransform));
                sw.transform.SetParent(b.transform, false);
                var srt = (RectTransform)sw.transform;
                srt.anchoredPosition = new Vector2(-50f, 0f);
                srt.sizeDelta = new Vector2(26f, 26f);
                var img = sw.AddComponent<Image>();
                img.raycastTarget = false;
                chipButtons.Add(b);
                chipSwatches.Add(img);
                // le texte du bouton se décale pour laisser la place à l'échantillon
                var lbl = b.GetComponentInChildren<Text>();
                if (lbl != null) lbl.rectTransform.offsetMin = new Vector2(26f, 0f);
            }
            refreshers.Add(SyncHsv);
            BuildColorPicker(colorsBox);

            // ---- Forme
            formBox = NewBox(page, "Forme");
            float fy = 60f;
            Slider(formBox, ref fy, "Opacite des fonds", 0.15f, 1f, () => CurStyle(selElem).alpha, v => { CurStyle(selElem).alpha = v; Touch(); }, "0.00");
            shapeBox = NewBox(formBox, "Arrondi");
            Slider(shapeBox, ref fy, "Coins arrondis", 0f, 40f, () => CurStyle(selElem).corner, v => { CurStyle(selElem).corner = Mathf.RoundToInt(v); Touch(); }, "0");
            texBox = NewBox(formBox, "Texture");
            Slider(texBox, ref fy, "Intensite de la texture", 0f, 0.6f, () => CurStyle(selElem).texAlpha, v => { CurStyle(selElem).texAlpha = v; Touch(); }, "0.00");
            Ar6dofVrUi.Label(texBox, "Texture de fond", new Vector2(-150f, -176f), new Vector2(300f, 28f), 22, TextAnchor.MiddleLeft);
            Btn(texBox, -250f, -224f, 70f, "<", () => CycleTexture(-1));
            Btn(texBox, 250f, -224f, 70f, ">", () => CycleTexture(1));
            texNameLbl = Ar6dofVrUi.Label(texBox, "", new Vector2(0f, -224f), new Vector2(380f, 40f), 24);
            Btn(texBox, 0f, -284f, 440f, "Ouvrir le dossier pictures (tes images)", () => Ar6dofTheme.OpenPicturesFolder(), 19);
            laserBox = NewBox(formBox, "Rayon");
            float ly = -24f;
            Slider(laserBox, ref ly, "Epaisseur du rayon (mm)", 1f, 15f, () => CurStyle(Ar6dofElem.Laser).lineWidth * 1000f, v => { CurStyle(Ar6dofElem.Laser).lineWidth = v / 1000f; Touch(); }, "0.0");

            // ---- Fenetres : boutons reduire / fermer de tous les ecrans et menus
            windowsBox = NewBox(page, "Fenetres");
            Ar6dofVrUi.Label(windowsBox, "Style des boutons de fenetre", new Vector2(0f, 250f), new Vector2(W - 70f, 34f), 24, TextAnchor.MiddleLeft).MakeAccent();
            chromeStyleButtons.Add(Btn(windowsBox, -178f, 196f, 340f, "Windows", () => { Ar6dofSettings.Current.chromeStyle = 0; Touch(); }, 24));
            chromeStyleButtons.Add(Btn(windowsBox, 178f, 196f, 340f, "Mac", () => { Ar6dofSettings.Current.chromeStyle = 1; Touch(); }, 24));
            Ar6dofVrUi.Label(windowsBox, "Position de Reduire / Fermer", new Vector2(0f, 120f), new Vector2(W - 70f, 34f), 24, TextAnchor.MiddleLeft).MakeAccent();
            string[] corners = { "Haut gauche", "Haut droite", "Bas gauche", "Bas droite" };
            for (int i = 0; i < 4; i++)
            {
                int ci = i;
                chromeCornerButtons.Add(Btn(windowsBox, (i % 2 == 0) ? -178f : 178f, (i < 2) ? 66f : 8f, 340f, corners[i], () => { Ar6dofSettings.Current.chromeCorner = ci; Touch(); }, 22));
            }
            Ar6dofVrUi.Label(windowsBox, "Valable pour tous les ecrans et tous les menus. Par defaut : Windows, en haut a droite. Le redimensionnement se fait par la poignee unique en bas a droite de chaque fenetre.", new Vector2(0f, -110f), new Vector2(W - 90f, 120f), 19, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;
            windowsBox.gameObject.SetActive(false);

            refreshers.Add(RefreshStyleUi);
        }

        void CycleTexture(int dir)
        {
            var ids = Ar6dofTheme.TextureIds();
            var st = CurStyle(selElem);
            int i = Mathf.Max(0, ids.IndexOf(st.texture ?? ""));
            i = (i + dir + ids.Count) % ids.Count;
            st.texture = ids[i];
            if (st.texture.Length > 0 && st.texAlpha < 0.05f) st.texAlpha = 0.18f;
            Touch();
        }

        // 12 palettes : nom, fond, boutons, accent, texte
        static readonly string[] PalNames = { "Nuit", "Rouge", "Vert", "Violet", "Clair", "Ocean", "Orange", "Rose", "Menthe", "Or", "Gris", "Contraste" };
        static readonly string[,] PalCols =
        {
            { "#14171C", "#384252", "#40D9FF", "#FFFFFF" },
            { "#240D0F", "#5C2429", "#FF4D4D", "#FFFFFF" },
            { "#0D1F12", "#245233", "#59FF73", "#FFFFFF" },
            { "#1A0F29", "#452E66", "#BF73FF", "#FFFFFF" },
            { "#EBEDF2", "#B8C2D1", "#1A73D9", "#14171C" },
            { "#071A2B", "#15446B", "#29B6F6", "#FFFFFF" },
            { "#211208", "#5E3410", "#FF9A1F", "#FFFFFF" },
            { "#2A0F1F", "#6B2A4F", "#FF6EB4", "#FFFFFF" },
            { "#0A211F", "#1D5C55", "#4DFFD2", "#FFFFFF" },
            { "#1C1608", "#524215", "#FFD23F", "#FFFFFF" },
            { "#1B1B1B", "#4A4A4A", "#B0B0B0", "#FFFFFF" },
            { "#000000", "#262626", "#FFE600", "#FFFFFF" },
        };

        static Color Hex(string h)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString(h, out c)) c = Color.magenta;
            c.a = 1f;
            return c;
        }

        void ApplyPalette(int p)
        {
            var st = CurStyle(selElem);
            st.bg = Hex(PalCols[p, 0]);
            st.button = Hex(PalCols[p, 1]);
            st.accent = Hex(PalCols[p, 2]);
            st.text = Hex(PalCols[p, 3]);
            Touch();
        }

        static float Rnd(float a, float b) { return UnityEngine.Random.Range(a, b); }

        /// <summary>Couleur aléatoire pour la case choisie (fond sombre, boutons moyens, accent vif, texte clair).</summary>
        void RandomColor()
        {
            float h = UnityEngine.Random.value, s, v;
            switch (colorSlot)
            {
                case 0: s = Rnd(0.25f, 0.75f); v = Rnd(0.06f, 0.22f); break;
                case 1: s = Rnd(0.3f, 0.7f); v = Rnd(0.25f, 0.5f); break;
                case 2: s = Rnd(0.6f, 1f); v = Rnd(0.85f, 1f); break;
                default: s = Rnd(0f, 0.12f); v = Rnd(0.9f, 1f); break;
            }
            hue = h; sat = s; val = v;
            SetHsv();
        }

        /// <summary>Palette aléatoire harmonieuse : une teinte pour le fond et les boutons, la teinte opposée pour l'accent.</summary>
        void RandomPalette()
        {
            var st = CurStyle(selElem);
            float h = UnityEngine.Random.value;
            float h2 = Mathf.Repeat(h + Rnd(0.35f, 0.65f), 1f);
            if (UnityEngine.Random.value < 0.15f)
            {
                st.bg = Color.HSVToRGB(h, Rnd(0.03f, 0.1f), Rnd(0.9f, 0.97f));
                st.button = Color.HSVToRGB(h, Rnd(0.1f, 0.25f), Rnd(0.7f, 0.82f));
                st.accent = Color.HSVToRGB(h2, Rnd(0.7f, 0.95f), Rnd(0.6f, 0.85f));
                st.text = Color.HSVToRGB(h, 0.2f, 0.1f);
            }
            else
            {
                st.bg = Color.HSVToRGB(h, Rnd(0.35f, 0.7f), Rnd(0.07f, 0.16f));
                st.button = Color.HSVToRGB(h, Rnd(0.3f, 0.55f), Rnd(0.25f, 0.4f));
                st.accent = Color.HSVToRGB(h2, Rnd(0.6f, 0.95f), 1f);
                st.text = Color.white;
            }
            Touch();
        }

        // ---- sélecteur : anneau de teinte + carré saturation / luminosité + champs HEX, RGB et HSV
        static void SetFld(Ar6dofVrTextField f, string t)
        {
            if (f != null && Ar6dofVrTextField.Current != f) f.SetText(t);
        }

        void ApplyHex(string t)
        {
            if (string.IsNullOrEmpty(t)) return;
            t = t.Trim();
            if (t.StartsWith("#")) t = t.Substring(1);
            if (t.Length != 6 && t.Length != 3) return;
            Color c;
            if (!ColorUtility.TryParseHtmlString("#" + t, out c)) return;
            SetC(colorSlot, c);
        }

        void ApplyRgb(int ch, string t)
        {
            int v;
            if (!int.TryParse((t ?? "").Trim(), out v)) return;
            v = Mathf.Clamp(v, 0, 255);
            Color c = GetC(colorSlot);
            if (ch == 0) c.r = v / 255f; else if (ch == 1) c.g = v / 255f; else c.b = v / 255f;
            SetC(colorSlot, c);
        }

        void ApplyHsvField(int k, string t)
        {
            float v;
            if (!float.TryParse((t ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return;
            if (k == 0) hue = Mathf.Repeat(v, 360f) / 360f;
            else if (k == 1) sat = Mathf.Clamp01(v / 100f);
            else val = Mathf.Clamp01(v / 100f);
            SetHsv();
        }

        Ar6dofVrTextField ColorField(RectTransform box, float gx, float y, string label, Action<string> onValue)
        {
            Ar6dofVrUi.Label(box, label, new Vector2(gx + 12f, y), new Vector2(26f, 30f), 20, TextAnchor.MiddleCenter);
            return Ar6dofVrTextField.Make(box, new Vector2(gx + 65f, y), new Vector2(78f, 44f), "", "", t => onValue(t), t => { onValue(t); RefreshAll(); }, 21);
        }

        void BuildColorPicker(RectTransform box)
        {
            picker = Ar6dofVrColorPicker.Make(box, new Vector2(-175f, -95f), 300f, h => { hue = h; SetHsv(); }, (sa, va) => { sat = sa; val = va; SetHsv(); });

            var pv = Ar6dofVrUi.Rect(box, "Apercu", new Vector2(160f, 22f), new Vector2(300f, 44f));
            preview = pv.gameObject.AddComponent<Image>();
            preview.raycastTarget = false;
            Ar6dofTheme.SetRounded(preview, 10);

            Ar6dofVrUi.Label(box, "HEX", new Vector2(48f, -40f), new Vector2(70f, 30f), 20, TextAnchor.MiddleLeft);
            fHex = Ar6dofVrTextField.Make(box, new Vector2(215f, -40f), new Vector2(190f, 44f), "", "#RRGGBB", t => ApplyHex(t), t => { ApplyHex(t); RefreshAll(); }, 22);

            string[] rgbN = { "R", "G", "B" }, hsvN = { "H", "S", "V" };
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                float gx = 10f + 100f * k;
                fRgb[k] = ColorField(box, gx, -100f, rgbN[k], t => ApplyRgb(kk, t));
                fHsv[k] = ColorField(box, gx, -160f, hsvN[k], t => ApplyHsvField(kk, t));
            }
            Btn(box, 160f, -222f, 300f, "Couleur aleatoire", RandomColor, 20, 44f);

            Ar6dofVrUi.Label(box, "Palettes", new Vector2(-250f, -300f), new Vector2(200f, 28f), 18, TextAnchor.MiddleLeft);
            Btn(box, 150f, -300f, 320f, "Palette aleatoire", RandomPalette, 18, 40f);
            for (int i = 0; i < PalNames.Length; i++)
            {
                int pi = i;
                var b = Btn(box, -234f + 156f * (i % 4), -360f - 54f * (i / 4), 150f, PalNames[i], () => ApplyPalette(pi), 17, 46f);
                // échantillon : carré de la couleur de fond avec un rond d'accent
                var sw = Ar6dofVrUi.Rect(b.transform, "Echantillon", new Vector2(-52f, 0f), new Vector2(28f, 28f));
                var si = sw.gameObject.AddComponent<Image>();
                si.color = Hex(PalCols[i, 0]);
                si.raycastTarget = false;
                Ar6dofTheme.SetRounded(si, 6);
                var ac = Ar6dofVrUi.Rect(sw, "Accent", Vector2.zero, new Vector2(14f, 14f));
                var ai = ac.gameObject.AddComponent<Image>();
                ai.color = Hex(PalCols[i, 2]);
                ai.raycastTarget = false;
                Ar6dofTheme.SetRounded(ai, 100);
                var lbl = b.GetComponentInChildren<Text>();
                if (lbl != null) lbl.rectTransform.offsetMin = new Vector2(24f, 0f);
            }
            refreshers.Add(RefreshPicker);
        }

        void RefreshPicker()
        {
            if (picker == null) return;
            Color c = GetC(colorSlot);
            c.a = 1f;
            picker.SetHsv(hue, sat, val);
            preview.color = c;
            SetFld(fHex, "#" + ColorUtility.ToHtmlStringRGB(c));
            SetFld(fRgb[0], Mathf.RoundToInt(c.r * 255f).ToString());
            SetFld(fRgb[1], Mathf.RoundToInt(c.g * 255f).ToString());
            SetFld(fRgb[2], Mathf.RoundToInt(c.b * 255f).ToString());
            SetFld(fHsv[0], Mathf.RoundToInt(hue * 360f).ToString());
            SetFld(fHsv[1], Mathf.RoundToInt(sat * 100f).ToString());
            SetFld(fHsv[2], Mathf.RoundToInt(val * 100f).ToString());
        }

        void RefreshStyleUi()
        {
            for (int i = 0; i < elemButtons.Count; i++) Ar6dofTheme.SetSelected(elemButtons[i], Elems[i] == selElem);
            for (int i = 0; i < subButtons.Count; i++) Ar6dofTheme.SetSelected(subButtons[i], i == styleSub);
            for (int i = 0; i < chipButtons.Count; i++)
            {
                Ar6dofTheme.SetSelected(chipButtons[i], i == colorSlot);
                Color c = GetC(i); c.a = 1f;
                chipSwatches[i].color = c;
            }
            var es = Ar6dofSettings.ElemOf(selElem);
            bool separate = es != null && es.separate;
            if (scopeLbl != null)
                scopeLbl.text = Ar6dofLoc.T(selElem == Ar6dofElem.Global ? "Style commun : s'applique a tout" : separate ? "Style propre a cet element" : "Modifie le style commun (tout le reste le suit)");
            bool win = styleSub == 2;
            foreach (var eb in elemButtons) eb.gameObject.SetActive(!win);
            scopeLbl.gameObject.SetActive(!win);
            separateBox.gameObject.SetActive(!win && selElem != Ar6dofElem.Global);
            colorsBox.gameObject.SetActive(styleSub == 0);
            formBox.gameObject.SetActive(styleSub == 1);
            windowsBox.gameObject.SetActive(win);
            for (int i = 0; i < chromeStyleButtons.Count; i++) Ar6dofTheme.SetSelected(chromeStyleButtons[i], Ar6dofSettings.Current.chromeStyle == i);
            for (int i = 0; i < chromeCornerButtons.Count; i++) Ar6dofTheme.SetSelected(chromeCornerButtons[i], Ar6dofSettings.Current.chromeCorner == i);
            shapeBox.gameObject.SetActive(selElem != Ar6dofElem.Laser);
            laserBox.gameObject.SetActive(selElem == Ar6dofElem.Laser);
            texBox.gameObject.SetActive(selElem == Ar6dofElem.Global || selElem == Ar6dofElem.Dock || selElem == Ar6dofElem.Menus);
            if (texNameLbl != null) texNameLbl.text = Ar6dofTheme.TextureLabel(CurStyle(selElem).texture ?? "");
        }

        // ================================================================== DOCK
        void BuildDock(RectTransform page)
        {
            float y = TopY;
            Toggle(page, 0f, y, "Dock aux pieds (cache, apparait en baissant la tete)", () => Ar6dofSettings.Current.dockAtFeet, v => { Ar6dofSettings.Current.dockAtFeet = v; Touch(); }, 600f); y -= 78f;
            Slider(page, ref y, "Distance devant les pieds (m)", 0.3f, 1.4f, () => Ar6dofSettings.Current.dockDistance, v => { Ar6dofSettings.Current.dockDistance = v; Touch(); }, "0.00");
            Slider(page, ref y, "Debut d'apparition : tete baissee de (deg)", 5f, 70f, () => Ar6dofSettings.Current.dockRevealStart, v => { Ar6dofSettings.Current.dockRevealStart = v; Touch(); }, "0");
            Slider(page, ref y, "Pleinement visible a (deg)", 15f, 85f, () => Ar6dofSettings.Current.dockRevealFull, v => { Ar6dofSettings.Current.dockRevealFull = v; Touch(); }, "0");
            Slider(page, ref y, "Taille du dock", 0.5f, 2f, () => Ar6dofSettings.Current.dockScale, v => { Ar6dofSettings.Current.dockScale = v; Touch(); }, "0.00");
            Slider(page, ref y, "Inclinaison supplementaire (deg)", -30f, 30f, () => Ar6dofSettings.Current.dockTilt, v => { Ar6dofSettings.Current.dockTilt = v; Touch(); }, "0");
            Slider(page, ref y, "Taille du clavier", 0.5f, 2.5f, () => Ar6dofSettings.Current.keyboardScale, v => { Ar6dofSettings.Current.keyboardScale = v; Touch(); }, "0.00");
        }

        // ================================================================== LANGUE
        void BuildLanguage(RectTransform page)
        {
            float y = TopY;
            Ar6dofVrUi.Label(page, "Langue de l'interface", new Vector2(0f, y), new Vector2(W - 60f, 34f), 24, TextAnchor.MiddleLeft); y -= 60f;
            langCodes.Clear();
            langCodes.Add("");
            langButtons.Add(Btn(page, 0f, y, 520f, "Automatique (langue de Windows)", () => { Ar6dofLoc.SetLanguage(""); }, 22));
            y -= 62f;
            var langs = Ar6dofLoc.Languages();
            for (int i = 0; i < langs.Count; i++)
            {
                string code = langs[i].Key;
                float x = (i % 2 == 0) ? -150f : 150f;
                langCodes.Add(code);
                langButtons.Add(Btn(page, x, y, 280f, langs[i].Value, () => { Ar6dofLoc.SetLanguage(code); }, 22));
                if (i % 2 == 1 || i == langs.Count - 1) y -= 62f;
            }
            y -= 20f;
            Ar6dofVrUi.Label(page, "Ajouter ou corriger une langue : fichier texte dans\n%APPDATA%\\AR6DoF\\lang (voir le LISEZMOI),\nune ligne par texte (texte francais, fleche, traduction).", new Vector2(0f, y - 30f), new Vector2(W - 60f, 110f), 19, TextAnchor.UpperLeft);
            refreshers.Add(() =>
            {
                string cur = Ar6dofSettings.Current.language ?? "";
                for (int i = 0; i < langButtons.Count; i++) Ar6dofTheme.SetSelected(langButtons[i], langCodes[i] == cur);
            });
        }
    }
}
