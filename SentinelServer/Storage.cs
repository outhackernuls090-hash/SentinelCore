using Microsoft.Data.Sqlite;
using Sentinel.Shared;
using System.Security.Cryptography;
using System.Text;

namespace SentinelServer;

public class SentinelEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Category { get; set; } = "";
    public string Action { get; set; } = "";
    public string? User { get; set; }
    public string? SourceIp { get; set; }
    public string? Process { get; set; }
    public string? Metadata { get; set; }
}

public class Storage
{
    private readonly string _connectionString;
    private readonly object _auditGate = new();

    public Storage(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        Init();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return c;
    }

    private void Init()
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY, timestamp TEXT, category TEXT, action TEXT,
                user TEXT, sourceIp TEXT, process TEXT, metadata TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp);

            CREATE TABLE IF NOT EXISTS incidents (
                id TEXT PRIMARY KEY, firstSeen TEXT, lastSeen TEXT, severity TEXT,
                state TEXT, rule TEXT, title TEXT, sourceIp TEXT, details TEXT, count INTEGER DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_incidents_state ON incidents(state);
            CREATE INDEX IF NOT EXISTS idx_incidents_rule_ip ON incidents(rule, sourceIp);

            CREATE TABLE IF NOT EXISTS incident_timeline (
                id INTEGER PRIMARY KEY AUTOINCREMENT, incidentId TEXT, timestamp TEXT, text TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_timeline_incident ON incident_timeline(incidentId);

            CREATE TABLE IF NOT EXISTS blocked_ips (
                ip TEXT PRIMARY KEY, blockedAt TEXT, expiresAt TEXT, reason TEXT, blockedBy TEXT
            );

            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp TEXT, actor TEXT, action TEXT,
                target TEXT, result TEXT, hash TEXT, prevHash TEXT
            );

            CREATE TABLE IF NOT EXISTS users (
                name TEXT PRIMARY KEY, role TEXT, apiKeyHash TEXT UNIQUE, created TEXT
            );

            CREATE TABLE IF NOT EXISTS allowlist (
                entry TEXT PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS kv (
                key TEXT PRIMARY KEY, value TEXT
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public void SaveEvent(SentinelEvent e)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO events (id,timestamp,category,action,user,sourceIp,process,metadata) VALUES (@id,@ts,@cat,@act,@user,@ip,@proc,@meta)";
        cmd.Parameters.AddWithValue("@id", e.Id);
        cmd.Parameters.AddWithValue("@ts", e.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@cat", e.Category);
        cmd.Parameters.AddWithValue("@act", e.Action);
        cmd.Parameters.AddWithValue("@user", (object?)e.User ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", (object?)e.SourceIp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@proc", (object?)e.Process ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@meta", (object?)e.Metadata ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public int PurgeOldEvents(DateTime cutoffUtc)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM events WHERE timestamp < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoffUtc.ToString("o"));
        return cmd.ExecuteNonQuery();
    }

    public Incident? FindSuppressibleIncident(string rule, string sourceIp, DateTime cooldownStartUtc)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT id,firstSeen,lastSeen,severity,state,rule,title,sourceIp,details,count FROM incidents
            WHERE rule=@rule AND sourceIp=@ip AND state='open' AND lastSeen >= @cutoff ORDER BY lastSeen DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@rule", rule);
        cmd.Parameters.AddWithValue("@ip", sourceIp);
        cmd.Parameters.AddWithValue("@cutoff", cooldownStartUtc.ToString("o"));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadIncident(r) : null;
    }

    private static Incident ReadIncident(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        FirstSeen = DateTime.Parse(r.GetString(1)),
        LastSeen = DateTime.Parse(r.GetString(2)),
        Severity = r.GetString(3),
        State = r.GetString(4),
        Rule = r.GetString(5),
        Title = r.GetString(6),
        SourceIp = r.IsDBNull(7) ? null : r.GetString(7),
        Details = r.GetString(8),
        Count = r.GetInt32(9)
    };

    public void UpsertIncident(Incident i)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO incidents (id,firstSeen,lastSeen,severity,state,rule,title,sourceIp,details,count)
            VALUES (@id,@fs,@ls,@sev,@st,@rule,@title,@ip,@details,@count)
            ON CONFLICT(id) DO UPDATE SET lastSeen=@ls, state=@st, details=@details, count=@count, severity=@sev";
        cmd.Parameters.AddWithValue("@id", i.Id);
        cmd.Parameters.AddWithValue("@fs", i.FirstSeen.ToString("o"));
        cmd.Parameters.AddWithValue("@ls", i.LastSeen.ToString("o"));
        cmd.Parameters.AddWithValue("@sev", i.Severity);
        cmd.Parameters.AddWithValue("@st", i.State);
        cmd.Parameters.AddWithValue("@rule", i.Rule);
        cmd.Parameters.AddWithValue("@title", i.Title);
        cmd.Parameters.AddWithValue("@ip", (object?)i.SourceIp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@details", i.Details);
        cmd.Parameters.AddWithValue("@count", i.Count);
        cmd.ExecuteNonQuery();
    }

    public void AddTimeline(string incidentId, string text)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO incident_timeline (incidentId,timestamp,text) VALUES (@id,@ts,@text)";
        cmd.Parameters.AddWithValue("@id", incidentId);
        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@text", text);
        cmd.ExecuteNonQuery();
    }

    public List<TimelineEntry> GetTimeline(string incidentId)
    {
        var list = new List<TimelineEntry>();
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT timestamp,text FROM incident_timeline WHERE incidentId=@id ORDER BY id ASC";
        cmd.Parameters.AddWithValue("@id", incidentId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new TimelineEntry { Timestamp = DateTime.Parse(r.GetString(0)), Text = r.GetString(1) });
        return list;
    }

    public List<Incident> GetIncidents(string? state, int limit)
    {
        var list = new List<Incident>();
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = string.IsNullOrEmpty(state)
            ? "SELECT id,firstSeen,lastSeen,severity,state,rule,title,sourceIp,details,count FROM incidents ORDER BY lastSeen DESC LIMIT @limit"
            : "SELECT id,firstSeen,lastSeen,severity,state,rule,title,sourceIp,details,count FROM incidents WHERE state=@state ORDER BY lastSeen DESC LIMIT @limit";
        if (!string.IsNullOrEmpty(state)) cmd.Parameters.AddWithValue("@state", state);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadIncident(r));
        return list;
    }

    public bool SetIncidentState(string id, string state)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE incidents SET state=@st, lastSeen=@ls WHERE id=@id";
        cmd.Parameters.AddWithValue("@st", state);
        cmd.Parameters.AddWithValue("@ls", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int PurgeOldIncidents(DateTime cutoffUtc)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM incidents WHERE lastSeen < @cutoff AND state IN ('resolved','false_positive','ignored')";
        cmd.Parameters.AddWithValue("@cutoff", cutoffUtc.ToString("o"));
        return cmd.ExecuteNonQuery();
    }

    public void SaveBlockedIp(BlockedIp b)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO blocked_ips (ip,blockedAt,expiresAt,reason,blockedBy) VALUES (@ip,@ba,@ea,@r,@by)";
        cmd.Parameters.AddWithValue("@ip", b.Ip);
        cmd.Parameters.AddWithValue("@ba", b.BlockedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ea", (object?)b.ExpiresAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", b.Reason);
        cmd.Parameters.AddWithValue("@by", b.BlockedBy);
        cmd.ExecuteNonQuery();
    }

    public void RemoveBlockedIp(string ip)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM blocked_ips WHERE ip=@ip";
        cmd.Parameters.AddWithValue("@ip", ip);
        cmd.ExecuteNonQuery();
    }

    public bool IsBlocked(string ip)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM blocked_ips WHERE ip=@ip";
        cmd.Parameters.AddWithValue("@ip", ip);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public List<BlockedIp> GetBlockedIps()
    {
        var list = new List<BlockedIp>();
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ip,blockedAt,expiresAt,reason,blockedBy FROM blocked_ips ORDER BY blockedAt DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new BlockedIp
            {
                Ip = r.GetString(0),
                BlockedAt = DateTime.Parse(r.GetString(1)),
                ExpiresAt = r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2)),
                Reason = r.GetString(3),
                BlockedBy = r.IsDBNull(4) ? "" : r.GetString(4)
            });
        }
        return list;
    }

    public List<BlockedIp> GetExpiredBlocks(DateTime nowUtc)
    {
        var list = new List<BlockedIp>();
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ip,blockedAt,expiresAt,reason,blockedBy FROM blocked_ips WHERE expiresAt IS NOT NULL AND expiresAt < @now";
        cmd.Parameters.AddWithValue("@now", nowUtc.ToString("o"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new BlockedIp
            {
                Ip = r.GetString(0),
                BlockedAt = DateTime.Parse(r.GetString(1)),
                ExpiresAt = r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2)),
                Reason = r.GetString(3),
                BlockedBy = r.IsDBNull(4) ? "" : r.GetString(4)
            });
        }
        return list;
    }

    public AuditEntry AddAudit(string actor, string action, string target, string result)
    {
        lock (_auditGate)
        {
            using var c = Open();
            string prevHash = "";
            var last = c.CreateCommand();
            last.CommandText = "SELECT hash FROM audit_log ORDER BY id DESC LIMIT 1";
            var lastResult = last.ExecuteScalar();
            if (lastResult != null) prevHash = (string)lastResult;

            var ts = DateTime.UtcNow;
            var hash = ComputeAuditHash(prevHash, ts, actor, action, target, result);

            var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO audit_log (timestamp,actor,action,target,result,hash,prevHash)
                VALUES (@ts,@actor,@action,@target,@result,@hash,@prev);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@ts", ts.ToString("o"));
            cmd.Parameters.AddWithValue("@actor", actor);
            cmd.Parameters.AddWithValue("@action", action);
            cmd.Parameters.AddWithValue("@target", target);
            cmd.Parameters.AddWithValue("@result", result);
            cmd.Parameters.AddWithValue("@hash", hash);
            cmd.Parameters.AddWithValue("@prev", prevHash);
            var id = (long)cmd.ExecuteScalar()!;

            return new AuditEntry { Id = id, Timestamp = ts, Actor = actor, Action = action, Target = target, Result = result, Hash = hash };
        }
    }

    private static string ComputeAuditHash(string prevHash, DateTime ts, string actor, string action, string target, string result)
    {
        var payload = $"{prevHash}|{ts:o}|{actor}|{action}|{target}|{result}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public List<AuditEntry> GetAudit(int limit)
    {
        var list = new List<AuditEntry>();
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,timestamp,actor,action,target,result,hash FROM audit_log ORDER BY id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AuditEntry
            {
                Id = r.GetInt64(0),
                Timestamp = DateTime.Parse(r.GetString(1)),
                Actor = r.GetString(2),
                Action = r.GetString(3),
                Target = r.GetString(4),
                Result = r.GetString(5),
                Hash = r.GetString(6)
            });
        }
        return list;
    }

    public AuditVerifyResult VerifyAuditChain()
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,timestamp,actor,action,target,result,hash,prevHash FROM audit_log ORDER BY id ASC";
        using var r = cmd.ExecuteReader();
        string expectedPrev = "";
        int count = 0;
        while (r.Read())
        {
            count++;
            var id = r.GetInt64(0);
            var ts = DateTime.Parse(r.GetString(1));
            var actor = r.GetString(2);
            var action = r.GetString(3);
            var target = r.GetString(4);
            var result = r.GetString(5);
            var hash = r.GetString(6);
            var prevHash = r.GetString(7);

            if (prevHash != expectedPrev)
                return new AuditVerifyResult { Valid = false, Entries = count, BrokenAtId = id, Message = $"prevHash mismatch at audit id {id}" };

            var recomputed = ComputeAuditHash(prevHash, ts, actor, action, target, result);
            if (recomputed != hash)
                return new AuditVerifyResult { Valid = false, Entries = count, BrokenAtId = id, Message = $"hash mismatch at audit id {id} - entry may have been tampered with" };

            expectedPrev = hash;
        }
        return new AuditVerifyResult { Valid = true, Entries = count, Message = count == 0 ? "no audit entries yet" : "chain intact" };
    }

    public int PurgeOldAudit(DateTime cutoffUtc)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM audit_log WHERE timestamp < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoffUtc.ToString("o"));
        return cmd.ExecuteNonQuery();
    }

    public void CreateUser(string name, string role, string apiKeyHash)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO users (name,role,apiKeyHash,created) VALUES (@n,@r,@h,@c)";
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@r", role);
        cmd.Parameters.AddWithValue("@h", apiKeyHash);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public bool DeleteUser(string name)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE name=@n";
        cmd.Parameters.AddWithValue("@n", name);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int UserCount()
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public (string name, string role)? FindUserByApiKeyHash(string hash)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name,role FROM users WHERE apiKeyHash=@h";
        cmd.Parameters.AddWithValue("@h", hash);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1)) : null;
    }

    public List<UserInfo> GetUsers()
    {
        var list = new List<UserInfo>();
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name,role,created FROM users ORDER BY created ASC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UserInfo { Name = r.GetString(0), Role = r.GetString(1), Created = DateTime.Parse(r.GetString(2)) });
        return list;
    }

    public void AddAllowlistEntry(string entry)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO allowlist (entry) VALUES (@e)";
        cmd.Parameters.AddWithValue("@e", entry);
        cmd.ExecuteNonQuery();
    }

    public void RemoveAllowlistEntry(string entry)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM allowlist WHERE entry=@e";
        cmd.Parameters.AddWithValue("@e", entry);
        cmd.ExecuteNonQuery();
    }

    public List<string> GetAllowlist()
    {
        var list = new List<string>();
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT entry FROM allowlist";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public string? GetKv(string key)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM kv WHERE key=@k";
        cmd.Parameters.AddWithValue("@k", key);
        var v = cmd.ExecuteScalar();
        return v as string;
    }

    public void SetKv(string key, string value)
    {
        using var c = Open();
        var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO kv (key,value) VALUES (@k,@v) ON CONFLICT(key) DO UPDATE SET value=@v";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    public void SeedFakeData()
    {
        if (GetIncidents(null, 1).Count > 0) return;

        var i1 = new Incident
        {
            Id = Guid.NewGuid().ToString(),
            FirstSeen = DateTime.UtcNow.AddMinutes(-5),
            LastSeen = DateTime.UtcNow,
            Rule = "SSH_RDP_BRUTE_FORCE",
            Title = "SSH_RDP_BRUTE_FORCE",
            Severity = "high",
            State = "open",
            SourceIp = "203.0.113.77",
            Details = "14 failed logins within 60s (sample data)",
            Count = 14
        };
        UpsertIncident(i1);
        AddTimeline(i1.Id, "Incident created (sample data)");

        var i2 = new Incident
        {
            Id = Guid.NewGuid().ToString(),
            FirstSeen = DateTime.UtcNow.AddHours(-2),
            LastSeen = DateTime.UtcNow.AddHours(-1).AddMinutes(-50),
            Rule = "NETWORK_PORT_SCAN",
            Title = "NETWORK_PORT_SCAN",
            Severity = "medium",
            State = "resolved",
            SourceIp = "198.51.100.23",
            Details = "27 unique ports within 5s (sample data)",
            Count = 27
        };
        UpsertIncident(i2);
        AddTimeline(i2.Id, "Incident created (sample data)");
        AddTimeline(i2.Id, "Marked resolved (sample data)");

        SaveBlockedIp(new BlockedIp { Ip = "203.0.113.77", Reason = "sample data - auto-block on brute force", ExpiresAt = DateTime.UtcNow.AddMinutes(30), BlockedBy = "system" });
    }
}
