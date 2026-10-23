using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Le panneau de réglages en réalité virtuelle (roue du dock) : image, écrans, système, yeux, style, dock et langue.
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
        readonly List<Action> live = new List<Action>();   // textes qui changent tout seuls (onglet Yeux)
        int curTab;
        float nextLive;

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
        int styleSub;               // 0 couleurs, 1 forme
        float hue, sat, val = 1f;
        readonly List<Button> elemButtons = new List<Button>();
        readonly List<Button> chipButtons = new List<Button>();
        readonly List<Image> chipSwatches = new List<Image>();
        readonly List<Button> subButtons = new List<Button>();
        RectTransform colorsBox, formBox, shapeBox, texBox, laserBox, separateBox;
        Text scopeLbl, texNameLbl;

        // onglet Langue
        readonly List<Button> langButtons = new List<Button>();
        readonly List<string> langCodes = new List<string>();

        const float W = 640f, H = 900f;
        const float PageH = H - 250f;

        public Ar6dofVrSettings(Ar6dofDock d)
        {
            dock = d;
            canvas = Ar6dofVrUi.NewCanvas("Reglages VR", new Vector2(W, H));
            Ar6dofVrUi.Label(canvas.transform, "Reglages", new Vector2(0f, H * 0.5f - 36f), new Vector2(W - 40f, 50f), 34);
            string[] names = { "Image", "Ecrans", "Systeme", "Yeux", "Style", "Dock", "Langue" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                Text t;
                var b = Ar6dofVrUi.MakeButton(canvas.transform, names[i], new Vector2(-270f + 90f * i, H * 0.5f - 92f), new Vector2(86f, 46f), () => ShowTab(idx), out t, 17);
                tabButtons.Add(b);
            }
            for (int i = 0; i < names.Length; i++)
            {
                var go = new GameObject("Page" + i, typeof(RectTransform));
                go.transform.SetParent(canvas.transform, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(W, PageH);
                rt.anchoredPosition = Vector2.zero;
                pages.Add(rt);
            }
            BuildPassthrough(pages[0]);
            BuildScreens(pages[1]);
            BuildSystem(pages[2]);
            BuildEyes(pages[3]);
            BuildStyle(pages[4]);
            BuildDock(pages[5]);
            BuildLanguage(pages[6]);
            BuildBottom();
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
            foreach (var a in live) a();
            UpdateStatus();
        }

        /// <summary>Appelé à chaque image par la barre de tâches : textes vivants de l'onglet Yeux, état Enregistré / Aperçu.</summary>
        public void Tick()
        {
            if (canvas == null || !canvas.gameObject.activeInHierarchy) return;
            float now = Time.unscaledTime;
            if (resetArmed && now > resetUntil) { resetArmed = false; UpdateResetLabel(); }
            if (now < nextLive) return;
            nextLive = now + 0.25f;
            if (curTab == 3) foreach (var a in live) a();
            UpdateStatus();
        }

        public void ShowTab(int i)
        {
            curTab = i;
            for (int k = 0; k < pages.Count; k++)
            {
                pages[k].gameObject.SetActive(k == i);
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

        void Slider(RectTransform page, ref float y, string label, float min, float max, Func<float> get, Action<float> set, string fmt = "0.##")
        {
            Text lbl = Ar6dofVrUi.Label(page, "", new Vector2(0f, y + 22f), new Vector2(W - 80f, 32f), 24, TextAnchor.MiddleLeft);
            Action upd = () => lbl.text = Ar6dofLoc.T(label) + " : " + get().ToString(fmt);
            var s = Ar6dofVrUi.MakeSlider(page, new Vector2(0f, y - 18f), new Vector2(W - 80f, 44f), min, max, get(), v => { set(v); upd(); });
            refreshers.Add(() => { s.SetValueWithoutNotify(Mathf.Clamp(get(), min, max)); upd(); });
            upd();
            y -= 84f;
        }

        void Toggle(RectTransform page, float x, float y, string label, Func<bool> get, Action<bool> set, float width = 290f)
        {
            Text t = null;
            Action upd = () => t.text = (get() ? "[x] " : "[  ] ") + Ar6dofLoc.T(label);
            Ar6dofVrUi.MakeButton(page, "", new Vector2(x, y), new Vector2(width, 48f), () => { set(!get()); upd(); }, out t, 20);
            t.alignment = TextAnchor.MiddleLeft;
            t.rectTransform.offsetMin = new Vector2(12f, 0f);
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

        static void Touch() { Ar6dofSettings.Notify(); }

        // ---- bas du panneau : Enregistrer / Annuler / Par defaut ----
        void BuildBottom()
        {
            float by = -H * 0.5f + 50f;
            Text t;
            saveBtn = Ar6dofVrUi.MakeButton(canvas.transform, "Enregistrer", new Vector2(-205f, by), new Vector2(190f, 56f), OnSave, out t, 22);
            Ar6dofVrUi.MakeButton(canvas.transform, "Annuler", new Vector2(0f, by), new Vector2(190f, 56f), OnCancel, out t, 22);
            Ar6dofVrUi.MakeButton(canvas.transform, "Par defaut", new Vector2(205f, by), new Vector2(190f, 56f), OnReset, out resetLbl, 22);
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

        // ---- pages ----
        void BuildPassthrough(RectTransform page)
        {
            var pt = dock.passthrough;
            float y = TopY;
            if (pt == null) { Ar6dofVrUi.Label(page, "Passthrough introuvable dans la scene", new Vector2(0f, y), new Vector2(W - 60f, 40f), 24); return; }
            Slider(page, ref y, "FOV affiche (deg)", 40f, 130f, () => pt.settings.displayFovDeg, v => pt.settings.displayFovDeg = v, "0");
            Slider(page, ref y, "IPD logiciel (ecarte +)", -0.4f, 0.4f, () => pt.settings.ipdShift, v => pt.settings.ipdShift = v, "0.00");
            Slider(page, ref y, "Delai manuel (ms)", 0f, 120f, () => pt.settings.captureDelayMs, v => pt.settings.captureDelayMs = v, "0");
            y -= 10f;
            Toggle(page, -160f, y, "Echanger G / D", () => pt.settings.swapEyes, v => pt.settings.swapEyes = v);
            Toggle(page, 160f, y, "Yeux lies", () => pt.settings.linkEyes, v => pt.settings.linkEyes = v); y -= 56f;
            Toggle(page, -160f, y, "Calibration camera", () => pt.settings.useCalibration, v => pt.settings.useCalibration = v);
            Toggle(page, 160f, y, "Reprojection", () => pt.settings.reproject, v => pt.settings.reproject = v); y -= 56f;
            Toggle(page, -160f, y, "Delai auto", () => pt.settings.autoDelay, v => pt.settings.autoDelay = v);
            Toggle(page, 160f, y, "Etirer les bords", () => pt.settings.clampEdges, v => pt.settings.clampEdges = v); y -= 56f;
            Toggle(page, -160f, y, "Rendu a l'envers", () => pt.settings.flipComposite, v => pt.settings.flipComposite = v); y -= 66f;
            Btn(page, 0f, y, 360f, "Sauver ces reglages d'image", () => { pt.SaveSettings(); Say("Reglages d'image sauves"); });
        }

        void BuildScreens(RectTransform page)
        {
            float y = TopY;
            Ar6dofVrUi.Label(page, "Images / s de tous les ecrans", new Vector2(0f, y), new Vector2(W - 60f, 34f), 24, TextAnchor.MiddleLeft); y -= 52f;
            Btn(page, -200f, y, 180f, "30", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(30); });
            Btn(page, 0f, y, 180f, "60", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(60); });
            Btn(page, 200f, y, 180f, "90", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(90); }); y -= 70f;
            Toggle(page, 0f, y, "Coller les ecrans aux bords", () => Ar6dofScreens.Instance != null && Ar6dofScreens.Instance.snapEnabled,
                   v => { if (Ar6dofScreens.Instance != null) { Ar6dofScreens.Instance.snapEnabled = v; Ar6dofScreens.Instance.Save(); } }, 400f); y -= 64f;
            Text hinge = ButtonText(page, 0f, y, 400f, "", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.CycleHinge(); });
            refreshers.Add(() => hinge.text = Ar6dofLoc.F("Courbure : {0} deg", Ar6dofScreens.Instance != null ? Mathf.RoundToInt(Ar6dofScreens.Instance.hingeDegrees) : 0));
            y -= 64f;
            Btn(page, 0f, y, 400f, "+ Ajouter un ecran", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.AddScreen(); }); y -= 64f;
            Btn(page, 0f, y, 400f, "Recentrer les ecrans devant moi", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.RecenterAll(); }); y -= 80f;
            Ar6dofVrUi.Label(page, "La resolution d'un ecran est celle du moniteur Windows\ncapture : change-la dans Windows (ou dans le pilote\nd'ecran virtuel). Un ecran range dans la barre ne\nconsomme rien.", new Vector2(0f, y - 30f), new Vector2(W - 60f, 120f), 20, TextAnchor.UpperLeft);
        }

        void BuildSystem(RectTransform page)
        {
            float y = TopY;
            if (dock.launcher != null)
            {
                Ar6dofVrUi.Label(page, "Suivi de la tete", new Vector2(0f, y), new Vector2(W - 60f, 34f), 24, TextAnchor.MiddleLeft); y -= 52f;
                Btn(page, 0f, y, 420f, "Recentrer le tracking", () => dock.launcher.Send('T')); y -= 64f;
                Btn(page, 0f, y, 420f, "Verrouiller le sol", () => dock.launcher.Send('V')); y -= 64f;
                Btn(page, 0f, y, 420f, "Remettre le cap a zero", () => dock.launcher.Send('Z')); y -= 76f;
            }
            Ar6dofVrUi.Label(page, "Telecommande : filtre du rayon", new Vector2(0f, y), new Vector2(W - 60f, 34f), 24, TextAnchor.MiddleLeft); y -= 52f;
            Slider(page, ref y, "Stabilite (plus bas = plus stable)", 0.5f, 8f, () => Ar6dofSettings.Current.remoteMinCutoff, v => { Ar6dofSettings.Current.remoteMinCutoff = v; Touch(); }, "0.0");
            Slider(page, ref y, "Reactivite en mouvement", 0f, 1f, () => Ar6dofSettings.Current.remoteBeta, v => { Ar6dofSettings.Current.remoteBeta = v; Touch(); }, "0.00");
            Slider(page, ref y, "Anticipation (ms)", 0f, 40f, () => Ar6dofSettings.Current.remotePredictMs, v => { Ar6dofSettings.Current.remotePredictMs = v; Touch(); }, "0");
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
            live.Add(() =>
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
            subButtons.Add(Btn(page, -150f, 140f, 280f, "Couleurs", () => { styleSub = 0; RefreshAll(); }));
            subButtons.Add(Btn(page, 150f, 140f, 280f, "Forme", () => { styleSub = 1; RefreshAll(); }));

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
            float y = 26f;
            Slider(colorsBox, ref y, "Teinte", 0f, 1f, () => hue, v => { hue = v; SetHsv(); }, "0.00");
            Slider(colorsBox, ref y, "Saturation", 0f, 1f, () => sat, v => { sat = v; SetHsv(); }, "0.00");
            Slider(colorsBox, ref y, "Luminosite", 0f, 1f, () => val, v => { val = v; SetHsv(); }, "0.00");
            Ar6dofVrUi.Label(colorsBox, "Palettes", new Vector2(-250f, -200f), new Vector2(200f, 28f), 18, TextAnchor.MiddleLeft);
            string[] palNames = { "Nuit", "Rouge", "Vert", "Violet", "Clair" };
            for (int i = 0; i < palNames.Length; i++)
            {
                int pi = i;
                Btn(colorsBox, -240f + 120f * i, -240f, 112f, palNames[i], () => ApplyPalette(pi), 18);
            }

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
            laserBox = NewBox(formBox, "Rayon");
            float ly = -24f;
            Slider(laserBox, ref ly, "Epaisseur du rayon (mm)", 1f, 15f, () => CurStyle(Ar6dofElem.Laser).lineWidth * 1000f, v => { CurStyle(Ar6dofElem.Laser).lineWidth = v / 1000f; Touch(); }, "0.0");

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

        void ApplyPalette(int p)
        {
            var st = CurStyle(selElem);
            switch (p)
            {
                case 0: st.bg = new Color(0.08f, 0.09f, 0.11f); st.button = new Color(0.22f, 0.26f, 0.32f); st.accent = new Color(0.25f, 0.85f, 1f); st.text = Color.white; break;
                case 1: st.bg = new Color(0.14f, 0.05f, 0.06f); st.button = new Color(0.36f, 0.14f, 0.16f); st.accent = new Color(1f, 0.30f, 0.30f); st.text = Color.white; break;
                case 2: st.bg = new Color(0.05f, 0.12f, 0.07f); st.button = new Color(0.14f, 0.32f, 0.20f); st.accent = new Color(0.35f, 1f, 0.45f); st.text = Color.white; break;
                case 3: st.bg = new Color(0.10f, 0.06f, 0.16f); st.button = new Color(0.27f, 0.18f, 0.40f); st.accent = new Color(0.75f, 0.45f, 1f); st.text = Color.white; break;
                default: st.bg = new Color(0.92f, 0.93f, 0.95f); st.button = new Color(0.72f, 0.76f, 0.82f); st.accent = new Color(0.10f, 0.45f, 0.85f); st.text = new Color(0.08f, 0.09f, 0.11f); break;
            }
            Touch();
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
            separateBox.gameObject.SetActive(selElem != Ar6dofElem.Global);
            colorsBox.gameObject.SetActive(styleSub == 0);
            formBox.gameObject.SetActive(styleSub == 1);
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
