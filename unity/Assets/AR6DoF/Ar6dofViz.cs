using System;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Visualisations du son de la table de mixage. Un seul composant graphique dessine tout dans UN maillage (aucun objet par barre) :
    /// il se reconstruit au plus 30 fois par seconde, et seulement tant que la fenêtre est ouverte (Feed n'est appelé que par elle).
    /// Les classiques de l'industrie (analyseur à barres avec retenue des crêtes, miroir, courbe lissée, oscilloscope, spectrogramme, aiguilles VU)
    /// et des effets plus fous dans l'esprit du Lecteur Windows Media de XP (spectre circulaire, ondes psychédéliques).
    /// </summary>
    public class Ar6dofViz : Ar6dofRasterGfx
    {
        public const int Bands = 32, Hist = 48, WaveN = 128;
        public static readonly string[] Names =
        {
            "Barres", "Miroir", "Courbe", "Oscilloscope", "Spectrogramme", "Circulaire", "Ondes psychedeliques", "Aiguilles VU"
        };
        public static int Count { get { return Names.Length; } }

        protected override float Supersample { get { return 1.5f; } }
        public int mode;
        readonly float[] lvl = new float[Bands], hold = new float[Bands], holdT = new float[Bands];
        readonly float[] wave = new float[WaveN];
        readonly float[] hist = new float[Hist * Bands];
        int histPos;
        float histAcc, needleL = -20f, needleR = -20f, clipL, clipR;
        float bass, mid, treble, phase, lastFeed = -1f, lastMesh = -1f;
        Color accent = new Color(0.3f, 0.7f, 1f), txt = Color.white;

        /// <summary>Crée la zone : un fond (rôle Track) et la trace par-dessus.</summary>
        public static Ar6dofViz Make(Transform parent, Vector2 pos, Vector2 size)
        {
            var root = Ar6dofVrUi.Rect(parent, "Visuel", pos, size);
            var bg = root.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            Ar6dofTheme.Attach(root.gameObject, Ar6dofRole.Track, 10);
            var t = Ar6dofVrUi.Rect(root, "Trace", Vector2.zero, Vector2.zero);
            t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one;
            t.offsetMin = new Vector2(4f, 4f); t.offsetMax = new Vector2(-4f, -4f);
            var v = t.gameObject.AddComponent<Ar6dofViz>();
            v.raycastTarget = false;
            return v;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Ar6dofSettings.Changed += ReadTheme;
            ReadTheme();
        }

        protected override void OnDisable()
        {
            Ar6dofSettings.Changed -= ReadTheme;
            base.OnDisable();
        }

        void ReadTheme()
        {
            try
            {
                var st = Ar6dofSettings.StyleFor(Ar6dofTheme.ElemOf(transform));
                accent = st.accent;
                txt = st.text;
                accent.a = 1f;
                txt.a = 1f;
            }
            catch (Exception) { }
            SetVerticesDirty();
        }

        // ------------------------------------------------------------------ données
        /// <summary>À appeler à chaque image par la fenêtre ouverte : lisse les niveaux et ne reconstruit le maillage que 30 fois par seconde.</summary>
        public void Feed(AudioMeters m, float now)
        {
            float dt = lastFeed < 0f ? 0.033f : Mathf.Min(0.1f, now - lastFeed);
            lastFeed = now;
            for (int i = 0; i < Bands; i++)
            {
                float v = Mathf.Clamp01(m.specOut[i]);
                lvl[i] = v > lvl[i] ? v : Mathf.Max(0f, lvl[i] - dt * 1.6f);
                if (lvl[i] >= hold[i]) { hold[i] = lvl[i]; holdT[i] = 0.4f; }
                else { holdT[i] -= dt; if (holdT[i] < 0f) hold[i] = Mathf.Max(lvl[i], hold[i] - dt * 0.9f); }
            }
            Array.Copy(m.wave, wave, WaveN);

            histAcc += dt;
            while (histAcc >= 0.05f)
            {
                histAcc -= 0.05f;
                histPos = (histPos + 1) % Hist;
                Array.Copy(lvl, 0, hist, histPos * Bands, Bands);
            }

            float k = 1f - Mathf.Exp(-dt / 0.28f);   // inertie d'un vu-mètre : ~300 ms
            needleL += (Db(m.peak[4]) - needleL) * k;
            needleR += (Db(m.peak[5]) - needleR) * k;
            clipL = m.peak[4] > 0.97f ? 0.5f : Mathf.Max(0f, clipL - dt);
            clipR = m.peak[5] > 0.97f ? 0.5f : Mathf.Max(0f, clipR - dt);

            bass = Avg(lvl, 0, 6); mid = Avg(lvl, 6, 18); treble = Avg(lvl, 18, Bands);
            phase += dt * (0.6f + bass * 2.2f);

            if (now - lastMesh >= 1f / 30f) { lastMesh = now; SetVerticesDirty(); }
        }

        /// <summary>Remet tout à zéro (changement de visuel ou arrêt du son).</summary>
        public void Clear()
        {
            Array.Clear(lvl, 0, Bands); Array.Clear(hold, 0, Bands); Array.Clear(wave, 0, WaveN); Array.Clear(hist, 0, hist.Length);
            needleL = needleR = -20f;
            SetVerticesDirty();
        }

        static float Db(float peak) { return peak <= 0.0001f ? -40f : Mathf.Clamp(20f * Mathf.Log10(peak), -40f, 6f); }
        static float Avg(float[] a, int from, int to) { float s = 0f; for (int i = from; i < to; i++) s += a[i]; return s / Mathf.Max(1, to - from); }

        // ------------------------------------------------------------------ dessin
        protected override void Draw(VertexHelper vh, Rect r)
        {
            if (r.width < 8f || r.height < 8f) return;
            switch (mode)
            {
                case 0: Bars(vh, r); break;
                case 1: Mirror(vh, r); break;
                case 2: Curve(vh, r); break;
                case 3: Scope(vh, r); break;
                case 4: Spectrogram(vh, r); break;
                case 5: Circular(vh, r); break;
                case 6: Psychedelic(vh, r); break;
                default: Needles(vh, r); break;
            }
        }

        // -- primitives
        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, ca, Vector2.zero);
            vh.AddVert(b, cb, Vector2.zero);
            vh.AddVert(c, cc, Vector2.zero);
            vh.AddVert(d, cd, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        // rectangle avec dégradé vertical (bas -> haut)
        static void Box(VertexHelper vh, float x0, float y0, float x1, float y1, Color bottom, Color top)
        {
            Quad(vh, new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0), bottom, top, top, bottom);
        }

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float thick, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.0001f) return;
            Vector2 n = new Vector2(-d.y, d.x) * (thick * 0.5f / len);
            Quad(vh, a - n, a + n, b + n, b - n, c, c, c, c);
        }

        static Color A(Color c, float a) { c.a = a; return c; }

        // vert -> jaune -> rouge, comme un analyseur de studio
        static Color Ramp(float t)
        {
            var g = new Color(0.20f, 0.85f, 0.35f);
            var y = new Color(0.96f, 0.86f, 0.20f);
            var rd = new Color(0.95f, 0.25f, 0.20f);
            return t < 0.6f ? g : t < 0.85f ? Color.Lerp(g, y, Mathf.InverseLerp(0.6f, 0.85f, t)) : Color.Lerp(y, rd, Mathf.InverseLerp(0.85f, 1f, t));
        }

        // noir -> bleu -> violet -> orange -> blanc
        static Color Heat(float v)
        {
            if (v < 0.25f) return Color.Lerp(new Color(0.02f, 0.02f, 0.10f, 0f), new Color(0.10f, 0.15f, 0.75f, 0.9f), v / 0.25f);
            if (v < 0.5f) return Color.Lerp(new Color(0.10f, 0.15f, 0.75f, 0.9f), new Color(0.75f, 0.15f, 0.75f, 1f), (v - 0.25f) / 0.25f);
            if (v < 0.75f) return Color.Lerp(new Color(0.75f, 0.15f, 0.75f, 1f), new Color(1f, 0.65f, 0.15f, 1f), (v - 0.5f) / 0.25f);
            return Color.Lerp(new Color(1f, 0.65f, 0.15f, 1f), Color.white, (v - 0.75f) / 0.25f);
        }

        // -- 1. barres avec retenue des crêtes
        void Bars(VertexHelper vh, Rect r)
        {
            float slot = r.width / Bands, bw = slot * 0.74f;
            for (int i = 0; i < Bands; i++)
            {
                float x = r.xMin + slot * (i + 0.5f) - bw * 0.5f;
                float h = Mathf.Max(2f, lvl[i] * r.height);
                Box(vh, x, r.yMin, x + bw, r.yMin + h, Ramp(0f), Ramp(lvl[i]));
                float hy = r.yMin + Mathf.Max(3f, hold[i] * r.height);
                Box(vh, x, hy - 2f, x + bw, hy + 1f, txt, txt);
            }
        }

        // -- 2. miroir : le spectre se reflète de part et d'autre d'un axe
        void Mirror(VertexHelper vh, Rect r)
        {
            float slot = r.width / Bands, bw = slot * 0.74f, cy = r.center.y, half = r.height * 0.5f;
            Color hi = Color.Lerp(accent, Color.white, 0.45f);
            for (int i = 0; i < Bands; i++)
            {
                float x = r.xMin + slot * (i + 0.5f) - bw * 0.5f;
                float h = Mathf.Max(1.5f, lvl[i] * half);
                Box(vh, x, cy, x + bw, cy + h, accent, hi);
                Box(vh, x, cy - h, x + bw, cy, A(hi, 0.55f), A(accent, 0.55f));
            }
            Line(vh, new Vector2(r.xMin, cy), new Vector2(r.xMax, cy), 1.5f, A(txt, 0.35f));
        }

        // -- 3. courbe lissée (Catmull-Rom) remplie
        void Curve(VertexHelper vh, Rect r)
        {
            const int sub = 4;
            float slot = r.width / Bands;
            Vector2 prev = Vector2.zero;
            Color hi = Color.Lerp(accent, Color.white, 0.5f);
            bool first = true;
            for (int i = 0; i < Bands - 1; i++)
            {
                float p0 = lvl[Mathf.Max(0, i - 1)], p1 = lvl[i], p2 = lvl[i + 1], p3 = lvl[Mathf.Min(Bands - 1, i + 2)];
                for (int s = 0; s < sub; s++)
                {
                    float t = s / (float)sub, t2 = t * t, t3 = t2 * t;
                    float v = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    v = Mathf.Clamp01(v);
                    var pt = new Vector2(r.xMin + slot * (i + 0.5f + t), r.yMin + v * r.height);
                    if (!first)
                    {
                        Quad(vh, new Vector2(prev.x, r.yMin), prev, pt, new Vector2(pt.x, r.yMin), A(accent, 0.04f), A(accent, 0.55f), A(accent, 0.55f), A(accent, 0.04f));
                        Line(vh, prev, pt, 3f, hi);
                    }
                    prev = pt;
                    first = false;
                }
            }
        }

        // -- 4. oscilloscope (la vraie forme d'onde de la sortie)
        void Scope(VertexHelper vh, Rect r)
        {
            float cy = r.center.y;
            for (int g = 1; g < 8; g++) Line(vh, new Vector2(r.xMin + r.width * g / 8f, r.yMin), new Vector2(r.xMin + r.width * g / 8f, r.yMax), 1f, A(txt, 0.10f));
            for (int g = 1; g < 4; g++) Line(vh, new Vector2(r.xMin, r.yMin + r.height * g / 4f), new Vector2(r.xMax, r.yMin + r.height * g / 4f), 1f, A(txt, g == 2 ? 0.25f : 0.10f));
            Vector2 prev = Vector2.zero;
            float amp = r.height * 0.48f;
            Color hi = Color.Lerp(accent, Color.white, 0.35f);
            for (int i = 0; i < WaveN; i++)
            {
                var pt = new Vector2(r.xMin + r.width * i / (WaveN - 1f), cy + Mathf.Clamp(wave[i] * 1.8f, -1f, 1f) * amp);
                if (i > 0)
                {
                    Line(vh, prev, pt, 9f, A(accent, 0.22f));   // halo
                    Line(vh, prev, pt, 2.5f, hi);
                }
                prev = pt;
            }
        }

        // -- 5. spectrogramme : le temps défile de gauche à droite, les graves en bas
        void Spectrogram(VertexHelper vh, Rect r)
        {
            float cw = r.width / Hist, ch = r.height / Bands;
            for (int c = 0; c < Hist; c++)
            {
                int col = (histPos + 1 + c) % Hist;   // la plus ancienne à gauche
                float x = r.xMin + c * cw;
                for (int b = 0; b < Bands; b++)
                {
                    float v = hist[col * Bands + b];
                    if (v < 0.04f) continue;
                    Color k = Heat(v);
                    float y = r.yMin + b * ch;
                    Quad(vh, new Vector2(x, y), new Vector2(x, y + ch), new Vector2(x + cw, y + ch), new Vector2(x + cw, y), k, k, k, k);
                }
            }
        }

        // -- 6. spectre circulaire qui tourne doucement, avec un noyau qui bat avec les graves
        void Circular(VertexHelper vh, Rect r)
        {
            const int N = Bands * 2;
            Vector2 c = r.center;
            float rmax = Mathf.Min(r.width, r.height) * 0.48f, r0 = rmax * (0.30f + bass * 0.10f);
            float rot = phase * 0.25f, step = Mathf.PI * 2f / N;
            for (int i = 0; i < N; i++)
            {
                int bi = i < Bands ? i : N - 1 - i;
                float a0 = rot + i * step, a1 = a0 + step * 0.78f;
                float len = Mathf.Max(2f, lvl[bi] * (rmax - r0));
                Color col = Color.HSVToRGB(Mathf.Repeat(i / (float)N + phase * 0.03f, 1f), 0.65f, 1f);
                Color col2 = Color.Lerp(col, Color.white, 0.5f);
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                Quad(vh, c + d0 * r0, c + d0 * (r0 + len), c + d1 * (r0 + len), c + d1 * r0, col, col2, col2, col);
            }
            // noyau
            const int seg = 40;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector2 p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * (r0 - 3f), p1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * (r0 - 3f);
                Line(vh, p0, p1, 2.5f, A(txt, 0.5f + bass * 0.5f));
            }
        }

        // -- 7. ondes psychédéliques et orbes, dans l'esprit des visualisations du Lecteur Windows Media de XP
        void Psychedelic(VertexHelper vh, Rect r)
        {
            const int ribbons = 5, seg = 44;
            float cy = r.center.y;
            for (int k = 0; k < ribbons; k++)
            {
                float e = k == 0 ? bass : k == 1 ? Avg(lvl, 4, 10) : k == 2 ? mid : k == 3 ? Avg(lvl, 14, 24) : treble;
                float amp = r.height * (0.07f + 0.36f * Mathf.Clamp01(e * 1.6f));
                float freq = 1.1f + k * 0.65f, spd = 1f + k * 0.4f;
                Color col = A(Color.HSVToRGB(Mathf.Repeat(0.08f * k + phase * 0.05f, 1f), 0.8f, 1f), 0.6f);
                Vector2 prev = Vector2.zero;
                for (int i = 0; i <= seg; i++)
                {
                    float u = i / (float)seg, x = r.xMin + u * r.width;
                    float env = Mathf.Sin(u * Mathf.PI);   // les ondes s'éteignent sur les bords
                    float y = cy + env * amp * (Mathf.Sin(u * Mathf.PI * 2f * freq + phase * spd) + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * freq * 2.3f - phase * spd * 1.7f)) * 0.7f;
                    var pt = new Vector2(x, y);
                    if (i > 0) { Line(vh, prev, pt, 11f - k, A(col, 0.22f)); Line(vh, prev, pt, 3.5f, col); }
                    prev = pt;
                }
            }
            // orbes : losanges qui flottent et gonflent avec les aigus
            for (int i = 0; i < 14; i++)
            {
                float u = Mathf.Repeat(i * 0.137f + phase * 0.02f * (1f + (i % 3)), 1f);
                float x = r.xMin + r.width * Mathf.Repeat(i * 0.311f + Mathf.Sin(phase * 0.3f + i) * 0.04f, 1f);
                float y = r.yMin + r.height * u;
                float sz = 3f + (4f + 12f * treble) * (0.4f + 0.6f * Mathf.Abs(Mathf.Sin(phase + i)));
                Color col = A(Color.HSVToRGB(Mathf.Repeat(i * 0.07f + phase * 0.04f, 1f), 0.5f, 1f), 0.75f * (1f - Mathf.Abs(u - 0.5f) * 1.6f));
                Quad(vh, new Vector2(x - sz, y), new Vector2(x, y + sz), new Vector2(x + sz, y), new Vector2(x, y - sz), col, col, col, col);
            }
        }

        // -- 8. deux aiguilles de vu-mètre (gauche / droite), graduées de -20 à +3 dB
        void Needles(VertexHelper vh, Rect r)
        {
            DrawNeedle(vh, new Rect(r.xMin, r.yMin, r.width * 0.5f, r.height), needleL, clipL > 0f);
            DrawNeedle(vh, new Rect(r.center.x, r.yMin, r.width * 0.5f, r.height), needleR, clipR > 0f);
        }

        static void DrawNeedle(VertexHelper vh, Rect r, float db, bool clip)
        {
            const float arc = 52f;   // demi-ouverture en degrés
            Vector2 pivot = new Vector2(r.center.x, r.yMin + 4f);
            float R = Mathf.Min(r.width * 0.46f, r.height * 0.9f);
            Color tick = new Color(0.85f, 0.85f, 0.85f, 0.85f), red = new Color(0.95f, 0.30f, 0.25f, 1f);
            for (int d = -20; d <= 3; d++)
            {
                float f = (d + 20f) / 23f;
                float ang = (-arc + f * 2f * arc) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                bool major = d % 5 == 0 || d == 0 || d == 3, over = d >= 0;
                float len = R * (major ? 0.12f : 0.06f);
                Line(vh, pivot + dir * (R - len), pivot + dir * R, major ? 2.5f : 1.5f, over ? red : tick);
            }
            // arc de la zone rouge (0 à +3 dB)
            for (int s = 0; s < 6; s++)
            {
                float a0 = (-arc + (20f / 23f + s / 6f * 3f / 23f) * 2f * arc) * Mathf.Deg2Rad, a1 = (-arc + (20f / 23f + (s + 1) / 6f * 3f / 23f) * 2f * arc) * Mathf.Deg2Rad;
                Line(vh, pivot + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * (R - 2f), pivot + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * (R - 2f), 4f, red);
            }
            float fr = Mathf.Clamp((db + 20f) / 23f, 0f, 1f);
            float na = (-arc + fr * 2f * arc) * Mathf.Deg2Rad;
            Vector2 nd = new Vector2(Mathf.Sin(na), Mathf.Cos(na));
            Line(vh, pivot + nd * (R * 0.12f), pivot + nd * (R * 0.94f), 3f, Color.white);
            float cs = 5f;
            Quad(vh, pivot + new Vector2(-cs, -cs) + nd * (R * 0.12f), pivot + new Vector2(-cs, cs) + nd * (R * 0.12f), pivot + new Vector2(cs, cs) + nd * (R * 0.12f), pivot + new Vector2(cs, -cs) + nd * (R * 0.12f), tick, tick, tick, tick);
            if (clip)   // voyant de saturation
            {
                var c = new Vector2(r.xMax - 12f, r.yMax - 12f);
                Quad(vh, c + new Vector2(-6f, -6f), c + new Vector2(-6f, 6f), c + new Vector2(6f, 6f), c + new Vector2(6f, -6f), red, red, red, red);
            }
        }
    }
}
