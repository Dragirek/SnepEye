using System;
using System.Diagnostics;
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
        public double recvTime;      // secondes (horloge locale du récepteur)

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

    /// <summary>Écoute le port UDP et garde la dernière pose reçue.</summary>
    public class Ar6dofReceiver : MonoBehaviour
    {
        public int port = 9876;

        static readonly Stopwatch clock = Stopwatch.StartNew();
        public static double Now => clock.Elapsed.TotalSeconds;

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
                client = new UdpClient(port);
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "Ar6dofUdp" };
                thread.Start();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("AR6DoF : impossible d'ouvrir le port UDP " + port + " : " + e.Message);
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
                if (d.Length != 68) continue;
                if (BitConverter.ToUInt32(d, 0) != 0x44365241u || BitConverter.ToUInt16(d, 4) != 1) continue;

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
                p.recvTime = Now;
                lock (lockObj) latest = p;
                Interlocked.Increment(ref packetCount);
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
