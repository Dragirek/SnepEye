using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Dessins des memes : trois personnages ORIGINAUX (un chat toupie, un escargot immortel, un cafard qui danse), decrits en formes simples et transformes en texture
    /// une seule fois, a la premiere pose (Ar6dofGameArt.FromSpec). Aucune image a installer. Repere 64 x 64, y vers le haut.
    /// </summary>
    public static class Ar6dofMemeArt
    {
        public const int Cat = 0, Snail = 1, Roach = 2, Count = 3;
        public static readonly string[] Names = { "Chat toupie", "Escargot immortel", "Cafard dansant" };
        public static readonly string[] AddNames = { "+ Chat", "+ Escargot", "+ Cafard" };
        public static readonly float[] Sizes = { 0.32f, 0.30f, 0.34f };

        static readonly string[] Spec =
        {
            // chat gris-bleu, yeux brillants, bouche ouverte, moustaches
            "#8fa6c4; e 32 20 16 14; #f4f6fa; e 32 17 9 9; #8fa6c4; e 22 7 6.5 3.5; e 42 7 6.5 3.5;" +
            " #8fa6c4; p 17 46 15 62 29 54; p 47 46 49 62 35 54; c 32 38 17;" +
            " #ff9fb2; p 19 50 19 57 25 54; p 45 50 45 57 39 54;" +
            " #6f86a6; s 32 49 32 54 1.2; s 27 48.5 26 52.5 1.1; s 37 48.5 38 52.5 1.1;" +
            " #f4f6fa; e 32 31 9.5 6.5;" +
            " #20242c; e 24.5 38.5 3.6 4.6; e 39.5 38.5 3.6 4.6; #fff; c 25.7 40.3 1.3; c 40.7 40.3 1.3;" +
            " #ff9fb2; c 18.5 32 2.6; c 45.5 32 2.6;" +
            " #ff8fa3; t 30 35.2 34 35.2 32 33;" +
            " #7a1f2e; e 32 28 3.6 3.2; #ff8fa3; e 32 26.8 2.2 1.3;" +
            " #20242c; s 17 32 9 31 0.35; s 17 29.5 9 29.5 0.35; s 47 32 55 31 0.35; s 47 29.5 55 29.5 0.35",
            // escargot de profil avec une petite auriole
            "#f0dc8a; e 30 11 27 6; p 43 12 55 12 56 26 51 34 45 26; e 51.5 25 6 11;" +
            " #d9953f; c 26 27 17.5; #8a5a1f; o 26 27 11.5 1.4; o 26 27 5.5 1.3; c 26 27 1.6; #f0b868; s 18 36 24 41 1.8;" +
            " #e6cf75; s 50 34 48.5 46 1.1; s 55 33 57.5 45 1.1; #fff; c 48.5 47.5 3; c 57.5 46.5 3; #20242c; c 49.3 47.5 1.4; c 58.3 46.5 1.4;" +
            " #20242c; s 48 30 50 28.7 0.55; s 50 28.7 53 28.7 0.55; s 53 28.7 55 30 0.55;" +
            " #ffe27a; o 26 56 6.5 1.3",
            // cafard debout, lunettes noires, un bras en l'air et l'autre en bas
            "#6b3d1c; s 24 14 21 5 1.8; s 40 14 43 5 1.8; e 20 3 3.6 1.8; e 44 3.5 3.6 1.8;" +
            " s 19 36 9 46 1.8; s 9 46 5 55 1.6; s 45 34 55 29 1.8; s 55 29 59 20 1.6;" +
            " s 28 55 19 62 0.9; s 36 55 45 62 0.9;" +
            " e 32 27 13.5 19; #a96a35; e 25 34 3 8; #4a2810; s 32 44 32 9 0.7; s 22 30 42 30 0.8; s 22 23 42 23 0.8; s 24 16 40 16 0.8;" +
            " #6b3d1c; e 32 50 10.5 8;" +
            " #111; r 26.5 50.5 4.6 3.3 1.2; r 37.5 50.5 4.6 3.3 1.2; s 30 51 34 51 0.7; #fff; s 24.5 52 26.5 53 0.5; s 35.5 52 37.5 53 0.5;" +
            " #111; s 28.5 45.5 30.5 44.5 0.55; s 30.5 44.5 33.5 44.5 0.55; s 33.5 44.5 35.5 45.5 0.55",
        };

        static readonly Color[] Outline =
        {
            new Color(0.12f, 0.10f, 0.14f), new Color(0.20f, 0.12f, 0.05f), new Color(0.10f, 0.06f, 0.03f)
        };

        public static Texture2D Tex(int kind)
        {
            kind = Mathf.Clamp(kind, 0, Count - 1);
            var sp = Ar6dofGameArt.FromSpec("meme" + kind, Spec[kind], 160, 2.5f, Outline[kind]);
            return sp != null ? sp.texture : null;
        }
    }

    /// <summary>
    /// Les memes poses dans le monde. Rien n'existe tant qu'aucun meme n'est pose : pas d'objet, pas de mise a jour. Chaque meme est un petit quad avec sa propre animation
    /// (script Ar6dofMemeActor) ; leurs textures et materiaux sont partages et crees a la premiere pose. Les memes ne sont pas enregistres : ils disparaissent a la fermeture du casque.
    /// </summary>
    public class Ar6dofMemes
    {
        public const int Max = 12;
        public readonly List<Ar6dofMemeActor> actors = new List<Ar6dofMemeActor>();
        public bool interactive = true;
        public float snailCmS = 2f;
        public float sizeMul = 1f;

        GameObject root;
        static Shader shader;
        static Mesh quad;
        readonly Material[] mats = new Material[Ar6dofMemeArt.Count];

        public Ar6dofMemes()
        {
            try
            {
                interactive = PlayerPrefs.GetInt("ar6dof_memes_laser", 1) != 0;
                snailCmS = Mathf.Clamp(PlayerPrefs.GetFloat("ar6dof_memes_snail", 2f), 0f, 10f);
                sizeMul = Mathf.Clamp(PlayerPrefs.GetFloat("ar6dof_memes_size", 1f), 0.5f, 2f);
            }
            catch (Exception) { }
        }

        static void PutF(string k, float v) { try { PlayerPrefs.SetFloat(k, v); } catch (Exception) { } }

        public void SetInteractive(bool on)
        {
            interactive = on;
            try { PlayerPrefs.SetInt("ar6dof_memes_laser", on ? 1 : 0); } catch (Exception) { }
            foreach (var a in actors) a.SetInteractive(on);
        }

        public void SetSnail(float cmPerSec) { snailCmS = Mathf.Clamp(cmPerSec, 0f, 10f); PutF("ar6dof_memes_snail", snailCmS); }
        public void SetSize(float mul) { sizeMul = Mathf.Clamp(mul, 0.5f, 2f); PutF("ar6dof_memes_size", sizeMul); }

        static Shader Sh()
        {
            if (shader == null)
            {
                shader = Resources.Load<Shader>("Ar6dofObject");
                if (shader == null) shader = Shader.Find("AR6DoF/Object");
                if (shader == null) shader = Shader.Find("Sprites/Default");
            }
            return shader;
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "AR6DoF_MemeQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateBounds();
            return quad;
        }

        Material Mat(int kind)
        {
            if (mats[kind] == null)
            {
                var m = new Material(Sh());
                var t = Ar6dofMemeArt.Tex(kind);
                if (t != null) m.mainTexture = t;
                m.SetColor("_Color", Color.white);
                m.SetFloat("_Shade", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3000;
                mats[kind] = m;
            }
            return mats[kind];
        }

        public Ar6dofMemeActor Add(int kind, Vector3 pos, float yaw)
        {
            if (actors.Count >= Max) return null;
            kind = Mathf.Clamp(kind, 0, Ar6dofMemeArt.Count - 1);
            if (root == null) root = new GameObject("AR6DoF_Memes");
            var go = new GameObject("Meme " + Ar6dofMemeArt.Names[kind]);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat(kind);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(1f, 1f, 0.12f);   // assez epais pour rester visable au laser quand le chat est de profil
            col.enabled = interactive;
            var a = go.AddComponent<Ar6dofMemeActor>();
            a.Init(this, kind, pos, yaw, col);
            actors.Add(a);
            return a;
        }

        public void Remove(Ar6dofMemeActor a)
        {
            if (a == null) return;
            actors.Remove(a);
            if (a.gameObject != null) UnityEngine.Object.Destroy(a.gameObject);
        }

        public void Clear()
        {
            foreach (var a in actors) if (a != null && a.gameObject != null) UnityEngine.Object.Destroy(a.gameObject);
            actors.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            for (int i = 0; i < mats.Length; i++) { if (mats[i] != null) UnityEngine.Object.Destroy(mats[i]); mats[i] = null; }
        }
    }

    /// <summary>
    /// Un meme vivant. Chat toupie : tourne sur lui-meme, chaque touche l'accelere, il finit par decoller puis redescend en ralentissant. Escargot immortel : avance vers toi,
    /// lentement, sans jamais s'arreter (il reste a 40 cm de ta tete), et fait un petit saut quand on le touche. Cafard dansant : danse en rythme (balancement, rotation,
    /// tremblement), chaque touche change de pas. Au laser : une touche reagit, garder le bouton appuye en bougeant le deplace.
    /// </summary>
    public class Ar6dofMemeActor : MonoBehaviour, IAr6dofPointerTarget, IAr6dofPointerDrag
    {
        const float Bpm = 118f;
        const float SnailStop = 0.4f;

        Ar6dofMemes owner;
        public int kind;
        BoxCollider col;
        public Vector3 home;
        float yawBase, spinAngle, boost, lift, kick, hoverK;
        int style;
        bool flipped, hover, dragging, moved;
        float downTime, dist;
        Vector3 offset;
        static Transform head;

        public string Label { get { return Ar6dofMemeArt.Names[Mathf.Clamp(kind, 0, Ar6dofMemeArt.Count - 1)]; } }

        public void Init(Ar6dofMemes o, int k, Vector3 pos, float yaw, BoxCollider c)
        {
            owner = o; kind = k; home = pos; yawBase = yaw; col = c;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void SetInteractive(bool on) { if (col != null) col.enabled = on; if (!on) { dragging = false; hover = false; } }

        /// <summary>Ramene le meme devant toi.</summary>
        public void Bring(Vector3 pos, float yaw) { home = pos; yawBase = yaw; kick = 1f; }

        static Transform Head()
        {
            if (head == null)
            {
                var d = Ar6dofDock.Instance;
                head = d != null && d.passthrough != null ? d.passthrough.HeadView : null;
                if (head == null && Camera.main != null) head = Camera.main.transform;
            }
            return head;
        }

        void Tap()
        {
            kick = 1f;
            if (kind == Ar6dofMemeArt.Cat) boost = Mathf.Min(boost + 260f, 2000f);   // plus on touche, plus il tourne
            else if (kind == Ar6dofMemeArt.Roach) style = (style + 1) % 3;           // un autre pas de danse
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float t = Time.unscaledTime;
            hoverK = Mathf.MoveTowards(hoverK, hover || dragging ? 1f : 0f, dt * 6f);
            kick *= Mathf.Exp(-6f * dt);
            if (kick < 0.002f) kick = 0f;

            float sx = 1f, sy = 1f, roll = 0f, hop = 0f, sideShake = 0f;
            float yaw = yawBase;
            Transform h = Head();

            switch (kind)
            {
                case Ar6dofMemeArt.Cat:
                {
                    boost *= Mathf.Exp(-0.5f * dt);
                    if (boost < 1f) boost = 0f;
                    spinAngle = Mathf.Repeat(spinAngle + (90f + boost) * dt, 360f);
                    float target = Mathf.Clamp01((boost - 500f) / 1500f) * 0.45f;
                    lift = Mathf.Lerp(lift, target, 1f - Mathf.Exp(-3f * dt));
                    hop = lift + Mathf.Sin(t * 2.4f) * 0.012f;
                    roll = Mathf.Sin(t * 1.7f) * 3f + Mathf.Clamp01(boost / 1500f) * Mathf.Sin(t * 11f) * 6f;
                    yaw = yawBase + spinAngle;
                    sy += kick * 0.12f; sx -= kick * 0.08f;
                    break;
                }
                case Ar6dofMemeArt.Snail:
                {
                    float w = Mathf.Sin(t * 2.4f);
                    sx += 0.07f * w; sy -= 0.05f * w;
                    roll = Mathf.Sin(t * 2.4f + 1f) * 1.5f;
                    hop = kick * 0.08f;
                    sy += kick * 0.15f;
                    if (h != null)
                    {
                        Vector3 to = h.position - home;
                        to.y = 0f;
                        float d = to.magnitude;
                        if (d > SnailStop && owner.snailCmS > 0f && !dragging)
                        {
                            float step = Mathf.Min(owner.snailCmS * 0.01f * dt, d - SnailStop);
                            home += to / d * step;
                        }
                        if (d > 0.01f) yaw = Mathf.Atan2(-to.x, -to.z) * Mathf.Rad2Deg;
                        float lat = Vector3.Dot(home - h.position, h.right);
                        if (lat > 0.06f) flipped = true; else if (lat < -0.06f) flipped = false;   // il regarde du cote ou il va
                    }
                    break;
                }
                default:
                {
                    float b = t * Bpm / 60f;
                    float s = Mathf.Sin(Mathf.PI * b), a = Mathf.Abs(s);
                    if (h != null)
                    {
                        Vector3 to = home - h.position;
                        to.y = 0f;
                        if (to.sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    }
                    if (style == 1) spinAngle = Mathf.Repeat(spinAngle + 280f * dt, 360f);
                    else spinAngle = Mathf.MoveTowardsAngle(spinAngle, 0f, 540f * dt);
                    yaw += spinAngle;
                    if (style == 2)
                    {
                        roll = Mathf.Sin(t * 34f) * 9f;
                        sideShake = Mathf.Sin(t * 41f) * 0.006f;
                        hop = a * 0.012f;
                    }
                    else
                    {
                        roll = s * (style == 1 ? 4f : 13f);
                        hop = a * (style == 1 ? 0.03f : 0.045f);
                        sy += 0.08f * (1f - a); sx -= 0.05f * (1f - a);   // ecrase en retombant
                    }
                    sx -= kick * 0.1f; sy += kick * 0.14f;
                    break;
                }
            }

            float size = Ar6dofMemeArt.Sizes[Mathf.Clamp(kind, 0, Ar6dofMemeArt.Count - 1)] * owner.sizeMul * (1f + 0.07f * hoverK);
            var tr = transform;
            tr.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, roll);
            tr.position = home + Vector3.up * hop + tr.right * sideShake;
            tr.localScale = new Vector3(size * sx * (flipped ? -1f : 1f), size * sy, 1f);
        }

        // ------------------------------------------------------------------ laser
        public void OnPointerEnter() { hover = true; }
        public void OnPointerExit() { hover = false; }
        public void OnPointerHover(Vector3 worldPoint) { }

        public void OnPointerDown(Vector3 worldPoint, int button)
        {
            dragging = true;
            moved = false;
            downTime = Time.unscaledTime;
            dist = Vector3.Distance(Ar6dofPointer.LastRay.origin, worldPoint);
            offset = home - worldPoint;
        }

        public void OnPointerUp(Vector3 worldPoint, int button)
        {
            if (!dragging) return;
            dragging = false;
            if (!moved && Time.unscaledTime - downTime < 0.7f) Tap();   // une touche courte : il reagit
        }

        public void OnDrag(Ray ray)
        {
            if (!dragging) return;
            Vector3 p = ray.GetPoint(dist) + offset;
            if (!moved)
            {
                if ((p - home).sqrMagnitude < 0.0016f) return;   // moins de 4 cm : c'est encore une touche
                moved = true;
            }
            home = p;
        }
    }

    /// <summary>
    /// Memes : pose dans le monde un chat toupie, un escargot immortel ou un cafard dansant. Touche-les au laser, garde le bouton appuye pour les deplacer.
    /// Reglages : reagir au laser (a couper si un meme gene le laser devant une fenetre), vitesse de l'escargot, taille. Sans meme pose, rien ne tourne.
    /// </summary>
    public class Ar6dofMemesApp : Ar6dofAppWindow
    {
        const int PerPage = 4;
        readonly Ar6dofMemes memes = new Ar6dofMemes();
        int page;
        bool confirmAll;
        string status = "";

        public Ar6dofMemesApp(Ar6dofDock d) : base(d, "memes", "Memes", "memes", "Chat toupie, escargot immortel et cafard dansant a poser dans le monde", 700f) { }

        public override void Destroy()
        {
            memes.Dispose();
            base.Destroy();
        }

        protected override void OnVisible(bool on) { if (!on) confirmAll = false; }

        Transform Head()
        {
            if (dock.passthrough != null && dock.passthrough.HeadView != null) return dock.passthrough.HeadView;
            return Camera.main != null ? Camera.main.transform : null;
        }

        /// <summary>Position et cap devant toi : les memes se rangent en eventail pour ne pas se chevaucher.</summary>
        void Spot(int kind, int index, out Vector3 pos, out float yaw)
        {
            var h = Head();
            Vector3 f = Vector3.forward;
            if (h != null)
            {
                f = h.forward; f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
            }
            Vector3 dir = Quaternion.AngleAxis(((index % 5) - 2) * 18f, Vector3.up) * f;
            float d = kind == Ar6dofMemeArt.Snail ? 1.3f : 0.9f;
            Vector3 origin = h != null ? h.position : Vector3.zero;
            pos = origin + dir * d;
            pos.y = origin.y - 0.05f;
            yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        void AddMeme(int kind)
        {
            Vector3 pos; float yaw;
            Spot(kind, memes.actors.Count, out pos, out yaw);
            var a = memes.Add(kind, pos, yaw);
            status = a == null ? Ar6dofLoc.F("Maximum : {0} memes", Ar6dofMemes.Max) : "";
            page = Mathf.Max(0, (memes.actors.Count - 1) / PerPage);
            confirmAll = false;
            Rebuild();
        }

        protected override void BuildMain()
        {
            Para("Pose des memes animes dans le monde. Touche-les au laser : ils reagissent. Garde le bouton appuye en bougeant pour les deplacer.", 56, 18);
            BtnRow(54f, 21, Ar6dofMemeArt.AddNames[0], (Action)(() => AddMeme(0)),
                            Ar6dofMemeArt.AddNames[1], (Action)(() => AddMeme(1)),
                            Ar6dofMemeArt.AddNames[2], (Action)(() => AddMeme(2)));
            var st = Para("", 30, 17);
            refreshers.Add(() => st.text = status);

            Para("Chat toupie : touche-le plusieurs fois, il tourne de plus en plus vite et finit par decoller.", 44, 16);
            Para("Escargot immortel : il avance vers toi, lentement, sans jamais s'arreter. Il reste a 40 cm de toi.", 44, 16);
            Para("Cafard dansant : il danse en rythme. Touche-le pour changer de pas.", 44, 16);

            Section("Reglages");
            ToggleRow("Reagir au laser", () => memes.interactive, v => memes.SetInteractive(v), 50f, 22);
            Para("Coupe-le si un meme gene le laser devant une fenetre : ils continuent de bouger.", 30, 16);
            SliderRow("Vitesse de l'escargot", 0f, 10f, () => memes.snailCmS, v => memes.SetSnail(v), v => Fmt("{0:0.#} cm/s", v), 50f);
            SliderRow("Taille des memes", 0.5f, 2f, () => memes.sizeMul, v => memes.SetSize(v), Pct, 50f);

            int n = memes.actors.Count;
            Section(Ar6dofLoc.F("Memes poses : {0}", n));
            if (n == 0) { Para("Aucun meme. Utilise un des boutons « + » ci-dessus.", 30, 18); return; }
            Para("Touche un nom pour le ramener devant toi. Les memes ne sont pas gardes a la fermeture du casque.", 44, 16);
            int pages = Mathf.Max(1, (n + PerPage - 1) / PerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            for (int i = page * PerPage; i < Mathf.Min(n, (page + 1) * PerPage); i++)
            {
                var a = memes.actors[i];
                int idx = i;
                var bs = BtnRow(48f, 20, "", (Action)(() => Bring(a)), "Retirer", (Action)(() => { memes.Remove(a); confirmAll = false; Rebuild(); }));
                var lt = bs[0].GetComponentInChildren<Text>();
                if (lt != null) lt.text = Ar6dofLoc.T(a.Label) + " " + (idx + 1);
            }
            if (pages > 1)
            {
                BtnRow(46f, 22, "<", (Action)(() => { page = (page + pages - 1) % pages; Rebuild(); }),
                                Ar6dofLoc.F("Page {0}/{1}", page + 1, pages), (Action)(() => { }),
                                ">", (Action)(() => { page = (page + 1) % pages; Rebuild(); }));
            }
            Text dt;
            var del = BtnT("", () =>
            {
                if (!confirmAll) { confirmAll = true; Refresh(); return; }
                memes.Clear();
                confirmAll = false; page = 0;
                Rebuild();
            }, out dt, 48f, 20);
            Text dtt = dt;
            refreshers.Add(() => { dtt.text = Ar6dofLoc.T(confirmAll ? "Confirmer : tout retirer" : "Tout retirer"); Ar6dofTheme.SetSelected(del, confirmAll); });
        }

        void Bring(Ar6dofMemeActor a)
        {
            Vector3 pos; float yaw;
            Spot(a.kind, 2, out pos, out yaw);   // au milieu de l'eventail
            a.Bring(pos, yaw);
        }
    }
}
