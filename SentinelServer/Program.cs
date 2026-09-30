using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sentinel.Shared;
using System.Net;
using System.Text;

namespace SentinelServer;

public class Program
{
    public static void Main(string[] args)
    {
        var dataDir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SentinelCore")
            : "/var/lib/sentinelcore";

        Directory.CreateDirectory(dataDir);

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(configPath, optional: false).Build();

        var config = new AppConfig();
        configuration.GetSection("management").Bind(config.Management);
        configuration.GetSection("tls").Bind(config.Tls);
        configuration.GetSection("detection").Bind(config.Detection);
        configuration.GetSection("response").Bind(config.Response);
        configuration.GetSection("smtp").Bind(config.Smtp);
        configuration.GetSection("retention").Bind(config.Retention);
        configuration.GetSection("testMode").Bind(config.TestMode);

        var storage = new Storage(Path.Combine(dataDir, "sentinel.db"));

        string? bootstrapKey = null;
        if (storage.UserCount() == 0)
        {
            bootstrapKey = ApiKeys.Generate();
            storage.CreateUser("admin", "Owner", ApiKeys.Hash(bootstrapKey));
            storage.AddAudit("system", "BOOTSTRAP_USER_CREATED", "admin", "SUCCESS");
        }

        if (config.TestMode.SeedFakeData)
            storage.SeedFakeData();

        IFirewallController firewall = OperatingSystem.IsWindows() ? new WindowsFirewallController() : new LinuxFirewallController();
        firewall.Init();

        IProcessController processController = OperatingSystem.IsWindows() ? new WindowsProcessController() : new LinuxProcessController();

        var notifier = new SmtpNotifier(config, storage);
        var engine = new DetectionEngine(storage, firewall, config, notifier);

        IAuthEventWatcher authWatcher = OperatingSystem.IsWindows() ? new WindowsSecurityEventLogWatcher(engine) : new LinuxAuthLogWatcher(engine);
        authWatcher.Start();

        var netMonitor = new NetworkMonitor(engine);
        netMonitor.Start();

        var retention = new RetentionService(storage, engine, config);
        retention.Start();

        var builder = WebApplication.CreateBuilder(args);

        string? tlsFingerprint = null;
        builder.WebHost.ConfigureKestrel(options =>
        {
            if (config.Tls.Enabled)
            {
                var cert = TlsCertProvider.GetOrCreate(dataDir);
                tlsFingerprint = TlsCertProvider.Fingerprint(cert);
                options.ListenAnyIP(config.Management.Port, listenOptions => listenOptions.UseHttps(cert));
            }
            else
            {
                options.ListenAnyIP(config.Management.Port);
            }
        });

        if (OperatingSystem.IsWindows())
            builder.Host.UseWindowsService();
        else
            builder.Host.UseSystemd();

        var app = builder.Build();
        var startedAt = DateTime.UtcNow;

        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/health")
            {
                await next();
                return;
            }

            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp != null && !IsAllowedNetwork(remoteIp, config.Management.AllowedNetworks))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "source not in allowedNetworks" });
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-Api-Key", out var key) || string.IsNullOrWhiteSpace(key))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "missing X-Api-Key header" });
                return;
            }

            var user = storage.FindUserByApiKeyHash(ApiKeys.Hash(key!));
            if (user == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "invalid API key" });
                return;
            }

            context.Items["User"] = user.Value.name;
            context.Items["Role"] = user.Value.role;
            await next();
        });

        Func<HttpContext, string, bool> hasRole = (ctx, role) =>
            RoleCheck.Satisfies(ctx.Items["Role"] as string ?? "Viewer", role);

        Func<HttpContext, string> actorOf = ctx => ctx.Items["User"] as string ?? "unknown";

        app.MapGet("/health", () => new HealthInfo
        {
            Status = "ok",
            Version = Product.Version,
            TestMode = config.TestMode.Enabled,
            Tls = config.Tls.Enabled
        });

        app.MapGet("/api/v1/status", (HttpContext ctx) => new HostStatus
        {
            HostName = Environment.MachineName,
            Os = Environment.OSVersion.ToString(),
            Platform = OperatingSystem.IsWindows() ? "windows" : "linux",
            Version = Product.Version,
            Role = ctx.Items["Role"] as string ?? "Viewer",
            User = ctx.Items["User"] as string ?? "",
            LastHeartbeat = DateTime.UtcNow,
            ActiveIncidents = storage.GetIncidents("open", 10000).Count,
            BlockedIps = storage.GetBlockedIps().Count,
            MaintenanceMode = engine.MaintenanceMode,
            DryRun = config.Response.DryRun,
            TestMode = config.TestMode.Enabled,
            UptimeSeconds = (long)(DateTime.UtcNow - startedAt).TotalSeconds
        });

        app.MapGet("/api/v1/incidents", (string? state, int? limit) => storage.GetIncidents(state, limit ?? 300));

        app.MapGet("/api/v1/incidents/{id}/timeline", (string id) => storage.GetTimeline(id));

        app.MapPost("/api/v1/actions/incident-state", (HttpContext ctx, IncidentStateRequest req) =>
        {
            if (!hasRole(ctx, "Analyst")) return Results.Json(new { error = "requires Analyst role" }, statusCode: 403);
            var valid = new[] { "open", "investigating", "contained", "resolved", "false_positive", "ignored" };
            if (!valid.Contains(req.State)) return Results.BadRequest(new { error = "invalid state" });
            var ok = storage.SetIncidentState(req.IncidentId, req.State);
            if (ok) storage.AddTimeline(req.IncidentId, $"State changed to {req.State} by {actorOf(ctx)}");
            storage.AddAudit(actorOf(ctx), "SET_INCIDENT_STATE", $"{req.IncidentId}:{req.State}", ok ? "SUCCESS" : "FAILED");
            return Results.Ok(new ActionResult { Success = ok });
        });

        app.MapGet("/api/v1/processes", () => ProcessMonitor.GetProcesses());
        app.MapGet("/api/v1/network", () => NetworkMonitor.GetCurrentConnections());
        app.MapGet("/api/v1/blocked-ips", () => storage.GetBlockedIps());
        app.MapGet("/api/v1/audit", (int? limit) => storage.GetAudit(limit ?? 500));
        app.MapGet("/api/v1/audit/verify", () => storage.VerifyAuditChain());
        app.MapGet("/api/v1/allowlist", () => new AllowlistInfo { Entries = storage.GetAllowlist() });

        app.MapPost("/api/v1/allowlist", (HttpContext ctx, AllowlistRequest req) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            if (req.Remove) storage.RemoveAllowlistEntry(req.Entry);
            else storage.AddAllowlistEntry(req.Entry);
            storage.AddAudit(actorOf(ctx), req.Remove ? "ALLOWLIST_REMOVE" : "ALLOWLIST_ADD", req.Entry, "SUCCESS");
            return Results.Ok(new ActionResult { Success = true });
        });

        app.MapPost("/api/v1/actions/block-ip", (HttpContext ctx, BlockIpRequest req) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            var ok = firewall.BlockIp(req.Ip);
            storage.SaveBlockedIp(new BlockedIp
            {
                Ip = req.Ip,
                Reason = req.Reason ?? "manual block",
                ExpiresAt = req.DurationMinutes.HasValue ? DateTime.UtcNow.AddMinutes(req.DurationMinutes.Value) : null,
                BlockedBy = actorOf(ctx)
            });
            storage.AddAudit(actorOf(ctx), "BLOCK_IP", req.Ip, ok ? "SUCCESS" : "FAILED");
            return Results.Ok(new ActionResult { Success = ok });
        });

        app.MapPost("/api/v1/actions/unblock-ip", (HttpContext ctx, IpRequest req) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            var ok = firewall.UnblockIp(req.Ip);
            storage.RemoveBlockedIp(req.Ip);
            storage.AddAudit(actorOf(ctx), "UNBLOCK_IP", req.Ip, ok ? "SUCCESS" : "FAILED");
            return Results.Ok(new ActionResult { Success = ok });
        });

        app.MapPost("/api/v1/actions/suspend-process", (HttpContext ctx, ProcessActionRequest req) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            var ok = processController.SuspendProcess(req.Pid);
            storage.AddAudit(actorOf(ctx), "SUSPEND_PROCESS", req.Pid.ToString(), ok ? "SUCCESS" : "FAILED");
            return Results.Ok(new ActionResult { Success = ok });
        });

        app.MapPost("/api/v1/actions/resume-process", (HttpContext ctx, ProcessActionRequest req) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            var ok = processController.ResumeProcess(req.Pid);
            storage.AddAudit(actorOf(ctx), "RESUME_PROCESS", req.Pid.ToString(), ok ? "SUCCESS" : "FAILED");
            return Results.Ok(new ActionResult { Success = ok });
        });

        app.MapPost("/api/v1/actions/terminate-process", (HttpContext ctx, ProcessActionRequest req) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            var ok = processController.TerminateProcess(req.Pid);
            storage.AddAudit(actorOf(ctx), "TERMINATE_PROCESS", req.Pid.ToString(), ok ? "SUCCESS" : "FAILED");
            return Results.Ok(new ActionResult { Success = ok });
        });

        app.MapPost("/api/v1/actions/maintenance", (HttpContext ctx, MaintenanceRequest req) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            engine.SetMaintenanceMode(req.Enabled);
            storage.AddAudit(actorOf(ctx), "SET_MAINTENANCE", req.Enabled.ToString(), "SUCCESS");
            return Results.Ok(new ActionResult { Success = true });
        });

        app.MapPost("/api/v1/actions/test-email", (HttpContext ctx) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            var ok = notifier.SendTestAsync().GetAwaiter().GetResult();
            storage.AddAudit(actorOf(ctx), "TEST_EMAIL", config.Smtp.Host, ok ? "SUCCESS" : "FAILED");
            return Results.Ok(new ActionResult { Success = ok, Message = ok ? "sent" : "check smtp config" });
        });

        app.MapPost("/api/v1/actions/simulate-attack", (HttpContext ctx, SimulateRequest req) =>
        {
            if (!hasRole(ctx, "Administrator")) return Results.Json(new { error = "requires Administrator role" }, statusCode: 403);
            if (!config.TestMode.Enabled) return Results.BadRequest(new { error = "testMode.enabled is false in appsettings.json" });

            var ip = string.IsNullOrWhiteSpace(req.Ip) ? "203.0.113.55" : req.Ip;
            if (req.Kind == "portscan") engine.SimulatePortScan(ip);
            else engine.SimulateBruteForce(ip);

            storage.AddAudit(actorOf(ctx), "SIMULATE_ATTACK", $"{req.Kind ?? "bruteforce"}:{ip}", "SUCCESS");
            return Results.Ok(new ActionResult { Success = true, Message = ip });
        });

        app.MapGet("/api/v1/users", (HttpContext ctx) =>
        {
            if (!hasRole(ctx, "Owner")) return Results.Json(new { error = "requires Owner role" }, statusCode: 403);
            return Results.Ok(storage.GetUsers());
        });

        app.MapPost("/api/v1/users", (HttpContext ctx, CreateUserRequest req) =>
        {
            if (!hasRole(ctx, "Owner")) return Results.Json(new { error = "requires Owner role" }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(req.Name) || !RoleCheck.IsValidRole(req.Role))
                return Results.BadRequest(new { error = "invalid name or role" });

            var apiKey = ApiKeys.Generate();
            try
            {
                storage.CreateUser(req.Name, req.Role, ApiKeys.Hash(apiKey));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "could not create user: " + ex.Message });
            }
            storage.AddAudit(actorOf(ctx), "CREATE_USER", $"{req.Name}:{req.Role}", "SUCCESS");
            return Results.Ok(new CreateUserResult { Name = req.Name, Role = req.Role, ApiKey = apiKey });
        });

        app.MapDelete("/api/v1/users/{name}", (HttpContext ctx, string name) =>
        {
            if (!hasRole(ctx, "Owner")) return Results.Json(new { error = "requires Owner role" }, statusCode: 403);
            if (name == actorOf(ctx)) return Results.BadRequest(new { error = "cannot revoke your own currently-connected user" });
            var ok = storage.DeleteUser(name);
            storage.AddAudit(actorOf(ctx), "REVOKE_USER", name, ok ? "SUCCESS" : "FAILED");
            return Results.Ok(new ActionResult { Success = ok });
        });

        app.MapGet("/api/v1/report.csv", () =>
        {
            var incidents = storage.GetIncidents(null, 10000);
            var sb = new StringBuilder();
            sb.AppendLine("Id,FirstSeen,LastSeen,Severity,State,Rule,SourceIp,Count,Details");
            foreach (var i in incidents)
                sb.AppendLine($"{i.Id},{i.FirstSeen:o},{i.LastSeen:o},{i.Severity},{i.State},{CsvEscape(i.Rule)},{i.SourceIp},{i.Count},{CsvEscape(i.Details)}");
            return Results.Text(sb.ToString(), "text/csv");
        });

        Console.WriteLine($"SentinelCore Server v{Product.Version} starting on {(OperatingSystem.IsWindows() ? "Windows" : "Linux")}");
        Console.WriteLine($"Data directory: {dataDir}");
        Console.WriteLine($"Listening on port {config.Management.Port} ({(config.Tls.Enabled ? "HTTPS" : "HTTP - not recommended for production")})");
        if (tlsFingerprint != null)
            Console.WriteLine($"TLS certificate SHA-256 fingerprint: {PinStore.Format(tlsFingerprint)}");
        if (bootstrapKey != null)
        {
            Console.WriteLine("================================================================");
            Console.WriteLine(" First run: created Owner account 'admin'");
            Console.WriteLine($" API key: {bootstrapKey}");
            Console.WriteLine(" Save this now - it is not stored anywhere and cannot be shown again.");
            Console.WriteLine(" Use it to connect from SentinelConsole or SentinelCli.");
            Console.WriteLine("================================================================");
        }

        app.Run();
    }

    private static string CsvEscape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static bool IsAllowedNetwork(IPAddress remote, List<string> networks)
    {
        if (IPAddress.IsLoopback(remote)) return true;
        var mapped = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;

        foreach (var net in networks)
        {
            if (net.Contains('/'))
            {
                if (IsInCidr(mapped, net)) return true;
            }
            else if (IPAddress.TryParse(net, out var single) && single.Equals(mapped))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsInCidr(IPAddress address, string cidr)
    {
        var parts = cidr.Split('/');
        if (!IPAddress.TryParse(parts[0], out var baseAddress)) return false;
        if (address.AddressFamily != baseAddress.AddressFamily) return false;
        var prefixLength = int.Parse(parts[1]);

        var addressBytes = address.GetAddressBytes();
        var baseBytes = baseAddress.GetAddressBytes();

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
            if (addressBytes[i] != baseBytes[i]) return false;

        if (remainingBits > 0 && fullBytes < addressBytes.Length)
        {
            var mask = (byte)~(255 >> remainingBits);
            if ((addressBytes[fullBytes] & mask) != (baseBytes[fullBytes] & mask)) return false;
        }
        return true;
    }
}
