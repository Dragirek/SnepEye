using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Télécommande gyroscopique Bluetooth (ESP32-C3 + BNO085 + 2 boutons, firmware firmware/ar6dof_remote) : ar6dof_probe reçoit
    /// le Bluetooth et relaie son orientation et ses boutons en UDP local (127.0.0.1) ; ce composant en déduit la direction visée.
    /// Paquet de 28 octets : magic 0x52365241 | version u8 | boutons u8 (bit0 gauche, bit1 droit) | seq u16 | quaternion w,x,y,z (4 float) | micros u32.
    /// </summary>
    public class Ar6dofRemote : MonoBehaviour
    {
        public int port = 9878;
        [Tooltip("Axe du capteur qui pointe vers l'avant de la télécommande. Réglé par la calibration en 2 étapes (panneau F1).")]
        public Vector3 pointAxisSensor = new Vector3(1f, 0f, 0f);

        const string PrefKey = "ar6dof_remote_axis";
        readonly object lockObj = new object();
        UdpClient udp;
        Thread thread;
        volatile bool running;
        double[] q = { 1, 0, 0, 0 };
        bool left, right, haveData;
        double recvTime, lastAck;
        int packets, packetsPerSec, packetsWindow;
        double windowStart;
        int calUpIdx = -1;
        string calMsg = "";

        public bool Left { get { lock (lockObj) return left; } }
        public bool Right { get { lock (lockObj) return right; } }
        public bool Connected { get { lock (lockObj) return haveData && Ar6dofClock.Now() - recvTime < 0.5; } }

        void Awake()
        {
            string s = PlayerPrefs.GetString(PrefKey, "");
            string[] p = s.Split(',');
            if (p.Length == 3 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                pointAxisSensor = new Vector3(x, y, z);
        }

        void OnEnable()
        {
            try
            {
                udp = new UdpClient(port);
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "Ar6dofRemote" };
                thread.Start();
            }
            catch (Exception e) { UnityEngine.Debug.LogError("AR6DoF : télécommande, port UDP " + port + " inutilisable : " + e.Message + "\nPort deja pris par : (1) un autre Unity encore ouvert ou plante mais reste dans le Gestionnaire des taches (termine Unity.exe / Unity Bug Reporter), (2) un 2e composant identique dans la scene (cherche-le dans la Hierarchy : un seul par scene), (3) un autre programme."); }
        }

        void OnDisable()
        {
            running = false;
            try { udp?.Close(); } catch { }
            udp = null;
        }

        void Loop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            byte[] ack = { (byte)'A', (byte)'6', (byte)'A', (byte)'C', (byte)'K' };
            while (running)
            {
                byte[] d;
                try { d = udp.Receive(ref any); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                if (d.Length != 28 || BitConverter.ToUInt32(d, 0) != 0x52365241u || d[4] != 1) continue;
                double now = Ar6dofClock.Now();
                lock (lockObj)
                {
                    left = (d[5] & 1) != 0;
                    right = (d[5] & 2) != 0;
                    q = new double[] { BitConverter.ToSingle(d, 8), BitConverter.ToSingle(d, 12), BitConverter.ToSingle(d, 16), BitConverter.ToSingle(d, 20) };
                    q = Ar6dofMath.Normalize(q);
                    haveData = true;
                    recvTime = now;
                    packets++;
                    packetsWindow++;
                    if (now - windowStart >= 1.0) { packetsPerSec = packetsWindow; packetsWindow = 0; windowStart = now; }
                }
                if (now - lastAck > 1.0)   // la télécommande nous trouve ainsi sans qu'on lui donne d'adresse IP
                {
                    try { udp.Send(ack, ack.Length, any); } catch { }
                    lastAck = now;
                }
            }
        }

        /// <summary>Rotation capteur -> monde (matrice 3x3 en ligne, Z vers le haut).</summary>
        public bool TryGetRotation(out double[] R9)
        {
            lock (lockObj)
            {
                R9 = haveData ? Ar6dofMath.QuatToMat(q) : null;
                return haveData;
            }
        }

        /// <summary>Direction visée (repère Unity), sans correction de cap : le cap du capteur est arbitraire, voir Ar6dofPointer.</summary>
        public bool TryGetPointDirection(out Vector3 dirUnity)
        {
            dirUnity = Vector3.forward;
            if (!TryGetRotation(out double[] R) || !Connected) return false;
            double px = pointAxisSensor.x, py = pointAxisSensor.y, pz = pointAxisSensor.z;
            var g = new Vector3((float)(R[0] * px + R[1] * py + R[2] * pz), (float)(R[3] * px + R[4] * py + R[5] * pz), (float)(R[6] * px + R[7] * py + R[8] * pz));
            dirUnity = Ar6dofConvert.Position(g).normalized;
            return true;
        }

        // ---- Calibration en 2 gestes (comme pour la sonde du casque) ----
        // 1) télécommande à plat, immobile  -> axe "haut" du capteur
        // 2) pointe relevée d'environ 30 deg, immobile -> axe qui pointe vers l'avant
        public string CalibrateStep1()
        {
            if (!TryGetRotation(out double[] R)) return "pas de donnees de la telecommande";
            double a = Math.Abs(R[6]), b = Math.Abs(R[7]), c = Math.Abs(R[8]);
            int i = (a >= b && a >= c) ? 0 : (b >= c ? 1 : 2);
            if (Math.Abs(R[6 + i]) < 0.9) return "etape 1 : pose la telecommande bien a plat";
            calUpIdx = i;
            return "etape 1 OK. Releve maintenant la POINTE de ~30 deg, immobile, puis etape 2";
        }

        public string CalibrateStep2()
        {
            if (calUpIdx < 0) return "fais d'abord l'etape 1";
            if (!TryGetRotation(out double[] R)) return "pas de donnees de la telecommande";
            int best = -1;
            double bestV = 0;
            for (int j = 0; j < 3; j++)
            {
                if (j == calUpIdx) continue;
                if (Math.Abs(R[6 + j]) > Math.Abs(bestV)) { bestV = R[6 + j]; best = j; }
            }
            if (best < 0 || Math.Abs(bestV) < 0.25) return "etape 2 : releve davantage la pointe (au moins 15 deg)";
            var axis = Vector3.zero;
            axis[best] = bestV > 0 ? 1f : -1f;   // l'axe qui pointe vers le haut quand la pointe est relevée
            pointAxisSensor = axis;
            PlayerPrefs.SetString(PrefKey, axis.x.ToString(CultureInfo.InvariantCulture) + "," + axis.y.ToString(CultureInfo.InvariantCulture) + "," + axis.z.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
            calUpIdx = -1;
            return "calibration OK : axe de visee " + axis;
        }

        void OnGUI()
        {
            if (!Ar6dofUi.Visible) return;
            GUILayout.BeginArea(new Rect(840, 60, 380, 260), GUI.skin.box);
            GUILayout.Label("-- Telecommande");
            GUILayout.Label(Connected ? "connectee : " + packetsPerSec + " paquets/s" : "non connectee (UDP " + port + ")");
            GUILayout.Label("boutons : gauche " + (Left ? "ON" : "-") + " | droit " + (Right ? "ON" : "-"));
            if (GUILayout.Button("Calibration 1/2 : a plat")) calMsg = CalibrateStep1();
            if (GUILayout.Button("Calibration 2/2 : pointe relevee 30 deg")) calMsg = CalibrateStep2();
            GUILayout.Label(calMsg);
            GUILayout.Label("axe de visee (capteur) : " + pointAxisSensor);
            GUILayout.Label("Recentrer : maintenir les 2 boutons 0,6 s");
            GUILayout.EndArea();
        }
    }
}
