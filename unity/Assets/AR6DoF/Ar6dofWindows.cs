using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AR6DoF
{
    /// <summary>
    /// Outils Windows pour l'écran virtuel LOGICIEL (une fenêtre d'application affichée comme un écran) : liste des fenêtres, taille,
    /// remise au premier plan sans la rendre active. La capture elle-même est faite par ar6dof_screen.dll (A6S_StartWindow).
    /// </summary>
    public static class Ar6dofWindows
    {
        public struct Info
        {
            public long hwnd;
            public string title;
            public int width, height;
        }

        /// <summary>Tailles proposées pour la zone utile de la fenêtre (0 = ne pas toucher).</summary>
        public static readonly int[][] Presets = { new[] { 0, 0 }, new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 } };

        public static string PresetName(int i)
        {
            i = Math.Max(0, Math.Min(Presets.Length - 1, i));
            return Presets[i][0] == 0 ? "Garder la taille" : Presets[i][0] + " x " + Presets[i][1];
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowTextLengthW(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr h, int index);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x80;
        const uint GW_OWNER = 4;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;

        static List<Info> enumResult;
        static uint selfPid;
        static readonly EnumProc enumCb = EnumCallback;   // gardé en vie : Windows l'appelle pendant EnumWindows

        [AOT.MonoPInvokeCallback(typeof(EnumProc))]
        static bool EnumCallback(IntPtr h, IntPtr lp)
        {
            try
            {
                if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;         // invisible ou fenêtre fille d'une autre
                if ((GetWindowLongW(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;              // palettes, info-bulles...
                int cloaked;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true;   // fenêtres "fantômes" (autres bureaux, applis suspendues)
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == selfPid) return true;                                                         // pas la fenêtre de Unity
                int n = GetWindowTextLengthW(h);
                if (n <= 0) return true;
                var sb = new StringBuilder(n + 1);
                GetWindowTextW(h, sb, n + 1);
                string title = sb.ToString();
                int l, t, r, b;
                if (!Bounds((long)h, out l, out t, out r, out b)) return true;
                if (r - l < 120 || b - t < 80) return true;
                if (title == "Program Manager") return true;
                enumResult.Add(new Info { hwnd = (long)h, title = title, width = r - l, height = b - t });
            }
            catch (Exception) { }
            return true;
        }

        /// <summary>Fenêtres d'application visibles, de la plus haute à la plus basse (ordre d'empilement de Windows).</summary>
        public static List<Info> List()
        {
            var res = new List<Info>();
            try
            {
                selfPid = GetCurrentProcessId();
                enumResult = res;
                EnumWindows(enumCb, IntPtr.Zero);
            }
            catch (Exception) { }
            enumResult = null;
            return res;
        }

        public static bool Alive(long hwnd) { try { return hwnd != 0 && IsWindow((IntPtr)hwnd); } catch (Exception) { return false; } }

        public static string Title(long hwnd)
        {
            try
            {
                int n = GetWindowTextLengthW((IntPtr)hwnd);
                if (n <= 0) return "";
                var sb = new StringBuilder(n + 1);
                GetWindowTextW((IntPtr)hwnd, sb, n + 1);
                return sb.ToString();
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Rectangle visible de la fenêtre (sans les ombres invisibles), en pixels du bureau virtuel.</summary>
        public static bool Bounds(long hwnd, out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            try
            {
                RECT r;
                if (DwmGetWindowAttribute((IntPtr)hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, 16) != 0 && !GetWindowRect((IntPtr)hwnd, out r)) return false;
                left = r.left; top = r.top; right = r.right; bottom = r.bottom;
                return right > left && bottom > top;
            }
            catch (Exception) { return false; }
        }

        public static bool IsMinimized(long hwnd) { try { return IsIconic((IntPtr)hwnd); } catch (Exception) { return false; } }

        /// <summary>Sort la fenêtre de la barre des tâches (une fenêtre réduite ne peut pas être capturée).</summary>
        public static void Restore(long hwnd)
        {
            try { if (IsIconic((IntPtr)hwnd)) ShowWindow((IntPtr)hwnd, 9); } catch (Exception) { }   // SW_RESTORE
        }

        /// <summary>Met la fenêtre au-dessus des autres SANS la rendre active : le clic qui suit l'active et tombe bien sur elle.</summary>
        public static void Raise(long hwnd)
        {
            try { SetWindowPos((IntPtr)hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); } catch (Exception) { }   // HWND_TOP = 0
        }

        /// <summary>Règle la taille de la ZONE UTILE de la fenêtre (sans la barre de titre ni les bordures).</summary>
        public static bool SetClientSize(long hwnd, int w, int h)
        {
            try
            {
                var ptr = (IntPtr)hwnd;
                Restore(hwnd);
                RECT wr, cr;
                if (!GetWindowRect(ptr, out wr) || !GetClientRect(ptr, out cr)) return false;
                int extraW = (wr.right - wr.left) - (cr.right - cr.left);
                int extraH = (wr.bottom - wr.top) - (cr.bottom - cr.top);
                return SetWindowPos(ptr, IntPtr.Zero, wr.left, wr.top, w + extraW, h + extraH, SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch (Exception) { return false; }
        }
#else
        public static List<Info> List() { return new List<Info>(); }
        public static bool Alive(long hwnd) { return false; }
        public static string Title(long hwnd) { return ""; }
        public static bool Bounds(long hwnd, out int left, out int top, out int right, out int bottom) { left = top = right = bottom = 0; return false; }
        public static bool IsMinimized(long hwnd) { return false; }
        public static void Restore(long hwnd) { }
        public static void Raise(long hwnd) { }
        public static bool SetClientSize(long hwnd, int w, int h) { return false; }
#endif
    }
}
