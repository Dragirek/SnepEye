using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AR6DoF
{
    // Puissance 4 et demineur : du calcul pur, sans Unity. L'IA du puissance 4 tourne dans un fil a part (voir Ar6dofAiJob).

    // ======================================================================== PUISSANCE 4
    /// <summary>Grille de 6 lignes x 7 colonnes, case = ligne * 7 + colonne, ligne 0 en haut. Joueur 1 (moi, rouge) et 2 (PC, jaune).</summary>
    public sealed class Ar6dofConnect4
    {
        public const int Rows = 6, Cols = 7;
        public readonly int[] b = new int[Rows * Cols];
        public int side = 1, count;
        public int[] winCells;

        public Ar6dofConnect4 Clone()
        {
            var g = new Ar6dofConnect4();
            Array.Copy(b, g.b, b.Length);
            g.side = side; g.count = count; g.winCells = winCells;
            return g;
        }

        /// <summary>Ligne ou tomberait un jeton dans cette colonne (la plus basse libre), ou -1 si elle est pleine.</summary>
        public int DropRow(int col)
        {
            for (int r = Rows - 1; r >= 0; r--) if (b[r * Cols + col] == 0) return r;
            return -1;
        }

        /// <summary>Joue dans la colonne ; rend la case occupee, ou -1 si la colonne est pleine.</summary>
        public int Play(int col)
        {
            int r = DropRow(col);
            if (r < 0) return -1;
            int i = r * Cols + col;
            b[i] = side;
            count++;
            winCells = LineAt(b, r, col);
            side = 3 - side;
            return i;
        }

        /// <summary>0 en cours, 1 quelqu'un a aligne quatre jetons, 2 grille pleine.</summary>
        public int Status()
        {
            if (winCells != null) return 1;
            return count >= Rows * Cols ? 2 : 0;
        }

        /// <summary>Les quatre cases alignees passant par (r, c), ou null.</summary>
        public static int[] LineAt(int[] b, int r, int c)
        {
            int p = b[r * Cols + c];
            if (p == 0) return null;
            int[] dr = { 0, 1, 1, 1 }, dc = { 1, 0, 1, -1 };
            for (int d = 0; d < 4; d++)
            {
                int n = 1;
                for (int s = 1; s < 4; s++) { int rr = r + dr[d] * s, cc = c + dc[d] * s; if (rr < 0 || rr >= Rows || cc < 0 || cc >= Cols || b[rr * Cols + cc] != p) break; n++; }
                int back = 0;
                for (int s = 1; s < 4; s++) { int rr = r - dr[d] * s, cc = c - dc[d] * s; if (rr < 0 || rr >= Rows || cc < 0 || cc >= Cols || b[rr * Cols + cc] != p) break; back++; }
                if (n + back >= 4)
                {
                    var res = new List<int>();
                    for (int s = -back; s < n; s++) res.Add((r + dr[d] * s) * Cols + (c + dc[d] * s));
                    return res.ToArray();
                }
            }
            return null;
        }
    }

    public sealed class Ar6dofConnect4Ai
    {
        public volatile bool cancel;
        readonly Random rnd;
        readonly int[] b = new int[Ar6dofConnect4.Rows * Ar6dofConnect4.Cols];
        readonly int[] h = new int[Ar6dofConnect4.Cols];     // jetons deja poses par colonne
        static readonly int[] Order = { 3, 2, 4, 1, 5, 0, 6 };
        Stopwatch clock;
        long limitMs;
        bool timeUp;
        int nodes;
        int me;

        public Ar6dofConnect4Ai(Ar6dofConnect4 g, int seed)
        {
            rnd = new Random(seed);
            Array.Copy(g.b, b, b.Length);
            for (int c = 0; c < Ar6dofConnect4.Cols; c++)
            {
                int n = 0;
                for (int r = 0; r < Ar6dofConnect4.Rows; r++) if (b[r * Ar6dofConnect4.Cols + c] != 0) n++;
                h[c] = n;
            }
            me = g.side;
        }

        int Cell(int c) { return (Ar6dofConnect4.Rows - 1 - h[c]) * Ar6dofConnect4.Cols + c; }

        bool WinsAt(int c, int p)
        {
            int i = Cell(c);
            b[i] = p;
            bool w = Ar6dofConnect4.LineAt(b, i / Ar6dofConnect4.Cols, c) != null;
            b[i] = 0;
            return w;
        }

        static int Window(int a, int o)
        {
            if (a > 0 && o > 0) return 0;
            int n = a > 0 ? a : o;
            int v = n == 3 ? 6 : (n == 2 ? 2 : 0);
            return a > 0 ? v : -v;
        }

        int Eval(int p)
        {
            const int R = Ar6dofConnect4.Rows, C = Ar6dofConnect4.Cols;
            int score = 0;
            for (int r = 0; r < R; r++) if (b[r * C + 3] == p) score += 3; else if (b[r * C + 3] == 3 - p) score -= 3;
            int[] dr = { 0, 1, 1, 1 }, dc = { 1, 0, 1, -1 };
            for (int r = 0; r < R; r++)
                for (int c = 0; c < C; c++)
                    for (int d = 0; d < 4; d++)
                    {
                        int er = r + dr[d] * 3, ec = c + dc[d] * 3;
                        if (er < 0 || er >= R || ec < 0 || ec >= C) continue;
                        int a = 0, o = 0;
                        for (int s = 0; s < 4; s++) { int v = b[(r + dr[d] * s) * C + c + dc[d] * s]; if (v == p) a++; else if (v != 0) o++; }
                        score += Window(a, o);
                    }
            return score;
        }

        int Negamax(int depth, int alpha, int beta, int p)
        {
            if ((++nodes & 2047) == 0 && (cancel || clock.ElapsedMilliseconds > limitMs)) timeUp = true;
            if (timeUp) return 0;
            if (depth == 0) return Eval(p);
            bool any = false;
            int best = -1000000;
            for (int k = 0; k < Order.Length; k++)
            {
                int c = Order[k];
                if (h[c] >= Ar6dofConnect4.Rows) continue;
                any = true;
                int v;
                if (WinsAt(c, p)) v = 100000 + depth;
                else
                {
                    int i = Cell(c);
                    b[i] = p; h[c]++;
                    v = -Negamax(depth - 1, -beta, -alpha, 3 - p);
                    h[c]--; b[i] = 0;
                    if (timeUp) return 0;
                }
                if (v > best) best = v;
                if (best > alpha) alpha = best;
                if (alpha >= beta) break;
            }
            return any ? best : 0;
        }

        /// <summary>Colonne jouee par 'me'. Niveau 0 : prudent mais distrait ; 1 : regarde 5 coups a l'avance ; 2 : cherche aussi loin qu'il peut pendant ~2 secondes.</summary>
        public int Think(int level)
        {
            const int C = Ar6dofConnect4.Cols;
            var free = new List<int>();
            for (int c = 0; c < C; c++) if (h[c] < Ar6dofConnect4.Rows) free.Add(c);
            if (free.Count == 0) return -1;
            foreach (int c in free) if (WinsAt(c, me)) return c;
            if (level <= 0 && rnd.Next(100) < 40) return free[rnd.Next(free.Count)];
            foreach (int c in free) if (WinsAt(c, 3 - me) && level >= 1) return c;
            int maxDepth = level <= 0 ? 2 : (level == 1 ? 5 : 12);
            limitMs = level >= 2 ? 1800 : 700;
            clock = Stopwatch.StartNew();
            int bestCol = free[0];
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                timeUp = false;
                var bests = new List<int>();
                int bestV = -2000000;
                for (int k = 0; k < Order.Length; k++)
                {
                    int c = Order[k];
                    if (h[c] >= Ar6dofConnect4.Rows) continue;
                    int i = Cell(c);
                    b[i] = me; h[c]++;
                    int v = -Negamax(depth - 1, -1000000, 1000000, 3 - me);
                    h[c]--; b[i] = 0;
                    if (timeUp) break;
                    if (v > bestV) { bestV = v; bests.Clear(); }
                    if (v == bestV) bests.Add(c);
                }
                if (timeUp || cancel) break;
                if (bests.Count > 0) bestCol = bests[rnd.Next(bests.Count)];
                if (bestV > 90000 || bestV < -90000) break;      // partie decidee : inutile d'aller plus loin
            }
            return bestCol;
        }
    }

    // ======================================================================== DEMINEUR
    public sealed class Ar6dofMines
    {
        public readonly int rows, cols, mineCount;
        public readonly bool[] mine, open, flag;
        public readonly int[] near;
        /// <summary>0 pas commence, 1 en cours, 2 gagne, 3 perdu.</summary>
        public int state;
        public int opened, flags, hit = -1;
        readonly Random rnd;

        public Ar6dofMines(int rows, int cols, int mines, Random rnd)
        {
            this.rows = rows; this.cols = cols; this.mineCount = Math.Min(mines, rows * cols - 1); this.rnd = rnd;
            int t = rows * cols;
            mine = new bool[t]; open = new bool[t]; flag = new bool[t]; near = new int[t];
        }

        void Neighbors(int i, List<int> into)
        {
            into.Clear();
            int r = i / cols, c = i % cols;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int rr = r + dr, cc = c + dc;
                    if (rr >= 0 && rr < rows && cc >= 0 && cc < cols) into.Add(rr * cols + cc);
                }
        }

        readonly List<int> tmp = new List<int>(8), tmp2 = new List<int>(8);

        void Place(int safe)
        {
            // les mines evitent la premiere case et ses voisines (la premiere case est donc toujours un « 0 » qui s'ouvre), si la grille le permet
            var banned = new HashSet<int> { safe };
            Neighbors(safe, tmp);
            foreach (int n in tmp) banned.Add(n);
            if (rows * cols - banned.Count < mineCount) { banned.Clear(); banned.Add(safe); }
            var cand = new List<int>();
            for (int i = 0; i < rows * cols; i++) if (!banned.Contains(i)) cand.Add(i);
            for (int k = 0; k < mineCount; k++)
            {
                int j = k + rnd.Next(cand.Count - k);
                int t = cand[k]; cand[k] = cand[j]; cand[j] = t;
                mine[cand[k]] = true;
            }
            for (int i = 0; i < rows * cols; i++)
            {
                if (mine[i]) continue;
                Neighbors(i, tmp);
                int n = 0;
                foreach (int q in tmp) if (mine[q]) n++;
                near[i] = n;
            }
        }

        /// <summary>Creuse une case (rien si elle porte un drapeau ou est deja ouverte).</summary>
        public void Open(int i)
        {
            if (state >= 2 || flag[i] || open[i]) return;
            if (state == 0) { Place(i); state = 1; }
            Reveal(i);
            CheckWin();
        }

        void Reveal(int i)
        {
            if (state >= 2 || flag[i] || open[i]) return;
            if (mine[i]) { hit = i; state = 3; open[i] = true; return; }
            var stack = new Stack<int>();
            stack.Push(i);
            while (stack.Count > 0)
            {
                int q = stack.Pop();
                if (open[q] || flag[q]) continue;
                open[q] = true; opened++;
                if (near[q] != 0) continue;
                Neighbors(q, tmp2);
                foreach (int n in tmp2) if (!open[n] && !flag[n] && !mine[n]) stack.Push(n);
            }
        }

        void CheckWin()
        {
            if (state == 1 && opened >= rows * cols - mineCount)
            {
                state = 2;
                for (int i = 0; i < rows * cols; i++) if (mine[i] && !flag[i]) { flag[i] = true; flags++; }
            }
        }

        public void ToggleFlag(int i)
        {
            if (state >= 2 || open[i]) return;
            if (state == 0) return;     // avant le premier clic il n'y a pas encore de mines : un drapeau ne servirait a rien
            flag[i] = !flag[i];
            flags += flag[i] ? 1 : -1;
        }

        /// <summary>Sur un chiffre dont les drapeaux sont tous poses : ouvre toutes les autres voisines d'un coup.</summary>
        public void Chord(int i)
        {
            if (state != 1 || !open[i] || near[i] == 0) return;
            Neighbors(i, tmp);
            var list = new List<int>(tmp);
            int f = 0;
            foreach (int n in list) if (flag[n]) f++;
            if (f != near[i]) return;
            foreach (int n in list) if (!flag[n] && !open[n]) Reveal(n);
            CheckWin();
        }
    }
}
