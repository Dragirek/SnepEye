using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Champ de texte pour les menus VR. Un clic le sélectionne (le clavier virtuel s'ouvre) : les touches tapées vont dans le champ au lieu de Windows.
    /// Entrée valide. Cliquer un autre champ valide aussi celui-ci.
    /// </summary>
    public class Ar6dofVrTextField : MonoBehaviour, IPointerClickHandler
    {
        public static Ar6dofVrTextField Current;
        public string text = "";
        public string placeholder = "";
        public int maxLength = 64;
        public Action<string> onChanged;   // à chaque touche
        public Action<string> onSubmit;    // Entrée, ou champ quitté
        public Text label;
        public Image bg;
        bool focused;

        static Color NormalColor { get { return Ar6dofTheme.FieldColor(false); } }
        static Color FocusColor { get { return Ar6dofTheme.FieldColor(true); } }

        public static Ar6dofVrTextField Make(Transform parent, Vector2 pos, Vector2 size, string value, string placeholder, Action<string> onChanged, Action<string> onSubmit, int fontSize = 22)
        {
            var t = Ar6dofVrUi.Rect(parent, "Champ", pos, size);
            var img = t.gameObject.AddComponent<Image>();
            img.color = NormalColor;
            Ar6dofTheme.SetRounded(img, Mathf.Min(Ar6dofSettings.StyleFor(Ar6dofElem.Menus).corner, 16));
            var f = t.gameObject.AddComponent<Ar6dofVrTextField>();
            f.bg = img;
            f.text = value ?? "";
            f.placeholder = placeholder;
            f.onChanged = onChanged;
            f.onSubmit = onSubmit;
            f.label = Ar6dofVrUi.Label(t, "", new Vector2(0f, 0f), new Vector2(size.x - 16f, size.y), fontSize, TextAnchor.MiddleLeft);
            f.Refresh();
            return f;
        }

        public void OnPointerClick(PointerEventData e) { Focus(); }

        public void Focus()
        {
            if (Current != null && Current != this) Current.Blur();
            Current = this;
            focused = true;
            Ar6dofKeyboard.CharSink = Type;
            Ar6dofKeyboard.VkSink = Key;
            bg.color = FocusColor;
            Refresh();
            if (Ar6dofDock.Instance != null) Ar6dofDock.Instance.ShowKeyboard();
        }

        public void Blur()
        {
            if (!focused) return;
            focused = false;
            if (Current == this) { Current = null; Ar6dofKeyboard.CharSink = null; Ar6dofKeyboard.VkSink = null; }
            bg.color = NormalColor;
            Refresh();
            if (onSubmit != null) onSubmit(text);
        }

        void Type(char c)
        {
            if (c < 32 || text.Length >= maxLength) return;
            text += c;
            if (onChanged != null) onChanged(text);
            Refresh();
        }

        void Key(ushort vk)
        {
            if (vk == 0x08 && text.Length > 0) { text = text.Substring(0, text.Length - 1); if (onChanged != null) onChanged(text); Refresh(); }
            else if (vk == 0x0D) Blur();
        }

        public void SetText(string s) { text = s ?? ""; Refresh(); }

        void Refresh()
        {
            if (label == null) return;
            bool empty = text.Length == 0 && !focused;
            label.text = empty ? placeholder : text + (focused ? "|" : "");
            label.color = empty ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
        }

        void OnDestroy()
        {
            if (Current == this) { Current = null; Ar6dofKeyboard.CharSink = null; Ar6dofKeyboard.VkSink = null; }
        }
    }
}
