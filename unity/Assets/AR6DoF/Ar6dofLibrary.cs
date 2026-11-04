using System;
using System.IO;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Dossiers de AR6DoF : un dossier par type de contenu (musiques, sons, modèles 3D, stickers), tous dans Documents\AR6DoF.
    /// Les fenêtres d'applications n'affichent que le contenu de LEUR dossier : plus besoin de naviguer dans tout le disque en VR.
    /// Pour ajouter un fichier : « Ouvrir le dossier » (Explorateur Windows) puis on y dépose le fichier.
    /// </summary>
    public static class Ar6dofLibrary
    {
        public const string Music = "Musiques", Sounds = "Sons", Models = "Modeles3D", Stickers = "Stickers";

        public static string Root
        {
            get
            {
                string docs = "";
                try { docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch (Exception) { }
                if (string.IsNullOrEmpty(docs)) docs = Application.persistentDataPath;
                return Path.Combine(docs, "AR6DoF");
            }
        }

        /// <summary>Chemin du dossier (créé s'il n'existe pas).</summary>
        public static string Folder(string name)
        {
            string p = Path.Combine(Root, name);
            try { Directory.CreateDirectory(p); } catch (Exception) { }
            return p;
        }

        /// <summary>Ouvre un dossier dans l'Explorateur Windows.</summary>
        public static void OpenInExplorer(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch (Exception) { }
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception)
            {
                try { Application.OpenURL("file:///" + dir.Replace('\\', '/')); } catch (Exception) { }
            }
        }

        /// <summary>Le chemin tel qu'on l'affiche : Documents\AR6DoF\Musiques.</summary>
        public static string Pretty(string name) { return "Documents\\AR6DoF\\" + name; }
    }
}
