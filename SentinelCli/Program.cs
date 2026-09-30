using Sentinel.Shared;
using System.Text.Json;

namespace SentinelCli;

public class CliConfig
{
    public string Host { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public static class Program
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sentinel");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "cli.json");
    private static readonly string PinsPath = Path.Combine(ConfigDir, "pins.json");

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0];
        var rest = args.Skip(1).ToArray();

        if (command == "connect")
            return await ConnectAsync(rest);

        var cfg = LoadConfig();
        if (string.IsNullOrEmpty(cfg.Host) || string.IsNullOrEmpty(cfg.ApiKey))
        {
            Console.Error.WriteLine("Not configured. Run: sentinel connect --host https://<agent>:47152 --key <api-key>");
            return 1;
        }

        var pins = new PinStore(PinsPath);
        var pin = pins.Get(cfg.Host);
        using var api = new SentinelApi(cfg.Host, cfg.ApiKey, pin);

        try
        {
            return await DispatchAsync(api, command, rest);
        }
        catch (ApiException ex)
        {
            Console.Error.WriteLine($"Error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> DispatchAsync(SentinelApi api, string command, string[] a)
    {
        switch (command)
        {
            case "status":
                {
                    var s = await api.GetStatusAsync();
                    Console.WriteLine($"{s.HostName} ({s.Platform}) v{s.Version} - role {s.Role}");
                    Console.WriteLine($"Open incidents: {s.ActiveIncidents}   Blocked IPs: {s.BlockedIps}");
                    Console.WriteLine($"Maintenance mode: {(s.MaintenanceMode ? "ON" : "off")}   Dry run: {(s.DryRun ? "ON" : "off")}   Test mode: {(s.TestMode ? "ON" : "off")}");
                    Console.WriteLine($"Uptime: {TimeSpan.FromSeconds(s.UptimeSeconds)}");
                    return 0;
                }
            case "incidents":
                {
                    var state = FlagValue(a, "--state");
                    var incidents = await api.GetIncidentsAsync(state);
                    if (incidents.Count == 0) { Console.WriteLine("No incidents."); return 0; }
                    foreach (var i in incidents)
                        Console.WriteLine($"{i.Id.Substring(0, 8)}  {i.LastSeen:u}  {i.Severity,-8} {i.State,-12} {i.Rule,-24} {i.SourceIp,-16} x{i.Count}  {i.Details}");
                    return 0;
                }
            case "timeline":
                {
                    if (a.Length < 1) { Console.Error.WriteLine("Usage: sentinel timeline <incidentId>"); return 1; }
                    var tl = await api.GetTimelineAsync(a[0]);
                    foreach (var t in tl) Console.WriteLine($"{t.Timestamp:u}  {t.Text}");
                    return 0;
                }
            case "ack":
            case "acknowledge":
                return await SetState(api, a, "investigating");
            case "resolve":
                return await SetState(api, a, "resolved");
            case "ignore":
                return await SetState(api, a, "ignored");
            case "false-positive":
                return await SetState(api, a, "false_positive");
            case "processes":
                {
                    var procs = await api.GetProcessesAsync();
                    foreach (var p in procs.OrderBy(p => p.Name))
                        Console.WriteLine($"{p.Pid,-8} {p.Name,-24} {(p.Flag != null ? "[" + p.Flag + "] " : "")}{p.Path}");
                    return 0;
                }
            case "network":
                {
                    var conns = await api.GetNetworkAsync();
                    foreach (var c in conns)
                        Console.WriteLine($"{c.LocalAddress}:{c.LocalPort,-6} -> {c.RemoteAddress}:{c.RemotePort,-6} {c.State}");
                    return 0;
                }
            case "blocked":
                {
                    var blocked = await api.GetBlockedIpsAsync();
                    foreach (var b in blocked)
                        Console.WriteLine($"{b.Ip,-16} blocked {b.BlockedAt:u} by {b.BlockedBy}  expires {(b.ExpiresAt?.ToString("u") ?? "never")}  {b.Reason}");
                    return 0;
                }
            case "block-ip":
                {
                    if (a.Length < 1) { Console.Error.WriteLine("Usage: sentinel block-ip <ip> [--minutes N] [--reason text]"); return 1; }
                    var minutes = int.TryParse(FlagValue(a, "--minutes"), out var m) ? (int?)m : null;
                    var reason = FlagValue(a, "--reason") ?? "manual (cli)";
                    var res = await api.BlockIpAsync(a[0], reason, minutes);
                    Console.WriteLine(res.Success ? $"Blocked {a[0]}" : $"Failed: {res.Message}");
                    return res.Success ? 0 : 1;
                }
            case "unblock-ip":
                {
                    if (a.Length < 1) { Console.Error.WriteLine("Usage: sentinel unblock-ip <ip>"); return 1; }
                    var res = await api.UnblockIpAsync(a[0]);
                    Console.WriteLine(res.Success ? $"Unblocked {a[0]}" : $"Failed: {res.Message}");
                    return res.Success ? 0 : 1;
                }
            case "suspend":
                return await ProcAction(api, a, api.SuspendProcessAsync, "Suspended");
            case "resume":
                return await ProcAction(api, a, api.ResumeProcessAsync, "Resumed");
            case "terminate":
                return await ProcAction(api, a, api.TerminateProcessAsync, "Terminated");
            case "audit":
                {
                    var limit = int.TryParse(FlagValue(a, "--limit"), out var l) ? l : 100;
                    var entries = await api.GetAuditAsync(limit);
                    foreach (var e in entries)
                        Console.WriteLine($"#{e.Id,-6} {e.Timestamp:u}  {e.Actor,-12} {e.Action,-24} {e.Target,-24} {e.Result}");
                    return 0;
                }
            case "verify-audit":
                {
                    var v = await api.VerifyAuditAsync();
                    Console.WriteLine(v.Valid ? $"OK - {v.Entries} audit entries, chain intact" : $"TAMPERED - {v.Message}");
                    return v.Valid ? 0 : 2;
                }
            case "allowlist":
                return await AllowlistCommand(api, a);
            case "maintenance":
                {
                    if (a.Length < 1 || a[0] is not ("on" or "off")) { Console.Error.WriteLine("Usage: sentinel maintenance on|off"); return 1; }
                    var res = await api.SetMaintenanceAsync(a[0] == "on");
                    Console.WriteLine(res.Success ? $"Maintenance mode {a[0]}" : "Failed");
                    return 0;
                }
            case "users":
                return await UsersCommand(api, a);
            case "simulate":
                {
                    if (a.Length < 2) { Console.Error.WriteLine("Usage: sentinel simulate bruteforce|portscan <ip>"); return 1; }
                    var res = await api.SimulateAsync(a[0], a[1]);
                    Console.WriteLine(res.Success ? $"Simulated {a[0]} from {a[1]}" : $"Failed: {res.Message}");
                    return 0;
                }
            case "test-email":
                {
                    var res = await api.TestEmailAsync();
                    Console.WriteLine(res.Success ? "Test email sent" : $"Failed: {res.Message}");
                    return res.Success ? 0 : 1;
                }
            case "report":
                {
                    var csv = await api.GetReportCsvAsync();
                    var outPath = FlagValue(a, "--out");
                    if (outPath != null) { await File.WriteAllTextAsync(outPath, csv); Console.WriteLine($"Written to {outPath}"); }
                    else Console.WriteLine(csv);
                    return 0;
                }
            default:
                Console.Error.WriteLine($"Unknown command '{command}'. Run 'sentinel help'.");
                return 1;
        }
    }

    private static async Task<int> SetState(SentinelApi api, string[] a, string state)
    {
        if (a.Length < 1) { Console.Error.WriteLine("Usage: sentinel <ack|resolve|ignore|false-positive> <incidentId>"); return 1; }
        var res = await api.SetIncidentStateAsync(a[0], state);
        Console.WriteLine(res.Success ? $"Incident {a[0]} -> {state}" : "Failed - incident not found?");
        return res.Success ? 0 : 1;
    }

    private static async Task<int> ProcAction(SentinelApi api, string[] a, Func<int, Task<ActionResult>> action, string verb)
    {
        if (a.Length < 1 || !int.TryParse(a[0], out var pid)) { Console.Error.WriteLine("Usage: sentinel <suspend|resume|terminate> <pid>"); return 1; }
        var res = await action(pid);
        Console.WriteLine(res.Success ? $"{verb} PID {pid}" : $"Failed for PID {pid}");
        return res.Success ? 0 : 1;
    }

    private static async Task<int> AllowlistCommand(SentinelApi api, string[] a)
    {
        if (a.Length == 0 || a[0] == "list")
        {
            var list = await api.GetAllowlistAsync();
            foreach (var e in list.Entries) Console.WriteLine(e);
            return 0;
        }
        if (a[0] == "add" && a.Length > 1)
        {
            var res = await api.ChangeAllowlistAsync(a[1], false);
            Console.WriteLine(res.Success ? $"Added {a[1]}" : "Failed");
            return 0;
        }
        if (a[0] == "remove" && a.Length > 1)
        {
            var res = await api.ChangeAllowlistAsync(a[1], true);
            Console.WriteLine(res.Success ? $"Removed {a[1]}" : "Failed");
            return 0;
        }
        Console.Error.WriteLine("Usage: sentinel allowlist [list|add <entry>|remove <entry>]");
        return 1;
    }

    private static async Task<int> UsersCommand(SentinelApi api, string[] a)
    {
        if (a.Length == 0 || a[0] == "list")
        {
            var users = await api.GetUsersAsync();
            foreach (var u in users) Console.WriteLine($"{u.Name,-16} {u.Role,-14} created {u.Created:u}");
            return 0;
        }
        if (a[0] == "create" && a.Length > 2)
        {
            var res = await api.CreateUserAsync(a[1], a[2]);
            Console.WriteLine($"Created {res.Name} ({res.Role})");
            Console.WriteLine($"API key: {res.ApiKey}");
            Console.WriteLine("Save this now - it will not be shown again.");
            return 0;
        }
        if (a[0] == "revoke" && a.Length > 1)
        {
            var res = await api.RevokeUserAsync(a[1]);
            Console.WriteLine(res.Success ? $"Revoked {a[1]}" : "Failed");
            return 0;
        }
        Console.Error.WriteLine("Usage: sentinel users [list|create <name> <role>|revoke <name>]");
        return 1;
    }

    private static async Task<int> ConnectAsync(string[] a)
    {
        var host = FlagValue(a, "--host");
        var key = FlagValue(a, "--key");
        var yes = a.Contains("--yes");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(key))
        {
            Console.Error.WriteLine("Usage: sentinel connect --host https://<agent>:47152 --key <api-key> [--yes]");
            return 1;
        }

        var (health, fingerprint, error) = await SentinelApi.ProbeAsync(host);
        if (error != null)
        {
            Console.Error.WriteLine($"Could not reach {host}: {error}");
            return 1;
        }

        if (fingerprint != null)
        {
            Console.WriteLine($"TLS certificate fingerprint: {PinStore.Format(fingerprint)}");
            if (!yes)
            {
                Console.Write("Trust this certificate and continue? [y/N] ");
                var answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Aborted.");
                    return 1;
                }
            }
            new PinStore(PinsPath).Set(host, fingerprint);
        }

        SaveConfig(new CliConfig { Host = host, ApiKey = key });
        Console.WriteLine($"Connected to {host} (test mode: {health?.TestMode})");
        Console.WriteLine("Configuration saved to " + ConfigPath);
        return 0;
    }

