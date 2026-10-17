using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Menu flottant "comme un Quest" : ancré dans le monde (on peut tourner autour),
    /// avec un mode de suivi paresseux : si tu t'éloignes ou tournes la tête trop longtemps,
    /// il se déplace en douceur devant toi.
    /// À placer sur la racine du menu (par exemple un Canvas en "World Space").
    /// </summary>
    public class Ar6dofFloatingMenu : MonoBehaviour
    {
        public Transform head;                    // la caméra (ou le rig) de la tête
        public Ar6dofHeadRig rig;                 // optionnel : pour connaître la hauteur au-dessus du sol
        public float distance = 0.9f;             // distance d'apparition devant toi (m)
        public float heightOffset = -0.05f;       // par rapport aux yeux (m)
        public float minHeightAboveFloor = 0.9f;  // le menu ne descend pas plus bas (m, y = 0 = sol)
        public bool summonOnStart = true;

        [Header("Suivi automatique (touche F pour activer / désactiver)")]
        public bool follow = false;
        public float maxDistance = 1.6f;          // au-delà, le menu vient te chercher
        public float maxAngle = 50f;              // angle de regard au-delà duquel il vient te chercher
        public float followDelay = 0.6f;          // temps passé hors de la zone de confort avant de bouger
        public float smoothTime = 0.35f;

        [Header("Touches")]
        public KeyCode summonKey = KeyCode.M;     // fait apparaître le menu devant toi, maintenant
        public KeyCode followKey = KeyCode.F;     // active / désactive le suivi automatique

        Vector3 velocity, lastFlatForward = Vector3.forward;
        float outsideTimer;
        bool moving, started;

        void Start()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
        }

        void LateUpdate()
        {
            if (head == null) return;
            if (!started && summonOnStart && (rig == null || rig.HasPose)) { Summon(); started = true; }
            if (Ar6dofInput.Down(summonKey)) Summon();
            if (Ar6dofInput.Down(followKey)) follow = !follow;
            if (follow) FollowStep(Time.deltaTime);
        }

        Vector3 FlatForward()
        {
            Vector3 f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) lastFlatForward = f.normalized;
            return lastFlatForward;
        }

        Vector3 FrontPoint()
        {
            Vector3 p = head.position + FlatForward() * distance;
            p.y = Mathf.Max(head.position.y + heightOffset, minHeightAboveFloor);
            return p;
        }

        void Face()
        {
            Vector3 dir = transform.position - head.position;  // l'axe Z du Canvas s'éloigne de toi : le texte est lisible de ton côté
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>Fait apparaître le menu devant toi, à hauteur du regard.</summary>
        public void Summon()
        {
            if (head == null) return;
            transform.position = FrontPoint();
            velocity = Vector3.zero;
            outsideTimer = 0f;
            moving = false;
            Face();
        }

        void FollowStep(float dt)
        {
            Vector3 toMenu = transform.position - head.position;
            Vector3 flat = new Vector3(toMenu.x, 0f, toMenu.z);
            float dist = flat.magnitude;
            float angle = dist > 1e-3f ? Vector3.Angle(FlatForward(), flat) : 0f;
            bool outside = dist > maxDistance || angle > maxAngle;
            outsideTimer = outside ? outsideTimer + dt : 0f;
            if (outsideTimer > followDelay) moving = true;

            if (moving)
            {
                Vector3 target = FrontPoint();
                transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, smoothTime);
                Face();
                if ((transform.position - target).sqrMagnitude < 0.0025f) { moving = false; outsideTimer = 0f; }
            }
        }
    }
}
