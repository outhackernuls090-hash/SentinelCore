using Sentinel.Shared;
using System.Net.NetworkInformation;

namespace SentinelServer;

public class NetworkMonitor
{
    private readonly DetectionEngine _engine;
    private CancellationTokenSource? _cts;

    public NetworkMonitor(DetectionEngine engine)
    {
        _engine = engine;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => Loop(_cts.Token));
    }

    private async Task Loop(CancellationToken token)
    {
        var seen = new HashSet<string>();
        while (!token.IsCancellationRequested)
        {
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                foreach (var c in props.GetActiveTcpConnections())
                {
                    if (c.State != TcpState.Established) continue;
                    var remoteIp = c.RemoteEndPoint.Address.ToString();
                    if (remoteIp is "0.0.0.0" or "::") continue;
                    var key = $"{remoteIp}:{c.LocalEndPoint.Port}";
                    if (seen.Add(key))
                        _engine.HandleConnection(remoteIp, c.LocalEndPoint.Port);
                }
                if (seen.Count > 5000) seen.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkMonitor] error: {ex.Message}");
            }

            try { await Task.Delay(2000, token); } catch (TaskCanceledException) { }
        }
    }

    public static List<NetworkConnectionInfo> GetCurrentConnections()
    {
        var list = new List<NetworkConnectionInfo>();
        var props = IPGlobalProperties.GetIPGlobalProperties();
        foreach (var c in props.GetActiveTcpConnections())
        {
            list.Add(new NetworkConnectionInfo
            {
                Protocol = "TCP",
                LocalAddress = c.LocalEndPoint.Address.ToString(),
                LocalPort = c.LocalEndPoint.Port,
                RemoteAddress = c.RemoteEndPoint.Address.ToString(),
                RemotePort = c.RemoteEndPoint.Port,
                State = c.State.ToString()
            });
        }
        return list;
    }

    public void Stop()
    {
        _cts?.Cancel();
    }
}
