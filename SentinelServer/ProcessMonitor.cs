using Sentinel.Shared;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SentinelServer;

public static class ProcessMonitor
{
    private static readonly string[] SuspiciousParents = { "winword", "excel", "outlook", "powerpnt", "chrome", "msedge", "firefox" };
    private static readonly string[] ScriptEngines = { "powershell", "pwsh", "cmd", "wscript", "cscript", "bash", "sh", "python", "python3" };

    public static List<ProcessInfo> GetProcesses()
    {
        var parentMap = OperatingSystem.IsWindows() ? WindowsParentMap() : LinuxParentMap();
        var list = new List<ProcessInfo>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                var path = SafeGetPath(p);
                var parentPid = parentMap.TryGetValue(p.Id, out var pp) ? pp : 0;
                var parentName = parentMap.TryGetValue(p.Id, out _) && ProcessNameOrNull(parentPid) is string pn ? pn : null;

                list.Add(new ProcessInfo
                {
                    Pid = p.Id,
                    ParentPid = parentPid,
                    Name = name,
                    Path = path,
                    StartTime = SafeGetStartTime(p),
                    Flag = ComputeFlag(name, path, parentName)
                });
            }
            catch
            {
            }
        }
        return list;
    }

    private static string? ProcessNameOrNull(int pid)
    {
        if (pid <= 0) return null;
        try { return Process.GetProcessById(pid).ProcessName; } catch { return null; }
    }

    private static string? ComputeFlag(string name, string? path, string? parentName)
    {
        var lowerName = name.ToLowerInvariant();

        if (parentName != null)
        {
            var lowerParent = parentName.ToLowerInvariant();
            if (Array.Exists(ScriptEngines, e => lowerName.Contains(e)) && Array.Exists(SuspiciousParents, s => lowerParent.Contains(s)))
                return $"Suspicious chain: {parentName} -> {name}";
        }

        if (path != null)
        {
            var lowerPath = path.ToLowerInvariant();
            var isTemp = lowerPath.Contains("\\temp\\") || lowerPath.Contains("/tmp/") || lowerPath.Contains("/var/tmp/") || lowerPath.Contains("\\appdata\\local\\temp");
            if (isTemp) return "Running from a temp directory";
        }

        return null;
    }

    private static string? SafeGetPath(Process p)
    {
        try { return p.MainModule?.FileName; } catch { return null; }
    }

    private static DateTime? SafeGetStartTime(Process p)
    {
        try { return p.StartTime; } catch { return null; }
    }

    private static Dictionary<int, int> LinuxParentMap()
    {
        var map = new Dictionary<int, int>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                var name = Path.GetFileName(dir);
                if (!int.TryParse(name, out var pid)) continue;
                try
                {
                    var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                    var close = stat.LastIndexOf(')');
                    if (close < 0) continue;
                    var rest = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (rest.Length > 1 && int.TryParse(rest[1], out var ppid))
                        map[pid] = ppid;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return map;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int priClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private static Dictionary<int, int> WindowsParentMap()
    {
        var map = new Dictionary<int, int>();
        const uint TH32CS_SNAPPROCESS = 0x00000002;
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap.ToInt64() == -1) return map;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snap, ref entry)) return map;
            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            } while (Process32Next(snap, ref entry));
        }
        finally
        {
            CloseHandle(snap);
        }
        return map;
    }
}
