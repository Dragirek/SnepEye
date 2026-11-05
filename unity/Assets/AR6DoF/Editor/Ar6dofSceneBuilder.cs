#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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

        // ------------------------------------------------------------------ réglages graphiques URP adaptés au projet
        static bool SetP(SerializedObject so, string name, object value, StringBuilder log)
        {
            var p = so.FindProperty(name);
            if (p == null) return false;
            bool changed = false;
            try
            {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean:
                {
                    bool v = (bool)value;
                    changed = p.boolValue != v; p.boolValue = v; break;
                }
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                {
                    int v = (int)value;
                    changed = p.intValue != v; p.intValue = v; break;
                }
                case SerializedPropertyType.Float:
                {
                    float v = System.Convert.ToSingle(value);
                    changed = !Mathf.Approximately(p.floatValue, v); p.floatValue = v; break;
                }
                default: return false;
            }
            }
            catch (System.Exception) { return false; }   // propriété d'un autre type que prévu dans cette version d'URP : on la laisse
            if (changed && log != null) log.AppendLine("  - " + name + " = " + value);
            return changed;
        }

        /// <summary>
        /// Le casque n'affiche que de l'interface : pas de lumière, pas d'ombre, pas de post-traitement. On coupe donc dans l'asset URP du projet
        /// tout ce qui coûte du GPU sans rien apporter : lissage MSAA (les textes et les coins arrondis se lissent déjà), HDR, copie de l'image,
        /// texture de profondeur, lumières et ombres, Forward+ ; et dans le moteur de rendu : l'occlusion ambiante (SSAO), les décalques, l'amorçage de profondeur.
        /// </summary>
        static string OptimizeGraphics()
        {
            var assets = new List<Object>();
            System.Action<Object> add = a => { if (a != null && !assets.Contains(a)) assets.Add(a); };
            add(GraphicsSettings.defaultRenderPipeline);
            add(GraphicsSettings.currentRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++) add(QualitySettings.GetRenderPipelineAssetAt(i));
            var log = new StringBuilder();
            int nAssets = 0;
            foreach (var a in assets)
            {
                if (a.GetType().Name != "UniversalRenderPipelineAsset") continue;
                nAssets++;
                log.AppendLine("Asset URP : " + a.name);
                var so = new SerializedObject(a);
                SetP(so, "m_MSAA", 1, log);
                SetP(so, "m_SupportsHDR", false, log);
                SetP(so, "m_RequireDepthTexture", false, log);
                SetP(so, "m_RequireOpaqueTexture", false, log);
                SetP(so, "m_MainLightRenderingMode", 0, log);
                SetP(so, "m_MainLightShadowsSupported", false, log);
                SetP(so, "m_AdditionalLightsRenderingMode", 0, log);
                SetP(so, "m_AdditionalLightsShadowsSupported", false, log);
                SetP(so, "m_SoftShadowsSupported", false, log);
                SetP(so, "m_ShadowCascadeCount", 1, log);
                SetP(so, "m_ShadowDistance", 1f, log);
                SetP(so, "m_UseSRPBatcher", true, log);
                SetP(so, "m_RenderScale", 1f, log);
                SetP(so, "m_SupportsTerrainHoles", false, log);
                SetP(so, "m_ColorGradingMode", 0, log);
                SetP(so, "m_SupportsLightLayers", false, log);
                SetP(so, "m_ReflectionProbeBlending", false, log);
                SetP(so, "m_ReflectionProbeBoxProjection", false, log);
                SetP(so, "m_UseAdaptivePerformance", false, log);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(a);

                var list = so.FindProperty("m_RendererDataList");
                if (list == null) continue;
                for (int r = 0; r < list.arraySize; r++)
                {
                    var rd = list.GetArrayElementAtIndex(r).objectReferenceValue;
                    if (rd == null) continue;
                    log.AppendLine(" Moteur de rendu : " + rd.name);
                    var rso = new SerializedObject(rd);
                    SetP(rso, "m_RenderingMode", 0, log);            // Forward (pas Forward+ ni Deferred)
                    SetP(rso, "m_DepthPrimingMode", 0, log);         // désactivé
                    SetP(rso, "m_IntermediateTextureMode", 0, log);  // Auto : pas de texture intermédiaire inutile
                    var feats = rso.FindProperty("m_RendererFeatures");
                    if (feats != null)
                        for (int f = 0; f < feats.arraySize; f++)
                        {
                            var fo = feats.GetArrayElementAtIndex(f).objectReferenceValue;
                            if (fo == null) continue;
                            string tn = fo.GetType().Name;
                            if (!(tn.Contains("ScreenSpaceAmbientOcclusion") || tn.Contains("Decal") || tn.Contains("ScreenSpaceShadows"))) continue;
                            var fso = new SerializedObject(fo);
                            if (SetP(fso, "m_Active", false, log)) log.AppendLine("    effet coupe : " + tn);
                            fso.ApplyModifiedProperties();
                            EditorUtility.SetDirty(fo);
                        }
                    rso.ApplyModifiedProperties();
                    EditorUtility.SetDirty(rd);
                }
            }
            // lumières de la scène : inutiles (tout est sans éclairage), au moins plus d'ombres
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.shadows != LightShadows.None) { Undo.RecordObject(l, "AR6DoF lumiere"); l.shadows = LightShadows.None; EditorUtility.SetDirty(l); }
            AssetDatabase.SaveAssets();
            if (nAssets == 0) return "Aucun asset URP trouve dans le projet (Edit > Project Settings > Graphics) : rien a optimiser.";
            string text = log.ToString().Trim();
            Debug.Log("AR6DoF : reglages graphiques optimises\n" + text);
            return nAssets + " asset(s) URP optimise(s). Le detail est dans la Console.\nSi rien n'est liste, c'est que tout etait deja regle.";
        }

        [MenuItem("AR6DoF/Optimiser les reglages graphiques (GPU)")]
        static void OptimizeGraphicsMenu()
        {
            EditorUtility.DisplayDialog("AR6DoF", OptimizeGraphics(), "OK");
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
            var shRounded = Find<Shader>("Ar6dofRounded");
            var shObject = Find<Shader>("Ar6dofObject");
            var model = Find<TextAsset>("camera_model");
            if (shComposite == null || shScreen == null) todo.Add("Les fichiers .shader d'AR6DoF sont introuvables : copie bien tout le dossier AR6DoF dans Assets.");
            string probeDir = Ar6dofAutoSetup.ProbeDir;
            if (model == null && !File.Exists(Path.Combine(probeDir, "camera_model.json")))
                todo.Add("camera_model.json introuvable : place le dossier ar6dof_probe-win64 a cote du dossier Assets (le fichier y est lu tel quel, rien a glisser dans la scene) et lance ar6dof_probe une fois avec la calibration.");

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
            if (shRounded != null) AlwaysInclude(shRounded);
            if (shObject != null) AlwaysInclude(shObject);
            PlayerSettings.runInBackground = true;
            OptimizeGraphics();
            if (ForceD3D11()) todo.Insert(0, "REDEMARRE UNITY : l'API graphique a ete reglee sur Direct3D11 (obligatoire, sinon les ecrans virtuels font planter Unity).");

            bool dll = Ar6dofAutoSetup.EnsureDll(false);   // copiee toute seule depuis ar6dof_probe-win64 vers Assets/Plugins/x86_64 (hors du dossier AR6DoF)
            if (!dll) todo.Add("ar6dof_screen.dll introuvable : place le dossier ar6dof_probe-win64 a cote de Assets (elle sera copiee toute seule dans Assets/Plugins/x86_64), sinon copie-la a la main dans Assets/Plugins/x86_64/.");
            todo.Add("Reglages graphiques optimises pour le casque (MSAA, SSAO, ombres... coupes ; detail dans la Console). A refaire a tout moment : menu AR6DoF > Optimiser les reglages graphiques (GPU).");
            if (!File.Exists(Path.Combine(probeDir, "ar6dof_probe.exe")))
                todo.Add("ar6dof_probe.exe : place le dossier ar6dof_probe-win64 a cote du dossier Assets, ou renseigne Launcher > Exe Path une seule fois (Unity retient le chemin).");

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = rigGo;
            EditorUtility.DisplayDialog("AR6DoF", "Scene creee.\n\nIl te reste :\n- " + string.Join("\n- ", todo), "OK");
        }
    }
}
#endif
