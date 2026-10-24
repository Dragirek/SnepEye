using UnityEngine;

namespace AR6DoF
{
    /// <summary>Indique si le panneau de réglages (F1) est affiché : les autres composants y ajoutent leurs réglages.</summary>
    public static class Ar6dofUi
    {
        public static bool Visible;
    }

    /// <summary>Mode debug (case "Debug" du composant Ar6dofDock) : affiche les anciens panneaux d'infos en haut à gauche de l'écran Unity.</summary>
    public static class Ar6dofDebug
    {
        public static bool On;
    }

    /// <summary>
    /// Lecture clavier compatible avec les deux systèmes d'entrée de Unity 6
    /// (Input System "nouveau" ou ancien Input Manager). Lettres et touches F1..F12 (KeyCode.R, KeyCode.F1...).
    /// </summary>
    public static class Ar6dofInput
    {
        /// <summary>Faux par défaut : plus aucun raccourci clavier (tu tapes sur de vrais claviers dans tes écrans virtuels). Voir Ar6dofDock > Hotkeys.</summary>
        public static bool HotkeysEnabled;

        public static bool Down(KeyCode k)
        {
            if (!HotkeysEnabled) return false;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return false;
            if (System.Enum.TryParse<UnityEngine.InputSystem.Key>(k.ToString(), out var key))
                return kb[key].wasPressedThisFrame;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(k);
#else
            return false;
#endif
        }

        /// <summary>Molette d'une vraie souris (positive = vers le haut), avec les deux systèmes d'entrée. Indépendant des raccourcis clavier.</summary>
        public static float ScrollY()
        {
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            return m != null ? m.scroll.ReadValue().y / 120f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#else
            return 0f;
#endif
        }
    }
}
