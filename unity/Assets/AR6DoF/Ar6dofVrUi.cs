using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Petits outils pour construire par code des menus Unity UI en World Space, cliquables avec la télécommande.</summary>
    public static class Ar6dofVrUi
    {
        // Couleurs courantes du style des menus (Ar6dofSettings)
        public static Color Bg { get { var st = Ar6dofSettings.StyleFor(Ar6dofElem.Menus); var c = st.bg; c.a = st.alpha; return c; } }
        public static Color Btn { get { return Ar6dofSettings.StyleFor(Ar6dofElem.Menus).button; } }
        public static Color Accent { get { return Ar6dofSettings.StyleFor(Ar6dofElem.Menus).accent; } }

        /// <summary>Texte en couleur d'accent, qui suit le style choisi.</summary>
        public static Text MakeAccent(this Text t)
        {
            if (t != null) Ar6dofTheme.Attach(t.gameObject, Ar6dofRole.AccentText);
            return t;
        }

        static Font font;
        public static Font GetFont()
        {
            if (font != null) return font;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 64);
            return font;
        }

        /// <summary>Canvas World Space de 'size' pixels ; 1000 pixels = 1 mètre. Pas encore actif dans la scène : à placer par l'appelant.</summary>
        public static Canvas NewCanvas(string name, Vector2 size, Ar6dofElem elem = Ar6dofElem.Menus)
        {
            var go = new GameObject(name);
            go.AddComponent<Ar6dofThemeScope>().elem = elem;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * 0.001f;
            var bg = new GameObject("Fond", typeof(RectTransform));
            bg.transform.SetParent(go.transform, false);
            var brt = (RectTransform)bg.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            var img = bg.AddComponent<Image>();
            img.raycastTarget = false;
            Ar6dofTheme.Attach(bg, Ar6dofRole.Surface);
            return c;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size) { return Child(parent, name, pos, size); }

        static RectTransform Child(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var rt = (RectTransform)g.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Text Label(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize = 26, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var rt = Child(parent, "Texte", pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = GetFont();
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = Color.white;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            Ar6dofTheme.Attach(rt.gameObject, Ar6dofRole.Text);
            Ar6dofLocText.Attach(t, text);
            return t;
        }

        public static Button MakeButton(Transform parent, string text, Vector2 pos, Vector2 size, UnityAction onClick, out Text label, int fontSize = 26)
        {
            var rt = Child(parent, "Bouton", pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            Ar6dofTheme.Attach(rt.gameObject, Ar6dofRole.Button, 24);
            if (onClick != null) b.onClick.AddListener(onClick);
            label = Label(rt, text, Vector2.zero, size, fontSize, TextAnchor.MiddleCenter);
            return b;
        }

        /// <summary>Curseur horizontal (Slider Unity UI). Le texte 'valueLabel' (facultatif) est mis à jour par l'appelant.</summary>
        public static Slider MakeSlider(Transform parent, Vector2 pos, Vector2 size, float min, float max, float value, UnityAction<float> onChange)
        {
            var root = Child(parent, "Curseur", pos, size);
            var bgImg = root.gameObject.AddComponent<Image>();
            Ar6dofTheme.Attach(root.gameObject, Ar6dofRole.Track, 14);

            var fillArea = Child(root, "Zone remplie", Vector2.zero, Vector2.zero);
            fillArea.anchorMin = new Vector2(0f, 0.3f); fillArea.anchorMax = new Vector2(1f, 0.7f);
            fillArea.offsetMin = new Vector2(14f, 0f); fillArea.offsetMax = new Vector2(-14f, 0f);
            var fill = Child(fillArea, "Remplissage", Vector2.zero, new Vector2(10f, 0f));
            var fillImg = fill.gameObject.AddComponent<Image>();
            Ar6dofTheme.Attach(fill.gameObject, Ar6dofRole.Accent, 8);

            var handleArea = Child(root, "Zone poignee", Vector2.zero, Vector2.zero);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(14f, 0f); handleArea.offsetMax = new Vector2(-14f, 0f);
            var handle = Child(handleArea, "Poignee", Vector2.zero, new Vector2(30f, 0f));
            var hImg = handle.gameObject.AddComponent<Image>();
            Ar6dofTheme.Attach(handle.gameObject, Ar6dofRole.Handle, 15);

            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hImg;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            s.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        /// <summary>Conteneur collé en haut du canvas, de hauteur nulle : placer les éléments avec des y négatifs (vers le bas), puis appeler FitHeight.</summary>
        public static RectTransform NewContent(Canvas c)
        {
            var rt = Child(c.transform, "Contenu", Vector2.zero, Vector2.zero);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>Ajuste la hauteur du canvas au contenu (hauteur en pixels). Le haut reste en place.</summary>
        public static void FitHeight(Canvas c, float height)
        {
            var rt = (RectTransform)c.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        }

        /// <summary>Roue crantée dessinée par code (icône du bouton Réglages).</summary>
        public static Sprite GearSprite()
        {
            const int N = 96;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dy, dx);
                    float outer = 34f + (Mathf.Sin(a * 8f) > 0f ? 9f : 0f);
                    bool on = r < outer && r > 14f;
                    px[y * N + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
