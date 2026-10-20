using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace AR6DoF
{
    /// <summary>Envoi de souris / clavier à Windows (SendInput) et recherche de fenêtres. Windows uniquement.</summary>
    public static class Ar6dofWinInput
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int cbSize);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern short VkKeyScan(char ch);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT r, int size);

        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_WHEEL = 0x0800;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;

        /// <summary>Handle d'une fenêtre visible dont le titre contient `contains` (la plus grande). Zéro si aucune.</summary>
        public static IntPtr FindWindowByTitle(string contains)
        {
            if (string.IsNullOrEmpty(contains)) return IntPtr.Zero;
            uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            EnumWindows((IntPtr h, IntPtr l) =>
            {
                if (!IsWindowVisible(h)) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == me) return true;                                   // pas notre propre fenêtre
                int len = GetWindowTextLength(h);
                if (len <= 0) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(h, sb, sb.Capacity);
                if (sb.ToString().IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                long area = (long)(r.right - r.left) * (r.bottom - r.top);
                if (area > bestArea) { bestArea = area; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        public static bool IsValid(IntPtr h) { return h != IntPtr.Zero && IsWindow(h); }

        /// <summary>Rectangle de la fenêtre à l'écran (bords visibles si extended = vrai : sans les marges invisibles de Windows 10/11).</summary>
        public static bool GetRect(IntPtr h, bool extended, out RECT r)
        {
            r = default(RECT);
            if (!IsValid(h)) return false;
            if (extended && DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0) return true;   // DWMWA_EXTENDED_FRAME_BOUNDS
            return GetWindowRect(h, out r);
        }

        public static void SetCursor(int x, int y) { SetCursorPos(x, y); }

        public static void BringToFront(IntPtr h)
        {
            if (!IsValid(h)) return;
            if (IsIconic(h)) ShowWindow(h, 9);   // SW_RESTORE
            keybd_event(0x12, 0, 0, UIntPtr.Zero);                 // appui sur Alt : autorise SetForegroundWindow
            keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            SetForegroundWindow(h);
        }

        static void Send(INPUT i) { SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT))); }

        /// <summary>button 0 = gauche, 1 = droit.</summary>
        public static void MouseButton(int button, bool down)
        {
            var i = new INPUT { type = INPUT_MOUSE };
            i.u.mi.dwFlags = button == 0 ? (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP) : (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP);
            Send(i);
        }

        public static void Wheel(int delta)
        {
            var i = new INPUT { type = INPUT_MOUSE };
            i.u.mi.dwFlags = MOUSEEVENTF_WHEEL;
            i.u.mi.mouseData = unchecked((uint)delta);
            Send(i);
        }

        static bool IsExtended(ushort vk)
        {   // flèches, Inser, Suppr, Début, Fin, Page préc./suiv. : touches "étendues"
            return (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E;
        }

        public static void Key(ushort vk, bool down)
        {
            var i = new INPUT { type = INPUT_KEYBOARD };
            i.u.ki.wVk = vk;
            i.u.ki.dwFlags = (down ? 0u : KEYEVENTF_KEYUP) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0u);
            Send(i);
        }

        /// <summary>Tape un caractère, indépendamment de la disposition du clavier de Windows (AZERTY, QWERTY...).</summary>
        public static void TypeChar(char c)
        {
            var d = new INPUT { type = INPUT_KEYBOARD };
            d.u.ki.wScan = c;
            d.u.ki.dwFlags = KEYEVENTF_UNICODE;
            var u = d;
            u.u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            SendInput(2, new[] { d, u }, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>Touche virtuelle de Windows qui produit ce caractère avec la disposition actuelle (pour Ctrl+C, Ctrl+V...).</summary>
        public static ushort VkForChar(char c) { return (ushort)(VkKeyScan(c) & 0xFF); }
#else
        public static IntPtr FindWindowByTitle(string contains) { return IntPtr.Zero; }
        public static bool IsValid(IntPtr h) { return false; }
        public static bool GetRect(IntPtr h, bool extended, out RECT r) { r = default(RECT); return false; }
        public static void SetCursor(int x, int y) { }
        public static void BringToFront(IntPtr h) { }
        public static void MouseButton(int button, bool down) { }
        public static void Wheel(int delta) { }
        public static void Key(ushort vk, bool down) { }
        public static void TypeChar(char c) { }
        public static ushort VkForChar(char c) { return 0; }
#endif
    }
}
