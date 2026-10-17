using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Lecture clavier compatible avec les deux systèmes d'entrée de Unity 6
    /// (Input System "nouveau" ou ancien Input Manager). Lettres uniquement (KeyCode.R, KeyCode.M...).
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
