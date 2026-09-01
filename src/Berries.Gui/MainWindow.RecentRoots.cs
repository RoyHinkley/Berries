using System.Diagnostics;

namespace Berries.Gui;

internal static class RecentRootsStore
{
    private static readonly string RootsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Berries",
        "roots.txt");

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(RootsPath)) return [];
            return File.ReadLines(RootsPath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Berries] Could not load recent roots: {ex.Message}");
            return [];
        }
    }

    public static void Save(IEnumerable<string> roots)
    {
        try
        {
            var directory = Path.GetDirectoryName(RootsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllLines(RootsPath, roots);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Berries] Could not save recent roots: {ex.Message}");
        }
    }
}
