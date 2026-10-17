using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Rendu stéréo côte à côte (oeil gauche = moitié gauche de la fenêtre, oeil droit = moitié droite).
    /// À placer sur le Rig, à la place de la caméra principale. À ADAPTER à ton optique :
    /// champ de vision, écart des yeux et correction de distorsion des lentilles ne sont pas gérés ici.
    /// </summary>
    public class Ar6dofStereoRig : MonoBehaviour
    {
        public float ipdMeters = 0.063f;
        public float verticalFov = 90f;
        public float nearClip = 0.05f, farClip = 100f;
        public Color background = Color.black;

        void Start()
        {
            Make("LeftEye", -ipdMeters * 0.5f, new Rect(0f, 0f, 0.5f, 1f));
            Make("RightEye", +ipdMeters * 0.5f, new Rect(0.5f, 0f, 0.5f, 1f));
        }

        void Make(string name, float x, Rect viewport)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            var c = go.AddComponent<Camera>();
            c.rect = viewport;
            c.fieldOfView = verticalFov;
            c.nearClipPlane = nearClip;
            c.farClipPlane = farClip;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = background;
        }
    }
}
