using UnityEngine;

namespace AR6DoF
{
    /// <summary>Un objet que la télécommande peut viser et cliquer (fenêtre Windows, touche du clavier virtuel...). Il doit avoir un Collider.</summary>
    public interface IAr6dofPointerTarget
    {
        void OnPointerEnter();
        void OnPointerExit();
        void OnPointerHover(Vector3 worldPoint);
        /// <summary>button : 0 = bouton gauche, 1 = bouton droit.</summary>
        void OnPointerDown(Vector3 worldPoint, int button);
        void OnPointerUp(Vector3 worldPoint, int button);
    }
}
