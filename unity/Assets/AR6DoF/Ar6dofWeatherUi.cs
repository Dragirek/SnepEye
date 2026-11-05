using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Petites briques de dessin pour les éléments graphiques de la météo (aucune texture : tout est calculé).</summary>
    static class WxDraw
    {
        static void V(VertexHelper vh, Vector2 p, Color c)
        {
            var v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            vh.AddVert(v);
        }

        public static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int i = vh.currentVertCount;
            V(vh, a, col); V(vh, b, col); V(vh, c, col);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
        {
            int i = vh.currentVertCount;
            V(vh, a, col); V(vh, b, col); V(vh, c, col); V(vh, d, col);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        public static void Rect(VertexHelper vh, Vector2 min, Vector2 max, Color col)
        {
            Quad(vh, new Vector2(min.x, min.y), new Vector2(min.x, max.y), new Vector2(max.x, max.y), new Vector2(max.x, min.y), col);
        }

        public static void Disc(VertexHelper vh, Vector2 c, float r, Color col, int seg = 20)
        {
            int i = vh.currentVertCount;
            V(vh, c, col);
            for (int k = 0; k <= seg; k++)
            {
                float a = 2f * Mathf.PI * k / seg;
                V(vh, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, col);
            }
            for (int k = 0; k < seg; k++) vh.AddTriangle(i, i + 1 + k, i + 2 + k);
        }

        public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color col)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            d.Normalize();
            Vector2 n = new Vector2(-d.y, d.x) * (w * 0.5f);
            Quad(vh, a + n, b + n, b - n, a - n, col);
        }

        /// <summary>Trait aux bouts ronds.</summary>
        public static void Pill(VertexHelper vh, Vector2 a, Vector2 b, float w, Color col)
        {
            Line(vh, a, b, w, col);
            Disc(vh, a, w * 0.5f, col, 10);
            Disc(vh, b, w * 0.5f, col, 10);
        }

        /// <summary>Portion d'anneau entre deux rayons, d'un angle à l'autre (degrés).</summary>
        public static void Arc(VertexHelper vh, Vector2 c, float rIn, float rOut, float a0, float a1, Color col, int seg = 24)
        {
            int i = vh.currentVertCount;
            for (int k = 0; k <= seg; k++)
            {
                float a = Mathf.Lerp(a0, a1, (float)k / seg) * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                V(vh, c + d * rIn, col);
                V(vh, c + d * rOut, col);
            }
            for (int k = 0; k < seg; k++)
            {
                int j = i + 2 * k;
                vh.AddTriangle(j, j + 1, j + 3);
                vh.AddTriangle(j + 3, j + 2, j);
            }
        }
    }

    /// <summary>Fond de ciel : rectangle à coins arrondis dégradé du haut (zénith) vers le bas (horizon).</summary>
    public class Ar6dofSkyGraphic : Ar6dofRasterGfx
    {
        Color top = new Color(0.30f, 0.58f, 0.92f), bottom = new Color(0.66f, 0.86f, 0.98f);
        float radius = 24f;

        protected override float Supersample { get { return 0.5f; } }   // simple degrade : texture petite, redessinee vite (a chaque eclair)

        public void SetColors(Color topColor, Color bottomColor, float cornerRadius)
        {
            top = topColor; bottom = bottomColor; radius = cornerRadius;
            SetVerticesDirty();
        }

        static void AddArc(List<Vector2> pts, Vector2 c, float r, float a0, float a1)
        {
            const int N = 8;
            for (int k = 0; k <= N; k++)
            {
                float a = Mathf.Lerp(a0, a1, (float)k / N) * Mathf.Deg2Rad;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }

        protected override void Draw(VertexHelper vh, Rect r)
        {
            if (r.width < 2f || r.height < 2f) return;
            float rad = Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f);
            var pts = new List<Vector2>(40);
            AddArc(pts, new Vector2(r.xMin + rad, r.yMin + rad), rad, 180f, 270f);
            AddArc(pts, new Vector2(r.xMax - rad, r.yMin + rad), rad, 270f, 360f);
            AddArc(pts, new Vector2(r.xMax - rad, r.yMax - rad), rad, 0f, 90f);
            AddArc(pts, new Vector2(r.xMin + rad, r.yMax - rad), rad, 90f, 180f);
            var ctr = r.center;
            var v = UIVertex.simpleVert;
            v.position = ctr;
            v.color = Color.Lerp(bottom, top, 0.5f);
            vh.AddVert(v);
            for (int i = 0; i < pts.Count; i++)
            {
                v.position = pts[i];
                v.color = Color.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, pts[i].y));
                vh.AddVert(v);
            }
            int n = pts.Count;
            for (int i = 0; i < n; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % n);
        }
    }

    /// <summary>Icône de condition météo dessinée par code : soleil, lune, nuages, pluie, neige, éclair, brouillard.</summary>
    public class Ar6dofWxIcon : Ar6dofRasterGfx
    {
        WxCond cond = WxCond.Clear;
        bool day = true;

        public void Set(WxCond c, bool isDay)
        {
            cond = c; day = isDay;
            SetVerticesDirty();
        }

        static readonly Color SunCol = new Color(1f, 0.84f, 0.26f), RayCol = new Color(1f, 0.70f, 0.16f);
        static readonly Color CloudWhite = new Color(0.97f, 0.98f, 1f), CloudGray = new Color(0.70f, 0.74f, 0.82f), CloudDark = new Color(0.46f, 0.50f, 0.60f);
        static readonly Color RainCol = new Color(0.38f, 0.68f, 1f), SnowCol = new Color(1f, 1f, 1f), MoonCol = new Color(0.98f, 0.96f, 0.80f);
        static readonly Color BoltCol = new Color(1f, 0.90f, 0.30f), HailCol = new Color(0.82f, 0.94f, 1f);

        Vector2 o;
        float u;

        Vector2 P(float x, float y) { return o + new Vector2(x, y) * u; }

        void Sun(VertexHelper vh, float x, float y, float r)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                WxDraw.Pill(vh, P(x, y) + d * (r * 1.45f * u), P(x, y) + d * (r * 1.95f * u), r * 0.34f * u, RayCol);
            }
            WxDraw.Disc(vh, P(x, y), r * u, SunCol, 24);
        }

        void Moon(VertexHelper vh, float x, float y, float r)
        {
            WxDraw.Arc(vh, P(x, y), r * 0.42f * u, r * u, 55f, 305f, MoonCol, 28);
            WxDraw.Disc(vh, P(x - r * 0.92f, y + r * 0.04f), r * 0.14f * u, MoonCol, 8);   // rondeur du croissant
        }

        void Cloud(VertexHelper vh, float x, float y, float s, Color col)
        {
            WxDraw.Disc(vh, P(x - 0.17f * s, y - 0.03f * s), 0.14f * s * u, col, 16);
            WxDraw.Disc(vh, P(x + 0.00f * s, y + 0.06f * s), 0.20f * s * u, col, 20);
            WxDraw.Disc(vh, P(x + 0.19f * s, y - 0.03f * s), 0.15f * s * u, col, 16);
            WxDraw.Rect(vh, P(x - 0.17f * s, y - 0.17f * s), P(x + 0.19f * s, y - 0.02f * s), col);
        }

        void Drops(VertexHelper vh, float y, int n, Color col, float w)
        {
            float step = 0.17f;
            float x0 = -step * (n - 1) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float x = x0 + i * step;
                float yy = y - (i % 2) * 0.05f;
                WxDraw.Pill(vh, P(x + 0.04f, yy), P(x - 0.03f, yy - 0.14f), w * u, col);
            }
        }

        void Flakes(VertexHelper vh, float y, int n, Color col, float r)
        {
            float step = 0.15f;
            float x0 = -step * (n - 1) * 0.5f;
            for (int i = 0; i < n; i++) WxDraw.Disc(vh, P(x0 + i * step, y - (i % 2) * 0.09f), r * u, col, 8);
        }

        void Bolt(VertexHelper vh, float x, float y)
        {
            WxDraw.Quad(vh, P(x + 0.05f, y), P(x - 0.06f, y - 0.15f), P(x + 0.03f, y - 0.15f), P(x + 0.12f, y), BoltCol);
            WxDraw.Tri(vh, P(x - 0.02f, y - 0.11f), P(x + 0.12f, y - 0.11f), P(x - 0.05f, y - 0.30f), BoltCol);
        }

        void SunOrMoon(VertexHelper vh, float x, float y, float r)
        {
            if (day) Sun(vh, x, y, r); else Moon(vh, x, y, r * 1.15f);
        }

        protected override void Draw(VertexHelper vh, Rect rc)
        {
            u = Mathf.Min(rc.width, rc.height);
            o = rc.center;
            if (u < 4f) return;
            switch (cond)
            {
                case WxCond.Clear:
                    SunOrMoon(vh, 0f, 0f, 0.20f);
                    if (!day) { WxDraw.Disc(vh, P(0.30f, 0.28f), 0.025f * u, MoonCol, 6); WxDraw.Disc(vh, P(-0.30f, 0.20f), 0.02f * u, MoonCol, 6); }
                    break;
                case WxCond.MostlyClear:
                    SunOrMoon(vh, -0.06f, 0.08f, 0.17f);
                    Cloud(vh, 0.12f, -0.14f, 0.7f, CloudWhite);
                    break;
                case WxCond.PartlyCloudy:
                    SunOrMoon(vh, 0.10f, 0.14f, 0.15f);
                    Cloud(vh, -0.04f, -0.08f, 1.0f, CloudWhite);
                    break;
                case WxCond.Overcast:
                    Cloud(vh, 0.10f, 0.12f, 0.8f, CloudGray);
                    Cloud(vh, -0.04f, -0.06f, 1.05f, CloudWhite);
                    break;
                case WxCond.Fog:
                    Cloud(vh, 0f, 0.14f, 0.9f, CloudWhite);
                    WxDraw.Pill(vh, P(-0.22f, -0.12f), P(0.24f, -0.12f), 0.055f * u, CloudGray);
                    WxDraw.Pill(vh, P(-0.14f, -0.24f), P(0.30f, -0.24f), 0.055f * u, CloudGray);
                    WxDraw.Pill(vh, P(-0.28f, -0.36f), P(0.16f, -0.36f), 0.055f * u, CloudGray);
                    break;
                case WxCond.Drizzle:
                    Cloud(vh, 0f, 0.10f, 1.0f, CloudGray);
                    Drops(vh, -0.20f, 2, RainCol, 0.035f);
                    break;
                case WxCond.Rain:
                    Cloud(vh, 0f, 0.10f, 1.0f, CloudGray);
                    Drops(vh, -0.20f, 3, RainCol, 0.045f);
                    break;
                case WxCond.HeavyRain:
                    Cloud(vh, 0f, 0.12f, 1.0f, CloudDark);
                    Drops(vh, -0.19f, 4, RainCol, 0.05f);
                    break;
                case WxCond.Showers:
                    SunOrMoon(vh, 0.12f, 0.20f, 0.12f);
                    Cloud(vh, -0.03f, 0.04f, 0.95f, CloudGray);
                    Drops(vh, -0.26f, 3, RainCol, 0.045f);
                    break;
                case WxCond.FreezingRain:
                    Cloud(vh, 0f, 0.10f, 1.0f, CloudGray);
                    WxDraw.Pill(vh, P(-0.12f, -0.20f), P(-0.19f, -0.34f), 0.045f * u, RainCol);
                    WxDraw.Pill(vh, P(0.18f, -0.20f), P(0.11f, -0.34f), 0.045f * u, RainCol);
                    WxDraw.Disc(vh, P(0.03f, -0.28f), 0.035f * u, SnowCol, 8);
                    break;
                case WxCond.Snow:
                    Cloud(vh, 0f, 0.10f, 1.0f, CloudGray);
                    Flakes(vh, -0.24f, 3, SnowCol, 0.04f);
                    break;
                case WxCond.HeavySnow:
                    Cloud(vh, 0f, 0.12f, 1.0f, CloudDark);
                    Flakes(vh, -0.22f, 4, SnowCol, 0.042f);
                    break;
                case WxCond.SnowShowers:
                    SunOrMoon(vh, 0.12f, 0.20f, 0.12f);
                    Cloud(vh, -0.03f, 0.04f, 0.95f, CloudGray);
                    Flakes(vh, -0.26f, 3, SnowCol, 0.04f);
                    break;
                case WxCond.Thunder:
                    Cloud(vh, 0f, 0.14f, 1.0f, CloudDark);
                    Bolt(vh, 0.02f, -0.06f);
                    break;
                default:
                    Cloud(vh, 0f, 0.14f, 1.0f, CloudDark);
                    WxDraw.Disc(vh, P(-0.16f, -0.18f), 0.045f * u, HailCol, 10);
                    WxDraw.Disc(vh, P(0.00f, -0.28f), 0.045f * u, HailCol, 10);
                    WxDraw.Disc(vh, P(0.16f, -0.18f), 0.045f * u, HailCol, 10);
                    break;
            }
        }
    }

    /// <summary>Courbe de température sur les prochaines heures, avec la probabilité de pluie en barres.</summary>
    public class Ar6dofWxChart : Ar6dofRasterGfx
    {
        float[] temp = new float[0], pop = new float[0];
        Color lineCol = new Color(1f, 0.7f, 0.2f);
        public float tMin, tMax;

        public void Set(float[] temperatures, float[] rainChance, Color line)
        {
            temp = temperatures ?? new float[0];
            pop = rainChance ?? new float[0];
            lineCol = line;
            tMin = 999f; tMax = -999f;
            for (int i = 0; i < temp.Length; i++) { tMin = Mathf.Min(tMin, temp[i]); tMax = Mathf.Max(tMax, temp[i]); }
            if (temp.Length == 0) { tMin = 0f; tMax = 1f; }
            if (tMax - tMin < 3f) { float m = (tMax + tMin) * 0.5f; tMin = m - 1.5f; tMax = m + 1.5f; }
            SetVerticesDirty();
        }

        protected override void Draw(VertexHelper vh, Rect r)
        {
            int n = temp.Length;
            if (n < 2 || r.width < 10f) return;
            float x0 = r.xMin + 56f, x1 = r.xMax - 16f, y0 = r.yMin + 12f, y1 = r.yMax - 12f;
            // quadrillage
            Color grid = new Color(1f, 1f, 1f, 0.14f);
            for (int g = 0; g < 3; g++)
            {
                float yy = Mathf.Lerp(y0, y1, g * 0.5f);
                WxDraw.Rect(vh, new Vector2(r.xMin + 50f, yy - 0.75f), new Vector2(r.xMax - 8f, yy + 0.75f), grid);
            }
            float slot = (x1 - x0) / (n - 1);
            // probabilité de pluie : barres
            Color bar = new Color(0.40f, 0.68f, 1f, 0.45f);
            for (int i = 0; i < n && i < pop.Length; i++)
            {
                float h = Mathf.Clamp01(pop[i] / 100f) * (y1 - y0) * 0.6f;
                if (h < 1.5f) continue;
                float cx = x0 + i * slot;
                WxDraw.Rect(vh, new Vector2(cx - slot * 0.32f, r.yMin + 2f), new Vector2(cx + slot * 0.32f, r.yMin + 2f + h), bar);
            }
            // courbe
            Vector2 prev = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.InverseLerp(tMin, tMax, temp[i]);
                Vector2 p = new Vector2(x0 + i * slot, Mathf.Lerp(y0, y1, t));
                if (i > 0) WxDraw.Line(vh, prev, p, 4f, lineCol);
                prev = p;
            }
            for (int i = 0; i < n; i += 3)
            {
                float t = Mathf.InverseLerp(tMin, tMax, temp[i]);
                WxDraw.Disc(vh, new Vector2(x0 + i * slot, Mathf.Lerp(y0, y1, t)), 6f, lineCol, 12);
            }
        }
    }
}
