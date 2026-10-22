using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Étalonnage de l'eye tracking : une cible apparaît successivement à 9 endroits devant toi (collée à la tête, à 1 m) ; tu la regardes
    /// SANS bouger la tête. Pour chaque point, ar6dof_probe mesure la pupille de chaque oeil, puis calcule la correspondance
    /// position de la pupille -> angle du regard. A la fin, un point vert suit ton regard pendant quelques secondes pour vérifier.
    /// </summary>
    public class Ar6dofEyeCalibration : MonoBehaviour
    {
        // yaw / pitch en degrés par rapport à la tête : centre, puis le tour des 8 autres points
        public static readonly Vector2[] Points =
        {
            new Vector2(0f, 0f),
            new Vector2(-22f, 15f), new Vector2(0f, 15f), new Vector2(22f, 15f),
            new Vector2(22f, 0f),
            new Vector2(22f, -15f), new Vector2(0f, -15f), new Vector2(-22f, -15f),
            new Vector2(-22f, 0f),
        };

        const float Dist = 0.8f;          // mètres (un peu plus près que la barre de tâches : la cible passe devant les menus) ; le canvas fait 1000 pixels par mètre
        const int SettleMs = 900, FirstSettleMs = 1600, CollectMs = 700;

        public bool Running { get { return run != null; } }
        public string LastResult { get; private set; }

        Canvas canvas;
        RectTransform dot, gazeDot;
        Image dotImg;
        Text title, sub, cancelLabel;
        Ar6dofEyeTracker tracker;
        Ar6dofPassthrough passthrough;
        Coroutine run;
        bool stopRequested, pointDone, resultIn, verifying;
        int cntL, cntR;
        string resultLine;
        static Sprite circle;

        Transform Head()
        {
            if (passthrough == null) passthrough = FindFirstObjectByType<Ar6dofPassthrough>();
            if (passthrough != null && passthrough.HeadView != null) return passthrough.HeadView;
            return Camera.main != null ? Camera.main.transform : null;
        }

        static Sprite Circle()
        {
            if (circle != null) return circle;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01((c - r) / 1.5f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            tex.filterMode = FilterMode.Bilinear;
            circle = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            return circle;
        }

        void BuildCanvas()
        {
            if (canvas != null) return;
            canvas = Ar6dofVrUi.NewCanvas("Etalonnage des yeux", new Vector2(1400f, 900f));
            var fond = canvas.transform.Find("Fond");
            if (fond != null) { var fi = fond.GetComponent<Image>(); if (fi != null) fi.color = new Color(0.03f, 0.03f, 0.05f, 0.7f); }
            title = Ar6dofVrUi.Label(canvas.transform, "", new Vector2(0f, 395f), new Vector2(1300f, 60f), 38);
            sub = Ar6dofVrUi.Label(canvas.transform, "", new Vector2(0f, 335f), new Vector2(1300f, 50f), 26);
            sub.color = new Color(0.8f, 0.85f, 0.9f, 1f);

            var d = Ar6dofVrUi.Rect(canvas.transform, "Cible", Vector2.zero, new Vector2(70f, 70f));
            dotImg = d.gameObject.AddComponent<Image>();
            dotImg.sprite = Circle();
            dotImg.raycastTarget = false;
            dot = d;
            var core = Ar6dofVrUi.Rect(d, "Centre", Vector2.zero, new Vector2(14f, 14f));
            var ci = core.gameObject.AddComponent<Image>();
            ci.sprite = Circle();
            ci.color = Color.black;
            ci.raycastTarget = false;

            var g = Ar6dofVrUi.Rect(canvas.transform, "Regard", Vector2.zero, new Vector2(46f, 46f));
            var gi = g.gameObject.AddComponent<Image>();
            gi.sprite = Circle();
            gi.color = new Color(0.2f, 1f, 0.35f, 0.9f);
            gi.raycastTarget = false;
            gazeDot = g;
            gazeDot.gameObject.SetActive(false);

            Ar6dofVrUi.MakeButton(canvas.transform, "Annuler", new Vector2(0f, -405f), new Vector2(280f, 60f), () => stopRequested = true, out cancelLabel, 28);
            var pointer = FindFirstObjectByType<Ar6dofPointer>();
            if (pointer != null) pointer.AddCanvas(canvas);
            canvas.gameObject.SetActive(false);
        }

        public void Begin(Ar6dofEyeTracker t)
        {
            if (run != null || t == null) return;
            tracker = t;
            BuildCanvas();
            run = StartCoroutine(Run());
        }

        public void Cancel() { stopRequested = true; }

        void OnEngineMessage(string m)
        {
            var w = m.Split(' ');
            if (w.Length == 0) return;
            if (w[0] == "POINT_DONE" && w.Length >= 3)
            {
                int.TryParse(w[1], out cntL);
                int.TryParse(w[2], out cntR);
                pointDone = true;
            }
            else if (w[0] == "CAL_RESULT") { resultLine = m; resultIn = true; }
        }

        static string Fmt(float v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        void SetPoint(Vector2 p)
        {
            // 1 m = 1000 pixels : un angle devient tan(angle) * distance * 1000 pixels
            dot.anchoredPosition = new Vector2(Mathf.Tan(p.x * Mathf.Deg2Rad) * Dist * 1000f, Mathf.Tan(p.y * Mathf.Deg2Rad) * Dist * 1000f);
        }

        IEnumerator Run()
        {
            stopRequested = false;
            verifying = false;
            LastResult = "";
            canvas.gameObject.SetActive(true);
            gazeDot.gameObject.SetActive(false);
            dot.gameObject.SetActive(false);
            cancelLabel.text = "Annuler";
            tracker.EngineMessage += OnEngineMessage;
            tracker.SetEnabled(true);

            // 1) le programme doit répondre et les caméras tourner
            title.text = "Etalonnage des yeux";
            sub.text = "Attente des cameras d'oeil...";
            float t0 = Time.unscaledTime;
            while (!stopRequested && !tracker.Receiving && Time.unscaledTime - t0 < 10f) yield return null;
            if (!stopRequested && !tracker.Receiving)
            {
                LastResult = tracker.EngineOnline
                    ? "Aucune image des cameras d'oeil : verifie leurs numeros (ar6dof_probe, questions eyecamL / eyecamR)."
                    : "ar6dof_probe ne repond pas : lance-le avec 'Eye tracking = 1'.";
                title.text = "Impossible de demarrer";
                sub.text = LastResult;
                cancelLabel.text = "Fermer";
                stopRequested = false;
                t0 = Time.unscaledTime;
                while (!stopRequested && Time.unscaledTime - t0 < 8f) yield return null;
                Finish();
                yield break;
            }

            // 2) consignes puis les points
            tracker.BeginCalibration();
            title.text = "Regarde chaque cible sans bouger la tete";
            sub.text = "Garde la tete droite et immobile. Suis le point des yeux.";
            t0 = Time.unscaledTime;
            while (!stopRequested && Time.unscaledTime - t0 < 3.5f) yield return null;

            bool failed = false;
            for (int i = 0; i < Points.Length && !stopRequested && !failed; i++)
            {
                int tries = 0;
                while (!stopRequested)
                {
                    int settle = (i == 0 && tries == 0) ? FirstSettleMs : SettleMs;
                    title.text = "Point " + (i + 1) + " / " + Points.Length + (tries > 0 ? "  (on recommence)" : "");
                    sub.text = "Regarde le point noir au centre de la cible";
                    SetPoint(Points[i]);
                    dot.gameObject.SetActive(true);
                    pointDone = false;
                    tracker.Send("CAL_POINT " + Points[i].x.ToString("0.0", CultureInfo.InvariantCulture) + " " + Points[i].y.ToString("0.0", CultureInfo.InvariantCulture) + " " + settle + " " + CollectMs);
                    t0 = Time.unscaledTime;
                    float limit = (settle + CollectMs) / 1000f + 2.5f;
                    while (!stopRequested && !pointDone && Time.unscaledTime - t0 < limit)
                    {
                        float e = Time.unscaledTime - t0;
                        float s = 1f + 0.12f * Mathf.Sin(e * 8f);   // la cible pulse : elle attire le regard
                        dot.localScale = new Vector3(s, s, 1f);
                        dotImg.color = e * 1000f < settle ? new Color(1f, 0.75f, 0.2f, 1f) : new Color(0.3f, 1f, 0.5f, 1f);
                        yield return null;
                    }
                    if (stopRequested) break;
                    if (!pointDone) { failed = true; LastResult = "ar6dof_probe ne repond plus pendant l'etalonnage."; break; }
                    if (cntL >= 8 || cntR >= 8) break;   // assez de mesures sur ce point
                    if (++tries >= 2) break;             // pupille introuvable deux fois : on passe (le calcul ignore ce point)
                    sub.text = "Pupille non detectee : cligne puis regarde la cible";
                }
            }
            dot.gameObject.SetActive(false);

            if (stopRequested || failed)
            {
                tracker.CancelCalibration();
                if (!failed) LastResult = "Etalonnage annule.";
                Finish();
                yield break;
            }

            // 3) calcul
            title.text = "Calcul...";
            sub.text = "";
            resultIn = false;
            tracker.Send("CAL_SOLVE");
            t0 = Time.unscaledTime;
            while (!resultIn && Time.unscaledTime - t0 < 4f) yield return null;
            var w = (resultLine ?? "").Split(' ');
            if (!resultIn || w.Length < 3 || w[1] != "ok")
            {
                LastResult = resultIn ? "Echec : pas assez de mesures (pupille perdue ?). Verifie l'eclairage infrarouge et la fenetre 'yeux' d'ar6dof_probe." : "Pas de reponse d'ar6dof_probe.";
                title.text = "Etalonnage echoue";
                sub.text = LastResult;
                cancelLabel.text = "Fermer";
                stopRequested = false;
                t0 = Time.unscaledTime;
                while (!stopRequested && Time.unscaledTime - t0 < 10f) yield return null;
                Finish();
                yield break;
            }
            float rmsL = -1f, rmsR = -1f;
            if (w.Length >= 4) { float.TryParse(w[2], NumberStyles.Float, CultureInfo.InvariantCulture, out rmsL); float.TryParse(w[3], NumberStyles.Float, CultureInfo.InvariantCulture, out rmsR); }
            string q = (rmsL >= 0f ? "gauche " + Fmt(rmsL) + " deg" : "") + (rmsL >= 0f && rmsR >= 0f ? "  |  " : "") + (rmsR >= 0f ? "droit " + Fmt(rmsR) + " deg" : "");
            LastResult = "Etalonnage termine. Erreur moyenne : " + q;

            // 4) vérification : le point vert suit le regard
            verifying = true;
            title.text = "Etalonnage termine";
            sub.text = "Erreur moyenne : " + q + "   -   regarde autour : le point vert doit te suivre";
            cancelLabel.text = "Terminer";
            gazeDot.gameObject.SetActive(true);
            stopRequested = false;
            t0 = Time.unscaledTime;
            while (!stopRequested && Time.unscaledTime - t0 < 15f) yield return null;
            Finish();
        }

        void Finish()
        {
            if (tracker != null) tracker.EngineMessage -= OnEngineMessage;
            verifying = false;
            if (canvas != null) canvas.gameObject.SetActive(false);
            run = null;
        }

        void LateUpdate()
        {
            if (canvas == null || !canvas.gameObject.activeSelf) return;
            var h = Head();
            if (h != null) canvas.transform.SetPositionAndRotation(h.position + h.forward * Dist, h.rotation);   // collé à la tête
            if (verifying && gazeDot != null && tracker != null)
            {
                var g = tracker.Gaze;
                if (g.valid)
                {
                    float x = Mathf.Clamp(Mathf.Tan(Mathf.Clamp(g.yaw, -40f, 40f) * Mathf.Deg2Rad) * Dist * 1000f, -650f, 650f);
                    float y = Mathf.Clamp(Mathf.Tan(Mathf.Clamp(g.pitch, -30f, 30f) * Mathf.Deg2Rad) * Dist * 1000f, -400f, 400f);
                    gazeDot.anchoredPosition = new Vector2(x, y);
                }
            }
        }

        void OnDisable()
        {
            if (run != null) { StopCoroutine(run); run = null; }
            if (tracker != null) tracker.EngineMessage -= OnEngineMessage;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }
    }
}
