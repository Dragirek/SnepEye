using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Réglages de performance appliqués au démarrage et à chaque changement des réglages : limite d'images par seconde (sans elle, le GPU
    /// dessine aussi vite qu'il peut et reste à 80-100 % pour rien), options de qualité inutiles ici coupées, caméras sans calculs superflus
    /// (post-traitement, ombres, copie de l'image, texture de profondeur). Tout ce qui touche à l'asset URP du projet est fait une fois
    /// dans l'éditeur par le menu AR6DoF > Optimiser les reglages graphiques (Ar6dofSceneBuilder).
    /// </summary>
    public static class Ar6dofPerf
    {
        public static readonly string[] FpsNames = { "Ecran (auto)", "60", "90", "120", "Illimite" };
        public static readonly string[] AaNames = { "Aucun", "x2", "x4", "x8" };

        /// <summary>Nombre d'echantillons MSAA du reglage de lissage (1, 2, 4 ou 8).</summary>
        public static int AaSamples { get { int i = Mathf.Clamp(Ar6dofSettings.Current.perfAa, 0, 3); return i == 0 ? 1 : (1 << i); } }

        static bool hooked;
        static float fpsSmooth = 60f;

        /// <summary>Images par seconde réellement dessinées (moyenne lissée).</summary>
        public static int Fps { get { return Mathf.RoundToInt(fpsSmooth); } }

        /// <summary>À appeler une fois par image (par Ar6dofPassthrough).</summary>
        public static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.0001f) fpsSmooth = Mathf.Lerp(fpsSmooth, 1f / dt, 0.05f);
        }

        /// <summary>Applique les réglages courants. Peut être rappelée à tout moment.</summary>
        public static void Apply()
        {
            if (!hooked) { hooked = true; Ar6dofSettings.Changed += Apply; }
            var d = Ar6dofSettings.Current;

            int refresh = 60;
            try { refresh = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value); } catch (Exception) { }
            if (refresh < 30 || refresh > 500) refresh = 60;

            int mode = Mathf.Clamp(d.perfFps, 0, 4);
            int fps;
            switch (mode)
            {
                case 1: fps = 60; break;
                case 2: fps = 90; break;
                case 3: fps = 120; break;
                case 4: fps = -1; break;
                default: fps = refresh; break;
            }
            bool vsync = false;
#if !UNITY_EDITOR
            vsync = mode == 0;   // dans un exécutable : la synchronisation verticale suit l'écran sans à-coups. Dans l'éditeur, elle dépend de la vue Game : on limite par le nombre d'images.
#endif
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            Application.targetFrameRate = vsync ? -1 : fps;

            // options de qualité inutiles ici (tout est affiché sans lumière ni ombre)
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.pixelLightCount = 0;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.softParticles = false;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        }

        /// <summary>Retire à une caméra tout ce qui coûte du GPU sans servir ici. 'msaa' : lissage des bords de la caméra (en général faux).</summary>
        public static void ConfigureCamera(Camera c, bool msaa)
        {
            if (c == null) return;
            c.allowMSAA = msaa;
            c.allowHDR = false;
            c.allowDynamicResolution = false;
            c.useOcclusionCulling = false;
            try
            {
                // données propres à URP, atteintes sans dépendre du paquet à la compilation
                var type = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (type == null) return;
                var data = c.GetComponent(type);
                if (data == null) data = c.gameObject.AddComponent(type);
                SetProp(data, "renderPostProcessing", false);
                SetProp(data, "renderShadows", false);
                SetProp(data, "requiresDepthOption", 0);     // CameraOverrideOption.Off
                SetProp(data, "requiresColorOption", 0);     // CameraOverrideOption.Off : plus de copie de l'image (CopyColor)
                SetProp(data, "antialiasing", 0);            // AntialiasingMode.None
                SetProp(data, "stopNaN", false);
                SetProp(data, "dithering", false);
            }
            catch (Exception) { }
        }

        static void SetProp(object o, string name, object value)
        {
            try
            {
                var p = o.GetType().GetProperty(name);
                if (p == null || !p.CanWrite) return;
                object v = value;
                if (p.PropertyType.IsEnum) v = Enum.ToObject(p.PropertyType, value);
                p.SetValue(o, v, null);
            }
            catch (Exception) { }
        }
    }
}
