using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Onglet Photo, modes en serie de l'appareil photo : Panorama (images placees par l'orientation de la tete, Ar6dofPano), Video (AVI Motion JPEG sans son, ecrit au fil de l'eau, Ar6dofAvi)
    /// et Rafale / intervalle (une serie de PNG dans un dossier). Tout passe par Ar6dofPassthrough.StartSweep, camera seule (la fenetre de l'appareil photo reste affichee). Rien ne tourne hors serie.
    /// </summary>
    public partial class Ar6dofCameraApp
    {
        static readonly int[] VidWidths = { 640, 960, 1280 };
        static readonly int[] BurstCounts = { 5, 10, 20, 50 };
        static readonly float[] BurstIntervals = { 0.3f, 1f, 5f, 30f };
        int vidRes, burstN, burstI;

        int sKind;    // 0 rien, 1 panorama, 2 video, 3 rafale
        int sPhase;   // 0 rien, 1 prise en cours, 2 assemblage du panorama
        int sGen;

        // panorama
        List<Color32[]> pFrames;
        List<Quaternion> pRots;
        Quaternion pLast;
        int pW, pH, panoW, panoH;
        float pTan;
        Task<Color32[]> panoTask;

        // video
        Ar6dofAvi avi;
        int vFrames;
        float vT0 = -1f, vLast;

        // rafale
        string bDir = "";
        int bCount;

        // ------------------------------------------------------------------ arret
        /// <summary>Arrete la serie en cours. 'keep' : la video et la rafale gardent ce qui est pris (fenetre reduite) ; sinon tout est annule. Un panorama n'est jamais garde a moitie.</summary>
        void StopSeries(bool keep)
        {
            var pt = Pt;
            if (sPhase == 1 && pt != null && pt.PhotoBusy)
            {
                if (keep && sKind != 1) pt.StopSweep(); else pt.CancelPhoto();
            }
            if (sPhase == 2)
            {
                sGen++;
                panoTask = null; pFrames = null; pRots = null;
                sPhase = 0; sKind = 0;
            }
        }

        void FinishSeries() { var pt = Pt; if (pt != null && sPhase == 1) pt.StopSweep(); }

        void CancelSeries()
        {
            var pt = Pt;
            if (sPhase == 1 && pt != null && pt.PhotoBusy) { pt.CancelPhoto(); return; }   // la serie repond « Annule » et remet tout a zero
            StopSeries(false);
            status = "Annule";
            RebuildSafe();
        }

        void RebuildSafe() { if (IsOpen) Rebuild(); }

        void OnSeriesCount(int sec)
        {
            if (sec == lastCount) return;   // appele a chaque image : on ne touche aux textes que quand le chiffre change
            lastCount = sec;
            if (countOverlay != null) countOverlay.text = sec > 0 ? sec.ToString() : "";
            status = sec > 0 ? Ar6dofLoc.F("Demarrage dans {0} s...", sec) : "";
        }

        // ------------------------------------------------------------------ interface commune
        void BuildEyeRow()
        {
            var eb = BtnRow(48f, 21, "", (Action)(() => { eye = eye != 0 ? 0 : 1; Put("eye", eye); Rebuild(); }));
            var et = eb[0].GetComponentInChildren<Text>();
            refreshers.Add(() => et.text = Ar6dofLoc.T(eye == 0 ? "Camera : gauche" : "Camera : droite"));
        }

        void BuildSeriesStatus()
        {
            var st = Para("", 40, 17);
            refreshers.Add(() => st.text = status.Length > 0 ? Ar6dofLoc.T(status) : "");
        }

        // serie en cours : l'etat, Terminer (garde ce qui est pris) et Annuler
        void BuildSeriesRunning()
        {
            var st = Para("", 56, 21);
            refreshers.Add(() => st.text = status.Length > 0 ? Ar6dofLoc.T(status) : "");
            if (sPhase == 1) BtnRow(64f, 26, "Terminer", (Action)FinishSeries, "Annuler", (Action)CancelSeries);
            else Btn("Annuler", CancelSeries, 52f, 22);
        }

        void BuildFolderRow()
        {
            BtnRow(48f, 21, "Ouvrir le dossier", (Action)(() => OpenFolder()));
        }

        bool SeriesReady()
        {
            var pt = Pt;
            return pt != null && !pt.PhotoBusy && sPhase == 0;
        }

        // ------------------------------------------------------------------ panorama
        void BuildPano()
        {
            if (sKind == 1 && sPhase != 0) { BuildSeriesRunning(); return; }
            Para("Panorama : touche Demarrer puis tourne lentement la tete, sans bouger le corps ; une image est gardee tous les 14 degres environ (40 au plus). Camera seule, sans les interfaces. Pour un sujet tres proche (affiche...), utilise plutot le mode Planning du scanner.", 100, 17);
            BuildEyeRow();
            BuildTimerRow();
            var go = Btn("Demarrer le panorama", StartPano, 76f, 28);
            Ar6dofTheme.SetSelected(go, true);
            BuildSeriesStatus();
            BuildFolderRow();
        }

        void StartPano()
        {
            if (!SeriesReady()) return;
            var pt = Pt;
            var es = pt.EyeSize;
            float k = Mathf.Min(1f, 1280f / es.x);   // images reduites : la memoire reste raisonnable
            pW = Mathf.Max(64, Mathf.RoundToInt(es.x * k)); pH = Mathf.Max(64, Mathf.RoundToInt(es.y * k));
            pTan = pt.DisplayTan;
            pFrames = new List<Color32[]>(); pRots = new List<Quaternion>();
            int my = ++sGen;
            sKind = 1; sPhase = 1; status = ""; lastCount = -1;
            var o = new Ar6dofPassthrough.SweepOptions { width = pW, height = pH, eye = eye, withUi = false, interval = 0.5f, delay = Timers[Mathf.Clamp(timer, 0, Timers.Length - 1)], maxFrames = 400, maxSeconds = 90f };
            pt.StartSweep(o, OnSeriesCount, OnPanoFrame, e => OnPanoDone(my, e));
            Rebuild();
        }

        void OnPanoFrame(Texture2D t, Quaternion q, int index)
        {
            if (pFrames == null) return;
            if (pFrames.Count > 0 && Quaternion.Angle(q, pLast) < 14f) return;   // pas assez tourne depuis la derniere image gardee
            pFrames.Add(t.GetPixels32());
            pRots.Add(q);
            pLast = q;
            status = Ar6dofLoc.F("Panorama : {0} images. Continue de tourner la tete lentement", pFrames.Count);
            if (pFrames.Count >= 40 && Pt != null) Pt.StopSweep();
        }

        void OnPanoDone(int my, string err)
        {
            if (my != sGen) return;
            if (err != null || pFrames == null || pFrames.Count < 2)
            {
                status = err == "Annule" ? "Panorama annule" : (err != null ? Ar6dofLoc.F("Echec : {0}", err) : "Panorama : tourne la tete pour prendre au moins 2 images");
                pFrames = null; pRots = null; sPhase = 0; sKind = 0;
                RebuildSafe();
                return;
            }
            sPhase = 2;
            status = "Assemblage du panorama...";
            var fr = pFrames; var rt = pRots; int w = pW, h = pH; float tn = pTan;
            panoTask = Task.Run(() =>
            {
                int ow, oh;
                Color32[] px = Ar6dofPano.Build(fr, rt, w, h, tn, 6000, out ow, out oh);
                panoW = ow; panoH = oh;
                return px;
            });
            RebuildSafe();
        }

        // a appeler a chaque image par OnTick : termine le panorama quand le calcul est fini
        void TickSeries()
        {
            if (sPhase != 2 || panoTask == null || !panoTask.IsCompleted) return;
            var task = panoTask;
            panoTask = null;
            pFrames = null; pRots = null;
            sPhase = 0; sKind = 0;
            Color32[] px = null;
            string err = "";
            if (task.IsFaulted) err = task.Exception != null ? task.Exception.GetBaseException().Message : "?";
            else px = task.Result;
            if (px == null)
            {
                status = err.Length > 0 ? Ar6dofLoc.F("Echec : {0}", err) : "Panorama impossible : les images ne se recouvrent pas";
                RebuildSafe();
                return;
            }
            try
            {
                string dir = Ar6dofPassthrough.PhotosFolder;
                Directory.CreateDirectory(dir);
                var tex = new Texture2D(panoW, panoH, TextureFormat.RGB24, false);
                tex.SetPixels32(px);
                tex.Apply(false, false);
                string path = Path.Combine(dir, "ar6dof_pano_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                status = Ar6dofLoc.F("Panorama enregistre : {0} ({1} x {2})", Path.GetFileName(path), panoW, panoH);
                ShowShot(tex);
            }
            catch (Exception e) { status = Ar6dofLoc.F("Echec : {0}", e.Message); }
            RebuildSafe();
        }

        // ------------------------------------------------------------------ video
        void VideoSize(out int w, out int h)
        {
            var pt = Pt;
            var es = pt != null ? pt.EyeSize : new Vector2Int(960, 720);
            float asp = pt != null ? pt.EyeAspect : 4f / 3f;
            w = Mathf.Max(64, Mathf.Min(VidWidths[Mathf.Clamp(vidRes, 0, VidWidths.Length - 1)], es.x)) & ~1;
            h = Mathf.Max(64, Mathf.RoundToInt(w / asp)) & ~1;
        }

        void BuildVideo()
        {
            if (sKind == 2 && sPhase != 0) { BuildSeriesRunning(); return; }
            Para("Video : touche Demarrer, puis Terminer. Fichier AVI (Motion JPEG) sans son, camera seule sans les interfaces, 5 minutes au plus ; environ 15 images par seconde selon l'ordinateur.", 80, 17);
            BuildEyeRow();
            var sb = BtnRow(48f, 21, "", (Action)(() => { vidRes = (vidRes + 1) % VidWidths.Length; Put("vidres", vidRes); Refresh(); }));
            var st0 = sb[0].GetComponentInChildren<Text>();
            refreshers.Add(() => { int vw, vh; VideoSize(out vw, out vh); st0.text = Ar6dofLoc.F("Taille : {0} x {1}", vw, vh); });
            BuildTimerRow();
            var go = Btn("Demarrer la video", StartVideo, 76f, 28);
            Ar6dofTheme.SetSelected(go, true);
            BuildSeriesStatus();
            BuildFolderRow();
        }

        void StartVideo()
        {
            if (!SeriesReady()) return;
            var pt = Pt;
            int vw, vh;
            VideoSize(out vw, out vh);
            string dir = Ar6dofPassthrough.PhotosFolder;
            try { Directory.CreateDirectory(dir); } catch (Exception) { }
            string path = Path.Combine(dir, "ar6dof_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".avi");
            var a = new Ar6dofAvi();
            string err = a.Open(path, vw, vh);
            if (err.Length > 0) { status = Ar6dofLoc.F("Echec : {0}", err); Rebuild(); return; }
            avi = a; vFrames = 0; vT0 = -1f; vLast = 0f;
            int my = ++sGen;
            sKind = 2; sPhase = 1; status = ""; lastCount = -1;
            var o = new Ar6dofPassthrough.SweepOptions { width = vw, height = vh, eye = eye, withUi = false, interval = 1f / 15f, delay = Timers[Mathf.Clamp(timer, 0, Timers.Length - 1)], maxFrames = int.MaxValue, maxSeconds = 300f };
            pt.StartSweep(o, OnSeriesCount, (t, q, i) => OnVideoFrame(a, t), e => OnVideoDone(my, a, e));
            Rebuild();
        }

        void OnVideoFrame(Ar6dofAvi a, Texture2D t)
        {
            byte[] jpg = t.EncodeToJPG(80);
            a.Add(jpg);
            float now = Time.unscaledTime;
            if (vT0 < 0f) vT0 = now;
            vLast = now;
            vFrames++;
            status = Ar6dofLoc.F("Video en cours : {0} s, {1} images", Mathf.RoundToInt(vLast - vT0), vFrames);
        }

        void OnVideoDone(int my, Ar6dofAvi a, string err)
        {
            string name = Path.GetFileName(a.FilePath ?? "");
            string msg;
            if (err == "Annule" || vFrames == 0)
            {
                a.Abort();   // le fichier est supprime
                msg = err == "Annule" ? "Video annulee" : (err != null ? Ar6dofLoc.F("Echec : {0}", err) : "Video vide : rien n'a ete enregistre");
            }
            else
            {
                double dur = Math.Max(0.1, vLast - vT0);
                double fps = vFrames > 1 ? (vFrames - 1) / dur : 15.0;   // cadence reelle : la lecture a la bonne vitesse
                string cerr = a.Close(fps);
                if (cerr.Length > 0) msg = Ar6dofLoc.F("Echec : {0}", cerr);
                else
                {
                    msg = Ar6dofLoc.F("Video enregistree : {0} ({1} s, {2} images)", name, Mathf.RoundToInt((float)dur), vFrames);
                    if (err != null) msg += " - " + Ar6dofLoc.F("Echec : {0}", err);
                }
            }
            if (my != sGen) return;
            avi = null;
            status = msg;
            sPhase = 0; sKind = 0;
            RebuildSafe();
        }

        // ------------------------------------------------------------------ rafale / intervalle
        void BuildBurst()
        {
            if (sKind == 3 && sPhase != 0) { BuildSeriesRunning(); return; }
            Para("Rafale : une serie de photos (PNG, a la resolution de la camera) rangees dans un dossier Rafale_..., a l'intervalle choisi : 0,3 s pour une rafale, 5 ou 30 s pour un intervalle (accelere). Camera seule, sans les interfaces.", 80, 17);
            BuildEyeRow();
            var bb = BtnRow(48f, 21, "", (Action)(() => { burstN = (burstN + 1) % BurstCounts.Length; Put("burstn", burstN); Refresh(); }),
                                      "", (Action)(() => { burstI = (burstI + 1) % BurstIntervals.Length; Put("bursti", burstI); Refresh(); }));
            var tn = bb[0].GetComponentInChildren<Text>();
            var ti = bb[1].GetComponentInChildren<Text>();
            refreshers.Add(() =>
            {
                tn.text = Ar6dofLoc.F("Photos : {0}", BurstCounts[burstN]);
                ti.text = Ar6dofLoc.F("Intervalle : {0} s", BurstIntervals[burstI].ToString("0.#"));
            });
            BuildTimerRow();
            var go = Btn("Demarrer la rafale", StartBurst, 76f, 28);
            Ar6dofTheme.SetSelected(go, true);
            BuildSeriesStatus();
            BuildFolderRow();
        }

        void StartBurst()
        {
            if (!SeriesReady()) return;
            var pt = Pt;
            var es = pt.EyeSize;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            bDir = Path.Combine(Ar6dofPassthrough.PhotosFolder, "Rafale_" + stamp);
            try { Directory.CreateDirectory(bDir); }
            catch (Exception e) { status = Ar6dofLoc.F("Echec : {0}", e.Message); Rebuild(); return; }
            bCount = 0;
            int my = ++sGen;
            sKind = 3; sPhase = 1; status = ""; lastCount = -1;
            var o = new Ar6dofPassthrough.SweepOptions { width = es.x, height = es.y, eye = eye, withUi = false, interval = BurstIntervals[burstI], delay = Timers[Mathf.Clamp(timer, 0, Timers.Length - 1)], maxFrames = BurstCounts[burstN], maxSeconds = 7200f };
            pt.StartSweep(o, OnSeriesCount, OnBurstFrame, e => OnBurstDone(my, e));
            Rebuild();
        }

        void OnBurstFrame(Texture2D t, Quaternion q, int index)
        {
            string path = Path.Combine(bDir, "img_" + (index + 1).ToString("000") + ".png");
            File.WriteAllBytes(path, t.EncodeToPNG());
            bCount++;
            status = Ar6dofLoc.F("Rafale : {0}/{1} photos", bCount, BurstCounts[burstN]);
            var c = new Texture2D(t.width, t.height, TextureFormat.RGB24, false);
            c.SetPixels32(t.GetPixels32());
            c.Apply(false, false);
            ShowShot(c);   // la miniature montre la derniere photo prise
        }

        void OnBurstDone(int my, string err)
        {
            if (my != sGen) return;
            string folder = Path.GetFileName(bDir ?? "");
            if (err != null && err != "Annule") status = Ar6dofLoc.F("Echec : {0}", err) + (bCount > 0 ? " - " + Ar6dofLoc.F("{0} photos gardees dans {1}", bCount, folder) : "");
            else if (bCount == 0) status = err == "Annule" ? "Rafale annulee" : "Rien n'a ete enregistre";
            else status = err == "Annule" ? Ar6dofLoc.F("Rafale annulee : {0} photos gardees dans {1}", bCount, folder) : Ar6dofLoc.F("Rafale enregistree : {0} photos dans {1}", bCount, folder);
            sPhase = 0; sKind = 0;
            RebuildSafe();
        }
    }
}
