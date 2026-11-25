using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MemQuota
{
    internal sealed class ApplicationMemory
    {
        public string Name { get; set; }
        public string ExecutablePath { get; set; }
        public long Bytes { get; set; }
        public int ProcessCount { get; set; }
        public List<int> ProcessIds { get; private set; } = new List<int>();
    }

    internal sealed class MemorySnapshot
    {
        public ulong TotalBytes { get; set; }
        public ulong UsedBytes { get; set; }
        public int LoadPercent { get; set; }
        public int InaccessibleProcesses { get; set; }
        public IList<ApplicationMemory> Applications { get; set; }
    }

    internal static class MemoryMonitor
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags,
            StringBuilder executableName, ref int size);

        private static string GetExecutablePath(int processId)
        {
            const uint queryLimitedInformation = 0x1000;
            using (SafeProcessHandle process = OpenProcess(queryLimitedInformation, false, processId))
            {
                if (process.IsInvalid) return null;
                var path = new StringBuilder(32768);
                int size = path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
            }
        }

        public static MemorySnapshot Capture()
        {
            var status = new MemoryStatus();
            if (!GlobalMemoryStatusEx(status))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (status.TotalPhysical == 0)
                throw new InvalidOperationException("Total physical memory could not be determined.");

            var applications = new Dictionary<string, ApplicationMemory>(StringComparer.OrdinalIgnoreCase);
            int inaccessible = 0;
            Process[] processes = Process.GetProcesses();
            foreach (Process process in processes)
            {
                using (process)
                {
                    try
                    {
                        string name = process.ProcessName;
                        long bytes = process.WorkingSet64;
                        ApplicationMemory application;
                        if (!applications.TryGetValue(name, out application))
                        {
                            application = new ApplicationMemory { Name = name };
                            applications.Add(name, application);
                        }

                        application.Bytes += bytes;
                        application.ProcessCount++;
                        application.ProcessIds.Add(process.Id);
                    }
                    catch (Win32Exception) { inaccessible++; }
                    catch (InvalidOperationException) { inaccessible++; }
                    catch (NotSupportedException) { inaccessible++; }
                }
            }

            List<ApplicationMemory> topApplications = applications.Values.OrderByDescending(item => item.Bytes).Take(5).ToList();
            foreach (ApplicationMemory application in topApplications)
            {
                foreach (int id in application.ProcessIds)
                {
                    application.ExecutablePath = GetExecutablePath(id);
                    if (application.ExecutablePath != null) break;
                }
            }

            return new MemorySnapshot
            {
                TotalBytes = status.TotalPhysical,
                UsedBytes = status.TotalPhysical - status.AvailablePhysical,
                LoadPercent = (int)status.MemoryLoad,
                InaccessibleProcesses = inaccessible,
                Applications = topApplications
            };
        }
    }
}
