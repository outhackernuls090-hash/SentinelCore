using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SentinelServer;

public class WindowsProcessController : IProcessController
{
    private const int PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr handle);

    public bool SuspendProcess(int pid)
    {
        var handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (handle == IntPtr.Zero) return false;
        try { return NtSuspendProcess(handle) == 0; }
        finally { CloseHandle(handle); }
    }

    public bool ResumeProcess(int pid)
    {
        var handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (handle == IntPtr.Zero) return false;
        try { return NtResumeProcess(handle) == 0; }
        finally { CloseHandle(handle); }
    }

    public bool TerminateProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class LinuxProcessController : IProcessController
{
    public bool SuspendProcess(int pid) => Shell.Run("kill", $"-STOP {pid}");
    public bool ResumeProcess(int pid) => Shell.Run("kill", $"-CONT {pid}");
    public bool TerminateProcess(int pid) => Shell.Run("kill", $"-9 {pid}");
}
