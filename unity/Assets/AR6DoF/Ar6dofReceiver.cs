using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Pose reçue de ar6dof_probe.exe. Repère "G" : X avant, Y gauche, Z haut (main droite), mètres.</summary>
    public struct Ar6dofPoseData
    {
        public bool valid;
        public ushort flags;
        public uint seq;
        public Vector3 posG;
        public float qw, qx, qy, qz;
        public Vector3 velG, angG;   // vitesses linéaire (m/s) et angulaire (rad/s, repère monde G)
        public float floorZ;
        public double recvTime;      // secondes (QueryPerformanceCounter) à la réception
        public double tPose;         // instant auquel la pose est valable (QueryPerformanceCounter, envoyé par le tracker)
        public float camDelay;       // délai (s) entre l'arrivée d'une image et sa prise de vue, mesuré par comparaison caméra/gyro

        public bool Tracking => (flags & 1) != 0;
        public bool FloorValid => (flags & 2) != 0;
        public bool Lost => (flags & 4) != 0;
        public bool FloorLocked => (flags & 8) != 0;
    }

    /// <summary>Conversion du repère G (droit) vers le repère Unity (gauche : X droite, Y haut, Z avant).</summary>
    public static class Ar6dofConvert
    {
        // v_U = (-y, z, x)
        public static Vector3 Position(Vector3 g) => new Vector3(-g.y, g.z, g.x);
        public static Vector3 Velocity(Vector3 g) => Position(g);
        // Vecteur axial (vitesse angulaire) : le changement de main inverse le sens -> -(M g)
        public static Vector3 AngularVelocity(Vector3 g) => new Vector3(g.y, -g.z, -g.x);
        // Quaternion (w,x,y,z)_G -> Unity (x,y,z,w). Vérifié numériquement contre M*R*M^T.
        public static Quaternion Rotation(float w, float x, float y, float z) => new Quaternion(y, -z, -x, w);
    }

    /// <summary>Horloge système haute résolution, la même que celle d'ar6dof_probe (secondes).</summary>
    public static class Ar6dofClock
    {
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long v);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long v);
        static long freq;
        public static double Now()
        {
            if (freq == 0) QueryPerformanceFrequency(out freq);
            QueryPerformanceCounter(out long c);
            return (double)c / freq;
        }
    }

    /// <summary>Écoute le port UDP et garde la dernière pose reçue.</summary>
    public class Ar6dofReceiver : MonoBehaviour
    {
        public int port = 9876;

        public static double Now => Ar6dofClock.Now();

        struct RotSample { public double t, w, x, y, z; }
        readonly RotSample[] ring = new RotSample[512];   // historique des orientations (pour retrouver celle d'une image de la caméra)
        int ringCount, ringHead;

        readonly object lockObj = new object();
        Ar6dofPoseData latest;
        UdpClient client;
        Thread thread;
        volatile bool running;
        int packetCount;
        public int PacketCount => Volatile.Read(ref packetCount);

        void Awake()
        {
            Application.runInBackground = true;  // continue de recevoir quand la fenêtre n'a pas le focus
        }

        void OnEnable()
        {
            try
            {
                client = Ar6dofSocket.NoInherit(new UdpClient(port));
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "Ar6dofUdp" };
                thread.Start();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("AR6DoF : impossible d'ouvrir le port UDP " + port + " : " + e.Message + "\nPort deja pris par : (1) un autre Unity encore ouvert ou plante mais reste dans le Gestionnaire des taches (termine Unity.exe / Unity Bug Reporter), (2) un 2e composant identique dans la scene (cherche-le dans la Hierarchy : un seul par scene), (3) un autre programme.");
            }
        }

        void OnDisable()
        {
            running = false;
            try { client?.Close(); } catch { }
            client = null;
            thread = null;
        }

        void Loop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                byte[] d;
                try { d = client.Receive(ref any); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                if (d.Length != 80) continue;
                if (BitConverter.ToUInt32(d, 0) != 0x44365241u || BitConverter.ToUInt16(d, 4) != 3) continue;

                var p = new Ar6dofPoseData();
                p.valid = true;
                p.flags = BitConverter.ToUInt16(d, 6);
                p.seq = BitConverter.ToUInt32(d, 8);
                p.posG = new Vector3(BitConverter.ToSingle(d, 12), BitConverter.ToSingle(d, 16), BitConverter.ToSingle(d, 20));
                p.qw = BitConverter.ToSingle(d, 24);
                p.qx = BitConverter.ToSingle(d, 28);
                p.qy = BitConverter.ToSingle(d, 32);
                p.qz = BitConverter.ToSingle(d, 36);
                p.velG = new Vector3(BitConverter.ToSingle(d, 40), BitConverter.ToSingle(d, 44), BitConverter.ToSingle(d, 48));
                p.angG = new Vector3(BitConverter.ToSingle(d, 52), BitConverter.ToSingle(d, 56), BitConverter.ToSingle(d, 60));
                p.floorZ = BitConverter.ToSingle(d, 64);
                p.tPose = BitConverter.ToDouble(d, 68);
                p.camDelay = BitConverter.ToSingle(d, 76);
                p.recvTime = Now;
                lock (lockObj)
                {
                    latest = p;
                    ring[ringHead] = new RotSample { t = p.tPose, w = p.qw, x = p.qx, y = p.qy, z = p.qz };
                    ringHead = (ringHead + 1) % ring.Length;
                    if (ringCount < ring.Length) ringCount++;
                }
                Interlocked.Increment(ref packetCount);
            }
        }

        /// <summary>
        /// Orientation du casque (repère G, casque -> monde, matrice 3x3 en ligne) à l'instant t (horloge QPC),
        /// interpolée dans l'historique. Faux si t est antérieur à l'historique.
        /// </summary>
        public bool TryGetRotationG(double t, out double[] R9)
        {
            R9 = null;
            lock (lockObj)
            {
                if (ringCount == 0) return false;
                int newest = (ringHead - 1 + ring.Length) % ring.Length;
                if (t >= ring[newest].t) { R9 = Ar6dofMath.QuatToMat(new[] { ring[newest].w, ring[newest].x, ring[newest].y, ring[newest].z }); return true; }
                for (int k = 1; k < ringCount; k++)
                {
                    int ib = (newest - k + 1 + ring.Length) % ring.Length;   // plus récent des deux
                    int ia = (newest - k + ring.Length) % ring.Length;       // plus ancien
                    if (ring[ia].t <= t && t <= ring[ib].t)
                    {
                        double span = ring[ib].t - ring[ia].t;
                        double f = span > 1e-9 ? (t - ring[ia].t) / span : 1.0;
                        var qa = new[] { ring[ia].w, ring[ia].x, ring[ia].y, ring[ia].z };
                        var qb = new[] { ring[ib].w, ring[ib].x, ring[ib].y, ring[ib].z };
                        R9 = Ar6dofMath.QuatToMat(Ar6dofMath.Slerp(qa, qb, f));
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Dernière pose et son âge en secondes. Faux tant qu'aucun paquet n'est arrivé.</summary>
        public bool TryGetLatest(out Ar6dofPoseData pose, out double ageSeconds)
        {
            lock (lockObj) pose = latest;
            ageSeconds = pose.valid ? Now - pose.recvTime : double.MaxValue;
            return pose.valid;
        }
    }
}
