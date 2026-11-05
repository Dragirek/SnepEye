using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Appui long (1 s) sur une icône du dock : ramene la fenetre de l'application pres de toi (et l'ouvre si elle est fermee ou reduite).
    /// Le relachement qui suit ne declenche pas le clic normal. Quasi gratuit : un Update qui sort tout de suite tant que personne n'appuie (le composant doit rester actif pour recevoir les evenements du pointeur).
    /// </summary>
    public class Ar6dofLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float seconds = 1.0f;
        public Action onLong;
        float downAt = -1f;
        bool fired;

        public void OnPointerDown(PointerEventData e) { downAt = Time.unscaledTime; fired = false; }
        public void OnPointerUp(PointerEventData e) { downAt = -1f; }
        public void OnPointerExit(PointerEventData e) { downAt = -1f; }

        void Update()
        {
            if (downAt < 0f || fired) return;
            if (Time.unscaledTime - downAt >= seconds)
            {
                fired = true;
                if (onLong != null) onLong();
            }
        }

        /// <summary>Vrai (une seule fois) si l'appui long vient d'avoir lieu : le clic qui suit le relachement doit alors etre ignore.</summary>
        public bool ConsumeFired() { bool f = fired; fired = false; return f; }
    }

    /// <summary>Une "application" rangeable dans la barre de tâches.</summary>
    public abstract class Ar6dofDockApp
    {
        public string title = "", icon = "";
        public bool open;
        public bool docked;     // applications et menus d'appareils : present dans la barre (affiche OU reduit) ; ferme = absent de la barre
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
        readonly HashSet<Ar6dofDockApp> closing = new HashSet<Ar6dofDockApp>();   // fermeture demandee : retirees de la barre une fois rangees
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
        Text followLbl, seatLbl, viewLbl;
        Ar6dofCameras camHub;
        bool camBtnShown;
        bool seatApplied;
        Ar6dofFloatingMenu kbMenu;

        Transform Head
        {
            get
            {
                if (passthrough != null && passthrough.HeadView != null) return passthrough.HeadView;
                return Camera.main != null ? Camera.main.transform : null;
            }
        }

        /// <summary>Position de la tête (pour placer une fenêtre devant toi, comme la sonnerie de l'horloge).</summary>
        public Transform HeadTransform { get { return Head; } }

        Ar6dofDockInfo info;          // bandeau heure / date / batterie (null quand rien n'est coché)
        int infoSig = -1;
        Ar6dofClockService clock;

        void Awake() { Instance = this; }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Ar6dofSettings.Changed -= OnStyleChanged;
            Ar6dofLoc.Changed -= OnLangChanged;
            if (camHub != null) camHub.Changed -= OnCamerasChanged;
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
                if (rig != null && !seatApplied) { rig.SetSeated(Ar6dofSettings.Current.seated); seatApplied = true; }
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
            if (info != null && canvas != null && canvas.enabled) info.Tick();
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
            // assis, le dock est plus pres et moins bas : il apparait en baissant moins la tete (on compare l'angle reel du dock a celui d'un dock au sol, environ 66 deg)
            float sub = 0f;
            if (d.seated)
            {
                Vector3 toDock = feetPos - h.position;
                float hd = new Vector2(toDock.x, toDock.z).magnitude;
                float dockAng = Mathf.Atan2(-toDock.y, Mathf.Max(0.05f, hd)) * Mathf.Rad2Deg;
                sub = Mathf.Clamp(66f - dockAng, 0f, 30f);
            }
            float rs = Mathf.Max(8f, d.dockRevealStart - sub);
            float a = Mathf.Clamp01(Mathf.InverseLerp(rs, Mathf.Max(rs + 1f, d.dockRevealFull - sub), pitch));
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
            if (seatLbl != null) seatLbl.text = Ar6dofLoc.T(Ar6dofSettings.Current.seated ? "Assis : OUI" : "Assis : NON");
            if (viewLbl != null && camHub != null) viewLbl.text = camHub.ViewLabel();
        }

        /// <summary>Les caméras supplémentaires ont changé : le bouton « Vue » apparaît ou disparaît (il n'existe que s'il y a une caméra activée de vision nocturne ou thermique).</summary>
        void OnCamerasChanged()
        {
            if (!built) return;
            bool want = camHub != null && camHub.HasSwitchable;
            if (want != camBtnShown) RebuildIcons();
            else ApplyReveal();
        }

        /// <summary>Place le dock sur le sol devant tes pieds, orienté pile vers tes yeux (angle fort).</summary>
        void FollowFeet(bool snap)
        {
            Transform h = Head;
            if (h == null || canvas == null) return;
            var d = Ar6dofSettings.Current;
            Vector3 f = h.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-3f) lastFlat = f.normalized;
            // debout : sur le sol devant les pieds ; assis : a hauteur des mains (sous les yeux), plus pres
            float ty = d.seated ? Mathf.Max(0.2f, h.position.y - Mathf.Clamp(d.dockSeatedDrop, 0.3f, 1.4f)) : 0.12f;
            float dist = Mathf.Clamp(d.dockDistance, 0.3f, 1.5f);
            if (d.seated) dist = Mathf.Min(dist, 0.55f);
            Vector3 target = new Vector3(h.position.x, ty, h.position.z) + lastFlat * dist;
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
            if (Ar6dofDockInfo.Signature(d) != infoSig) RebuildIcons();   // heure, date ou batterie : le bandeau change
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
            camHub = GetComponent<Ar6dofCameras>();
            if (camHub == null) camHub = gameObject.AddComponent<Ar6dofCameras>();   // ne fait rien tant qu'aucune caméra n'est configurée et utilisée
            camHub.Changed += OnCamerasChanged;
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
            clock = GetComponent<Ar6dofClockService>();
            if (clock == null) clock = gameObject.AddComponent<Ar6dofClockService>();   // alarmes / minuteur : ne fait rien tant qu'aucun n'est actif
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
                if (dm != null) dm.docked = want;
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
            AddChrome(m.canvas, m.title, m);
            float yaw = -35f - 30f * (devMenus.menus.Count - 1);
            m.place = t => PlaceBeside(t, yaw, 0.28f);   // le haut du menu à peine au-dessus des yeux : il descend vers les mains
        }

        /// <summary>Réduire / fermer / redimensionner d'un menu (Canvas) : ici, les deux premiers le rangent dans la barre.</summary>
        void AddChrome(Canvas c, string name, Ar6dofDockApp app = null)
        {
            if (c == null) return;
            Action stow = () => StowCanvas(c);
            // Reduire : la fenetre se cache, son icone reste dans la barre. Fermer (applications et appareils) : elle est retiree de la barre et s'arrete.
            Action close = app != null ? (Action)(() => CloseApp(app)) : stow;
            var ch = Ar6dofWindowChrome.Create(name, c.transform, Ar6dofElem.Menus, Ar6dofWindowChrome.CanvasHalf(c), stow, close);
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
            foreach (var m in devMenus.menus) if (m.open || m.docked) groups.Add(new List<Ar6dofDockApp> { m });   // un menu d'appareil ferme n'est que dans Apps
            foreach (var w in windows) if (w.open || w.docked) groups.Add(new List<Ar6dofDockApp> { w });   // une application fermee n'est que dans Apps
            foreach (var g in groups) apps.AddRange(g);

            const float size = 96f, inCluster = 102f, between = 116f, gearSize = 96f;   // la roue a la meme taille que les autres icones
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
            float addX = (first ? size * 0.5f : cx + between);                     // place de la prochaine icone (plus de bouton + : les nouveaux appareils se creent dans Apps)
            float sepX = addX - size * 0.5f + 26f;                                 // le separateur isole le groupe de droite : Apps et reglages
            float appsX = sepX + 26f + size * 0.5f;                                // Apps : juste a gauche de la roue des reglages
            float gearX = appsX + size * 0.5f + 12f + gearSize * 0.5f;
            float total = gearX + gearSize * 0.5f;
            camBtnShown = camHub != null && camHub.HasSwitchable;
            float barW = Mathf.Max(camBtnShown ? 860f : 720f, total + 60f);
            var ds = Ar6dofSettings.Current;
            infoSig = Ar6dofDockInfo.Signature(ds);
            float extra = Ar6dofDockInfo.Wanted(ds) ? Ar6dofDockInfo.Height : 0f;   // le bandeau s'ajoute SOUS les icônes : la barre grandit, rien ne bouge dedans
            barRt.sizeDelta = new Vector2(barW, BarHeight + extra);
            iconsRoot.anchoredPosition = new Vector2(0f, extra * 0.5f);
            info = null;
            if (extra > 0f)
            {
                info = new Ar6dofDockInfo();
                info.Build(iconsRoot, barW, -BarHeight * 0.5f - extra * 0.5f);
            }
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
            const float gap = 6f, bh = 58f, y = 62f;
            int nb = camBtnShown ? 6 : 5;
            float bw = Mathf.Min(134f, (barW - 40f - (nb - 1) * gap) / nb);
            float x = -(nb * bw + (nb - 1) * gap) * 0.5f + bw * 0.5f;
            Text lbl;
            followLbl = null; seatLbl = null; viewLbl = null;
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
            x += bw + gap;
            var b5 = Ar6dofVrUi.MakeButton(iconsRoot, "Assis : NON", new Vector2(x, y), new Vector2(bw, bh), ToggleSeated, out lbl, 22);
            seatLbl = lbl;
            Tip(b5, "Mode assis : dock a hauteur des mains, menus et ecrans a hauteur des yeux assis. Assieds-toi puis active.");
            if (camBtnShown)
            {
                x += bw + gap;
                var b6 = Ar6dofVrUi.MakeButton(iconsRoot, "Vue : Normale", new Vector2(x, y), new Vector2(bw, bh), () => { if (camHub != null) camHub.CycleView(); ApplyReveal(); }, out lbl, 20);
                lbl.gameObject.AddComponent<Ar6dofFitText>().Init(lbl, 20);
                viewLbl = lbl;
                Tip(b6, "Change la vue : camera normale, puis vision nocturne et thermique (cameras activees dans Reglages > Image > Cameras)");
            }
        }

        static void Tip(Button b, string text)
        {
            var tip = b.gameObject.AddComponent<Ar6dofVrTooltip>();
            tip.text = () => text;
        }

        /// <summary>Mode assis (oui / non) : enregistre dans les reglages, ajuste la hauteur des yeux supposee, replace le dock et les menus.</summary>
        void ToggleSeated()
        {
            var d = Ar6dofSettings.Current;
            d.seated = !d.seated;
            Ar6dofSettings.SaveFields(new[] { "seated" });
            if (rig != null) rig.SetSeated(d.seated);
            feetInit = false;
            FollowFeet(true);
            BringMenus();
            ApplyReveal();
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
            Ar6dofLongPress lp = null;
            var b = Ar6dofVrUi.MakeButton(iconsRoot, (isGear || sa != null || a is KeyboardApp || a is Ar6dofAppWindow) ? "" : a.icon, new Vector2(x, -26f), new Vector2(size, size), () => { if (lp != null && lp.ConsumeFired()) return; Toggle(app); }, out lbl, 34);
            lp = b.gameObject.AddComponent<Ar6dofLongPress>();
            lp.onLong = () => Summon(app);
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

        /// <summary>Appui long sur une icone : amene la fenetre (ou l'ecran, le clavier) devant toi ; l'ouvre d'abord si elle est fermee ou reduite.</summary>
        public void Summon(Ar6dofDockApp app)
        {
            if (app == null || !built || !app.Alive || animating.Contains(app)) return;
            if (app.open) { MoveNear(app); return; }
            StartCoroutine(SummonRoutine(app));
        }

        IEnumerator SummonRoutine(Ar6dofDockApp app)
        {
            MoveNear(app);
            yield return StartCoroutine(Animate(app, true));
        }

        void MoveNear(Ar6dofDockApp app)
        {
            var aw = app as Ar6dofAppWindow;
            if (aw != null) { aw.Replace(); return; }
            var dm = app as DeviceMenu;
            if (dm != null) { dm.Replace(); return; }
            var ca = app as CanvasApp;
            if (ca != null) { ca.Replace(); return; }
            if (app is KeyboardApp) { RecenterKeyboard(); return; }
            var sa = app as ScreenApp;
            if (sa != null && sa.panel != null && Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.BringToMe(sa.panel);
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

        IEnumerator Animate(Ar6dofDockApp app, bool show)
        {
            animating.Add(app);
            var win = app as Ar6dofAppWindow;
            bool inCatalog = (win != null && windows.Contains(win)) || app is DeviceMenu;
            // l'application ouverte prend sa place dans la barre : l'animation part de son icone. La barre n'est refaite que si elle n'y etait pas deja (rouvrir une application reduite ne change rien aux icones)
            if (show && inCatalog) { bool wasDocked = app.docked; app.docked = true; app.open = true; if (!wasDocked) RebuildIcons(); }
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
            if (inCatalog && !show && closing.Remove(app)) FinishClose(app);   // fermer : retire l'icone de la barre (et arrete l'application)
            if (win != null && inCatalog) AudioSync();
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
            foreach (var w in windows) if (w.id == id) return w.open || w.docked;
            return false;
        }

        // ---- menus d'appareils Bluetooth (crees et ouverts depuis Apps)
        public List<DeviceMenu> DeviceMenus { get { return devMenus.menus; } }

        /// <summary>Cree un menu d'appareil Bluetooth et l'ouvre sur son choix d'appareil.</summary>
        public void NewDeviceMenu()
        {
            var m = devMenus.Create();
            Toggle(m);
        }

        public void ToggleDeviceMenu(DeviceMenu m) { TileToggle(m); }

        /// <summary>Dans Apps : ouvre l'application si elle est fermee ou reduite, la ferme (retiree de la barre, arretee) si elle est affichee.</summary>
        public void ToggleApp(string id)
        {
            foreach (var w in windows) if (w.id == id) { TileToggle(w); return; }
        }

        public void TileToggle(Ar6dofDockApp a)
        {
            if (a == null || animating.Contains(a)) return;
            if (a.open) CloseApp(a); else Toggle(a);
        }

        /// <summary>L'application est-elle dans la barre (affichee ou reduite) ?</summary>
        public bool IsDocked(Ar6dofDockApp a) { return a != null && (a.open || a.docked); }

        /// <summary>Ferme une application ou un menu d'appareil : retire de la barre et arrete ce qu'il faisait (son, apercu...). Reduire, lui, ne fait que cacher la fenetre.</summary>
        public void CloseApp(Ar6dofDockApp app)
        {
            if (app == null || !built || animating.Contains(app)) return;
            var win = app as Ar6dofAppWindow;
            bool dynamic = (win != null && windows.Contains(win)) || app is DeviceMenu;
            if (!dynamic) { if (app.open) Toggle(app); return; }
            if (app.open) { closing.Add(app); StartCoroutine(Animate(app, false)); }
            else FinishClose(app);
        }

        void FinishClose(Ar6dofDockApp app)
        {
            app.docked = false;
            var win = app as Ar6dofAppWindow;
            if (win != null) win.OnClosed();
            RebuildIcons();
            AudioSync();
        }

        static bool IsAudioApp(string id) { return id == "voice" || id == "audio" || id == "soundboard" || id == "mixer"; }

        /// <summary>Le moteur audio ne tourne que tant qu'une application audio est dans la barre (affichee ou reduite).</summary>
        void AudioSync()
        {
            if (audioEngine == null) return;
            bool any = false;
            foreach (var w in windows) if (IsAudioApp(w.id) && (w.open || w.docked)) any = true;
            if (any) audioEngine.Resume(); else audioEngine.Suspend();
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
            AddChrome(w.canvas, w.title, w);
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

        /// <summary>Zone du champ de vision déjà prise par une fenêtre ou un écran, en degrés par rapport au regard (cap : droite positive ; hauteur).</summary>
        struct Taken { public float y0, y1, p0, p1; }

        static readonly Vector3[] cornerBuf = new Vector3[4];

        static void AddTaken(List<Taken> list, Vector3 eye, Vector3 fwd, Vector3[] pts)
        {
            float y0 = 1e9f, y1 = -1e9f, p0 = 1e9f, p1 = -1e9f;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 v = pts[i] - eye;
                Vector3 vf = new Vector3(v.x, 0f, v.z);
                if (vf.sqrMagnitude < 1e-6f) return;
                float yw = Vector3.SignedAngle(fwd, vf, Vector3.up);
                float pt = Mathf.Atan2(v.y, vf.magnitude) * Mathf.Rad2Deg;
                if (yw < y0) y0 = yw; if (yw > y1) y1 = yw;
                if (pt < p0) p0 = pt; if (pt > p1) p1 = pt;
            }
            if (y1 - y0 > 200f) return;   // à cheval derrière toi : on ignore
            list.Add(new Taken { y0 = y0, y1 = y1, p0 = p0, p1 = p1 });
        }

        void CollectTaken(List<Taken> list, Transform self, Vector3 eye, Vector3 fwd)
        {
            var pts = cornerBuf;
            foreach (var a in ExtraApps())
            {
                Canvas c = null;
                var aw = a as Ar6dofAppWindow; if (aw != null) c = aw.canvas;
                var dm = a as DeviceMenu; if (dm != null) c = dm.canvas;
                var ca = a as CanvasApp; if (ca != null) c = ca.canvas;
                if (c == null || c.transform == self || !c.gameObject.activeInHierarchy) continue;
                ((RectTransform)c.transform).GetWorldCorners(pts);
                AddTaken(list, eye, fwd, pts);
            }
            var sc = Ar6dofScreens.Instance;
            if (sc != null)
            {
                var four = new Vector3[4];
                foreach (var p in sc.panels)
                {
                    if (p == null || !p.open || !p.gameObject.activeInHierarchy) continue;
                    Transform pt = p.transform;
                    Vector3 c0 = pt.position;
                    Vector3 right = pt.right * (p.Width * 0.5f), up = pt.up * (p.Height * 0.5f);
                    four[0] = c0 - right - up; four[1] = c0 - right + up; four[2] = c0 + right + up; four[3] = c0 + right - up;
                    AddTaken(list, eye, fwd, four);
                }
            }
        }

        /// <summary>
        /// Place une fenêtre (application, réglages, menu d'appareil) dans un endroit LIBRE du champ de vision : on essaie d'abord devant toi, puis de plus en plus sur les côtés
        /// (les coins), en évitant les écrans et les autres fenêtres ouvertes. Le côté préféré (signe de yawDeg) passe en premier ; derrière toi seulement s'il n'y a vraiment plus de place.
        /// </summary>
        void PlaceBeside(Transform t, float yawDeg, float dy = 0f)
        {
            Transform h = Head;
            if (h == null) return;
            Vector3 f = h.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            Vector3 eye = h.position;

            float pref = Mathf.Repeat(yawDeg + 180f, 360f) - 180f;
            float side = pref >= 0f ? 1f : -1f;

            // taille de la fenêtre à placer, en mètres (au moins 75 cm de haut : elle grandit vers le bas quand on la remplit)
            float wm = 0.8f, hm = 0.8f, pivY = 1f;
            var rt = t as RectTransform;
            if (rt != null)
            {
                wm = Mathf.Max(0.2f, rt.sizeDelta.x * Mathf.Abs(rt.lossyScale.x));
                hm = Mathf.Max(0.75f, rt.sizeDelta.y * Mathf.Abs(rt.lossyScale.y));
                pivY = rt.pivot.y;
            }

            var taken = new List<Taken>();
            CollectTaken(taken, t, eye, f);

            float bestYaw = 0f, bestDist = 1.2f, bestCost = float.MaxValue;
            float[] dists = { 1.2f, 1.6f };
            for (int di = 0; di < dists.Length && bestCost > 0f; di++)
            {
                float D = dists[di];
                float half = Mathf.Atan(wm * 0.5f / D) * Mathf.Rad2Deg;
                float step = half * 2f + 4f;
                float top = -0.05f + dy + (1f - pivY) * hm, bot = top - hm;
                float p1 = Mathf.Atan2(top, D) * Mathf.Rad2Deg, p0 = Mathf.Atan2(bot, D) * Mathf.Rad2Deg;
                int kmax = Mathf.Max(1, Mathf.FloorToInt(150f / step));
                for (int k = 0; k <= kmax * 2 && bestCost > 0f; k++)
                {
                    int n = (k + 1) / 2;                        // 0, 1, 1, 2, 2...
                    float yw = (k == 0 ? 0f : ((k % 2 == 1) ? side : -side) * n * step);
                    float cost = Mathf.Abs(yw) * 0.002f + di * 0.5f;   // à égalité de place libre : plus près de l'avant, plus près de toi
                    for (int i = 0; i < taken.Count; i++)
                    {
                        var o = taken[i];
                        float oy = Mathf.Min(yw + half, o.y1 + 3f) - Mathf.Max(yw - half, o.y0 - 3f);
                        float op = Mathf.Min(p1, o.p1 + 3f) - Mathf.Max(p0, o.p0 - 3f);
                        if (oy > 0f && op > 0f) cost += 10f + oy * op;
                    }
                    if (cost < bestCost) { bestCost = cost; bestYaw = yw; bestDist = D; }
                    if (cost < 1.2f) { bestCost = 0f; bestYaw = yw; bestDist = D; break; }   // libre
                }
            }

            Vector3 d = Quaternion.Euler(0f, bestYaw, 0f) * f;
            Vector3 pos = eye + d * bestDist;
            pos.y = eye.y - 0.05f + dy;
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(d));
        }
    }
}
