using UnityEngine;

namespace AR6DoF
{
    /// <summary>Indique si le panneau de réglages (F1) est affiché : les autres composants y ajoutent leurs réglages.</summary>
    public static class Ar6dofUi
    {
        public static bool Visible;
    }

    /// <summary>
    /// Lecture clavier compatible avec les deux systèmes d'entrée de Unity 6
    /// (Input System "nouveau" ou ancien Input Manager). Lettres et touches F1..F12 (KeyCode.R, KeyCode.F1...).
    /// </summary>
    public static class Ar6dofInput
    {
        public static bool Down(KeyCode k)
        {
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
    }
}
