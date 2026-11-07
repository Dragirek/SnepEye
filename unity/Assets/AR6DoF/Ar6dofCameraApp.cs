using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Appareil photo : aperçu en direct de ce que voit le casque (image de la caméra corrigée, sans la déformation des lentilles) au format réel de la caméra,
    /// et photo à sa résolution, de la caméra gauche ou droite, avec ou sans les interfaces. Minuteur : le compte à rebours s'affiche sur l'aperçu (pas sur le PNG).
    /// Les fichiers PNG vont dans Images\AR6DoF\Photos. Onglet Scanner : photo temporaire, lecture d'un QR code (lien, Wi-Fi, contact, événement...) ou du texte (OCR Windows),
    /// copie, ouverture d'un lien, traduction en ligne sur demande, ajout au calendrier ; le même scan que celui du calendrier (Ar6dofScanSession). Rien ne tourne tant que la fenêtre est fermée ou réduite.
    /// </summary>
    public class Ar6dofCameraApp : Ar6dofScanWindow
    {
        static readonly int[] Timers = { 0, 3, 5, 10 };
        const int PreviewLayer = 30;   // la fenetre est sur cette couche : l'apercu en direct l'exclut (sinon il se filmerait lui-meme)

        int mode;   // 0 photo, 1 scanner
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

        // ---- detection du QR code en direct (onglet Scanner seulement) : l'apercu est lu 5 fois par seconde dans un autre fil, le cadre du QR est dessine des qu'il est vu, et le contenu s'affiche des qu'il est lu
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

        public Ar6dofCameraApp(Ar6dofDock d) : base(d, "camera", "Appareil photo", "camera", "Apercu en direct, photo et scanner (QR code, texte)", 700f)
        {
            ui = Get("ui", 1); eye = Get("eye", 0); timer = Get("timer", 1); quality = Get("quality", 0);
        }

        static int Get(string k, int def) { try { return PlayerPrefs.GetInt("ar6dof_cam_" + k, def); } catch (Exception) { return def; } }
        static void Put(string k, int v) { try { PlayerPrefs.SetInt("ar6dof_cam_" + k, v); } catch (Exception) { } }

        protected override void OnRebuildStart() { base.OnRebuildStart(); shootLabel = null; countOverlay = null; quadLines = null; liveLabel = null; }

        public override void OnClosed() { mode = 0; base.OnClosed(); }

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
                var pt = dock.passthrough;
                if (pt != null) { if (pt.PhotoBusy) pt.CancelPhoto(); pt.ReleasePreviewScene(); }
                DestroyCount();
                ReleasePreview();
            }
        }

        public override void Destroy()
        {
            DropLive();
            AbortScan();
            DestroyCount();
            ReleasePreview();
            base.Destroy();
        }

        void ReleasePreview()
        {
            if (preview != null) { preview.Release(); UnityEngine.Object.Destroy(preview); }
            preview = null;
        }

        Ar6dofPassthrough Pt { get { return dock.passthrough; } }

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
            var pt = Pt;
            if (pt == null || canvas == null || !canvas.gameObject.activeInHierarchy || pt.PhotoShooting) return;   // le minuteur ne fige pas l'apercu
            if (Time.unscaledTime - lastPreview < 1f / 30f) return;                                              // 30 images/s
            lastPreview = Time.unscaledTime;
            EnsurePreview();
            pt.RenderPreview(preview, mode == 0 && ui != 0, PreviewLayer);   // onglet Scanner : l'apercu est toujours sans interfaces (la photo du scan aussi)
            if (mode == 1 && LiveOn && scan.phase == Ar6dofScanSession.Phase.Idle) TickLive();
            else if (liveTask != null || liveQuad != null) DropLive();
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

        protected override void BuildMain()
        {
            var pt = Pt;
            if (pt == null) { Para("Le passthrough n'est pas disponible dans cette scene.", 40, 20); return; }

            Tabs(new[] { "Photo", "Scanner" }, mode, t =>
            {
                if (t == mode) return;
                if (pt.PhotoBusy) pt.CancelPhoto();
                AbortScan();
                mode = t;
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
            quadLines = null;
            if (mode == 1)
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

            if (mode == 1) { BuildScanTab(); return; }

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

            Lbl("Minuteur", 30, 20).MakeAccent();
            var tn = new string[Timers.Length];
            for (int i = 0; i < tn.Length; i++) tn[i] = Timers[i] == 0 ? "Aucun" : Timers[i] + " s";
            Tabs(tn, timer, t => { timer = t; Put("timer", t); });

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

        // onglet Scanner : la photo est temporaire (supprimee des que la lecture est finie) ; le compte a rebours s'affiche sur l'apercu
        void BuildScanTab()
        {
            refreshers.Add(() =>
            {
                if (countOverlay != null) countOverlay.text = scan.phase == Ar6dofScanSession.Phase.Countdown && scan.countSec > 0 ? scan.countSec.ToString() : "";
            });
            switch (scan.phase)
            {
                case Ar6dofScanSession.Phase.Idle:
                    liveLabel = Lbl("", 32, 20, TextAnchor.MiddleCenter);
                    liveLabel.fontStyle = FontStyle.Bold;
                    refreshers.Add(() => { if (liveLabel != null && !LiveOn) liveLabel.text = ""; });
                    ToggleRow("Detection automatique du QR code", () => LiveOn, v => { LiveOn = v; if (!v) DropLive(); else UpdateQuad(); }, 46f, 20);
                    Para("Vise un QR code : son cadre est repere tout seul et son contenu s'affiche des qu'il est lu (la detection ne tourne que dans cet onglet). Pour lire du texte, touche Scanner : photo dans 3 secondes, lecture du QR code, sinon du texte par Windows.", 100, 18);
                    var go = Btn("Scanner", () => StartScan(true), 64f, 26);
                    Ar6dofTheme.SetSelected(go, true);
                    break;
                case Ar6dofScanSession.Phase.Countdown:
                case Ar6dofScanSession.Phase.Reading:
                    BuildScanBusy();
                    break;
                default:
                    BuildScanResult(true, true);
                    break;
            }
        }

        static void OpenFolder()
        {
            Ar6dofLibrary.OpenInExplorer(Ar6dofPassthrough.PhotosFolder);
        }

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
            if (tex != null) UnityEngine.Object.Destroy(tex);
            if (!string.IsNullOrEmpty(err)) status = err == "Annule" ? "Photo annulee" : Ar6dofLoc.F("Echec : {0}", err);
            else if (files.Count == 0) status = "Rien a enregistrer";
            else status = Ar6dofLoc.F("Photo enregistree : {0}", Path.GetFileName(files[0])) + (Pt != null && quality != 0 && Pt.LastStackInfo.Length > 0 ? " (" + Pt.LastStackInfo + ")" : "");
            if (canvas != null && canvas.gameObject.activeSelf) Refresh();
        }
    }
}
