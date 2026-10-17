using System;
using System.IO;
using UnityEngine;

namespace AR6DoF
{
    [Serializable]
    public class Ar6dofEyeSettings
    {
        public float panX, panY;               // décalage de l'image sur l'écran (0.1 = 5 % de la largeur d'un oeil)
        public float lensCenterX, lensCenterY; // centre optique de la lentille de l'afficheur (-1..1)
        public float k1, k2, k3;               // distorsion de la lentille de l'afficheur (pré-déformation)
        public bool mirrorX, mirrorY;          // miroir du flux de la caméra
        public int rotateQuarterTurns;         // rotation du flux, en quarts de tour horaires (0..3)
    }

    public enum Ar6dofStereoLayout { SideBySide, TopBottom }

    [Serializable]
    public class Ar6dofPassthroughSettings
    {
        public Ar6dofEyeSettings left = new Ar6dofEyeSettings();
        public Ar6dofEyeSettings right = new Ar6dofEyeSettings();
        public float displayFovDeg = 90f;      // champ de vision affiché par oeil (carré)
        public float ipdShift = 0f;            // écarte (+) ou rapproche (-) les deux images (IPD logiciel)
        public bool linkEyes = true;           // l'oeil droit est le miroir de l'oeil gauche
        public bool swapEyes = false;          // échange gauche / droite
        public Ar6dofStereoLayout layout = Ar6dofStereoLayout.SideBySide;
        public bool useCalibration = true;     // corrige la distorsion de la caméra avec camera_model.json
        public bool centerOnImage = true;      // centre l'affichage sur le milieu de l'image (et non sur l'axe optique)
        public float fallbackHorizontalFovDeg = 100f;  // sans calibration : champ horizontal supposé de la caméra
        public bool flipComposite = false;     // à cocher si le rendu est à l'envers
        public bool reproject = true;          // reprojection de rotation : l'image suit la tête à la cadence d'Unity, même si la caméra est plus lente
        public float captureDelayMs = 40f;     // délai entre la prise de vue et l'écriture de l'image par ar6dof_probe (à régler : tourne la tête vite)
        public bool clampEdges = false;        // étire les bords de l'image au lieu de les laisser noirs
    }

    [Serializable] class EyeModelJson { public double[] K; public double[] D; public double[] R; }
    [Serializable]
    class CameraModelJson
    {
        public int version, calib_width, calib_height;
        public double baseline_m, right_offset_x_m, rect_f, rect_cx, rect_cy;
        public EyeModelJson left, right;
    }

    /// <summary>
    /// Passthrough en vidéo à faible latence + composition de la scène 3D, pour un afficheur à deux yeux côte à côte
    /// (ex. 2880x1440). À placer sur le "Rig" (avec Ar6dofHeadRig). Désactive la Main Camera.
    /// </summary>
    public class Ar6dofPassthrough : MonoBehaviour
    {
        [Header("Entrées")]
        public string sharedMemoryName = "Local\\ar6dof_frame";
        [Tooltip("camera_model.json exporté par ar6dof_probe (glisse-le ici)")]
        public TextAsset cameraModelJson;
        [Tooltip("ou chemin complet du fichier")]
        public string cameraModelPath = "";
        public Shader compositeShader;
        public Ar6dofReceiver receiver;      // vide : trouvé automatiquement
        public Ar6dofHeadRig headRig;        // vide : trouvé automatiquement
        [Header("Rendu")]
        public int eyeRenderSize = 1440;
        [Tooltip("La scène 3D est rendue plus large que ce qui est affiché : le décalage IPD ne rogne plus le menu")]
        public float sceneOverscan = 1.3f;
        public int msaa = 2;
        public KeyCode toggleUiKey = KeyCode.F1;
        public Ar6dofPassthroughSettings settings = new Ar6dofPassthroughSettings();

        public double FrameAgeMs { get; private set; }
        /// <summary>Rotation compensée par la reprojection pour l'image affichée (degrés).</summary>
        public double ReprojDeg { get; private set; }
        public bool HasFrames { get; private set; }
        public bool HasCalibration => model != null;
        /// <summary>Transform de ta vue réelle (caméra de l'oeil gauche, orientée vers le centre affiché). Null tant qu'elle n'existe pas.</summary>
        public Transform HeadView => eyeCams[0] != null ? eyeCams[0].transform : null;

