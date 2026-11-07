using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AR6DoF
{
    // Regles et "intelligence" des jeux contre le PC. Aucune dependance a Unity : tout est du calcul pur (pas de UnityEngine ici),
    // ce qui permet de faire reflechir les echecs et les dames dans un fil a part sans jamais bloquer l'affichage ni le suivi de ta tete.

    // ======================================================================== MORPION
    public static class Ar6dofTicTacToe
    {
        static readonly int[,] Lines = { { 0, 1, 2 }, { 3, 4, 5 }, { 6, 7, 8 }, { 0, 3, 6 }, { 1, 4, 7 }, { 2, 5, 8 }, { 0, 4, 8 }, { 2, 4, 6 } };

        /// <summary>1 ou 2 si ce joueur a aligne trois pions, sinon 0.</summary>
        public static int Winner(int[] b)
        {
            for (int i = 0; i < 8; i++)
            {
                int a = b[Lines[i, 0]];
                if (a != 0 && a == b[Lines[i, 1]] && a == b[Lines[i, 2]]) return a;
            }
            return 0;
        }

        /// <summary>Les trois cases de la ligne gagnante (ou null).</summary>
        public static int[] WinLine(int[] b)
        {
            for (int i = 0; i < 8; i++)
            {
                int a = b[Lines[i, 0]];
                if (a != 0 && a == b[Lines[i, 1]] && a == b[Lines[i, 2]]) return new[] { Lines[i, 0], Lines[i, 1], Lines[i, 2] };
            }
            return null;
        }

        public static bool Full(int[] b) { for (int i = 0; i < 9; i++) if (b[i] == 0) return false; return true; }

        static int Minimax(int[] b, int turn, int me, int depth)
        {
            int w = Winner(b);
            if (w == me) return 10 - depth;
            if (w != 0) return depth - 10;
            if (Full(b)) return 0;
            bool mine = turn == me;
            int best = mine ? -99 : 99;
            for (int i = 0; i < 9; i++)
            {
                if (b[i] != 0) continue;
                b[i] = turn;
                int v = Minimax(b, 3 - turn, me, depth + 1);
                b[i] = 0;
                if (mine ? v > best : v < best) best = v;
            }
            return best;
        }

        /// <summary>Case que joue 'me' (1 ou 2). Niveau 0 : au hasard ; 1 : gagne ou bloque, sinon au hasard ; 2 : imbattable.</summary>
        public static int Best(int[] b, int me, int level, Random rnd)
        {
            var free = new List<int>();
            for (int i = 0; i < 9; i++) if (b[i] == 0) free.Add(i);
            if (free.Count == 0) return -1;
            if (level <= 0) return free[rnd.Next(free.Count)];
            int opp = 3 - me;
            if (level == 1)
            {
                foreach (int i in free) { b[i] = me; bool w = Winner(b) == me; b[i] = 0; if (w) return i; }
                foreach (int i in free) { b[i] = opp; bool w = Winner(b) == opp; b[i] = 0; if (w) return i; }
                return free[rnd.Next(free.Count)];
            }
            int bestV = -99;
            var best = new List<int>();
            foreach (int i in free)
            {
                b[i] = me;
                int v = Minimax(b, opp, me, 1);
                b[i] = 0;
                if (v > bestV) { bestV = v; best.Clear(); }
                if (v == bestV) best.Add(i);
            }
            return best[rnd.Next(best.Count)];
        }
    }

    // ======================================================================== BATAILLE NAVALE
    public sealed class Ar6dofFleet
    {
        public const int N = 10;
        public static readonly int[] Sizes = { 5, 4, 3, 3, 2 };
        public static readonly string[] Names = { "Porte-avions", "Croiseur", "Destroyer", "Sous-marin", "Patrouilleur" };

        public readonly int[] ship = new int[N * N];      // -1 : eau, sinon numero du navire
        public readonly bool[] shot = new bool[N * N];
        public readonly int[] left = new int[5];          // cases non touchees de chaque navire

        public Ar6dofFleet() { Clear(); }

        public void Clear()
        {
            for (int i = 0; i < N * N; i++) { ship[i] = -1; shot[i] = false; }
            for (int k = 0; k < Sizes.Length; k++) left[k] = Sizes[k];
        }

        public void PlaceRandom(Random r)
        {
            Clear();
            for (int k = 0; k < Sizes.Length; k++)
            {
                int len = Sizes[k];
                for (int tries = 0; tries < 2000; tries++)
                {
                    bool horiz = r.Next(2) == 0;
                    int x = r.Next(horiz ? N - len + 1 : N), y = r.Next(horiz ? N : N - len + 1);
                    bool ok = true;
                    for (int i = 0; i < len; i++)
                        if (ship[(y + (horiz ? 0 : i)) * N + x + (horiz ? i : 0)] != -1) { ok = false; break; }
                    if (!ok) continue;
                    for (int i = 0; i < len; i++) ship[(y + (horiz ? 0 : i)) * N + x + (horiz ? i : 0)] = k;
                    break;
                }
            }
        }

        /// <summary>0 : a l'eau, 1 : touche, 2 : coule (sunk = numero du navire), -1 : deja tire ici.</summary>
        public int Shoot(int i, out int sunk)
        {
            sunk = -1;
            if (shot[i]) return -1;
            shot[i] = true;
            int k = ship[i];
            if (k < 0) return 0;
            left[k]--;
            if (left[k] == 0) { sunk = k; return 2; }
            return 1;
        }

        public bool AllSunk { get { for (int k = 0; k < left.Length; k++) if (left[k] > 0) return false; return true; } }

        public int Afloat { get { int n = 0; for (int k = 0; k < left.Length; k++) if (left[k] > 0) n++; return n; } }
    }

    /// <summary>Le PC : il cherche en damier, et des qu'il touche il explore autour (et prolonge les alignements) jusqu'a couler le navire.</summary>
    public sealed class Ar6dofNavalAi
    {
        readonly int[] st = new int[Ar6dofFleet.N * Ar6dofFleet.N];   // 0 inconnu, 1 eau, 2 touche, 3 coule
        readonly Random rnd;
        public Ar6dofNavalAi(Random r) { rnd = r; }

        public int Next()
        {
            int n = Ar6dofFleet.N;
            var score = new float[n * n];
            bool any = false;
            int[] dxs = { 1, -1, 0, 0 }, dys = { 0, 0, 1, -1 };
            for (int i = 0; i < n * n; i++)
            {
                if (st[i] != 2) continue;
                int x = i % n, y = i / n;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dxs[d], ny = y + dys[d];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int j = ny * n + nx;
                    if (st[j] != 0) continue;
                    float s = 1f;
                    int px = x - dxs[d], py = y - dys[d];
                    if (px >= 0 && py >= 0 && px < n && py < n && st[py * n + px] == 2) s += 4f;
                    score[j] += s;
                    any = true;
                }
            }
            var pick = new List<int>();
            if (any)
            {
                float best = 0f;
                for (int i = 0; i < n * n; i++) if (score[i] > best) best = score[i];
                for (int i = 0; i < n * n; i++) if (score[i] >= best - 0.001f && score[i] > 0f) pick.Add(i);
            }
            else
            {
                for (int i = 0; i < n * n; i++) if (st[i] == 0 && (((i % n) + (i / n)) & 1) == 0) pick.Add(i);
                if (pick.Count == 0) for (int i = 0; i < n * n; i++) if (st[i] == 0) pick.Add(i);
            }
            return pick.Count == 0 ? -1 : pick[rnd.Next(pick.Count)];
        }

        public void Report(int i, int result, Ar6dofFleet f, int sunk)
        {
            if (result == 0) { st[i] = 1; return; }
            st[i] = 2;
            if (result == 2)
                for (int j = 0; j < st.Length; j++) if (f.ship[j] == sunk && st[j] == 2) st[j] = 3;
        }
    }

    // ======================================================================== ECHECS
    /// <summary>
    /// Position d'echecs avec toutes les regles (roque, prise en passant, promotion, echec et mat, pat, 50 coups, materiel insuffisant).
    /// Cases : rang * 8 + colonne, rang 0 = cote des blancs. Pieces : 1 pion, 2 cavalier, 3 fou, 4 tour, 5 dame, 6 roi ; negatif = noirs.
    /// Un coup tient dans un entier : depart | arrivee &lt;&lt; 6 | promotion &lt;&lt; 12 | drapeaux &lt;&lt; 16.
    /// </summary>
    public sealed class Ar6dofChess
    {
        public const int WK = 1, WQ = 2, BK = 4, BQ = 8;
        public const int FlEp = 1, FlCastleK = 2, FlCastleQ = 4, FlDouble = 8;
        static readonly int[] NdF = { 1, 2, 2, 1, -1, -2, -2, -1 }, NdR = { 2, 1, -1, -2, -2, -1, 1, 2 };
        static readonly int[] KdF = { 1, 1, 0, -1, -1, -1, 0, 1 }, KdR = { 0, 1, 1, 1, 0, -1, -1, -1 };
        static readonly int[] Rights = MakeRights();
        static int[] MakeRights() { var r = new int[64]; r[0] = WQ; r[7] = WK; r[4] = WK | WQ; r[56] = BQ; r[63] = BK; r[60] = BK | BQ; return r; }

        public readonly sbyte[] b = new sbyte[64];
        public int side = 1, castle = 15, ep = -1, half;
        public readonly int[] ks = { 4, 60 };       // cases des rois (blanc, noir)

        struct Undo { public int move, cap, castle, ep, half, k0, k1; }
        readonly Undo[] us = new Undo[160];
        int ply;

        public static int From(int m) { return m & 63; }
        public static int To(int m) { return (m >> 6) & 63; }
        public static int Promo(int m) { return (m >> 12) & 7; }

        public static Ar6dofChess Start()
        {
            var c = new Ar6dofChess();
            int[] back = { 4, 2, 3, 5, 6, 3, 2, 4 };
            for (int f = 0; f < 8; f++)
            {
                c.b[f] = (sbyte)back[f]; c.b[8 + f] = 1; c.b[48 + f] = -1; c.b[56 + f] = (sbyte)-back[f];
            }
            return c;
        }

        public Ar6dofChess Clone()
        {
            var c = new Ar6dofChess();
            Array.Copy(b, c.b, 64);
            c.side = side; c.castle = castle; c.ep = ep; c.half = half; c.ks[0] = ks[0]; c.ks[1] = ks[1];
            return c;
        }

        /// <summary>Joue un coup pour de bon (on ne pourra plus le defaire).</summary>
        public void Play(int m) { Make(m); ply = 0; }

        public bool Attacked(int sq, int by)
        {
            int f = sq & 7, r = sq >> 3;
            int pr = by == 1 ? r - 1 : r + 1;
            if (pr >= 0 && pr < 8)
            {
                if (f > 0 && b[pr * 8 + f - 1] == by) return true;
                if (f < 7 && b[pr * 8 + f + 1] == by) return true;
            }
            for (int k = 0; k < 8; k++)
            {
                int nf = f + NdF[k], nr = r + NdR[k];
                if (nf >= 0 && nf < 8 && nr >= 0 && nr < 8 && b[nr * 8 + nf] == by * 2) return true;
            }
            for (int k = 0; k < 8; k++)
            {
                int nf = f + KdF[k], nr = r + KdR[k];
                if (nf >= 0 && nf < 8 && nr >= 0 && nr < 8 && b[nr * 8 + nf] == by * 6) return true;
            }
            for (int k = 0; k < 8; k++)
            {
                bool diag = (k & 1) == 1;
                int nf = f + KdF[k], nr = r + KdR[k];
                while (nf >= 0 && nf < 8 && nr >= 0 && nr < 8)
                {
                    int p = b[nr * 8 + nf];
                    if (p != 0)
                    {
                        if (p == by * 5 || p == by * (diag ? 3 : 4)) return true;
                        break;
                    }
                    nf += KdF[k]; nr += KdR[k];
                }
            }
            return false;
        }

        public bool InCheck(int s) { return Attacked(ks[s == 1 ? 0 : 1], -s); }

        void AddPawn(int[] mv, ref int n, int fr, int to, int nr)
        {
            if (nr == 7 || nr == 0)
            {
                mv[n++] = fr | (to << 6) | (5 << 12);
                mv[n++] = fr | (to << 6) | (4 << 12);
                mv[n++] = fr | (to << 6) | (3 << 12);
                mv[n++] = fr | (to << 6) | (2 << 12);
            }
            else mv[n++] = fr | (to << 6);
        }

        /// <summary>Coups possibles sans verifier que le roi reste a l'abri ; renvoie leur nombre.</summary>
        public int GenPseudo(int[] mv)
        {
            int n = 0, sd = side;
            for (int sq = 0; sq < 64; sq++)
            {
                int p = b[sq];
                if (p == 0 || (p > 0) != (sd > 0)) continue;
                int t = p < 0 ? -p : p;
                int f = sq & 7, r = sq >> 3;
                if (t == 1)
                {
                    int nr = r + sd;
                    if (nr >= 0 && nr < 8)
                    {
                        int to = nr * 8 + f;
                        if (b[to] == 0)
                        {
                            AddPawn(mv, ref n, sq, to, nr);
                            if ((sd == 1 && r == 1) || (sd == -1 && r == 6))
                            {
                                int to2 = (r + 2 * sd) * 8 + f;
                                if (b[to2] == 0) mv[n++] = sq | (to2 << 6) | (FlDouble << 16);
                            }
                        }
                        for (int df = -1; df <= 1; df += 2)
                        {
                            int nf = f + df;
                            if (nf < 0 || nf > 7) continue;
                            int tt = nr * 8 + nf;
                            if (b[tt] != 0 && (b[tt] > 0) != (sd > 0)) AddPawn(mv, ref n, sq, tt, nr);
                            else if (tt == ep) mv[n++] = sq | (tt << 6) | (FlEp << 16);
                        }
                    }
                }
                else if (t == 2)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        int nf = f + NdF[k], nr = r + NdR[k];
                        if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                        int tt = nr * 8 + nf;
                        if (b[tt] == 0 || (b[tt] > 0) != (sd > 0)) mv[n++] = sq | (tt << 6);
                    }
                }
                else if (t >= 3 && t <= 5)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        bool diag = (k & 1) == 1;
                        if (t == 3 && !diag) continue;
                        if (t == 4 && diag) continue;
                        int nf = f + KdF[k], nr = r + KdR[k];
                        while (nf >= 0 && nf < 8 && nr >= 0 && nr < 8)
                        {
                            int tt = nr * 8 + nf;
                            if (b[tt] == 0) mv[n++] = sq | (tt << 6);
                            else
                            {
                                if ((b[tt] > 0) != (sd > 0)) mv[n++] = sq | (tt << 6);
                                break;
                            }
                            nf += KdF[k]; nr += KdR[k];
                        }
                    }
                }
                else
                {
                    for (int k = 0; k < 8; k++)
                    {
                        int nf = f + KdF[k], nr = r + KdR[k];
                        if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                        int tt = nr * 8 + nf;
                        if (b[tt] == 0 || (b[tt] > 0) != (sd > 0)) mv[n++] = sq | (tt << 6);
                    }
                    if (sd == 1 && sq == 4)
                    {
                        if ((castle & WK) != 0 && b[5] == 0 && b[6] == 0 && b[7] == 4 && !Attacked(4, -1) && !Attacked(5, -1)) mv[n++] = 4 | (6 << 6) | (FlCastleK << 16);
                        if ((castle & WQ) != 0 && b[3] == 0 && b[2] == 0 && b[1] == 0 && b[0] == 4 && !Attacked(4, -1) && !Attacked(3, -1)) mv[n++] = 4 | (2 << 6) | (FlCastleQ << 16);
                    }
                    else if (sd == -1 && sq == 60)
                    {
                        if ((castle & BK) != 0 && b[61] == 0 && b[62] == 0 && b[63] == -4 && !Attacked(60, 1) && !Attacked(61, 1)) mv[n++] = 60 | (62 << 6) | (FlCastleK << 16);
                        if ((castle & BQ) != 0 && b[59] == 0 && b[58] == 0 && b[57] == 0 && b[56] == -4 && !Attacked(60, 1) && !Attacked(59, 1)) mv[n++] = 60 | (58 << 6) | (FlCastleQ << 16);
                    }
                }
            }
            return n;
        }

        public void Make(int m)
        {
            int fr = m & 63, to = (m >> 6) & 63, pr = (m >> 12) & 7, fl = m >> 16;
            int p = b[fr], cap = b[to], sd = side;
            if ((fl & FlEp) != 0) { int cs = sd == 1 ? to - 8 : to + 8; cap = b[cs]; b[cs] = 0; }
            var u = new Undo();
            u.move = m; u.cap = cap; u.castle = castle; u.ep = ep; u.half = half; u.k0 = ks[0]; u.k1 = ks[1];
            us[ply++] = u;
            b[to] = (sbyte)(pr != 0 ? sd * pr : p);
            b[fr] = 0;
            if ((fl & FlCastleK) != 0) { b[to - 1] = b[to + 1]; b[to + 1] = 0; }
            else if ((fl & FlCastleQ) != 0) { b[to + 1] = b[to - 2]; b[to - 2] = 0; }
            if (p == 6 || p == -6) ks[sd == 1 ? 0 : 1] = to;
            half = (p == 1 || p == -1 || cap != 0) ? 0 : half + 1;
            ep = (fl & FlDouble) != 0 ? (fr + to) / 2 : -1;
            castle &= ~Rights[fr] & ~Rights[to];
            side = -sd;
        }

        public void Unmake()
        {
            var u = us[--ply];
            int m = u.move, fr = m & 63, to = (m >> 6) & 63, pr = (m >> 12) & 7, fl = m >> 16;
            side = -side;
            int sd = side;
            int p = pr != 0 ? sd : b[to];
            b[fr] = (sbyte)p;
            if ((fl & FlEp) != 0) { b[to] = 0; b[sd == 1 ? to - 8 : to + 8] = (sbyte)u.cap; }
            else b[to] = (sbyte)u.cap;
            if ((fl & FlCastleK) != 0) { b[to + 1] = b[to - 1]; b[to - 1] = 0; }
            else if ((fl & FlCastleQ) != 0) { b[to - 2] = b[to + 1]; b[to + 1] = 0; }
            castle = u.castle; ep = u.ep; half = u.half; ks[0] = u.k0; ks[1] = u.k1;
        }

        /// <summary>Coups legaux du camp au trait dans 'buf' (256 cases) ; renvoie leur nombre.</summary>
        public int LegalMoves(int[] buf)
        {
            var tmp = new int[256];
            int n = GenPseudo(tmp), k = 0, me = side;
            for (int i = 0; i < n; i++)
            {
                Make(tmp[i]);
                bool ok = !Attacked(ks[me == 1 ? 0 : 1], -me);
                Unmake();
                if (ok) buf[k++] = tmp[i];
            }
            return k;
        }

        /// <summary>0 en cours, 1 echec et mat (le camp au trait a perdu), 2 pat, 3 regle des 50 coups, 4 materiel insuffisant.</summary>
        public int Status()
        {
            var buf = new int[256];
            if (LegalMoves(buf) == 0) return InCheck(side) ? 1 : 2;
            if (half >= 100) return 3;
            int minor = 0;
            for (int sq = 0; sq < 64; sq++)
            {
                int t = b[sq] < 0 ? -b[sq] : b[sq];
                if (t == 1 || t == 4 || t == 5) return 0;
                if (t == 2 || t == 3) minor++;
            }
            return minor <= 1 ? 4 : 0;
        }
    }

    /// <summary>Le joueur d'echecs du PC : recherche alpha-beta avec approfondissement iteratif. A lancer dans un fil a part (Think peut durer 2 secondes).</summary>
    public sealed class Ar6dofChessAi
    {
        const int Mate = 100000;
        static readonly int[] Val = { 0, 100, 320, 330, 500, 900, 0 };
        readonly Ar6dofChess pos;
        readonly int[][] mv = new int[64][];
        readonly int[] key = new int[256];
        readonly Stopwatch sw = new Stopwatch();
        readonly Random rnd;
        int nodes, limit;
        bool stopped;
        public volatile bool cancel;

        public Ar6dofChessAi(Ar6dofChess p, int seed)
        {
            pos = p.Clone();
            rnd = new Random(seed);
            for (int i = 0; i < mv.Length; i++) mv[i] = new int[256];
        }

        static int Pst(int t, int sq, int col)
        {
            int f = sq & 7, r = sq >> 3;
            int rel = col == 1 ? r : 7 - r;
            int cd = Math.Abs(2 * f - 7) + Math.Abs(2 * r - 7);   // 0 au centre, 14 dans un coin
            switch (t)
            {
                case 1: return rel * 6 + ((f == 3 || f == 4) ? 8 : 0) + (rel >= 5 ? 10 : 0);
                case 2: return (14 - cd) * 4;
                case 3: return (14 - cd) * 2;
                case 5: return 14 - cd;
                case 6: return rel == 0 ? ((f <= 2 || f >= 6) ? 25 : 0) : -15;
                default: return 0;
            }
        }

        int Eval()
        {
            var b = pos.b;
            int sc = 0, mat = 0;
            for (int sq = 0; sq < 64; sq++)
            {
                int v = b[sq];
                if (v == 0) continue;
                int t = v < 0 ? -v : v, col = v > 0 ? 1 : -1;
                int x = Val[t] + Pst(t, sq, col);
                sc += col == 1 ? x : -x;
                if (t != 6) mat += Val[t] * col;
            }
            // finale : le camp qui gagne pousse le roi adverse vers le bord et rapproche le sien
            if (mat > 250 || mat < -250)
            {
                int w = mat > 0 ? 1 : -1;
                int lk = pos.ks[w == 1 ? 1 : 0], wk = pos.ks[w == 1 ? 0 : 1];
                int cd = Math.Abs(2 * (lk & 7) - 7) + Math.Abs(2 * (lk >> 3) - 7);
                int dist = Math.Abs((lk & 7) - (wk & 7)) + Math.Abs((lk >> 3) - (wk >> 3));
                sc += (cd * 14 + (14 - dist) * 12) * w;
            }
            return pos.side == 1 ? sc : -sc;
        }

        bool IsCapture(int m) { return pos.b[(m >> 6) & 63] != 0 || ((m >> 16) & 1) != 0 || ((m >> 12) & 7) != 0; }

        void Order(int[] m, int n)
        {
            var b = pos.b;
            for (int i = 0; i < n; i++)
            {
                int mm = m[i], to = (mm >> 6) & 63, fr = mm & 63, k = 0;
                int v = b[to] < 0 ? -b[to] : b[to];
                if (((mm >> 16) & 1) != 0) v = 1;
                if (v != 0) { int a = b[fr] < 0 ? -b[fr] : b[fr]; k = 10 * Val[v] - Val[a] / 10 + 1000; }
                if (((mm >> 12) & 7) != 0) k += 800;
                key[i] = k;
            }
            for (int i = 1; i < n; i++)
            {
                int mm = m[i], kk = key[i], j = i - 1;
                while (j >= 0 && key[j] < kk) { m[j + 1] = m[j]; key[j + 1] = key[j]; j--; }
                m[j + 1] = mm; key[j + 1] = kk;
            }
        }

        bool TimeUp()
        {
            if ((++nodes & 1023) == 0 && (cancel || sw.ElapsedMilliseconds > limit)) stopped = true;
            return stopped;
        }

        int Quiesce(int alpha, int beta, int ply)
        {
            if (TimeUp()) return 0;
            int stand = Eval();
            if (ply >= 56) return stand;
            if (stand >= beta) return stand;
            if (stand > alpha) alpha = stand;
            int[] m = mv[ply];
            int n = pos.GenPseudo(m), k = 0;
            for (int i = 0; i < n; i++) if (IsCapture(m[i])) m[k++] = m[i];
            n = k;
            Order(m, n);
            int me = pos.side;
            for (int i = 0; i < n; i++)
            {
                pos.Make(m[i]);
                if (pos.Attacked(pos.ks[me == 1 ? 0 : 1], -me)) { pos.Unmake(); continue; }
                int sc = -Quiesce(-beta, -alpha, ply + 1);
                pos.Unmake();
                if (stopped) return 0;
                if (sc >= beta) return sc;
                if (sc > alpha) alpha = sc;
            }
            return alpha;
        }

        int Search(int depth, int alpha, int beta, int ply)
        {
            if (TimeUp()) return 0;
            if (depth <= 0) return Quiesce(alpha, beta, ply);
            if (pos.half >= 100) return 0;
            int me = pos.side;
            bool inCheck = pos.Attacked(pos.ks[me == 1 ? 0 : 1], -me);
            if (inCheck && ply < 30) depth++;
            int[] m = mv[ply];
            int n = pos.GenPseudo(m);
            Order(m, n);
            int legal = 0, best = -Mate;
            for (int i = 0; i < n; i++)
            {
                pos.Make(m[i]);
                if (pos.Attacked(pos.ks[me == 1 ? 0 : 1], -me)) { pos.Unmake(); continue; }
                legal++;
                int sc = -Search(depth - 1, -beta, -alpha, ply + 1);
                pos.Unmake();
                if (stopped) return 0;
                if (sc > best) best = sc;
                if (sc > alpha) alpha = sc;
                if (alpha >= beta) break;
            }
            if (legal == 0) return inCheck ? -Mate + ply : 0;
            return best;
        }

        /// <summary>Le meilleur coup (0 s'il n'y en a pas, ou si on a annule). Niveau 0 facile, 1 moyen, 2 fort.</summary>
        public int Think(int level)
        {
            var legal = new int[256];
            int n = pos.LegalMoves(legal);
            if (n == 0) return 0;
            if (n == 1) return legal[0];
            sw.Reset(); sw.Start();
            stopped = false; nodes = 0;
            limit = level >= 2 ? 2500 : 1500;
            if (level <= 0)
            {
                var sc = new int[n];
                int bestSc = -2 * Mate;
                for (int i = 0; i < n; i++)
                {
                    pos.Make(legal[i]);
                    sc[i] = -Search(1, -2 * Mate, 2 * Mate, 1);
                    pos.Unmake();
                    if (sc[i] > bestSc) bestSc = sc[i];
                }
                if (cancel) return 0;
                var pick = new List<int>();
                for (int i = 0; i < n; i++) if (sc[i] >= bestSc - 90) pick.Add(legal[i]);
                return pick[rnd.Next(pick.Count)];
            }
            int maxDepth = level == 1 ? 3 : 8;
            int bestMove = legal[0];
            Order(legal, n);
            for (int d = 1; d <= maxDepth; d++)
            {
                int alpha = -2 * Mate, beta = 2 * Mate, dBest = -2 * Mate, dm = 0, dmIdx = 0;
                for (int i = 0; i < n; i++)
                {
                    pos.Make(legal[i]);
                    int sc = -Search(d - 1, -beta, -alpha, 1);
                    pos.Unmake();
                    if (stopped) break;
                    if (sc > dBest) { dBest = sc; dm = legal[i]; dmIdx = i; }
                    if (sc > alpha) alpha = sc;
                }
                if (stopped) break;
                bestMove = dm;
                for (int i = dmIdx; i > 0; i--) legal[i] = legal[i - 1];    // le meilleur coup passe en tete pour la profondeur suivante
                legal[0] = dm;
                if (dBest > Mate - 100) break;
            }
            return cancel ? 0 : bestMove;
        }
    }

    // ======================================================================== DAMES
    /// <summary>
    /// Jeu de dames sur 8 x 8 (regles anglaises / americaines) : prise obligatoire, rafles, le pion ne recule pas, la dame avance d'une case.
    /// Cases : rang * 8 + colonne, rang 0 = cote des blancs (le joueur). 1 pion blanc, 2 dame blanche, -1 pion noir, -2 dame noire.
    /// </summary>
    public sealed class Ar6dofCheckers
    {
        public sealed class Move
        {
            public int[] path;     // case de depart, puis chaque case ou le pion se pose
            public int[] caps;     // cases des pieces prises
        }

        static readonly int[] None = new int[0];
        public readonly sbyte[] b = new sbyte[64];
        public int side = 1, quiet;
        /// <summary>Vrai : dames francaises / bresiliennes (le pion prend aussi en arriere, la dame vole sur toute la diagonale, il faut prendre le plus de pieces possible). Faux : dames americaines.</summary>
        public bool french = true;
        const sbyte Taken = 9;      // pieces deja prises pendant une rafle : elles restent sur le plateau jusqu'a la fin du coup et bloquent le passage

        public static Ar6dofCheckers Start() { return Start(true); }

        public static Ar6dofCheckers Start(bool french)
        {
            var c = new Ar6dofCheckers { french = french };
            for (int r = 0; r < 8; r++)
                for (int f = 0; f < 8; f++)
                {
                    if (((r + f) & 1) != 0) continue;
                    if (r < 3) c.b[r * 8 + f] = 1;
                    else if (r > 4) c.b[r * 8 + f] = -1;
                }
            return c;
        }

        public Ar6dofCheckers Clone()
        {
            var c = new Ar6dofCheckers();
            Array.Copy(b, c.b, 64);
            c.side = side; c.quiet = quiet; c.french = french;
            return c;
        }

        void Record(List<int> path, List<int> taken, List<Move> outL)
        {
            outL.Add(new Move { path = path.ToArray(), caps = taken.ToArray() });
        }

        static bool In(int r, int f) { return r >= 0 && r < 8 && f >= 0 && f < 8; }

        void Take(sbyte[] w, int mid, int land, int p, List<int> path, List<int> taken, List<Move> outL)
        {
            sbyte mp = w[mid];
            w[mid] = Taken;
            path.Add(land); taken.Add(mid);
            Jumps(w, land, p, path, taken, outL);
            path.RemoveAt(path.Count - 1); taken.RemoveAt(taken.Count - 1);
            w[mid] = mp;
        }

        void JumpsFrench(sbyte[] w, int sq, int p, List<int> path, List<int> taken, List<Move> outL)
        {
            int r = sq >> 3, f = sq & 7;
            bool king = p == 2 || p == -2;
            int before = outL.Count;
            for (int dr = -1; dr <= 1; dr += 2)
                for (int df = -1; df <= 1; df += 2)
                {
                    int rr = r + dr, ff = f + df;
                    if (king) while (In(rr, ff) && w[rr * 8 + ff] == 0) { rr += dr; ff += df; }
                    if (!In(rr, ff)) continue;
                    int mid = rr * 8 + ff;
                    int mp = w[mid];
                    if (mp == 0 || mp == Taken || (mp > 0) == (p > 0)) continue;
                    int lr = rr + dr, lf = ff + df;
                    while (In(lr, lf) && w[lr * 8 + lf] == 0)
                    {
                        Take(w, mid, lr * 8 + lf, p, path, taken, outL);
                        if (!king) break;                 // le pion se pose juste derriere ; la dame peut se poser sur n'importe quelle case libre apres la prise
                        lr += dr; lf += df;
                    }
                }
            if (taken.Count > 0 && outL.Count == before) Record(path, taken, outL);     // plus rien a prendre depuis ici : la rafle s'arrete
        }

        void Jumps(sbyte[] w, int sq, int p, List<int> path, List<int> taken, List<Move> outL)
        {
            if (french) { JumpsFrench(w, sq, p, path, taken, outL); return; }
            int r = sq >> 3, f = sq & 7;
            bool king = p == 2 || p == -2, any = false;
            for (int dr = -1; dr <= 1; dr += 2)
            {
                if (!king && dr != side) continue;
                for (int df = -1; df <= 1; df += 2)
                {
                    int lr = r + 2 * dr, lf = f + 2 * df;
                    if (lr < 0 || lr > 7 || lf < 0 || lf > 7) continue;
                    int mid = (r + dr) * 8 + f + df, land = lr * 8 + lf;
                    int mp = w[mid];
                    if (mp == 0 || (mp > 0) == (p > 0) || w[land] != 0) continue;
                    any = true;
                    w[mid] = 0;
                    path.Add(land); taken.Add(mid);
                    bool crowned = !king && ((p == 1 && lr == 7) || (p == -1 && lr == 0));
                    if (crowned) Record(path, taken, outL);
                    else Jumps(w, land, p, path, taken, outL);
                    path.RemoveAt(path.Count - 1); taken.RemoveAt(taken.Count - 1);
                    w[mid] = (sbyte)mp;
                }
            }
            if (!any && taken.Count > 0) Record(path, taken, outL);
        }

        /// <summary>Coups permis : les prises sont obligatoires, et on prend toute la rafle d'un coup.</summary>
        public List<Move> Moves()
        {
            var caps = new List<Move>();
            var simple = new List<Move>();
            var work = (sbyte[])b.Clone();
            var path = new List<int>();
            var taken = new List<int>();
            for (int sq = 0; sq < 64; sq++)
            {
                int p = b[sq];
                if (p == 0 || (p > 0) != (side > 0)) continue;
                work[sq] = 0;
                path.Clear(); path.Add(sq); taken.Clear();
                Jumps(work, sq, p, path, taken, caps);
                work[sq] = (sbyte)p;
                if (caps.Count > 0) continue;       // plus la peine de chercher les coups simples
                int r = sq >> 3, f = sq & 7;
                bool king = p == 2 || p == -2;
                for (int dr = -1; dr <= 1; dr += 2)
                {
                    if (!king && dr != side) continue;
                    for (int df = -1; df <= 1; df += 2)
                    {
                        int nr = r + dr, nf = f + df;
                        while (In(nr, nf) && b[nr * 8 + nf] == 0)
                        {
                            simple.Add(new Move { path = new[] { sq, nr * 8 + nf }, caps = None });
                            if (!french || !king) break;         // dames francaises : la dame avance d'autant de cases libres qu'elle veut
                            nr += dr; nf += df;
                        }
                    }
                }
            }
            if (french && caps.Count > 1)
            {
                int max = 0;
                foreach (var m in caps) if (m.caps.Length > max) max = m.caps.Length;
                caps.RemoveAll(m => m.caps.Length < max);       // prise obligatoire, et la plus grosse (regle de la majorite)
            }
            return caps.Count > 0 ? caps : simple;
        }

        public void Apply(Move m)
        {
            int from = m.path[0], to = m.path[m.path.Length - 1];
            int p = b[from];
            b[from] = 0;
            for (int i = 0; i < m.caps.Length; i++) b[m.caps[i]] = 0;
            bool man = p == 1 || p == -1;
            int r = to >> 3;
            if (p == 1 && r == 7) p = 2;
            else if (p == -1 && r == 0) p = -2;
            b[to] = (sbyte)p;
            quiet = (man || m.caps.Length > 0) ? 0 : quiet + 1;
            side = -side;
        }

        /// <summary>0 en cours, 1 le camp au trait a perdu (plus de coup), 2 nulle (40 coups sans prise ni pion).</summary>
        public int Status()
        {
            if (Moves().Count == 0) return 1;
            return quiet >= 80 ? 2 : 0;
        }
    }

    public sealed class Ar6dofCheckersAi
    {
        const int Mate = 100000;
        readonly Ar6dofCheckers root;
        readonly Stopwatch sw = new Stopwatch();
        readonly Random rnd;
        int nodes, limit;
        bool stopped;
        public volatile bool cancel;

        public Ar6dofCheckersAi(Ar6dofCheckers c, int seed) { root = c.Clone(); rnd = new Random(seed); }

        static int Eval(Ar6dofCheckers c)
        {
            int sc = 0;
            for (int sq = 0; sq < 64; sq++)
            {
                int p = c.b[sq];
                if (p == 0) continue;
                int r = sq >> 3, f = sq & 7, v;
                if (p == 1) v = 100 + r * 4 + (r == 0 ? 8 : 0);
                else if (p == -1) v = 100 + (7 - r) * 4 + (r == 7 ? 8 : 0);
                else v = (c.french ? 300 : 160) + ((f >= 2 && f <= 5 && r >= 2 && r <= 5) ? 6 : 0);
                sc += p > 0 ? v : -v;
            }
            return c.side == 1 ? sc : -sc;
        }

        bool TimeUp()
        {
            if ((++nodes & 255) == 0 && (cancel || sw.ElapsedMilliseconds > limit)) stopped = true;
            return stopped;
        }

        int Search(Ar6dofCheckers c, int depth, int alpha, int beta, int ply)
        {
            if (TimeUp()) return 0;
            var moves = c.Moves();
            if (moves.Count == 0) return -Mate + ply;
            if (c.quiet >= 80) return 0;
            if (depth <= 0)
            {
                if (moves[0].caps.Length == 0 || ply >= 24) return Eval(c);   // une prise en cours : on la joue jusqu'au bout avant d'evaluer
            }
            int best = -2 * Mate;
            for (int i = 0; i < moves.Count; i++)
            {
                var n = c.Clone();
                n.Apply(moves[i]);
                int sc = -Search(n, depth - 1, -beta, -alpha, ply + 1);
                if (stopped) return 0;
                if (sc > best) best = sc;
                if (sc > alpha) alpha = sc;
                if (alpha >= beta) break;
            }
            return best;
        }

        /// <summary>Le meilleur coup (null s'il n'y en a pas, ou si on a annule). Niveau 0 facile, 1 moyen, 2 fort.</summary>
        public Ar6dofCheckers.Move Think(int level)
        {
            var moves = root.Moves();
            if (moves.Count == 0) return null;
            if (moves.Count == 1) return moves[0];
            sw.Reset(); sw.Start();
            stopped = false; nodes = 0;
            limit = level >= 2 ? 2200 : 1500;
            if (level <= 0)
            {
                var sc = new int[moves.Count];
                int bestSc = -2 * Mate;
                for (int i = 0; i < moves.Count; i++)
                {
                    var n = root.Clone();
                    n.Apply(moves[i]);
                    sc[i] = -Search(n, 1, -2 * Mate, 2 * Mate, 1);
                    if (sc[i] > bestSc) bestSc = sc[i];
                }
                if (cancel) return null;
                var pick = new List<Ar6dofCheckers.Move>();
                for (int i = 0; i < moves.Count; i++) if (sc[i] >= bestSc - 60) pick.Add(moves[i]);
                return pick[rnd.Next(pick.Count)];
            }
            int maxDepth = level == 1 ? 5 : 14;
            Ar6dofCheckers.Move bestMove = moves[0];
            for (int d = 1; d <= maxDepth; d++)
            {
                int alpha = -2 * Mate, beta = 2 * Mate, dBest = -2 * Mate, dmIdx = 0;
                for (int i = 0; i < moves.Count; i++)
                {
                    var n = root.Clone();
                    n.Apply(moves[i]);
                    int sc = -Search(n, d - 1, -beta, -alpha, 1);
                    if (stopped) break;
                    if (sc > dBest) { dBest = sc; dmIdx = i; }
                    if (sc > alpha) alpha = sc;
                }
                if (stopped) break;
                bestMove = moves[dmIdx];
                var first = moves[dmIdx];
                moves.RemoveAt(dmIdx);
                moves.Insert(0, first);
                if (dBest > Mate - 100 || dBest < -Mate + 100) break;
            }
            return cancel ? null : bestMove;
        }
    }
}
