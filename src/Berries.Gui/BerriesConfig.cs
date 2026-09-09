using System.Text.RegularExpressions;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

internal sealed class BerriesConfig
{
    private readonly Regex[] componentPatterns;
    private readonly Regex[] pathPatterns;

    private BerriesConfig(string path, IReadOnlyList<string> excludePatterns)
    {
        Path = path;
        ExcludePatterns = excludePatterns;

        componentPatterns = excludePatterns
            .Where(pattern => !ContainsSeparator(pattern))
            .Select(pattern => CompileGlob(pattern, matchPath: false))
            .ToArray();

        pathPatterns = excludePatterns
            .Where(ContainsSeparator)
            .Select(pattern => CompileGlob(pattern, matchPath: true))
            .ToArray();
    }

    public string Path { get; }
    public IReadOnlyList<string> ExcludePatterns { get; }

    public static BerriesConfig Load(string path)
    {
        if (!File.Exists(path))
            return new BerriesConfig(path, Array.Empty<string>());

        var patterns = new List<string>();
        var inExcludeSection = false;

        foreach (var sourceLine in File.ReadLines(path))
        {
            var line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inExcludeSection = line[1..^1].Trim()
                    .Equals("exclude", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inExcludeSection)
                patterns.Add(line);
        }

        return new BerriesConfig(path, patterns);
    }

    public static void AddExcludePatterns(string path, IEnumerable<string> patterns)
    {
        var requested = patterns
            .Select(pattern => pattern.Trim())
            .Where(pattern => pattern.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length == 0)
            return;

        var existing = Load(path).ExcludePatterns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = requested.Where(pattern => !existing.Contains(pattern)).ToArray();
        if (additions.Length == 0)
            return;

        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();

        var sectionStart = -1;
        var sectionEnd = lines.Count;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith('[') || !line.EndsWith(']'))
                continue;

            if (sectionStart < 0)
            {
                if (line[1..^1].Trim().Equals("exclude", StringComparison.OrdinalIgnoreCase))
                    sectionStart = i;
            }
            else
            {
                sectionEnd = i;
                break;
            }
        }

        if (sectionStart < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0)
                lines.Add(string.Empty);
            lines.Add("[exclude]");
            lines.AddRange(additions);
        }
        else
        {
            lines.InsertRange(sectionEnd, additions);
        }

        File.WriteAllLines(path, lines);
    }

    public bool IsExcluded(FileSystemPath path)
    {
        if (ExcludePatterns.Count == 0)
            return false;

        var normalized = path.Value.Replace('\\', '/');
        var components = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (componentPatterns.Any(pattern => components.Any(pattern.IsMatch)))
            return true;

        return pathPatterns.Any(pattern => pattern.IsMatch(normalized));
    }

    private static bool ContainsSeparator(string pattern) =>
        pattern.Contains('/') || pattern.Contains('\\');

    private static Regex CompileGlob(string sourcePattern, bool matchPath)
    {
        var pattern = sourcePattern.Replace('\\', '/').Trim('/');
        var expression = Regex.Escape(pattern)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");

        expression = matchPath
            ? $@"(?:^|/){expression}(?:/|$)"
            : $@"^{expression}$";

        return new Regex(
            expression,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
