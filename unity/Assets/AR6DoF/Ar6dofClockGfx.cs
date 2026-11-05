using System;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Base des dessins de l'horloge : aucune texture, tout est calculé. Les couleurs suivent le style choisi (texte, accent, boutons).</summary>
    public abstract class Ar6dofClockGfx : Ar6dofRasterGfx
    {
        protected Color cText = Color.white, cAccent = new Color(1f, 0.9f, 0f), cDim = new Color(1f, 1f, 1f, 0.25f), cFace = new Color(0.15f, 0.15f, 0.15f, 1f);
        Ar6dofElem elem = Ar6dofElem.Menus;

        protected override Color ClearRgb { get { return cText; } }

        protected override void OnEnable()
        {
            base.OnEnable();
            elem = Ar6dofTheme.ElemOf(transform);
            Ar6dofSettings.Changed += Restyle;
            Restyle();
        }

        protected override void OnDisable()
        {
            Ar6dofSettings.Changed -= Restyle;
            base.OnDisable();
        }

        void Restyle()
        {
            var st = Ar6dofSettings.StyleFor(elem);
            cText = st.text;
            cAccent = st.accent;
            cDim = st.text; cDim.a = 0.25f;
            cFace = Color.Lerp(st.button, Color.black, 0.15f); cFace.a = 1f;
            SetVerticesDirty();
        }

        protected static Vector2 Dir(float turn)   // 0 = midi (haut), 0.25 = 3 h, sens des aiguilles d'une montre
        {
            float a = (0.25f - turn) * 2f * Mathf.PI;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        /// <summary>Anneau entre deux rayons, de 'from' à 'to' (en tours, 0 = en haut, sens horaire).</summary>
        protected static void Ring(VertexHelper vh, Vector2 c, float rIn, float rOut, float from, float to, Color col, int seg = 48)
        {
            if (to <= from) return;
            WxDraw.Arc(vh, c, rIn, rOut, 90f - to * 360f, 90f - from * 360f, col, Mathf.Max(2, (int)(seg * (to - from))));
        }

        protected static void Needle(VertexHelper vh, Vector2 c, float turn, float tail, float len, float w, Color col)
        {
            Vector2 d = Dir(turn);
            WxDraw.Line(vh, c - d * tail, c + d * len, w, col);
        }

        protected static void Taper(VertexHelper vh, Vector2 c, float turn, float tail, float len, float w, Color col)
        {
            Vector2 d = Dir(turn), n = new Vector2(-d.y, d.x) * (w * 0.5f);
            WxDraw.Quad(vh, c - d * tail + n * 0.6f, c + n, c + d * len, c - n, col);
            WxDraw.Quad(vh, c - d * tail - n * 0.6f, c - d * tail + n * 0.6f, c + n, c - n, col);
        }
    }

    /// <summary>Cadran analogique : 6 visuels (classique, minimal, moderne, barres, points, anneaux). Redessiné seulement quand l'heure affichée change.</summary>
    public class Ar6dofClockFace : Ar6dofClockGfx
    {
        public const int Styles = 6;
        public static readonly string[] StyleNames = { "Classique", "Minimal", "Moderne", "Barres", "Points", "Anneaux" };
        double sod;
        int style;
        long lastQ = -1;
        int quant = 1;       // images par seconde pour les aiguilles (1 = tic par seconde, 10 = balayage fluide)
        bool showSeconds = true;

        public void Configure(int styleIndex, bool seconds, int stepsPerSecond)
        {
            style = Mathf.Clamp(styleIndex, 0, Styles - 1);
            showSeconds = seconds;
            quant = Mathf.Clamp(stepsPerSecond, 1, 30);
            lastQ = -1;
            SetVerticesDirty();
        }

        /// <summary>Secondes écoulées depuis minuit. Ne redessine que si l'affichage change.</summary>
        public void SetTime(double secondsOfDay)
        {
            sod = secondsOfDay;
            long q = (long)(secondsOfDay * quant);
            if (q == lastQ) return;
            lastQ = q;
            SetVerticesDirty();
        }

        protected override void Draw(VertexHelper vh, Rect rc)
        {
            float R = Mathf.Min(rc.width, rc.height) * 0.5f - 1f;
            if (R < 6f) return;
            Vector2 c = rc.center;
            double q = Math.Floor(sod * quant) / quant;
            float hTurn = (float)((q / 3600.0) % 12.0 / 12.0);
            float mTurn = (float)((q / 60.0) % 60.0 / 60.0);
            float sTurn = (float)(q % 60.0 / 60.0);
            int sec = (int)(q % 60.0);

            switch (style)
            {
                case 0:   // classique
                    WxDraw.Disc(vh, c, R, cFace, 48);
                    Ring(vh, c, R * 0.96f, R, 0f, 1f, cText, 64);
                    for (int i = 0; i < 60; i++)
                    {
                        bool big = i % 5 == 0;
                        Vector2 d = Dir(i / 60f);
                        WxDraw.Line(vh, c + d * (R * (big ? 0.78f : 0.88f)), c + d * (R * 0.93f), big ? R * 0.055f : R * 0.02f, big ? cText : cDim);
                    }
                    Taper(vh, c, hTurn, R * 0.1f, R * 0.5f, R * 0.12f, cText);
                    Taper(vh, c, mTurn, R * 0.12f, R * 0.74f, R * 0.085f, cText);
                    if (showSeconds) Needle(vh, c, sTurn, R * 0.18f, R * 0.84f, R * 0.025f, cAccent);
                    WxDraw.Disc(vh, c, R * 0.06f, showSeconds ? cAccent : cText, 12);
                    break;

                case 1:   // minimal
                    for (int i = 0; i < 12; i += 3) WxDraw.Disc(vh, c + Dir(i / 12f) * (R * 0.86f), R * 0.045f, cText, 8);
                    for (int i = 0; i < 12; i++) if (i % 3 != 0) WxDraw.Disc(vh, c + Dir(i / 12f) * (R * 0.86f), R * 0.02f, cDim, 6);
                    Needle(vh, c, hTurn, 0f, R * 0.46f, R * 0.09f, cText);
                    WxDraw.Disc(vh, c, R * 0.045f, cText, 8);
                    WxDraw.Disc(vh, c + Dir(hTurn) * (R * 0.46f), R * 0.045f, cText, 8);
                    Needle(vh, c, mTurn, 0f, R * 0.7f, R * 0.06f, cText);
                    WxDraw.Disc(vh, c + Dir(mTurn) * (R * 0.7f), R * 0.03f, cText, 8);
                    if (showSeconds) WxDraw.Disc(vh, c + Dir(sTurn) * (R * 0.94f), R * 0.05f, cAccent, 10);
                    break;

                case 2:   // moderne : anneau des secondes
                    Ring(vh, c, R * 0.90f, R, 0f, 1f, cDim, 64);
                    if (showSeconds) Ring(vh, c, R * 0.90f, R, 0f, Mathf.Max(0.004f, sTurn), cAccent, 64);
                    for (int i = 0; i < 12; i++) WxDraw.Disc(vh, c + Dir(i / 12f) * (R * 0.74f), i % 3 == 0 ? R * 0.035f : R * 0.018f, i % 3 == 0 ? cText : cDim, 8);
                    Needle(vh, c, hTurn, 0f, R * 0.42f, R * 0.11f, cText);
                    Needle(vh, c, mTurn, 0f, R * 0.64f, R * 0.075f, cText);
                    WxDraw.Disc(vh, c, R * 0.07f, cFace, 12);
                    WxDraw.Disc(vh, c, R * 0.045f, cAccent, 10);
                    break;

                case 3:   // barres : 60 traits, celui de la seconde en cours est allumé
                    for (int i = 0; i < 60; i++)
                    {
                        Vector2 d = Dir(i / 60f);
                        bool big = i % 5 == 0;
                        bool on = showSeconds && i <= sec;
                        WxDraw.Line(vh, c + d * (R * (big ? 0.80f : 0.86f)), c + d * (R * 0.98f), R * 0.03f, on ? cAccent : (big ? cText : cDim));
                    }
                    Needle(vh, c, hTurn, 0f, R * 0.44f, R * 0.09f, cText);
                    Needle(vh, c, mTurn, 0f, R * 0.66f, R * 0.06f, cText);
                    WxDraw.Disc(vh, c, R * 0.05f, cText, 10);
                    break;

                case 4:   // points : l'heure en cours est mise en valeur
                {
                    int hh = (int)(hTurn * 12f) % 12;
                    for (int i = 0; i < 12; i++)
                        WxDraw.Disc(vh, c + Dir(i / 12f) * (R * 0.84f), i == hh ? R * 0.10f : R * 0.04f, i == hh ? cAccent : cText, 12);
                    Needle(vh, c, hTurn, 0f, R * 0.40f, R * 0.07f, cText);
                    Needle(vh, c, mTurn, 0f, R * 0.62f, R * 0.045f, cText);
                    if (showSeconds) WxDraw.Disc(vh, c + Dir(sTurn) * (R * 0.62f), R * 0.03f, cAccent, 8);
                    WxDraw.Disc(vh, c, R * 0.045f, cText, 8);
                    break;
                }

                default:  // anneaux : heures, minutes, secondes
                {
                    float w = R * 0.15f;
                    Ring(vh, c, R - w, R, 0f, 1f, cDim, 64);
                    Ring(vh, c, R - w, R, 0f, Mathf.Max(0.004f, hTurn), cAccent, 64);
                    float r2 = R - w * 1.4f;
                    Ring(vh, c, r2 - w, r2, 0f, 1f, cDim, 64);
                    Ring(vh, c, r2 - w, r2, 0f, Mathf.Max(0.004f, mTurn), cText, 64);
                    if (showSeconds)
                    {
                        float r3 = r2 - w * 1.4f;
                        Ring(vh, c, r3 - w, r3, 0f, 1f, cDim, 64);
                        Ring(vh, c, r3 - w, r3, 0f, Mathf.Max(0.004f, sTurn), cAccent, 64);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Visuels de compte à rebours / progression : anneau, camembert, points, sablier, barre. 'remaining' va de 1 (plein) à 0 (fini).</summary>
    public class Ar6dofProgressGfx : Ar6dofClockGfx
    {
        public const int Styles = 5;
        public static readonly string[] StyleNames = { "Anneau", "Camembert", "Points", "Sablier", "Barre" };
        int style;
        float remaining = 1f;
        bool alert;
        int lastQ = -1;

        public void SetStyle(int s)
        {
            style = Mathf.Clamp(s, 0, Styles - 1);
            lastQ = -1;
            SetVerticesDirty();
        }

        /// <summary>'alert' = alarme en cours : le dessin passe en rouge. Ne redessine que si l'aspect change d'au moins 1/400.</summary>
        public void SetRemaining(float r, bool isAlert)
        {
            r = Mathf.Clamp01(r);
            int q = (int)(r * 400f) + (isAlert ? 1000 : 0);
            remaining = r;
            alert = isAlert;
            if (q == lastQ) return;
            lastQ = q;
            SetVerticesDirty();
        }

        protected override void Draw(VertexHelper vh, Rect rc)
        {
            float W = rc.width, H = rc.height, R = Mathf.Min(W, H) * 0.5f - 1f;
            if (R < 6f) return;
            Vector2 c = rc.center;
            Color on = alert ? new Color(1f, 0.32f, 0.28f) : cAccent;
            switch (style)
            {
                case 0:
                    Ring(vh, c, R * 0.86f, R, 0f, 1f, cDim, 64);
                    if (remaining > 0.002f) Ring(vh, c, R * 0.86f, R, 0f, remaining, on, 64);
                    if (remaining > 0.002f && remaining < 0.999f) WxDraw.Disc(vh, c + Dir(remaining) * (R * 0.93f), R * 0.075f, on, 10);
                    break;
                case 1:
                    WxDraw.Disc(vh, c, R, cDim, 48);
                    if (remaining > 0.002f)
                        WxDraw.Arc(vh, c, 0f, R * 0.97f, 90f - remaining * 360f, 90f, on, Mathf.Max(2, (int)(64 * remaining)));
                    break;
                case 2:
                {
                    int lit = Mathf.CeilToInt(remaining * 60f - 0.001f);
                    for (int i = 0; i < 60; i++)
                        WxDraw.Disc(vh, c + Dir(i / 60f) * (R * 0.9f), i % 5 == 0 ? R * 0.05f : R * 0.032f, i < lit ? on : cDim, 8);
                    break;
                }
                case 3:
                {
                    float hh = Mathf.Min(H * 0.5f - 2f, W * 0.95f), ww = hh * 0.62f;
                    Vector2 neck = c;
                    // cadre
                    WxDraw.Tri(vh, neck, neck + new Vector2(-ww, hh), neck + new Vector2(ww, hh), cDim);
                    WxDraw.Tri(vh, neck, neck + new Vector2(ww, -hh), neck + new Vector2(-ww, -hh), cDim);
                    WxDraw.Rect(vh, neck + new Vector2(-ww * 1.08f, hh), neck + new Vector2(ww * 1.08f, hh + 3f), cText);
                    WxDraw.Rect(vh, neck + new Vector2(-ww * 1.08f, -hh - 3f), neck + new Vector2(ww * 1.08f, -hh), cText);
                    float top = Mathf.Sqrt(remaining), bot = Mathf.Sqrt(1f - remaining);
                    if (top > 0.02f) WxDraw.Tri(vh, neck, neck + new Vector2(-ww * top, hh * top), neck + new Vector2(ww * top, hh * top), on);
                    if (bot > 0.02f) WxDraw.Tri(vh, neck + new Vector2(0f, -hh + hh * bot), neck + new Vector2(-ww * bot, -hh), neck + new Vector2(ww * bot, -hh), on);
                    if (remaining > 0.01f && remaining < 0.99f) WxDraw.Line(vh, neck, neck + new Vector2(0f, -hh + hh * bot), Mathf.Max(2f, hh * 0.015f), on);
                    break;
                }
                default:
                {
                    float bh = Mathf.Min(H * 0.5f, 30f);
                    float x0 = rc.xMin + 2f, x1 = rc.xMax - 2f;
                    Vector2 a = new Vector2(x0 + bh * 0.5f, c.y), b = new Vector2(x1 - bh * 0.5f, c.y);
                    WxDraw.Pill(vh, a, b, bh, cDim);
                    if (remaining > 0.002f)
                    {
                        Vector2 e = Vector2.Lerp(a, b, remaining);
                        WxDraw.Pill(vh, a, e, bh * 0.82f, on);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Cadran du chronomètre : grande trotteuse sur 60 secondes et petit compteur de minutes.</summary>
    public class Ar6dofSweepDial : Ar6dofClockGfx
    {
        double elapsed;
        long lastQ = -1;

        public void SetElapsed(double sec)
        {
            elapsed = sec;
            long q = (long)(sec * 30.0);
            if (q == lastQ) return;
            lastQ = q;
            SetVerticesDirty();
        }

        protected override void Draw(VertexHelper vh, Rect rc)
        {
            float R = Mathf.Min(rc.width, rc.height) * 0.5f - 1f;
            if (R < 6f) return;
            Vector2 c = rc.center;
            WxDraw.Disc(vh, c, R, cFace, 48);
            Ring(vh, c, R * 0.96f, R, 0f, 1f, cText, 64);
            for (int i = 0; i < 60; i++)
            {
                bool big = i % 5 == 0;
                Vector2 d = Dir(i / 60f);
                WxDraw.Line(vh, c + d * (R * (big ? 0.80f : 0.88f)), c + d * (R * 0.93f), big ? R * 0.045f : R * 0.018f, big ? cText : cDim);
            }
            // petit compteur de minutes (30 minutes), en bas
            Vector2 sc = c + new Vector2(0f, -R * 0.38f);
            float sr = R * 0.24f;
            Ring(vh, sc, sr * 0.92f, sr, 0f, 1f, cDim, 40);
            for (int i = 0; i < 6; i++) WxDraw.Line(vh, sc + Dir(i / 6f) * (sr * 0.72f), sc + Dir(i / 6f) * (sr * 0.9f), R * 0.02f, cText);
            float minTurn = (float)((elapsed / 60.0) % 30.0 / 30.0);
            Needle(vh, sc, minTurn, 0f, sr * 0.82f, R * 0.025f, cText);
            WxDraw.Disc(vh, sc, R * 0.02f, cText, 6);
            // grande trotteuse
            float sTurn = (float)(elapsed % 60.0 / 60.0);
            Needle(vh, c, sTurn, R * 0.16f, R * 0.88f, R * 0.03f, cAccent);
            WxDraw.Disc(vh, c, R * 0.06f, cAccent, 12);
        }
    }

    /// <summary>Icône de batterie. Sans batterie (PC fixe) : une prise secteur. Verte, orange sous 30 %, rouge sous 15 % ; éclair quand elle charge.</summary>
    public class Ar6dofBatteryGfx : Ar6dofClockGfx
    {
        bool has, charging;
        float pct = 1f;
        int lastQ = -1;

        public void Set(bool hasBattery, float fraction, bool isCharging)
        {
            float f = float.IsNaN(fraction) ? 1f : Mathf.Clamp01(fraction);
            int q = (int)(f * 100f) + (hasBattery ? 1000 : 0) + (isCharging ? 2000 : 0);
            has = hasBattery; pct = f; charging = isCharging;
            if (q == lastQ) return;
            lastQ = q;
            SetVerticesDirty();
        }

        public static Color LevelColor(float f)
        {
            if (f < 0.15f) return new Color(1f, 0.30f, 0.25f);
            if (f < 0.30f) return new Color(1f, 0.66f, 0.15f);
            return new Color(0.32f, 0.85f, 0.45f);
        }

        protected override void Draw(VertexHelper vh, Rect rc)
        {
            if (rc.width < 8f || rc.height < 8f) return;
            Vector2 c = rc.center;
            float w = Mathf.Min(rc.width, rc.height * 1.9f), h = w / 1.9f;
            if (!has)
            {
                // prise secteur
                float u = Mathf.Min(rc.width, rc.height) / 100f;
                WxDraw.Rect(vh, c + new Vector2(-22f, -6f) * u, c + new Vector2(22f, 34f) * u, cText);
                WxDraw.Rect(vh, c + new Vector2(-14f, 34f) * u, c + new Vector2(-6f, 48f) * u, cText);
                WxDraw.Rect(vh, c + new Vector2(6f, 34f) * u, c + new Vector2(14f, 48f) * u, cText);
                WxDraw.Rect(vh, c + new Vector2(-6f, -30f) * u, c + new Vector2(6f, -6f) * u, cText);
                WxDraw.Line(vh, c + new Vector2(0f, -30f) * u, c + new Vector2(0f, -48f) * u, 8f * u, cText);
                return;
            }
            float t = Mathf.Max(2f, h * 0.09f);
            float bodyW = w * 0.92f;
            Vector2 min = c + new Vector2(-w * 0.5f, -h * 0.5f), max = c + new Vector2(-w * 0.5f + bodyW, h * 0.5f);
            // contour
            WxDraw.Rect(vh, min, new Vector2(max.x, min.y + t), cText);
            WxDraw.Rect(vh, new Vector2(min.x, max.y - t), max, cText);
            WxDraw.Rect(vh, min, new Vector2(min.x + t, max.y), cText);
            WxDraw.Rect(vh, new Vector2(max.x - t, min.y), max, cText);
            // borne
            WxDraw.Rect(vh, new Vector2(max.x, c.y - h * 0.17f), new Vector2(max.x + w * 0.08f, c.y + h * 0.17f), cText);
            // niveau
            float pad = t * 1.7f;
            float fx0 = min.x + pad, fx1 = max.x - pad;
            Color lv = LevelColor(pct);
            if (pct > 0.005f) WxDraw.Rect(vh, new Vector2(fx0, min.y + pad), new Vector2(Mathf.Lerp(fx0, fx1, pct), max.y - pad), lv);
            if (charging)
            {
                float u = h / 100f;
                Color bolt = new Color(0.05f, 0.05f, 0.05f, 0.92f);
                WxDraw.Tri(vh, c + new Vector2(6f, 36f) * u, c + new Vector2(-10f, -4f) * u, c + new Vector2(2f, -4f) * u, bolt);
                WxDraw.Tri(vh, c + new Vector2(-6f, -36f) * u, c + new Vector2(10f, 4f) * u, c + new Vector2(-2f, 4f) * u, bolt);
            }
        }
    }
}
