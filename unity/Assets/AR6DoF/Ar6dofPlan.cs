using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Assemblage de plusieurs photos d'une meme affiche : decalage (translation) entre deux images par correlation croisee normalisee, du plus grossier (image reduite 16 fois)
    /// au plus fin (pixel), puis mosaique. Les images sont « tete en haut » (premiere ligne = haut), comme celles lues par l'OCR. Pas de Unity necessaire hors Color32.
    /// Limites : translation seule (pas de rotation ni de zoom entre deux images : garde l'affiche parallele a toi et a la meme distance).
    /// </summary>
    public static class Ar6dofAlign
    {
        public static Color32[] FlipV(Color32[] src, int w, int h)
        {
            var d = new Color32[src.Length];
            for (int y = 0; y < h; y++) Array.Copy(src, y * w, d, (h - 1 - y) * w, w);
            return d;
        }

        public static float[] Gray(Color32[] px)
        {
            var g = new float[px.Length];
            for (int i = 0; i < px.Length; i++) g[i] = px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f;
            return g;
        }

        // moyenne par blocs k x k ; resultat de taille (w/k) x (h/k)
        static float[] Down(float[] g, int w, int h, int k)
        {
            int w2 = w / k, h2 = h / k;
            var o = new float[w2 * h2];
            float inv = 1f / (k * k);
            for (int y = 0; y < h2; y++)
                for (int x = 0; x < w2; x++)
                {
                    float s = 0f;
                    for (int j = 0; j < k; j++)
                    {
                        int row = (y * k + j) * w + x * k;
                        for (int i = 0; i < k; i++) s += g[row + i];
                    }
                    o[y * w2 + x] = s * inv;
                }
            return o;
        }

        // correlation de b(x, y) avec a(x + dx, y + dy) sur la zone commune ; -2 si la zone est trop petite ou uniforme
        static float Ncc(float[] a, float[] b, int w, int h, int dx, int dy, int step, float minFrac)
        {
            int x0 = Math.Max(0, -dx), x1 = Math.Min(w, w - dx);
            int y0 = Math.Max(0, -dy), y1 = Math.Min(h, h - dy);
            if (x1 - x0 < 4 || y1 - y0 < 4) return -2f;
            if ((long)(x1 - x0) * (y1 - y0) < minFrac * w * h) return -2f;
            double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
            int n = 0;
            for (int y = y0; y < y1; y += step)
            {
                int ra = (y + dy) * w + dx, rb = y * w;
                for (int x = x0; x < x1; x += step)
                {
                    float va = a[ra + x], vb = b[rb + x];
                    sa += va; sb += vb; saa += va * va; sbb += vb * vb; sab += va * vb;
                    n++;
                }
            }
            if (n < 16) return -2f;
            double ma = sa / n, mb = sb / n;
            double va2 = saa / n - ma * ma, vb2 = sbb / n - mb * mb;
            if (va2 < 4.0 || vb2 < 4.0) return -2f;   // zone presque uniforme : rien a correler
            return (float)((sab / n - ma * mb) / Math.Sqrt(va2 * vb2));
        }

        /// <summary>Place l'image b dans le repere de a : le pixel (x, y) de b est le pixel (x + dx, y + dy) de a. Faux si aucune correspondance fiable.</summary>
        public static bool Shift(float[] a, float[] b, int w, int h, out int dx, out int dy, out float score)
        {
            return Shift(a, b, w, h, null, out dx, out dy, out score);
        }

        /// <summary>
        /// 'prior' (dx, dy attendus en pixels, deduits de l'orientation de la tete) : la recherche se limite a une fenetre autour (sans cela, une affiche tres reguliere -
        /// lignes, colonnes - donne de faux decalages d'une periode). Null : recherche complete.
        /// </summary>
        public static bool Shift(float[] a, float[] b, int w, int h, float[] prior, out int dx, out int dy, out float score)
        {
            dx = 0; dy = 0; score = 0f;
            int w4 = w / 4, h4 = h / 4, w16 = w4 / 4, h16 = h4 / 4;
            if (w16 < 24 || h16 < 18) return false;
            float[] a4 = Down(a, w, h, 4), b4 = Down(b, w, h, 4);
            float[] a16 = Down(a4, w4, h4, 4), b16 = Down(b4, w4, h4, 4);

            // 1) recherche complete sur l'image reduite 16 fois (recouvrement d'au moins 20 %)
            float best = -2f;
            int bx = 0, by = 0;
            int mx = (int)(w16 * 0.8f), my = (int)(h16 * 0.8f);
            int cx0 = 0, cy0 = 0, rx = mx, ry = my;
            bool hasPrior = prior != null && prior.Length >= 2;
            if (hasPrior)
            {
                cx0 = (int)Math.Round(prior[0] / 16f); cy0 = (int)Math.Round(prior[1] / 16f);
                rx = Math.Max(3, (int)(w16 * 0.3f)); ry = Math.Max(3, (int)(h16 * 0.3f));   // l'orientation ne dit pas tout (la tete se deplace aussi) : marge de 30 % de l'image
            }
            for (int sy = Math.Max(-my, cy0 - ry); sy <= Math.Min(my, cy0 + ry); sy++)
                for (int sx = Math.Max(-mx, cx0 - rx); sx <= Math.Min(mx, cx0 + rx); sx++)
                {
                    float c = Ncc(a16, b16, w16, h16, sx, sy, 1, 0.2f);
                    if (c <= -1f) continue;
                    float frac = (float)(w16 - Math.Abs(sx)) * (h16 - Math.Abs(sy)) / (w16 * h16);
                    c *= 0.85f + 0.15f * frac;   // a qualite egale on prefere le plus grand recouvrement
                    if (hasPrior)
                    {
                        float dd = (float)Math.Sqrt((double)((sx - cx0) * (sx - cx0) + (sy - cy0) * (sy - cy0))) / Math.Max(rx, ry);
                        c *= 1f - 0.08f * Math.Min(1f, dd);   // a qualite egale, le plus proche de ce que dit l'orientation
                    }
                    if (c > best) { best = c; bx = sx; by = sy; }
                }
            if (best < 0.2f) return false;

            // 2) affinage : image reduite 4 fois (+-5), 2 fois (+-3), pleine taille (+-3)
            int cx = bx * 4, cy = by * 4;
            Refine(a4, b4, w4, h4, ref cx, ref cy, 5, 2);
            float[] a2 = Down(a, w, h, 2), b2 = Down(b, w, h, 2);
            cx *= 2; cy *= 2;
            Refine(a2, b2, w / 2, h / 2, ref cx, ref cy, 3, 2);
            cx *= 2; cy *= 2;
            float sc = Refine(a, b, w, h, ref cx, ref cy, 3, 3);
            dx = cx; dy = cy; score = sc;
            return sc > 0f;
        }

        static float Refine(float[] a, float[] b, int w, int h, ref int cx, ref int cy, int range, int step)
        {
            float best = -2f;
            int bx = cx, by = cy;
            for (int j = -range; j <= range; j++)
                for (int i = -range; i <= range; i++)
                {
                    float c = Ncc(a, b, w, h, cx + i, cy + j, step, 0.05f);
                    if (c > best) { best = c; bx = cx + i; by = cy + j; }
                }
            cx = bx; cy = by;
            return best;
        }

        /// <summary>
        /// Place chaque image par rapport a la precedente retenue (chaine). 'used[i]' faux : image sans recouvrement fiable, ecartee. Decalages en pixels, relatifs a l'image 0.
        /// </summary>
        public static void Register(List<Color32[]> frames, int w, int h, out bool[] used, out int[] ox, out int[] oy)
        {
            Register(frames, w, h, null, out used, out ox, out oy);
        }

        /// <summary>'prior(a, b)' : decalage attendu (dx, dy) de l'image b par rapport a l'image a, ou null.</summary>
        public static void Register(List<Color32[]> frames, int w, int h, Func<int, int, float[]> prior, out bool[] used, out int[] ox, out int[] oy)
        {
            int n = frames.Count;
            used = new bool[n]; ox = new int[n]; oy = new int[n];
            if (n == 0) return;
            used[0] = true;
            int last = 0;
            float[] gRef = Gray(frames[0]);
            for (int i = 1; i < n; i++)
            {
                float[] g = Gray(frames[i]);
                int dx, dy;
                float sc;
                if (Shift(gRef, g, w, h, prior != null ? prior(last, i) : null, out dx, out dy, out sc) && sc >= 0.4f)
                {
                    used[i] = true;
                    ox[i] = ox[last] + dx;
                    oy[i] = oy[last] + dy;
                    last = i;
                    gRef = g;
                }
            }
        }
    }

    /// <summary>Image d'ensemble d'un planning : pixel du tableau = coordonnee en pleine resolution * s (premiere ligne = haut).</summary>
    public class PlanImage
    {
        public Color32[] px;
        public int w, h;
        public float s = 0.5f;
        public int fullW, fullH;
        public float[] frameX, frameY;   // origine de chaque image dans la mosaique (pleine resolution) ; -1 si ecartee
    }

    /// <summary>Un morceau de texte lu, en coordonnees de la mosaique (pleine resolution).</summary>
    public class PlanLine
    {
        public string text = "";
        public float x0, y0, x1, y1;
        public float h;
        public int frame;
        public float edge;
        public float cx { get { return (x0 + x1) * 0.5f; } }
        public float cy { get { return (y0 + y1) * 0.5f; } }
        public float w { get { return x1 - x0; } }
    }

    public class PlanEvent
    {
        public string title = "", room = "";
        public int startMin, endMin;      // minutes depuis minuit du premier jour ; au-dela de 1440 = apres minuit
        public bool endKnown;
        public bool hasBox;
        public float bx0, by0, bx1, by1;  // le bloc colore trouve (mosaique, pleine resolution)
        public float tx, ty;              // centre du texte
    }

    public class PlanResult
    {
        public List<PlanEvent> events = new List<PlanEvent>();
        public List<string> rooms = new List<string>();
        public string info = "";
        public int labels;
        public bool relative;   // heures illisibles : les heures sont comptees depuis la premiere ligne (0), l'utilisateur donne l'heure de depart
    }

    /// <summary>
    /// Lecture d'un planning de convention (grille : heures a gauche, salles en colonnes, blocs colores avec un titre) a partir du texte lu par l'OCR AVEC sa position.
    /// 1) les heures (« 10:00 ») forment une colonne : leur alignement donne l'inclinaison de l'affiche et l'echelle du temps ;
    /// 2) les titres de colonnes (salles) sont les textes au-dessus de la premiere heure ;
    /// 3) chaque titre d'evenement (lignes voisines regroupees) est cherche dans l'image : le bloc de couleur qui l'entoure donne son debut et sa fin ;
    /// 4) a defaut de bloc, on suppose une heure autour du texte. Estimation : l'utilisateur relit avant d'enregistrer. Pas de Unity necessaire, du texte et des pixels.
    /// </summary>
    public static class Ar6dofPlan
    {
        // « 20:00 », « 20h00 », « 20.00 », « 20 00 » ; sans separateur (« 2000 ») seulement sur un quart d'heure
        static readonly Regex TimeRx = new Regex(@"^(\d{1,2})\s*([:hH.;,]?)\s*(\d{2})$", RegexOptions.CultureInvariant);

        // ------------------------------------------------------------------ texte lu -> lignes
        public static List<PlanLine> BuildLines(List<Ar6dofOcr.Page> pages, float[] fx, float[] fy)
        {
            var all = new List<PlanLine>();
            for (int p = 0; p < pages.Count; p++)
            {
                if (fx[p] < 0f) continue;   // image ecartee
                var pg = pages[p];
                float pw = Math.Max(1, pg.w), ph = Math.Max(1, pg.h);
                foreach (var ln in pg.lines)
                {
                    if (ln.words.Count == 0) continue;
                    float avg = 0f;
                    foreach (var w in ln.words) avg += w.h;
                    avg /= ln.words.Count;
                    // l'OCR peut relier deux colonnes voisines en une seule ligne : on coupe aux grands ecarts
                    int start = 0;
                    for (int i = 1; i <= ln.words.Count; i++)
                    {
                        bool cut = i == ln.words.Count;
                        if (!cut)
                        {
                            var a = ln.words[i - 1]; var b = ln.words[i];
                            int ta, tb;
                            cut = b.x - (a.x + a.w) > 1.4f * avg || TryTime(a.text, out ta) != TryTime(b.text, out tb);   // une heure forme toujours sa propre ligne
                        }
                        if (!cut) continue;
                        var seg = new PlanLine { frame = p };
                        var sb = new StringBuilder();
                        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, hs = 0f;
                        for (int k = start; k < i; k++)
                        {
                            var w = ln.words[k];
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(w.text);
                            x0 = Math.Min(x0, w.x); y0 = Math.Min(y0, w.y); x1 = Math.Max(x1, w.x + w.w); y1 = Math.Max(y1, w.y + w.h);
                            hs += w.h;
                        }
                        seg.text = sb.ToString().Trim();
                        seg.x0 = x0 + fx[p]; seg.x1 = x1 + fx[p]; seg.y0 = y0 + fy[p]; seg.y1 = y1 + fy[p];
                        seg.h = hs / (i - start);
                        float cxl = (x0 + x1) * 0.5f, cyl = (y0 + y1) * 0.5f;
                        seg.edge = Math.Min(Math.Min(cxl, pw - cxl), Math.Min(cyl, ph - cyl)) / Math.Min(pw, ph);
                        if (seg.text.Length > 0 && seg.h > 3f) all.Add(seg);
                        start = i;
                    }
                }
            }
            // une meme ligne vue sur deux images : on garde celle qui est le plus au centre de son image (les lignes coupees par le bord sont tronquees)
            all.Sort((a, b) => b.edge.CompareTo(a.edge));
            var kept = new List<PlanLine>();
            foreach (var l in all)
            {
                bool dup = false;
                foreach (var k in kept) if (Dup(l, k)) { dup = true; break; }
                if (!dup) kept.Add(l);
            }
            return kept;
        }

        static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static bool Dup(PlanLine a, PlanLine b)
        {
            if (a.frame == b.frame) return false;
            float ix = Math.Min(a.x1, b.x1) - Math.Max(a.x0, b.x0), iy = Math.Min(a.y1, b.y1) - Math.Max(a.y0, b.y0);
            if (ix > 0f && iy > 0f)
            {
                float amin = Math.Min(a.w * (a.y1 - a.y0), b.w * (b.y1 - b.y0));
                if (ix * iy > 0.4f * amin) return true;
            }
            if (Norm(a.text) == Norm(b.text))
            {
                float hh = Math.Max(a.h, b.h);
                if (Math.Abs(a.cx - b.cx) < 2.5f * hh + 0.3f * Math.Max(a.w, b.w) && Math.Abs(a.cy - b.cy) < 2.5f * hh) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ utilitaires
        static bool TryTime(string text, out int min)
        {
            min = 0;
            if (text == null) return false;
            // l'OCR colle souvent les pointilles de la grille au chiffre (« 20:00 ---- ») : on retire la ponctuation autour
            string t = text.Trim().Trim('-', '.', '_', '~', '=', '|', '\u00b7', '\u2014', '\u2013', '*', ' ', ',', ';');
            if (t.Length < 3 || t.Length > 9) return false;
            var sb = new StringBuilder();
            foreach (char c in t)
            {
                char d = c;
                if (c == 'O' || c == 'o' || c == 'D' || c == 'Q') d = '0';
                else if (c == 'l' || c == 'I' || c == '|') d = '1';
                else if (c == 'S') d = '5';
                else if (c == 'B') d = '8';
                sb.Append(d);
            }
            var m = TimeRx.Match(sb.ToString());
            if (!m.Success) return false;
            int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[3].Value);
            if (h > 24 || mi > 59 || (h == 24 && mi > 0)) return false;
            if (m.Groups[2].Value.Length == 0 && (mi % 15 != 0 || sb.Length < 4)) return false;   // sans separateur : « 2000 » oui, « 100 » ou « 1234 » non
            min = h * 60 + mi;
            return true;
        }

        static int Letters(string s)
        {
            int n = 0;
            foreach (char c in s) if (char.IsLetter(c)) n++;
            return n;
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return 0f;
            var c = new List<float>(v);
            c.Sort();
            return c[c.Count / 2];
        }

        // droite x = a + s * y par moindres carres
        static void FitLine(List<float> ys, List<float> xs, out float a, out float s)
        {
            int n = ys.Count;
            double my = 0, mx = 0;
            for (int i = 0; i < n; i++) { my += ys[i]; mx += xs[i]; }
            my /= n; mx /= n;
            double sxy = 0, syy = 0;
            for (int i = 0; i < n; i++) { sxy += (ys[i] - my) * (xs[i] - mx); syy += (ys[i] - my) * (ys[i] - my); }
            s = syy > 1e-6 ? (float)(sxy / syy) : 0f;
            a = (float)(mx - s * my);
        }

        // reperes de l'affiche : u le long des lignes, t le long de la colonne des heures (le temps)
        class Axes
        {
            public float s, mh, det;
            public void To(float x, float y, out float u, out float t)
            {
                u = (x - s * y) / det;
                t = y - mh * u;
            }
        }

        class Group
        {
            public List<PlanLine> lines = new List<PlanLine>();
            public float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            public float u, t, h;
            public string text = "";
        }

        static int Find(int[] p, int i) { while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; } return i; }

        // regroupe les lignes voisines et centrees (un titre sur plusieurs lignes)
        static List<Group> MakeGroups(List<PlanLine> src, Axes ax)
        {
            int n = src.Count;
            var u = new float[n]; var t = new float[n];
            for (int i = 0; i < n; i++) ax.To(src[i].cx, src[i].cy, out u[i], out t[i]);
            var par = new int[n];
            for (int i = 0; i < n; i++) par[i] = i;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float hi = src[i].h, hj = src[j].h;
                    float hm = Math.Max(hi, hj);
                    if (Math.Min(hi, hj) < 0.55f * hm) continue;
                    if (Math.Abs(u[i] - u[j]) > 0.55f * Math.Max(src[i].w, src[j].w) + 0.3f * hm) continue;
                    float topI = t[i] - hi * 0.5f, botI = t[i] + hi * 0.5f, topJ = t[j] - hj * 0.5f, botJ = t[j] + hj * 0.5f;
                    float gap = Math.Max(topI, topJ) - Math.Min(botI, botJ);
                    if (gap > 0.9f * hm) continue;
                    int a = Find(par, i), b = Find(par, j);
                    if (a != b) par[a] = b;
                }
            var map = new Dictionary<int, Group>();
            var res = new List<Group>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(par, i);
                Group g;
                if (!map.TryGetValue(r, out g)) { g = new Group(); map[r] = g; res.Add(g); }
                g.lines.Add(src[i]);
            }
            foreach (var g in res)
            {
                g.lines.Sort((a, b) => a.cy.CompareTo(b.cy));
                var sb = new StringBuilder();
                float hs = 0f;
                foreach (var l in g.lines)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(l.text);
                    g.x0 = Math.Min(g.x0, l.x0); g.y0 = Math.Min(g.y0, l.y0); g.x1 = Math.Max(g.x1, l.x1); g.y1 = Math.Max(g.y1, l.y1);
                    hs += l.h;
                }
                g.h = hs / g.lines.Count;
                g.text = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                ax.To((g.x0 + g.x1) * 0.5f, (g.y0 + g.y1) * 0.5f, out g.u, out g.t);
            }
            return res;
        }

        // ------------------------------------------------------------------ analyse
        public static PlanResult Parse(List<PlanLine> all, PlanImage img)
        {
            // 1) les heures lues (« 20:00 »)
            var tl = new List<PlanLine>();
            var tm = new List<int>();
            foreach (var l in all) { int m; if (TryTime(l.text, out m)) { tl.Add(l); tm.Add(m); } }
            var first = ParseWith(all, img, tl, tm);
            if (first.events.Count > 0 && first.labels >= 2) return first;
            // 2) heures illisibles (texte trop petit, flou) : on cherche une colonne de petits textes regulierement espaces ; l'utilisateur donne l'heure de la premiere
            List<PlanLine> gl; List<int> gm;
            if (GuessLabels(all, out gl, out gm))
            {
                var second = ParseWith(all, img, gl, gm);
                if (second.events.Count > 0) { second.relative = true; return second; }
            }
            return first;
        }

        // colonne de petits textes (chiffres mal lus) a intervalles reguliers : numerotes 0, 60, 120... minutes ; faux si moins de 4
        static bool GuessLabels(List<PlanLine> all, out List<PlanLine> gl, out List<int> gm)
        {
            gl = new List<PlanLine>(); gm = new List<int>();
            var cand = new List<PlanLine>();
            foreach (var l in all)
            {
                string t = l.text.Trim();
                if (t.Length < 3 || t.Length > 9) continue;
                int dig = 0;
                foreach (char c in t) if (char.IsDigit(c) || c == 'O' || c == 'o' || c == 'l' || c == 'I' || c == ':' || c == '.' || c == 'h' || c == 'H') dig++;
                if (dig * 10 >= t.Length * 7 && Letters(t) <= 3) cand.Add(l);
            }
            if (cand.Count < 4) return false;
            var hs = new List<float>();
            foreach (var l in cand) hs.Add(l.h);
            float medH = Median(hs);
            // la colonne qui contient le plus de candidats (x a peu pres egal)
            float tol = Math.Max(10f, 1.2f * medH + 0.2f * Median(WidthsOf(cand)));
            List<PlanLine> best = null;
            foreach (var c0 in cand)
            {
                var col = new List<PlanLine>();
                foreach (var c1 in cand) if (Math.Abs(c1.cx - c0.cx) <= tol) col.Add(c1);
                if (best == null || col.Count > best.Count) best = col;
            }
            if (best == null || best.Count < 4) return false;
            best.Sort((a, b) => a.cy.CompareTo(b.cy));
            var dys = new List<float>();
            for (int i = 0; i + 1 < best.Count; i++) dys.Add(best[i + 1].cy - best[i].cy);
            float step = Median(dys);
            if (step < 2f * medH) return false;
            // une heure = un pas ; une ligne manquante donne un pas double : l'indice se deduit de la position
            int lastIdx = -1;
            foreach (var l in best)
            {
                int idx = (int)Math.Round((l.cy - best[0].cy) / step);
                if (idx <= lastIdx) continue;
                lastIdx = idx;
                gl.Add(l); gm.Add(idx * 60);
            }
            return gl.Count >= 4;
        }

        static List<float> WidthsOf(List<PlanLine> v)
        {
            var r = new List<float>();
            foreach (var l in v) r.Add(l.w);
            return r;
        }

        static PlanResult ParseWith(List<PlanLine> all, PlanImage img, List<PlanLine> tl, List<int> tm)
        {
            var res = new PlanResult();
            if (tl.Count < 2) { res.info = "Pas assez d'heures lues (il en faut au moins deux sur la colonne des horaires)"; return res; }
            var hs = new List<float>(); var ws = new List<float>();
            foreach (var l in tl) { hs.Add(l.h); ws.Add(l.w); }
            float medH = Median(hs), medW = Median(ws);

            // colonne des heures : la droite x = a + s * y qui en aligne le plus
            int n = tl.Count, bestCnt = 0;
            float bs = 0f, ba = 0f;
            float tolX = Math.Max(10f, 0.9f * medH + 0.2f * medW);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float dy = tl[j].cy - tl[i].cy;
                    if (Math.Abs(dy) < 2f * medH) continue;
                    float s0 = (tl[j].cx - tl[i].cx) / dy;
                    if (Math.Abs(s0) > 0.4f) continue;
                    float a0 = tl[i].cx - s0 * tl[i].cy;
                    int cnt = 0;
                    for (int k = 0; k < n; k++) if (Math.Abs(tl[k].cx - (a0 + s0 * tl[k].cy)) <= tolX) cnt++;
                    if (cnt > bestCnt) { bestCnt = cnt; bs = s0; ba = a0; }
                }
            if (bestCnt < 2) { res.info = "Les heures lues ne sont pas alignees"; return res; }
            var colIdx = new List<int>();
            for (int k = 0; k < n; k++) if (Math.Abs(tl[k].cx - (ba + bs * tl[k].cy)) <= tolX) colIdx.Add(k);
            {
                var ys = new List<float>(); var xs = new List<float>();
                foreach (int k in colIdx) { ys.Add(tl[k].cy); xs.Add(tl[k].cx); }
                float a1, s1;
                FitLine(ys, xs, out a1, out s1);
                if (Math.Abs(s1) <= 0.4f) { ba = a1; bs = s1; }
            }
            float yFirst = float.MaxValue;
            foreach (int k in colIdx) yFirst = Math.Min(yFirst, tl[k].cy);
            var dys = new List<float>();
            {
                var sorted = new List<float>();
                foreach (int k in colIdx) sorted.Add(tl[k].cy);
                sorted.Sort();
                for (int i = 0; i + 1 < sorted.Count; i++) dys.Add(sorted[i + 1] - sorted[i]);
            }
            float spY = Math.Max(medH * 2f, Median(dys));

            // 2) inclinaison des lignes : les titres de salles, au-dessus de la premiere heure
            var isTime = new HashSet<PlanLine>(tl);
            var hc = new List<PlanLine>();
            foreach (var l in all)
            {
                if (isTime.Contains(l) || Letters(l.text) < 3) continue;
                float colX = ba + bs * l.cy;
                if (l.cy < yFirst + 0.45f * spY + 0.12f * Math.Max(0f, l.cx - colX) && l.cy > yFirst - 3f * spY && l.cx > colX + 0.6f * medW) hc.Add(l);
            }
            float mh = -bs;   // a defaut : la page est seulement tournee (lignes perpendiculaires a la colonne des heures)
            if (hc.Count >= 2)
            {
                int bestH = 1;
                float bm = 0f;
                float tolY = 1.6f * medH + 4f;
                for (int i = 0; i < hc.Count; i++)
                    for (int j = i + 1; j < hc.Count; j++)
                    {
                        float dx = hc[j].cx - hc[i].cx;
                        if (Math.Abs(dx) < 6f * medH) continue;
                        float m0 = (hc[j].cy - hc[i].cy) / dx;
                        if (Math.Abs(m0) > 0.2f) continue;
                        float c0 = hc[i].cy - m0 * hc[i].cx;
                        int cnt = 0;
                        foreach (var l in hc) if (Math.Abs(l.cy - (c0 + m0 * l.cx)) <= tolY) cnt++;
                        if (cnt > bestH) { bestH = cnt; bm = m0; }
                    }
                if (bestH >= 2) mh = bm;
            }
            var ax = new Axes { s = bs, mh = mh, det = 1f - bs * mh };
            if (Math.Abs(ax.det) < 0.5f) { ax.mh = -bs; ax.det = 1f + bs * bs; }

            // 3) echelle du temps : minutes par unite de t, tolerante aux heures mal lues
            var lt = new List<float>(); var lm = new List<int>(); var lu = new List<float>();
            foreach (int k in colIdx)
            {
                float u, t;
                ax.To(tl[k].cx, tl[k].cy, out u, out t);
                lt.Add(t); lm.Add(tm[k]); lu.Add(u);
            }
            {
                // tri par t
                var order = new List<int>();
                for (int i = 0; i < lt.Count; i++) order.Add(i);
                order.Sort((p, q) => lt[p].CompareTo(lt[q]));
                var t2 = new List<float>(); var m2 = new List<int>(); var u2 = new List<float>();
                foreach (int i in order) { t2.Add(lt[i]); m2.Add(lm[i]); u2.Add(lu[i]); }
                lt = t2; lm = m2; lu = u2;
            }
            var slopes = new List<float>();
            for (int i = 0; i + 1 < lt.Count; i++)
            {
                float dt = lt[i + 1] - lt[i];
                if (dt < 1e-3f) continue;
                int dm = (((lm[i + 1] - lm[i]) % 1440) + 1440) % 1440;
                if (dm == 0 || dm > 720) continue;
                slopes.Add(dm / dt);
            }
            if (slopes.Count == 0) { res.info = "Les heures lues sont incoherentes"; return res; }
            float slope = Median(slopes);
            // phase : minutes - pente * t, modulo 24 h, la plus frequente
            var phis = new List<double>();
            for (int i = 0; i < lt.Count; i++)
            {
                double ph = (lm[i] - slope * lt[i]) % 1440.0;
                if (ph < 0) ph += 1440.0;
                phis.Add(ph);
            }
            int bestC = 0; double phiC = phis[0];
            for (int i = 0; i < phis.Count; i++)
            {
                int cnt = 0;
                for (int j = 0; j < phis.Count; j++)
                {
                    double d = Math.Abs(phis[i] - phis[j]);
                    if (d > 720.0) d = 1440.0 - d;
                    if (d <= 25.0) cnt++;
                }
                if (cnt > bestC) { bestC = cnt; phiC = phis[i]; }
            }
            var tt = new List<float>(); var um = new List<float>(); var uu = new List<float>();
            for (int i = 0; i < lt.Count; i++)
            {
                double d = Math.Abs(phis[i] - phiC);
                if (d > 720.0) d = 1440.0 - d;
                if (d > 25.0) continue;
                double pred = slope * lt[i] + phiC;
                double v = lm[i] + 1440.0 * Math.Round((pred - lm[i]) / 1440.0);
                tt.Add(lt[i]); um.Add((float)v); uu.Add(lu[i]);
            }
            if (tt.Count < 2) { res.info = "Les heures lues sont incoherentes"; return res; }
            float shiftDay = 1440f * (float)Math.Floor(um[0] / 1440f);
            for (int i = 0; i < um.Count; i++) um[i] -= shiftDay;
            res.labels = tt.Count;
            float tFirst = tt[0], tLast = tt[tt.Count - 1];
            float sp = Math.Max(1f, (tLast - tFirst) / (tt.Count - 1));
            float labelU = Median(uu);
            Func<float, float> timeAt = t =>
            {
                if (t <= tt[0]) return um[0] + slope * (t - tt[0]);
                if (t >= tt[tt.Count - 1]) return um[um.Count - 1] + slope * (t - tt[tt.Count - 1]);
                for (int i = 0; i + 1 < tt.Count; i++)
                    if (t <= tt[i + 1])
                    {
                        float f = (t - tt[i]) / Math.Max(1e-3f, tt[i + 1] - tt[i]);
                        return um[i] + (um[i + 1] - um[i]) * f;
                    }
                return um[um.Count - 1];
            };

            // 4) salles et evenements
            var heads = new List<PlanLine>();
            var evl = new List<PlanLine>();
            foreach (var l in all)
            {
                if (isTime.Contains(l)) continue;
                string nt = Norm(l.text);
                if (nt.Length < 2) continue;
                float u, t;
                ax.To(l.cx, l.cy, out u, out t);
                if (u < labelU + 0.6f * medW) continue;                  // la colonne des heures elle-meme
                if (t < tFirst - 3f * sp || t > tLast + 0.9f * sp) continue;   // en dehors de la grille (en-tete de page, pied de page)
                if (t < tFirst + 0.3f * sp) { if (Letters(l.text) >= 3) heads.Add(l); }
                else evl.Add(l);
            }
            var hg = MakeGroups(heads, ax);
            var rooms = new List<Group>();
            foreach (var g in hg) if (Letters(g.text) >= 3) rooms.Add(g);
            rooms.Sort((a, b) => a.u.CompareTo(b.u));
            foreach (var r in rooms) res.rooms.Add(r.text);

            var eg = MakeGroups(evl, ax);
            foreach (var g in eg)
            {
                if (Letters(g.text) < 3 && Norm(g.text).Length < 4) continue;
                var ev = new PlanEvent { title = g.text, tx = (g.x0 + g.x1) * 0.5f, ty = (g.y0 + g.y1) * 0.5f };
                float bx0, by0, bx1, by1;
                bool found = FindBlock(img, g.x0, g.y0, g.x1, g.y1, g.h, out bx0, out by0, out bx1, out by1);
                float uc = g.u;
                if (found)
                {
                    float u0, t0, u1, t1, um0, tm0;
                    ax.To(ev.tx, by0, out u0, out t0);
                    ax.To(ev.tx, by1, out u1, out t1);
                    int s = Snap(timeAt(t0)), e = Snap(timeAt(t1));
                    if (e - s >= 15 && e - s <= 12 * 60)
                    {
                        ev.startMin = s; ev.endMin = e; ev.endKnown = true; ev.hasBox = true;
                        ev.bx0 = bx0; ev.by0 = by0; ev.bx1 = bx1; ev.by1 = by1;
                        ax.To((bx0 + bx1) * 0.5f, (by0 + by1) * 0.5f, out um0, out tm0);
                        uc = um0;
                    }
                    else found = false;
                }
                if (!found)
                {
                    float tc = timeAt(g.t);
                    ev.startMin = Snap(tc - 30f); ev.endMin = ev.startMin + 60; ev.endKnown = false;
                }
                if (rooms.Count > 0)
                {
                    float bd = float.MaxValue;
                    foreach (var r in rooms) { float d = Math.Abs(r.u - uc); if (d < bd) { bd = d; ev.room = r.text; } }
                }
                // meme evenement lu deux fois
                bool dup = false;
                foreach (var o in res.events) if (o.startMin == ev.startMin && o.room == ev.room && Norm(o.title) == Norm(ev.title)) { dup = true; break; }
                if (!dup) res.events.Add(ev);
            }
            res.events.Sort((a, b) => a.startMin != b.startMin ? a.startMin.CompareTo(b.startMin) : string.CompareOrdinal(a.room, b.room));
            if (res.events.Count == 0) res.info = "Aucun evenement trouve dans la grille";
            return res;
        }

        // arrondi au quart d'heure
        static int Snap(float minutes) { return (int)Math.Round(minutes / 15.0) * 15; }

        // ------------------------------------------------------------------ bloc colore autour d'un texte
        // La couleur du fond est lue juste autour du texte ; on monte, descend, va a gauche et a droite tant que la couleur ne change pas.
        // Coordonnees d'entree et de sortie : mosaique en pleine resolution.
        static bool FindBlock(PlanImage im, float fx0, float fy0, float fx1, float fy1, float th, out float bx0, out float by0, out float bx1, out float by1)
        {
            bx0 = by0 = bx1 = by1 = 0f;
            if (im == null || im.px == null) return false;
            float s = im.s;
            int x0 = (int)(fx0 * s), y0 = (int)(fy0 * s), x1 = (int)(fx1 * s), y1 = (int)(fy1 * s);
            int W = im.w, H = im.h;
            if (x0 < 0 || y0 < 0 || x1 >= W || y1 >= H || x1 - x0 < 2 || y1 - y0 < 2) return false;
            int pad = Math.Max(4, (int)(th * s * 0.6f));
            // couleur du fond : mediane de l'anneau autour du texte
            var rr = new List<int>(); var gg = new List<int>(); var bb = new List<int>();
            int ya = Math.Max(0, y0 - pad), yb = Math.Min(H - 1, y1 + pad), xa = Math.Max(0, x0 - pad), xb = Math.Min(W - 1, x1 + pad);
            for (int y = ya; y <= yb; y++)
                for (int x = xa; x <= xb; x++)
                {
                    if (x >= x0 && x <= x1 && y >= y0 && y <= y1) continue;
                    var c = im.px[y * W + x];
                    rr.Add(c.r); gg.Add(c.g); bb.Add(c.b);
                }
            if (rr.Count < 8) return false;
            rr.Sort(); gg.Sort(); bb.Sort();
            int sr = rr[rr.Count / 2], sg = gg[gg.Count / 2], sbl = bb[bb.Count / 2];
            const int Tol = 80;
            const float Frac = 0.55f;
            int rx0 = xa, rx1 = xb;   // etendue horizontale testee pour monter / descendre
            int maxV = (int)(H * 0.7f), maxH = (int)(W * 0.7f);

            int top = y0;
            for (int k = 0, miss = 0; k < maxV; k++)
            {
                int y = y0 - 1 - k;
                if (y < 0) break;
                if (RowIn(im, y, rx0, rx1, sr, sg, sbl, Tol, Frac)) { top = y; miss = 0; }
                else if (++miss >= 2) break;
            }
            int bot = y1;
            for (int k = 0, miss = 0; k < maxV; k++)
            {
                int y = y1 + 1 + k;
                if (y >= H) break;
                if (RowIn(im, y, rx0, rx1, sr, sg, sbl, Tol, Frac)) { bot = y; miss = 0; }
                else if (++miss >= 2) break;
            }
            int left = x0;
            for (int k = 0, miss = 0; k < maxH; k++)
            {
                int x = x0 - 1 - k;
                if (x < 0) break;
                if (ColIn(im, x, top, bot, sr, sg, sbl, Tol, Frac)) { left = x; miss = 0; }
                else if (++miss >= 2) break;
            }
            int right = x1;
            for (int k = 0, miss = 0; k < maxH; k++)
            {
                int x = x1 + 1 + k;
                if (x >= W) break;
                if (ColIn(im, x, top, bot, sr, sg, sbl, Tol, Frac)) { right = x; miss = 0; }
                else if (++miss >= 2) break;
            }
            // un « bloc » qui n'a presque pas depasse le texte (fond different ou texte sur le papier) ou qui deborde sur toute la page : pas fiable
            float bh = (bot - top) / s, bw = (right - left) / s;
            if (bh < (fy1 - fy0) * 1.15f || bw < (fx1 - fx0) * 1.05f) return false;
            if (bh > H / s * 0.65f || bw > W / s * 0.65f) return false;
            bx0 = left / s; bx1 = right / s; by0 = top / s; by1 = bot / s;
            return true;
        }

        static bool RowIn(PlanImage im, int y, int xa, int xb, int r, int g, int b, int tol, float frac)
        {
            int ok = 0, n = 0, row = y * im.w;
            for (int x = xa; x <= xb; x++)
            {
                var c = im.px[row + x];
                if (Math.Abs(c.r - r) + Math.Abs(c.g - g) + Math.Abs(c.b - b) <= tol) ok++;
                n++;
            }
            return n > 0 && ok >= frac * n;
        }

        static bool ColIn(PlanImage im, int x, int ya, int yb, int r, int g, int b, int tol, float frac)
        {
            int ok = 0, n = 0;
            for (int y = ya; y <= yb; y++)
            {
                var c = im.px[y * im.w + x];
                if (Math.Abs(c.r - r) + Math.Abs(c.g - g) + Math.Abs(c.b - b) <= tol) ok++;
                n++;
            }
            return n > 0 && ok >= frac * n;
        }

        // ------------------------------------------------------------------ mosaique
        /// <summary>Assemble les images retenues sur une grande image reduite de moitie (premiere ligne = haut). 'used', 'ox', 'oy' viennent de Ar6dofAlign.Register.</summary>
        public static PlanImage BuildMosaic(List<Color32[]> frames, int w, int h, bool[] used, int[] ox, int[] oy)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < frames.Count; i++)
            {
                if (!used[i]) continue;
                minX = Math.Min(minX, ox[i]); minY = Math.Min(minY, oy[i]);
                maxX = Math.Max(maxX, ox[i] + w); maxY = Math.Max(maxY, oy[i] + h);
            }
            var im = new PlanImage { s = 0.5f };
            if (minX == int.MaxValue) return im;
            im.fullW = maxX - minX; im.fullH = maxY - minY;
            im.w = Math.Max(2, (int)(im.fullW * im.s)); im.h = Math.Max(2, (int)(im.fullH * im.s));
            im.px = new Color32[im.w * im.h];
            for (int i = 0; i < im.px.Length; i++) im.px[i] = new Color32(40, 40, 40, 255);
            im.frameX = new float[frames.Count]; im.frameY = new float[frames.Count];
            bool first = true;
            for (int i = 0; i < frames.Count; i++)
            {
                im.frameX[i] = -1f; im.frameY[i] = -1f;
                if (!used[i]) continue;
                int fx = ox[i] - minX, fy = oy[i] - minY;
                im.frameX[i] = fx; im.frameY[i] = fy;
                int mw = first ? 0 : (int)(w * 0.06f * 0.5f), mh = first ? 0 : (int)(h * 0.06f * 0.5f);
                first = false;
                var src = frames[i];
                int hw = w / 2, hh = h / 2;
                for (int y = mh; y < hh - mh; y++)
                {
                    int py = (fy >> 1) + y;
                    if (py < 0 || py >= im.h) continue;
                    for (int x = mw; x < hw - mw; x++)
                    {
                        int px = (fx >> 1) + x;
                        if (px < 0 || px >= im.w) continue;
                        int a = (2 * y) * w + 2 * x, b = a + w;
                        var c0 = src[a]; var c1 = src[a + 1]; var c2 = src[b]; var c3 = src[b + 1];
                        im.px[py * im.w + px] = new Color32((byte)((c0.r + c1.r + c2.r + c3.r) >> 2), (byte)((c0.g + c1.g + c2.g + c3.g) >> 2), (byte)((c0.b + c1.b + c2.b + c3.b) >> 2), 255);
                    }
                }
            }
            return im;
        }

        /// <summary>Apercu de ce qui a ete compris : la mosaique reduite (au plus 'maxSide' pixels) avec le contour de chaque bloc trouve (jaune) ou une croix (rouge, duree supposee). Premiere ligne = haut.</summary>
        public static Color32[] DrawPreview(PlanImage im, PlanResult res, int maxSide, out int pw, out int ph)
        {
            pw = 2; ph = 2;
            if (im == null || im.px == null) return new Color32[4];
            int f = Math.Max(1, (Math.Max(im.w, im.h) + maxSide - 1) / maxSide);
            pw = Math.Max(2, im.w / f); ph = Math.Max(2, im.h / f);
            var o = new Color32[pw * ph];
            for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                {
                    int sx = Math.Min(im.w - 1, x * f), sy = Math.Min(im.h - 1, y * f);
                    o[y * pw + x] = im.px[sy * im.w + sx];
                }
            float k = im.s / f;
            var yellow = new Color32(255, 220, 0, 255);
            var red = new Color32(255, 60, 60, 255);
            if (res != null)
                foreach (var e in res.events)
                {
                    if (e.hasBox) Outline(o, pw, ph, (int)(e.bx0 * k), (int)(e.by0 * k), (int)(e.bx1 * k), (int)(e.by1 * k), yellow);
                    else
                    {
                        int cx = (int)(e.tx * k), cy = (int)(e.ty * k);
                        Outline(o, pw, ph, cx - 6, cy - 1, cx + 6, cy + 1, red);
                        Outline(o, pw, ph, cx - 1, cy - 6, cx + 1, cy + 6, red);
                    }
                }
            return o;
        }

        static void Outline(Color32[] o, int w, int h, int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int t = 0; t < 2; t++)
            {
                for (int x = x0; x <= x1; x++) { Put(o, w, h, x, y0 + t, c); Put(o, w, h, x, y1 - t, c); }
                for (int y = y0; y <= y1; y++) { Put(o, w, h, x0 + t, y, c); Put(o, w, h, x1 - t, y, c); }
            }
        }

        static void Put(Color32[] o, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            o[y * w + x] = c;
        }
    }
}
