using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Une application proposée par Apps.</summary>
    public class Ar6dofAppEntry
    {
        public string id, title, desc, glyph;
        public float yaw;
        public Func<Ar6dofDock, Ar6dofAppWindow> make;
    }

    /// <summary>
    /// Liste des applications. Pour en ajouter une : une classe qui dérive de Ar6dofAppWindow (voir Ar6dofVoiceApp pour un exemple court)
    /// et une ligne ici. Une application est dans la barre de tâches seulement tant qu'elle est ouverte ; fermée, elle ne consomme rien.
    /// </summary>
    public static class Ar6dofCatalog
    {
        public static readonly List<Ar6dofAppEntry> Entries = new List<Ar6dofAppEntry>
        {
            new Ar6dofAppEntry { id = "voice", title = "Changeur de voix", glyph = "mic", yaw = -35f,
                desc = "Voix modifiee (hauteur, robot, echo, filtres...) avec presets et vision du son.",
                make = d => new Ar6dofVoiceApp(d) },
            new Ar6dofAppEntry { id = "audio", title = "Audio", glyph = "speaker", yaw = -143f,
                desc = "Moteur audio, micro et enceintes externes, pour toutes les applications de son.",
                make = d => new Ar6dofAudioDevicesApp(d) },
            new Ar6dofAppEntry { id = "soundboard", title = "Soundboard", glyph = "pads", yaw = -71f,
                desc = "Des boutons pour lancer des sons et des effets sur les enceintes, sans couper les precedents. Grille, liste, flux ou cercle.",
                make = d => new Ar6dofSoundboardApp(d) },
            new Ar6dofAppEntry { id = "mixer", title = "Table de mixage", glyph = "mixer", yaw = -107f,
                desc = "Volumes, muets et niveaux de la voix, des sons et de la musique, spectre, et lecteur de musique avec liste de lecture.",
                make = d => new Ar6dofMixerApp(d) },
            new Ar6dofAppEntry { id = "camera", title = "Appareil photo", glyph = "camera", yaw = 71f,
                desc = "Apercu en direct de ce que tu vois, et photo a la resolution de la camera, avec ou sans les interfaces. Minuteur, 3D. Les photos vont dans Images\\AR6DoF\\Photos.",
                make = d => new Ar6dofCameraApp(d) },
            new Ar6dofAppEntry { id = "objects", title = "Objets 3D", glyph = "cube", yaw = 107f,
                desc = "Pose des modeles 3D (.obj), des stickers et des formes dans le monde. Deplacement au laser, reglages, suppression.",
                make = d => new Ar6dofObjectsApp(d) },
            new Ar6dofAppEntry { id = "weather", title = "Meteo", glyph = "weather", yaw = 143f,
                desc = "Meteo de ta position en 3D : soleil, nuages, pluie, neige, vent et previsions.",
                make = d => new Ar6dofWeatherApp(d) },
            new Ar6dofAppEntry { id = "mirror", title = "Miroir", glyph = "mirror", yaw = -179f,
                desc = "Une camera libre (derriere ta tete, sur la queue...) dans une fenetre : effet miroir, retournement, taille. Elle ne tourne que fenetre affichee.",
                make = d => new Ar6dofMirrorApp(d) },
            new Ar6dofAppEntry { id = "clock", title = "Horloge", glyph = "clock", yaw = 179f,
                desc = "Heure avec plein de cadrans, chronometre, minuteur et alarmes (a renommer, AM / PM). Les alarmes sonnent meme application fermee.",
                make = d => new Ar6dofClockApp(d) },
        };

        public static Ar6dofAppEntry Find(string id)
        {
            foreach (var e in Entries) if (e.id == id) return e;
            return null;
        }
    }

    /// <summary>
    /// Apps : toutes les applications, avec leur icône et leur nom comme dans la barre de tâches. Toucher une icône ouvre l'application (elle apparaît alors dans
    /// la barre) ; la toucher de nouveau la range. Placé juste à gauche de la roue des réglages.
    /// </summary>
    public class Ar6dofAppsApp : Ar6dofAppWindow
    {
        const int Cols = 4;
        const float TileH = 132f, TileGap = 10f;

        public Ar6dofAppsApp(Ar6dofDock d) : base(d, "apps", "Apps", "apps", "Toutes les applications : touche-en une pour l'ouvrir ou la ranger", 720f) { }

        string deviceSig = "";

        string DeviceSignature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in dock.DeviceMenus) sb.Append(m.DisplayName).Append('|');
            return sb.ToString();
        }

        // un appareil cree, renomme ou supprime : la liste se refait
        protected override void OnTick()
        {
            string sig = DeviceSignature();
            if (sig != deviceSig) Rebuild();
        }

        protected override void BuildMain()
        {
            Para("Touche une application pour l'ouvrir ; elle prend alors sa place dans la barre. La toucher de nouveau la range : une application rangee ne consomme rien.", 56, 18);
            float tileW = (IW - (Cols - 1) * TileGap) / Cols;
            int n = Ar6dofCatalog.Entries.Count;
            for (int i = 0; i < n; i++)
            {
                var e = Ar6dofCatalog.Entries[i];
                string id = e.id;
                Tile(i, tileW, e.glyph, e.title, e.desc, () => dock.ToggleApp(id), () => dock.IsOpen(id));
            }

            Section("Appareils Bluetooth");
            deviceSig = DeviceSignature();
            var menus = dock.DeviceMenus;
            int k = 0;
            foreach (var m in menus)
            {
                var mm = m;
                Tile(k++, tileW, "bluetooth", m.DisplayName, "Menu de cet appareil Bluetooth : touche pour l'afficher ou le ranger.", () => dock.ToggleDeviceMenu(mm), () => dock.IsDocked(mm), false);
            }
            Tile(k, tileW, "plus", "Nouvel appareil", "Cree un menu pour un appareil Bluetooth (ventilateur, LED, telecommande...) : tu choisis l'appareil et ses boutons.", () => dock.NewDeviceMenu(), null, true);

            Section("Prochainement");
            Para("Apprentissage guide de la bouche, matrice de LED sur ESP32, servomoteurs.", 56, 18);
        }

        float rowCy;

        void Tile(int index, float tileW, string glyph, string title, string desc, Action click, Func<bool> isOpen, bool translate = true)
        {
            int col = index % Cols;
            if (col == 0) rowCy = RowY(TileH);
            AppTile(glyph, title, desc, click, isOpen, -IW * 0.5f + tileW * 0.5f + col * (tileW + TileGap), rowCy, tileW, translate);
        }

        void AppTile(string glyph, string title, string desc, Action click, Func<bool> isOpen, float cx, float cy, float w, bool translate)
        {
            Text lbl;
            var b = BtnAt(cx, cy, w, TileH, "", () => click(), out lbl, 18);
            var rt = (RectTransform)b.transform;
            lbl.text = "";

            var g = new GameObject("Glyphe", typeof(RectTransform));
            g.transform.SetParent(rt, false);
            var grt = (RectTransform)g.transform;
            grt.anchoredPosition = new Vector2(0f, 14f);
            grt.sizeDelta = new Vector2(74f, 74f);
            var gi = g.AddComponent<Image>();
            gi.sprite = Ar6dofDock.Glyph(glyph);
            gi.raycastTarget = false;
            Ar6dofTheme.Attach(g, Ar6dofRole.Text);

            string shown = translate || title == "Nouvel appareil" ? Ar6dofLoc.T(title) : title;
            var nl = Ar6dofVrUi.Label(rt, shown, new Vector2(0f, -42f), new Vector2(w - 10f, 28f), 18);
            nl.gameObject.AddComponent<Ar6dofFitText>().Init(nl, 18);

            // pas d'infobulle : le nom et l'icone suffisent

            if (isOpen == null) return;
            var ind = new GameObject("Indicateur", typeof(RectTransform));
            ind.transform.SetParent(rt, false);
            var irt = (RectTransform)ind.transform;
            irt.anchoredPosition = new Vector2(0f, -TileH * 0.5f + 6f);
            irt.sizeDelta = new Vector2(w * 0.4f, 6f);
            var ii = ind.AddComponent<Image>();
            ii.raycastTarget = false;
            Ar6dofTheme.Attach(ind, Ar6dofRole.Accent, 3);

            GameObject indGo = ind;
            refreshers.Add(() => indGo.SetActive(isOpen()));
        }
    }
}
