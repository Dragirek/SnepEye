using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// La machine a sous en 3D : une borne rouge et doree avec trois rouleaux a 8 facettes, un levier a tirer (touche-le au laser), des ampoules qui clignotent.
    /// L'application decide du resultat ; ici on ne fait que le montrer : chaque rouleau tourne vite puis ralentit et s'arrete sur la facette du symbole tire.
    /// </summary>
    public sealed class Ar6dofSlots3D
    {
        // symboles des 8 facettes d'un rouleau (0 cerise, 1 citron, 2 cloche, 3 etoile, 4 diamant, 5 sept)
        static readonly int[] Pattern = { 0, 1, 2, 0, 3, 1, 4, 5 };
        const float Step = 45f, SpinSpeed = 1400f, StopTime = 0.6f;
        const float DrumR = 0.075f, DrumLen = 0.085f;

        readonly Ar6dofTable3D t;
        readonly System.Random rnd = new System.Random();
        readonly Transform[] drum = new Transform[3];
        readonly Renderer[] bulb = new Renderer[11];
        readonly float[] ang = new float[3];
        readonly float[] stopFrom = new float[3], stopTo = new float[3];
        readonly int[] phase = new int[3];     // 0 immobile, 1 tourne, 2 s'arrete
        readonly float[] stopStart = new float[3];
        readonly int[] facet = new int[3];
        Transform lever, machine;
        float leverPull = -1f;
        float shakeStart, shakeUntil, shakeAmp;
        static readonly Vector3 MachinePos = new Vector3(0f, 0f, 0.07f);
        const float MachineScale = 0.7f;      // la machine fait 70 % de sa taille d'origine

        /// <summary>Appele quand un rouleau finit de s'arreter (0, 1, 2) : sert au bruit sec.</summary>
        public Action<int> OnReelStop;

        /// <summary>D'ou sortent les pieces (bac devant la machine) et les canons a confettis, dans le repere de la table.</summary>
        public Vector3 Chute { get { return MachinePos + new Vector3(0f, 0.075f, -0.115f) * MachineScale; } }
        public Vector3 CannonL { get { return new Vector3(-0.17f, 0.02f, -0.02f); } }
        public Vector3 CannonR { get { return new Vector3(0.17f, 0.02f, -0.02f); } }
        public Vector3 SignPos { get { return MachinePos + new Vector3(0f, 0.52f, -0.11f) * MachineScale; } }

        /// <summary>La machine tremble (gain) pendant 'seconds' secondes.</summary>
        public void Shake(float seconds, float amplitude, float now)
        {
            shakeStart = now; shakeUntil = now + seconds; shakeAmp = amplitude;
        }
        float spinStart;
        float[] stopAt;
        int lastBulb = -2;

        public Ar6dofSlots3D(Ar6dofTable3D table, Action onLever, int[] symbols)
        {
            t = table;
            t.SetSlab(0f, 0f, 0.42f, 0.38f);
            var mg = new GameObject("Machine");
            mg.transform.SetParent(t.content, false);
            mg.transform.localPosition = MachinePos;
            mg.transform.localScale = Vector3.one * MachineScale;
            machine = mg.transform;
            Build(onLever);
            for (int r = 0; r < 3; r++) { facet[r] = FacetFor(symbols[r]); ang[r] = -facet[r] * Step; Rot(r); }
            t.AddTick(Tick);
        }

        int FacetFor(int symbol)
        {
            int n = 0;
            for (int i = 0; i < Pattern.Length; i++) if (Pattern[i] == symbol) n++;
            if (n == 0) return 0;
            int pick = rnd.Next(n);
            for (int i = 0; i < Pattern.Length; i++) if (Pattern[i] == symbol && pick-- == 0) return i;
            return 0;
        }

        Texture2D Atlas()
        {
            const int S = 128;
            var tex = new Texture2D(S * 6, S, TextureFormat.RGBA32, false);
            var px = new Color[S * 6 * S];
            var bg = new Color(0.98f, 0.96f, 0.9f, 1f);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            for (int s = 0; s < 6; s++)
            {
                var src = Ar6dofGameArt.Slot(s).texture;
                Color[] sp = null;
                try
                {
                    // le dessin fait 128 x 128 : meme taille que la case de l'atlas
                    sp = src.GetPixels(0);
                }
                catch (Exception) { }
                if (sp == null || sp.Length != S * S) continue;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        Color c = sp[y * S + x];
                        int o = y * S * 6 + s * S + x;
                        px[o] = Color.Lerp(px[o], new Color(c.r, c.g, c.b, 1f), c.a);
                    }
            }
            tex.SetPixels(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false);
            return tex;
        }

        Mesh DrumMesh()
        {
            var b = new Ar6dofMeshBuilder();
            float rv = DrumR / Mathf.Cos(Step * 0.5f * Mathf.Deg2Rad);
            for (int f = 0; f < Pattern.Length; f++)
            {
                float a0 = (f * Step - Step * 0.5f) * Mathf.Deg2Rad, a1 = (f * Step + Step * 0.5f) * Mathf.Deg2Rad;
                // direction d'une facette d'angle beta : (0, sin beta, -cos beta)
                Vector3 d0 = new Vector3(0f, Mathf.Sin(a0), -Mathf.Cos(a0)) * rv, d1 = new Vector3(0f, Mathf.Sin(a1), -Mathf.Cos(a1)) * rv;
                float am = f * Step * Mathf.Deg2Rad;
                Vector3 nr = new Vector3(0f, Mathf.Sin(am), -Mathf.Cos(am));
                float h = DrumLen * 0.5f;
                float u0 = Pattern[f] / 6f + 0.004f, u1 = (Pattern[f] + 1) / 6f - 0.004f;
                b.Quad(d0 + new Vector3(-h, 0f, 0f), d0 + new Vector3(h, 0f, 0f), d1 + new Vector3(h, 0f, 0f), d1 + new Vector3(-h, 0f, 0f), nr,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f));
            }
            return b.ToMesh("AR6DoF_Drum");
        }

        void Build(Action onLever)
        {
            var red = new Color(0.82f, 0.13f, 0.2f); var gold = new Color(0.96f, 0.74f, 0.16f); var dark = new Color(0.16f, 0.07f, 0.1f);
            var box = Ar6dofShapes.UnitBox();
            Part(box, new Vector3(0f, 0.125f, 0f), new Vector3(0.34f, 0.09f, 0.2f), red);            // bas
            Part(box, new Vector3(0f, 0.04f, 0f), new Vector3(0.36f, 0.08f, 0.22f), gold);           // socle dore
            Part(box, new Vector3(0f, 0.375f, 0f), new Vector3(0.34f, 0.05f, 0.2f), red);            // haut de la fenetre
            Part(box, new Vector3(-0.1575f, 0.26f, 0f), new Vector3(0.025f, 0.18f, 0.2f), red);       // montants
            Part(box, new Vector3(0.1575f, 0.26f, 0f), new Vector3(0.025f, 0.18f, 0.2f), red);
            Part(box, new Vector3(0f, 0.26f, 0.05f), new Vector3(0.3f, 0.18f, 0.1f), dark);          // fond
            Part(box, new Vector3(0f, 0.43f, 0f), new Vector3(0.36f, 0.09f, 0.22f), gold);           // enseigne
            Part(box, new Vector3(0f, 0.26f, -0.083f), new Vector3(0.28f, 0.004f, 0.003f), new Color(1f, 0.15f, 0.15f));   // ligne de gain
            var tex = t.Own(Atlas());
            var mat = t.NewMat(tex);
            var mesh = t.Own(DrumMesh());
            for (int r = 0; r < 3; r++)
            {
                var g = t.Part(machine, "Rouleau", mesh, mat, new Vector3((r - 1) * 0.095f, 0.26f, -0.005f), Vector3.one, Color.white);
                drum[r] = g.transform;
            }
            for (int i = 0; i < bulb.Length; i++)
            {
                var g = t.Part(machine, "Ampoule", Ar6dofShapes.UnitSphere(), null, new Vector3((i - 5) * 0.03f, 0.405f, -0.112f), Vector3.one * 0.016f, gold);
                bulb[i] = g.GetComponent<Renderer>();
            }
            var sign = t.AddLabel(machine, new Vector3(0f, 0.435f, -0.112f), 0.2f, 60, gold, false);
            sign.Set("JACKPOT", gold);
            // levier : un bras qui pivote vers toi, une boule rouge au bout
            var pivot = new GameObject("Levier");
            pivot.transform.SetParent(machine, false);
            pivot.transform.localPosition = new Vector3(0.205f, 0.2f, 0f);
            lever = pivot.transform;
            var arm = t.Part(lever, "Bras", box, null, new Vector3(0f, 0.07f, 0f), new Vector3(0.012f, 0.14f, 0.012f), new Color(0.72f, 0.72f, 0.76f));
            arm.AddComponent<BoxCollider>().size = new Vector3(3.2f, 1f, 3.2f);   // plus large que le bras : facile a viser
            var at = arm.AddComponent<Ar6dofTapTarget>();
            at.action = onLever; at.glowRend = arm.GetComponent<Renderer>(); at.table = t; at.baseColor = new Color(0.72f, 0.72f, 0.76f);
            var ball = t.Part(lever, "Boule", Ar6dofShapes.UnitSphere(), null, new Vector3(0f, 0.15f, 0f), Vector3.one * 0.05f, new Color(0.9f, 0.12f, 0.16f));
            var bc = ball.AddComponent<SphereCollider>();
            bc.radius = 0.7f;   // un peu plus grosse que la boule : plus facile a viser
            var tt = ball.AddComponent<Ar6dofTapTarget>();
            tt.action = onLever; tt.glowRend = ball.GetComponent<Renderer>(); tt.table = t; tt.baseColor = new Color(0.9f, 0.12f, 0.16f);
        }

        void Part(Mesh m, Vector3 pos, Vector3 scale, Color c) { t.Part(machine, "Bloc", m, null, pos, scale, c); }

        void Rot(int r) { if (drum[r] != null) drum[r].localRotation = Quaternion.Euler(ang[r], 0f, 0f); }

        /// <summary>Lance le tirage : 'result' = symbole de chaque rouleau, 'stopTimes' = secondes apres 'now' auxquelles chaque rouleau doit etre arrete.</summary>
        public void Spin(int[] result, float[] stopTimes, float now)
        {
            spinStart = now;
            stopAt = stopTimes;
            leverPull = now;
            for (int r = 0; r < 3; r++) { facet[r] = FacetFor(result[r]); phase[r] = 1; }
        }

        /// <summary>Pose d'un coup les rouleaux sur les symboles donnes (sans animation).</summary>
        public void Show(int[] symbols)
        {
            for (int r = 0; r < 3; r++) { phase[r] = 0; facet[r] = FacetFor(symbols[r]); ang[r] = -facet[r] * Step; Rot(r); }
        }

        /// <summary>Faut-il faire clignoter les ampoules ?</summary>
        public bool Blink;

        void Tick(float now)
        {
            float el = now - spinStart;
            for (int r = 0; r < 3; r++)
            {
                if (phase[r] == 1)
                {
                    float startStop = stopAt[r] - StopTime;
                    if (el < startStop) ang[r] -= SpinSpeed * Time.unscaledDeltaTime;
                    else
                    {
                        phase[r] = 2;
                        stopStart[r] = spinStart + startStop;
                        stopFrom[r] = ang[r];
                        // distance a parcourir : au moins 420 degres, qui finit exactement sur la facette
                        float d = 420f + Mathf.Repeat(ang[r] + facet[r] * Step - 420f, 360f);
                        stopTo[r] = ang[r] - d;
                    }
                    Rot(r);
                }
                if (phase[r] == 2)
                {
                    float u = Mathf.Clamp01((now - stopStart[r]) / StopTime);
                    float e = 1f - (1f - u) * (1f - u);
                    ang[r] = Mathf.Lerp(stopFrom[r], stopTo[r], e);
                    if (u >= 1f) { phase[r] = 0; if (OnReelStop != null) OnReelStop(r); }
                    Rot(r);
                }
            }
            if (lever != null)
            {
                float k = leverPull < 0f ? 0f : (now - leverPull) / 0.45f;
                float down = k >= 1f ? 0f : (k < 0.5f ? k * 2f : (1f - k) * 2f);
                lever.localRotation = Quaternion.Euler(-70f * down, 0f, 0f);
            }
            if (machine != null)
            {
                if (now < shakeUntil)
                {
                    float k2 = 1f - (now - shakeStart) / Mathf.Max(0.01f, shakeUntil - shakeStart);
                    machine.localPosition = MachinePos + new Vector3(Mathf.Sin(now * 57f) * shakeAmp * k2, Mathf.Abs(Mathf.Sin(now * 43f)) * shakeAmp * 0.6f * k2, 0f);
                }
                else if (shakeUntil > 0f) { machine.localPosition = MachinePos; shakeUntil = 0f; }
            }
            int ph = Blink ? Mathf.FloorToInt(now * (shakeUntil > now ? 12f : 6f)) & 1 : -1;
            int key = Blink ? ph : -1;
            if (key != lastBulb)
            {
                lastBulb = key;
                var gold = new Color(0.96f, 0.74f, 0.16f); var white = new Color(1f, 0.97f, 0.8f); var orange = new Color(0.9f, 0.3f, 0.1f);
                for (int i = 0; i < bulb.Length; i++)
                    t.Paint(bulb[i], Blink ? (((i + ph) & 1) == 0 ? white : orange) : gold, Blink ? 0.25f : 0f);
            }
        }
    }
}
