using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    // Les jeux du Hub. Chaque jeu est une fenetre comme les autres applications : rien n'existe tant qu'elle n'est pas ouverte, et rien ne tourne
    // quand elle est fermee ou rangee. Pas de son, pas de reseau, pas de fichier : seulement l'etat de la partie en cours (et les jetons de la machine a sous).

    // ======================================================================== OUTILS COMMUNS
    static class Ar6dofGameUi
    {
        public static Color C(float r, float g, float b, float a = 1f) { return new Color(r, g, b, a); }

        /// <summary>Rectangle arrondi plein.</summary>
        public static Image Panel(Transform parent, Vector2 pos, Vector2 size, Color col)
        {
            var rt = Ar6dofVrUi.Rect(parent, "Panneau", pos, size);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = Ar6dofGameArt.Round();
            im.type = Image.Type.Sliced;
            im.color = col;
            im.raycastTarget = false;
            return im;
        }

        /// <summary>Image sans bord arrondi (sprite facultatif : sans sprite, un carre uni).</summary>
        public static Image Pic(Transform parent, Vector2 pos, Vector2 size, Sprite sp, Color col)
        {
            var rt = Ar6dofVrUi.Rect(parent, "Image", pos, size);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = sp;
            im.color = col;
            im.preserveAspect = sp != null;
            im.raycastTarget = false;
            return im;
        }

        /// <summary>Texte de couleur libre (les textes du theme ne gardent pas la couleur qu'on leur donne).</summary>
        public static Text Txt(Transform parent, string text, Vector2 pos, Vector2 size, int fs, Color col, bool bold = false, TextAnchor a = TextAnchor.MiddleCenter)
        {
            var rt = Ar6dofVrUi.Rect(parent, "Texte", pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Ar6dofVrUi.GetFont();
            t.fontSize = fs;
            t.alignment = a;
            t.alignByGeometry = true;
            t.color = col;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (bold) t.fontStyle = FontStyle.Bold;
            return t;
        }
    }

    /// <summary>Un calcul qui tourne dans un fil a part (l'IA des echecs et des dames) : on regarde s'il est fini a chaque image, sans jamais attendre.</summary>
    sealed class Ar6dofAiJob<T>
    {
        readonly Task<T> task;
        public Ar6dofAiJob(Func<T> f) { task = Task.Run(f); }
        public bool Done { get { return task.IsCompleted; } }
        public bool Failed { get { return task.IsFaulted || task.IsCanceled; } }
        public T Result { get { return task.Result; } }
    }

    /// <summary>Un plateau de n x n cases cliquables, avec pour chaque case : fond, piece, marque (point) et texte facultatif. Se met a jour sans rien reconstruire.</summary>
    sealed class Ar6dofBoardView
    {
        public readonly int n;
        readonly Image[] bg, pc, mk;
        readonly Text[] tx;

        Ar6dofBoardView(int n)
        {
            this.n = n;
            bg = new Image[n * n]; pc = new Image[n * n]; mk = new Image[n * n]; tx = new Text[n * n];
        }

        /// <summary>'cx' : centre horizontal ; 'top' : distance depuis le haut du contenu ; la case 0 est en haut a gauche.</summary>
        public static Ar6dofBoardView Make(Transform parent, float cx, float top, float cell, int n, Action<int> click, bool text)
        {
            var v = new Ar6dofBoardView(n);
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    var rt = Ar6dofVrUi.Rect(parent, "Case", new Vector2(cx + (c - (n - 1) * 0.5f) * cell, -(top + (r + 0.5f) * cell)), new Vector2(cell, cell));
                    var im = rt.gameObject.AddComponent<Image>();
                    im.color = Color.gray;
                    v.bg[i] = im;
                    if (click != null)
                    {
                        var b = rt.gameObject.AddComponent<Button>();
                        b.targetGraphic = im;
                        var cb = b.colors;
                        cb.normalColor = Color.white;
                        cb.highlightedColor = new Color(0.84f, 0.84f, 0.84f);
                        cb.pressedColor = new Color(0.66f, 0.66f, 0.66f);
                        cb.selectedColor = Color.white;
                        cb.disabledColor = Color.white;
                        cb.fadeDuration = 0.05f;
                        b.colors = cb;
                        int k = i;
                        b.onClick.AddListener(() => click(k));
                    }
                    var p = Ar6dofGameUi.Pic(rt, Vector2.zero, new Vector2(cell * 0.92f, cell * 0.92f), null, Color.white);
                    p.enabled = false;
                    v.pc[i] = p;
                    var m = Ar6dofGameUi.Pic(rt, Vector2.zero, new Vector2(cell * 0.3f, cell * 0.3f), Ar6dofGameArt.Disc(), Color.white);
                    m.enabled = false;
                    v.mk[i] = m;
                    if (text) v.tx[i] = Ar6dofGameUi.Txt(rt, "", Vector2.zero, new Vector2(cell, cell), Mathf.RoundToInt(cell * 0.62f), Color.white, true);
                }
            return v;
        }

        public void Bg(int i, Color c) { bg[i].color = c; }

        public void Piece(int i, Sprite s)
        {
            var im = pc[i];
            if (s == null) { if (im.enabled) im.enabled = false; return; }
            if (im.sprite != s) im.sprite = s;
            if (!im.enabled) im.enabled = true;
        }

        /// <summary>Petit disque pose sur la case (coup possible, tir manque...). 'scale' : taille relative a la case.</summary>
        public void Mark(int i, Color c, float scale)
        {
            var im = mk[i];
            if (c.a <= 0f) { if (im.enabled) im.enabled = false; return; }
            im.color = c;
            var rt = (RectTransform)im.transform;
            float s = bg[i].rectTransform.sizeDelta.x * scale;
            if (Mathf.Abs(rt.sizeDelta.x - s) > 0.5f) rt.sizeDelta = new Vector2(s, s);
            if (!im.enabled) im.enabled = true;
        }

        public void Caption(int i, string s, Color c)
        {
            if (tx[i] == null) return;
            if (tx[i].text != s) tx[i].text = s;
            tx[i].color = c;
        }
    }

    // ======================================================================== MORPION
    public sealed class Ar6dofTicTacToeApp : Ar6dofAppWindow
    {
        static readonly string[] Levels = { "Facile", "Moyen", "Imbattable" };
        readonly int[] cells = new int[9];
        readonly System.Random rnd = new System.Random();
        int level = 1, result, wins, losses, draws;
        bool pcFirst;
        float pcAt = -1f;
        Ar6dofBoardView view;
        Text status, score;

        public Ar6dofTicTacToeApp(Ar6dofDock d) : base(d, "tictactoe", "Morpion", "tictactoe", "Morpion contre le PC : trois niveaux, du facile a l'imbattable.", 540f) { }

        protected override void OnRebuildStart() { view = null; status = null; score = null; }

        public override void OnClosed() { pcAt = -1f; }

        void NewGame()
        {
            for (int i = 0; i < 9; i++) cells[i] = 0;
            result = 0;
            pcAt = pcFirst ? Time.unscaledTime + 0.5f : -1f;
            Rebuild();
        }

        protected override void BuildMain()
        {
            BtnRow(48f, 20,
                Ar6dofLoc.T("Niveau") + " : " + Ar6dofLoc.T(Levels[level]), (Action)(() => { level = (level + 1) % 3; NewGame(); }),
                Ar6dofLoc.T("Commence") + " : " + Ar6dofLoc.T(pcFirst ? "PC" : "Moi"), (Action)(() => { pcFirst = !pcFirst; NewGame(); }),
                "Nouvelle partie", (Action)NewGame);
            status = Lbl("", 40, 26, TextAnchor.MiddleCenter);
            status.fontStyle = FontStyle.Bold;
            Space(4f);
            view = Ar6dofBoardView.Make(content, 0f, y, 130f, 3, OnCell, true);
            y += 3 * 130f + Gap;
            score = Lbl("", 34, 21, TextAnchor.MiddleCenter);
            Draw();
        }

        void OnCell(int i)
        {
            if (result != 0 || pcAt > 0f || cells[i] != 0) return;
            cells[i] = 1;
            Evaluate();
            if (result == 0) pcAt = Time.unscaledTime + 0.45f;
            Draw();
        }

        protected override void OnTick()
        {
            if (pcAt > 0f && Time.unscaledTime >= pcAt)
            {
                pcAt = -1f;
                if (result != 0) return;
                int i = Ar6dofTicTacToe.Best(cells, 2, level, rnd);
                if (i >= 0) cells[i] = 2;
                Evaluate();
                Draw();
            }
        }

        void Evaluate()
        {
            int w = Ar6dofTicTacToe.Winner(cells);
            if (w == 1) { result = 1; wins++; }
            else if (w == 2) { result = 2; losses++; }
            else if (Ar6dofTicTacToe.Full(cells)) { result = 3; draws++; }
        }

        void Draw()
        {
            if (view == null) return;
            var line = Ar6dofTicTacToe.WinLine(cells);
            for (int i = 0; i < 9; i++)
            {
                bool win = line != null && (line[0] == i || line[1] == i || line[2] == i);
                view.Bg(i, win ? Ar6dofGameUi.C(0.22f, 0.5f, 0.28f) : ((i & 1) == 0 ? Ar6dofGameUi.C(0.24f, 0.28f, 0.36f) : Ar6dofGameUi.C(0.3f, 0.34f, 0.43f)));
                view.Caption(i, cells[i] == 1 ? "X" : (cells[i] == 2 ? "O" : ""), cells[i] == 1 ? Ar6dofGameUi.C(1f, 0.45f, 0.4f) : Ar6dofGameUi.C(0.5f, 0.78f, 1f));
            }
            string s;
            if (result == 1) s = "Tu as gagne !";
            else if (result == 2) s = "Le PC a gagne.";
            else if (result == 3) s = "Match nul.";
            else s = pcAt > 0f ? "Le PC joue..." : "A toi de jouer (tu es X).";
            status.text = Ar6dofLoc.T(s);
            score.text = Ar6dofLoc.T("Victoires") + " " + wins + "   " + Ar6dofLoc.T("Nuls") + " " + draws + "   " + Ar6dofLoc.T("Defaites") + " " + losses;
        }
    }

    // ======================================================================== DES
    public sealed class Ar6dofDiceApp : Ar6dofAppWindow
    {
        static readonly int[] Kinds = { 2, 4, 6, 8, 10, 12, 20, 100 };
        static readonly string[] Pips = { "000010000", "100000001", "100010001", "101000101", "101010101", "101101101" };
        const float Tile = 150f;
        int sides = 6, count = 2;
        readonly int[] vals = { 1, 1, 1, 1, 1, 1 };
        readonly int[] fin = new int[6];
        readonly System.Random rnd = new System.Random();
        bool rolling, rolled;
        float rollEnd, nextFlick;
        Image[] face;
        Text[] nums;
        Image[][] pips;
        Text totalText;
        readonly List<Button> kindBtns = new List<Button>();

        public Ar6dofDiceApp(Ar6dofDock d) : base(d, "dice", "Des", "dice", "Lance de 1 a 6 des : pile ou face, d4, d6, d8, d10, d12, d20 ou d100.", 600f) { }

        protected override void OnRebuildStart() { face = null; nums = null; pips = null; totalText = null; kindBtns.Clear(); }

        public override void OnClosed() { rolling = false; }

        static Color Tint(int s)
        {
            switch (s)
            {
                case 2: return Ar6dofGameUi.C(0.96f, 0.78f, 0.22f);
                case 4: return Ar6dofGameUi.C(0.45f, 0.82f, 0.45f);
                case 6: return Ar6dofGameUi.C(0.96f, 0.96f, 0.94f);
                case 8: return Ar6dofGameUi.C(0.45f, 0.7f, 0.96f);
                case 10: return Ar6dofGameUi.C(0.74f, 0.55f, 0.95f);
                case 12: return Ar6dofGameUi.C(0.98f, 0.62f, 0.3f);
                case 20: return Ar6dofGameUi.C(0.95f, 0.4f, 0.4f);
                default: return Ar6dofGameUi.C(0.9f, 0.82f, 0.4f);
            }
        }

        string KindName(int k) { return k == 2 ? Ar6dofLoc.T("Piece") : "d" + k; }

        protected override void BuildMain()
        {
            Section("Type de de");
            for (int row = 0; row < 2; row++)
            {
                var items = new object[8];
                for (int j = 0; j < 4; j++)
                {
                    int k = Kinds[row * 4 + j];
                    items[2 * j] = KindName(k);
                    items[2 * j + 1] = (Action)(() => { sides = k; rolled = false; rolling = false; for (int i = 0; i < 6; i++) vals[i] = Mathf.Min(vals[i], sides); Layout(); Refresh(); });
                }
                var bs = BtnRow(46f, 22, items);
                for (int j = 0; j < 4; j++) { var bb = bs[j]; int k = Kinds[row * 4 + j]; kindBtns.Add(bb); int kk = k; refreshers.Add(() => Ar6dofTheme.SetSelected(bb, sides == kk)); }
            }
            SliderRow("Nombre de des", 1, 6, () => count, v => { int c = Mathf.RoundToInt(v); if (c != count) { count = c; rolled = false; rolling = false; Layout(); } }, v => Mathf.RoundToInt(v).ToString(), 50f, true);
            Space(4f);

            face = new Image[6]; nums = new Text[6]; pips = new Image[6][];
            float areaTop = y;
            for (int i = 0; i < 6; i++)
            {
                face[i] = Ar6dofGameUi.Panel(content, Vector2.zero, new Vector2(Tile, Tile), Color.white);
                var rt = face[i].rectTransform;
                nums[i] = Ar6dofGameUi.Txt(rt, "", Vector2.zero, new Vector2(Tile, Tile), 64, Ar6dofGameUi.C(0.12f, 0.12f, 0.16f), true);
                pips[i] = new Image[9];
                for (int k = 0; k < 9; k++)
                {
                    var pp = Ar6dofGameUi.Pic(rt, new Vector2((k % 3 - 1) * Tile * 0.27f, (1 - k / 3) * Tile * 0.27f), new Vector2(Tile * 0.19f, Tile * 0.19f), Ar6dofGameArt.Disc(), Ar6dofGameUi.C(0.12f, 0.12f, 0.16f));
                    pp.enabled = false;
                    pips[i][k] = pp;
                }
            }
            y += 2 * Tile + 10f + Gap;
            totalText = Lbl("", 40, 28, TextAnchor.MiddleCenter);
            totalText.fontStyle = FontStyle.Bold;
            Btn("Lancer", Roll, 66f, 32);
            areaTopY = areaTop;
            Layout();
        }

        float areaTopY;

        /// <summary>Place et montre seulement les des utilises, centres ligne par ligne (3 par ligne).</summary>
        void Layout()
        {
            if (face == null) return;
            for (int i = 0; i < 6; i++)
            {
                bool on = i < count;
                face[i].gameObject.SetActive(on);
                if (!on) continue;
                int row = i / 3, inRow = row == 0 ? Mathf.Min(3, count) : count - 3;
                float x = (i % 3 - (inRow - 1) * 0.5f) * (Tile + 14f);
                face[i].rectTransform.anchoredPosition = new Vector2(x, -(areaTopY + Tile * 0.5f + row * (Tile + 10f)));
                face[i].rectTransform.localRotation = Quaternion.identity;
            }
            Faces();
        }

        void Faces()
        {
            if (face == null) return;
            for (int i = 0; i < count; i++)
            {
                face[i].color = Tint(sides);
                int v = Mathf.Clamp(vals[i], 1, sides);
                bool dark = sides == 8 || sides == 20 || sides == 10;
                nums[i].color = dark ? Color.white : Ar6dofGameUi.C(0.12f, 0.12f, 0.16f);
                if (sides == 6)
                {
                    nums[i].text = "";
                    string mask = Pips[v - 1];
                    for (int k = 0; k < 9; k++) pips[i][k].enabled = mask[k] == '1';
                }
                else
                {
                    for (int k = 0; k < 9; k++) pips[i][k].enabled = false;
                    nums[i].text = sides == 2 ? Ar6dofLoc.T(v == 1 ? "Pile" : "Face") : v.ToString();
                    nums[i].fontSize = sides == 2 ? 40 : (sides == 100 ? 56 : 68);
                }
            }
            if (totalText == null) return;
            if (!rolled || rolling) { totalText.text = ""; return; }
            int sum = 0, piles = 0;
            for (int i = 0; i < count; i++) { sum += vals[i]; if (vals[i] == 1) piles++; }
            if (sides == 2) totalText.text = Ar6dofLoc.T("Pile") + " : " + piles + "   " + Ar6dofLoc.T("Face") + " : " + (count - piles);
            else totalText.text = count > 1 ? Ar6dofLoc.T("Total") + " : " + sum : "";
        }

        void Roll()
        {
            for (int i = 0; i < count; i++) fin[i] = rnd.Next(1, sides + 1);
            rolling = true; rolled = true;
            rollEnd = Time.unscaledTime + 0.85f;
            nextFlick = 0f;
        }

        protected override void OnTick()
        {
            if (!rolling || face == null) return;
            float now = Time.unscaledTime;
            if (now >= rollEnd)
            {
                rolling = false;
                for (int i = 0; i < count; i++) { vals[i] = fin[i]; face[i].rectTransform.localRotation = Quaternion.identity; }
                Faces();
            }
            else if (now >= nextFlick)
            {
                nextFlick = now + 0.07f;
                for (int i = 0; i < count; i++)
                {
                    vals[i] = rnd.Next(1, sides + 1);
                    face[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, rnd.Next(-9, 10));
                }
                Faces();
            }
        }
    }

    // ======================================================================== MACHINE A SOUS
    public sealed class Ar6dofSlotsApp : Ar6dofAppWindow
    {
        // cerise, citron, cloche, etoile, diamant, sept
        static readonly int[] Weight = { 30, 25, 18, 12, 9, 6 };
        static readonly int[] Pay = { 6, 10, 16, 30, 60, 150 };
        static readonly int[] Bets = { 1, 5, 10 };
        static readonly float[] StopAt = { 1.1f, 1.6f, 2.1f };
        const string Prefs = "ar6dof_slots_credits";
        const float Pitch = 84f, Sym = 80f;

        readonly System.Random rnd = new System.Random();
        readonly int[] result = new int[3];
        readonly int[][] rows = { new int[3], new int[3], new int[3] };     // [rouleau][haut, milieu, bas]
        readonly bool[] stopped = { true, true, true };
        readonly float[] nextStep = new float[3], popAt = { -9f, -9f, -9f };
        int credits = 100, betIdx;
        bool spinning;
        float spinStart, winUntil, leverUntil;
        int lastWin;
        string message = "";
        Image[][] syms;
        Image[] bulbs;
        Image ball;
        Text creditText, msgText;
        Button rechargeBtn;
        readonly List<Button> betBtns = new List<Button>();

        public Ar6dofSlotsApp(Ar6dofDock d) : base(d, "slots", "Machine a sous", "slots", "Une machine a sous cartoon avec des jetons virtuels : aucun argent, juste pour le fun.", 640f)
        {
            credits = PlayerPrefs.GetInt(Prefs, 100);
            for (int r = 0; r < 3; r++) for (int k = 0; k < 3; k++) rows[r][k] = RandSymbol();
        }

        protected override void OnRebuildStart() { machineTop = -1f; syms = null; bulbs = null; ball = null; creditText = null; msgText = null; rechargeBtn = null; betBtns.Clear(); }

        public override void OnClosed() { spinning = false; }

        int RandSymbol()
        {
            int total = 0;
            for (int i = 0; i < Weight.Length; i++) total += Weight[i];
            int r = rnd.Next(total);
            for (int i = 0; i < Weight.Length; i++) { r -= Weight[i]; if (r < 0) return i; }
            return 0;
        }

        protected override void BuildMain()
        {
            var gold = Ar6dofGameUi.C(0.96f, 0.74f, 0.16f);
            float top = y, cx = -30f;
            Ar6dofGameUi.Panel(content, new Vector2(cx, -(top + 215f)), new Vector2(504f, 430f), gold);
            Ar6dofGameUi.Panel(content, new Vector2(cx, -(top + 215f)), new Vector2(480f, 408f), Ar6dofGameUi.C(0.82f, 0.13f, 0.2f));
            bulbs = new Image[11];
            for (int i = 0; i < bulbs.Length; i++)
                bulbs[i] = Ar6dofGameUi.Pic(content, new Vector2(cx + (i - 5) * 38f, -(top + 24f)), new Vector2(18f, 18f), Ar6dofGameArt.Disc(), gold);
            Ar6dofGameUi.Txt(content, "JACKPOT", new Vector2(cx, -(top + 62f)), new Vector2(420f, 48f), 40, gold, true);
            Ar6dofGameUi.Panel(content, new Vector2(cx, -(top + 232f)), new Vector2(456f, 284f), Ar6dofGameUi.C(0.16f, 0.07f, 0.1f));
            syms = new Image[3][];
            for (int r = 0; r < 3; r++)
            {
                float rx = cx + (r - 1) * 150f;
                Ar6dofGameUi.Panel(content, new Vector2(rx, -(top + 232f)), new Vector2(140f, 268f), Ar6dofGameUi.C(0.98f, 0.96f, 0.9f));
                syms[r] = new Image[3];
                for (int k = 0; k < 3; k++)
                    syms[r][k] = Ar6dofGameUi.Pic(content, new Vector2(rx, -(top + 232f + (k - 1) * Pitch)), new Vector2(Sym, Sym), Ar6dofGameArt.Slot(rows[r][k]), Color.white);
            }
            Ar6dofGameUi.Pic(content, new Vector2(cx, -(top + 232f)), new Vector2(440f, 4f), null, Ar6dofGameUi.C(1f, 0.15f, 0.15f, 0.6f));
            // levier : un baton et une boule rouge qui descend quand on lance
            Ar6dofGameUi.Panel(content, new Vector2(255f, -(top + 250f)), new Vector2(14f, 150f), Ar6dofGameUi.C(0.7f, 0.7f, 0.74f));
            Ar6dofGameUi.Panel(content, new Vector2(255f, -(top + 330f)), new Vector2(40f, 30f), Ar6dofGameUi.C(0.45f, 0.45f, 0.5f));
            ball = Ar6dofGameUi.Pic(content, new Vector2(255f, -(top + 160f)), new Vector2(46f, 46f), Ar6dofGameArt.Disc(), Ar6dofGameUi.C(0.9f, 0.12f, 0.16f));
            y += 430f + Gap;

            msgText = Lbl("", 44, 28, TextAnchor.MiddleCenter);
            msgText.fontStyle = FontStyle.Bold;
            creditText = Lbl("", 34, 24, TextAnchor.MiddleCenter);

            var items = new object[8];
            for (int i = 0; i < 3; i++) { int k = i; items[2 * i] = Ar6dofLoc.T("Mise") + " " + Bets[i]; items[2 * i + 1] = (Action)(() => { betIdx = k; Refresh(); }); }
            items[6] = "Recharger"; items[7] = (Action)(() => { if (credits < Bets[betIdx]) { credits += 100; Save(); message = "100 jetons offerts."; Draw(); Refresh(); } });
            var bs = BtnRow(50f, 21, items);
            for (int i = 0; i < 3; i++) { var bb = bs[i]; int k = i; betBtns.Add(bb); refreshers.Add(() => Ar6dofTheme.SetSelected(bb, betIdx == k)); }
            rechargeBtn = bs[3];
            refreshers.Add(() => { if (rechargeBtn != null) rechargeBtn.interactable = credits < Bets[betIdx]; });
            Btn("Lancer", Spin, 66f, 32);

            // table des gains
            Section("Gains (trois identiques au milieu)");
            float cy = RowY(50f);
            float cw = IW / 6f;
            for (int i = 0; i < 6; i++)
            {
                float x = -IW * 0.5f + cw * (i + 0.5f);
                Ar6dofGameUi.Pic(content, new Vector2(x - 20f, cy), new Vector2(40f, 40f), Ar6dofGameArt.Slot(i), Color.white);
                Ar6dofGameUi.Txt(content, "x" + Pay[i], new Vector2(x + 24f, cy), new Vector2(60f, 40f), 22, Color.white, true);
            }
            Para("Deux cerises : x2. Les jetons sont virtuels et ne valent rien : aucun argent reel n'est en jeu.", 44, 18);
            for (int r = 0; r < 3; r++) ShowReel(r);
            Draw();
        }

        void Save() { PlayerPrefs.SetInt(Prefs, credits); }

        void Spin()
        {
            if (spinning) return;
            int bet = Bets[betIdx];
            if (credits < bet) { message = "Pas assez de jetons."; Draw(); Refresh(); return; }
            credits -= bet;
            Save();
            for (int r = 0; r < 3; r++) { result[r] = RandSymbol(); stopped[r] = false; nextStep[r] = 0f; }
            spinning = true;
            spinStart = Time.unscaledTime;
            leverUntil = spinStart + 0.35f;
            message = "";
            winUntil = 0f;
            Draw();
            Refresh();
        }

        void ShowReel(int r)
        {
            if (syms == null) return;
            for (int k = 0; k < 3; k++)
            {
                var im = syms[r][k];
                im.sprite = Ar6dofGameArt.Slot(rows[r][k]);
                im.color = k == 1 ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            }
        }

        protected override void OnTick()
        {
            if (syms == null) return;
            float now = Time.unscaledTime;
            if (spinning)
            {
                float t = now - spinStart;
                bool allStopped = true;
                for (int r = 0; r < 3; r++)
                {
                    if (stopped[r]) continue;
                    if (t >= StopAt[r])
                    {
                        stopped[r] = true;
                        rows[r][1] = result[r];
                        rows[r][0] = RandSymbol();
                        rows[r][2] = RandSymbol();
                        popAt[r] = now;
                        ShowReel(r);
                        continue;
                    }
                    allStopped = false;
                    if (now >= nextStep[r])
                    {
                        float slow = Mathf.Clamp01((t - (StopAt[r] - 0.7f)) / 0.7f);      // le rouleau ralentit avant de s'arreter
                        nextStep[r] = now + Mathf.Lerp(0.06f, 0.3f, slow * slow);
                        rows[r][2] = rows[r][1]; rows[r][1] = rows[r][0]; rows[r][0] = RandSymbol();
                        ShowReel(r);
                    }
                }
                if (allStopped) EndSpin(now);
            }
            // petit rebond du symbole quand le rouleau s'arrete
            for (int r = 0; r < 3; r++)
            {
                float a = (now - popAt[r]) / 0.25f;
                float s = a >= 0f && a < 1f ? 1f + 0.22f * (1f - a) * (1f - a) : 1f;
                var rt = syms[r][1].rectTransform;
                if (Mathf.Abs(rt.localScale.x - s) > 0.001f) rt.localScale = new Vector3(s, s, 1f);
            }
            // levier
            if (ball != null)
            {
                float lv = leverUntil > now ? 1f - (leverUntil - now) / 0.35f : 1f;
                float down = lv < 0.5f ? lv * 2f : (1f - lv) * 2f;
                var brt = ball.rectTransform;
                var p = brt.anchoredPosition;
                float baseY = -(topOfMachine() + 160f);
                brt.anchoredPosition = new Vector2(p.x, baseY - 70f * Mathf.Clamp01(down));
            }
            // ampoules : elles clignotent pendant le tirage et apres un gain
            if (bulbs != null)
            {
                bool blink = spinning || winUntil > now;
                var gold = Ar6dofGameUi.C(0.96f, 0.74f, 0.16f);
                var white = Ar6dofGameUi.C(1f, 0.97f, 0.8f);
                int phase = Mathf.FloorToInt(now * 6f);
                for (int i = 0; i < bulbs.Length; i++) bulbs[i].color = blink ? (((i + phase) & 1) == 0 ? white : Ar6dofGameUi.C(0.9f, 0.3f, 0.1f)) : gold;
            }
        }

        float machineTop = -1f;
        float topOfMachine()
        {
            if (machineTop < 0f && bulbs != null) machineTop = -bulbs[0].rectTransform.anchoredPosition.y - 24f;
            return machineTop;
        }

        void EndSpin(float now)
        {
            spinning = false;
            int a = result[0], b = result[1], c = result[2], mult = 0;
            if (a == b && b == c) mult = Pay[a];
            else
            {
                int cherries = (a == 0 ? 1 : 0) + (b == 0 ? 1 : 0) + (c == 0 ? 1 : 0);
                if (cherries == 2) mult = 2;
            }
            int bet = Bets[betIdx];
            lastWin = mult * bet;
            if (lastWin > 0)
            {
                credits += lastWin;
                Save();
                message = Ar6dofLoc.F("Gagne ! +{0} jetons", lastWin);
                winUntil = now + 2f;
            }
            else message = "Perdu... retente ta chance !";
            Draw();
            Refresh();
        }

        void Draw()
        {
            if (creditText == null) return;
            creditText.text = Ar6dofLoc.T("Jetons") + " : " + credits;
            msgText.text = Ar6dofLoc.T(message);
            msgText.color = lastWin > 0 && winUntil > Time.unscaledTime ? Ar6dofGameUi.C(1f, 0.85f, 0.2f) : Color.white;
        }
    }

    // ======================================================================== BATAILLE NAVALE
    public sealed class Ar6dofBattleshipApp : Ar6dofAppWindow
    {
        const float Cell = 36f;
        readonly System.Random rnd = new System.Random();
        readonly Ar6dofFleet mine = new Ar6dofFleet(), theirs = new Ar6dofFleet();
        Ar6dofNavalAi ai;
        int phase;                 // 0 : placement de ma flotte, 1 : bataille, 2 : fini
        bool playerTurn = true, iWon;
        float pcAt = -1f;
        string myShot = "", pcShot = "";
        Ar6dofBoardView enemyView, ownView;
        Text status, shots, count;

        public Ar6dofBattleshipApp(Ar6dofDock d) : base(d, "battleship", "Bataille navale", "ship", "Bataille navale contre le PC : place ta flotte et coule la sienne.", 840f)
        {
            Shuffle();
        }

        protected override void OnRebuildStart() { enemyView = null; ownView = null; status = null; shots = null; count = null; }

        public override void OnClosed() { pcAt = -1f; }

        void Shuffle() { mine.PlaceRandom(rnd); }

        void NewGame()
        {
            Shuffle();
            theirs.PlaceRandom(rnd);
            ai = new Ar6dofNavalAi(rnd);
            phase = 0; playerTurn = true; iWon = false; pcAt = -1f; myShot = ""; pcShot = "";
            Rebuild();
        }

        void Start()
        {
            if (phase != 0) return;
            theirs.PlaceRandom(rnd);
            ai = new Ar6dofNavalAi(rnd);
            phase = 1; playerTurn = true; myShot = ""; pcShot = "";
            Draw();
        }

        protected override void BuildMain()
        {
            BtnRow(48f, 20, "Nouvelle partie", (Action)NewGame, "Melanger ma flotte", (Action)(() => { if (phase == 0) { Shuffle(); Draw(); } }), "Commencer", (Action)Start);
            status = Lbl("", 40, 26, TextAnchor.MiddleCenter);
            status.fontStyle = FontStyle.Bold;
            shots = Lbl("", 30, 20, TextAnchor.MiddleCenter);
            Space(4f);
            float labY = y;
            Ar6dofVrUi.Label(content, Ar6dofLoc.T("Flotte ennemie") + "  (" + Ar6dofLoc.T("touche pour tirer") + ")", new Vector2(-210f, -(labY + 15f)), new Vector2(390f, 30f), 20, TextAnchor.MiddleCenter);
            Ar6dofVrUi.Label(content, Ar6dofLoc.T("Ma flotte"), new Vector2(210f, -(labY + 15f)), new Vector2(390f, 30f), 20, TextAnchor.MiddleCenter);
            y += 34f;
            enemyView = Ar6dofBoardView.Make(content, -210f, y, Cell, 10, OnEnemyCell, false);
            ownView = Ar6dofBoardView.Make(content, 210f, y, Cell, 10, null, false);
            y += 10 * Cell + Gap;
            count = Lbl("", 32, 20, TextAnchor.MiddleCenter);
            Draw();
        }

        void OnEnemyCell(int i)
        {
            if (phase != 1 || !playerTurn || theirs.shot[i]) return;
            int sunk;
            int res = theirs.Shoot(i, out sunk);
            myShot = ShotText(res, sunk);
            if (theirs.AllSunk) { phase = 2; iWon = true; }
            else { playerTurn = false; pcAt = Time.unscaledTime + 0.7f; }
            Draw();
        }

        static string ShotText(int res, int sunk)
        {
            if (res == 0) return Ar6dofLoc.T("a l'eau");
            if (res == 1) return Ar6dofLoc.T("touche !");
            return Ar6dofLoc.F("coule : {0} !", Ar6dofLoc.T(Ar6dofFleet.Names[Mathf.Clamp(sunk, 0, 4)]));
        }

        protected override void OnTick()
        {
            if (pcAt > 0f && Time.unscaledTime >= pcAt)
            {
                pcAt = -1f;
                if (phase != 1) return;
                int i = ai.Next();
                if (i < 0) return;
                int sunk;
                int res = mine.Shoot(i, out sunk);
                ai.Report(i, res, mine, sunk);
                pcShot = ShotText(res, sunk);
                if (mine.AllSunk) { phase = 2; iWon = false; }
                else playerTurn = true;
                Draw();
            }
        }

        static readonly Color Water = new Color(0.12f, 0.3f, 0.48f), Hull = new Color(0.55f, 0.6f, 0.67f), Hit = new Color(0.85f, 0.28f, 0.2f), Sunk = new Color(0.45f, 0.08f, 0.08f), Splash = new Color(0.88f, 0.94f, 1f, 0.9f), Fire = new Color(1f, 0.8f, 0.1f, 1f);

        void Draw()
        {
            if (enemyView == null) return;
            for (int i = 0; i < 100; i++)
            {
                // flotte ennemie : on ne voit que ce qu'on a touche
                if (theirs.shot[i])
                {
                    int k = theirs.ship[i];
                    if (k < 0) { enemyView.Bg(i, Water); enemyView.Mark(i, Splash, 0.3f); }
                    else if (theirs.left[k] == 0) { enemyView.Bg(i, Sunk); enemyView.Mark(i, Ar6dofGameUi.C(0.1f, 0.1f, 0.1f, 0.8f), 0.5f); }
                    else { enemyView.Bg(i, Hit); enemyView.Mark(i, Fire, 0.45f); }
                }
                else { enemyView.Bg(i, Water); enemyView.Mark(i, Color.clear, 0f); }
                // ma flotte : tout est visible
                int m = mine.ship[i];
                if (mine.shot[i])
                {
                    if (m < 0) { ownView.Bg(i, Water); ownView.Mark(i, Splash, 0.3f); }
                    else if (mine.left[m] == 0) { ownView.Bg(i, Sunk); ownView.Mark(i, Ar6dofGameUi.C(0.1f, 0.1f, 0.1f, 0.8f), 0.5f); }
                    else { ownView.Bg(i, Hit); ownView.Mark(i, Fire, 0.45f); }
                }
                else { ownView.Bg(i, m >= 0 ? Hull : Water); ownView.Mark(i, Color.clear, 0f); }
            }
            string s;
            if (phase == 0) s = "Place ta flotte : melange-la jusqu'a ce qu'elle te plaise, puis commence.";
            else if (phase == 2) s = iWon ? "Victoire ! Toute la flotte ennemie est coulee." : "Defaite : ta flotte est coulee.";
            else s = playerTurn ? "A toi de tirer !" : "Le PC vise...";
            status.text = Ar6dofLoc.T(s);
            string line = "";
            if (myShot.Length > 0) line += Ar6dofLoc.T("Toi") + " : " + myShot;
            if (pcShot.Length > 0) line += (line.Length > 0 ? "     " : "") + Ar6dofLoc.T("PC") + " : " + pcShot;
            shots.text = line;
            count.text = Ar6dofLoc.T("Navires restants") + " - " + Ar6dofLoc.T("toi") + " : " + mine.Afloat + "    PC : " + theirs.Afloat;
        }
    }

    // ======================================================================== ECHECS
    public sealed class Ar6dofChessApp : Ar6dofAppWindow
    {
        static readonly string[] Levels = { "Facile", "Moyen", "Fort" };
        static readonly int[] SpriteOf = { 0, 0, 2, 3, 1, 4, 5 };    // code du moteur -> dessin (pion, tour, cavalier, fou, dame, roi)
        const float Cell = 62f;
        Ar6dofChess game = Ar6dofChess.Start();
        readonly List<Ar6dofChess> history = new List<Ar6dofChess>();
        readonly int[] legal = new int[256];
        int nLegal, level = 1, playerSide = 1, selected = -1, lastFrom = -1, lastTo = -1, status;
        Ar6dofAiJob<int> job;
        Ar6dofChessAi ai;
        float aiStart;
        Ar6dofBoardView view;
        Text statusText;

        public Ar6dofChessApp(Ar6dofDock d) : base(d, "chess", "Echecs", "chess", "Echecs contre le PC : toutes les regles, trois niveaux, tu peux jouer les blancs ou les noirs.", 560f)
        {
            nLegal = game.LegalMoves(legal);
        }

        protected override void OnRebuildStart() { view = null; statusText = null; }

        public override void OnClosed() { Cancel(); }

        void Cancel() { if (ai != null) ai.cancel = true; ai = null; job = null; }

        void NewGame()
        {
            Cancel();
            game = Ar6dofChess.Start();
            history.Clear();
            selected = -1; lastFrom = lastTo = -1; status = 0;
            nLegal = game.LegalMoves(legal);
            if (game.side != playerSide) StartAi();
            Rebuild();
        }

        void StartAi()
        {
            Cancel();
            var a = new Ar6dofChessAi(game, Environment.TickCount);
            int lv = level;
            ai = a;
            job = new Ar6dofAiJob<int>(() => a.Think(lv));
            aiStart = Time.unscaledTime;
        }

        int Map(int i) { int r = i >> 3, c = i & 7; return playerSide == 1 ? (7 - r) * 8 + c : r * 8 + (7 - c); }

        protected override void BuildMain()
        {
            BtnRow(48f, 20,
                Ar6dofLoc.T("Niveau") + " : " + Ar6dofLoc.T(Levels[level]), (Action)(() => { level = (level + 1) % 3; Rebuild(); }),
                Ar6dofLoc.T("Je joue") + " : " + Ar6dofLoc.T(playerSide == 1 ? "Blancs" : "Noirs"), (Action)(() => { playerSide = -playerSide; NewGame(); }),
                "Nouvelle partie", (Action)NewGame);
            statusText = Lbl("", 40, 25, TextAnchor.MiddleCenter);
            statusText.fontStyle = FontStyle.Bold;
            Space(2f);
            view = Ar6dofBoardView.Make(content, 0f, y, Cell, 8, OnCell, false);
            y += 8 * Cell + Gap;
            BtnRow(46f, 20, "Annuler mon dernier coup", (Action)Undo);
            Draw();
        }

        void Undo()
        {
            if (history.Count == 0) return;
            Cancel();
            game = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            selected = -1; lastFrom = lastTo = -1; status = 0;
            nLegal = game.LegalMoves(legal);
            Draw();
        }

        bool MyTurn { get { return status == 0 && game.side == playerSide && job == null; } }

        void OnCell(int vi)
        {
            if (!MyTurn) return;
            int sq = Map(vi), p = game.b[sq];
            if (selected >= 0)
            {
                for (int k = 0; k < nLegal; k++)
                {
                    int m = legal[k], pr = Ar6dofChess.Promo(m);
                    if (Ar6dofChess.From(m) == selected && Ar6dofChess.To(m) == sq && (pr == 0 || pr == 5)) { PlayMine(m); return; }
                }
            }
            selected = (p != 0 && (p > 0) == (playerSide > 0) && sq != selected) ? sq : -1;
            Draw();
        }

        void PlayMine(int m)
        {
            history.Add(game.Clone());
            if (history.Count > 200) history.RemoveAt(0);
            Apply(m);
        }

        void Apply(int m)
        {
            game.Play(m);
            lastFrom = Ar6dofChess.From(m); lastTo = Ar6dofChess.To(m);
            selected = -1;
            status = game.Status();
            nLegal = game.LegalMoves(legal);
            if (status == 0 && game.side != playerSide) StartAi();
            Draw();
        }

        protected override void OnTick()
        {
            if (job != null && job.Done && Time.unscaledTime > aiStart + 0.35f)
            {
                int m = job.Failed ? 0 : job.Result;
                job = null; ai = null;
                if (m == 0 && status == 0 && nLegal > 0) m = legal[0];
                if (m != 0 && status == 0) Apply(m);
                else Draw();
            }
        }

        static readonly Color Light = new Color(0.93f, 0.85f, 0.68f), Dark = new Color(0.62f, 0.44f, 0.28f);

        void Draw()
        {
            if (view == null) return;
            int kingInCheck = status == 0 && game.InCheck(game.side) ? game.ks[game.side == 1 ? 0 : 1] : -1;
            for (int vi = 0; vi < 64; vi++)
            {
                int sq = Map(vi), p = game.b[sq];
                var bg = (((sq >> 3) + (sq & 7)) & 1) == 0 ? Dark : Light;
                if (sq == lastFrom || sq == lastTo) bg = Color.Lerp(bg, new Color(0.9f, 0.85f, 0.25f), 0.5f);
                if (sq == selected) bg = Color.Lerp(bg, new Color(0.35f, 0.8f, 0.4f), 0.65f);
                if (sq == kingInCheck) bg = Color.Lerp(bg, new Color(0.9f, 0.2f, 0.2f), 0.7f);
                view.Bg(vi, bg);
                view.Piece(vi, p == 0 ? null : Ar6dofGameArt.Piece(SpriteOf[p < 0 ? -p : p], p > 0));
                bool dest = false;
                if (selected >= 0)
                    for (int k = 0; k < nLegal; k++)
                    {
                        int m = legal[k];
                        if (Ar6dofChess.From(m) == selected && Ar6dofChess.To(m) == sq) { dest = true; break; }
                    }
                view.Mark(vi, dest ? new Color(0.1f, 0.45f, 0.15f, 0.65f) : Color.clear, p != 0 ? 0.5f : 0.3f);
            }
            string s;
            if (status == 1) s = game.side == playerSide ? "Echec et mat : le PC gagne." : "Echec et mat : tu gagnes !";
            else if (status == 2) s = "Pat : partie nulle.";
            else if (status == 3) s = "Nulle (regle des 50 coups).";
            else if (status == 4) s = "Nulle (materiel insuffisant).";
            else if (game.side != playerSide) s = "Le PC reflechit...";
            else s = kingInCheck >= 0 ? "Echec ! A toi de jouer." : "A toi de jouer.";
            statusText.text = Ar6dofLoc.T(s);
        }
    }

    // ======================================================================== DAMES
    public sealed class Ar6dofCheckersApp : Ar6dofAppWindow
    {
        static readonly string[] Levels = { "Facile", "Moyen", "Fort" };
        const float Cell = 62f;
        Ar6dofCheckers game = Ar6dofCheckers.Start();
        readonly List<Ar6dofCheckers> history = new List<Ar6dofCheckers>();
        List<Ar6dofCheckers.Move> moves = new List<Ar6dofCheckers.Move>();
        int level = 1, playerSide = 1, selected = -1, lastFrom = -1, lastTo = -1, status;
        Ar6dofAiJob<Ar6dofCheckers.Move> job;
        Ar6dofCheckersAi ai;
        float aiStart;
        Ar6dofBoardView view;
        Text statusText;

        public Ar6dofCheckersApp(Ar6dofDock d) : base(d, "checkers", "Dames", "checkers", "Jeu de dames contre le PC (8 x 8, prise obligatoire) : trois niveaux.", 560f)
        {
            moves = game.Moves();
        }

        protected override void OnRebuildStart() { view = null; statusText = null; }

        public override void OnClosed() { Cancel(); }

        void Cancel() { if (ai != null) ai.cancel = true; ai = null; job = null; }

        void NewGame()
        {
            Cancel();
            game = Ar6dofCheckers.Start();
            history.Clear();
            selected = -1; lastFrom = lastTo = -1; status = 0;
            moves = game.Moves();
            if (game.side != playerSide) StartAi();
            Rebuild();
        }

        void StartAi()
        {
            Cancel();
            var a = new Ar6dofCheckersAi(game, Environment.TickCount);
            int lv = level;
            ai = a;
            job = new Ar6dofAiJob<Ar6dofCheckers.Move>(() => a.Think(lv));
            aiStart = Time.unscaledTime;
        }

        int Map(int i) { int r = i >> 3, c = i & 7; return playerSide == 1 ? (7 - r) * 8 + c : r * 8 + (7 - c); }

        protected override void BuildMain()
        {
            BtnRow(48f, 20,
                Ar6dofLoc.T("Niveau") + " : " + Ar6dofLoc.T(Levels[level]), (Action)(() => { level = (level + 1) % 3; Rebuild(); }),
                Ar6dofLoc.T("Je joue") + " : " + Ar6dofLoc.T(playerSide == 1 ? "Blancs" : "Noirs"), (Action)(() => { playerSide = -playerSide; NewGame(); }),
                "Nouvelle partie", (Action)NewGame);
            statusText = Lbl("", 40, 25, TextAnchor.MiddleCenter);
            statusText.fontStyle = FontStyle.Bold;
            Space(2f);
            view = Ar6dofBoardView.Make(content, 0f, y, Cell, 8, OnCell, false);
            y += 8 * Cell + Gap;
            BtnRow(46f, 20, "Annuler mon dernier coup", (Action)Undo);
            Draw();
        }

        void Undo()
        {
            if (history.Count == 0) return;
            Cancel();
            game = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            selected = -1; lastFrom = lastTo = -1; status = 0;
            moves = game.Moves();
            Draw();
        }

        bool MyTurn { get { return status == 0 && game.side == playerSide && job == null; } }

        void OnCell(int vi)
        {
            if (!MyTurn) return;
            int sq = Map(vi), p = game.b[sq];
            if (selected >= 0)
            {
                for (int k = 0; k < moves.Count; k++)
                {
                    var m = moves[k];
                    if (m.path[0] == selected && m.path[m.path.Length - 1] == sq)
                    {
                        history.Add(game.Clone());
                        if (history.Count > 200) history.RemoveAt(0);
                        Apply(m);
                        return;
                    }
                }
            }
            selected = (p != 0 && (p > 0) == (playerSide > 0) && sq != selected) ? sq : -1;
            Draw();
        }

        void Apply(Ar6dofCheckers.Move m)
        {
            game.Apply(m);
            lastFrom = m.path[0]; lastTo = m.path[m.path.Length - 1];
            selected = -1;
            status = game.Status();
            moves = game.Moves();
            if (status == 0 && game.side != playerSide) StartAi();
            Draw();
        }

        protected override void OnTick()
        {
            if (job != null && job.Done && Time.unscaledTime > aiStart + 0.4f)
            {
                var m = job.Failed ? null : job.Result;
                job = null; ai = null;
                if (m == null && status == 0 && moves.Count > 0) m = moves[0];
                if (m != null && status == 0) Apply(m);
                else Draw();
            }
        }

        static readonly Color Light = new Color(0.92f, 0.82f, 0.62f), Dark = new Color(0.4f, 0.27f, 0.18f);

        void Draw()
        {
            if (view == null) return;
            bool mustCapture = moves.Count > 0 && moves[0].caps.Length > 0;
            for (int vi = 0; vi < 64; vi++)
            {
                int sq = Map(vi), p = game.b[sq];
                var bg = (((sq >> 3) + (sq & 7)) & 1) == 0 ? Dark : Light;
                if (sq == lastFrom || sq == lastTo) bg = Color.Lerp(bg, new Color(0.9f, 0.85f, 0.25f), 0.45f);
                if (sq == selected) bg = Color.Lerp(bg, new Color(0.35f, 0.8f, 0.4f), 0.65f);
                view.Bg(vi, bg);
                view.Piece(vi, p == 0 ? null : Ar6dofGameArt.Checker(p == 2 || p == -2, p > 0));
                bool dest = false;
                if (selected >= 0)
                    for (int k = 0; k < moves.Count; k++)
                        if (moves[k].path[0] == selected && moves[k].path[moves[k].path.Length - 1] == sq) { dest = true; break; }
                view.Mark(vi, dest ? new Color(0.4f, 0.95f, 0.45f, 0.8f) : Color.clear, 0.34f);
            }
            string s;
            if (status == 1) s = game.side == playerSide ? "Plus de coup : le PC gagne." : "Le PC n'a plus de coup : tu gagnes !";
            else if (status == 2) s = "Partie nulle.";
            else if (game.side != playerSide) s = "Le PC reflechit...";
            else s = mustCapture ? "Prise obligatoire !" : "A toi de jouer.";
            statusText.text = Ar6dofLoc.T(s);
        }
    }
}
