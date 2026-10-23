using UnityEngine;

namespace AR6DoF
{
    /// <summary>Une touche du clavier virtuel (créée par Ar6dofKeyboard). Couleurs : style "Clavier" (Ar6dofSettings).</summary>
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
            var st = Ar6dofSettings.StyleFor(Ar6dofElem.Keyboard);
            Color c = down ? Color.Lerp(st.accent, Color.white, 0.25f)
                    : active ? Color.Lerp(st.button, st.accent, 0.85f)
                    : hover ? Color.Lerp(st.button, st.accent, 0.5f)
                    : st.button;
            c.a = Mathf.Lerp(1f, st.alpha, 0.5f);
            quad.sharedMaterial.color = c;
            if (label != null) label.color = st.text;
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

    /// <summary>Poignée du clavier virtuel : barre = déplacer (bouton gauche ou droit), coin = changer la taille.</summary>
    public class Ar6dofKeyboardHandle : MonoBehaviour, IAr6dofPointerTarget, IAr6dofPointerDrag
    {
        public enum Kind { Move, Resize }
        public Kind kind;
        public Ar6dofKeyboard keyboard;
        public Renderer rend;
        bool hover, dragging;

        public void Refresh()
        {
            if (rend == null) return;
            var st = Ar6dofSettings.StyleFor(Ar6dofElem.Keyboard);
            Color c = (hover || dragging) ? st.accent : Color.Lerp(st.button, st.text, 0.15f);
            c.a = Mathf.Lerp(1f, st.alpha, 0.5f);
            rend.sharedMaterial.color = c;
        }

        public void OnPointerEnter() { hover = true; Refresh(); }
        public void OnPointerExit() { hover = false; Refresh(); }
        public void OnPointerHover(Vector3 world) { }

        public void OnPointerDown(Vector3 world, int button)
        {
            dragging = true;
            Refresh();
            keyboard.BeginDrag(kind, world);
        }

        public void OnPointerUp(Vector3 world, int button)
        {
            if (!dragging) return;
            dragging = false;
            Refresh();
            keyboard.EndDrag();
        }

        public void OnDrag(Ray ray) { if (dragging) keyboard.Drag(ray); }
    }
}