        readonly Ar6dofFrameReader reader = new Ar6dofFrameReader();
        Texture2D camTex;
        RenderTexture rtL, rtR;
        readonly Camera[] eyeCams = new Camera[2];
        Camera compositeCam;
        Material mat;
        CameraModelJson model;
        readonly Vector4[] U = new Vector4[20];
        float nextOpenTry, fpsTimer;
        double frameWriteQpc;
        static readonly double[] RCB = { 0, -1, 0, 0, 0, -1, 1, 0, 0 };   // casque (X avant, Y gauche, Z haut) -> caméra (x droite, y bas, z avant)
        int uploads, uploadsPerSec;
        bool showUi;
        Vector2 scroll;
        int layer;

        string SettingsPath => Path.Combine(Application.persistentDataPath, "ar6dof_passthrough.json");

        // ------------------------------------------------------------------ démarrage
        void Start()
        {
            Application.runInBackground = true;
            if (receiver == null) receiver = FindFirstObjectByType<Ar6dofReceiver>();
            if (headRig == null) headRig = FindFirstObjectByType<Ar6dofHeadRig>();
            LoadModel();
            LoadSettings();
            layer = LayerMask.NameToLayer("TransparentFX");
            if (layer < 0) layer = 1;
            BuildEyes();
            BuildComposite();
            foreach (var c in Camera.allCameras)
            {
                if (c == eyeCams[0] || c == eyeCams[1] || c == compositeCam) continue;
                if (c.targetTexture == null && c.enabled)
                {
                    Debug.LogWarning("AR6DoF : caméra '" + c.name + "' désactivée (le casque utilise ses propres caméras).");
                    c.enabled = false;
                }
            }
        }

        void LoadModel()
        {
            string json = null;
            if (cameraModelJson != null) json = cameraModelJson.text;
            else if (!string.IsNullOrEmpty(cameraModelPath) && File.Exists(cameraModelPath)) json = File.ReadAllText(cameraModelPath);
            if (json == null)
            {
                Debug.LogWarning("AR6DoF : pas de camera_model.json : l'image sera affichée sans correction de distorsion.");
                return;
            }
            try { model = JsonUtility.FromJson<CameraModelJson>(json); }
            catch (Exception e) { Debug.LogError("AR6DoF : camera_model.json illisible : " + e.Message); model = null; }
            if (model == null || !ValidEye(model.left) || !ValidEye(model.right) || model.calib_width <= 0 || model.calib_height <= 0)
            {
                Debug.LogError("AR6DoF : camera_model.json incomplet.");
                model = null;
                return;
            }
            if (model.right_offset_x_m < 0)
                Debug.LogWarning("AR6DoF : d'après la calibration, les yeux de la caméra semblent inversés : coche 'Swap Eyes'.");
        }

        static bool ValidEye(EyeModelJson e) =>
            e != null && e.K != null && e.K.Length == 9 && e.D != null && e.D.Length >= 8 && e.R != null && e.R.Length == 9;

        RenderTexture MakeRt(string n)
        {
            int size = Mathf.RoundToInt(eyeRenderSize * Mathf.Max(1f, sceneOverscan));
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = Mathf.Max(1, msaa);
            rt.name = n;
            rt.Create();
            return rt;
        }

        Camera MakeEye(string n, RenderTexture rt)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var c = go.AddComponent<Camera>();
            c.targetTexture = rt;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent : on voit la caméra à travers
            c.nearClipPlane = 0.05f;
            c.farClipPlane = 100f;
            c.cullingMask = ~(1 << layer);
            c.depth = 0;
            c.allowHDR = false;
            return c;
        }

        void BuildEyes()
        {
            rtL = MakeRt("AR6DoF_EyeLeft");
            rtR = MakeRt("AR6DoF_EyeRight");
            eyeCams[0] = MakeEye("EyeLeft", rtL);
            eyeCams[1] = MakeEye("EyeRight", rtR);
        }

