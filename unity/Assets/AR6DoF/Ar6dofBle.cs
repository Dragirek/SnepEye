using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Client Bluetooth : dialogue en UDP local avec ar6dof_probe (qui parle au Bluetooth de Windows). Liste les appareils à portée,
    /// enregistre ceux des menus (connexion et reconnexion automatiques) et envoie des octets à leurs caractéristiques.
    /// Protocole : voir src/ble_hub_proto.h. Créé automatiquement par Ar6dofDock.
    /// </summary>
    public class Ar6dofBle : MonoBehaviour
    {
        public static Ar6dofBle Instance;
        public const int CmdPort = 9881, EvtPort = 9882;

        public class Dev { public string addr = "", name = ""; public int rssi; public float lastSeen; }

        public readonly Dictionary<string, Dev> devices = new Dictionary<string, Dev>();
        readonly Dictionary<string, string> states = new Dictionary<string, string>();
        readonly HashSet<string> wanted = new HashSet<string>();
        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        UdpClient rx, tx;
        Thread thread;
        volatile bool running;
        bool scan;
        float lastHeartbeat, lastEvent = -100f;

        /// <summary>Vrai tant que ar6dof_probe répond (sinon il n'est pas lancé, ou lancé avec --no-ble).</summary>
        public bool ProbeAlive { get { return Time.unscaledTime - lastEvent < 5f; } }

        public static string Normalize(string addr) { return string.IsNullOrEmpty(addr) ? "" : addr.Replace(":", "").Replace("-", "").ToUpperInvariant(); }

        void Awake() { Instance = this; }

        void OnEnable()
        {
            try
            {
                tx = Ar6dofSocket.NoInherit(new UdpClient());
                rx = Ar6dofSocket.NoInherit(new UdpClient(new IPEndPoint(IPAddress.Loopback, EvtPort)));
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "Ar6dofBle" };
                thread.Start();
            }
            catch (Exception e) { Debug.LogWarning("AR6DoF : port UDP " + EvtPort + " inutilisable (Bluetooth des appareils desactive) : " + e.Message); }
        }

        void OnDisable()
        {
            running = false;
            try { rx?.Close(); } catch { }
            try { tx?.Close(); } catch { }
            rx = null; tx = null;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Loop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                byte[] d;
                try { d = rx.Receive(ref any); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                foreach (var line in Encoding.UTF8.GetString(d).Split('\n'))
                    if (line.Length > 0) inbox.Enqueue(line.TrimEnd('\r'));
            }
        }

        // ---------------------------------------------------------------- API
        public void SetScan(bool on) { scan = on; if (on) Send("SCAN 1"); else Send("SCAN 0"); if (!on) devices.Clear(); }
        public bool Scanning { get { return scan; } }

        public void Register(string addr)
        {
            addr = Normalize(addr);
            if (addr.Length != 12) return;
            if (wanted.Add(addr)) Send("CONNECT " + addr);
        }

        public void Unregister(string addr)
        {
            addr = Normalize(addr);
            if (wanted.Remove(addr)) { Send("DISCONNECT " + addr); states.Remove(addr); }
        }

        /// <summary>"connected", "connecting", "disconnected", "unknown" (pas encore de nouvelles) ou "noprobe" (ar6dof_probe absent).</summary>
        public string State(string addr)
        {
            addr = Normalize(addr);
            if (addr.Length != 12) return "none";
            if (!ProbeAlive) return "noprobe";
            string s;
            return states.TryGetValue(addr, out s) ? s : "unknown";
        }

        public bool IsConnected(string addr) { return State(addr) == "connected"; }

        /// <summary>Envoie des octets à une caractéristique. 'latestWins' : un envoi plus récent du même lot remplace les anciens (curseurs).</summary>
        public void Write(string addr, string service, string characteristic, byte[] data, bool withResponse, bool latestWins)
        {
            addr = Normalize(addr);
            if (addr.Length != 12 || string.IsNullOrEmpty(service) || string.IsNullOrEmpty(characteristic)) return;
            var sb = new StringBuilder("WRITE ");
            sb.Append(addr).Append(' ').Append(service.Trim()).Append(' ').Append(characteristic.Trim()).Append(' ');
            foreach (byte b in data) sb.Append(b.ToString("X2"));
            if (data.Length == 0) sb.Append('0').Append('0');   // un octet nul plutôt qu'un champ vide (le champ serait perdu)
            sb.Append(' ').Append((withResponse ? "r" : "") + (latestWins ? "c" : "") + (!withResponse && !latestWins ? "-" : ""));
            Send(sb.ToString());
        }

        // ---------------------------------------------------------------- interne
        void Send(string line)
        {
            if (tx == null) return;
            try { byte[] b = Encoding.UTF8.GetBytes(line + "\n"); tx.Send(b, b.Length, "127.0.0.1", CmdPort); } catch (Exception) { }
        }

        void Update()
        {
            string l;
            while (inbox.TryDequeue(out l)) Handle(l);
            if (Time.unscaledTime - lastHeartbeat > 2f)
            {
                lastHeartbeat = Time.unscaledTime;
                Send("PING");
                foreach (var a in wanted) Send("CONNECT " + a);   // idempotent : si le programme a redémarré, il retrouve ses appareils
                if (scan) Send("SCAN 1");
            }
            if (scan)
            {   // oublie les appareils plus vus depuis 10 s
                List<string> old = null;
                foreach (var kv in devices) if (Time.unscaledTime - kv.Value.lastSeen > 10f) (old ?? (old = new List<string>())).Add(kv.Key);
                if (old != null) foreach (var k in old) devices.Remove(k);
            }
        }

        void Handle(string line)
        {
            lastEvent = Time.unscaledTime;
            string[] p = line.Split(new[] { ' ' }, 4);
            if (p.Length == 0) return;
            if (p[0] == "STATE" && p.Length >= 3) states[p[1]] = p[2];
            else if (p[0] == "DEV" && p.Length >= 3)
            {
                Dev d;
                if (!devices.TryGetValue(p[1], out d)) { d = new Dev { addr = p[1] }; devices[p[1]] = d; }
                int r; if (int.TryParse(p[2], out r)) d.rssi = r;
                if (p.Length >= 4 && p[3].Length > 0) d.name = p[3];
                d.lastSeen = Time.unscaledTime;
            }
        }
    }
}
