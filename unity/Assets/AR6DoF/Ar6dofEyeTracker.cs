using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Un instant de regard, angles en degrés par rapport à la tête (yaw &gt; 0 = vers la droite, pitch &gt; 0 = vers le haut).</summary>
    public struct Ar6dofGaze
    {
        public bool valid, validL, validR;       // valid : au moins un oeil mesuré et étalonné à cet instant
        public bool blinkL, blinkR;              // paupière fermée
        public bool calibrated, calibrating;
        public float yaw, pitch;                 // regard combiné des deux yeux
        public float yawL, pitchL, yawR, pitchR;
        public float openL, openR;               // 0 = fermé ... 1 = grand ouvert
        public ushort seq;
        public double time;                      // Ar6dofClock.Now() à la réception
    }

    /// <summary>Ce que ar6dof_probe dit de l'eye tracking (message STATUS, une fois par seconde).</summary>
    public struct Ar6dofEyeStatus
    {
        public bool on;
        public int camL, camR;                   // -1 pas de caméra configurée, 0 fermée, 1 ouverte
        public float fpsL, fpsR;
        public bool calL, calR, seeL, seeR;      // étalonné, pupille vue
        public double time;                      // dernière réception (Ar6dofClock.Now())
    }

    /// <summary>
    /// Eye tracking : reçoit le regard calculé par ar6dof_probe (UDP 9883), lui envoie les commandes (UDP 9884) et lit ses messages (UDP 9885).
    /// DÉSACTIVÉ par défaut : tant qu'on ne l'active pas (onglet "Yeux" du panneau F1 ou SetEnabled(true)), aucun port n'est ouvert,
    /// aucun thread ne tourne et les caméras d'oeil restent arrêtées.
    /// Pour t'en servir (animations de fursuit...) : lis Ar6dofEyeTracker.Instance.Gaze (yaw/pitch, clignements, ouverture des paupières).
    /// </summary>
    public class Ar6dofEyeTracker : MonoBehaviour
    {
        public static Ar6dofEyeTracker Instance;
        public int gazePort = 9883, commandPort = 9884, eventPort = 9885;

        const string PrefKey = "ar6dof_eye_enabled";
        static readonly float[] ParkAt = { 0.5f, 6f, 20f };
        readonly object lockObj = new object();
        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        UdpClient gazeUdp, eventUdp, sender;
        Thread gazeThread, eventThread;
        volatile bool running;
        Ar6dofGaze gaze;
        Ar6dofEyeStatus status;
        bool haveGaze;
        double lastGazeTime;
        float startTime, lastPoke = -10f;
        int parkSent;

        public bool Enabled { get; private set; }

        /// <summary>Message d'ar6dof_probe pendant l'étalonnage (POINT_DONE, CAL_RESULT...), livré dans le thread Unity.</summary>
        public event Action<string> EngineMessage;

        public Ar6dofGaze Gaze { get { lock (lockObj) return gaze; } }
        public Ar6dofEyeStatus Status { get { lock (lockObj) return status; } }
        /// <summary>Des paquets de regard arrivent (ar6dof_probe tourne avec l'eye tracking et les caméras sont ouvertes).</summary>
        public bool Receiving { get { lock (lockObj) return haveGaze && Ar6dofClock.Now() - lastGazeTime < 0.5; } }
        public bool EngineOnline { get { lock (lockObj) return status.time > 0 && Ar6dofClock.Now() - status.time < 3.0; } }

        /// <summary>Direction du regard dans le repère de la tête (Z = devant, X = droite, Y = haut).</summary>
        public Vector3 GazeDirectionLocal
        {
            get { var g = Gaze; return Quaternion.Euler(-g.pitch, g.yaw, 0f) * Vector3.forward; }
        }

        void Awake() { Instance = this; }

        void Start()
        {
            startTime = Time.unscaledTime;
            bool want = false;
            try { want = PlayerPrefs.GetInt(PrefKey, 0) != 0; } catch (Exception) { }
            if (want) SetEnabled(true, false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CloseSockets();
            try { sender?.Close(); } catch (Exception) { }
        }

        void OnApplicationQuit()
        {
            if (Enabled) Send("EYE 0");   // ne laisse pas les caméras tourner après Unity
        }

        /// <summary>Active / désactive tout l'eye tracking : ports, threads et caméras d'oeil d'ar6dof_probe.</summary>
        public void SetEnabled(bool on, bool save = true)
        {
            if (on == Enabled && (on == running)) return;
            Enabled = on;
            if (save) { try { PlayerPrefs.SetInt(PrefKey, on ? 1 : 0); PlayerPrefs.Save(); } catch (Exception) { } }
            if (on)
            {
                OpenSockets();
                Send("EYE 1");
                lastPoke = Time.unscaledTime;
            }
            else
            {
                Send("EYE 0");
                CloseSockets();
                lock (lockObj) { haveGaze = false; gaze = new Ar6dofGaze(); status = new Ar6dofEyeStatus(); }
                parkSent = 0;
                startTime = Time.unscaledTime;
            }
        }

        // ---------------------------------------------------------------- commandes
        public void Send(string line)
        {
            try
            {
                if (sender == null) sender = new UdpClient();
                byte[] d = Encoding.ASCII.GetBytes(line + "\n");
                sender.Send(d, d.Length, "127.0.0.1", commandPort);
            }
            catch (Exception) { }
        }

        public void BeginCalibration() { Send("CAL_BEGIN"); }
        public void CancelCalibration() { Send("CAL_CANCEL"); }
        public void ClearCalibration() { Send("CAL_CLEAR"); }
        /// <summary>Le regard actuel devient (0, 0) : à utiliser en regardant droit devant soi.</summary>
        public void Recenter() { Send("RECENTER"); }

        // ---------------------------------------------------------------- réception
        void OpenSockets()
        {
            if (running) return;
            try
            {
                gazeUdp = new UdpClient(gazePort);
                eventUdp = new UdpClient(eventPort);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("AR6DoF : eye tracking, ports UDP " + gazePort + "/" + eventPort + " inutilisables : " + e.Message);
                CloseSockets();
                return;
            }
            running = true;
            gazeThread = new Thread(GazeLoop) { IsBackground = true, Name = "Ar6dofEyeGaze" };
            eventThread = new Thread(EventLoop) { IsBackground = true, Name = "Ar6dofEyeEvents" };
            gazeThread.Start();
            eventThread.Start();
        }

        void CloseSockets()
        {
            running = false;
            try { gazeUdp?.Close(); } catch (Exception) { }
            try { eventUdp?.Close(); } catch (Exception) { }
            gazeUdp = null;
            eventUdp = null;
        }

        void GazeLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            var udp = gazeUdp;
            while (running)
            {
                byte[] d;
                try { d = udp.Receive(ref any); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                if (d.Length != 44 || BitConverter.ToUInt32(d, 0) != 0x31455945u || d[4] != 1) continue;
                byte f = d[5];
                var g = new Ar6dofGaze
                {
                    validL = (f & 1) != 0,
                    validR = (f & 2) != 0,
                    blinkL = (f & 4) != 0,
                    blinkR = (f & 8) != 0,
                    calibrated = (f & 16) != 0,
                    calibrating = (f & 32) != 0,
                    seq = BitConverter.ToUInt16(d, 6),
                    yawL = BitConverter.ToSingle(d, 8),
                    pitchL = BitConverter.ToSingle(d, 12),
                    yawR = BitConverter.ToSingle(d, 16),
                    pitchR = BitConverter.ToSingle(d, 20),
                    yaw = BitConverter.ToSingle(d, 24),
                    pitch = BitConverter.ToSingle(d, 28),
                    openL = BitConverter.ToSingle(d, 32),
                    openR = BitConverter.ToSingle(d, 36),
                    time = Ar6dofClock.Now()
                };
                g.valid = g.validL || g.validR;
                lock (lockObj) { gaze = g; haveGaze = true; lastGazeTime = g.time; }
            }
        }

        void EventLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            var udp = eventUdp;
            while (running)
            {
                byte[] d;
                try { d = udp.Receive(ref any); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                string s = Encoding.ASCII.GetString(d).Trim();
                if (s.StartsWith("STATUS ", StringComparison.Ordinal)) ParseStatus(s);
                else inbox.Enqueue(s);
            }
        }

        static float F(string v) { float r; return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : 0f; }

        void ParseStatus(string s)
        {
            var st = new Ar6dofEyeStatus();
            foreach (var part in s.Split(' '))
            {
                int e = part.IndexOf('=');
                if (e <= 0) continue;
                string k = part.Substring(0, e), v = part.Substring(e + 1);
                switch (k)
                {
                    case "on": st.on = v == "1"; break;
                    case "camL": st.camL = (int)F(v); break;
                    case "camR": st.camR = (int)F(v); break;
                    case "fpsL": st.fpsL = F(v); break;
                    case "fpsR": st.fpsR = F(v); break;
                    case "calL": st.calL = v == "1"; break;
                    case "calR": st.calR = v == "1"; break;
                    case "seeL": st.seeL = v == "1"; break;
                    case "seeR": st.seeR = v == "1"; break;
                }
            }
            st.time = Ar6dofClock.Now();
            lock (lockObj) status = st;
        }

        void Update()
        {
            string m;
            while (inbox.TryDequeue(out m))
            {
                var h = EngineMessage;
                if (h != null) h(m);
            }
            if (Enabled)
            {
                // ar6dof_probe peut démarrer après Unity, ou avoir ses caméras arrêtées : on le relance doucement
                var st = Status;
                bool silent = !EngineOnline;
                bool off = EngineOnline && !st.on;
                if ((silent || off) && Time.unscaledTime - lastPoke > 3f) { Send("EYE 1"); lastPoke = Time.unscaledTime; }
            }
            else if (parkSent < 3)
            {
                // Désactivé : si ar6dof_probe a été lancé avec l'eye tracking, on arrête ses caméras (quelques essais, il peut démarrer après nous).
                if (Time.unscaledTime - startTime > ParkAt[parkSent]) { Send("EYE 0"); parkSent++; }
            }
        }
    }
}
