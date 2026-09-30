using System.Diagnostics;

namespace SentinelServer;

public static class Shell
{
    public static bool Run(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static string RunCapture(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch
        {
            return "";
        }
    }
}

public class WindowsFirewallController : IFirewallController
{
    public void Init()
    {
    }

    public bool BlockIp(string ip)
    {
        var ruleName = RuleName(ip);
        return Shell.Run("netsh", $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=block remoteip={ip}")
             & Shell.Run("netsh", $"advfirewall firewall add rule name=\"{ruleName}_OUT\" dir=out action=block remoteip={ip}");
    }

    public bool UnblockIp(string ip)
    {
        var ruleName = RuleName(ip);
        return Shell.Run("netsh", $"advfirewall firewall delete rule name=\"{ruleName}\"")
             & Shell.Run("netsh", $"advfirewall firewall delete rule name=\"{ruleName}_OUT\"");
    }

    private static string RuleName(string ip) => $"SENTINEL_BLOCK_{ip.Replace('.', '_').Replace(':', '_')}";
}

public class LinuxFirewallController : IFirewallController
{
    private const string Table = "inet sentinel";
    private const string SetName = "sentinel_blocked";

    public void Init()
    {
        Shell.Run("nft", $"add table {Table}");
        Shell.Run("nft", $"add set {Table} {SetName} {{ type ipv4_addr; }}");
        Shell.Run("nft", $"add chain {Table} input {{ type filter hook input priority 0; }}");
        Shell.Run("nft", $"add rule {Table} input ip saddr @{SetName} drop");
    }

    public bool BlockIp(string ip) => Shell.Run("nft", $"add element {Table} {SetName} {{ {ip} }}");

    public bool UnblockIp(string ip) => Shell.Run("nft", $"delete element {Table} {SetName} {{ {ip} }}");
}
