namespace BackupApp.Domain;

public static class PathNormalizer
{
    public static CanonicalPath ToCanonicalPath(string rootPath, string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);

        var normalizedRoot = NormalizePathString(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedFile = NormalizePathString(absoluteFilePath);

        // Long path prefix handling \\?\
        var rootCompare = StripLongPathPrefix(normalizedRoot);
        var fileCompare = StripLongPathPrefix(normalizedFile);

        if (!fileCompare.StartsWith(rootCompare, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"File path '{absoluteFilePath}' is not under root '{rootPath}'.");
        }

        var relative = fileCompare[rootCompare.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new ArgumentException($"File path '{absoluteFilePath}' cannot be identical to the root directory.");
        }

        return CanonicalPath.From(relative);
    }

    private static string NormalizePathString(string path)
    {
        var full = Path.GetFullPath(path);
        return full;
    }

    private static string StripLongPathPrefix(string path)
    {
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }
}
