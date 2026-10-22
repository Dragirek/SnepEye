#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AR6DoF
{
    /// <summary>
    /// Menu Unity « AR6DoF > Creer la scene complete » : crée les objets de la scène, y ajoute les bons composants et remplit tous les champs.
    /// À utiliser dans une scène vide (ou après avoir supprimé les anciens objets AR6DoF).
    /// </summary>
    public static class Ar6dofSceneBuilder
    {
        static T Add<T>(GameObject go) where T : Component { return Undo.AddComponent<T>(go); }

        static GameObject New(string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "AR6DoF " + name);
            return go;
        }

        /// <summary>Cherche un fichier du projet par son nom exact (sans extension) et son type.</summary>
        static T Find<T>(string fileName) where T : Object
        {
            foreach (string guid in AssetDatabase.FindAssets(fileName + " t:" + typeof(T).Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == fileName) return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return null;
        }

        static void AlwaysInclude(Shader s)
        {
            if (s == null) return;
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) return;
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == s) return;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = s;
            so.ApplyModifiedProperties();
        }

        /// <summary>ar6dof_screen.dll ne sait parler qu'à Direct3D 11 : avec Direct3D 12 (défaut des nouveaux projets Unity 6) ou Vulkan, Unity plante.</summary>
        [MenuItem("AR6DoF/Forcer Direct3D11 (obligatoire pour les ecrans virtuels)")]
        static void ForceD3D11Menu()
        {
            ForceD3D11();
            EditorUtility.DisplayDialog("AR6DoF", "Direct3D 11 est maintenant la seule API graphique Windows du projet.\nREDEMARRE UNITY pour que ce soit pris en compte.", "OK");
        }

        static bool ForceD3D11()
        {
            var t = BuildTarget.StandaloneWindows64;
            var cur = PlayerSettings.GetGraphicsAPIs(t);
            bool ok = !PlayerSettings.GetUseDefaultGraphicsAPIs(t) && cur.Length == 1 && cur[0] == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11;
            if (ok) return false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(t, false);
            PlayerSettings.SetGraphicsAPIs(t, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            return true;
        }

        [MenuItem("AR6DoF/Creer la scene complete")]
        static void Build()
        {
            if (Object.FindFirstObjectByType<Ar6dofReceiver>() != null || Object.FindFirstObjectByType<Ar6dofDock>() != null)
            {
                EditorUtility.DisplayDialog("AR6DoF", "Cette scene contient deja des objets AR6DoF.\nSupprime-les d'abord (Rig, Pointer, Launcher, Screens, Keyboard, Dock...) ou ouvre une scene vide.", "OK");
                return;
            }
            Undo.IncrementCurrentGroup();
            var todo = new List<string>();

            var shComposite = Find<Shader>("Ar6dofComposite");
            var shScreen = Find<Shader>("Ar6dofScreen");
            var shFlat = Find<Shader>("Ar6dofFlat");
            var model = Find<TextAsset>("camera_model");
            if (shComposite == null || shScreen == null) todo.Add("Les fichiers .shader d'AR6DoF sont introuvables : copie bien tout le dossier AR6DoF dans Assets.");
            if (model == null) todo.Add("Copie camera_model.json (a cote de ar6dof_probe.exe) dans Assets, puis glisse-le dans Rig > Ar6dof Passthrough > Camera Model Json.");

            // Rig : reception de la pose, mouvement de la tete, passthrough (cree les 2 cameras des yeux en dessous)
            var rigGo = New("Rig");
            var rec = Add<Ar6dofReceiver>(rigGo);
            var head = Add<Ar6dofHeadRig>(rigGo);
            head.receiver = rec;
            var pt = Add<Ar6dofPassthrough>(rigGo);
            pt.receiver = rec;
            pt.headRig = head;
            pt.compositeShader = shComposite;
            pt.cameraModelJson = model;

            // Lanceur d'ar6dof_probe.exe
            var launchGo = New("Launcher");
            var launcher = Add<Ar6dofLauncher>(launchGo);
            pt.launcher = launcher;

            // Telecommande + laser
            var ptrGo = New("Pointer");
            var remote = Add<Ar6dofRemote>(ptrGo);
            var pointer = Add<Ar6dofPointer>(ptrGo);
            pointer.remote = remote;
            pointer.passthrough = pt;

            // Ecrans virtuels
            var scrGo = New("Screens");
            var screens = Add<Ar6dofScreens>(scrGo);
            screens.passthrough = pt;
            screens.rig = head;
            screens.pointer = pointer;
            screens.screenShader = shScreen;

            // Clavier
            var kbGo = New("Keyboard");
            var kb = Add<Ar6dofKeyboard>(kbGo);
            kb.passthrough = pt;
            kb.keyShader = shFlat;

            // Barre de taches (cree aussi les menus, le Bluetooth, l'eye tracking)
            var dockGo = New("Dock");
            var dock = Add<Ar6dofDock>(dockGo);
            dock.passthrough = pt;
            dock.rig = head;
            dock.pointer = pointer;
            dock.keyboard = kb;
            dock.launcher = launcher;

            // Fenetre du casque (builds) et petit affichage de diagnostic
            New("DisplayStarter").AddComponent<Ar6dofDisplayStarter>();
            var hudGo = New("StatusHud");
            var hud = Add<Ar6dofStatusHud>(hudGo);
            hud.receiver = rec;
            hud.rig = head;

            // La Main Camera est inutile : le casque cree ses propres cameras
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam.CompareTag("MainCamera")) { Undo.DestroyObjectImmediate(cam.gameObject); }

            AlwaysInclude(shComposite);
            AlwaysInclude(shScreen);
            AlwaysInclude(shFlat);
            PlayerSettings.runInBackground = true;
            if (ForceD3D11()) todo.Insert(0, "REDEMARRE UNITY : l'API graphique a ete reglee sur Direct3D11 (obligatoire, sinon les ecrans virtuels font planter Unity).");

            bool dll = false;
            foreach (string g in AssetDatabase.FindAssets("ar6dof_screen"))
                if (AssetDatabase.GUIDToAssetPath(g).EndsWith("ar6dof_screen.dll")) dll = true;
            if (!dll) todo.Add("Copie ar6dof_screen.dll dans Assets/AR6DoF/Plugins/x86_64/ (sinon pas d'ecrans virtuels).");
            todo.Add("Dans Launcher > Exe Path : chemin d'ar6dof_probe.exe (ou place le dossier ar6dof_probe-win64 a cote du dossier Assets).");

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = rigGo;
            EditorUtility.DisplayDialog("AR6DoF", "Scene creee.\n\nIl te reste :\n- " + string.Join("\n- ", todo), "OK");
        }
    }
}
#endif
