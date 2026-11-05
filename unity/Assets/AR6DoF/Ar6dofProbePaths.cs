using System.IO;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Ou est ar6dof_probe ? Une seule reponse pour tout le monde (lanceur, moteur audio, camera_model.json, ar6dof_screen.dll, verification de l'editeur) :
    ///  1. le chemin renseigne dans Launcher > Exe Path (fichier .exe, ou simplement son dossier, guillemets acceptes) s'il existe ;
    ///  2. le dossier ar6dof_probe-win64 a cote de Assets ;
    ///  3. le dernier chemin qui a marche, retenu par Unity (survit a la suppression de la scene ou du dossier AR6DoF).
    /// </summary>
    public static class Ar6dofProbePaths
    {
        const string Key = "ar6dof_probe_exe";
        static string remembered = "";

        public static string DefaultExe
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ar6dof_probe-win64", "ar6dof_probe.exe")); }
        }

        /// <summary>Accepte un .exe, un dossier, des guillemets. Renvoie le chemin du .exe ou "" si rien d'utilisable.</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string p = path.Trim().Trim('"');
            try
            {
                if (Directory.Exists(p)) p = Path.Combine(p, "ar6dof_probe.exe");
                return File.Exists(p) ? Path.GetFullPath(p) : "";
            }
            catch (System.Exception) { return ""; }
        }

        /// <summary>Chemin d'ar6dof_probe.exe (le premier qui existe), ou le chemin par defaut s'il n'y en a aucun.</summary>
        public static string Resolve(string fieldValue)
        {
            string found = Normalize(fieldValue);
            if (found.Length == 0 && File.Exists(DefaultExe)) found = DefaultExe;
            if (found.Length == 0)
            {
                string last = "";
                try { last = PlayerPrefs.GetString(Key, ""); } catch (System.Exception) { }
                found = Normalize(last);
            }
            if (found.Length == 0) return string.IsNullOrWhiteSpace(fieldValue) ? DefaultExe : fieldValue.Trim().Trim('"');
            Remember(found);
            return found;
        }

        /// <summary>Retient ce chemin pour les prochaines fois (seulement s'il a change).</summary>
        public static void Remember(string exe)
        {
            if (exe == remembered) return;
            remembered = exe;
            try { PlayerPrefs.SetString(Key, exe); } catch (System.Exception) { }
        }

        /// <summary>Dossier d'ar6dof_probe pour ce champ 'Exe Path' (ou vide).</summary>
        public static string Dir(string fieldValue)
        {
            try { return Path.GetDirectoryName(Resolve(fieldValue)) ?? ""; } catch (System.Exception) { return ""; }
        }
    }
}
