using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AR6DoF
{
    /// <summary>
    /// Pointeur laser piloté par la télécommande gyroscopique. Vise les objets qui ont un Collider et un composant
    /// IAr6dofPointerTarget (fenêtres Windows, touches du clavier virtuel, boutons de fenêtre) ainsi que les Canvas en World Space listés
    /// ci-dessous (boutons Unity UI). Bouton gauche = clic gauche, bouton droit = clic droit.
    /// Fiabilité des clics : (1) les appuis plus courts qu'une image ne sont jamais perdus (compteurs dans Ar6dofRemote), (2) le rayon est
    /// "figé" sur ce que tu visais juste AVANT l'appui (appuyer sur un bouton fait bouger la main), avec une petite zone morte pendant l'appui.
    /// </summary>
    public class Ar6dofPointer : MonoBehaviour
    {
        public Ar6dofRemote remote;
        public Ar6dofPassthrough passthrough;
        public Transform head;                                   // vide : la vue réelle du passthrough
        [Tooltip("Position de la main par rapport à la tête (tourne avec le cap de la tête) : droite, bas, avant")]
        public Vector3 originOffset = new Vector3(0.2f, -0.3f, 0.1f);
        public float maxDistance = 6f;
        [Tooltip("Canvas en World Space que la télécommande doit pouvoir cliquer")]
        public Canvas[] canvases;
        [Header("Fiabilité des clics")]
        [Tooltip("Pendant un appui, le rayon ne bouge pas tant que la main reste dans ce cône (degrés). 0 = désactivé.")]
        public float clickDeadzoneDeg = 1.3f;
        [Tooltip("Au moment de l'appui, on vise ce que tu visais il y a ce temps (secondes) : l'appui lui-même fait bouger la main.")]
        public float clickLookbackSec = 0.09f;
        [Header("Lissage supplémentaire")]
        [Tooltip("Désactivé par défaut : le filtre (stabilité + anticipation) est déjà dans Ar6dofRemote et ajouter un lissage ici ajoute du retard.")]
        public bool extraSmoothing = false;
        public float minCutoffHz = 1.5f;     // plus bas = plus stable à l'arrêt
        public float beta = 0.04f;           // plus haut = plus réactif en mouvement
        public float recenterHoldSeconds = 0.6f;

        /// <summary>Dernier rayon du laser (origine = main, direction lissée) : utilisé pour déplacer / redimensionner les écrans.</summary>
        public static Ray LastRay;

        /// <summary>Ajoute un Canvas World Space cliquable au pointeur (menus créés par code).</summary>
        public void AddCanvas(Canvas c)
        {
            if (c == null) return;
            var l = new List<Canvas>(canvases ?? new Canvas[0]);
            if (!l.Contains(c)) l.Add(c);
            canvases = l.ToArray();
            if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem));
        }

        struct Hit
        {
            public IAr6dofPointerTarget target;
            public Vector3 point;
            public GameObject ui;
            public Vector3 uiPoint;
            public RaycastResult uiRes;
        }

        LineRenderer laser;
        Vector3 dirSmooth = Vector3.forward;
        bool haveDir;
        float yawOffset;
        float bothHeld;
        bool firstRecenterDone;
        IAr6dofPointerTarget hovered, pressedL, pressedR;
        bool heldL, heldR, pendingUpL, pendingUpR;
        // visée figée pendant un appui
        bool pressActive;
        Vector3 pressDir;
        readonly List<float> histT = new List<float>();
        readonly List<Vector3> histD = new List<Vector3>();
        // Unity UI
        readonly List<RaycastResult> results = new List<RaycastResult>();
        GameObject uiHover, uiPressL, uiPressR;
        Camera eventCam;
        PointerEventData uiData;
        RaycastResult uiRes, pressRes;   // le résultat du raycast garde le GraphicRaycaster : c'est lui qui donne la caméra (pressEventCamera est en lecture seule)

        void Start()
        {
            if (remote == null) remote = FindFirstObjectByType<Ar6dofRemote>();
            if (passthrough == null) passthrough = FindFirstObjectByType<Ar6dofPassthrough>();
            if (EventSystem.current == null && canvases != null && canvases.Length > 0)
                new GameObject("EventSystem", typeof(EventSystem));   // nécessaire pour cliquer les boutons Unity UI
            var go = new GameObject("PointerLaser");
            go.transform.SetParent(transform, false);
            laser = go.AddComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.startWidth = 0.004f;
            laser.endWidth = 0.002f;
            laser.useWorldSpace = true;
            Shader sh = Shader.Find("AR6DoF/Flat");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            m.color = new Color(0.2f, 0.9f, 1f, 1f);
            laser.material = m;
            ApplyLaser();
            Ar6dofSettings.Changed += ApplyLaser;
        }

        void OnDestroy() { Ar6dofSettings.Changed -= ApplyLaser; }

        /// <summary>Couleur et épaisseur du rayon : style "Rayon" des paramètres (couleur d'accent, transparence, épaisseur).</summary>
        void ApplyLaser()
        {
            if (laser == null) return;
            var st = Ar6dofSettings.StyleFor(Ar6dofElem.Laser);
            Color c = st.accent;
            c.a = Mathf.Clamp01(Mathf.Max(0.25f, st.alpha));
            laser.startWidth = Mathf.Clamp(st.lineWidth, 0.001f, 0.02f);
            laser.endWidth = laser.startWidth * 0.5f;
            laser.startColor = c;
            laser.endColor = c;
            if (laser.material != null) laser.material.color = c;
        }

        float Yaw(Vector3 v) { return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }

        /// <summary>Direction visée il y a 'age' secondes (la plus ancienne connue si l'historique est plus court).</summary>
        Vector3 DirAgo(float age, float now)
        {
            for (int i = histT.Count - 1; i >= 0; i--)
                if (histT[i] <= now - age) return histD[i];
            return histD.Count > 0 ? histD[0] : dirSmooth;
        }

        void Update()
        {
            if (head == null && passthrough != null) head = passthrough.HeadView;
            if (remote == null || head == null || laser == null) return;
            if (!remote.TryGetPointDirection(out Vector3 dRaw)) { laser.enabled = false; ReleaseAll(); return; }

            bool left = remote.Left, right = remote.Right;
            Vector3 headFwd = head.forward; headFwd.y = 0f;
            if (headFwd.sqrMagnitude < 1e-4f) headFwd = Vector3.forward;

            // Recentrage : le cap de la télécommande est arbitraire. Au premier contact, ou en maintenant les 2 boutons.
            bothHeld = (left && right) ? bothHeld + Time.unscaledDeltaTime : 0f;
            if (!firstRecenterDone || bothHeld > recenterHoldSeconds)
            {
                Vector3 flat = new Vector3(dRaw.x, 0f, dRaw.z);
                if (flat.sqrMagnitude > 0.05f) { yawOffset = Yaw(headFwd) - Yaw(flat); firstRecenterDone = true; }
                bothHeld = -999f;   // un seul recentrage par appui
            }

            Vector3 d = Quaternion.Euler(0f, yawOffset, 0f) * dRaw;
            // Lissage adaptatif (type "One Euro") : stable à l'arrêt, sans retard en mouvement rapide
            if (!haveDir || !extraSmoothing) { dirSmooth = d; haveDir = true; }
            else
            {
                float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
                float speed = Vector3.Angle(dirSmooth, d) / dt;   // deg/s
                float cutoff = minCutoffHz + beta * speed;
                float tau = 1f / (2f * Mathf.PI * cutoff);
                dirSmooth = Vector3.Slerp(dirSmooth, d, dt / (dt + tau)).normalized;
            }

            // historique de la visée (0,4 s)
            float now = Time.unscaledTime;
            histT.Add(now); histD.Add(dirSmooth);
            while (histT.Count > 1 && histT[0] < now - 0.4f) { histT.RemoveAt(0); histD.RemoveAt(0); }

            // boutons : appuis / relâchements depuis la dernière image (aucun appui court n'est perdu)
            int dl, ul, dr, ur;
            remote.ConsumeEdges(0, out dl, out ul);
            remote.ConsumeEdges(1, out dr, out ur);
            bool wantDownL = dl > 0 || (left && !heldL);
            bool wantDownR = dr > 0 || (right && !heldR);

            // Au premier appui : on fige la visée sur ce qui était visé juste avant (sauf si la main bougeait vite : on vise alors là où elle est)
            if ((wantDownL || wantDownR) && !pressActive)
            {
                Vector3 back = DirAgo(Mathf.Max(0f, clickLookbackSec), now);
                pressDir = Vector3.Angle(back, dirSmooth) <= 3.5f ? back : dirSmooth;
                pressActive = clickDeadzoneDeg > 0f;
            }
            Vector3 aim = dirSmooth;
            if (pressActive)
            {
                float ang = Vector3.Angle(pressDir, dirSmooth);
                aim = ang <= clickDeadzoneDeg ? pressDir : Vector3.Slerp(pressDir, dirSmooth, 1f - clickDeadzoneDeg / ang).normalized;
            }

            Vector3 origin = head.position + Quaternion.Euler(0f, Yaw(headFwd), 0f) * originOffset;
            var ray = new Ray(origin, aim);
            LastRay = ray;

            Camera ec = passthrough != null && passthrough.HeadView != null ? passthrough.HeadView.GetComponent<Camera>() : null;
            eventCam = ec;
            Hit hit = Resolve(ray, ec);

            laser.enabled = true;
            Vector3 end = hit.ui != null ? hit.uiPoint : (hit.target != null ? hit.point : origin + aim * 2f);
            laser.SetPosition(0, origin);
            laser.SetPosition(1, end);

            // ---- survol ----
            if (hit.target != hovered)
            {
                if (hovered != null) hovered.OnPointerExit();
                hovered = hit.target;
                if (hovered != null) hovered.OnPointerEnter();
            }
            if (hovered != null) hovered.OnPointerHover(hit.point);
            UiHover(hit.ui);

            // ---- boutons ----
            HandleButton(0, left, wantDownL, ul > 0 || (!left && heldL), ref hit, ec);
            HandleButton(1, right, wantDownR, ur > 0 || (!right && heldR), ref hit, ec);
            if (heldL && uiPressL != null) UiDrag(ray, uiPressL);   // curseurs (Slider) : suit le laser tant que le bouton est maintenu
            if (heldL && pressedL is IAr6dofPointerDrag dragL) dragL.OnDrag(ray);
            if (heldR && pressedR is IAr6dofPointerDrag dragR) dragR.OnDrag(ray);
            if (!heldL && !heldR) pressActive = false;
        }

        /// <summary>Traite un bouton : appui, relâchement, ou appui très court (appui maintenant, relâchement à l'image suivante).</summary>
        void HandleButton(int b, bool state, bool wantDown, bool wantUp, ref Hit hit, Camera ec)
        {
            bool held = b == 0 ? heldL : heldR;
            bool pending = b == 0 ? pendingUpL : pendingUpR;
            if (pending)
            {
                pending = false;
                if (held) { DoUp(b, ref hit); held = false; }
            }
            if (held && wantUp && !wantDown) { DoUp(b, ref hit); held = false; }
            else if (held && wantUp && wantDown) { DoUp(b, ref hit); DoDown(b, ref hit, ec); held = true; pending = !state; }
            else if (!held && wantDown) { DoDown(b, ref hit, ec); held = true; pending = !state; }
            if (b == 0) { heldL = held; pendingUpL = pending; } else { heldR = held; pendingUpR = pending; }
        }

        void DoDown(int b, ref Hit hit, Camera ec)
        {
            var tgt = hovered;
            if (b == 0) pressedL = tgt; else pressedR = tgt;
            if (tgt != null) tgt.OnPointerDown(hit.point, b);
            if (hit.ui != null && uiData != null && ec != null)
            {
                uiData.position = ec.WorldToScreenPoint(hit.uiPoint);
                uiRes = hit.uiRes;
            }
            if (b == 0) UiDown(ref uiPressL, hit.ui, 0); else UiDown(ref uiPressR, hit.ui, 1);
        }

        void DoUp(int b, ref Hit hit)
        {
            var tgt = b == 0 ? pressedL : pressedR;
            if (tgt != null) tgt.OnPointerUp(hit.point, b);
            if (b == 0) { pressedL = null; UiUp(ref uiPressL, hit.ui, 0); }
            else { pressedR = null; UiUp(ref uiPressR, hit.ui, 1); }
        }

        /// <summary>Ce que le rayon touche : objet 3D le plus proche, ou Canvas Unity UI s'il est plus proche.</summary>
        Hit Resolve(Ray ray, Camera ec)
        {
            var h = new Hit();
            h.point = ray.origin + ray.direction * maxDistance;
            float best = maxDistance;
            if (Physics.Raycast(ray, out RaycastHit rh, maxDistance))
            {
                var t = rh.collider.GetComponentInParent<IAr6dofPointerTarget>();
                if (t != null) { h.target = t; h.point = rh.point; best = rh.distance; }
            }
            if (canvases != null && ec != null && EventSystem.current != null)
                foreach (var c in canvases)
                {
                    if (c == null || !c.gameObject.activeInHierarchy || !c.enabled) continue;
                    if (c.worldCamera != ec) c.worldCamera = ec;
                    var rt = (RectTransform)c.transform;
                    var plane = new Plane(rt.forward, rt.position);
                    if (!plane.Raycast(ray, out float dist) || dist <= 0f || dist >= best) continue;
                    Vector3 wp = ray.GetPoint(dist);
                    Vector3 lp = rt.InverseTransformPoint(wp);
                    if (!rt.rect.Contains(new Vector2(lp.x, lp.y))) continue;
                    if (uiData == null) uiData = new PointerEventData(EventSystem.current);
                    uiData.position = ec.WorldToScreenPoint(wp);
                    results.Clear();
                    EventSystem.current.RaycastAll(uiData, results);
                    if (results.Count > 0) { h.ui = results[0].gameObject; h.uiRes = results[0]; h.uiPoint = wp; best = dist; h.target = null; }
                }
            return h;
        }

        void ReleaseAll()
        {
            if (hovered != null) { hovered.OnPointerExit(); hovered = null; }
            pressedL = pressedR = null;
            heldL = heldR = pendingUpL = pendingUpR = false;
            pressActive = false;
        }

        // ---- Unity UI (boutons...) ----
        /// <summary>Élément Unity UI actuellement visé par le laser (null si aucun).</summary>
        public GameObject HoverUi { get { return uiHover; } }

        void UiHover(GameObject go)
        {
            if (go == uiHover) return;
            if (uiHover != null && uiData != null) ExecuteEvents.Execute(uiHover, uiData, ExecuteEvents.pointerExitHandler);
            uiHover = go;
            if (uiHover != null && uiData != null) ExecuteEvents.Execute(uiHover, uiData, ExecuteEvents.pointerEnterHandler);
        }

        void UiDown(ref GameObject slot, GameObject go, int button)
        {
            if (go == null || uiData == null) { slot = null; return; }
            uiData.button = button == 0 ? PointerEventData.InputButton.Left : PointerEventData.InputButton.Right;
            slot = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            if (slot == null) slot = ExecuteEvents.GetEventHandler<IPointerDownHandler>(go);   // curseur (Slider)
            if (slot == null) slot = go;
            pressRes = uiRes;
            uiData.pointerPressRaycast = uiRes;
            uiData.pointerCurrentRaycast = uiRes;
            uiData.pointerPress = slot;
            ExecuteEvents.Execute(slot, uiData, ExecuteEvents.pointerDownHandler);
        }

        void UiDrag(Ray ray, GameObject slot)
        {
            if (uiData == null || eventCam == null || slot == null) return;
            var cv = slot.GetComponentInParent<Canvas>();
            if (cv == null) return;
            var rt = (RectTransform)cv.transform;
            var plane = new Plane(rt.forward, rt.position);
            float d;
            if (!plane.Raycast(ray, out d) || d <= 0f) return;
            uiData.position = eventCam.WorldToScreenPoint(ray.GetPoint(d));
            uiData.pointerPressRaycast = pressRes;
            uiData.button = PointerEventData.InputButton.Left;
            ExecuteEvents.Execute(slot, uiData, ExecuteEvents.dragHandler);
        }

        void UiUp(ref GameObject slot, GameObject go, int button)
        {
            if (slot == null || uiData == null) return;
            uiData.button = button == 0 ? PointerEventData.InputButton.Left : PointerEventData.InputButton.Right;
            ExecuteEvents.Execute(slot, uiData, ExecuteEvents.pointerUpHandler);
            GameObject under = go != null ? (ExecuteEvents.GetEventHandler<IPointerClickHandler>(go) ?? go) : null;
            if (under == slot) ExecuteEvents.Execute(slot, uiData, ExecuteEvents.pointerClickHandler);   // relâché sur le même bouton : clic
            slot = null;
        }
    }
}
