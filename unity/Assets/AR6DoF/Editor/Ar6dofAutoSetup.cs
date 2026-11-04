#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Evite de recopier a la main, a chaque nouvelle version, les fichiers qui ne viennent pas d'AR6DoF lui-meme :
    ///  - ar6dof_screen.dll (ecrans virtuels) : copiee depuis le dossier ar6dof_probe-win64 (a cote de Assets) vers Assets/Plugins/x86_64 si elle manque dans le projet.
    ///    Ce dossier est HORS du dossier AR6DoF : supprimer ou remplacer AR6DoF ne l'emporte plus.
    ///  - camera_model.json : lu directement dans le dossier d'ar6dof_probe (voir Ar6dofPassthrough) ;
    ///  - chemin d'ar6dof_probe.exe : retenu par Unity (voir Ar6dofLauncher).
    /// Menu : AR6DoF > Verifier l'installation.
    /// </summary>
    [InitializeOnLoad]
    public static class Ar6dofAutoSetup
    {
        static Ar6dofAutoSetup() { EditorApplication.delayCall += () => EnsureDll(false); }

        static string ProbeDir { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ar6dof_probe-win64")); } }

        static bool DllInProject()
        {
            foreach (string g in AssetDatabase.FindAssets("ar6dof_screen"))
                if (AssetDatabase.GUIDToAssetPath(g).EndsWith("ar6dof_screen.dll")) return true;
            return false;
        }

        /// <summary>Copie ar6dof_screen.dll dans Assets/Plugins/x86_64 si le projet n'en a pas. Vrai si le projet en a une (deja la ou copiee).</summary>
        public static bool EnsureDll(bool report)
        {
            if (DllInProject()) return true;
            string src = Path.Combine(ProbeDir, "ar6dof_screen.dll");
            if (!File.Exists(src))
            {
                if (report) Debug.LogWarning("AR6DoF : ar6dof_screen.dll introuvable (ni dans le projet, ni dans " + ProbeDir + ").");
                return false;
            }
            try
            {
                string dstDir = Path.Combine(Application.dataPath, "Plugins", "x86_64");
                Directory.CreateDirectory(dstDir);
                File.Copy(src, Path.Combine(dstDir, "ar6dof_screen.dll"), true);
                AssetDatabase.ImportAsset("Assets/Plugins/x86_64/ar6dof_screen.dll");
                Debug.Log("AR6DoF : ar6dof_screen.dll copiee dans Assets/Plugins/x86_64 (depuis " + ProbeDir + "). Si les ecrans virtuels restent absents, redemarre Unity une fois.");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("AR6DoF : copie de ar6dof_screen.dll impossible : " + e.Message);
                return false;
            }
        }

        [MenuItem("AR6DoF/Verifier l'installation")]
        static void Check()
        {
            bool dll = EnsureDll(true);
            bool exe = File.Exists(Path.Combine(ProbeDir, "ar6dof_probe.exe"));
            bool model = File.Exists(Path.Combine(ProbeDir, "camera_model.json"));
            EditorUtility.DisplayDialog("AR6DoF",
                "ar6dof_probe.exe : " + (exe ? "trouve" : "ABSENT dans " + ProbeDir + " (ou renseigne Launcher > Exe Path une fois : Unity s'en souvient)") + "\n" +
                "camera_model.json : " + (model ? "trouve (lu directement dans ce dossier)" : "absent : lance ar6dof_probe une fois avec la calibration pour l'exporter") + "\n" +
                "ar6dof_screen.dll : " + (dll ? "dans le projet" : "ABSENTE") + "\n" +
                "Direct3D 11 : " + (UnityEditor.PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64).Length == 1 &&
                                     UnityEditor.PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64)[0] == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ? "oui" : "NON : menu AR6DoF > Forcer Direct3D11"),
                "OK");
        }
    }
}
#endif
