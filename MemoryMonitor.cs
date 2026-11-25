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
    internal sealed class ProcessTarget
    {
        public int Id { get; set; }
        public long? CreationTime { get; set; }
    }

    internal sealed class ApplicationMemory
    {
        public string Name { get; set; }
        public string ExecutablePath { get; set; }
        public long Bytes { get; set; }
        public ulong? EstimatedReleasableBytes { get; set; }
        public int ProcessCount { get; set; }
        public List<ProcessTarget> Processes { get; private set; } = new List<ProcessTarget>();
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

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCountersEx2
        {
            public uint Size;
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
            public UIntPtr PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(SafeProcessHandle process,
            out ProcessMemoryCountersEx2 counters, uint size);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(SafeProcessHandle process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(SafeProcessHandle process, out long creationTime,
            out long exitTime, out long kernelTime, out long userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags,
            StringBuilder executableName, ref int size);

        private delegate bool WindowCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

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

        private static long? GetCreationTime(int processId)
        {
            using (SafeProcessHandle process = OpenProcess(0x1000, false, processId))
            {
                long creation, exit, kernel, user;
                if (process.IsInvalid || !GetProcessTimes(process, out creation, out exit, out kernel, out user))
                    return null;
                return creation;
            }
        }

        public static string ActivateApplication(ApplicationMemory application)
        {
            var targets = application.Processes.Where(target => target.CreationTime.HasValue)
                .ToDictionary(target => target.Id, target => target.CreationTime.Value);
            IntPtr selected = IntPtr.Zero;
            bool enumerated = EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (!IsWindowVisible(window) || GetWindowTextLength(window) == 0) return true;
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                long expected;
                if (processId <= int.MaxValue && targets.TryGetValue((int)processId, out expected) &&
                    GetCreationTime((int)processId) == expected)
                {
                    selected = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (selected == IntPtr.Zero)
            {
                if (!enumerated) return "Windows could not enumerate application windows.";
                return "No accessible application window was found. The process may have exited or may not have a window.";
            }
            if (IsIconic(selected) && !ShowWindowAsync(selected, 9))
                return "The application window could not be restored.";
            if (!SetForegroundWindow(selected))
                return "Windows did not allow the application window to be activated.";
            return null;
        }

        public static string TrimWorkingSets(ApplicationMemory application)
        {
            int succeeded = 0;
            var failures = new List<string>();
            foreach (ProcessTarget target in application.Processes)
            {
                if (!target.CreationTime.HasValue)
                {
                    failures.Add(string.Format("PID {0}: process identity unavailable", target.Id));
                    continue;
                }
                using (SafeProcessHandle process = OpenProcess(0x1000 | 0x0100, false, target.Id))
                {
                    if (process.IsInvalid)
                    {
                        failures.Add(string.Format("PID {0}: {1}", target.Id, new Win32Exception(Marshal.GetLastWin32Error()).Message));
                        continue;
                    }
                    long creation, exit, kernel, user;
                    if (!GetProcessTimes(process, out creation, out exit, out kernel, out user))
                    {
                        failures.Add(string.Format("PID {0}: {1}", target.Id, new Win32Exception(Marshal.GetLastWin32Error()).Message));
                        continue;
                    }
                    if (creation != target.CreationTime.Value)
                    {
                        failures.Add(string.Format("PID {0}: process has changed", target.Id));
                        continue;
                    }
                    if (!EmptyWorkingSet(process))
                    {
                        failures.Add(string.Format("PID {0}: {1}", target.Id, new Win32Exception(Marshal.GetLastWin32Error()).Message));
                        continue;
                    }
                    succeeded++;
                }
            }

            string result = string.Format("{0}: requested working-set trimming for {1} of {2} processes.\n" +
                "Windows may reclaim pages temporarily; actual memory savings are not guaranteed.",
                application.Name, succeeded, application.Processes.Count);
            if (failures.Count > 0)
            {
                result += "\n\nNot trimmed:\n" + string.Join("\n", failures.Take(8));
                if (failures.Count > 8) result += string.Format("\n... and {0} more.", failures.Count - 8);
            }
            return result;
        }

        private static ulong? GetPrivateWorkingSet(int processId)
        {
            const uint queryLimitedInformation = 0x1000;
            using (SafeProcessHandle process = OpenProcess(queryLimitedInformation, false, processId))
            {
                if (process.IsInvalid) return null;
                ProcessMemoryCountersEx2 counters;
                uint size = (uint)Marshal.SizeOf(typeof(ProcessMemoryCountersEx2));
                if (!GetProcessMemoryInfo(process, out counters, size) || counters.Size < size) return null;
                return counters.PrivateWorkingSetSize.ToUInt64();
            }
        }

        public static MemorySnapshot Capture()
        {
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
                        int id = process.Id;
                        application.Processes.Add(new ProcessTarget { Id = id, CreationTime = GetCreationTime(id) });
                    }
                    catch (Win32Exception) { inaccessible++; }
                    catch (InvalidOperationException) { inaccessible++; }
                    catch (NotSupportedException) { inaccessible++; }
                }
            }

            List<ApplicationMemory> sortedApplications = applications.Values.OrderByDescending(item => item.Bytes).ToList();
            foreach (ApplicationMemory application in sortedApplications)
            {
                ulong estimatedBytes = 0;
                bool estimateAvailable = true;
                foreach (ProcessTarget target in application.Processes)
                {
                    if (application.ExecutablePath == null)
                        application.ExecutablePath = GetExecutablePath(target.Id);
                    ulong? privateWorkingSet = GetPrivateWorkingSet(target.Id);
                    if (privateWorkingSet.HasValue)
                        estimatedBytes += privateWorkingSet.Value;
                    else
                        estimateAvailable = false;
                }
                application.EstimatedReleasableBytes = estimateAvailable ? (ulong?)estimatedBytes : null;
            }

            var status = new MemoryStatus();
            if (!GlobalMemoryStatusEx(status))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (status.TotalPhysical == 0 || status.AvailablePhysical > status.TotalPhysical)
                throw new InvalidOperationException("Physical memory information is invalid.");

            return new MemorySnapshot
            {
                TotalBytes = status.TotalPhysical,
                UsedBytes = status.TotalPhysical - status.AvailablePhysical,
                LoadPercent = (int)status.MemoryLoad,
                InaccessibleProcesses = inaccessible,
                Applications = sortedApplications
            };
        }
    }
}
