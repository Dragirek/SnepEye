using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Un écran virtuel : affiche un moniteur Windows (capture GPU, voir ar6dof_screen.dll) sur un rectangle plat dans la pièce
    /// et le rend cliquable avec la télécommande. Créé et géré par Ar6dofScreens.
    ///  - Barre sous l'écran : bouton GAUCHE = déplace tout le groupe collé, bouton DROIT = déplace cet écran seul (il se décolle).
    ///  - Coins : redimensionnent cet écran seulement (le groupe se recolle autour).
    ///  - Bouton "Param." : paramètres (source, images/s, ajouter / retirer un écran...).
    /// </summary>
    public class Ar6dofScreenPanel : MonoBehaviour, IAr6dofPointerTarget
    {
        [NonSerialized] public Ar6dofScreens owner;
        public int slot;
        public int monitor;
        public int fps = 60;
        public float width = 1.6f;
        [NonSerialized] public Ar6dofScreenPanel parent;   // écran auquel celui-ci est collé (null = libre)
        public int side;                                    // +1 : à droite du parent, -1 : à gauche
        public bool open = true;                            // fermé = rangé dans la barre de tâches : objet désactivé, capture arrêtée, aucun calcul

        public float Width { get { return width; } }
        public float Height { get { return width / aspect; } }

        float aspect = 16f / 9f;
        RenderTexture rt;
        Material screenMat;
        Transform quad;
        Renderer quadRenderer;
        Transform barRoot;
        readonly List<Transform> cornerT = new List<Transform>();
        Transform moveT, gearT;
        Font font;
        int rectL, rectT, rectR, rectB;
        bool haveRect;
        float nextRect;
        long lastFrames;
        int lastCursorX = int.MinValue, lastCursorY = int.MinValue;
        float hoverAmount, hoverTarget;
        int hoverCount;

        // glisser
        Ar6dofScreenHandle.Kind dragKind;
        int dragButton;
        float grabDist, startRadius, startWidth;
        Vector3 grabOffset;
        List<Ar6dofScreenPanel> dragGroup = new List<Ar6dofScreenPanel>();
        bool dragging;

        public void Init(Ar6dofScreens o, int slotIndex, int monitorIndex, int fpsValue, float widthMeters)
        {
            owner = o;
            slot = slotIndex;
            monitor = monitorIndex;
            fps = fpsValue;
            width = widthMeters;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 64);

            screenMat = new Material(owner.ScreenShader);
            screenMat.SetFloat("_UseTex", 1f);
            screenMat.SetFloat("_FlipY", owner.flipY ? 1f : 0f);
            screenMat.SetFloat("_Hover", 0f);

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Screen";
            q.transform.SetParent(transform, false);
            Destroy(q.GetComponent<Collider>());
            var bc = q.AddComponent<BoxCollider>();
            bc.size = new Vector3(1f, 1f, 0.02f);
            quad = q.transform;
            quadRenderer = q.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = screenMat;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;

            barRoot = new GameObject("Chrome").transform;
            barRoot.SetParent(transform, false);
            moveT = MakeHandle("Deplacer", Ar6dofScreenHandle.Kind.Move, "");
            gearT = MakeHandle("Parametres", Ar6dofScreenHandle.Kind.Settings, "Param.");
            for (int i = 0; i < 4; i++) cornerT.Add(MakeHandle("Coin" + i, Ar6dofScreenHandle.Kind.Resize, ""));
            Layout();
            RefreshRect();
            Ar6dofScreenNative.Start(slot, monitor, fps);
        }

        Transform MakeHandle(string n, Ar6dofScreenHandle.Kind kind, string label)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = n;
            g.transform.SetParent(barRoot, false);
            Destroy(g.GetComponent<Collider>());
            var bc = g.AddComponent<BoxCollider>();
            bc.size = new Vector3(1f, 1f, 0.05f);
            var r = g.GetComponent<Renderer>();
            var m = new Material(owner.ScreenShader);
            m.SetFloat("_UseTex", 0f);
            m.SetColor("_Color", new Color(0.22f, 0.24f, 0.28f, 1f));
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var h = g.AddComponent<Ar6dofScreenHandle>();
            h.kind = kind;
            h.panel = this;
            h.rend = r;
            if (label.Length > 0 && font != null)
            {
                var t = new GameObject("Texte");
                t.transform.SetParent(g.transform, false);
                t.transform.localPosition = new Vector3(0f, 0f, -0.02f);
                var tm = t.AddComponent<TextMesh>();
                tm.text = label;
                tm.font = font;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.characterSize = 0.1f;
                tm.fontSize = 24;
                tm.color = Color.white;
                t.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return g.transform;
        }

        /// <summary>Dimensions des poignées en mètres (indépendantes de la taille de l'écran).</summary>
        public void Layout()
        {
            float w = width, h = Height;
            quad.localScale = new Vector3(w, h, 1f);
            float barW = Mathf.Clamp(w * 0.35f, 0.3f, 0.8f), barH = 0.045f;
            float y = -h * 0.5f - 0.05f;
            moveT.localPosition = new Vector3(0f, y, 0f);
            moveT.localScale = new Vector3(barW, barH, 1f);
            gearT.localPosition = new Vector3(barW * 0.5f + 0.09f, y, 0f);
            gearT.localScale = new Vector3(0.14f, barH, 1f);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                cornerT[i].localPosition = new Vector3(sx * (w * 0.5f + 0.035f), sy * (h * 0.5f + 0.035f), 0f);
                cornerT[i].localScale = new Vector3(0.07f, 0.07f, 1f);
            }
        }

        // ------------------------------------------------------------------ mise à jour
        void RefreshRect()
        {
            haveRect = Ar6dofScreenNative.MonitorRect(monitor, out rectL, out rectT, out rectR, out rectB);
        }

        void RecreateTexture(int w, int h)
        {
            Ar6dofScreenNative.SetTarget(slot, IntPtr.Zero);
            if (rt != null) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.BGRA32, RenderTextureReadWrite.sRGB);
            rt.useMipMap = true;
            rt.autoGenerateMips = false;
            rt.filterMode = FilterMode.Trilinear;
            rt.anisoLevel = 4;
            rt.name = "Ar6dofScreen" + slot;
            rt.Create();
            screenMat.SetTexture("_MainTex", rt);
            Ar6dofScreenNative.SetTarget(slot, rt.GetNativeTexturePtr());
            aspect = (float)w / h;
            Layout();
            owner.RelayoutAttached();
        }

        void Update()
        {
            if (Time.unscaledTime > nextRect) { nextRect = Time.unscaledTime + 2f; RefreshRect(); }
            int w = Ar6dofScreenNative.Width(slot), h = Ar6dofScreenNative.Height(slot);
            if (w > 0 && h > 0 && (rt == null || rt.width != w || rt.height != h)) RecreateTexture(w, h);

            bool vis = quadRenderer != null && quadRenderer.isVisible;
            Ar6dofScreenNative.SetActive(slot, vis);
            if (rt != null && vis)
            {
                var fn = Ar6dofScreenNative.RenderEventFunc();
                if (fn != IntPtr.Zero) GL.IssuePluginEvent(fn, slot);
                long fr = Ar6dofScreenNative.Frames(slot);
                if (fr != lastFrames) { lastFrames = fr; rt.GenerateMips(); }
            }
            hoverAmount = Mathf.MoveTowards(hoverAmount, hoverTarget, Time.unscaledDeltaTime * 8f);
            screenMat.SetFloat("_Hover", hoverAmount);
        }

        public void SetMonitor(int m)
        {
            monitor = m;
            lastFrames = 0;
            Ar6dofScreenNative.Start(slot, monitor, fps);
            RefreshRect();
        }

        /// <summary>Affiche l'écran (reprend la capture) ou le range (arrête tout : aucun calcul tant qu'il est rangé).</summary>
        public void SetContentActive(bool on)
        {
            open = on;
            gameObject.SetActive(on);
            if (on) { lastFrames = 0; Ar6dofScreenNative.Start(slot, monitor, fps); }
            else Ar6dofScreenNative.Stop(slot);
        }

        public void SetFps(int f) { fps = f; Ar6dofScreenNative.SetFps(slot, f); }

        public string SourceText()
        {
            string st = "";
            switch (Ar6dofScreenNative.Status(slot))
            {
                case 1: st = " (demarrage)"; break;
                case 3: st = " (inaccessible)"; break;
                case 4: st = " (HDR non gere)"; break;
            }
            return "Moniteur " + (monitor + 1) + " - " + Ar6dofScreenNative.Width(slot) + "x" + Ar6dofScreenNative.Height(slot) + st;
        }

        void OnDestroy()
        {
            Ar6dofScreenNative.SetTarget(slot, IntPtr.Zero);
            Ar6dofScreenNative.Stop(slot);
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (screenMat != null) Destroy(screenMat);
        }

        // ------------------------------------------------------------------ survol
        public void SetHover(bool on)
        {
            hoverCount = Mathf.Max(0, hoverCount + (on ? 1 : -1));
            hoverTarget = hoverCount > 0 ? 1f : 0f;
        }

        public void OpenSettings() { owner.OpenSettings(this); }

        // ------------------------------------------------------------------ contenu : clics vers Windows
        bool Map(Vector3 world, out int sx, out int sy)
        {
            sx = sy = 0;
            if (!haveRect) return false;
            Vector3 lp = transform.InverseTransformPoint(world);
            float u = Mathf.Clamp01(lp.x / width + 0.5f);
            float v = Mathf.Clamp01(lp.y / Height + 0.5f);   // 0 en bas, 1 en haut
            sx = rectL + Mathf.RoundToInt(u * (rectR - rectL - 1));
            sy = rectT + Mathf.RoundToInt((1f - v) * (rectB - rectT - 1));
            return true;
        }

        public void OnPointerEnter() { SetHover(true); }
        public void OnPointerExit() { SetHover(false); }

        public void OnPointerHover(Vector3 world)
        {
            int x, y;
            if (!Map(world, out x, out y)) return;
            if (x == lastCursorX && y == lastCursorY) return;
            lastCursorX = x; lastCursorY = y;
            Ar6dofWinInput.SetCursor(x, y);
        }

        public void OnPointerDown(Vector3 world, int button)
        {
            int x, y;
            if (!Map(world, out x, out y)) return;
            Ar6dofWinInput.SetCursor(x, y);
            Ar6dofWinInput.MouseButton(button, true);
        }

        public void OnPointerUp(Vector3 world, int button)
        {
            int x, y;
            if (Map(world, out x, out y)) Ar6dofWinInput.SetCursor(x, y);
            Ar6dofWinInput.MouseButton(button, false);
        }

        // ------------------------------------------------------------------ déplacer / redimensionner
        public void BeginDrag(Ar6dofScreenHandle.Kind kind, Vector3 hit, int button)
        {
            dragKind = kind;
            dragButton = button;
            dragging = true;
            if (kind == Ar6dofScreenHandle.Kind.Move)
            {
                grabDist = Vector3.Distance(Ar6dofPointer.LastRay.origin, hit);
                grabOffset = transform.position - hit;
                if (button == 1) owner.Detach(this);   // bouton droit : cet écran seul, il se décolle
                dragGroup = button == 1 ? new List<Ar6dofScreenPanel> { this } : owner.GroupOf(this);
            }
            else
            {
                startRadius = Mathf.Max(0.05f, (hit - transform.position).magnitude);
                startWidth = width;
            }
        }

        public void Drag(Ray ray)
        {
            if (!dragging) return;
            if (dragKind == Ar6dofScreenHandle.Kind.Move)
            {
                Transform head = owner.Head;
                Vector3 p = ray.origin + ray.direction * grabDist + grabOffset;
                Quaternion r = transform.rotation;
                if (head != null)
                {
                    Vector3 d = p - head.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 1e-4f) r = Quaternion.LookRotation(d.normalized);   // l'écran reste de face, à la verticale
                }
                var oldM = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                var newM = Matrix4x4.TRS(p, r, Vector3.one);
                Matrix4x4 delta = newM * oldM.inverse;
                foreach (var g in dragGroup)
                {
                    Matrix4x4 m = delta * Matrix4x4.TRS(g.transform.position, g.transform.rotation, Vector3.one);
                    g.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                }
            }
            else
            {
                var plane = new Plane(transform.forward, transform.position);
                float t;
                if (!plane.Raycast(ray, out t)) return;
                float radius = (ray.GetPoint(t) - transform.position).magnitude;
                width = Mathf.Clamp(startWidth * radius / startRadius, 0.3f, 8f);
                Layout();
                owner.RelayoutAttached();
            }
        }

        public void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            if (dragKind == Ar6dofScreenHandle.Kind.Move && parent == null) owner.TrySnap(this);
            dragGroup.Clear();
            owner.Save();
        }
    }
}
