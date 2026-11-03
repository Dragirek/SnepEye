using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Écrans virtuels (1 à 3) autour de toi, plats mais disposés en arc, fixes dans la pièce. Chaque écran affiche un moniteur
    /// Windows capturé directement par le GPU (ar6dof_screen.dll) et se pilote à la télécommande (voir Ar6dofScreenPanel).
    /// Conseil : installer un pilote d'écran virtuel Windows (Virtual Display Driver) à 1920x1080 / 60 Hz : tu glisses tes
    /// fenêtres dessus, et le casque ne se capture jamais lui-même.
    /// À mettre sur un objet de la scène, hors du Rig. Renseigner 'Screen Shader' = Ar6dofScreen.shader.
    /// </summary>
    public class Ar6dofScreens : MonoBehaviour
    {
        public static Ar6dofScreens Instance;

        public Ar6dofPassthrough passthrough;
        public Ar6dofHeadRig rig;
        public Ar6dofPointer pointer;
        public Shader screenShader;
        public int maxScreens = 3;
        public float startDistance = 1.6f;
        public float startWidth = 1.6f;
        [Tooltip("Hauteur du centre de l'écran par rapport aux yeux (m)")]
        public float heightOffset = -0.05f;
        public float hingeDegrees = 25f;
        public bool snapEnabled = true;
        public float snapDistance = 0.3f;
        public int defaultFps = 60;
        [Tooltip("Image à l'envers : décocher")]
        public bool flipY = true;

        public Shader ScreenShader { get { return screenShader; } }
        public readonly List<Ar6dofScreenPanel> panels = new List<Ar6dofScreenPanel>();
        public Transform Head
        {
            get
            {
                if (passthrough != null && passthrough.HeadView != null) return passthrough.HeadView;
                return Camera.main != null ? Camera.main.transform : null;
            }
        }

        const string PrefKey = "ar6dof_screens_v1";
        bool built;
        public bool Built { get { return built; } }

        // ---- données sauvegardées ----
        [Serializable] class Item { public int monitor, fps, parent, side; public bool closed; public Vector3 pos, euler; public float width; }
        [Serializable] class Cfg { public List<Item> items = new List<Item>(); public float hinge = 25f; public bool snap = true; }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
#endif

        void Awake()
        {
            Instance = this;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (Exception) { }   // coordonnées de souris en vrais pixels
#endif
        }

        void OnDestroy() { if (Instance == this) Instance = null; Ar6dofScreenNative.StopAll(); }
        void OnApplicationQuit() { Ar6dofScreenNative.StopAll(); }

        void Update()
        {
            if (!built)
            {
                if (Head == null) return;
                if (rig == null) rig = FindFirstObjectByType<Ar6dofHeadRig>();
                if (rig != null && !rig.HasPose) return;
                Build();
            }
        }

        // ------------------------------------------------------------------ création
        void Build()
        {
            built = true;
            if (passthrough == null) passthrough = FindFirstObjectByType<Ar6dofPassthrough>();
            if (pointer == null) pointer = FindFirstObjectByType<Ar6dofPointer>();
            if (screenShader == null) screenShader = Shader.Find("AR6DoF/Screen");
            if (screenShader == null) { Debug.LogError("AR6DoF : renseigne 'Screen Shader' (Ar6dofScreen.shader) sur Ar6dofScreens."); return; }
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
            {
                // ar6dof_screen.dll copie l'image dans une texture Direct3D 11 : avec Direct3D 12 ou Vulkan, Unity PLANTE.
                Debug.LogError("AR6DoF : ecrans virtuels desactives : Unity utilise " + SystemInfo.graphicsDeviceType + " au lieu de Direct3D11. " +
                               "Project Settings > Player > Other Settings > decoche 'Auto Graphics API for Windows', ne garde que Direct3D11 (en premier), redemarre Unity. " +
                               "(ou menu AR6DoF > Forcer Direct3D11)");
                return;
            }
            if (!Ar6dofScreenNative.Available) return;
            if (Ar6dofScreenNative.MonitorCount() == 0) { Debug.LogWarning("AR6DoF : aucun moniteur capturable."); return; }

            Cfg cfg = null;
            try { string j = PlayerPrefs.GetString(PrefKey, ""); if (j.Length > 0) cfg = JsonUtility.FromJson<Cfg>(j); } catch (Exception) { }
            if (cfg != null && cfg.items != null && cfg.items.Count > 0)
            {
                hingeDegrees = cfg.hinge;
                snapEnabled = cfg.snap;
                foreach (var it in cfg.items)
                {
                    if (panels.Count >= maxScreens) break;
                    var p = NewPanel(Mathf.Clamp(it.monitor, 0, Mathf.Max(0, Ar6dofScreenNative.MonitorCount() - 1)), it.fps > 0 ? it.fps : defaultFps, Mathf.Clamp(it.width, 0.3f, 8f));
                    p.transform.SetPositionAndRotation(it.pos, Quaternion.Euler(it.euler));
                    if (it.closed) p.SetContentActive(false);   // rangé dans la barre de tâches
                }
                for (int i = 0; i < cfg.items.Count && i < panels.Count; i++)
                {
                    int pi = cfg.items[i].parent;
                    if (pi >= 0 && pi < panels.Count && pi != i) { panels[i].parent = panels[pi]; panels[i].side = cfg.items[i].side >= 0 ? 1 : -1; }
                }
                RelayoutAttached();
            }
            else
            {
                var p = NewPanel(PickMonitor(), defaultFps, startWidth);
                PlaceInFront(p);
                Save();
            }
            BuildSettings();
            var kb = FindFirstObjectByType<Ar6dofKeyboard>();
            if (kb != null) { var fm = kb.GetComponent<Ar6dofFloatingMenu>(); if (fm != null) fm.Summon(); }   // le clavier se centre sous les écrans
        }

        Ar6dofScreenPanel NewPanel(int monitor, int fps, float width, long windowHandle = 0)
        {
            var go = new GameObject("Ecran virtuel " + (panels.Count + 1));
            var p = go.AddComponent<Ar6dofScreenPanel>();
            int slot = 0;
            while (slot < 8 && panels.Exists(x => x.slot == slot)) slot++;   // 4 emplacements natifs au maximum
            if (windowHandle != 0) { p.hwnd = windowHandle; p.windowTitle = Ar6dofWindows.Title(windowHandle); monitor = -1; }
            p.Init(this, slot, monitor, fps, width);
            panels.Add(p);
            return p;
        }

        void PlaceInFront(Ar6dofScreenPanel p)
        {
            Transform h = Head;
            Vector3 f = h.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            Vector3 pos = h.position + f * startDistance;
            pos.y = Mathf.Max(h.position.y + heightOffset, 0.9f);
            p.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(f));
        }

        // moniteur de départ : un qui n'héberge pas le casque, pas déjà utilisé
        int PickMonitor()
        {
            int n = Ar6dofScreenNative.MonitorCount();
            int self = -1;
            try
            {
                Vector2Int wp = Screen.mainWindowPosition;
                for (int i = 0; i < n; i++)
                {
                    int l, t, r, b;
                    if (Ar6dofScreenNative.MonitorRect(i, out l, out t, out r, out b) && wp.x + 10 >= l && wp.x + 10 < r && wp.y + 10 >= t && wp.y + 10 < b) self = i;
                }
            }
            catch (Exception) { }
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    if (pass == 0 && i == self) continue;
                    if (panels.Exists(x => x.monitor == i)) continue;
                    return i;
                }
            return 0;
        }

        // ------------------------------------------------------------------ ajout / retrait
        public void AddScreen() { AddScreen(PickMonitor()); }

        public void AddScreen(int monitor) { AddScreenCore(monitor, 0); }

        /// <summary>Écran virtuel logiciel : une fenêtre d'application affichée comme un écran, à la taille choisie (0 = on ne touche pas).</summary>
        public void AddWindowScreen(long hwnd, int clientW, int clientH)
        {
            if (!Ar6dofWindows.Alive(hwnd)) return;
            if (clientW > 0 && clientH > 0) Ar6dofWindows.SetClientSize(hwnd, clientW, clientH);
            AddScreenCore(-1, hwnd);
        }

        void AddScreenCore(int monitor, long windowHandle)
        {
            if (panels.Count >= maxScreens || panels.Count >= 4) return;
            var p = NewPanel(monitor, defaultFps, startWidth, windowHandle);
            // le premier côté libre d'un écran existant, sinon devant toi
            bool placed = false;
            foreach (var q in panels)
            {
                if (q == p) continue;
                for (int s = 1; s >= -1 && !placed; s -= 2)
                    if (!Occupied(q, s)) { p.parent = q; p.side = s; ApplyAttach(p); placed = true; }
                if (placed) break;
            }
            if (!placed) PlaceInFront(p);
            Save();
        }

        public void RemoveScreen(Ar6dofScreenPanel p)
        {
            if (panels.Count <= 1) return;   // il en reste toujours un
            foreach (var q in panels) if (q.parent == p) q.parent = null;
            panels.Remove(p);
            if (settingsFor == p) CloseSettings();
            Destroy(p.gameObject);
            Save();
        }

        public void Detach(Ar6dofScreenPanel p)
        {
            foreach (var q in panels) if (q.parent == p) q.parent = null;   // les voisins accrochés à lui restent où ils sont
            p.parent = null;
            Save();
        }

        // ------------------------------------------------------------------ collage
        public bool Occupied(Ar6dofScreenPanel p, int s)
        {
            if (p.parent != null && p.side == -s) return true;   // le parent est de l'autre côté
            foreach (var q in panels) if (q.parent == p && q.side == s) return true;
            return false;
        }

        /// <summary>Place 'c' contre son parent : bord commun, charnière de 'hingeDegrees', centres à la même hauteur.</summary>
        public void ApplyAttach(Ar6dofScreenPanel c)
        {
            var p = c.parent;
            if (p == null) return;
            float s = c.side;
            Quaternion rot = p.transform.rotation * Quaternion.Euler(0f, s * hingeDegrees, 0f);
            Vector3 edge = p.transform.position + p.transform.right * (s * p.Width * 0.5f);
            Vector3 pos = edge + rot * Vector3.right * (s * c.Width * 0.5f);
            pos.y = p.transform.position.y;
            c.transform.SetPositionAndRotation(pos, rot);
        }

        public void RelayoutAttached()
        {
            var queue = new Queue<Ar6dofScreenPanel>();
            foreach (var p in panels) if (p.parent == null) queue.Enqueue(p);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 64)
            {
                var p = queue.Dequeue();
                foreach (var c in panels) if (c.parent == p) { ApplyAttach(c); queue.Enqueue(c); }
            }
        }

        public List<Ar6dofScreenPanel> GroupOf(Ar6dofScreenPanel p)
        {
            var set = new List<Ar6dofScreenPanel> { p };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var q in panels)
                {
                    if (set.Contains(q)) continue;
                    if ((q.parent != null && set.Contains(q.parent)) || set.Exists(x => x.parent == q)) { set.Add(q); grew = true; }
                }
            }
            return set;
        }

        /// <summary>Après un déplacement : si un bord de 'p' est proche d'un bord libre d'un autre écran, on les colle.</summary>
        public void TrySnap(Ar6dofScreenPanel p)
        {
            if (!snapEnabled) return;
            var mine = GroupOf(p);
            float best = snapDistance;
            Ar6dofScreenPanel bq = null;
            int bs = 0;
            foreach (var q in panels)
            {
                if (mine.Contains(q)) continue;
                for (int s = -1; s <= 1; s += 2)
                {
                    if (Occupied(q, s)) continue;
                    // p serait du côté s de q : le bord de p tourné vers q est à -s
                    Vector3 qEdge = q.transform.position + q.transform.right * (s * q.Width * 0.5f);
                    Vector3 pEdge = p.transform.position - p.transform.right * (s * p.Width * 0.5f);
                    float d = Vector3.Distance(qEdge, pEdge);
                    if (d < best) { best = d; bq = q; bs = s; }
                }
            }
            if (bq == null) return;
            p.parent = bq;
            p.side = bs;
            RelayoutAttached();
        }

        // ------------------------------------------------------------------ clavier
        /// <summary>Position du clavier virtuel : centré sous l'ensemble des écrans, devant toi.</summary>
        public static bool TryGetKeyboardPose(Vector3 headPos, out Vector3 pos, out Quaternion rot)
        {
            pos = headPos; rot = Quaternion.identity;
            var inst = Instance;
            if (inst == null || inst.panels.Count == 0) return false;
            Vector3 dir = Vector3.zero;
            float lowest = float.MaxValue;
            foreach (var p in inst.panels)
            {
                Vector3 d = p.transform.position - headPos; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) dir += d.normalized;
                lowest = Mathf.Min(lowest, p.transform.position.y - p.Height * 0.5f);
            }
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            pos = headPos + dir * 0.75f;
            pos.y = Mathf.Clamp(lowest - 0.22f, 0.5f, headPos.y - 0.25f);
            rot = Quaternion.LookRotation(dir);
            return true;
        }

        // ------------------------------------------------------------------ sauvegarde
        public void Save()
        {
            try
            {
                var cfg = new Cfg { hinge = hingeDegrees, snap = snapEnabled };
                // les écrans-fenêtres ne sont pas conservés d'un lancement à l'autre (la poignée de la fenêtre change) : on ne sauve que les moniteurs
                var saved = panels.FindAll(x => x.hwnd == 0);
                foreach (var p in saved)
                    cfg.items.Add(new Item
                    {
                        monitor = p.monitor, fps = p.fps, width = p.width,
                        pos = p.transform.position, euler = p.transform.rotation.eulerAngles,
                        parent = p.parent != null ? saved.IndexOf(p.parent) : -1, side = p.side, closed = !p.open
                    });
                PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(cfg));
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ menu de paramètres (Canvas dans la pièce)
        Canvas settings;
        Ar6dofScreenPanel settingsFor;
        Text titleText, sourceText, fpsText, snapText, hingeText, addText;

        Text MakeText(Transform parent, string label, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
        {
            return Ar6dofVrUi.Label(parent, label, pos, size, fontSize, anchor);
        }

        Text MakeButton(string label, Vector2 pos, Vector2 size, UnityAction onClick)
        {
            Text t;
            Ar6dofVrUi.MakeButton(settings.transform, label, pos, size, onClick, out t, 26);
            return t;
        }

        void BuildSettings()
        {
            settings = Ar6dofVrUi.NewCanvas("Parametres ecrans", new Vector2(440, 580));
            var go = settings.gameObject;

            Vector2 W = new Vector2(400, 46);
            float y = 255;
            titleText = MakeText(go.transform, "", new Vector2(0, y), W, 28, TextAnchor.MiddleCenter); y -= 54;
            sourceText = MakeButton("", new Vector2(0, y), W, () => CycleSource()); y -= 52;
            fpsText = MakeButton("", new Vector2(0, y), W, () => CycleFps()); y -= 52;
            snapText = MakeButton("", new Vector2(0, y), W, () => { snapEnabled = !snapEnabled; Refresh(); Save(); }); y -= 52;
            hingeText = MakeButton("", new Vector2(0, y), W, () => CycleHinge()); y -= 52;
            addText = MakeButton("+ Ajouter un ecran", new Vector2(0, y), W, () => RequestAddScreen()); y -= 52;
            MakeButton("Retirer cet ecran", new Vector2(0, y), W, () => { if (settingsFor != null) RemoveScreen(settingsFor); }); y -= 52;
            MakeButton("Dissocier cet ecran", new Vector2(0, y), W, () => { if (settingsFor != null) Detach(settingsFor); }); y -= 52;
            MakeButton("Recentrer devant moi", new Vector2(0, y), W, () => RecenterAll()); y -= 52;
            MakeButton("Fermer", new Vector2(0, y), W, () => CloseSettings());
            go.SetActive(false);
            if (pointer != null) pointer.AddCanvas(settings);
            // réduire / fermer : ferment tous les deux le panneau ; coin bas droit : agrandit / réduit le panneau
            Ar6dofWindowChrome.Create("Parametres ecrans", settings.transform, Ar6dofElem.Menus, Ar6dofWindowChrome.CanvasHalf(settings), CloseSettings, CloseSettings);
        }

        /// <summary>Roue d'un écran : ouvre son panneau, ou le referme s'il est déjà ouvert pour cet écran.</summary>
        public void ToggleSettings(Ar6dofScreenPanel p)
        {
            if (SettingsOpenFor(p)) CloseSettings(); else OpenSettings(p);
        }

        public bool SettingsOpenFor(Ar6dofScreenPanel p)
        {
            return settings != null && settings.gameObject.activeSelf && settingsFor == p;
        }

        // ------------------------------------------------------------------ choix du nouvel écran : écran réel ou virtuel
        Canvas addDlg;
        Text addInfo, addHeader, addModeText, addBackText;
        readonly List<Button> addMonBtns = new List<Button>();
        readonly List<Text> addMonTexts = new List<Text>();
        readonly List<Button> addWinBtns = new List<Button>();
        readonly List<Text> addWinTexts = new List<Text>();
        readonly List<Ar6dofWindows.Info> addWins = new List<Ar6dofWindows.Info>();
        bool addWinMode;
        int addSizeIdx = 2;   // 1920 x 1080 par défaut
        const int DlgMax = 6, DlgWinMax = 7;

        void BuildAddDialog()
        {
            addDlg = Ar6dofVrUi.NewCanvas("Nouvel ecran", new Vector2(560, 700));
            var tr = addDlg.transform;
            var row = new Vector2(500, 50);
            Ar6dofVrUi.Label(tr, "Nouvel ecran", new Vector2(0, 315), new Vector2(500, 44), 30);
            addHeader = Ar6dofVrUi.Label(tr, "Ecrans reels (a leur resolution)", new Vector2(0, 262), new Vector2(500, 30), 20, TextAnchor.MiddleLeft);
            for (int i = 0; i < DlgMax; i++)
            {
                int idx = i;
                Text t;
                var b = Ar6dofVrUi.MakeButton(tr, "", new Vector2(0, 205 - 56 * i), row, () => AddRealScreen(idx), out t, 21);
                addMonBtns.Add(b);
                addMonTexts.Add(t);
            }
            for (int i = 0; i < DlgWinMax; i++)
            {
                int idx = i;
                Text t;
                var b = Ar6dofVrUi.MakeButton(tr, "", new Vector2(0, 205 - 56 * i), row, () => AddWindowChoice(idx), out t, 19);
                addWinBtns.Add(b);
                addWinTexts.Add(t);
            }
            Ar6dofVrUi.MakeButton(tr, "Ecran virtuel : une fenetre d'application", new Vector2(0, -150), row, () => ToggleAddMode(), out addModeText, 21);
            addInfo = Ar6dofVrUi.Label(tr, "", new Vector2(0, -232), new Vector2(500, 100), 17, TextAnchor.UpperLeft);
            addInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ar6dofVrUi.MakeButton(tr, "Annuler", new Vector2(0, -305), row, () => { if (addWinMode) { addWinMode = false; FillAddDialog(); } else CloseAddDialog(); }, out addBackText, 22);
            addDlg.gameObject.SetActive(false);
            if (pointer != null) pointer.AddCanvas(addDlg);
            Ar6dofWindowChrome.Create("Nouvel ecran", addDlg.transform, Ar6dofElem.Menus, Ar6dofWindowChrome.CanvasHalf(addDlg), CloseAddDialog, CloseAddDialog);
        }

        void CloseAddDialog() { if (addDlg != null) addDlg.gameObject.SetActive(false); }

        /// <summary>Demande quel écran ajouter : un moniteur réel (avec sa résolution) ou un écran virtuel logiciel (fenêtre d'application).</summary>
        public void RequestAddScreen()
        {
            if (panels.Count >= maxScreens || panels.Count >= 4) return;
            if (addDlg == null) BuildAddDialog();
            addWinMode = false;
            FillAddDialog();
            Transform hd = Head;
            Vector3 pos = hd != null ? hd.position + FlatForward(hd) * 0.9f : transform.position;
            if (hd != null) pos.y = hd.position.y - 0.05f;
            Vector3 toHead = hd != null ? hd.position - pos : Vector3.back;
            toHead.y = 0f;
            if (toHead.sqrMagnitude < 1e-4f) toHead = Vector3.back;
            addDlg.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-toHead.normalized));
            addDlg.gameObject.SetActive(true);
        }

        void ToggleAddMode()
        {
            if (!addWinMode) { addWinMode = true; FillAddDialog(); }
            else { addSizeIdx = (addSizeIdx + 1) % Ar6dofWindows.Presets.Length; FillAddDialog(); }   // en mode fenêtres, ce bouton change la taille
        }

        /// <summary>Remplit le dialogue : liste des moniteurs, ou liste des fenêtres ouvertes + taille choisie.</summary>
        void FillAddDialog()
        {
            if (addDlg == null) return;
            int n = Ar6dofScreenNative.MonitorCount();
            for (int i = 0; i < DlgMax; i++)
            {
                bool on = !addWinMode && i < n;
                addMonBtns[i].gameObject.SetActive(on);
                if (!on) continue;
                int l, t, r, b;
                int w = 0, h = 0;
                if (Ar6dofScreenNative.MonitorRect(i, out l, out t, out r, out b)) { w = r - l; h = b - t; }
                bool used = panels.Exists(x => x.monitor == i);
                addMonTexts[i].text = Ar6dofLoc.F("Moniteur {0} - {1} x {2}", i + 1, w, h) + (used ? Ar6dofLoc.T("  (deja affiche)") : "");
            }
            if (addWinMode)
            {
                addWins.Clear();
                addWins.AddRange(Ar6dofWindows.List());
                addHeader.text = Ar6dofLoc.T("Choisis la fenetre a afficher comme un ecran");
                for (int i = 0; i < DlgWinMax; i++)
                {
                    bool on = i < addWins.Count;
                    addWinBtns[i].gameObject.SetActive(on);
                    if (!on) continue;
                    string t = addWins[i].title;
                    if (t.Length > 40) t = t.Substring(0, 39) + "...";
                    bool used = panels.Exists(x => x.hwnd == addWins[i].hwnd);
                    addWinTexts[i].text = t + "  (" + addWins[i].width + "x" + addWins[i].height + ")" + (used ? Ar6dofLoc.T("  (deja affichee)") : "");
                }
                addModeText.text = Ar6dofLoc.T("Taille de la zone utile : ") + Ar6dofLoc.T(Ar6dofWindows.PresetName(addSizeIdx)) + Ar6dofLoc.T("  (clic = autre)");
                addInfo.text = addWins.Count == 0 ? Ar6dofLoc.T("Aucune fenetre ouverte a afficher. Ouvre une application sur ton PC, puis reviens ici.")
                                                  : Ar6dofLoc.T("La fenetre est capturee meme si elle est cachee derriere d'autres. Elle se redimensionne a la taille choisie (ex. 1920 x 1080).");
                addBackText.text = Ar6dofLoc.T("Retour");
            }
            else
            {
                addHeader.text = Ar6dofLoc.T("Ecrans reels (a leur resolution)");
                addModeText.text = Ar6dofLoc.T("Ecran virtuel : une fenetre d'application");
                for (int i = 0; i < DlgWinMax; i++) addWinBtns[i].gameObject.SetActive(false);
                addInfo.text = n == 0 ? Ar6dofLoc.T("Aucun moniteur capturable.") : Ar6dofLoc.T("Ecran virtuel logiciel : une fenetre d'application (navigateur, editeur, jeu...) affichee comme un ecran, a la resolution choisie, sans rien installer. Pour un vrai ecran Windows en plus, il faut un pilote d'affichage virtuel (voir le README).");
                addBackText.text = Ar6dofLoc.T("Annuler");
            }
        }

        void AddWindowChoice(int i)
        {
            if (i < 0 || i >= addWins.Count) return;
            var wi = addWins[i];
            var pre = Ar6dofWindows.Presets[Mathf.Clamp(addSizeIdx, 0, Ar6dofWindows.Presets.Length - 1)];
            CloseAddDialog();
            AddWindowScreen(wi.hwnd, pre[0], pre[1]);
            Refresh();
        }

        static Vector3 FlatForward(Transform t)
        {
            Vector3 f = t.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        void AddRealScreen(int monitor)
        {
            CloseAddDialog();
            AddScreen(monitor);
            Refresh();
        }

        public void OpenSettings(Ar6dofScreenPanel p)
        {
            if (settings == null) return;
            settingsFor = p;
            Transform h = Head;
            Vector3 pos = p.transform.position + Vector3.down * (p.Height * 0.5f + 0.42f);
            Vector3 toHead = h != null ? h.position - pos : -p.transform.forward;
            toHead.y = 0f;
            if (toHead.sqrMagnitude < 1e-4f) toHead = -p.transform.forward;
            pos += toHead.normalized * 0.05f;
            pos.y = Mathf.Max(pos.y, 0.5f);
            settings.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-toHead.normalized));   // le texte est lisible de ton côté
            settings.gameObject.SetActive(true);
            Refresh();
        }

        void CloseSettings() { if (settings != null) settings.gameObject.SetActive(false); settingsFor = null; }

        void Refresh()
        {
            if (settingsFor == null) return;
            titleText.text = Ar6dofLoc.F("Ecran {0} / {1}", panels.IndexOf(settingsFor) + 1, panels.Count);
            sourceText.text = Ar6dofLoc.T("Source : ") + settingsFor.SourceText();
            if (settingsFor.hwnd != 0) sourceText.text = settingsFor.SourceText() + Ar6dofLoc.T("  (clic = autre taille)");
            fpsText.text = Ar6dofLoc.F("Images / s : {0}", settingsFor.fps);
            snapText.text = Ar6dofLoc.T("Coller aux bords : ") + Ar6dofLoc.T(snapEnabled ? "OUI" : "NON");
            hingeText.text = Ar6dofLoc.F("Courbure : {0} deg", Mathf.RoundToInt(hingeDegrees));
            addText.text = Ar6dofLoc.T(panels.Count >= maxScreens ? "(3 ecrans maximum)" : "+ Ajouter un ecran");
        }

        /// <summary>Moniteur : passe au moniteur suivant. Fenêtre (écran virtuel logiciel) : change la taille de la zone utile de la fenêtre.</summary>
        void CycleSource()
        {
            if (settingsFor != null && settingsFor.hwnd != 0)
            {
                windowSizeIdx = (windowSizeIdx % (Ar6dofWindows.Presets.Length - 1)) + 1;   // 1..4 : jamais "garder"
                var pre = Ar6dofWindows.Presets[windowSizeIdx];
                Ar6dofWindows.SetClientSize(settingsFor.hwnd, pre[0], pre[1]);
                Refresh();
                return;
            }
            CycleMonitor();
        }
        int windowSizeIdx = 1;

        void CycleMonitor()
        {
            if (settingsFor == null) return;
            int n = Ar6dofScreenNative.MonitorCount();
            if (n == 0) return;
            settingsFor.SetMonitor((settingsFor.monitor + 1) % n);
            Refresh();
            Save();
        }

        void CycleFps()
        {
            if (settingsFor == null) return;
            int f = settingsFor.fps;
            settingsFor.SetFps(f < 45 ? 60 : (f < 75 ? 90 : 30));
            Refresh();
            Save();
        }

        public void CycleHinge()
        {
            float[] opts = { 0f, 15f, 25f, 35f, 45f };
            int idx = 0;
            for (int i = 0; i < opts.Length; i++) if (Mathf.Abs(opts[i] - hingeDegrees) < 1f) idx = i;
            hingeDegrees = opts[(idx + 1) % opts.Length];
            RelayoutAttached();
            Refresh();
            Save();
        }

        public void SetAllFps(int f)
        {
            defaultFps = f;
            foreach (var p in panels) p.SetFps(f);
            Save();
        }

        public void RecenterAll()
        {
            if (panels.Count == 0 || Head == null) return;
            foreach (var p in panels) p.parent = null;
            PlaceInFront(panels[0]);
            for (int i = 1; i < panels.Count; i++)
            {
                var c = panels[i];
                bool ok = false;
                for (int k = 0; k < i && !ok; k++)
                    for (int s = 1; s >= -1 && !ok; s -= 2)
                        if (!Occupied(panels[k], s)) { c.parent = panels[k]; c.side = s; ApplyAttach(c); ok = true; }
            }
            var kb = FindFirstObjectByType<Ar6dofKeyboard>();
            if (kb != null) { var fm = kb.GetComponent<Ar6dofFloatingMenu>(); if (fm != null) fm.Summon(); }
            Save();
        }
    }
}
