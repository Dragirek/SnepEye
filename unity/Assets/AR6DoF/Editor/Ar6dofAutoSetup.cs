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

        /// <summary>Dossier d'ar6dof_probe : celui de Launcher > Exe Path (scene ouverte) s'il est renseigne, sinon ar6dof_probe-win64 a cote de Assets, sinon le dernier chemin retenu.</summary>
        public static string ProbeDir
        {
            get
            {
                var l = Object.FindFirstObjectByType<Ar6dofLauncher>();
                return Ar6dofProbePaths.Dir(l != null ? l.exePath : "");
            }
        }

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
        static void Check() { Check(0); }

        static void Check(int depth)
        {
            bool dll = EnsureDll(true);
            var l = Object.FindFirstObjectByType<Ar6dofLauncher>();
            string field = l != null ? l.exePath : null;
            string pd = ProbeDir;
            bool exe = File.Exists(Path.Combine(pd, "ar6dof_probe.exe"));
            bool model = File.Exists(Path.Combine(pd, "camera_model.json"));
            string src = l == null ? "aucun Launcher dans la scene ouverte (ouvre ta scene AR6DoF)" : "Launcher > Exe Path = \"" + field + "\"";
            string msg =
                "Dossier d'ar6dof_probe : " + pd + "\n" +
                "(" + src + ")\n\n" +
                "ar6dof_probe.exe : " + (exe ? "trouve" : "ABSENT") + "\n" +
                "camera_model.json : " + (model ? "trouve (lu directement dans ce dossier)" : "absent de ce dossier : ar6dof_probe l'ecrit a cote de lui quand la calibration est chargee (lance le suivi une fois)") + "\n" +
                "ar6dof_screen.dll : " + (dll ? "dans le projet" : "ABSENTE") + "\n" +
                "Direct3D 11 : " + (UnityEditor.PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64).Length == 1 &&
                                     UnityEditor.PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64)[0] == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ? "oui" : "NON : menu AR6DoF > Forcer Direct3D11");
            if (exe || depth > 0)
            {
                EditorUtility.DisplayDialog("AR6DoF", msg, "OK");
                return;
            }
            // ar6dof_probe.exe introuvable : on propose de le designer, et Unity s'en souvient (aussi pour la DLL et camera_model.json)
            if (!EditorUtility.DisplayDialog("AR6DoF", msg + "\n\nDesigner ar6dof_probe.exe maintenant ?", "Choisir ar6dof_probe.exe...", "Plus tard")) return;
            string pick = EditorUtility.OpenFilePanel("Choisir ar6dof_probe.exe", string.IsNullOrEmpty(pd) ? "" : pd, "exe");
            string found = Ar6dofProbePaths.Normalize(pick);
            if (found.Length == 0) return;
            Ar6dofProbePaths.Remember(found);
            if (l != null)
            {
                Undo.RecordObject(l, "Exe Path AR6DoF");
                l.exePath = found;
                EditorUtility.SetDirty(l);
            }
            EnsureDll(true);
            Check(1);
        }
    }
}
#endif
