using SentinelServer;
using System;
using System.IO;
using Xunit;

namespace SentinelServer.Tests;

public class FakeFirewall : IFirewallController
{
    public int BlockCalls;
    public int UnblockCalls;
    public string? LastBlockedIp;

    public void Init()
    {
    }

    public bool BlockIp(string ip)
    {
        BlockCalls++;
        LastBlockedIp = ip;
        return true;
    }

    public bool UnblockIp(string ip)
    {
        UnblockCalls++;
        return true;
    }
}

public class DetectionEngineTests
{
    private static (Storage storage, DetectionEngine engine, FakeFirewall firewall) NewEngine(int bruteForceThreshold = 5)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentinel-test-{Guid.NewGuid():N}.db");
        var storage = new Storage(dbPath);
        var config = new AppConfig();
        config.Detection.BruteForce.Threshold = bruteForceThreshold;
        config.Detection.BruteForce.WindowSeconds = 60;
        config.Detection.SuppressionCooldownMinutes = 15;
        config.Smtp.Enabled = false;
        var firewall = new FakeFirewall();
        var notifier = new SmtpNotifier(config, storage);
        var engine = new DetectionEngine(storage, firewall, config, notifier);
        return (storage, engine, firewall);
    }

    [Fact]
    public void BruteForce_BelowThreshold_NoIncident()
    {
        var (storage, engine, firewall) = NewEngine(bruteForceThreshold: 5);
        for (int i = 0; i < 4; i++)
            engine.HandleAuthEvent("login_failed", "bob", "198.51.100.10");

        var incidents = storage.GetIncidents(null, 10);
        Assert.Empty(incidents);
        Assert.Equal(0, firewall.BlockCalls);
    }

    [Fact]
    public void BruteForce_AtThreshold_CreatesIncidentAndBlocks()
    {
        var (storage, engine, firewall) = NewEngine(bruteForceThreshold: 5);
        for (int i = 0; i < 5; i++)
            engine.HandleAuthEvent("login_failed", "bob", "198.51.100.20");

        var incidents = storage.GetIncidents(null, 10);
        Assert.Single(incidents);
        Assert.Equal("SSH_RDP_BRUTE_FORCE", incidents[0].Rule);
        Assert.Equal(1, firewall.BlockCalls);
        Assert.Equal("198.51.100.20", firewall.LastBlockedIp);
        Assert.True(storage.IsBlocked("198.51.100.20"));
    }

    [Fact]
    public void BruteForce_AllowlistedIp_NeverBlocked()
    {
        var (storage, engine, firewall) = NewEngine(bruteForceThreshold: 3);
        storage.AddAllowlistEntry("198.51.100.30");

        for (int i = 0; i < 10; i++)
            engine.HandleAuthEvent("login_failed", "bob", "198.51.100.30");

        Assert.Empty(storage.GetIncidents(null, 10));
        Assert.Equal(0, firewall.BlockCalls);
    }

    [Fact]
    public void MaintenanceMode_SkipsAutoBlockButStillLogsIncident()
    {
        var (storage, engine, firewall) = NewEngine(bruteForceThreshold: 3);
        engine.SetMaintenanceMode(true);

        for (int i = 0; i < 3; i++)
            engine.HandleAuthEvent("login_failed", "bob", "198.51.100.40");

        var incidents = storage.GetIncidents(null, 10);
        Assert.Single(incidents);
        Assert.Equal(0, firewall.BlockCalls);
        Assert.False(storage.IsBlocked("198.51.100.40"));
    }

    [Fact]
    public void RepeatedTriggersWithinCooldown_AreSuppressedIntoSameIncident()
    {
        var (storage, engine, _) = NewEngine(bruteForceThreshold: 3);

        for (int i = 0; i < 3; i++) engine.HandleAuthEvent("login_failed", "bob", "198.51.100.50");
        for (int i = 0; i < 3; i++) engine.HandleAuthEvent("login_failed", "bob", "198.51.100.50");

        var incidents = storage.GetIncidents(null, 10);
        Assert.Single(incidents);
        Assert.Equal(2, incidents[0].Count);
    }
}

public class AuditChainTests
{
    private static Storage NewStorage()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentinel-audit-test-{Guid.NewGuid():N}.db");
        return new Storage(dbPath);
    }

    [Fact]
    public void EmptyChain_IsValid()
    {
        var storage = NewStorage();
        var result = storage.VerifyAuditChain();
        Assert.True(result.Valid);
        Assert.Equal(0, result.Entries);
    }

    [Fact]
    public void ChainOfEntries_VerifiesIntact()
    {
        var storage = NewStorage();
        storage.AddAudit("alice", "BLOCK_IP", "1.2.3.4", "SUCCESS");
        storage.AddAudit("bob", "UNBLOCK_IP", "1.2.3.4", "SUCCESS");
        storage.AddAudit("system", "INCIDENT_CREATED", "abc-123", "SSH_RDP_BRUTE_FORCE");

        var result = storage.VerifyAuditChain();
        Assert.True(result.Valid);
        Assert.Equal(3, result.Entries);
    }

    [Fact]
    public void TamperedEntry_IsDetected()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sentinel-tamper-test-{Guid.NewGuid():N}.db");
        var storage = new Storage(dbPath);
        storage.AddAudit("alice", "BLOCK_IP", "1.2.3.4", "SUCCESS");
        storage.AddAudit("bob", "UNBLOCK_IP", "1.2.3.4", "SUCCESS");

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE audit_log SET target='9.9.9.9' WHERE id=1";
            cmd.ExecuteNonQuery();
        }

        var result = storage.VerifyAuditChain();
        Assert.False(result.Valid);
        Assert.Equal(1, result.BrokenAtId);
    }
}
