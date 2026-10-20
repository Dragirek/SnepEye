using UnityEngine;

namespace AR6DoF
{
    /// <summary>Poignée d'un écran virtuel (barre de déplacement, coins de redimensionnement, bouton paramètres).</summary>
    public class Ar6dofScreenHandle : MonoBehaviour, IAr6dofPointerTarget, IAr6dofPointerDrag
    {
        public enum Kind { Move, Resize, Settings }
        public Kind kind;
        public Ar6dofScreenPanel panel;
        public Renderer rend;
        public Color baseColor = new Color(0.22f, 0.24f, 0.28f, 1f);
        public Color hoverColor = new Color(0.25f, 0.85f, 1f, 1f);
        MaterialPropertyBlock mpb;
        bool hover, dragging;

        void SetColor(Color c)
        {
            if (rend == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", c);
            rend.SetPropertyBlock(mpb);
        }

        void Start() { SetColor(baseColor); }
        public void OnPointerEnter() { hover = true; SetColor(hoverColor); panel.SetHover(true); }
        public void OnPointerExit() { hover = false; if (!dragging) SetColor(baseColor); panel.SetHover(false); }
        public void OnPointerHover(Vector3 worldPoint) { }

        public void OnPointerDown(Vector3 worldPoint, int button)
        {
            if (kind == Kind.Settings) { panel.OpenSettings(); return; }
            dragging = true;
            SetColor(hoverColor);
            panel.BeginDrag(kind, worldPoint, button);
        }

        public void OnPointerUp(Vector3 worldPoint, int button)
        {
            if (!dragging) return;
            dragging = false;
            SetColor(hover ? hoverColor : baseColor);
            panel.EndDrag();
        }

        public void OnDrag(Ray ray) { if (dragging) panel.Drag(ray); }
    }
}
