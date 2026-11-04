using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Un objet pose dans le monde : modele 3D (.obj), sticker (image) ou forme simple.</summary>
    [Serializable]
    public class WorldObj
    {
        public int id;
        public string name = "";
        public string file = "";          // chemin du .obj / de l'image, ou nom de la forme (cube, sphere, cylindre, capsule)
        public int kind;                  // 0 = modele 3D, 1 = sticker, 2 = forme
        public float px, py, pz;          // position dans le monde Unity (repere du suivi 6DoF de cette session)
        public float yaw, pitch, roll;    // degres
        public float size = 0.3f;         // metres (plus grande dimension du modele, largeur du sticker)
        public float alpha = 1f;
        public float r = 1f, g = 1f, b = 1f;   // teinte
        public bool billboard;            // toujours tourne vers toi
        public bool lit = true;           // eclairage simple
        public bool visible = true;
    }

    [Serializable]
    public class WorldObjData
    {
        public List<WorldObj> items = new List<WorldObj>();
        public int nextId = 1;
    }

    /// <summary>Modele lu dans un fichier .obj : un seul maillage (un sous-maillage par materiau), recentre et ramene a une taille de 1.</summary>
    public class ObjModel
    {
        public Mesh mesh;
        public Color[] kd = new Color[0];
        public string[] map = new string[0];
        public string error;
    }

    /// <summary>Lecteur de fichiers Wavefront .obj (+ .mtl : couleur Kd et texture map_Kd). Ecrit pour AR6DoF, sans bibliotheque externe.</summary>
    public static class Ar6dofObjLoader
    {
        const long MaxBytes = 300L * 1024L * 1024L;
        const int MaxIndex = (1 << 21) - 2;
        static readonly char[] Sep = { ' ', '\t' };
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        class Mtl { public Color kd = Color.white; public string map = ""; }

        static float F(string s) { float v; return float.TryParse(s, NumberStyles.Float, Ci, out v) ? v : 0f; }

        static ObjModel Err(string m) { return new ObjModel { error = m }; }

        public static ObjModel Load(string path)
        {
            try { return LoadInner(path); }
            catch (Exception e) { return Err(e.Message); }
        }

        static bool Conv(string s, int count, out int r)
        {
            int v;
            r = -1;
            if (!int.TryParse(s, NumberStyles.Integer, Ci, out v) || v == 0) return false;
            r = v > 0 ? v - 1 : count + v;
            return r >= 0 && r < count && r < MaxIndex;
        }

        static bool ParseIdx(string s, int nv, int nt, int nn, out int vi, out int ti, out int ni)
        {
            vi = -1; ti = -1; ni = -1;
            int a = s.IndexOf('/');
            if (a < 0) return Conv(s, nv, out vi);
            if (!Conv(s.Substring(0, a), nv, out vi)) return false;
            int b = s.IndexOf('/', a + 1);
            string st = b < 0 ? s.Substring(a + 1) : s.Substring(a + 1, b - a - 1);
            if (st.Length > 0 && !Conv(st, nt, out ti)) return false;
            if (b >= 0)
            {
                string sn = s.Substring(b + 1);
                if (sn.Length > 0 && !Conv(sn, nn, out ni)) return false;
            }
            return true;
        }

        static void ReadMtl(string file, Dictionary<string, Mtl> into)
        {
            if (!File.Exists(file)) return;
            string dir = Path.GetDirectoryName(file);
            Mtl cur = null;
            foreach (string raw in File.ReadLines(file))
            {
                string line = raw.Trim();
                if (line.Length < 3 || line[0] == '#') continue;
                string[] t = line.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 2) continue;
                if (t[0] == "newmtl") { cur = new Mtl(); into[line.Substring(6).Trim()] = cur; }
                else if (cur == null) continue;
                else if (t[0] == "Kd" && t.Length >= 4) cur.kd = new Color(F(t[1]), F(t[2]), F(t[3]), 1f);
                else if (t[0] == "map_Kd")
                {
                    string rest = line.Substring(6).Trim();
                    string fn = rest.StartsWith("-") ? t[t.Length - 1] : rest;   // avec options (-s -o...) : le nom est le dernier mot
                    try { cur.map = Path.Combine(dir, fn); } catch (Exception) { cur.map = ""; }
                }
            }
        }

        static ObjModel LoadInner(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return Err("Fichier introuvable");
            if (fi.Length > MaxBytes) return Err("Fichier trop gros (300 Mo maximum)");
            string dir = fi.DirectoryName;

            var P = new List<Vector3>();
            var T = new List<Vector2>();
            var N = new List<Vector3>();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var nors = new List<Vector3>();
            var cache = new Dictionary<ulong, int>();
            var groups = new List<List<int>>();
            var gnames = new List<string>();
            var mtls = new Dictionary<string, Mtl>();
            var poly = new List<int>();
            int cur = -1;
            bool anyUv = false, missingN = false;

            foreach (string raw in File.ReadLines(path))
            {
                if (raw.Length < 2) continue;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] t = line.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
                switch (t[0])
                {
                    case "v": if (t.Length >= 4) P.Add(new Vector3(-F(t[1]), F(t[2]), F(t[3]))); break;   // main droite -> main gauche : x inverse
                    case "vt": if (t.Length >= 3) T.Add(new Vector2(F(t[1]), F(t[2]))); break;
                    case "vn": if (t.Length >= 4) N.Add(new Vector3(-F(t[1]), F(t[2]), F(t[3]))); break;
                    case "mtllib":
                        if (t.Length >= 2) { try { ReadMtl(Path.Combine(dir, line.Substring(6).Trim()), mtls); } catch (Exception) { } }
                        break;
                    case "usemtl":
                    {
                        string nm = line.Length > 7 ? line.Substring(6).Trim() : "";
                        cur = gnames.IndexOf(nm);
                        if (cur < 0) { gnames.Add(nm); groups.Add(new List<int>()); cur = gnames.Count - 1; }
                        break;
                    }
                    case "f":
                    {
                        poly.Clear();
                        for (int k = 1; k < t.Length; k++)
                        {
                            int vi, ti, ni;
                            if (!ParseIdx(t[k], P.Count, T.Count, N.Count, out vi, out ti, out ni)) { poly.Clear(); break; }
                            ulong key = (ulong)(uint)(vi + 1) | ((ulong)(uint)(ti + 1) << 21) | ((ulong)(uint)(ni + 1) << 42);
                            int mi;
                            if (!cache.TryGetValue(key, out mi))
                            {
                                mi = verts.Count;
                                cache[key] = mi;
                                verts.Add(P[vi]);
                                if (ti >= 0) { uvs.Add(T[ti]); anyUv = true; } else uvs.Add(Vector2.zero);
                                if (ni >= 0) nors.Add(N[ni]); else { nors.Add(Vector3.zero); missingN = true; }
                            }
                            poly.Add(mi);
                        }
                        if (poly.Count >= 3)
                        {
                            if (cur < 0) { gnames.Add(""); groups.Add(new List<int>()); cur = 0; }
                            var g = groups[cur];
                            for (int k = 1; k < poly.Count - 1; k++) { g.Add(poly[0]); g.Add(poly[k + 1]); g.Add(poly[k]); }   // x inverse : sens des triangles inverse
                        }
                        break;
                    }
                }
            }
            if (verts.Count == 0) return Err("Aucune face dans ce fichier");

            // recentrage et mise a l'echelle : la plus grande dimension vaut 1
            Vector3 mn = verts[0], mx = verts[0];
            for (int i = 1; i < verts.Count; i++) { mn = Vector3.Min(mn, verts[i]); mx = Vector3.Max(mx, verts[i]); }
            Vector3 c = (mn + mx) * 0.5f;
            Vector3 ext = mx - mn;
            float big = Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z));
            float k1 = big > 1e-9f ? 1f / big : 1f;
            for (int i = 0; i < verts.Count; i++) verts[i] = (verts[i] - c) * k1;

            var mesh = new Mesh();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            if (anyUv) mesh.SetUVs(0, uvs);
            int nonEmpty = 0;
            for (int i = 0; i < groups.Count; i++) if (groups[i].Count > 0) nonEmpty++;
            mesh.subMeshCount = Mathf.Max(1, nonEmpty);
            var kd = new List<Color>();
            var map = new List<string>();
            int sm = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Count == 0) continue;
                mesh.SetTriangles(groups[i], sm++);
                Mtl m;
                if (mtls.TryGetValue(gnames[i], out m)) { kd.Add(m.kd); map.Add(m.map); }
                else { kd.Add(Color.white); map.Add(""); }
            }
            if (missingN) mesh.RecalculateNormals(); else { mesh.SetNormals(nors); }
            mesh.RecalculateBounds();
            return new ObjModel { mesh = mesh, kd = kd.ToArray(), map = map.ToArray() };
        }
    }

    /// <summary>Tourne l'objet vers la tete (option « Toujours face a moi »). N'existe que sur les objets qui le demandent.</summary>
    public class Ar6dofBillboard : MonoBehaviour
    {
        public float roll;
        static Transform head;

        void LateUpdate()
        {
            if (head == null)
            {
                var d = Ar6dofDock.Instance;
                head = d != null && d.passthrough != null ? d.passthrough.HeadView : null;
                if (head == null && Camera.main != null) head = Camera.main.transform;
                if (head == null) return;
            }
            Vector3 dir = transform.position - head.position;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
        }
    }

    /// <summary>Poignee d'un objet : avec « Deplacer au laser », on le saisit avec le laser et il suit (a la meme distance) tant que le bouton est maintenu.</summary>
    public class Ar6dofWorldHandle : MonoBehaviour, IAr6dofPointerTarget, IAr6dofPointerDrag
    {
        public Ar6dofWorldObjects owner;
        public WorldObj d;
        public Renderer rend;
        bool hover, dragging;
        float dist;
        Vector3 offset;
        MaterialPropertyBlock mpb;

        void Glow(float g)
        {
            if (rend == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetFloat("_Glow", g);
            rend.SetPropertyBlock(mpb);
        }

        public void OnPointerEnter() { hover = true; Glow(0.2f); }
        public void OnPointerExit() { hover = false; if (!dragging) Glow(0f); }
        public void OnPointerHover(Vector3 worldPoint) { }

        public void OnPointerDown(Vector3 worldPoint, int button)
        {
            dragging = true;
            Glow(0.35f);
            dist = Vector3.Distance(Ar6dofPointer.LastRay.origin, worldPoint);
            offset = transform.position - worldPoint;
        }

        public void OnPointerUp(Vector3 worldPoint, int button)
        {
            if (!dragging) return;
            dragging = false;
            Glow(hover ? 0.2f : 0f);
            owner.MarkDirty();
        }

        public void OnDrag(Ray ray)
        {
            if (!dragging || d == null) return;
            Vector3 p = ray.GetPoint(dist) + offset;
            transform.position = p;
            d.px = p.x; d.py = p.y; d.pz = p.z;
            owner.MarkDirty();
        }
    }

    /// <summary>
    /// Les objets 3D et stickers poses dans le monde : creation, chargement des fichiers, sauvegarde (objects.json dans %APPDATA%\AR6DoF).
    /// Un objet est cree seulement s'il existe : sans objet, rien ne tourne. Les positions sont celles du monde Unity, c'est-a-dire le repere du suivi 6DoF
    /// de la session en cours (il repart du point ou tu as lance le suivi).
    /// </summary>
    public class Ar6dofWorldObjects
    {
        public const int MaxObjects = 200;
        public static readonly string[] ShapeNames = { "cube", "sphere", "cylindre", "capsule" };
        public static readonly string[] KindNames = { "Modele 3D", "Sticker", "Forme" };

        public class Inst
        {
            public WorldObj d;
            public GameObject go;
            public MeshFilter mf;
            public MeshRenderer mr;
            public BoxCollider col;
            public Ar6dofWorldHandle handle;
            public Ar6dofBillboard bill;
            public Material[] mats = new Material[0];
            public Color[] baseCol = new Color[0];
            public float aspect = 1f;       // hauteur / largeur du sticker
            public float unit = 1f;         // facteur d'echelle du maillage de base
            public string error = "";
        }

        public readonly WorldObjData data = new WorldObjData();
        public readonly Dictionary<int, Inst> inst = new Dictionary<int, Inst>();
        public bool grab { get; private set; }
        public bool IsDirty { get { return dirty; } }

        GameObject root;
        bool dirty;
        float saveAt;

        static Shader shader;
        static Mesh quad;
        static readonly Dictionary<string, Mesh> shapes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, ObjModel> models = new Dictionary<string, ObjModel>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        public static string FilePath { get { return Path.Combine(Ar6dofSettings.FolderPath, "objects.json"); } }

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

        // ------------------------------------------------------------------ fichiers
        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonUtility.FromJson<WorldObjData>(File.ReadAllText(FilePath));
                    if (d != null && d.items != null) { data.items.Clear(); data.items.AddRange(d.items); data.nextId = Mathf.Max(1, d.nextId); }
                }
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : objects.json illisible : " + e.Message); }
            foreach (var o in data.items) { if (o.id >= data.nextId) data.nextId = o.id + 1; Create(o); }
        }

        public void MarkDirty() { dirty = true; saveAt = Time.unscaledTime + 0.6f; }

        public void Tick() { if (dirty && Time.unscaledTime > saveAt) Save(); }

        public void Save()
        {
            dirty = false;
            try
            {
                Directory.CreateDirectory(Ar6dofSettings.FolderPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : objects.json non enregistre : " + e.Message); }
        }

        // ------------------------------------------------------------------ ressources
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "AR6DoF_Quad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateBounds();
            return quad;
        }

        static Mesh Shape(string name, out float unit)
        {
            PrimitiveType pt = PrimitiveType.Cube;
            unit = 1f;
            switch (name)
            {
                case "sphere": pt = PrimitiveType.Sphere; break;
                case "cylindre": pt = PrimitiveType.Cylinder; unit = 0.5f; break;   // ces deux-la font 2 de haut
                case "capsule": pt = PrimitiveType.Capsule; unit = 0.5f; break;
                default: name = "cube"; break;
            }
            Mesh m;
            if (shapes.TryGetValue(name, out m) && m != null) return m;
            var tmp = GameObject.CreatePrimitive(pt);
            m = tmp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(tmp);
            shapes[name] = m;
            return m;
        }

        static Texture2D Tex(string path, bool clamp)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = path + (clamp ? "|c" : "|r");
            Texture2D t;
            if (textures.TryGetValue(key, out t) && t != null) return t;
            try
            {
                if (!File.Exists(path)) return null;
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!t.LoadImage(File.ReadAllBytes(path), true)) { UnityEngine.Object.Destroy(t); return null; }
                t.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                t.filterMode = FilterMode.Bilinear;
                t.anisoLevel = 4;
                textures[key] = t;
                return t;
            }
            catch (Exception) { return null; }
        }

        static ObjModel Model(string path)
        {
            ObjModel m;
            if (models.TryGetValue(path, out m) && (m.mesh != null || m.error != null)) return m;
            m = Ar6dofObjLoader.Load(path);
            if (m.mesh != null) models[path] = m;
            return m;
        }

        /// <summary>Oublie les fichiers en memoire (apres « Remplacer le fichier » ou pour relire un fichier modifie).</summary>
        public static void ForgetFile(string path)
        {
            ObjModel m;
            if (path != null && models.TryGetValue(path, out m)) { if (m.mesh != null) UnityEngine.Object.Destroy(m.mesh); models.Remove(path); }
            foreach (string k in new List<string>(textures.Keys))
                if (path != null && k.StartsWith(path + "|")) { if (textures[k] != null) UnityEngine.Object.Destroy(textures[k]); textures.Remove(k); }
        }

        // ------------------------------------------------------------------ objets
        public WorldObj Add(int kind, string file, Vector3 pos, float yawDeg)
        {
            if (data.items.Count >= MaxObjects) return null;
            var o = new WorldObj();
            o.id = data.nextId++;
            o.kind = kind;
            o.file = file ?? "";
            o.name = kind == 2 && !string.IsNullOrEmpty(file) ? char.ToUpper(file[0]) + file.Substring(1) : Path.GetFileNameWithoutExtension(file ?? "");
            if (string.IsNullOrEmpty(o.name)) o.name = Ar6dofLoc.T(KindNames[Mathf.Clamp(kind, 0, 2)]);
            o.px = pos.x; o.py = pos.y; o.pz = pos.z;
            o.yaw = yawDeg;
            o.size = kind == 1 ? 0.4f : 0.3f;
            if (kind == 2) { o.r = 0.25f; o.g = 0.6f; o.b = 0.95f; }
            data.items.Add(o);
            Create(o);
            MarkDirty();
            return o;
        }

        public WorldObj Duplicate(WorldObj s)
        {
            if (data.items.Count >= MaxObjects) return null;
            var o = JsonUtility.FromJson<WorldObj>(JsonUtility.ToJson(s));
            o.id = data.nextId++;
            o.px += 0.15f; o.pz += 0.0f;
            data.items.Add(o);
            Create(o);
            MarkDirty();
            return o;
        }

        public void Remove(WorldObj o)
        {
            Inst it;
            if (inst.TryGetValue(o.id, out it)) { Release(it); inst.Remove(o.id); }
            data.items.Remove(o);
            MarkDirty();
        }

        public void RemoveAll()
        {
            foreach (var it in inst.Values) Release(it);
            inst.Clear();
            data.items.Clear();
            MarkDirty();
        }

        public Inst Get(WorldObj o) { Inst it; inst.TryGetValue(o.id, out it); return it; }

        void Release(Inst it)
        {
            if (it.mats != null) foreach (var m in it.mats) if (m != null) UnityEngine.Object.Destroy(m);
            if (it.go != null) UnityEngine.Object.Destroy(it.go);
        }

        public void Dispose()
        {
            if (dirty) Save();
            foreach (var it in inst.Values) Release(it);
            inst.Clear();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }

        void Create(WorldObj o)
        {
            if (root == null) { root = new GameObject("AR6DoF_Objets"); }
            var it = new Inst { d = o };
            it.go = new GameObject("Objet " + o.id);
            it.go.transform.SetParent(root.transform, false);
            it.mf = it.go.AddComponent<MeshFilter>();
            it.mr = it.go.AddComponent<MeshRenderer>();
            it.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            it.mr.receiveShadows = false;
            it.col = it.go.AddComponent<BoxCollider>();
            it.col.enabled = grab;
            it.handle = it.go.AddComponent<Ar6dofWorldHandle>();
            it.handle.owner = this; it.handle.d = o; it.handle.rend = it.mr;
            inst[o.id] = it;
            Reload(o);
        }

        /// <summary>Relit le fichier d'un objet (apres un changement de fichier ou de forme) et applique tous ses reglages.</summary>
        public void Reload(WorldObj o)
        {
            Inst it = Get(o);
            if (it == null) return;
            if (it.mats != null) foreach (var m in it.mats) if (m != null) UnityEngine.Object.Destroy(m);
            it.error = "";
            it.aspect = 1f;
            it.unit = 1f;
            Mesh mesh = null;
            var mats = new List<Material>();
            var cols = new List<Color>();
            if (o.kind == 0)
            {
                var m = Model(o.file);
                if (m.mesh == null) { it.error = m.error ?? "Modele illisible"; mesh = Shape("cube", out it.unit); mats.Add(NewMat(null, Color.magenta)); cols.Add(Color.magenta); }
                else
                {
                    mesh = m.mesh;
                    int n = Mathf.Max(1, mesh.subMeshCount);
                    for (int i = 0; i < n; i++)
                    {
                        Color kd = i < m.kd.Length ? m.kd[i] : Color.white;
                        Texture2D t = i < m.map.Length ? Tex(m.map[i], false) : null;
                        mats.Add(NewMat(t, kd)); cols.Add(kd);
                    }
                }
            }
            else if (o.kind == 1)
            {
                mesh = Quad();
                var t = Tex(o.file, true);
                if (t == null) { it.error = "Image illisible"; mats.Add(NewMat(null, Color.magenta)); cols.Add(Color.magenta); }
                else { it.aspect = (float)t.height / Mathf.Max(1, t.width); mats.Add(NewMat(t, Color.white)); cols.Add(Color.white); }
            }
            else
            {
                mesh = Shape(o.file, out it.unit);
                mats.Add(NewMat(null, Color.white)); cols.Add(Color.white);
            }
            it.mf.sharedMesh = mesh;
            it.mats = mats.ToArray();
            it.baseCol = cols.ToArray();
            it.mr.sharedMaterials = it.mats;
            Apply(o);
        }

        static Material NewMat(Texture2D t, Color c)
        {
            var m = new Material(Sh());
            if (t != null) m.mainTexture = t;
            m.SetColor("_Color", c);
            return m;
        }

        /// <summary>Applique la position, la rotation, la taille, la teinte... d'un objet.</summary>
        public void Apply(WorldObj o)
        {
            Inst it = Get(o);
            if (it == null || it.go == null) return;
            var tr = it.go.transform;
            tr.position = new Vector3(o.px, o.py, o.pz);
            if (!o.billboard) tr.rotation = Quaternion.Euler(o.pitch, o.yaw, o.roll);
            float s = Mathf.Max(0.005f, o.size);
            tr.localScale = o.kind == 1 ? new Vector3(s, s * it.aspect, 1f) : Vector3.one * (s * it.unit);
            it.go.SetActive(o.visible);

            bool transparent = o.kind == 1 || o.alpha < 0.99f;
            for (int i = 0; i < it.mats.Length; i++)
            {
                var m = it.mats[i];
                if (m == null) continue;
                Color b = i < it.baseCol.Length ? it.baseCol[i] : Color.white;
                if (it.error.Length == 0) b = new Color(b.r * o.r, b.g * o.g, b.b * o.b, o.alpha);
                m.SetColor("_Color", b);
                m.SetFloat("_Shade", o.lit && o.kind != 1 ? 1f : 0f);
                m.SetFloat("_SrcBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : 1f);
                m.SetFloat("_DstBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : 0f);
                m.SetFloat("_ZWrite", transparent ? 0f : 1f);
                m.renderQueue = transparent ? 3000 : 2000;
            }

            // zone cliquable : le maillage, avec une epaisseur minimale (sticker plat)
            if (it.mf.sharedMesh != null)
            {
                Bounds bd = it.mf.sharedMesh.bounds;
                Vector3 sz = bd.size;
                float thick = 0.04f / Mathf.Max(0.01f, tr.localScale.z);
                it.col.center = bd.center;
                it.col.size = new Vector3(Mathf.Max(sz.x, 0.02f), Mathf.Max(sz.y, 0.02f), Mathf.Max(sz.z, thick));
            }

            if (o.billboard)
            {
                if (it.bill == null) it.bill = it.go.AddComponent<Ar6dofBillboard>();
                it.bill.roll = o.roll;
            }
            else if (it.bill != null) { UnityEngine.Object.Destroy(it.bill); it.bill = null; tr.rotation = Quaternion.Euler(o.pitch, o.yaw, o.roll); }
        }

        /// <summary>Active ou coupe les poignees : quand c'est coupe, aucun objet ne reagit au laser.</summary>
        public void SetGrab(bool on)
        {
            grab = on;
            foreach (var it in inst.Values) if (it.col != null) it.col.enabled = on;
        }
    }
}
