namespace SentinelServer;

public class RetentionService
{
    private readonly Storage _storage;
    private readonly DetectionEngine _engine;
    private readonly AppConfig _config;
    private CancellationTokenSource? _cts;
    private DateTime _lastPurgeUtc = DateTime.MinValue;

    public RetentionService(Storage storage, DetectionEngine engine, AppConfig config)
    {
        _storage = storage;
        _engine = engine;
        _config = config;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => Loop(_cts.Token));
    }

    private async Task Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                _engine.ExpireBlocks();

                if ((DateTime.UtcNow - _lastPurgeUtc).TotalHours >= _config.Retention.CleanupIntervalHours)
                {
                    var events = _storage.PurgeOldEvents(DateTime.UtcNow.AddDays(-_config.Retention.EventDays));
                    var incidents = _storage.PurgeOldIncidents(DateTime.UtcNow.AddDays(-_config.Retention.IncidentDays));
                    var audit = _storage.PurgeOldAudit(DateTime.UtcNow.AddDays(-_config.Retention.AuditDays));
                    if (events + incidents + audit > 0)
                        Console.WriteLine($"[Retention] purged {events} events, {incidents} incidents, {audit} audit entries");
                    _lastPurgeUtc = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Retention] error: {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(60), token); } catch (TaskCanceledException) { }
        }
    }

    public void Stop() => _cts?.Cancel();
}
