using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Lance ar6dof_probe.exe tout seul (sans fenêtre, avec les réponses de sa dernière utilisation) et l'arrête quand
    /// Unity se ferme. Plus besoin de le lancer à la main ni dans un ordre précis.
    /// Prérequis : avoir lancé ar6dof_probe.exe UNE fois à la main pour choisir caméra, IMU, calibration (g puis f).
    /// </summary>
    public class Ar6dofLauncher : MonoBehaviour
    {
        [Tooltip("Chemin d'ar6dof_probe.exe. Vide : <projet>/ar6dof_probe-win64/ar6dof_probe.exe")]
        public string exePath = "";
        public bool launchOnStart = true;
        [Tooltip("Aucune fenêtre OpenCV : nettement moins de CPU")]
        public bool hideProbeWindows = true;
        public int controlPort = 9877;

        Process proc;
        UdpClient udp;

        string ResolveExe()
        {
            if (!string.IsNullOrEmpty(exePath)) return exePath;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ar6dof_probe-win64", "ar6dof_probe.exe"));
        }

        void Start() { if (launchOnStart) Launch(); }

        public void Launch()
        {
            if (Process.GetProcessesByName("ar6dof_probe").Length > 0)
            {
                UnityEngine.Debug.Log("AR6DoF : ar6dof_probe tourne déjà, il est réutilisé.");
                return;
            }
            string exe = ResolveExe();
            if (!File.Exists(exe))
            {
                UnityEngine.Debug.LogError("AR6DoF : ar6dof_probe.exe introuvable : " + exe + " (renseigne 'Exe Path').");
                return;
            }
            var psi = new ProcessStartInfo(exe, "--auto" + (hideProbeWindows ? " --no-windows" : ""))
            {
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = hideProbeWindows
            };
            try { proc = Process.Start(psi); }
            catch (System.Exception e) { UnityEngine.Debug.LogError("AR6DoF : lancement impossible : " + e.Message); }
        }

        /// <summary>Commande envoyée à ar6dof_probe : T = recentrer le tracking, V = verrouiller le sol, Z = yaw à zéro, Q = quitter.</summary>
        public void Send(char command)
        {
            try
            {
                if (udp == null) udp = new UdpClient();
                udp.Send(new[] { (byte)command }, 1, "127.0.0.1", controlPort);
            }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("AR6DoF : commande non envoyée : " + e.Message); }
        }

        public void Stop()
        {
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
            proc = null;
        }

        void OnApplicationQuit() { Stop(); udp?.Close(); }
    }
}
