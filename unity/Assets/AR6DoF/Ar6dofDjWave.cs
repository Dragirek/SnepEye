using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Forme d'onde d'une platine DJ, dessinée comme sur un lecteur de DJ : bleu pour les graves, blanc-bleuté pour le reste.
    ///  - vue d'ensemble (detail = faux) : toute la piste, avec les repères et la zone de boucle ; ne se redessine que quand l'onde ou un repère change ;
    ///  - vue détaillée (detail = vrai) : 8 secondes autour de la position de lecture, qui défile, avec la grille des temps (traits plus marqués tous les 4 temps).
    /// Les données viennent de DjDeck (50 points par seconde). Aucun calcul tant que la fenêtre n'est pas ouverte sur les platines.
    /// </summary>
    public class Ar6dofDjWave : Ar6dofRasterGfx
    {
        public DjDeck deck;
        public bool detail;
        public Color accent = new Color(0.2f, 0.8f, 1f);
        const float WinSec = 8f;
        float pos, lastReq = -1f;
        int seenWave = -1, seenCue = -1;
        static readonly Color[] HotCol =
        {
            new Color(0.95f, 0.30f, 0.30f), new Color(0.35f, 0.90f, 0.40f), new Color(0.98f, 0.85f, 0.25f), new Color(0.75f, 0.45f, 0.98f)
        };
        static readonly Color CueCol = new Color(1f, 0.58f, 0.15f);

        protected override float Supersample { get { return 1f; } }
        protected override Color ClearRgb { get { return new Color(0.04f, 0.05f, 0.08f); } }

        public static Ar6dofDjWave Make(Transform parent, Vector2 pos, Vector2 size, DjDeck d, bool detail, Color accent)
        {
            var rt = Ar6dofVrUi.Rect(parent, detail ? "Onde detail" : "Onde", pos, size);
            var g = rt.gameObject.AddComponent<Ar6dofDjWave>();
            g.raycastTarget = false;
            g.deck = d;
            g.detail = detail;
            g.accent = accent;
            return g;
        }

        /// <summary>À appeler à chaque image par la fenêtre : demande un redessin seulement si quelque chose a changé.</summary>
        public void Tick(float position, float now)
        {
            pos = position;
            if (deck == null) return;
            bool need = deck.waveVersion != seenWave || deck.cueVersion != seenCue;
            if (detail && deck.waveTotal > 0 && now - lastReq > 0.033f && (deck.playing || Mathf.Abs(position - lastPos) > 0.002f)) need = true;
            if (!need) return;
            seenWave = deck.waveVersion; seenCue = deck.cueVersion; lastReq = now; lastPos = position;
            SetVerticesDirty();
        }
        float lastPos = -999f;

        // ------------------------------------------------------------------ primitives
        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, ca, Vector2.zero);
            vh.AddVert(b, cb, Vector2.zero);
            vh.AddVert(c, cc, Vector2.zero);
            vh.AddVert(d, cd, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
        static void Box(VertexHelper vh, float x0, float y0, float x1, float y1, Color c)
        {
            Quad(vh, new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0), c, c, c, c);
        }
        static void VLine(VertexHelper vh, float x, float y0, float y1, float w, Color c) { Box(vh, x - w * 0.5f, y0, x + w * 0.5f, y1, c); }
        static Color A(Color c, float a) { c.a = a; return c; }

        static readonly Color Hi = new Color(0.82f, 0.93f, 1f), Lo = new Color(0.10f, 0.38f, 1f);
        static Color WaveColor(float lowRatio) { return Color.Lerp(Hi, Lo, Mathf.Clamp01((lowRatio - 0.25f) / 0.6f)); }

        // ------------------------------------------------------------------ dessin
        protected override void Draw(VertexHelper vh, Rect r)
        {
            Box(vh, r.xMin, r.yMin, r.xMax, r.yMax, new Color(0.04f, 0.05f, 0.08f, 1f));
            if (deck == null || deck.waveTotal <= 0 || deck.dur <= 0f) return;
            if (detail) Detail(vh, r); else Overview(vh, r);
        }

        void Overview(VertexHelper vh, Rect r)
        {
            int total = deck.waveTotal;
            int cols = Mathf.Max(1, Mathf.Min(Mathf.FloorToInt(r.width / 2f), total));
            float step = total / (float)cols, cw = r.width / cols, cy = r.center.y, half = r.height * 0.46f;
            for (int c = 0; c < cols; c++)
            {
                int i0 = (int)(c * step), i1 = Mathf.Min(total, Mathf.Max(i0 + 1, (int)((c + 1) * step)));
                float pk = 0f, lr = 0f;
                for (int i = i0; i < i1; i++)
                {
                    pk = Mathf.Max(pk, deck.peak[i]);
                    lr = Mathf.Max(lr, deck.low[i]);
                }
                if (pk < 2f) continue;
                float h = Mathf.Max(1f, pk / 255f * half);
                Box(vh, r.xMin + c * cw, cy - h, r.xMin + (c + 1) * cw + 0.5f, cy + h, WaveColor(lr / 255f));
            }
            Marks(vh, r, 0f, deck.dur, 2f);
        }

        void Detail(VertexHelper vh, Rect r)
        {
            int total = deck.waveTotal;
            float t0 = pos - WinSec * 0.5f, pxs = r.width / WinSec, cy = r.center.y, half = r.height * 0.46f;
            int p0 = Mathf.Max(0, Mathf.FloorToInt(t0 * 50f)), p1 = Mathf.Min(total - 1, Mathf.CeilToInt((pos + WinSec * 0.5f) * 50f));
            float bw = pxs / 50f + 0.7f;
            for (int p = p0; p <= p1; p++)
            {
                float pk = deck.peak[p];
                if (pk < 2f) continue;
                float t = p / 50f, x = r.xMin + (t - t0) * pxs;
                float h = Mathf.Max(1f, pk / 255f * half);
                Color c = WaveColor(deck.low[p] / 255f);
                if (t < pos) c = Color.Lerp(c, new Color(0.04f, 0.05f, 0.08f), 0.45f);   // la partie déjà jouée est plus sombre
                Box(vh, x, cy - h, x + bw, cy + h, c);
            }
            // grille des temps
            if (deck.bpm > 0f)
            {
                float bl = deck.BeatLen;
                int k0 = Mathf.CeilToInt((t0 - deck.firstBeat) / bl), k1 = Mathf.FloorToInt((pos + WinSec * 0.5f - deck.firstBeat) / bl);
                for (int k = k0; k <= k1; k++)
                {
                    float x = r.xMin + (deck.firstBeat + k * bl - t0) * pxs;
                    bool bar = ((k % 4) + 4) % 4 == 0;
                    VLine(vh, x, r.yMin, r.yMax, bar ? 2f : 1f, new Color(1f, 1f, 1f, bar ? 0.55f : 0.22f));
                }
            }
            Marks(vh, r, t0, WinSec, 3f);
            // tête de lecture au centre
            VLine(vh, r.center.x, r.yMin, r.yMax, 2.5f, new Color(1f, 1f, 1f, 0.95f));
            float tri = r.height * 0.14f;
            Quad(vh, new Vector2(r.center.x - tri, r.yMax), new Vector2(r.center.x + tri, r.yMax), new Vector2(r.center.x, r.yMax - tri), new Vector2(r.center.x, r.yMax - tri), accent, accent, accent, accent);
        }

        // repères (cue principal, hot cues) et zone de boucle, sur la fenêtre de temps [t0, t0 + span]
        void Marks(VertexHelper vh, Rect r, float t0, float span, float lineW)
        {
            float pxs = r.width / span;
            if (deck.loop && deck.loopIn >= 0f && deck.loopOut > deck.loopIn)
            {
                float xa = Mathf.Clamp(r.xMin + (deck.loopIn - t0) * pxs, r.xMin, r.xMax), xb = Mathf.Clamp(r.xMin + (deck.loopOut - t0) * pxs, r.xMin, r.xMax);
                if (xb > xa) Box(vh, xa, r.yMin, xb, r.yMax, new Color(0.2f, 0.9f, 0.4f, 0.28f));
            }
            Mark(vh, r, deck.cue, t0, pxs, CueCol, lineW);
            for (int i = 0; i < 4; i++) if (deck.hot[i] >= 0f) Mark(vh, r, deck.hot[i], t0, pxs, HotCol[i], lineW);
        }

        static void Mark(VertexHelper vh, Rect r, float t, float t0, float pxs, Color c, float w)
        {
            float x = r.xMin + (t - t0) * pxs;
            if (x < r.xMin - 1f || x > r.xMax + 1f) return;
            VLine(vh, x, r.yMin, r.yMax, w, c);
            float f = Mathf.Min(10f, r.height * 0.28f);
            Box(vh, x, r.yMax - f, x + f, r.yMax, c);   // petit drapeau en haut
        }

        /// <summary>Couleur d'un hot cue (même teinte que son bouton).</summary>
        public static Color HotColor(int i) { return HotCol[Mathf.Clamp(i, 0, 3)]; }
        public static Color CueColor { get { return CueCol; } }
    }
}
