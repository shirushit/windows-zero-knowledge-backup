using System.Text.RegularExpressions;

namespace BackupApp.Domain;

public readonly partial record struct CanonicalPath : IEquatable<CanonicalPath>, IComparable<CanonicalPath>
{
    private static readonly char[] IllegalChars = ['<', '>', ':', '"', '|', '?', '*', '\\'];
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public string Value { get; }

    public CanonicalPath(string rawPath)
    {
        Value = NormalizeAndValidate(rawPath);
    }

    public static CanonicalPath From(string rawPath) => new(rawPath);

    private static string NormalizeAndValidate(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);

        // Normalize backslashes to forward slashes
        var normalized = rawPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Path cannot be empty or root slash.", nameof(rawPath));
        }

        var segments = normalized.Split('/', StringSplitOptions.None);
        var cleanSegments = new List<string>(segments.Length);

        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg))
            {
                throw new ArgumentException($"Path contains empty segment: '{rawPath}'", nameof(rawPath));
            }

            if (seg == "." || seg == "..")
            {
                throw new ArgumentException($"Path traversal segment '{seg}' is forbidden: '{rawPath}'", nameof(rawPath));
            }

            if (seg.IndexOfAny(IllegalChars) >= 0)
            {
                throw new ArgumentException($"Path contains illegal characters: '{seg}'", nameof(rawPath));
            }

            // Check for control characters (ASCII 0..31)
            foreach (var c in seg)
            {
                if (char.IsControl(c))
                {
                    throw new ArgumentException($"Path contains control character: '{rawPath}'", nameof(rawPath));
                }
            }

            // Check for reserved Windows device names
            var nameWithoutExt = seg.Split('.')[0];
            if (ReservedDeviceNames.Contains(nameWithoutExt))
            {
                throw new ArgumentException($"Path segment contains reserved Windows device name: '{seg}'", nameof(rawPath));
            }

            cleanSegments.Add(seg);
        }

        return string.Join('/', cleanSegments);
    }

    public string GetFileName()
    {
        var lastSlash = Value.LastIndexOf('/');
        return lastSlash >= 0 ? Value[(lastSlash + 1)..] : Value;
    }

    public string? GetParentDirectory()
    {
        var lastSlash = Value.LastIndexOf('/');
        return lastSlash >= 0 ? Value[..lastSlash] : null;
    }

    public string ResolveUnder(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var fullBase = Path.GetFullPath(baseDirectory);
        var osSpecificRelative = Value.Replace('/', Path.DirectorySeparatorChar);
        var fullTarget = Path.GetFullPath(Path.Combine(fullBase, osSpecificRelative));

        // Path traversal invariant check
        if (!fullTarget.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path traversal detected: target '{fullTarget}' escapes base '{fullBase}'.");
        }

        return fullTarget;
    }

    public override string ToString() => Value;

    public bool Equals(CanonicalPath other) =>
        string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public int CompareTo(CanonicalPath other) =>
        string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public static bool operator <(CanonicalPath left, CanonicalPath right) => left.CompareTo(right) < 0;
    public static bool operator <=(CanonicalPath left, CanonicalPath right) => left.CompareTo(right) <= 0;
    public static bool operator >(CanonicalPath left, CanonicalPath right) => left.CompareTo(right) > 0;
    public static bool operator >=(CanonicalPath left, CanonicalPath right) => left.CompareTo(right) >= 0;
}
