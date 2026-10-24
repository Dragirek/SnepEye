using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Posé sur la racine d'un menu : dit à quel élément (Dock, Menus...) appartiennent les boutons et textes qu'il contient.</summary>
    public class Ar6dofThemeScope : MonoBehaviour
    {
        public Ar6dofElem elem = Ar6dofElem.Menus;
    }

    public enum Ar6dofRole { Surface, Button, Accent, Text, AccentText, Track, Handle, Dim }

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
                    Ar6dofTheme.SetRounded(img, st.corner);
                    ApplyTexture(img, st);
                    break;
                }
                case Ar6dofRole.Button:
                {
                    Color c = selected ? Color.Lerp(st.button, st.accent, 0.65f) : st.button;
                    c.a = Mathf.Lerp(1f, st.alpha, 0.5f);
                    g.color = c;
                    Ar6dofTheme.SetRounded(img, Mathf.Min(st.corner, radiusCap));
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
                    Ar6dofTheme.SetRounded(img, Mathf.Min(st.corner, radiusCap));
                    break;
                case Ar6dofRole.Track:
                {
                    Color c = Color.Lerp(st.button, Color.black, 0.45f); c.a = 1f;
                    g.color = c;
                    Ar6dofTheme.SetRounded(img, Mathf.Min(st.corner, radiusCap));
                    break;
                }
                case Ar6dofRole.Handle:
                    g.color = st.text;
                    Ar6dofTheme.SetRounded(img, Mathf.Min(st.corner, radiusCap));
                    break;
                case Ar6dofRole.Dim:
                {
                    Color c = st.text; c.a = 0.25f;
                    g.color = c;
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
            Sprite sp = null;
            bool tiled = true, custom = false;
            bool want = !string.IsNullOrEmpty(st.texture) && st.texAlpha > 0.005f;
            if (want) Ar6dofTheme.GetTextureSprite(st.texture, out sp, out tiled, out custom);
            if (sp == null)
            {
                if (layer != null) layer.gameObject.SetActive(false);
                return;
            }
            if (layer == null)
            {
                var go = new GameObject("Texture", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rt0 = (RectTransform)go.transform;
                rt0.anchorMin = Vector2.zero;
                rt0.anchorMax = Vector2.one;
                var ti0 = go.AddComponent<Image>();
                ti0.raycastTarget = false;
                layer = go.transform;
            }
            layer.gameObject.SetActive(true);
            float inset = st.corner * 0.3f;   // le carré de texture reste à l'intérieur des coins arrondis
            var rt = (RectTransform)layer;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            var ti = layer.GetComponent<Image>();
            ti.sprite = sp;
            ti.type = tiled ? Image.Type.Tiled : Image.Type.Simple;
            Color tc = custom ? Color.white : st.text;
            tc.a = st.texAlpha;
            ti.color = tc;
        }
    }

    /// <summary>Sprites arrondis et textures de fond dessinés par code, et petits outils de style.</summary>
    public static class Ar6dofTheme
    {
        static readonly Dictionary<int, Sprite> rounded = new Dictionary<int, Sprite>();
        static readonly Dictionary<string, Sprite> texSprites = new Dictionary<string, Sprite>();
        static readonly string[] BuiltinTex = { "", "grid", "dots", "carbon", "stripes", "gradient", "noise" };
        static readonly string[] BuiltinTexNames = { "Aucune", "Grille", "Points", "Carbone", "Rayures", "Degrade", "Bruit" };

        /// <summary>Dossier où poser tes images (png / jpg) pour les utiliser comme fonds de menus. Créé automatiquement.</summary>
        public static string PicturesFolder
        {
            get
            {
                string p = Path.Combine(Ar6dofSettings.FolderPath, "pictures");
                try
                {
                    if (!Directory.Exists(p))
                    {
                        Directory.CreateDirectory(p);
                        File.WriteAllText(Path.Combine(p, "LISEZMOI.txt"),
                            "Dossier d'images AR6DoF\r\n\r\n" +
                            "Depose ici tes images .png ou .jpg : elles apparaissent dans le menu Style > Forme > Fond (en VR).\r\n" +
                            "Petite image (256 px ou moins) = motif repete. Grande image = etiree sur tout le menu.\r\n" +
                            "Pas besoin de redemarrer : rouvre simplement la liste des fonds.\r\n");
                    }
                }
                catch (Exception) { }
                return p;
            }
        }

        static string LegacyFolder { get { return Path.Combine(Ar6dofSettings.FolderPath, "textures"); } }

        /// <summary>Ouvre le dossier d'images dans l'explorateur de Windows.</summary>
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
            string a = Path.Combine(PicturesFolder, name);
            if (File.Exists(a)) return a;
            string b = Path.Combine(LegacyFolder, name);   // ancien dossier "textures" toujours lu
            return File.Exists(b) ? b : null;
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

        // ------------------------------------------------------------------ textures de fond
        public static List<string> TextureIds()
        {
            var l = new List<string>(BuiltinTex);
            try
            {
                foreach (string dir in new[] { PicturesFolder, LegacyFolder })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        string id = "file:" + Path.GetFileName(f);
                        if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && !l.Contains(id)) l.Add(id);
                    }
                }
            }
            catch (Exception) { }
            return l;
        }

        /// <summary>Nom à afficher (déjà traduit) d'une texture.</summary>
        public static string TextureLabel(string id)
        {
            for (int i = 0; i < BuiltinTex.Length; i++) if (BuiltinTex[i] == id) return Ar6dofLoc.T(BuiltinTexNames[i]);
            return id.StartsWith("file:") ? id.Substring(5) : id;
        }

        public static void GetTextureSprite(string id, out Sprite sprite, out bool tiled, out bool custom)
        {
            sprite = null;
            tiled = true;
            custom = id != null && id.StartsWith("file:");
            if (string.IsNullOrEmpty(id)) return;
            if (!texSprites.TryGetValue(id, out sprite) || sprite == null)
            {
                sprite = custom ? LoadFile(id.Substring(5)) : MakeBuiltin(id);
                if (sprite != null) texSprites[id] = sprite;
            }
            if (sprite == null) return;
            tiled = id != "gradient" && sprite.rect.width <= 256f && sprite.rect.height <= 256f;
        }

        static Sprite LoadFile(string name)
        {
            try
            {
                string path = FindPicture(name);
                if (path == null) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) return null;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
            catch (Exception) { return null; }
        }

        static Sprite MakeBuiltin(string id)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = id == "gradient" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
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
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
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
}
