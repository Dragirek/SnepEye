using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Une application proposée par le Store.</summary>
    public class Ar6dofAppEntry
    {
        public string id, title, desc, glyph;
        public float yaw;
        public Func<Ar6dofDock, Ar6dofAppWindow> make;
    }

    /// <summary>
    /// Liste des applications que le Store peut ajouter à la barre de tâches. Pour en ajouter une : une classe qui dérive de Ar6dofAppWindow
    /// (voir Ar6dofVoiceApp pour un exemple court) et une ligne ici.
    /// </summary>
    public static class Ar6dofCatalog
    {
        public static readonly List<Ar6dofAppEntry> Entries = new List<Ar6dofAppEntry>
        {
            new Ar6dofAppEntry { id = "voice", title = "Voix", glyph = "mic", yaw = -35f,
                desc = "Micro, voix modifiee (hauteur, robot, echo, filtres...) et sortie sur des enceintes externes, avec la vision du son.",
                make = d => new Ar6dofVoiceApp(d) },
            new Ar6dofAppEntry { id = "soundboard", title = "Soundboard", glyph = "pads", yaw = -71f,
                desc = "Des boutons pour lancer des sons et des effets sur les enceintes, sans couper les precedents. Grille, liste, flux ou cercle.",
                make = d => new Ar6dofSoundboardApp(d) },
            new Ar6dofAppEntry { id = "mixer", title = "Table de mixage", glyph = "mixer", yaw = -107f,
                desc = "Volumes, muets et niveaux de la voix, des sons et de la musique, spectre, et lecteur de musique avec liste de lecture.",
                make = d => new Ar6dofMixerApp(d) },
            new Ar6dofAppEntry { id = "camera", title = "Appareil photo", glyph = "camera", yaw = 71f,
                desc = "Photographie ce que tu vois : avec les interfaces, sans, ou les deux. Minuteur, qualite, format, 3D. Les photos vont dans Images\\AR6DoF\\Photos.",
                make = d => new Ar6dofCameraApp(d) },
            new Ar6dofAppEntry { id = "objects", title = "Objets 3D", glyph = "cube", yaw = 107f,
                desc = "Pose des modeles 3D (.obj), des stickers et des formes dans le monde. Deplacement au laser, reglages, suppression.",
                make = d => new Ar6dofObjectsApp(d) },
        };

        public static Ar6dofAppEntry Find(string id)
        {
            foreach (var e in Entries) if (e.id == id) return e;
            return null;
        }
    }

    /// <summary>Le Store : toujours dans la barre de tâches, juste à gauche des réglages. Ajoute ou retire des applications de la barre.</summary>
    public class Ar6dofStoreApp : Ar6dofAppWindow
    {
        public Ar6dofStoreApp(Ar6dofDock d) : base(d, "store", "Store", "store", "Ajouter ou retirer des applications de la barre", 700f) { }

        protected override void BuildMain()
        {
            Para("Ajoute les applications dont tu as besoin : elles apparaissent dans la barre de taches. Une application fermee ne consomme rien.", 56, 19);
            foreach (var e in Ar6dofCatalog.Entries)
            {
                var en = e;
                const float h = 128f;
                float cy = RowY(h);
                float x0 = -IW * 0.5f;
                var band = Ar6dofVrUi.Rect(content, "Fond", new Vector2(0f, cy), new Vector2(IW, h));
                var bi = band.gameObject.AddComponent<Image>();
                bi.raycastTarget = false;
                Ar6dofTheme.Attach(band.gameObject, Ar6dofRole.Band, 18);

                var g = Ar6dofVrUi.Rect(content, "Icone", new Vector2(x0 + 52f, cy + 10f), new Vector2(64f, 64f));
                var gi = g.gameObject.AddComponent<Image>();
                gi.sprite = Ar6dofDock.Glyph(e.glyph);
                gi.raycastTarget = false;
                Ar6dofTheme.Attach(g.gameObject, Ar6dofRole.Text);

                var name = Ar6dofVrUi.Label(content, e.title, new Vector2(x0 + 52f, cy - 44f), new Vector2(96f, 26f), 17, TextAnchor.MiddleCenter);
                name.fontStyle = FontStyle.Bold;

                var desc = Ar6dofVrUi.Label(content, e.desc, new Vector2(x0 + 104f + (IW - 104f - 200f) * 0.5f, cy), new Vector2(IW - 104f - 200f, h - 16f), 17, TextAnchor.MiddleLeft);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                desc.verticalOverflow = VerticalWrapMode.Truncate;

                Text t1, t2;
                var bAdd = BtnAt(x0 + IW - 92f, cy + 30f, 176f, 52f, "", () => { dock.SetInstalled(en.id, !dock.IsInstalled(en.id)); Rebuild(); }, out t1, 20);
                var bOpen = BtnAt(x0 + IW - 92f, cy - 30f, 176f, 52f, "Ouvrir", () => dock.OpenApp(en.id), out t2, 20);
                Text tt = t1;
                refreshers.Add(() =>
                {
                    bool on = dock.IsInstalled(en.id);
                    tt.text = Ar6dofLoc.T(on ? "Retirer" : "Ajouter");
                    Ar6dofTheme.SetSelected(bAdd, !on);
                    bOpen.gameObject.SetActive(on);
                });
            }
            Section("Prochainement");
            Para("Apprentissage guide de la bouche, matrice de LED sur ESP32, servomoteurs.", 56, 18);
        }
    }
}
