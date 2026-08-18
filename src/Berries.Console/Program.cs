using Berries.Core.Analysis;
using Berries.Core.Domain;

namespace Berries.Console;

internal static class Program
{
    private static void Main()
    {
        // This deliberately uses a synthetic Portrait: no UI or filesystem adapter
        // is required to exercise Berries.Core.
        var portrait = new Portrait([]);
        IAnalysisEngine analysisEngine = new AnalysisEngine();

        System.Console.WriteLine($"Berries development console — synthetic portrait contains {portrait.Files.Count} files.");
        System.Console.WriteLine($"Core analysis entry point: {analysisEngine.GetType().Name}");
    }
}
