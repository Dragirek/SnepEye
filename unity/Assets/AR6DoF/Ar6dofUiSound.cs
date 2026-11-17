using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Les bruitages de l'interface.</summary>
    public enum Ar6dofUiSnd { Click, On, Off, Open, Close, Minimize, Grab, Drop, Tick, Error }

    /// <summary>
    /// Sons d'interface : clic, interrupteur, ouvrir, fermer, reduire, saisir / poser une fenetre, cran d'un curseur. Tous fabriques par le code au premier besoin
    /// (aucun fichier), en trois themes (doux, cristal, retro), joues par Unity sur la sortie par defaut de Windows.
    /// Reglages dans Reglages > Interface > Sons : tout couper, volume, theme, et un interrupteur par famille de sons.
    /// Sons coupes : rien n'est cree ni calcule (la fonction Play sort tout de suite).
    /// </summary>
    public static class Ar6dofUiSound
    {
        const int SR = 24000;
        const int Themes = 3;
        public static readonly string[] ThemeNames = { "Doux", "Cristal", "Retro" };
        static readonly AudioClip[] cache = new AudioClip[Themes * 10];
        static readonly float[] last = new float[10];
        static readonly float[] minGap = { 0.03f, 0.05f, 0.05f, 0.10f, 0.10f, 0.10f, 0.08f, 0.08f, 0.045f, 0.30f };
        static GameObject go;
        static AudioSource src;

        /// <summary>Joue un son d'interface (selon les reglages). 'vol' : 0..1 en plus du volume general.</summary>
        public static void Play(Ar6dofUiSnd id, float vol = 1f)
        {
            Ar6dofSettingsData s = Ar6dofSettings.Current;
            if (s == null || !s.uiSound || s.uiSoundVolume <= 0.001f) return;
            switch (id)
            {
                case Ar6dofUiSnd.Click: case Ar6dofUiSnd.On: case Ar6dofUiSnd.Off: case Ar6dofUiSnd.Error: if (!s.uiSoundClick) return; break;
                case Ar6dofUiSnd.Open: case Ar6dofUiSnd.Close: case Ar6dofUiSnd.Minimize: if (!s.uiSoundWindow) return; break;
                case Ar6dofUiSnd.Grab: case Ar6dofUiSnd.Drop: if (!s.uiSoundMove) return; break;
                case Ar6dofUiSnd.Tick: if (!s.uiSoundSlider) return; break;
            }
            int i = (int)id;
            float now = Time.unscaledTime;
            if (now - last[i] < minGap[i]) return;
            last[i] = now;
            if (!Ensure()) return;
            try { src.PlayOneShot(Clip(Mathf.Clamp(s.uiSoundTheme, 0, Themes - 1), id), Mathf.Clamp01(vol * s.uiSoundVolume)); }
            catch (Exception) { }
        }

        /// <summary>Pour le bouton « Essayer » des reglages : joue un son meme si la famille est decochee (mais pas si tout est coupe).</summary>
        public static void Preview(Ar6dofUiSnd id)
        {
            Ar6dofSettingsData s = Ar6dofSettings.Current;
            if (s == null || !s.uiSound || s.uiSoundVolume <= 0.001f || !Ensure()) return;
            try { src.PlayOneShot(Clip(Mathf.Clamp(s.uiSoundTheme, 0, Themes - 1), id), Mathf.Clamp01(s.uiSoundVolume)); }
            catch (Exception) { }
        }

        static bool Ensure()
        {
            if (src != null) return true;
            try
            {
                go = new GameObject("AR6DoF_SonsInterface");
                UnityEngine.Object.DontDestroyOnLoad(go);
                // Unity ne joue rien sans « oreille » dans la scene : on en ajoute une seulement s'il n'y en a aucune
                if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                return true;
            }
            catch (Exception)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                go = null; src = null;
                return false;
            }
        }

        static AudioClip Clip(int theme, Ar6dofUiSnd id)
        {
            int k = theme * 10 + (int)id;
            if (cache[k] == null) cache[k] = Build(theme, id);
            return cache[k];
        }

        // ---------------------------------------------------------------- fabrication
        /// <summary>Une note : frequence glissant de f0 a f1, enveloppe attaque 3 ms puis decroissance exponentielle. wave : 0 sinus, 1 cloche (partiels), 2 carre adouci.</summary>
        static void Note(float[] d, float start, float f0, float f1, float dur, float amp, float decay, int wave)
        {
            int i0 = (int)(start * SR), n = (int)(dur * SR);
            double ph = 0.0;
            for (int i = 0; i < n && i0 + i < d.Length; i++)
            {
                float t = (float)i / SR;
                float k = n > 1 ? (float)i / (n - 1) : 0f;
                float f = f0 + (f1 - f0) * k;
                ph += 2.0 * Math.PI * f / SR;
                float v;
                if (wave == 1) v = Mathf.Sin((float)ph) + 0.35f * Mathf.Sin((float)ph * 2.76f) + 0.15f * Mathf.Sin((float)ph * 5.4f);
                else if (wave == 2) { float sq = Mathf.Sin((float)ph); v = sq > 0.15f ? 0.7f : (sq < -0.15f ? -0.7f : sq * 4.6f); }
                else v = Mathf.Sin((float)ph);
                float env = Mathf.Min(1f, i / (0.003f * SR)) * Mathf.Exp(-t * decay);
                float tail = Mathf.Clamp01((n - i) / (0.004f * SR));   // fin adoucie : pas de claquement
                d[i0 + i] += v * env * tail * amp;
            }
        }

        static AudioClip Build(int theme, Ar6dofUiSnd id)
        {
            int wave = theme == 1 ? 1 : (theme == 2 ? 2 : 0);
            float m = theme == 1 ? 1.5f : 1f;           // cristal : plus aigu
            float dm = theme == 1 ? 0.8f : 1f;          // cristal : sonne un peu plus longtemps
            var d = new float[(int)(0.32f * SR)];
            switch (id)
            {
                case Ar6dofUiSnd.Click:
                    Note(d, 0f, 1000f * m, 780f * m, 0.06f, 0.55f, 70f * dm, wave);
                    break;
                case Ar6dofUiSnd.On:
                    Note(d, 0f, 700f * m, 700f * m, 0.07f, 0.45f, 45f * dm, wave);
                    Note(d, 0.055f, 1050f * m, 1050f * m, 0.11f, 0.5f, 38f * dm, wave);
                    break;
                case Ar6dofUiSnd.Off:
                    Note(d, 0f, 1050f * m, 1050f * m, 0.07f, 0.45f, 45f * dm, wave);
                    Note(d, 0.055f, 700f * m, 700f * m, 0.11f, 0.5f, 38f * dm, wave);
                    break;
                case Ar6dofUiSnd.Open:
                    Note(d, 0f, 520f * m, 520f * m, 0.09f, 0.4f, 30f * dm, wave);
                    Note(d, 0.05f, 780f * m, 780f * m, 0.09f, 0.4f, 30f * dm, wave);
                    Note(d, 0.10f, 1040f * m, 1040f * m, 0.16f, 0.45f, 24f * dm, wave);
                    break;
                case Ar6dofUiSnd.Close:
                    Note(d, 0f, 820f * m, 820f * m, 0.09f, 0.4f, 30f * dm, wave);
                    Note(d, 0.05f, 610f * m, 610f * m, 0.09f, 0.4f, 30f * dm, wave);
                    Note(d, 0.10f, 410f * m, 410f * m, 0.16f, 0.45f, 24f * dm, wave);
                    break;
                case Ar6dofUiSnd.Minimize:
                    Note(d, 0f, 900f * m, 340f * m, 0.14f, 0.45f, 22f, wave);
                    break;
                case Ar6dofUiSnd.Grab:
                    Note(d, 0f, 330f * m, 230f * m, 0.08f, 0.55f, 40f, wave);
                    Note(d, 0f, 1400f * m, 1400f * m, 0.02f, 0.2f, 140f, wave);
                    break;
                case Ar6dofUiSnd.Drop:
                    Note(d, 0f, 240f * m, 120f * m, 0.11f, 0.65f, 32f, wave);
                    Note(d, 0f, 900f * m, 900f * m, 0.02f, 0.2f, 150f, wave);
                    break;
                case Ar6dofUiSnd.Tick:
                    Note(d, 0f, 1900f * m, 1900f * m, 0.025f, 0.28f, 150f, wave);
                    break;
                default:   // Error
                    Note(d, 0f, 230f, 230f, 0.11f, 0.5f, 18f, 2);
                    Note(d, 0.13f, 190f, 190f, 0.15f, 0.5f, 16f, 2);
                    break;
            }
            float peak = 0.001f;
            for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            float g = Mathf.Min(1f, 0.8f / peak);
            for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp(d[i] * g, -0.95f, 0.95f);
            var c = AudioClip.Create("Ar6dofUi" + id, d.Length, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
