using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Réglages de l'affichage de la bouche sur les écrans du costume (enregistrés tout de suite, dans les préférences d'Unity).</summary>
    [Serializable]
    public class Ar6dofMouthDisplayConfig
    {
        public bool enabled;
        public int layout;             // 0 = face : un seul écran montre toute la bouche ; 1 = côtés : deux écrans (A = côté gauche du costume, B = côté droit), chacun la moitié
        public int style;              // 0 = dessin (bouche remplie, dents, langue), 1 = trait (juste le contour des lèvres)
        public int palette;            // voir Palettes
        public float size = 1f;        // taille de la bouche (hauteur)
        public float stretch = 1f;     // étirement horizontal : > 1 pour un museau long
        public float curve;            // -1 ... 1 : courbure de la bouche (écran incurvé, mâchoire en arc)
        public float hinge;            // 0 ... 1 : ouverture en coin (la bouche s'ouvre en V, fermée à la commissure comme une vraie mâchoire)
        public float thick = 0.5f;     // épaisseur des lèvres
        public float offsetX, offsetY; // décalage de la bouche (en demi-hauteurs d'écran)
        public bool teeth = true;
        public int displayA = 1, displayB = 2;   // numéro d'écran Unity (0 = écran principal, déjà pris par le casque)
        public bool flipA, flipB;      // miroir horizontal de chaque écran
        public int rotA, rotB;         // quarts de tour (0..3) de chaque écran, pour un écran monté de travers
    }

    /// <summary>
    /// Dessine la bouche suivie (Ar6dofMouthTracker, 45 paramètres) sur un ou plusieurs écrans : les écrans du costume, branchés au PC comme des moniteurs.
    /// Un dessin simple (lèvres, intérieur, dents, langue) construit chaque image à partir des paramètres, puis déformé selon la forme du costume
    /// (étirement du museau, courbure, ouverture en coin). RIEN ne tourne tant que l'affichage n'est pas activé (ni caméra, ni maillage, ni écran).
    /// Les écrans supplémentaires doivent être branchés AVANT de lancer l'application (Unity les active au démarrage de cette fonction).
    /// </summary>
    public class Ar6dofMouthDisplay : MonoBehaviour
    {
        public static Ar6dofMouthDisplay Instance;
        public Ar6dofMouthTracker tracker;

        const string PrefKey = "ar6dof_mouth_display";
        const int DrawLayer = 31;
        const int Samples = 40;

        public struct Palette { public string name; public Color bg, lip, inside, tongue, teeth; }
        public static readonly Palette[] Palettes =
        {
            new Palette { name = "Classique", bg = new Color(0.02f, 0.02f, 0.03f), lip = new Color(0.93f, 0.33f, 0.42f), inside = new Color(0.30f, 0.04f, 0.07f), tongue = new Color(0.95f, 0.45f, 0.52f), teeth = Color.white },
            new Palette { name = "Neon", bg = Color.black, lip = new Color(0.1f, 1f, 1f), inside = new Color(0f, 0.08f, 0.1f), tongue = new Color(1f, 0.2f, 0.8f), teeth = Color.white },
            new Palette { name = "Noir et blanc", bg = Color.black, lip = Color.white, inside = new Color(0.04f, 0.04f, 0.04f), tongue = new Color(0.55f, 0.55f, 0.55f), teeth = Color.white },
            new Palette { name = "Jaune", bg = Color.black, lip = new Color(1f, 0.9f, 0f), inside = new Color(0.1f, 0.08f, 0f), tongue = new Color(1f, 0.55f, 0f), teeth = new Color(1f, 1f, 0.85f) },
            new Palette { name = "Vert", bg = new Color(0f, 0.03f, 0f), lip = new Color(0.45f, 1f, 0.2f), inside = new Color(0.02f, 0.12f, 0.02f), tongue = new Color(1f, 0.4f, 0.5f), teeth = Color.white },
        };
        public static readonly string[] PresetNames = { "Plat (ecran de TV)", "Museau long (2 cotes)", "Ecran incurve" };

        public Ar6dofMouthDisplayConfig Config = new Ar6dofMouthDisplayConfig();

        // ---- sorties (un écran = une caméra orthographique qui ne voit que le dessin de la bouche)
        class Out
        {
            public GameObject go;
            public Camera cam;
            public Mesh mesh;
            public RenderTexture rt;     // aperçu du menu (sinon null : écran réel)
            public int half;             // 0 = toute la bouche, 1 = moitié gauche de la personne (écran A), 2 = moitié droite (écran B)
            public int display = -1;
            public bool isA;
            public float aspect = 16f / 9f;
        }
        readonly List<Out> outs = new List<Out>();
        int builtKey = -1;
        Material mat;
        float previewUntil = -1f;
        readonly float[] sh = new float[Ar6dofMouthShapes.Count];
        // courbes de la bouche, en « espace bouche » (X de -1 à 1, du côté droit de la personne au côté gauche, vu de face)
        readonly float[] cx = new float[Samples + 1], cu = new float[Samples + 1], cl = new float[Samples + 1], cg = new float[Samples + 1];
        float tongueX, tongueTop, tongueBottom, tongueW, teethH, lipW;
        // tampons du maillage (réutilisés : aucune allocation à chaque image)
        readonly List<Vector3> vv = new List<Vector3>(4096);
        readonly List<Color32> vc = new List<Color32>(4096);
        readonly List<int> vt = new List<int>(8192);
        float layerZ;

        public bool Enabled { get { return Config.enabled; } }
        /// <summary>Textures d'aperçu pour le menu (null si aucun aperçu demandé). B n'existe qu'avec la disposition « côtés ».</summary>
        public Texture PreviewA { get { return PreviewOut(true); } }
        public Texture PreviewB { get { return PreviewOut(false); } }

        public event Action Changed;

        void Awake()
        {
            Instance = this;
            try
            {
                string js = PlayerPrefs.GetString(PrefKey, "");
                if (!string.IsNullOrEmpty(js)) JsonUtility.FromJsonOverwrite(js, Config);
            }
            catch (Exception) { }
            Sanitize();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Flush();
            Teardown();
        }

        void Sanitize()
        {
            var c = Config;
            c.layout = Mathf.Clamp(c.layout, 0, 1);
            c.style = Mathf.Clamp(c.style, 0, 1);
            c.palette = Mathf.Clamp(c.palette, 0, Palettes.Length - 1);
            c.size = Mathf.Clamp(c.size, 0.3f, 1.6f);
            c.stretch = Mathf.Clamp(c.stretch, 0.5f, 3f);
            c.curve = Mathf.Clamp(c.curve, -1f, 1f);
            c.hinge = Mathf.Clamp01(c.hinge);
            c.thick = Mathf.Clamp01(c.thick);
            c.offsetX = Mathf.Clamp(c.offsetX, -1f, 1f);
            c.offsetY = Mathf.Clamp(c.offsetY, -1f, 1f);
            c.displayA = Mathf.Clamp(c.displayA, 0, 7);
            c.displayB = Mathf.Clamp(c.displayB, 0, 7);
            c.rotA = ((c.rotA % 4) + 4) % 4;
            c.rotB = ((c.rotB % 4) + 4) % 4;
        }

        float dirtyAt = -1f;
        bool askedTracker;

        /// <summary>À appeler après avoir modifié Config (le menu le fait) : corrige les valeurs, prévient les abonnés et enregistre (une demi-seconde après le dernier changement, pour ne pas écrire à chaque pas d'un curseur).</summary>
        public void Touch()
        {
            Sanitize();
            dirtyAt = Time.unscaledTime;
            if (Changed != null) Changed();
        }

        void Flush()
        {
            if (dirtyAt < 0f) return;
            dirtyAt = -1f;
            try { PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(Config)); PlayerPrefs.Save(); } catch (Exception) { }
        }

        void OnApplicationQuit() { Flush(); }

        /// <summary>Active / désactive l'affichage. L'activer allume aussi le mouth tracking (il faut des données à afficher).</summary>
        public void SetEnabled(bool on)
        {
            Config.enabled = on;
            if (on && tracker != null && !tracker.Enabled) tracker.SetEnabled(true);
            Touch();
        }

        /// <summary>Applique une forme de costume : 0 plat (TV), 1 museau long (deux écrans de côté), 2 écran incurvé.</summary>
        public void ApplyPreset(int p)
        {
            var c = Config;
            c.offsetX = 0f; c.offsetY = 0f; c.size = 1f; c.thick = 0.5f;
            if (p == 1) { c.layout = 1; c.stretch = 1f; c.hinge = 0.75f; c.curve = 0.15f; }
            else if (p == 2) { c.layout = 0; c.stretch = 1.2f; c.hinge = 0f; c.curve = 0.5f; }
            else { c.layout = 0; c.stretch = 1f; c.hinge = 0f; c.curve = 0f; }
            Touch();
        }

        /// <summary>Le menu demande un aperçu (à renouveler à chaque image tant qu'il est visible) : les aperçus s'arrêtent d'eux-mêmes sinon.</summary>
        public void RequestPreview() { previewUntil = Time.unscaledTime + 0.4f; }

        /// <summary>Résumé pour le menu : état de chaque écran de sortie.</summary>
        public string Describe()
        {
            if (!Config.enabled) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < outs.Count; i++)
            {
                var o = outs[i];
                if (o.rt != null) continue;
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(o.isA ? "A" : "B").Append(" : ").Append(Ar6dofLoc.F("ecran {0}", o.display));
            }
            string miss = MissingText();
            return (sb.Length > 0 ? sb.ToString() : "") + (miss.Length > 0 ? (sb.Length > 0 ? "   " : "") + miss : "");
        }

        string MissingText()
        {
            if (!Config.enabled) return "";
            string r = "";
            if (!DisplayOk(Config.displayA)) r += Ar6dofLoc.F("ecran {0} absent", Config.displayA);
            if (Config.layout == 1 && !DisplayOk(Config.displayB)) r += (r.Length > 0 ? ", " : "") + Ar6dofLoc.F("ecran {0} absent", Config.displayB);
            return r;
        }

        static bool DisplayOk(int idx) { return idx > 0 && idx < Display.displays.Length; }

        // ================================================================== boucle
        void Update()
        {
            if (dirtyAt >= 0f && Time.unscaledTime - dirtyAt > 0.5f) Flush();
            bool preview = Time.unscaledTime < previewUntil;
            bool real = Config.enabled;
            if (!real && !preview)
            {
                if (outs.Count > 0) Teardown();
                return;
            }
            if (real && !askedTracker && tracker != null)
            {
                askedTracker = true;   // l'affichage est actif : il lui faut des données (une seule fois : si tu coupes ensuite le suivi à la main, on ne le rallume pas)
                if (!tracker.Enabled) tracker.SetEnabled(true, false);
            }
            EnsureOutputs(real, preview);
            if (outs.Count == 0) return;

            // données : 45 paramètres (neutre tant que rien n'arrive)
            if (tracker != null && tracker.Enabled && tracker.Receiving) tracker.GetShapes(sh);
            else for (int i = 0; i < sh.Length; i++) sh[i] = 0f;
            ComputeCurves();

            var pal = Palettes[Config.palette];
            for (int i = 0; i < outs.Count; i++)
            {
                var o = outs[i];
                o.cam.backgroundColor = pal.bg;
                BuildMesh(o, pal);
                Graphics.DrawMesh(o.mesh, Matrix4x4.identity, mat, DrawLayer, o.cam);
            }
        }

        // ================================================================== sorties
        Texture PreviewOut(bool a)
        {
            for (int i = 0; i < outs.Count; i++) if (outs[i].rt != null && outs[i].isA == a) return outs[i].rt;
            return null;
        }

        void EnsureOutputs(bool real, bool preview)
        {
            int key = (real ? 1 : 0) | (preview ? 2 : 0) | (Config.layout << 2) | (Config.displayA << 3) | (Config.displayB << 6) | (Display.displays.Length << 9);
            if (key == builtKey) return;
            Teardown();
            builtKey = key;
            if (mat == null)
            {
                var sh2 = Shader.Find("Sprites/Default");
                if (sh2 == null) { UnityEngine.Debug.LogWarning("AR6DoF : shader Sprites/Default introuvable, bouche non affichee."); return; }
                mat = new Material(sh2) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (real)
            {
                if (Config.layout == 0) { if (DisplayOk(Config.displayA)) outs.Add(MakeOut(0, true, Config.displayA, null)); }
                else
                {
                    if (DisplayOk(Config.displayA)) outs.Add(MakeOut(1, true, Config.displayA, null));
                    if (DisplayOk(Config.displayB) && Config.displayB != Config.displayA) outs.Add(MakeOut(2, false, Config.displayB, null));
                }
            }
            if (preview)
            {
                if (Config.layout == 0) outs.Add(MakeOut(0, true, -1, new RenderTexture(480, 180, 16, RenderTextureFormat.ARGB32)));
                else
                {
                    outs.Add(MakeOut(1, true, -1, new RenderTexture(240, 180, 16, RenderTextureFormat.ARGB32)));
                    outs.Add(MakeOut(2, false, -1, new RenderTexture(240, 180, 16, RenderTextureFormat.ARGB32)));
                }
            }
        }

        Out MakeOut(int half, bool isA, int display, RenderTexture rt)
        {
            var o = new Out { half = half, isA = isA, display = display, rt = rt };
            o.go = new GameObject("Ar6dofMouthOut" + (rt != null ? "Preview" : "Screen"));
            o.go.transform.SetParent(transform, false);
            o.go.transform.localPosition = new Vector3(0f, 0f, -5f);
            o.cam = o.go.AddComponent<Camera>();
            o.cam.orthographic = true;
            o.cam.orthographicSize = 1f;
            o.cam.nearClipPlane = 0.1f;
            o.cam.farClipPlane = 10f;
            o.cam.clearFlags = CameraClearFlags.SolidColor;
            o.cam.cullingMask = 1 << DrawLayer;
            o.cam.stereoTargetEye = StereoTargetEyeMask.None;   // jamais dans le casque
            o.cam.allowMSAA = false;
            o.cam.allowHDR = false;
            o.cam.useOcclusionCulling = false;
            o.cam.depth = -40f;
            if (rt != null)
            {
                o.cam.targetTexture = rt;
                o.aspect = (float)rt.width / rt.height;
            }
            else
            {
                if (display > 0 && display < Display.displays.Length)
                {
                    try { Display.displays[display].Activate(); } catch (Exception) { }
                    var d = Display.displays[display];
                    if (d.systemHeight > 0) o.aspect = (float)d.systemWidth / d.systemHeight;
                }
                o.cam.targetDisplay = display;
            }
            o.mesh = new Mesh { name = "Ar6dofMouth", hideFlags = HideFlags.HideAndDontSave };
            o.mesh.MarkDynamic();
            return o;
        }

        void Teardown()
        {
            for (int i = 0; i < outs.Count; i++)
            {
                var o = outs[i];
                if (o.go != null) Destroy(o.go);
                if (o.mesh != null) Destroy(o.mesh);
                if (o.rt != null) { o.rt.Release(); Destroy(o.rt); }
            }
            outs.Clear();
            builtKey = -1;
        }

        // ================================================================== la bouche (espace bouche)
        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        void ComputeCurves()
        {
            float open = sh[Ar6dofMouthShapes.JawOpen];
            float funnel = sh[Ar6dofMouthShapes.MouthFunnel], pucker = sh[Ar6dofMouthShapes.MouthPucker];
            float close = sh[Ar6dofMouthShapes.MouthClose];
            float gapAmp = Mathf.Clamp01(open * (1f - 0.6f * close)) * (1f + 0.35f * funnel) * 0.95f;
            float hinge = Config.hinge;
            float shift = (sh[Ar6dofMouthShapes.MouthLeft] - sh[Ar6dofMouthShapes.MouthRight] + sh[Ar6dofMouthShapes.JawLeft] - sh[Ar6dofMouthShapes.JawRight]) * 0.12f;
            for (int i = 0; i <= Samples; i++)
            {
                float X = -1f + 2f * i / Samples;           // X > 0 : côté GAUCHE de la personne (vu de face, c'est à droite)
                float a = Mathf.Abs(X);
                float wl = Mathf.Clamp01(0.5f + X / 0.3f), wr = 1f - wl;
                float smile = wl * sh[Ar6dofMouthShapes.MouthSmileL] + wr * sh[Ar6dofMouthShapes.MouthSmileR];
                float frown = wl * sh[Ar6dofMouthShapes.MouthFrownL] + wr * sh[Ar6dofMouthShapes.MouthFrownR];
                float stretchS = wl * sh[Ar6dofMouthShapes.MouthStretchL] + wr * sh[Ar6dofMouthShapes.MouthStretchR];
                float upUp = wl * sh[Ar6dofMouthShapes.MouthUpperUpL] + wr * sh[Ar6dofMouthShapes.MouthUpperUpR];
                float lowDown = wl * sh[Ar6dofMouthShapes.MouthLowerDownL] + wr * sh[Ar6dofMouthShapes.MouthLowerDownR];
                float press = wl * sh[Ar6dofMouthShapes.MouthPressL] + wr * sh[Ar6dofMouthShapes.MouthPressR];
                float wf = Mathf.Clamp(1f + 0.22f * stretchS + 0.12f * smile - 0.38f * pucker - 0.22f * funnel, 0.35f, 1.4f);
                float round = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a));
                float wedge = 0.12f + 0.88f * (1f - a);
                float prof = Mathf.Lerp(round, wedge, hinge) * Sm(1f, 0.93f, a);
                float gap = gapAmp * prof * (1f - 0.5f * press);
                float lift = (smile - 0.9f * frown) * 0.32f * Mathf.Pow(a, 1.6f);
                float yc = lift;
                cx[i] = X * wf + shift;
                cu[i] = yc + gap * 0.38f + 0.12f * upUp * round;
                cl[i] = yc - gap * 0.62f - 0.14f * lowDown * round;
                cg[i] = cu[i] - cl[i];
            }
            lipW = 0.02f + 0.075f * Config.thick;
            float tOut = sh[Ar6dofMouthShapes.TongueOut];
            tongueX = (sh[Ar6dofMouthShapes.TongueLeft] - sh[Ar6dofMouthShapes.TongueRight]) * 0.25f + shift;
            float mid = Samples / 2;
            float gapMid = cg[(int)mid];
            tongueW = 0.30f * (1f - 0.35f * sh[Ar6dofMouthShapes.TongueSquish]);
            tongueTop = cl[(int)mid] + gapMid * 0.45f;
            tongueBottom = cl[(int)mid] - tOut * 0.5f - 0.02f;
            if (tOut < 0.12f && gapMid < 0.2f) tongueW = 0f;            // bouche presque fermée : pas de langue
            teethH = Mathf.Min(gapMid * 0.45f, 0.17f);
        }

        // ================================================================== maillage d'un écran
        float ox, oy, hw, sizeY, outScale; bool outFlip; int outRotQ; float outCurve;

        void BeginOut(Out o)
        {
            int rq = o.isA ? Config.rotA : Config.rotB;
            bool odd = (rq & 1) == 1;
            float A = Mathf.Max(0.2f, o.aspect);
            float la = odd ? 1f / A : A;           // largeur logique de l'écran (en demi-hauteurs) avant la rotation
            hw = la * 0.92f;
            sizeY = Config.size * 0.9f;
            outFlip = o.isA ? Config.flipA : Config.flipB;
            outRotQ = rq;
            outScale = odd ? A : 1f;
            ox = Config.offsetX * la * 0.5f;
            oy = Config.offsetY * 0.5f;
            outCurve = Config.curve;
        }

        // point de l'espace bouche -> coordonnées de l'écran (demi-hauteur 1)
        Vector2 MapPt(Out o, float mx, float my)
        {
            float lx;
            if (o.half == 0) lx = mx * hw * Config.size * Config.stretch;
            else if (o.half == 1) lx = (mx * 2f - 1f) * hw * Config.stretch;
            else lx = (mx * 2f + 1f) * hw * Config.stretch;
            float ly = my * sizeY;
            lx += ox; ly += oy;
            ly += outCurve * 0.55f * (lx / hw) * (lx / hw);
            if (outFlip) lx = -lx;
            lx *= outScale; ly *= outScale;
            switch (outRotQ)
            {
                case 1: return new Vector2(-ly, lx);
                case 2: return new Vector2(-lx, -ly);
                case 3: return new Vector2(ly, -lx);
                default: return new Vector2(lx, ly);
            }
        }

        void V(Vector2 p, Color32 c) { vv.Add(new Vector3(p.x, p.y, layerZ)); vc.Add(c); }

        void Tri(int a, int b, int c) { vt.Add(a); vt.Add(b); vt.Add(c); }

        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
        {
            int i = vv.Count;
            V(a, col); V(b, col); V(c, col); V(d, col);
            Tri(i, i + 1, i + 2); Tri(i, i + 2, i + 3);
        }

        void NextLayer() { layerZ -= 0.01f; }

        // trait d'épaisseur w le long d'une ligne brisée
        void Stroke(List<Vector2> pts, float w, Color32 col)
        {
            int n = pts.Count;
            if (n < 2) return;
            int baseIdx = vv.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = i == 0 ? pts[1] - pts[0] : i == n - 1 ? pts[n - 1] - pts[n - 2] : pts[i + 1] - pts[i - 1];
                if (d.sqrMagnitude < 1e-9f) d = Vector2.right;
                d.Normalize();
                Vector2 nrm = new Vector2(-d.y, d.x) * (w * 0.5f);
                V(pts[i] + nrm, col);
                V(pts[i] - nrm, col);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = baseIdx + i * 2;
                Tri(a, a + 1, a + 2); Tri(a + 1, a + 3, a + 2);
            }
        }

        readonly List<Vector2> pU = new List<Vector2>(Samples + 1), pL = new List<Vector2>(Samples + 1);

        void BuildMesh(Out o, Palette pal)
        {
            BeginOut(o);
            vv.Clear(); vc.Clear(); vt.Clear();
            layerZ = 0f;
            pU.Clear(); pL.Clear();
            // moitié affichée : on garde les échantillons dont X est dans la moitié (avec un peu de marge pour ne pas laisser de trou au milieu)
            for (int i = 0; i <= Samples; i++)
            {
                float X = -1f + 2f * i / Samples;
                if (o.half == 1 && X < -0.06f) continue;
                if (o.half == 2 && X > 0.06f) continue;
                pU.Add(MapPt(o, cx[i], cu[i]));
                pL.Add(MapPt(o, cx[i], cl[i]));
            }
            Color32 lip = pal.lip, inside = pal.inside, tongue = pal.tongue, teeth = pal.teeth;
            bool draw = Config.style == 0;
            float w = lipW * sizeY * (o.half == 0 ? 1f : 1f);
            int n = pU.Count;
            if (draw && n >= 2)
            {
                // intérieur de la bouche
                for (int i = 0; i < n - 1; i++) Quad(pL[i], pU[i], pU[i + 1], pL[i + 1], inside);
                NextLayer();
            }
            // langue : ellipse (sous les dents et les lèvres)
            if (tongueW > 0f && (draw || sh[Ar6dofMouthShapes.TongueOut] > 0.25f))
            {
                const int seg = 20;
                float tcx = tongueX, tcy = (tongueTop + tongueBottom) * 0.5f, rx = tongueW, ry = Mathf.Max(0.04f, (tongueTop - tongueBottom) * 0.5f);
                int bi = vv.Count;
                V(MapPt(o, tcx, tcy), tongue);
                for (int k = 0; k <= seg; k++)
                {
                    float ang = k * Mathf.PI * 2f / seg;
                    V(MapPt(o, tcx + Mathf.Cos(ang) * rx, tcy + Mathf.Sin(ang) * ry), tongue);
                }
                for (int k = 1; k <= seg; k++) Tri(bi, bi + k, bi + k + 1);
                NextLayer();
            }
            // dents du haut
            if (draw && n >= 2 && Config.teeth && teethH > 0.02f)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    Vector2 a = pU[i], b = pU[i + 1];
                    Vector2 da = a + (pL[i] - a).normalized * Mathf.Min((pU[i] - pL[i]).magnitude, teethH * sizeY) * 0.9f;
                    Vector2 db = b + (pL[i + 1] - b).normalized * Mathf.Min((pU[i + 1] - pL[i + 1]).magnitude, teethH * sizeY) * 0.9f;
                    Quad(da, a, b, db, teeth);
                }
                NextLayer();
            }
            // lèvres (par-dessus)
            if (n >= 2)
            {
                Stroke(pU, w, lip);
                Stroke(pL, w, lip);
            }
            // arrondis aux commissures et fermeture : petit disque à chaque extrémité de chaque ligne
            if (n >= 2) { Cap(pU[0], w, lip); Cap(pU[n - 1], w, lip); Cap(pL[0], w, lip); Cap(pL[n - 1], w, lip); }

            o.mesh.Clear();
            o.mesh.SetVertices(vv);
            o.mesh.SetColors(vc);
            o.mesh.SetTriangles(vt, 0, false);
            o.mesh.bounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f));
        }

        void Cap(Vector2 c, float w, Color32 col)
        {
            const int seg = 10;
            int bi = vv.Count;
            V(c, col);
            for (int k = 0; k <= seg; k++)
            {
                float ang = k * Mathf.PI * 2f / seg;
                V(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (w * 0.5f), col);
            }
            for (int k = 1; k <= seg; k++) Tri(bi, bi + k, bi + k + 1);
        }
    }
}
