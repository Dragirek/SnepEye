using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Petit constructeur de maillages pour les jeux 3D (boites, tours de potier, spheres, tores, polyedres). Le shader est double face : le sens des triangles n'a pas d'importance.</summary>
    public sealed class Ar6dofMeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public int VertexCount { get { return v.Count; } }

        int Add(Vector3 p, Vector3 nr, Vector2 u) { v.Add(p); n.Add(nr); uv.Add(u); return v.Count - 1; }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nr, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = Add(a, nr, ua); Add(b, nr, ub); Add(c, nr, uc); Add(d, nr, ud);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 nr)
        {
            int i = Add(a, nr, Vector2.zero); Add(b, nr, Vector2.zero); Add(c, nr, Vector2.zero);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rot)
        {
            Vector3 h = size * 0.5f;
            for (int axis = 0; axis < 3; axis++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 nr = Vector3.zero; nr[axis] = sgn;
                    Vector3 a = Vector3.zero, b = Vector3.zero;
                    a[(axis + 1) % 3] = h[(axis + 1) % 3];
                    b[(axis + 2) % 3] = h[(axis + 2) % 3];
                    Vector3 c = nr; c[axis] = sgn * h[axis];
                    Vector3 p0 = center + rot * (c - a - b), p1 = center + rot * (c + a - b), p2 = center + rot * (c + a + b), p3 = center + rot * (c - a + b);
                    Quad(p0, p1, p2, p3, rot * nr, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                }
        }

        public void Box(Vector3 center, Vector3 size) { Box(center, size, Quaternion.identity); }

        /// <summary>Solide de revolution autour de l'axe Y. 'prof' : paires (rayon, hauteur), du bas vers le haut.</summary>
        public void Lathe(float[] prof, int seg, Vector3 offset)
        {
            int m = prof.Length / 2;
            int baseI = v.Count;
            for (int i = 0; i < m; i++)
            {
                float r = prof[2 * i], y = prof[2 * i + 1];
                int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(m - 1, i + 1);
                float dr = prof[2 * i1] - prof[2 * i0], dy = prof[2 * i1 + 1] - prof[2 * i0 + 1];
                Vector2 nn = new Vector2(dy, -dr);
                if (nn.sqrMagnitude < 1e-12f) nn = Vector2.up; else nn.Normalize();
                for (int j = 0; j <= seg; j++)
                {
                    float a = j * 2f * Mathf.PI / seg;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    Add(offset + new Vector3(r * ca, y, r * sa), new Vector3(nn.x * ca, nn.y, nn.x * sa), new Vector2((float)j / seg, m > 1 ? (float)i / (m - 1) : 0f));
                }
            }
            for (int i = 0; i < m - 1; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a = baseI + i * (seg + 1) + j, b = a + 1, c = a + (seg + 1), d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
        }

        public void Sphere(Vector3 center, float r, int lat = 7, int seg = 12)
        {
            var p = new float[(lat + 1) * 2];
            for (int i = 0; i <= lat; i++)
            {
                float a = Mathf.PI * i / lat;
                p[2 * i] = r * Mathf.Sin(a);
                p[2 * i + 1] = -r * Mathf.Cos(a);
            }
            Lathe(p, seg, center);
        }

        /// <summary>Tore couche a plat (axe Y).</summary>
        public void Torus(Vector3 center, float R, float r, int seg = 24, int tube = 8)
        {
            int baseI = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * 2f * Mathf.PI / seg;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= tube; j++)
                {
                    float b = j * 2f * Mathf.PI / tube;
                    Vector3 nr = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    Add(center + dir * R + nr * r, nr, new Vector2((float)i / seg, (float)j / tube));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < tube; j++)
                {
                    int a = baseI + i * (tube + 1) + j, b = a + 1, c = a + (tube + 1), d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
        }

        /// <summary>Face plate d'un polyedre convexe (sommets dans l'ordre autour de la face) : ombrage plat.</summary>
        public Vector3 Face(Vector3[] pts)
        {
            Vector3 c = Vector3.zero;
            for (int i = 0; i < pts.Length; i++) c += pts[i];
            c /= pts.Length;
            Vector3 nr = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]).normalized;
            if (Vector3.Dot(nr, c) < 0f) nr = -nr;
            for (int i = 1; i < pts.Length - 1; i++) Tri(pts[0], pts[i], pts[i + 1], nr);
            return nr;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Un polyedre pour les des : maillage, normale et distance au centre de chaque face (pour poser le de a plat).</summary>
    public sealed class Ar6dofPoly
    {
        public Mesh mesh;
        public Vector3[] normals;
        public float[] dist;
    }

    /// <summary>Les formes des jeux 3D : pieces (echecs, dames, morpion, bateaux) et des. Tout est construit a la demande et garde en cache.</summary>
    public static class Ar6dofShapes
    {
        // jetons des pieces : 100 + type * 2 + blanc (echecs : 0 pion, 1 tour, 2 cavalier, 3 fou, 4 dame, 5 roi) ; 200 + 2 * dame + blanc (dames) ; 300 X ; 301 O ; 400 a 402 bateaux
        public const int TokX = 300, TokO = 301, TokShip = 400, TokShipHit = 401, TokShipSunk = 402;
        public static int Chess(int kind, bool white) { return 100 + kind * 2 + (white ? 1 : 0); }
        public static int Checker(bool king, bool white) { return 200 + (king ? 2 : 0) + (white ? 1 : 0); }

        static readonly Dictionary<int, Mesh> cache = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Ar6dofPoly> polys = new Dictionary<int, Ar6dofPoly>();

        /// <summary>Hauteur d'une piece, en cases.</summary>
        public static float Height(int tok)
        {
            if (tok >= 100 && tok < 200)
            {
                switch ((tok - 100) >> 1) { case 0: return 0.6f; case 1: return 0.72f; case 2: return 0.8f; case 3: return 0.88f; case 4: return 0.98f; default: return 1.12f; }
            }
            if (tok >= 200 && tok < 300) return tok >= 202 ? 0.32f : 0.16f;
            if (tok == TokX || tok == TokO) return 0.1f;
            if (tok >= 400) return 0.2f;
            return 0.1f;
        }

        public static Color ColorOf(int tok)
        {
            bool white = (tok & 1) == 1;
            if ((tok >= 100 && tok < 300)) return white ? new Color(0.95f, 0.92f, 0.85f) : new Color(0.16f, 0.16f, 0.2f);
            if (tok == TokX) return new Color(1f, 0.45f, 0.4f);
            if (tok == TokO) return new Color(0.5f, 0.78f, 1f);
            if (tok == TokShip) return new Color(0.58f, 0.62f, 0.7f);
            if (tok == TokShipHit) return new Color(0.9f, 0.32f, 0.2f);
            return new Color(0.4f, 0.1f, 0.1f);
        }

        public static Mesh Get(int tok)
        {
            Mesh m;
            if (cache.TryGetValue(tok, out m) && m != null) return m;
            m = Build(tok);
            cache[tok] = m;
            return m;
        }

        static readonly float[] Base = { 0f, 0f, 0.34f, 0f, 0.34f, 0.05f, 0.27f, 0.1f };

        static float[] Join(float[] a, params float[] b)
        {
            var r = new float[a.Length + b.Length];
            Array.Copy(a, r, a.Length); Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }

        static Mesh Build(int tok)
        {
            var b = new Ar6dofMeshBuilder();
            string name = "AR6DoF_Piece" + tok;
            if (tok >= 100 && tok < 200)
            {
                switch ((tok - 100) >> 1)
                {
                    case 0:   // pion
                        b.Lathe(Join(Base, 0.16f, 0.16f, 0.1f, 0.3f, 0.2f, 0.34f, 0.2f, 0.37f, 0.08f, 0.38f), 14, Vector3.zero);
                        b.Sphere(new Vector3(0f, 0.46f, 0f), 0.14f);
                        break;
                    case 1:   // tour
                        b.Lathe(Join(Base, 0.2f, 0.18f, 0.2f, 0.46f, 0.29f, 0.5f, 0.29f, 0.62f, 0.15f, 0.62f, 0f, 0.62f), 16, Vector3.zero);
                        for (int k = 0; k < 4; k++)
                        {
                            float a = k * Mathf.PI * 0.5f;
                            b.Box(new Vector3(Mathf.Cos(a) * 0.2f, 0.67f, Mathf.Sin(a) * 0.2f), new Vector3(0.13f, 0.1f, 0.13f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
                        }
                        break;
                    case 2:   // cavalier : tourne vers +z
                        b.Lathe(Join(Base, 0.27f, 0.12f), 14, Vector3.zero);
                        b.Box(new Vector3(0f, 0.32f, -0.02f), new Vector3(0.24f, 0.4f, 0.3f));
                        b.Box(new Vector3(0f, 0.56f, 0.1f), new Vector3(0.2f, 0.2f, 0.38f), Quaternion.Euler(25f, 0f, 0f));
                        b.Box(new Vector3(0f, 0.5f, 0.3f), new Vector3(0.16f, 0.14f, 0.2f), Quaternion.Euler(15f, 0f, 0f));
                        b.Box(new Vector3(-0.06f, 0.72f, -0.02f), new Vector3(0.05f, 0.1f, 0.05f));
                        b.Box(new Vector3(0.06f, 0.72f, -0.02f), new Vector3(0.05f, 0.1f, 0.05f));
                        break;
                    case 3:   // fou
                        b.Lathe(Join(Base, 0.12f, 0.2f, 0.1f, 0.3f, 0.19f, 0.38f, 0.19f, 0.46f, 0.17f, 0.6f, 0.1f, 0.74f, 0f, 0.8f), 16, Vector3.zero);
                        b.Sphere(new Vector3(0f, 0.84f, 0f), 0.06f, 5, 8);
                        break;
                    case 4:   // dame
                        b.Lathe(Join(Base, 0.13f, 0.24f, 0.11f, 0.4f, 0.17f, 0.5f, 0.23f, 0.64f, 0.25f, 0.78f, 0.17f, 0.84f, 0f, 0.84f), 18, Vector3.zero);
                        for (int k = 0; k < 6; k++)
                        {
                            float a = k * Mathf.PI / 3f;
                            b.Sphere(new Vector3(Mathf.Cos(a) * 0.18f, 0.86f, Mathf.Sin(a) * 0.18f), 0.055f, 4, 7);
                        }
                        b.Sphere(new Vector3(0f, 0.92f, 0f), 0.07f, 5, 8);
                        break;
                    default:  // roi
                        b.Lathe(Join(Base, 0.13f, 0.24f, 0.11f, 0.4f, 0.17f, 0.5f, 0.23f, 0.64f, 0.25f, 0.8f, 0.17f, 0.88f, 0f, 0.88f), 18, Vector3.zero);
                        b.Box(new Vector3(0f, 1f, 0f), new Vector3(0.07f, 0.24f, 0.07f));
                        b.Box(new Vector3(0f, 1.03f, 0f), new Vector3(0.2f, 0.07f, 0.07f));
                        break;
                }
            }
            else if (tok >= 200 && tok < 300)
            {
                bool king = tok >= 202;
                b.Lathe(new[] { 0f, 0f, 0.36f, 0f, 0.37f, 0.03f, 0.37f, 0.13f, 0.3f, 0.16f, 0f, 0.16f }, 20, Vector3.zero);
                if (king) b.Lathe(new[] { 0f, 0f, 0.36f, 0f, 0.37f, 0.03f, 0.37f, 0.13f, 0.3f, 0.16f, 0f, 0.16f }, 20, new Vector3(0f, 0.16f, 0f));
            }
            else if (tok == TokX)
            {
                b.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.72f, 0.1f, 0.14f), Quaternion.Euler(0f, 45f, 0f));
                b.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.72f, 0.1f, 0.14f), Quaternion.Euler(0f, -45f, 0f));
            }
            else if (tok == TokO)
            {
                b.Torus(new Vector3(0f, 0.06f, 0f), 0.27f, 0.07f, 28, 8);
            }
            else
            {
                b.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.88f, 0.2f, 0.88f));
            }
            return b.ToMesh(name);
        }

        // ---------------------------------------------------------------- des
        const float Phi = 1.6180339887f;
        static readonly Vector3[] IcoV =
        {
            new Vector3(-1, Phi, 0), new Vector3(1, Phi, 0), new Vector3(-1, -Phi, 0), new Vector3(1, -Phi, 0),
            new Vector3(0, -1, Phi), new Vector3(0, 1, Phi), new Vector3(0, -1, -Phi), new Vector3(0, 1, -Phi),
            new Vector3(Phi, 0, -1), new Vector3(Phi, 0, 1), new Vector3(-Phi, 0, -1), new Vector3(-Phi, 0, 1),
        };
        static readonly int[] IcoF =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        /// <summary>Polyedre d'un de a 'sides' faces (4, 8, 10, 12, 20), de rayon 1.</summary>
        public static Ar6dofPoly Poly(int sides)
        {
            Ar6dofPoly p;
            if (polys.TryGetValue(sides, out p) && p != null && p.mesh != null) return p;
            var faces = new List<Vector3[]>();
            switch (sides)
            {
                case 4:
                    {
                        var q = new[] { new Vector3(1, 1, 1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(-1, -1, 1) };
                        int[,] f = { { 0, 1, 2 }, { 0, 3, 1 }, { 0, 2, 3 }, { 1, 3, 2 } };
                        for (int i = 0; i < 4; i++) faces.Add(new[] { q[f[i, 0]], q[f[i, 1]], q[f[i, 2]] });
                        break;
                    }
                case 8:
                    {
                        Vector3[] ax = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                        for (int sx = 0; sx < 2; sx++) for (int sy = 2; sy < 4; sy++) for (int sz = 4; sz < 6; sz++) faces.Add(new[] { ax[sx], ax[sy], ax[sz] });
                        break;
                    }
                case 10:
                    {
                        float R = 1f, a = 0.105f, h = a * (1f + Mathf.Cos(36f * Mathf.Deg2Rad)) / (1f - Mathf.Cos(36f * Mathf.Deg2Rad));
                        var U = new Vector3[5]; var L = new Vector3[5];
                        for (int k = 0; k < 5; k++)
                        {
                            float au = k * 72f * Mathf.Deg2Rad, al = (k * 72f + 36f) * Mathf.Deg2Rad;
                            U[k] = new Vector3(Mathf.Cos(au) * R, a, Mathf.Sin(au) * R);
                            L[k] = new Vector3(Mathf.Cos(al) * R, -a, Mathf.Sin(al) * R);
                        }
                        var top = new Vector3(0f, h, 0f); var bot = new Vector3(0f, -h, 0f);
                        for (int k = 0; k < 5; k++)
                        {
                            faces.Add(new[] { top, U[k], L[k], U[(k + 1) % 5] });
                            faces.Add(new[] { bot, L[k], U[(k + 1) % 5], L[(k + 1) % 5] });
                        }
                        break;
                    }
                case 12:
                    {
                        int nf = IcoF.Length / 3;
                        var cen = new Vector3[nf];
                        for (int i = 0; i < nf; i++) cen[i] = ((IcoV[IcoF[3 * i]] + IcoV[IcoF[3 * i + 1]] + IcoV[IcoF[3 * i + 2]]) / 3f).normalized;
                        for (int vi = 0; vi < IcoV.Length; vi++)
                        {
                            Vector3 ax = IcoV[vi].normalized;
                            var around = new List<Vector3>();
                            for (int i = 0; i < nf; i++) if (IcoF[3 * i] == vi || IcoF[3 * i + 1] == vi || IcoF[3 * i + 2] == vi) around.Add(cen[i]);
                            Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(ax, u);
                            var keys = new float[around.Count];
                            for (int i = 0; i < keys.Length; i++) keys[i] = Mathf.Atan2(Vector3.Dot(around[i], w), Vector3.Dot(around[i], u));
                            for (int i = 1; i < keys.Length; i++)
                                for (int j = i; j > 0 && keys[j - 1] > keys[j]; j--)
                                {
                                    float tk = keys[j]; keys[j] = keys[j - 1]; keys[j - 1] = tk;
                                    Vector3 tv = around[j]; around[j] = around[j - 1]; around[j - 1] = tv;
                                }
                            faces.Add(around.ToArray());
                        }
                        break;
                    }
                default:
                    {
                        int nf = IcoF.Length / 3;
                        for (int i = 0; i < nf; i++) faces.Add(new[] { IcoV[IcoF[3 * i]].normalized, IcoV[IcoF[3 * i + 1]].normalized, IcoV[IcoF[3 * i + 2]].normalized });
                        break;
                    }
            }
            // rayon 1
            float maxR = 0f;
            for (int i = 0; i < faces.Count; i++) for (int j = 0; j < faces[i].Length; j++) maxR = Mathf.Max(maxR, faces[i][j].magnitude);
            var b = new Ar6dofMeshBuilder();
            p = new Ar6dofPoly { normals = new Vector3[faces.Count], dist = new float[faces.Count] };
            for (int i = 0; i < faces.Count; i++)
            {
                var pts = faces[i];
                for (int j = 0; j < pts.Length; j++) pts[j] /= maxR;
                p.normals[i] = b.Face(pts);
                p.dist[i] = Vector3.Dot(p.normals[i], pts[0]);
            }
            p.mesh = b.ToMesh("AR6DoF_D" + sides);
            polys[sides] = p;
            return p;
        }

        // faces du de a six faces : normale de chaque valeur (1 en haut, 6 en bas, 2 + 5, 3 + 4)
        public static readonly Vector3[] CubeN = { Vector3.up, Vector3.forward, Vector3.right, Vector3.left, Vector3.back, Vector3.down };

        /// <summary>Cube de cote 1, texture 3 x 2 (une case par valeur, 1 a 6).</summary>
        public static Mesh Cube()
        {
            Mesh m;
            if (cache.TryGetValue(-6, out m) && m != null) return m;
            var b = new Ar6dofMeshBuilder();
            for (int val = 0; val < 6; val++)
            {
                Vector3 nr = CubeN[val];
                Vector3 a = Vector3.Cross(nr, Mathf.Abs(nr.y) > 0.9f ? Vector3.right : Vector3.up).normalized * 0.5f;
                Vector3 c = Vector3.Cross(nr, a).normalized * 0.5f;
                Vector3 ctr = nr * 0.5f;
                float u0 = (val % 3) / 3f + 0.01f, u1 = (val % 3 + 1) / 3f - 0.01f, v0 = (val / 3) / 2f + 0.01f, v1 = (val / 3 + 1) / 2f - 0.01f;
                b.Quad(ctr - a - c, ctr + a - c, ctr + a + c, ctr - a + c, nr, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
            }
            m = b.ToMesh("AR6DoF_Cube");
            cache[-6] = m;
            return m;
        }

        /// <summary>Disque plat (axe Y) de rayon 1, pour les pieces de monnaie, a la hauteur 'y'.</summary>
        public static Mesh Disc(bool rim)
        {
            int key = rim ? -3 : -2;
            Mesh m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            var b = new Ar6dofMeshBuilder();
            if (rim) b.Lathe(new[] { 1f, -0.5f, 1f, 0.5f }, 24, Vector3.zero);
            else b.Lathe(new[] { 0f, 0f, 1f, 0f }, 24, Vector3.zero);
            m = b.ToMesh(rim ? "AR6DoF_CoinRim" : "AR6DoF_CoinFace");
            cache[key] = m;
            return m;
        }

        /// <summary>Boite de cote 1 (a mettre a l'echelle).</summary>
        public static Mesh UnitBox()
        {
            Mesh m;
            if (cache.TryGetValue(-1, out m) && m != null) return m;
            var b = new Ar6dofMeshBuilder();
            b.Box(Vector3.zero, Vector3.one);
            m = b.ToMesh("AR6DoF_Box");
            cache[-1] = m;
            return m;
        }

        public static Mesh UnitSphere()
        {
            Mesh m;
            if (cache.TryGetValue(-4, out m) && m != null) return m;
            var b = new Ar6dofMeshBuilder();
            b.Sphere(Vector3.zero, 0.5f, 8, 14);
            m = b.ToMesh("AR6DoF_Sphere");
            cache[-4] = m;
            return m;
        }

        /// <summary>Disque epais de rayon 0.5 et de hauteur 1 (axe Y), pour les marques posees sur les cases.</summary>
        public static Mesh UnitCylinder()
        {
            Mesh m;
            if (cache.TryGetValue(-5, out m) && m != null) return m;
            var b = new Ar6dofMeshBuilder();
            b.Lathe(new[] { 0f, 0f, 0.5f, 0f, 0.5f, 1f, 0f, 1f }, 20, Vector3.zero);
            m = b.ToMesh("AR6DoF_Cyl");
            cache[-5] = m;
            return m;
        }

        public static Mesh UnitRing()
        {
            Mesh m;
            if (cache.TryGetValue(-7, out m) && m != null) return m;
            var b = new Ar6dofMeshBuilder();
            b.Torus(Vector3.zero, 0.45f, 0.05f, 28, 6);
            m = b.ToMesh("AR6DoF_Ring");
            cache[-7] = m;
            return m;
        }
    }
}
