using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Lit l'image de la caméra partagée par ar6dof_probe (mémoire partagée Windows, protocole sans verrou
    /// décrit dans src/frame_share_core.h). Aucun code "unsafe" nécessaire.
    /// </summary>
    public class Ar6dofFrameReader : IDisposable
    {
        [DllImport("kernel32.dll", EntryPoint = "OpenFileMappingA", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr MapViewOfFile(IntPtr hMap, uint access, uint offHigh, uint offLow, UIntPtr bytes);
        [DllImport("kernel32.dll")] static extern bool UnmapViewOfFile(IntPtr p);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceCounter(out long v);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long v);

        const uint FILE_MAP_READ = 0x0004;
        const uint Magic = 0x46365241u;   // 'A','R','6','F'
        const int HeaderBytes = 64, SlotHeaderBytes = 64;

        IntPtr hMap, basePtr;
        long slotBytes, freq;
        ulong lastId;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool IsOpen => basePtr != IntPtr.Zero;
        /// <summary>Instant (QueryPerformanceCounter, secondes) où ar6dof_probe a écrit la dernière image copiée.</summary>
        public double LastWriteQpc { get; private set; }

        /// <summary>Ouvre la mémoire créée par ar6dof_probe (nom : "Local\\ar6dof_frame").</summary>
        public bool Open(string name)
        {
            if (IsOpen) return true;
            hMap = OpenFileMapping(FILE_MAP_READ, false, name);
            if (hMap == IntPtr.Zero) return false;
            basePtr = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, UIntPtr.Zero);
            if (basePtr == IntPtr.Zero) { Dispose(); return false; }
            if ((uint)Marshal.ReadInt32(basePtr, 0) != Magic || Marshal.ReadInt32(basePtr, 4) != 1
                || Marshal.ReadInt32(basePtr, 16) != 4) { Dispose(); return false; }
            Width = Marshal.ReadInt32(basePtr, 8);
            Height = Marshal.ReadInt32(basePtr, 12);
            slotBytes = (uint)Marshal.ReadInt32(basePtr, 24);
            QueryPerformanceFrequency(out freq);
            lastId = 0;
            return true;
        }

        /// <summary>
        /// Copie la dernière image dans la texture (BGRA32, mêmes dimensions) si une nouvelle image cohérente existe.
        /// ageMs = temps écoulé depuis son écriture par ar6dof_probe (n'inclut pas l'exposition ni le transfert USB).
        /// </summary>
        public bool TryCopyLatest(Texture2D tex, out double ageMs)
        {
            ageMs = 0;
            if (!IsOpen) return false;
            ulong latest = (ulong)Marshal.ReadInt64(basePtr, 32);
            if (latest == 0 || latest == lastId) return false;
            int slot = Marshal.ReadInt32(basePtr, 40) & 1;
            IntPtr s = IntPtr.Add(basePtr, (int)(HeaderBytes + slot * slotBytes));
            long s1 = Marshal.ReadInt64(s, 0);
            if ((s1 & 1) != 0) return false;                          // écriture en cours
            tex.LoadRawTextureData(IntPtr.Add(s, SlotHeaderBytes), Width * 4 * Height);
            long s2 = Marshal.ReadInt64(s, 0);
            if (s1 != s2) return false;                               // écrasée pendant la copie : on jette
            tex.Apply(false, false);
            lastId = (ulong)Marshal.ReadInt64(s, 8);
            double tWrite = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(s, 16));
            QueryPerformanceCounter(out long now);
            LastWriteQpc = tWrite;
            ageMs = ((double)now / freq - tWrite) * 1000.0;
            return true;
        }

        public void Dispose()
        {
            if (basePtr != IntPtr.Zero) UnmapViewOfFile(basePtr);
            if (hMap != IntPtr.Zero) CloseHandle(hMap);
            basePtr = IntPtr.Zero;
            hMap = IntPtr.Zero;
        }
    }
}
