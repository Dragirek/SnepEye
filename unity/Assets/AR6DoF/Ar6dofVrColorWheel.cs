using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Roue de couleur (teinte = angle, saturation = distance du centre) pour les menus VR. La luminosité se règle avec un curseur à côté.</summary>
    public class Ar6dofVrColorWheel : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<float, float> onChanged;   // teinte 0..1, saturation 0..1
        public RectTransform marker;
        RectTransform rect;
        static Texture2D wheelTex;

        static Texture2D Wheel()
        {
            if (wheelTex != null) return wheelTex;
            const int N = 192;
            wheelTex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > 1f) { px[y * N + x] = new Color32(0, 0, 0, 0); continue; }
                    float h = Mathf.Atan2(dy, dx) / (2f * Mathf.PI);
                    if (h < 0f) h += 1f;
                    Color col = Color.HSVToRGB(h, r, 1f);
                    px[y * N + x] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), 255);
                }
            wheelTex.SetPixels32(px);
            wheelTex.Apply(true);
            wheelTex.filterMode = FilterMode.Bilinear;
            return wheelTex;
        }

        public static Ar6dofVrColorWheel Make(Transform parent, Vector2 pos, float size, float hue, float sat, Action<float, float> onChanged)
        {
            var rt = Ar6dofVrUi.Rect(parent, "RoueCouleur", pos, new Vector2(size, size));
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = Wheel();
            var w = rt.gameObject.AddComponent<Ar6dofVrColorWheel>();
            w.rect = rt;
            w.onChanged = onChanged;
            var mk = Ar6dofVrUi.Rect(rt, "Repere", Vector2.zero, new Vector2(22f, 22f));
            var mi = mk.gameObject.AddComponent<Image>();
            mi.color = new Color(1f, 1f, 1f, 0.9f);
            mi.raycastTarget = false;
            w.marker = mk;
            w.SetHueSat(hue, sat);
            return w;
        }

        public void SetHueSat(float h, float s)
        {
            if (marker == null || rect == null) return;
            float ang = h * 2f * Mathf.PI;
            float rad = Mathf.Clamp01(s) * rect.sizeDelta.x * 0.5f;
            marker.anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
        }

        void Pick(PointerEventData e)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out local)) return;
            float R = rect.sizeDelta.x * 0.5f;
            Vector2 v = local / R;
            float s = Mathf.Clamp01(v.magnitude);
            float h = Mathf.Atan2(v.y, v.x) / (2f * Mathf.PI);
            if (h < 0f) h += 1f;
            SetHueSat(h, s);
            if (onChanged != null) onChanged(h, s);
        }

        public void OnPointerDown(PointerEventData e) { Pick(e); }
        public void OnDrag(PointerEventData e) { Pick(e); }
    }
}
