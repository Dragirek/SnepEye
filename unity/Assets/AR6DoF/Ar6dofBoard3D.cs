using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Un plateau de n x n cases en 3D, avec la meme API que le plateau 2D (fond, piece, marque, texte). La case 0 est en haut a gauche vue du joueur (le joueur est du cote z negatif).
    /// Chaque case cliquable porte une colonne de detection dont la hauteur suit la piece posee dessus : viser une piece choisit sa case, pas celle de derriere.
    /// </summary>
    public sealed class Ar6dofBoard3D
    {
        public readonly int n;
        public readonly float cell;
        public bool whiteNear = true;

        readonly Ar6dofTable3D table;
        readonly Transform root;
        readonly Action<int> click;
        readonly Renderer[] tile;
        readonly Color[] tcol;
        readonly bool[] hov;
        readonly BoxCollider[] col;
        readonly Transform[] pc;
        readonly Renderer[] pcR;
        readonly int[] tok;
        readonly Transform[] cellT;
        readonly Transform[] mk;
        readonly Renderer[] mkR;
        readonly Color[] mkC;
        readonly float[] mkS;
        readonly MeshFilter[] mkF;

        public Ar6dofBoard3D(Ar6dofTable3D table, int n, float cell, float cx, float cz, Action<int> click)
        {
            this.table = table; this.n = n; this.cell = cell; this.click = click;
            int total = n * n;
            tile = new Renderer[total]; tcol = new Color[total]; hov = new bool[total]; col = new BoxCollider[total];
            pc = new Transform[total]; pcR = new Renderer[total]; tok = new int[total]; cellT = new Transform[total];
            mk = new Transform[total]; mkR = new Renderer[total]; mkC = new Color[total]; mkS = new float[total]; mkF = new MeshFilter[total];
            root = new GameObject("Plateau").transform;
            root.SetParent(table.content, false);
            root.localPosition = new Vector3(cx, 0f, cz);
            var box = Ar6dofShapes.UnitBox();
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    var g = new GameObject("Case");
                    g.transform.SetParent(root, false);
                    g.transform.localPosition = new Vector3((c - (n - 1) * 0.5f) * cell, 0f, ((n - 1) * 0.5f - r) * cell);
                    cellT[i] = g.transform;
                    var t = table.Part(g.transform, "Dalle", box, null, new Vector3(0f, Ar6dofTable3D.TileH * 0.5f, 0f), new Vector3(cell * 0.97f, Ar6dofTable3D.TileH, cell * 0.97f), Color.gray);
                    tile[i] = t.GetComponent<Renderer>();
                    tcol[i] = Color.gray;
                    if (click != null)
                    {
                        col[i] = g.AddComponent<BoxCollider>();
                        var ct = g.AddComponent<Ar6dofCellTarget>();
                        ct.board = this; ct.idx = i;
                        SetColH(i);
                    }
                }
        }

        public void Click(int i) { if (click != null) click(i); }

        public void SetHover(int i, bool on)
        {
            if (i < 0 || i >= hov.Length || hov[i] == on) return;
            hov[i] = on;
            PaintTile(i);
        }

        void PaintTile(int i) { table.Paint(tile[i], tcol[i], hov[i] ? 0.14f : 0f); }

        void SetColH(int i)
        {
            if (col[i] == null) return;
            float h = Ar6dofTable3D.TileH + 0.004f + (tok[i] > 0 ? Ar6dofShapes.Height(tok[i]) * cell : 0f);
            col[i].size = new Vector3(cell, h, cell);
            col[i].center = new Vector3(0f, h * 0.5f, 0f);
        }

        public void Bg(int i, Color c)
        {
            if (tcol[i] == c) return;
            tcol[i] = c;
            PaintTile(i);
        }

        /// <summary>Pose (ou enleve, jeton 0) une piece sur la case.</summary>
        public void Piece(int i, int token)
        {
            if (token < 0) token = 0;
            if (tok[i] == token) return;
            if (pc[i] != null) { UnityEngine.Object.Destroy(pc[i].gameObject); pc[i] = null; pcR[i] = null; }
            tok[i] = token;
            if (token > 0)
            {
                var g = table.Part(cellT[i], "Piece", Ar6dofShapes.Get(token), null, new Vector3(0f, Ar6dofTable3D.TileH, 0f), Vector3.one * cell, Ar6dofShapes.ColorOf(token));
                if (token >= 100 && token < 200) g.transform.localRotation = Quaternion.Euler(0f, ((token & 1) == 1) == whiteNear ? 0f : 180f, 0f);
                pc[i] = g.transform; pcR[i] = g.GetComponent<Renderer>();
                table.PopIn(g.transform, Vector3.one * cell);
            }
            SetColH(i);
            ApplyMark(i);
        }

        /// <summary>Texte d'une case (morpion) : « X » et « O » deviennent des pieces.</summary>
        public void Caption(int i, string s)
        {
            Piece(i, s == "X" ? Ar6dofShapes.TokX : (s == "O" ? Ar6dofShapes.TokO : 0));
        }

        public void Mark(int i, Color c, float scale)
        {
            mkC[i] = c; mkS[i] = scale;
            ApplyMark(i);
        }

        void ApplyMark(int i)
        {
            if (mkC[i].a <= 0f)
            {
                if (mk[i] != null && mk[i].gameObject.activeSelf) mk[i].gameObject.SetActive(false);
                return;
            }
            if (mk[i] == null)
            {
                var g = table.Part(cellT[i], "Marque", Ar6dofShapes.UnitCylinder(), null, Vector3.zero, Vector3.one, Color.white);
                mk[i] = g.transform; mkR[i] = g.GetComponent<Renderer>(); mkF[i] = g.GetComponent<MeshFilter>();
            }
            if (!mk[i].gameObject.activeSelf) mk[i].gameObject.SetActive(true);
            bool tall = tok[i] > 0 && Ar6dofShapes.Height(tok[i]) > 0.3f;
            float baseY = Ar6dofTable3D.TileH + (tok[i] > 0 && !tall ? Ar6dofShapes.Height(tok[i]) * cell : 0f);
            if (tall)
            {
                mkF[i].sharedMesh = Ar6dofShapes.UnitRing();
                mk[i].localPosition = new Vector3(0f, Ar6dofTable3D.TileH + 0.002f, 0f);
                mk[i].localScale = Vector3.one * cell * 0.9f;
            }
            else
            {
                mkF[i].sharedMesh = Ar6dofShapes.UnitCylinder();
                mk[i].localPosition = new Vector3(0f, baseY, 0f);
                float s = cell * Mathf.Max(0.1f, mkS[i]);
                mk[i].localScale = new Vector3(s, 0.004f, s);
            }
            table.Paint(mkR[i], mkC[i], 0f);
        }
    }
}
