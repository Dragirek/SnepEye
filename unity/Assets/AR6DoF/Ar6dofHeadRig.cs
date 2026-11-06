using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// À placer sur le parent des caméras ("Rig"). Applique la pose 6DoF reçue :
    ///  - y = 0 est le SOL (détecté par le tracker, ou hauteur d'yeux supposée sinon)
    ///  - la pose est prédite jusqu'à l'instant du rendu avec les vitesses reçues (la rotation est mise à jour
    ///    à la cadence de l'IMU, ~200 Hz, pas à celle de la caméra)
    ///  - touche de recentrage : l'origine et l'avant sont ramenés à la position/direction actuelles
    /// </summary>
    public class Ar6dofHeadRig : MonoBehaviour
    {
        public Ar6dofReceiver receiver;
        [Header("Sol")]
        public bool useDetectedFloor = true;
        [Tooltip("Hauteur des yeux au-dessus du sol si le tracker n'a pas détecté le sol")]
        public float assumedEyeHeight = 1.65f;
        public const float StandingEyeHeight = 1.65f, SeatedEyeHeight = 1.2f;
        [Header("Prédiction")]
        public bool predictToRenderTime = true;
        public float maxPredictSeconds = 0.1f;
        [Header("Recentrage")]
        public bool recenterOnFirstPose = true;
        public KeyCode recenterKey = KeyCode.R;
        [Header("Connexion")]
        public float staleSeconds = 0.5f;

        public bool HasPose { get; private set; }
        float startT = -1f;
        /// <summary>true des qu'une pose est arrivee, ou apres 2,5 s sans rien : l'interface se construit quand meme (suivi arrete = tu vois quand meme le dock et les ecrans).</summary>
        public bool Ready
        {
            get
            {
                if (HasPose) return true;
                if (startT < 0f) startT = Time.unscaledTime;
                return Time.unscaledTime - startT > 2.5f;
            }
        }
        public bool TrackingOk { get; private set; }
        public bool ConnectionLost { get; private set; }
        public bool FloorDetected { get; private set; }
        /// <summary>Sol fige (bouton « Verrouiller le sol » ou « Sol ici ») : y = 0 ne bouge plus, quelle que soit la source.</summary>
        public bool FloorLocked { get { return floorLocked; } }
        /// <summary>0 = suppose (hauteur des yeux reglee), 1 = detecte par le suivi, 2 = verrouille.</summary>
        public int FloorMode { get { return floorLocked ? 2 : (FloorDetected ? 1 : 0); } }
        /// <summary>Hauteur des yeux au-dessus du sol (m).</summary>
        public float HeadHeight { get; private set; }
        /// <summary>Position de la tête relative à l'origine, SANS l'échelle des déplacements (sert à la mesurer : x, hauteur au-dessus du sol, z).</summary>
        public Vector3 RawPosition { get; private set; }
        /// <summary>Position réellement donnée au Rig dans Unity (avec l'échelle des déplacements) : sert à comparer avec RawPosition quand on mesure.</summary>
        public Vector3 CameraStep { get; private set; }
        /// <summary>Orientation casque -> monde (repère G, matrice 3x3 en ligne) prédite à l'instant du rendu. Sert à la reprojection de l'image.</summary>
        public double[] RenderRotationG { get; private set; }

        bool wantRecenter, haveOrigin;
        Vector3 originU;
        Quaternion yawFix = Quaternion.identity;
        float floorSmoothed;
        bool floorInit, haveAssumed, floorLocked, wantYaw;
        float lastPosZ;
        float assumedFloorG;

        public void Recenter() { wantRecenter = true; }

        /// <summary>Mode assis : sans sol detecte, la hauteur des yeux supposee passe de debout (1,65 m) a assis (1,2 m) ; le sol est recalcule a partir de la tete maintenant.</summary>
        /// <summary>Hauteur des yeux supposee (m) selon le mode et le reglage « Hauteur de tes yeux ».</summary>
        public static float ConfiguredEyeHeight(bool seated)
        {
            var d = Ar6dofSettings.Current;
            return Mathf.Clamp((seated ? d.eyeHeightSeatedCm : d.eyeHeightCm) * 0.01f, 0.4f, 2.4f);
        }

        /// <summary>Verrouille / deverrouille le sol : verrouille, y = 0 reste exactement ou il est maintenant (detecte ou suppose).</summary>
        public void ToggleFloorLock()
        {
            floorLocked = !floorLocked;
            if (!floorLocked) { floorInit = false; haveAssumed = false; }
        }

        /// <summary>
        /// « Sol ici » : tes yeux sont maintenant a la hauteur reglee (debout ou assis) ; le sol est place a cette distance sous la tete et verrouille.
        /// A utiliser quand la detection du sol ne trouve rien ou se trompe.
        /// </summary>
        public void SetFloorFromEyeHeight()
        {
            assumedEyeHeight = ConfiguredEyeHeight(Ar6dofSettings.Current.seated);
            assumedFloorG = lastPosZ - assumedEyeHeight;
            haveAssumed = true;
            floorLocked = true;
            floorSmoothed = assumedFloorG; floorInit = true;
            frozenFloor = assumedFloorG;
        }
        float frozenFloor;

        /// <summary>Le suivi redemarre : son monde repart de zero (origine, sol et cap sont a refaire).</summary>
        public void ResetWorld()
        {
            floorLocked = false; floorInit = false; haveAssumed = false; haveOrigin = false;
        }

        /// <summary>Remet le cap a zero : l'avant du monde devient la ou tu regardes, sans deplacer ta position.</summary>
        public void RecenterYaw() { wantYaw = true; }

        public void SetSeated(bool seated)
        {
            float h = ConfiguredEyeHeight(seated);
            if (Mathf.Abs(assumedEyeHeight - h) < 1e-4f) return;
            assumedEyeHeight = h;
            haveAssumed = false;
        }

        void OnEnable() { Application.onBeforeRender += Apply; }
        void OnDisable() { Application.onBeforeRender -= Apply; }

        void Update()
        {
            if (Ar6dofInput.Down(recenterKey)) wantRecenter = true;
        }

        [BeforeRenderOrder(0)]   // avant le passthrough (qui en dépend)
        void Apply()
        {
            if (receiver == null) return;
            if (!receiver.TryGetLatest(out var p, out double age)) return;
            HasPose = true;
            ConnectionLost = age > staleSeconds;
            TrackingOk = p.Tracking && !ConnectionLost;

            // Pose prédite à l'instant du rendu. tPose est l'instant auquel la pose reçue est valable ;
            // un dt négatif (rendu avant tPose) est possible quand le tracker anticipe.
            double dt = predictToRenderTime ? Math.Max(-0.03, Math.Min(maxPredictSeconds, Ar6dofClock.Now() - p.tPose)) : 0.0;
            double[] qG = { p.qw, p.qx, p.qy, p.qz };
            if (dt != 0.0)   // vitesse angulaire en repère monde : la rotation s'applique après l'orientation
                qG = Ar6dofMath.QuatMul(Ar6dofMath.QuatFromRotVec(p.angG.x * dt, p.angG.y * dt, p.angG.z * dt), qG);
            qG = Ar6dofMath.Normalize(qG);
            RenderRotationG = Ar6dofMath.QuatToMat(qG);
            Quaternion rotU = Ar6dofConvert.Rotation((float)qG[0], (float)qG[1], (float)qG[2], (float)qG[3]);
            Vector3 posU = Ar6dofConvert.Position(p.posG + p.velG * (float)dt);

            // Sol : y = 0
            lastPosZ = p.posG.z;
            float floorG;
            FloorDetected = useDetectedFloor && p.FloorValid;
            if (floorLocked) floorG = frozenFloor;
            else if (FloorDetected) floorG = p.floorZ;
            else
            {
                if (!haveAssumed) { assumedFloorG = p.posG.z - assumedEyeHeight; haveAssumed = true; }
                floorG = assumedFloorG;
            }
            if (!floorLocked)
                floorSmoothed = floorInit
                    ? Mathf.Lerp(floorSmoothed, floorG, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f))
                    : floorG;
            else floorSmoothed = floorG;
            if (!floorLocked) frozenFloor = floorSmoothed;   // au verrouillage, on garde exactement le sol d'a l'instant
            floorInit = true;
            posU.y -= floorSmoothed;

            if (wantRecenter || (!haveOrigin && recenterOnFirstPose))
            {
                originU = new Vector3(posU.x, 0f, posU.z);
                Vector3 f = rotU * Vector3.forward;
                f.y = 0f;
                if (f.sqrMagnitude > 1e-4f)
                    yawFix = Quaternion.Euler(0f, -Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 0f);
                haveOrigin = true;
                haveAssumed = false;
                wantRecenter = false;
            }

            if (wantYaw && haveOrigin)
            {
                // cap a zero : nouvel avant = regard actuel ; l'origine est deplacee pour que TA position dans le monde ne saute pas
                Vector3 f2 = rotU * Vector3.forward;
                f2.y = 0f;
                if (f2.sqrMagnitude > 1e-4f)
                {
                    Quaternion yawNew = Quaternion.Euler(0f, -Mathf.Atan2(f2.x, f2.z) * Mathf.Rad2Deg, 0f);
                    Vector3 rel = new Vector3(posU.x - originU.x, 0f, posU.z - originU.z);
                    Vector3 worldNow = yawFix * rel;
                    Vector3 relNew = Quaternion.Inverse(yawNew) * worldNow;
                    originU = new Vector3(posU.x - relNew.x, 0f, posU.z - relNew.z);
                    yawFix = yawNew;
                }
                wantYaw = false;
            }

            Vector3 flat = new Vector3(posU.x - originU.x, 0f, posU.z - originU.z);
            RawPosition = new Vector3(flat.x, posU.y, flat.z);
            // échelle des déplacements : agrandit les pas SUR LE SOL (avancer, reculer, côté). La hauteur n'est jamais multipliée : elle reste la hauteur mesurée.
            float ms = Mathf.Clamp(Ar6dofSettings.Current.moveScale, 0.25f, 4f);
            Vector3 world = yawFix * (flat * ms) + new Vector3(0f, posU.y, 0f);
            transform.SetPositionAndRotation(world, yawFix * rotU);
            HeadHeight = posU.y;
            CameraStep = world;
        }
    }
}
