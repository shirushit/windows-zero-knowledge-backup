using System.IO;
using System.Windows;

namespace BackupApp.UI;

public partial class App : System.Windows.Application
{
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "backupapp_diag.log");

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("App.OnStartup called.");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"AppDomain UnhandledException: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log($"UnobservedTaskException: {args.Exception}");
        };

        Exit += (s, args) =>
        {
            Log($"App.Exit event triggered with ExitCode: {args.ApplicationExitCode}");
        };

        base.OnStartup(e);
    }
}

