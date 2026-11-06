using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Les des en 3D : cube a points (d6), piece de monnaie, d4 / d8 / d10 / d12 / d20 / d100 en polyedres. Ils sont lances sur un tapis (culbute, rebonds), se posent a plat
    /// et on les relance en touchant l'un d'eux au laser. La valeur des des autres que le d6 flotte au-dessus du de.
    /// </summary>
    public sealed class Ar6dofDice3D
    {
        static readonly string[] Pips = { "000010000", "100000001", "100010001", "101000101", "101010101", "101101101" };
        const float Side = 0.06f, CoinR = 0.036f, CoinT = 0.009f;

        readonly Ar6dofTable3D t;
        readonly Action tap;
        readonly System.Random rnd = new System.Random();
        readonly Transform[] die = new Transform[6];
        readonly Ar6dofLabel3D[] lab = new Ar6dofLabel3D[6];
        readonly Vector3[] rest = new Vector3[6], startP = new Vector3[6], axis = new Vector3[6];
        readonly Quaternion[] rot = new Quaternion[6];
        readonly float[] spin = new float[6];
        readonly int[] shown = new int[6];
        Material pipMat;
        int sides, count;
        Color tint;
        bool anim;
        float t0, dur = 1f;

        public Ar6dofDice3D(Ar6dofTable3D table, Action onTap)
        {
            t = table; tap = onTap;
            t.AddTick(Tick);
        }

        public bool Rolling { get { return anim; } }

        static Texture2D MakePips()
        {
            const int S = 96;
            var tex = new Texture2D(S * 3, S * 2, TextureFormat.RGBA32, false);
            var px = new Color32[S * 3 * S * 2];
            var face = new Color32(244, 242, 235, 255); var edge = new Color32(200, 195, 185, 255); var ink = new Color32(28, 28, 38, 255);
            for (int v = 0; v < 6; v++)
            {
                int cx0 = (v % 3) * S, cy0 = (v / 3) * S;
                string mask = Pips[v];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        Color32 c = (x < 3 || y < 3 || x >= S - 3 || y >= S - 3) ? edge : face;
                        for (int k = 0; k < 9; k++)
                        {
                            if (mask[k] != '1') continue;
                            float dx = x - S * (0.27f + 0.23f * (k % 3)), dy = y - S * (0.27f + 0.23f * (k / 3));
                            if (dx * dx + dy * dy < S * S * 0.011f) { c = ink; break; }
                        }
                        px[(cy0 + y) * S * 3 + cx0 + x] = c;
                    }
            }
            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false);
            return tex;
        }

        static float Rad(int s) { return s == 4 ? 0.05f : (s == 20 ? 0.047f : 0.04f); }

        /// <summary>Quaternion qui pose le de a plat avec la valeur v vers le haut (une face quelconque pour les polyedres).</summary>
        Quaternion RestRot(int i, int v, out float restY)
        {
            Quaternion yaw = Quaternion.AngleAxis(rnd.Next(0, 360), Vector3.up);
            if (sides == 6) { restY = Side * 0.5f; return yaw * Quaternion.FromToRotation(Ar6dofShapes.CubeN[Mathf.Clamp(v, 1, 6) - 1], Vector3.up); }
            if (sides == 2) { restY = CoinT * 0.5f; return yaw * (v == 1 ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f)); }
            var p = Ar6dofShapes.Poly(sides == 100 ? 10 : sides);
            int f = rnd.Next(p.normals.Length);
            restY = p.dist[f] * Rad(sides);
            return yaw * Quaternion.FromToRotation(p.normals[f], Vector3.up);
        }

        void RestPos(int i, float y)
        {
            int row = i / 3, inRow = row == 0 ? Mathf.Min(3, count) : count - 3;
            float x = (i % 3 - (inRow - 1) * 0.5f) * 0.15f;
            float z = count <= 3 ? 0f : (row == 0 ? 0.065f : -0.065f);
            rest[i] = new Vector3(x, y, z);
        }

        /// <summary>(Re)construit les des au repos avec leurs valeurs.</summary>
        public void Setup(int sidesNow, int countNow, int[] vals, Color tintNow)
        {
            Clear();
            sides = sidesNow; count = countNow; tint = tintNow;
            t.SetSlab(0f, 0f, 0.56f, 0.32f);
            for (int i = 0; i < count; i++)
            {
                float y;
                int v = Mathf.Clamp(vals[i], 1, sides);
                rot[i] = RestRot(i, v, out y);
                RestPos(i, y);
                Make(i);
                shown[i] = v;
                Put(i, rest[i], rot[i]);
                ShowLabel(i, true);
            }
        }

        void Clear()
        {
            anim = false;
            for (int i = 0; i < 6; i++)
            {
                if (die[i] != null) UnityEngine.Object.Destroy(die[i].gameObject);
                if (lab[i] != null && lab[i].tr != null) UnityEngine.Object.Destroy(lab[i].tr.gameObject);
                die[i] = null; lab[i] = null;
            }
        }

        void Make(int i)
        {
            var g = new GameObject("De");
            g.transform.SetParent(t.content, false);
            Renderer main = null;
            if (sides == 6)
            {
                if (pipMat == null) pipMat = t.NewMat(t.Own(MakePips()));
                var v = t.Part(g.transform, "Cube", Ar6dofShapes.Cube(), pipMat, Vector3.zero, Vector3.one * Side, tint);
                main = v.GetComponent<Renderer>();
                g.AddComponent<BoxCollider>().size = Vector3.one * Side;
            }
            else if (sides == 2)
            {
                var rim = t.Part(g.transform, "Tranche", Ar6dofShapes.Disc(true), null, Vector3.zero, new Vector3(CoinR, CoinT, CoinR), new Color(0.72f, 0.55f, 0.12f));
                t.Part(g.transform, "Pile", Ar6dofShapes.Disc(false), null, new Vector3(0f, CoinT * 0.5f + 0.0002f, 0f), new Vector3(CoinR, 1f, CoinR), new Color(0.98f, 0.8f, 0.25f));
                t.Part(g.transform, "Face", Ar6dofShapes.Disc(false), null, new Vector3(0f, -CoinT * 0.5f - 0.0002f, 0f), new Vector3(CoinR, 1f, CoinR), new Color(0.82f, 0.84f, 0.88f));
                main = rim.GetComponent<Renderer>();
                var bc = g.AddComponent<BoxCollider>();
                bc.size = new Vector3(CoinR * 2f, CoinT * 3f, CoinR * 2f);
            }
            else
            {
                float r = Rad(sides);
                var p = Ar6dofShapes.Poly(sides == 100 ? 10 : sides);
                var v = t.Part(g.transform, "Poly", p.mesh, null, Vector3.zero, Vector3.one * r, tint);
                main = v.GetComponent<Renderer>();
                g.AddComponent<SphereCollider>().radius = r * 0.85f;
            }
            var tt = g.AddComponent<Ar6dofTapTarget>();
            tt.action = tap; tt.glowRend = main; tt.table = t; tt.baseColor = tint;
            die[i] = g.transform;
            lab[i] = t.AddLabel(null, Vector3.zero, 0.07f, 64, Color.white, true);
            lab[i].Show(false);
        }

        void Put(int i, Vector3 p, Quaternion q)
        {
            die[i].localPosition = p;
            die[i].localRotation = q;
        }

        string LabelText(int v)
        {
            if (sides == 6) return "";
            if (sides == 2) return Ar6dofLoc.T(v == 1 ? "Pile" : "Face");
            return v.ToString();
        }

        void ShowLabel(int i, bool on)
        {
            if (lab[i] == null) return;
            string s = on ? LabelText(shown[i]) : "";
            lab[i].Show(s.Length > 0);
            if (s.Length == 0) return;
            float top = sides == 6 ? Side : (sides == 2 ? CoinT : Rad(sides) * 1.2f);
            lab[i].tr.localPosition = new Vector3(rest[i].x, top + 0.05f, rest[i].z);
            lab[i].Set(s, new Color(1f, 0.95f, 0.6f));
        }

        /// <summary>Lance : chaque de culbute puis se pose sur la valeur donnee.</summary>
        public void Roll(int[] fin, float now, float duration)
        {
            if (die[0] == null && count > 0) return;
            t0 = now; dur = duration; anim = true;
            for (int i = 0; i < count; i++)
            {
                float y;
                int v = Mathf.Clamp(fin[i], 1, sides);
                rot[i] = RestRot(i, v, out y);
                RestPos(i, y);
                shown[i] = v;
                startP[i] = new Vector3(rest[i].x * 0.4f + (float)(rnd.NextDouble() - 0.5) * 0.1f, y, -0.2f);
                axis[i] = UnityEngine.Random.onUnitSphere;
                spin[i] = 360f * (2 + rnd.Next(0, 3)) * (rnd.Next(2) == 0 ? 1f : -1f);
                ShowLabel(i, false);
            }
        }

        void Tick(float now)
        {
            if (!anim) return;
            float u = Mathf.Clamp01((now - t0) / dur);
            for (int i = 0; i < count; i++)
            {
                if (die[i] == null) continue;
                float e = 1f - (1f - u) * (1f - u);
                Vector3 p = Vector3.Lerp(startP[i], rest[i], e);
                p.y = rest[i].y + 0.17f * (1f - u) * Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2.5f + 0.9f));
                float k = (1f - u) * (1f - u);
                Put(i, p, Quaternion.AngleAxis(spin[i] * k, axis[i]) * rot[i]);
            }
            if (u >= 1f)
            {
                anim = false;
                for (int i = 0; i < count; i++) { if (die[i] != null) Put(i, rest[i], rot[i]); ShowLabel(i, true); }
            }
        }
    }
}
