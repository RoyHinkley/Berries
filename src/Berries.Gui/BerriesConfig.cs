using System.Text.RegularExpressions;
using Berries.FileSystem.Abstractions;

namespace Berries.Gui;

internal sealed class BerriesConfig
{
    private readonly Regex[] componentPatterns;
    private readonly Regex[] pathPatterns;

    private BerriesConfig(string path, IReadOnlyList<string> ignorePatterns)
    {
        Path = path;
        IgnorePatterns = ignorePatterns;

        componentPatterns = ignorePatterns
            .Where(pattern => !ContainsSeparator(pattern))
            .Select(pattern => CompileGlob(pattern, matchPath: false))
            .ToArray();

        pathPatterns = ignorePatterns
            .Where(ContainsSeparator)
            .Select(pattern => CompileGlob(pattern, matchPath: true))
            .ToArray();
    }

    public string Path { get; }
    public IReadOnlyList<string> IgnorePatterns { get; }

    public static BerriesConfig Load(string path)
    {
        if (!File.Exists(path))
            return new BerriesConfig(path, Array.Empty<string>());

        var patterns = new List<string>();
        var inIgnoreSection = false;

        foreach (var sourceLine in File.ReadLines(path))
        {
            var line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inIgnoreSection = line[1..^1].Trim()
                    .Equals("ignore", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inIgnoreSection)
                continue;

            patterns.Add(line);
        }

        return new BerriesConfig(path, patterns);
    }

    public bool IsIgnored(FileSystemPath path)
    {
        if (IgnorePatterns.Count == 0)
            return false;

        var normalized = path.Value.Replace('\\', '/');
        var components = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pattern in componentPatterns)
        {
            if (components.Any(component => pattern.IsMatch(component)))
                return true;
        }

        foreach (var pattern in pathPatterns)
        {
            if (pattern.IsMatch(normalized))
                return true;
        }

        return false;
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
