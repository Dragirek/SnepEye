using System;
using System.Collections.Generic;
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

        /// <summary>Tete de l'utilisateur (renseignee par Ar6dofPassthrough) : sert a masquer le dos des menus.</summary>
        public static Transform ViewHead;

        static Font font;
        public static Font GetFont()
        {
            if (font != null) return font;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 64);
            return font;
        }

        /// <summary>Taille d'un pixel d'interface en mètres : 1 mm x 0,5 x le réglage général "Taille de l'interface" (100 % = fenêtres d'environ 40 cm).</summary>
        public const float UnitScale = 0.0005f;
        public static float BaseScale { get { return UnitScale * Mathf.Clamp(Ar6dofSettings.Current.uiScale, 0.4f, 3f); } }

        /// <summary>Annoncé quand la taille générale change : reçoit la nouvelle BaseScale.</summary>
        public static event Action<float> UiScaleChanged;

        static readonly Dictionary<Canvas, float> tracked = new Dictionary<Canvas, float>();   // canvas -> BaseScale avec laquelle il a été dimensionné

        /// <summary>À appeler quand le réglage général change : met à l'échelle les fenêtres déjà ouvertes en gardant leur redimensionnement manuel.</summary>
        public static void ApplyUiScale()
        {
            float now = BaseScale;
            var keys = new List<Canvas>(tracked.Keys);
            foreach (var k in keys)
            {
                if (k == null) { tracked.Remove(k); continue; }
                float was = tracked[k];
                if (Mathf.Abs(now - was) < 1e-8f) continue;
                k.transform.localScale *= now / was;
                tracked[k] = now;
            }
            var ev = UiScaleChanged;
            if (ev != null) ev(now);
        }

        /// <summary>Canvas World Space de 'size' pixels ; 1 pixel = 0,5 mm x la taille générale. Pas encore actif dans la scène : à placer par l'appelant.
        /// scaled = faux : garde 1 pixel = 1 mm (étalonnage, où la géométrie est calculée en pixels). Le dock gère lui-même son échelle.</summary>
        public static Canvas NewCanvas(string name, Vector2 size, Ar6dofElem elem = Ar6dofElem.Menus, bool scaled = true)
        {
            var go = new GameObject(name);
            go.AddComponent<Ar6dofThemeScope>().elem = elem;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<Ar6dofBackFace>();   // le dos du menu est vide (pas de texte a l'envers)
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * (scaled ? BaseScale : 0.001f);
            if (scaled && elem != Ar6dofElem.Dock) tracked[c] = BaseScale;
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
            t.alignByGeometry = true;   // centré sur le dessin réel des lettres, pas sur leurs marges internes
            t.color = Color.white;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            Ar6dofTheme.Attach(rt.gameObject, Ar6dofRole.Text);
            Ar6dofLocText.Attach(t, text);
            return t;
        }

        /// <summary>
        /// Place le texte d'un bouton (ou n'importe quel texte) à une position et une taille PRÉCISES, en pixels du menu, relatives au centre de son parent.
        /// À utiliser à la place de offsetMin / offsetMax : ces deux-là, sur un texte ancré au centre, donnent une largeur négative (texte invisible) ou minuscule.
        /// </summary>
        public static void PlaceLabel(Text t, Vector2 pos, Vector2 size, TextAnchor anchor)
        {
            if (t == null) return;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            t.alignment = anchor;
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
            label.gameObject.AddComponent<Ar6dofFitText>().Init(label, fontSize);   // un texte trop long rétrécit au lieu de dépasser du bouton
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

        /// <summary>Curseur vertical (fader de table de mixage) : le bas = min, le haut = max. La poignée est large, comme le capuchon d'un fader.</summary>
        public static Slider MakeVSlider(Transform parent, Vector2 pos, Vector2 size, float min, float max, float value, UnityAction<float> onChange)
        {
            var root = Child(parent, "Fader", pos, size);
            root.gameObject.AddComponent<Image>();
            Ar6dofTheme.Attach(root.gameObject, Ar6dofRole.Track, 14);

            var fillArea = Child(root, "Zone remplie", Vector2.zero, Vector2.zero);
            fillArea.anchorMin = new Vector2(0.42f, 0f); fillArea.anchorMax = new Vector2(0.58f, 1f);
            fillArea.offsetMin = new Vector2(0f, 16f); fillArea.offsetMax = new Vector2(0f, -16f);
            var fill = Child(fillArea, "Remplissage", Vector2.zero, new Vector2(0f, 10f));
            fill.gameObject.AddComponent<Image>();
            Ar6dofTheme.Attach(fill.gameObject, Ar6dofRole.Accent, 8);

            var handleArea = Child(root, "Zone poignee", Vector2.zero, Vector2.zero);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(0f, 16f); handleArea.offsetMax = new Vector2(0f, -16f);
            var handle = Child(handleArea, "Poignee", Vector2.zero, new Vector2(0f, 32f));
            var hImg = handle.gameObject.AddComponent<Image>();
            Ar6dofTheme.Attach(handle.gameObject, Ar6dofRole.Handle, 8);

            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hImg;
            s.direction = Slider.Direction.BottomToTop;
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

    /// <summary>Réduit la taille d'un texte (une seule ligne) pour qu'il tienne dans sa zone, sans jamais dépasser sa taille de départ. Se remet à jour quand le texte change.</summary>
    public class Ar6dofFitText : MonoBehaviour
    {
        Text t;
        int baseSize, minSize;
        string last;
        float lastW = -1f;

        public void Init(Text text, int size)
        {
            t = text;
            baseSize = size;
            minSize = Mathf.Max(9, Mathf.RoundToInt(size * 0.55f));
        }

        void LateUpdate()
        {
            if (t == null) return;
            string cur = t.text;
            float w = ((RectTransform)transform).rect.width;
            if ((object)cur == (object)last && Mathf.Abs(w - lastW) < 0.5f) return;
            last = cur;
            lastW = w;
            if (string.IsNullOrEmpty(cur) || t.horizontalOverflow != HorizontalWrapMode.Overflow) return;
            float avail = w - 14f;
            if (avail < 20f) return;
            t.fontSize = baseSize;
            float pw = t.preferredWidth;
            if (pw > avail) t.fontSize = Mathf.Max(minSize, Mathf.FloorToInt(baseSize * avail / pw));
        }
    }
}
