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
        const string Pref = "ar6dof_games_3d", PrefSound = "ar6dof_games_sound", PrefVol = "ar6dof_games_volume", PrefFloor = "ar6dof_floor_drop";
        protected Ar6dofTable3D table;
        protected readonly Ar6dofGameSound snd = new Ar6dofGameSound();
        bool failed;
        Ar6dofLabel3D banner;
        int bannerKey = -1, lastKind;

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

        /// <summary>De combien de metres sous ta tete se trouve le sol, pour les pieces qui tombent.</summary>
        public static float FloorDrop
        {
            get { try { return Mathf.Clamp(PlayerPrefs.GetFloat(PrefFloor, 1.5f), 0.6f, 2.4f); } catch (Exception) { return 1.5f; } }
            set { try { PlayerPrefs.SetFloat(PrefFloor, Mathf.Clamp(value, 0.6f, 2.4f)); } catch (Exception) { } }
        }

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
            BtnRow(44f, 20, items.ToArray());
            if (withSound) ToggleRow("Son", () => SoundOn, v => SoundOn = v, 44f, 20);
            if (Is3D) Para("Le jeu est pose devant toi : vise-le au laser. Garde le bouton appuye sur le socle pour le deplacer.", 44, 17);
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

        protected sealed override void OnRebuildStart()
        {
            banner = null; bannerKey = -1;
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
            if (!on) { snd.StopWhirr(); snd.Dispose(); DisposeTable(); }
        }

        public override void Destroy()
        {
            snd.Dispose();
            DisposeTable();
            base.Destroy();
        }
    }
}
