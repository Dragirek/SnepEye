using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Les tirs de la bataille navale en 3D : un missile balistique, un avion qui largue une bombe ou un canon, au hasard, puis l'impact (gerbe d'eau, explosion avec fumee,
    /// explosion totale quand un navire coule) et la fumee qui continue sur les navires touches.
    /// Le vol est TOUJOURS le meme, que le tir touche ou non : rien ne revele la flotte ennemie avant l'impact. Les particules sont de simples tableaux dessines
    /// directement (Graphics.DrawMesh) ; rien n'existe tant qu'on ne tire pas, et tout disparait avec la table.
    /// </summary>
    public sealed class Ar6dofBattleFx
    {
        const int Max = 300;

        sealed class P
        {
            public int kind;          // 0 flamme (addition), 1 fumee (transparent), 2 onde (addition), 3 debris (opaque), 4 goutte (opaque)
            public Vector3 pos, vel, spin;
            public Quaternion rot = Quaternion.identity;
            public float age, life, s0, s1, grav, drag;
            public Color c0, c1;
            public bool alive;
        }

        sealed class Flight
        {
            public int type;          // 0 missile, 1 avion, 2 canon
            public Vector3 s, e;
            public float t0, dur, h, sign, trail, endAt;
            public Transform go, extra;
            public Action done;
            public bool started, impacted, released;
        }

        sealed class Later { public float at; public Ar6dofGameSound.Id id; public float vol; }

        readonly Ar6dofTable3D t;
        readonly Ar6dofBoard3D enemy, own;
        readonly Action<Ar6dofGameSound.Id, float> sound;
        readonly Material mOpaque, mAlpha, mAdd;
        readonly Mesh sphere, box, ring, missileMesh, planeMesh, bombMesh, barrelMesh, shellMesh;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly System.Random rnd = new System.Random();
        readonly P[] ps = new P[Max];
        readonly List<Flight> flights = new List<Flight>();
        readonly List<Later> laters = new List<Later>();
        readonly float[] nextE, nextO;
        int[] burnE, burnO;
        int cursor;

        public Ar6dofBattleFx(Ar6dofTable3D table, Ar6dofBoard3D enemyBoard, Ar6dofBoard3D ownBoard, Action<Ar6dofGameSound.Id, float> sound)
        {
            t = table; enemy = enemyBoard; own = ownBoard; this.sound = sound;
            for (int i = 0; i < Max; i++) ps[i] = new P();
            nextE = new float[enemy.rows * enemy.cols]; nextO = new float[own.rows * own.cols];
            for (int i = 0; i < nextE.Length; i++) nextE[i] = (float)rnd.NextDouble() * 0.5f;
            for (int i = 0; i < nextO.Length; i++) nextO[i] = (float)rnd.NextDouble() * 0.5f;
            mOpaque = NewMat(1, 0, 1f, 1f, -1);
            mAlpha = NewMat(5, 10, 0f, 1f, 3000);
            mAdd = NewMat(5, 1, 0f, 0f, 3001);
            sphere = Ar6dofShapes.UnitSphere(); box = Ar6dofShapes.UnitBox(); ring = Ar6dofShapes.UnitRing();

            var b = new Ar6dofMeshBuilder();
            b.Lathe(new[] { 0f, -0.03f, 0.005f, -0.03f, 0.005f, 0.015f, 0.0035f, 0.03f, 0f, 0.04f }, 10, Vector3.zero);
            b.Box(new Vector3(0f, -0.022f, 0f), new Vector3(0.022f, 0.014f, 0.0012f));
            b.Box(new Vector3(0f, -0.022f, 0f), new Vector3(0.0014f, 0.014f, 0.022f));
            missileMesh = t.Own(b.ToMesh("AR6DoF_Missile"));

            b = new Ar6dofMeshBuilder();                   // avion : nez vers +x
            b.Box(new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.012f, 0.012f));
            b.Box(new Vector3(0.012f, 0.008f, 0f), new Vector3(0.016f, 0.008f, 0.01f));
            b.Box(new Vector3(0.005f, 0f, 0f), new Vector3(0.016f, 0.003f, 0.075f));
            b.Box(new Vector3(-0.03f, 0.01f, 0f), new Vector3(0.012f, 0.014f, 0.002f));
            b.Box(new Vector3(-0.03f, 0.003f, 0f), new Vector3(0.01f, 0.002f, 0.03f));
            planeMesh = t.Own(b.ToMesh("AR6DoF_Plane"));

            b = new Ar6dofMeshBuilder();                   // bombe, pointe vers le bas
            b.Lathe(new[] { 0f, -0.02f, 0.005f, -0.012f, 0.006f, 0f, 0.006f, 0.01f, 0f, 0.012f }, 10, Vector3.zero);
            bombMesh = t.Own(b.ToMesh("AR6DoF_Bomb"));

            b = new Ar6dofMeshBuilder();                   // canon : tube vers +z sur un petit affut
            b.Box(new Vector3(0f, 0.004f, 0f), new Vector3(0.03f, 0.008f, 0.03f));
            b.Box(new Vector3(0f, 0.016f, 0.012f), new Vector3(0.012f, 0.012f, 0.05f));
            barrelMesh = t.Own(b.ToMesh("AR6DoF_Cannon"));

            b = new Ar6dofMeshBuilder();                   // obus : cylindre a ogive, pointe vers +y, avec une bague de forcement
            b.Lathe(new[] { 0f, -0.02f, 0.0055f, -0.02f, 0.0065f, -0.016f, 0.0065f, -0.002f, 0.0075f, 0f, 0.0075f, 0.003f, 0.0065f, 0.004f, 0.0062f, 0.008f, 0.0042f, 0.0165f, 0.0018f, 0.022f, 0f, 0.0245f }, 12, Vector3.zero);
            shellMesh = t.Own(b.ToMesh("AR6DoF_Shell"));
        }

        Material NewMat(int src, int dst, float zwrite, float shade, int queue)
        {
            var m = new Material(Ar6dofTable3D.Sh());
            m.SetFloat("_Shade", shade);
            m.SetFloat("_SrcBlend", src);
            m.SetFloat("_DstBlend", dst);
            m.SetFloat("_ZWrite", zwrite);
            if (queue > 0) m.renderQueue = queue;
            return t.Own(m);
        }

        float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

        Vector3 RandDir()
        {
            var v = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f));
            return v.sqrMagnitude < 1e-4f ? Vector3.up : v.normalized;
        }

        void Snd(Ar6dofGameSound.Id id, float vol) { if (sound != null) sound(id, vol); }

        void SndLater(float delay, Ar6dofGameSound.Id id, float vol)
        {
            laters.Add(new Later { at = Time.unscaledTime + delay, id = id, vol = vol });
        }

        // ---------------------------------------------------------------- particules
        P Alloc(bool force)
        {
            for (int k = 0; k < Max; k++)
            {
                cursor = (cursor + 1) % Max;
                if (!ps[cursor].alive) return ps[cursor];
            }
            if (!force) return null;
            P best = null; float ba = -1f;
            for (int i = 0; i < Max; i++) if (ps[i].kind == 1 && ps[i].age > ba) { ba = ps[i].age; best = ps[i]; }
            return best;
        }

        P Add(int kind, Vector3 pos, Vector3 vel, float life, float s0, float s1, Color c0, Color c1, float grav, float drag, float delay, bool force)
        {
            var p = Alloc(force);
            if (p == null) return null;
            p.kind = kind; p.pos = pos; p.vel = vel; p.life = life; p.s0 = s0; p.s1 = s1; p.c0 = c0; p.c1 = c1;
            p.grav = grav; p.drag = drag; p.age = -delay; p.alive = true; p.rot = Quaternion.identity; p.spin = Vector3.zero;
            return p;
        }

        static readonly Color FireA = new Color(1f, 0.92f, 0.5f, 0.95f), FireB = new Color(1f, 0.28f, 0.04f, 0f);
        static readonly Color SmokeA = new Color(0.14f, 0.14f, 0.15f, 0.8f), SmokeB = new Color(0.4f, 0.4f, 0.42f, 0f);

        void Explosion(Vector3 p, float k, float delay)
        {
            Add(0, p + Vector3.up * 0.01f, Vector3.zero, 0.16f, 0.04f * k, 0.12f * k, new Color(1f, 1f, 0.85f, 1f), new Color(1f, 0.8f, 0.3f, 0f), 0f, 0f, delay, true);     // eclair blanc
            for (int i = 0; i < 7; i++)
            {
                Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) * 0.8f + 0.2f;
                Add(0, p + Vector3.up * 0.01f, d * R(0.04f, 0.11f) * k, R(0.35f, 0.6f), 0.03f * k, R(0.06f, 0.09f) * k, FireA, FireB, -0.05f, 1.5f, delay, true);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) * 0.4f + 0.6f;
                Add(1, p + Vector3.up * 0.015f, d * R(0.05f, 0.12f), R(1.5f, 2.5f), 0.03f * k, R(0.08f, 0.12f) * k, SmokeA, SmokeB, -0.03f, 0.7f, delay + R(0f, 0.15f), true);
            }
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) + 0.5f;
                Add(0, p + Vector3.up * 0.01f, d * R(0.25f, 0.5f), R(0.4f, 0.8f), 0.007f, 0.002f, new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.4f, 0.1f, 0f), 1.6f, 0.2f, delay, false);   // etincelles
            }
            Add(2, p + Vector3.up * 0.006f, Vector3.zero, 0.45f, 0.02f, 0.16f * k, new Color(1f, 0.9f, 0.6f, 0.8f), new Color(1f, 0.5f, 0.2f, 0f), 0f, 0f, delay, true);          // onde de choc
        }

        void BigExtra(Vector3 p, float delay)
        {
            Add(2, p + Vector3.up * 0.006f, Vector3.zero, 0.8f, 0.03f, 0.34f, new Color(1f, 0.95f, 0.75f, 0.9f), new Color(1f, 0.5f, 0.2f, 0f), 0f, 0f, delay, true);
            for (int i = 0; i < 12; i++)
            {
                Vector3 d = RandDir(); d.y = Mathf.Abs(d.y) * 1.2f + 0.7f;
                var c = new Color(0.12f + R(0f, 0.12f), 0.1f + R(0f, 0.08f), 0.1f, 1f);
                var q = Add(3, p + Vector3.up * 0.02f, d * R(0.25f, 0.6f), R(0.9f, 1.4f), R(0.008f, 0.016f), R(0.008f, 0.016f), c, c, 2.6f, 0.1f, delay, false);
                if (q != null) q.spin = new Vector3(R(-700f, 700f), R(-700f, 700f), R(-700f, 700f));
            }
            for (int i = 0; i < 6; i++)
                Add(1, p + new Vector3(R(-0.02f, 0.02f), 0.02f, R(-0.02f, 0.02f)), new Vector3(R(-0.03f, 0.03f), R(0.1f, 0.2f), R(-0.03f, 0.03f)), R(2.4f, 3.4f), 0.04f, R(0.16f, 0.24f), SmokeA, SmokeB, -0.02f, 0.5f, delay + 0.1f + R(0f, 0.3f), true);
        }

        void SplashAt(Vector3 p)
        {
            Add(2, p + Vector3.up * 0.004f, Vector3.zero, 0.7f, 0.01f, 0.075f, new Color(0.8f, 0.92f, 1f, 0.8f), new Color(0.6f, 0.8f, 1f, 0f), 0f, 0f, 0f, true);
            for (int i = 0; i < 10; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                var v = new Vector3(Mathf.Cos(a) * R(0.02f, 0.07f), R(0.3f, 0.55f), Mathf.Sin(a) * R(0.02f, 0.07f));
                Add(4, p + Vector3.up * 0.004f, v, 0.9f, 0.009f, 0.005f, new Color(0.85f, 0.94f, 1f, 1f), new Color(0.7f, 0.85f, 1f, 1f), 2.4f, 0.1f, 0f, false);
            }
            for (int i = 0; i < 3; i++)
                Add(0, p + Vector3.up * 0.01f, new Vector3(R(-0.02f, 0.02f), R(0.1f, 0.2f), R(-0.02f, 0.02f)), 0.35f, 0.02f, 0.05f, new Color(0.9f, 0.96f, 1f, 0.55f), new Color(0.9f, 0.96f, 1f, 0f), 0.6f, 1f, 0f, false);
        }

        // ---------------------------------------------------------------- tirs
        /// <summary>Lance un tir (le meme vol quel que soit le resultat). 'fromPlayer' : du plateau du joueur vers la flotte ennemie ; faux : du PC vers le joueur. 'impact' est appele a l'arrivee.</summary>
        public void Launch(bool fromPlayer, int cell, Action impact)
        {
            var tb = fromPlayer ? enemy : own;
            var ob = fromPlayer ? own : enemy;
            Vector3 e = tb.CellLocal(cell);
            Vector3 c0 = ob.GridLocal((ob.rows - 1) * 0.5f, (ob.cols - 1) * 0.5f);
            float sign = e.x >= c0.x ? 1f : -1f;
            float dist = Vector2.Distance(new Vector2(e.x, e.z), new Vector2(c0.x, c0.z));
            var f = new Flight { e = e, done = impact, t0 = Time.unscaledTime + 0.15f, type = rnd.Next(3), sign = sign };
            try
            {
                if (f.type == 0)
                {
                    f.s = c0 + Vector3.up * 0.01f;
                    f.dur = 1.3f + dist * 0.6f; f.h = 0.2f + dist * 0.35f;
                    f.go = MakePart("Missile", missileMesh, new Color(0.92f, 0.92f, 0.95f));
                    f.go.gameObject.SetActive(false);
                }
                else if (f.type == 1)
                {
                    f.s = new Vector3(e.x - sign * 0.5f, 0.2f, e.z);
                    f.dur = 1.4f + 0.36f;
                    f.go = MakePart("Avion", planeMesh, new Color(0.8f, 0.82f, 0.86f));
                    f.go.gameObject.SetActive(false);
                }
                else
                {
                    f.s = c0 + new Vector3(sign * (ob.cols * ob.cell * 0.5f + 0.02f), 0.005f, 0f);
                    f.dur = 0.7f + dist * 0.3f; f.h = 0.04f + dist * 0.15f;
                    f.go = MakePart("Canon", barrelMesh, new Color(0.28f, 0.3f, 0.34f));
                    f.go.gameObject.SetActive(false);
                    f.extra = MakePart("Obus", shellMesh, new Color(0.42f, 0.4f, 0.26f));
                    f.extra.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("AR6DoF : tir impossible a animer : " + ex.Message);
                if (f.go != null) UnityEngine.Object.Destroy(f.go.gameObject);
                if (f.extra != null) UnityEngine.Object.Destroy(f.extra.gameObject);
                if (impact != null) impact();
                return;
            }
            flights.Add(f);
        }

        Transform MakePart(string name, Mesh m, Color c)
        {
            return t.Part(null, name, m, null, Vector3.zero, Vector3.one, c).transform;
        }

        static void Kill(Transform tr) { if (tr != null) UnityEngine.Object.Destroy(tr.gameObject); }

        Vector3 Arc(Flight f, float u)
        {
            u = Mathf.Clamp01(u);
            return Vector3.Lerp(f.s, f.e, u) + Vector3.up * (f.h * 4f * u * (1f - u));
        }

        void Land(Flight f)
        {
            if (f.impacted) return;
            f.impacted = true;
            var d = f.done; f.done = null;
            if (d != null) { try { d(); } catch (Exception ex) { Debug.LogWarning("AR6DoF : fin de tir : " + ex.Message); } }
        }

        void UpdateFlights(float now)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                var f = flights[i];
                if (now < f.t0) continue;
                float time = now - f.t0;
                if (!f.started)
                {
                    f.started = true;
                    if (f.go != null) f.go.gameObject.SetActive(true);
                    if (f.type == 2)
                    {
                        Vector3 mz = f.s + (f.e - f.s).normalized * 0.05f + Vector3.up * 0.016f;
                        Vector3 h = f.e - f.s; h.y = 0f; if (h.sqrMagnitude < 1e-6f) h = Vector3.right;
                        f.go.localPosition = f.s;
                        f.go.localRotation = Quaternion.LookRotation(h.normalized + Vector3.up * 0.35f);
                        if (f.extra != null) f.extra.gameObject.SetActive(true);
                        for (int k = 0; k < 5; k++) Add(0, mz, RandDir() * 0.06f, R(0.1f, 0.2f), 0.02f, 0.05f, FireA, FireB, 0f, 2f, 0f, true);
                        for (int k = 0; k < 4; k++) Add(1, mz, RandDir() * 0.03f + Vector3.up * 0.03f, R(0.8f, 1.3f), 0.015f, 0.06f, new Color(0.55f, 0.55f, 0.58f, 0.7f), SmokeB, -0.02f, 0.8f, 0f, true);
                        Snd(Ar6dofGameSound.Id.Cannon, 0.9f);
                    }
                    else Snd(Ar6dofGameSound.Id.Whoosh, f.type == 1 ? 0.55f : 0.8f);
                }

                if (f.type == 0 || f.type == 2)
                {
                    float u = time / f.dur;
                    if (u < 1f)
                    {
                        Vector3 p = Arc(f, u), ahead = Arc(f, u + 0.02f), behind = Arc(f, u - 0.02f);
                        Vector3 dir = ahead - behind; if (dir.sqrMagnitude < 1e-9f) dir = Vector3.up; dir.Normalize();
                        Transform mover = f.type == 0 ? f.go : f.extra;
                        if (mover != null)
                        {
                            mover.localPosition = p;
                            mover.localRotation = Quaternion.FromToRotation(Vector3.up, dir);        // missile et obus : la pointe suit la trajectoire
                        }
                        if (now - f.trail > 0.02f)
                        {
                            f.trail = now;
                            if (f.type == 0)
                            {
                                Add(0, p - dir * 0.03f, Vector3.zero, 0.3f, 0.016f, 0.002f, new Color(1f, 0.75f, 0.3f, 0.95f), new Color(1f, 0.3f, 0.05f, 0f), 0f, 0f, 0f, false);
                                Add(1, p - dir * 0.04f, RandDir() * 0.01f, 1.0f, 0.012f, 0.04f, new Color(0.7f, 0.7f, 0.72f, 0.5f), SmokeB, -0.02f, 0.5f, 0f, false);
                            }
                            else Add(1, p, Vector3.zero, 0.5f, 0.008f, 0.025f, new Color(0.7f, 0.7f, 0.72f, 0.4f), SmokeB, 0f, 0f, 0f, false);
                        }
                    }
                    else
                    {
                        Land(f);
                        if (f.extra != null) { Kill(f.extra); f.extra = null; }
                        if (f.type == 0) { Kill(f.go); flights.RemoveAt(i); continue; }
                        if (time > f.dur + 0.6f) { Kill(f.go); flights.RemoveAt(i); }       // le canon reste un instant apres le tir
                    }
                    continue;
                }

                // avion : approche a vitesse constante, largue la bombe au-dessus de la case, puis continue sa route
                const float Approach = 1.4f, Fall = 0.36f;
                float speed = 0.5f / Approach;
                Vector3 pp = new Vector3(f.s.x + f.sign * speed * time, f.s.y + Mathf.Sin(time * 9f) * 0.002f, f.s.z);
                if (f.go != null)
                {
                    f.go.localPosition = pp;
                    f.go.localRotation = Quaternion.Euler(0f, f.sign > 0f ? 0f : 180f, Mathf.Sin(time * 5f) * 4f);
                }
                if (now - f.trail > 0.05f && time < Approach + Fall + 0.5f)
                {
                    f.trail = now;
                    Add(1, pp - new Vector3(f.sign * 0.035f, 0f, 0f), Vector3.zero, 0.7f, 0.007f, 0.022f, new Color(0.8f, 0.8f, 0.82f, 0.4f), SmokeB, 0f, 0f, 0f, false);
                }
                if (!f.released && time >= Approach)
                {
                    f.released = true;
                    f.extra = MakePart("Bombe", bombMesh, new Color(0.15f, 0.16f, 0.18f));
                }
                if (f.released && !f.impacted)
                {
                    float k = Mathf.Clamp01((time - Approach) / Fall);
                    float y = Mathf.Lerp(0.2f, f.e.y, k * k);
                    if (f.extra != null) f.extra.localPosition = new Vector3(f.e.x, y, f.e.z);
                    if (k >= 1f) { Kill(f.extra); f.extra = null; Land(f); }
                }
                if (time > Approach + Fall + 0.9f) { Kill(f.go); Kill(f.extra); flights.RemoveAt(i); }
            }
        }

        // ---------------------------------------------------------------- impact
        /// <summary>L'impact d'un tir : 'res' 0 a l'eau, 1 touche, 2 coule ('shipCells' : toutes les cases du navire coule).</summary>
        public void Impact(bool onEnemy, int cell, int res, int[] shipCells)
        {
            var board = onEnemy ? enemy : own;
            Vector3 p = board.CellLocal(cell);
            if (res <= 0) { SplashAt(p); Snd(Ar6dofGameSound.Id.Splash, 0.8f); return; }
            if (res == 1) { Explosion(p, 1f, 0f); Snd(Ar6dofGameSound.Id.Boom, 0.9f); return; }
            Snd(Ar6dofGameSound.Id.BigBoom, 1f);
            if (shipCells == null || shipCells.Length == 0) { Explosion(p, 1.6f, 0f); BigExtra(p, 0.1f); return; }
            // les cases du navire explosent l'une apres l'autre en partant de celle qui a ete touchee
            var order = new List<int>(shipCells);
            order.Sort((a, b) => Vector3.Distance(board.CellLocal(a), p).CompareTo(Vector3.Distance(board.CellLocal(b), p)));
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < order.Count; i++)
            {
                Vector3 q = board.CellLocal(order[i]);
                mid += q;
                Explosion(q, 1.35f, i * 0.11f);
                if (i > 0) SndLater(i * 0.11f, Ar6dofGameSound.Id.Boom, 0.5f);
            }
            mid /= order.Count;
            BigExtra(mid, 0.2f);
        }

        /// <summary>Navires touches (1) ou coules (2) cases par cases : de la fumee continue de s'en echapper. Les tableaux ont une valeur par case.</summary>
        public void SetBurning(int[] enemyLevels, int[] ownLevels)
        {
            burnE = enemyLevels; burnO = ownLevels;
        }

        void Burn(float now, int[] lv, float[] next, Ar6dofBoard3D b)
        {
            if (lv == null) return;
            for (int i = 0; i < lv.Length && i < next.Length; i++)
            {
                if (lv[i] <= 0 || now < next[i]) continue;
                next[i] = now + (lv[i] >= 2 ? 0.4f : 0.65f) * R(0.7f, 1.3f);
                Vector3 p = b.CellLocal(i) + new Vector3(R(-0.006f, 0.006f), 0.012f, R(-0.006f, 0.006f));
                bool sunk = lv[i] >= 2;
                Add(1, p, new Vector3(R(-0.006f, 0.006f), R(0.05f, 0.09f), R(-0.006f, 0.006f)), 1.6f, 0.012f, sunk ? 0.06f : 0.045f,
                    sunk ? new Color(0.1f, 0.1f, 0.11f, 0.7f) : new Color(0.25f, 0.25f, 0.27f, 0.55f), SmokeB, -0.01f, 0.4f, 0f, false);
                if (R(0f, 1f) < (sunk ? 0.5f : 0.35f))
                    Add(0, p, new Vector3(0f, R(0.03f, 0.06f), 0f), 0.35f, 0.014f, 0.003f, new Color(1f, 0.6f, 0.15f, 0.85f), new Color(1f, 0.25f, 0.05f, 0f), 0f, 0f, 0f, false);
            }
        }

        // ---------------------------------------------------------------- image par image
        public void Tick(float now)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
            UpdateFlights(now);
            for (int i = laters.Count - 1; i >= 0; i--)
                if (now >= laters[i].at) { Snd(laters[i].id, laters[i].vol); laters.RemoveAt(i); }
            Burn(now, burnE, nextE, enemy);
            Burn(now, burnO, nextO, own);

            Matrix4x4 l2w = t.content.localToWorldMatrix;
            for (int i = 0; i < Max; i++)
            {
                var p = ps[i];
                if (!p.alive) continue;
                p.age += dt;
                if (p.age < 0f) continue;
                if (p.age >= p.life) { p.alive = false; continue; }
                float u = p.age / p.life;
                if (p.drag > 0f) p.vel *= Mathf.Max(0f, 1f - p.drag * dt);
                p.vel.y -= p.grav * dt;
                p.pos += p.vel * dt;
                if (p.kind >= 3 && p.pos.y < 0.002f && p.vel.y < 0f) { p.alive = false; continue; }
                if (p.spin.sqrMagnitude > 0f) p.rot = Quaternion.Euler(p.spin * dt) * p.rot;
                float size = Mathf.Lerp(p.s0, p.s1, u);
                Color c = Color.Lerp(p.c0, p.c1, u);
                Mesh m; Material mat; Vector3 sc;
                switch (p.kind)
                {
                    case 0: m = sphere; mat = mAdd; sc = Vector3.one * size; break;
                    case 1: m = sphere; mat = mAlpha; sc = Vector3.one * size; c.a *= Mathf.Clamp01(p.age / 0.12f); break;
                    case 2: m = ring; mat = mAdd; sc = new Vector3(size, size * 0.4f, size); break;
                    case 3: m = box; mat = mOpaque; sc = new Vector3(size, size * 0.5f, size * 0.8f); break;
                    default: m = sphere; mat = mOpaque; sc = Vector3.one * size; break;
                }
                mpb.SetColor("_Color", c);
                mpb.SetFloat("_Glow", 0f);
                Graphics.DrawMesh(m, l2w * Matrix4x4.TRS(p.pos, p.rot, sc), mat, 0, null, 0, mpb);
            }
        }
    }
}
