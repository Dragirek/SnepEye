using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Petits outils pour construire par code des menus Unity UI en World Space, cliquables avec la télécommande.</summary>
    public static class Ar6dofVrUi
    {
        public static readonly Color Bg = new Color(0.08f, 0.09f, 0.11f, 0.96f);
        public static readonly Color Btn = new Color(0.22f, 0.26f, 0.32f, 1f);
        public static readonly Color Accent = new Color(0.25f, 0.85f, 1f, 1f);

        static Font font;
        public static Font GetFont()
        {
            if (font != null) return font;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 64);
            return font;
        }

        /// <summary>Canvas World Space de 'size' pixels ; 1000 pixels = 1 mètre. Pas encore actif dans la scène : à placer par l'appelant.</summary>
        public static Canvas NewCanvas(string name, Vector2 size)
        {
            var go = new GameObject(name);
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
            img.color = Bg;
            img.raycastTarget = false;
            return c;
        }

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
            return t;
        }

        public static Button MakeButton(Transform parent, string text, Vector2 pos, Vector2 size, UnityAction onClick, out Text label, int fontSize = 26)
        {
            var rt = Child(parent, "Bouton", pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Btn;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.highlightedColor = new Color(0.5f, 1.2f, 1.4f, 1f);
            cb.pressedColor = new Color(0.3f, 0.7f, 0.9f, 1f);
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(onClick);
            label = Label(rt, text, Vector2.zero, size, fontSize, TextAnchor.MiddleCenter);
            return b;
        }

        /// <summary>Curseur horizontal (Slider Unity UI). Le texte 'valueLabel' (facultatif) est mis à jour par l'appelant.</summary>
        public static Slider MakeSlider(Transform parent, Vector2 pos, Vector2 size, float min, float max, float value, UnityAction<float> onChange)
        {
            var root = Child(parent, "Curseur", pos, size);
            var bgImg = root.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.16f, 0.18f, 0.22f, 1f);

            var fillArea = Child(root, "Zone remplie", Vector2.zero, Vector2.zero);
            fillArea.anchorMin = new Vector2(0f, 0.3f); fillArea.anchorMax = new Vector2(1f, 0.7f);
            fillArea.offsetMin = new Vector2(14f, 0f); fillArea.offsetMax = new Vector2(-14f, 0f);
            var fill = Child(fillArea, "Remplissage", Vector2.zero, new Vector2(10f, 0f));
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = Accent;

            var handleArea = Child(root, "Zone poignee", Vector2.zero, Vector2.zero);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(14f, 0f); handleArea.offsetMax = new Vector2(-14f, 0f);
            var handle = Child(handleArea, "Poignee", Vector2.zero, new Vector2(30f, 0f));
            var hImg = handle.gameObject.AddComponent<Image>();
            hImg.color = Color.white;

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
