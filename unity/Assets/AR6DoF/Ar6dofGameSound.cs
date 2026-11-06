using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Les bruitages des jeux (levier, rouleaux, tintement des pieces, jingles de victoire, defaite). Tous les sons sont fabriques par le code au premier besoin
    /// (aucun fichier a installer) et joues par Unity sur la sortie par defaut de Windows, comme la sonnerie des alarmes. Rien n'existe tant qu'on n'a pas joue un son.
    /// </summary>
    public sealed class Ar6dofGameSound
    {
        public enum Id { Lever, Clack, Ping, Whirr, Win1, Win2, Win3, Win4, Lose }

        const int SR = 22050;
        static readonly AudioClip[] cache = new AudioClip[16];

        public bool on = true;
        public float volume = 0.7f;
        GameObject go;
        AudioSource fx, pings, loop;
        float lastPing;
        readonly System.Random rnd = new System.Random();

        // ---------------------------------------------------------------- fabrication des sons
        static void Tone(float[] d, float start, float freq, float dur, float amp, float decay, float bright)
        {
            int i0 = (int)(start * SR), n = (int)(dur * SR);
            for (int i = 0; i < n && i0 + i < d.Length; i++)
            {
                float t = (float)i / SR;
                float env = Mathf.Min(1f, i / (0.004f * SR)) * Mathf.Exp(-t * decay);
                float ph = 2f * Mathf.PI * freq * t;
                float v = Mathf.Sin(ph) + bright * 0.5f * Mathf.Sin(2f * ph) + bright * 0.25f * Mathf.Sin(3f * ph);
                d[i0 + i] += v * env * amp;
            }
        }

        static AudioClip Make(string name, float[] d)
        {
            for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp(d[i], -0.95f, 0.95f);
            var c = AudioClip.Create(name, d.Length, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip Build(Id id)
        {
            var r = new System.Random(7);
            switch (id)
            {
                case Id.Lever:
                    {
                        var d = new float[(int)(0.3f * SR)];
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = (float)i / SR;
                            d[i] = Mathf.Sin(2f * Mathf.PI * 75f * t) * Mathf.Exp(-t * 14f) * 0.9f + Mathf.Sin(2f * Mathf.PI * 1500f * t) * Mathf.Exp(-t * 120f) * 0.5f;
                        }
                        return Make("Ar6dofLever", d);
                    }
                case Id.Clack:
                    {
                        var d = new float[(int)(0.18f * SR)];
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = (float)i / SR;
                            float noise = (float)(r.NextDouble() * 2.0 - 1.0);
                            d[i] = noise * Mathf.Exp(-t * 90f) * 0.8f + Mathf.Sin(2f * Mathf.PI * 180f * t) * Mathf.Exp(-t * 35f) * 0.7f;
                        }
                        return Make("Ar6dofClack", d);
                    }
                case Id.Ping:
                    {
                        var d = new float[(int)(0.3f * SR)];
                        Tone(d, 0f, 2637f, 0.3f, 0.5f, 16f, 0f);
                        Tone(d, 0f, 3951f, 0.3f, 0.25f, 20f, 0f);
                        Tone(d, 0f, 5274f, 0.3f, 0.15f, 26f, 0f);
                        return Make("Ar6dofPing", d);
                    }
                case Id.Whirr:
                    {
                        var d = new float[(int)(0.6f * SR)];       // 90 Hz et 15 Hz tombent sur un nombre entier de cycles : la boucle est continue
                        for (int i = 0; i < d.Length; i++)
                        {
                            float t = (float)i / SR;
                            float tone = Mathf.Sin(2f * Mathf.PI * 90f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 180f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 270f * t);
                            d[i] = tone * (0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 15f * t)) * 0.2f;
                        }
                        return Make("Ar6dofWhirr", d);
                    }
                case Id.Win1:
                    {
                        var d = new float[(int)(0.9f * SR)];
                        float[] f = { 523.25f, 659.25f, 783.99f };
                        for (int k = 0; k < f.Length; k++) Tone(d, k * 0.12f, f[k], 0.5f, 0.4f, 6f, 0.6f);
                        return Make("Ar6dofWin1", d);
                    }
                case Id.Win2:
                    {
                        var d = new float[(int)(2.0f * SR)];
                        float[] f = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };
                        for (int k = 0; k < f.Length; k++) Tone(d, k * 0.1f, f[k], 0.5f, 0.3f, 6f, 0.6f);
                        for (int k = 0; k < 4; k++) Tone(d, 0.55f, f[k], 1.3f, 0.2f, 2.8f, 0.5f);
                        return Make("Ar6dofWin2", d);
                    }
                case Id.Win3:
                    {
                        var d = new float[(int)(3.0f * SR)];
                        float[] f = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };
                        for (int run = 0; run < 2; run++)
                            for (int k = 0; k < f.Length; k++) Tone(d, run * 0.55f + k * 0.09f, f[k], 0.5f, 0.28f, 6f, 0.6f);
                        for (int k = 0; k < 5; k++) Tone(d, 1.15f, f[k], 1.7f, 0.17f, 2.2f, 0.5f);
                        for (int k = 0; k < 22; k++) Tone(d, 0.4f + k * 0.11f, 2000f + (float)r.NextDouble() * 2600f, 0.25f, 0.1f, 14f, 0f);
                        return Make("Ar6dofWin3", d);
                    }
                case Id.Win4:
                    {
                        var d = new float[(int)(5.0f * SR)];
                        float[] up = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568f };
                        for (int k = 0; k < up.Length; k++) Tone(d, k * 0.1f, up[k], 0.6f, 0.28f, 5f, 0.7f);
                        float[] c1 = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };
                        for (int k = 0; k < c1.Length; k++) Tone(d, 0.7f, c1[k], 1.3f, 0.17f, 2.2f, 0.6f);
                        float[] c2 = { 698.46f, 880f, 1046.5f, 1396.9f };
                        for (int k = 0; k < c2.Length; k++) Tone(d, 2.0f, c2[k], 1.2f, 0.17f, 2.2f, 0.6f);
                        for (int k = 0; k < up.Length; k++) Tone(d, 3.2f + k * 0.04f, up[k], 1.7f, 0.17f, 1.9f, 0.6f);
                        for (int k = 0; k < 40; k++) Tone(d, 0.5f + k * 0.1f, 1800f + (float)r.NextDouble() * 3200f, 0.3f, 0.09f, 12f, 0f);
                        return Make("Ar6dofWin4", d);
                    }
                default:
                    {
                        var d = new float[(int)(1.8f * SR)];
                        Tone(d, 0f, 392f, 0.35f, 0.3f, 3f, 1f);
                        Tone(d, 0.35f, 370f, 0.35f, 0.3f, 3f, 1f);
                        Tone(d, 0.7f, 349.2f, 0.35f, 0.3f, 3f, 1f);
                        Tone(d, 1.05f, 329.6f, 0.7f, 0.3f, 2f, 1f);
                        return Make("Ar6dofLose", d);
                    }
            }
        }

        static AudioClip Clip(Id id)
        {
            int k = (int)id;
            if (cache[k] == null) cache[k] = Build(id);
            return cache[k];
        }

        // ---------------------------------------------------------------- lecture
        bool Ensure()
        {
            if (go != null) return true;
            try
            {
                go = new GameObject("AR6DoF_SonsJeux");
                // Unity ne joue rien sans « oreille » dans la scene : on en ajoute une seulement s'il n'y en a aucune
                if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
                fx = go.AddComponent<AudioSource>(); fx.playOnAwake = false; fx.spatialBlend = 0f;
                pings = go.AddComponent<AudioSource>(); pings.playOnAwake = false; pings.spatialBlend = 0f;
                loop = go.AddComponent<AudioSource>(); loop.playOnAwake = false; loop.spatialBlend = 0f; loop.loop = true;
                return true;
            }
            catch (Exception)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                go = null;
                return false;
            }
        }

        public void Play(Id id, float vol = 1f)
        {
            if (!on || volume <= 0.001f || !Ensure()) return;
            fx.PlayOneShot(Clip(id), Mathf.Clamp01(vol * volume));
        }

        /// <summary>Tintement de pieces (appele avec le nombre de pieces nees : un tintement a la fois, au plus ~20 par seconde).</summary>
        public void Coins(int n)
        {
            if (n <= 0 || !on || volume <= 0.001f) return;
            float now = Time.unscaledTime;
            if (now - lastPing < 0.05f || !Ensure()) return;
            lastPing = now;
            pings.pitch = 0.85f + (float)rnd.NextDouble() * 0.6f;
            pings.PlayOneShot(Clip(Id.Ping), Mathf.Clamp01(0.5f * volume));
        }

        public void StartWhirr()
        {
            if (!on || volume <= 0.001f || !Ensure()) return;
            loop.clip = Clip(Id.Whirr);
            loop.volume = Mathf.Clamp01(0.55f * volume);
            if (!loop.isPlaying) loop.Play();
        }

        public void StopWhirr()
        {
            if (loop != null && loop.isPlaying) loop.Stop();
        }

        public void Dispose()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            go = null; fx = pings = loop = null;
        }
    }
}
