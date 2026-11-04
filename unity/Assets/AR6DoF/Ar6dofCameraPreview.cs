using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Aperçu d'une caméra d'ar6dof_probe (stéréo, yeux, bouche) dans un menu, À LA DEMANDE : tant qu'on appelle Pump() avec une source, Unity envoie
    /// « PV n » à ar6dof_probe (UDP 9877, au plus deux fois par seconde) et copie l'image 800x300 qu'il écrit dans la mémoire partagée
    /// « Local\ar6dof_preview ». Quand on arrête d'appeler Pump() (page fermée, menu fermé, Unity arrêté), ar6dof_probe cesse tout seul au bout de 3 s :
    /// plus aucune copie d'image, plus aucun traitement, aucun coût. Rien n'est jamais ouvert ni alloué tant qu'un aperçu n'a pas été demandé.
    /// </summary>
    public class Ar6dofCameraPreview
    {
        public const int Stereo = 1, Eyes = 2, Mouth = 3;
        const int W = 800, H = 300;
        readonly int controlPort;
        Ar6dofFrameReader reader;
        Texture2D tex;
        UdpClient udp;
        int source;
        float lastSend = -10f, lastTry = -10f;
        double lastFrameClock;

        public Ar6dofCameraPreview(int controlPort = 9877) { this.controlPort = controlPort; }

        /// <summary>Texture à afficher (null tant qu'aucun aperçu n'a été demandé).</summary>
        public Texture Texture { get { return tex; } }
        /// <summary>Une image récente est affichée.</summary>
        public bool HasFrame { get { return tex != null && source != 0 && Ar6dofClock.Now() - lastFrameClock < 1.0; } }
        public int Source { get { return source; } }

        void Send(string text)
        {
            try
            {
                if (udp == null) udp = Ar6dofSocket.NoInherit(new UdpClient());
                byte[] d = Encoding.ASCII.GetBytes(text);
                udp.Send(d, d.Length, "127.0.0.1", controlPort);
            }
            catch (Exception) { }
        }

        /// <summary>À appeler à chaque image tant que l'aperçu doit tourner (src = 0 : arrêt).</summary>
        public void Pump(int src)
        {
            if (src != source)
            {
                if (src == 0) Send("PV 0");
                source = src;
                lastSend = -10f;
                lastFrameClock = 0;
            }
            if (src == 0) return;
            float now = Time.unscaledTime;
            if (now - lastSend >= 0.5f) { lastSend = now; Send("PV " + src); }
            if (reader == null || !reader.IsOpen)
            {
                if (now - lastTry < 0.5f) return;   // ar6dof_probe crée la mémoire partagée à sa première image d'aperçu
                lastTry = now;
                if (reader == null) reader = new Ar6dofFrameReader();
                if (!reader.Open("Local\\ar6dof_preview") || reader.Width != W || reader.Height != H) { reader.Dispose(); return; }
            }
            if (tex == null) tex = new Texture2D(W, H, TextureFormat.BGRA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            double ageMs;
            if (reader.TryCopyLatest(tex, out ageMs) && ageMs < 1500.0) lastFrameClock = Ar6dofClock.Now();
        }

        /// <summary>Arrête l'aperçu et libère la mémoire (quand le menu se ferme, par exemple).</summary>
        public void Stop()
        {
            if (source != 0) Send("PV 0");
            source = 0;
            if (reader != null) reader.Dispose();
            reader = null;
            if (tex != null) UnityEngine.Object.Destroy(tex);
            tex = null;
        }
    }
}
