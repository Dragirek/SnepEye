using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Menu flottant "comme un Quest".
    ///  - Suivi automatique DÉSACTIVÉ (par défaut) : le menu reste FIXE dans la pièce, là où il a été posé
    ///    (hauteur des yeux au moment de l'appel). Tu peux t'approcher, reculer, tourner autour. M le rappelle devant toi.
    ///  - Suivi automatique ACTIVÉ (case à cocher dans F1, ou touche F) : si tu t'éloignes ou tournes la tête trop
    ///    longtemps, il vient se replacer devant toi en douceur.
    /// À placer sur la racine du menu (par exemple un Canvas en "World Space"), HORS du Rig.
    /// </summary>
    public class Ar6dofFloatingMenu : MonoBehaviour
    {
        public Transform head;                    // la caméra de la tête (laisse vide si tu utilises Ar6dofPassthrough)
        public Ar6dofPassthrough passthrough;     // la vue réelle (centre de l'affichage) sert de "tête"
        public Ar6dofHeadRig rig;                 // optionnel : vide = trouvé automatiquement
        public float distance = 0.9f;             // distance d'apparition devant toi (m)
        public float heightOffset = -0.05f;       // par rapport aux yeux (m)
        public float minHeightAboveFloor = 0.9f;  // le menu ne descend pas plus bas (m, y = 0 = sol)
        public bool summonOnStart = true;

        [Header("Suivi automatique")]
        public bool follow = false;               // case à cocher (aussi dans le panneau F1)
        public float maxDistance = 1.6f;          // au-delà, le menu vient te chercher
        public float maxAngle = 50f;              // angle de regard au-delà duquel il vient te chercher
        public float followDelay = 0.6f;          // temps passé hors de la zone de confort avant de bouger
        public float smoothTime = 0.35f;

        [Header("Touches")]
        public KeyCode summonKey = KeyCode.M;     // place le menu devant toi, maintenant
        public KeyCode followKey = KeyCode.F;     // active / désactive le suivi automatique

        Vector3 velocity, lastFlatForward = Vector3.forward;
        float outsideTimer;
        bool moving, started;

        void Start()
        {
            if (rig == null) rig = FindFirstObjectByType<Ar6dofHeadRig>();
            if (head == null && Camera.main != null) head = Camera.main.transform;
            // Un menu enfant du Rig suivrait la tête en permanence (il ne serait jamais "fixe dans la pièce") : on le détache.
            if (rig != null && transform.IsChildOf(rig.transform))
            {
                Debug.LogWarning("AR6DoF : le menu '" + name + "' était enfant du Rig : il suivrait ta tête. Il est détaché pour rester fixe dans la pièce.");
                transform.SetParent(null, true);
            }
        }

        void LateUpdate()
        {
            if (head == null && passthrough != null) head = passthrough.HeadView;
            if (head == null) return;
            if (!started && summonOnStart && (rig == null || rig.HasPose)) { Summon(); started = true; }
            if (Ar6dofInput.Down(summonKey)) Summon();
            if (Ar6dofInput.Down(followKey)) follow = !follow;
            if (follow) FollowStep(Time.deltaTime);
            else { moving = false; outsideTimer = 0f; velocity = Vector3.zero; }   // suivi coupé : rien ne bouge le menu
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

        /// <summary>Place le menu devant toi, à hauteur du regard. Il y reste fixe.</summary>
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

        static float Slider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + "  " + v.ToString("0.00"), GUILayout.Width(190));
            v = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return v;
        }

        // Réglages du menu : dans le panneau F1 (à droite des réglages du passthrough)
        void OnGUI()
        {
            if (!Ar6dofUi.Visible || head == null) return;
            GUILayout.BeginArea(new Rect(450, 60, 380, 330), GUI.skin.box);
            GUILayout.Label("-- Menu flottant");
            follow = GUILayout.Toggle(follow, "Suivi automatique (OFF = fixe dans la piece)");
            if (GUILayout.Button("Recentrer le menu devant moi (" + summonKey + ")")) Summon();
            distance = Slider("distance d'apparition (m)", distance, 0.4f, 2.5f);
            heightOffset = Slider("hauteur / yeux (m)", heightOffset, -0.6f, 0.4f);
            if (follow)
            {
                maxDistance = Slider("distance max (m)", maxDistance, 0.8f, 4f);
                maxAngle = Slider("angle max (deg)", maxAngle, 20f, 120f);
                followDelay = Slider("delai avant de suivre (s)", followDelay, 0f, 3f);
            }
            Vector3 hp = head.position;
            Vector3 d = transform.position - hp;
            GUILayout.Label("tete (cm) : x " + (hp.x * 100f).ToString("0") + "  y " + (hp.y * 100f).ToString("0") + "  z " + (hp.z * 100f).ToString("0"));
            GUILayout.Label("distance au menu : " + (new Vector3(d.x, 0f, d.z).magnitude * 100f).ToString("0") + " cm" + (moving ? "  (en deplacement)" : ""));
            GUILayout.EndArea();
        }
    }
}
