using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Dessin d'une mire (damier) en perspective, pour montrer comment placer ou incliner la vraie : au centre, dans un coin, tournée vers la gauche,
    /// penchée... Passe par Ar6dofRasterGfx (les maillages de Graphic ne s'affichent pas dans les menus VR). Angles en degrés ;
    /// 'px', 'py' (-1..1, py positif = vers le haut) placent le centre dans le rectangle, 'size' donne la largeur de la mire par rapport à celle du rectangle.
    /// Convention : lacet positif = bord droit plus proche ; tangage positif = bord du haut plus loin.
    /// </summary>
    public class Ar6dofBoardIcon : Ar6dofRasterGfx
    {
        public int squaresX = 6, squaresY = 4;
        public float px, py, size = 0.5f, yaw, pitch, roll;
        public Color dark = new Color(0.05f, 0.05f, 0.07f, 1f), light = new Color(0.95f, 0.95f, 0.95f, 1f);
        public float alpha = 1f;

        public void Set(float px, float py, float size, float yaw, float pitch, float roll)
        {
            if (Mathf.Approximately(px, this.px) && Mathf.Approximately(py, this.py) && Mathf.Approximately(size, this.size) &&
                Mathf.Approximately(yaw, this.yaw) && Mathf.Approximately(pitch, this.pitch) && Mathf.Approximately(roll, this.roll)) return;
            this.px = px; this.py = py; this.size = size; this.yaw = yaw; this.pitch = pitch; this.roll = roll;
            SetVerticesDirty();
        }

        // point (x, y) du plan de la mire (x et y : -1..1) -> point du rectangle (repère local, y vers le haut)
        Vector2 Project(float x, float y, Rect r)
        {
            float aspect = (float)squaresY / Mathf.Max(1, squaresX);
            float bx = x, by = y * aspect;
            float cr = Mathf.Cos(roll * Mathf.Deg2Rad), sr = Mathf.Sin(roll * Mathf.Deg2Rad);
            float rx = bx * cr - by * sr, ry = bx * sr + by * cr;
            float cy = Mathf.Cos(yaw * Mathf.Deg2Rad), sy = Mathf.Sin(yaw * Mathf.Deg2Rad);
            float cp = Mathf.Cos(pitch * Mathf.Deg2Rad), sp = Mathf.Sin(pitch * Mathf.Deg2Rad);
            float x1 = rx * cy, z1 = -rx * sy;                      // lacet : autour de l'axe vertical
            float y2 = ry * cp - z1 * sp, z2 = ry * sp + z1 * cp;   // tangage : autour de l'axe horizontal
            const float d = 3.2f;                                   // distance de la camera, en demi-largeurs de mire
            float k = d / Mathf.Max(0.5f, d + z2);
            float u = size * 0.5f * r.width;
            return new Vector2(r.center.x + px * 0.5f * r.width + x1 * k * u, r.center.y + py * 0.5f * r.height + y2 * k * u);
        }

        protected override void Draw(VertexHelper vh, Rect r)
        {
            for (int j = 0; j < squaresY; j++)
                for (int i = 0; i < squaresX; i++)
                {
                    float x0 = -1f + 2f * i / squaresX, x1 = -1f + 2f * (i + 1) / squaresX;
                    float y0 = -1f + 2f * j / squaresY, y1 = -1f + 2f * (j + 1) / squaresY;
                    Color c = (i + j) % 2 == 0 ? dark : light;
                    c.a *= alpha;
                    int n = vh.currentVertCount;
                    vh.AddVert(Project(x0, y0, r), c, Vector2.zero);
                    vh.AddVert(Project(x1, y0, r), c, Vector2.zero);
                    vh.AddVert(Project(x1, y1, r), c, Vector2.zero);
                    vh.AddVert(Project(x0, y1, r), c, Vector2.zero);
                    vh.AddTriangle(n, n + 1, n + 2);
                    vh.AddTriangle(n, n + 2, n + 3);
                }
        }
    }

    /// <summary>
    /// Coins de la mire détectés, dessinés par-dessus l'aperçu de la caméra (un petit carré par coin, un seul dessin pour tous).
    /// Coordonnées normalisées (0..1, origine en haut à gauche), comme celles envoyées par ar6dof_probe.
    /// </summary>
    public class Ar6dofDotsGraphic : Ar6dofRasterGfx
    {
        float[] pts = new float[0];
        int count;
        Color dot = new Color(0.2f, 1f, 0.35f, 1f);
        float dotSize = 7f;

        protected override float Supersample { get { return 1f; } }

        /// <summary>Remplace les points (x y x y...) ; 'n' = nombre de points valides.</summary>
        public void SetPoints(float[] xy, int n, Color c, float sizePx)
        {
            if (xy == null || n <= 0) { Clear(); return; }
            if (pts.Length < n * 2) pts = new float[n * 2];
            System.Array.Copy(xy, pts, n * 2);
            count = n; dot = c; dotSize = sizePx;
            SetVerticesDirty();
        }

        public void Clear() { if (count == 0) return; count = 0; SetVerticesDirty(); }

        protected override void Draw(VertexHelper vh, Rect r)
        {
            float h = dotSize * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float x = r.xMin + pts[2 * i] * r.width, y = r.yMax - pts[2 * i + 1] * r.height;
                int n = vh.currentVertCount;
                vh.AddVert(new Vector3(x - h, y - h), dot, Vector2.zero);
                vh.AddVert(new Vector3(x + h, y - h), dot, Vector2.zero);
                vh.AddVert(new Vector3(x + h, y + h), dot, Vector2.zero);
                vh.AddVert(new Vector3(x - h, y + h), dot, Vector2.zero);
                vh.AddTriangle(n, n + 1, n + 2);
                vh.AddTriangle(n, n + 2, n + 3);
            }
        }
    }
}
