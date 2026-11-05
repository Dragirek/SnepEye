using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AR6DoF
{
    /// <summary>
    /// Charge et températures du PC (processeur global et par coeur, mémoire, carte graphique, batterie), uniquement avec des API de Windows :
    /// rien à installer. L'échantillonneur est un fil léger qui lit une fois par seconde et s'arrête tout seul 3,5 s après la dernière demande
    /// (Touch) : tant que la page Performances n'est pas affichée, il ne tourne pas du tout.
    ///
    /// Limites honnêtes : Windows ne donne PAS la température réelle du processeur (surtout sur Ryzen) sans pilote noyau ; on ne lit que le capteur
    /// thermique ACPI de la carte mère quand il existe (portables surtout), sinon « indisponible ». La carte graphique : température, charge et
    /// mémoire pour NVIDIA (nvml.dll fourni avec le pilote) ; pour les autres, la charge seulement (compteurs Windows), sans température.
    /// </summary>
    public static class Ar6dofSysInfo
    {
        /// <summary>Un jeu de mesures. Valeurs inconnues : NaN (flottants) ou -1.</summary>
        public class Sample
        {
            public bool valid;                       // faux avant la deuxième lecture (il faut deux points pour une charge)
            public float cpuTotal = float.NaN;       // 0..1
            public float[] cores = new float[0];     // 0..1 par coeur logique
            public float cpuTemp = float.NaN;        // °C (capteur ACPI)
            public float ramPct = float.NaN;         // 0..1
            public float ramUsedGb = float.NaN, ramTotalGb = float.NaN;
            public string gpuName = "";
            public float gpuLoad = float.NaN;        // 0..1
            public float gpuTemp = float.NaN;        // °C
            public float vramUsedGb = float.NaN, vramTotalGb = float.NaN;
            public bool hasBattery, onAc, charging;
            public float batteryPct = float.NaN;     // 0..1
            public int batterySeconds = -1;          // temps restant sur batterie, -1 inconnu
        }

        public static bool Supported
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>État de la batterie du PC (portable) : lecture directe, sans fil ni échantillonneur (utilisée par l'affichage du dock).</summary>
        public struct BatteryInfo
        {
            public bool has, onAc, charging;
            public float pct;        // 0..1, NaN si inconnu
            public int seconds;      // temps restant sur batterie, -1 inconnu
        }

        static volatile Sample current = new Sample();
        public static Sample Current { get { return current; } }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // ------------------------------------------------------------------ cycle de vie
        static readonly object gate = new object();
        static bool running;
        static int lastTouch;

        /// <summary>À appeler tant qu'on affiche ces mesures (quelques fois par seconde suffit). Démarre l'échantillonneur s'il dort.</summary>
        public static void Touch()
        {
            lock (gate)
            {
                lastTouch = Environment.TickCount;
                if (running) return;
                running = true;
                var t = new Thread(Loop) { IsBackground = true, Name = "AR6DoF sysinfo", Priority = ThreadPriority.BelowNormal };
                t.Start();
            }
        }

        static void Loop()
        {
            try
            {
                Open();
                Read(false);
                while (true)
                {
                    Thread.Sleep(1000);
                    lock (gate)
                    {
                        if (unchecked(Environment.TickCount - lastTouch) > 3500) { running = false; break; }
                    }
                    Read(true);
                }
            }
            catch (Exception) { lock (gate) { running = false; } }
            Close();
        }

        // ------------------------------------------------------------------ Windows
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);

        [StructLayout(LayoutKind.Sequential)]
        struct MemStatus
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);

        [StructLayout(LayoutKind.Sequential)]
        struct PowerStatus
        {
            public byte acLine, batteryFlag, batteryPercent, systemFlag;
            public uint lifeTime, fullLifeTime;
        }
        [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus s);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryW(string path);

        // compteurs de performance de Windows (PDH)
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQueryW(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterArrayW(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr q);
        const uint PdhFmtDouble = 0x200, PdhMoreData = 0x800007D2;

        // NVIDIA (nvml.dll est installée avec le pilote)
        [StructLayout(LayoutKind.Sequential)] struct NvUtil { public uint gpu, memory; }
        [StructLayout(LayoutKind.Sequential)] struct NvMem { public ulong total, free, used; }
        [DllImport("nvml.dll")] static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] static extern int nvmlShutdown();
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr dev);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensor, out uint temp);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetUtilizationRates(IntPtr dev, out NvUtil u);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetMemoryInfo(IntPtr dev, out NvMem m);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetName(IntPtr dev, StringBuilder name, uint len);

        static int nCpu;
        static long[] prevIdle, prevBusy;
        static IntPtr cpuBuf = IntPtr.Zero;
        static IntPtr pdhThermal, pdhGpu, thermalCounter, gpuCounter;
        static bool nvOk, nvTried;
        static IntPtr nvDev;
        static string nvName = "";

        static void Open()
        {
            nCpu = Math.Max(1, Environment.ProcessorCount);
            cpuBuf = Marshal.AllocHGlobal(nCpu * 48);
            prevIdle = new long[nCpu];
            prevBusy = new long[nCpu];
            try
            {
                if (PdhOpenQueryW(null, IntPtr.Zero, out pdhThermal) == 0)
                    if (PdhAddEnglishCounterW(pdhThermal, "\\Thermal Zone Information(*)\\Temperature", IntPtr.Zero, out thermalCounter) != 0) thermalCounter = IntPtr.Zero;
            }
            catch (Exception) { thermalCounter = IntPtr.Zero; }
            try
            {
                if (PdhOpenQueryW(null, IntPtr.Zero, out pdhGpu) == 0)
                    if (PdhAddEnglishCounterW(pdhGpu, "\\GPU Engine(*)\\Utilization Percentage", IntPtr.Zero, out gpuCounter) != 0) gpuCounter = IntPtr.Zero;
            }
            catch (Exception) { gpuCounter = IntPtr.Zero; }
        }

        static void Close()
        {
            try { if (cpuBuf != IntPtr.Zero) Marshal.FreeHGlobal(cpuBuf); } catch (Exception) { }
            cpuBuf = IntPtr.Zero;
            try { if (pdhThermal != IntPtr.Zero) PdhCloseQuery(pdhThermal); } catch (Exception) { }
            try { if (pdhGpu != IntPtr.Zero) PdhCloseQuery(pdhGpu); } catch (Exception) { }
            pdhThermal = pdhGpu = thermalCounter = gpuCounter = IntPtr.Zero;
            if (nvOk) { try { nvmlShutdown(); } catch (Exception) { } }
            nvOk = nvTried = false;
        }

        static void Read(bool publish)
        {
            var s = new Sample { valid = publish };

            // processeur : temps inactif / occupé de chaque coeur logique
            int ret;
            if (NtQuerySystemInformation(8, cpuBuf, nCpu * 48, out ret) == 0)
            {
                int n = Math.Min(nCpu, ret / 48);
                s.cores = new float[n];
                long totIdle = 0, totAll = 0;
                for (int i = 0; i < n; i++)
                {
                    long idle = Marshal.ReadInt64(cpuBuf, i * 48), kernel = Marshal.ReadInt64(cpuBuf, i * 48 + 8), user = Marshal.ReadInt64(cpuBuf, i * 48 + 16);
                    long busy = kernel + user;   // le temps noyau contient le temps inactif
                    long dI = idle - prevIdle[i], dB = busy - prevBusy[i];
                    prevIdle[i] = idle; prevBusy[i] = busy;
                    if (dB > 0) { s.cores[i] = Clamp01(1f - (float)dI / dB); totIdle += dI; totAll += dB; }
                }
                if (totAll > 0) s.cpuTotal = Clamp01(1f - (float)totIdle / totAll);
            }

            // mémoire
            var ms = new MemStatus { dwLength = (uint)Marshal.SizeOf(typeof(MemStatus)) };
            if (GlobalMemoryStatusEx(ref ms))
            {
                s.ramPct = ms.dwMemoryLoad / 100f;
                s.ramTotalGb = ms.ullTotalPhys / 1073741824f;
                s.ramUsedGb = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824f;
            }

            // batterie
            PowerStatus ps;
            if (GetSystemPowerStatus(out ps))
            {
                s.hasBattery = (ps.batteryFlag & 128) == 0 && ps.batteryFlag != 255;
                s.onAc = ps.acLine == 1;
                s.charging = (ps.batteryFlag & 8) != 0;
                if (s.hasBattery)
                {
                    if (ps.batteryPercent <= 100) s.batteryPct = ps.batteryPercent / 100f;
                    if (ps.lifeTime != 0xFFFFFFFFu) s.batterySeconds = (int)Math.Min(ps.lifeTime, int.MaxValue);
                }
            }

            // température : capteur thermique ACPI (le plus chaud des capteurs)
            if (thermalCounter != IntPtr.Zero && PdhCollectQueryData(pdhThermal) == 0)
            {
                float best = float.NaN;
                foreach (var kv in PdhArray(thermalCounter))
                {
                    float c = (float)kv.Value - 273.15f;
                    if (c > 5f && c < 125f && (float.IsNaN(best) || c > best)) best = c;
                }
                s.cpuTemp = best;
            }

            ReadGpu(s);
            if (publish) current = s;
        }

        static void ReadGpu(Sample s)
        {
            // NVIDIA : température, charge, mémoire
            if (!nvTried)
            {
                nvTried = true;
                try
                {
                    // sur certains PC, nvml.dll n'est pas dans System32 mais dans le dossier NVSMI
                    string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll");
                    if (File.Exists(p)) LoadLibraryW(p);
                    if (nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out nvDev) == 0)
                    {
                        nvOk = true;
                        var sb = new StringBuilder(96);
                        if (nvmlDeviceGetName(nvDev, sb, 96) == 0) nvName = sb.ToString();
                    }
                }
                catch (Exception) { nvOk = false; }
            }
            if (nvOk)
            {
                try
                {
                    uint t;
                    if (nvmlDeviceGetTemperature(nvDev, 0, out t) == 0) s.gpuTemp = t;
                    NvUtil u;
                    if (nvmlDeviceGetUtilizationRates(nvDev, out u) == 0) s.gpuLoad = u.gpu / 100f;
                    NvMem m;
                    if (nvmlDeviceGetMemoryInfo(nvDev, out m) == 0) { s.vramUsedGb = m.used / 1073741824f; s.vramTotalGb = m.total / 1073741824f; }
                    s.gpuName = nvName;
                    return;
                }
                catch (Exception) { nvOk = false; }
            }
            // autres cartes : charge du moteur 3D (compteurs Windows), sans température
            if (gpuCounter != IntPtr.Zero && PdhCollectQueryData(pdhGpu) == 0)
            {
                var perAdapter = new Dictionary<string, double>();
                foreach (var kv in PdhArray(gpuCounter))
                {
                    if (kv.Key.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int a = kv.Key.IndexOf("luid_", StringComparison.Ordinal), b = kv.Key.IndexOf("_phys", StringComparison.Ordinal);
                    string luid = a >= 0 && b > a ? kv.Key.Substring(a, b - a) : "";
                    double cur;
                    perAdapter.TryGetValue(luid, out cur);
                    perAdapter[luid] = cur + kv.Value;
                }
                double top = -1;
                foreach (var v in perAdapter.Values) if (v > top) top = v;
                if (top >= 0) s.gpuLoad = Clamp01((float)(top / 100.0));
            }
        }

        /// <summary>Instances et valeurs (double) d'un compteur à caractères génériques.</summary>
        static List<KeyValuePair<string, double>> PdhArray(IntPtr counter)
        {
            var list = new List<KeyValuePair<string, double>>();
            uint size = 0, count = 0;
            uint r = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref size, out count, IntPtr.Zero);
            if (r != PdhMoreData || size == 0 || size > 8 * 1024 * 1024) return list;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref size, out count, buf) != 0) return list;
                for (int i = 0; i < count; i++)
                {
                    int off = i * 24;   // PDH_FMT_COUNTERVALUE_ITEM_W : nom (8 octets), état (4 + 4 de remplissage), valeur (8)
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buf, off)) ?? "";
                    int status = Marshal.ReadInt32(buf, off + 8);
                    if (status != 0 && status != 1) continue;   // PDH_CSTATUS_VALID_DATA / NEW_DATA
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buf, off + 16));
                    list.Add(new KeyValuePair<string, double>(name, v));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return list;
        }

        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        public static BatteryInfo ReadBattery()
        {
            var b = new BatteryInfo { pct = float.NaN, seconds = -1 };
            try
            {
                PowerStatus ps;
                if (!GetSystemPowerStatus(out ps)) return b;
                b.has = (ps.batteryFlag & 128) == 0 && ps.batteryFlag != 255;
                b.onAc = ps.acLine == 1;
                b.charging = (ps.batteryFlag & 8) != 0;
                if (b.has)
                {
                    if (ps.batteryPercent <= 100) b.pct = ps.batteryPercent / 100f;
                    if (ps.lifeTime != 0xFFFFFFFFu) b.seconds = (int)Math.Min(ps.lifeTime, int.MaxValue);
                }
            }
            catch (Exception) { }
            return b;
        }
#else
        public static void Touch() { }
        public static BatteryInfo ReadBattery() { return new BatteryInfo { pct = float.NaN, seconds = -1 }; }
#endif
    }
}
