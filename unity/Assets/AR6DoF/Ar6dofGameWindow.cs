using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Base des fenetres de jeux : la fenetre garde les reglages et le score, et le jeu lui-meme peut etre pose en 3D devant toi (reglage commun « Vue : 3D / 2D »).
    /// La table 3D n'existe que tant que la fenetre est ouverte ; si quoi que ce soit echoue en 3D, le jeu retombe sur l'affichage 2D.
    /// </summary>
    public abstract class Ar6dofGameWindow : Ar6dofAppWindow
    {
        const string Pref = "ar6dof_games_3d";
        protected Ar6dofTable3D table;
        bool failed;

        protected Ar6dofGameWindow(Ar6dofDock d, string id, string name, string glyph, string desc, float width) : base(d, id, name, glyph, desc, width) { }

        public static bool Want3D
        {
            get { try { return PlayerPrefs.GetInt(Pref, 1) != 0; } catch (Exception) { return true; } }
        }

        protected bool Is3D { get { return Want3D && !failed; } }

        /// <summary>La table 3D (creee au premier besoin), ou null si le jeu est en 2D.</summary>
        protected Ar6dofTable3D EnsureTable(Color slab)
        {
            if (!Is3D) return null;
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

        /// <summary>Ligne de boutons « Vue : 3D / 2D » et « Ramener le plateau ».</summary>
        protected void ViewBar()
        {
            var items = new List<object>();
            items.Add(Ar6dofLoc.T("Vue") + " : " + (Is3D ? "3D" : "2D"));
            items.Add((Action)ToggleView);
            if (Is3D)
            {
                items.Add("Ramener le plateau");
                items.Add((Action)(() => { if (table != null) table.Place(); }));
            }
            BtnRow(44f, 20, items.ToArray());
            if (Is3D) Para("Le jeu est pose devant toi : vise-le au laser. Garde le bouton appuye sur le socle pour le deplacer.", 44, 17);
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

        protected sealed override void OnRebuildStart()
        {
            if (table != null) table.ClearContent();
            GameRebuildStart();
        }

        protected virtual void GameRebuildStart() { }

        protected sealed override void OnTick()
        {
            if (table != null) table.Tick();
            GameTick();
        }

        protected virtual void GameTick() { }

        protected override void OnVisible(bool on)
        {
            if (!on) DisposeTable();
        }

        public override void Destroy()
        {
            DisposeTable();
            base.Destroy();
        }
    }
}
