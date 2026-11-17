using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// DVD (categorie Memes du Hub) : un logo « DVD » qui rebondit sur UN ecran statique pose dans le monde (plat, ultra-large, incurve, ancien, portrait).
    /// Rien n'existe ni ne tourne tant qu'aucun ecran n'est pose ; l'ecran pose reste dans le monde quand la fenetre est seulement reduite ; la fermer (croix, ou toucher son icone du Hub) le retire.
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

        protected override bool CanHideMenu { get { return Ar6dofDvdWorld.Screens.Count > 0; } }

        /// <summary>Fermee (pas reduite) : l'ecran pose disparait aussi, il ne reste pas dans le monde sans fenetre pour le retirer.</summary>
        public override void OnClosed()
        {
            Ar6dofDvdWorld.Clear();
            base.OnClosed();
        }

        void PlaceScreen(int type)
        {
            Ar6dofDvdWorld.Prune();
            bool first = Ar6dofDvdWorld.Screens.Count == 0;
            Ar6dofDvdWorld.Place(type, Ar6dofDvdWorld.ScreenWidth);
            if (first) LowerForScreen();
            Rebuild();
        }

        // ---- l'ecran est lie a la fenetre du menu, comme le jeu 3D l'est a la sienne : il est pose au-dessus, le deplacer (la fenetre ou l'ecran au laser) deplace l'ensemble
        const float Gap = 0.03f;
        Ar6dofDvdScreen lastSc;
        Vector3 lastWinPos;
        Quaternion lastWinRot;
        float lastW = -1f, lastK = 1f;
        int lastType = -1;

        Ar6dofDvdScreen Current
        {
            get { Ar6dofDvdWorld.Prune(); return Ar6dofDvdWorld.Screens.Count > 0 ? Ar6dofDvdWorld.Screens[0] : null; }
        }

        /// <summary>
        /// Pose l'ecran juste au-dessus du menu, dans la meme direction : son bord bas est vu sous le meme angle que le haut du menu, meme s'il est plus loin
        /// (un grand ecran reste a bonne distance). Rien n'est recalcule tant que ni la fenetre ni l'ecran ne bougent.
        /// </summary>
        void AttachScreen(bool force)
        {
            var sc = Current;
            if (sc == null || canvas == null) return;
            if (sc.dragTo == null) sc.dragTo = MoveFromScreen;
            Transform wt = canvas.transform;
            if (!force && sc == lastSc && wt.position == lastWinPos && wt.rotation == lastWinRot && sc.width == lastW && sc.type == lastType) return;
            lastSc = sc; lastWinPos = wt.position; lastWinRot = wt.rotation; lastW = sc.width; lastType = sc.type;
            Transform h = Ar6dofDvdWorld.HeadT();
            Vector3 head = h != null ? h.position : wt.position - wt.forward;
            Vector3 flat = wt.position - head; flat.y = 0f;
            float dw = Mathf.Max(0.25f, flat.magnitude);
            Vector3 dir = flat.sqrMagnitude > 1e-4f ? flat / flat.magnitude : Vector3.forward;
            float ds = Mathf.Max(dw, Mathf.Max(1.3f, sc.width * 1.1f));
            lastK = ds / dw;
            float bottomY = head.y + (wt.position.y - head.y) * lastK + Gap * lastK;       // le pivot de la fenetre est en haut : sa position est le haut du menu
            Vector3 center = head + dir * ds;
            center.y = bottomY + sc.Height * 0.5f;
            sc.transform.SetPositionAndRotation(center, Quaternion.LookRotation(dir, Vector3.up));
        }

        /// <summary>Ecran pose pour la premiere fois : la fenetre est baissee de la hauteur apparente de l'ecran, pour que l'ensemble (menu + ecran) soit a la hauteur d'un menu ordinaire.</summary>
        void LowerForScreen()
        {
            var sc = Current;
            if (sc == null || canvas == null || !canvas.gameObject.activeInHierarchy) return;
            AttachScreen(true);
            float rise = Mathf.Clamp(sc.Height / lastK + Gap, 0f, 0.55f);
            canvas.transform.position += Vector3.down * rise;
            AttachScreen(true);
        }

        /// <summary>Le laser deplace l'ecran : c'est la fenetre qui bouge (et l'ecran la suit), en gardant le lien de position calcule par AttachScreen.</summary>
        void MoveFromScreen(Vector3 p)
        {
            Transform h = Ar6dofDvdWorld.HeadT();
            var sc = lastSc;
            if (canvas == null || !open || h == null || sc == null) return;
            Vector3 head = h.position;
            Vector3 flat = p - head; flat.y = 0f;
            float dsp = flat.magnitude;
            if (dsp < 0.05f) return;
            Vector3 dir = flat / dsp;
            float bottom = p.y - sc.Height * 0.5f - Gap * lastK;
            Vector3 pos = head + dir * (dsp / lastK);
            pos.y = head.y + (bottom - head.y) / lastK;
            canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
        }

        protected override void BuildMain()
        {
            line = null; shown = null;
            var sc = Current;

            if (sc == null)
            {
                Section("Poser l'ecran");
                BtnRow(48f, 19,
                    Ar6dofDvdScreen.TypeNames[0], (Action)(() => PlaceScreen(0)),
                    Ar6dofDvdScreen.TypeNames[1], (Action)(() => PlaceScreen(1)),
                    Ar6dofDvdScreen.TypeNames[2], (Action)(() => PlaceScreen(2)));
                BtnRow(48f, 19,
                    Ar6dofDvdScreen.TypeNames[3], (Action)(() => PlaceScreen(3)),
                    Ar6dofDvdScreen.TypeNames[4], (Action)(() => PlaceScreen(4)));
                Para("Aucun ecran pour le moment : choisis un type ci-dessus. Il est pose au-dessus de ce menu ; deplace le menu avec sa barre du bas (ou l'ecran au laser), l'ecran suit.", 70, 17);
            }
            else
            {
                shown = sc;
                line = Lbl("", 30, 18, TextAnchor.MiddleLeft);
                BtnRow(48f, 19,
                    Ar6dofLoc.F("Ecran : {0}", Ar6dofLoc.T(Ar6dofDvdScreen.TypeNames[sc.type])), (Action)(() => PlaceScreen((sc.type + 1) % Ar6dofDvdScreen.TypeNames.Length)),
                    "Retirer l'ecran", (Action)(() => { Ar6dofDvdWorld.Remove(sc); Rebuild(); }));
                SliderRow("Largeur", 0.4f, 2.6f, () => sc != null ? sc.width : 1.2f, v => { if (sc != null) sc.Resize(v); }, v => Fmt("{0:0.0} m", v), 50f);
            }
            RefreshLine();

            string logo = Ar6dofLoc.T("Logo") + " : " + Ar6dofLoc.T(LogoNames[Ar6dofDvdWorld.LogoType]);
            Action nextLogo = () => { Ar6dofDvdWorld.LogoType = (Ar6dofDvdWorld.LogoType + 1) % 3; Rebuild(); };
            if (Ar6dofDvdWorld.LogoType == 2)
                BtnRow(48f, 20,
                    logo, nextLogo,
                    "Choisir mon image", (Action)(() => Browse("Choisir une image", ImageExt, false, f => { if (f != null && f.Count > 0) { Ar6dofDvdWorld.LogoPath = f[0]; Ar6dofDvdWorld.LogoType = 2; } Rebuild(); }, Ar6dofLibrary.Stickers)));
            else
                BtnRow(48f, 20, logo, nextLogo);
            if (Ar6dofDvdWorld.LogoType == 2 && Ar6dofDvdWorld.CustomSprite() == null)
                Para(Ar6dofLoc.F("Aucune image valable choisie : le mot DVD est affiche. Les images se posent dans {0}.", Ar6dofLibrary.Pretty(Ar6dofLibrary.Stickers)), 44, 16);
            SliderRow("Vitesse", 0.04f, 0.6f, () => Ar6dofDvdWorld.Speed, v => Ar6dofDvdWorld.Speed = v, v => Mathf.RoundToInt(v * 100f) + " %", 50f);
            SliderRow("Taille du logo", 0.05f, 0.35f, () => Ar6dofDvdWorld.LogoSize, v => Ar6dofDvdWorld.LogoSize = v, v => Mathf.RoundToInt(v * 100f) + " %", 50f);
            ToggleRow("Change de couleur a chaque rebond", () => Ar6dofDvdWorld.ColorChange, v => Ar6dofDvdWorld.ColorChange = v, 46f, 20);
            MuteRow("Son des rebonds", () => Ar6dofDvdWorld.BounceSound, v => Ar6dofDvdWorld.BounceSound = v, 46f, 20);
        }

        void RefreshLine()
        {
            if (line == null || shown == null) return;
            line.text = Ar6dofLoc.T(Ar6dofDvdScreen.TypeNames[shown.type]) + " - " + Fmt("{0:0.0} m", shown.width) + "     " + Ar6dofLoc.T("Rebonds") + " " + shown.bounces + "   " + Ar6dofLoc.T("Coins") + " " + shown.corners;
        }

        protected override void OnTick()
        {
            AttachScreen(false);
            if (line == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            RefreshLine();
        }
    }
}
