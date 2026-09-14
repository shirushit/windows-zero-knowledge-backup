using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace BackupApp.UI.Services;

public sealed class WindowsProcessLauncher : IProcessLauncher
{
    private static readonly HashSet<string> KnownTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".h", ".cpp", ".hpp", ".cs", ".txt", ".json", ".xml", ".log",
        ".md", ".py", ".java", ".ini", ".cfg", ".as", ".am", ".s", ".asm",
        ".bat", ".cmd", ".ps1", ".sh", ".yaml", ".yml", ".sql", ".tsv", ".csv"
    };

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

        try
        {
            Process.Start(psi);
        }
        catch (Win32Exception)
        {
            // Windows error when no application is associated with this file extension
            if (File.Exists(fileName))
            {
                var ext = Path.GetExtension(fileName);
                if (KnownTextExtensions.Contains(ext) || IsLikelyTextFile(fileName))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "notepad.exe",
                            Arguments = $"\"{fileName}\"",
                            UseShellExecute = false
                        });
                        return;
                    }
                    catch
                    {
                        // Fallback to explorer select
                    }
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{fileName}\"",
                        UseShellExecute = false
                    });
                    return;
                }
                catch
                {
                    // If explorer select also fails, rethrow
                }
            }

            throw;
        }
    }

    private static bool IsLikelyTextFile(string path)
    {
        try
        {
            var buffer = new byte[512];
            using var fs = File.OpenRead(path);
            int read = fs.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return false;
            }
            return read > 0;
        }
        catch
        {
            return false;
        }
    }
}
