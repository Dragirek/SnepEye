using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Une "application" rangeable dans la barre de tâches.</summary>
    public abstract class Ar6dofDockApp
    {
        public string title = "", icon = "";
        public bool open;
        public Image indicator, statusDot;
        public abstract Transform AnimRoot { get; }
        /// <summary>Pastille d'état sur l'icône (menus d'appareils : connecté ou non).</summary>
        public virtual bool HasStatus { get { return false; } }
        public virtual Color StatusColor { get { return Color.clear; } }
        /// <summary>Texte affiché quand on pointe l'icône.</summary>
        public virtual string TooltipText() { return title; }
        /// <summary>Active ou désactive le contenu ET tous ses calculs (fermé = juste une icône).</summary>
        public abstract void Activate(bool on);
        public virtual void BeforeFirstShow() { }
        public virtual bool Alive { get { return AnimRoot != null; } }
    }

    class ScreenApp : Ar6dofDockApp
    {
        public Ar6dofScreenPanel panel;
        public int number;
        public override Transform AnimRoot { get { return panel != null ? panel.transform : null; } }
        public override void Activate(bool on) { if (panel != null) { panel.SetContentActive(on); if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.Save(); } }
    }

    class KeyboardApp : Ar6dofDockApp
    {
        public Ar6dofKeyboard kb;
        public override Transform AnimRoot { get { return kb != null ? kb.transform : null; } }
        public override void Activate(bool on) { if (kb != null) kb.SetVisible(on); }
    }

    class CanvasApp : Ar6dofDockApp
    {
        public Canvas canvas;
        public Action<Transform> place;
        bool placed;
        public override Transform AnimRoot { get { return canvas != null ? canvas.transform : null; } }
        public override void Activate(bool on) { if (canvas != null) canvas.gameObject.SetActive(on); }
        public override void BeforeFirstShow() { if (!placed && place != null && canvas != null) { place(canvas.transform); placed = true; } }
        /// <summary>Ramène le menu à sa place d'origine (devant toi).</summary>
        public void Replace() { if (place != null && canvas != null) place(canvas.transform); }
    }

    /// <summary>
    /// Barre de tâches en réalité virtuelle, toujours présente et fixe dans la pièce, sous les écrans. Chaque icône est une application :
    /// un clic l'affiche (elle sort de la barre avec une animation), un second clic la range (elle y rentre). Rangée, elle est désactivée :
    /// aucun calcul, aucune capture. La roue à droite ouvre les réglages (le panneau F1 en VR).
    /// Contient : les écrans virtuels, le clavier, tes menus d'appareils Bluetooth (Ar6dofDeviceMenus, bouton +) et les réglages.
    /// À mettre sur un objet de la scène, hors du Rig.
    /// </summary>
    public class Ar6dofDock : MonoBehaviour
    {
        public static Ar6dofDock Instance;
        public Ar6dofPassthrough passthrough;
        public Ar6dofHeadRig rig;
        public Ar6dofPointer pointer;
        public Ar6dofKeyboard keyboard;
        public Ar6dofLauncher launcher;
        [HideInInspector] public Ar6dofEyeTracker eyes;           // eye tracking (créé automatiquement, inactif tant qu'on ne l'active pas)
        [HideInInspector] public Ar6dofEyeCalibration eyeCal;
        [HideInInspector] public Ar6dofMouthDisplay mouthDisplay;   // dessine la bouche sur les écrans du costume (inactif tant qu'on ne l'active pas)
        [HideInInspector] public Ar6dofMouthTracker mouth;       // mouth tracking (créé automatiquement, inactif tant qu'on ne l'active pas)
        [HideInInspector] public Ar6dofAudio audioEngine;             // client du moteur audio (voix, soundboard, musique) : inactif tant qu'on ne l'active pas
        public float animSeconds = 0.25f;
        public float distance = 1.0f;

        [Header("Developpement")]
        [Tooltip("Coche pour afficher les anciens panneaux d'information en haut a gauche de la vue (statut, reglages bruts du passthrough, menu flottant). Hors Unity, tout est dans les menus en VR.")]
        public bool debug = false;
        [Tooltip("Coche pour reactiver les raccourcis clavier (R recentrer, M rappeler le menu, F suivi, B, K). Decoche par defaut : un vrai clavier branche sert a taper dans les ecrans virtuels.")]
        public bool hotkeys = false;

        Canvas canvas;
        RectTransform iconsRoot;
        RectTransform barRt;
        Sprite gear;
        readonly List<Ar6dofDockApp> apps = new List<Ar6dofDockApp>();      // dans la barre (sans les réglages)
        readonly Dictionary<Ar6dofScreenPanel, ScreenApp> screenApps = new Dictionary<Ar6dofScreenPanel, ScreenApp>();
        readonly HashSet<Ar6dofDockApp> animating = new HashSet<Ar6dofDockApp>();
        readonly Dictionary<Ar6dofDockApp, RectTransform> iconOf = new Dictionary<Ar6dofDockApp, RectTransform>();
        KeyboardApp kbApp;
        CanvasApp settingsApp;
        Ar6dofVrSettings vrSettings;
        readonly List<Ar6dofAppWindow> windows = new List<Ar6dofAppWindow>();     // toutes les applications du catalogue (Ar6dofCatalog) : dans la barre seulement quand elles sont ouvertes
        Ar6dofAppsApp appsApp;
        bool built;
        float waitStart = -1f;
        int lastSig = -1;
        // dock accroché aux pieds
        CanvasGroup group;
        float reveal;                 // 0 = invisible, 1 = entièrement visible
        Vector3 feetPos;
        bool feetInit, lastFeetMode;
        Vector3 lastFlat = Vector3.forward;
        Text followLbl;
        Ar6dofFloatingMenu kbMenu;

        Transform Head
        {
            get
            {
                if (passthrough != null && passthrough.HeadView != null) return passthrough.HeadView;
                return Camera.main != null ? Camera.main.transform : null;
            }
        }

        void Awake() { Instance = this; }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Ar6dofSettings.Changed -= OnStyleChanged;
            Ar6dofLoc.Changed -= OnLangChanged;
        }

        void Update()
        {
            Ar6dofDebug.On = debug;
            Ar6dofInput.HotkeysEnabled = hotkeys;
            if (!built)
            {
                if (Head == null) return;
                if (rig == null) rig = FindFirstObjectByType<Ar6dofHeadRig>();
                if (rig != null && !rig.Ready) return;
                if (waitStart < 0f) waitStart = Time.unscaledTime;
                var sc = Ar6dofScreens.Instance;
                bool screensReady = sc == null || sc.Built || Time.unscaledTime - waitStart > 3f;   // la barre se place sous les écrans
                if (!screensReady) return;
                Build();
                return;
            }
            int sig = ScreenSignature();
            if (sig != lastSig) { SyncScreens(); RebuildIcons(); }
            foreach (var a in apps) if (a.statusDot != null) a.statusDot.color = a.StatusColor;
            if (vrSettings != null) vrSettings.Tick();
            for (int i = 0; i < windows.Count; i++) if (windows[i].open) windows[i].Tick();
            if (appsApp != null && appsApp.open) appsApp.Tick();
        }

        const float BarHeight = 200f;

        void LateUpdate()
        {
            if (!built || canvas == null) return;
            Transform h = Head;
            if (h == null) return;
            var d = Ar6dofSettings.Current;
            if (!d.dockAtFeet) { reveal = 1f; ApplyReveal(); return; }
            if (rig != null && !rig.HasPose)   // pas de suivi : on ne sait pas ou sont tes pieds, le dock reste visible devant toi
            {
                reveal = 1f;
                if (!feetInit) FollowFeet(true);
                ApplyReveal();
                return;
            }

            // inclinaison de la tête vers le bas (degrés) : le dock apparaît entre dockRevealStart et dockRevealFull
            float pitch = Mathf.Asin(Mathf.Clamp(-h.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float a = Mathf.Clamp01(Mathf.InverseLerp(d.dockRevealStart, Mathf.Max(d.dockRevealStart + 1f, d.dockRevealFull), pitch));
            a = a * a * (3f - 2f * a);
            float dt = Time.unscaledDeltaTime;
            reveal = Mathf.Lerp(reveal, a, 1f - Mathf.Exp(-12f * dt));
            if (reveal < 0.003f && a == 0f) reveal = 0f;

            // invisible : il suit tes pieds ; visible : il reste immobile pour être touché sans bouger (sauf si tu t'es éloigné)
            Vector3 flat = h.position - feetPos; flat.y = 0f;
            bool far = flat.magnitude > 1.2f;
            if (!feetInit || far) FollowFeet(true);
            else if (reveal <= 0.02f) FollowFeet(false);
            ApplyReveal();
        }

        void ApplyReveal()
        {
            if (group == null) return;
            group.alpha = reveal;
            bool on = reveal > 0.05f;
            group.blocksRaycasts = on;
            group.interactable = on;
            canvas.enabled = reveal > 0.003f;
            if (followLbl != null && kbMenu != null) followLbl.text = Ar6dofLoc.T(kbMenu.follow ? "Suivi : OUI" : "Suivi : NON");
        }

        /// <summary>Place le dock sur le sol devant tes pieds, orienté pile vers tes yeux (angle fort).</summary>
        void FollowFeet(bool snap)
        {
            Transform h = Head;
            if (h == null || canvas == null) return;
            var d = Ar6dofSettings.Current;
            Vector3 f = h.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-3f) lastFlat = f.normalized;
            Vector3 target = new Vector3(h.position.x, 0.12f, h.position.z) + lastFlat * Mathf.Clamp(d.dockDistance, 0.3f, 1.5f);
            if (snap || !feetInit) { feetPos = target; feetInit = true; }
            else feetPos = Vector3.Lerp(feetPos, target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            Vector3 dir = feetPos - h.position;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.down + lastFlat;
            canvas.transform.SetPositionAndRotation(feetPos, Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(d.dockTilt, 0f, 0f));
            canvas.transform.localScale = Vector3.one * 0.001f * Mathf.Clamp(d.dockScale, 0.4f, 2.5f);
        }

        void OnStyleChanged()
        {
            if (!built || canvas == null) return;
            var d = Ar6dofSettings.Current;
            if (d.dockAtFeet != lastFeetMode) { lastFeetMode = d.dockAtFeet; feetInit = false; Place(); }
            else if (d.dockAtFeet) { feetInit = false; FollowFeet(true); }
            else canvas.transform.localScale = Vector3.one * 0.001f * Mathf.Clamp(d.dockScale, 0.4f, 2.5f);
            foreach (var a in apps) SetIndicator(a);
            SetIndicator(settingsApp);
            if (appsApp != null) SetIndicator(appsApp);
            UpdateGearLook();
        }

        void OnLangChanged() { if (built) RebuildIcons(); }

        int ScreenSignature()
        {
            var sc = Ar6dofScreens.Instance;
            if (sc == null) return 0;
            int h = sc.panels.Count;
            foreach (var p in sc.panels) h = h * 31 + p.GetInstanceID();
            return h;
        }

        // ------------------------------------------------------------------ construction
        void Build()
        {
            built = true;
            if (passthrough == null) passthrough = FindFirstObjectByType<Ar6dofPassthrough>();
            if (pointer == null) pointer = FindFirstObjectByType<Ar6dofPointer>();
            if (keyboard == null) keyboard = FindFirstObjectByType<Ar6dofKeyboard>();
            if (launcher == null) launcher = FindFirstObjectByType<Ar6dofLauncher>();

            canvas = Ar6dofVrUi.NewCanvas("Barre de taches", new Vector2(680f, BarHeight), Ar6dofElem.Dock);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            barRt = (RectTransform)canvas.transform;
            Ar6dofSettings.Changed += OnStyleChanged;
            Ar6dofLoc.Changed += OnLangChanged;
            lastFeetMode = Ar6dofSettings.Current.dockAtFeet;
            var ir = new GameObject("Icones", typeof(RectTransform));
            ir.transform.SetParent(canvas.transform, false);
            iconsRoot = (RectTransform)ir.transform;
            if (pointer != null) pointer.AddCanvas(canvas);
            gear = Ar6dofVrUi.GearSprite();

            if (keyboard != null)
            {
                kbApp = new KeyboardApp { kb = keyboard, title = "Clavier", icon = "KB", open = keyboard.IsVisible };
                kbMenu = keyboard.GetComponent<Ar6dofFloatingMenu>();
            }
            if (eyes == null) eyes = GetComponent<Ar6dofEyeTracker>();
            if (eyes == null) eyes = gameObject.AddComponent<Ar6dofEyeTracker>();
            if (mouth == null) mouth = GetComponent<Ar6dofMouthTracker>();
            if (mouth == null) mouth = gameObject.AddComponent<Ar6dofMouthTracker>();
            if (mouthDisplay == null) mouthDisplay = GetComponent<Ar6dofMouthDisplay>();
            if (mouthDisplay == null) mouthDisplay = gameObject.AddComponent<Ar6dofMouthDisplay>();
            mouthDisplay.tracker = mouth;
            if (eyeCal == null) eyeCal = GetComponent<Ar6dofEyeCalibration>();
            if (eyeCal == null) eyeCal = gameObject.AddComponent<Ar6dofEyeCalibration>();
            vrSettings = new Ar6dofVrSettings(this);
            settingsApp = new CanvasApp { canvas = vrSettings.canvas, title = "Reglages", icon = "", open = false };
            settingsApp.place = t => PlaceBeside(t, 35f);
            vrSettings.canvas.gameObject.SetActive(false);
            if (pointer != null) pointer.AddCanvas(vrSettings.canvas);
            AddChrome(vrSettings.canvas, "Reglages");

            // menus d'appareils Bluetooth (client Bluetooth + menus enregistrés dans ar6dof_devices.json)
            ble = GetComponent<Ar6dofBle>();
            if (ble == null) ble = gameObject.AddComponent<Ar6dofBle>();
            devMenus = gameObject.AddComponent<Ar6dofDeviceMenus>();
            devMenus.dock = this;
            devMenus.Load();

            // moteur audio (inactif tant qu'il n'est pas active), hub Apps et toutes les applications (fermees : une icone dans Apps, rien d'autre)
            audioEngine = GetComponent<Ar6dofAudio>();
            if (audioEngine == null) audioEngine = gameObject.AddComponent<Ar6dofAudio>();
            appsApp = new Ar6dofAppsApp(this);
            AdoptWindow(appsApp, 0f);
            foreach (var e in Ar6dofCatalog.Entries)
            {
                var w = e.make(this);
                AdoptWindow(w, e.yaw);
                windows.Add(w);
            }

            Place();
            ApplyReveal();
            // états mémorisés (clavier, menus) : affichés tout de suite, sans animation
            foreach (var a in ExtraApps())
            {
                var dm = a as DeviceMenu;
                bool want = dm != null ? dm.def.open : PlayerPrefs.GetInt("ar6dof_dock_open_" + a.title, a == kbApp ? 1 : 0) != 0;
                if (want) { a.BeforeFirstShow(); }
                a.Activate(want);
                a.open = want;
            }
            SyncScreens();
            RebuildIcons();
        }

        Ar6dofBle ble;
        Ar6dofDeviceMenus devMenus;

        IEnumerable<Ar6dofDockApp> ExtraApps()
        {
            if (kbApp != null) yield return kbApp;
            foreach (var m in devMenus.menus) yield return m;
            foreach (var w in windows) yield return w;
            if (appsApp != null) yield return appsApp;
            yield return settingsApp;
        }

        /// <summary>Appelé par le gestionnaire quand un menu d'appareil apparaît : branche le pointeur et choisit son emplacement.</summary>
        public void Adopt(DeviceMenu m)
        {
            if (pointer != null) pointer.AddCanvas(m.canvas);
            AddChrome(m.canvas, m.title);
            float yaw = -35f - 30f * (devMenus.menus.Count - 1);
            m.place = t => PlaceBeside(t, yaw, 0.28f);   // le haut du menu à peine au-dessus des yeux : il descend vers les mains
        }

        /// <summary>Réduire / fermer / redimensionner d'un menu (Canvas) : ici, les deux premiers le rangent dans la barre.</summary>
        void AddChrome(Canvas c, string name)
        {
            if (c == null) return;
            Action stow = () => StowCanvas(c);
            var ch = Ar6dofWindowChrome.Create(name, c.transform, Ar6dofElem.Menus, Ar6dofWindowChrome.CanvasHalf(c), stow, stow);
            ch.canMove = true;
        }

        /// <summary>Range un menu dans la barre (comme un second clic sur son icône).</summary>
        public void StowCanvas(Canvas c)
        {
            if (!built || c == null) return;
            foreach (var a in ExtraApps())
            {
                var ca = a as CanvasApp;
                var dm = a as DeviceMenu;
                var aw = a as Ar6dofAppWindow;
                bool match = (ca != null && ca.canvas == c) || (dm != null && dm.canvas == c) || (aw != null && aw.canvas == c);
                if (match) { if (a.open) Toggle(a); return; }
            }
        }

        /// <summary>Range un écran virtuel dans la barre (il y reste, éteint, jusqu'au prochain clic sur son icône).</summary>
        public void StowScreen(Ar6dofScreenPanel p)
        {
            ScreenApp sa;
            if (built && p != null && screenApps.TryGetValue(p, out sa) && sa.open) Toggle(sa);
        }

        public void RefreshIcons() { if (built) RebuildIcons(); }

        /// <summary>Range le clavier dans la barre (boutons réduire / fermer du clavier).</summary>
        public void StowKeyboard()
        {
            if (kbApp != null && kbApp.open && !animating.Contains(kbApp)) Toggle(kbApp);
        }

        /// <summary>Affiche le clavier virtuel (appelé quand un champ de texte est cliqué).</summary>
        public void ShowKeyboard()
        {
            if (kbApp != null && !kbApp.open && !animating.Contains(kbApp)) Toggle(kbApp);
        }

        void SyncScreens()
        {
            var sc = Ar6dofScreens.Instance;
            var gone = new List<Ar6dofScreenPanel>();
            foreach (var kv in screenApps) if (kv.Key == null || sc == null || !sc.panels.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var g in gone) screenApps.Remove(g);
            if (sc != null)
                foreach (var p in sc.panels)
                    if (!screenApps.ContainsKey(p)) screenApps[p] = new ScreenApp { panel = p, open = p.open };
            lastSig = ScreenSignature();
        }

        // ------------------------------------------------------------------ icônes
        void RebuildIcons()
        {
            foreach (Transform c in iconsRoot) Destroy(c.gameObject);
            iconOf.Clear();
            apps.Clear();
            var sc = Ar6dofScreens.Instance;
            // groupes d'icônes : chaque cluster d'écrans (de gauche à droite) reste côte à côte, puis le clavier et les menus d'appareils
            var groups = new List<List<Ar6dofDockApp>>();
            if (sc != null)
            {
                int n = 0;
                foreach (var cl in sc.Clusters())
                {
                    var g = new List<Ar6dofDockApp>();
                    foreach (var p in cl)
                    {
                        var a = screenApps[p];
                        n++;
                        a.number = n;
                        a.title = Ar6dofLoc.F("Ecran {0}", n);
                        a.icon = "";
                        a.open = p.open;
                        g.Add(a);
                    }
                    groups.Add(g);
                }
            }
            if (kbApp != null) groups.Add(new List<Ar6dofDockApp> { kbApp });
            foreach (var m in devMenus.menus) groups.Add(new List<Ar6dofDockApp> { m });
            foreach (var w in windows) if (w.open) groups.Add(new List<Ar6dofDockApp> { w });   // une application fermee n'est que dans Apps
            foreach (var g in groups) apps.AddRange(g);

            const float size = 96f, inCluster = 102f, between = 116f, gearSize = 116f;
            // positions (de gauche à droite), puis largeur de la barre
            var xs = new List<float>();
            float cx = size * 0.5f;
            bool first = true;
            foreach (var g in groups)
                for (int i = 0; i < g.Count; i++)
                {
                    if (!first) cx += (i == 0) ? between : inCluster;
                    first = false;
                    xs.Add(cx);
                }
            float addX = (first ? size * 0.5f : cx + between);
            float sepX = addX + size * 0.5f + 26f;                                 // le separateur isole le groupe de droite : Apps et reglages
            float appsX = sepX + 26f + size * 0.5f;                                // Apps : juste a gauche de la roue des reglages
            float gearX = appsX + size * 0.5f + 12f + gearSize * 0.5f;
            float total = gearX + gearSize * 0.5f;
            float barW = Mathf.Max(680f, total + 60f);
            barRt.sizeDelta = new Vector2(barW, BarHeight);
            float shift = -total * 0.5f;
            MakeQuickRow(barW);

            // fond commun derrière les icônes d'un même cluster
            int xi = 0;
            foreach (var g in groups)
            {
                if (g.Count >= 2 && g[0] is ScreenApp)
                {
                    float x0 = xs[xi] + shift, x1 = xs[xi + g.Count - 1] + shift;
                    var pill = Ar6dofVrUi.Rect(iconsRoot, "Cluster", new Vector2((x0 + x1) * 0.5f, -26f), new Vector2(x1 - x0 + size + 20f, size + 20f));
                    var pim = pill.gameObject.AddComponent<Image>();
                    pim.raycastTarget = false;
                    Ar6dofTheme.Attach(pill.gameObject, Ar6dofRole.Band, 40);
                }
                xi += g.Count;
            }
            xi = 0;
            foreach (var g in groups) foreach (var a in g) { MakeIcon(a, xs[xi] + shift, size, false); xi++; }
            MakeAddButton(addX + shift, size);
            if (appsApp != null) MakeIcon(appsApp, appsX + shift, size, false);

            // séparateur + roue des réglages : plus grande, en couleur d'accent, à part
            var sep = new GameObject("Separateur", typeof(RectTransform));
            sep.transform.SetParent(iconsRoot, false);
            var srt = (RectTransform)sep.transform;
            srt.anchoredPosition = new Vector2(sepX + shift, -26f);
            srt.sizeDelta = new Vector2(4f, 120f);
            var simg = sep.AddComponent<Image>();
            simg.raycastTarget = false;
            Ar6dofTheme.Attach(sep, Ar6dofRole.Accent, 2);
            MakeIcon(settingsApp, gearX + shift, gearSize, true);
        }

        // ------------------------------------------------------------------ actions rapides (remplacent les touches R, M et F)
        void MakeQuickRow(float barW)
        {
            const float bw = 150f, bh = 58f, gap = 10f, y = 62f;
            float x = -(4 * bw + 3 * gap) * 0.5f + bw * 0.5f;
            Text lbl;
            followLbl = null;
            var b1 = Ar6dofVrUi.MakeButton(iconsRoot, "Recentrer", new Vector2(x, y), new Vector2(bw, bh), () => { if (rig != null) rig.Recenter(); FollowFeet(true); }, out lbl, 22);
            Tip(b1, "Recentrer la tete : ici devant moi = cap de depart");
            x += bw + gap;
            var b2 = Ar6dofVrUi.MakeButton(iconsRoot, "Menus ici", new Vector2(x, y), new Vector2(bw, bh), BringMenus, out lbl, 22);
            Tip(b2, "Ramener les menus ouverts devant moi");
            x += bw + gap;
            var b3 = Ar6dofVrUi.MakeButton(iconsRoot, "Clavier ici", new Vector2(x, y), new Vector2(bw, bh), () => { RecenterKeyboard(); ShowKeyboard(); }, out lbl, 22);
            Tip(b3, "Poser le clavier devant moi");
            x += bw + gap;
            var b4 = Ar6dofVrUi.MakeButton(iconsRoot, "Suivi : NON", new Vector2(x, y), new Vector2(bw, bh), () =>
            {
                if (kbMenu != null) kbMenu.follow = !kbMenu.follow;
                ApplyReveal();
            }, out lbl, 22);
            followLbl = lbl;
            Tip(b4, "Le clavier te suit quand tu t'eloignes (oui / non)");
        }

        static void Tip(Button b, string text)
        {
            var tip = b.gameObject.AddComponent<Ar6dofVrTooltip>();
            tip.text = () => text;
        }

        void BringMenus()
        {
            foreach (var a in ExtraApps())
            {
                if (!a.open || a == kbApp) continue;
                var ca = a as CanvasApp;
                if (ca != null) ca.Replace();
                var dm = a as DeviceMenu;
                if (dm != null) dm.Replace();
                var aw = a as Ar6dofAppWindow;
                if (aw != null) aw.Replace();
            }
        }

        void MakeAddButton(float x, float size)
        {
            Text lbl;
            var b = Ar6dofVrUi.MakeButton(iconsRoot, "+", new Vector2(x, -26f), new Vector2(size, size), () =>
            {
                var m = devMenus.Create();
                RebuildIcons();
                Toggle(m);   // s'ouvre tout de suite : on est dans ses parametres a choisir l'appareil
            }, out lbl, 54);
            lbl.MakeAccent();
            var tip = b.gameObject.AddComponent<Ar6dofVrTooltip>();
            tip.text = () => "Nouveau menu d'appareil Bluetooth";
        }

        static readonly Dictionary<string, Sprite> glyphs = new Dictionary<string, Sprite>();

        public static Sprite Glyph(string name)
        {
            Sprite sp;
            if (glyphs.TryGetValue(name, out sp) && sp != null) return sp;
            var tex = Ar6dofIcons.Get(name);
            if (tex == null) return null;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            glyphs[name] = sp;
            return sp;
        }

        Image gearImg;
        Button gearBtn;
        Text gearLbl;

        /// <summary>La roue des réglages est un bouton comme les autres (couleur normale, pas allumée en permanence) : seuls sa taille et le séparateur la mettent à part. Son dessin suit la couleur du texte.</summary>
        void UpdateGearLook()
        {
            if (gearImg == null) return;
            Color on = Ar6dofSettings.StyleFor(Ar6dofElem.Dock).text;
            gearImg.color = on;
            if (gearLbl != null) gearLbl.color = on;
        }

        void MakeIcon(Ar6dofDockApp a, float x, float size, bool isGear)
        {
            Text lbl;
            Ar6dofDockApp app = a;
            var sa = a as ScreenApp;
            var b = Ar6dofVrUi.MakeButton(iconsRoot, (isGear || sa != null || a is KeyboardApp || a is Ar6dofAppWindow) ? "" : a.icon, new Vector2(x, -26f), new Vector2(size, size), () => Toggle(app), out lbl, 34);
            var rt = (RectTransform)b.transform;
            iconOf[a] = rt;
            if (isGear)
            {
                // plus grand que les autres et séparé par un trait ; couleurs normales (le soulignement indique qu'il est ouvert)
                var g = new GameObject("Roue", typeof(RectTransform));
                g.transform.SetParent(rt, false);
                var grt = (RectTransform)g.transform;
                grt.anchoredPosition = new Vector2(0f, 8f);
                grt.sizeDelta = new Vector2(size * 0.62f, size * 0.62f);
                gearImg = g.AddComponent<Image>();
                gearImg.sprite = gear;
                gearImg.raycastTarget = false;
                gearBtn = b;
                UpdateGearLook();
                gearLbl = Ar6dofVrUi.Label(rt, "Reglages", new Vector2(0f, -size * 0.5f + 20f), new Vector2(size, 22f), 16);
                UpdateGearLook();
            }
            else if (sa != null)
            {
                // icône d'écran avec son numéro dedans (une fenêtre d'application a une barre de titre)
                var g = new GameObject("Glyphe", typeof(RectTransform));
                g.transform.SetParent(rt, false);
                var grt = (RectTransform)g.transform;
                grt.anchoredPosition = new Vector2(0f, 2f);
                grt.sizeDelta = new Vector2(size * 0.9f, size * 0.9f);
                var gi = g.AddComponent<Image>();
                gi.sprite = Glyph(sa.panel != null && sa.panel.IsWindow ? "window" : "monitor");
                gi.raycastTarget = false;
                Ar6dofTheme.Attach(g, Ar6dofRole.Text);
                var num = Ar6dofVrUi.Label(rt, sa.number.ToString(), new Vector2(0f, sa.panel != null && sa.panel.IsWindow ? 2f : 8f), new Vector2(size * 0.6f, 44f), 36);
                num.fontStyle = FontStyle.Bold;
            }
            else if (a is KeyboardApp)
            {
                // clavier : dessin d'un clavier a la place des lettres "KB"
                var g = new GameObject("Clavier", typeof(RectTransform));
                g.transform.SetParent(rt, false);
                var grt = (RectTransform)g.transform;
                grt.anchoredPosition = new Vector2(0f, 10f);
                grt.sizeDelta = new Vector2(size * 0.62f, size * 0.62f);
                var gi = g.AddComponent<Image>();
                gi.sprite = Glyph("keyboard");
                gi.raycastTarget = false;
                Ar6dofTheme.Attach(g, Ar6dofRole.Text);
                Ar6dofVrUi.Label(rt, Ar6dofLoc.T(a.title), new Vector2(0f, -32f), new Vector2(size, 24f), 15);
            }
            else if (a is Ar6dofAppWindow)
            {
                // application (Apps, camera...) : son dessin et son nom
                var aw = (Ar6dofAppWindow)a;
                var g = new GameObject("Glyphe", typeof(RectTransform));
                g.transform.SetParent(rt, false);
                var grt = (RectTransform)g.transform;
                grt.anchoredPosition = new Vector2(0f, 10f);
                grt.sizeDelta = new Vector2(size * 0.62f, size * 0.62f);
                var gi = g.AddComponent<Image>();
                gi.sprite = Glyph(aw.glyph);
                gi.raycastTarget = false;
                Ar6dofTheme.Attach(g, Ar6dofRole.Text);
                var nl = Ar6dofVrUi.Label(rt, Ar6dofLoc.T(a.title), new Vector2(0f, -32f), new Vector2(size, 24f), 15);
                nl.gameObject.AddComponent<Ar6dofFitText>().Init(nl, 15);
            }
            else
            {
                lbl.rectTransform.anchoredPosition = new Vector2(0f, 12f);
                string shown = Ar6dofLoc.T(a.title);
                if (shown.Length > 10) shown = shown.Substring(0, 9) + "..";
                Ar6dofVrUi.Label(rt, shown, new Vector2(0f, -32f), new Vector2(size, 24f), 15);
            }
            var tip = b.gameObject.AddComponent<Ar6dofVrTooltip>();
            tip.text = () => app.TooltipText();
            if (a.HasStatus)
            {
                var dot = new GameObject("Etat", typeof(RectTransform));
                dot.transform.SetParent(rt, false);
                var drt = (RectTransform)dot.transform;
                drt.anchoredPosition = new Vector2(size * 0.5f - 14f, size * 0.5f - 14f);
                drt.sizeDelta = new Vector2(18f, 18f);
                a.statusDot = dot.AddComponent<Image>();
                a.statusDot.raycastTarget = false;
                a.statusDot.color = a.StatusColor;
            }
            // trait sous l'icône = application affichée
            var ind = new GameObject("Indicateur", typeof(RectTransform));
            ind.transform.SetParent(rt, false);
            var irt = (RectTransform)ind.transform;
            irt.anchoredPosition = new Vector2(0f, -size * 0.5f + 5f);
            irt.sizeDelta = new Vector2(size * 0.5f, 6f);
            a.indicator = ind.AddComponent<Image>();
            a.indicator.raycastTarget = false;
            SetIndicator(a);
        }

        void SetIndicator(Ar6dofDockApp a)
        {
            if (a.indicator == null) return;
            var acc = Ar6dofSettings.StyleFor(Ar6dofElem.Dock).accent;
            Color on = a == settingsApp ? Ar6dofTheme.OnColor(acc) : acc;   // la roue est déjà en accent : son trait est noir ou blanc
            a.indicator.color = a.open ? on : new Color(1f, 1f, 1f, 0f);
        }

        // ------------------------------------------------------------------ afficher / ranger
        public void Toggle(Ar6dofDockApp app)
        {
            if (app == null || animating.Contains(app) || !app.Alive) return;
            StartCoroutine(Animate(app, !app.open));
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

        IEnumerator Animate(Ar6dofDockApp app, bool show)
        {
            animating.Add(app);
            var win = app as Ar6dofAppWindow;
            bool inCatalog = win != null && windows.Contains(win);
            if (show && inCatalog) { app.open = true; RebuildIcons(); }   // l'application ouverte prend sa place dans la barre : l'animation part de son icone
            Transform t = app.AnimRoot;
            RectTransform icon;
            iconOf.TryGetValue(app, out icon);
            Vector3 from = icon != null ? icon.position : transform.position;
            if (show) { app.BeforeFirstShow(); app.Activate(true); app.open = true; SetIndicator(app); }
            Vector3 rest = t.position;
            Vector3 restScale = t.localScale;
            float p = 0f;
            while (p < 1f)
            {
                if (t == null) break;
                p += Time.unscaledDeltaTime / Mathf.Max(0.05f, animSeconds);
                float e = Ease(show ? p : 1f - p);
                t.position = Vector3.Lerp(from, rest, e);
                t.localScale = restScale * Mathf.Max(0.02f, e);
                yield return null;
            }
            if (t != null)
            {
                if (!show) { app.Activate(false); app.open = false; SetIndicator(app); }
                t.position = rest;
                t.localScale = restScale;
            }
            if (app is KeyboardApp || app == settingsApp) PlayerPrefs.SetInt("ar6dof_dock_open_" + app.title, app.open ? 1 : 0);
            animating.Remove(app);
            if (inCatalog && !show) RebuildIcons();   // rangee : son icone quitte la barre (elle reste dans Apps)
        }

        // ------------------------------------------------------------------ placement
        /// <summary>Place la barre sous les écrans, au centre de leur arc ; fixe dans la pièce.</summary>
        public void Place()
        {
            Transform h = Head;
            if (h == null || canvas == null) return;
            if (Ar6dofSettings.Current.dockAtFeet) { feetInit = false; FollowFeet(true); return; }
            canvas.transform.localScale = Vector3.one * 0.001f * Mathf.Clamp(Ar6dofSettings.Current.dockScale, 0.4f, 2.5f);
            Vector3 dir = Vector3.zero;
            float lowest = h.position.y - 0.45f;
            var sc = Ar6dofScreens.Instance;
            if (sc != null && sc.panels.Count > 0)
            {
                lowest = float.MaxValue;
                foreach (var p in sc.panels)
                {
                    Vector3 d = p.transform.position - h.position; d.y = 0f;
                    if (d.sqrMagnitude > 1e-4f) dir += d.normalized;
                    lowest = Mathf.Min(lowest, p.transform.position.y - p.Height * 0.5f);
                }
            }
            if (dir.sqrMagnitude < 1e-4f) { dir = h.forward; dir.y = 0f; if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; }
            dir.Normalize();
            Vector3 pos = h.position + dir * distance;
            pos.y = Mathf.Clamp(lowest - 0.14f, 0.4f, h.position.y - 0.3f);
            canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir) * Quaternion.Euler(20f, 0f, 0f));   // légèrement inclinée vers tes yeux
        }

        // ------------------------------------------------------------------ Apps : toutes les applications
        /// <summary>L'application est-elle affichee ?</summary>
        public bool IsOpen(string id)
        {
            foreach (var w in windows) if (w.id == id) return w.open;
            return false;
        }

        /// <summary>Ouvre l'application si elle est rangee, la range si elle est ouverte.</summary>
        public void ToggleApp(string id)
        {
            foreach (var w in windows) if (w.id == id) { Toggle(w); return; }
        }

        /// <summary>Affiche une application (sans effet si elle l'est deja).</summary>
        public void OpenApp(string id)
        {
            foreach (var w in windows) if (w.id == id && !w.open) { Toggle(w); return; }
        }

        /// <summary>Branche une fenêtre d'application : pointeur, barre de déplacement / fermeture, emplacement d'origine.</summary>
        void AdoptWindow(Ar6dofAppWindow w, float yawDeg)
        {
            if (pointer != null) pointer.AddCanvas(w.canvas);
            AddChrome(w.canvas, w.title);
            w.place = t => PlaceBeside(t, yawDeg, 0.2f);
        }

        public void Recenter()
        {
            var sc = Ar6dofScreens.Instance;
            if (sc != null) { /* l'arc des écrans reste en place : la barre se recale dessous */ }
            Place();
        }

        public void RecenterKeyboard()
        {
            if (keyboard == null) return;
            var fm = keyboard.GetComponent<Ar6dofFloatingMenu>();
            if (fm != null) fm.Summon();
        }

        /// <summary>Menus (réglages, menus perso) : à côté des écrans, à hauteur du regard.</summary>
        void PlaceBeside(Transform t, float yawDeg, float dy = 0f)
        {
            Transform h = Head;
            if (h == null) return;
            Vector3 f = h.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            Vector3 d = Quaternion.Euler(0f, yawDeg, 0f) * f.normalized;
            Vector3 pos = h.position + d * 1.2f;
            pos.y = h.position.y - 0.05f + dy;
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(d));
        }
    }
}
