using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// La table des jeux 3D : un socle pose dans le monde (devant toi, un peu en dessous du regard, incline vers toi) qui porte le plateau, les des ou la machine a sous.
    /// Le socle se deplace au laser (garder le bouton appuye). Rien n'existe tant qu'un jeu 3D n'est pas ouvert ; tout est detruit a la fermeture de la fenetre.
    /// </summary>
    public sealed class Ar6dofTable3D
    {
        public const float TileH = 0.006f;
        static readonly List<Ar6dofTable3D> live = new List<Ar6dofTable3D>();
        static Shader shader;

        public readonly GameObject root;
        public readonly Transform content;
        public readonly Material mat;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly List<Action<float>> ticks = new List<Action<float>>();
        readonly List<Ar6dofLabel3D> labels = new List<Ar6dofLabel3D>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        struct Pop { public Transform tr; public float start; public Vector3 scale; }
        readonly List<Pop> pops = new List<Pop>();
        readonly Transform slab;
        readonly Renderer slabRend;
        readonly Color slabCol;
        float minX, maxX, minZ, maxZ;
        bool hasBounds;

        public static Shader Sh()
        {
            if (shader == null)
            {
                shader = Resources.Load<Shader>("Ar6dofObject");
                if (shader == null) shader = Shader.Find("AR6DoF/Object");
                if (shader == null) shader = Shader.Find("Sprites/Default");
            }
            return shader;
        }

        public Ar6dofTable3D(Color slabColor)
        {
            root = new GameObject("AR6DoF_Table");
            content = new GameObject("Contenu").transform;
            content.SetParent(root.transform, false);
            mat = NewMat(null);
            slabCol = slabColor;
            var sg = new GameObject("Socle");
            sg.transform.SetParent(root.transform, false);
            sg.AddComponent<MeshFilter>().sharedMesh = Ar6dofShapes.UnitBox();
            slabRend = sg.AddComponent<MeshRenderer>();
            slabRend.sharedMaterial = mat;
            slabRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            slabRend.receiveShadows = false;
            sg.AddComponent<BoxCollider>().size = Vector3.one;
            var h = sg.AddComponent<Ar6dofTableHandle>();
            h.table = this;
            slab = sg.transform;
            slab.gameObject.SetActive(false);
            Paint(slabRend, slabColor, 0f);
            live.Add(this);
            Place();
        }

        public Material NewMat(Texture tex)
        {
            var m = new Material(Sh());
            if (tex != null) m.mainTexture = tex;
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Shade", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            owned.Add(m);
            return m;
        }

        public T Own<T>(T o) where T : UnityEngine.Object { owned.Add(o); return o; }

        public void Paint(Renderer r, Color c, float glow)
        {
            if (r == null) return;
            mpb.Clear();
            mpb.SetColor("_Color", new Color(c.r, c.g, c.b, 1f));
            mpb.SetFloat("_Glow", glow);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Un morceau de decor : maillage + materiau + couleur.</summary>
        public GameObject Part(Transform parent, string name, Mesh mesh, Material m, Vector3 pos, Vector3 scale, Color col)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : content, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m != null ? m : mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Paint(r, col, 0f);
            return go;
        }

        static Transform HeadT()
        {
            var d = Ar6dofDock.Instance;
            Transform h = d != null ? d.HeadTransform : null;
            if (h == null && Camera.main != null) h = Camera.main.transform;
            return h;
        }

        /// <summary>Remet la table devant toi, un peu en dessous du regard, inclinee vers toi.</summary>
        public void Place()
        {
            Transform h = HeadT();
            Vector3 head = h != null ? h.position : Vector3.zero;
            Vector3 f = h != null ? h.forward : Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);
            int idx = Mathf.Max(0, live.IndexOf(this));
            Vector3 pos = head + f * 0.75f + Vector3.down * 0.34f + right * (0.62f * idx);
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(-22f, yaw, 0f));
        }

        // ---------------------------------------------------------------- socle
        public void SetSlab(float cx, float cz, float w, float d)
        {
            hasBounds = true;
            minX = cx - w * 0.5f; maxX = cx + w * 0.5f; minZ = cz - d * 0.5f; maxZ = cz + d * 0.5f;
            ApplySlab();
        }

        /// <summary>Agrandit le socle pour qu'il couvre aussi ce rectangle (marge comprise).</summary>
        public void Grow(float x0, float x1, float z0, float z1, float margin)
        {
            x0 -= margin; x1 += margin; z0 -= margin; z1 += margin;
            if (!hasBounds) { hasBounds = true; minX = x0; maxX = x1; minZ = z0; maxZ = z1; }
            else { minX = Mathf.Min(minX, x0); maxX = Mathf.Max(maxX, x1); minZ = Mathf.Min(minZ, z0); maxZ = Mathf.Max(maxZ, z1); }
            ApplySlab();
        }

        void ApplySlab()
        {
            slab.gameObject.SetActive(true);
            slab.localScale = new Vector3(maxX - minX, 0.014f, maxZ - minZ);
            slab.localPosition = new Vector3((minX + maxX) * 0.5f, -0.007f, (minZ + maxZ) * 0.5f);
        }

        public void SlabGlow(float g) { Paint(slabRend, slabCol, g); }

        // ---------------------------------------------------------------- contenu
        public Ar6dofBoard3D AddBoard(int n, float cell, float cx, float cz, Action<int> click)
        {
            var b = new Ar6dofBoard3D(this, n, cell, cx, cz, click);
            Grow(cx - n * cell * 0.5f, cx + n * cell * 0.5f, cz - n * cell * 0.5f, cz + n * cell * 0.5f, 0.03f);
            return b;
        }

        public Ar6dofLabel3D AddLabel(Transform parent, Vector3 pos, float width, int fs, Color col, bool billboard)
        {
            var l = Ar6dofLabel3D.Make(parent != null ? parent : content, pos, width, fs, col);
            if (billboard) labels.Add(l);
            return l;
        }

        public void AddTick(Action<float> a) { ticks.Add(a); }

        public void PopIn(Transform tr, Vector3 scale)
        {
            tr.localScale = Vector3.zero;
            pops.Add(new Pop { tr = tr, start = Time.unscaledTime, scale = scale });
        }

        /// <summary>Vide le plateau / les des / la machine (la table et sa position restent).</summary>
        public void ClearContent()
        {
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            ticks.Clear(); labels.Clear(); pops.Clear();
            hasBounds = false;
            slab.gameObject.SetActive(false);
        }

        public void Tick()
        {
            float now = Time.unscaledTime;
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i];
                if (p.tr == null) { pops.RemoveAt(i); continue; }
                float u = (now - p.start) / 0.2f;
                if (u >= 1f) { p.tr.localScale = p.scale; pops.RemoveAt(i); continue; }
                float e = (1f - Mathf.Pow(1f - Mathf.Max(0f, u), 3f)) * (1f + 0.14f * Mathf.Sin(Mathf.Max(0f, u) * Mathf.PI));
                p.tr.localScale = p.scale * e;
            }
            for (int i = 0; i < ticks.Count; i++) ticks[i](now);
            if (labels.Count > 0)
            {
                Transform h = HeadT();
                if (h != null)
                    for (int i = 0; i < labels.Count; i++)
                    {
                        var l = labels[i];
                        if (l == null || l.tr == null || !l.tr.gameObject.activeSelf) continue;
                        Vector3 d = l.tr.position - h.position;
                        if (d.sqrMagnitude > 1e-6f) l.tr.rotation = Quaternion.LookRotation(d);
                    }
            }
        }

        public void Dispose()
        {
            live.Remove(this);
            if (root != null) UnityEngine.Object.Destroy(root);
            for (int i = 0; i < owned.Count; i++) if (owned[i] != null) UnityEngine.Object.Destroy(owned[i]);
            owned.Clear(); ticks.Clear(); labels.Clear(); pops.Clear();
        }
    }

    /// <summary>Le socle de la table : se deplace au laser.</summary>
    public sealed class Ar6dofTableHandle : MonoBehaviour, IAr6dofPointerTarget, IAr6dofPointerDrag
    {
        public Ar6dofTable3D table;
        bool dragging, hover;
        float dist;
        Vector3 offset;

        public void OnPointerEnter() { hover = true; if (table != null) table.SlabGlow(0.1f); }
        public void OnPointerExit() { hover = false; if (table != null && !dragging) table.SlabGlow(0f); }
        public void OnPointerHover(Vector3 worldPoint) { }

        public void OnPointerDown(Vector3 worldPoint, int button)
        {
            dragging = true;
            if (table != null) table.SlabGlow(0.22f);
            dist = Vector3.Distance(Ar6dofPointer.LastRay.origin, worldPoint);
            offset = table.root.transform.position - worldPoint;
        }

        public void OnPointerUp(Vector3 worldPoint, int button)
        {
            if (!dragging) return;
            dragging = false;
            if (table != null) table.SlabGlow(hover ? 0.1f : 0f);
        }

        public void OnDrag(Ray ray)
        {
            if (!dragging || table == null) return;
            table.root.transform.position = ray.GetPoint(dist) + offset;
        }
    }

    /// <summary>Un texte flottant (toile Unity UI sans lanceur de rayons : le laser le traverse).</summary>
    public sealed class Ar6dofLabel3D
    {
        public Transform tr;
        Text text;

        public static Ar6dofLabel3D Make(Transform parent, Vector3 pos, float width, int fs, Color col)
        {
            var l = new Ar6dofLabel3D();
            var go = new GameObject("Texte", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(240f, 100f);
            rt.localScale = Vector3.one * (width / 240f);
            var tg = new GameObject("T", typeof(RectTransform));
            tg.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tg.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tg.AddComponent<Text>();
            t.font = Ar6dofVrUi.GetFont();
            t.fontSize = fs;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.color = col;
            var ol = tg.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.85f);
            ol.effectDistance = new Vector2(3f, -3f);
            l.tr = go.transform;
            l.text = t;
            return l;
        }

        public void Set(string s, Color c)
        {
            if (text == null) return;
            if (text.text != s) text.text = s;
            text.color = c;
        }

        public void Show(bool on) { if (tr != null && tr.gameObject.activeSelf != on) tr.gameObject.SetActive(on); }
    }

    /// <summary>Une case du plateau 3D : surbrillance au survol, clic au relachement si le laser est toujours sur la case.</summary>
    public sealed class Ar6dofCellTarget : MonoBehaviour, IAr6dofPointerTarget
    {
        public Ar6dofBoard3D board;
        public int idx;
        bool down, hover;

        public void OnPointerEnter() { hover = true; board.SetHover(idx, true); }
        public void OnPointerExit() { hover = false; board.SetHover(idx, false); }
        public void OnPointerHover(Vector3 worldPoint) { }
        public void OnPointerDown(Vector3 worldPoint, int button) { if (button == 0) down = true; }
        public void OnPointerUp(Vector3 worldPoint, int button)
        {
            bool fire = button == 0 && down && hover;
            down = false;
            if (fire) board.Click(idx);
        }
    }

    /// <summary>Un objet cliquable (un de, le levier) : appelle 'action' quand on relache le bouton dessus.</summary>
    public sealed class Ar6dofTapTarget : MonoBehaviour, IAr6dofPointerTarget
    {
        public Action action;
        public Renderer glowRend;
        public Ar6dofTable3D table;
        public Color baseColor = Color.white;
        bool down, hover;

        void Glow(float g) { if (table != null && glowRend != null) table.Paint(glowRend, baseColor, g); }

        public void OnPointerEnter() { hover = true; Glow(0.15f); }
        public void OnPointerExit() { hover = false; Glow(0f); }
        public void OnPointerHover(Vector3 worldPoint) { }
        public void OnPointerDown(Vector3 worldPoint, int button) { if (button == 0) down = true; }
        public void OnPointerUp(Vector3 worldPoint, int button)
        {
            bool fire = button == 0 && down && hover;
            down = false;
            if (fire && action != null) action();
        }
    }
}
