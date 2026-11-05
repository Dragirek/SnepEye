using System;
using System.IO;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Ce que le suivi utilise en ce moment, ecrit par ar6dof_probe dans calib_info.json (la calibration elle-meme se fait dans ar6dof_app.exe).</summary>
    [Serializable]
    public class CalInfoJson
    {
        public string source = "", model = "", created = "", error = "", zoomSource = "none";
        public int calibW, calibH, runW, runH, views, fps, runFps;
        public float scale, baselineMm, rms, epi, squareMm, scaleCorr, hfov, zoom = 1f;
        public bool mismatch, zoomAuto = true, zoomConfirmed;
    }

    /// <summary>
    /// Lecture de calib_info.json, au plus 5 fois par seconde et seulement quand le fichier a change. Ne lit rien tant que Poll() n'est pas appele
    /// (le menu Image ne l'appelle que lorsqu'il est affiche).
    /// </summary>
    public class Ar6dofCalibLink
    {
        readonly Ar6dofDock dock;
        string dir = "";
        DateTime infoStamp = DateTime.MinValue;
        float nextPoll;
        public CalInfoJson Info;                 // null tant que le fichier n'existe pas

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

        static string ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) return sr.ReadToEnd();
        }

        public void Poll()
        {
            float now = Time.unscaledTime;
            if (now < nextPoll) return;
            nextPoll = now + 0.2f;
            string d = Dir();
            if (d.Length == 0) return;
            try
            {
                string p = Path.Combine(d, "calib_info.json");
                if (!File.Exists(p)) return;
                var t = File.GetLastWriteTimeUtc(p);
                if (t == infoStamp) return;
                var i = JsonUtility.FromJson<CalInfoJson>(ReadShared(p));
                if (i != null) { Info = i; infoStamp = t; }
            }
            catch (Exception) { /* fichier en cours d'ecriture : au prochain passage */ }
        }
    }
}
