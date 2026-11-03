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
    ///
    /// Mise en page : un bandeau fixe en haut (titre + onglets), un bandeau fixe en bas (état + Enregistrer / Annuler / Par defaut), et entre les
    /// deux une page qui défile, faite de CADRES : chaque cadre regroupe les réglages qui vont ensemble sous un titre, et les cadres sont séparés
    /// par du vide. Les éléments sont posés les uns sous les autres par un curseur (Next) : plus de coordonnées écrites à la main qui se chevauchent.
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
        // Les noms de la rangée « Appliquer a » sont différents de ceux des onglets (Dock, Ecrans) et de la rangée de catégories (Clavier) pour ne pas se mélanger.
        static readonly Ar6dofElem[] Elems = { Ar6dofElem.Global, Ar6dofElem.Dock, Ar6dofElem.Menus, Ar6dofElem.Keyboard, Ar6dofElem.Screens, Ar6dofElem.Laser };
        static readonly string[] ElemNames = { "Tout", "Barre", "Menus", "Touches", "Cadres", "Rayon" };
        Ar6dofElem selElem = Ar6dofElem.Global;
        int colorSlot = 2;          // 0 fond, 1 boutons, 2 accent, 3 texte
        int styleSub;               // 0 couleurs, 1 forme, 2 fenetres, 3 clavier
        float hue, sat, val = 1f;
        readonly List<Button> elemButtons = new List<Button>();
        readonly List<Button> chipButtons = new List<Button>();
        readonly List<Image> chipSwatches = new List<Image>();
        readonly List<Button> subButtons = new List<Button>();
        readonly List<Button> chromeStyleButtons = new List<Button>();
        readonly List<Button> chromeCornerButtons = new List<Button>();
        readonly List<Button> fpsBtns = new List<Button>();
        RectTransform separateBox;
        readonly List<Button> kbLayoutBtns = new List<Button>();
        readonly List<Button> kbModeBtns = new List<Button>();
        readonly List<Button> kbDirBtns = new List<Button>();
        readonly List<Button> kbPalBtns = new List<Button>();
        readonly List<Texture2D> kbPalTex = new List<Texture2D>();
        readonly bool[] kbPalDrawn = new bool[16];
        Text scopeLbl, texNameLbl, palCurLbl;
        readonly List<Button> palBtns = new List<Button>();
        readonly List<Button> texModeBtns = new List<Button>();
        static readonly int[] TexModeOrder = { 0, 4, 3, 1, 2 };                       // ordre d'affichage -> valeur de texMode
        static readonly string[] TexModeNames = { "Etirer", "Remplir", "Centree", "Repeter", "Miroir" };
        Sec customSec;
        bool styleBuilt;
        string newStyleName = "";

        /// <summary>Un groupe de réglages de l'onglet Style : posé sous le précédent, visible selon la catégorie et l'élément choisis.</summary>
        class Sec
        {
            public RectTransform rt;
            public float h;
            public int sub;
            public Func<bool> vis;
        }
        readonly List<Sec> secs = new List<Sec>();
        Sec scopeSec;
        float styleBottom = -500f;

        // onglet Optique
        int optEye;                 // 0 gauche, 1 droit (quand les yeux ne sont pas liés)
        readonly List<Button> eyeButtons = new List<Button>();
        RectTransform eyeSelBox;
        Text eyeHint;

        // onglet Système : calibration de la télécommande en un seul bouton, avec compte à rebours (pour avoir le temps de poser la manette)
        Text calMsg, calBtnText;
        string calMsgFr = "";
        int calStep;                // 0 = rien, 1 ou 2 = étape en attente
        float calAt;
        const float CalDelay = 3f;

        // défilement des pages
        readonly List<GameObject> views = new List<GameObject>();
        readonly List<Ar6dofVrScrollArea> scrolls = new List<Ar6dofVrScrollArea>();
        readonly float[] bottoms = new float[8];     // bas du dernier cadre de chaque onglet (ordonnée dans la page)
        Button scrollUp, scrollDown;
        GameObject scrollBarGo;
        RectTransform scrollHandle;

        // sélecteur de couleur
        Ar6dofVrColorPicker picker;
        Image preview;
        Ar6dofVrTextField fHex;
        readonly Ar6dofVrTextField[] fRgb = new Ar6dofVrTextField[3];
        readonly Ar6dofVrTextField[] fHsv = new Ar6dofVrTextField[3];

        // onglet Langue
        readonly List<Button> langButtons = new List<Button>();
        readonly List<string> langCodes = new List<string>();

        const float W = 780f, H = 940f;
        const float CW = 648f;                  // largeur utile des réglages (bord gauche -324, bord droit +324)
        const float CardW = CW + 32f;           // largeur d'un cadre
        const float PageH = H - 250f;
        const float VisTop = PageH * 0.5f;      // haut de la zone qui défile
        const float Start = VisTop - 16f;       // haut du premier cadre
        const float ScrollX = W * 0.5f - 24f;   // colonne de la barre de défilement : à droite des cadres

        // colonnes : 2 colonnes (largeur 320), 3 colonnes (208), 4 colonnes (156), 6 colonnes (102)
        static float Col3(int i) { return (i - 1) * 220f; }
        static float Col4(int i) { return (i - 1.5f) * 162f; }
        static float Col6(int i) { return (i - 2.5f) * 109.2f; }

        public Ar6dofVrSettings(Ar6dofDock d)
        {
            dock = d;
            canvas = Ar6dofVrUi.NewCanvas("Reglages VR", new Vector2(W, H));
            // bandeaux fixes (haut et bas) : posés en premier, donc dessinés derrière tout le reste
            Band(new Vector2(0f, 400f), new Vector2(W - 24f, 116f));   // 12 px de marge partout : le rayon du bandeau = celui du panneau - 12 (concentrique)
            Band(new Vector2(0f, -400f), new Vector2(W - 24f, 116f));
            Ar6dofVrUi.Label(canvas.transform, "Reglages", new Vector2(0f, 433f), new Vector2(CW, 50f), 34);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int idx = i;
                Text t;
                var b = Ar6dofVrUi.MakeButton(canvas.transform, TabNames[i], new Vector2((i - (TabNames.Length - 1) * 0.5f) * 96f, 380f), new Vector2(92f, 46f), () => ShowTab(idx), out t, 18);
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

        /// <summary>Bandeau de fond (haut ou bas du panneau) : un carré légèrement différent du fond qui sépare nettement les commandes fixes de la page qui défile.</summary>
        void Band(Vector2 pos, Vector2 size)
        {
            var rt = Ar6dofVrUi.Rect(canvas.transform, "Bandeau", pos, size);
            var im = rt.gameObject.AddComponent<Image>();
            im.raycastTarget = false;
            Ar6dofTheme.Attach(rt.gameObject, Ar6dofRole.Band, 18);
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
        /// <summary>Hauteur à découvrir en plus pour un onglet (0 = tout tient). Calculée d'après le bas du dernier cadre.</summary>
        float MaxScroll(int tab)
        {
            float bottom = tab == TabStyle ? styleBottom : bottoms[tab];
            return Mathf.Max(0f, -bottom - VisTop + 16f);
        }

        /// <summary>Note où finit le contenu d'un onglet (c = curseur après le dernier cadre).</summary>
        void Finish(int tab, float c) { bottoms[tab] = c + 18f; }

        void ScrollBy(float d) { if (curTab < scrolls.Count) scrolls[curTab].By(d); }

        const float BarH = PageH - 96f;     // hauteur de la piste de défilement

        void BuildScrollControls()
        {
            float x = ScrollX, top = PageH * 0.5f;
            Text t;
            scrollUp = Ar6dofVrUi.MakeButton(canvas.transform, "^", new Vector2(x, top - 22f), new Vector2(34f, 34f), () => ScrollBy(-150f), out t, 22);
            scrollDown = Ar6dofVrUi.MakeButton(canvas.transform, "v", new Vector2(x, -top + 22f), new Vector2(34f, 34f), () => ScrollBy(150f), out t, 22);
            // zone de visée de la barre (assez large pour être visée avec le laser) ; le trait fin et la poignée sont dessinés dedans
            var hit = Ar6dofVrUi.Rect(canvas.transform, "BarreDefilement", new Vector2(x, 0f), new Vector2(28f, BarH));
            var hi = hit.gameObject.AddComponent<Image>();
            hi.color = new Color(0f, 0f, 0f, 0f);
            hi.raycastTarget = true;
            var tr = Ar6dofVrUi.Rect(hit, "Piste", Vector2.zero, new Vector2(8f, BarH));
            var ti = tr.gameObject.AddComponent<Image>();
            ti.raycastTarget = false;
            Ar6dofTheme.Attach(tr.gameObject, Ar6dofRole.Track, 4);
            var hd = Ar6dofVrUi.Rect(hit, "Poignee", Vector2.zero, new Vector2(16f, 40f));
            var hdi = hd.gameObject.AddComponent<Image>();
            hdi.raycastTarget = false;
            Ar6dofTheme.Attach(hd.gameObject, Ar6dofRole.Accent, 7);
            scrollHandle = hd;
            scrollBarGo = hit.gameObject;
            var sb = hit.gameObject.AddComponent<Ar6dofScrollBar>();
            sb.handle = hd;
            sb.setFraction = f => { if (curTab < scrolls.Count) scrolls[curTab].SetOffset(f * scrolls[curTab].Max); };
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
            scrollBarGo.SetActive(on);
            if (!on) return;
            float hh = Mathf.Clamp(BarH * PageH / (PageH + max), 40f, BarH);
            scrollHandle.sizeDelta = new Vector2(16f, hh);
            float frac = Mathf.Clamp01(sa.offset / max);
            scrollHandle.anchoredPosition = new Vector2(0f, (BarH - hh) * 0.5f - frac * (BarH - hh));
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
            // calibration de la télécommande : étape 1 puis étape 2, chacune après un compte à rebours
            if (calStep != 0)
            {
                if (now >= calAt)
                {
                    var r = Remote;
                    if (r == null) { calMsgFr = "telecommande introuvable"; calStep = 0; }
                    else if (calStep == 1)
                    {
                        calMsgFr = r.CalibrateStep1();
                        if (r.CalStep1Done) { calMsgFr = ""; calStep = 2; calAt = now + CalDelay + 1f; }   // étape 1 réussie : on enchaîne, avec un peu plus de temps pour relever la pointe
                        else calStep = 0;
                    }
                    else { calMsgFr = r.CalibrateStep2(); calStep = 0; }
                    UpdateCalBtn();
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
            if (i == TabStyle && styleSub == 1) Ar6dofTheme.RescanPictures();   // la liste d'images n'est relue qu'en entrant ici
            for (int k = 0; k < pages.Count; k++)
            {
                views[k].SetActive(k == i);
                if (k != i) scrolls[k].SetOffset(0f);
                Ar6dofTheme.SetSelected(tabButtons[k], k == i);
            }
            RefreshAll();
        }

        // ---- éléments ----
        static RectTransform NewBox(RectTransform parent, string name)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var rt = (RectTransform)g.transform;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Réserve une ligne de hauteur h sous le curseur c et renvoie son centre ; le curseur descend de h + gap.
        /// c = ordonnée du haut de la prochaine ligne.
        /// </summary>
        static float Next(ref float c, float h, float gap = 12f)
        {
            float yc = c - h * 0.5f;
            c -= h + gap;
            return yc;
        }

        /// <summary>Commence un cadre au curseur c : réserve la marge du haut et le titre (accent). Renvoie le haut du cadre pour CloseCard.</summary>
        float OpenCard(RectTransform p, ref float c, string title)
        {
            float top = c;
            c -= 16f;
            if (!string.IsNullOrEmpty(title))
            {
                Heading(p, c - 17f, title);
                c -= 34f + 12f;
            }
            return top;
        }

        /// <summary>Termine le cadre commencé en 'top' : dessine son fond DERRIÈRE son contenu et laisse du vide avant le suivant.</summary>
        void CloseCard(RectTransform p, float top, ref float c)
        {
            float bottom = c - 6f;   // le dernier élément a déjà laissé son espace
            var rt = Ar6dofVrUi.Rect(p, "Cadre", new Vector2(0f, (top + bottom) * 0.5f), new Vector2(CardW, top - bottom));
            var im = rt.gameObject.AddComponent<Image>();
            im.raycastTarget = false;
            Ar6dofTheme.Attach(rt.gameObject, Ar6dofRole.Band, 18);
            rt.SetAsFirstSibling();
            c = bottom - 18f;
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

        /// <summary>Un curseur sur toute la largeur, posé sous le curseur c.</summary>
        void SliderRow(RectTransform page, ref float c, string label, float min, float max, Func<float> get, Action<float> set, string fmt = "0.##")
        {
            SliderAt(page, 0f, Next(ref c, 74f, 10f), CW, label, min, max, get, set, fmt);
        }

        /// <summary>Case à cocher : un petit carré SUR LE CÔTÉ, le texte à droite du carré (aligné à gauche, jamais dans le carré).</summary>
        void Toggle(RectTransform page, float x, float y, string label, Func<bool> get, Action<bool> set, float width = 290f)
        {
            if (Mathf.Abs(x) > 100f) { width = 320f; x = Mathf.Sign(x) * 164f; }                          // deux colonnes : bords à -324 et +324
            else { width = Mathf.Min(width, CW); x = -CW * 0.5f + width * 0.5f; }                          // seul sur sa ligne : collé au bord gauche, comme les titres
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

        Button Btn(RectTransform page, float x, float y, float w, string label, Action click, int fs = 22, float h = 48f)
        {
            Text t;
            if (Mathf.Abs(x) > 170f && Mathf.Abs(x) < 190f && w > 330f && w < 350f) { x = Mathf.Sign(x) * 164f; w = 320f; }   // paires gauche / droite : mêmes bords que le reste
            return Ar6dofVrUi.MakeButton(page, label, new Vector2(x, y), new Vector2(w, h), () => click(), out t, fs);
        }

        Text ButtonText(RectTransform page, float x, float y, float w, string label, Action click, int fs = 22)
        {
            Text t;
            if (Mathf.Abs(x) > 170f && Mathf.Abs(x) < 190f && w > 330f && w < 350f) { x = Mathf.Sign(x) * 164f; w = 320f; }
            Ar6dofVrUi.MakeButton(page, label, new Vector2(x, y), new Vector2(w, 48f), () => click(), out t, fs);
            return t;
        }

        /// <summary>Ligne d'information (texte aligné à gauche) qui se met à jour toute seule.</summary>
        Text InfoLine(RectTransform page, float y, int tab, Func<string> text, int fs = 20)
        {
            Text l = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(CW, 28f), fs, TextAnchor.MiddleLeft);
            Live(tab, () => l.text = text());
            return l;
        }

        /// <summary>Paragraphe qui revient à la ligne, de hauteur h, posé sous le curseur c.</summary>
        Text Para(RectTransform p, ref float c, string text, float h, int fs = 19)
        {
            var l = Ar6dofVrUi.Label(p, text, new Vector2(0f, Next(ref c, h, 12f)), new Vector2(CW, h), fs, TextAnchor.UpperLeft);
            l.horizontalOverflow = HorizontalWrapMode.Wrap;
            return l;
        }

        Text Heading(RectTransform page, float y, string text)
        {
            return Ar6dofVrUi.Label(page, text, new Vector2(0f, y), new Vector2(CW, 34f), 24, TextAnchor.MiddleLeft).MakeAccent();
        }

        static void Touch() { Ar6dofSettings.Notify(); }

        // ---- bas du panneau : Enregistrer / Annuler / Par defaut ----
        void BuildBottom()
        {
            float by = -420f;
            Text t;
            statusLbl = Ar6dofVrUi.Label(canvas.transform, "", new Vector2(0f, -364f), new Vector2(CW, 28f), 19);
            saveBtn = Ar6dofVrUi.MakeButton(canvas.transform, "Enregistrer", new Vector2(-224f, by), new Vector2(208f, 56f), OnSave, out t, 22);
            Ar6dofVrUi.MakeButton(canvas.transform, "Annuler", new Vector2(0f, by), new Vector2(208f, 56f), OnCancel, out t, 22);
            Ar6dofVrUi.MakeButton(canvas.transform, "Par defaut", new Vector2(224f, by), new Vector2(208f, 56f), OnReset, out resetLbl, 22);
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
            float c = Start;
            if (pt == null)
            {
                Ar6dofVrUi.Label(page, "Passthrough introuvable dans la scene", new Vector2(0f, c - 30f), new Vector2(CW, 40f), 24);
                Finish(TabImage, c - 60f);
                return;
            }
            // état de l'image (avant : panneau F1)
            float top = OpenCard(page, ref c, "Etat de l'image");
            InfoLine(page, Next(ref c, 28f, 4f), TabImage, () => pt.SourceOpen
                ? Ar6dofLoc.F("Source : {0} x {1}   -   {2} img/s   -   age {3} ms", pt.SourceWidth, pt.SourceHeight, pt.UploadsPerSec, pt.FrameAgeMs.ToString("0"))
                : Ar6dofLoc.T("Source : camera non ouverte"), 19);
            InfoLine(page, Next(ref c, 28f, 4f), TabImage, () => Ar6dofLoc.F("Envoi GPU : {0} ms   -   reprojection {1} deg   -   delai {2} ms",
                pt.UploadMs.ToString("0.0"), pt.ReprojDeg.ToString("0.0"), pt.DelayUsedMs.ToString("0")), 19);
            InfoLine(page, Next(ref c, 28f, 12f), TabImage, () => Ar6dofLoc.T(pt.HasCalibration ? "Calibration camera : chargee" : "Calibration camera : ABSENTE (camera_model.json)"), 19);
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Affichage");
            SliderRow(page, ref c, "FOV affiche (deg)", 40f, 130f, () => pt.settings.displayFovDeg, v => pt.settings.displayFovDeg = v, "0");
            SliderRow(page, ref c, "IPD logiciel (ecarte +)", -0.4f, 0.4f, () => pt.settings.ipdShift, v => pt.settings.ipdShift = v, "0.00");
            SliderRow(page, ref c, "Delai manuel (ms)", 0f, 120f, () => pt.settings.captureDelayMs, v => pt.settings.captureDelayMs = v, "0");
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Options");
            float y = Next(ref c, 48f, 10f);
            Toggle(page, -164f, y, "Calibration camera", () => pt.settings.useCalibration, v => pt.settings.useCalibration = v);
            Toggle(page, 164f, y, "Reprojection", () => pt.settings.reproject, v => pt.settings.reproject = v);
            y = Next(ref c, 48f, 10f);
            Toggle(page, -164f, y, "Delai auto", () => pt.settings.autoDelay, v => pt.settings.autoDelay = v);
            Toggle(page, 164f, y, "Etirer les bords", () => pt.settings.clampEdges, v => pt.settings.clampEdges = v);
            y = Next(ref c, 48f, 16f);
            Toggle(page, -164f, y, "Rendu a l'envers", () => pt.settings.flipComposite, v => pt.settings.flipComposite = v);
            Btn(page, 164f, y, 320f, "Sauver ces reglages d'image", () => { pt.SaveSettings(); Say("Reglages d'image sauves"); }, 20);
            CloseCard(page, top, ref c);
            Finish(TabImage, c);
        }

        // ================================================================== OPTIQUE
        Ar6dofEyeSettings Eye(Ar6dofPassthrough pt)
        {
            return (!pt.settings.linkEyes && optEye == 1) ? pt.settings.right : pt.settings.left;
        }

        void BuildOptics(RectTransform page)
        {
            var pt = dock.passthrough;
            float c = Start;
            if (pt == null)
            {
                Ar6dofVrUi.Label(page, "Passthrough introuvable dans la scene", new Vector2(0f, c - 30f), new Vector2(CW, 40f), 24);
                Finish(TabOptics, c - 60f);
                return;
            }
            const float cx = 164f, cw = 320f;

            float top = OpenCard(page, ref c, "Yeux");
            float y = Next(ref c, 48f, 10f);
            Toggle(page, -cx, y, "Yeux lies (droit = miroir du gauche)", () => pt.settings.linkEyes, v => pt.settings.linkEyes = v);
            Toggle(page, cx, y, "Echanger G / D", () => pt.settings.swapEyes, v => pt.settings.swapEyes = v);
            // quel oeil : visible seulement quand les yeux ne sont pas liés (sinon une phrase explique que tout est commun)
            y = Next(ref c, 48f, 12f);
            eyeSelBox = NewBox(page, "Oeil");
            eyeButtons.Add(Btn(eyeSelBox, -cx, y, cw, "Oeil gauche", () => { optEye = 0; RefreshAll(); }, 22));
            eyeButtons.Add(Btn(eyeSelBox, cx, y, cw, "Oeil droit", () => { optEye = 1; RefreshAll(); }, 22));
            eyeHint = Ar6dofVrUi.Label(page, "Les reglages ci-dessous s'appliquent aux deux yeux", new Vector2(0f, y), new Vector2(CW, 30f), 19, TextAnchor.MiddleLeft);
            refreshers.Add(() =>
            {
                bool linked = pt.settings.linkEyes;
                eyeSelBox.gameObject.SetActive(!linked);
                eyeHint.gameObject.SetActive(linked);
                for (int i = 0; i < eyeButtons.Count; i++) Ar6dofTheme.SetSelected(eyeButtons[i], i == optEye);
            });
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Position de l'image");
            y = Next(ref c, 74f, 10f);
            SliderAt(page, -cx, y, cw, "Decalage horizontal", -0.6f, 0.6f, () => Eye(pt).panX, v => Eye(pt).panX = v, "0.00");
            SliderAt(page, cx, y, cw, "Decalage vertical", -0.6f, 0.6f, () => Eye(pt).panY, v => Eye(pt).panY = v, "0.00");
            y = Next(ref c, 74f, 10f);
            SliderAt(page, -cx, y, cw, "Centre lentille X", -0.5f, 0.5f, () => Eye(pt).lensCenterX, v => Eye(pt).lensCenterX = v, "0.000");
            SliderAt(page, cx, y, cw, "Centre lentille Y", -0.5f, 0.5f, () => Eye(pt).lensCenterY, v => Eye(pt).lensCenterY = v, "0.000");
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Distorsion de la lentille");
            y = Next(ref c, 74f, 10f);
            SliderAt(page, -cx, y, cw, "Distorsion k1", -0.8f, 0.8f, () => Eye(pt).k1, v => Eye(pt).k1 = v, "0.000");
            SliderAt(page, cx, y, cw, "Distorsion k2", -0.8f, 0.8f, () => Eye(pt).k2, v => Eye(pt).k2 = v, "0.000");
            y = Next(ref c, 74f, 10f);
            SliderAt(page, -cx, y, cw, "Distorsion k3", -0.8f, 0.8f, () => Eye(pt).k3, v => Eye(pt).k3 = v, "0.000");
            Text rot = ButtonText(page, cx, y - 8f, cw, "", () => { var e = Eye(pt); e.rotateQuarterTurns = (e.rotateQuarterTurns + 1) & 3; RefreshAll(); }, 20);
            refreshers.Add(() => rot.text = Ar6dofLoc.F("Rotation du flux : {0} deg", (Eye(pt).rotateQuarterTurns & 3) * 90));
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Flux de la camera");
            y = Next(ref c, 48f, 10f);
            Toggle(page, -cx, y, "Miroir horizontal", () => Eye(pt).mirrorX, v => Eye(pt).mirrorX = v);
            Toggle(page, cx, y, "Miroir vertical", () => Eye(pt).mirrorY, v => Eye(pt).mirrorY = v);
            y = Next(ref c, 48f, 10f);
            Text lay = ButtonText(page, -cx, y, cw, "", () => { pt.settings.layout = pt.settings.layout == Ar6dofStereoLayout.SideBySide ? Ar6dofStereoLayout.TopBottom : Ar6dofStereoLayout.SideBySide; RefreshAll(); }, 20);
            refreshers.Add(() => lay.text = Ar6dofLoc.T(pt.settings.layout == Ar6dofStereoLayout.SideBySide ? "Flux : cote a cote" : "Flux : haut / bas"));
            Text fov = ButtonText(page, cx, y, cw, "", () => { pt.settings.displayFovDeg = Mathf.Clamp(pt.MaxFovNoBlackDeg, 40f, 130f); RefreshAll(); }, 20);
            refreshers.Add(() => fov.text = Ar6dofLoc.F("FOV max sans bord noir : {0} deg", pt.MaxFovNoBlackDeg.ToString("0")));
            y = Next(ref c, 48f, 10f);
            Toggle(page, 0f, y, "Recentrer sur le milieu de l'image (decale tout : deconseille)", () => pt.settings.centerOnImage, v => pt.settings.centerOnImage = v, 640f);
            CloseCard(page, top, ref c);

            // fichier de réglages : 3 actions groupées sous les cadres
            y = Next(ref c, 48f, 12f);
            Btn(page, Col3(0), y, 208f, "Recharger", () => { pt.ReloadSettings(); Say("Reglages d'image recharges"); RefreshAll(); }, 20);
            Btn(page, Col3(1), y, 208f, "Sauver", () => { pt.SaveSettings(); Say("Reglages d'image sauves"); }, 20);
            Btn(page, Col3(2), y, 208f, "Remettre a zero", () => { pt.ResetSettings(); Say("Reglages d'image remis a zero (Sauver pour garder)"); RefreshAll(); }, 20);
            Finish(TabOptics, c);
        }

        // ================================================================== ECRANS
        void BuildScreens(RectTransform page)
        {
            float c = Start;
            float top = OpenCard(page, ref c, "Images / s de tous les ecrans");
            float y = Next(ref c, 48f, 12f);
            Btn(page, Col3(0), y, 208f, "30", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(30); });
            Btn(page, Col3(1), y, 208f, "60", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(60); });
            Btn(page, Col3(2), y, 208f, "90", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(90); });
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Disposition");
            y = Next(ref c, 48f, 12f);
            Toggle(page, -164f, y, "Coller les ecrans aux bords", () => Ar6dofScreens.Instance != null && Ar6dofScreens.Instance.snapEnabled,
                   v => { if (Ar6dofScreens.Instance != null) { Ar6dofScreens.Instance.snapEnabled = v; Ar6dofScreens.Instance.Save(); } });
            Btn(page, 164f, y, 320f, "Recentrer les ecrans", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.RecenterAll(); }, 20);
            SliderRow(page, ref c, "Angle de tous les clusters (deg)", 0f, 60f,
                      () => Ar6dofScreens.Instance != null ? Ar6dofScreens.Instance.hingeDegrees : 25f,
                      v => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllHinge(v); }, "0");
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Ecrans");
            InfoLine(page, Next(ref c, 28f, 10f), TabScreens, () => Ar6dofLoc.F("Ecrans : {0} / {1}  (clusters de 3 max)", Ar6dofScreens.Instance != null ? Ar6dofScreens.Instance.panels.Count : 0, Ar6dofScreens.Instance != null ? Ar6dofScreens.Instance.MaxAllowed : 12), 20);
            y = Next(ref c, 48f, 12f);
            Btn(page, 0f, y, 440f, "+ Ajouter un ecran", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.RequestAddScreen(); }, 22);
            CloseCard(page, top, ref c);
            Finish(TabScreens, c);
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

        /// <summary>Un seul bouton : lance la calibration (étape 1 à plat puis étape 2 pointe relevée, chacune après un compte à rebours) ou l'annule.</summary>
        void StartCal()
        {
            if (calStep != 0)
            {
                calStep = 0;
                calMsgFr = "Calibration annulee";
            }
            else
            {
                calStep = 1;
                calAt = Time.unscaledTime + CalDelay;
                calMsgFr = "";
            }
            UpdateCalMsg();
            UpdateCalBtn();
        }

        void UpdateCalBtn()
        {
            if (calBtnText != null) calBtnText.text = Ar6dofLoc.T(calStep != 0 ? "Annuler la calibration" : "Calibrer la telecommande");
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
            float c = Start;
            float top = OpenCard(page, ref c, "Suivi de la tete");
            InfoLine(page, Next(ref c, 28f, 4f), TabSystem, () =>
            {
                var rig = dock.rig;
                if (rig == null) return Ar6dofLoc.T("Tete : casque introuvable");
                if (!rig.HasPose) return Ar6dofLoc.T("Tete : en attente du casque");
                if (rig.ConnectionLost) return Ar6dofLoc.T("Tete : liaison perdue");
                return Ar6dofLoc.T(rig.TrackingOk ? "Tete : suivi OK" : "Tete : suivi degrade");
            });
            InfoLine(page, Next(ref c, 28f, 12f), TabSystem, () =>
            {
                var rig = dock.rig;
                if (rig == null || !rig.HasPose) return "";
                return Ar6dofLoc.F("Sol : {0}   -   hauteur des yeux : {1} cm", Ar6dofLoc.T(rig.FloorDetected ? "detecte" : "estime"), Mathf.RoundToInt(rig.HeadHeight * 100f));
            });
            if (dock.launcher != null)
            {
                float y = Next(ref c, 48f, 12f);
                Btn(page, Col3(0), y, 208f, "Recentrer le tracking", () => dock.launcher.Send('T'), 18);
                Btn(page, Col3(1), y, 208f, "Verrouiller le sol", () => dock.launcher.Send('V'), 18);
                Btn(page, Col3(2), y, 208f, "Remettre le cap a zero", () => dock.launcher.Send('Z'), 18);
            }
            CloseCard(page, top, ref c);

            // télécommande : état, puis UN bouton de calibration
            top = OpenCard(page, ref c, "Telecommande");
            InfoLine(page, Next(ref c, 28f, 8f), TabSystem, () =>
            {
                var r = Remote;
                if (r == null) return Ar6dofLoc.T("Telecommande : composant introuvable");
                if (!r.Connected) return Ar6dofLoc.T("Telecommande : non connectee");
                string b = (r.Left ? Ar6dofLoc.T("G") : "-") + " " + (r.Right ? Ar6dofLoc.T("D") : "-");
                return Ar6dofLoc.F("Telecommande : connectee   -   {0} paquets/s   -   boutons {1}", r.PacketsPerSec, b);
            });
            Ar6dofVrUi.Label(page, "Recentrer le rayon : maintenir les 2 boutons 0,6 s", new Vector2(0f, Next(ref c, 28f, 2f)), new Vector2(CW, 28f), 19, TextAnchor.MiddleLeft);
            Ar6dofVrUi.Label(page, "Un compte a rebours de 3 s laisse le temps de poser la telecommande", new Vector2(0f, Next(ref c, 28f, 12f)), new Vector2(CW, 28f), 19, TextAnchor.MiddleLeft);
            float cy = Next(ref c, 48f, 8f);
            var calBtn = Btn(page, 0f, cy, 440f, "Calibrer la telecommande", StartCal, 22);
            calBtnText = calBtn.GetComponentInChildren<Text>();
            calMsg = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 52f, 12f)), new Vector2(CW, 52f), 19, TextAnchor.UpperLeft);
            calMsg.horizontalOverflow = HorizontalWrapMode.Wrap;
            refreshers.Add(UpdateCalMsg);
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Filtre du rayon");
            SliderRow(page, ref c, "Stabilite (plus bas = plus stable)", 0.5f, 8f, () => Ar6dofSettings.Current.remoteMinCutoff, v => { Ar6dofSettings.Current.remoteMinCutoff = v; Touch(); }, "0.0");
            SliderRow(page, ref c, "Reactivite en mouvement", 0f, 1f, () => Ar6dofSettings.Current.remoteBeta, v => { Ar6dofSettings.Current.remoteBeta = v; Touch(); }, "0.00");
            SliderRow(page, ref c, "Anticipation (ms)", 0f, 40f, () => Ar6dofSettings.Current.remotePredictMs, v => { Ar6dofSettings.Current.remotePredictMs = v; Touch(); }, "0");
            CloseCard(page, top, ref c);

            // performances : limiter les images par seconde évite que le GPU tourne à fond pour rien
            top = OpenCard(page, ref c, "Performances (GPU)");
            Ar6dofVrUi.Label(page, "Images par seconde maximum", new Vector2(0f, Next(ref c, 28f, 8f)), new Vector2(CW, 28f), 20, TextAnchor.MiddleLeft);
            float fy = Next(ref c, 46f, 10f);
            for (int i = 0; i < Ar6dofPerf.FpsNames.Length; i++)
            {
                int fi = i;
                fpsBtns.Add(Btn(page, -259f + 129.5f * i, fy, 124f, Ar6dofPerf.FpsNames[i], () => { Ar6dofSettings.Current.perfFps = fi; Touch(); }, 18, 46f));
            }
            refreshers.Add(() => { for (int i = 0; i < fpsBtns.Count; i++) Ar6dofTheme.SetSelected(fpsBtns[i], Ar6dofSettings.Current.perfFps == i); });
            InfoLine(page, Next(ref c, 28f, 12f), TabSystem, () => Ar6dofLoc.F("Images par seconde actuelles : {0}", Ar6dofPerf.Fps));
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Lissage des bords (MSAA x2) : plus beau, plus lourd", () => Ar6dofSettings.Current.perfMsaa > 1, v => { Ar6dofSettings.Current.perfMsaa = v ? 2 : 1; Touch(); }, 640f);
            SliderRow(page, ref c, "Resolution de l'interface (au prochain lancement)", 0.5f, 1f, () => Ar6dofSettings.Current.perfRenderScale, v => { Ar6dofSettings.Current.perfRenderScale = v; Touch(); }, "0.00");
            CloseCard(page, top, ref c);
            Finish(TabSystem, c);
        }

        static string CamTxt(int state, float fps)
        {
            if (state < 0) return Ar6dofLoc.T("aucune");
            return state == 0 ? Ar6dofLoc.T("arretee") : Ar6dofLoc.F("{0} fps", Mathf.RoundToInt(fps));
        }

        void BuildEyes(RectTransform page)
        {
            var et = dock.eyes;
            float c = Start;
            if (et == null)
            {
                Ar6dofVrUi.Label(page, "Eye tracking introuvable", new Vector2(0f, c - 30f), new Vector2(CW, 40f), 24);
                Finish(TabEyes, c - 60f);
                return;
            }
            float top = OpenCard(page, ref c, "Etat");
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Eye tracking actif", () => et.Enabled, v => et.SetEnabled(v), 400f);
            Text l1 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l2 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l3 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l4 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 12f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Live(TabEyes, () =>
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
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Etalonnage");
            float y = Next(ref c, 48f, 12f);
            Btn(page, -164f, y, 320f, "Etalonner (9 points)", () =>
            {
                et.SetEnabled(true);
                if (dock.eyeCal != null) dock.eyeCal.Begin(et);
            }, 22);
            Btn(page, 164f, y, 320f, "Effacer l'etalonnage", () => et.ClearCalibration(), 22);
            y = Next(ref c, 48f, 12f);
            Btn(page, 0f, y, 440f, "Recentrer (regarde droit devant)", () => et.Recenter(), 22);
            Para(page, ref c, "Pendant l'etalonnage, garde la tete immobile et suis la cible\ndes yeux. Les donnees du regard sont lues par Unity dans\nAr6dofEyeTracker.Instance.Gaze (pour tes animations).", 100f);
            CloseCard(page, top, ref c);
            Finish(TabEyes, c);
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

        Sec NewSec(RectTransform page, string name, int sub, Func<bool> vis)
        {
            var s = new Sec { rt = NewBox(page, name), sub = sub, vis = vis };
            secs.Add(s);
            return s;
        }

        void SelectSub(int k)
        {
            styleSub = k;
            if (k == 1) Ar6dofTheme.RescanPictures();
            scrolls[TabStyle].SetOffset(0f);
            RefreshAll();
        }

        /// <summary>
        /// Onglet Style, du plus général au plus fin : 1) catégorie (Couleurs, Forme, Fenetres, Clavier) tout en haut ; 2) pour Couleurs et Forme
        /// seulement, à quoi ça s'applique (Tout ou un élément) ; 3) les cadres de réglages. Les groupes sont empilés par LayoutStyle.
        /// </summary>
        void BuildStyle(RectTransform page)
        {
            string[] cats = { "Couleurs", "Forme", "Fenetres", "Clavier" };
            for (int i = 0; i < cats.Length; i++)
            {
                int k = i;
                subButtons.Add(Btn(page, Col4(i), Start - 23f, 156f, cats[i], () => SelectSub(k), 22, 46f));
            }

            // ---- à quoi ça s'applique (Couleurs et Forme)
            scopeSec = new Sec { rt = NewBox(page, "Cible"), sub = -1 };
            {
                var b = scopeSec.rt;
                float c = 0f;
                float top = OpenCard(b, ref c, "Appliquer a");
                float y = Next(ref c, 44f, 10f);
                for (int i = 0; i < Elems.Length; i++)
                {
                    int idx = i;
                    elemButtons.Add(Btn(b, Col6(i), y, 102f, ElemNames[i], () => { selElem = Elems[idx]; RefreshAll(); }, 19, 44f));
                }
                y = Next(ref c, 48f, 10f);
                scopeLbl = Ar6dofVrUi.Label(b, "", new Vector2(0f, y), new Vector2(CW, 30f), 19, TextAnchor.MiddleLeft);
                separateBox = NewBox(b, "Separe");
                Toggle(separateBox, 0f, y, "Style a part pour cet element", () =>
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
                CloseCard(b, top, ref c);
                scopeSec.h = -c;
            }

            BuildColorSecs(page);
            BuildFormSecs(page);
            BuildWindowSecs(page);
            BuildKeyboardSecs(page);
            refreshers.Add(RefreshStyleUi);
            styleBuilt = true;
        }

        // ---- Couleurs : d'abord les palettes toutes faites (le plus simple), puis la personnalisation fine en dessous
        void BuildColorSecs(RectTransform page)
        {
            var sp = NewSec(page, "Palettes", 0, null);
            {
                float c = 0f;
                float top = OpenCard(sp.rt, ref c, null);
                float hy = Next(ref c, 40f, 6f);
                Heading(sp.rt, hy, "Palettes");
                Btn(sp.rt, 204f, hy, 240f, "Palette aleatoire", RandomPalette, 19, 40f);
                float ly = Next(ref c, 30f, 10f);
                palCurLbl = Ar6dofVrUi.Label(sp.rt, "", new Vector2(0f, ly), new Vector2(CW, 28f), 20, TextAnchor.MiddleLeft).MakeAccent();
                float y = 0f;
                for (int i = 0; i < PalNames.Length; i++)
                {
                    int pi = i;
                    if (i % 3 == 0) y = Next(ref c, 64f, 10f);
                    float x = Col3(i % 3);
                    var b = Btn(sp.rt, x, y, 208f, PalNames[i], () => ApplyPalette(pi), 20, 64f);
                    palBtns.Add(b);
                    // nom en haut à gauche, bande des 4 couleurs (fond, boutons, accent, texte) dessous : jamais l'un sur l'autre
                    var lbl = b.GetComponentInChildren<Text>();
                    Ar6dofVrUi.PlaceLabel(lbl, new Vector2(0f, 14f), new Vector2(176f, 28f), TextAnchor.MiddleLeft);
                    for (int k = 0; k < 4; k++)
                    {
                        var sw = Ar6dofVrUi.Rect(b.transform, "Couleur" + k, new Vector2(-67.5f + 45f * k, -17f), new Vector2(40f, 14f));
                        var si = sw.gameObject.AddComponent<Image>();
                        si.color = Hex(PalCols[i, k]);
                        si.raycastTarget = false;
                        Ar6dofTheme.SetRounded(si, 5);
                    }
                }
                CloseCard(sp.rt, top, ref c);
                sp.h = -c;
            }

            // ---- Mes styles : juste sous les palettes ; créer, nommer, réappliquer, supprimer
            customSec = NewSec(page, "MesStyles", 0, null);
            RebuildCustomSec();

            var cs = NewSec(page, "Personnaliser", 0, null);
            {
                float c = 0f;
                float top = OpenCard(cs.rt, ref c, "Personnaliser");
                float y = Next(ref c, 46f, 14f);
                string[] chipNames = { "Fond", "Boutons", "Accent", "Texte" };
                for (int i = 0; i < 4; i++)
                {
                    int slot = i;
                    var b = Btn(cs.rt, Col4(i), y, 156f, chipNames[i], () => { colorSlot = slot; RefreshAll(); }, 19, 46f);
                    var sw = new GameObject("Echantillon", typeof(RectTransform));
                    sw.transform.SetParent(b.transform, false);
                    var srt = (RectTransform)sw.transform;
                    srt.anchoredPosition = new Vector2(-52f, 0f);
                    srt.sizeDelta = new Vector2(26f, 26f);
                    var img = sw.AddComponent<Image>();
                    img.raycastTarget = false;
                    chipButtons.Add(b);
                    chipSwatches.Add(img);
                    // le texte se place à droite de l'échantillon, dans une zone de taille connue (jamais de largeur négative)
                    Ar6dofVrUi.PlaceLabel(b.GetComponentInChildren<Text>(), new Vector2(18f, 0f), new Vector2(104f, 40f), TextAnchor.MiddleCenter);
                }
                refreshers.Add(SyncHsv);
                BuildColorPicker(cs.rt, c);   // le sélecteur commence juste sous la rangée de choix
                c -= 312f;                    // hauteur du sélecteur (anneau de 300 px) + espace
                CloseCard(cs.rt, top, ref c);
                cs.h = -c;
            }
        }

        // ---- Forme : un cadre par réglage (selon l'élément choisi)
        void BuildFormSecs(RectTransform page)
        {
            var so = NewSec(page, "Opacite", 1, null);
            {
                float c = 0f;
                float top = OpenCard(so.rt, ref c, null);
                SliderRow(so.rt, ref c, "Opacite des fonds", 0.15f, 1f, () => CurStyle(selElem).alpha, v => { CurStyle(selElem).alpha = v; Touch(); }, "0.00");
                CloseCard(so.rt, top, ref c);
                so.h = -c;
            }
            var sh = NewSec(page, "Arrondi", 1, () => selElem != Ar6dofElem.Laser);
            {
                float c = 0f;
                float top = OpenCard(sh.rt, ref c, null);
                SliderRow(sh.rt, ref c, "Coins arrondis", 0f, 40f, () => CurStyle(selElem).corner, v => { CurStyle(selElem).corner = Mathf.RoundToInt(v); Touch(); }, "0");
                CloseCard(sh.rt, top, ref c);
                sh.h = -c;
            }
            Func<bool> texElem = () => selElem == Ar6dofElem.Global || selElem == Ar6dofElem.Dock || selElem == Ar6dofElem.Menus;
            var st = NewSec(page, "Texture", 1, texElem);
            {
                float c = 0f;
                float top = OpenCard(st.rt, ref c, "Image de fond");
                float y = Next(ref c, 48f, 10f);
                Btn(st.rt, -289f, y, 70f, "<", () => CycleTexture(-1));
                Btn(st.rt, 289f, y, 70f, ">", () => CycleTexture(1));
                texNameLbl = Ar6dofVrUi.Label(st.rt, "", new Vector2(0f, y), new Vector2(440f, 40f), 24);
                y = Next(ref c, 48f, 12f);
                Btn(st.rt, -164f, y, 320f, "Ouvrir le dossier d'images", () => Ar6dofTheme.OpenPicturesFolder(), 20);
                Btn(st.rt, 164f, y, 320f, "Actualiser la liste", () => { Ar6dofTheme.RescanPictures(); Touch(); }, 20);
                CloseCard(st.rt, top, ref c);
                st.h = -c;
            }
            Func<bool> hasTex = () => texElem() && !string.IsNullOrEmpty(CurStyle(selElem).texture);
            var sm = NewSec(page, "TexReglages", 1, hasTex);
            {
                float c = 0f;
                float top = OpenCard(sm.rt, ref c, null);
                SliderRow(sm.rt, ref c, "Opacite de l'image", 0.02f, 1f, () => CurStyle(selElem).texAlpha, v => { CurStyle(selElem).texAlpha = v; Touch(); }, "0.00");
                float y = 0f;
                for (int i = 0; i < TexModeOrder.Length; i++)
                {
                    int mode = TexModeOrder[i];
                    if (i % 3 == 0) y = Next(ref c, 48f, 10f);
                    texModeBtns.Add(Btn(sm.rt, Col3(i % 3), y, 208f, TexModeNames[i], () => { CurStyle(selElem).texMode = mode; Touch(); }, 20));
                }
                CloseCard(sm.rt, top, ref c);
                sm.h = -c;
            }
            var ss = NewSec(page, "TexEchelle", 1, () => hasTex() && CurStyle(selElem).texMode >= 1 && CurStyle(selElem).texMode <= 3);
            {
                float c = 0f;
                float top = OpenCard(ss.rt, ref c, null);
                SliderRow(ss.rt, ref c, "Echelle de l'image", 0.25f, 4f, () => CurStyle(selElem).texScale, v => { CurStyle(selElem).texScale = v; Touch(); }, "0.00");
                CloseCard(ss.rt, top, ref c);
                ss.h = -c;
            }
            var sl = NewSec(page, "Rayon", 1, () => selElem == Ar6dofElem.Laser);
            {
                float c = 0f;
                float top = OpenCard(sl.rt, ref c, null);
                SliderRow(sl.rt, ref c, "Epaisseur du rayon (mm)", 1f, 15f, () => CurStyle(Ar6dofElem.Laser).lineWidth * 1000f, v => { CurStyle(Ar6dofElem.Laser).lineWidth = v / 1000f; Touch(); }, "0.0");
                CloseCard(sl.rt, top, ref c);
                sl.h = -c;
            }
        }

        // ---- Fenetres : boutons reduire / fermer de tous les ecrans et menus
        void BuildWindowSecs(RectTransform page)
        {
            var a = NewSec(page, "FenStyle", 2, null);
            {
                float c = 0f;
                float top = OpenCard(a.rt, ref c, "Style des boutons de fenetre");
                float y = Next(ref c, 48f, 12f);
                chromeStyleButtons.Add(Btn(a.rt, -164f, y, 320f, "Windows", () => { Ar6dofSettings.Current.chromeStyle = 0; Touch(); }, 24));
                chromeStyleButtons.Add(Btn(a.rt, 164f, y, 320f, "Mac", () => { Ar6dofSettings.Current.chromeStyle = 1; Touch(); }, 24));
                CloseCard(a.rt, top, ref c);
                a.h = -c;
            }
            var b = NewSec(page, "FenPos", 2, null);
            {
                float c = 0f;
                float top = OpenCard(b.rt, ref c, "Position de Reduire / Fermer");
                string[] corners = { "Haut gauche", "Haut droite", "Bas gauche", "Bas droite" };
                float y = 0f;
                for (int i = 0; i < 4; i++)
                {
                    int ci = i;
                    if (i % 2 == 0) y = Next(ref c, 48f, 10f);
                    chromeCornerButtons.Add(Btn(b.rt, (i % 2 == 0) ? -164f : 164f, y, 320f, corners[i], () => { Ar6dofSettings.Current.chromeCorner = ci; Touch(); }, 22));
                }
                CloseCard(b.rt, top, ref c);
                b.h = -c;
            }
            var d = NewSec(page, "FenTaille", 2, null);
            {
                float c = 0f;
                float top = OpenCard(d.rt, ref c, null);
                SliderRow(d.rt, ref c, "Taille des boutons de fenetre", 0.6f, 2.5f, () => Ar6dofSettings.Current.chromeScale, v => { Ar6dofSettings.Current.chromeScale = v; Touch(); }, "0.00");
                Para(d.rt, ref c, "Valable pour tous les ecrans et tous les menus. Par defaut : Windows, en haut a droite. Le redimensionnement se fait par la poignee unique en bas a droite de chaque fenetre.", 96f);
                CloseCard(d.rt, top, ref c);
                d.h = -c;
            }
        }

        void CycleTexture(int dir)
        {
            var ids = Ar6dofTheme.TextureIds();
            var st = CurStyle(selElem);
            int i = Mathf.Max(0, ids.IndexOf(st.texture ?? ""));
            i = (i + dir + ids.Count) % ids.Count;
            st.texture = ids[i];
            if (st.texture.Length > 0 && st.texAlpha < 0.05f) st.texAlpha = 0.18f;
            // mode de départ logique : motif d'usine répété, dégradé étiré, petite image répétée, grande image recadrée
            st.texScale = 1f;
            if (st.texture == "gradient") st.texMode = 0;
            else if (st.texture.StartsWith("file:"))
            {
                var sz = Ar6dofTheme.TextureSize(st.texture);
                st.texMode = (sz.x > 0 && sz.x <= 256 && sz.y <= 256) ? 1 : 4;
            }
            else st.texMode = 1;
            Touch();
        }

        // 12 palettes : nom, fond, boutons, accent, texte
        static readonly string[] PalNames = { "Eclipse", "Minuit", "Rubis", "Emeraude", "Amethyste", "Porcelaine", "Abysse", "Ambre", "Sakura", "Lagon", "Imperial", "Graphite" };
        static readonly string[,] PalCols =
        {
            { "#000000", "#262626", "#FFE600", "#FFFFFF" },
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
        };

        static Color Hex(string h)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString(h, out c)) c = Color.magenta;
            c.a = 1f;
            return c;
        }

        /// <summary>Le nom du style actuel : une palette d'usine ou un de tes styles s'il a exactement les mêmes quatre couleurs, sinon null.</summary>
        string CurrentStyleName()
        {
            var st = CurStyle(selElem);
            for (int i = 0; i < PalNames.Length; i++)
                if (st.SameColors(Hex(PalCols[i, 0]), Hex(PalCols[i, 1]), Hex(PalCols[i, 2]), Hex(PalCols[i, 3]))) return PalNames[i];
            foreach (var cs in D.customStyles)
                if (st.SameColors(cs.style.bg, cs.style.button, cs.style.accent, cs.style.text)) return cs.name;
            return null;
        }

        /// <summary>(Re)construit le cadre « Mes styles » : un bouton par style enregistré (avec sa croix), puis le nom et le bouton d'enregistrement.</summary>
        void RebuildCustomSec()
        {
            var rt = customSec.rt;
            for (int i = rt.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(rt.GetChild(i).gameObject);
            customBtns.Clear();
            var list = D.customStyles;
            float c = 0f;
            float top = OpenCard(rt, ref c, "Mes styles");
            if (list.Count == 0) Para(rt, ref c, "Regle tes couleurs, donne un nom, puis enregistre ton style ici.", 30f, 19);
            float y = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                int si = i;
                var cst = list[i];
                if (i % 2 == 0) y = Next(ref c, 64f, 10f);
                float xc = (i % 2 == 0) ? -164f : 164f;
                var b = Btn(rt, xc - 34f, y, 252f, cst.name, () => ApplyCustom(si), 20, 64f);
                customBtns.Add(b);
                Ar6dofVrUi.PlaceLabel(b.GetComponentInChildren<Text>(), new Vector2(0f, 14f), new Vector2(222f, 28f), TextAnchor.MiddleLeft);
                Color[] cols = { cst.style.bg, cst.style.button, cst.style.accent, cst.style.text };
                for (int k = 0; k < 4; k++)
                {
                    var sw = Ar6dofVrUi.Rect(b.transform, "Couleur" + k, new Vector2(-84f + 56f * k, -17f), new Vector2(50f, 14f));
                    var im = sw.gameObject.AddComponent<Image>();
                    Color cc = cols[k]; cc.a = 1f;
                    im.color = cc;
                    im.raycastTarget = false;
                    Ar6dofTheme.SetRounded(im, 5);
                }
                var del = Btn(rt, xc + 130f, y, 60f, "X", () => DeleteCustom(si), 20, 64f);
                var dt = del.gameObject.AddComponent<Ar6dofVrTooltip>();
                dt.text = () => Ar6dofLoc.T("Supprimer ce style");
            }
            float ry = Next(ref c, 50f, 12f);
            Ar6dofVrTextField.Make(rt, new Vector2(-124f, ry), new Vector2(400f, 50f), newStyleName, Ar6dofLoc.T("Nom de ton style"),
                t => newStyleName = t, t => newStyleName = t, 22);
            Btn(rt, 206f, ry, 236f, "Enregistrer ce style", SaveCustom, 20, 50f);
            CloseCard(rt, top, ref c);
            customSec.h = -c;
            if (styleBuilt) RefreshStyleUi();
        }

        readonly List<Button> customBtns = new List<Button>();

        void SaveCustom()
        {
            string n = (newStyleName ?? "").Trim();
            if (n.Length == 0) n = Ar6dofLoc.F("Mon style {0}", D.customStyles.Count + 1);
            if (n.Length > 22) n = n.Substring(0, 22);
            int ex = D.customStyles.FindIndex(x => x.name == n);
            var cs = new Ar6dofCustomStyle { name = n, style = CurStyle(selElem).Clone() };
            if (ex >= 0) D.customStyles[ex] = cs;
            else
            {
                if (D.customStyles.Count >= 12) { Say("12 styles maximum : supprime-en un"); return; }
                D.customStyles.Add(cs);
            }
            newStyleName = "";
            Touch();
            RebuildCustomSec();
            Say("Style enregistre dans Mes styles (pense a Enregistrer)");
        }

        void ApplyCustom(int i)
        {
            if (i < 0 || i >= D.customStyles.Count) return;
            CurStyle(selElem).CopyLookFrom(D.customStyles[i].style);
            Touch();
        }

        void DeleteCustom(int i)
        {
            if (i < 0 || i >= D.customStyles.Count) return;
            D.customStyles.RemoveAt(i);
            Touch();
            RebuildCustomSec();
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

        // ---- Clavier : disposition, animation lumineuse, couleurs de l'animation, puis vitesse et intensité (sous les couleurs)
        static Ar6dofSettingsData D { get { return Ar6dofSettings.Current; } }

        void BuildKeyboardSecs(RectTransform page)
        {
            var a = NewSec(page, "KbDispo", 3, null);
            {
                float c = 0f;
                float top = OpenCard(a.rt, ref c, null);
                float ly = Next(ref c, 48f, 12f);
                kbLayoutBtns.Add(Btn(a.rt, -164f, ly, 320f, "AZERTY", () => { D.kbAzerty = true; Touch(); }, 22));
                kbLayoutBtns.Add(Btn(a.rt, 164f, ly, 320f, "QWERTY", () => { D.kbAzerty = false; Touch(); }, 22));
                Toggle(a.rt, 0f, Next(ref c, 48f, 12f), "Pave numerique", () => D.kbNumpad, v => { D.kbNumpad = v; Touch(); }, 340f);
                CloseCard(a.rt, top, ref c);
                a.h = -c;
            }
            var m = NewSec(page, "KbAnim", 3, null);
            {
                float c = 0f;
                float top = OpenCard(m.rt, ref c, "Animation lumineuse");
                float y = 0f;
                for (int i = 0; i < Ar6dofKbFx.AnimNames.Length; i++)
                {
                    int mi = i;
                    if (i % 4 == 0) y = Next(ref c, 48f, 10f);
                    kbModeBtns.Add(Btn(m.rt, Col4(i % 4), y, 156f, Ar6dofKbFx.AnimNames[i], () => { D.kbAnim = mi; Touch(); }, 20));
                }
                CloseCard(m.rt, top, ref c);
                m.h = -c;
            }
            var dr = NewSec(page, "KbSens", 3, () => D.kbAnim == 1);
            {
                float c = 0f;
                float top = OpenCard(dr.rt, ref c, "Sens de la vague");
                float y = 0f;
                for (int i = 0; i < Ar6dofKbFx.DirNames.Length; i++)
                {
                    int di = i;
                    if (i % 3 == 0) y = Next(ref c, 48f, 10f);
                    kbDirBtns.Add(Btn(dr.rt, Col3(i % 3), y, 208f, Ar6dofKbFx.DirNames[i], () => { D.kbAnimDir = di; Touch(); }, 19));
                }
                CloseCard(dr.rt, top, ref c);
                dr.h = -c;
            }
            var co = NewSec(page, "KbCouleurs", 3, () => D.kbAnim != 0);
            {
                float c = 0f;
                float top = OpenCard(co.rt, ref c, "Couleurs de l'animation");
                float y = 0f;
                for (int i = 0; i < Ar6dofKbFx.PaletteNames.Length; i++)
                {
                    int pi = i;
                    if (i % 3 == 0) y = Next(ref c, 60f, 10f);
                    var b = Btn(co.rt, Col3(i % 3), y, 208f, Ar6dofKbFx.PaletteNames[i], () => { D.kbAnimPalette = pi; Touch(); }, 19, 60f);
                    var tex = new Texture2D(32, 1, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    kbPalTex.Add(tex);
                    // nom en haut, dégradé dessous : séparés, aucun ne cache l'autre
                    Ar6dofVrUi.PlaceLabel(b.GetComponentInChildren<Text>(), new Vector2(0f, 12f), new Vector2(176f, 26f), TextAnchor.MiddleLeft);
                    var gr = Ar6dofVrUi.Rect(b.transform, "Degrade", new Vector2(0f, -14f), new Vector2(176f, 12f));
                    var raw = gr.gameObject.AddComponent<RawImage>();
                    raw.texture = tex;
                    raw.raycastTarget = false;
                    kbPalBtns.Add(b);
                }
                CloseCard(co.rt, top, ref c);
                co.h = -c;
            }
            var tu = NewSec(page, "KbReglage", 3, () => D.kbAnim != 0);
            {
                float c = 0f;
                float top = OpenCard(tu.rt, ref c, "Vitesse et intensite");
                SliderRow(tu.rt, ref c, "Vitesse de l'animation", 0.1f, 4f, () => D.kbAnimSpeed, v => { D.kbAnimSpeed = v; Touch(); }, "0.00");
                SliderRow(tu.rt, ref c, "Intensite", 0f, 1f, () => D.kbAnimStrength, v => { D.kbAnimStrength = v; Touch(); }, "0.00");
                CloseCard(tu.rt, top, ref c);
                tu.h = -c;
            }
            refreshers.Add(() =>
            {
                for (int i = 0; i < kbModeBtns.Count; i++) Ar6dofTheme.SetSelected(kbModeBtns[i], D.kbAnim == i);
                for (int i = 0; i < kbDirBtns.Count; i++) Ar6dofTheme.SetSelected(kbDirBtns[i], D.kbAnimDir == i);
                for (int i = 0; i < kbPalBtns.Count; i++) Ar6dofTheme.SetSelected(kbPalBtns[i], D.kbAnimPalette == i);
                for (int i = 0; i < kbPalTex.Count; i++)
                {
                    if (kbPalDrawn[i] && i != 10) continue;   // palettes fixes : dessinées une seule fois ; "Theme du clavier" suit le style
                    kbPalDrawn[i] = true;
                    var px = new Color[32];
                    for (int x = 0; x < 32; x++) px[x] = Ar6dofKbFx.Sample(i, x / 32f);
                    kbPalTex[i].SetPixels(px);
                    kbPalTex[i].Apply(false);
                }
            });
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

        /// <summary>
        /// Sélecteur fin (anneau, carré, champs). oy = ordonnée du haut du bloc, juste sous la rangée Fond / Boutons / Accent / Texte.
        /// À gauche l'anneau de 300 px ; à droite l'aperçu, HEX, RGB, HSV et la couleur aléatoire.
        /// </summary>
        void BuildColorPicker(RectTransform box, float oy)
        {
            picker = Ar6dofVrColorPicker.Make(box, new Vector2(-175f, oy - 150f), 300f, h => { hue = h; SetHsv(); }, (sa, va) => { sat = sa; val = va; SetHsv(); });

            var pv = Ar6dofVrUi.Rect(box, "Apercu", new Vector2(160f, oy - 22f), new Vector2(300f, 44f));
            preview = pv.gameObject.AddComponent<Image>();
            preview.raycastTarget = false;
            Ar6dofTheme.SetRounded(preview, 10);

            Ar6dofVrUi.Label(box, "HEX", new Vector2(48f, oy - 82f), new Vector2(70f, 30f), 20, TextAnchor.MiddleLeft);
            fHex = Ar6dofVrTextField.Make(box, new Vector2(215f, oy - 82f), new Vector2(190f, 44f), "", "#RRGGBB", t => ApplyHex(t), t => { ApplyHex(t); RefreshAll(); }, 22);

            string[] rgbN = { "R", "G", "B" }, hsvN = { "H", "S", "V" };
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                float gx = 10f + 100f * k;
                fRgb[k] = ColorField(box, gx, oy - 146f, rgbN[k], t => ApplyRgb(kk, t));
                fHsv[k] = ColorField(box, gx, oy - 206f, hsvN[k], t => ApplyHsvField(kk, t));
            }
            Btn(box, 160f, oy - 270f, 300f, "Couleur aleatoire", RandomColor, 20, 44f);
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
            // sur la ligne sous les boutons : le texte d'explication pour "Tout", la case "Style a part" pour un élément précis
            scopeLbl.gameObject.SetActive(selElem == Ar6dofElem.Global);
            separateBox.gameObject.SetActive(selElem != Ar6dofElem.Global);
            for (int i = 0; i < chromeStyleButtons.Count; i++) Ar6dofTheme.SetSelected(chromeStyleButtons[i], Ar6dofSettings.Current.chromeStyle == i);
            for (int i = 0; i < chromeCornerButtons.Count; i++) Ar6dofTheme.SetSelected(chromeCornerButtons[i], Ar6dofSettings.Current.chromeCorner == i);
            if (texNameLbl != null)
            {
                var ids = Ar6dofTheme.TextureIds();
                string cur = CurStyle(selElem).texture ?? "";
                texNameLbl.text = Ar6dofTheme.TextureLabel(cur) + "   (" + (Mathf.Max(0, ids.IndexOf(cur)) + 1) + "/" + ids.Count + ")";
            }
            for (int i = 0; i < kbLayoutBtns.Count; i++) Ar6dofTheme.SetSelected(kbLayoutBtns[i], D.kbAzerty == (i == 0));
            for (int i = 0; i < texModeBtns.Count; i++) Ar6dofTheme.SetSelected(texModeBtns[i], CurStyle(selElem).texMode == TexModeOrder[i]);
            // style actuel : la palette ou ton style qui a les mêmes couleurs est marqué, son nom est écrit au-dessus
            var cst = CurStyle(selElem);
            string curName = CurrentStyleName();
            if (palCurLbl != null)
            {
                palCurLbl.text = curName != null ? Ar6dofLoc.F("Style actuel : {0}", curName) : Ar6dofLoc.T("Style actuel : personnalise");
            }
            for (int i = 0; i < palBtns.Count; i++)
                Ar6dofTheme.SetSelected(palBtns[i], cst.SameColors(Hex(PalCols[i, 0]), Hex(PalCols[i, 1]), Hex(PalCols[i, 2]), Hex(PalCols[i, 3])));
            for (int i = 0; i < customBtns.Count && i < D.customStyles.Count; i++)
            {
                var ss0 = D.customStyles[i].style;
                Ar6dofTheme.SetSelected(customBtns[i], cst.SameColors(ss0.bg, ss0.button, ss0.accent, ss0.text));
            }
            LayoutStyle();
        }

        /// <summary>Empile les groupes visibles de la catégorie choisie, sous la rangée de catégories (et sous « Appliquer a » pour Couleurs et Forme).</summary>
        void LayoutStyle()
        {
            float y = Start - 46f - 16f;   // sous la rangée de catégories
            bool target = styleSub <= 1;
            scopeSec.rt.gameObject.SetActive(target);
            if (target)
            {
                scopeSec.rt.anchoredPosition = new Vector2(0f, y);
                y -= scopeSec.h;
            }
            foreach (var s in secs)
            {
                bool on = s.sub == styleSub && (s.vis == null || s.vis());
                s.rt.gameObject.SetActive(on);
                if (!on) continue;
                s.rt.anchoredPosition = new Vector2(0f, y);
                y -= s.h;
            }
            styleBottom = y + 18f;   // bas du dernier cadre
        }

        // ================================================================== DOCK
        void BuildDock(RectTransform page)
        {
            float c = Start;
            float top = OpenCard(page, ref c, "Position");
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Dock aux pieds (cache, apparait en baissant la tete)", () => Ar6dofSettings.Current.dockAtFeet, v => { Ar6dofSettings.Current.dockAtFeet = v; Touch(); }, 600f);
            SliderRow(page, ref c, "Distance devant les pieds (m)", 0.3f, 1.4f, () => Ar6dofSettings.Current.dockDistance, v => { Ar6dofSettings.Current.dockDistance = v; Touch(); }, "0.00");
            SliderRow(page, ref c, "Debut d'apparition : tete baissee de (deg)", 5f, 70f, () => Ar6dofSettings.Current.dockRevealStart, v => { Ar6dofSettings.Current.dockRevealStart = v; Touch(); }, "0");
            SliderRow(page, ref c, "Pleinement visible a (deg)", 15f, 85f, () => Ar6dofSettings.Current.dockRevealFull, v => { Ar6dofSettings.Current.dockRevealFull = v; Touch(); }, "0");
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Taille");
            SliderRow(page, ref c, "Taille du dock", 0.5f, 2f, () => Ar6dofSettings.Current.dockScale, v => { Ar6dofSettings.Current.dockScale = v; Touch(); }, "0.00");
            SliderRow(page, ref c, "Inclinaison supplementaire (deg)", -30f, 30f, () => Ar6dofSettings.Current.dockTilt, v => { Ar6dofSettings.Current.dockTilt = v; Touch(); }, "0");
            SliderRow(page, ref c, "Taille du clavier", 0.5f, 2.5f, () => Ar6dofSettings.Current.keyboardScale, v => { Ar6dofSettings.Current.keyboardScale = v; Touch(); }, "0.00");
            CloseCard(page, top, ref c);
            Finish(TabDock, c);
        }

        // ================================================================== LANGUE
        void BuildLanguage(RectTransform page)
        {
            float c = Start;
            float top = OpenCard(page, ref c, "Langue de l'interface");
            langCodes.Clear();
            langCodes.Add("");
            langButtons.Add(Btn(page, 0f, Next(ref c, 48f, 12f), 520f, "Automatique (langue de Windows)", () => { Ar6dofLoc.SetLanguage(""); }, 22));
            var langs = Ar6dofLoc.Languages();
            float y = 0f;
            for (int i = 0; i < langs.Count; i++)
            {
                string code = langs[i].Key;
                if (i % 2 == 0) y = Next(ref c, 48f, 12f);
                langCodes.Add(code);
                langButtons.Add(Btn(page, (i % 2 == 0) ? -164f : 164f, y, 320f, langs[i].Value, () => { Ar6dofLoc.SetLanguage(code); }, 22));
            }
            CloseCard(page, top, ref c);
            Para(page, ref c, "Ajouter ou corriger une langue : fichier texte dans\n%APPDATA%\\AR6DoF\\lang (voir le LISEZMOI),\nune ligne par texte (texte francais, fleche, traduction).", 100f);
            Finish(TabLang, c);
            refreshers.Add(() =>
            {
                string cur = Ar6dofSettings.Current.language ?? "";
                for (int i = 0; i < langButtons.Count; i++) Ar6dofTheme.SetSelected(langButtons[i], langCodes[i] == cur);
            });
        }
    }
}
