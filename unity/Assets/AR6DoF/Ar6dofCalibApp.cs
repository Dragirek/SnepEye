using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Calibration guidée de la caméra stéréo : on mesure la grille, on la montre à la caméra sous une quinzaine de poses (conseils dessinés, barre de
    /// progression, aperçu avec les coins détectés), ar6dof_probe calcule (distorsion, focale, écart entre les caméras), note le résultat, puis on
    /// vérifie l'échelle avec une règle. Tout le calcul est dans ar6dof_probe (calib_session.h) ; cette fenêtre ne fait que commander et afficher.
    /// Fermée, elle ne lit aucun fichier. Étapes : Préparation, Capture, Calcul, Résultat, Vérification à la règle.
    /// </summary>
    public class Ar6dofCalibApp : Ar6dofAppWindow
    {
        enum Step { Prep, Capture, Compute, Result, Verify }

        struct Goal
        {
            public string name, title, tip;
            public float px, py, size, yaw, pitch, roll;   // où et comment montrer la mire (py positif = vers le haut)
        }

        static Goal G(string name, string title, string tip, float px, float py, float size, float yaw, float pitch, float roll)
        {
            return new Goal { name = name, title = title, tip = tip, px = px, py = py, size = size, yaw = yaw, pitch = pitch, roll = roll };
        }

        // Même ordre que l'énumération Goal de calib_session.h
        static readonly Goal[] Goals =
        {
            G("Centre pres", "Au centre, de pres", "Tiens la mire bien a plat au centre de l'image, assez pres pour qu'elle en remplisse plus de la moitie de la largeur.", 0f, 0f, 0.62f, 0f, 0f, 0f),
            G("Coin haut G", "Dans le coin en haut a gauche", "Place la mire de face dans le coin haut gauche. Les bords de l'image sont ou l'objectif deforme le plus : ils comptent beaucoup.", -0.55f, 0.55f, 0.34f, 0f, 0f, 0f),
            G("Coin haut D", "Dans le coin en haut a droite", "Place la mire de face dans le coin haut droit de l'image, sans que ses coins sortent de l'image.", 0.55f, 0.55f, 0.34f, 0f, 0f, 0f),
            G("Coin bas D", "Dans le coin en bas a droite", "Place la mire de face dans le coin bas droit de l'image, sans que ses coins sortent de l'image.", 0.55f, -0.55f, 0.34f, 0f, 0f, 0f),
            G("Coin bas G", "Dans le coin en bas a gauche", "Place la mire de face dans le coin bas gauche de l'image, sans que ses coins sortent de l'image.", -0.55f, -0.55f, 0.34f, 0f, 0f, 0f),
            G("Centre loin", "Au centre, de loin", "Recule : la mire doit rester petite au centre (moins du tiers de la largeur). C'est ce qui donne la bonne mesure des distances.", 0f, 0f, 0.24f, 0f, 0f, 0f),
            G("Gauche vers toi", "Cote gauche vers toi", "Tourne la mire autour de la verticale : son cote gauche (vu de la camera) plus pres de toi que le droit, de 30 a 45 degres.", 0f, 0f, 0.46f, -42f, 0f, 0f),
            G("Droite vers toi", "Cote droit vers toi", "Tourne la mire autour de la verticale : son cote droit (vu de la camera) plus pres de toi que le gauche, de 30 a 45 degres.", 0f, 0f, 0.46f, 42f, 0f, 0f),
            G("Haut vers toi", "Haut penche vers toi", "Penche la mire : le haut plus pres de toi que le bas, de 30 a 45 degres. Reste de face dans l'autre sens.", 0f, 0f, 0.46f, 0f, -38f, 0f),
            G("Bas vers toi", "Bas penche vers toi", "Penche la mire : le bas plus pres de toi que le haut, de 30 a 45 degres. Reste de face dans l'autre sens.", 0f, 0f, 0.46f, 0f, 38f, 0f),
            G("Tres pres", "Tres pres", "Approche la mire jusqu'a ce qu'elle remplisse presque toute la largeur de l'image, en gardant tous ses coins visibles.", 0f, 0f, 0.88f, 0f, 0f, 0f),
            G("En diagonale", "Tournee a 45 degres", "Fais tourner la mire dans son plan, comme un losange : environ 45 degres, au milieu de l'image.", 0f, 0f, 0.42f, 0f, 0f, 45f),
            G("Bord gauche", "Contre le bord gauche", "Pousse la mire contre le bord gauche de l'image, sans que ses coins sortent de l'image.", -0.6f, 0f, 0.34f, 0f, 0f, 0f),
            G("Bord droit", "Contre le bord droit", "Pousse la mire contre le bord droit de l'image, sans que ses coins sortent de l'image.", 0.6f, 0f, 0.34f, 0f, 0f, 0f),
            G("Bord haut", "Contre le bord du haut", "Pousse la mire contre le haut de l'image, sans que ses coins sortent de l'image.", 0f, 0.6f, 0.34f, 0f, 0f, 0f),
            G("Bord bas", "Contre le bord du bas", "Pousse la mire contre le bas de l'image, sans que ses coins sortent de l'image.", 0f, -0.6f, 0.34f, 0f, 0f, 0f),
        };

        // nombre de carreaux (largeur, hauteur) ; les coins interieurs sont (n-1, m-1), de parites differentes comme le demande la detection
        static readonly int[][] Presets = { new[] { 10, 7 }, new[] { 9, 6 }, new[] { 11, 8 }, new[] { 8, 5 } };
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        readonly Ar6dofCalibLink link;
        Step built = (Step)(-1);
        bool wantVerify;
        int preset;
        float totalMm = 250f, hintMm, verifyMm = 500f;
        bool startPending;
        float startAt;
        float reloadAt;
        int reloadStage;
        bool prevApplied;
        int lastAcceptSeq = -1;
        float flashUntil;
        int verifySentSeq = -1;
        float verifySentAt = -1f;
        float aliveUntil;
        bool alive;

        // elements de la vue en cours
        RawImage preview;
        Ar6dofDotsGraphic dots;
        Ar6dofBoardIcon ghost, tipIcon;
        float[] dotBuf = new float[0];

        public Ar6dofCalibApp(Ar6dofDock d) : base(d, "calibration", "Calibration camera", "calib", "Calibration guidee de la camera : distorsion, focale et distance entre les cameras", 760f)
        {
            link = new Ar6dofCalibLink(d);
            preset = Mathf.Clamp(GetI("preset", 0), 0, Presets.Length - 1);
            totalMm = GetF("total", 250f);
            hintMm = GetF("hint", 0f);
            verifyMm = GetF("verify", 500f);
        }

        static int GetI(string k, int def) { try { return PlayerPrefs.GetInt("ar6dof_cal_" + k, def); } catch (Exception) { return def; } }
        static float GetF(string k, float def) { try { return PlayerPrefs.GetFloat("ar6dof_cal_" + k, def); } catch (Exception) { return def; } }
        static void PutI(string k, int v) { try { PlayerPrefs.SetInt("ar6dof_cal_" + k, v); } catch (Exception) { } }
        static void PutF(string k, float v) { try { PlayerPrefs.SetFloat("ar6dof_cal_" + k, v); } catch (Exception) { } }

        static string N1(float v) { return v.ToString("0.0", Ci); }
        static string N2(float v) { return v.ToString("0.00", Ci); }
        static string N0(float v) { return Mathf.RoundToInt(v).ToString(Ci); }
        static string T(string fr) { return Ar6dofLoc.T(fr); }

        CalStatusJson S { get { return link.Status; } }
        int SquaresX { get { return Presets[preset][0]; } }
        int SquaresY { get { return Presets[preset][1]; } }
        float SquareMm { get { return totalMm / SquaresX; } }

        // ------------------------------------------------------------------ etat
        bool ProbeAlive()
        {
            if (Time.unscaledTime < aliveUntil) return alive;
            aliveUntil = Time.unscaledTime + 1.5f;
            alive = link.ProbeRunning;
            if (!alive)
            {
                try { alive = System.Diagnostics.Process.GetProcessesByName("ar6dof_probe").Length > 0; }
                catch (Exception) { alive = false; }
            }
            return alive;
        }

        Step Cur()
        {
            if (!ProbeAlive()) return Step.Prep;
            switch (S.state)
            {
                case "capture": return Step.Capture;
                case "compute": return Step.Compute;
                case "done": return wantVerify ? Step.Verify : Step.Result;
                case "error": return Step.Result;
                default: return Step.Prep;
            }
        }

        protected override void OnVisible(bool on)
        {
            if (on) { aliveUntil = 0f; link.Poll(true); }
        }

        // fermee en pleine capture : on rend la main a la sonde (le suivi reprend). Pas pendant le calcul : l'annuler bloquerait la sonde jusqu'a sa fin.
        public override void OnClosed()
        {
            if (S.state == "capture" || S.state == "done" || S.state == "error") link.Send("CALCANCEL");
            wantVerify = false;
            startPending = false;
        }

        protected override void OnRebuildStart() { preview = null; dots = null; ghost = null; tipIcon = null; }

        protected override void OnTick()
        {
            bool changed = link.Poll();
            var cur = Cur();
            if (cur != built) { Rebuild(); return; }
            if (changed) Refresh();

            bool applied = S.applied;
            if (applied && !prevApplied) { reloadAt = Time.unscaledTime + 0.4f; reloadStage = 0; }
            prevApplied = applied;
            if (reloadAt > 0f && Time.unscaledTime >= reloadAt)
            {
                if (dock.passthrough != null) dock.passthrough.ReloadCalibration();
                reloadStage++;
                reloadAt = reloadStage < 2 ? Time.unscaledTime + 1.5f : 0f;   // deux relectures : au cas ou le fichier arrive en retard
            }
        }

        void ScheduleReload() { reloadAt = Time.unscaledTime + 1.0f; reloadStage = 0; }

        // ------------------------------------------------------------------ construction
        protected override void BuildMain()
        {
            link.Poll(true);
            built = Cur();
            if (built != Step.Capture) { preview = null; dots = null; ghost = null; tipIcon = null; }
            switch (built)
            {
                case Step.Capture: BuildCapture(); break;
                case Step.Compute: BuildCompute(); break;
                case Step.Result: BuildResult(); break;
                case Step.Verify: BuildVerify(); break;
                default: BuildPrep(); break;
            }
        }

        // -------- petits outils d'interface
        static Text Wrap(Transform parent, string text, Vector2 pos, Vector2 size, int fs, TextAnchor a)
        {
            var t = Ar6dofVrUi.Label(parent, text, pos, size, fs, a);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        void Line(Func<string> text, float h = 30f, int fs = 20)
        {
            var t = Lbl("", h, fs);
            refreshers.Add(() => t.text = text());
        }

        void ParaLine(Func<string> text, float h, int fs = 18)
        {
            var t = Para("", h, fs);
            refreshers.Add(() => t.text = text());
        }

        void ProgressBar(Func<float> value, Func<string> text, float h = 38f)
        {
            float cy = RowY(h);
            var bg = Ar6dofVrUi.Rect(content, "Progression", new Vector2(0f, cy), new Vector2(IW, h));
            var bi = bg.gameObject.AddComponent<Image>();
            bi.raycastTarget = false;
            Ar6dofTheme.Attach(bg.gameObject, Ar6dofRole.Track, 14);
            var fill = Ar6dofVrUi.Rect(bg, "Remplissage", Vector2.zero, Vector2.zero);
            fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(0f, 0f);
            var fi = fill.gameObject.AddComponent<Image>();
            fi.raycastTarget = false;
            Ar6dofTheme.Attach(fill.gameObject, Ar6dofRole.Accent, 14);
            var lab = Ar6dofVrUi.Label(bg, "", Vector2.zero, new Vector2(IW, h), 21, TextAnchor.MiddleCenter);
            lab.fontStyle = FontStyle.Bold;
            refreshers.Add(() =>
            {
                float v = Mathf.Clamp01(value());
                fill.sizeDelta = new Vector2(Mathf.Max(0f, IW * v), 0f);
                fill.gameObject.SetActive(v > 0.01f);
                lab.text = text();
            });
        }

        Ar6dofBoardIcon MakeBoard(Transform parent, Vector2 pos, Vector2 size, float alpha)
        {
            var rt = Ar6dofVrUi.Rect(parent, "Mire", pos, size);
            var bi = rt.gameObject.AddComponent<Ar6dofBoardIcon>();
            bi.raycastTarget = false;
            bi.squaresX = Mathf.Clamp(S.cols > 0 ? S.cols + 1 : SquaresX, 3, 14);
            bi.squaresY = Mathf.Clamp(S.rows > 0 ? S.rows + 1 : SquaresY, 3, 14);
            bi.alpha = alpha;
            return bi;
        }

        // ------------------------------------------------------------------ 1. Preparation
        void BuildPrep()
        {
            Para("Imprime une mire (damier noir et blanc) et colle-la sur une surface PLATE et RIGIDE : carton epais, plaque, vitre. Ensuite je te guide pour la montrer sous une quinzaine d'angles, avec une barre de progression. Le resultat est note, puis verifie avec une regle.", 112, 19);

            // illustration : la mire a imprimer
            float cy = RowY(150f);
            var card = Ar6dofVrUi.Rect(content, "Exemple", new Vector2(0f, cy), new Vector2(IW, 150f));
            var ci = card.gameObject.AddComponent<Image>();
            ci.raycastTarget = false;
            Ar6dofTheme.Attach(card.gameObject, Ar6dofRole.Track, 14);
            var ex = MakeBoard(card, new Vector2(-IW * 0.5f + 135f, 0f), new Vector2(240f, 140f), 1f);
            ex.squaresX = SquaresX; ex.squaresY = SquaresY;
            ex.Set(0f, 0f, 0.92f, 0f, 0f, 0f);
            float tx = -IW * 0.5f + 270f + (IW - 285f) * 0.5f;
            Wrap(card, T("Compte les CARREAUX (pas les coins), noirs et blancs. Sur l'exemple : 10 en largeur, 7 en hauteur. Mesure ensuite la largeur de TOUTE la grille a la regle."),
                 new Vector2(tx, 0f), new Vector2(IW - 285f, 136f), 18, TextAnchor.MiddleLeft);

            Section("1. Ta mire imprimee");
            var names = new string[Presets.Length];
            for (int i = 0; i < names.Length; i++) names[i] = Presets[i][0] + " x " + Presets[i][1];
            Tabs(names, preset, k => { preset = k; PutI("preset", k); });
            Para("Nombre de carreaux, largeur x hauteur. Plus il y en a, plus la mesure est fine (mais il faut une grande feuille ou une camera nette).", 56, 17);

            SliderRow("Largeur de toute la grille", 60f, 800f, () => totalMm, v => { totalMm = Mathf.Round(v); PutF("total", totalMm); }, v => N0(v) + " mm", 54f, true);
            BtnRow(44f, 21, "-10", (Action)(() => AdjustTotal(-10f)), "-1", (Action)(() => AdjustTotal(-1f)), "+1", (Action)(() => AdjustTotal(1f)), "+10", (Action)(() => AdjustTotal(10f)));
            Line(() => Ar6dofLoc.F("Un carreau = {0} mm  ({1} mm / {2} carreaux)", N2(SquareMm), N0(totalMm), SquaresX), 32, 21);
            Para("Imprime a 100% (taille reelle, pas \"ajuster a la page\") puis mesure de nouveau : une imprimante change souvent la taille. C'est cette mesure qui donne l'echelle en millimetres.", 56, 17);

            Section("2. Ta camera (facultatif)");
            SliderRow("Ecart entre les objectifs", 0f, 150f, () => hintMm, v => { hintMm = Mathf.Round(v); PutF("hint", hintMm); }, v => v < 1f ? T("inconnu") : N0(v) + " mm", 54f, true);
            Para("Distance entre le centre des deux objectifs si tu la connais : elle sert seulement a te prevenir si le resultat s'en ecarte beaucoup.", 40, 17);

            Section("3. Ce qui est utilise en ce moment");
            ParaLine(InfoText, 92, 18);
            var warn = Para("", 76, 18);
            warn.MakeAccent();
            refreshers.Add(() => warn.text = WarnText());

            Space(6f);
            Text startLbl;
            var start = BtnT("Commencer la calibration", StartCapture, out startLbl, 60f, 26);
            refreshers.Add(() => { start.interactable = ProbeAlive(); });
            ParaLine(PrepStatus, 50, 18);

            var imp = Btn("Importer la calibration RTAB-Map (convertie en format AR6DoF)", () => { link.Send("CALIMPORT"); ScheduleReload(); link.Poll(true); }, 48f, 19);
            refreshers.Add(() => { imp.interactable = ProbeAlive(); });
            Para("Garde ton ancienne calibration RTAB-Map utilisable : elle est convertie, l'ancien fichier AR6DoF est sauvegarde (.prev). Une calibration faite ici est en general meilleure.", 56, 16);
        }

        void AdjustTotal(float d)
        {
            totalMm = Mathf.Clamp(Mathf.Round(totalMm + d), 60f, 800f);
            PutF("total", totalMm);
            Refresh();
            // le curseur se remet a jour par ses propres rafraichissements
        }

        string InfoText()
        {
            var i = link.Info;
            if (i == null) return T("Aucune information : le programme de suivi n'a pas encore ecrit sa calibration (lance-le d'abord).");
            string src = i.source == "ar6dof" ? T("calibration AR6DoF") : (i.source == "rtabmap" ? T("calibration RTAB-Map") : i.source);
            string s = Ar6dofLoc.F("Calibration : {0}, {1} vues, ecart entre les cameras {2} mm, champ {3} degres.", src, i.views, N1(i.baselineMm), N0(i.hfov));
            s += "\n" + Ar6dofLoc.F("Faite en {0} x {1} par oeil ; mode camera actuel : {2} x {3}.", i.calibW, i.calibH, i.runW, i.runH);
            if (!string.IsNullOrEmpty(i.error)) s += "\n" + T(i.error);
            return s;
        }

        string WarnText()
        {
            var i = link.Info;
            if (i == null || !i.mismatch) return "";
            return T("ATTENTION : la camera n'est pas dans le mode de la calibration. La profondeur et la distance seront fausses. Refais la calibration dans ce mode (le mode de reference est celui du suivi).");
        }

        string PrepStatus()
        {
            if (!ProbeAlive()) return T("Le programme de suivi (ar6dof_probe) n'est pas lance : lance-le, puis reviens ici.");
            if (startPending)
            {
                if (S.state == "capture") return "";
                if (Time.unscaledTime - startAt > 4f) return T("Pas de reponse du programme de suivi. Verifie qu'il tourne et qu'il est a jour (version 62).");
                return T("Demarrage de la calibration...");
            }
            return T("Pendant la calibration le suivi de position est suspendu ; il reprend tout seul a la fin.");
        }

        void StartCapture()
        {
            var p = Presets[preset];
            string line = "CALSTART " + (p[0] - 1) + " " + (p[1] - 1) + " " + SquareMm.ToString("0.###", Ci) + " " + hintMm.ToString("0.#", Ci);
            link.Send(line);
            startPending = true;
            startAt = Time.unscaledTime;
            lastAcceptSeq = -1;
            flashUntil = 0f;
        }

        // ------------------------------------------------------------------ 2. Capture
        void BuildCapture()
        {
            startPending = false;
            ProgressBar(() => S.progress, () => Ar6dofLoc.F("Progression {0}%   -   {1} vues / {2}   -   {3} poses sur {4}", N0(S.progress * 100f), S.views, S.target, PopCount(S.goalsDone), S.goalCount), 40f);
            var msg = Para("", 56, 22);
            msg.fontStyle = FontStyle.Bold;
            refreshers.Add(() => msg.text = MessageText());

            // apercu de la camera gauche avec les coins detectes et la mire "fantome" a reproduire
            var pt = dock.passthrough;
            float aspect = S.imgW > 0 && S.imgH > 0 ? (float)S.imgW / S.imgH : (pt != null ? pt.EyeAspect : 4f / 3f);
            float ph = Mathf.Min(400f, IW / aspect);
            float pw = ph * aspect;
            float cy = RowY(ph);
            var frame = Ar6dofVrUi.Rect(content, "Apercu", new Vector2(0f, cy), new Vector2(pw, ph));
            var bgi = frame.gameObject.AddComponent<Image>();
            bgi.raycastTarget = false;
            Ar6dofTheme.Attach(frame.gameObject, Ar6dofRole.Track, 6);
            var pr = Ar6dofVrUi.Rect(frame, "Camera", Vector2.zero, new Vector2(pw, ph));
            preview = pr.gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;
            preview.color = new Color(1f, 1f, 1f, 0f);
            ghost = MakeBoard(frame, Vector2.zero, new Vector2(pw, ph), 0.38f);
            var dr = Ar6dofVrUi.Rect(frame, "Coins", Vector2.zero, new Vector2(pw, ph));
            dots = dr.gameObject.AddComponent<Ar6dofDotsGraphic>();
            dots.raycastTarget = false;
            refreshers.Add(UpdatePreview);

            // conseil illustre
            float ccy = RowY(160f);
            var card = Ar6dofVrUi.Rect(content, "Conseil", new Vector2(0f, ccy), new Vector2(IW, 160f));
            var cim = card.gameObject.AddComponent<Image>();
            cim.raycastTarget = false;
            Ar6dofTheme.Attach(card.gameObject, Ar6dofRole.Track, 14);
            var frm = Ar6dofVrUi.Rect(card, "Cadre", new Vector2(-IW * 0.5f + 110f, 0f), new Vector2(200f, 150f));
            var fimg = frm.gameObject.AddComponent<Image>();
            fimg.raycastTarget = false;
            Ar6dofTheme.Attach(frm.gameObject, Ar6dofRole.Surface, 6);
            tipIcon = MakeBoard(frm, Vector2.zero, new Vector2(200f, 150f), 1f);
            float tx = -IW * 0.5f + 225f + (IW - 240f) * 0.5f;
            var tt = Ar6dofVrUi.Label(card, "", new Vector2(tx, 52f), new Vector2(IW - 240f, 36f), 24, TextAnchor.MiddleLeft);
            tt.fontStyle = FontStyle.Bold;
            tt.MakeAccent();
            var tb = Wrap(card, "", new Vector2(tx, -26f), new Vector2(IW - 240f, 104f), 18, TextAnchor.UpperLeft);
            refreshers.Add(() =>
            {
                int g = S.goal;
                bool has = g >= 0 && g < Goals.Length;
                tt.text = has ? T(Goals[g].title) : T("Tout est couvert !");
                tb.text = has ? T(Goals[g].tip) : T("Tu as montre la mire sous tous les angles. Tu peux lancer le calcul, ou continuer quelques vues pour plus de precision.");
                if (has && tipIcon != null) { var q = Goals[g]; tipIcon.Set(q.px, q.py, q.size, q.yaw, q.pitch, q.roll); }
                else if (tipIcon != null) tipIcon.Set(0f, 0f, 0.5f, 0f, 0f, 0f);
            });

            // liste des poses : cochees quand elles sont faites
            const int cols = 4;
            float cw = IW / cols;
            int rows = (Goals.Length + cols - 1) / cols;
            float top = y;
            var chips = new Text[Goals.Length];
            for (int i = 0; i < Goals.Length; i++)
            {
                int cx = i % cols, cr = i / cols;
                chips[i] = Ar6dofVrUi.Label(content, "", new Vector2(-IW * 0.5f + cw * (cx + 0.5f), -(top + 16f + cr * 30f)), new Vector2(cw - 4f, 28f), 16, TextAnchor.MiddleLeft);
            }
            y = top + rows * 30f + Gap;
            refreshers.Add(() =>
            {
                for (int i = 0; i < chips.Length; i++)
                {
                    bool done = (S.goalsDone & (1 << i)) != 0;
                    chips[i].text = (done ? "[x] " : (i == S.goal ? "[>] " : "[ ] ")) + T(Goals[i].name);
                }
            });

            Para("Tiens la mire IMMOBILE environ une seconde pour chaque vue : une mire floue est refusee. Eclairage uniforme, pas de reflet sur la mire, et ne la plie pas.", 56, 16);

            var row = BtnRow(52f, 20, "Calculer maintenant", (Action)(() => link.Send("CALCOMPUTE")),
                                      "Inverser G / D", (Action)(() => { link.SendKey('x'); }),
                                      "Annuler", (Action)(() => { link.Send("CALCANCEL"); wantVerify = false; }));
            var calc = row[0];
            refreshers.Add(() => { calc.interactable = S.views >= 10; });
        }

        static int PopCount(int v) { int n = 0; while (v != 0) { n += v & 1; v = (int)((uint)v >> 1); } return n; }

        string MessageText()
        {
            if (Time.unscaledTime < flashUntil) return T("Vue enregistree !");
            switch (S.msg)
            {
                case "show": return T("Montre la mire a la camera : tout le damier doit etre visible, sans coin coupe.");
                case "showR": return T("L'autre camera ne voit pas la mire : centre-la un peu plus ou recule.");
                case "still": return T("Bien. Ne bouge plus... (une seconde)");
                case "move": return T("Trop proche d'une pose deja prise : change de place, de distance ou d'angle (voir le conseil).");
                case "ok": return T("Vue enregistree !");
                case "edge": return T("La mire touche le bord de l'image : rentre-la un peu.");
                case "small": return T("Trop loin : approche la mire.");
                case "swapped": return T("Les deux cameras semblent inversees : touche Inverser G / D, puis recommence cette pose.");
                case "order": return T("Les deux cameras ne voient pas la mire dans le meme sens : ne la retourne pas, garde le meme cote devant.");
                case "parity": return T("Grille impossible : le nombre de carreaux en largeur et en hauteur doivent etre de parites differentes (par ex. 10 x 7). Annule et change de grille.");
                default: return T("Montre la mire a la camera.");
            }
        }

        void UpdatePreview()
        {
            if (preview == null) return;
            var pt = dock.passthrough;
            Texture tex = pt != null ? pt.RawCameraTexture : null;
            if (tex != null)
            {
                if (preview.texture != tex) preview.texture = tex;
                if (preview.color.a < 0.5f) preview.color = Color.white;
                // moitie de l'image brute qui est la camera « gauche » de la sonde
                bool sw = S.swapEyes;
                if (pt.RawSideBySide) preview.uvRect = new Rect(sw ? 0.5f : 0f, 0f, 0.5f, 1f);
                else preview.uvRect = new Rect(0f, sw ? 0f : 0.5f, 1f, 0.5f);
            }
            if (S.acceptSeq != lastAcceptSeq)
            {
                if (lastAcceptSeq >= 0) flashUntil = Time.unscaledTime + 1.2f;
                lastAcceptSeq = S.acceptSeq;
            }
            if (dots != null)
            {
                int n = S.corners != null ? S.corners.Length / 2 : 0;
                if (n > 0 && S.foundL) dots.SetPoints(S.corners, n, S.foundR ? new Color(0.2f, 1f, 0.35f, 1f) : new Color(1f, 0.75f, 0.1f, 1f), 7f);
                else dots.Clear();
            }
            if (ghost != null)
            {
                int g = S.goal;
                if (g >= 0 && g < Goals.Length) { var q = Goals[g]; ghost.Set(q.px, q.py, q.size, q.yaw, q.pitch, q.roll); ghost.alpha = 0.38f; }
                else ghost.Set(0f, 0f, 0.001f, 0f, 0f, 0f);
            }
        }

        // ------------------------------------------------------------------ 3. Calcul
        void BuildCompute()
        {
            ProgressBar(() => S.cprogress, () => Ar6dofLoc.F("Calcul {0}%   -   {1}", N0(S.cprogress * 100f), StageText(S.stage)), 44f);
            Space(6f);
            Para("Je cherche le meilleur modele de distorsion de ton objectif, la focale et la position exacte des deux cameras l'une par rapport a l'autre. Cela prend en general 10 a 60 secondes. Tu peux enlever la mire.", 96, 20);
            Para("Ne ferme pas la fenetre et n'annule pas pendant ce temps : le programme de suivi attend la fin du calcul.", 56, 18);
        }

        static string StageText(string s)
        {
            switch (s)
            {
                case "focale de depart": return T("estimation de la focale");
                case "choix du modele de distorsion": return T("choix du modele de distorsion");
                case "calibrage des deux cameras": return T("calibrage de chaque camera");
                case "calibrage stereo": return T("calibrage stereo");
                case "controle de la qualite": return T("controle de la qualite");
                case "termine": return T("termine");
                default: return T("preparation");
            }
        }

        // ------------------------------------------------------------------ 4. Resultat
        void BuildResult()
        {
            if (S.state == "error")
            {
                Section("La calibration a echoue");
                Para(string.IsNullOrEmpty(S.error) ? T("Erreur inconnue.") : T(S.error), 96, 20);
                Para("Causes frequentes : mire pas assez variee, floue, ou pliee ; mauvais eclairage ; pas assez de vues. Recommence en suivant les conseils.", 76, 18);
                Btn("Refaire la calibration", Redo, 56f, 24);
                return;
            }
            var r = S.result;
            ProgressBar(() => S.result.score / 100f, () => Ar6dofLoc.F("Note {0} / 100   -   {1}", S.result.score, Verdict(S.result.score)), 44f);
            Space(4f);
            Line(() => Ar6dofLoc.F("Erreur de reprojection : G {0} px, D {1} px  (moins de 0,5 = tres bien)", N2(S.result.rmsL), N2(S.result.rmsR)), 28, 19);
            Line(() => Ar6dofLoc.F("Alignement des deux cameras : {0} px, pire cas {1} px  (moins de 0,3 = tres bien)", N2(S.result.epi), N2(S.result.epiMax)), 28, 19);
            Line(() => Ar6dofLoc.F("Controle sur des vues mises de cote : {0} px", N2(S.result.heldOut)), 28, 19);
            Line(() => Ar6dofLoc.F("Distance entre les deux cameras : {0} mm", N1(S.result.baselineMm)), 28, 19);
            Line(() => Ar6dofLoc.F("Champ de vision horizontal : {0} degres", N0(S.result.hfov)), 28, 19);
            Line(() => Ar6dofLoc.F("Vues utilisees : {0} sur {1}  -  image couverte a {2}%", S.result.used, S.result.total, N0(S.result.coverage * 100f)), 28, 19);
            Line(() => Ar6dofLoc.F("Modele de distorsion choisi : {0}", ModelName(S.result.model)), 28, 19);
            for (int i = 0; i < r.warnings.Length; i++)
            {
                string w = WarningText(r.warnings[i]);
                if (w.Length == 0) continue;
                var t = Para("! " + w, 56, 18);
                t.MakeAccent();
            }
            Space(6f);
            Text lab;
            var save = BtnT("Enregistrer et appliquer", () => { link.Send("CALAPPLY"); ScheduleReload(); }, out lab, 58f, 25);
            Text lb = lab;
            refreshers.Add(() => { lb.text = T(S.applied ? "Enregistree et appliquee" : "Enregistrer et appliquer"); save.interactable = !S.applied; });
            Btn("Verifier l'echelle avec une regle", () => { wantVerify = true; Rebuild(); }, 54f, 22);
            Btn("Refaire la calibration", Redo, 48f, 21);
            Para("L'echelle (les millimetres) vient de la taille d'un carreau et de la distance entre les cameras : la verification a la regle confirme que les distances mesurees sont justes.", 56, 16);
        }

        void Redo()
        {
            link.Send("CALCANCEL");
            wantVerify = false;
            startPending = false;
        }

        static string Verdict(int score)
        {
            if (score >= 85) return T("excellente");
            if (score >= 70) return T("bonne");
            if (score >= 50) return T("moyenne : a refaire si possible");
            return T("mauvaise : a refaire");
        }

        static string ModelName(string m)
        {
            if (string.IsNullOrEmpty(m)) return "-";
            return m;
        }

        static string WarningText(string code)
        {
            switch (code)
            {
                case "few_views": return T("Peu de vues utilisables : refais avec plus de poses variees.");
                case "cov_low": return T("L'image n'est pas assez couverte : mets la mire dans les coins et contre les bords.");
                case "rms_high": return T("Erreur de reprojection elevee : mire pas plane, floue, ou taille de carreau mal mesuree.");
                case "epi_high": return T("Les deux cameras ne sont pas bien alignees apres calibration : verifie qu'elles ne bougent pas l'une par rapport a l'autre.");
                case "roundtrip": return T("La correction de la distorsion est peu fiable sur les bords : montre la mire plus pres des bords.");
                case "swapped": return T("Les cameras gauche et droite sont probablement inversees : touche Inverser G / D pendant la capture, puis refais.");
                case "pp_far": return T("Le centre optique est tres decale : la camera est peut-etre recadree (mode different) ou l'objectif mal monte.");
                case "parity": return T("Grille de parite invalide : le resultat peut etre retourne.");
                case "base_far": return T("La distance entre les cameras differe de plus de 12% de ta mesure : verifie la taille d'un carreau.");
                default: return "";
            }
        }

        // ------------------------------------------------------------------ 5. Verification a la regle
        void BuildVerify()
        {
            Para("Tiens la mire de FACE, immobile, bien visible par les deux cameras. Mesure avec une regle (ou un metre) la distance entre la vitre des objectifs et la mire, puis regle-la ci-dessous. Plus c'est loin (50 cm ou plus), plus la mesure est juste.", 96, 19);
            float cy = RowY(150f);
            var card = Ar6dofVrUi.Rect(content, "Schema", new Vector2(0f, cy), new Vector2(IW, 150f));
            var ci = card.gameObject.AddComponent<Image>();
            ci.raycastTarget = false;
            Ar6dofTheme.Attach(card.gameObject, Ar6dofRole.Track, 14);
            var ic = MakeBoard(card, new Vector2(-IW * 0.5f + 135f, 0f), new Vector2(240f, 140f), 1f);
            ic.Set(0f, 0f, 0.6f, 0f, 0f, 0f);
            float tx = -IW * 0.5f + 270f + (IW - 285f) * 0.5f;
            Wrap(card, T("La mire est de face, pas inclinee. Mesure du verre de l'objectif jusqu'au papier de la mire."), new Vector2(tx, 0f), new Vector2(IW - 285f, 136f), 18, TextAnchor.MiddleLeft);

            SliderRow("Distance mesuree", 100f, 1500f, () => verifyMm, v => { verifyMm = Mathf.Round(v); PutF("verify", verifyMm); }, v => N0(v) + " mm", 54f, true);
            BtnRow(44f, 21, "-10", (Action)(() => AdjustVerify(-10f)), "-1", (Action)(() => AdjustVerify(-1f)), "+1", (Action)(() => AdjustVerify(1f)), "+10", (Action)(() => AdjustVerify(10f)));
            Btn("Mesurer", () =>
            {
                verifySentSeq = S.verify.seq;
                verifySentAt = Time.unscaledTime;
                link.Send("CALVERIFY " + verifyMm.ToString("0.#", Ci));
            }, 56f, 25);

            ParaLine(VerifyText, 96, 19);
            Text lab;
            var fix = BtnT("Corriger l'echelle", () =>
            {
                float ratio = S.verify.ratio;
                if (ratio < 0.3f || ratio > 3f) return;
                link.Send("CALSCALE " + ratio.ToString("0.#####", Ci));
                link.Send("CALAPPLY");
                ScheduleReload();
                verifySentSeq = S.verify.seq;
                verifySentAt = -1f;
            }, out lab, 54f, 22);
            Text fl = lab;
            refreshers.Add(() =>
            {
                bool measured = S.verify.seq > 0 && S.verify.ratio > 0.3f;
                bool off = measured && Mathf.Abs(S.verify.ratio - 1f) > 0.02f;
                fix.gameObject.SetActive(measured);
                fix.interactable = off;
                fl.text = off ? Ar6dofLoc.F("Corriger l'echelle (x {0})", S.verify.ratio.ToString("0.000", Ci)) : T("Echelle correcte, rien a corriger");
            });
            Btn("Retour au resultat", () => { wantVerify = false; Rebuild(); }, 48f, 21);
        }

        void AdjustVerify(float d)
        {
            verifyMm = Mathf.Clamp(Mathf.Round(verifyMm + d), 100f, 1500f);
            PutF("verify", verifyMm);
            Refresh();
        }

        string VerifyText()
        {
            var v = S.verify;
            if (verifySentAt > 0f && v.seq == verifySentSeq)
            {
                if (Time.unscaledTime - verifySentAt > 2.5f)
                    return T("Mesure impossible : la mire doit etre visible par les deux cameras, de face. Rapproche-la ou centre-la, puis recommence.");
                return T("Mesure en cours...");
            }
            if (v.seq <= 0) return T("Pas encore de mesure. Place la mire, regle la distance et touche Mesurer.");
            float dev = (v.ratio - 1f) * 100f;
            string s = Ar6dofLoc.F("Le systeme estime {0} mm ; tu as mesure {1} mm. Rapport : {2}.", N0(v.estMm), N0(verifyMm), v.ratio.ToString("0.000", Ci));
            s += "\n" + (Mathf.Abs(dev) <= 2f
                ? Ar6dofLoc.F("Ecart de {0}% seulement : l'echelle est bonne.", N1(Mathf.Abs(dev)))
                : Ar6dofLoc.F("Ecart de {0}% : touche Corriger l'echelle. Si l'ecart revient, verifie la taille d'un carreau et mesure de plus loin.", N1(Mathf.Abs(dev))));
            return s;
        }
    }
}
