namespace Sentinel.Shared;

public static class Product
{
    public const string Name = "SentinelCore";
    public const string Version = "1.0.0";
}

public enum Role
{
    Viewer = 1,
    Analyst = 2,
    Administrator = 3,
    Owner = 4
}

public static class SeverityLevel
{
    public static int Rank(string? s) => (s ?? "").ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };
}

public class Incident
{
    public string Id { get; set; } = "";
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public string Severity { get; set; } = "medium";
    public string State { get; set; } = "open";
    public string Rule { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SourceIp { get; set; }
    public string Details { get; set; } = "";
    public int Count { get; set; } = 1;
}

public class TimelineEntry
{
    public DateTime Timestamp { get; set; }
    public string Text { get; set; } = "";
}

public class BlockedIp
{
    public string Ip { get; set; } = "";
    public DateTime BlockedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Reason { get; set; } = "";
    public string BlockedBy { get; set; } = "";
}

public class AuditEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Result { get; set; } = "";
    public string Hash { get; set; } = "";
}

public class AuditVerifyResult
{
    public bool Valid { get; set; }
    public int Entries { get; set; }
    public long? BrokenAtId { get; set; }
    public string Message { get; set; } = "";
}

public class ProcessInfo
{
    public int Pid { get; set; }
    public int ParentPid { get; set; }
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public DateTime? StartTime { get; set; }
    public string? Flag { get; set; }
}

public class NetworkConnectionInfo
{
    public string Protocol { get; set; } = "TCP";
    public string LocalAddress { get; set; } = "";
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = "";
    public int RemotePort { get; set; }
    public string State { get; set; } = "";
    public int Pid { get; set; }
    public string? Process { get; set; }
}

public class HostStatus
{
    public string HostName { get; set; } = "";
    public string Os { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Version { get; set; } = "";
    public string Role { get; set; } = "Viewer";
    public string User { get; set; } = "";
    public DateTime LastHeartbeat { get; set; }
    public int ActiveIncidents { get; set; }
    public int BlockedIps { get; set; }
    public bool MaintenanceMode { get; set; }
    public bool DryRun { get; set; }
    public bool TestMode { get; set; }
    public long UptimeSeconds { get; set; }
}

public class HealthInfo
{
    public string Status { get; set; } = "ok";
    public string Version { get; set; } = "";
    public bool TestMode { get; set; }
    public bool Tls { get; set; }
}

public class ActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class BlockIpRequest
{
    public string Ip { get; set; } = "";
    public string? Reason { get; set; }
    public int? DurationMinutes { get; set; }
}

public class IpRequest
{
    public string Ip { get; set; } = "";
}

public class ProcessActionRequest
{
    public int Pid { get; set; }
}

public class IncidentStateRequest
{
    public string IncidentId { get; set; } = "";
    public string State { get; set; } = "";
}

public class MaintenanceRequest
{
    public bool Enabled { get; set; }
}

public class AllowlistRequest
{
    public string Entry { get; set; } = "";
    public bool Remove { get; set; }
}

public class AllowlistInfo
{
    public List<string> Entries { get; set; } = new();
}

public class UserInfo
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime Created { get; set; }
}

public class CreateUserRequest
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "Viewer";
}

public class CreateUserResult
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public class SimulateRequest
{
    public string? Ip { get; set; }
    public string? Kind { get; set; }
}
