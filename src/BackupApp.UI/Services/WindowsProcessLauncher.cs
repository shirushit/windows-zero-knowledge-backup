using System.Diagnostics;

namespace BackupApp.UI.Services;

public sealed class WindowsProcessLauncher : IProcessLauncher
{
    public void Start(string fileName, string? arguments = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = string.IsNullOrEmpty(arguments)
        };

        if (!string.IsNullOrEmpty(arguments))
        {
            psi.Arguments = arguments;
        }

        Process.Start(psi);
    }
}
