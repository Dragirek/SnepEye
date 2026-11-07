using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Scan d'un planning (affiche, programme de convention) par accumulation de photos : apres 3 s, une photo de pres toutes les 1,4 s (jusqu'a 10 ; « Terminer » arrete avant) pendant
    /// que tu balaies l'affiche de la tete ; les images sont assemblees (decalage entre images, Ar6dofAlign), le texte de chaque image est lu AVEC sa position (OCR de Windows,
    /// un seul lancement), puis Ar6dofPlan en tire la grille : heures, salles, debut et fin de chaque bloc. Rien ne tourne hors scan ; les images sont temporaires.
    /// </summary>
    public class Ar6dofPlanSession
    {
        public enum Phase { Idle, Capturing, Reading, Done }

        public Phase phase = Phase.Idle;
        public string status = "", error = "", info = "";
        public int shots, countSec;
        public PlanResult result;
        public Color32[] previewPx;      // apercu de ce qui a ete compris (premiere ligne = bas, pret pour Texture2D)
        public int previewW, previewH;

        readonly Ar6dofDock dock;
        readonly Action changed, statusChanged;
        int gen, lastCount = -1, stage;
        int fw, fh;
        List<Color32[]> frames = new List<Color32[]>();   // premiere ligne = haut
        List<string> paths = new List<string>();
        Task alignTask;
        bool[] used; int[] ox, oy;
        Ar6dofOcr.BoxJob ocr;
        Task<PlanResult> parseTask;
        // sortie du fil de calcul
        Color32[] outPreview; int outPw, outPh; int outKept, outTotal, outLines, outLabels; string outOcr = "";

        public static string TempFolder { get { return Path.Combine(Path.GetTempPath(), "AR6DoF_scan"); } }

        public Ar6dofPlanSession(Ar6dofDock d, Action changed, Action statusChanged)
        {
            dock = d; this.changed = changed; this.statusChanged = statusChanged;
        }

        public bool Busy { get { return phase == Phase.Capturing || phase == Phase.Reading; } }

        void Clear()
        {
            phase = Phase.Idle; status = ""; error = ""; info = ""; shots = 0; countSec = 0; lastCount = -1; stage = 0;
            result = null; previewPx = null; previewW = previewH = 0;
            frames = new List<Color32[]>();
            DeletePaths();
            alignTask = null; ocr = null; parseTask = null; used = null; ox = null; oy = null;
        }

        void DeletePaths()
        {
            foreach (string f in paths) { try { if (f != null) File.Delete(f); } catch (Exception) { } }
            paths = new List<string>();
        }

        /// <summary>Arret sans bruit (fenetre fermee, reduite, autre mode).</summary>
        public void Abort()
        {
            var pt = dock != null ? dock.passthrough : null;
            if (pt != null && pt.PhotoBusy && phase == Phase.Capturing) pt.CancelPhoto();
            gen++;
            Clear();
        }

        /// <summary>Bouton Annuler.</summary>
        public void Cancel()
        {
            var pt = dock != null ? dock.passthrough : null;
            if (phase == Phase.Capturing && pt != null && pt.PhotoBusy) { pt.CancelPhoto(); return; }   // la serie repond « Annule » et remet tout a zero
            gen++;
            Clear();
            if (changed != null) changed();
        }

        /// <summary>Bouton Terminer : garde les images prises et passe a la lecture.</summary>
        public void Finish()
        {
            var pt = dock != null ? dock.passthrough : null;
            if (phase == Phase.Capturing && pt != null) pt.StopSweep();
        }

        public void Start()
        {
            var pt = dock != null ? dock.passthrough : null;
            gen++;
            Clear();
            Ar6dofNotifyService.Resolve("scan");
            if (pt == null || pt.PhotoBusy) { Fail("Camera indisponible : le passthrough n'est pas pret"); return; }
            int my = gen;
            var es = pt.EyeSize;
            fw = es.x; fh = es.y;
            try { Directory.CreateDirectory(TempFolder); } catch (Exception) { }
            phase = Phase.Capturing;
            status = Ar6dofLoc.F("Planning dans {0} s...", 3);
            var o = new Ar6dofPassthrough.SweepOptions { width = es.x, height = es.y, eye = 0, withUi = false, interval = 1.4f, delay = 3f, maxFrames = 10, maxSeconds = 45f };
            pt.StartSweep(o, s => OnCount(my, s), (t, q, i) => OnFrame(my, t, i), e => OnSweepDone(my, e));
            if (changed != null) changed();
        }

        void OnCount(int my, int sec)
        {
            if (my != gen || sec == lastCount) return;
            lastCount = sec;
            countSec = sec;
            status = sec > 0 ? Ar6dofLoc.F("Planning dans {0} s...", sec) : Ar6dofLoc.T("Balaye lentement l'affiche, de pres...");
            if (statusChanged != null) statusChanged();
        }

        void OnFrame(int my, Texture2D t, int index)
        {
            if (my != gen || t == null) return;
            if (frames.Count == 0) { fw = t.width; fh = t.height; }
            if (t.width != fw || t.height != fh) return;
            try
            {
                string path = Path.Combine(TempFolder, "plan_" + DateTime.Now.ToString("HHmmss") + "_" + index + ".png");
                File.WriteAllBytes(path, t.EncodeToPNG());
                frames.Add(Ar6dofAlign.FlipV(t.GetPixels32(), fw, fh));
                paths.Add(path);
            }
            catch (Exception) { return; }
            shots = frames.Count;
            status = Ar6dofLoc.F("Image {0} prise : continue de balayer l'affiche", shots);
            if (statusChanged != null) statusChanged();
        }

        void OnSweepDone(int my, string err)
        {
            if (my != gen) { DeletePaths(); return; }
            if (err == "Annule") { gen++; Clear(); if (changed != null) changed(); return; }
            if (err != null) { Fail(Ar6dofLoc.F("Prise impossible : {0}", err)); return; }
            if (frames.Count == 0) { Fail("Aucune image prise"); return; }
            phase = Phase.Reading;
            stage = 0;
            status = Ar6dofLoc.T("Assemblage et lecture du texte...");
            var fr = frames; int w = fw, h = fh;
            ocr = Ar6dofOcr.StartBoxes(paths);   // supprime les fichiers apres lecture
            paths = new List<string>();
            alignTask = Task.Run(() =>
            {
                bool[] u2; int[] x2, y2;
                Ar6dofAlign.Register(fr, w, h, out u2, out x2, out y2);
                used = u2; ox = x2; oy = y2;
            });
            if (changed != null) changed();
        }

        void Fail(string msg)
        {
            gen++;
            Clear();
            error = msg;
            phase = Phase.Done;
            Ar6dofNotifyService.Post(Ar6dofNoteKind.Error, Ar6dofLoc.T("Appareil photo"), Ar6dofLoc.T(msg), "camera", "scan");
            if (changed != null) changed();
        }

        /// <summary>A appeler a chaque image par la fenetre ouverte (ne fait rien hors lecture).</summary>
        public void Tick()
        {
            if (phase != Phase.Reading) return;
            if (stage == 0)
            {
                if (alignTask == null || !alignTask.IsCompleted || ocr == null || !ocr.done) return;
                if (alignTask.IsFaulted) { Fail(Ar6dofLoc.F("Assemblage impossible : {0}", alignTask.Exception != null ? alignTask.Exception.GetBaseException().Message : "?")); return; }
                if (ocr.error.Length > 0) { Fail(ocr.error); return; }
                stage = 1;
                status = Ar6dofLoc.T("Analyse du planning...");
                var pages = ocr.pages; var frs = frames; var u = used; var xx = ox; var yy = oy; int w = fw, h = fh;
                string ocrInfo = ocr.info;
                parseTask = Task.Run(() => Analyse(pages, frs, w, h, u, xx, yy, ocrInfo));
                if (statusChanged != null) statusChanged();
                return;
            }
            if (stage == 1 && parseTask != null && parseTask.IsCompleted)
            {
                if (parseTask.IsFaulted) { Fail(Ar6dofLoc.F("Analyse impossible : {0}", parseTask.Exception != null ? parseTask.Exception.GetBaseException().Message : "?")); return; }
                result = parseTask.Result;
                previewPx = outPreview; previewW = outPw; previewH = outPh;
                info = Ar6dofLoc.F("{0}/{1} images assemblees, {2} lignes de texte, {3} heures reconnues", outKept, outTotal, outLines, outLabels) + (outOcr.Length > 0 ? " ; OCR " + outOcr : "");
                frames = new List<Color32[]>();   // la memoire des images est rendue
                parseTask = null;
                phase = Phase.Done;
                if (changed != null) changed();
            }
        }

        // fil de calcul : mosaique, lignes de texte, grille, apercu
        PlanResult Analyse(List<Ar6dofOcr.Page> pages, List<Color32[]> frs, int w, int h, bool[] u, int[] xx, int[] yy, string ocrInfo)
        {
            var img = Ar6dofPlan.BuildMosaic(frs, w, h, u, xx, yy);
            int total = frs.Count, kept = 0;
            for (int i = 0; i < u.Length; i++) if (u[i]) kept++;
            var lines = img.frameX != null ? Ar6dofPlan.BuildLines(pages, img.frameX, img.frameY) : new List<PlanLine>();
            var res = Ar6dofPlan.Parse(lines, img);
            int pw, ph;
            Color32[] pv = Ar6dofPlan.DrawPreview(img, res, 760, out pw, out ph);
            outPreview = Ar6dofAlign.FlipV(pv, pw, ph);
            outPw = pw; outPh = ph;
            outKept = kept; outTotal = total; outLines = lines.Count; outLabels = res.labels; outOcr = ocrInfo ?? "";
            return res;
        }

        /// <summary>Evenements pour le calendrier : ceux de la salle choisie (vide = toutes), un jour donne (0 = aujourd'hui).</summary>
        public List<Ar6dofParsedEvent> Events(string room, int dayOffset, DateTime now)
        {
            var list = new List<Ar6dofParsedEvent>();
            if (result == null) return list;
            DateTime baseDay = now.Date.AddDays(dayOffset);
            foreach (var e in result.events)
            {
                if (!string.IsNullOrEmpty(room) && e.room != room) continue;
                var pe = new Ar6dofParsedEvent { title = e.title, place = e.room, start = baseDay.AddMinutes(e.startMin), durMin = Math.Max(0, e.endMin - e.startMin) };
                list.Add(pe);
            }
            return list;
        }
    }
}
