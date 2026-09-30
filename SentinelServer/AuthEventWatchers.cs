using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using System.Xml;

namespace SentinelServer;

public class WindowsSecurityEventLogWatcher : IAuthEventWatcher
{
    private readonly DetectionEngine _engine;
    private EventLogWatcher? _watcher;

    public WindowsSecurityEventLogWatcher(DetectionEngine engine)
    {
        _engine = engine;
    }

    public void Start()
    {
        try
        {
            var query = new EventLogQuery("Security", PathType.LogName, "*[System[(EventID=4625 or EventID=4624)]]");
            _watcher = new EventLogWatcher(query);
            _watcher.EventRecordWritten += OnEvent;
            _watcher.Enabled = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowsSecurityEventLogWatcher] failed to start: {ex.Message}. Run as Administrator to enable Security Event Log monitoring.");
        }
    }

    private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord == null) return;
        try
        {
            var xml = e.EventRecord.ToXml();
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");

            string GetData(string name)
            {
                var node = doc.SelectSingleNode($"//e:Data[@Name='{name}']", ns);
                return node?.InnerText ?? "";
            }

            var eventId = e.EventRecord.Id;
            var user = GetData("TargetUserName");
            var ip = GetData("IpAddress");
            var normalizedIp = string.IsNullOrWhiteSpace(ip) || ip == "-" ? "unknown" : ip;

            if (eventId == 4625)
                _engine.HandleAuthEvent("login_failed", user, normalizedIp);
            else if (eventId == 4624)
                _engine.HandleAuthEvent("login_success", user, normalizedIp);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowsSecurityEventLogWatcher] parse error: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.Enabled = false;
            _watcher.Dispose();
        }
    }
}

public class LinuxAuthLogWatcher : IAuthEventWatcher
{
    private readonly DetectionEngine _engine;
    private Process? _process;

    private static readonly Regex InvalidUserFailed = new(@"Failed password for invalid user\s+(\S+)\s+from\s+(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Compiled);
    private static readonly Regex FailedPassword = new(@"Failed password.*?from\s+(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Compiled);
    private static readonly Regex FailedPublicKey = new(@"Failed publickey.*?from\s+(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Compiled);
    private static readonly Regex Accepted = new(@"Accepted (?:password|publickey).*?for\s+(\S+)\s+from\s+(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Compiled);

    public LinuxAuthLogWatcher(DetectionEngine engine)
    {
        _engine = engine;
    }

    public void Start()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "journalctl",
                Arguments = "-f -n 0 -o cat -u ssh -u sshd",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _process = Process.Start(psi);
            if (_process == null)
            {
                Console.WriteLine("[LinuxAuthLogWatcher] could not start journalctl - is systemd-journald installed?");
                return;
            }
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) HandleLine(e.Data); };
            _process.BeginOutputReadLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinuxAuthLogWatcher] failed to start: {ex.Message}");
        }
    }

    private void HandleLine(string line)
    {
        var invalidUser = InvalidUserFailed.Match(line);
        if (invalidUser.Success)
        {
            _engine.HandleAuthEvent("login_failed", invalidUser.Groups[1].Value, invalidUser.Groups[2].Value);
            return;
        }

        var failedPw = FailedPassword.Match(line);
        if (failedPw.Success)
        {
            _engine.HandleAuthEvent("login_failed", null, failedPw.Groups[1].Value);
            return;
        }

        var failedKey = FailedPublicKey.Match(line);
        if (failedKey.Success)
        {
            _engine.HandleAuthEvent("login_failed", null, failedKey.Groups[1].Value);
            return;
        }

        var accepted = Accepted.Match(line);
        if (accepted.Success)
            _engine.HandleAuthEvent("login_success", accepted.Groups[1].Value, accepted.Groups[2].Value);
    }

    public void Stop()
    {
        try { _process?.Kill(); } catch { }
    }
}
