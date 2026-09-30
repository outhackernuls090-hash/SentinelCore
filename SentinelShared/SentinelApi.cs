using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Sentinel.Shared;

public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

public sealed class SentinelApi : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public string BaseUrl { get; }
    public string? PinnedFingerprint { get; }
    public string? SeenFingerprint { get; private set; }

    public SentinelApi(string baseUrl, string apiKey, string? pinnedFingerprint)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        PinnedFingerprint = pinnedFingerprint;
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = Validate };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
    }

    private bool Validate(HttpRequestMessage req, X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors)
    {
        if (cert == null) return false;
        SeenFingerprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
        if (errors == SslPolicyErrors.None) return true;
        return !string.IsNullOrEmpty(PinnedFingerprint) &&
               string.Equals(SeenFingerprint, PinnedFingerprint, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<(HealthInfo? health, string? fingerprint, string? error)> ProbeAsync(string baseUrl)
    {
        string? fp = null;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (m, cert, chain, errs) =>
            {
                if (cert != null) fp = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
                return true;
            }
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            var res = await http.GetAsync(baseUrl.TrimEnd('/') + "/health");
            var text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return (null, fp, $"HTTP {(int)res.StatusCode}");
            return (JsonSerializer.Deserialize<HealthInfo>(text, Json), fp, null);
        }
        catch (Exception ex)
        {
            return (null, fp, ex.Message);
        }
    }

    private static string Extract(string body, HttpResponseMessage res)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "message", "error", "title" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                        return v.GetString() ?? "";
                }
            }
        }
        catch
        {
        }
        return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}" : body;
    }

    private async Task<string> SendRawAsync(HttpMethod method, string path, object? body)
    {
        using var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.Add("X-Api-Key", _apiKey);
        if (body != null) req.Content = JsonContent.Create(body, body.GetType(), null, Json);
        using var res = await _http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new ApiException((int)res.StatusCode, Extract(text, res));
        return text;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        var text = await SendRawAsync(method, path, body);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    public Task<HostStatus> GetStatusAsync() => SendAsync<HostStatus>(HttpMethod.Get, "/api/v1/status");

    public Task<List<Incident>> GetIncidentsAsync(string? state = null, int limit = 300) =>
        SendAsync<List<Incident>>(HttpMethod.Get, "/api/v1/incidents?limit=" + limit + (string.IsNullOrEmpty(state) ? "" : "&state=" + Uri.EscapeDataString(state)));

    public Task<List<TimelineEntry>> GetTimelineAsync(string incidentId) =>
        SendAsync<List<TimelineEntry>>(HttpMethod.Get, "/api/v1/incidents/" + Uri.EscapeDataString(incidentId) + "/timeline");

    public Task<ActionResult> SetIncidentStateAsync(string incidentId, string state) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/incident-state", new IncidentStateRequest { IncidentId = incidentId, State = state });

    public Task<List<ProcessInfo>> GetProcessesAsync() => SendAsync<List<ProcessInfo>>(HttpMethod.Get, "/api/v1/processes");
    public Task<List<NetworkConnectionInfo>> GetNetworkAsync() => SendAsync<List<NetworkConnectionInfo>>(HttpMethod.Get, "/api/v1/network");
    public Task<List<BlockedIp>> GetBlockedIpsAsync() => SendAsync<List<BlockedIp>>(HttpMethod.Get, "/api/v1/blocked-ips");
    public Task<List<AuditEntry>> GetAuditAsync(int limit = 500) => SendAsync<List<AuditEntry>>(HttpMethod.Get, "/api/v1/audit?limit=" + limit);
    public Task<AuditVerifyResult> VerifyAuditAsync() => SendAsync<AuditVerifyResult>(HttpMethod.Get, "/api/v1/audit/verify");

    public Task<ActionResult> BlockIpAsync(string ip, string reason, int? minutes) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/block-ip", new BlockIpRequest { Ip = ip, Reason = reason, DurationMinutes = minutes });

    public Task<ActionResult> UnblockIpAsync(string ip) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/unblock-ip", new IpRequest { Ip = ip });

    public Task<ActionResult> SuspendProcessAsync(int pid) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/suspend-process", new ProcessActionRequest { Pid = pid });

    public Task<ActionResult> ResumeProcessAsync(int pid) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/resume-process", new ProcessActionRequest { Pid = pid });

    public Task<ActionResult> TerminateProcessAsync(int pid) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/terminate-process", new ProcessActionRequest { Pid = pid });

    public Task<ActionResult> SetMaintenanceAsync(bool enabled) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/maintenance", new MaintenanceRequest { Enabled = enabled });

    public Task<AllowlistInfo> GetAllowlistAsync() => SendAsync<AllowlistInfo>(HttpMethod.Get, "/api/v1/allowlist");

    public Task<ActionResult> ChangeAllowlistAsync(string entry, bool remove) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/allowlist", new AllowlistRequest { Entry = entry, Remove = remove });

    public Task<List<UserInfo>> GetUsersAsync() => SendAsync<List<UserInfo>>(HttpMethod.Get, "/api/v1/users");

    public Task<CreateUserResult> CreateUserAsync(string name, string role) =>
        SendAsync<CreateUserResult>(HttpMethod.Post, "/api/v1/users", new CreateUserRequest { Name = name, Role = role });

    public Task<ActionResult> RevokeUserAsync(string name) =>
        SendAsync<ActionResult>(HttpMethod.Delete, "/api/v1/users/" + Uri.EscapeDataString(name));

    public Task<ActionResult> TestEmailAsync() => SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/test-email", new IpRequest());

    public Task<ActionResult> SimulateAsync(string kind, string ip) =>
        SendAsync<ActionResult>(HttpMethod.Post, "/api/v1/actions/simulate-attack", new SimulateRequest { Kind = kind, Ip = ip });

    public Task<string> GetReportCsvAsync() => SendRawAsync(HttpMethod.Get, "/api/v1/report.csv", null);

    public void Dispose() => _http.Dispose();
}
