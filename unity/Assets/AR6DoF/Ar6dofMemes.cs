using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Memes : pour l'instant un logo « DVD » qui rebondit sur un ecran statique pose dans le monde (plat, ultra-large, incurve, ancien, portrait). D'autres memes arriveront.
    /// Rien n'existe ni ne tourne tant qu'aucun ecran n'est pose ; les ecrans posees restent dans le monde meme quand la fenetre est rangee.
    /// </summary>
    public class Ar6dofMemesApp : Ar6dofAppWindow
    {
        static readonly string[] LogoNames = { "DVD", "Disque", "Mon image" };
        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg" };
        readonly List<Text> lines = new List<Text>();
        readonly List<Ar6dofDvdScreen> shown = new List<Ar6dofDvdScreen>();
        float nextRefresh;
        string status = "";

        public Ar6dofMemesApp(Ar6dofDock d) : base(d, "memes", "Memes", "memes", "Un logo DVD qui rebondit sur un ecran statique pose dans le monde : plat, ultra-large, incurve, ancien ou portrait.", 560f) { }

        public override void Destroy()
        {
            Ar6dofDvdWorld.Clear();
            base.Destroy();
        }

        void AddScreen(int type)
        {
            var s = Ar6dofDvdWorld.Add(type, Ar6dofDvdWorld.ScreenWidth);
            status = s == null ? Ar6dofLoc.F("Maximum : {0} ecrans", Ar6dofDvdWorld.Max) : "";
            Rebuild();
        }

        protected override void BuildMain()
        {
            lines.Clear(); shown.Clear();
            Ar6dofDvdWorld.Prune();
            Section("Ajouter un ecran statique");
            BtnRow(48f, 19,
                Ar6dofDvdScreen.TypeNames[0], (Action)(() => AddScreen(0)),
                Ar6dofDvdScreen.TypeNames[1], (Action)(() => AddScreen(1)),
                Ar6dofDvdScreen.TypeNames[2], (Action)(() => AddScreen(2)));
            BtnRow(48f, 19,
                Ar6dofDvdScreen.TypeNames[3], (Action)(() => AddScreen(3)),
                Ar6dofDvdScreen.TypeNames[4], (Action)(() => AddScreen(4)));
            SliderRow("Taille des nouveaux ecrans", 0.4f, 2.6f, () => Ar6dofDvdWorld.ScreenWidth, v => Ar6dofDvdWorld.ScreenWidth = v, v => Fmt("{0:0.0} m", v), 50f);
            var st = Para("", 28, 17);
            st.text = status.Length > 0 ? Ar6dofLoc.T(status) : "";
            Para("L'ecran est pose devant toi. Vise-le au laser et garde le bouton appuye pour le deplacer : il te fait toujours face.", 44, 16);

            int n = Ar6dofDvdWorld.Screens.Count;
            Section(Ar6dofLoc.F("Ecrans poses : {0}", n));
            if (n == 0) Para("Aucun ecran pour le moment : utilise un des boutons ci-dessus.", 30, 18);
            for (int i = 0; i < n; i++)
            {
                var sc = Ar6dofDvdWorld.Screens[i];
                if (sc == null) continue;
                shown.Add(sc);
                var ln = Lbl("", 30, 18, TextAnchor.MiddleLeft);
                lines.Add(ln);
                BtnRow(42f, 18,
                    "Plus petit", (Action)(() => { if (sc != null) sc.Resize(sc.width / 1.2f); Refresh(); }),
                    "Plus grand", (Action)(() => { if (sc != null) sc.Resize(sc.width * 1.2f); Refresh(); }),
                    "Compteurs a zero", (Action)(() => { if (sc != null) sc.ResetCounters(); RefreshLines(); }),
                    "Retirer", (Action)(() => { Ar6dofDvdWorld.Remove(sc); Rebuild(); }));
            }
            RefreshLines();

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

        void RefreshLines()
        {
            for (int i = 0; i < lines.Count && i < shown.Count; i++)
            {
                var s = shown[i];
                if (s == null || lines[i] == null) continue;
                lines[i].text = (i + 1) + " - " + Ar6dofLoc.T(Ar6dofDvdScreen.TypeNames[s.type]) + " - " + Fmt("{0:0.0} m", s.width) + "     " + Ar6dofLoc.T("Rebonds") + " " + s.bounces + "   " + Ar6dofLoc.T("Coins") + " " + s.corners;
            }
        }

        protected override void OnTick()
        {
            if (lines.Count == 0 || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            RefreshLines();
        }
    }
}
