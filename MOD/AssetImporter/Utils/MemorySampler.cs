using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ExtraAssetsImporter.AssetImporter.Utils
{
    /// <summary>
    /// Samples the process memory at a fixed interval while an import runs, to report its start, end and peak next to
    /// the performance stats. Textures are decoded and compressed in native buffers, so the process working set and
    /// private bytes matter more than the managed heap. The values are process wide: importers running at the same
    /// time see each other's memory.
    /// </summary>
    internal sealed class MemorySampler : IDisposable
    {
        private const int k_IntervalMs = 250;

        private readonly Timer m_Timer;
        private readonly object m_Lock = new();

        private Sample m_Start;
        private Sample m_End;
        private Sample m_Peak;
        private bool m_Stopped;

        // Unity's Mono returns 0 for Process.WorkingSet64/PrivateMemorySize64, so the native memory is read from
        // Windows directly. Disabled for the session when the API isn't there (other platforms).
        private static bool s_NativeAvailable = true;

        private struct Sample
        {
            public long WorkingSet;
            public long PrivateBytes;
            public long Managed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCountersEx
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCountersEx counters, uint size);

        public MemorySampler()
        {
            m_Start = m_Peak = m_End = TakeSample();
            m_Timer = new Timer(_ => Record(), null, k_IntervalMs, k_IntervalMs);
        }

        private static Sample TakeSample()
        {
            Sample sample = new() { Managed = GC.GetTotalMemory(false) };

            if (!s_NativeAvailable) return sample;

            // Profiling only: an import must never fail because the memory couldn't be read.
            try
            {
                if (GetProcessMemoryInfo(GetCurrentProcess(), out ProcessMemoryCountersEx counters, (uint)Marshal.SizeOf<ProcessMemoryCountersEx>()))
                {
                    sample.WorkingSet = (long)counters.WorkingSetSize.ToUInt64();
                    sample.PrivateBytes = (long)counters.PrivateUsage.ToUInt64();
                }
            }
            catch (Exception e)
            {
                s_NativeAvailable = false;
                EAI.Logger.Warn($"Couldn't read the process memory, only the managed memory will be reported: {e.Message}");
            }

            return sample;
        }

        private void Record()
        {
            lock (m_Lock)
            {
                if (m_Stopped) return;

                Sample sample = TakeSample();
                m_End = sample;
                m_Peak.WorkingSet = Math.Max(m_Peak.WorkingSet, sample.WorkingSet);
                m_Peak.PrivateBytes = Math.Max(m_Peak.PrivateBytes, sample.PrivateBytes);
                m_Peak.Managed = Math.Max(m_Peak.Managed, sample.Managed);
            }
        }

        /// <summary>
        /// Stops sampling (taking a last sample) and formats the result as a line of the performance report.
        /// </summary>
        public string Stop()
        {
            Record();
            lock (m_Lock) m_Stopped = true;
            m_Timer.Dispose();

            return $"  Memory (process, GB, start → end / peak): working set {ToGB(m_Start.WorkingSet)} → {ToGB(m_End.WorkingSet)} / {ToGB(m_Peak.WorkingSet)}"
                + $", private {ToGB(m_Start.PrivateBytes)} → {ToGB(m_End.PrivateBytes)} / {ToGB(m_Peak.PrivateBytes)}"
                + $", managed {ToGB(m_Start.Managed)} → {ToGB(m_End.Managed)} / {ToGB(m_Peak.Managed)}";
        }

        public void Dispose()
        {
            lock (m_Lock) m_Stopped = true;
            m_Timer.Dispose();
        }

        private static string ToGB(long bytes) => (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2");
    }
}
