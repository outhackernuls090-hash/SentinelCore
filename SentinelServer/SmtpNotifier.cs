using Sentinel.Shared;
using System.Net;
using System.Net.Mail;

namespace SentinelServer;

public class SmtpNotifier
{
    private readonly AppConfig _config;
    private readonly Storage _storage;
    private DateTime _lastSentUtc = DateTime.MinValue;
    private readonly object _gate = new();

    public SmtpNotifier(AppConfig config, Storage storage)
    {
        _config = config;
        _storage = storage;
    }

    public async Task NotifyAsync(Incident incident)
    {
        if (!_config.Smtp.Enabled) return;
        if (SeverityLevel.Rank(incident.Severity) < SeverityLevel.Rank(_config.Smtp.MinSeverity)) return;

        lock (_gate)
        {
            if ((DateTime.UtcNow - _lastSentUtc).TotalMinutes < _config.Smtp.RateLimitMinutes) return;
            _lastSentUtc = DateTime.UtcNow;
        }

        var subject = $"[SentinelCore] {incident.Severity.ToUpperInvariant()} - {incident.Title} on {Environment.MachineName}";
        var body = $"Host: {Environment.MachineName}\n" +
                   $"Severity: {incident.Severity}\n" +
                   $"Rule: {incident.Rule}\n" +
                   $"Source IP: {incident.SourceIp}\n" +
                   $"Details: {incident.Details}\n" +
                   $"First seen: {incident.FirstSeen:u}\n" +
                   $"Occurrences: {incident.Count}\n";

        try
        {
            await SendAsync(subject, body);
            _storage.AddAudit("system", "SMTP_ALERT_SENT", incident.Id, "SUCCESS");
        }
        catch (Exception ex)
        {
            _storage.AddAudit("system", "SMTP_ALERT_SENT", incident.Id, $"FAILED: {ex.Message}");
        }
    }

    public async Task<bool> SendTestAsync()
    {
        try
        {
            await SendAsync("[SentinelCore] Test alert", $"This is a test alert from {Environment.MachineName} sent at {DateTime.UtcNow:u}.");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SendAsync(string subject, string body)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_config.Smtp.From),
            Subject = subject,
            Body = body
        };
        foreach (var to in _config.Smtp.To)
            message.To.Add(to);

        using var client = new SmtpClient(_config.Smtp.Host, _config.Smtp.Port)
        {
            EnableSsl = _config.Smtp.UseTls,
            Credentials = string.IsNullOrEmpty(_config.Smtp.Username)
                ? null
                : new NetworkCredential(_config.Smtp.Username, _config.Smtp.Password)
        };
        await client.SendMailAsync(message);
    }
}
