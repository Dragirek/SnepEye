using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Reglages (sauvegardes) et liste des ecrans poses. Rien n'existe tant qu'aucun ecran n'est pose : aucun objet, aucun calcul.</summary>
    public static class Ar6dofDvdWorld
    {
        public const int Max = 4;
        public static readonly List<Ar6dofDvdScreen> Screens = new List<Ar6dofDvdScreen>();
        static Ar6dofGameSound snd;
        static Sprite custom;
        static string customPath = "", customStamp = "";

        static float GetF(string k, float d, float lo, float hi) { try { return Mathf.Clamp(PlayerPrefs.GetFloat(k, d), lo, hi); } catch (Exception) { return d; } }
        static void SetF(string k, float v) { try { PlayerPrefs.SetFloat(k, v); } catch (Exception) { } }
        static int GetI(string k, int d) { try { return PlayerPrefs.GetInt(k, d); } catch (Exception) { return d; } }
        static void SetI(string k, int v) { try { PlayerPrefs.SetInt(k, v); } catch (Exception) { } }

        /// <summary>Vitesse du logo, en largeurs d'ecran par seconde.</summary>
        public static float Speed { get { return GetF("ar6dof_dvd_speed", 0.18f, 0.04f, 0.6f); } set { SetF("ar6dof_dvd_speed", value); } }
        /// <summary>Largeur du logo, en fraction de la largeur de l'ecran.</summary>
        public static float LogoSize { get { return GetF("ar6dof_dvd_size", 0.14f, 0.05f, 0.35f); } set { SetF("ar6dof_dvd_size", value); } }
        public static float ScreenWidth { get { return GetF("ar6dof_dvd_width", 1.2f, 0.4f, 2.6f); } set { SetF("ar6dof_dvd_width", value); } }
        public static bool ColorChange { get { return GetI("ar6dof_dvd_color", 1) != 0; } set { SetI("ar6dof_dvd_color", value ? 1 : 0); } }
        public static bool BounceSound { get { return GetI("ar6dof_dvd_bounce_snd", 0) != 0; } set { SetI("ar6dof_dvd_bounce_snd", value ? 1 : 0); } }
        /// <summary>0 le mot « DVD », 1 un disque, 2 ton image.</summary>
        public static int LogoType { get { return Mathf.Clamp(GetI("ar6dof_dvd_logo", 0), 0, 2); } set { SetI("ar6dof_dvd_logo", value); } }
        public static string LogoPath
        {
            get { try { return PlayerPrefs.GetString("ar6dof_dvd_logo_path", ""); } catch (Exception) { return ""; } }
            set { try { PlayerPrefs.SetString("ar6dof_dvd_logo_path", value ?? ""); } catch (Exception) { } }
        }

        public static void Prune() { Screens.RemoveAll(s => s == null); }

        public static Ar6dofDvdScreen Add(int type, float width)
        {
            Prune();
            if (Screens.Count >= Max) return null;
            Transform h = HeadT();
            Vector3 head = h != null ? h.position : new Vector3(0f, 1.5f, 0f);
            Vector3 f = h != null ? h.forward : Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, f);
            float dist = Mathf.Max(1.3f, width * 1.1f);
            Vector3 pos = head + f * dist + right * (Screens.Count * 0.25f) + Vector3.up * -0.05f;
            var s = Ar6dofDvdScreen.Create(type, width, pos, Quaternion.LookRotation(f, Vector3.up));
            Screens.Add(s);
            return s;
        }

        public static void Remove(Ar6dofDvdScreen s)
        {
            if (s != null) UnityEngine.Object.Destroy(s.gameObject);
            Screens.Remove(s);
            Prune();
            if (Screens.Count == 0) DisposeSound();
        }

        public static void Clear()
        {
            for (int i = 0; i < Screens.Count; i++) if (Screens[i] != null) UnityEngine.Object.Destroy(Screens[i].gameObject);
            Screens.Clear();
            DisposeSound();
            if (custom != null) { if (custom.texture != null) UnityEngine.Object.Destroy(custom.texture); UnityEngine.Object.Destroy(custom); custom = null; customPath = ""; }
        }

        static void DisposeSound() { if (snd != null) { snd.Dispose(); snd = null; } }

        public static void Play(Ar6dofGameSound.Id id, float vol)
        {
            if (snd == null) snd = new Ar6dofGameSound();
            snd.on = Ar6dofGameWindow.SoundOn; snd.volume = Ar6dofGameWindow.SoundVolume;
            snd.Play(id, vol);
        }

        public static Transform HeadT()
        {
            var d = Ar6dofDock.Instance;
            Transform h = d != null ? d.HeadTransform : null;
            if (h == null && Camera.main != null) h = Camera.main.transform;
            return h;
        }

        /// <summary>Ton image de logo (png / jpg), chargee une fois (et rechargee si le fichier change) ; null si elle n'existe pas ou est illisible.</summary>
        public static Sprite CustomSprite()
        {
            string path = LogoPath;
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > 8 * 1024 * 1024) return null;
                string stamp = fi.LastWriteTimeUtc.Ticks.ToString();
                if (custom != null && path == customPath && stamp == customStamp) return custom;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.Destroy(tex); return null; }
                if (custom != null) { if (custom.texture != null) UnityEngine.Object.Destroy(custom.texture); UnityEngine.Object.Destroy(custom); }
                tex.wrapMode = TextureWrapMode.Clamp;
                custom = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                customPath = path; customStamp = stamp;
                return custom;
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// Un ecran statique pose dans le monde (plat, ultra-large, incurve, ancien a bords bombes, portrait) sur lequel un logo rebondit. On le deplace au laser (bouton appuye).
    /// Le logo est un texte « DVD » ecrit par le code, un disque dessine par le code, ou ton image : pas de logo de marque reproduit.
    /// </summary>
    public sealed class Ar6dofDvdScreen : MonoBehaviour, IAr6dofPointerTarget, IAr6dofPointerDrag
    {
        public static readonly string[] TypeNames = { "Plat 16:9", "Ultra-large 21:9", "Incurve 21:9", "Ancien 4:3", "Portrait 9:16" };
        static readonly float[] Aspect = { 16f / 9f, 21f / 9f, 21f / 9f, 4f / 3f, 9f / 16f };
        const float Back = 0.006f;

        public int type;
        public float width = 1.2f;
        public int bounces, corners;

        Material screenMat, bezelMat;
        MeshFilter screenMf, bezelMf;
        BoxCollider box;
        RectTransform logoRt;
        Text logoText;
        Image logoImg;
        Ar6dofLabel3D cornerLabel;
        float cornerUntil, flash, lastFlash = -1f, hue, logoAspect = 0.55f, lastScale = -1f;
        Vector2 pos, dir;
        bool hover, dragging;
        float dist;
        Vector3 offset;
        int shownLogo = -1;
        string shownPath = "";
        Color logoColor = Color.white;
        static Sprite discSprite;

        public float Height { get { return width / Aspect[type]; } }

        public static Ar6dofDvdScreen Create(int type, float width, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("AR6DoF_EcranDVD");
            go.transform.SetPositionAndRotation(position, rotation);
            var s = go.AddComponent<Ar6dofDvdScreen>();
            s.Init(type, width);
            return s;
        }

        // ---------------------------------------------------------------- forme de la surface
        // (u, v) entre -0,5 et 0,5 : position sur l'ecran, en repere de l'ecran (x droite, y haut, z derriere : la face visible regarde vers -z)
        Vector3 P(float u, float v)
        {
            float W = width, H = W / Aspect[type], x = u * W, y = v * H;
            switch (type)
            {
                case 2: { float rc = W * 1.1f, th = x / rc; return new Vector3(rc * Mathf.Sin(th), y, -rc * (1f - Mathf.Cos(th))); }
                case 3: { float b = 0.05f * W; return new Vector3(x, y, -b * (1f - 4f * u * u) * (1f - 4f * v * v)); }
                default: return new Vector3(x, y, 0f);
            }
        }

        Vector3 Normal(float u, float v)
        {
            const float e = 0.002f;
            Vector3 du = P(u + e, v) - P(u - e, v), dv = P(u, v + e) - P(u, v - e);
            Vector3 n = Vector3.Cross(dv, du);
            return n.sqrMagnitude < 1e-12f ? Vector3.back : n.normalized;
        }

        Mesh Grid(float scale, float back, string name)
        {
            const int nu = 32, nv = 16;
            var verts = new Vector3[(nu + 1) * (nv + 1)];
            var norms = new Vector3[verts.Length];
            var tris = new int[nu * nv * 6];
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float u = ((float)i / nu - 0.5f) * scale, v = ((float)j / nv - 0.5f) * scale;
                    Vector3 n = Normal(u, v);
                    int k = j * (nu + 1) + i;
                    verts[k] = P(u, v) - n * back;
                    norms[k] = n;
                }
            int t = 0;
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var m = new Mesh { name = name };
            m.vertices = verts; m.normals = norms; m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        void BuildGeometry()
        {
            if (screenMf.sharedMesh != null) Destroy(screenMf.sharedMesh);
            if (bezelMf.sharedMesh != null) Destroy(bezelMf.sharedMesh);
            screenMf.sharedMesh = Grid(1f, 0f, "AR6DoF_Ecran");
            bezelMf.sharedMesh = Grid(1.04f, Back, "AR6DoF_Cadre");
            float depth = Mathf.Max(-P(0f, 0f).z, -P(0.5f, 0f).z);
            box.size = new Vector3(width * 1.05f, Height * 1.05f, depth + 0.04f);
            box.center = new Vector3(0f, 0f, -depth * 0.5f);
        }

        void Init(int t, float w)
        {
            type = Mathf.Clamp(t, 0, TypeNames.Length - 1);
            width = Mathf.Clamp(w, 0.3f, 3f);
            screenMat = NewMat(0f, new Color(0.012f, 0.012f, 0.018f));
            bezelMat = NewMat(1f, new Color(0.1f, 0.1f, 0.12f));
            screenMf = Quad("Ecran", screenMat);
            bezelMf = Quad("Cadre", bezelMat);
            box = gameObject.AddComponent<BoxCollider>();
            BuildGeometry();
            BuildLogo();
            float a = UnityEngine.Random.Range(0.45f, 1.1f);
            dir = new Vector2(Mathf.Cos(a) * (UnityEngine.Random.value < 0.5f ? 1f : -1f), Mathf.Sin(a) * (UnityEngine.Random.value < 0.5f ? 1f : -1f));
            dir.Normalize();
            pos = new Vector2(UnityEngine.Random.Range(-0.3f, 0.3f) * width, UnityEngine.Random.Range(-0.2f, 0.2f) * Height);
            hue = UnityEngine.Random.value;
            PickColor(false);
        }

        static Material NewMat(float shade, Color c)
        {
            var m = new Material(Ar6dofTable3D.Sh());
            m.SetFloat("_Shade", shade);
            m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 0f); m.SetFloat("_ZWrite", 1f);
            m.SetColor("_Color", c);
            return m;
        }

        MeshFilter Quad(string name, Material m)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var mf = g.AddComponent<MeshFilter>();
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return mf;
        }

        void BuildLogo()
        {
            var cg = new GameObject("Logo", typeof(RectTransform));
            cg.transform.SetParent(transform, false);
            var cv = cg.AddComponent<Canvas>();
            cv.renderMode = RenderMode.WorldSpace;
            logoRt = (RectTransform)cg.transform;
            logoRt.sizeDelta = new Vector2(200f, 110f);
            var ig = new GameObject("Image", typeof(RectTransform));
            ig.transform.SetParent(cg.transform, false);
            var irt = (RectTransform)ig.transform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = irt.offsetMax = Vector2.zero;
            logoImg = ig.AddComponent<Image>();
            logoImg.raycastTarget = false;
            logoImg.preserveAspect = true;
            var tg = new GameObject("Texte", typeof(RectTransform));
            tg.transform.SetParent(cg.transform, false);
            var trt = (RectTransform)tg.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            logoText = tg.AddComponent<Text>();
            logoText.font = Ar6dofVrUi.GetFont();
            logoText.fontSize = 100;
            logoText.fontStyle = FontStyle.BoldAndItalic;
            logoText.alignment = TextAnchor.MiddleCenter;
            logoText.horizontalOverflow = HorizontalWrapMode.Overflow;
            logoText.verticalOverflow = VerticalWrapMode.Overflow;
            logoText.raycastTarget = false;
            logoText.text = "DVD";
        }

        /// <summary>Applique le choix du logo (mot, disque, image) : seulement quand il change.</summary>
        void ApplyLogo()
        {
            int lt = Ar6dofDvdWorld.LogoType;
            string path = Ar6dofDvdWorld.LogoPath;
            if (lt == shownLogo && (lt != 2 || path == shownPath)) return;
            Sprite sp = null;
            if (lt == 2) { sp = Ar6dofDvdWorld.CustomSprite(); if (sp == null) lt = 0; }
            shownLogo = Ar6dofDvdWorld.LogoType; shownPath = path;
            if (lt == 1)
            {
                if (discSprite == null)
                {
                    var tex = Ar6dofGameArt.Icon("disc");
                    if (tex != null) discSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                }
                sp = discSprite;
                if (sp == null) lt = 0;
            }
            if (lt == 0)
            {
                logoImg.enabled = false; logoText.enabled = true;
                logoRt.sizeDelta = new Vector2(200f, 110f); logoAspect = 0.55f;
            }
            else
            {
                logoText.enabled = false; logoImg.enabled = true; logoImg.sprite = sp;
                float asp = lt == 1 ? 1f : Mathf.Clamp((float)sp.rect.height / Mathf.Max(1f, sp.rect.width), 0.15f, 4f);
                logoRt.sizeDelta = new Vector2(160f, 160f * asp); logoAspect = asp;
            }
            lastScale = -1f;
            PickColor(false);
        }

        void PickColor(bool next)
        {
            if (next) hue = Mathf.Repeat(hue + 0.17f + UnityEngine.Random.value * 0.4f, 1f);
            logoColor = Ar6dofDvdWorld.ColorChange ? Color.HSVToRGB(hue, 0.85f, 1f) : Color.white;
            if (logoText != null) logoText.color = logoColor;
            if (logoImg != null) logoImg.color = logoColor;
        }

        // ---------------------------------------------------------------- mouvement
        void Update()
        {
            ApplyLogo();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float W = width, H = Height;
            float sizeFrac = Ar6dofDvdWorld.LogoSize;
            float lw = Mathf.Min(sizeFrac * W, H * 0.7f / Mathf.Max(0.1f, logoAspect));
            float lh = lw * logoAspect;
            float hx = Mathf.Max(0.001f, (W - lw) * 0.5f - W * 0.004f), hy = Mathf.Max(0.001f, (H - lh) * 0.5f - W * 0.004f);

            float step = Ar6dofDvdWorld.Speed * W * dt;
            pos += dir * step;
            bool hitX = false, hitY = false;
            if (pos.x > hx) { pos.x = hx; dir.x = -Mathf.Abs(dir.x); hitX = true; }
            else if (pos.x < -hx) { pos.x = -hx; dir.x = Mathf.Abs(dir.x); hitX = true; }
            if (pos.y > hy) { pos.y = hy; dir.y = -Mathf.Abs(dir.y); hitY = true; }
            else if (pos.y < -hy) { pos.y = -hy; dir.y = Mathf.Abs(dir.y); hitY = true; }
            if (pos.x > hx || pos.x < -hx) pos.x = Mathf.Clamp(pos.x, -hx, hx);     // l'ecran a ete redimensionne : on rentre le logo
            if (pos.y > hy || pos.y < -hy) pos.y = Mathf.Clamp(pos.y, -hy, hy);

            if (hitX || hitY)
            {
                bounces++;
                bool corner = hitX && hitY;
                float tol = W * 0.004f + 0.004f;
                if (!corner && hitX && Mathf.Abs(pos.y) > hy - tol) corner = true;       // le logo touche un mur et frole l'autre : c'est un coin
                if (!corner && hitY && Mathf.Abs(pos.x) > hx - tol) corner = true;
                PickColor(true);
                if (corner) OnCorner();
                else if (Ar6dofDvdWorld.BounceSound) Ar6dofDvdWorld.Play(Ar6dofGameSound.Id.Clack, 0.18f);
            }

            if (flash > 0f) flash = Mathf.Max(0f, flash - dt * 0.9f);
            if (flash != lastFlash)
            {
                lastFlash = flash;
                screenMat.SetColor("_Color", Color.Lerp(new Color(0.012f, 0.012f, 0.018f), new Color(0.85f, 0.85f, 0.95f), flash * 0.55f));
            }
            if (cornerLabel != null && cornerUntil > 0f && Time.unscaledTime > cornerUntil) { cornerLabel.Show(false); cornerUntil = 0f; }

            // le logo est pose sur la surface, regarde vers toi, juste devant l'ecran
            float u = pos.x / W, v = pos.y / H;
            Vector3 p = P(u, v), n = Normal(u, v);
            logoRt.localPosition = p + n * 0.004f;
            logoRt.localRotation = Quaternion.LookRotation(-n, Vector3.up);
            float sc = lw / logoRt.sizeDelta.x * (1f + 0.3f * flash);
            if (Mathf.Abs(sc - lastScale) > 1e-6f) { lastScale = sc; logoRt.localScale = Vector3.one * sc; }
        }

        void OnCorner()
        {
            corners++;
            flash = 1f;
            Ar6dofDvdWorld.Play(Ar6dofGameSound.Id.Win3, 0.8f);
            try
            {
                if (cornerLabel == null)
                {
                    cornerLabel = Ar6dofLabel3D.Make(transform, Vector3.zero, width * 0.3f, 70, new Color(1f, 0.9f, 0.3f));
                    cornerLabel.Set(Ar6dofLoc.T("COIN !"), new Color(1f, 0.9f, 0.3f));
                }
                Vector3 p = P(0f, 0.3f), n = Normal(0f, 0.3f);
                cornerLabel.tr.localPosition = p + n * 0.01f;
                cornerLabel.tr.localRotation = Quaternion.LookRotation(-n, Vector3.up);
                cornerLabel.Show(true);
                cornerUntil = Time.unscaledTime + 2f;
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : texte de coin impossible : " + e.Message); }
        }

        public void Resize(float w)
        {
            width = Mathf.Clamp(w, 0.3f, 3f);
            BuildGeometry();
            lastScale = -1f;
            if (cornerLabel != null && cornerLabel.tr != null) { Destroy(cornerLabel.tr.gameObject); cornerLabel = null; }
        }

        public void ResetCounters() { bounces = 0; corners = 0; }

        // ---------------------------------------------------------------- laser
        void Glow(float g) { if (bezelMat != null) bezelMat.SetFloat("_Glow", g); }

        public void OnPointerEnter() { hover = true; Glow(0.12f); }
        public void OnPointerExit() { hover = false; if (!dragging) Glow(0f); }
        public void OnPointerHover(Vector3 worldPoint) { }

        public void OnPointerDown(Vector3 worldPoint, int button)
        {
            if (button != 0) return;
            dragging = true;
            dist = Vector3.Distance(Ar6dofPointer.LastRay.origin, worldPoint);
            offset = transform.position - worldPoint;
            Glow(0.25f);
        }

        public void OnPointerUp(Vector3 worldPoint, int button)
        {
            if (!dragging) return;
            dragging = false;
            Glow(hover ? 0.12f : 0f);
        }

        public void OnDrag(Ray ray)
        {
            if (!dragging) return;
            Vector3 p = ray.GetPoint(dist) + offset;
            transform.position = p;
            Transform h = Ar6dofDvdWorld.HeadT();
            if (h != null)
            {
                Vector3 d = p - h.position; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);     // l'ecran te fait toujours face
            }
        }

        void OnDestroy()
        {
            if (screenMat != null) Destroy(screenMat);
            if (bezelMat != null) Destroy(bezelMat);
            if (screenMf != null && screenMf.sharedMesh != null) Destroy(screenMf.sharedMesh);
            if (bezelMf != null && bezelMf.sharedMesh != null) Destroy(bezelMf.sharedMesh);
        }
    }
}
