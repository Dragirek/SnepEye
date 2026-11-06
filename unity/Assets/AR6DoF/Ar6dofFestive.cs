using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Le spectacle des gains : un flot de pieces d'or qui jaillit de la machine, une pluie de pieces pour les gros gains et des confettis. Physique maison (gravite, rebonds, frottements) :
    /// les pieces rebondissent sur le socle de la table, glissent, tombent par terre, s'y couchent ; quand tout est pose elles retrecissent peu a peu puis disparaissent.
    /// Pas de corps physiques Unity : de simples tableaux de nombres et un dessin direct (Graphics.DrawMesh), donc quelques centaines de pieces ne coutent presque rien.
    /// Rien n'est cree avant le premier gain, et tout disparait avec la table.
    /// </summary>
    public sealed class Ar6dofFestive
    {
        public const int MaxCoins = 480, MaxConf = 280;
        const float CoinR = 0.02f, CoinT = 0.0045f;
        static readonly Color[] ConfCols =
        {
            new Color(0.95f, 0.2f, 0.25f), new Color(1f, 0.82f, 0.15f), new Color(0.25f, 0.8f, 0.35f),
            new Color(0.25f, 0.55f, 1f), new Color(1f, 0.45f, 0.8f), new Color(0.97f, 0.97f, 0.95f),
        };

        sealed class Job
        {
            public int kind;           // 0 jet de pieces, 1 pluie de pieces, 2 confettis
            public float start, dur;
            public int count, done;
            public Vector3 pos, dir;   // repere de la table
        }

        readonly Ar6dofTable3D t;
        readonly System.Random rnd = new System.Random();
        readonly Mesh coinMesh, quadMesh;
        readonly Material coinMat;
        readonly Material[] confMat = new Material[6];
        readonly List<Job> jobs = new List<Job>();

        // pieces
        readonly Vector3[] cp = new Vector3[MaxCoins], cv = new Vector3[MaxCoins], cw = new Vector3[MaxCoins];
        readonly Quaternion[] cq = new Quaternion[MaxCoins];
        readonly byte[] cs = new byte[MaxCoins];           // 0 libre, 1 en vol / glisse, 2 couchee par terre
        readonly float[] cBorn = new float[MaxCoins], cRest = new float[MaxCoins], cFade = new float[MaxCoins], cJit = new float[MaxCoins];
        int cNext;

        // confettis
        readonly Vector3[] fp = new Vector3[MaxConf], fv = new Vector3[MaxConf], fw = new Vector3[MaxConf];
        readonly Quaternion[] fq = new Quaternion[MaxConf];
        readonly byte[] fs = new byte[MaxConf], fc = new byte[MaxConf];
        readonly Vector2[] fz = new Vector2[MaxConf];
        readonly float[] fBorn = new float[MaxConf], fRest = new float[MaxConf], fFade = new float[MaxConf], fPh = new float[MaxConf];
        int fNext;

        float floorY, globalFade = -1f;
        int flyingCoins, flyingConf;

        /// <summary>Appele a chaque image avec le nombre de pieces nees (pour les tintements).</summary>
        public Action<int> OnCoins;

        public Ar6dofFestive(Ar6dofTable3D table)
        {
            t = table;
            var b = new Ar6dofMeshBuilder();
            b.Lathe(new[] { 0f, 0.5f, 0.82f, 0.5f, 1f, 0.38f, 1f, -0.38f, 0.82f, -0.5f, 0f, -0.5f }, 20, Vector3.zero);
            coinMesh = t.Own(b.ToMesh("AR6DoF_Coin"));
            var q = new Ar6dofMeshBuilder();
            q.Quad(new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), -Vector3.forward,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            quadMesh = t.Own(q.ToMesh("AR6DoF_Confetti"));
            coinMat = t.NewMat(null);
            coinMat.SetColor("_Color", new Color(1f, 0.8f, 0.18f));
            coinMat.SetFloat("_Glow", 0.07f);
            for (int i = 0; i < confMat.Length; i++)
            {
                confMat[i] = t.NewMat(null);
                confMat[i].SetColor("_Color", ConfCols[i]);
                confMat[i].SetFloat("_Shade", 0f);
                confMat[i].SetFloat("_Glow", 0.08f);
            }
        }

        public bool Active { get { return jobs.Count > 0 || flyingCoins > 0 || flyingConf > 0 || AnyRest(); } }

        bool AnyRest()
        {
            for (int i = 0; i < MaxCoins; i++) if (cs[i] != 0) return true;
            for (int i = 0; i < MaxConf; i++) if (fs[i] != 0) return true;
            return false;
        }

        float Rn(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

        // ---------------------------------------------------------------- lancement
        /// <summary>Lance un spectacle de niveau 1 (petit gain) a 4 (jackpot). Positions dans le repere de la table.</summary>
        public void Show(int tier, Vector3 chute, Vector3 canonG, Vector3 canonD, float now, float floorDrop)
        {
            tier = Mathf.Clamp(tier, 1, 4);
            floorY = t.HeadY() - floorDrop;
            globalFade = -1f;
            int[] coins = { 0, 34, 100, 200, 340 }, conf = { 0, 50, 120, 180, 260 };
            float[] dur = { 0f, 1.3f, 2.4f, 3.6f, 5.2f };
            int rain = tier >= 3 ? coins[tier] / 3 : 0;
            jobs.Add(new Job { kind = 0, start = now + 0.2f, dur = dur[tier], count = coins[tier] - rain, pos = chute });
            if (rain > 0) jobs.Add(new Job { kind = 1, start = now + 0.4f, dur = dur[tier], count = rain, pos = new Vector3(0f, 0.75f, 0.05f) });
            AddConfetti(conf[tier] / 2, canonG, new Vector3(0.55f, 1f, -0.35f), now + 0.1f, 0.5f);
            AddConfetti(conf[tier] / 2, canonD, new Vector3(-0.55f, 1f, -0.35f), now + 0.1f, 0.5f);
            if (tier >= 3)
            {
                AddConfetti(conf[tier] / 3, canonG, new Vector3(0.5f, 1f, -0.3f), now + 1.4f, 0.5f);
                AddConfetti(conf[tier] / 3, canonD, new Vector3(-0.5f, 1f, -0.3f), now + 1.4f, 0.5f);
            }
            if (tier >= 4)
            {
                AddConfetti(conf[tier] / 3, canonG, new Vector3(0.5f, 1f, -0.3f), now + 3f, 0.6f);
                AddConfetti(conf[tier] / 3, canonD, new Vector3(-0.5f, 1f, -0.3f), now + 3f, 0.6f);
            }
        }

        /// <summary>Quelques confettis seulement (victoire dans un jeu de plateau).</summary>
        public void Confetti(int count, Vector3 canonG, Vector3 canonD, float now, float floorDrop)
        {
            floorY = t.HeadY() - floorDrop;
            globalFade = -1f;
            AddConfetti(count / 2, canonG, new Vector3(0.55f, 1f, -0.3f), now, 0.4f);
            AddConfetti(count / 2, canonD, new Vector3(-0.55f, 1f, -0.3f), now, 0.4f);
        }

        void AddConfetti(int count, Vector3 pos, Vector3 dir, float start, float dur)
        {
            if (count > 0) jobs.Add(new Job { kind = 2, start = start, dur = dur, count = count, pos = pos, dir = dir });
        }

        // ---------------------------------------------------------------- naissance
        void SpawnCoin(Vector3 p, Vector3 v)
        {
            int i = -1;
            for (int k = 0; k < MaxCoins; k++)
            {
                int j = (cNext + k) % MaxCoins;
                if (cs[j] == 0) { i = j; break; }
            }
            if (i < 0) i = cNext;
            if (cs[i] == 1) flyingCoins--;
            cNext = (i + 1) % MaxCoins;
            cs[i] = 1; cp[i] = p; cv[i] = v; cq[i] = UnityEngine.Random.rotation; cw[i] = UnityEngine.Random.onUnitSphere * Rn(8f, 26f);
            cBorn[i] = Time.unscaledTime; cFade[i] = -1f; cJit[i] = Rn(0f, 0.0035f);
            flyingCoins++;
        }

        void SpawnConf(Vector3 p, Vector3 v)
        {
            int i = -1;
            for (int k = 0; k < MaxConf; k++)
            {
                int j = (fNext + k) % MaxConf;
                if (fs[j] == 0) { i = j; break; }
            }
            if (i < 0) i = fNext;
            if (fs[i] == 1) flyingConf--;
            fNext = (i + 1) % MaxConf;
            fs[i] = 1; fp[i] = p; fv[i] = v; fq[i] = UnityEngine.Random.rotation; fw[i] = UnityEngine.Random.onUnitSphere * Rn(4f, 13f);
            fc[i] = (byte)rnd.Next(ConfCols.Length); fz[i] = new Vector2(Rn(0.018f, 0.03f), Rn(0.01f, 0.018f));
            fBorn[i] = Time.unscaledTime; fFade[i] = -1f; fPh[i] = Rn(0f, 6.28f);
            flyingConf++;
        }

        void Spawn(Job j, int n)
        {
            var tr = t.root.transform;
            Quaternion R = tr.rotation;
            for (int k = 0; k < n; k++)
            {
                if (j.kind == 0)
                {
                    Vector3 lp = j.pos + new Vector3(Rn(-0.012f, 0.012f), Rn(0f, 0.01f), 0f);
                    Vector3 lv = new Vector3(Rn(-0.4f, 0.4f), Rn(0.9f, 1.5f), Rn(-1f, -0.45f)).normalized * Rn(1.0f, 2.2f);
                    SpawnCoin(tr.TransformPoint(lp), R * lv);
                }
                else if (j.kind == 1)
                {
                    Vector3 lp = j.pos + new Vector3(Rn(-0.2f, 0.2f), Rn(0f, 0.3f), Rn(-0.15f, 0.15f));
                    SpawnCoin(tr.TransformPoint(lp), R * new Vector3(Rn(-0.2f, 0.2f), Rn(-0.5f, 0f), Rn(-0.2f, 0.2f)));
                }
                else
                {
                    Vector3 lv = (j.dir + new Vector3(Rn(-0.45f, 0.45f), Rn(-0.2f, 0.35f), Rn(-0.45f, 0.45f))).normalized * Rn(1.3f, 3.1f);
                    SpawnConf(tr.TransformPoint(j.pos), R * lv);
                }
            }
        }

        // ---------------------------------------------------------------- physique
        /// <summary>Choc d'une piece avec un plan (le dessus du socle, ou le sol) : rebond, frottement, mise a plat.</summary>
        static bool Hit(ref Vector3 p, ref Vector3 v, ref Quaternion q, ref Vector3 w, Matrix4x4 toL, Matrix4x4 toW, Quaternion R, Quaternion invR, float dt, float fric, bool useB, Vector4 b, float depth)
        {
            Vector3 lp = toL.MultiplyPoint3x4(p);
            if (useB && (lp.x < b.x || lp.x > b.y || lp.z < b.z || lp.z > b.w)) return false;
            Vector3 ax = invR * (q * Vector3.up);
            float ay = Mathf.Abs(ax.y);
            float eff = ay * CoinT * 0.5f + Mathf.Sqrt(Mathf.Max(0f, 1f - ay * ay)) * CoinR;
            if (lp.y >= eff || lp.y < -depth) return false;
            Vector3 lv = invR * v;
            if (lv.y < 0f) { lv.y = -lv.y * 0.42f; if (lv.y < 0.3f) lv.y = 0f; }
            float sp = Mathf.Sqrt(lv.x * lv.x + lv.z * lv.z);
            if (sp > 1e-5f)
            {
                float k = Mathf.Max(0f, sp - (fric * dt + sp * 0.06f)) / sp;
                lv.x *= k; lv.z *= k;
            }
            v = R * lv;
            lp.y = eff;
            p = toW.MultiplyPoint3x4(lp);
            Vector3 n = R * Vector3.up, a = q * Vector3.up;
            if (Vector3.Dot(a, n) < 0f) a = -a;
            q = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(a, n), Mathf.Clamp01(9f * dt)) * q;
            w *= Mathf.Max(0f, 1f - 6f * dt);
            return true;
        }

        public void Tick(float now)
        {
            // naissances
            int born = 0;
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                var j = jobs[i];
                if (now < j.start) continue;
                int target = Mathf.Min(j.count, Mathf.RoundToInt(j.count * Mathf.Clamp01((now - j.start) / Mathf.Max(0.05f, j.dur))));
                if (now - j.start >= j.dur) target = j.count;
                int n = target - j.done;
                if (n > 0) { Spawn(j, n); j.done = target; if (j.kind < 2) born += n; }
                if (j.done >= j.count) jobs.RemoveAt(i);
            }
            if (born > 0 && OnCoins != null) OnCoins(born);
            if (flyingCoins == 0 && flyingConf == 0 && jobs.Count == 0 && !AnyRest()) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
            var tr = t.root.transform;
            Quaternion R = tr.rotation, invR = Quaternion.Inverse(R);
            Matrix4x4 w2l = tr.worldToLocalMatrix, l2w = tr.localToWorldMatrix;
            Matrix4x4 fToL = Matrix4x4.Translate(new Vector3(0f, -floorY, 0f)), fToW = Matrix4x4.Translate(new Vector3(0f, floorY, 0f));
            Vector4 sb;
            bool hasSlab = t.TryGetSlab(out sb);
            bool settledNow = jobs.Count == 0 && flyingCoins == 0 && flyingConf == 0;
            if (settledNow && globalFade < 0f) globalFade = now + 0.9f;
            var coinScale = new Vector3(CoinR, CoinT, CoinR);

            for (int i = 0; i < MaxCoins; i++)
            {
                if (cs[i] == 0) continue;
                float s = 1f;
                if (cs[i] == 1)
                {
                    cv[i] += Vector3.down * (9.81f * dt);
                    cp[i] += cv[i] * dt;
                    float ang = cw[i].magnitude * dt * Mathf.Rad2Deg;
                    if (ang > 1e-4f) cq[i] = Quaternion.AngleAxis(ang, cw[i].normalized) * cq[i];
                    if (hasSlab) Hit(ref cp[i], ref cv[i], ref cq[i], ref cw[i], w2l, l2w, R, invR, dt, 3.2f, true, sb, 0.02f);
                    if (Hit(ref cp[i], ref cv[i], ref cq[i], ref cw[i], fToL, fToW, Quaternion.identity, Quaternion.identity, dt, 4.5f, false, sb, 5f))
                    {
                        if (cv[i].sqrMagnitude < 0.012f)
                        {
                            Vector3 a = cq[i] * Vector3.up;
                            if (a.y < 0f) a = -a;
                            cq[i] = Quaternion.FromToRotation(a, Vector3.up) * cq[i];
                            cp[i].y = floorY + CoinT * 0.5f + cJit[i];
                            cv[i] = Vector3.zero; cw[i] = Vector3.zero;
                            cs[i] = 2; cRest[i] = now; flyingCoins--;
                        }
                    }
                    else if (cp[i].y < floorY - 3f || now - cBorn[i] > 14f)
                    {
                        // sorti du monde ou trop long : il disparait
                        if (cFade[i] < 0f) cFade[i] = now;
                    }
                }
                else
                {
                    if (cFade[i] < 0f && ((globalFade >= 0f && now >= globalFade) || now - cRest[i] > 9f)) cFade[i] = now + Rn(0f, 1f);
                }
                if (cFade[i] >= 0f && now > cFade[i])
                {
                    s = 1f - (now - cFade[i]) / 0.9f;
                    if (s <= 0f) { if (cs[i] == 1) flyingCoins--; cs[i] = 0; continue; }
                }
                Graphics.DrawMesh(coinMesh, Matrix4x4.TRS(cp[i], cq[i], coinScale * s), coinMat, 0);
            }

            for (int i = 0; i < MaxConf; i++)
            {
                if (fs[i] == 0) continue;
                float s = 1f;
                if (fs[i] == 1)
                {
                    fv[i] *= Mathf.Max(0f, 1f - 2.8f * dt);
                    fv[i].y -= 2.0f * dt;
                    fp[i] += fv[i] * dt + Vector3.right * (Mathf.Sin(now * 6f + fPh[i]) * 0.1f * dt);
                    float ang = fw[i].magnitude * dt * Mathf.Rad2Deg;
                    if (ang > 1e-4f) fq[i] = Quaternion.AngleAxis(ang, fw[i].normalized) * fq[i];
                    if (hasSlab && fFade[i] < 0f)
                    {
                        Vector3 lp = w2l.MultiplyPoint3x4(fp[i]);
                        if (lp.x > sb.x && lp.x < sb.y && lp.z > sb.z && lp.z < sb.w && lp.y < 0.004f && lp.y > -0.03f) { fFade[i] = now; flyingConf--; fs[i] = 2; fRest[i] = now; }
                    }
                    if (fs[i] == 1 && fp[i].y < floorY + 0.002f)
                    {
                        fp[i].y = floorY + 0.002f;
                        fq[i] = Quaternion.Euler(90f, Rn(0f, 360f), 0f);
                        fs[i] = 2; fRest[i] = now; flyingConf--;
                    }
                    else if (fs[i] == 1 && now - fBorn[i] > 14f) { fFade[i] = now; }
                }
                else if (fFade[i] < 0f && ((globalFade >= 0f && now >= globalFade) || now - fRest[i] > 8f)) fFade[i] = now + Rn(0f, 1f);
                if (fFade[i] >= 0f && now > fFade[i])
                {
                    s = 1f - (now - fFade[i]) / 0.8f;
                    if (s <= 0f) { if (fs[i] == 1) flyingConf--; fs[i] = 0; continue; }
                }
                Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(fp[i], fq[i], new Vector3(fz[i].x * s, fz[i].y * s, 1f)), confMat[fc[i]], 0);
            }
        }
    }
}
