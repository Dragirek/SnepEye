using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Au démarrage d'un build Windows : place la fenêtre du jeu, sans bordure, sur l'écran du casque (celui qui a la
    /// taille 2880x1440 par défaut), même si Windows l'a ouverte minimisée ou sur l'écran principal. Sans effet dans l'éditeur.
    /// Réglages conseillés (Project Settings > Player) : Fullscreen Mode = Windowed, Run In Background coché.
    /// </summary>
    public class Ar6dofDisplayStarter : MonoBehaviour
    {
        public int targetWidth = 2880, targetHeight = 1440;
        [Tooltip("Utilisé si aucun écran n'a exactement la taille ci-dessus. -1 = premier écran qui n'est pas le principal")]
        public int monitorIndex = -1;
        public bool borderless = true;
        [Tooltip("Exclut la fenêtre du casque de toute capture d'écran : les écrans virtuels ne peuvent jamais se capturer eux-mêmes (écran dans l'écran). Windows 10 2004 ou plus récent.")]
        public bool excludeFromCapture = true;
        [Tooltip("EXPERIMENTAL : la fenêtre du casque laisse passer les clics de souris vers les fenêtres Windows situées dessous. Utile seulement si le casque est le SEUL écran et sans écran virtuel. Désactivé par défaut.")]
        public bool clickThrough = false;
        [Tooltip("Pendant combien de secondes on réessaie (Unity repositionne parfois la fenêtre au démarrage)")]
        public float retrySeconds = 4f;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc proc, IntPtr dwData);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);

        const int GWL_STYLE = -16;
        const uint SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040;

        IEnumerator Start()
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;   // pas de plein écran exclusif : la fenêtre peut être placée où on veut
            yield return new WaitForSeconds(0.5f);
            float t = 0f;
            while (t < retrySeconds)
            {
                if (Apply()) { yield return new WaitForSeconds(1f); Apply(); yield break; }
                t += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
            UnityEngine.Debug.LogWarning("AR6DoF : fenêtre du jeu ou écran du casque introuvable (taille " + targetWidth + "x" + targetHeight + ").");
        }

        bool Apply()
        {
            IntPtr hwnd = FindOwnWindow();
            if (hwnd == IntPtr.Zero) return false;
            var mons = new List<RECT>();
            var primary = new List<bool>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                GetMonitorInfo(h, ref mi);
                mons.Add(mi.rcMonitor);
                primary.Add((mi.dwFlags & 1) != 0);
                return true;
            }, IntPtr.Zero);
            if (mons.Count == 0) return false;

            int pick = -1;
            for (int i = 0; i < mons.Count && pick < 0; i++)
                if (mons[i].right - mons[i].left == targetWidth && mons[i].bottom - mons[i].top == targetHeight) pick = i;
            if (pick < 0 && monitorIndex >= 0 && monitorIndex < mons.Count) pick = monitorIndex;
            if (pick < 0)
                for (int i = 0; i < mons.Count && pick < 0; i++) if (!primary[i]) pick = i;
            if (pick < 0) pick = 0;

            RECT m = mons[pick];
            ShowWindow(hwnd, 9);   // SW_RESTORE : sort la fenêtre de l'état minimisé
            if (borderless) SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(0x90000000L));   // WS_POPUP | WS_VISIBLE
            SetWindowPos(hwnd, IntPtr.Zero, m.left, m.top, m.right - m.left, m.bottom - m.top, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            SetForegroundWindow(hwnd);
            if (excludeFromCapture) SetWindowDisplayAffinity(hwnd, 0x11);   // WDA_EXCLUDEFROMCAPTURE
            if (clickThrough)
            {
                long ex = GetWindowLongPtr(hwnd, -20).ToInt64();             // GWL_EXSTYLE
                SetWindowLongPtr(hwnd, -20, new IntPtr(ex | 0x80000L | 0x20L));   // WS_EX_LAYERED | WS_EX_TRANSPARENT
                SetLayeredWindowAttributes(hwnd, 0, 255, 2);                  // LWA_ALPHA, opaque
            }
            return true;
        }

        static IntPtr FindOwnWindow()
        {
            uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            EnumWindows((IntPtr h, IntPtr l) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != me || !IsWindowVisible(h)) return true;
                var sb = new StringBuilder(64);
                GetClassName(h, sb, 64);
                if (sb.ToString() == "UnityWndClass") { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
#endif
    }
}
