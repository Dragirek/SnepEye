using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    // Puissance 4 et demineur : deux jeux de plateau de plus, en 3D sur la table comme les autres (2D si la vue 3D est coupee).

    // ======================================================================== PUISSANCE 4
    public sealed class Ar6dofConnect4App : Ar6dofGameWindow
    {
        static readonly string[] Levels = { "Facile", "Moyen", "Fort" };
        const float Cell = 62f;
        static readonly Color Blue = new Color(0.1f, 0.27f, 0.72f), BlueHover = new Color(0.22f, 0.42f, 0.92f), BlueWin = new Color(0.2f, 0.62f, 0.4f);
        static readonly Color Red = new Color(0.9f, 0.14f, 0.16f), Yellow = new Color(1f, 0.85f, 0.15f), Hole = new Color(0.04f, 0.06f, 0.14f);
        Ar6dofConnect4 game = new Ar6dofConnect4();
        int level = 1, status, winner, wins, losses, draws, hoverCol = -1, dropCell = -1;
        bool pcFirst;
        Ar6dofAiJob<int> job;
        Ar6dofConnect4Ai ai;
        float aiStart;
        Ar6dofBoardView view;
        Text statusText, scoreText;

        public Ar6dofConnect4App(Ar6dofDock d) : base(d, "connect4", "Puissance 4", "connect4", "Puissance 4 contre le PC : aligne quatre jetons avant lui, trois niveaux.", 560f) { }

        int Me { get { return pcFirst ? 2 : 1; } }      // le premier joueur a les jetons rouges

        protected override void GameRebuildStart() { view = null; statusText = null; scoreText = null; }

        public override void OnClosed() { Cancel(); }

        void Cancel() { if (ai != null) ai.cancel = true; ai = null; job = null; }

        void NewGame()
        {
            Cancel();
            game = new Ar6dofConnect4();
            status = 0; winner = 0; dropCell = -1; hoverCol = -1;
            if (game.side != Me) StartAi();
            Rebuild();
        }

        void StartAi()
        {
            Cancel();
            var a = new Ar6dofConnect4Ai(game, Environment.TickCount);
            int lv = level;
            ai = a;
            job = new Ar6dofAiJob<int>(() => a.Think(lv));
            aiStart = Time.unscaledTime;
        }

        protected override void BuildMain()
        {
            ViewBar();
            BtnRow(48f, 20,
                Ar6dofLoc.T("Niveau") + " : " + Ar6dofLoc.T(Levels[level]), (Action)(() => { level = (level + 1) % 3; NewGame(); }),
                Ar6dofLoc.T("Commence") + " : " + Ar6dofLoc.T(pcFirst ? "PC" : "Moi"), (Action)(() => { pcFirst = !pcFirst; NewGame(); }),
                "Nouvelle partie", (Action)NewGame);
            statusText = Lbl("", 40, 25, TextAnchor.MiddleCenter);
            statusText.fontStyle = FontStyle.Bold;
            Space(2f);
            view = NewBoard(0f, y, Cell, Ar6dofConnect4.Rows, Ar6dofConnect4.Cols, OnCell, false, 0f, 0.052f, new Color(0.08f, 0.1f, 0.22f));
            view.OnHover = (i, on) =>
            {
                int c = i % Ar6dofConnect4.Cols;
                if (on) hoverCol = c; else if (hoverCol == c) hoverCol = -1;
                Draw();
            };
            y += view.Space(Cell);
            scoreText = Lbl("", 34, 21, TextAnchor.MiddleCenter);
            Draw();
        }

        bool MyTurn { get { return status == 0 && game.side == Me && job == null; } }

        void OnCell(int i)
        {
            if (!MyTurn) return;
            int col = i % Ar6dofConnect4.Cols;
            if (game.DropRow(col) < 0) return;
            Apply(col);
        }

        void Apply(int col)
        {
            int cell = game.Play(col);
            if (cell < 0) { Draw(); return; }
            dropCell = cell;
            status = game.Status();
            if (status == 1)
            {
                winner = game.b[cell];
                if (winner == Me) wins++; else losses++;
            }
            else if (status == 2) draws++;
            if (status == 0 && game.side != Me) StartAi();
            Draw();
        }

        protected override void GameTick()
        {
            if (job != null && job.Done && Time.unscaledTime > aiStart + 0.5f)
            {
                int col = job.Failed ? -1 : job.Result;
                job = null; ai = null;
                if (status != 0) { Draw(); return; }
                if (col < 0 || col >= Ar6dofConnect4.Cols || game.DropRow(col) < 0)
                {
                    col = -1;
                    for (int c = 0; c < Ar6dofConnect4.Cols; c++) if (game.DropRow(c) >= 0) { col = c; break; }
                }
                if (col >= 0) Apply(col); else Draw();
            }
        }

        void Draw()
        {
            if (view == null) return;
            bool hoverOn = MyTurn;
            for (int i = 0; i < Ar6dofConnect4.Rows * Ar6dofConnect4.Cols; i++)
            {
                int p = game.b[i], c = i % Ar6dofConnect4.Cols;
                bool win = false;
                if (game.winCells != null) for (int k = 0; k < game.winCells.Length; k++) if (game.winCells[k] == i) win = true;
                view.Bg(i, win ? BlueWin : (hoverOn && hoverCol == c ? BlueHover : Blue));
                int tok = p == 0 ? 0 : (p == 1 ? Ar6dofShapes.TokRed : Ar6dofShapes.TokYellow);
                var sp = p == 0 ? null : Ar6dofGameArt.Disc();
                if (i == dropCell) view.PieceDrop(i, sp, tok, 0.26f); else view.Piece(i, sp, tok);
                if (p != 0) view.Tint(i, p == 1 ? Red : Yellow);
                if (p == 0) view.Mark(i, Hole, 0.74f);
                else view.Mark(i, win ? new Color(1f, 1f, 1f, 0.95f) : Color.clear, 0.3f);
            }
            dropCell = -1;
            Result(status == 0 ? 0 : (status == 1 ? (winner == Me ? 1 : 2) : 3));
            string s;
            if (status == 1) s = winner == Me ? "Quatre jetons alignes : tu gagnes !" : "Le PC a aligne quatre jetons.";
            else if (status == 2) s = "Grille pleine : egalite.";
            else if (game.side != Me) s = "Le PC reflechit...";
            else s = "A toi de jouer : choisis une colonne.";
            statusText.text = Ar6dofLoc.T(s);
            scoreText.text = Ar6dofLoc.T("Tes jetons") + " : " + Ar6dofLoc.T(Me == 1 ? "rouges" : "jaunes") + "     " + Ar6dofLoc.T("Gagnees") + " " + wins + "   " + Ar6dofLoc.T("Perdues") + " " + losses + "   " + Ar6dofLoc.T("Nulles") + " " + draws;
        }
    }

    // ======================================================================== DEMINEUR
    public sealed class Ar6dofMinesApp : Ar6dofGameWindow
    {
        static readonly string[] Levels = { "Debutant", "Intermediaire", "Expert" };
        static readonly int[] Size = { 9, 12, 16 }, Count = { 10, 24, 40 };
        static readonly float[] Cell2D = { 48f, 40f, 31f }, Cell3D = { 0.05f, 0.042f, 0.034f };
        static readonly Color Closed2D = new Color(0.55f, 0.62f, 0.76f), Closed3D = new Color(0.3f, 0.35f, 0.47f), Opened = new Color(0.16f, 0.18f, 0.24f), Boom = new Color(0.85f, 0.16f, 0.14f), Wrong = new Color(0.55f, 0.2f, 0.2f);
        readonly System.Random rnd = new System.Random();
        Ar6dofMines game;
        int level, lastSec = -1, endSec = -1;
        float startT = -1f;
        bool flagTool;
        Ar6dofBoardView view;
        Text header, statusText;
        Text toolLabel;

        public Ar6dofMinesApp(Ar6dofDock d) : base(d, "mines", "Demineur", "mines", "Demineur : ouvre toutes les cases sans mine et pose des drapeaux sur les autres. Trois tailles.", 560f)
        {
            game = new Ar6dofMines(Size[0], Size[0], Count[0], rnd);
        }

        protected override void GameRebuildStart() { view = null; header = null; statusText = null; toolLabel = null; }

        void NewGame()
        {
            game = new Ar6dofMines(Size[level], Size[level], Count[level], rnd);
            startT = -1f; endSec = -1; lastSec = -1;
            Rebuild();
        }

        string ToolText() { return Ar6dofLoc.T("Outil") + " : " + Ar6dofLoc.T(flagTool ? "Drapeau" : "Creuser"); }

        protected override void BuildMain()
        {
            ViewBar();
            var bs = BtnRow(48f, 20,
                Ar6dofLoc.T("Niveau") + " : " + Ar6dofLoc.T(Levels[level]), (Action)(() => { level = (level + 1) % 3; NewGame(); }),
                ToolText(), (Action)(() => { flagTool = !flagTool; if (toolLabel != null) toolLabel.text = ToolText(); }),
                "Nouvelle partie", (Action)NewGame);
            toolLabel = bs.Count > 1 ? bs[1].GetComponentInChildren<Text>() : null;
            header = Lbl("", 38, 25, TextAnchor.MiddleCenter);
            header.fontStyle = FontStyle.Bold;
            statusText = Lbl("", 36, 22, TextAnchor.MiddleCenter);
            Space(2f);
            view = NewBoard(0f, y, Cell2D[level], game.rows, game.cols, OnCell, true, 0f, Cell3D[level], new Color(0.1f, 0.12f, 0.17f));
            view.OnRightClick = OnFlag;
            y += view.Space(Cell2D[level]);
            Para("Creuser : touche une case. Touche un chiffre dont les drapeaux sont poses pour ouvrir ses voisines d'un coup. Bouton droit du laser ou outil Drapeau : poser / enlever un drapeau. La premiere case n'est jamais une mine.", 90, 17);
            Draw();
        }

        void OnCell(int i)
        {
            if (game.state >= 2) return;
            if (flagTool) game.ToggleFlag(i);
            else if (game.open[i]) game.Chord(i);
            else game.Open(i);
            AfterMove();
        }

        void OnFlag(int i)
        {
            if (game.state >= 2) return;
            game.ToggleFlag(i);
            AfterMove();
        }

        void AfterMove()
        {
            if (game.state == 1 && startT < 0f) startT = Time.unscaledTime;
            if (game.state >= 2 && endSec < 0) endSec = startT < 0f ? 0 : (int)(Time.unscaledTime - startT);
            Draw();
        }

        protected override void GameTick()
        {
            if (game.state == 1 && startT >= 0f)
            {
                int s = (int)(Time.unscaledTime - startT);
                if (s != lastSec) { lastSec = s; Header(); }
            }
        }

        void Header()
        {
            if (header == null) return;
            int s = game.state >= 2 ? Mathf.Max(0, endSec) : (startT < 0f ? 0 : (int)(Time.unscaledTime - startT));
            header.text = Ar6dofLoc.T("Mines") + " : " + (game.mineCount - game.flags) + "      " + Ar6dofLoc.T("Temps") + " : " + (s / 60) + ":" + (s % 60).ToString("00");
        }

        static readonly Color[] DigitCol =
        {
            Color.white, new Color(0.45f, 0.65f, 1f), new Color(0.4f, 0.88f, 0.5f), new Color(1f, 0.45f, 0.45f), new Color(0.7f, 0.55f, 1f),
            new Color(1f, 0.65f, 0.3f), new Color(0.35f, 0.9f, 0.9f), Color.white, new Color(0.75f, 0.75f, 0.78f),
        };

        void Draw()
        {
            if (view == null) return;
            bool lost = game.state == 3;
            bool d3 = view.Is3D;
            for (int i = 0; i < game.rows * game.cols; i++)
            {
                Color bg;
                int tok = 0;
                string cap = "";
                Color capCol = Color.white;
                if (game.open[i])
                {
                    bg = Opened;
                    if (game.mine[i]) { bg = Boom; tok = Ar6dofShapes.TokMine; cap = "*"; }
                    else if (game.near[i] > 0) { tok = 600 + game.near[i]; cap = game.near[i].ToString(); capCol = DigitCol[game.near[i]]; }
                }
                else if (game.flag[i])
                {
                    bg = d3 ? Closed3D : Closed2D;
                    tok = Ar6dofShapes.TokFlag; cap = "F"; capCol = new Color(0.9f, 0.1f, 0.1f);
                    if (lost && !game.mine[i]) bg = Wrong;
                }
                else
                {
                    bg = d3 ? Closed3D : Closed2D;
                    tok = Ar6dofShapes.TokHidden;
                    if (lost && game.mine[i]) { bg = Opened; tok = Ar6dofShapes.TokMine; cap = "*"; capCol = new Color(0.1f, 0.1f, 0.1f); }
                }
                view.Bg(i, bg);
                if (d3) view.Token(i, tok);
                else view.Caption(i, cap, capCol);
            }
            Header();
            Result(game.state == 2 ? 1 : (game.state == 3 ? 2 : 0));
            string s;
            if (game.state == 2) s = "Bravo : toutes les mines sont reperees !";
            else if (game.state == 3) s = "Boum ! Tu as marche sur une mine.";
            else if (game.state == 0) s = "Touche une case pour commencer.";
            else s = "Continue : ouvre les cases sans mine.";
            statusText.text = Ar6dofLoc.T(s);
        }
    }
}
