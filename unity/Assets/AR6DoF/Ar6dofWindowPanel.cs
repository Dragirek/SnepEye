using System;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Une fenêtre Windows affichée dans la scène (par exemple avec le composant UwcWindowTexture de uWindowCapture sur le même
    /// Quad) et cliquable avec la télécommande. Le point visé sur le Quad est converti en point de la vraie fenêtre, puis la souris
    /// de Windows est déplacée et cliquée. À mettre sur le même objet que le Quad affichant la fenêtre.
    /// Limites : la fenêtre doit rester visible sur un écran (pas minimisée, pas recouverte par une autre) pour que les clics
    /// arrivent ; si les clics tombent à côté sur un écran à mise à l'échelle > 100 %, mets cet écran à 100 %.
    /// </summary>
    public class Ar6dofWindowPanel : MonoBehaviour, IAr6dofPointerTarget
    {
        [Tooltip("Partie du titre de la fenêtre à contrôler (la même que dans UwcWindowTexture)")]
        public string windowTitleContains = "";
        [Tooltip("Taille du maillage affichant la fenêtre en coordonnées locales (un Quad Unity = 1 x 1)")]
        public Vector2 meshSize = Vector2.one;
        public bool mirrorHorizontal;   // à cocher si le clic tombe à l'opposé en horizontal
        public bool flipVertical;       // idem en vertical
        [Tooltip("Utilise les bords visibles de la fenêtre (sans les marges invisibles de Windows 10/11)")]
        public bool useVisibleBounds = true;
        public bool moveCursorOnHover = true;

        IntPtr hwnd;
        float nextFind;
        public IntPtr Handle { get { return hwnd; } }

        void Awake()
        {
            if (GetComponent<Collider>() == null) gameObject.AddComponent<BoxCollider>();
        }

        void Update()
        {
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 1f;
            if (!Ar6dofWinInput.IsValid(hwnd)) hwnd = Ar6dofWinInput.FindWindowByTitle(windowTitleContains);
        }

        bool Map(Vector3 world, out int sx, out int sy)
        {
            sx = 0; sy = 0;
            Ar6dofWinInput.RECT r;
            if (!Ar6dofWinInput.GetRect(hwnd, useVisibleBounds, out r)) return false;
            Vector3 lp = transform.InverseTransformPoint(world);
            float u = lp.x / meshSize.x + 0.5f, v = lp.y / meshSize.y + 0.5f;   // v : 0 en bas, 1 en haut
            if (mirrorHorizontal) u = 1f - u;
            if (flipVertical) v = 1f - v;
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            sx = r.left + Mathf.RoundToInt(u * (r.right - r.left - 1));
            sy = r.top + Mathf.RoundToInt((1f - v) * (r.bottom - r.top - 1));
            return true;
        }

        public void OnPointerEnter() { }
        public void OnPointerExit() { }

        public void OnPointerHover(Vector3 world)
        {
            int x, y;
            if (moveCursorOnHover && Map(world, out x, out y)) Ar6dofWinInput.SetCursor(x, y);
        }

        public void OnPointerDown(Vector3 world, int button)
        {
            int x, y;
            if (!Map(world, out x, out y)) return;
            Ar6dofWinInput.BringToFront(hwnd);   // la fenêtre reçoit aussi le clavier virtuel
            Ar6dofWinInput.SetCursor(x, y);
            Ar6dofWinInput.MouseButton(button, true);
        }

        public void OnPointerUp(Vector3 world, int button)
        {
            int x, y;
            if (Map(world, out x, out y)) Ar6dofWinInput.SetCursor(x, y);
            Ar6dofWinInput.MouseButton(button, false);
        }
    }
}
