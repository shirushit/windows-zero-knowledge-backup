using BackupApp.Crypto;

namespace BackupApp.RestoreEngine;

public sealed class RestoreTreeBrowser
{
    private readonly SnapshotManifest _manifest;

    public RestoreTreeBrowser(SnapshotManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        _manifest = manifest;
    }

    public IReadOnlyList<SnapshotManifestItem> GetAllFiles()
    {
        return _manifest.Items;
    }

    public IReadOnlyList<SnapshotManifestItem> GetFiles(string? directoryPrefix = null)
    {
        var prefix = NormalizePrefix(directoryPrefix);

        return _manifest.Items
            .Where(item =>
            {
                var normalizedPath = item.Path.Replace('\\', '/');
                if (string.IsNullOrEmpty(prefix))
                {
                    // Files in root (contain no '/')
                    return !normalizedPath.Contains('/');
                }

                if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var remaining = normalizedPath[prefix.Length..];
                // Immediate children only (contain no '/')
                return !remaining.Contains('/');
            })
            .ToList();
    }

    public IReadOnlyList<string> GetDirectories(string? directoryPrefix = null)
    {
        var prefix = NormalizePrefix(directoryPrefix);
        var subdirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _manifest.Items)
        {
            var normalizedPath = item.Path.Replace('\\', '/');
            if (!string.IsNullOrEmpty(prefix) && !normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remaining = string.IsNullOrEmpty(prefix) ? normalizedPath : normalizedPath[prefix.Length..];
            var slashIdx = remaining.IndexOf('/');
            if (slashIdx > 0)
            {
                var dirName = remaining[..slashIdx];
                subdirs.Add(dirName);
            }
        }

        return subdirs.OrderBy(d => d).ToList();
    }

    public IReadOnlyList<SnapshotManifestItem> SearchFiles(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return _manifest.Items
            .Where(item => item.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Path)
            .ToList();
    }

    private static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        var normalized = prefix.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized + "/";
    }
}