    private static string? FlagValue(string[] a, string flag)
    {
        var idx = Array.IndexOf(a, flag);
        return idx >= 0 && idx + 1 < a.Length ? a[idx + 1] : null;
    }

    private static CliConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<CliConfig>(File.ReadAllText(ConfigPath)) ?? new CliConfig();
        }
        catch
        {
        }
        return new CliConfig();
    }

    private static void SaveConfig(CliConfig cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("SentinelCore CLI v" + Product.Version);
        Console.WriteLine();
        Console.WriteLine("  sentinel connect --host https://<agent>:47152 --key <api-key>");
        Console.WriteLine("  sentinel status");
        Console.WriteLine("  sentinel incidents [--state open]");
        Console.WriteLine("  sentinel timeline <incidentId>");
        Console.WriteLine("  sentinel ack|resolve|ignore|false-positive <incidentId>");
        Console.WriteLine("  sentinel processes");
        Console.WriteLine("  sentinel network");
        Console.WriteLine("  sentinel blocked");
        Console.WriteLine("  sentinel block-ip <ip> [--minutes N] [--reason text]");
        Console.WriteLine("  sentinel unblock-ip <ip>");
        Console.WriteLine("  sentinel suspend|resume|terminate <pid>");
        Console.WriteLine("  sentinel audit [--limit N]");
        Console.WriteLine("  sentinel verify-audit");
        Console.WriteLine("  sentinel allowlist [list|add <entry>|remove <entry>]");
        Console.WriteLine("  sentinel maintenance on|off");
        Console.WriteLine("  sentinel users [list|create <name> <role>|revoke <name>]");
        Console.WriteLine("  sentinel simulate bruteforce|portscan <ip>");
        Console.WriteLine("  sentinel test-email");
        Console.WriteLine("  sentinel report [--out file.csv]");
    }
}
