using System;
using System.Runtime.InteropServices;

namespace AR6DoF
{
    /// <summary>Accès à ar6dof_screen.dll (capture d'écran GPU). À copier dans Assets/AR6DoF/Plugins/x86_64/.</summary>
    public static class Ar6dofScreenNative
    {
        const string Dll = "ar6dof_screen";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport(Dll)] static extern int A6S_MonitorCount();
        [DllImport(Dll)] static extern int A6S_MonitorRect(int i, int[] rect4);
        [DllImport(Dll)] static extern void A6S_Start(int slot, int monitor, int maxFps);
        [DllImport(Dll)] static extern void A6S_Stop(int slot);
        [DllImport(Dll)] static extern void A6S_StopAll();
        [DllImport(Dll)] static extern void A6S_SetActive(int slot, int active);
        [DllImport(Dll)] static extern void A6S_SetFps(int slot, int fps);
        [DllImport(Dll)] static extern void A6S_SetTarget(int slot, IntPtr tex);
        [DllImport(Dll)] static extern int A6S_GetWidth(int slot);
        [DllImport(Dll)] static extern int A6S_GetHeight(int slot);
        [DllImport(Dll)] static extern int A6S_GetStatus(int slot);
        [DllImport(Dll)] static extern long A6S_GetFrames(int slot);
        [DllImport(Dll)] static extern IntPtr A6S_GetRenderEventFunc();

        static bool? available;
        /// <summary>Vrai si la DLL est présente et chargeable.</summary>
        public static bool Available
        {
            get
            {
                if (available == null)
                {
                    try { A6S_MonitorCount(); available = true; }
                    catch (Exception e) { UnityEngine.Debug.LogError("AR6DoF : ar6dof_screen.dll introuvable ou illisible (" + e.GetType().Name + "). Copie-la dans Assets/AR6DoF/Plugins/x86_64/. " + e.Message); available = false; }
                }
                return available.Value;
            }
        }

        public static int MonitorCount() { return Available ? A6S_MonitorCount() : 0; }
        /// <summary>Rectangle du moniteur dans le bureau virtuel de Windows (pixels).</summary>
        public static bool MonitorRect(int i, out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            if (!Available) return false;
            var r = new int[4];
            if (A6S_MonitorRect(i, r) == 0) return false;
            left = r[0]; top = r[1]; right = r[2]; bottom = r[3];
            return true;
        }
        public static void Start(int slot, int monitor, int fps) { if (Available) A6S_Start(slot, monitor, fps); }
        public static void Stop(int slot) { if (Available) A6S_Stop(slot); }
        public static void StopAll() { if (Available) A6S_StopAll(); }
        public static void SetActive(int slot, bool a) { if (Available) A6S_SetActive(slot, a ? 1 : 0); }
        public static void SetFps(int slot, int fps) { if (Available) A6S_SetFps(slot, fps); }
        public static void SetTarget(int slot, IntPtr tex) { if (Available) A6S_SetTarget(slot, tex); }
        public static int Width(int slot) { return Available ? A6S_GetWidth(slot) : 0; }
        public static int Height(int slot) { return Available ? A6S_GetHeight(slot) : 0; }
        /// <summary>0 arrêté, 1 démarrage, 2 capture en cours, 3 écran inaccessible, 4 format non géré (HDR).</summary>
        public static int Status(int slot) { return Available ? A6S_GetStatus(slot) : 0; }
        public static long Frames(int slot) { return Available ? A6S_GetFrames(slot) : 0; }
        public static IntPtr RenderEventFunc() { return Available ? A6S_GetRenderEventFunc() : IntPtr.Zero; }
#else
        public static bool Available { get { return false; } }
        public static int MonitorCount() { return 0; }
        public static bool MonitorRect(int i, out int left, out int top, out int right, out int bottom) { left = top = right = bottom = 0; return false; }
        public static void Start(int slot, int monitor, int fps) { }
        public static void Stop(int slot) { }
        public static void StopAll() { }
        public static void SetActive(int slot, bool a) { }
        public static void SetFps(int slot, int fps) { }
        public static void SetTarget(int slot, IntPtr tex) { }
        public static int Width(int slot) { return 0; }
        public static int Height(int slot) { return 0; }
        public static int Status(int slot) { return 0; }
        public static long Frames(int slot) { return 0; }
        public static IntPtr RenderEventFunc() { return IntPtr.Zero; }
#endif
    }
}
