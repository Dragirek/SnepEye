using System;
using System.IO;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Les parties de l'interface qui peuvent avoir leur propre style. Global = le style commun à tout.</summary>
    public enum Ar6dofElem { Global = 0, Dock = 1, Menus = 2, Keyboard = 3, Screens = 4, Laser = 5 }

    /// <summary>Un style : couleurs, transparence, arrondi, texture de fond. Pour le rayon : accent = couleur, lineWidth = épaisseur.</summary>
    [Serializable]
    public class Ar6dofStyle
    {
        public Color bg = new Color(0.08f, 0.09f, 0.11f, 1f);       // fond des panneaux
        public Color button = new Color(0.22f, 0.26f, 0.32f, 1f);   // boutons, touches, poignées
        public Color accent = new Color(0.25f, 0.85f, 1f, 1f);      // survol, sélection, curseurs, rayon
        public Color text = new Color(1f, 1f, 1f, 1f);
        public float alpha = 0.88f;        // opacité des fonds : 1 = opaque, plus bas = plus transparent
        public int corner = 14;            // arrondi des coins, en pixels de menu
        public string texture = "";        // "" = aucune ; grid, dots, carbon, stripes, gradient, noise ; ou file:nom d'un .png du dossier textures
        public float texAlpha = 0.18f;     // intensité de la texture
        public float lineWidth = 0.004f;   // épaisseur du rayon (m)

        public Ar6dofStyle Clone() { return (Ar6dofStyle)MemberwiseClone(); }
    }

    /// <summary>Style d'un élément. Tant que 'separate' est faux, l'élément utilise le style commun.</summary>
    [Serializable]
    public class Ar6dofElemStyle
    {
        public bool separate;
        public Ar6dofStyle style = new Ar6dofStyle();
    }

    /// <summary>Tout ce qui est enregistré dans settings.json (paramètres globaux, partagés par tous les projets Unity).</summary>
    [Serializable]
    public class Ar6dofSettingsData
    {
        public int version = 1;
        public string language = "";       // "" = automatique (langue de Windows)
        public Ar6dofStyle global = new Ar6dofStyle();
        public Ar6dofElemStyle dock = new Ar6dofElemStyle();
        public Ar6dofElemStyle menus = new Ar6dofElemStyle();
        public Ar6dofElemStyle keyboard = new Ar6dofElemStyle();
        public Ar6dofElemStyle screens = new Ar6dofElemStyle();
        public Ar6dofElemStyle laser = new Ar6dofElemStyle();

        // Dock
        public bool dockAtFeet = true;     // vrai : accroché à tes pieds, invisible tant que tu ne baisses pas la tête
        public float dockDistance = 0.65f; // devant tes pieds (m)
        public float dockRevealStart = 35f;// angle de la tête vers le bas où il commence à apparaître (deg)
        public float dockRevealFull = 55f; // angle où il est entièrement visible (deg)
        public float dockScale = 1f;
        public float dockTilt = 0f;        // inclinaison en plus (deg) ; 0 = face à tes yeux

        public float keyboardScale = 1f;

        // Boutons de fenêtre (fermer / réduire) de tous les écrans et menus
        public int chromeStyle = 0;        // 0 = style Windows, 1 = style Mac
        public int chromeCorner = 1;       // 0 haut gauche, 1 haut droite, 2 bas gauche, 3 bas droite

        // Télécommande
        public float remoteMinCutoff = 2f;
        public float remoteBeta = 0.15f;
        public float remotePredictMs = 10f;
    }

    /// <summary>
    /// Paramètres globaux de l'interface. Les changements sont d'abord un APERÇU : ils s'appliquent tout de suite mais ne sont gardés
    /// qu'après Save(). Revert() revient au dernier enregistrement, ResetDefaults() aux valeurs d'usine (aperçu aussi).
    /// Fichier : %APPDATA%\AR6DoF\settings.json
    /// </summary>
    public static class Ar6dofSettings
    {
        static Ar6dofSettingsData cur;
        static string savedJson = "";

        /// <summary>Déclenché à chaque changement (couleur, langue, dock...). Les éléments d'interface s'y abonnent.</summary>
        public static event Action Changed;

        public static string FolderPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AR6DoF"); }
        }
        static string FilePath { get { return Path.Combine(FolderPath, "settings.json"); } }

        public static Ar6dofSettingsData Current
        {
            get
            {
                if (cur == null) Load();
                return cur;
            }
        }

        public static void Load()
        {
            var d = new Ar6dofSettingsData();
            try
            {
                if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), d);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("AR6DoF : settings.json illisible (" + e.Message + "), valeurs par defaut."); d = new Ar6dofSettingsData(); }
            Fix(d);
            cur = d;
            savedJson = JsonUtility.ToJson(cur);
        }

        static void Fix(Ar6dofSettingsData d)
        {
            if (d.global == null) d.global = new Ar6dofStyle();
            if (d.dock == null) d.dock = new Ar6dofElemStyle();
            if (d.menus == null) d.menus = new Ar6dofElemStyle();
            if (d.keyboard == null) d.keyboard = new Ar6dofElemStyle();
            if (d.screens == null) d.screens = new Ar6dofElemStyle();
            if (d.laser == null) d.laser = new Ar6dofElemStyle();
            if (d.dock.style == null) d.dock.style = new Ar6dofStyle();
            if (d.menus.style == null) d.menus.style = new Ar6dofStyle();
            if (d.keyboard.style == null) d.keyboard.style = new Ar6dofStyle();
            if (d.screens.style == null) d.screens.style = new Ar6dofStyle();
            if (d.laser.style == null) d.laser.style = new Ar6dofStyle();
        }

        /// <summary>Vrai si des changements n'ont pas été enregistrés.</summary>
        public static bool Dirty { get { return cur != null && JsonUtility.ToJson(cur) != savedJson; } }

        public static bool Save()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
                savedJson = JsonUtility.ToJson(cur);
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("AR6DoF : impossible d'ecrire " + FilePath + " : " + e.Message);
                return false;
            }
        }

        /// <summary>Abandonne les changements non enregistrés.</summary>
        public static void Revert()
        {
            var d = new Ar6dofSettingsData();
            try { JsonUtility.FromJsonOverwrite(savedJson, d); } catch (Exception) { }
            Fix(d);
            cur = d;
            Notify();
        }

        /// <summary>Valeurs d'usine (en aperçu : il faut encore enregistrer).</summary>
        public static void ResetDefaults()
        {
            cur = new Ar6dofSettingsData();
            Notify();
        }

        public static void Notify()
        {
            var h = Changed;
            if (h != null) h();
        }

        public static Ar6dofElemStyle ElemOf(Ar6dofElem e)
        {
            var d = Current;
            switch (e)
            {
                case Ar6dofElem.Dock: return d.dock;
                case Ar6dofElem.Menus: return d.menus;
                case Ar6dofElem.Keyboard: return d.keyboard;
                case Ar6dofElem.Screens: return d.screens;
                case Ar6dofElem.Laser: return d.laser;
                default: return null;
            }
        }

        /// <summary>Style réellement utilisé par un élément : le sien s'il est séparé, sinon le style commun.</summary>
        public static Ar6dofStyle StyleFor(Ar6dofElem e)
        {
            var es = ElemOf(e);
            if (es != null && es.separate && es.style != null) return es.style;
            return Current.global;
        }
    }
}
