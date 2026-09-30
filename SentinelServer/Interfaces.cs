namespace SentinelServer;

public interface IFirewallController
{
    void Init();
    bool BlockIp(string ip);
    bool UnblockIp(string ip);
}

public interface IAuthEventWatcher
{
    void Start();
    void Stop();
}

public interface IProcessController
{
    bool SuspendProcess(int pid);
    bool ResumeProcess(int pid);
    bool TerminateProcess(int pid);
}
