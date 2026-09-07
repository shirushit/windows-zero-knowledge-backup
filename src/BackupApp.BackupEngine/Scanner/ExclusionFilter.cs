using System.Text.RegularExpressions;
using BackupApp.Domain;

namespace BackupApp.BackupEngine.Scanner;

public sealed class ExclusionFilter
{
    private readonly List<Regex> _compiledPatterns = [];
    private readonly HashSet<string> _exactNames = new(StringComparer.OrdinalIgnoreCase);

    public ExclusionFilter(IEnumerable<string>? patterns = null)
    {
        if (patterns == null)
        {
            return;
        }

        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                continue;
            }

            var trimmed = p.Trim().Replace('\\', '/');

            // Exact filename match without slashes or wildcards (e.g. "Thumbs.db", "desktop.ini")
            if (!trimmed.Contains('/') && !trimmed.Contains('*') && !trimmed.Contains('?'))
            {
                _exactNames.Add(trimmed);
                continue;
            }

            // Convert glob pattern to regular expression
            var regexPattern = GlobToRegex(trimmed);
            _compiledPatterns.Add(new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }
    }

    public bool IsExcluded(CanonicalPath path, bool isDirectory = false)
    {
        var fileName = path.GetFileName();

        if (_exactNames.Contains(fileName))
        {
            return true;
        }

        var pathValue = path.Value;
        if (isDirectory && !pathValue.EndsWith('/'))
        {
            pathValue += "/";
        }

        foreach (var regex in _compiledPatterns)
        {
            if (regex.IsMatch(pathValue) || regex.IsMatch(fileName))
            {
                return true;
            }
        }

        return false;
    }

    private static string GlobToRegex(string glob)
    {
        var escaped = Regex.Escape(glob)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", ".");

        return $"^{escaped}(/.*)?$";
    }
}
