using Sentinel.Shared;
using System.Collections.Concurrent;

namespace SentinelServer;

public class DetectionEngine
{
    private readonly Storage _storage;
    private readonly IFirewallController _firewall;
    private readonly AppConfig _config;
    private readonly SmtpNotifier _notifier;

    private readonly ConcurrentDictionary<string, List<DateTime>> _failedLoginsByIp = new();
    private readonly ConcurrentDictionary<string, List<(DateTime time, int port)>> _connectionsByIp = new();
    private readonly object _lock = new();

    public DetectionEngine(Storage storage, IFirewallController firewall, AppConfig config, SmtpNotifier notifier)
    {
        _storage = storage;
        _firewall = firewall;
        _config = config;
        _notifier = notifier;
    }

    public bool MaintenanceMode => _storage.GetKv("maintenanceMode") == "true";

    public void SetMaintenanceMode(bool enabled) => _storage.SetKv("maintenanceMode", enabled ? "true" : "false");

    private bool IsAllowlisted(string ip) => _storage.GetAllowlist().Contains(ip);

    public void HandleAuthEvent(string action, string? user, string sourceIp)
    {
        _storage.SaveEvent(new SentinelEvent { Category = "authentication", Action = action, User = user, SourceIp = sourceIp });
        if (action == "login_failed") CheckBruteForce(sourceIp);
    }

    public void HandleConnection(string sourceIp, int destPort)
    {
        if (!_config.Detection.PortScan.Enabled) return;
        if (IsAllowlisted(sourceIp)) return;

        lock (_lock)
        {
            var list = _connectionsByIp.GetOrAdd(sourceIp, _ => new List<(DateTime, int)>());
            list.Add((DateTime.UtcNow, destPort));
            var windowStart = DateTime.UtcNow.AddSeconds(-_config.Detection.PortScan.WindowSeconds);
            list.RemoveAll(x => x.Item1 < windowStart);
            var uniquePorts = list.Select(x => x.Item2).Distinct().Count();

            if (uniquePorts >= _config.Detection.PortScan.Threshold)
            {
                RaiseOrUpdateIncident("NETWORK_PORT_SCAN", "high", sourceIp,
                    $"{uniquePorts} unique destination ports contacted within {_config.Detection.PortScan.WindowSeconds}s");
                list.Clear();
            }
        }
    }

    private void CheckBruteForce(string ip)
    {
        if (!_config.Detection.BruteForce.Enabled) return;
        if (IsAllowlisted(ip)) return;

        lock (_lock)
        {
            var list = _failedLoginsByIp.GetOrAdd(ip, _ => new List<DateTime>());
            list.Add(DateTime.UtcNow);
            var windowStart = DateTime.UtcNow.AddSeconds(-_config.Detection.BruteForce.WindowSeconds);
            list.RemoveAll(t => t < windowStart);

            if (list.Count >= _config.Detection.BruteForce.Threshold)
            {
                var incident = RaiseOrUpdateIncident("SSH_RDP_BRUTE_FORCE", "high", ip,
                    $"{list.Count} failed logins within {_config.Detection.BruteForce.WindowSeconds}s");

                if (!_storage.IsBlocked(ip))
                    AutoBlock(ip, incident.Id, "brute force auto-block");

                list.Clear();
            }
        }
    }

    private void AutoBlock(string ip, string incidentId, string reason)
    {
        if (MaintenanceMode)
        {
            _storage.AddTimeline(incidentId, $"Maintenance mode active - auto-block skipped for {ip}");
            _storage.AddAudit("system", "AUTO_BLOCK_SKIPPED_MAINTENANCE", ip, "SKIPPED");
            return;
        }

        if (_config.Response.DryRun)
        {
            _storage.AddTimeline(incidentId, $"DRY RUN - would block {ip}");
            _storage.AddAudit("system", "DRY_RUN_BLOCK_IP", ip, "SIMULATED");
            return;
        }

        var blocked = _firewall.BlockIp(ip);
        _storage.SaveBlockedIp(new BlockedIp
        {
            Ip = ip,
            Reason = reason,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_config.Response.AutoBlockDurationMinutes),
            BlockedBy = "system"
        });
        _storage.AddTimeline(incidentId, blocked ? $"Auto-blocked {ip}" : $"Auto-block FAILED for {ip}");
        _storage.AddAudit("system", "BLOCK_IP", ip, blocked ? "SUCCESS" : "FAILED");
    }

    private Incident RaiseOrUpdateIncident(string rule, string severity, string sourceIp, string details)
    {
        var cooldownStart = DateTime.UtcNow.AddMinutes(-_config.Detection.SuppressionCooldownMinutes);
        var existing = _storage.FindSuppressibleIncident(rule, sourceIp, cooldownStart);

        if (existing != null)
        {
            existing.LastSeen = DateTime.UtcNow;
            existing.Count++;
            existing.Details = details;
            _storage.UpsertIncident(existing);
            _storage.AddTimeline(existing.Id, $"Repeat trigger ({existing.Count} total) - {details}");
            return existing;
        }

        var incident = new Incident
        {
            Rule = rule,
            Title = rule,
            Severity = severity,
            SourceIp = sourceIp,
            Details = details,
            Count = 1
        };
        _storage.UpsertIncident(incident);
        _storage.AddTimeline(incident.Id, $"Incident created - {details}");
        _storage.AddAudit("system", "INCIDENT_CREATED", incident.Id, rule);
        _ = _notifier.NotifyAsync(incident);
        return incident;
    }

    public void ExpireBlocks()
    {
        foreach (var b in _storage.GetExpiredBlocks(DateTime.UtcNow))
        {
            var ok = _firewall.UnblockIp(b.Ip);
            _storage.RemoveBlockedIp(b.Ip);
            _storage.AddAudit("system", "AUTO_UNBLOCK_EXPIRED", b.Ip, ok ? "SUCCESS" : "FAILED");
        }
    }

    public void SimulateBruteForce(string ip)
    {
        var count = _config.Detection.BruteForce.Threshold;
        for (int i = 0; i < count; i++)
            HandleAuthEvent("login_failed", "test-user", ip);
    }

    public void SimulatePortScan(string ip)
    {
        var count = _config.Detection.PortScan.Threshold;
        for (int i = 0; i < count; i++)
            HandleConnection(ip, 1000 + i);
    }
}
