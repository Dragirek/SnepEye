using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AR6DoF
{
    /// <summary>
    /// Rend les sockets UDP d'Unity NON héritables. Sans ça, quand Unity lance ar6dof_probe.exe, celui-ci hérite des sockets
    /// ouverts (ports 9876, 9878, 9883...) et les garde ouverts même après l'arrêt du Play : au Play suivant,
    /// "Une seule utilisation de chaque adresse de socket est habituellement autorisée".
    /// </summary>
    public static class Ar6dofSocket
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);
        const uint HANDLE_FLAG_INHERIT = 1;
#endif

        public static UdpClient NoInherit(UdpClient c)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try { if (c != null && c.Client != null) SetHandleInformation(c.Client.Handle, HANDLE_FLAG_INHERIT, 0); } catch (Exception) { }
#endif
            return c;
        }
    }
}
