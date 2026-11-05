using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Dessin « par code » (cadrans, visuels du son, icônes météo...) affiché comme une IMAGE : le dessin est calculé en maillage virtuel (VertexHelper),
    /// puis rempli triangle par triangle dans une texture, montrée par un RawImage. Les maillages de Graphic personnalisés ne s'affichaient pas dans les menus VR
    /// (cause non identifiée), alors que les images à texture s'affichent partout : on passe par elles.
    /// Rien n'est recalculé tant que SetVerticesDirty() n'est pas appelé ; au plus un redessin par image. Les classes dérivées ne font que remplir Draw().
    /// </summary>
    public abstract class Ar6dofRasterGfx : RawImage
    {
        /// <summary>Pixels de texture par pixel de menu (2 = lissage des bords par sur-échantillonnage).</summary>
        protected virtual float Supersample { get { return 2f; } }
        /// <summary>Couleur des pixels transparents (évite un liseré sombre au lissage).</summary>
        protected virtual Color ClearRgb { get { return Color.white; } }
        /// <summary>Dessine dans 'r' (repère local du composant). Utiliser WxDraw ou vh.AddVert / AddTriangle.</summary>
        protected abstract void Draw(VertexHelper vh, Rect r);

        Texture2D tex;
        float[] buf;
        Color32[] px;
        int tw, th;
        bool dirty = true, shown;
        VertexHelper helper;
        readonly List<UIVertex> stream = new List<UIVertex>(2048);

        /// <summary>Demande un redessin (au plus un par image).</summary>
        public new void SetVerticesDirty() { dirty = true; }

        protected override void OnEnable()
        {
            base.OnEnable();
            dirty = true;
            if (!shown) color = new Color(1f, 1f, 1f, 0f);   // invisible tant que rien n'est dessiné (sinon un carré blanc)
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            dirty = true;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (tex != null) Destroy(tex);
            if (helper != null) helper.Dispose();
        }

        void LateUpdate()
        {
            if (!dirty) return;
            Rect r = rectTransform.rect;
            if (r.width < 2f || r.height < 2f) return;
            dirty = false;
            Render(r);
        }

        void Render(Rect r)
        {
            float s = Mathf.Max(0.5f, Supersample);
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width * s), 4, 1024);
            int h = Mathf.Clamp(Mathf.RoundToInt(r.height * s), 4, 1024);
            if (tex == null || w != tw || h != th)
            {
                if (tex != null) Destroy(tex);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, s > 1.01f);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                buf = new float[w * h * 4];
                px = new Color32[w * h];
                tw = w; th = h;
                texture = tex;
            }
            if (helper == null) helper = new VertexHelper();
            helper.Clear();
            Draw(helper, r);
            stream.Clear();
            helper.GetUIVertexStream(stream);

            Array.Clear(buf, 0, buf.Length);
            for (int i = 0; i + 2 < stream.Count; i += 3) Fill(stream[i], stream[i + 1], stream[i + 2], r);

            Color cr = ClearRgb;
            byte cr8 = (byte)Mathf.RoundToInt(Mathf.Clamp01(cr.r) * 255f), cg8 = (byte)Mathf.RoundToInt(Mathf.Clamp01(cr.g) * 255f), cb8 = (byte)Mathf.RoundToInt(Mathf.Clamp01(cr.b) * 255f);
            int n = w * h;
            for (int i = 0; i < n; i++)
            {
                int k = i * 4;
                float a = buf[k + 3];
                if (a < 0.003f) { px[i] = new Color32(cr8, cg8, cb8, 0); continue; }
                float inv = 1f / a;
                px[i] = new Color32((byte)Mathf.Min(255, (int)(buf[k] * inv * 255f + 0.5f)), (byte)Mathf.Min(255, (int)(buf[k + 1] * inv * 255f + 0.5f)),
                                    (byte)Mathf.Min(255, (int)(buf[k + 2] * inv * 255f + 0.5f)), (byte)Mathf.Min(255, (int)(a * 255f + 0.5f)));
            }
            tex.SetPixels32(px);
            tex.Apply(s > 1.01f, false);
            if (!shown) { shown = true; color = Color.white; }
        }

        /// <summary>Remplit un triangle (couleurs des sommets interpolées) par-dessus ce qui est déjà dessiné.</summary>
        void Fill(UIVertex a, UIVertex b, UIVertex c, Rect r)
        {
            float sx = tw / r.width, sy = th / r.height;
            float ax = (a.position.x - r.xMin) * sx, ay = (a.position.y - r.yMin) * sy;
            float bx = (b.position.x - r.xMin) * sx, by = (b.position.y - r.yMin) * sy;
            float cx = (c.position.x - r.xMin) * sx, cy = (c.position.y - r.yMin) * sy;
            float den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Mathf.Abs(den) < 1e-6f) return;
            float inv = 1f / den;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))));
            int x1 = Mathf.Min(tw - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))));
            int y1 = Mathf.Min(th - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));
            if (x1 < x0 || y1 < y0) return;
            Color32 ca = a.color, cb = b.color, cc = c.color;
            bool flat = ca.r == cb.r && ca.r == cc.r && ca.g == cb.g && ca.g == cc.g && ca.b == cb.b && ca.b == cc.b && ca.a == cb.a && ca.a == cc.a;
            float fr = ca.r / 255f, fg = ca.g / 255f, fb = ca.b / 255f, fa = ca.a / 255f;
            const float eps = -0.0008f;
            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;
                for (int x = x0; x <= x1; x++)
                {
                    float pxx = x + 0.5f;
                    float w0 = ((by - cy) * (pxx - cx) + (cx - bx) * (py - cy)) * inv;
                    float w1 = ((cy - ay) * (pxx - cx) + (ax - cx) * (py - cy)) * inv;
                    float w2 = 1f - w0 - w1;
                    if (w0 < eps || w1 < eps || w2 < eps) continue;
                    float sr, sg, sb, sa;
                    if (flat) { sr = fr; sg = fg; sb = fb; sa = fa; }
                    else
                    {
                        sr = (w0 * ca.r + w1 * cb.r + w2 * cc.r) / 255f;
                        sg = (w0 * ca.g + w1 * cb.g + w2 * cc.g) / 255f;
                        sb = (w0 * ca.b + w1 * cb.b + w2 * cc.b) / 255f;
                        sa = (w0 * ca.a + w1 * cb.a + w2 * cc.a) / 255f;
                    }
                    if (sa <= 0f) continue;
                    int k = (y * tw + x) * 4;
                    float ia = 1f - sa;
                    buf[k] = sr * sa + buf[k] * ia;
                    buf[k + 1] = sg * sa + buf[k + 1] * ia;
                    buf[k + 2] = sb * sa + buf[k + 2] * ia;
                    buf[k + 3] = sa + buf[k + 3] * ia;
                }
            }
        }
    }
}
