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
        // Style d'usine = Eclipse (noir, gris foncé, jaune)
        public Color bg = new Color(0f, 0f, 0f, 1f);                  // fond des panneaux
        public Color button = new Color(0.149f, 0.149f, 0.149f, 1f);  // boutons, touches, poignées
        public Color accent = new Color(1f, 0.902f, 0f, 1f);          // survol, sélection, curseurs, rayon
        public Color text = new Color(1f, 1f, 1f, 1f);
        public float alpha = 0.9f;         // opacité des fonds : 1 = opaque, plus bas = plus transparent
        public int corner = 32;            // arrondi des coins du panneau, en pixels ; les cadres et boutons qu'il contient s'arrondissent en concentrique (voir Ar6dofTheme.Radius)
        public string texture = "";        // "" = aucune ; grid, dots, carbon, stripes, gradient, noise ; ou file:nom d'une image du dossier d'images
        public float texAlpha = 0.18f;     // opacité de l'image de fond
        public int texMode = 1;            // 0 étirer, 1 répéter, 2 miroir, 3 centrée (sans répétition), 4 remplir (recadrée)
        public float texScale = 1f;        // échelle de l'image (répétition, miroir, centrée)
        public float lineWidth = 0.004f;   // épaisseur du rayon (m)

        public Ar6dofStyle Clone() { return (Ar6dofStyle)MemberwiseClone(); }

        /// <summary>Copie l'apparence (couleurs, transparence, arrondi, image de fond) ; l'épaisseur du rayon reste celle de la cible.</summary>
        public void CopyLookFrom(Ar6dofStyle o)
        {
            bg = o.bg; button = o.button; accent = o.accent; text = o.text;
            alpha = o.alpha; corner = o.corner;
            texture = o.texture; texAlpha = o.texAlpha; texMode = o.texMode; texScale = o.texScale;
        }

        /// <summary>Mêmes quatre couleurs (fond, boutons, accent, texte) ?</summary>
        public bool SameColors(Color b, Color bt, Color a, Color t)
        {
            return Near(bg, b) && Near(button, bt) && Near(accent, a) && Near(text, t);
        }

        static bool Near(Color x, Color y) { return Mathf.Abs(x.r - y.r) < 0.006f && Mathf.Abs(x.g - y.g) < 0.006f && Mathf.Abs(x.b - y.b) < 0.006f; }
    }

    /// <summary>Un style nommé par l'utilisateur (menu Style > Couleurs > Mes styles).</summary>
    [Serializable]
    public class Ar6dofCustomStyle
    {
        public string name = "";
        public Ar6dofStyle style = new Ar6dofStyle();
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
        public int version = 2;
        public string language = "";       // "" = automatique (langue de Windows)
        public Ar6dofStyle global = new Ar6dofStyle();
        public Ar6dofElemStyle dock = new Ar6dofElemStyle();
        public Ar6dofElemStyle menus = new Ar6dofElemStyle();
        public Ar6dofElemStyle keyboard = new Ar6dofElemStyle();
        public Ar6dofElemStyle screens = new Ar6dofElemStyle();
        public Ar6dofElemStyle laser = new Ar6dofElemStyle();
        public System.Collections.Generic.List<Ar6dofCustomStyle> customStyles = new System.Collections.Generic.List<Ar6dofCustomStyle>();

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
        public float chromeScale = 1f;     // taille des boutons réduire / fermer / redimensionner (1 = normal)

        // Clavier virtuel : disposition, pavé numérique, animation lumineuse
        public bool kbAzerty = true;       // vrai : AZERTY, faux : QWERTY
        public bool kbNumpad = false;
        public int kbAnim = 0;             // 0 aucune, 1 vague, 2 cycle, 3 respiration, 4 réactif, 5 scintillement, 6 onde
        public int kbAnimDir = 0;          // sens de la vague : 0 droite, 1 gauche, 2 bas, 3 haut, 4 depuis le centre
        public int kbAnimPalette = 0;      // 0 arc-en-ciel, 1.. autres palettes
        public float kbAnimSpeed = 1f;     // 0.1 .. 4
        public float kbAnimStrength = 0.7f;// 0 .. 1 : part de la couleur animée mélangée à la touche

        // Performances
        public int perfFps = 0;            // 0 suit l'écran, 1 = 60, 2 = 90, 3 = 120, 4 = illimité
        public int perfMsaa = 1;           // 1 = pas de lissage des bords (inutile : textes et coins arrondis se lissent déjà), 2 = MSAA x2
        public float perfRenderScale = 1f; // 0.5 .. 1 : résolution de l'interface des yeux (appliqué au lancement suivant)
        public float moveScale = 1f;       // échelle des déplacements de la tête (0,25 .. 4) : corrige un suivi qui sous- ou sur-estime les pas (base entre les objectifs mal réglée)

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
            if (d.customStyles == null) d.customStyles = new System.Collections.Generic.List<Ar6dofCustomStyle>();
            d.customStyles.RemoveAll(x => x == null || x.style == null);
            if (d.version < 2)
            {   // anciens fichiers : si le style n'a jamais été changé (cyan d'origine), on passe au nouveau style d'usine Eclipse
                var g = d.global;
                if (Mathf.Abs(g.accent.r - 0.25f) < 0.01f && Mathf.Abs(g.accent.g - 0.85f) < 0.01f && Mathf.Abs(g.bg.r - 0.08f) < 0.01f && Mathf.Abs(g.button.b - 0.32f) < 0.01f)
                    d.global = new Ar6dofStyle();
                d.version = 2;
            }
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

        // ---- opérations sur CERTAINS champs seulement (une page des réglages VR) : le reste de l'aperçu n'est pas touché ----
        static Ar6dofSettingsData SavedCopy()
        {
            var d = new Ar6dofSettingsData();
            try { JsonUtility.FromJsonOverwrite(savedJson, d); } catch (Exception) { }
            Fix(d);
            return d;
        }

        /// <summary>Vrai si l'un de ces champs a été modifié depuis le dernier enregistrement.</summary>
        public static bool DirtyFields(string[] fields)
        {
            if (cur == null) return false;
            return Ar6dofScope.Sig(cur, fields) != Ar6dofScope.Sig(SavedCopy(), fields);
        }

        /// <summary>Enregistre ces champs seulement : le fichier garde les autres tels qu'ils étaient enregistrés.</summary>
        public static bool SaveFields(string[] fields)
        {
            try
            {
                var saved = SavedCopy();
                Ar6dofScope.Copy(Current, saved, fields);
                Directory.CreateDirectory(FolderPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(saved, true));
                savedJson = JsonUtility.ToJson(saved);
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("AR6DoF : impossible d'ecrire " + FilePath + " : " + e.Message);
                return false;
            }
        }

        /// <summary>Remet ces champs à leur dernier enregistrement.</summary>
        public static void RevertFields(string[] fields)
        {
            Ar6dofScope.Copy(SavedCopy(), Current, fields);
            Notify();
        }

        /// <summary>Remet ces champs aux valeurs d'usine (aperçu : il faut encore enregistrer).</summary>
        public static void ResetFields(string[] fields)
        {
            Ar6dofScope.Copy(new Ar6dofSettingsData(), Current, fields);
            Notify();
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
