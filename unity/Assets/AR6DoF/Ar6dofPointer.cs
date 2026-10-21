using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AR6DoF
{
    /// <summary>
    /// Pointeur laser piloté par la télécommande gyroscopique. Vise les objets qui ont un Collider et un composant
    /// IAr6dofPointerTarget (fenêtres Windows, touches du clavier virtuel) ainsi que les Canvas en World Space listés
    /// ci-dessous (boutons Unity UI). Bouton gauche = clic gauche, bouton droit = clic droit.
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
        [Header("Lissage (anti-tremblement)")]
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

        LineRenderer laser;
        Vector3 dirSmooth = Vector3.forward;
        bool haveDir;
        float yawOffset;
        float bothHeld;
        bool prevLeft, prevRight, firstRecenterDone;
        IAr6dofPointerTarget hovered, pressedL, pressedR;
        // Unity UI
        readonly List<RaycastResult> results = new List<RaycastResult>();
        GameObject uiHover, uiPressL, uiPressR;
        Camera eventCam;
        PointerEventData uiData;

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
        }

        float Yaw(Vector3 v) { return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }

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
            if (!haveDir) { dirSmooth = d; haveDir = true; }
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            float speed = Vector3.Angle(dirSmooth, d) / dt;   // deg/s
            float cutoff = minCutoffHz + beta * speed;
            float tau = 1f / (2f * Mathf.PI * cutoff);
            dirSmooth = Vector3.Slerp(dirSmooth, d, dt / (dt + tau)).normalized;

            Vector3 origin = head.position + Quaternion.Euler(0f, Yaw(headFwd), 0f) * originOffset;
            var ray = new Ray(origin, dirSmooth);
            LastRay = ray;

            // 1) objets 3D (fenêtres, touches)
            IAr6dofPointerTarget target = null;
            Vector3 point = origin + dirSmooth * maxDistance;
            float best = maxDistance;
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                var t = hit.collider.GetComponentInParent<IAr6dofPointerTarget>();
                if (t != null) { target = t; point = hit.point; best = hit.distance; }
            }
            // 2) Canvas Unity UI (plan du Canvas) : seulement s'il est plus proche
            GameObject uiGo = null;
            Vector3 uiPoint = point;
            Camera ec = passthrough != null && passthrough.HeadView != null ? passthrough.HeadView.GetComponent<Camera>() : null;
            eventCam = ec;
            if (canvases != null && ec != null)
                foreach (var c in canvases)
                {
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
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
                    if (results.Count > 0) { uiGo = results[0].gameObject; uiPoint = wp; best = dist; target = null; }
                }

            laser.enabled = true;
            Vector3 end = uiGo != null ? uiPoint : (target != null ? point : origin + dirSmooth * 2f);
            laser.SetPosition(0, origin);
            laser.SetPosition(1, end);

            // ---- survol ----
            if (target != hovered)
            {
                if (hovered != null) hovered.OnPointerExit();
                hovered = target;
                if (hovered != null) hovered.OnPointerEnter();
            }
            if (hovered != null) hovered.OnPointerHover(point);
            UiHover(uiGo);

            // ---- boutons ----
            if (left && !prevLeft) { pressedL = hovered; pressedL?.OnPointerDown(point, 0); UiDown(ref uiPressL, uiGo, 0); }
            if (!left && prevLeft) { pressedL?.OnPointerUp(point, 0); pressedL = null; UiUp(ref uiPressL, uiGo, 0); }
            if (right && !prevRight) { pressedR = hovered; pressedR?.OnPointerDown(point, 1); UiDown(ref uiPressR, uiGo, 1); }
            if (!right && prevRight) { pressedR?.OnPointerUp(point, 1); pressedR = null; UiUp(ref uiPressR, uiGo, 1); }
            if (left && uiPressL != null) UiDrag(ray, uiPressL);   // curseurs (Slider) : suit le laser tant que le bouton est maintenu
            if (left && pressedL is IAr6dofPointerDrag dragL) dragL.OnDrag(ray);
            if (right && pressedR is IAr6dofPointerDrag dragR) dragR.OnDrag(ray);
            prevLeft = left;
            prevRight = right;
        }

        void ReleaseAll()
        {
            if (hovered != null) { hovered.OnPointerExit(); hovered = null; }
            pressedL = pressedR = null;
        }

        // ---- Unity UI (boutons...) ----
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
            uiData.pressEventCamera = eventCam;
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
            uiData.pressEventCamera = eventCam;
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
