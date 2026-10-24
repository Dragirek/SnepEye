using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Sélecteur de couleur pour les menus VR : un anneau de teinte et, dedans, un carré saturation / luminosité.
    /// Les deux se règlent en visant et en maintenant le bouton. Le sélecteur ne garde aucune couleur : l'appelant lui donne
    /// la teinte / saturation / luminosité courantes avec SetHsv() et reçoit les changements par les deux événements.
    /// </summary>
    public class Ar6dofVrColorPicker : MonoBehaviour
    {
        public Action<float> onHue;                 // teinte 0..1
        public Action<float, float> onSv;           // saturation, luminosité 0..1

        const float InnerRatio = 0.80f;             // rayon intérieur de l'anneau / rayon extérieur
        RectTransform ringRt, squareRt, ringMark, squareMark;
        RawImage squareImg;
        Texture2D squareTex;
        float texHue = -1f;
        float radius, side;
        static Texture2D ringTex;

        static Texture2D Ring()
        {
            if (ringTex != null) return ringTex;
            const int N = 256;
            ringTex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // bords adoucis (1 pixel) : dehors < 1, dedans > InnerRatio
                    float edge = 1.5f / c;
                    float a = Mathf.Clamp01((1f - r) / edge) * Mathf.Clamp01((r - InnerRatio) / edge);
                    float h = Mathf.Atan2(dy, dx) / (2f * Mathf.PI);
                    if (h < 0f) h += 1f;
                    Color col = Color.HSVToRGB(h, 1f, 1f);
                    px[y * N + x] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(a * 255f));
                }
            ringTex.SetPixels32(px);
            ringTex.Apply(true);
            ringTex.wrapMode = TextureWrapMode.Clamp;
            ringTex.filterMode = FilterMode.Bilinear;
            return ringTex;
        }

        /// <summary>Crée le sélecteur : 'size' = diamètre de l'anneau en pixels de menu.</summary>
        public static Ar6dofVrColorPicker Make(Transform parent, Vector2 pos, float size, Action<float> onHue, Action<float, float> onSv)
        {
            var root = Ar6dofVrUi.Rect(parent, "SelecteurCouleur", pos, new Vector2(size, size));
            var p = root.gameObject.AddComponent<Ar6dofVrColorPicker>();
            p.onHue = onHue;
            p.onSv = onSv;
            p.radius = size * 0.5f;
            p.side = size * InnerRatio * 0.96f / 1.4142f;   // côté du carré inscrit dans l'anneau (un peu de marge)

            // anneau de teinte
            var ring = Ar6dofVrUi.Rect(root, "Anneau", Vector2.zero, new Vector2(size, size));
            var raw = ring.gameObject.AddComponent<RawImage>();
            raw.texture = Ring();
            var hp = ring.gameObject.AddComponent<Ar6dofRingHandler>();
            hp.owner = p;
            p.ringRt = ring;

            // carré saturation / luminosité
            var sq = Ar6dofVrUi.Rect(root, "Carre", Vector2.zero, new Vector2(p.side, p.side));
            p.squareImg = sq.gameObject.AddComponent<RawImage>();
            p.squareTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            p.squareTex.wrapMode = TextureWrapMode.Clamp;
            p.squareTex.filterMode = FilterMode.Bilinear;
            p.squareImg.texture = p.squareTex;
            var sh = sq.gameObject.AddComponent<Ar6dofSquareHandler>();
            sh.owner = p;
            p.squareRt = sq;

            p.ringMark = MakeMark(ring, 24f);
            p.squareMark = MakeMark(sq, 22f);
            p.SetHsv(0f, 1f, 1f);
            return p;
        }

        static RectTransform MakeMark(RectTransform parent, float d)
        {
            var o = Ar6dofVrUi.Rect(parent, "Repere", Vector2.zero, new Vector2(d, d));
            var oi = o.gameObject.AddComponent<Image>();
            oi.color = Color.black;
            oi.raycastTarget = false;
            Ar6dofTheme.SetRounded(oi, 100);
            var i = Ar6dofVrUi.Rect(o, "Centre", Vector2.zero, new Vector2(d - 6f, d - 6f));
            var ii = i.gameObject.AddComponent<Image>();
            ii.color = Color.white;
            ii.raycastTarget = false;
            Ar6dofTheme.SetRounded(ii, 100);
            return o;
        }

        /// <summary>Met à jour l'affichage (couleur du carré, repères) sans déclencher d'événement.</summary>
        public void SetHsv(float h, float s, float v)
        {
            h = Mathf.Repeat(h, 1f);
            if (Mathf.Abs(h - texHue) > 0.002f) RebuildSquare(h);
            float ang = h * 2f * Mathf.PI;
            float mid = radius * (1f + InnerRatio) * 0.5f;
            ringMark.anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * mid;
            squareMark.anchoredPosition = new Vector2((Mathf.Clamp01(s) - 0.5f) * side, (Mathf.Clamp01(v) - 0.5f) * side);
        }

        void RebuildSquare(float h)
        {
            texHue = h;
            const int N = 32;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Color c = Color.HSVToRGB(h, x / (N - 1f), y / (N - 1f));
                    px[y * N + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
                }
            squareTex.SetPixels32(px);
            squareTex.Apply(false);
        }

        static bool Local(RectTransform rt, PointerEventData e, out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local);
        }

        internal void PickRing(PointerEventData e, bool start)
        {
            Vector2 l;
            if (!Local(ringRt, e, out l)) return;
            if (start && l.magnitude < radius * InnerRatio * 0.9f) return;   // appui dans le carré / hors anneau : ignoré
            float h = Mathf.Atan2(l.y, l.x) / (2f * Mathf.PI);
            if (h < 0f) h += 1f;
            if (onHue != null) onHue(h);
        }

        internal void PickSquare(PointerEventData e)
        {
            Vector2 l;
            if (!Local(squareRt, e, out l)) return;
            float s = Mathf.Clamp01(l.x / side + 0.5f), v = Mathf.Clamp01(l.y / side + 0.5f);
            if (onSv != null) onSv(s, v);
        }
    }

    class Ar6dofRingHandler : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Ar6dofVrColorPicker owner;
        public void OnPointerDown(PointerEventData e) { owner.PickRing(e, true); }
        public void OnDrag(PointerEventData e) { owner.PickRing(e, false); }
    }

    class Ar6dofSquareHandler : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Ar6dofVrColorPicker owner;
        public void OnPointerDown(PointerEventData e) { owner.PickSquare(e); }
        public void OnDrag(PointerEventData e) { owner.PickSquare(e); }
    }

    /// <summary>
    /// Défilement vertical d'une page de menu : appuie sur une zone vide et glisse (le contenu suit le laser), ou molette d'une souris.
    /// La page est le 'content' ; 'viewport' la fenêtre qui la découpe. Les boutons et curseurs gardent la priorité sur le glissement.
    /// </summary>
    public class Ar6dofVrScrollArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public RectTransform content, viewport;
        public Func<float> maxScroll;          // hauteur en plus à découvrir (pixels de menu)
        public Action onChanged;
        public float offset;                   // 0 = haut de la page
        Vector2 last;
        bool have;

        public float Max { get { return maxScroll != null ? Mathf.Max(0f, maxScroll()) : 0f; } }

        public void SetOffset(float v)
        {
            offset = Mathf.Clamp(v, 0f, Max);
            if (content != null) content.anchoredPosition = new Vector2(content.anchoredPosition.x, offset);
            if (onChanged != null) onChanged();
        }

        public void By(float delta) { SetOffset(offset + delta); }

        public void OnPointerDown(PointerEventData e)
        {
            have = RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, e.position, e.pressEventCamera, out last);
        }

        public void OnDrag(PointerEventData e)
        {
            Vector2 cur;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, e.position, e.pressEventCamera, out cur)) return;
            if (have) SetOffset(offset + (cur.y - last.y));
            last = cur;
            have = true;
        }
    }
}
