using UnityEngine;

namespace AR6DoF
{
    /// <summary>Une touche du clavier virtuel (créée par Ar6dofKeyboard).</summary>
    public class Ar6dofKey : MonoBehaviour, IAr6dofPointerTarget
    {
        public Ar6dofKeyboard keyboard;
        public Ar6dofKeyboard.KeyDef def;
        public TextMesh label;
        public Renderer quad;
        public bool active;   // Maj / Ctrl / Alt enfoncées

        bool hover, down;

        public void Refresh()
        {
            if (quad == null) return;
            Color c = down ? new Color(0.95f, 0.65f, 0.2f) : active ? new Color(0.2f, 0.7f, 0.4f) : hover ? new Color(0.3f, 0.5f, 0.9f) : new Color(0.17f, 0.17f, 0.2f);
            quad.material.color = c;
        }

        public void OnPointerEnter() { hover = true; Refresh(); }
        public void OnPointerExit() { hover = false; down = false; Refresh(); }
        public void OnPointerHover(Vector3 world) { }

        public void OnPointerDown(Vector3 world, int button)
        {
            if (button != 0) return;
            down = true;
            Refresh();
            keyboard.Press(def);
        }

        public void OnPointerUp(Vector3 world, int button) { down = false; Refresh(); }
    }
}
