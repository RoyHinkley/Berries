using System.Diagnostics;

namespace Berries.Gui;

public partial class MainWindow
{
    private static readonly string RecentRootsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Berries",
        "roots.txt");

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        LoadRecentRoots();
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveRecentRoots();
        base.OnClosed(e);
    }

    private void LoadRecentRoots()
    {
        try
        {
            if (!File.Exists(RecentRootsPath)) return;

            var savedRoots = File.ReadLines(RecentRootsPath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
            var normalized = controller.NormalizeRoots(savedRoots);

            roots.Clear();
            roots.AddRange(normalized);
            RefreshRoots();
            StatusText.Text = roots.Count == 0
                ? "Select roots to begin."
                : "Review roots, then Explore.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Debug.WriteLine($"[Berries] Could not load recent roots: {ex.Message}");
        }
    }

    private void SaveRecentRoots()
    {
        try
        {
            var directory = Path.GetDirectoryName(RecentRootsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllLines(RecentRootsPath, roots);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Berries] Could not save recent roots: {ex.Message}");
        }
    }
}
