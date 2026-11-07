using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Lecteur de QR code, ecrit pour ce projet (rien a installer, aucune bibliotheque). Pur calcul, sans aucune API Unity : il tourne dans un autre fil
    /// (Task.Run) et ne coute donc rien tant qu'on ne scanne pas. Entree : une image en niveaux de gris (octets) ; sortie : le texte du QR ou null.
    /// Etapes : seuil local (image integrale) -> reperes 1:1:3:1:1 -> triplet de reperes -> transformation (homographie) -> echantillonnage de la grille ->
    /// format / version (BCH) -> masque -> blocs -> correction Reed-Solomon -> modes numerique, alphanumerique, octets (UTF-8) et ECI.
    /// Lit aussi les QR en miroir (image de camera inversee) et recale la transformation sur les motifs connus (reperes, pointilles, alignement), ce qui tient la perspective.
    /// Limites assumees : pas de mode Kanji, une perspective extreme (QR vu de tres biais) peut echouer ; QR de face, tourne, penche ou en miroir : bon.
    /// Essaie l'image normale puis inversee (QR clair sur fond sombre). Pas plus de ~2,5 s de calcul.
    /// </summary>
    public static class Ar6dofQr
    {
        // ------------------------------------------------------------------ API
        /// <summary>Pixels Unity (Texture2D.GetPixels32, origine en bas a gauche) -> texte du QR, ou null. A appeler dans un autre fil.</summary>
        public static string DecodePixels(Color32[] px, int w, int h)
        {
            if (px == null || w < 21 || h < 21 || px.Length < w * h) return null;
            int f = 1;
            int big = Mathf.Max(w, h);
            while (big / f > 1600) f++;
            int ow = w / f, oh = h / f;
            var lum = new byte[ow * oh];
            for (int y = 0; y < oh; y++)
            {
                int sy0 = (oh - 1 - y) * f;   // Unity range les lignes de bas en haut : on retourne l'image (sinon le QR serait en miroir)
                for (int x = 0; x < ow; x++)
                {
                    int sum = 0;
                    for (int dy = 0; dy < f; dy++)
                    {
                        int row = (sy0 + dy) * w + x * f;
                        for (int dx = 0; dx < f; dx++)
                        {
                            Color32 c = px[row + dx];
                            sum += (c.r * 77 + c.g * 150 + c.b * 29) >> 8;
                        }
                    }
                    lum[y * ow + x] = (byte)(sum / (f * f));
                }
            }
            return Decode(lum, ow, oh, 4000);
        }

        /// <summary>Niveaux de gris (octets, ligne 0 en haut) -> texte du QR, ou null. Plusieurs essais (seuils plus ou moins locaux, normal puis inverse, puis image reduite de moitie) jusqu'a 'maxMs' au total.</summary>
        public static string Decode(byte[] lum, int w, int h, int maxMs)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                string r = DecodeAt(lum, w, h, maxMs, sw);
                if (r != null) return r;
                if (w >= 640 && h >= 480 && sw.ElapsedMilliseconds < maxMs)
                {
                    // image reduite de moitie : le bruit se moyenne et un code flou ou petit redevient lisible
                    int w2 = w / 2, h2 = h / 2;
                    var small = new byte[w2 * h2];
                    for (int y = 0; y < h2; y++)
                    {
                        int r0 = 2 * y * w, r1 = r0 + w;
                        for (int x = 0; x < w2; x++)
                            small[y * w2 + x] = (byte)((lum[r0 + 2 * x] + lum[r0 + 2 * x + 1] + lum[r1 + 2 * x] + lum[r1 + 2 * x + 1]) >> 2);
                    }
                    r = DecodeAt(small, w2, h2, maxMs, sw);
                }
                return r;
            }
            catch (Exception) { return null; }
        }

        // taille de la fenetre du seuil local (fraction du petit cote : 1/8, 1/16...) et seuil (pourcentage de la moyenne locale), pour chaque essai
        static readonly int[] PassDiv = { 8, 16, 32, 8 };
        static readonly int[] PassThr = { 92, 94, 96, 98 };

        static string DecodeAt(byte[] lum, int w, int h, int maxMs, Stopwatch sw)
        {
            for (int p = 0; p < PassDiv.Length; p++)
            {
                if (sw.ElapsedMilliseconds > maxMs) return null;
                var d = new Decoder { w = w, h = h, limit = maxMs, sw = sw, lum = lum, inv = false };
                d.bw = Binarize(lum, w, h, PassDiv[p], PassThr[p]);
                string r = d.TryDecode();
                if (r != null) return r;
                var inv = new byte[d.bw.Length];
                for (int i = 0; i < inv.Length; i++) inv[i] = (byte)(1 - d.bw[i]);
                d.bw = inv;
                d.inv = true;
                r = d.TryDecode();
                if (r != null) return r;
            }
            return null;
        }

        // ------------------------------------------------------------------ tables
        static readonly int[][] EccPerBlock =
        {
            new[] {-1,7,10,15,20,26,18,20,24,30,18,20,24,26,30,22,24,28,30,28,28,28,28,30,30,26,28,30,30,30,30,30,30,30,30,30,30,30,30,30,30},
            new[] {-1,10,16,26,18,24,16,18,22,22,26,30,22,22,24,24,28,28,26,26,26,26,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28},
            new[] {-1,13,22,18,26,18,24,18,22,20,24,28,26,24,20,30,24,28,28,26,30,28,30,30,30,30,28,30,30,30,30,30,30,30,30,30,30,30,30,30,30},
            new[] {-1,17,28,22,16,22,28,26,26,24,28,24,28,22,24,24,30,28,28,26,28,30,24,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30,30},
        };
        static readonly int[][] NumBlocks =
        {
            new[] {-1,1,1,1,1,1,2,2,2,2,4,4,4,4,4,6,6,6,6,7,8,8,9,9,10,12,12,12,13,14,15,16,17,18,19,19,20,21,22,24,25},
            new[] {-1,1,1,1,2,2,4,4,4,5,5,5,8,9,9,10,10,11,13,14,16,17,17,18,20,21,23,25,26,28,29,31,33,35,37,38,40,43,45,47,49},
            new[] {-1,1,1,2,2,4,4,6,6,8,8,8,10,12,16,12,17,16,18,21,20,23,23,25,27,29,34,34,35,38,40,43,45,48,51,53,56,59,62,65,68},
            new[] {-1,1,1,2,4,4,4,5,6,8,8,11,11,16,16,18,16,19,21,25,25,25,34,30,32,35,37,40,42,45,48,51,54,57,60,63,66,70,74,77,81},
        };

        static int RawModules(int v)
        {
            int r = (16 * v + 128) * v + 64;
            if (v >= 2)
            {
                int n = v / 7 + 2;
                r -= (25 * n - 10) * n - 55;
                if (v >= 7) r -= 36;
            }
            return r;
        }

        static int[] AlignPositions(int v)
        {
            if (v == 1) return new int[0];
            int n = v / 7 + 2;
            int step = v == 32 ? 26 : (v * 4 + n * 2 + 1) / (n * 2 - 2) * 2;
            int size = v * 4 + 17;
            var res = new int[n];
            res[0] = 6;
            for (int i = 1; i < n; i++) res[i] = size - 7 - (n - 1 - i) * step;
            return res;
        }

        // ------------------------------------------------------------------ Galois (0x11D) et Reed-Solomon
        static readonly int[] Exp = new int[512];
        static readonly int[] Log = new int[256];
        static Ar6dofQr()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = x; Log[x] = i;
                x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        static int GMul(int a, int b) { return a == 0 || b == 0 ? 0 : Exp[Log[a] + Log[b]]; }
        static int GInv(int a) { return Exp[255 - Log[a]]; }

        /// <summary>'block' = donnees + correction. Corrige sur place ; vrai si le bloc est valide apres correction.</summary>
        static bool RsCorrect(int[] block, int nec)
        {
            int n = block.Length;
            var synd = new int[nec];
            bool anyErr = false;
            for (int j = 0; j < nec; j++)
            {
                int s = 0;
                for (int i = 0; i < n; i++) s = GMul(s, Exp[j]) ^ block[i];
                synd[j] = s;
                if (s != 0) anyErr = true;
            }
            if (!anyErr) return true;
            var C = new List<int> { 1 };
            var B = new List<int> { 1 };
            int L = 0, m = 1, b = 1;
            for (int nn = 0; nn < nec; nn++)
            {
                int d = synd[nn];
                for (int i = 1; i <= L; i++) if (i < C.Count) d ^= GMul(C[i], synd[nn - i]);
                if (d == 0) { m++; continue; }
                var T = new List<int>(C);
                int coef = GMul(d, GInv(b));
                while (C.Count < B.Count + m) C.Add(0);
                for (int i = 0; i < B.Count; i++) C[i + m] ^= GMul(coef, B[i]);
                if (2 * L <= nn) { L = nn + 1 - L; B = T; b = d; m = 1; }
                else m++;
            }
            if (L == 0 || L > nec / 2) return false;
            var errs = new List<int>();
            for (int deg = 0; deg < n; deg++)
            {
                int v = 0;
                for (int i = 0; i < C.Count; i++) if (C[i] != 0) v ^= GMul(C[i], Exp[((255 - deg) * i) % 255]);
                if (v == 0) errs.Add(deg);
            }
            if (errs.Count != L) return false;
            var omega = new int[nec];
            for (int i = 0; i < nec; i++)
                for (int j = 0; j < C.Count; j++)
                    if (i + j < nec) omega[i + j] ^= GMul(synd[i], C[j]);
            foreach (int deg in errs)
            {
                int num = 0;
                for (int k = 0; k < nec; k++) if (omega[k] != 0) num ^= GMul(omega[k], Exp[((255 - deg) * k) % 255]);
                int den = 0;
                for (int i = 1; i < C.Count; i += 2) if (C[i] != 0) den ^= GMul(C[i], Exp[((255 - deg) * (i - 1)) % 255]);
                if (den == 0) return false;
                int e = GMul(Exp[deg % 255], GMul(num, GInv(den)));
                int idx = n - 1 - deg;
                if (idx < 0) return false;
                block[idx] ^= e;
            }
            for (int j = 0; j < nec; j++)
            {
                int s = 0;
                for (int i = 0; i < n; i++) s = GMul(s, Exp[j]) ^ block[i];
                if (s != 0) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ binarisation : seuil local par image integrale (1 = noir)
        static byte[] Binarize(byte[] lum, int w, int h, int div, int thr)
        {
            int win = Math.Max(16, (Math.Min(w, h) / div) | 1);
            int half = win / 2;
            int iw = w + 1;
            var integ = new int[iw * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                int row = 0, bi = (y + 1) * iw, pi = y * iw, bl = y * w;
                for (int x = 0; x < w; x++)
                {
                    row += lum[bl + x];
                    integ[bi + x + 1] = integ[pi + x + 1] + row;
                }
            }
            var bw = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = Math.Max(0, y - half), y1 = Math.Min(h, y + half + 1);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Max(0, x - half), x1 = Math.Min(w, x + half + 1);
                    long s = integ[y1 * iw + x1] - integ[y0 * iw + x1] - integ[y1 * iw + x0] + integ[y0 * iw + x0];
                    long cnt = (long)(x1 - x0) * (y1 - y0);
                    if ((long)lum[y * w + x] * cnt * 100L < s * thr) bw[y * w + x] = 1;
                }
            }
            return bw;
        }

        // ------------------------------------------------------------------ le decodeur (un par image)
        class Cand { public float cx, cy, ms; public int count; }
        class Triple { public float score; public Cand tl, tr, bl; }

        class Decoder
        {
            public int w, h, limit;
            public byte[] bw, lum;
            public bool inv;     // vrai : bw est l'image inversee (QR clair sur fond sombre)
            public Stopwatch sw;

            bool TimeUp { get { return sw.ElapsedMilliseconds > limit; } }

            static bool RatioOk(int[] c)
            {
                int tot = c[0] + c[1] + c[2] + c[3] + c[4];
                if (tot < 7) return false;
                for (int i = 0; i < 5; i++) if (c[i] == 0) return false;
                float m = tot / 7f, tol = m * 0.6f;
                return Math.Abs(c[0] - m) < tol && Math.Abs(c[1] - m) < tol && Math.Abs(c[2] - 3 * m) < 3 * tol && Math.Abs(c[3] - m) < tol && Math.Abs(c[4] - m) < tol;
            }

            bool In(int x, int y) { return x >= 0 && x < w && y >= 0 && y < h; }

            /// <summary>Compte 5 segments autour d'un pixel noir dans la direction (dx,dy). Vrai si 1:1:3:1:1 ; 'off' = centre du segment noir central le long de l'axe.</summary>
            bool ScanLine(int x, int y, int dx, int dy, int[] cnt, out float off)
            {
                off = 0f;
                for (int i = 0; i < 5; i++) cnt[i] = 0;
                int px = x, py = y, cb = 0;
                while (In(px, py) && bw[py * w + px] == 1) { cb++; px -= dx; py -= dy; }
                if (!In(px, py)) return false;
                while (In(px, py) && bw[py * w + px] == 0) { cnt[1]++; px -= dx; py -= dy; }
                if (!In(px, py)) return false;
                while (In(px, py) && bw[py * w + px] == 1) { cnt[0]++; px -= dx; py -= dy; }
                px = x + dx; py = y + dy;
                int cf = 0;
                while (In(px, py) && bw[py * w + px] == 1) { cf++; px += dx; py += dy; }
                if (!In(px, py)) return false;
                while (In(px, py) && bw[py * w + px] == 0) { cnt[3]++; px += dx; py += dy; }
                if (!In(px, py)) return false;
                while (In(px, py) && bw[py * w + px] == 1) { cnt[4]++; px += dx; py += dy; }
                cnt[2] = cb + cf;
                if (!RatioOk(cnt)) return false;
                int a = -(cb - 1), b = cf;
                off = (a + b) / 2f;
                return true;
            }

            List<Cand> FindFinders()
            {
                var cands = new List<Cand>();
                int step = h < 700 ? 1 : 2;
                var cnt = new int[5];
                var vc = new int[5];
                var hc = new int[5];
                var runC = new List<int>();
                var runS = new List<int>();
                var runL = new List<int>();
                for (int y = 0; y < h; y += step)
                {
                    if ((y & 63) == 0 && TimeUp) break;
                    runC.Clear(); runS.Clear(); runL.Clear();
                    int x = 0, rb = y * w;
                    while (x < w)
                    {
                        byte c = bw[rb + x];
                        int s = x;
                        while (x < w && bw[rb + x] == c) x++;
                        runC.Add(c); runS.Add(s); runL.Add(x - s);
                    }
                    for (int i = 2; i < runC.Count - 2; i++)
                    {
                        if (runC[i] != 1) continue;
                        cnt[0] = runL[i - 2]; cnt[1] = runL[i - 1]; cnt[2] = runL[i]; cnt[3] = runL[i + 1]; cnt[4] = runL[i + 2];
                        if (!RatioOk(cnt)) continue;
                        float xc = runS[i] + runL[i] / 2f;
                        int ix = (int)xc;
                        if (ix >= w || bw[rb + ix] != 1) continue;
                        float vo;
                        if (!ScanLine(ix, y, 0, 1, vc, out vo)) continue;
                        float cy = y + vo + 0.5f;
                        int iy = (int)cy;
                        if (iy < 0 || iy >= h) continue;
                        float ho;
                        if (!ScanLine(ix, iy, 1, 0, hc, out ho)) continue;
                        float cx = ix + ho + 0.5f;
                        float ms = (cnt[0] + cnt[1] + cnt[2] + cnt[3] + cnt[4] + vc[0] + vc[1] + vc[2] + vc[3] + vc[4] + hc[0] + hc[1] + hc[2] + hc[3] + hc[4]) / 21f;
                        bool merged = false;
                        for (int k = 0; k < cands.Count; k++)
                        {
                            var c2 = cands[k];
                            if (Math.Abs(c2.cx - cx) < c2.ms * 1.5f && Math.Abs(c2.cy - cy) < c2.ms * 1.5f && Math.Abs(c2.ms - ms) < c2.ms * 0.5f)
                            {
                                int n = c2.count;
                                c2.cx = (c2.cx * n + cx) / (n + 1); c2.cy = (c2.cy * n + cy) / (n + 1); c2.ms = (c2.ms * n + ms) / (n + 1); c2.count = n + 1;
                                merged = true;
                                break;
                            }
                        }
                        if (!merged) cands.Add(new Cand { cx = cx, cy = cy, ms = ms, count = 1 });
                    }
                }
                var res = new List<Cand>();
                foreach (var c in cands) if (c.count >= 2) res.Add(c);
                return res;
            }

            static float Dist(Cand p, Cand q) { return (float)Math.Sqrt((p.cx - q.cx) * (p.cx - q.cx) + (p.cy - q.cy) * (p.cy - q.cy)); }

            static List<Triple> PickTriples(List<Cand> cands)
            {
                cands.Sort((u, z) => z.count.CompareTo(u.count));
                if (cands.Count > 12) cands.RemoveRange(12, cands.Count - 12);
                var outl = new List<Triple>();
                int n = cands.Count;
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        for (int k = j + 1; k < n; k++)
                        {
                            Cand a = cands[i], b = cands[j], c = cands[k];
                            float mx = Math.Max(a.ms, Math.Max(b.ms, c.ms)), mn = Math.Min(a.ms, Math.Min(b.ms, c.ms));
                            if (mx > mn * 2.2f) continue;
                            float dab = Dist(a, b), dac = Dist(a, c), dbc = Dist(b, c);
                            Cand tl, p, q;
                            if (dbc >= dac && dbc >= dab) { tl = a; p = b; q = c; }
                            else if (dac >= dab) { tl = b; p = a; q = c; }
                            else { tl = c; p = a; q = b; }
                            float s1 = Dist(tl, p), s2 = Dist(tl, q);
                            if (Math.Min(s1, s2) <= 0f || Math.Max(s1, s2) > Math.Min(s1, s2) * 1.8f) continue;
                            float cosang = ((p.cx - tl.cx) * (q.cx - tl.cx) + (p.cy - tl.cy) * (q.cy - tl.cy)) / (s1 * s2);
                            if (Math.Abs(cosang) > 0.6f) continue;
                            float avgms = (a.ms + b.ms + c.ms) / 3f;
                            if ((s1 + s2) / 2f < avgms * 7f) continue;
                            float cr = (p.cx - tl.cx) * (q.cy - tl.cy) - (p.cy - tl.cy) * (q.cx - tl.cx);
                            Cand tr = cr > 0 ? p : q, bl = cr > 0 ? q : p;
                            float score = Math.Min(a.count, Math.Min(b.count, c.count)) / (1f + Math.Abs(s1 - s2) / (s1 + s2));
                            outl.Add(new Triple { score = score, tl = tl, tr = tr, bl = bl });
                        }
                outl.Sort((u, z) => z.score.CompareTo(u.score));
                return outl;
            }

            // ---- transformation
            static double[] Solve8(double[,] A, double[] bv)
            {
                const int n = 8;
                var M = new double[n, n + 1];
                for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) M[i, j] = A[i, j]; M[i, n] = bv[i]; }
                for (int col = 0; col < n; col++)
                {
                    int piv = col;
                    for (int r = col + 1; r < n; r++) if (Math.Abs(M[r, col]) > Math.Abs(M[piv, col])) piv = r;
                    if (Math.Abs(M[piv, col]) < 1e-12) return null;
                    if (piv != col) for (int c = 0; c <= n; c++) { double t = M[col, c]; M[col, c] = M[piv, c]; M[piv, c] = t; }
                    for (int r = 0; r < n; r++)
                    {
                        if (r == col) continue;
                        double f = M[r, col] / M[col, col];
                        for (int c = col; c <= n; c++) M[r, c] -= f * M[col, c];
                    }
                }
                var res = new double[n];
                for (int i = 0; i < n; i++) res[i] = M[i, n] / M[i, i];
                return res;
            }

            static double[] Homography(double[] sx, double[] sy, double[] dx, double[] dy)
            {
                var A = new double[8, 8];
                var bv = new double[8];
                for (int i = 0; i < 4; i++)
                {
                    double x = sx[i], y = sy[i], u = dx[i], v = dy[i];
                    int r = 2 * i;
                    A[r, 0] = x; A[r, 1] = y; A[r, 2] = 1; A[r, 6] = -u * x; A[r, 7] = -u * y; bv[r] = u;
                    r++;
                    A[r, 3] = x; A[r, 4] = y; A[r, 5] = 1; A[r, 6] = -v * x; A[r, 7] = -v * y; bv[r] = v;
                }
                return Solve8(A, bv);
            }

            static void Apply(double[] H, double x, double y, out double px, out double py)
            {
                double d = H[6] * x + H[7] * y + 1.0;
                px = (H[0] * x + H[1] * y + H[2]) / d;
                py = (H[3] * x + H[4] * y + H[5]) / d;
            }

            byte[] SampleGrid(double[] H, int dim)
            {
                var g = new byte[dim * dim];
                double[] ox = { 0, -0.25, 0.25, 0, 0 };
                double[] oy = { 0, 0, 0, -0.25, 0.25 };
                for (int r = 0; r < dim; r++)
                    for (int c = 0; c < dim; c++)
                    {
                        int votes = 0;
                        for (int k = 0; k < 5; k++)
                        {
                            double px, py;
                            Apply(H, c + 0.5 + ox[k], r + 0.5 + oy[k], out px, out py);
                            int X = (int)Math.Floor(px), Y = (int)Math.Floor(py);
                            if (X >= 0 && X < w && Y >= 0 && Y < h && bw[Y * w + X] == 1) votes++;
                        }
                        g[r * dim + c] = (byte)(votes >= 3 ? 1 : 0);
                    }
                return g;
            }

            /// <summary>Modules dont la valeur est connue d'avance (reperes + separateurs, pointilles, motifs d'alignement) : triplets x, y, valeur (1 noir, 0 clair).</summary>
            static int[] FuncPoints(int dim)
            {
                int v = (dim - 17) / 4;
                var val = new sbyte[dim * dim];
                for (int i = 0; i < val.Length; i++) val[i] = -1;
                int[] fx = { 3, dim - 4, 3 }, fy = { 3, 3, dim - 4 };
                for (int k = 0; k < 3; k++)
                    for (int dy = -4; dy <= 4; dy++)
                        for (int dx = -4; dx <= 4; dx++)
                        {
                            int x = fx[k] + dx, y = fy[k] + dy;
                            if (x < 0 || y < 0 || x >= dim || y >= dim) continue;
                            int m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                            val[y * dim + x] = (sbyte)((m <= 1 || m == 3) ? 1 : 0);
                        }
                for (int i = 8; i < dim - 8; i++)
                {
                    sbyte t = (sbyte)(i % 2 == 0 ? 1 : 0);
                    val[6 * dim + i] = t; val[i * dim + 6] = t;
                }
                var ap = AlignPositions(v);
                for (int i = 0; i < ap.Length; i++)
                    for (int j = 0; j < ap.Length; j++)
                    {
                        if ((i == 0 && j == 0) || (i == 0 && j == ap.Length - 1) || (i == ap.Length - 1 && j == 0)) continue;
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                                val[(ap[i] + dy) * dim + ap[j] + dx] = (sbyte)(Math.Max(Math.Abs(dx), Math.Abs(dy)) == 1 ? 0 : 1);
                    }
                int n = 0;
                for (int i = 0; i < val.Length; i++) if (val[i] >= 0) n++;
                var res = new int[n * 3];
                int o = 0;
                for (int i = 0; i < val.Length; i++)
                    if (val[i] >= 0) { res[o++] = i % dim; res[o++] = i / dim; res[o++] = val[i]; }
                return res;
            }

            /// <summary>Accord entre l'image (niveaux de gris) et les modules connus, pour une transformation donnee. Plus c'est grand, mieux la grille colle.</summary>
            double Score(double[] H, int[] fp)
            {
                double s = 0;
                for (int i = 0; i < fp.Length; i += 3)
                {
                    double px, py;
                    Apply(H, fp[i] + 0.5, fp[i + 1] + 0.5, out px, out py);
                    int X = (int)px, Y = (int)py;
                    if (px < 1 || py < 1 || X >= w - 1 || Y >= h - 1) { s -= 1; continue; }
                    int l = lum[Y * w + X];
                    int dark = inv ? l : 255 - l;
                    s += (fp[i + 2] == 1 ? dark : 255 - dark) / 255.0;
                }
                return s;
            }

            /// <summary>Recale la transformation : on deplace un a un les 4 points de reference (3 reperes + 4e coin) par pas de plus en plus fins tant que l'accord avec les modules connus s'ameliore.
            /// C'est ce qui rattrape la perspective : le 4e coin deduit des reperes est faux quand le QR est vu de biais.</summary>
            double[] Refine(double[] bx, double[] by, int dim, int[] fp, out double best)
            {
                double[] sx = { 3.5, dim - 3.5, 3.5, dim - 3.5 };
                double[] sy = { 3.5, 3.5, dim - 3.5, dim - 3.5 };
                var dx = (double[])bx.Clone();
                var dy = (double[])by.Clone();
                best = double.NegativeInfinity;
                var H = Homography(sx, sy, dx, dy);
                if (H == null) return null;
                best = Score(H, fp);
                double step = 3.0;
                for (int it = 0; it < 60 && step >= 0.15; it++)
                {
                    bool improved = false;
                    for (int k = 0; k < 4; k++)
                        for (int ax = 0; ax < 2; ax++)
                            for (int sg = -1; sg <= 1; sg += 2)
                            {
                                double old = ax == 0 ? dx[k] : dy[k];
                                if (ax == 0) dx[k] = old + sg * step; else dy[k] = old + sg * step;
                                var Hn = Homography(sx, sy, dx, dy);
                                double sc = Hn == null ? double.NegativeInfinity : Score(Hn, fp);
                                if (sc > best + 1e-9) { best = sc; H = Hn; improved = true; }
                                else { if (ax == 0) dx[k] = old; else dy[k] = old; }
                            }
                    if (!improved) step *= 0.5;
                }
                return H;
            }

            /// <summary>Lit la grille telle quelle, puis transposee (QR en miroir).</summary>
            static string DecodeBoth(byte[] g, int dim)
            {
                string r = DecodeMatrix(g, dim);
                if (r != null) return r;
                var t = new byte[g.Length];
                for (int y = 0; y < dim; y++)
                    for (int x = 0; x < dim; x++) t[x * dim + y] = g[y * dim + x];
                return DecodeMatrix(t, dim);
            }

            public string TryDecode()
            {
                var cands = FindFinders();
                if (cands.Count < 3) return null;
                var triples = PickTriples(cands);
                int nt = Math.Min(6, triples.Count);
                for (int t = 0; t < nt; t++)
                {
                    if (TimeUp) return null;
                    Cand tl = triples[t].tl, tr = triples[t].tr, bl = triples[t].bl;
                    float ms = (tl.ms + tr.ms + bl.ms) / 3f;
                    float dtr = Dist(tl, tr), dbl = Dist(tl, bl);
                    float est = (dtr + dbl) / 2f / ms + 7f;
                    int bse = (int)Math.Round((est - 17f) / 4f) * 4 + 17;
                    double brx = tr.cx + bl.cx - tl.cx, bry = tr.cy + bl.cy - tl.cy;
                    double[] bx = { tl.cx, tr.cx, bl.cx, brx };
                    double[] by = { tl.cy, tr.cy, bl.cy, bry };

                    // 1) essai direct : le 4e coin est deduit des reperes (parallelogramme), bon pour un QR de face
                    int[] dims = { bse, bse - 4, bse + 4, bse - 8, bse + 8 };
                    foreach (int dim in dims)
                    {
                        if (dim < 21 || dim > 177) continue;
                        if (TimeUp) return null;
                        double[] sx = { 3.5, dim - 3.5, 3.5, dim - 3.5 };
                        double[] sy = { 3.5, 3.5, dim - 3.5, dim - 3.5 };
                        var H = Homography(sx, sy, bx, by);
                        if (H == null) continue;
                        string r = DecodeBoth(SampleGrid(H, dim), dim);
                        if (r != null) return r;
                    }

                    // 2) transformation recalee sur les modules connus (perspective), pour les 3 tailles les plus probables, la mieux collee d'abord
                    int[] rd = { bse, bse - 4, bse + 4 };
                    var hs = new double[3][];
                    var sc = new double[3];
                    var dd = new int[3];
                    int cnt = 0;
                    for (int q = 0; q < 3; q++)
                    {
                        int dim = rd[q];
                        if (dim < 21 || dim > 177) continue;
                        if (TimeUp) return null;
                        int[] fp = FuncPoints(dim);
                        double s;
                        var H = Refine(bx, by, dim, fp, out s);
                        if (H == null) continue;
                        hs[cnt] = H; sc[cnt] = s / (fp.Length / 3); dd[cnt] = dim; cnt++;
                    }
                    var used = new bool[3];
                    for (int n = 0; n < cnt; n++)
                    {
                        int bi = -1;
                        for (int q = 0; q < cnt; q++) if (!used[q] && (bi < 0 || sc[q] > sc[bi])) bi = q;
                        used[bi] = true;
                        if (TimeUp) return null;
                        string r = DecodeBoth(SampleGrid(hs[bi], dd[bi]), dd[bi]);
                        if (r != null) return r;
                    }
                }
                return null;
            }
        }

        // ------------------------------------------------------------------ lecture de la matrice
        static int PopCount(int x) { int n = 0; while (x != 0) { n += x & 1; x >>= 1; } return n; }

        static readonly int[] FmtEc = new int[32];     // declares AVANT FmtCode : les initialiseurs statiques s'executent dans l'ordre du fichier
        static readonly int[] FmtMask = new int[32];
        static readonly int[] FmtCode = BuildFormats();
        static int[] BuildFormats()
        {
            var res = new int[32];
            int i = 0;
            for (int ecbits = 0; ecbits < 4; ecbits++)
                for (int mask = 0; mask < 8; mask++)
                {
                    int data = ecbits << 3 | mask, rem = data;
                    for (int k = 0; k < 10; k++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
                    res[i] = (data << 10 | rem) ^ 0x5412;
                    FmtEc[i] = ecbits; FmtMask[i] = mask;
                    i++;
                }
            return res;
        }

        static bool ReadFormat(byte[] g, int size, out int ecbits, out int mask)
        {
            ecbits = 0; mask = 0;
            int b1 = 0;
            int[] x1 = new int[15], y1 = new int[15];
            for (int i = 0; i < 6; i++) { x1[i] = 8; y1[i] = i; }
            x1[6] = 8; y1[6] = 7; x1[7] = 8; y1[7] = 8; x1[8] = 7; y1[8] = 8;
            for (int i = 9; i < 15; i++) { x1[i] = 14 - i; y1[i] = 8; }
            for (int i = 0; i < 15; i++) b1 |= g[y1[i] * size + x1[i]] << i;
            int b2 = 0;
            for (int i = 0; i < 8; i++) b2 |= g[8 * size + (size - 1 - i)] << i;
            for (int i = 8; i < 15; i++) b2 |= g[(size - 15 + i) * size + 8] << i;
            int bd = 99, best = -1;
            for (int k = 0; k < 32; k++)
            {
                int d = Math.Min(PopCount(FmtCode[k] ^ b1), PopCount(FmtCode[k] ^ b2));
                if (d < bd) { bd = d; best = k; }
            }
            if (bd > 3 || best < 0) return false;
            ecbits = FmtEc[best]; mask = FmtMask[best];
            return true;
        }

        static bool[] FunctionMask(int v)
        {
            int size = v * 4 + 17;
            var fn = new bool[size * size];
            for (int i = 0; i < size; i++) { fn[6 * size + i] = true; fn[i * size + 6] = true; }
            int[] fx = { 3, size - 4, 3 }, fy = { 3, 3, size - 4 };
            for (int k = 0; k < 3; k++)
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = fx[k] + dx, y = fy[k] + dy;
                        if (x >= 0 && x < size && y >= 0 && y < size) fn[y * size + x] = true;
                    }
            var ap = AlignPositions(v);
            for (int i = 0; i < ap.Length; i++)
                for (int j = 0; j < ap.Length; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == ap.Length - 1) || (i == ap.Length - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++) fn[(ap[i] + dy) * size + ap[j] + dx] = true;
                }
            for (int i = 0; i < 9; i++) { fn[8 * size + i] = true; fn[i * size + 8] = true; }
            for (int i = 0; i < 8; i++) { fn[8 * size + size - 1 - i] = true; fn[(size - 1 - i) * size + 8] = true; }
            fn[(size - 8) * size + 8] = true;
            if (v >= 7)
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 3; j++) { fn[i * size + size - 11 + j] = true; fn[(size - 11 + j) * size + i] = true; }
            return fn;
        }

        static bool Inverted(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (x / 3 + y / 2) % 2 == 0;
                case 5: return (x * y) % 2 + (x * y) % 3 == 0;
                case 6: return ((x * y) % 2 + (x * y) % 3) % 2 == 0;
                default: return ((x + y) % 2 + (x * y) % 3) % 2 == 0;
            }
        }

        static string DecodeMatrix(byte[] g, int dim)
        {
            int v = (dim - 17) / 4;
            if (v < 1 || v > 40 || dim != v * 4 + 17) return null;
            if (v >= 7)
            {
                int b1 = 0, b2 = 0;
                for (int i = 0; i < 18; i++)
                {
                    int a = dim - 11 + i % 3, b = i / 3;
                    b1 |= g[b * dim + a] << i;
                    b2 |= g[a * dim + b] << i;
                }
                bool ok = false;
                for (int vv = 7; vv < 41; vv++)
                {
                    int rem = vv;
                    for (int k = 0; k < 12; k++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                    int code = vv << 12 | rem;
                    if (Math.Min(PopCount(code ^ b1), PopCount(code ^ b2)) <= 3)
                    {
                        if (vv != v) return null;
                        ok = true;
                        break;
                    }
                }
                if (!ok) return null;
            }
            int ecbits, mask;
            if (!ReadFormat(g, dim, out ecbits, out mask)) return null;
            int ecl = ecbits == 1 ? 0 : (ecbits == 0 ? 1 : (ecbits == 3 ? 2 : 3));
            var fn = FunctionMask(v);
            int total = RawModules(v) / 8;
            var bits = new List<int>(total * 8 + 8);
            for (int right0 = dim - 1; right0 > 0; right0 -= 2)
            {
                int right = right0;
                if (right <= 6) right -= 1;
                for (int vert = 0; vert < dim; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? dim - 1 - vert : vert;
                        if (fn[y * dim + x]) continue;
                        int bitv = g[y * dim + x];
                        if (Inverted(mask, x, y)) bitv ^= 1;
                        bits.Add(bitv);
                    }
            }
            if (bits.Count < total * 8) return null;
            var cw = new int[total];
            for (int i = 0; i < total; i++)
            {
                int val = 0;
                for (int k = 0; k < 8; k++) val = (val << 1) | bits[i * 8 + k];
                cw[i] = val;
            }
            int nb = NumBlocks[ecl][v], el = EccPerBlock[ecl][v];
            int nshort = nb - total % nb, slen = total / nb;
            var blocks = new List<int>[nb];
            for (int j = 0; j < nb; j++) blocks[j] = new List<int>();
            int kk = 0;
            for (int i = 0; i < slen - el + 1; i++)
                for (int j = 0; j < nb; j++)
                    if (i < slen - el + (j < nshort ? 0 : 1)) blocks[j].Add(cw[kk++]);
            for (int i = 0; i < el; i++)
                for (int j = 0; j < nb; j++) blocks[j].Add(cw[kk++]);
            var data = new List<int>();
            foreach (var blk in blocks)
            {
                var arr = blk.ToArray();
                if (!RsCorrect(arr, el)) return null;
                for (int i = 0; i < arr.Length - el; i++) data.Add(arr[i]);
            }
            return ParseData(data, v);
        }

        const string Alnum = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

        static string ParseData(List<int> data, int v)
        {
            int pos = 0, total = data.Count * 8;
            var o = new List<byte>();
            Func<int, int> read = n =>
            {
                int val = 0;
                for (int i = 0; i < n; i++)
                {
                    val = (val << 1) | ((data[pos >> 3] >> (7 - (pos & 7))) & 1);
                    pos++;
                }
                return val;
            };
            while (pos + 4 <= total)
            {
                int mode = read(4);
                if (mode == 0) break;
                int t = v <= 9 ? 0 : (v <= 26 ? 1 : 2);
                if (mode == 1)
                {
                    int n = read(t == 0 ? 10 : (t == 1 ? 12 : 14));
                    if (pos + (n / 3) * 10 + (n % 3 == 2 ? 7 : (n % 3 == 1 ? 4 : 0)) > total) return null;
                    while (n >= 3) { int x = read(10); if (x > 999) return null; o.AddRange(Encoding.ASCII.GetBytes(x.ToString("000"))); n -= 3; }
                    if (n == 2) { int x = read(7); if (x > 99) return null; o.AddRange(Encoding.ASCII.GetBytes(x.ToString("00"))); }
                    else if (n == 1) { int x = read(4); if (x > 9) return null; o.AddRange(Encoding.ASCII.GetBytes(x.ToString())); }
                }
                else if (mode == 2)
                {
                    int n = read(t == 0 ? 9 : (t == 1 ? 11 : 13));
                    if (pos + (n / 2) * 11 + (n % 2) * 6 > total) return null;
                    while (n >= 2)
                    {
                        int x = read(11);
                        if (x / 45 >= 45) return null;
                        o.Add((byte)Alnum[x / 45]); o.Add((byte)Alnum[x % 45]);
                        n -= 2;
                    }
                    if (n == 1) { int x = read(6); if (x >= 45) return null; o.Add((byte)Alnum[x]); }
                }
                else if (mode == 4)
                {
                    int n = read(t == 0 ? 8 : 16);
                    for (int i = 0; i < n; i++)
                    {
                        if (pos + 8 > total) return null;
                        o.Add((byte)read(8));
                    }
                }
                else if (mode == 7)
                {
                    if (pos + 8 > total) return null;
                    int first = read(8);
                    if ((first & 0x80) == 0) { }
                    else if ((first & 0xC0) == 0x80) read(8);
                    else read(16);
                }
                else return null;   // Kanji (8) et modes rares : non geres
            }
            var bytes = o.ToArray();
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (Exception)
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (byte b in bytes) sb.Append((char)b);   // Latin-1
                return sb.ToString();
            }
        }
    }
}
