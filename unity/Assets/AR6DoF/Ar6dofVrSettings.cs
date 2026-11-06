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
        int curTab;                 // PAGE affichée (voir PgView...)
        float nextLive;

        // 5 onglets (comme dans Style, chacun peut avoir des sous-pages en haut de sa page) :
        //   Image      > Affichage, Optique, Cameras
        //   Ecrans
        //   Appareils  > Tete, Telecommande, Yeux, Bouche
        //   Style      > Couleurs, Forme, Fenetres, Clavier (sous-pages propres a Style)
        //   Interface  > Dock, Langue, Performances
        // Chaque sous-page est une PAGE (défilement et textes vivants à elle) ; Enregistrer / Annuler / Par defaut ne touchent que la page affichée.
        const int PgView = 0, PgOptics = 1, PgScreens = 2, PgHead = 3, PgRemote = 4, PgEyes = 5, PgStyle = 6, PgDock = 7, PgLang = 8, PgPerf = 9, PgMouth = 10, PgCams = 11, PageCount = 12;
        static readonly string[] TabNames = { "Image", "Ecrans", "Appareils", "Style", "Interface" };
        static readonly int[][] TabPages = { new[] { PgView, PgOptics, PgCams }, new[] { PgScreens }, new[] { PgHead, PgRemote, PgEyes, PgMouth }, new[] { PgStyle }, new[] { PgDock, PgLang, PgPerf } };
        static readonly string[] PageNames = { "Affichage", "Optique", "Ecrans", "Tete", "Telecommande", "Yeux", "Style", "Dock", "Langue", "Performances", "Bouche", "Cameras" };
        static int TabOf(int pg)
        {
            for (int t = 0; t < TabPages.Length; t++) foreach (int p in TabPages[t]) if (p == pg) return t;
            return 0;
        }
        readonly int[] lastPage = { PgView, PgScreens, PgHead, PgStyle, PgDock };   // dernière sous-page vue de chaque onglet
        readonly List<GameObject> tabDots = new List<GameObject>();                   // point « modifié, pas enregistré » sur chaque onglet
        readonly List<KeyValuePair<int, GameObject>> subDots = new List<KeyValuePair<int, GameObject>>();   // idem sur chaque bouton de sous-page (page, point)

        // bas du panneau
        string imgSaved = "";          // réglages d'image tels qu'enregistrés (ils ont leur propre fichier, mais suivent Enregistrer / Annuler / Par defaut)
        string ImgJson() { return dock.passthrough != null ? JsonUtility.ToJson(dock.passthrough.settings) : ""; }
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
        readonly List<GameObject> styleDots = new List<GameObject>();   // un point par catégorie de Style (Couleurs, Forme, Fenetres, Clavier)
        readonly List<Button> chromeStyleButtons = new List<Button>();
        readonly List<Button> chromeCornerButtons = new List<Button>();
        readonly List<Button> fpsBtns = new List<Button>();
        readonly List<Button> aaBtns = new List<Button>();
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
        // aperçus des caméras d'oeil / de bouche : à la demande seulement (voir Ar6dofCameraPreview)
        readonly Ar6dofCameraPreview camPreview = new Ar6dofCameraPreview();
        bool eyePvOn, mouthPvOn, eyePvAuto, mouthPvAuto;     // aperçu demandé ; Auto : l'aperçu a lui-même activé le suivi (arrêté avec l'aperçu)
        RawImage eyePvImg, mouthPvImg;
        Text eyePvBtn, mouthPvBtn, eyePvMsg, mouthPvMsg;
        Text calMsg, calBtnText;
        string calMsgFr = "";
        int calStep;                // 0 = rien, 1 ou 2 = étape en attente
        float calAt;
        const float CalDelay = 3f;

        // défilement des pages
        readonly List<GameObject> views = new List<GameObject>();
        readonly List<Ar6dofVrScrollArea> scrolls = new List<Ar6dofVrScrollArea>();
        readonly float[] bottoms = new float[PageCount];     // bas du dernier cadre de chaque page (ordonnée dans la page)
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
                var b = Ar6dofVrUi.MakeButton(canvas.transform, TabNames[i], new Vector2((i - (TabNames.Length - 1) * 0.5f) * 148f, 380f), new Vector2(140f, 46f), () => ShowTab(idx), out t, 22);
                tabButtons.Add(b);
                tabDots.Add(Dot(b.transform, new Vector2(62f, 15f)));
            }
            for (int i = 0; i < PageCount; i++)
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
            BuildPassthrough(pages[PgView]);
            BuildOptics(pages[PgOptics]);
            BuildCams(pages[PgCams]);
            BuildScreens(pages[PgScreens]);
            BuildHead(pages[PgHead]);
            BuildRemote(pages[PgRemote]);
            BuildEyes(pages[PgEyes]);
            BuildMouth(pages[PgMouth]);
            BuildStyle(pages[PgStyle]);
            BuildDock(pages[PgDock]);
            BuildLanguage(pages[PgLang]);
            BuildPerf(pages[PgPerf]);
            imgSaved = ImgJson();
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
            float bottom = tab == PgStyle ? styleBottom : bottoms[tab];
            return Mathf.Max(0f, -bottom - VisTop + 16f);
        }

        /// <summary>Note où finit le contenu d'un onglet (c = curseur après le dernier cadre).</summary>
        void Finish(int tab, float c) { bottoms[tab] = c + 18f; }

        /// <summary>
        /// Début d'une page : si son onglet a plusieurs sous-pages, la rangée de boutons des sous-pages (comme les catégories de Style) est posée tout en haut
        /// et le curseur démarre juste dessous ; sinon le curseur démarre en haut.
        /// </summary>
        float BeginPage(RectTransform page, int pg)
        {
            int tab = TabOf(pg);
            int[] subs = TabPages[tab];
            if (subs.Length < 2) return Start;
            for (int k = 0; k < subs.Length; k++)
            {
                int target = subs[k];
                float x = subs.Length == 2 ? (k == 0 ? -164f : 164f) : subs.Length == 4 ? Col4(k) : Col3(k);
                float w = subs.Length == 2 ? 320f : subs.Length == 4 ? 156f : 208f;
                var b = Btn(page, x, Start - 23f, w, PageNames[target], () => ShowPage(target), subs.Length == 4 ? 19 : 22, 46f);
                subDots.Add(new KeyValuePair<int, GameObject>(target, Dot(b.transform, new Vector2(w * 0.5f - 18f, 15f))));
                refreshers.Add(() => Ar6dofTheme.SetSelected(b, target == curTab));
            }
            return Start - 46f - 16f;
        }

        /// <summary>Petit point d'accent dans le coin d'un bouton : « cette page a des modifications pas encore enregistrées ».</summary>
        static GameObject Dot(Transform parent, Vector2 pos)
        {
            var rt = Ar6dofVrUi.Rect(parent, "Point", pos, new Vector2(14f, 14f));
            var im = rt.gameObject.AddComponent<Image>();
            im.raycastTarget = false;
            Ar6dofTheme.Attach(rt.gameObject, Ar6dofRole.Accent, 7);
            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

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
            if (canvas == null || !canvas.gameObject.activeInHierarchy)
            {
                // menu fermé : plus d'aperçu (et on rend leur état aux suivis activés seulement pour l'aperçu)
                if (eyePvOn) SetPreview(true, false);
                if (mouthPvOn) SetPreview(false, false);
                if (camPreview.Texture != null) camPreview.Stop();
                return;
            }
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
            PumpPreview();
            if (now < nextLive) return;
            nextLive = now + 0.25f;
            foreach (var kv in live) if (kv.Key == curTab) kv.Value();
            UpdateStatus();
        }

        /// <summary>Affiche un onglet (sa dernière sous-page vue).</summary>
        public void ShowTab(int i)
        {
            ShowPage(lastPage[Mathf.Clamp(i, 0, lastPage.Length - 1)]);
        }

        /// <summary>Affiche une page (sous-page d'un onglet).</summary>
        public void ShowPage(int pg)
        {
            curTab = pg;
            int tab = TabOf(pg);
            lastPage[tab] = pg;
            if (pg == PgStyle && styleSub == 1) Ar6dofTheme.RescanPictures();   // la liste d'images n'est relue qu'en entrant ici
            for (int k = 0; k < pages.Count; k++)
            {
                views[k].SetActive(k == pg);
                if (k != pg) scrolls[k].SetOffset(0f);
            }
            for (int k = 0; k < tabButtons.Count; k++) Ar6dofTheme.SetSelected(tabButtons[k], k == tab);
            resetArmed = false;
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

        /// <summary>
        /// Réglage précis : la valeur au-dessus, un curseur qui accroche sur des pas de s1, et deux petits boutons - et + pour l'ajustement fin (un pas à chaque clic).
        /// s2 et s3 (anciens gros pas) ne servent plus : le curseur les remplace.
        /// </summary>
        void StepRow(RectTransform page, ref float c, string label, float min, float max, float s1, float s2, float s3, Func<float> get, Action<float> set, string fmt = "0")
        {
            float y = Next(ref c, 74f, 10f);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Text lbl = Ar6dofVrUi.Label(page, "", new Vector2(0f, y + 22f), new Vector2(CW, 30f), 22, TextAnchor.MiddleLeft);
            Action upd = () => lbl.text = Ar6dofLoc.T(label) + " : " + get().ToString(fmt, inv);
            const float bw = 64f, gap = 10f;
            float sw = CW - 2f * (bw + gap);
            float step = Mathf.Max(1e-4f, s1);
            Slider sl = null;
            Action<float> apply = v =>
            {
                float q = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
                q = Mathf.Round(q * 1000f) / 1000f;
                if (Mathf.Abs(q - get()) > 1e-5f) set(q);
                if (sl != null) sl.SetValueWithoutNotify(q);
                upd();
            };
            Text t1, t2;
            Ar6dofVrUi.MakeButton(page, "-", new Vector2(-CW * 0.5f + bw * 0.5f, y - 16f), new Vector2(bw, 42f), () => apply(get() - step), out t1, 26);
            Ar6dofVrUi.MakeButton(page, "+", new Vector2(CW * 0.5f - bw * 0.5f, y - 16f), new Vector2(bw, 42f), () => apply(get() + step), out t2, 26);
            sl = Ar6dofVrUi.MakeSlider(page, new Vector2(0f, y - 16f), new Vector2(sw, 40f), min, max, get(), v => apply(v));
            refreshers.Add(() => { sl.SetValueWithoutNotify(Mathf.Clamp(get(), min, max)); upd(); });
            upd();
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

        /// <summary>Choix dans une liste : [<] texte [>]. 'value' donne le texte de la valeur actuelle (déjà traduit si besoin).</summary>
        void CycleRow(RectTransform page, ref float c, string label, int count, Func<int> get, Action<int> set, Func<int, string> valueOf)
        {
            float y = Next(ref c, 48f, 12f);
            Text lbl = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(CW - 160f, 48f), 22, TextAnchor.MiddleCenter);
            Action upd = () => lbl.text = Ar6dofLoc.T(label) + " : " + valueOf(get());
            Text t;
            Ar6dofVrUi.MakeButton(page, "<", new Vector2(-CW * 0.5f + 36f, y), new Vector2(72f, 48f), () => { set((get() + count - 1) % count); upd(); }, out t, 26);
            Ar6dofVrUi.MakeButton(page, ">", new Vector2(CW * 0.5f - 36f, y), new Vector2(72f, 48f), () => { set((get() + 1) % count); upd(); }, out t, 26);
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

        // ---- portée de Enregistrer / Annuler / Par defaut : la PAGE affichée seulement ----
        static readonly string[] LookFields = { "global", "dock", "menus", "keyboard", "screens", "laser", "customStyles" };
        static readonly string[] ChromeFields = { "chromeStyle", "chromeCorner", "chromeScale" };
        static readonly string[] KbFields = { "kbAzerty", "kbNumpad", "kbAnim", "kbAnimDir", "kbAnimPalette", "kbAnimSpeed", "kbAnimStrength" };
        static readonly string[] HeadFields = { "moveScale", "eyeHeightCm", "eyeHeightSeatedCm", "menusFollow", "openDistance" };
        static readonly string[] RemoteFields = { "remoteMinCutoff", "remoteBeta", "remotePredictMs" };
        static readonly string[] DockFields = { "dockAtFeet", "dockDistance", "dockRevealStart", "dockRevealFull", "uiScale", "dockScale", "dockTilt", "dockSeatedDrop", "keyboardScale", "dockShowClock", "dockClockStyle", "dockFaceStyle", "dockShowDate", "dockShowBattery", "dockBatteryStyle", "clock24h", "clockSeconds", "dateFormat" };
        static readonly string[] LangFields = { "language" };
        static readonly string[] PerfFields = { "perfFps", "perfAa", "perfRenderScale" };
        static readonly string[] ViewImgFields = { "displayFovDeg", "ipdShift", "captureDelayMs", "useCalibration", "reproject", "autoDelay", "clampEdges", "flipComposite" };
        static readonly string[] OpticsImgFields = { "left", "right", "linkEyes", "swapEyes", "layout", "centerOnImage" };

        /// <summary>Champs d'Ar6dofSettings (settings.json) que contrôle cette page ; null si la page n'en a pas.</summary>
        string[] SettingsScope(int pg)
        {
            switch (pg)
            {
                case PgHead: return HeadFields;
                case PgRemote: return RemoteFields;
                case PgDock: return DockFields;
                case PgLang: return LangFields;
                case PgPerf: return PerfFields;
                case PgStyle: return styleSub >= 3 ? KbFields : styleSub == 2 ? ChromeFields : LookFields;
                default: return null;
            }
        }

        /// <summary>Champs des réglages d'image (passthrough) que contrôle cette page ; null si la page n'en a pas.</summary>
        string[] ImgScope(int pg)
        {
            if (dock.passthrough == null) return null;
            return pg == PgView ? ViewImgFields : pg == PgOptics ? OpticsImgFields : null;
        }

        bool ImgDirty(string[] f)
        {
            if (dock.passthrough == null || imgSaved.Length == 0) return false;
            var saved = new Ar6dofPassthroughSettings();
            JsonUtility.FromJsonOverwrite(imgSaved, saved);
            return Ar6dofScope.Sig(saved, f) != Ar6dofScope.Sig(dock.passthrough.settings, f);
        }

        bool PageDirty(int pg)
        {
            if (pg == PgStyle) return Ar6dofSettings.DirtyFields(LookFields) || Ar6dofSettings.DirtyFields(ChromeFields) || Ar6dofSettings.DirtyFields(KbFields);
            return ScopeDirty(pg);
        }

        static readonly string[] StyleElemFields = { "global", "dock", "menus", "keyboard", "screens", "laser" };

        // 'global' est un style ; dock, menus, clavier, ecrans et laser sont des Ar6dofElemStyle qui contiennent le leur
        static Ar6dofStyle StyleOf(object o)
        {
            var st = o as Ar6dofStyle;
            if (st != null) return st;
            var es = o as Ar6dofElemStyle;
            return es != null ? es.style : null;
        }

        static string StyleSig(Ar6dofStyle s, bool colors)
        {
            if (s == null) return "";
            if (colors)
                return ColorUtility.ToHtmlStringRGBA(s.bg) + ColorUtility.ToHtmlStringRGBA(s.button) + ColorUtility.ToHtmlStringRGBA(s.accent) + ColorUtility.ToHtmlStringRGBA(s.text) + "|" + s.alpha.ToString("0.000");
            return s.corner + "|" + s.texture + "|" + s.texAlpha.ToString("0.000") + "|" + s.texMode + "|" + s.texScale.ToString("0.000") + "|" + s.lineWidth.ToString("0.0000");
        }

        /// <summary>Une catégorie de Style a-t-elle des changements non enregistrés ? (0 Couleurs, 1 Forme, 2 Fenetres, 3 Clavier)</summary>
        bool StyleSubDirty(int k)
        {
            if (k >= 3) return Ar6dofSettings.DirtyFields(KbFields);
            if (k == 2) return Ar6dofSettings.DirtyFields(ChromeFields);
            var cur = Ar6dofSettings.Current;
            var sv = Ar6dofSettings.SavedSnapshot();
            var t = typeof(Ar6dofSettingsData);
            foreach (string name in StyleElemFields)
            {
                var fi = t.GetField(name);
                if (fi == null) continue;
                if (StyleSig(StyleOf(fi.GetValue(cur)), k == 0) != StyleSig(StyleOf(fi.GetValue(sv)), k == 0)) return true;
            }
            if (k == 0 && Ar6dofSettings.DirtyFields(new[] { "customStyles" })) return true;
            // « separate » (un element a son propre style) : dans Couleurs
            if (k == 0)
                foreach (string name in StyleElemFields)
                {
                    var a = t.GetField(name).GetValue(cur) as Ar6dofElemStyle;
                    var b = t.GetField(name).GetValue(sv) as Ar6dofElemStyle;
                    if (a != null && b != null && a.separate != b.separate) return true;
                }
            return false;   // « Mes styles » : dans Couleurs
        }

        /// <summary>Modifications non enregistrées dans la portée de cette page (pour Style : la sous-page affichée).</summary>
        bool ScopeDirty(int pg)
        {
            var sf = SettingsScope(pg);
            if (sf != null && Ar6dofSettings.DirtyFields(sf)) return true;
            var imf = ImgScope(pg);
            return imf != null && ImgDirty(imf);
        }

        void UpdateStatus()
        {
            if (statusLbl == null) return;
            bool hasScope = SettingsScope(curTab) != null || ImgScope(curTab) != null;
            bool here = hasScope && ScopeDirty(curTab);
            bool anyDirty = false;
            var dirtyPage = new bool[PageCount];
            for (int p = 0; p < PageCount; p++) { dirtyPage[p] = PageDirty(p); anyDirty |= dirtyPage[p]; }
            for (int i = 0; i < styleDots.Count; i++) if (styleDots[i] != null) styleDots[i].SetActive(StyleSubDirty(i));   // Style : la catégorie concernée
            // les points : un par bouton de sous-page, un par onglet
            foreach (var kv in subDots) if (kv.Value != null) kv.Value.SetActive(dirtyPage[kv.Key]);
            for (int t = 0; t < tabDots.Count; t++)
            {
                bool d = false;
                foreach (int p in TabPages[t]) d |= dirtyPage[p];
                tabDots[t].SetActive(d);
            }
            string msg;
            if (Time.unscaledTime < statusUntil) msg = statusMsg;
            else if (!hasScope) msg = Ar6dofLoc.T("Cette page s'applique tout de suite");
            else if (here) msg = Ar6dofLoc.T("Apercu : pas encore enregistre");
            else if (anyDirty) msg = Ar6dofLoc.T("Cette page est enregistree (d'autres pages ont des changements)");
            else msg = Ar6dofLoc.T("Tout est enregistre");
            statusLbl.text = msg;
            Ar6dofTheme.SetSelected(saveBtn, here);
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
            var sf = SettingsScope(curTab);
            var imf = ImgScope(curTab);
            if (sf == null && imf == null) { Say("Cette page s'applique tout de suite : rien a enregistrer"); return; }
            bool ok = true;
            if (sf != null) ok &= Ar6dofSettings.SaveFields(sf);
            if (imf != null)
            {
                // le fichier d'image garde les autres pages telles qu'elles sont enregistrées : seuls les champs de cette page changent
                var saved = new Ar6dofPassthroughSettings();
                if (imgSaved.Length > 0) JsonUtility.FromJsonOverwrite(imgSaved, saved);
                Ar6dofScope.Copy(dock.passthrough.settings, saved, imf);
                ok &= dock.passthrough.SaveSettings(saved);
                imgSaved = JsonUtility.ToJson(saved);
            }
            Say(ok ? "Enregistre" : "Echec de l'enregistrement");
            UpdateStatus();
        }

        void OnCancel()
        {
            var sf = SettingsScope(curTab);
            var imf = ImgScope(curTab);
            if (sf == null && imf == null) { Say("Cette page s'applique tout de suite : rien a annuler"); return; }
            if (sf != null) Ar6dofSettings.RevertFields(sf);
            if (imf != null && imgSaved.Length > 0)
            {
                var saved = new Ar6dofPassthroughSettings();
                JsonUtility.FromJsonOverwrite(imgSaved, saved);
                Ar6dofScope.Copy(saved, dock.passthrough.settings, imf);
            }
            RefreshAll();
            Say("Modifications de cette page annulees");
        }

        void OnReset()
        {
            var sf = SettingsScope(curTab);
            var imf = ImgScope(curTab);
            if (sf == null && imf == null) { Say("Cette page s'applique tout de suite : rien a remettre"); return; }
            if (!resetArmed)
            {
                resetArmed = true;
                resetUntil = Time.unscaledTime + 3f;
                UpdateResetLabel();
                return;
            }
            resetArmed = false;
            if (sf != null) Ar6dofSettings.ResetFields(sf);
            if (imf != null) Ar6dofScope.Copy(new Ar6dofPassthroughSettings(), dock.passthrough.settings, imf);
            RefreshAll();
            Say("Valeurs d'usine de cette page en apercu : clique Enregistrer pour les garder");
        }

        // ================================================================== IMAGE
        void BuildPassthrough(RectTransform page)
        {
            var pt = dock.passthrough;
            float c = BeginPage(page, PgView);
            if (pt == null)
            {
                Ar6dofVrUi.Label(page, "Passthrough introuvable dans la scene", new Vector2(0f, c - 30f), new Vector2(CW, 40f), 24);
                Finish(PgView, c - 60f);
                return;
            }
            // état de l'image (avant : panneau F1)
            float top = OpenCard(page, ref c, "Etat de l'image");
            InfoLine(page, Next(ref c, 28f, 4f), PgView, () => pt.SourceOpen
                ? Ar6dofLoc.F("Source : {0} x {1}   -   {2} img/s   -   age {3} ms", pt.SourceWidth, pt.SourceHeight, pt.UploadsPerSec, pt.FrameAgeMs.ToString("0"))
                : Ar6dofLoc.T("Source : camera non ouverte"), 19);
            InfoLine(page, Next(ref c, 28f, 4f), PgView, () => Ar6dofLoc.F("Envoi GPU : {0} ms   -   reprojection {1} deg   -   delai {2} ms",
                pt.UploadMs.ToString("0.0"), pt.ReprojDeg.ToString("0.0"), pt.DelayUsedMs.ToString("0")), 19);
            InfoLine(page, Next(ref c, 28f, 4f), PgView, () => Ar6dofLoc.T(pt.HasCalibration ? "Calibration camera : chargee" : "Calibration camera : ABSENTE (camera_model.json)"), 19);
            InfoLine(page, Next(ref c, 28f, 4f), PgView, CalibInfoLine, 19);
            InfoLine(page, Next(ref c, 28f, 8f), PgView, CalibModeLine, 19);
            InfoLine(page, Next(ref c, 28f, 12f), PgView, CalibHintLine, 19);
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Affichage");
            StepRow(page, ref c, "FOV affiche (deg)", 40f, 130f, 1f, 5f, 10f, () => pt.settings.displayFovDeg, v => pt.settings.displayFovDeg = v, "0");
            StepRow(page, ref c, "IPD logiciel (+ = ecarte)", -400f, 400f, 1f, 10f, 50f, () => Mathf.Round(pt.settings.ipdShift * 1000f), v => pt.settings.ipdShift = v / 1000f, "0");
            StepRow(page, ref c, "Delai manuel (ms)", 0f, 120f, 1f, 5f, 10f, () => pt.settings.captureDelayMs, v => pt.settings.captureDelayMs = v, "0");
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Traitement de l'image");
            float y = Next(ref c, 48f, 10f);
            Toggle(page, -164f, y, "Calibration camera", () => pt.settings.useCalibration, v => pt.settings.useCalibration = v);
            Toggle(page, 164f, y, "Reprojection", () => pt.settings.reproject, v => pt.settings.reproject = v);
            y = Next(ref c, 48f, 10f);
            Toggle(page, -164f, y, "Delai auto", () => pt.settings.autoDelay, v => pt.settings.autoDelay = v);
            Toggle(page, 164f, y, "Etirer les bords", () => pt.settings.clampEdges, v => pt.settings.clampEdges = v);
            y = Next(ref c, 48f, 16f);
            Toggle(page, -164f, y, "Rendu a l'envers", () => pt.settings.flipComposite, v => pt.settings.flipComposite = v);
            CloseCard(page, top, ref c);
            Finish(PgView, c);
        }

        Ar6dofCalibLink calLink;

        CalInfoJson CalInfo()
        {
            if (calLink == null) calLink = new Ar6dofCalibLink(dock);
            calLink.Poll();
            return calLink.Info;
        }

        // ce que le suivi utilise : d'ou vient la calibration et la distance entre les cameras
        string CalibInfoLine()
        {
            var i = CalInfo();
            if (i == null) return Ar6dofLoc.T("Suivi : pas encore de calibration lue");
            string src = i.source == "ar6dof" ? Ar6dofLoc.T("AR6DoF (guidee)") : (i.source == "rtabmap" ? "RTAB-Map" : i.source);
            return Ar6dofLoc.F("Suivi : calibration {0}, cameras a {1} mm", src, i.baselineMm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }

        // le suivi adapte tout seul la calibration au mode camera utilise (resolution, cadence) : plus d'alerte, seulement l'information
        string CalibModeLine()
        {
            var i = CalInfo();
            if (i == null) return "";
            if (!i.mismatch) return Ar6dofLoc.F("Mode camera {0} x {1} : identique a la calibration", i.runW, i.runH);
            return Ar6dofLoc.F("Mode camera {0} x {1} a {2} i/s, different de la calibration ({3} x {4}) : adapte automatiquement (zoom du mode x{5})",
                i.runW, i.runH, i.runFps, i.calibW, i.calibH, i.zoom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }

        string CalibHintLine()
        {
            var i = CalInfo();
            if (i != null && i.mismatch && !i.zoomConfirmed && i.zoomSource != "calib" && i.zoomSource != "manual")
                return Ar6dofLoc.T("Zoom du mode pas encore mesure : bouge la tete quelques secondes pour que l'IMU le mesure");
            return Ar6dofLoc.T("Calibration de la camera : dans ar6dof_app.exe, page Calibration");
        }

        // ================================================================== OPTIQUE
        Ar6dofEyeSettings Eye(Ar6dofPassthrough pt)
        {
            return (!pt.settings.linkEyes && optEye == 1) ? pt.settings.right : pt.settings.left;
        }

        void BuildOptics(RectTransform page)
        {
            var pt = dock.passthrough;
            float c = BeginPage(page, PgOptics);
            if (pt == null)
            {
                Ar6dofVrUi.Label(page, "Passthrough introuvable dans la scene", new Vector2(0f, c - 30f), new Vector2(CW, 40f), 24);
                Finish(PgOptics, c - 60f);
                return;
            }
            const float cx = 164f, cw = 320f;

            float top = OpenCard(page, ref c, "Reglage par oeil");
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

            // plus de Recharger / Sauver / Remettre à zéro propres à l'image : Enregistrer / Annuler / Par defaut (en bas) s'en chargent aussi
            Finish(PgOptics, c);
        }

        // ================================================================== ECRANS
        void BuildScreens(RectTransform page)
        {
            float c = BeginPage(page, PgScreens);
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
            InfoLine(page, Next(ref c, 28f, 10f), PgScreens, () => Ar6dofLoc.F("Ecrans : {0} / {1}  (clusters de 3 max)", Ar6dofScreens.Instance != null ? Ar6dofScreens.Instance.panels.Count : 0, Ar6dofScreens.Instance != null ? Ar6dofScreens.Instance.MaxAllowed : 12), 20);
            y = Next(ref c, 48f, 12f);
            Btn(page, 0f, y, 440f, "+ Ajouter un ecran", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.RequestAddScreen(); }, 22);
            CloseCard(page, top, ref c);
            Finish(PgScreens, c);
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

        // mesure de l'échelle des déplacements : 1) point de départ, 2) avance en ligne droite de la distance indiquée, 3) arrivée
        float scaleDistCm = 100f;
        int scaleStep;
        Vector3 scaleStart, scaleCamStart;
        Text scaleBtnText;

        Text lockFloorText;
        void UpdateLockBtn()
        {
            if (lockFloorText == null) return;
            bool on = dock.rig != null && dock.rig.FloorLocked;
            lockFloorText.text = Ar6dofLoc.T(on ? "Deverrouiller le sol" : "Verrouiller le sol");
        }

        void UpdateScaleBtn()
        {
            if (scaleBtnText == null) return;
            scaleBtnText.text = scaleStep == 0 ? Ar6dofLoc.T("Mesurer l'echelle : point de depart") : Ar6dofLoc.F("Arrivee : j'ai avance de {0} cm", Mathf.RoundToInt(scaleDistCm));
        }

        // test d'ancrage : ne change AUCUN reglage. Mesure ce que fait ce menu quand tu te deplaces : il doit rester a 0 cm (fixe dans la piece).
        // Si le menu bouge alors que le suivi et la camera ont bouge de la bonne distance, quelque chose dans le code deplace le menu.
        int anchorStep;
        Vector3 anchorRaw, anchorCam, anchorMenu;
        Quaternion anchorMenuRot;
        Text anchorBtnText;

        void UpdateAnchorBtn()
        {
            if (anchorBtnText == null) return;
            anchorBtnText.text = Ar6dofLoc.T(anchorStep == 0 ? "Test d'ancrage du menu : depart" : "Test d'ancrage : arrivee (j'ai bouge)");
        }

        void AnchorTest()
        {
            var rig = dock.rig;
            if (rig == null || !rig.HasPose || rig.ConnectionLost || canvas == null) { Say("Suivi indisponible"); return; }
            Transform cam = dock.HeadTransform;
            if (anchorStep == 0)
            {
                anchorRaw = rig.RawPosition;
                anchorCam = cam != null ? cam.position : rig.CameraStep;
                anchorMenu = canvas.transform.position;
                anchorMenuRot = canvas.transform.rotation;
                anchorStep = 1;
                Say("Depart note : deplace-toi (50 cm), puis clique a l'arrivee");
            }
            else
            {
                Vector3 dr = rig.RawPosition - anchorRaw;
                Vector3 dc = (cam != null ? cam.position : rig.CameraStep) - anchorCam;
                Vector3 dm = canvas.transform.position - anchorMenu;
                float rot = Quaternion.Angle(canvas.transform.rotation, anchorMenuRot);
                anchorStep = 0;
                var ptd = dock.passthrough;
                statusMsg = Ar6dofLoc.F("Test d'ancrage : suivi {0} cm, camera d'Unity {1} cm, MENU {2} cm (doit etre 0), rotation du menu {3} deg. Ecart des yeux de la scene : {4} mm (fichier {5})",
                    Mathf.RoundToInt(dr.magnitude * 100f), Mathf.RoundToInt(dc.magnitude * 100f), Mathf.RoundToInt(dm.magnitude * 100f), Mathf.RoundToInt(rot),
                    ptd != null ? Mathf.RoundToInt(ptd.SceneBaselineMm) : 0, ptd != null && ptd.ModelFileShort.Length > 0 ? ptd.ModelFileShort : "aucun");
                statusUntil = Time.unscaledTime + 15f;
                UpdateStatus();
            }
            UpdateAnchorBtn();
        }

        void ScaleMeasure()
        {
            var rig = dock.rig;
            if (rig == null || !rig.HasPose || rig.ConnectionLost) { Say("Suivi indisponible"); return; }
            if (scaleStep == 0)
            {
                scaleStart = rig.RawPosition;
                scaleCamStart = dock.HeadTransform != null ? dock.HeadTransform.position : rig.CameraStep;
                scaleStep = 1;
                Say("Depart note : avance en ligne droite, puis clique a l'arrivee");
            }
            else
            {
                Vector3 d = rig.RawPosition - scaleStart;
                d.y = 0f;
                // ce que la camera d'Unity a vraiment parcouru (doit valoir la mesure du suivi x l'echelle actuelle : sinon la perte est dans Unity)
                Vector3 dc = (dock.HeadTransform != null ? dock.HeadTransform.position : rig.CameraStep) - scaleCamStart;
                dc.y = 0f;
                float camCm = dc.magnitude * 100f;
                scaleStep = 0;
                if (d.magnitude < 0.15f) Say("Deplacement trop court : recommence");
                else
                {
                    float k = Mathf.Clamp(scaleDistCm * 0.01f / d.magnitude, 0.25f, 4f);
                    Ar6dofSettings.Current.moveScale = k;
                    Touch();
                    statusMsg = Ar6dofLoc.F("Echelle reglee a {0} (le suivi voyait {1} cm, la camera d'Unity {2} cm)", k.ToString("0.00"), Mathf.RoundToInt(d.magnitude * 100f), Mathf.RoundToInt(camCm));
                    statusUntil = Time.unscaledTime + 5f;
                    UpdateStatus();
                }
            }
            UpdateScaleBtn();
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

        void BuildHead(RectTransform page)
        {
            float c = BeginPage(page, PgHead);
            float top = OpenCard(page, ref c, "Suivi de la tete");
            InfoLine(page, Next(ref c, 28f, 4f), PgHead, () =>
            {
                var rig = dock.rig;
                if (rig == null) return Ar6dofLoc.T("Tete : casque introuvable");
                if (!rig.HasPose) return Ar6dofLoc.T("Tete : en attente du casque");
                if (rig.ConnectionLost) return Ar6dofLoc.T("Tete : liaison perdue");
                return Ar6dofLoc.T(rig.TrackingOk ? "Tete : suivi OK" : "Tete : suivi degrade");
            });
            InfoLine(page, Next(ref c, 28f, 12f), PgHead, () =>
            {
                var rig = dock.rig;
                if (rig == null || !rig.HasPose) return "";
                string src = rig.FloorMode == 2 ? "verrouille" : (rig.FloorDetected ? "detecte" : "estime");
                return Ar6dofLoc.F("Sol : {0}   -   hauteur des yeux : {1} cm", Ar6dofLoc.T(src), Mathf.RoundToInt(rig.HeadHeight * 100f));
            });
            if (dock.launcher != null)
            {
                float y = Next(ref c, 48f, 12f);
                Btn(page, Col3(0), y, 208f, "Redemarrer le suivi", () => { dock.launcher.Send('T'); if (dock.rig != null) dock.rig.ResetWorld(); }, 18);
                // le verrouillage se fait AUSSI dans Unity : y = 0 reste exactement ou il est (avant, seul le calcul de la sonde etait fige, donc rien ne se voyait)
                lockFloorText = ButtonText(page, Col3(1), y, 208f, "Verrouiller le sol", () =>
                {
                    if (dock.rig != null) dock.rig.ToggleFloorLock();
                    dock.launcher.Send('V');
                    UpdateLockBtn();
                }, 18);
                // le cap du monde est celui d'Unity (le « cap » de la sonde ne concerne que l'IMU) : on remet l'avant sur ton regard et on ramene les menus devant toi
                Btn(page, Col3(2), y, 208f, "Remettre le cap a zero", () =>
                {
                    dock.launcher.Send('Z');
                    if (dock.rig != null) dock.rig.RecenterYaw();
                    dock.BringMenusHere();
                }, 18);
                refreshers.Add(UpdateLockBtn);
            }
            StepRow(page, ref c, "Hauteur de tes yeux debout (cm)", 100f, 210f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.eyeHeightCm), v => { Ar6dofSettings.Current.eyeHeightCm = v; Touch(); }, "0");
            StepRow(page, ref c, "Hauteur de tes yeux assis (cm)", 60f, 160f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.eyeHeightSeatedCm), v => { Ar6dofSettings.Current.eyeHeightSeatedCm = v; Touch(); }, "0");
            ButtonText(page, 0f, Next(ref c, 48f, 12f), 520f, "Sol ici : mes yeux sont a cette hauteur", () => { if (dock.rig != null && dock.rig.HasPose) dock.rig.SetFloorFromEyeHeight(); }, 20);
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Suivi actif : les menus me suivent (non = ancres dans la piece)", () => Ar6dofSettings.Current.menusFollow, v => { Ar6dofSettings.Current.menusFollow = v; Touch(); }, 600f);
            // échelle des déplacements : si les menus "reculent" quand tu avances, le suivi sous-estime tes pas ; on la mesure avec un pas connu
            StepRow(page, ref c, "Distance d'ouverture des fenetres (cm, a la prochaine ouverture)", 30f, 200f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.openDistance * 100f), v => { Ar6dofSettings.Current.openDistance = v / 100f; Touch(); }, "0");
            StepRow(page, ref c, "Echelle des deplacements (%, 100 = exact)", 40f, 400f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.moveScale * 100f), v => { Ar6dofSettings.Current.moveScale = v / 100f; Touch(); }, "0");
            StepRow(page, ref c, "Distance que tu vas parcourir (cm)", 30f, 300f, 1f, 10f, 50f, () => scaleDistCm, v => { scaleDistCm = Mathf.Round(v); UpdateScaleBtn(); }, "0");
            scaleBtnText = ButtonText(page, 0f, Next(ref c, 48f, 12f), 520f, "", ScaleMeasure, 20);
            refreshers.Add(UpdateScaleBtn);
            anchorBtnText = ButtonText(page, 0f, Next(ref c, 48f, 12f), 520f, "", AnchorTest, 20);
            refreshers.Add(UpdateAnchorBtn);
            if (dock.passthrough != null)
                Toggle(page, 0f, Next(ref c, 48f, 12f), "Test sans relief (meme image aux deux yeux)", () => dock.passthrough.settings.monoTest, v => dock.passthrough.settings.monoTest = v, 520f);
            CloseCard(page, top, ref c);

            Finish(PgHead, c);
        }

        void BuildRemote(RectTransform page)
        {
            float c = BeginPage(page, PgRemote);
            // télécommande : état, puis UN bouton de calibration
            float top = OpenCard(page, ref c, "Telecommande");
            InfoLine(page, Next(ref c, 28f, 8f), PgRemote, () =>
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
            StepRow(page, ref c, "Anticipation (ms)", 0f, 40f, 1f, 5f, 10f, () => Ar6dofSettings.Current.remotePredictMs, v => { Ar6dofSettings.Current.remotePredictMs = v; Touch(); }, "0");
            CloseCard(page, top, ref c);

            Finish(PgRemote, c);
        }

        // ================================================================== PERFORMANCES (onglet Interface)
        void BuildPerf(RectTransform page)
        {
            float c = BeginPage(page, PgPerf);
            // limiter les images par seconde évite que le GPU tourne à fond pour rien
            float top = OpenCard(page, ref c, "Performances (GPU)");
            Ar6dofVrUi.Label(page, "Images par seconde maximum", new Vector2(0f, Next(ref c, 28f, 8f)), new Vector2(CW, 28f), 20, TextAnchor.MiddleLeft);
            float fy = Next(ref c, 46f, 10f);
            for (int i = 0; i < Ar6dofPerf.FpsNames.Length; i++)
            {
                int fi = i;
                fpsBtns.Add(Btn(page, -259f + 129.5f * i, fy, 124f, Ar6dofPerf.FpsNames[i], () => { Ar6dofSettings.Current.perfFps = fi; Touch(); }, 18, 46f));
            }
            refreshers.Add(() => { for (int i = 0; i < fpsBtns.Count; i++) Ar6dofTheme.SetSelected(fpsBtns[i], Ar6dofSettings.Current.perfFps == i); });
            InfoLine(page, Next(ref c, 28f, 12f), PgPerf, () => Ar6dofLoc.F("Images par seconde actuelles : {0}", Ar6dofPerf.Fps));
            Ar6dofVrUi.Label(page, "Lissage des bords (net, sans flou)", new Vector2(0f, Next(ref c, 28f, 8f)), new Vector2(CW, 28f), 20, TextAnchor.MiddleLeft);
            float ay = Next(ref c, 46f, 6f);
            for (int i = 0; i < Ar6dofPerf.AaNames.Length; i++)
            {
                int ai = i;
                aaBtns.Add(Btn(page, -259f + 172.67f * i, ay, 166f, Ar6dofPerf.AaNames[i], () => { Ar6dofSettings.Current.perfAa = ai; Touch(); }, 18, 46f));
            }
            refreshers.Add(() => { for (int i = 0; i < aaBtns.Count; i++) Ar6dofTheme.SetSelected(aaBtns[i], Ar6dofSettings.Current.perfAa == i); });
            Ar6dofVrUi.Label(page, "MSAA materiel : bords nets, s'applique tout de suite. x4 conseille ; x8 seulement si le GPU suit.", new Vector2(0f, Next(ref c, 44f, 12f)), new Vector2(CW, 44f), 16, TextAnchor.UpperLeft);
            StepRow(page, ref c, "Resolution de l'interface (%, au prochain lancement)", 50f, 100f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.perfRenderScale * 100f), v => { Ar6dofSettings.Current.perfRenderScale = v / 100f; Touch(); }, "0");
            CloseCard(page, top, ref c);

            // ---- mesures du PC : lues seulement tant que cette page est affichee (voir Ar6dofSysInfo)
            top = OpenCard(page, ref c, "Systeme");
            Live(PgPerf, () => { if (curTab == PgPerf) Ar6dofSysInfo.Touch(); });
            if (!Ar6dofSysInfo.Supported)
            {
                Para(page, ref c, "Mesures disponibles sous Windows seulement.", 30, 19);
            }
            else
            {
                InfoLine(page, Next(ref c, 28f, 4f), PgPerf, () =>
                {
                    var m = Ar6dofSysInfo.Current;
                    if (!m.valid) return Ar6dofLoc.T("Mesure en cours...");
                    string t = float.IsNaN(m.cpuTemp) ? Ar6dofLoc.T("temperature indisponible") : Ar6dofLoc.F("{0} C (capteur ACPI)", Mathf.RoundToInt(m.cpuTemp));
                    return Ar6dofLoc.F("Processeur : {0} %   -   {1}", Pct(m.cpuTotal), t);
                });
                float sy = Next(ref c, 64f, 4f);
                var coreBars = Ar6dofSpectrum.Make(page, new Vector2(0f, sy), new Vector2(CW, 64f), Mathf.Max(1, Environment.ProcessorCount));
                Live(PgPerf, () => { var m = Ar6dofSysInfo.Current; if (m.valid && m.cores.Length > 0) coreBars.Set(m.cores); });
                Ar6dofVrUi.Label(page, Ar6dofLoc.T("Une barre = un coeur (thread) du processeur."), new Vector2(0f, Next(ref c, 24f, 10f)), new Vector2(CW, 24f), 16, TextAnchor.MiddleLeft);
                InfoLine(page, Next(ref c, 28f, 4f), PgPerf, () =>
                {
                    var m = Ar6dofSysInfo.Current;
                    if (!m.valid) return "";
                    return Ar6dofLoc.F("Memoire : {0} %   ({1} / {2} Go)", Pct(m.ramPct), m.ramUsedGb.ToString("0.0", CultureInfo.InvariantCulture), m.ramTotalGb.ToString("0.0", CultureInfo.InvariantCulture));
                });
                InfoLine(page, Next(ref c, 28f, 4f), PgPerf, () =>
                {
                    var m = Ar6dofSysInfo.Current;
                    if (!m.valid) return "";
                    if (float.IsNaN(m.gpuLoad) && float.IsNaN(m.gpuTemp)) return Ar6dofLoc.T("Carte graphique : mesure indisponible");
                    string name = string.IsNullOrEmpty(m.gpuName) ? Ar6dofLoc.T("Carte graphique") : m.gpuName;
                    string load = float.IsNaN(m.gpuLoad) ? "-" : Pct(m.gpuLoad) + " %";
                    string temp = float.IsNaN(m.gpuTemp) ? Ar6dofLoc.T("temperature indisponible") : Mathf.RoundToInt(m.gpuTemp) + " C";
                    return name + " : " + load + "   -   " + temp;
                });
                InfoLine(page, Next(ref c, 28f, 4f), PgPerf, () =>
                {
                    var m = Ar6dofSysInfo.Current;
                    if (!m.valid || float.IsNaN(m.vramTotalGb)) return "";
                    return Ar6dofLoc.F("Memoire video : {0} / {1} Go", m.vramUsedGb.ToString("0.0", CultureInfo.InvariantCulture), m.vramTotalGb.ToString("0.0", CultureInfo.InvariantCulture));
                });
                InfoLine(page, Next(ref c, 28f, 12f), PgPerf, () =>
                {
                    var m = Ar6dofSysInfo.Current;
                    if (!m.valid) return "";
                    if (!m.hasBattery) return Ar6dofLoc.T("Batterie : aucune (alimentation secteur)");
                    string pct = float.IsNaN(m.batteryPct) ? "?" : Pct(m.batteryPct);
                    string state;
                    if (m.charging) state = Ar6dofLoc.T("en charge");
                    else if (m.onAc) state = Ar6dofLoc.T("sur secteur");
                    else if (m.batterySeconds >= 0) state = Ar6dofLoc.F("{0} h {1} min restantes", m.batterySeconds / 3600, (m.batterySeconds % 3600) / 60);
                    else state = Ar6dofLoc.T("temps restant inconnu");
                    return Ar6dofLoc.F("Batterie : {0} %   -   {1}", pct, state);
                });
            }
            CloseCard(page, top, ref c);
            Finish(PgPerf, c);
        }

        static string Pct(float v) { return float.IsNaN(v) ? "?" : Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture); }

        static string CamTxt(int state, float fps)
        {
            if (state < 0) return Ar6dofLoc.T("aucune");
            return state == 0 ? Ar6dofLoc.T("arretee") : Ar6dofLoc.F("{0} fps", Mathf.RoundToInt(fps));
        }

        void BuildEyes(RectTransform page)
        {
            var et = dock.eyes;
            float c = BeginPage(page, PgEyes);
            if (et == null)
            {
                Ar6dofVrUi.Label(page, "Eye tracking introuvable", new Vector2(0f, c - 30f), new Vector2(CW, 40f), 24);
                Finish(PgEyes, c - 60f);
                return;
            }
            float top = OpenCard(page, ref c, "Eye tracking");
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Eye tracking actif", () => et.Enabled, v => et.SetEnabled(v), 400f);
            Text l1 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l2 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l3 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l4 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 12f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Live(PgEyes, () =>
            {
                var st = et.Status;
                var g = et.Gaze;
                if (!et.Enabled) l1.text = Ar6dofLoc.T("Desactive : aucune camera, aucun port, aucune charge");
                else if (!et.EngineOnline) l1.text = Ar6dofLoc.T("ar6dof_probe ne repond pas (coche Eye tracking dans l'application ar6dof_app)");
                else l1.text = Ar6dofLoc.T("ar6dof_probe : en ligne") + (st.on ? "" : Ar6dofLoc.T(" (cameras arretees)"));
                bool on = et.Enabled && et.EngineOnline;
                l2.text = on ? Ar6dofLoc.F("Cameras : gauche {0}   droite {1}", CamTxt(st.camL, st.fpsL), CamTxt(st.camR, st.fpsR)) : "";
                l3.text = on ? Ar6dofLoc.F("Etalonnage : {0}   pupille : gauche {1}, droite {2}", Ar6dofLoc.T((st.calL || st.calR) ? "fait" : "a faire"),
                    Ar6dofLoc.T(st.seeL ? "vue" : "non"), Ar6dofLoc.T(st.seeR ? "vue" : "non")) : "";
                if (on && et.Receiving)
                {
                    if (g.valid)
                        l4.text = Ar6dofLoc.F("Regard : {0} deg (horizontal)   {1} deg (vertical)", g.yaw.ToString("+0.0;-0.0;0.0"), g.pitch.ToString("+0.0;-0.0;0.0"))
                            + ((g.blinkL || g.blinkR) ? Ar6dofLoc.T("   clignement") : "")
                            + Ar6dofLoc.F("   paupieres : gauche {0} %, droite {1} %", Mathf.RoundToInt(Mathf.Clamp01(g.openL) * 100f), Mathf.RoundToInt(Mathf.Clamp01(g.openR) * 100f));
                    else
                        l4.text = Ar6dofLoc.T(g.calibrated ? "Regard : oeil non detecte" : "Regard : pas d'etalonnage");
                }
                else l4.text = "";
            });
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Apercu des cameras d'oeil");
            BuildPreviewBox(page, ref c, true);
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Etalonnage des yeux");
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
            Finish(PgEyes, c);
        }


        // ================================================================== APERCU DES CAMERAS (a la demande)
        /// <summary>Cadre d'aperçu 480x180 + bouton « Voir l'aperçu ». Rien ne tourne (ni caméra ni copie d'image) tant que l'aperçu n'est pas demandé.</summary>
        void BuildPreviewBox(RectTransform page, ref float c, bool eyes)
        {
            float y = Next(ref c, 48f, 12f);
            Text bt = ButtonText(page, 0f, y, 440f, "Voir l'apercu", () => TogglePreview(eyes), 22);
            if (eyes) eyePvBtn = bt; else mouthPvBtn = bt;
            float py = Next(ref c, 180f, 12f);
            var bg = Ar6dofVrUi.Rect(page, "ApercuFond", new Vector2(0f, py), new Vector2(480f, 180f));
            var bgi = bg.gameObject.AddComponent<Image>();
            bgi.color = new Color(0.09f, 0.09f, 0.09f, 1f);
            bgi.raycastTarget = false;
            var rawRt = Ar6dofVrUi.Rect(bg, "Apercu", Vector2.zero, new Vector2(480f, 180f));
            var raw = rawRt.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            Text msg = Ar6dofVrUi.Label(bg, "", Vector2.zero, new Vector2(460f, 60f), 20);
            msg.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (eyes) { eyePvImg = raw; eyePvMsg = msg; } else { mouthPvImg = raw; mouthPvMsg = msg; }
            UpdatePreviewUi();
        }

        void TogglePreview(bool eyes)
        {
            if (eyes) SetPreview(true, !eyePvOn);
            else SetPreview(false, !mouthPvOn);
        }

        /// <summary>Démarre / arrête l'aperçu. S'il faut pour cela activer le suivi, il est réactivé... puis désactivé avec l'aperçu (rien ne reste allumé).</summary>
        void SetPreview(bool eyes, bool on)
        {
            if (eyes)
            {
                if (on == eyePvOn) return;
                eyePvOn = on;
                var et = dock.eyes;
                if (on && et != null && !et.Enabled) { et.SetEnabled(true, false); eyePvAuto = true; }
                else if (!on && eyePvAuto && et != null) { et.SetEnabled(false, false); eyePvAuto = false; }
            }
            else
            {
                if (on == mouthPvOn) return;
                mouthPvOn = on;
                var mt = dock.mouth;
                if (on && mt != null && !mt.Enabled) { mt.SetEnabled(true, false); mouthPvAuto = true; }
                else if (!on && mouthPvAuto && mt != null) { mt.SetEnabled(false, false); mouthPvAuto = false; }
            }
            UpdatePreviewUi();
        }

        void UpdatePreviewUi()
        {
            if (eyePvBtn != null) eyePvBtn.text = Ar6dofLoc.T(eyePvOn ? "Arreter l'apercu" : "Voir l'apercu");
            if (mouthPvBtn != null) mouthPvBtn.text = Ar6dofLoc.T(mouthPvOn ? "Arreter l'apercu" : "Voir l'apercu");
        }

        /// <summary>Appelé à chaque image par Tick() : n'envoie et ne copie rien tant qu'aucun aperçu n'est affiché.</summary>
        void PumpPreview()
        {
            PumpMouthScreensPreview();
            // l'aperçu s'arrête dès qu'on quitte sa page (ou que le menu se ferme : Tick n'est plus appelé et ar6dof_probe coupe seul au bout de 3 s)
            if (eyePvOn && curTab != PgEyes) SetPreview(true, false);
            if (mouthPvOn && curTab != PgMouth) SetPreview(false, false);
            int src = eyePvOn ? Ar6dofCameraPreview.Eyes : mouthPvOn ? Ar6dofCameraPreview.Mouth : 0;
            camPreview.Pump(src);
            if (src == 0)
            {
                if (camPreview.Texture != null) camPreview.Stop();   // libère la texture
                if (eyePvImg != null) eyePvImg.enabled = false;
                if (mouthPvImg != null) mouthPvImg.enabled = false;
                if (eyePvMsg != null) eyePvMsg.text = Ar6dofLoc.T("Apercu desactive");
                if (mouthPvMsg != null) mouthPvMsg.text = Ar6dofLoc.T("Apercu desactive");
                return;
            }
            bool has = camPreview.HasFrame;
            var img = src == Ar6dofCameraPreview.Eyes ? eyePvImg : mouthPvImg;
            var msg = src == Ar6dofCameraPreview.Eyes ? eyePvMsg : mouthPvMsg;
            if (img != null)
            {
                if (img.texture != camPreview.Texture) img.texture = camPreview.Texture;
                img.enabled = has;
            }
            if (msg != null) msg.text = has ? "" : Ar6dofLoc.T("En attente de la camera...");
        }

        // ================================================================== BOUCHE
        RawImage mdPvFull, mdPvA, mdPvB;

        void BuildMouth(RectTransform page)
        {
            var mt = dock.mouth;
            var md = dock.mouthDisplay;
            float c = BeginPage(page, PgMouth);
            if (mt == null)
            {
                Ar6dofVrUi.Label(page, "Mouth tracking introuvable", new Vector2(0f, c - 30f), new Vector2(CW, 40f), 24);
                Finish(PgMouth, c - 60f);
                return;
            }
            float top = OpenCard(page, ref c, "Mouth tracking");
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Mouth tracking actif", () => mt.Enabled, v => mt.SetEnabled(v), 400f);
            Text l1 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l2 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l3 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 4f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Text l4 = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 12f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Live(PgMouth, () =>
            {
                var st = mt.Status;
                var m = mt.Mouth;
                if (!mt.Enabled) l1.text = Ar6dofLoc.T("Desactive : aucune camera, aucun port, aucune charge");
                else if (!mt.EngineOnline) l1.text = Ar6dofLoc.T("ar6dof_probe ne repond pas (coche Mouth tracking dans l'application ar6dof_app)");
                else l1.text = Ar6dofLoc.T("ar6dof_probe : en ligne") + (st.on ? "" : Ar6dofLoc.T(" (camera arretee)"));
                bool on = mt.Enabled && mt.EngineOnline;
                l2.text = on ? Ar6dofLoc.F("Camera : {0}   Etalonnage : {1}", CamTxt(st.cam, st.fps), Ar6dofLoc.T(st.calibrated ? "fait" : "a faire")) : "";
                l3.text = on ? Ar6dofLoc.T(st.ml ? "Methode : modele IA (45 parametres)" : "Methode : detection classique (pas de babble_model.onnx)") : "";
                if (on && mt.Receiving)
                    l4.text = m.valid ? Ar6dofLoc.F("Ouverture {0}   largeur {1}   cote {2}", m.jawOpen.ToString("0.00"), m.wide.ToString("+0.00;-0.00;0.00"), m.side.ToString("+0.00;-0.00;0.00"))
                                      : Ar6dofLoc.T("Pas d'etalonnage");
                else l4.text = "";
            });
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Apercu de la camera de bouche");
            BuildPreviewBox(page, ref c, false);
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Etalonnage de la bouche");
            float y = Next(ref c, 48f, 12f);
            Text tNeutral = ButtonText(page, -164f, y, 320f, "1 - Bouche neutre", () => mt.Calibrate("neutral"), 22);
            Text tOpen = ButtonText(page, 164f, y, 320f, "2 - Grande ouverture", () => mt.Calibrate(mt.Status.ml ? "range" : "open"), 22);
            y = Next(ref c, 48f, 12f);
            Button bWide = Btn(page, -164f, y, 320f, "3 - Sourire large", () => mt.Calibrate("wide"), 22);
            Button bNarrow = Btn(page, 164f, y, 320f, "4 - Bouche serree", () => mt.Calibrate("narrow"), 22);
            y = Next(ref c, 48f, 12f);
            Btn(page, 0f, y, 440f, "Effacer l'etalonnage", () => mt.ClearCalibration(), 22);
            Text help = Para(page, ref c, "", 120f);
            Live(PgMouth, () =>
            {
                bool ml = mt.Status.ml;
                tNeutral.text = Ar6dofLoc.T(ml ? "1 - Visage neutre" : "1 - Bouche neutre");
                tOpen.text = Ar6dofLoc.T(ml ? "2 - Toutes les expressions (8 s)" : "2 - Grande ouverture");
                bWide.gameObject.SetActive(!ml);
                bNarrow.gameObject.SetActive(!ml);
                help.text = Ar6dofLoc.T(ml
                    ? "Etape 1 : visage detendu, bouche fermee. Etape 2 : pendant 8 s, fais toutes\nles expressions (grande ouverture, sourire, moue, langue, joues...).\nLes 45 parametres sont lus dans Ar6dofMouthTracker.Instance.GetShapes()."
                    : "Clique sur une etape puis prends l'expression tout de suite et garde-la\n1,5 s. Les etapes 1 et 2 suffisent pour l'ouverture ; 3 et 4 reglent la\nlargeur. Les valeurs sont lues dans Ar6dofMouthTracker.Instance.Mouth.");
            });
            CloseCard(page, top, ref c);

            if (md != null) BuildMouthScreens(page, ref c, md);
            Finish(PgMouth, c);
        }

        /// <summary>Cartes « Ecrans du costume » et « Apercu de la bouche » : la bouche suivie, dessinée sur les écrans du costume.</summary>
        void BuildMouthScreens(RectTransform page, ref float c, Ar6dofMouthDisplay md)
        {
            var cfg = md.Config;
            Action<Action> mod = a => { a(); md.Touch(); RefreshAll(); };
            float top = OpenCard(page, ref c, "Ecrans du costume");
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Afficher la bouche sur les ecrans du costume", () => md.Enabled, v => { md.SetEnabled(v); RefreshAll(); }, 640f);
            Text info = Ar6dofVrUi.Label(page, "", new Vector2(0f, Next(ref c, 30f, 12f)), new Vector2(CW, 30f), 20, TextAnchor.MiddleLeft);
            Live(PgMouth, () =>
            {
                string miss = md.Describe();
                info.text = !md.Enabled ? Ar6dofLoc.T("Les ecrans du costume sont des moniteurs : branche-les avant de lancer l'application") : miss;
            });

            float y = Next(ref c, 48f, 12f);
            Button bFace = Btn(page, -164f, y, 320f, "Face : 1 ecran", () => mod(() => cfg.layout = 0), 22);
            Button bSide = Btn(page, 164f, y, 320f, "Cotes : 2 ecrans", () => mod(() => cfg.layout = 1), 22);
            refreshers.Add(() => { Ar6dofTheme.SetSelected(bFace, cfg.layout == 0); Ar6dofTheme.SetSelected(bSide, cfg.layout == 1); });

            Ar6dofVrUi.Label(page, "Forme du costume", new Vector2(0f, Next(ref c, 28f, 6f)), new Vector2(CW, 28f), 20, TextAnchor.MiddleLeft);
            y = Next(ref c, 48f, 12f);
            for (int k = 0; k < Ar6dofMouthDisplay.PresetNames.Length; k++)
            {
                int kk = k;
                Btn(page, Col3(k), y, 208f, Ar6dofMouthDisplay.PresetNames[k], () => { md.ApplyPreset(kk); RefreshAll(); }, 17);
            }
            SliderRow(page, ref c, "Taille de la bouche", 0.3f, 1.6f, () => cfg.size, v => { cfg.size = v; md.Touch(); }, "0.00");
            SliderRow(page, ref c, "Etirement horizontal (museau long)", 0.5f, 3f, () => cfg.stretch, v => { cfg.stretch = v; md.Touch(); }, "0.00");
            SliderRow(page, ref c, "Courbure", -1f, 1f, () => cfg.curve, v => { cfg.curve = v; md.Touch(); }, "0.00");
            SliderRow(page, ref c, "Ouverture en coin (machoire)", 0f, 1f, () => cfg.hinge, v => { cfg.hinge = v; md.Touch(); }, "0.00");
            SliderRow(page, ref c, "Epaisseur des levres", 0f, 1f, () => cfg.thick, v => { cfg.thick = v; md.Touch(); }, "0.00");
            SliderRow(page, ref c, "Decalage horizontal", -1f, 1f, () => cfg.offsetX, v => { cfg.offsetX = v; md.Touch(); }, "0.00");
            SliderRow(page, ref c, "Decalage vertical", -1f, 1f, () => cfg.offsetY, v => { cfg.offsetY = v; md.Touch(); }, "0.00");

            y = Next(ref c, 48f, 12f);
            Button bDraw = Btn(page, -164f, y, 320f, "Dessin (remplie)", () => mod(() => cfg.style = 0), 22);
            Button bLine = Btn(page, 164f, y, 320f, "Trait (contour)", () => mod(() => cfg.style = 1), 22);
            refreshers.Add(() => { Ar6dofTheme.SetSelected(bDraw, cfg.style == 0); Ar6dofTheme.SetSelected(bLine, cfg.style == 1); });
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Dents", () => cfg.teeth, v => { cfg.teeth = v; md.Touch(); }, 400f);
            y = Next(ref c, 48f, 12f);
            Text tPal = ButtonText(page, 0f, y, 440f, "", () => mod(() => cfg.palette = (cfg.palette + 1) % Ar6dofMouthDisplay.Palettes.Length), 22);

            y = Next(ref c, 48f, 12f);
            Text tA = ButtonText(page, -164f, y, 320f, "", () => mod(() => cfg.displayA = (cfg.displayA + 1) % 8), 22);
            Text tB = ButtonText(page, 164f, y, 320f, "", () => mod(() => cfg.displayB = (cfg.displayB + 1) % 8), 22);
            y = Next(ref c, 48f, 12f);
            Text tRA = ButtonText(page, -164f, y, 320f, "", () => mod(() => cfg.rotA = (cfg.rotA + 1) % 4), 22);
            Text tRB = ButtonText(page, 164f, y, 320f, "", () => mod(() => cfg.rotB = (cfg.rotB + 1) % 4), 22);
            y = Next(ref c, 48f, 12f);
            Toggle(page, -164f, y, "Miroir ecran A", () => cfg.flipA, v => { cfg.flipA = v; md.Touch(); }, 320f);
            Toggle(page, 164f, y, "Miroir ecran B", () => cfg.flipB, v => { cfg.flipB = v; md.Touch(); }, 320f);
            Live(PgMouth, () =>
            {
                tPal.text = Ar6dofLoc.F("Couleurs : {0}", Ar6dofLoc.T(Ar6dofMouthDisplay.Palettes[cfg.palette].name));
                tA.text = Ar6dofLoc.F(cfg.layout == 0 ? "Ecran : {0}" : "Ecran A (cote gauche) : {0}", cfg.displayA);
                tB.text = Ar6dofLoc.F("Ecran B (cote droit) : {0}", cfg.displayB);
                tB.transform.parent.gameObject.SetActive(cfg.layout == 1);
                tRA.text = Ar6dofLoc.F("Rotation A : {0} deg", cfg.rotA * 90);
                tRB.text = Ar6dofLoc.F("Rotation B : {0} deg", cfg.rotB * 90);
                tRB.transform.parent.gameObject.SetActive(cfg.layout == 1);
            });
            Para(page, ref c, "Ecran 0 = ecran principal (celui du casque) : choisis les numeros des ecrans du costume, comme dans les parametres d'affichage de Windows.\nEn mode Cotes, l'ecran A montre la moitie gauche de la bouche, l'ecran B la moitie droite.", 100f);
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Apercu de la bouche");
            float py = Next(ref c, 180f, 12f);
            var bg = Ar6dofVrUi.Rect(page, "ApercuBouche", new Vector2(0f, py), new Vector2(480f, 180f));
            var bgi = bg.gameObject.AddComponent<Image>();
            bgi.color = new Color(0.09f, 0.09f, 0.09f, 1f);
            bgi.raycastTarget = false;
            mdPvFull = MakeRaw(bg, "Plein", 0f, 480f);
            mdPvA = MakeRaw(bg, "A", -121f, 238f);
            mdPvB = MakeRaw(bg, "B", 121f, 238f);
            Para(page, ref c, "L'apercu n'existe que tant que cette page est ouverte : la bouche est dessinee avec les memes reglages que sur les ecrans du costume.", 56f);
            CloseCard(page, top, ref c);
        }

        static RawImage MakeRaw(RectTransform parent, string name, float x, float w)
        {
            var rt = Ar6dofVrUi.Rect(parent, name, new Vector2(x, 0f), new Vector2(w, 180f));
            var r = rt.gameObject.AddComponent<RawImage>();
            r.raycastTarget = false;
            r.enabled = false;
            return r;
        }

        /// <summary>Apercu des ecrans du costume : seulement tant que la page Bouche est visible (sinon aucune camera, aucun maillage).</summary>
        void PumpMouthScreensPreview()
        {
            var md = dock.mouthDisplay;
            if (md == null || mdPvFull == null) return;
            if (curTab != PgMouth) { mdPvFull.enabled = mdPvA.enabled = mdPvB.enabled = false; return; }
            md.RequestPreview();
            bool sides = md.Config.layout == 1;
            Texture a = md.PreviewA, b = md.PreviewB;
            mdPvFull.texture = sides ? null : a;
            mdPvFull.enabled = !sides && a != null;
            mdPvA.texture = sides ? a : null;
            mdPvA.enabled = sides && a != null;
            mdPvB.texture = sides ? b : null;
            mdPvB.enabled = sides && b != null;
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
            scrolls[PgStyle].SetOffset(0f);
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
                var sb = Btn(page, Col4(i), Start - 23f, 156f, cats[i], () => SelectSub(k), 22, 46f);
                subButtons.Add(sb);
                styleDots.Add(Dot(sb.transform, new Vector2(60f, 15f)));
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
                StepRow(d.rt, ref c, "Taille des boutons de fenetre (%)", 60f, 250f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.chromeScale * 100f), v => { Ar6dofSettings.Current.chromeScale = v / 100f; Touch(); }, "0");
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


        // ================================================================== CAMERAS (onglet Image)
        // Cameras supplementaires (vision nocturne, thermique, autre). Une camera est choisie en haut ; ses reglages sont rangés dans quatre sous-menus
        // (Source, Image, Type, LED IR) pour ne pas tout mettre sur une seule page. Rien ne tourne ici : une camera ne demarre que quand on s'en sert.
        // Cette page s'applique tout de suite et s'enregistre toute seule (ar6dof_cameras.json).
        int camSel, camSub;
        GameObject camBody;
        readonly List<GameObject> camGroups = new List<GameObject>();
        readonly List<string> usbNames = new List<string>();
        bool usbListed, camTestLed;
        float camDelArm;
        static readonly string[] CamSubNames = { "Source", "Image", "Type", "LED IR" };
        static readonly int[] CamResW = { 320, 640, 800, 1280, 1920 }, CamResH = { 240, 480, 600, 720, 1080 }, CamFps = { 15, 30, 60 };

        static Ar6dofCameras Cams { get { return Ar6dofCameras.Instance; } }

        Ar6dofCamCfg CamSel
        {
            get
            {
                var h = Cams;
                if (h == null || h.cams.Count == 0) return null;
                camSel = Mathf.Clamp(camSel, 0, h.cams.Count - 1);
                return h.cams[camSel];
            }
        }

        void CamSet(Action<Ar6dofCamCfg> a)
        {
            var sel = CamSel;
            if (sel == null) return;
            a(sel);
            Cams.MarkChanged(sel);
        }

        T CamGet<T>(Func<Ar6dofCamCfg, T> f, T def)
        {
            var sel = CamSel;
            return sel == null ? def : f(sel);
        }

        void ListUsb()
        {
            usbNames.Clear();
            usbListed = true;
            try { foreach (var d in WebCamTexture.devices) usbNames.Add(d.name); }
            catch (Exception) { }
        }

        /// <summary>Choix dans une liste qui change (appareils USB, menus Bluetooth...) : [<] texte [>]. La premiere valeur est « aucun / automatique » (texte vide).</summary>
        void DynCycle(RectTransform p, ref float c, string label, Func<List<string>> items, Func<string> get, Action<string> set, string none)
        {
            float y = Next(ref c, 48f, 12f);
            Text lbl = Ar6dofVrUi.Label(p, "", new Vector2(0f, y), new Vector2(CW - 160f, 48f), 22, TextAnchor.MiddleCenter);
            lbl.gameObject.AddComponent<Ar6dofFitText>().Init(lbl, 22);
            Action upd = () =>
            {
                string g = get();
                lbl.text = Ar6dofLoc.T(label) + " : " + (string.IsNullOrEmpty(g) ? Ar6dofLoc.T(none) : g);
            };
            Action<int> step = d =>
            {
                var all = new List<string> { "" };
                all.AddRange(items());
                int i = Mathf.Max(0, all.IndexOf(get()));
                set(all[(i + d + all.Count) % all.Count]);
                upd();
            };
            Text t;
            Ar6dofVrUi.MakeButton(p, "<", new Vector2(-CW * 0.5f + 36f, y), new Vector2(72f, 48f), () => step(-1), out t, 26);
            Ar6dofVrUi.MakeButton(p, ">", new Vector2(CW * 0.5f - 36f, y), new Vector2(72f, 48f), () => step(1), out t, 26);
            refreshers.Add(upd);
            upd();
        }

        List<string> BtMenuNames()
        {
            var l = new List<string>();
            foreach (var m in dock.DeviceMenus) l.Add(m.DisplayName);
            return l;
        }

        List<string> BtControlNames()
        {
            var sel = CamSel;
            var l = new List<string>();
            if (sel == null) return l;
            foreach (var m in dock.DeviceMenus) if (m.DisplayName == sel.ledMenu) return m.ControlLabels();
            return l;
        }

        void BuildCams(RectTransform page)
        {
            float c = BeginPage(page, PgCams);

            // ---- choix de la camera
            float top = OpenCard(page, ref c, "Cameras supplementaires");
            Para(page, ref c, "La camera normale (celle du tracking) ne change jamais. Ajoute ici une camera de vision nocturne, thermique ou autre (derriere ta tete...). Rien ne tourne tant qu'une camera n'est pas utilisee ; le bouton Vue du dock n'apparait que s'il y en a une activee.", 112f, 18);
            float y = Next(ref c, 48f, 12f);
            Text selLbl = Ar6dofVrUi.Label(page, "", new Vector2(0f, y), new Vector2(CW - 160f, 48f), 22, TextAnchor.MiddleCenter);
            selLbl.gameObject.AddComponent<Ar6dofFitText>().Init(selLbl, 22);
            Text t;
            Ar6dofVrUi.MakeButton(page, "<", new Vector2(-CW * 0.5f + 36f, y), new Vector2(72f, 48f), () => { var h = Cams; if (h != null && h.cams.Count > 0) { camSel = (camSel + h.cams.Count - 1) % h.cams.Count; RefreshAll(); } }, out t, 26);
            Ar6dofVrUi.MakeButton(page, ">", new Vector2(CW * 0.5f - 36f, y), new Vector2(72f, 48f), () => { var h = Cams; if (h != null && h.cams.Count > 0) { camSel = (camSel + 1) % h.cams.Count; RefreshAll(); } }, out t, 26);
            refreshers.Add(() =>
            {
                var h = Cams;
                var sel = CamSel;
                selLbl.text = h == null ? Ar6dofLoc.T("Cameras indisponibles")
                    : sel == null ? Ar6dofLoc.T("Aucune camera : ajoutes-en une")
                    : sel.name + "   (" + (camSel + 1) + " / " + h.cams.Count + ")";
            });
            y = Next(ref c, 48f, 14f);
            Btn(page, -164f, y, 320f, "Ajouter une camera", () => { var h = Cams; if (h == null) return; h.Add(); camSel = h.cams.Count - 1; camSub = 0; usbListed = false; RefreshAll(); });
            var delBtn = Btn(page, 164f, y, 320f, "Supprimer cette camera", () =>
            {
                var h = Cams; var sel = CamSel;
                if (h == null || sel == null) return;
                if (Time.unscaledTime > camDelArm) { camDelArm = Time.unscaledTime + 4f; RefreshAll(); return; }   // un deuxieme clic dans les 4 secondes confirme
                camDelArm = 0f;
                h.Remove(sel);
                camSel = Mathf.Max(0, camSel - 1);
                RefreshAll();
            });
            var delTxt = delBtn.GetComponentInChildren<Text>();
            refreshers.Add(() => delTxt.text = Ar6dofLoc.T(Time.unscaledTime < camDelArm ? "Confirmer : supprimer" : "Supprimer cette camera"));
            CloseCard(page, top, ref c);

            // ---- tout ce qui concerne la camera choisie (cache s'il n'y en a pas)
            var body = NewBox(page, "CameraChoisie");
            camBody = body.gameObject;
            refreshers.Add(() => body.gameObject.SetActive(CamSel != null));

            top = OpenCard(body, ref c, "Cette camera");
            float ny = Next(ref c, 50f, 12f);
            var nameField = Ar6dofVrTextField.Make(body.transform, new Vector2(0f, ny), new Vector2(CW, 50f), "", "Nom", tx => { var sel = CamSel; if (sel != null) sel.name = tx; }, tx => CamSet(sl => sl.name = tx), 22);
            nameField.maxLength = 32;
            refreshers.Add(() => { var sel = CamSel; if (sel != null && Ar6dofVrTextField.Current != nameField && nameField.text != sel.name) nameField.SetText(sel.name); });
            Toggle(body, 0f, Next(ref c, 48f, 12f), "Activee (bouton Vue du dock, application Miroir)", () => CamGet(sl => sl.enabled, false), v => CamSet(sl => sl.enabled = v), 640f);
            Toggle(body, 0f, Next(ref c, 48f, 12f), "Voir cette camera dans le casque maintenant", () => Cams != null && CamSel != null && Cams.ViewCam == CamSel, v =>
            {
                var h = Cams; var sel = CamSel;
                if (h == null || sel == null) return;
                if (v && sel.enabled && sel.role != Ar6dofCamRole.Other) h.SetView(sel); else if (!v && h.ViewCam == sel) h.SetView(null);
            }, 640f);
            InfoLine(body, Next(ref c, 28f, 4f), PgCams, () => { var sel = CamSel; return sel == null || Cams == null ? "" : Cams.StatusOf(sel); }, 19);
            InfoLine(body, Next(ref c, 28f, 12f), PgCams, () => { var sel = CamSel; return sel == null || Cams == null ? "" : Cams.LedStatusOf(sel); }, 19);
            CloseCard(body, top, ref c);

            // ---- sous-menus
            float sy = Next(ref c, 46f, 14f);
            var subBtns = new List<Button>();
            for (int k = 0; k < CamSubNames.Length; k++)
            {
                int kk = k;
                var b = Btn(body, Col4(k), sy, 156f, CamSubNames[k], () => { camSub = kk; RefreshAll(); }, 20, 46f);
                subBtns.Add(b);
            }
            refreshers.Add(() => { for (int k = 0; k < subBtns.Count; k++) Ar6dofTheme.SetSelected(subBtns[k], k == camSub); });

            float c0 = c, deepest = c;
            camGroups.Clear();
            for (int g = 0; g < 4; g++)
            {
                var gr = NewBox(body, "Sous-menu" + g);
                camGroups.Add(gr.gameObject);
                c = c0;
                if (g == 0) BuildCamSource(gr, ref c);
                else if (g == 1) BuildCamImage(gr, ref c);
                else if (g == 2) BuildCamType(gr, ref c);
                else BuildCamLed(gr, ref c);
                deepest = Mathf.Min(deepest, c);
            }
            refreshers.Add(() => { for (int g = 0; g < camGroups.Count; g++) camGroups[g].SetActive(g == camSub); });
            Finish(PgCams, deepest);
        }

        void BuildCamSource(RectTransform gr, ref float c)
        {
            float top = OpenCard(gr, ref c, "Source de l'image");
            CycleRow(gr, ref c, "Source", 2, () => CamGet(sl => sl.source, 0), v => CamSet(sl => sl.source = v), i => Ar6dofLoc.T(i == 0 ? "Camera USB" : "Flux reseau (http)"));
            float cs = c;

            var usb = NewBox(gr, "USB");
            float cu = cs;
            DynCycle(usb, ref cu, "Camera USB", () => { if (!usbListed) ListUsb(); return usbNames; }, () => CamGet(sl => sl.device, ""), v => CamSet(sl => sl.device = v), "premiere camera");
            Btn(usb, 0f, Next(ref cu, 48f, 12f), 640f, "Actualiser la liste des cameras USB", () => { ListUsb(); RefreshAll(); }, 21, 48f);
            CycleRow(usb, ref cu, "Resolution", CamResW.Length, () =>
            {
                var sel = CamSel; int best = 1;
                if (sel != null) for (int i = 0; i < CamResW.Length; i++) if (CamResW[i] == sel.width) best = i;
                return best;
            }, v => CamSet(sl => { sl.width = CamResW[v]; sl.height = CamResH[v]; }), i => CamResW[i] + " x " + CamResH[i]);
            CycleRow(usb, ref cu, "Images par seconde", CamFps.Length, () =>
            {
                var sel = CamSel; int best = 1;
                if (sel != null) for (int i = 0; i < CamFps.Length; i++) if (CamFps[i] == sel.fps) best = i;
                return best;
            }, v => CamSet(sl => sl.fps = CamFps[v]), i => CamFps[i].ToString());
            Para(usb, ref cu, "Ne choisis pas la camera du tracking : elle est deja utilisee par ar6dof_probe.", 52f, 18);

            var net = NewBox(gr, "Reseau");
            float cn = cs;
            Ar6dofVrUi.Label(net, "Adresse du flux d'images (MJPEG)", new Vector2(0f, Next(ref cn, 28f, 6f)), new Vector2(CW, 28f), 20, TextAnchor.MiddleLeft);
            var urlField = Ar6dofVrTextField.Make(net.transform, new Vector2(0f, Next(ref cn, 50f, 12f)), new Vector2(CW, 50f), "", "http://192.168.1.50:81/stream", tx => { var sel = CamSel; if (sel != null) sel.url = tx; }, tx => CamSet(sl => sl.url = tx), 20);
            urlField.maxLength = 200;
            refreshers.Add(() => { var sel = CamSel; if (sel != null && Ar6dofVrTextField.Current != urlField && urlField.text != sel.url) urlField.SetText(sel.url); });
            Para(net, ref cn, "Une camera Wi-Fi (ESP32-CAM, camera IP...) qui envoie du MJPEG. Un identifiant se met dans l'adresse : http://nom:mot@adresse/flux. Le RTSP n'est pas gere.", 100f, 18);

            c = Mathf.Min(cu, cn);
            refreshers.Add(() => { var sel = CamSel; bool u = sel == null || sel.source == 0; usb.gameObject.SetActive(u); net.gameObject.SetActive(!u); });

            CycleRow(gr, ref c, "Contenu de l'image", 3, () => CamGet(sl => sl.stereo, 0), v => CamSet(sl => sl.stereo = v), i => Ar6dofLoc.T(Ar6dofCamRole.Layouts[i]));
            Toggle(gr, 0f, Next(ref c, 48f, 12f), "Echanger gauche / droite (si deux images)", () => CamGet(sl => sl.swapEyes, false), v => CamSet(sl => sl.swapEyes = v), 640f);
            CloseCard(gr, top, ref c);
        }

        void BuildCamImage(RectTransform gr, ref float c)
        {
            float top = OpenCard(gr, ref c, "Image et IPD");
            Para(gr, ref c, "Une seule image sert aux deux yeux (faux stereo : pas de relief). L'IPD ecarte ou rapproche l'image dans chaque oeil, comme l'IPD logiciel de l'image normale (les deux s'ajoutent).", 84f, 18);
            StepRow(gr, ref c, "Champ de la camera (deg)", 20f, 200f, 1f, 5f, 10f, () => Mathf.Round(CamGet(sl => sl.hfov, 90f)), v => CamSet(sl => sl.hfov = v), "0");
            StepRow(gr, ref c, "IPD de cette camera (+ = ecarte)", -400f, 400f, 1f, 10f, 50f, () => Mathf.Round(CamGet(sl => sl.ipd, 0f) * 1000f), v => CamSet(sl => sl.ipd = v / 1000f), "0");
            StepRow(gr, ref c, "Decalage vertical", -400f, 400f, 1f, 10f, 50f, () => Mathf.Round(CamGet(sl => sl.panY, 0f) * 1000f), v => CamSet(sl => sl.panY = v / 1000f), "0");
            StepRow(gr, ref c, "Delai de l'image (ms)", 0f, 400f, 1f, 5f, 20f, () => Mathf.Round(CamGet(sl => sl.delayMs, 60f)), v => CamSet(sl => sl.delayMs = v), "0");
            CycleRow(gr, ref c, "Rotation", 4, () => CamGet(sl => sl.rotate, 0), v => CamSet(sl => sl.rotate = v), i => (i * 90) + " deg");
            float y = Next(ref c, 48f, 16f);
            Toggle(gr, -164f, y, "Miroir horizontal", () => CamGet(sl => sl.mirrorX, false), v => CamSet(sl => sl.mirrorX = v));
            Toggle(gr, 164f, y, "Miroir vertical", () => CamGet(sl => sl.mirrorY, false), v => CamSet(sl => sl.mirrorY = v));
            CloseCard(gr, top, ref c);
        }

        void BuildCamType(RectTransform gr, ref float c)
        {
            float top = OpenCard(gr, ref c, "Type de camera");
            CycleRow(gr, ref c, "Type", 3, () => CamGet(sl => sl.role, 0), v => CamSet(sl => sl.role = v), i => Ar6dofLoc.T(Ar6dofCamRole.Names[i]));
            float cs = c;

            var night = NewBox(gr, "Nuit");
            float cn = cs;
            CycleRow(night, ref cn, "Teinte", Ar6dofCamRole.Tints.Length, () => CamGet(sl => sl.tint, 0), v => CamSet(sl => sl.tint = v), i => Ar6dofLoc.T(Ar6dofCamRole.Tints[i]));
            StepRow(night, ref cn, "Gain (%)", 20f, 800f, 5f, 25f, 100f, () => Mathf.Round(CamGet(sl => sl.gain, 1f) * 100f), v => CamSet(sl => sl.gain = v / 100f), "0");
            StepRow(night, ref cn, "Luminosite (%)", -50f, 50f, 1f, 5f, 10f, () => Mathf.Round(CamGet(sl => sl.bright, 0f) * 100f), v => CamSet(sl => sl.bright = v / 100f), "0");

            var therm = NewBox(gr, "Thermique");
            float ct = cs;
            CycleRow(therm, ref ct, "Palette", Ar6dofCamRole.Palettes.Length, () => CamGet(sl => sl.palette, 0), v => CamSet(sl => sl.palette = v), i => Ar6dofLoc.T(Ar6dofCamRole.Palettes[i]));
            StepRow(therm, ref ct, "Contraste (%)", 20f, 800f, 5f, 25f, 100f, () => Mathf.Round(CamGet(sl => sl.gain, 1f) * 100f), v => CamSet(sl => sl.gain = v / 100f), "0");
            StepRow(therm, ref ct, "Niveau (%)", -50f, 50f, 1f, 5f, 10f, () => Mathf.Round(CamGet(sl => sl.bright, 0f) * 100f), v => CamSet(sl => sl.bright = v / 100f), "0");

            var other = NewBox(gr, "Autre");
            float co = cs;
            Para(other, ref co, "Image affichee telle quelle : pour une camera libre (derriere la tete...), ouvre l'application Miroir. Une camera 'Autre' n'apparait pas dans le bouton Vue du dock.", 100f, 18);

            c = Mathf.Min(cn, Mathf.Min(ct, co));
            refreshers.Add(() => { var sel = CamSel; int r = sel == null ? 0 : sel.role; night.gameObject.SetActive(r == 0); therm.gameObject.SetActive(r == 1); other.gameObject.SetActive(r == 2); });
            Para(gr, ref c, "Le suivi de position reste toujours sur la camera normale. L'effet (vert, palette) est applique par la carte graphique, sans cout quand la camera est arretee.", 84f, 18);
            CloseCard(gr, top, ref c);
        }

        void BuildCamLed(RectTransform gr, ref float c)
        {
            float top = OpenCard(gr, ref c, "LED infrarouge");
            Para(gr, ref c, "Pour une camera de vision nocturne sans eclairage propre. Cree d'abord dans Apps > Nouvel appareil un menu Bluetooth pour ta LED, avec un curseur (puissance) ou un interrupteur, puis choisis-le ici. La LED s'allume seulement quand la camera tourne.", 112f, 18);
            Toggle(gr, 0f, Next(ref c, 48f, 12f), "Allumer une LED infrarouge avec cette camera", () => CamGet(sl => sl.ledOn, false), v => CamSet(sl => sl.ledOn = v), 640f);
            DynCycle(gr, ref c, "Menu Bluetooth", BtMenuNames, () => CamGet(sl => sl.ledMenu, ""), v => CamSet(sl => { sl.ledMenu = v; sl.ledControl = ""; }), "aucun");
            DynCycle(gr, ref c, "Controle", BtControlNames, () => CamGet(sl => sl.ledControl, ""), v => CamSet(sl => sl.ledControl = v), "aucun");
            StepRow(gr, ref c, "Puissance (%)", 0f, 100f, 1f, 5f, 10f, () => CamGet(sl => (float)sl.ledPower, 100f), v => CamSet(sl => sl.ledPower = Mathf.RoundToInt(v)), "0");
            var tb = Btn(gr, 0f, Next(ref c, 48f, 12f), 640f, "Tester la LED", () =>
            {
                var sel = CamSel;
                if (sel == null || Cams == null) return;
                bool want = !camTestLed;
                if (Cams.TestLed(sel, want)) camTestLed = want;
                RefreshAll();
            }, 22, 48f);
            var tbt = tb.GetComponentInChildren<Text>();
            refreshers.Add(() => tbt.text = Ar6dofLoc.T(camTestLed ? "Eteindre la LED (test)" : "Tester la LED"));
            InfoLine(gr, Next(ref c, 28f, 12f), PgCams, () => { var sel = CamSel; return sel == null || Cams == null ? "" : Cams.LedStatusOf(sel); }, 19);
            CloseCard(gr, top, ref c);
        }

        // ================================================================== DOCK
        void BuildDock(RectTransform page)
        {
            float c = BeginPage(page, PgDock);
            float top = OpenCard(page, ref c, "Position");
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Dock aux pieds (cache, apparait en baissant la tete)", () => Ar6dofSettings.Current.dockAtFeet, v => { Ar6dofSettings.Current.dockAtFeet = v; Touch(); }, 600f);
            StepRow(page, ref c, "Distance devant les pieds (cm)", 30f, 140f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.dockDistance * 100f), v => { Ar6dofSettings.Current.dockDistance = v / 100f; Touch(); }, "0");
            StepRow(page, ref c, "Assis : dock sous les yeux de (cm)", 30f, 140f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.dockSeatedDrop * 100f), v => { Ar6dofSettings.Current.dockSeatedDrop = v / 100f; Touch(); }, "0");
            StepRow(page, ref c, "Debut d'apparition : tete baissee de (deg)", 5f, 70f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.dockRevealStart), v => { Ar6dofSettings.Current.dockRevealStart = v; Touch(); }, "0");
            StepRow(page, ref c, "Pleinement visible a (deg)", 15f, 85f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.dockRevealFull), v => { Ar6dofSettings.Current.dockRevealFull = v; Touch(); }, "0");
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Taille");
            StepRow(page, ref c, "Taille de l'interface (%, 100 = fenetres d'environ 40 cm)", 40f, 300f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.uiScale * 100f), v => { Ar6dofSettings.Current.uiScale = v / 100f; Touch(); }, "0");
            StepRow(page, ref c, "Taille du dock (%)", 50f, 200f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.dockScale * 100f), v => { Ar6dofSettings.Current.dockScale = v / 100f; Touch(); }, "0");
            StepRow(page, ref c, "Inclinaison supplementaire (deg)", -30f, 30f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.dockTilt), v => { Ar6dofSettings.Current.dockTilt = v; Touch(); }, "0");
            StepRow(page, ref c, "Taille du clavier (%)", 50f, 250f, 1f, 5f, 10f, () => Mathf.Round(Ar6dofSettings.Current.keyboardScale * 100f), v => { Ar6dofSettings.Current.keyboardScale = v / 100f; Touch(); }, "0");
            CloseCard(page, top, ref c);

            top = OpenCard(page, ref c, "Informations sur le dock");
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Afficher l'heure", () => Ar6dofSettings.Current.dockShowClock, v => { Ar6dofSettings.Current.dockShowClock = v; Touch(); }, 600f);
            CycleRow(page, ref c, "Affichage de l'heure", 3, () => Ar6dofSettings.Current.dockClockStyle, v => { Ar6dofSettings.Current.dockClockStyle = v; Touch(); }, i => Ar6dofLoc.T(i == 0 ? "Numerique" : i == 1 ? "Analogique" : "Les deux"));
            CycleRow(page, ref c, "Cadran analogique", Ar6dofClockFace.Styles, () => Ar6dofSettings.Current.dockFaceStyle, v => { Ar6dofSettings.Current.dockFaceStyle = v; Touch(); }, i => Ar6dofLoc.T(Ar6dofClockFace.StyleNames[i]));
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Format 24 h (decoche : 12 h avec AM / PM)", () => Ar6dofSettings.Current.clock24h, v => { Ar6dofSettings.Current.clock24h = v; Touch(); }, 600f);
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Afficher les secondes", () => Ar6dofSettings.Current.clockSeconds, v => { Ar6dofSettings.Current.clockSeconds = v; Touch(); }, 600f);
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Afficher la date", () => Ar6dofSettings.Current.dockShowDate, v => { Ar6dofSettings.Current.dockShowDate = v; Touch(); }, 600f);
            CycleRow(page, ref c, "Format de la date", Ar6dofClockFmt.DateFormats.Length, () => Ar6dofSettings.Current.dateFormat, v => { Ar6dofSettings.Current.dateFormat = v; Touch(); }, i => Ar6dofClockFmt.Date(DateTime.Now, i));
            Toggle(page, 0f, Next(ref c, 48f, 12f), "Afficher la batterie du PC", () => Ar6dofSettings.Current.dockShowBattery, v => { Ar6dofSettings.Current.dockShowBattery = v; Touch(); }, 600f);
            CycleRow(page, ref c, "Affichage de la batterie", 3, () => Ar6dofSettings.Current.dockBatteryStyle, v => { Ar6dofSettings.Current.dockBatteryStyle = v; Touch(); }, i => Ar6dofLoc.T(i == 0 ? "Texte" : i == 1 ? "Icone" : "Icone, pourcentage, temps restant"));
            Para(page, ref c, "Un PC fixe n'a pas de batterie : le dock affiche alors une prise (Secteur). Rien de tout cela ne tourne quand les trois cases sont decochees.", 70f);
            CloseCard(page, top, ref c);
            Finish(PgDock, c);
        }

        // ================================================================== LANGUE
        void BuildLanguage(RectTransform page)
        {
            float c = BeginPage(page, PgLang);
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
            Finish(PgLang, c);
            refreshers.Add(() =>
            {
                string cur = Ar6dofSettings.Current.language ?? "";
                for (int i = 0; i < langButtons.Count; i++) Ar6dofTheme.SetSelected(langButtons[i], langCodes[i] == cur);
            });
        }
    }
}
