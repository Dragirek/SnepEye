using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Appareil photo : aperçu en direct de ce que voit le casque (image de la caméra corrigée, sans la déformation des lentilles) au format réel de la caméra.
    /// Onglet Photo : photo (camera gauche ou droite, avec ou sans les interfaces, minuteur, accumulation), panorama (tourne la tete), video (AVI sans son), rafale / intervalle.
    /// Onglet Scanner : Auto (QR puis texte), QR code, Texte, Traduction, Planning (plusieurs photos d'une affiche assemblees), Carte de visite. La photo prise reste affichee quelques secondes
    /// en petit sur l'apercu. Les fichiers vont dans Images\AR6DoF\Photos. Rien ne tourne tant que la fenêtre est fermée ou réduite (voir aussi Ar6dofCameraSeries.cs).
    /// </summary>
    public partial class Ar6dofCameraApp : Ar6dofScanWindow
    {
        static readonly int[] Timers = { 0, 3, 5, 10 };
        static readonly string[] SNames = { "QR code", "Texte", "Traduction", "Planning", "Carte" };   // le mode interne 0 (Auto) n'est plus propose : QR code est le mode de base
        const int PreviewLayer = 30;   // la fenetre est sur cette couche : l'apercu en direct l'exclut (sinon il se filmerait lui-meme)
        const float ShotSeconds = 4f;  // duree d'affichage de la miniature de la photo prise

        int mode;   // 0 photo, 1 scanner
        int pmode;  // onglet Photo : 0 photo, 1 panorama, 2 video, 3 rafale
        int smode;  // onglet Scanner : 1 QR code, 2 texte, 3 traduction, 4 planning, 5 carte de visite (0 = ancien mode Auto, plus propose)
        int ui, eye, timer, quality;   // quality : 0 normale, 1 accumulee (8 images), 2 accumulee sur grille x2
        string status = "";
        Text shootLabel;
        Text countOverlay;
        Canvas countCanvas;
        Text countText;
        RenderTexture preview;
        float lastPreview;
        int lastCount = -1;
        bool shown;   // la fenetre est affichee (pas reduite, pas fermee) : HideUi la cache seulement pendant la prise

        // ---- miniature de la derniere photo, par-dessus l'apercu
        RectTransform previewRt;
        Texture2D shotTex;
        GameObject shotRoot;
        float shotUntil;

        // ---- detection du QR code en direct (onglet Scanner, modes Auto, QR code et Carte) : l'apercu est lu 5 fois par seconde dans un autre fil, le cadre du QR est dessine des qu'il est vu, et le contenu s'affiche des qu'il est lu
        Texture2D liveTex;
        Task<string> liveTask;
        Ar6dofQr.Result liveRes;
        float nextLive, liveSeen = -9f;
        float[] liveQuad;
        int liveW = 1, liveH = 1;
        RectTransform[] quadLines;
        Vector2 previewSize;
        Text liveLabel;
        static bool LiveOn
        {
            get { try { return PlayerPrefs.GetInt("ar6dof_cam_live", 1) != 0; } catch (Exception) { return true; } }
            set { try { PlayerPrefs.SetInt("ar6dof_cam_live", value ? 1 : 0); } catch (Exception) { } }
        }
        bool LiveMode { get { return smode == 0 || smode == 1 || smode == 5; } }

        // ---- planning
        readonly Ar6dofPlanSession plan;
        Texture2D planTex;
        Color32[] planTexSrc;
        int planRoom = -1, planDay;   // salle choisie (-1 = toutes), decalage de jour (0 = aujourd'hui)

        public Ar6dofCameraApp(Ar6dofDock d) : base(d, "camera", "Appareil photo", "camera", "Apercu en direct, photo et scanner (QR code, texte)", 940f)
        {
            ui = Get("ui", 1); eye = Get("eye", 0); timer = Get("timer", 1); quality = Get("quality", 0);
            pmode = Mathf.Clamp(Get("pmode", 0), 0, 3);
            smode = Mathf.Clamp(Get("smode", 1), 1, 5);   // l'ancien mode Auto (0) devient QR code
            vidRes = Mathf.Clamp(Get("vidres", 1), 0, 2);
            burstN = Mathf.Clamp(Get("burstn", 1), 0, BurstCounts.Length - 1);
            burstI = Mathf.Clamp(Get("bursti", 1), 0, BurstIntervals.Length - 1);
            autoTranslate = smode == 3;
            scan.shotCb = ShowShot;
            plan = new Ar6dofPlanSession(d, OnPlanChanged, OnPlanStatus);
        }

        static int Get(string k, int def) { try { return PlayerPrefs.GetInt("ar6dof_cam_" + k, def); } catch (Exception) { return def; } }
        static void Put(string k, int v) { try { PlayerPrefs.SetInt("ar6dof_cam_" + k, v); } catch (Exception) { } }

        protected override void OnRebuildStart() { base.OnRebuildStart(); shootLabel = null; countOverlay = null; quadLines = null; liveLabel = null; previewRt = null; shotRoot = null; }

        public override void OnClosed()
        {
            mode = 0;
            StopSeries(false);
            plan.Abort();
            DropShot();
            DropPlanTex();
            base.OnClosed();
        }

        protected override void OnRebuilt() { SetLayer(canvas != null ? canvas.gameObject : null, PreviewLayer); }

        static void SetLayer(GameObject go, int layer)
        {
            if (go == null) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        protected override void OnVisible(bool on)
        {
            shown = on;
            if (!on)
            {
                DropLive();
                AbortScan();
                plan.Abort();
                StopSeries(true);
                var pt = dock.passthrough;
                if (pt != null) { if (pt.PhotoBusy) pt.CancelPhoto(); pt.ReleasePreviewScene(); }
                DestroyCount();
                DropShot();
                DropPlanTex();
                ReleasePreview();
            }
        }

        public override void Destroy()
        {
            DropLive();
            AbortScan();
            plan.Abort();
            StopSeries(true);
            DestroyCount();
            DropShot();
            DropPlanTex();
            ReleasePreview();
            base.Destroy();
        }

        void ReleasePreview()
        {
            if (preview != null) { preview.Release(); UnityEngine.Object.Destroy(preview); }
            preview = null;
        }

        Ar6dofPassthrough Pt { get { return dock.passthrough; } }

        // texte d'etat, rafraichi par le dock toutes les 0,25 s
        void RefreshSafe() { if (canvas != null && canvas.gameObject.activeSelf) Refresh(); }

        // texture de l'apercu : deux yeux cote a cote, chacun au format de l'oeil de la camera (se refait si la resolution de la camera change)
        void EnsurePreview()
        {
            var pt = Pt;
            float aspect = pt != null ? pt.EyeAspect : 4f / 3f;
            int h = mode == 1 ? 540 : 360, w = Mathf.RoundToInt(h * aspect);   // scanner : plus de pixels pour lire un QR de loin
            if (preview != null && (preview.height != h || preview.width != 2 * w)) ReleasePreview();
            if (preview == null)
            {
                preview = new RenderTexture(2 * w, h, 24, RenderTextureFormat.ARGB32);
                preview.Create();
            }
        }

        // apercu en direct : 30 images/s tant que la fenetre est affichee (rien quand elle est reduite ou fermee)
        protected override void OnTick()
        {
            TickScan();
            plan.Tick();
            TickSeries();
            if (shotTex != null && Time.unscaledTime > shotUntil) DropShot();
            if (planTex != null && plan.phase != Ar6dofPlanSession.Phase.Done) DropPlanTex();
            var pt = Pt;
            if (pt == null || canvas == null || !canvas.gameObject.activeInHierarchy || pt.PhotoShooting) return;   // le minuteur ne fige pas l'apercu
            if (Time.unscaledTime - lastPreview < 1f / 30f) return;                                              // 30 images/s
            lastPreview = Time.unscaledTime;
            EnsurePreview();
            pt.RenderPreview(preview, mode == 0 && pmode == 0 && ui != 0, PreviewLayer);   // seule la photo simple peut inclure les interfaces ; le scanner et les series : camera seule
            if (mode == 1 && LiveOn && LiveMode && scan.phase == Ar6dofScanSession.Phase.Idle) TickLive();
            else if (liveTask != null || liveQuad != null) DropLive();
        }

        // ------------------------------------------------------------------ miniature de la photo prise
        /// <summary>Garde la photo (propriete de la fenetre) quelques secondes en petit sur l'apercu.</summary>
        void ShowShot(Texture2D t)
        {
            DropShot();
            if (t == null) return;
            shotTex = t;
            shotUntil = Time.unscaledTime + ShotSeconds;
            AttachShot();
        }

        void AttachShot()
        {
            if (shotTex == null || previewRt == null || shotRoot != null) return;
            float tw = previewSize.x * 0.34f;
            float th = tw * shotTex.height / Mathf.Max(1, shotTex.width);
            var fr = Ar6dofVrUi.Rect(previewRt, "Miniature", new Vector2(previewSize.x * 0.5f - tw * 0.5f - 18f, -previewSize.y * 0.5f + th * 0.5f + 18f), new Vector2(tw + 10f, th + 10f));
            var bg = fr.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.95f);
            bg.raycastTarget = false;
            var pic = Ar6dofVrUi.Rect(fr, "Photo", Vector2.zero, new Vector2(tw, th));
            var ri = pic.gameObject.AddComponent<RawImage>();
            ri.texture = shotTex;
            ri.raycastTarget = false;
            shotRoot = fr.gameObject;
            SetLayer(shotRoot, PreviewLayer);   // sinon l'apercu en direct filmerait sa propre miniature
        }

        void DropShot()
        {
            if (shotRoot != null) UnityEngine.Object.Destroy(shotRoot);
            shotRoot = null;
            if (shotTex != null) UnityEngine.Object.Destroy(shotTex);
            shotTex = null;
        }

        // ------------------------------------------------------------------ detection en direct
        void TickLive()
        {
            float now = Time.unscaledTime;
            if (liveTask != null)
            {
                if (!liveTask.IsCompleted) return;
                string text = liveTask.IsFaulted ? null : liveTask.Result;
                var res = liveRes;
                liveTask = null; liveRes = null;
                if (!string.IsNullOrEmpty(text)) { DropLive(); scan.AcceptLive(text); return; }
                liveQuad = res != null ? res.quad : null;
                if (liveQuad != null) liveSeen = now;
                UpdateQuad();
            }
            else if (liveQuad != null && now - liveSeen > 0.8f) { liveQuad = null; UpdateQuad(); }
            if (now < nextLive || preview == null) return;
            nextLive = now + 0.2f;
            int w = preview.width / 2, h = preview.height;
            if (liveTex == null || liveTex.width != w || liveTex.height != h)
            {
                if (liveTex != null) UnityEngine.Object.Destroy(liveTex);
                liveTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            }
            var prev = RenderTexture.active;
            RenderTexture.active = preview;
            liveTex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);   // oeil gauche (moitie gauche de l'apercu)
            RenderTexture.active = prev;
            Color32[] px = liveTex.GetPixels32();
            var rs = new Ar6dofQr.Result();
            liveRes = rs; liveW = w; liveH = h;
            liveTask = Task.Run(() => Ar6dofQr.DecodePixels(px, w, h, 600, rs));
        }

        void DropLive()
        {
            liveTask = null; liveRes = null; liveQuad = null;
            if (liveTex != null) { UnityEngine.Object.Destroy(liveTex); liveTex = null; }
            UpdateQuad();
        }

        /// <summary>Dessine le cadre du QR trouve sur l'apercu (4 traits) ; rien si aucun QR n'est vu.</summary>
        void UpdateQuad()
        {
            if (quadLines == null) return;
            bool on = liveQuad != null && liveQuad.Length >= 8;
            for (int i = 0; i < 4; i++)
            {
                var rt = quadLines[i];
                if (rt == null) continue;
                if (!on) { if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false); continue; }
                int j = (i + 1) & 3;
                Vector2 a = new Vector2((liveQuad[2 * i] / liveW - 0.5f) * previewSize.x, (0.5f - liveQuad[2 * i + 1] / liveH) * previewSize.y);
                Vector2 b = new Vector2((liveQuad[2 * j] / liveW - 0.5f) * previewSize.x, (0.5f - liveQuad[2 * j + 1] / liveH) * previewSize.y);
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = (a + b) * 0.5f;
                rt.sizeDelta = new Vector2((b - a).magnitude + 5f, 5f);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            }
            if (liveLabel != null) liveLabel.text = Ar6dofLoc.T(on ? "QR detecte : tiens-toi immobile, lecture en cours..." : "Cherche un QR code...");
        }

        // ------------------------------------------------------------------ fenetre
        /// <summary>Annule tout ce qui est en cours (changement d'onglet ou de mode).</summary>
        void CancelAll()
        {
            var pt = Pt;
            if (pt != null && pt.PhotoBusy) pt.CancelPhoto();
            AbortScan();
            plan.Abort();
            StopSeries(false);
            DropLive();
        }

        // rangee d'onglets avec une police plus petite (six modes de scanner)
        void TabsRow(string[] names, int current, int fs, Action<int> choose)
        {
            var items = new object[names.Length * 2];
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                items[2 * i] = names[i];
                items[2 * i + 1] = (Action)(() => { choose(k); Rebuild(); });
            }
            var bs = BtnRow(50f, fs, items);
            for (int i = 0; i < bs.Count; i++) Ar6dofTheme.SetSelected(bs[i], i == current);
        }

        protected override void BuildMain()
        {
            var pt = Pt;
            if (pt == null) { Para("Le passthrough n'est pas disponible dans cette scene.", 40, 20); return; }

            Tabs(new[] { "Photo", "Scanner" }, mode, t =>
            {
                if (t == mode) return;
                CancelAll();
                mode = t;
            });
            if (mode == 0)
                TabsRow(new[] { "Photo", "Panorama", "Video", "Rafale" }, pmode, 21, t =>
                {
                    if (t == pmode) return;
                    CancelAll();
                    pmode = t; Put("pmode", t); status = "";
                });
            else
                TabsRow(SNames, smode - 1, 20, t =>
                {
                    int m = t + 1;
                    if (m == smode) return;
                    CancelAll();
                    smode = m; Put("smode", m);
                    autoTranslate = smode == 3;
                });

            // apercu en direct : remplit la fenetre, au format reel de la camera
            EnsurePreview();
            float aspect = pt.EyeAspect;
            Vector2 sz = new Vector2(IW, IW / aspect);
            float cy = RowY(sz.y);
            var r = Ar6dofVrUi.Rect(content, "Apercu", new Vector2(0f, cy), sz);
            var ri = r.gameObject.AddComponent<RawImage>();
            ri.texture = preview;
            ri.uvRect = new Rect(eye == 0 || mode == 1 ? 0f : 0.5f, 0f, 0.5f, 1f);   // scanner : toujours l'oeil gauche, celui de la photo et de la detection
            previewSize = sz;
            previewRt = r;
            quadLines = null;
            if (mode == 1 && LiveMode)
            {
                // cadre du QR detecte en direct : quatre traits sur l'apercu
                quadLines = new RectTransform[4];
                for (int i = 0; i < 4; i++)
                {
                    var ln = Ar6dofVrUi.Rect(r, "Cadre", Vector2.zero, new Vector2(10f, 5f));
                    var im = ln.gameObject.AddComponent<Image>();
                    im.color = new Color(1f, 0.85f, 0.1f, 0.95f);
                    im.raycastTarget = false;
                    ln.gameObject.SetActive(false);
                    quadLines[i] = ln;
                }
            }
            ri.raycastTarget = false;
            // compte a rebours du minuteur, sur l'apercu
            countOverlay = Ar6dofVrUi.Label(r, "", Vector2.zero, new Vector2(sz.x, sz.y), 150, TextAnchor.MiddleCenter);
            countOverlay.fontStyle = FontStyle.Bold;
            var sh = countOverlay.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(4f, -4f);
            AttachShot();   // la miniature de la derniere photo, tant qu'elle est encore la

            if (mode == 1) { BuildScanTab(); return; }
            switch (pmode)
            {
                case 1: BuildPano(); break;
                case 2: BuildVideo(); break;
                case 3: BuildBurst(); break;
                default: BuildClassic(); break;
            }
        }

        // ------------------------------------------------------------------ onglet Photo : photo simple
        void BuildClassic()
        {
            var bb = BtnRow(52f, 21, "", (Action)(() => { ui = ui != 0 ? 0 : 1; Put("ui", ui); Refresh(); }),
                                      "", (Action)(() => { eye = eye != 0 ? 0 : 1; Put("eye", eye); Rebuild(); }));
            var uiTxt = bb[0].GetComponentInChildren<Text>();
            var eyeTxt = bb[1].GetComponentInChildren<Text>();
            var uiBtn = bb[0];
            refreshers.Add(() =>
            {
                uiTxt.text = Ar6dofLoc.T(ui != 0 ? "Interfaces incluses : oui" : "Interfaces incluses : non");
                eyeTxt.text = Ar6dofLoc.T(eye == 0 ? "Camera : gauche" : "Camera : droite");
                Ar6dofTheme.SetSelected(uiBtn, ui != 0);
            });

            var qb = BtnRow(48f, 21, "", (Action)(() => { quality = (quality + 1) % 3; Put("quality", quality); Refresh(); }));
            var qTxt = qb[0].GetComponentInChildren<Text>();
            refreshers.Add(() => qTxt.text = Ar6dofLoc.T(quality == 0 ? "Qualite : normale" : (quality == 1 ? "Qualite : 8 images accumulees" : "Qualite : 8 images, grille x2")));

            BuildTimerRow();

            Text lab;
            var shoot = BtnT("", OnShoot, out lab, 76f, 28);
            shootLabel = lab;
            Ar6dofTheme.SetSelected(shoot, true);
            refreshers.Add(UpdateShootLabel);

            var st = Para("", 40, 17);
            refreshers.Add(() => st.text = status.Length > 0 ? Ar6dofLoc.T(status) : "");

            BtnRow(48f, 21, "Ouvrir le dossier", (Action)(() => OpenFolder()));
            Para("Photos dans Images\\AR6DoF\\Photos, a la resolution de la camera.", 28, 16);
            Para("Accumulation : reste immobile une seconde ; le calcul prend 1 a 3 s.", 28, 16);
        }

        void BuildTimerRow()
        {
            Lbl("Minuteur", 30, 20).MakeAccent();
            var tn = new string[Timers.Length];
            for (int i = 0; i < tn.Length; i++) tn[i] = Timers[i] == 0 ? "Aucun" : Timers[i] + " s";
            Tabs(tn, timer, t => { timer = t; Put("timer", t); });
        }

        // ------------------------------------------------------------------ onglet Scanner
        // la photo est temporaire (supprimee des que la lecture est finie) ; le compte a rebours s'affiche sur l'apercu
        void BuildScanTab()
        {
            refreshers.Add(() =>
            {
                if (countOverlay == null) return;
                int c = smode == 4 ? (plan.phase == Ar6dofPlanSession.Phase.Capturing ? plan.countSec : 0) : (scan.phase == Ar6dofScanSession.Phase.Countdown ? scan.countSec : 0);
                countOverlay.text = c > 0 ? c.ToString() : "";
            });
            if (smode == 4) { BuildPlanTab(); return; }
            switch (scan.phase)
            {
                case Ar6dofScanSession.Phase.Idle:
                    if (LiveMode)
                    {
                        liveLabel = Lbl("", 32, 20, TextAnchor.MiddleCenter);
                        liveLabel.fontStyle = FontStyle.Bold;
                        refreshers.Add(() => { if (liveLabel != null && !LiveOn) liveLabel.text = ""; });
                        ToggleRow("Detection automatique du QR code", () => LiveOn, v => { LiveOn = v; if (!v) DropLive(); else UpdateQuad(); }, 46f, 20);
                    }
                    Para(ScanHint(), smode == 0 ? 100 : 80, 18);
                    if (smode == 3)
                    {
                        var lb = BtnRow(48f, 20, "", (Action)(() => { TargetLang = (TargetLang + 1) % Ar6dofTranslator.Codes.Length; Refresh(); }));
                        var lt = lb[0].GetComponentInChildren<Text>();
                        refreshers.Add(() => lt.text = Ar6dofLoc.T("Traduire vers") + " : " + Ar6dofTranslator.Names[TargetLang]);
                    }
                    var go = Btn("Scanner", StartScanMode, 64f, 26);
                    Ar6dofTheme.SetSelected(go, true);
                    break;
                case Ar6dofScanSession.Phase.Countdown:
                case Ar6dofScanSession.Phase.Reading:
                    BuildScanBusy();
                    break;
                default:
                    if (smode == 3) BuildScanResult(true, false, true);
                    else if (smode == 5) BuildScanResult(false, false);
                    else BuildScanResult(true, true);
                    break;
            }
        }

        string ScanHint()
        {
            switch (smode)
            {
                case 1: return "Vise un QR code : son cadre est repere tout seul et son contenu s'affiche des qu'il est lu. Si rien ne s'affiche, touche Scanner : photo dans 3 secondes, lecture du code QR seulement.";
                case 2: return "Vise du texte et touche Scanner : photo dans 3 secondes, le texte est lu par Windows (hors ligne) ; tu peux ensuite le copier ou le traduire.";
                case 3: return "Vise du texte (panneau, menu, notice) et touche Scanner : photo dans 3 secondes, lecture par Windows, puis traduction automatique en ligne (service MyMemory) vers la langue choisie.";
                case 5: return "Vise une carte de visite et touche Scanner : le code QR est lu s'il y en a un, sinon le texte est classe en telephone, e-mail et site.";
                default: return "Vise un QR code : son cadre est repere tout seul et son contenu s'affiche des qu'il est lu (la detection ne tourne que dans cet onglet). Pour lire du texte, touche Scanner : photo dans 3 secondes, lecture du QR code, sinon du texte par Windows.";
            }
        }

        void StartScanMode()
        {
            autoTranslate = smode == 3;
            switch (smode)
            {
                case 1: StartScan(true, false, false); break;
                case 2: StartScan(false, true, false); break;
                case 3: StartScan(false, true, false); break;
                case 5: StartScan(true, true, true); break;
                default: StartScan(true, true, false); break;
            }
        }

        protected override void Rescan() { StartScanMode(); }

        // ------------------------------------------------------------------ onglet Scanner : planning
        void OnPlanChanged() { if (IsOpen) Rebuild(); }
        void OnPlanStatus() { if (IsOpen) Refresh(); }

        void StartPlan()
        {
            scanNote = "";
            planRoom = -1; planDay = 0;
            plan.Start();
        }

        void BuildPlanTab()
        {
            switch (plan.phase)
            {
                case Ar6dofPlanSession.Phase.Idle:
                    Para("Vise le planning de tres pres (le texte doit etre gros et net) et touche Scanner le planning : 3 secondes, puis une photo toutes les 1,4 secondes pendant que tu balaies l'affiche lentement, de haut en bas et de gauche a droite, a distance constante et l'affiche bien en face de toi. Touche Terminer quand tout est passe (10 photos au plus).", 128, 17);
                    var go = Btn("Scanner le planning", StartPlan, 64f, 26);
                    Ar6dofTheme.SetSelected(go, true);
                    break;
                case Ar6dofPlanSession.Phase.Capturing:
                    var st = Para("", 56, 21);
                    refreshers.Add(() => st.text = plan.status);
                    BtnRow(60f, 24, "Terminer", (Action)plan.Finish, "Annuler", (Action)plan.Cancel);
                    break;
                case Ar6dofPlanSession.Phase.Reading:
                    var st2 = Para("", 56, 21);
                    refreshers.Add(() => st2.text = plan.status);
                    Btn("Annuler", plan.Cancel, 52f, 22);
                    break;
                default:
                    BuildPlanResult();
                    break;
            }
        }

        int AnchorShift { get { return plan.result != null && plan.result.relative ? plan.anchor * 60 : 0; } }

        static string Hm(int minutes)
        {
            int m = ((minutes % 1440) + 1440) % 1440;
            return (m / 60).ToString("00") + ":" + (m % 60).ToString("00");
        }

        string PlanRoomName()
        {
            var res = plan.result;
            if (res == null || planRoom < 0 || planRoom >= res.rooms.Count) return "";
            return res.rooms[planRoom];
        }

        void BuildPlanResult()
        {
            if (scanNote.Length > 0) Lbl(scanNote, 30, 19).MakeAccent();
            var res = plan.result;
            if (plan.error.Length > 0 || res == null)
            {
                Para(plan.error.Length > 0 ? plan.error : "Rien de lisible.", 80, 19);
                Btn("Nouveau scan", StartPlan, 52f, 22);
                return;
            }
            // apercu de ce qui a ete compris : cadre jaune = bloc trouve, croix rouge = bloc non trouve (duree supposee)
            if (plan.previewPx != null && plan.previewW > 1)
            {
                if (planTex == null || planTexSrc != plan.previewPx)
                {
                    DropPlanTex();
                    planTex = new Texture2D(plan.previewW, plan.previewH, TextureFormat.RGBA32, false);
                    planTex.SetPixels32(plan.previewPx);
                    planTex.Apply(false, false);
                    planTexSrc = plan.previewPx;
                }
                float asp = (float)plan.previewW / plan.previewH;
                float ph = Mathf.Min(IW / asp, 520f), pw = ph * asp;
                float pcy = RowY(ph);
                var pr = Ar6dofVrUi.Rect(content, "Planning", new Vector2(0f, pcy), new Vector2(pw, ph));
                var pri = pr.gameObject.AddComponent<RawImage>();
                pri.texture = planTex;
                pri.raycastTarget = false;
            }
            Lbl(Ar6dofLoc.F("Planning : {0} evenement(s), {1} salle(s)", res.events.Count, res.rooms.Count), 30, 20).MakeAccent();
            if (plan.info.Length > 0) Para(plan.info, 40, 14);
            if (res.events.Count == 0)
            {
                Para(res.info.Length > 0 ? res.info : "Aucun evenement trouve dans la grille", 70, 18);
                Para("Rapproche-toi pour que les heures de la colonne de gauche et les titres soient bien lisibles, et balaie toute l'affiche.", 60, 16);
                Btn("Nouveau scan", StartPlan, 52f, 22);
                return;
            }

            // heures illisibles : les heures sont comptees depuis la premiere ligne, l'utilisateur donne celle-ci
            if (res.relative)
            {
                Para("Les heures de l'affiche sont illisibles : elles sont comptees depuis la premiere ligne. Regle l'heure de cette premiere ligne (le haut de la colonne des heures).", 56, 15);
                var ab = BtnRow(52f, 21, "", (Action)(() => { plan.anchor = (plan.anchor + 23) % 24; Rebuild(); }),
                                         "", (Action)(() => { plan.anchor = (plan.anchor + 1) % 24; Rebuild(); }));
                ab[0].GetComponentInChildren<Text>().text = Ar6dofLoc.T("Premiere ligne") + " " + plan.anchor.ToString("00") + ":00  -1 h";
                ab[1].GetComponentInChildren<Text>().text = Ar6dofLoc.T("Premiere ligne") + " " + plan.anchor.ToString("00") + ":00  +1 h";
            }

            // salle et jour
            var fb = BtnRow(52f, 20, "", (Action)(() =>
                                    {
                                        planRoom++;
                                        if (planRoom >= res.rooms.Count) planRoom = -1;
                                        Rebuild();
                                    }),
                                     "", (Action)(() => { planDay = (planDay + 1) % 7; Refresh(); }));
            var fRoom = fb[0].GetComponentInChildren<Text>();
            var fDay = fb[1].GetComponentInChildren<Text>();
            refreshers.Add(() =>
            {
                string rn = PlanRoomName();
                fRoom.text = Ar6dofLoc.T("Salle") + " : " + (rn.Length > 0 ? rn : Ar6dofLoc.T("toutes"));
                fDay.text = planDay == 0 ? Ar6dofLoc.T("Jour : aujourd'hui") : (planDay == 1 ? Ar6dofLoc.T("Jour : demain") : Ar6dofLoc.F("Jour : dans {0} jours", planDay));
            });
            if (res.rooms.Count == 0) Para("Aucun nom de salle lu au-dessus de la premiere heure : tous les evenements sont dans la meme liste.", 40, 15);

            // liste (un ? apres l'heure de fin : la fin n'a pas ete trouvee, une heure est supposee)
            string room = PlanRoomName();
            var sb = new System.Text.StringBuilder();
            int nShown = 0, total = 0;
            foreach (var e in res.events)
            {
                if (room.Length > 0 && e.room != room) continue;
                total++;
                if (nShown >= 18) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Hm(e.startMin + AnchorShift)).Append('-').Append(Hm(e.endMin + AnchorShift)).Append(e.endKnown ? "  " : "?  ").Append(e.title);
                if (room.Length == 0 && e.room.Length > 0) sb.Append("  (").Append(e.room).Append(')');
                nShown++;
            }
            string listText = sb.ToString();
            if (total > nShown) listText += "\n" + Ar6dofLoc.F("... et {0} de plus", total - nShown);
            ParaFit(listText, 17, 22);
            Para("Cadre jaune sur l'apercu : bloc trouve. Croix rouge ou ? : fin non trouvee, une heure est supposee. Relis avant d'enregistrer : la lecture du texte peut se tromper.", 56, 14);

            int upcoming = 0;
            DateTime now = DateTime.Now;
            foreach (var p in plan.Events(room, planDay, now)) if (p.start > now) upcoming++;
            if (upcoming > 0) Btn(Ar6dofLoc.F("Ajouter au calendrier ({0})", upcoming), AddPlanToCalendar, 52f, 21);
            else Para("Aucun de ces evenements n'est dans le futur pour le jour choisi : change le jour.", 40, 16);

            string copyAll = listText;
            Text cl = null;
            var cb = BtnRow(52f, 21, "Copier la liste", (Action)(() =>
            {
                try { GUIUtility.systemCopyBuffer = copyAll; } catch (Exception) { }
                if (cl != null) cl.text = Ar6dofLoc.T("Copie !");
            }), "Nouveau scan", (Action)StartPlan);
            cl = cb[0].GetComponentInChildren<Text>();
        }

        void AddPlanToCalendar()
        {
            var svc = Ar6dofCalendarService.Instance;
            if (svc == null) { scanNote = Ar6dofLoc.T("Calendrier indisponible."); Rebuild(); return; }
            var list = new List<Ar6dofCalEvent>();
            DateTime now = DateTime.Now;
            foreach (var p in plan.Events(PlanRoomName(), planDay, now))
            {
                if (p.start <= now) continue;
                var ev = new Ar6dofCalEvent { title = p.title, place = p.place, durMin = p.durMin, remind = true };
                ev.Start = p.start;
                list.Add(ev);
            }
            int added = svc.AddRange(list);
            scanNote = added == 0 ? Ar6dofLoc.T("Rien de nouveau a enregistrer") : Ar6dofLoc.F("{0} evenement(s) ajoute(s)", added);
            Rebuild();
        }

        void DropPlanTex()
        {
            if (planTex != null) UnityEngine.Object.Destroy(planTex);
            planTex = null; planTexSrc = null;
        }

        static void OpenFolder()
        {
            Ar6dofLibrary.OpenInExplorer(Ar6dofPassthrough.PhotosFolder);
        }

        // ------------------------------------------------------------------ photo simple
        void UpdateShootLabel()
        {
            if (shootLabel == null) return;
            var pt = Pt;
            shootLabel.text = Ar6dofLoc.T(pt != null && pt.PhotoBusy ? "Annuler" : "Prendre la photo");
        }

        void OnShoot()
        {
            var pt = Pt;
            if (pt == null) return;
            if (pt.PhotoBusy) { pt.CancelPhoto(); return; }
            var es = pt.EyeSize;
            var o = new Ar6dofPassthrough.PhotoOptions
            {
                withUi = ui != 0,
                eye = eye,
                width = es.x, height = es.y,     // resolution reelle de la camera : agrandir n'ajouterait aucun detail
                burst = quality == 0 ? 1 : 8,    // accumulation : plusieurs images alignees puis additionnees (moins de bruit, un peu plus de detail)
                upscale = quality == 2 ? 2 : 1,
            };
            int delay = Timers[Mathf.Clamp(timer, 0, Timers.Length - 1)];
            status = delay > 0 ? "Minuteur lance..." : "";
            lastCount = -1;
            if (delay > 0) ShowCount(delay);
            pt.TakePhoto(o, delay, OnCount, HideUi, OnDone);
            UpdateShootLabel();
        }

        // ------------------------------------------------------------------ minuteur
        // Le chiffre est affiche sur l'apercu (dans la fenetre) et, pour ne pas la rater, devant toi : jamais dans le PNG.
        void ShowCount(int sec)
        {
            DestroyCount();
            if (countOverlay != null) countOverlay.text = sec.ToString();
            var head = Pt != null ? Pt.HeadView : null;
            if (head == null) return;
            countCanvas = Ar6dofVrUi.NewCanvas("Compte a rebours", new Vector2(260f, 260f));
            countText = Ar6dofVrUi.Label(countCanvas.transform, sec.ToString(), Vector2.zero, new Vector2(240f, 240f), 150, TextAnchor.MiddleCenter);
            Vector3 f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            var tr = countCanvas.transform;
            tr.position = head.position + f * Mathf.Clamp(Ar6dofSettings.Current.openDistance + 0.2f, 0.5f, 2f);
            tr.rotation = Quaternion.LookRotation(tr.position - head.position, Vector3.up);
            SetLayer(countCanvas.gameObject, PreviewLayer);
        }

        void DestroyCount()
        {
            if (countCanvas != null) UnityEngine.Object.Destroy(countCanvas.gameObject);
            countCanvas = null; countText = null;
            if (countOverlay != null) countOverlay.text = "";
        }

        void OnCount(int sec)
        {
            if (sec == lastCount) return;   // appele a chaque image : on ne touche aux textes que quand le chiffre change
            lastCount = sec;
            string t = sec > 0 ? sec.ToString() : "";
            if (countText != null) countText.text = t;
            if (countOverlay != null) countOverlay.text = t;
            status = sec > 0 ? Ar6dofLoc.F("Photo dans {0} s...", sec) : "";
            if (canvas != null && canvas.gameObject.activeSelf) Refresh();
        }

        void HideUi(bool hide)
        {
            if (hide) DestroyCount();
            if (canvas != null) canvas.gameObject.SetActive(!hide && shown);
        }

        void OnDone(List<string> files, Texture2D tex, string err)
        {
            DestroyCount();
            if (tex != null)
            {
                if (string.IsNullOrEmpty(err) && files != null && files.Count > 0) ShowShot(tex);   // la fenetre garde la photo quelques secondes
                else UnityEngine.Object.Destroy(tex);
            }
            if (!string.IsNullOrEmpty(err)) status = err == "Annule" ? "Photo annulee" : Ar6dofLoc.F("Echec : {0}", err);
            else if (files == null || files.Count == 0) status = "Rien a enregistrer";
            else status = Ar6dofLoc.F("Photo enregistree : {0}", Path.GetFileName(files[0])) + (Pt != null && quality != 0 && Pt.LastStackInfo.Length > 0 ? " (" + Pt.LastStackInfo + ")" : "");
            if (canvas != null && canvas.gameObject.activeSelf) Refresh();
        }
    }
}
