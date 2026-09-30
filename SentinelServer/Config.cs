namespace SentinelServer;

public class AppConfig
{
    public ManagementConfig Management { get; set; } = new();
    public TlsConfig Tls { get; set; } = new();
    public DetectionConfig Detection { get; set; } = new();
    public ResponseConfig Response { get; set; } = new();
    public SmtpConfig Smtp { get; set; } = new();
    public RetentionConfig Retention { get; set; } = new();
    public TestModeConfig TestMode { get; set; } = new();
}

public class ManagementConfig
{
    public int Port { get; set; } = 47152;
    public List<string> AllowedNetworks { get; set; } = new();
}

public class TlsConfig
{
    public bool Enabled { get; set; } = true;
}

public class DetectionConfig
{
    public BruteForceConfig BruteForce { get; set; } = new();
    public PortScanConfig PortScan { get; set; } = new();
    public int SuppressionCooldownMinutes { get; set; } = 15;
}

public class BruteForceConfig
{
    public bool Enabled { get; set; } = true;
    public int Threshold { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}

public class PortScanConfig
{
    public bool Enabled { get; set; } = true;
    public int Threshold { get; set; } = 20;
    public int WindowSeconds { get; set; } = 5;
}

public class ResponseConfig
{
    public bool DryRun { get; set; } = false;
    public int AutoBlockDurationMinutes { get; set; } = 30;
}

public class SmtpConfig
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public List<string> To { get; set; } = new();
    public string MinSeverity { get; set; } = "high";
    public int RateLimitMinutes { get; set; } = 5;
}

public class RetentionConfig
{
    public int EventDays { get; set; } = 30;
    public int IncidentDays { get; set; } = 365;
    public int AuditDays { get; set; } = 365;
    public int CleanupIntervalHours { get; set; } = 24;
}

public class TestModeConfig
{
    public bool Enabled { get; set; } = false;
    public bool SeedFakeData { get; set; } = false;
}
