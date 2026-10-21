using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Le panneau F1 en réalité virtuelle : réglages du passthrough, des écrans et du système, cliquables à la télécommande.</summary>
    public class Ar6dofVrSettings
    {
        public Canvas canvas;
        readonly Ar6dofDock dock;
        readonly List<RectTransform> pages = new List<RectTransform>();
        readonly List<Image> tabImages = new List<Image>();
        readonly List<Action> refreshers = new List<Action>();

        const float W = 640f, H = 780f;

        public Ar6dofVrSettings(Ar6dofDock d)
        {
            dock = d;
            canvas = Ar6dofVrUi.NewCanvas("Reglages VR", new Vector2(W, H));
            Ar6dofVrUi.Label(canvas.transform, "Reglages", new Vector2(0f, H * 0.5f - 36f), new Vector2(W - 40f, 50f), 34);
            string[] names = { "Passthrough", "Ecrans", "Systeme" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                Text t;
                var b = Ar6dofVrUi.MakeButton(canvas.transform, names[i], new Vector2(-200f + 200f * i, H * 0.5f - 96f), new Vector2(190f, 48f), () => ShowTab(idx), out t, 24);
                tabImages.Add(b.GetComponent<Image>());
            }
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Page" + i, typeof(RectTransform));
                go.transform.SetParent(canvas.transform, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(W, H - 150f);
                rt.anchoredPosition = new Vector2(0f, -60f);
                pages.Add(rt);
            }
            BuildPassthrough(pages[0]);
            BuildScreens(pages[1]);
            BuildSystem(pages[2]);
            ShowTab(0);
        }

        public void ShowTab(int i)
        {
            for (int k = 0; k < pages.Count; k++)
            {
                pages[k].gameObject.SetActive(k == i);
                tabImages[k].color = k == i ? new Color(0.2f, 0.55f, 0.7f, 1f) : Ar6dofVrUi.Btn;
            }
            foreach (var r in refreshers) r();
        }

        // ---- éléments ----
        float TopY { get { return (H - 150f) * 0.5f - 30f; } }

        void Slider(RectTransform page, ref float y, string label, float min, float max, Func<float> get, Action<float> set, string fmt = "0.##")
        {
            Text lbl = Ar6dofVrUi.Label(page, "", new Vector2(0f, y + 22f), new Vector2(W - 80f, 32f), 24, TextAnchor.MiddleLeft);
            Action upd = () => lbl.text = label + " : " + get().ToString(fmt);
            var s = Ar6dofVrUi.MakeSlider(page, new Vector2(0f, y - 18f), new Vector2(W - 80f, 44f), min, max, get(), v => { set(v); upd(); });
            refreshers.Add(() => { s.SetValueWithoutNotify(Mathf.Clamp(get(), min, max)); upd(); });
            upd();
            y -= 84f;
        }

        void Toggle(RectTransform page, float x, float y, string label, Func<bool> get, Action<bool> set)
        {
            Text t = null;
            Action upd = () => t.text = (get() ? "[x] " : "[  ] ") + label;
            Ar6dofVrUi.MakeButton(page, "", new Vector2(x, y), new Vector2(290f, 48f), () => { set(!get()); upd(); }, out t, 20);
            t.alignment = TextAnchor.MiddleLeft;
            t.rectTransform.offsetMin = new Vector2(12f, 0f);
            refreshers.Add(upd);
            upd();
        }

        Text Button(RectTransform page, float x, float y, float w, string label, Action click, int fs = 24)
        {
            Text t;
            Ar6dofVrUi.MakeButton(page, label, new Vector2(x, y), new Vector2(w, 48f), () => click(), out t, fs);
            return t;
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
            Button(page, 0f, y, 300f, "Sauver ces reglages", () => pt.SaveSettings());
        }

        void BuildScreens(RectTransform page)
        {
            float y = TopY;
            Ar6dofVrUi.Label(page, "Images / s de tous les ecrans", new Vector2(0f, y), new Vector2(W - 60f, 34f), 24, TextAnchor.MiddleLeft); y -= 52f;
            Button(page, -200f, y, 180f, "30", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(30); });
            Button(page, 0f, y, 180f, "60", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(60); });
            Button(page, 200f, y, 180f, "90", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.SetAllFps(90); }); y -= 70f;
            Toggle(page, 0f, y, "Coller les ecrans aux bords", () => Ar6dofScreens.Instance != null && Ar6dofScreens.Instance.snapEnabled,
                   v => { if (Ar6dofScreens.Instance != null) { Ar6dofScreens.Instance.snapEnabled = v; Ar6dofScreens.Instance.Save(); } }); y -= 64f;
            Text hinge = Button(page, 0f, y, 400f, "", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.CycleHinge(); });
            refreshers.Add(() => hinge.text = "Courbure : " + (Ar6dofScreens.Instance != null ? Mathf.RoundToInt(Ar6dofScreens.Instance.hingeDegrees) : 0) + " deg");
            y -= 64f;
            Button(page, 0f, y, 400f, "+ Ajouter un ecran", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.AddScreen(); }); y -= 64f;
            Button(page, 0f, y, 400f, "Recentrer les ecrans devant moi", () => { if (Ar6dofScreens.Instance != null) Ar6dofScreens.Instance.RecenterAll(); }); y -= 80f;
            Ar6dofVrUi.Label(page, "La resolution d'un ecran est celle du moniteur Windows\ncapture : change-la dans Windows (ou dans le pilote\nd'ecran virtuel). Un ecran range dans la barre ne\nconsomme rien.", new Vector2(0f, y - 30f), new Vector2(W - 60f, 120f), 20, TextAnchor.UpperLeft);
        }

        void BuildSystem(RectTransform page)
        {
            float y = TopY;
            Button(page, 0f, y, 420f, "Recentrer la barre de taches", () => dock.Recenter()); y -= 64f;
            Button(page, 0f, y, 420f, "Recentrer le clavier", () => dock.RecenterKeyboard()); y -= 64f;
            if (dock.launcher != null)
            {
                y -= 10f;
                Ar6dofVrUi.Label(page, "Suivi de la tete", new Vector2(0f, y), new Vector2(W - 60f, 34f), 24, TextAnchor.MiddleLeft); y -= 52f;
                Button(page, 0f, y, 420f, "Recentrer le tracking", () => dock.launcher.Send('T')); y -= 64f;
                Button(page, 0f, y, 420f, "Verrouiller le sol", () => dock.launcher.Send('V')); y -= 64f;
                Button(page, 0f, y, 420f, "Remettre le cap a zero", () => dock.launcher.Send('Z'));
            }
        }
    }
}
