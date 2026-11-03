using UnityEngine;

namespace AR6DoF
{
    /// <summary>Palettes de couleurs pour l'animation lumineuse du clavier virtuel.</summary>
    public static class Ar6dofKbFx
    {
        public static readonly string[] AnimNames = { "Aucune", "Vague", "Cycle", "Respiration", "Reactif", "Scintillement", "Onde" };
        public static readonly string[] DirNames = { "Vers la droite", "Vers la gauche", "Vers le bas", "Vers le haut", "Depuis le centre" };
        public static readonly string[] PaletteNames = { "Spectre", "Marine", "Brasier", "Synthwave", "Sylve", "Aurore", "Dragee", "Glacier", "Crepuscule", "Matrix", "Theme du clavier" };

        // arrêts de couleur de chaque palette (indices = PaletteNames) (la dernière se raccorde à la première : la boucle est continue)
        static readonly string[][] Stops =
        {
            null,                                                         // arc-en-ciel : calculé
            new[] { "#0A3D91", "#1EA7FF", "#2EE6D6", "#1EA7FF" },
            new[] { "#B3120C", "#FF5A00", "#FFC400", "#FF5A00" },
            new[] { "#FF2BD6", "#7A3CFF", "#19E6FF", "#7A3CFF" },
            new[] { "#0B6B2E", "#35D65A", "#C6F23A", "#35D65A" },
            new[] { "#1CFF9B", "#19C5FF", "#9B5CFF", "#FF5CC8" },
            new[] { "#FF8FC9", "#C79BFF", "#8FD3FF", "#C79BFF" },
            new[] { "#FFFFFF", "#BFEFFF", "#4FB4FF", "#BFEFFF" },
            new[] { "#6A1BB3", "#FF5C8A", "#FF9F3A", "#FF5C8A" },
            new[] { "#003B0F", "#00A82D", "#7CFF9B", "#00A82D" },
            null,                                                         // style du clavier : accent <-> texte
        };

        static Color[][] parsed;

        static Color Parse(string h)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString(h, out c)) c = Color.white;
            c.a = 1f;
            return c;
        }

        /// <summary>Couleur de la palette à la position x (n'importe quel réel : la palette boucle).</summary>
        public static Color Sample(int palette, float x)
        {
            x = Mathf.Repeat(x, 1f);
            palette = Mathf.Clamp(palette, 0, Stops.Length - 1);
            if (palette == 0) return Color.HSVToRGB(x, 0.85f, 1f);
            if (palette == 10)
            {
                var st = Ar6dofSettings.StyleFor(Ar6dofElem.Keyboard);
                float t = x < 0.5f ? x * 2f : (1f - x) * 2f;
                return Color.Lerp(st.accent, st.text, t);
            }
            if (parsed == null) parsed = new Color[Stops.Length][];
            if (parsed[palette] == null)
            {
                var a = new Color[Stops[palette].Length];
                for (int i = 0; i < a.Length; i++) a[i] = Parse(Stops[palette][i]);
                parsed[palette] = a;
            }
            var cols = parsed[palette];
            float f = x * cols.Length;
            int i0 = Mathf.FloorToInt(f) % cols.Length;
            int i1 = (i0 + 1) % cols.Length;
            return Color.Lerp(cols[i0], cols[i1], f - Mathf.Floor(f));
        }
    }
}
