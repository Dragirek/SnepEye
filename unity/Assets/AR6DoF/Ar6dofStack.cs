using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Accumulation de plusieurs images de la camera avant une photo ou un scan : on aligne les images (recherche d'un decalage sur une image reduite
    /// au quart, puis sur l'image entiere avec precision sous le pixel), on ecarte celles qui ne correspondent pas (flou de bouger), puis on additionne
    /// chaque pixel a sa vraie place sur une grille 'scale' fois plus fine (« shift and add »). Resultat : beaucoup moins de bruit (surtout dans une piece
    /// sombre, ou la camera monte son gain) et un peu plus de detail quand la tete a bouge de fractions de pixel entre les images. Ce n'est PAS un zoom magique :
    /// aucun detail qui n'etait pas dans les images ne sera invente. Port de tests/stack_ref.py (qui contient l'essai chiffre). A appeler hors du fil de Unity.
    /// </summary>
    public static class Ar6dofStack
    {
        public class Result
        {
            public Color32[] pixels;
            public int w, h;
            public int used, total;
        }

        static float[] ToGray(Color32[] f)
        {
            var g = new float[f.Length];
            for (int i = 0; i < f.Length; i++) g[i] = f[i].r * 0.299f + f[i].g * 0.587f + f[i].b * 0.114f;
            return g;
        }

        static float Sharpness(float[] g, int w, int h)
        {
            double s = 0; long n = 0;
            for (int y = 2; y < h - 2; y += 2)
                for (int x = 2; x < w - 2; x += 2)
                {
                    int i = y * w + x;
                    float lap = g[i] * 4f - g[i - w] - g[i + w] - g[i - 1] - g[i + 1];
                    s += lap * lap; n++;
                }
            return n > 0 ? (float)(s / n) : 0f;
        }

        static float[] Box4(float[] g, int w, int h, out int w4, out int h4)
        {
            w4 = w / 4; h4 = h / 4;
            var o = new float[w4 * h4];
            for (int y = 0; y < h4; y++)
                for (int x = 0; x < w4; x++)
                {
                    float s = 0f;
                    for (int j = 0; j < 4; j++)
                    {
                        int row = (y * 4 + j) * w + x * 4;
                        s += g[row] + g[row + 1] + g[row + 2] + g[row + 3];
                    }
                    o[y * w4 + x] = s * (1f / 16f);
                }
            return o;
        }

        // moyenne des carres des ecarts entre ref(x,y) et fr(x+dx,y+dy) sur x dans [x0,x1[, y dans [y0,y1[, un pixel sur 'stride'
        static float Ssd(float[] a, float[] b, int w, int x0, int x1, int y0, int y1, int stride, int dx, int dy)
        {
            double s = 0; int n = 0;
            for (int y = y0; y < y1; y += stride)
            {
                int ra = y * w, rb = (y + dy) * w + dx;
                for (int x = x0; x < x1; x += stride)
                {
                    float d = a[ra + x] - b[rb + x];
                    s += d * d; n++;
                }
            }
            return n > 0 ? (float)(s / n) : float.MaxValue;
        }

        static float Parabola(float sm, float c, float p)
        {
            float d = sm - 2f * c + p;
            if (d <= 1e-9f) return 0f;
            float o = 0.5f * (sm - p) / d;
            return Mathf.Clamp(o, -0.5f, 0.5f);
        }

        /// <summary>Decalage (dx, dy) tel que fr(x+dx, y+dy) = ref(x, y), et l'ecart restant (plus il est grand, moins l'image ressemble a la reference).</summary>
        static float Estimate(float[] rf, float[] fr, int w, int h, out float dxo, out float dyo)
        {
            dxo = 0f; dyo = 0f;
            const int R = 8;
            if (w < 200 || h < 150) return 0f;
            int w4, h4;
            var r4 = Box4(rf, w, h, out w4, out h4);
            var f4 = Box4(fr, w, h, out w4, out h4);
            int m = R + 2;
            float best = float.MaxValue; int bi = 0, bj = 0;
            for (int j = -R; j <= R; j++)
                for (int i = -R; i <= R; i++)
                {
                    float s = Ssd(r4, f4, w4, m, w4 - m, m, h4 - m, 2, i, j);
                    if (s < best) { best = s; bi = i; bj = j; }
                }
            int cdx = bi * 4, cdy = bj * 4;
            int marg = 4 * R + 8;
            const int F = 3;
            best = float.MaxValue; int dx = cdx, dy = cdy;
            for (int j = -F; j <= F; j++)
                for (int i = -F; i <= F; i++)
                {
                    float s = Ssd(rf, fr, w, marg, w - marg, marg, h - marg, 3, cdx + i, cdy + j);
                    if (s < best) { best = s; dx = cdx + i; dy = cdy + j; }
                }
            float sx = 0f, sy = 0f, c = 0f;
            for (int it = 0; it < 3; it++)   // l'entier trouve sur l'echantillonnage grossier peut etre faux de 1 : on recentre tant que la parabole bute
            {
                var g = new float[9];
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++) g[j * 3 + i] = Ssd(rf, fr, w, marg, w - marg, marg, h - marg, 1, dx - 1 + i, dy - 1 + j);
                c = g[4];
                sx = Parabola(g[3], g[4], g[5]);
                sy = Parabola(g[1], g[4], g[7]);
                int mx = sx >= 0.499f ? 1 : (sx <= -0.499f ? -1 : 0);
                int my = sy >= 0.499f ? 1 : (sy <= -0.499f ? -1 : 0);
                if (mx == 0 && my == 0) break;
                dx += mx; dy += my;
            }
            dxo = dx + sx; dyo = dy + sy;
            return c;
        }

        /// <summary>
        /// Fusionne 'frames' (toutes de w x h). 'gray' : sortie en niveaux de gris (pour la lecture de texte) avec etirement du contraste, correction des images sombres et
        /// leger renforcement des contours. 'scale' : 1 ou 2 (2 = grille deux fois plus fine ; ramene a 1 si l'image est tres grande).
        /// </summary>
        public static Result Fuse(List<Color32[]> frames, int w, int h, int scale, bool gray)
        {
            int n = frames.Count;
            var res = new Result { total = n };
            if (n == 0) return res;
            if ((long)w * h > 2600000L) scale = 1;   // la memoire de la grille fine (couleur) serait trop grande
            scale = scale < 2 ? 1 : 2;
            var gs = new List<float[]>(n);
            int k = 0; float bestSharp = -1f;
            for (int i = 0; i < n; i++)
            {
                gs.Add(ToGray(frames[i]));
                float sh = Sharpness(gs[i], w, h);
                if (sh > bestSharp) { bestSharp = sh; k = i; }
            }
            var dxs = new float[n]; var dys = new float[n]; var errs = new float[n];
            var list = new List<float>();
            for (int i = 0; i < n; i++)
            {
                if (i == k) continue;
                errs[i] = Estimate(gs[k], gs[i], w, h, out dxs[i], out dys[i]);
                list.Add(errs[i]);
            }
            list.Sort();
            float med = list.Count > 0 ? list[list.Count / 2] : 0f;
            float limit = Math.Max(2f * med, med + 4f);
            var keep = new bool[n];
            for (int i = 0; i < n; i++) { keep[i] = i == k || errs[i] <= limit; if (keep[i]) res.used++; }

            int W = w * scale, H = h * scale, C = gray ? 1 : 3;
            var acc = new float[C][];
            for (int c = 0; c < C; c++) acc[c] = new float[W * H];
            var wt = new float[W * H];
            for (int fi = 0; fi < n; fi++)
            {
                if (!keep[fi]) continue;
                var f = frames[fi];
                float dx = dxs[fi], dy = dys[fi];
                var g = gs[fi];
                for (int y = 0; y < h; y++)
                {
                    float Y = (y - dy + 0.5f) * scale - 0.5f;
                    int y0 = (int)Math.Floor(Y);
                    float fy = Y - y0;
                    for (int x = 0; x < w; x++)
                    {
                        float X = (x - dx + 0.5f) * scale - 0.5f;
                        int x0 = (int)Math.Floor(X);
                        float fx = X - x0;
                        int si = y * w + x;
                        for (int q = 0; q < 4; q++)
                        {
                            int xx = x0 + (q & 1), yy = y0 + (q >> 1);
                            if (xx < 0 || xx >= W || yy < 0 || yy >= H) continue;
                            float wgt = ((q & 1) != 0 ? fx : 1f - fx) * ((q >> 1) != 0 ? fy : 1f - fy);
                            int di = yy * W + xx;
                            wt[di] += wgt;
                            if (gray) acc[0][di] += wgt * g[si];
                            else { acc[0][di] += wgt * f[si].r; acc[1][di] += wgt * f[si].g; acc[2][di] += wgt * f[si].b; }
                        }
                    }
                }
            }
            // sortie (calculee sur place pour economiser la memoire) ; les trous (peu ou pas de pixels tombes la) reprennent l'image de reference
            var fk = frames[k]; var gk = gs[k];
            var plane = acc;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int di = y * W + x;
                    if (wt[di] > 0.25f)
                    {
                        float inv = 1f / wt[di];
                        for (int c = 0; c < C; c++) plane[c][di] *= inv;
                    }
                    else
                    {
                        int si = Math.Min(h - 1, y / scale) * w + Math.Min(w - 1, x / scale);
                        if (gray) plane[0][di] = gk[si];
                        else { plane[0][di] = fk[si].r; plane[1][di] = fk[si].g; plane[2][di] = fk[si].b; }
                    }
                }
            if (gray) Enhance(plane[0], W, H);
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++)
            {
                if (gray) { byte v = ToByte(plane[0][i]); px[i] = new Color32(v, v, v, 255); }
                else px[i] = new Color32(ToByte(plane[0][i]), ToByte(plane[1][i]), ToByte(plane[2][i]), 255);
            }
            res.pixels = px; res.w = W; res.h = H;
            return res;
        }

        static byte ToByte(float v) { return (byte)(v < 0f ? 0 : (v > 255f ? 255 : (int)(v + 0.5f))); }

        /// <summary>Etire le contraste (1 % - 99 %), relève les images sombres (gamma), puis renforce legerement les contours : ce que la lecture de texte aime.</summary>
        static void Enhance(float[] v, int w, int h)
        {
            var hist = new int[256];
            for (int i = 0; i < v.Length; i++) hist[(int)Math.Max(0f, Math.Min(255f, v[i]))]++;
            long total = v.Length, acc = 0;
            int lo = 0, hi = 255;
            for (int b = 0; b < 256; b++) { acc += hist[b]; if (acc >= total * 0.01) { lo = b; break; } }
            acc = 0;
            for (int b = 255; b >= 0; b--) { acc += hist[b]; if (acc >= total * 0.01) { hi = b; break; } }
            if (hi - lo < 16) { lo = Math.Max(0, lo - 8); hi = Math.Min(255, lo + 32); }
            float inv = 1f / (hi - lo);
            double sum = 0;
            for (int i = 0; i < v.Length; i++)
            {
                float t = (v[i] - lo) * inv;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                v[i] = t; sum += t;
            }
            float mean = (float)(sum / v.Length);
            float gamma = 1f;
            if (mean < 0.45f && mean > 0.02f) gamma = Math.Max(0.4f, (float)(Math.Log(0.5) / Math.Log(mean)));
            // flou 3x3 (separable) pour le renforcement
            var tmp = new float[v.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    tmp[i] = (v[x > 0 ? i - 1 : i] + v[i] + v[x < w - 1 ? i + 1 : i]) * (1f / 3f);
                }
            var blur = new float[v.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    blur[i] = (tmp[y > 0 ? i - w : i] + tmp[i] + tmp[y < h - 1 ? i + w : i]) * (1f / 3f);
                }
            for (int i = 0; i < v.Length; i++)
            {
                float a = gamma != 1f ? (float)Math.Pow(v[i], gamma) : v[i];
                float b = gamma != 1f ? (float)Math.Pow(blur[i], gamma) : blur[i];
                float o = a + 0.9f * (a - b);
                v[i] = (o < 0f ? 0f : (o > 1f ? 1f : o)) * 255f;
            }
        }
    }
}
