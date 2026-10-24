using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Petites icônes dessinées par code (fermer, réduire, redimensionner) ; la roue crantée vient d'Ar6dofVrUi.</summary>
    public static class Ar6dofIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            Texture2D t;
            if (cache.TryGetValue(name, out t) && t != null) return t;
            if (name == "gear")
            {
                var sp = Ar6dofVrUi.GearSprite();
                t = sp != null ? sp.texture : null;
            }
            else t = Draw(name);
            if (t != null) cache[name] = t;
            return t;
        }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
            float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Texture2D Draw(string name)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f, a = 0f;
                    switch (name)
                    {
                        case "close":
                            a = Mathf.Clamp01(3.5f - Mathf.Min(SegDist(fx, fy, 19f, 19f, 45f, 45f), SegDist(fx, fy, 19f, 45f, 45f, 19f)));
                            break;
                        case "minimize":
                            a = Mathf.Clamp01(3.5f - SegDist(fx, fy, 17f, 32f, 47f, 32f));
                            break;
                        case "resize":
                        {
                            // 3 traits en diagonale dans le coin bas droit (le plus long est le plus loin du coin)
                            float[] off = { 10f, 24f, 38f };
                            for (int k = 0; k < off.Length; k++)
                            {
                                float o = off[k];
                                float d = SegDist(fx, fy, o + 6f, 6f, 56f, 56f - o);
                                a = Mathf.Max(a, Mathf.Clamp01(3.2f - d));
                            }
                            break;
                        }
                    }
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }
    }

    /// <summary>Un bouton de fenêtre (réduire, fermer, redimensionner) : petit carré ou rond 3D que le laser peut viser.</summary>
    public class Ar6dofChromeButton : MonoBehaviour, IAr6dofPointerTarget, IAr6dofPointerDrag
    {
        public enum Kind { Minimize, Close, Resize }
        public Kind kind;
        public Ar6dofWindowChrome chrome;
        public Renderer bg, icon;
        public bool armed;      // fermeture en attente de confirmation
        bool hover, pressed;

        public void Refresh()
        {
            if (bg == null || chrome == null) return;
            var d = Ar6dofSettings.Current;
            var st = Ar6dofSettings.StyleFor(chrome.elem);
            bool mac = d.chromeStyle == 1;
            bool active = hover || pressed;
            Color c, ic = st.text;
            float iconAlpha = 1f;
            if (armed) { c = new Color(0.9f, 0.2f, 0.2f); ic = Color.white; }
            else if (mac && kind == Kind.Close) { c = new Color(1f, 0.37f, 0.34f); ic = new Color(0.3f, 0.05f, 0.05f); iconAlpha = active ? 1f : 0f; }
            else if (mac && kind == Kind.Minimize) { c = new Color(1f, 0.74f, 0.18f); ic = new Color(0.35f, 0.22f, 0f); iconAlpha = active ? 1f : 0f; }
            else if (kind == Kind.Close && active) { c = new Color(0.88f, 0.2f, 0.2f); ic = Color.white; }
            else
            {
                c = active ? st.accent : st.button;
                c.a = Mathf.Lerp(1f, st.alpha, 0.5f);
            }
            ic.a = iconAlpha;
            bg.sharedMaterial.color = c;
            if (icon != null) icon.sharedMaterial.color = ic;
        }

        public void OnPointerEnter() { hover = true; Refresh(); }
        public void OnPointerExit() { hover = false; Refresh(); }
        public void OnPointerHover(Vector3 world) { }

        public void OnPointerDown(Vector3 world, int button)
        {
            pressed = true;
            Refresh();
            if (kind == Kind.Resize) chrome.ResizeBegin(world);
        }

        public void OnPointerUp(Vector3 world, int button)
        {
            bool was = pressed;
            pressed = false;
            Refresh();
            if (kind == Kind.Resize) { if (was) chrome.ResizeEnd(); return; }
            if (was && hover) chrome.Click(kind);   // clic = appuyé ET relâché sur le bouton
        }

        public void OnDrag(Ray ray) { if (pressed && kind == Kind.Resize) chrome.ResizeDrag(ray); }
    }

    /// <summary>
    /// Boutons d'une fenêtre (écran virtuel ou menu) : Réduire et Fermer dans un coin au choix, style Windows ou Mac (paramètres globaux),
    /// et une seule poignée de redimensionnement dans le coin bas droit. Placés juste à l'extérieur de la fenêtre, donc jamais devant son contenu.
    /// </summary>
    public class Ar6dofWindowChrome : MonoBehaviour
    {
        public Transform target;
        public Ar6dofElem elem = Ar6dofElem.Menus;
        public Func<Vector2> halfSize;                 // demi-taille de la fenêtre en mètres (dans les axes de 'target')
        public Action onMinimize, onClose;
        public Func<bool> confirmClose;                // vrai : la fermeture demande un second clic
        public Action<Vector3> resizeBegin;            // vides : redimensionnement par défaut = échelle de 'target' (menus)
        public Action<Ray> resizeDrag;
        public Action resizeEnd;

        const float S = 0.045f, Gap = 0.008f;
        Ar6dofChromeButton bMin, bClose, bResize;
        GameObject group;
        float baseScale = 1f, startRadius = 0.1f, startScale = 1f, armedUntil;
        bool dirty = true;
        int lastStyle = -1;

        /// <summary>Demi-taille en mètres d'un menu Canvas.</summary>
        public static Func<Vector2> CanvasHalf(Canvas c)
        {
            return () =>
            {
                var rt = (RectTransform)c.transform;
                return rt.rect.size * 0.5f * c.transform.lossyScale.x;
            };
        }

        public static Ar6dofWindowChrome Create(string name, Transform target, Ar6dofElem elem, Func<Vector2> halfSize, Action onMinimize, Action onClose)
        {
            var go = new GameObject("Fenetre " + name);
            var c = go.AddComponent<Ar6dofWindowChrome>();
            c.target = target;
            c.elem = elem;
            c.halfSize = halfSize;
            c.onMinimize = onMinimize;
            c.onClose = onClose;
            c.baseScale = target != null ? target.localScale.x : 1f;
            c.Build();
            return c;
        }

        void OnEnable() { Ar6dofSettings.Changed += MarkDirty; }
        void OnDisable() { Ar6dofSettings.Changed -= MarkDirty; }
        void MarkDirty() { dirty = true; }

        void Build()
        {
            group = new GameObject("Boutons");
            group.transform.SetParent(transform, false);
            bMin = MakeBtn(Ar6dofChromeButton.Kind.Minimize, "minimize");
            bClose = MakeBtn(Ar6dofChromeButton.Kind.Close, "close");
            bResize = MakeBtn(Ar6dofChromeButton.Kind.Resize, "resize");
            group.SetActive(false);
        }

        Ar6dofChromeButton MakeBtn(Ar6dofChromeButton.Kind kind, string iconName)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = kind.ToString();
            var mc = go.GetComponent<MeshCollider>();
            if (mc != null) Destroy(mc);
            go.transform.SetParent(group.transform, false);
            go.transform.localScale = new Vector3(S, S, 1f);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(1.25f, 1.25f, 0.05f);   // zone de visée un peu plus grande que le dessin
            var r = go.GetComponent<Renderer>();
            Shader sh = Ar6dofTheme.RoundedShader;
            if (sh == null) sh = Shader.Find("Sprites/Default");
            r.sharedMaterial = new Material(sh);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var ig = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ig.name = "Icone";
            var imc = ig.GetComponent<MeshCollider>();
            if (imc != null) Destroy(imc);
            ig.transform.SetParent(go.transform, false);
            ig.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            ig.transform.localScale = new Vector3(0.62f, 0.62f, 1f);
            var ir = ig.GetComponent<Renderer>();
            var ish = Shader.Find("Sprites/Default");
            if (ish == null) ish = sh;
            var im = new Material(ish);
            im.mainTexture = Ar6dofIcons.Get(iconName);
            ir.sharedMaterial = im;
            ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ir.receiveShadows = false;

            var b = go.AddComponent<Ar6dofChromeButton>();
            b.kind = kind;
            b.chrome = this;
            b.bg = r;
            b.icon = ir;
            return b;
        }

        void ApplyShape()
        {
            var d = Ar6dofSettings.Current;
            bool mac = d.chromeStyle == 1;
            int corner = mac ? 999 : Mathf.Min(Ar6dofSettings.StyleFor(elem).corner, 10);
            foreach (var b in new[] { bMin, bClose, bResize })
            {
                Ar6dofTheme.SetRoundedSize(b.bg.sharedMaterial, S, S, corner);
                b.Refresh();
            }
            lastStyle = d.chromeStyle;
        }

        void LateUpdate()
        {
            if (target == null) { Destroy(gameObject); return; }
            bool vis = target.gameObject.activeInHierarchy;
            if (group.activeSelf != vis) group.SetActive(vis);
            if (!vis) return;
            if (dirty) { dirty = false; ApplyShape(); }
            if (bClose.armed && Time.unscaledTime > armedUntil) { bClose.armed = false; bClose.Refresh(); }

            var d = Ar6dofSettings.Current;
            bool mac = d.chromeStyle == 1;
            Vector2 hs = halfSize();
            transform.SetPositionAndRotation(target.position, target.rotation);
            int corner = Mathf.Clamp(d.chromeCorner, 0, 3);
            bool right = corner == 1 || corner == 3;
            bool top = corner == 0 || corner == 1;
            float yy = top ? hs.y + Gap + S * 0.5f : -hs.y - Gap - S * 0.5f;
            float sign = right ? 1f : -1f;
            float step = (S + Gap) * (mac ? 0.9f : 1f);
            float x0 = sign * (hs.x - S * 0.5f);
            if (corner == 3) x0 -= step;                 // en bas à droite : la poignée de taille occupe le coin, les boutons se décalent d'un cran
            bClose.gameObject.SetActive(onClose != null);
            bMin.gameObject.SetActive(onMinimize != null);
            bClose.transform.localPosition = new Vector3(x0, yy, 0f);                     // fermer = le plus près du coin
            bMin.transform.localPosition = new Vector3(x0 - sign * step, yy, 0f);
            // poignée de taille : SOUS la fenêtre, dans son aplomb (jamais à droite : elle tomberait sur l'écran voisin)
            bResize.transform.localPosition = new Vector3(hs.x - S * 0.5f, -hs.y - Gap - S * 0.5f, 0f);
        }

        // ---- actions
        public void Click(Ar6dofChromeButton.Kind kind)
        {
            if (kind == Ar6dofChromeButton.Kind.Minimize) { if (onMinimize != null) onMinimize(); return; }
            if (kind != Ar6dofChromeButton.Kind.Close) return;
            if (confirmClose != null && confirmClose() && !bClose.armed)
            {
                bClose.armed = true;                      // 1er clic : le bouton devient rouge ; 2e clic dans les 2,5 s : fermeture
                armedUntil = Time.unscaledTime + 2.5f;
                bClose.Refresh();
                return;
            }
            bClose.armed = false;
            if (onClose != null) onClose();
        }

        public void ResizeBegin(Vector3 hit)
        {
            if (resizeBegin != null) { resizeBegin(hit); return; }
            startRadius = Mathf.Max(0.05f, (hit - target.position).magnitude);
            startScale = target.localScale.x;
        }

        public void ResizeDrag(Ray ray)
        {
            if (resizeDrag != null) { resizeDrag(ray); return; }
            var plane = new Plane(target.forward, target.position);
            float t;
            if (!plane.Raycast(ray, out t)) return;
            float radius = (ray.GetPoint(t) - target.position).magnitude;
            float sc = Mathf.Clamp(startScale * radius / startRadius, baseScale * 0.5f, baseScale * 3f);
            target.localScale = Vector3.one * sc;
        }

        public void ResizeEnd() { if (resizeEnd != null) resizeEnd(); }
    }
}
