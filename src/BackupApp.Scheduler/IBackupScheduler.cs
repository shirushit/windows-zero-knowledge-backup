namespace BackupApp.Scheduler;

public interface IBackupScheduler
{
    bool IsRunning { get; }
    void StartScheduler();
    void StopScheduler();
    void PauseScheduler();
    void ResumeScheduler();
}

public sealed class BackupScheduler : IBackupScheduler
{
    public bool IsRunning => false;

    public void StartScheduler() { }
    public void StopScheduler() { }
    public void PauseScheduler() { }
    public void ResumeScheduler() { }
}
