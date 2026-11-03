using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Posé sur la racine d'un menu : dit à quel élément (Dock, Menus...) appartiennent les boutons et textes qu'il contient.</summary>
    public class Ar6dofThemeScope : MonoBehaviour
    {
        public Ar6dofElem elem = Ar6dofElem.Menus;
    }

    public enum Ar6dofRole { Surface, Button, Accent, Text, AccentText, Track, Handle, Dim, Band }

    /// <summary>Applique le style courant (Ar6dofSettings) à un Image ou un Text, et se remet à jour quand le style change.</summary>
    public class Ar6dofThemed : MonoBehaviour
    {
        public Ar6dofRole role = Ar6dofRole.Surface;
        public Ar6dofElem elem = Ar6dofElem.Menus;
        public int radiusCap = 999;
        public bool selected;   // bouton d'onglet / choix actif : teinté avec la couleur d'accent

        bool ready;
        Graphic g;
        Button btn;
        Color last;
        bool hasLast;
        public bool concentric;   // rôle Accent : suit les arrondis concentriques (bouton plein) au lieu de garder sa forme
        public int radius;   // rayon réellement appliqué (le cadre de survol s'y accorde)

        public void Init(Ar6dofRole r, int cap)
        {
            role = r;
            radiusCap = cap;
            elem = Ar6dofTheme.ElemOf(transform);
            ready = true;
            Apply();
        }

        void OnEnable()
        {
            Ar6dofSettings.Changed += Apply;
            if (ready) Apply();
        }

        void OnDisable() { Ar6dofSettings.Changed -= Apply; }

        public void Apply()
        {
            if (!ready) return;
            if (g == null) g = GetComponent<Graphic>();
            if (g == null) return;
            var st = Ar6dofSettings.StyleFor(elem);
            var img = g as Image;
            switch (role)
            {
                case Ar6dofRole.Surface:
                {
                    Color c = st.bg; c.a = st.alpha;
                    g.color = c;
                    radius = Ar6dofTheme.Radius(transform, st.corner, 999, 0);
                    Ar6dofTheme.SetRounded(img, radius);
                    ApplyTexture(img, st);
                    break;
                }
                case Ar6dofRole.Button:
                {
                    Color c = selected ? Color.Lerp(st.button, st.accent, 0.65f) : st.button;
                    c.a = Mathf.Lerp(1f, st.alpha, 0.5f);
                    g.color = c;
                    radius = Ar6dofTheme.Radius(transform, st.corner, radiusCap, 4);
                    Ar6dofTheme.SetRounded(img, radius);
                    if (btn == null) btn = GetComponent<Button>();
                    if (btn != null)
                    {
                        var cb = btn.colors;
                        Color hv = Color.Lerp(Color.white, st.accent, 0.5f);
                        cb.normalColor = Color.white;
                        cb.highlightedColor = new Color(Mathf.Min(1.5f, hv.r * 1.35f), Mathf.Min(1.5f, hv.g * 1.35f), Mathf.Min(1.5f, hv.b * 1.35f), 1f);
                        cb.pressedColor = Color.Lerp(Color.white, st.accent, 0.9f);
                        cb.selectedColor = Color.white;
                        cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
                        cb.colorMultiplier = 1f;
                        cb.fadeDuration = 0.06f;
                        btn.colors = cb;
                    }
                    break;
                }
                case Ar6dofRole.Accent:
                    g.color = st.accent;
                    radius = concentric ? Ar6dofTheme.Radius(transform, st.corner, radiusCap, 4) : Mathf.Min(st.corner, radiusCap);   // pastilles et traits gardent leur forme ; un bouton plein suit les arrondis concentriques
                    Ar6dofTheme.SetRounded(img, radius);
                    break;
                case Ar6dofRole.Track:
                {
                    Color c = Color.Lerp(st.button, Color.black, 0.45f); c.a = 1f;
                    g.color = c;
                    radius = Mathf.Min(st.corner, radiusCap);
                    Ar6dofTheme.SetRounded(img, radius);
                    break;
                }
                case Ar6dofRole.Handle:
                    g.color = st.text;
                    radius = Mathf.Min(st.corner, radiusCap);
                    Ar6dofTheme.SetRounded(img, radius);
                    break;
                case Ar6dofRole.Dim:
                {
                    Color c = st.text; c.a = 0.25f;
                    g.color = c;
                    break;
                }
                case Ar6dofRole.Band:
                {
                    // cadre de regroupement : le fond tire legerement vers la couleur du texte (plus clair sur fond sombre, plus sombre sur fond clair)
                    Color c = Color.Lerp(st.bg, st.text, 0.07f);
                    c.a = Mathf.Max(0.6f, st.alpha);
                    g.color = c;
                    radius = Ar6dofTheme.Radius(transform, st.corner, radiusCap, 4);
                    Ar6dofTheme.SetRounded(img, radius);
                    break;
                }
                case Ar6dofRole.Text:
                case Ar6dofRole.AccentText:
                {
                    if (hasLast && g.color != last) break;   // le code a donné une couleur précise à ce texte : on n'y touche plus
                    Color c = role == Ar6dofRole.Text ? st.text : st.accent;
                    g.color = c;
                    last = c;
                    hasLast = true;
                    break;
                }
            }
        }

        void ApplyTexture(Image img, Ar6dofStyle st)
        {
            if (img == null) return;
            Transform layer = transform.Find("Texture");
            Texture2D tex = null;
            bool custom = false;
            int mode = Mathf.Clamp(st.texMode, 0, 4);
            bool want = !string.IsNullOrEmpty(st.texture) && st.texAlpha > 0.005f;
            if (want) Ar6dofTheme.GetTexture(st.texture, mode == 2, out tex, out custom);
            if (tex == null)
            {
                if (layer != null) layer.gameObject.SetActive(false);
                return;
            }
            if (layer == null)
            {
                var go = new GameObject("Texture", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                go.AddComponent<RawImage>().raycastTarget = false;
                layer = go.transform;
            }
            layer.gameObject.SetActive(true);
            var rt = (RectTransform)layer;
            var prt = (RectTransform)transform;
            float inset = Mathf.Max(2f, radius * 0.3f);   // l'image reste à l'intérieur des coins arrondis
            float pw = Mathf.Max(8f, prt.rect.width - 2f * inset), ph = Mathf.Max(8f, prt.rect.height - 2f * inset);
            float tw = tex.width, th = tex.height;
            float sc = Mathf.Clamp(st.texScale, 0.1f, 8f);
            Rect uv = new Rect(0f, 0f, 1f, 1f);
            Vector2 size = new Vector2(pw, ph);
            switch (mode)
            {
                case 1:
                case 2:   // répéter / miroir : le motif se répète sur toute la surface
                    uv = new Rect(0f, 0f, pw / (tw * sc), ph / (th * sc));
                    break;
                case 3:   // centrée, sans répétition : taille d'origine x échelle, rognée si elle dépasse
                {
                    float dw = tw * sc, dh = th * sc;
                    float sw = Mathf.Min(pw, dw), sh = Mathf.Min(ph, dh);
                    size = new Vector2(sw, sh);
                    uv = new Rect((1f - sw / dw) * 0.5f, (1f - sh / dh) * 0.5f, sw / dw, sh / dh);
                    break;
                }
                case 4:   // remplir : toute la surface, proportions gardées, bords rognés
                {
                    float ia = tw / th, ra = pw / ph;
                    if (ia > ra) { float f = ra / ia; uv = new Rect((1f - f) * 0.5f, 0f, f, 1f); }
                    else { float f = ia / ra; uv = new Rect(0f, (1f - f) * 0.5f, 1f, f); }
                    break;
                }
            }
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var ti = layer.GetComponent<RawImage>();
            ti.texture = tex;
            ti.uvRect = uv;
            Color tc = custom ? Color.white : st.text;
            tc.a = st.texAlpha;
            ti.color = tc;
        }
    }

    /// <summary>Sprites arrondis et textures de fond dessinés par code, et petits outils de style.</summary>
    public static class Ar6dofTheme
    {
        static readonly Dictionary<int, Sprite> rounded = new Dictionary<int, Sprite>();
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static readonly string[] BuiltinTex = { "", "grid", "dots", "carbon", "stripes", "gradient", "noise" };
        static readonly string[] BuiltinTexNames = { "Aucune", "Grille", "Points", "Carbone", "Rayures", "Degrade", "Bruit" };

        /// <summary>Dossier facile d'accès où poser tes images de fond (png / jpg) : Images\AR6DoF. Créé automatiquement.</summary>
        public static string PicturesFolder
        {
            get
            {
                string p;
                try { p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AR6DoF"); }
                catch (Exception) { p = Path.Combine(Ar6dofSettings.FolderPath, "pictures"); }
                if (string.IsNullOrEmpty(p) || p == "AR6DoF") p = Path.Combine(Ar6dofSettings.FolderPath, "pictures");
                try
                {
                    if (!Directory.Exists(p))
                    {
                        Directory.CreateDirectory(p);
                        File.WriteAllText(Path.Combine(p, "LISEZMOI.txt"),
                            "Dossier d'images AR6DoF\r\n\r\n" +
                            "Glisse ici tes images .png ou .jpg : elles apparaissent dans Reglages > Style > Forme > Image de fond.\r\n" +
                            "La liste se relit quand tu ouvres l'onglet Style (pas besoin de redemarrer).\r\n");
                    }
                }
                catch (Exception) { }
                return p;
            }
        }

        // anciens dossiers, toujours lus : %APPDATA%\AR6DoF\pictures et \textures
        static string AppDataPictures { get { return Path.Combine(Ar6dofSettings.FolderPath, "pictures"); } }
        static string LegacyFolder { get { return Path.Combine(Ar6dofSettings.FolderPath, "textures"); } }

        /// <summary>Ouvre le dossier d'images dans l'explorateur de Windows (sur l'écran principal du PC).</summary>
        public static void OpenPicturesFolder()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + PicturesFolder + "\""); }
            catch (Exception)
            {
                try { Application.OpenURL("file:///" + PicturesFolder.Replace('\\', '/')); } catch (Exception) { }
            }
        }

        static string FindPicture(string name)
        {
            foreach (string dir in new[] { PicturesFolder, AppDataPictures, LegacyFolder })
            {
                string a = Path.Combine(dir, name);
                if (File.Exists(a)) return a;
            }
            return null;
        }

        /// <summary>Rayon concentrique : chaque cadre ou panneau parent retire 'Pad' pixels au rayon du panneau. Un bouton dans un cadre dans un panneau a donc le rayon du panneau - 2 x Pad.</summary>
        const int Pad = 12;

        public static int Radius(Transform t, int corner, int cap, int minRadius)
        {
            if (corner <= 0) return 0;
            int depth = 0;
            if (t != null)
            {
                // le fond du menu (« Fond », Surface) compte comme un parent de tout ce que contient le menu
                var cv = t.GetComponentInParent<Canvas>(true);
                if (cv != null)
                {
                    var fond = cv.rootCanvas.transform.Find("Fond");
                    if (fond != null && fond != t && fond.GetComponent<Ar6dofThemed>() != null) depth = 1;
                }
                for (Transform p = t.parent; p != null; p = p.parent)
                {
                    var th = p.GetComponent<Ar6dofThemed>();
                    if (th != null && (th.role == Ar6dofRole.Surface || th.role == Ar6dofRole.Band)) depth++;
                }
            }
            int r = Mathf.Max(minRadius, corner - Pad * depth);
            return Mathf.Clamp(Mathf.Min(r, cap), 0, 48);
        }

        public static Ar6dofElem ElemOf(Transform t)
        {
            var sc = t != null ? t.GetComponentInParent<Ar6dofThemeScope>(true) : null;
            return sc != null ? sc.elem : Ar6dofElem.Menus;
        }

        /// <summary>Rend un élément (Image ou Text) dépendant du style. Toujours appelé juste après sa création.</summary>
        public static Ar6dofThemed Attach(GameObject go, Ar6dofRole role, int radiusCap = 999)
        {
            var th = go.GetComponent<Ar6dofThemed>();
            if (th == null) th = go.AddComponent<Ar6dofThemed>();
            th.Init(role, radiusCap);
            if ((role == Ar6dofRole.Button || role == Ar6dofRole.Handle) && go.GetComponent<Ar6dofHoverFx>() == null) go.AddComponent<Ar6dofHoverFx>();
            return th;
        }

        /// <summary>Met en évidence (ou non) un bouton thématisé : onglet actif, langue choisie...</summary>
        public static void SetSelected(Component c, bool sel)
        {
            if (c == null) return;
            var th = c.GetComponent<Ar6dofThemed>();
            if (th == null) return;
            th.selected = sel;
            th.Apply();
        }

        public static Color FieldColor(bool focused)
        {
            var st = Ar6dofSettings.StyleFor(Ar6dofElem.Menus);
            return focused ? Color.Lerp(st.button, st.accent, 0.4f) : Color.Lerp(st.bg, Color.black, 0.3f);
        }

        // ------------------------------------------------------------------ arrondis
        public static void SetRounded(Image img, int radius)
        {
            if (img == null) return;
            Sprite sp = Rounded(radius);
            img.sprite = sp;
            img.type = sp != null ? Image.Type.Sliced : Image.Type.Simple;
        }

        /// <summary>Rectangle à coins arrondis de rayon r (pixels), découpable en 9 parties (Image.Type.Sliced). Null si r = 0.</summary>
        public static Sprite Rounded(int r)
        {
            r = Mathf.Clamp(r, 0, 48);
            if (r <= 0) return null;
            Sprite s;
            if (rounded.TryGetValue(r, out s) && s != null) return s;
            int n = r * 2 + 2;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = x + 0.5f, cy = y + 0.5f;
                    float dx = Mathf.Max(r - cx, Mathf.Max(cx - (n - r), 0f));
                    float dy = Mathf.Max(r - cy, Mathf.Max(cy - (n - r), 0f));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            rounded[r] = s;
            return s;
        }

        // ------------------------------------------------------------------ cadre de survol
        static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>();

        /// <summary>Contour arrondi de rayon r et d'épaisseur t (pixels), découpable en 9 parties : le cadre qui entoure un bouton visé par le laser.</summary>
        public static Sprite Ring(int r, int t)
        {
            r = Mathf.Clamp(r, 3, 48);
            t = Mathf.Clamp(t, 1, r);
            int key = r * 100 + t;
            Sprite s;
            if (rings.TryGetValue(key, out s) && s != null) return s;
            int n = r * 2 + 2;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n];
            float half = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - r);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - r);
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float sd = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;   // distance au bord : négative à l'intérieur
                    float a = Mathf.Clamp01(0.5f - sd) * Mathf.Clamp01(sd + t + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            rings[key] = s;
            return s;
        }

        static float Lin(float v) { return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }

        static float Lum(Color c) { return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b); }

        /// <summary>Rapport de contraste (1 à 21) entre deux couleurs, comme les règles d'accessibilité du web.</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Lum(a), lb = Lum(b);
            if (la < lb) { float t = la; la = lb; lb = t; }
            return (la + 0.05f) / (lb + 0.05f);
        }

        /// <summary>Noir ou blanc, selon ce qui se lit le mieux sur cette couleur de fond.</summary>
        public static Color OnColor(Color bg)
        {
            bg.a = 1f;
            return Contrast(Color.white, bg) >= Contrast(new Color(0.02f, 0.02f, 0.02f, 1f), bg) ? Color.white : new Color(0.02f, 0.02f, 0.02f, 1f);
        }

        /// <summary>Couleur du cadre de survol : l'accent s'il se voit nettement, sinon blanc ou noir, selon ce qui contraste le plus avec le bouton ET avec le fond du menu.</summary>
        public static Color HoverColor(Color button, Ar6dofStyle st)
        {
            button.a = 1f;
            Color bg = st.bg; bg.a = 1f;
            Color acc = st.accent; acc.a = 1f;
            Color white = Color.white, black = new Color(0.02f, 0.02f, 0.02f, 1f);
            float sa = Mathf.Min(Contrast(acc, button), Contrast(acc, bg));
            if (sa >= 2.6f) return acc;
            float sw = Mathf.Min(Contrast(white, button), Contrast(white, bg));
            float sb = Mathf.Min(Contrast(black, button), Contrast(black, bg));
            return sw >= sb ? white : black;
        }

        // ------------------------------------------------------------------ textures de fond
        static List<string> textureIds;

        /// <summary>Relit le dossier d'images. Appelé seulement en entrant dans l'onglet Style : jamais en continu.</summary>
        public static void RescanPictures()
        {
            var l = new List<string>(BuiltinTex);
            try
            {
                foreach (string dir in new[] { PicturesFolder, AppDataPictures, LegacyFolder })
                {
                    if (!Directory.Exists(dir)) continue;
                    var files = Directory.GetFiles(dir);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        string id = "file:" + Path.GetFileName(f);
                        if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && !l.Contains(id)) l.Add(id);
                    }
                }
            }
            catch (Exception) { }
            textureIds = l;
        }

        /// <summary>Liste des fonds (motifs d'usine puis tes images). Lue une fois, puis seulement sur demande (RescanPictures).</summary>
        public static List<string> TextureIds()
        {
            if (textureIds == null) RescanPictures();
            return textureIds;
        }

        /// <summary>Nom à afficher (déjà traduit) d'une texture.</summary>
        public static string TextureLabel(string id)
        {
            for (int i = 0; i < BuiltinTex.Length; i++) if (BuiltinTex[i] == id) return Ar6dofLoc.T(BuiltinTexNames[i]);
            return id.StartsWith("file:") ? id.Substring(5) : id;
        }

        /// <summary>Image de fond : motif d'usine ou fichier. Le mode "miroir" utilise sa propre copie (le mode d'adressage appartient à la texture).</summary>
        public static void GetTexture(string id, bool mirror, out Texture2D tex, out bool custom)
        {
            tex = null;
            custom = id != null && id.StartsWith("file:");
            if (string.IsNullOrEmpty(id)) return;
            string key = mirror ? id + "|m" : id;
            if (!texCache.TryGetValue(key, out tex) || tex == null)
            {
                tex = custom ? LoadFile(id.Substring(5), mirror) : MakeBuiltin(id, mirror);
                if (tex != null) texCache[key] = tex;
            }
        }

        /// <summary>Taille en pixels de l'image (0 si introuvable) : sert à choisir un mode de départ logique.</summary>
        public static Vector2Int TextureSize(string id)
        {
            Texture2D t; bool c;
            GetTexture(id, false, out t, out c);
            return t != null ? new Vector2Int(t.width, t.height) : Vector2Int.zero;
        }

        static Texture2D LoadFile(string name, bool mirror)
        {
            try
            {
                string path = FindPicture(name);
                if (path == null) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) return null;
                tex.wrapMode = mirror ? TextureWrapMode.Mirror : TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            catch (Exception) { return null; }
        }

        static Texture2D MakeBuiltin(string id, bool mirror)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = mirror ? TextureWrapMode.Mirror : TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[N * N];
            var rnd = new System.Random(1234);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float a = 0f;
                    switch (id)
                    {
                        case "grid": a = (x % 16 == 0 || y % 16 == 0) ? 1f : 0f; break;
                        case "dots":
                        {
                            float dx = (x % 16) - 8f, dy = (y % 16) - 8f;
                            a = Mathf.Clamp01(2.6f - Mathf.Sqrt(dx * dx + dy * dy));
                            break;
                        }
                        case "carbon": a = (((x / 8) + (y / 8)) % 2 == 0) ? 1f : 0.3f; break;
                        case "stripes": a = ((x + y) % 16 < 6) ? 1f : 0f; break;
                        case "gradient": a = y / (float)(N - 1); break;
                        case "noise": a = (float)rnd.NextDouble(); break;
                        default: return null;
                    }
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ------------------------------------------------------------------ objets 3D (touches, poignées) : matériau à coins arrondis
        static Shader roundedShader;
        public static Shader RoundedShader
        {
            get
            {
                if (roundedShader == null) roundedShader = Shader.Find("AR6DoF/Rounded");
                return roundedShader;
            }
        }

        /// <summary>Règle un matériau AR6DoF/Rounded : taille réelle de la face (m) et rayon des coins (m).</summary>
        public static void SetRoundedSize(Material m, float width, float height, int cornerPx)
        {
            if (m == null || !m.HasProperty("_Size")) return;
            m.SetVector("_Size", new Vector4(width, height, 0f, 0f));
            m.SetFloat("_Radius", Mathf.Clamp(cornerPx * 0.0006f, 0f, Mathf.Min(width, height) * 0.5f));
        }
    }
    /// <summary>
    /// Cadre de survol : quand le laser vise un bouton (ou la poignée d'un curseur), un contour net apparaît autour de lui.
    /// Sa couleur est choisie pour se voir sur n'importe quelle palette (l'ancienne simple éclaircie du bouton disparaissait sur certaines couleurs).
    /// </summary>
    public class Ar6dofHoverFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        Image ring;

        public void OnPointerEnter(PointerEventData e) { Show(true); }
        public void OnPointerExit(PointerEventData e) { Show(false); }
        void OnDisable() { if (ring != null) ring.gameObject.SetActive(false); }

        void Show(bool on)
        {
            if (!on)
            {
                if (ring != null) ring.gameObject.SetActive(false);
                return;
            }
            var g = GetComponent<Graphic>();
            if (g == null) return;
            var th = GetComponent<Ar6dofThemed>();
            var st = Ar6dofSettings.StyleFor(Ar6dofTheme.ElemOf(transform));
            int r = Mathf.Clamp((th != null && th.radius > 0 ? th.radius : Mathf.Min(st.corner, 12)) + 4, 4, 48);   // le cadre épouse le bouton, 4 px autour
            if (ring == null)
            {
                var go = new GameObject("Survol", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(-4f, -4f);
                rt.offsetMax = new Vector2(4f, 4f);
                ring = go.AddComponent<Image>();
                ring.raycastTarget = false;
            }
            ring.sprite = Ar6dofTheme.Ring(r, 4);
            ring.type = Image.Type.Sliced;
            ring.color = Ar6dofTheme.HoverColor(g.color, st);
            ring.transform.SetAsLastSibling();
            ring.gameObject.SetActive(true);
        }
    }
}