        void BuildComposite()
        {
            if (compositeShader == null) compositeShader = Shader.Find("AR6DoF/Composite");
            if (compositeShader == null)
            {
                Debug.LogError("AR6DoF : shader AR6DoF/Composite introuvable. Glisse Ar6dofComposite.shader dans le champ 'Composite Shader'.");
                enabled = false;
                return;
            }
            mat = new Material(compositeShader);
            var root = new GameObject("Ar6dofComposite");
            root.transform.position = new Vector3(0f, -5000f, 0f);
            root.layer = layer;

            var camGo = new GameObject("CompositeCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -5f);
            camGo.layer = layer;
            compositeCam = camGo.AddComponent<Camera>();
            compositeCam.orthographic = true;
            compositeCam.orthographicSize = 1f;
            compositeCam.nearClipPlane = 0.1f;
            compositeCam.farClipPlane = 10f;
            compositeCam.clearFlags = CameraClearFlags.SolidColor;
            compositeCam.backgroundColor = Color.black;
            compositeCam.cullingMask = 1 << layer;
            compositeCam.depth = 100f;                      // dessiné en dernier, vers l'écran
            compositeCam.allowHDR = false;

            var quad = new GameObject("CompositeQuad");
            quad.transform.SetParent(root.transform, false);
            quad.layer = layer;
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));   // jamais éliminé : le shader ignore les matrices
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = quad.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void OnEnable() { Application.onBeforeRender += OnBeforeRenderUpdate; }
        void OnDisable() { Application.onBeforeRender -= OnBeforeRenderUpdate; }

        void OnDestroy()
        {
            reader.Dispose();
            if (rtL != null) rtL.Release();
            if (rtR != null) rtR.Release();
        }

