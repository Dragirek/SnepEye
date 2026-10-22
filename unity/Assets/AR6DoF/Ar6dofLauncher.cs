using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Lance ar6dof_probe.exe tout seul (sans fenêtre, avec les réponses de sa dernière utilisation) et l'arrête quand
    /// Unity se ferme. Les messages du programme apparaissent dans la Console Unity (préfixe [probe]) et dans
    /// ar6dof_probe.log (à côté de l'exe) : c'est là qu'il faut regarder si le lancement ne se passe pas comme prévu.
    /// Prérequis : avoir lancé ar6dof_probe.exe UNE fois à la main pour choisir caméra, IMU, calibration (g puis f).
    /// </summary>
    public class Ar6dofLauncher : MonoBehaviour
    {
        [Tooltip("Chemin d'ar6dof_probe.exe. Vide : <projet>/ar6dof_probe-win64/ar6dof_probe.exe")]
        public string exePath = "";
        public bool launchOnStart = true;
        [Tooltip("Aucune fenêtre OpenCV : nettement moins de CPU")]
        public bool hideProbeWindows = true;
        [Tooltip("Affiche aussi les lignes de statistiques (une par seconde) dans la Console")]
        public bool showStatsInConsole = false;
        public int controlPort = 9877;
        [Tooltip("Si un ar6dof_probe tourne déjà (resté ouvert après un plantage, lancé à la main) : l'arrêter et le relancer proprement, lié à cet Unity. " +
                 "Décoche seulement pour réutiliser un exe lancé à la main avec ses fenêtres.")]
        public bool restartRunningProbe = true;

        /// <summary>Dernière ligne de statistiques d'ar6dof_probe (cadence caméra, suivi, délai...).</summary>
        public string LastStats { get; private set; } = "";
        public bool IsRunning => proc != null && !proc.HasExited;

        Process proc;
        UdpClient udp;
        volatile bool stopping;

        string ResolveExe()
        {
            if (!string.IsNullOrEmpty(exePath)) return exePath;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ar6dof_probe-win64", "ar6dof_probe.exe"));
        }

        void Start() { if (launchOnStart) Launch(); }

        public void Launch()
        {
            var old = Process.GetProcessesByName("ar6dof_probe");
            if (old.Length > 0)
            {
                if (!restartRunningProbe)
                {
                    UnityEngine.Debug.Log("AR6DoF : ar6dof_probe tourne déjà, il est réutilisé (Restart Running Probe décoché).");
                    return;
                }
                // Un exe resté ouvert (Unity planté...) garde la caméra et peut bloquer les ports : on le remplace.
                UnityEngine.Debug.Log("AR6DoF : un ancien ar6dof_probe tournait (" + old.Length + "), il est arrêté puis relancé proprement.");
                Send('Q');
                foreach (var p in old) { try { if (!p.WaitForExit(2500)) { p.Kill(); p.WaitForExit(2000); } } catch { } }
            }
            string exe = ResolveExe();
            if (!File.Exists(exe))
            {
                UnityEngine.Debug.LogError("AR6DoF : ar6dof_probe.exe introuvable : " + exe + " (renseigne 'Exe Path').");
                return;
            }
            string dir = Path.GetDirectoryName(exe);
            string cfg = Path.Combine(dir, "ar6dof_config.txt");
            UnityEngine.Debug.Log("AR6DoF : lancement de " + exe + "\n  configuration : " + cfg + (File.Exists(cfg) ? " (trouvée)" : " (ABSENTE : lance l'exe une fois à la main)"));
            var psi = new ProcessStartInfo(exe, "--auto" + (hideProbeWindows ? " --no-windows" : "") + " --parent " + Process.GetCurrentProcess().Id)
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = hideProbeWindows,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                stopping = false;
                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    if (e.Data.StartsWith("camera ")) { LastStats = e.Data; if (!showStatsInConsole) return; }
                    UnityEngine.Debug.Log("[probe] " + e.Data);
                };
                proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogWarning("[probe] " + e.Data); };
                proc.Exited += (s, e) => { if (!stopping) UnityEngine.Debug.LogWarning("AR6DoF : ar6dof_probe s'est arrêté. Détails : " + Path.Combine(dir, "ar6dof_probe.log")); };
                proc.Start();
                proc.BeginOutputReadLine();   // la sortie DOIT être lue en continu, sinon le programme finirait par se bloquer
                proc.BeginErrorReadLine();
            }
            catch (System.Exception e) { UnityEngine.Debug.LogError("AR6DoF : lancement impossible : " + e.Message); }
        }

        /// <summary>Commande envoyée à ar6dof_probe : T = recentrer le tracking, V = verrouiller le sol, Z = yaw à zéro, Q = quitter.</summary>
        public void Send(char command)
        {
            try
            {
                if (udp == null) udp = Ar6dofSocket.NoInherit(new UdpClient());
                udp.Send(new[] { (byte)command }, 1, "127.0.0.1", controlPort);
            }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("AR6DoF : commande non envoyée : " + e.Message); }
        }

        /// <summary>Arrête ar6dof_probe : d'abord poliment (Q : libère caméras et Bluetooth), puis de force si besoin.</summary>
        public void Stop()
        {
            stopping = true;
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    Send('Q');
                    if (!proc.WaitForExit(1500)) proc.Kill();
                }
            }
            catch { }
            proc = null;
        }

        void OnApplicationQuit() { Stop(); try { udp?.Close(); } catch { } }
        void OnDestroy() { Stop(); }
    }
}
