using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Une mesure de la bouche. Toutes les valeurs sont normalisées par l'étalonnage (étapes « neutre » et « grande ouverture » au minimum).</summary>
    public struct Ar6dofMouth
    {
        public bool valid, calibrated, calibrating;
        public float jawOpen;        // 0 = fermée ... 1 = grande ouverte
        public float wide;           // -1 = serrée (bisou) ... 0 = neutre ... +1 = large (sourire)
        public float side;           // -1 = mâchoire vers la gauche ... +1 = vers la droite
        public float vert;           // déplacement vertical de la bouche (-1 ... 1)
        public float rawArea, rawWidth;   // mesures brutes (part de l'image)
        public ushort seq;
        public double time;          // Ar6dofClock.Now() à la réception
    }

    /// <summary>Ce que ar6dof_probe dit du mouth tracking (message STATUS, une fois par seconde).</summary>
    public struct Ar6dofMouthStatus
    {
        public bool on;
        public int cam;              // 0 fermée, 1 ouverte
        public float fps;
        public bool see;             // cavité de la bouche vue
        public bool hasNeutral, hasOpen, hasWide, hasNarrow, calibrated;
        public double time;          // dernière réception (Ar6dofClock.Now())
    }

    /// <summary>
    /// Mouth tracking : reçoit la bouche mesurée par ar6dof_probe (UDP 9886), lui envoie les commandes (UDP 9887) et lit ses messages (UDP 9888).
    /// Indépendant de l'eye tracking (l'un sans l'autre). DÉSACTIVÉ par défaut : tant qu'on ne l'active pas (menu Appareils &gt; Bouche ou SetEnabled(true)),
    /// aucun port n'est ouvert, aucun thread ne tourne et la caméra de bouche reste arrêtée.
    /// Pour t'en servir (animations de fursuit...) : lis Ar6dofMouthTracker.Instance.Mouth (jawOpen, wide, side).
    /// </summary>
    public class Ar6dofMouthTracker : MonoBehaviour
    {
        public static Ar6dofMouthTracker Instance;
        public int dataPort = 9886, commandPort = 9887, eventPort = 9888;

        const string PrefKey = "ar6dof_mouth_enabled";
        static readonly float[] ParkAt = { 0.5f, 6f, 20f };
        readonly object lockObj = new object();
        UdpClient dataUdp, eventUdp, sender;
        Thread dataThread, eventThread;
        volatile bool running;
        Ar6dofMouth mouth;
        Ar6dofMouthStatus status;
        bool haveData;
        double lastDataTime;
        float startTime, lastPoke = -10f;
        int parkSent;

        public bool Enabled { get; private set; }

        public Ar6dofMouth Mouth { get { lock (lockObj) return mouth; } }
        public Ar6dofMouthStatus Status { get { lock (lockObj) return status; } }
        /// <summary>Des mesures arrivent (ar6dof_probe tourne avec le mouth tracking et la caméra est ouverte).</summary>
        public bool Receiving { get { lock (lockObj) return haveData && Ar6dofClock.Now() - lastDataTime < 0.5; } }
        public bool EngineOnline { get { lock (lockObj) return status.time > 0 && Ar6dofClock.Now() - status.time < 3.0; } }

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
            if (Enabled) Send("MOUTH 0");   // ne laisse pas la caméra tourner après Unity
        }

        /// <summary>Active / désactive tout le mouth tracking : ports, threads et caméra de bouche d'ar6dof_probe.</summary>
        public void SetEnabled(bool on, bool save = true)
        {
            if (on == Enabled && (on == running)) return;
            Enabled = on;
            if (save) { try { PlayerPrefs.SetInt(PrefKey, on ? 1 : 0); PlayerPrefs.Save(); } catch (Exception) { } }
            if (on)
            {
                OpenSockets();
                Send("MOUTH 1");
                lastPoke = Time.unscaledTime;
            }
            else
            {
                Send("MOUTH 0");
                CloseSockets();
                lock (lockObj) { haveData = false; mouth = new Ar6dofMouth(); status = new Ar6dofMouthStatus(); }
                parkSent = 0;
                startTime = Time.unscaledTime;
            }
        }

        // ---------------------------------------------------------------- commandes
        public void Send(string line)
        {
            try
            {
                if (sender == null) sender = Ar6dofSocket.NoInherit(new UdpClient());
                byte[] d = Encoding.ASCII.GetBytes(line + "\n");
                sender.Send(d, d.Length, "127.0.0.1", commandPort);
            }
            catch (Exception) { }
        }

        /// <summary>Étape d'étalonnage : "neutral" (bouche détendue), "open" (grande ouverture), "wide" (sourire large), "narrow" (bouche serrée).
        /// La mesure se fait 1 s après la commande et dure 1,5 s : prends l'expression tout de suite.</summary>
        public void Calibrate(string phase) { SetEnabled(true); Send("MCAL " + phase); }
        public void ClearCalibration() { Send("MCAL_CLEAR"); }

        // ---------------------------------------------------------------- réception
        void OpenSockets()
        {
            if (running) return;
            try
            {
                dataUdp = Ar6dofSocket.NoInherit(new UdpClient(dataPort));
                eventUdp = Ar6dofSocket.NoInherit(new UdpClient(eventPort));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("AR6DoF : mouth tracking, ports UDP " + dataPort + "/" + eventPort + " inutilisables : " + e.Message);
                CloseSockets();
                return;
            }
            running = true;
            dataThread = new Thread(DataLoop) { IsBackground = true, Name = "Ar6dofMouthData" };
            eventThread = new Thread(EventLoop) { IsBackground = true, Name = "Ar6dofMouthEvents" };
            dataThread.Start();
            eventThread.Start();
        }

        void CloseSockets()
        {
            running = false;
            try { dataUdp?.Close(); } catch (Exception) { }
            try { eventUdp?.Close(); } catch (Exception) { }
            dataUdp = null;
            eventUdp = null;
        }

        void DataLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            var udp = dataUdp;
            while (running)
            {
                byte[] d;
                try { d = udp.Receive(ref any); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                if (d.Length != 36 || BitConverter.ToUInt32(d, 0) != 0x3148544Du || d[4] != 1) continue;
                byte f = d[5];
                var m = new Ar6dofMouth
                {
                    valid = (f & 1) != 0,
                    calibrated = (f & 2) != 0,
                    calibrating = (f & 4) != 0,
                    seq = BitConverter.ToUInt16(d, 6),
                    jawOpen = BitConverter.ToSingle(d, 8),
                    wide = BitConverter.ToSingle(d, 12),
                    side = BitConverter.ToSingle(d, 16),
                    vert = BitConverter.ToSingle(d, 20),
                    rawArea = BitConverter.ToSingle(d, 24),
                    rawWidth = BitConverter.ToSingle(d, 28),
                    time = Ar6dofClock.Now()
                };
                lock (lockObj) { mouth = m; haveData = true; lastDataTime = m.time; }
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
            }
        }

        static float F(string v) { float r; return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : 0f; }

        void ParseStatus(string s)
        {
            var st = new Ar6dofMouthStatus();
            foreach (var part in s.Split(' '))
            {
                int e = part.IndexOf('=');
                if (e <= 0) continue;
                string k = part.Substring(0, e), v = part.Substring(e + 1);
                switch (k)
                {
                    case "on": st.on = v == "1"; break;
                    case "cam": st.cam = (int)F(v); break;
                    case "fps": st.fps = F(v); break;
                    case "see": st.see = v == "1"; break;
                    case "neutral": st.hasNeutral = v == "1"; break;
                    case "open": st.hasOpen = v == "1"; break;
                    case "wide": st.hasWide = v == "1"; break;
                    case "narrow": st.hasNarrow = v == "1"; break;
                    case "cal": st.calibrated = v == "1"; break;
                }
            }
            st.time = Ar6dofClock.Now();
            lock (lockObj) status = st;
        }

        void Update()
        {
            if (Enabled)
            {
                // ar6dof_probe peut démarrer après Unity, ou avoir sa caméra arrêtée : on le relance doucement
                var st = Status;
                bool silent = !EngineOnline;
                bool off = EngineOnline && !st.on;
                if ((silent || off) && Time.unscaledTime - lastPoke > 3f) { Send("MOUTH 1"); lastPoke = Time.unscaledTime; }
            }
            else if (parkSent < 3)
            {
                // Désactivé : si ar6dof_probe a été lancé avec le mouth tracking, on arrête sa caméra (quelques essais, il peut démarrer après nous).
                if (Time.unscaledTime - startTime > ParkAt[parkSent]) { Send("MOUTH 0"); parkSent++; }
            }
        }
    }
}
