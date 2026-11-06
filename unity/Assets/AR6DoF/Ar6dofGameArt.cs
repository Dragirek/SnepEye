using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Dessin des jeux sans aucune image ni police a installer : chaque dessin est une courte description de formes (cercles, rectangles arrondis,
    /// polygones...) transformee en texture une seule fois, a la premiere utilisation. Repere 64 x 64, y vers le haut.
    /// Formes : c cx cy r | e cx cy rx ry | r cx cy demiL demiH rayon | t x1 y1 x2 y2 x3 y3 | s x1 y1 x2 y2 demiEpaisseur | o cx cy r demiEpaisseur (anneau) | p x1 y1 x2 y2 ... (polygone).
    /// '#rrggbb' change la couleur ; un '-' devant une forme la gomme. Aucun cout tant qu'aucun jeu n'est ouvert.
    /// </summary>
    public static class Ar6dofGameArt
    {
        // ---------------------------------------------------------------- descriptions
        static readonly string[] PieceSpec =
        {
            /* 0 pion   */ "#FILL; c 32 43 9.5; p 24 15 40 15 36 33 28 33; e 32 33 8 3.5; r 32 14 16 4.5 3",
            /* 1 tour   */ "#FILL; r 32 14 17 4.5 3; p 22 18 42 18 39 44 25 44; r 32 48 16 6 2; -r 24 56 3.2 4 0; -r 32 56 3.2 4 0; -r 40 56 3.2 4 0",
            /* 2 cavalier */ "#FILL; r 32 14 18 4.5 3; p 20 18 46 18 46 34 42 46 34 54 30 58 26 52 12 38 14 33 24 35 30 30 22 24; -c 31 47 2.2",
            /* 3 fou    */ "#FILL; r 32 14 17 4.5 3; p 24 18 40 18 37 34 27 34; e 32 42 10 15; c 32 59 4; -s 33 44 38 51 1.7",
            /* 4 dame   */ "#FILL; r 32 14 18 4.5 3; p 22 18 42 18 49 46 40 33 32 50 24 33 15 46; c 14 49 4.5; c 24 55 4; c 32 58 4; c 40 55 4; c 50 49 4.5; e 32 34 14 4",
            /* 5 roi    */ "#FILL; r 32 14 18 4.5 3; p 22 18 42 18 46 38 40 48 24 48 18 38; s 32 50 32 62 2.8; s 26 57 38 57 2.8; r 32 46 12 3 1.5",
        };

        static readonly string[] CheckSpec =
        {
            /* pion  */ "#FILL; c 32 32 27; #RING; o 32 32 18 2.3; o 32 32 10 1.8",
            /* dame  */ "#FILL; c 32 32 27; #RING; o 32 32 18 2.3; #ffc61a; p 16 22 18 44 27 34 32 48 37 34 46 44 48 22; #b57a00; s 18 22 46 22 1.6",
        };

        /// <summary>Les six symboles de la machine a sous : cerise, citron, cloche, etoile, diamant, sept.</summary>
        static readonly string[] SlotSpec =
        {
            "#3f7a1f; s 40 54 33 28 1.8; s 40 54 22 26 1.8; #4fae2c; e 47 55 9 4; #c4122c; c 33 22 11; c 20 22 11; #ff7088; c 29 26 3; c 16 26 3",
            "#f6d416; e 32 32 24 17; t 6 32 14 23 14 41; t 58 32 50 23 50 41; #fff8b0; e 24 40 7 3.5",
            "#f2b01e; c 32 36 17; r 32 22 24 8 3; #d98a0c; c 32 8 5; #ffe48a; e 24 42 4 8",
            "#ffcc1a; p 32 60 39 41 58 41 43 29 49 9 32 21 15 9 21 29 6 41 25 41; #fff2a0; c 26 40 3",
            "#2aa3ff; p 18 54 46 54 58 38 32 8 6 38; #8fd4ff; p 18 54 32 38 46 54; p 6 38 32 38 32 8; #e8f8ff; s 16 44 24 50 1.5",
            "#e5192e; p 14 56 52 56 52 48 32 6 21 6 38 46 14 46; #ff8590; s 18 51 46 51 2",
        };
        public const int SlotSymbols = 6;

        /// <summary>Icones d'une couleur (blanc, teintees par l'interface) pour la barre et le Hub.</summary>
        static readonly Dictionary<string, string> IconSpec = new Dictionary<string, string>
        {
            { "dice",      "#fff; r 32 32 23 23 7; -c 21 43 4; -c 43 43 4; -c 32 32 4; -c 21 21 4; -c 43 21 4" },
            { "slots",     "#fff; r 29 32 22 25 6; -r 17 38 4.5 9 1.5; -r 29 38 4.5 9 1.5; -r 41 38 4.5 9 1.5; -r 29 15 11 3 1; s 55 28 55 46 2.6; c 55 49 5" },
            { "tictactoe", "#fff; s 24 6 24 58 1.8; s 40 6 40 58 1.8; s 6 24 58 24 1.8; s 6 40 58 40 1.8; o 14 50 6 2.3; s 26 26 38 38 2.3; s 38 26 26 38 2.3; s 44 12 56 24 2.3; s 56 12 44 24 2.3" },
            { "ship",      "#fff; p 6 30 58 30 49 17 15 17; r 32 37 12 6 1.5; r 32 47 5 4 1; s 32 50 32 58 1.8; s 22 33 22 42 1.8; -s 14 24 50 24 1" },
            { "chess",     "#fff; r 32 14 18 4.5 3; p 20 18 46 18 46 34 42 46 34 54 30 58 26 52 12 38 14 33 24 35 30 30 22 24; -c 31 47 2.2" },
            { "checkers",  "#fff; c 32 32 26; -o 32 32 17 2.2; -c 32 32 9" },
        };

        public static bool HasIcon(string name) { return IconSpec.ContainsKey(name); }

        public static Texture2D Icon(string name)
        {
            string spec;
            if (!IconSpec.TryGetValue(name, out spec)) return null;
            return Render(spec, 64, "ffffff", "", 0f, Color.black);
        }

        // ---------------------------------------------------------------- sprites (crees une fois, a la demande)
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        static Sprite Cached(string key, Func<Sprite> make)
        {
            Sprite s;
            if (sprites.TryGetValue(key, out s) && s != null) return s;
            s = make();
            sprites[key] = s;
            return s;
        }

        static Sprite ToSprite(Texture2D t, Vector4 border)
        {
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        /// <summary>Piece d'echecs : 0 pion, 1 tour, 2 cavalier, 3 fou, 4 dame, 5 roi.</summary>
        public static Sprite Piece(int kind, bool white)
        {
            kind = Mathf.Clamp(kind, 0, 5);
            return Cached("p" + kind + (white ? "w" : "b"), () => ToSprite(white
                ? Render(PieceSpec[kind], 96, "f4f4f4", "", 2f, new Color(0.10f, 0.10f, 0.12f))
                : Render(PieceSpec[kind], 96, "2b2b30", "", 2f, new Color(0.85f, 0.85f, 0.88f)), Vector4.zero));
        }

        /// <summary>Pion ou dame du jeu de dames.</summary>
        public static Sprite Checker(bool king, bool white)
        {
            return Cached("c" + (king ? "k" : "m") + (white ? "w" : "b"), () => ToSprite(white
                ? Render(CheckSpec[king ? 1 : 0], 96, "f4f4f4", "c9c9c9", 2f, new Color(0.10f, 0.10f, 0.12f))
                : Render(CheckSpec[king ? 1 : 0], 96, "2b2b30", "55555d", 2f, new Color(0.85f, 0.85f, 0.88f)), Vector4.zero));
        }

        /// <summary>Symbole de la machine a sous (0..5), avec un trait sombre autour.</summary>
        public static Sprite Slot(int i)
        {
            i = Mathf.Clamp(i, 0, SlotSymbols - 1);
            return Cached("s" + i, () => ToSprite(Render(SlotSpec[i], 128, "ffffff", "", 3f, new Color(0.25f, 0.10f, 0.05f)), Vector4.zero));
        }

        /// <summary>Rectangle arrondi blanc a bords etirables (Image de type Sliced) : on le teinte avec la couleur de l'Image.</summary>
        public static Sprite Round()
        {
            return Cached("round", () => ToSprite(Render("#fff; r 32 32 32 32 20", 64, "ffffff", "", 0f, Color.white), new Vector4(22, 22, 22, 22)));
        }

        /// <summary>Disque blanc.</summary>
        public static Sprite Disc()
        {
            return Cached("disc", () => ToSprite(Render("#fff; c 32 32 31", 64, "ffffff", "", 0f, Color.white), Vector4.zero));
        }

        /// <summary>Dessin libre (autres applications) : memes formes, mis en cache sous 'key'.</summary>
        public static Sprite FromSpec(string key, string spec, int n, float outline, Color outlineColor)
        {
            return Cached("x" + key, () => ToSprite(Render(spec, n, "ffffff", "", outline, outlineColor), Vector4.zero));
        }

        // ---------------------------------------------------------------- rendu
        sealed class Op
        {
            public char k;
            public float[] a;
            public bool cut;
            public Color col;
        }

        static List<Op> Parse(string spec, string fill, string ring)
        {
            var ops = new List<Op>();
            Color col = Color.white;
            string s = spec.Replace("FILL", fill);
            if (!string.IsNullOrEmpty(ring)) s = s.Replace("RING", ring);
            var toks = s.Split(';');
            for (int i = 0; i < toks.Length; i++)
            {
                string tok = toks[i].Trim();
                if (tok.Length == 0) continue;
                if (tok[0] == '#')
                {
                    Color c;
                    if (ColorUtility.TryParseHtmlString(tok, out c)) col = c;
                    continue;
                }
                bool cut = tok[0] == '-';
                if (cut) tok = tok.Substring(1).Trim();
                var p = tok.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                var o = new Op { k = p[0][0], cut = cut, col = col, a = new float[p.Length - 1] };
                for (int j = 1; j < p.Length; j++) o.a[j - 1] = float.Parse(p[j], CultureInfo.InvariantCulture);
                ops.Add(o);
            }
            return ops;
        }

        static float Hyp(float x, float y) { return Mathf.Sqrt(x * x + y * y); }

        static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float l = vx * vx + vy * vy;
            float t = l > 0f ? Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / l) : 0f;
            return Hyp(px - (ax + vx * t), py - (ay + vy * t));
        }

        static float Dist(Op o, float px, float py)
        {
            var a = o.a;
            switch (o.k)
            {
                case 'c': return Hyp(px - a[0], py - a[1]) - a[2];
                case 'e': return (Hyp((px - a[0]) / a[2], (py - a[1]) / a[3]) - 1f) * Mathf.Min(a[2], a[3]);
                case 'r':
                {
                    float qx = Mathf.Abs(px - a[0]) - (a[2] - a[4]), qy = Mathf.Abs(py - a[1]) - (a[3] - a[4]);
                    return Hyp(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - a[4];
                }
                case 's': return Seg(px, py, a[0], a[1], a[2], a[3]) - a[4];
                case 'o': return Mathf.Abs(Hyp(px - a[0], py - a[1]) - a[2]) - a[3];
                default:
                {
                    // polygone (triangle compris) : distance au contour, negative a l'interieur
                    int n = a.Length / 2;
                    float md = 1e9f;
                    bool inside = false;
                    for (int i = 0; i < n; i++)
                    {
                        float ax = a[2 * i], ay = a[2 * i + 1];
                        int j = (i + 1) % n;
                        float bx = a[2 * j], by = a[2 * j + 1];
                        md = Mathf.Min(md, Seg(px, py, ax, ay, bx, by));
                        if ((ay > py) != (by > py) && px < (bx - ax) * (py - ay) / (by - ay) + ax) inside = !inside;
                    }
                    return inside ? -md : md;
                }
            }
        }

        static Texture2D Render(string spec, int n, string fillHex, string ringHex, float outline, Color outlineColor)
        {
            var ops = Parse(spec, fillHex, ringHex);
            float s = n / 64f, inv = 1f / s;
            var pr = new float[n * n * 3];
            var al = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float px = (x + 0.5f) * inv, py = (y + 0.5f) * inv;
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int i = 0; i < ops.Count; i++)
                    {
                        var o = ops[i];
                        float cov = Mathf.Clamp01(0.5f - Dist(o, px, py) * s);
                        if (cov <= 0f) continue;
                        float k = 1f - cov;
                        if (o.cut) { r *= k; g *= k; b *= k; a *= k; }
                        else { r = r * k + o.col.r * cov; g = g * k + o.col.g * cov; b = b * k + o.col.b * cov; a = a * k + cov; }
                    }
                    int idx = y * n + x;
                    pr[idx * 3] = r; pr[idx * 3 + 1] = g; pr[idx * 3 + 2] = b; al[idx] = a;
                }

            Color bleed = ops.Count > 0 ? ops[0].col : Color.white;
            if (outline > 0f) bleed = outlineColor;
            var res = new Color32[n * n];
            int rad = Mathf.CeilToInt(outline);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int idx = y * n + x;
                    float r = pr[idx * 3], g = pr[idx * 3 + 1], b = pr[idx * 3 + 2], a = al[idx];
                    if (outline > 0f && a < 1f)
                    {
                        float oa = 0f;
                        for (int dy = -rad; dy <= rad; dy++)
                            for (int dx = -rad; dx <= rad; dx++)
                            {
                                if (dx * dx + dy * dy > outline * outline + 0.5f) continue;
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                                oa = Mathf.Max(oa, al[yy * n + xx]);
                            }
                        float w = (1f - a) * oa;
                        r += outlineColor.r * w; g += outlineColor.g * w; b += outlineColor.b * w;
                        a += w;
                    }
                    if (a < 0.002f) { res[idx] = new Color32((byte)(bleed.r * 255f), (byte)(bleed.g * 255f), (byte)(bleed.b * 255f), 0); continue; }
                    res[idx] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(r / a) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(g / a) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(b / a) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels32(res);
            tex.Apply(true, false);
            return tex;
        }
    }
}
