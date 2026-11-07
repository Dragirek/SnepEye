using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Base des fenetres de jeux : la fenetre garde les reglages et le score, et le jeu lui-meme peut etre pose en 3D devant toi (reglage commun « Vue : 3D / 2D »).
    /// La table 3D n'existe que tant que la fenetre est ouverte ; si quoi que ce soit echoue en 3D, le jeu retombe sur l'affichage 2D.
    /// </summary>
    public abstract class Ar6dofGameWindow : Ar6dofAppWindow
    {
        const string Pref = "ar6dof_games_3d", PrefSound = "ar6dof_games_sound", PrefVol = "ar6dof_games_volume";
        protected Ar6dofTable3D table;
        protected readonly Ar6dofGameSound snd = new Ar6dofGameSound();
        bool failed;
        Ar6dofLabel3D banner;
        int bannerKey = -1, lastKind;
        bool showRules;
        int rulesPage;
        bool resetPending = true;       // la prochaine ouverture repart d'une partie neuve (jamais ouvert, ou ferme depuis)
        bool shown;                     // fenetre affichee : pas de plateau 3D ni de calcul de jeu quand elle est cachee

        // ---- reglages communs aux jeux (sauvegardes)
        public static bool SoundOn
        {
            get { try { return PlayerPrefs.GetInt(PrefSound, 1) != 0; } catch (Exception) { return true; } }
            set { try { PlayerPrefs.SetInt(PrefSound, value ? 1 : 0); } catch (Exception) { } }
        }

        public static float SoundVolume
        {
            get { try { return Mathf.Clamp01(PlayerPrefs.GetFloat(PrefVol, 0.7f)); } catch (Exception) { return 0.7f; } }
            set { try { PlayerPrefs.SetFloat(PrefVol, Mathf.Clamp01(value)); } catch (Exception) { } }
        }

        /// <summary>De combien de metres sous ta tete se trouve le sol : la hauteur des yeux connue du suivi (sol detecte / verrouille), sinon celle des reglages.</summary>
        protected float FloorDrop
        {
            get
            {
                var r = dock != null ? dock.rig : null;
                if (r != null && r.HasPose && r.HeadHeight > 0.3f) return Mathf.Clamp(r.HeadHeight, 0.4f, 2.6f);
                return Ar6dofHeadRig.ConfiguredEyeHeight(Ar6dofSettings.Current.seated);
            }
        }

        protected Ar6dofGameWindow(Ar6dofDock d, string id, string name, string glyph, string desc, float width) : base(d, id, name, glyph, desc, width) { }

        /// <summary>Construit le jeu dans la fenetre (a la place de BuildMain). Appeler ViewBar() en premier pour avoir Vue 3D / 2D, Regles et Son.</summary>
        protected abstract void GameBuild();

        /// <summary>Remet le jeu a zero, SANS reconstruire la fenetre (le dock la reconstruit juste apres) ; si le PC joue en premier, il est lance ici. Appele a chaque ouverture qui suit une fermeture (et a la premiere) : le jeu demarre tout seul, sans bouton a presser.</summary>
        protected virtual void GameReset() { }

        /// <summary>Fermeture (pas une simple reduction) : arreter ce que le jeu faisait (calcul du PC, tirs en vol, son).</summary>
        protected virtual void GameClosed() { }

        /// <summary>La fenetre est cachee (reduite ou en train de se fermer) : finir ce qui doit l'etre pour ne rien perdre (le tirage d'une machine a sous, par exemple). L'etat de la partie est conserve.</summary>
        protected virtual void GameHidden() { }

        public override void Activate(bool on)
        {
            if (on && resetPending)
            {
                resetPending = false;
                try { GameReset(); }
                catch (Exception e) { Debug.LogWarning("AR6DoF : remise a zero du jeu impossible : " + e); }
            }
            shown = on;
            base.Activate(on);
        }

        /// <summary>Appele par le dock des le DEBUT d'une reduction ou d'une fermeture : le plateau 3D disparait tout de suite (il revient a la reouverture) et le jeu se fige. La partie reste telle quelle.</summary>
        public void HideWorld()
        {
            if (!shown) return;
            shown = false;
            snd.StopWhirr();
            DisposeTable();
            ClearBanner();
            try { GameHidden(); }
            catch (Exception e) { Debug.LogWarning("AR6DoF : jeu cache : " + e.Message); }
        }

        /// <summary>Fermee (croix) : le jeu s'arrete et la prochaine ouverture lance une partie neuve.</summary>
        public sealed override void OnClosed()
        {
            HideWorld();
            try { GameClosed(); }
            catch (Exception e) { Debug.LogWarning("AR6DoF : fermeture du jeu : " + e.Message); }
            snd.StopWhirr(); snd.Dispose();
            DisposeTable();
            showRules = false; rulesPage = 0;
            lastKind = 0;
            resetPending = true;
        }

        protected sealed override void BuildMain()
        {
            if (showRules)
            {
                DisposeTable();       // pas de plateau 3D pendant la lecture des regles ; il revient avec le jeu
                BuildRules();
                return;
            }
            GameBuild();
        }

        void ToggleRules(bool on)
        {
            showRules = on;
            rulesPage = 0;
            Rebuild();
        }

        public static bool Want3D
        {
            get { try { return PlayerPrefs.GetInt(Pref, 1) != 0; } catch (Exception) { return true; } }
        }

        protected bool Is3D { get { return Want3D && !failed; } }

        /// <summary>La table 3D (creee au premier besoin), ou null si le jeu est en 2D.</summary>
        protected Ar6dofTable3D EnsureTable(Color slab)
        {
            if (!Is3D || !shown) return null;
            if (table == null)
            {
                try { table = new Ar6dofTable3D(slab); }
                catch (Exception e) { Fail3D(e); }
            }
            return table;
        }

        protected void Fail3D(Exception e)
        {
            Debug.LogWarning("AR6DoF : jeu 3D impossible, retour en 2D : " + e);
            failed = true;
            DisposeTable();
        }

        void DisposeTable()
        {
            if (table != null) { table.Dispose(); table = null; }
        }

        protected void Play(Ar6dofGameSound.Id id, float vol = 1f)
        {
            snd.on = SoundOn; snd.volume = SoundVolume;
            snd.Play(id, vol);
        }

        /// <summary>Ligne de boutons « Vue : 3D / 2D » et « Ramener le plateau » (+ interrupteur du son).</summary>
        protected void ViewBar(bool withSound = true)
        {
            var items = new List<object>();
            items.Add(Ar6dofLoc.T("Vue") + " : " + (Is3D ? "3D" : "2D"));
            items.Add((Action)ToggleView);
            if (Is3D)
            {
                items.Add("Ramener le plateau");
                items.Add((Action)(() => { if (table != null) table.Place(); }));
            }
            items.Add("Regles");
            items.Add((Action)(() => ToggleRules(true)));
            BtnRow(44f, 20, items.ToArray());
            if (withSound) ToggleRow("Son", () => SoundOn, v => SoundOn = v, 44f, 20);
            if (Is3D) Para("Le jeu est pose devant toi : vise-le au laser. Garde le bouton appuye sur le socle pour le deplacer.", 44, 17);
        }

        // ---------------------------------------------------------------- regles
        static readonly Color RuleLight = new Color(0.86f, 0.78f, 0.62f), RuleDark = new Color(0.45f, 0.3f, 0.2f), RulePlain = new Color(0.2f, 0.24f, 0.32f);
        static readonly Color RuleRed = new Color(0.8f, 0.25f, 0.22f), RuleGreen = new Color(0.3f, 0.7f, 0.35f), RuleYellow = new Color(0.9f, 0.8f, 0.25f);

        /// <summary>Paragraphe dont la hauteur est estimee d'apres la longueur du texte (traduit).</summary>
        Text ParaAuto(string key, int fs)
        {
            string tr = Ar6dofLoc.T(key);
            float cpl = Mathf.Max(12f, IW / (fs * 0.55f));
            int lines = Mathf.CeilToInt(tr.Length / cpl) + 1;
            return Para(key, lines * (fs + 6f) + 6f, fs);
        }

        void BuildRules()
        {
            var pages = Ar6dofRules.For(id);
            Btn("Retour au jeu", () => ToggleRules(false), 46f, 22);
            if (pages == null || pages.Length == 0) { Para("Pas de regles pour ce jeu.", 40, 18); return; }
            if (rulesPage >= pages.Length) rulesPage = 0;
            if (pages.Length > 1)
            {
                var names = new string[pages.Length];
                for (int i = 0; i < pages.Length; i++) names[i] = pages[i].tab;
                Tabs(names, rulesPage, t => rulesPage = t);
            }
            var pg = pages[rulesPage];
            Section(pg.title);
            ParaAuto(pg.text, 18);
            if (pg.diagrams != null && pg.diagrams.Length > 0) DrawDiagrams(pg.diagrams);
        }

        /// <summary>Schemas les uns a cote des autres, chacun avec sa legende dessous.</summary>
        void DrawDiagrams(Ar6dofRuleDiagram[] ds)
        {
            int n = ds.Length, sumCols = 0, maxRows = 0;
            var tok = new string[n][][];
            for (int k = 0; k < n; k++)
            {
                var rows = ds[k].rows;
                tok[k] = new string[rows.Length][];
                for (int r = 0; r < rows.Length; r++) tok[k][r] = rows[r].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                sumCols += tok[k][0].Length;
                maxRows = Mathf.Max(maxRows, rows.Length);
            }
            const float gap = 22f, capH = 58f;
            float cell = Mathf.Min(34f, (IW - gap * (n - 1)) / Mathf.Max(1, sumCols));
            float x = -(sumCols * cell + gap * (n - 1)) * 0.5f;
            Space(6f);
            float top = y;
            for (int k = 0; k < n; k++)
            {
                var d = ds[k];
                int rows = tok[k].Length, cols = tok[k][0].Length;
                float w = cols * cell, cx = x + w * 0.5f;
                var bv = Ar6dofBoardView.Make(content, cx, top, cell, rows, cols, null, d.kind == 2 || d.kind == 4);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols && c < tok[k][r].Length; c++) FillDiagramCell(bv, d.kind, r * cols + c, r, c, tok[k][r][c]);
                var cap = Ar6dofVrUi.Label(content, d.caption, new Vector2(cx, -(top + rows * cell + 6f + capH * 0.5f)), new Vector2(w + gap - 2f, capH), 16, TextAnchor.UpperCenter);
                cap.horizontalOverflow = HorizontalWrapMode.Wrap;
                cap.verticalOverflow = VerticalWrapMode.Truncate;
                x += w + gap;
            }
            y = top + maxRows * cell + capH + 14f;
        }

        void FillDiagramCell(Ar6dofBoardView bv, int kind, int i, int r, int c, string t)
        {
            char ch = t[0];
            bool dot = false;
            Color? hl = null;
            for (int j = 1; j < t.Length; j++)
            {
                switch (t[j])
                {
                    case 'o': dot = true; break;
                    case 'r': hl = RuleRed; break;
                    case 'g': hl = RuleGreen; break;
                    case 'y': hl = RuleYellow; break;
                }
            }
            if (ch == 'o' && kind != 6) { dot = true; ch = '.'; }
            bool dark = ((r + c) & 1) == 1;
            Color bg = RulePlain;
            Sprite piece = null;
            string cap = ""; Color capCol = Color.white;
            Color? tint = null;
            Color? mark = null; float markScale = 0.32f;
            switch (kind)
            {
                case 0:
                    bg = dark ? RuleDark : RuleLight;
                    if (ch != '.')
                    {
                        int pk = "PRNBQK".IndexOf(char.ToUpperInvariant(ch));
                        if (pk >= 0) piece = Ar6dofGameArt.Piece(pk, char.IsUpper(ch));
                    }
                    break;
                case 1:
                    bg = dark ? RuleDark : RuleLight;
                    if (ch != '.') piece = Ar6dofGameArt.Checker(ch == 'W' || ch == 'B', ch == 'w' || ch == 'W');
                    break;
                case 2:
                    if (ch == 'X') { cap = "X"; capCol = new Color(1f, 0.45f, 0.4f); }
                    else if (ch == 'O') { cap = "O"; capCol = new Color(0.5f, 0.78f, 1f); }
                    break;
                case 3:
                    bg = new Color(0.12f, 0.26f, 0.62f);
                    piece = Ar6dofGameArt.Disc();
                    tint = ch == 'R' ? new Color(0.9f, 0.14f, 0.16f) : (ch == 'Y' ? new Color(1f, 0.85f, 0.15f) : new Color(0.05f, 0.09f, 0.22f));
                    break;
                case 4:
                    if (ch == '#' || ch == 'F') bg = new Color(0.58f, 0.66f, 0.8f); else bg = new Color(0.18f, 0.2f, 0.26f);
                    if (ch == 'F') { cap = "F"; capCol = new Color(0.9f, 0.1f, 0.1f); }
                    else if (ch >= '1' && ch <= '8') { cap = ch.ToString(); capCol = Ar6dofShapes.ColorOf(600 + (ch - '0')); }
                    else if (ch == 'M') { mark = new Color(0.05f, 0.05f, 0.07f); markScale = 0.55f; }
                    break;
                case 5:
                    bg = new Color(0.12f, 0.12f, 0.18f);
                    if (ch >= '0' && ch <= '5') piece = Ar6dofGameArt.Slot(ch - '0');
                    break;
                case 6:
                    bg = new Color(0.93f, 0.93f, 0.9f);
                    if (ch == 'o') { mark = new Color(0.1f, 0.1f, 0.12f); markScale = 0.55f; }
                    break;
                case 7:
                    bg = new Color(0.12f, 0.3f, 0.48f);
                    if (ch == '#') bg = new Color(0.55f, 0.6f, 0.67f);
                    else if (ch == 'm') { mark = new Color(0.88f, 0.94f, 1f, 0.9f); markScale = 0.3f; }
                    else if (ch == 'H') { bg = new Color(0.85f, 0.28f, 0.2f); mark = new Color(1f, 0.8f, 0.1f); markScale = 0.45f; }
                    else if (ch == 'S') { bg = new Color(0.45f, 0.08f, 0.08f); mark = new Color(0.1f, 0.1f, 0.1f, 0.8f); markScale = 0.5f; }
                    break;
            }
            if (hl.HasValue) bg = hl.Value;
            bv.Bg(i, bg);
            bv.Piece(i, piece);
            if (tint.HasValue) bv.Tint(i, tint.Value);
            if (cap.Length > 0) bv.Caption(i, cap, capCol);
            if (mark.HasValue) bv.Mark(i, mark.Value, markScale);
            else if (dot) bv.Mark(i, new Color(0.2f, 0.9f, 0.3f, 0.95f), 0.34f);
        }

        // ---------------------------------------------------------------- bannieres Gagne / Perdu / Egalite
        /// <summary>Gros texte flottant au-dessus du jeu (3D seulement). 'key' identifie le message : le meme message n'est pas refait.</summary>
        protected void Banner(int key, string text, Color col, Vector3 localPos, float meters)
        {
            if (key == bannerKey) return;
            ClearBanner();
            bannerKey = key;
            if (table == null || string.IsNullOrEmpty(text)) return;
            try
            {
                float px = Mathf.Max(60f, text.Length * 64f * 0.56f);
                banner = table.AddLabel(null, localPos, meters * 240f / px, 64, col, true);
                banner.Set(text, col);
                table.PopIn(banner.tr, banner.tr.localScale);
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : banniere impossible : " + e.Message); banner = null; }
        }

        protected void ClearBanner()
        {
            if (banner != null && banner.tr != null) UnityEngine.Object.Destroy(banner.tr.gameObject);
            banner = null;
            bannerKey = -1;
        }

        /// <summary>Quelques confettis lances des deux cotes du socle (3D seulement).</summary>
        protected void ShootConfetti(int count)
        {
            if (table == null) return;
            try
            {
                Vector4 b;
                table.TryGetSlab(out b);
                table.Festive.Confetti(count, new Vector3(b.x + 0.03f, 0.02f, 0f), new Vector3(b.y - 0.03f, 0.02f, 0f), Time.unscaledTime, FloorDrop);
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : confettis impossibles : " + e.Message); }
        }

        /// <summary>Fin de partie : 0 en cours, 1 gagne, 2 perdu, 3 egalite. Affiche la banniere et, au moment ou la partie se termine, joue le son (et les confettis si gagne).</summary>
        protected void Result(int kind)
        {
            if (kind == 0) { ClearBanner(); lastKind = 0; return; }
            bool fresh = kind != lastKind;
            lastKind = kind;
            string txt = kind == 1 ? "Gagne !" : (kind == 2 ? "Perdu" : "Egalite");
            Color c = kind == 1 ? new Color(0.4f, 1f, 0.5f) : (kind == 2 ? new Color(1f, 0.4f, 0.38f) : new Color(1f, 0.9f, 0.4f));
            Banner(kind, Ar6dofLoc.T(txt), c, new Vector3(0f, 0.22f, 0.05f), 0.26f);
            if (!fresh) return;
            if (kind == 1) { Play(Ar6dofGameSound.Id.Win2, 0.9f); ShootConfetti(90); }
            else if (kind == 2) Play(Ar6dofGameSound.Id.Lose, 0.8f);
            else Play(Ar6dofGameSound.Id.Win1, 0.5f);
        }

        void ToggleView()
        {
            bool now = !Want3D;
            try { PlayerPrefs.SetInt(Pref, now ? 1 : 0); } catch (Exception) { }
            failed = false;
            if (!now) DisposeTable();
            Rebuild();
        }

        /// <summary>Un plateau : 3D sur la table si la vue 3D est active, sinon dans la fenetre. 'x3' / 'cell3' : position et taille de case sur la table (en metres).</summary>
        protected Ar6dofBoardView NewBoard(float cx, float top, float cell, int n, Action<int> click, bool text, float x3, float cell3, Color slab)
        {
            var tb = EnsureTable(slab);
            if (tb != null)
            {
                try { return Ar6dofBoardView.Make3D(tb, n, cell3, x3, 0f, click); }
                catch (Exception e) { Fail3D(e); }
            }
            return Ar6dofBoardView.Make(content, cx, top, cell, n, click, text);
        }

        /// <summary>Plateau rectangulaire (lignes x colonnes), meme principe que NewBoard.</summary>
        protected Ar6dofBoardView NewBoard(float cx, float top, float cell, int rows, int cols, Action<int> click, bool text, float x3, float cell3, Color slab)
        {
            var tb = EnsureTable(slab);
            if (tb != null)
            {
                try { return Ar6dofBoardView.Make3D(tb, rows, cols, cell3, x3, 0f, click); }
                catch (Exception e) { Fail3D(e); }
            }
            return Ar6dofBoardView.Make(content, cx, top, cell, rows, cols, click, text);
        }

        protected sealed override void OnRebuildStart()
        {
            banner = null; bannerKey = -1;
            if (table != null) table.ClearContent();
            GameRebuildStart();
        }

        protected virtual void GameRebuildStart() { }

        protected sealed override void OnTick()
        {
            if (!shown) return;
            if (table != null) table.Tick();
            GameTick();
        }

        protected virtual void GameTick() { }

        protected override void OnVisible(bool on)
        {
            if (!on) { HideWorld(); snd.StopWhirr(); snd.Dispose(); DisposeTable(); }
        }

        public override void Destroy()
        {
            snd.Dispose();
            DisposeTable();
            base.Destroy();
        }
    }
}
