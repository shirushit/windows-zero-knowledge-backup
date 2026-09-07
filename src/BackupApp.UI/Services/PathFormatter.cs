using System.Text;

namespace BackupApp.UI.Services;

public static class PathFormatter
{
    // Unicode Left-to-Right Mark (LRM) ensures Windows paths render correctly inside RTL contexts
    public const char Lrm = '\u200E';
    public const char Rlm = '\u200F';

    public static string FormatForRtl(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        // Wrap the entire path with LRM so that punctuation like 'C:\' and file extensions don't flip
        var sb = new StringBuilder();
        sb.Append(Lrm);

        // Replace any slash sequences with LRM around separators to keep hierarchy ordered left-to-right
        var normalized = path.Replace('/', '\\');
        var parts = normalized.Split('\\');

        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('\\');
                sb.Append(Lrm);
            }
            sb.Append(parts[i]);
            sb.Append(Lrm);
        }

        return sb.ToString();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 בייט";
        string[] sizes = ["בייט", "ק\"ב", "מ\"ב", "ג\"ב", "ט\"ב"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
