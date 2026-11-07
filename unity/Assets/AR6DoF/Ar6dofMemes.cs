using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// DVD (categorie Memes du Hub) : un logo « DVD » qui rebondit sur UN ecran statique pose dans le monde (plat, ultra-large, incurve, ancien, portrait).
    /// Rien n'existe ni ne tourne tant qu'aucun ecran n'est pose ; l'ecran pose reste dans le monde meme quand la fenetre est rangee.
    /// </summary>
    public class Ar6dofMemesApp : Ar6dofAppWindow
    {
        static readonly string[] LogoNames = { "DVD", "Disque", "Mon image" };
        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg" };
        Text line;
        Ar6dofDvdScreen shown;
        float nextRefresh;

        public Ar6dofMemesApp(Ar6dofDock d) : base(d, "dvd", "DVD", "disc", "Un logo DVD qui rebondit sur un ecran statique pose dans le monde : plat, ultra-large, incurve, ancien ou portrait.", 560f) { }

        public override void Destroy()
        {
            Ar6dofDvdWorld.Clear();
            base.Destroy();
        }

        void PlaceScreen(int type)
        {
            Ar6dofDvdWorld.Place(type, Ar6dofDvdWorld.ScreenWidth);
            Rebuild();
        }

        protected override void BuildMain()
        {
            line = null; shown = null;
            Ar6dofDvdWorld.Prune();
            var sc = Ar6dofDvdWorld.Screens.Count > 0 ? Ar6dofDvdWorld.Screens[0] : null;

            Section(sc == null ? "Poser l'ecran" : "Changer de type d'ecran");
            BtnRow(48f, 19,
                Ar6dofDvdScreen.TypeNames[0], (Action)(() => PlaceScreen(0)),
                Ar6dofDvdScreen.TypeNames[1], (Action)(() => PlaceScreen(1)),
                Ar6dofDvdScreen.TypeNames[2], (Action)(() => PlaceScreen(2)));
            BtnRow(48f, 19,
                Ar6dofDvdScreen.TypeNames[3], (Action)(() => PlaceScreen(3)),
                Ar6dofDvdScreen.TypeNames[4], (Action)(() => PlaceScreen(4)));

            if (sc == null)
                Para("Aucun ecran pour le moment : choisis un type ci-dessus. Il est pose devant toi ; vise-le au laser et garde le bouton appuye pour le deplacer, il te fait toujours face.", 70, 17);
            else
            {
                shown = sc;
                line = Lbl("", 30, 18, TextAnchor.MiddleLeft);
                BtnRow(42f, 18,
                    "Plus petit", (Action)(() => { if (sc != null) sc.Resize(sc.width / 1.2f); Refresh(); }),
                    "Plus grand", (Action)(() => { if (sc != null) sc.Resize(sc.width * 1.2f); Refresh(); }),
                    "Compteurs a zero", (Action)(() => { if (sc != null) sc.ResetCounters(); RefreshLine(); }),
                    "Retirer l'ecran", (Action)(() => { Ar6dofDvdWorld.Remove(sc); Rebuild(); }));
                Para("Vise l'ecran au laser et garde le bouton appuye pour le deplacer : il te fait toujours face.", 44, 16);
            }
            RefreshLine();

            Section("Logo");
            BtnRow(48f, 20,
                Ar6dofLoc.T("Logo") + " : " + Ar6dofLoc.T(LogoNames[Ar6dofDvdWorld.LogoType]), (Action)(() => { Ar6dofDvdWorld.LogoType = (Ar6dofDvdWorld.LogoType + 1) % 3; Rebuild(); }),
                "Choisir mon image", (Action)(() => Browse("Choisir une image", ImageExt, false, f => { if (f != null && f.Count > 0) { Ar6dofDvdWorld.LogoPath = f[0]; Ar6dofDvdWorld.LogoType = 2; } Rebuild(); }, Ar6dofLibrary.Stickers)));
            if (Ar6dofDvdWorld.LogoType == 2 && Ar6dofDvdWorld.CustomSprite() == null)
                Para(Ar6dofLoc.F("Aucune image valable choisie : le mot DVD est affiche. Les images se posent dans {0}.", Ar6dofLibrary.Pretty(Ar6dofLibrary.Stickers)), 44, 16);
            SliderRow("Vitesse", 0.04f, 0.6f, () => Ar6dofDvdWorld.Speed, v => Ar6dofDvdWorld.Speed = v, v => Mathf.RoundToInt(v * 100f) + " %", 50f);
            SliderRow("Taille du logo", 0.05f, 0.35f, () => Ar6dofDvdWorld.LogoSize, v => Ar6dofDvdWorld.LogoSize = v, v => Mathf.RoundToInt(v * 100f) + " %", 50f);
            ToggleRow("Change de couleur a chaque rebond", () => Ar6dofDvdWorld.ColorChange, v => Ar6dofDvdWorld.ColorChange = v, 46f, 20);
            ToggleRow("Son des rebonds", () => Ar6dofDvdWorld.BounceSound, v => Ar6dofDvdWorld.BounceSound = v, 46f, 20);
            Para("Quand le logo touche un coin : le texte COIN ! s'affiche et un jingle joue (le son des jeux se regle dans les jeux).", 44, 16);
        }

        void RefreshLine()
        {
            if (line == null || shown == null) return;
            line.text = Ar6dofLoc.T(Ar6dofDvdScreen.TypeNames[shown.type]) + " - " + Fmt("{0:0.0} m", shown.width) + "     " + Ar6dofLoc.T("Rebonds") + " " + shown.bounces + "   " + Ar6dofLoc.T("Coins") + " " + shown.corners;
        }

        protected override void OnTick()
        {
            if (line == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            RefreshLine();
        }
    }
}
