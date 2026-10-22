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
        public float animSeconds = 0.25f;
        public float distance = 1.0f;

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
        bool built;
        float waitStart = -1f;
        int lastSig = -1;

        Transform Head
        {
            get
            {
                if (passthrough != null && passthrough.HeadView != null) return passthrough.HeadView;
                return Camera.main != null ? Camera.main.transform : null;
            }
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (!built)
            {
                if (Head == null) return;
                if (rig == null) rig = FindFirstObjectByType<Ar6dofHeadRig>();
                if (rig != null && !rig.HasPose) return;
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
        }

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

            canvas = Ar6dofVrUi.NewCanvas("Barre de taches", new Vector2(600f, 130f));
            barRt = (RectTransform)canvas.transform;
            var ir = new GameObject("Icones", typeof(RectTransform));
            ir.transform.SetParent(canvas.transform, false);
            iconsRoot = (RectTransform)ir.transform;
            if (pointer != null) pointer.AddCanvas(canvas);
            gear = Ar6dofVrUi.GearSprite();

            if (keyboard != null)
            {
                kbApp = new KeyboardApp { kb = keyboard, title = "Clavier", icon = "KB", open = keyboard.IsVisible };
            }
            if (eyes == null) eyes = GetComponent<Ar6dofEyeTracker>();
            if (eyes == null) eyes = gameObject.AddComponent<Ar6dofEyeTracker>();
            if (eyeCal == null) eyeCal = GetComponent<Ar6dofEyeCalibration>();
            if (eyeCal == null) eyeCal = gameObject.AddComponent<Ar6dofEyeCalibration>();
            vrSettings = new Ar6dofVrSettings(this);
            settingsApp = new CanvasApp { canvas = vrSettings.canvas, title = "Reglages", icon = "", open = false };
            settingsApp.place = t => PlaceBeside(t, 35f);
            vrSettings.canvas.gameObject.SetActive(false);
            if (pointer != null) pointer.AddCanvas(vrSettings.canvas);

            // menus d'appareils Bluetooth (client Bluetooth + menus enregistrés dans ar6dof_devices.json)
            ble = GetComponent<Ar6dofBle>();
            if (ble == null) ble = gameObject.AddComponent<Ar6dofBle>();
            devMenus = gameObject.AddComponent<Ar6dofDeviceMenus>();
            devMenus.dock = this;
            devMenus.Load();

            Place();
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
            yield return settingsApp;
        }

        /// <summary>Appelé par le gestionnaire quand un menu d'appareil apparaît : branche le pointeur et choisit son emplacement.</summary>
        public void Adopt(DeviceMenu m)
        {
            if (pointer != null) pointer.AddCanvas(m.canvas);
            float yaw = -35f - 30f * (devMenus.menus.Count - 1);
            m.place = t => PlaceBeside(t, yaw, 0.28f);   // le haut du menu à peine au-dessus des yeux : il descend vers les mains
        }

        public void RefreshIcons() { if (built) RebuildIcons(); }

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
            int n = 0;
            if (sc != null)
                foreach (var p in sc.panels)
                {
                    var a = screenApps[p];
                    a.title = "Ecran " + (n + 1);
                    a.icon = "E" + (n + 1);
                    a.open = p.open;
                    apps.Add(a);
                    n++;
                }
            if (kbApp != null) apps.Add(kbApp);
            foreach (var m in devMenus.menus) apps.Add(m);

            const float cell = 108f, size = 96f;
            float total = (apps.Count + 1) * cell + 30f + size + 40f;   // + le bouton "ajouter un menu"
            barRt.sizeDelta = new Vector2(Mathf.Max(260f, total), 130f);
            float x = -total * 0.5f + 20f + size * 0.5f;
            foreach (var a in apps) { MakeIcon(a, x, size, false); x += cell; }
            MakeAddButton(x, size);
            x += cell;
            x += 30f - (cell - size);
            // séparateur + roue des réglages
            var sep = new GameObject("Separateur", typeof(RectTransform));
            sep.transform.SetParent(iconsRoot, false);
            var srt = (RectTransform)sep.transform;
            srt.anchoredPosition = new Vector2(x - size * 0.5f - 15f, 0f);
            srt.sizeDelta = new Vector2(3f, 90f);
            var simg = sep.AddComponent<Image>();
            simg.color = new Color(1f, 1f, 1f, 0.25f);
            simg.raycastTarget = false;
            MakeIcon(settingsApp, x, size, true);
        }

        void MakeAddButton(float x, float size)
        {
            Text lbl;
            var b = Ar6dofVrUi.MakeButton(iconsRoot, "+", new Vector2(x, 4f), new Vector2(size, size), () =>
            {
                var m = devMenus.Create();
                RebuildIcons();
                Toggle(m);   // s'ouvre tout de suite : on est dans ses parametres a choisir l'appareil
            }, out lbl, 54);
            lbl.color = Ar6dofVrUi.Accent;
            var tip = b.gameObject.AddComponent<Ar6dofVrTooltip>();
            tip.text = () => "Nouveau menu d'appareil Bluetooth";
        }

        void MakeIcon(Ar6dofDockApp a, float x, float size, bool isGear)
        {
            Text lbl;
            Ar6dofDockApp app = a;
            var b = Ar6dofVrUi.MakeButton(iconsRoot, isGear ? "" : a.icon, new Vector2(x, 4f), new Vector2(size, size), () => Toggle(app), out lbl, 34);
            var rt = (RectTransform)b.transform;
            iconOf[a] = rt;
            if (isGear)
            {
                var g = new GameObject("Roue", typeof(RectTransform));
                g.transform.SetParent(rt, false);
                var grt = (RectTransform)g.transform;
                grt.sizeDelta = new Vector2(size * 0.75f, size * 0.75f);
                var gi = g.AddComponent<Image>();
                gi.sprite = gear;
                gi.raycastTarget = false;
            }
            else
            {
                lbl.rectTransform.anchoredPosition = new Vector2(0f, 12f);
                string shown = a.title.Length > 10 ? a.title.Substring(0, 9) + ".." : a.title;
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
            if (a.indicator != null) a.indicator.color = a.open ? Ar6dofVrUi.Accent : new Color(1f, 1f, 1f, 0f);
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
        }

        // ------------------------------------------------------------------ placement
        /// <summary>Place la barre sous les écrans, au centre de leur arc ; fixe dans la pièce.</summary>
        public void Place()
        {
            Transform h = Head;
            if (h == null || canvas == null) return;
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
