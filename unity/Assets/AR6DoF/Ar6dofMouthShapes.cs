using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Les 45 paramètres du mouth tracking par modèle IA (ordre de Project Babble, noms ARKit / VRChat). Les valeurs vont de 0 à 1.
    /// "Left" / "Right" = la gauche / la droite de la personne qui porte le casque.
    /// Sans modèle IA (détection classique), FromLegacy en déduit les paramètres principaux, pour que tout ce qui lit ces 45 valeurs marche dans les deux cas.
    /// </summary>
    public static class Ar6dofMouthShapes
    {
        public const int Count = 45;
        public const int CheekPuffL = 0, CheekPuffR = 1, CheekSuckL = 2, CheekSuckR = 3, JawOpen = 4, JawForward = 5, JawLeft = 6, JawRight = 7,
            NoseSneerL = 8, NoseSneerR = 9, MouthFunnel = 10, MouthPucker = 11, MouthLeft = 12, MouthRight = 13, MouthRollUpper = 14, MouthRollLower = 15,
            MouthShrugUpper = 16, MouthShrugLower = 17, MouthClose = 18, MouthSmileL = 19, MouthSmileR = 20, MouthFrownL = 21, MouthFrownR = 22,
            MouthDimpleL = 23, MouthDimpleR = 24, MouthUpperUpL = 25, MouthUpperUpR = 26, MouthLowerDownL = 27, MouthLowerDownR = 28,
            MouthPressL = 29, MouthPressR = 30, MouthStretchL = 31, MouthStretchR = 32, TongueOut = 33, TongueUp = 34, TongueDown = 35,
            TongueLeft = 36, TongueRight = 37, TongueRoll = 38, TongueBendDown = 39, TongueCurlUp = 40, TongueSquish = 41, TongueFlat = 42,
            TongueTwistL = 43, TongueTwistR = 44;

        public static readonly string[] Names =
        {
            "cheekPuffLeft", "cheekPuffRight", "cheekSuckLeft", "cheekSuckRight", "jawOpen", "jawForward", "jawLeft", "jawRight", "noseSneerLeft",
            "noseSneerRight", "mouthFunnel", "mouthPucker", "mouthLeft", "mouthRight", "mouthRollUpper", "mouthRollLower", "mouthShrugUpper",
            "mouthShrugLower", "mouthClose", "mouthSmileLeft", "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight", "mouthDimpleLeft",
            "mouthDimpleRight", "mouthUpperUpLeft", "mouthUpperUpRight", "mouthLowerDownLeft", "mouthLowerDownRight", "mouthPressLeft",
            "mouthPressRight", "mouthStretchLeft", "mouthStretchRight", "tongueOut", "tongueUp", "tongueDown", "tongueLeft", "tongueRight",
            "tongueRoll", "tongueBendDown", "tongueCurlUp", "tongueSquish", "tongueFlat", "tongueTwistLeft", "tongueTwistRight"
        };

        /// <summary>Index d'un paramètre par son nom (ex. "jawOpen"), -1 s'il n'existe pas.</summary>
        public static int IndexOf(string name)
        {
            for (int i = 0; i < Names.Length; i++) if (Names[i] == name) return i;
            return -1;
        }

        /// <summary>Remplit les 45 paramètres à partir des seules valeurs de la détection classique (ouverture, largeur, côté).</summary>
        public static void FromLegacy(Ar6dofMouth m, float[] dst)
        {
            for (int i = 0; i < Count; i++) dst[i] = 0f;
            if (!m.valid) return;
            float open = Mathf.Clamp01(m.jawOpen);
            dst[JawOpen] = open;
            float w = Mathf.Clamp(m.wide, -1f, 1f);
            if (w > 0f) { dst[MouthSmileL] = dst[MouthSmileR] = w * 0.7f; dst[MouthStretchL] = dst[MouthStretchR] = w * 0.5f; }
            else { dst[MouthPucker] = -w; dst[MouthFunnel] = -w * open; }
            float s = Mathf.Clamp(m.side, -1f, 1f);
            if (s > 0f) dst[JawRight] = s; else dst[JawLeft] = -s;
            dst[MouthClose] = open < 0.05f ? 1f : 0f;
        }

        /// <summary>Valeurs « classiques » déduites des 45 paramètres (pour qui lisait jawOpen / wide / side).</summary>
        public static void ToLegacy(float[] s, ref Ar6dofMouth m)
        {
            m.jawOpen = s[JawOpen];
            m.wide = Mathf.Clamp(0.5f * (s[MouthSmileL] + s[MouthSmileR]) + 0.25f * (s[MouthStretchL] + s[MouthStretchR]) - s[MouthPucker], -1f, 1f);
            m.side = Mathf.Clamp(s[JawRight] - s[JawLeft], -1f, 1f);
            m.vert = 0f;
        }
    }
}
