namespace BackupApp.UI.Services;

public interface IProcessLauncher
{
    void Start(string fileName, string? arguments = null);
}
