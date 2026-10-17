using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// À placer sur le parent de la caméra ("Rig"). Applique la pose 6DoF reçue :
    ///  - y = 0 est le SOL (détecté par le tracker, ou hauteur d'yeux supposée sinon)
    ///  - prédiction jusqu'à l'instant du rendu (vitesses reçues)
    ///  - touche de recentrage : l'origine et l'avant sont ramenés à la position/direction actuelles
    /// </summary>
    public class Ar6dofHeadRig : MonoBehaviour
    {
        public Ar6dofReceiver receiver;
        [Header("Sol")]
        public bool useDetectedFloor = true;
        [Tooltip("Hauteur des yeux au-dessus du sol si le tracker n'a pas détecté le sol")]
        public float assumedEyeHeight = 1.65f;
        [Header("Prédiction")]
        public bool predictToRenderTime = true;
        public float maxPredictSeconds = 0.1f;
        [Header("Recentrage")]
        public bool recenterOnFirstPose = true;
        public KeyCode recenterKey = KeyCode.R;
        [Header("Connexion")]
        public float staleSeconds = 0.5f;

        public bool HasPose { get; private set; }
        public bool TrackingOk { get; private set; }
        public bool ConnectionLost { get; private set; }
        public bool FloorDetected { get; private set; }
        /// <summary>Hauteur des yeux au-dessus du sol (m).</summary>
        public float HeadHeight { get; private set; }

        bool wantRecenter, haveOrigin;
        Vector3 originU;
        Quaternion yawFix = Quaternion.identity;
        float floorSmoothed;
        bool floorInit, haveAssumed;
        float assumedFloorG;

        public void Recenter() { wantRecenter = true; }

        void OnEnable() { Application.onBeforeRender += Apply; }
        void OnDisable() { Application.onBeforeRender -= Apply; }

        void Update()
        {
            if (Ar6dofInput.Down(recenterKey)) wantRecenter = true;
        }

        void Apply()
        {
            if (receiver == null) return;
            if (!receiver.TryGetLatest(out var p, out double age)) return;
            HasPose = true;
            ConnectionLost = age > staleSeconds;
            TrackingOk = p.Tracking && !ConnectionLost;

            // Pose prédite à l'instant du rendu
            double dt = predictToRenderTime ? Math.Min(age, maxPredictSeconds) : 0.0;
            Vector3 posU = Ar6dofConvert.Position(p.posG + p.velG * (float)dt);
            Quaternion rotU = Ar6dofConvert.Rotation(p.qw, p.qx, p.qy, p.qz);
            Vector3 w = Ar6dofConvert.AngularVelocity(p.angG);
            float angle = w.magnitude * (float)dt;
            if (angle > 1e-5f) rotU = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, w.normalized) * rotU;

            // Sol : y = 0
            float floorG;
            FloorDetected = useDetectedFloor && p.FloorValid;
            if (FloorDetected) floorG = p.floorZ;
            else
            {
                if (!haveAssumed) { assumedFloorG = p.posG.z - assumedEyeHeight; haveAssumed = true; }
                floorG = assumedFloorG;
            }
            floorSmoothed = floorInit
                ? Mathf.Lerp(floorSmoothed, floorG, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f))
                : floorG;
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

            Vector3 flat = new Vector3(posU.x - originU.x, 0f, posU.z - originU.z);
            Vector3 world = yawFix * flat + new Vector3(0f, posU.y, 0f);
            transform.SetPositionAndRotation(world, yawFix * rotU);
            HeadHeight = posU.y;
        }
    }
}