        // ------------------------------------------------------------------ boucle
        void Update()
        {
            if (Ar6dofInput.Down(toggleUiKey)) { showUi = !showUi; Ar6dofUi.Visible = showUi; }
            if (!reader.IsOpen && Time.unscaledTime >= nextOpenTry)
            {
                nextOpenTry = Time.unscaledTime + 0.5f;
                reader.Open(sharedMemoryName);
            }
            if (reader.IsOpen)
            {
                if (camTex == null)
                {
                    if ((reader.Width * 3) % 4 != 0) { Debug.LogError("AR6DoF : largeur d'image non multiple de 4."); enabled = false; return; }
                    camTex = new Texture2D(reader.Width, reader.Height, TextureFormat.RGB24, false, false);
                    camTex.wrapMode = TextureWrapMode.Clamp;
                    camTex.filterMode = FilterMode.Bilinear;
                }
                if (reader.TryCopyLatest(camTex, out double age)) { FrameAgeMs = age; HasFrames = true; uploads++; frameWriteQpc = reader.LastWriteQpc; }
            }
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 1f) { uploadsPerSec = uploads; uploads = 0; fpsTimer = 0f; }
        }

        // Juste avant le rendu (après Ar6dofHeadRig, qui a l'ordre 0) : paramètres du shader et caméras de scène.
        [BeforeRenderOrder(10)]
        void OnBeforeRenderUpdate() { UpdateRendering(); }

        Ar6dofEyeSettings EyeSettingsFor(int disp)
        {
            if (disp == 0 || !settings.linkEyes) return disp == 0 ? settings.left : settings.right;
            var l = settings.left;   // oeil droit = miroir de l'oeil gauche
            return new Ar6dofEyeSettings
            {
                panX = -l.panX, panY = l.panY, lensCenterX = -l.lensCenterX, lensCenterY = l.lensCenterY,
                k1 = l.k1, k2 = l.k2, k3 = l.k3, mirrorX = l.mirrorX, mirrorY = l.mirrorY, rotateQuarterTurns = l.rotateQuarterTurns
            };
        }

        void UpdateRendering()
        {
            if (mat == null) return;
            int W = camTex != null ? camTex.width : 1600, H = camTex != null ? camTex.height : 600;

            // Reprojection de rotation : de combien la tête a-t-elle tourné depuis la prise de vue de l'image affichée ?
            // dR = R_cam * (R_casque(capture)^T * R_casque(maintenant)) * R_cam^T  (voir tests/passthrough_ref.py : delta_cam)
            double[] dR = null;
            if (settings.reproject && HasFrames && receiver != null && headRig != null && headRig.RenderRotationG != null
                && receiver.TryGetRotationG(frameWriteQpc - settings.captureDelayMs * 0.001, out double[] Rcap))
            {
                double[] delta = Ar6dofMath.Mul3(Ar6dofMath.Transpose3(Rcap), headRig.RenderRotationG);
                dR = Ar6dofMath.Mul3(Ar6dofMath.Mul3(RCB, delta), Ar6dofMath.Transpose3(RCB));
            }
            ReprojDeg = dR == null ? 0.0 : Math.Acos(Math.Max(-1.0, Math.Min(1.0, (dR[0] + dR[4] + dR[8] - 1.0) * 0.5))) * 180.0 / Math.PI;
            double tanHalf = Math.Tan(settings.displayFovDeg * 0.5 * Math.PI / 180.0);
            float sceneFov = (float)(2.0 * Math.Atan(tanHalf * Mathf.Max(1f, sceneOverscan)) * 180.0 / Math.PI);

            for (int disp = 0; disp < 2; disp++)
            {
                int feed = settings.swapEyes ? 1 - disp : disp;     // quelle moitié du flux va sur quel oeil
                Vector3 axisU = FillEye(disp, feed, EyeSettingsFor(disp), W, H, dR);
                var cam = eyeCams[disp];
                cam.fieldOfView = sceneFov;   // plus large que l'affichage (marge)
                float offX = feed == 0 ? 0f : (model != null ? (float)model.right_offset_x_m : 0.063f);
                cam.transform.localPosition = new Vector3(offX, 0f, 0f);        // la pose du Rig est celle de l'oeil gauche de la caméra
                cam.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, axisU);
            }
            mat.SetVectorArray("_U", U);
            mat.SetVector("_TexSize", new Vector4(W, H, 0f, 0f));
            if (camTex != null) mat.SetTexture("_Cam", camTex);
            mat.SetTexture("_SceneL", rtL);
            mat.SetTexture("_SceneR", rtR);
            mat.SetFloat("_Flip", settings.flipComposite ? -1f : 1f);
            mat.SetVector("_Misc", new Vector4(Mathf.Max(1f, sceneOverscan), settings.clampEdges ? 1f : 0f, 0f, 0f));
        }

        // Assemble les paramètres d'un oeil pour le shader (même calcul que `assemble` dans tests/passthrough_ref.py).
        // Retourne l'axe de vue (repère Unity) pour orienter la caméra de scène de la même façon.
        Vector3 FillEye(int disp, int feed, Ar6dofEyeSettings es, int W, int H, double[] dR)
        {
            int Ws, Hs, x0 = 0, y0 = 0;
            if (settings.layout == Ar6dofStereoLayout.SideBySide) { Ws = W / 2; Hs = H; x0 = feed * Ws; }
            else { Ws = W; Hs = H / 2; y0 = feed * Hs; }
            int quarter = ((es.rotateQuarterTurns % 4) + 4) % 4;
            int Wc = (quarter % 2 == 0) ? Ws : Hs;
            int Hc = (quarter % 2 == 0) ? Hs : Ws;

            double[] K, D, R;
            int calW, calH;
            double rectF, rectCx, rectCy;
            EyeModelJson em = (model != null && settings.useCalibration) ? (feed == 0 ? model.left : model.right) : null;
            if (em != null)
            {
                K = em.K; D = em.D; R = em.R;
                calW = model.calib_width; calH = model.calib_height;
                rectF = model.rect_f; rectCx = model.rect_cx; rectCy = model.rect_cy;
            }
            else
            {
                double f = (Wc / 2.0) / Math.Tan(settings.fallbackHorizontalFovDeg * 0.5 * Math.PI / 180.0);
                K = new[] { f, 0, (Wc - 1) / 2.0, 0, f, (Hc - 1) / 2.0, 0, 0, 1 };
                D = new double[8];
                R = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
                calW = Wc; calH = Hc;
                rectF = f; rectCx = (Wc - 1) / 2.0; rectCy = (Hc - 1) / 2.0;
            }
            double sx = (double)Wc / calW, sy = (double)Hc / calH;
            double fx = K[0] * sx, fy = K[4] * sy;
            double cx = (K[2] + 0.5) * sx - 0.5, cy = (K[5] + 0.5) * sy - 0.5;
            double fP = rectF * sx, cxP = (rectCx + 0.5) * sx - 0.5, cyP = (rectCy + 0.5) * sy - 0.5;
            double tx = 0, ty = 0;
            if (settings.centerOnImage) { tx = ((Wc - 1) / 2.0 - cxP) / fP; ty = ((Hc - 1) / 2.0 - cyP) / fP; }
            double an = Math.Sqrt(tx * tx + ty * ty + 1.0);
            double[] a = { tx / an, ty / an, 1.0 / an };
            double[] Rv = RotFromTo(new[] { 0.0, 0.0, 1.0 }, a);
            // M = R^T * dR * Rv  (rayon de la vue -> repère brut de la caméra, à l'instant de la prise de vue)
            double[] RvT = dR != null ? Ar6dofMath.Mul3(dR, Rv) : Rv;
            double[] M = Ar6dofMath.Mul3(Ar6dofMath.Transpose3(R), RvT);
            double tanH = Math.Tan(settings.displayFovDeg * 0.5 * Math.PI / 180.0);
            float panX = es.panX + (disp == 0 ? -settings.ipdShift : settings.ipdShift);

            int b = disp * 10;
            U[b + 0] = new Vector4(panX, es.panY, es.lensCenterX, es.lensCenterY);
            U[b + 1] = new Vector4(es.k1, es.k2, es.k3, (float)tanH);
            U[b + 2] = new Vector4((float)fx, (float)fy, (float)cx, (float)cy);
            U[b + 3] = new Vector4((float)D[0], (float)D[1], (float)D[2], (float)D[3]);
            U[b + 4] = new Vector4((float)D[4], (float)D[5], (float)D[6], (float)D[7]);
            U[b + 5] = new Vector4((float)M[0], (float)M[1], (float)M[2], 0f);
            U[b + 6] = new Vector4((float)M[3], (float)M[4], (float)M[5], 0f);
            U[b + 7] = new Vector4((float)M[6], (float)M[7], (float)M[8], 0f);
            U[b + 8] = new Vector4(Wc, Hc, es.mirrorX ? 1f : 0f, es.mirrorY ? 1f : 0f);
            U[b + 9] = new Vector4(quarter, x0, y0, 0f);
            // axe de vue : repère caméra (x droite, y bas, z avant) -> Unity (x droite, y haut, z avant)
            return new Vector3((float)a[0], (float)-a[1], (float)a[2]);
        }

        static double[] Mul3(double[] a, double[] b)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
            return r;
        }

        // Rotation minimale qui amène le vecteur unitaire a sur le vecteur unitaire b.
        static double[] RotFromTo(double[] a, double[] b)
        {
            double vx = a[1] * b[2] - a[2] * b[1], vy = a[2] * b[0] - a[0] * b[2], vz = a[0] * b[1] - a[1] * b[0];
            double c = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
            double s = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            var R = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            if (s < 1e-12) return R;
            double kx = vx / s, ky = vy / s, kz = vz / s;
            double th = Math.Atan2(s, c), sn = Math.Sin(th), cs = 1.0 - Math.Cos(th);
            double[] Km = { 0, -kz, ky, kz, 0, -kx, -ky, kx, 0 };
            double[] K2 = Mul3(Km, Km);
            for (int i = 0; i < 9; i++) R[i] += sn * Km[i] + cs * K2[i];
            return R;
        }

        // ------------------------------------------------------------------ réglages (F1)
        void LoadSettings()
        {
            try { if (File.Exists(SettingsPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(SettingsPath), settings); }
            catch (Exception e) { Debug.LogWarning("AR6DoF : réglages illisibles : " + e.Message); }
        }

        void SaveSettings()
        {
            try { File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true)); }
            catch (Exception e) { Debug.LogWarning("AR6DoF : sauvegarde impossible : " + e.Message); }
        }

        static float Slider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + "  " + v.ToString("0.000"), GUILayout.Width(210));
            v = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return v;
        }

        void OnGUI()
        {
            string info = reader.IsOpen
                ? "Passthrough : " + reader.Width + "x" + reader.Height + " | " + uploadsPerSec + " img/s | age de l'image " + FrameAgeMs.ToString("0.0") + " ms | reprojection " + ReprojDeg.ToString("0.0") + " deg"
                : "Passthrough : en attente d'ar6dof_probe (partage d'image active ?)";
            info += HasCalibration ? " | calibration OK" : " | SANS calibration";
            GUI.Label(new Rect(10, 30, 1400, 24), info + "   (" + toggleUiKey + " : reglages)");
            if (!showUi) return;

            GUILayout.BeginArea(new Rect(10, 60, 430, Screen.height - 80), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            var s = settings;
            s.displayFovDeg = Slider("FOV affiche (deg)", s.displayFovDeg, 40f, 130f);
            s.ipdShift = Slider("IPD logiciel (ecarte +)", s.ipdShift, -0.4f, 0.4f);
            s.swapEyes = GUILayout.Toggle(s.swapEyes, "Echanger gauche / droite");
            s.linkEyes = GUILayout.Toggle(s.linkEyes, "Yeux lies (droit = miroir du gauche)");
            s.useCalibration = GUILayout.Toggle(s.useCalibration, "Corriger la distorsion de la camera (calibration)");
            s.centerOnImage = GUILayout.Toggle(s.centerOnImage, "Centrer sur l'image (pas sur l'axe optique)");
            s.flipComposite = GUILayout.Toggle(s.flipComposite, "Inverser verticalement le rendu (si a l'envers)");
            s.reproject = GUILayout.Toggle(s.reproject, "Reprojection de rotation (l'image suit la tete a la cadence d'Unity)");
            s.captureDelayMs = Slider("delai capture->ecriture (ms)", s.captureDelayMs, 0f, 120f);
            s.clampEdges = GUILayout.Toggle(s.clampEdges, "Etirer les bords au lieu du noir");
            GUILayout.Label("Disposition du flux : " + s.layout + (s.layout == Ar6dofStereoLayout.SideBySide ? "" : " (haut = oeil gauche)"));
            if (GUILayout.Button("Basculer cote a cote / haut-bas"))
                s.layout = s.layout == Ar6dofStereoLayout.SideBySide ? Ar6dofStereoLayout.TopBottom : Ar6dofStereoLayout.SideBySide;

            EyeUi("Oeil gauche" + (s.linkEyes ? " (regle aussi le droit)" : ""), s.left);
            if (!s.linkEyes) EyeUi("Oeil droit", s.right);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sauver")) SaveSettings();
            if (GUILayout.Button("Recharger")) LoadSettings();
            if (GUILayout.Button("Tout remettre a zero")) { var f = s.fallbackHorizontalFovDeg; settings = new Ar6dofPassthroughSettings { fallbackHorizontalFovDeg = f }; }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static void EyeUi(string title, Ar6dofEyeSettings e)
        {
            GUILayout.Space(6);
            GUILayout.Label("-- " + title);
            e.panX = Slider("decalage X", e.panX, -0.5f, 0.5f);
            e.panY = Slider("decalage Y", e.panY, -0.5f, 0.5f);
            e.k1 = Slider("lentille k1", e.k1, -0.6f, 0.6f);
            e.k2 = Slider("lentille k2", e.k2, -0.6f, 0.6f);
            e.k3 = Slider("lentille k3", e.k3, -0.6f, 0.6f);
            e.lensCenterX = Slider("centre lentille X", e.lensCenterX, -0.3f, 0.3f);
            e.lensCenterY = Slider("centre lentille Y", e.lensCenterY, -0.3f, 0.3f);
            GUILayout.BeginHorizontal();
            e.mirrorX = GUILayout.Toggle(e.mirrorX, "miroir X");
            e.mirrorY = GUILayout.Toggle(e.mirrorY, "miroir Y");
            if (GUILayout.Button("rotation +90 (" + (e.rotateQuarterTurns * 90) + ")")) e.rotateQuarterTurns = (e.rotateQuarterTurns + 1) % 4;
            GUILayout.EndHorizontal();
        }
    }
}
