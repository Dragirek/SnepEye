using System;
using System.IO;
using UnityEngine;

namespace AR6DoF
{
    [Serializable] public class CalVerifyJson { public int seq; public float estMm, ratio; }

    [Serializable]
    public class CalResultJson
    {
        public bool ok;
        public float rmsL, rmsR, rmsS, epi, epiMax, baselineMm, hfov, coverage, roundTrip, recon, heldOut, scaleCorr, baselineHint;
        public int used, total, score;
        public string model = "";
        public string[] warnings = new string[0];
    }

    /// <summary>Etat de la calibration, ecrit par ar6dof_probe dans calib_status.json (voir calib_session.h, StatusJson).</summary>
    [Serializable]
    public class CalStatusJson
    {
        public string state = "idle", msg = "idle", stage = "", error = "";
        public int goal = -1, goalsDone, goalCount = 16, views, target = 18, acceptSeq, imgW, imgH, cols, rows;
        public float progress, stillness, squareMm, cprogress;
        public bool foundL, foundR, swapEyes, applied;
        public float[] corners = new float[0];
        public CalVerifyJson verify = new CalVerifyJson();
        public CalResultJson result = new CalResultJson();
    }

    /// <summary>Ce que le suivi utilise en ce moment, ecrit par ar6dof_probe dans calib_info.json.</summary>
    [Serializable]
    public class CalInfoJson
    {
        public string source = "", model = "", created = "", error = "";
        public int calibW, calibH, runW, runH, views;
        public float scale, baselineMm, rms, epi, fps, squareMm, scaleCorr, hfov;
        public bool mismatch;
    }

    /// <summary>
    /// Liaison avec ar6dof_probe pour la calibration : commandes texte (UDP, via Ar6dofLauncher.SendLine) et lecture des deux fichiers d'etat.
    /// Rien ne tourne tant que Poll() n'est pas appele (la fenetre de calibration le fait seulement quand elle est ouverte, au plus 5 fois par seconde).
    /// </summary>
    public class Ar6dofCalibLink
    {
        readonly Ar6dofDock dock;
        string dir = "";
        DateTime statusStamp = DateTime.MinValue, infoStamp = DateTime.MinValue;
        float nextPoll;
        public CalStatusJson Status = new CalStatusJson();
        public CalInfoJson Info;                 // null tant que le fichier n'existe pas
        public int StatusSeq;                    // change a chaque nouvel etat lu
        public int InfoSeq;

        public Ar6dofCalibLink(Ar6dofDock d) { dock = d; }

        string Dir()
        {
            if (dir.Length > 0) return dir;
            try
            {
                if (dock.launcher != null) dir = dock.launcher.ExeDirectory() ?? "";
                if (dir.Length == 0) dir = Ar6dofProbePaths.Dir("");
            }
            catch (Exception) { dir = ""; }
            return dir;
        }

        public bool ProbeRunning { get { return dock.launcher != null && dock.launcher.IsRunning; } }

        public void Send(string line)
        {
            if (dock.launcher != null) dock.launcher.SendLine(line);
        }

        public void SendKey(char c)
        {
            if (dock.launcher != null) dock.launcher.Send(c);
        }

        static string ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) return sr.ReadToEnd();
        }

        /// <summary>Relit les fichiers s'ils ont change (au plus 5 fois par seconde). Renvoie vrai si l'etat de la calibration est nouveau.</summary>
        public bool Poll(bool force = false)
        {
            float now = Time.unscaledTime;
            if (!force && now < nextPoll) return false;
            nextPoll = now + 0.2f;
            string d = Dir();
            if (d.Length == 0) return false;
            bool changed = false;
            try
            {
                string p = Path.Combine(d, "calib_status.json");
                if (File.Exists(p))
                {
                    var t = File.GetLastWriteTimeUtc(p);
                    if (force || t != statusStamp)
                    {
                        string txt = ReadShared(p);
                        var s = JsonUtility.FromJson<CalStatusJson>(txt);
                        if (s != null)
                        {
                            if (s.corners == null) s.corners = new float[0];
                            if (s.verify == null) s.verify = new CalVerifyJson();
                            if (s.result == null) s.result = new CalResultJson();
                            if (s.result.warnings == null) s.result.warnings = new string[0];
                            Status = s;
                            statusStamp = t;
                            StatusSeq++;
                            changed = true;
                        }
                    }
                }
            }
            catch (Exception) { /* fichier en cours d'ecriture : au prochain passage */ }
            try
            {
                string p = Path.Combine(d, "calib_info.json");
                if (File.Exists(p))
                {
                    var t = File.GetLastWriteTimeUtc(p);
                    if (force || t != infoStamp)
                    {
                        var i = JsonUtility.FromJson<CalInfoJson>(ReadShared(p));
                        if (i != null) { Info = i; infoStamp = t; InfoSeq++; }
                    }
                }
            }
            catch (Exception) { }
            return changed;
        }
    }
}
