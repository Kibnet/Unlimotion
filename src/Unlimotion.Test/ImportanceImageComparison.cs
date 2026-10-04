using System;
using System.IO;
using System.Text.Json;
using SkiaSharp;

namespace Unlimotion.Test;

internal static class ImportanceImageComparison
{
    public static string CreateArtifactDirectory(string name)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("UNLIMOTION_IMPORTANCE_ARTIFACTS_DIRECTORY");
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "artifacts", "task-importance")
            : Path.GetFullPath(configuredRoot);
        return Path.Combine(root, Guid.NewGuid().ToString("N"), name);
    }

    public static void Save(SKBitmap image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        encoded.SaveTo(stream);
    }

    public static void MatchBaseline(SKBitmap actual, string baselineName, string directory)
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "Baselines", "TaskImportance", baselineName);
        if (!File.Exists(baselinePath))
            throw new InvalidOperationException($"Reviewed baseline is missing: {baselinePath}. Candidate: {Path.Combine(directory, "actual.png")}");
        using var expected = SKBitmap.Decode(baselinePath) ?? throw new InvalidOperationException($"Invalid baseline: {baselinePath}");
        VerifyEnvironment(Path.ChangeExtension(baselinePath, ".json"), Path.Combine(directory, "metadata.json"));
        var sameSize = expected.Width == actual.Width && expected.Height == actual.Height;
        using var diff = new SKBitmap(Math.Max(expected.Width, actual.Width), Math.Max(expected.Height, actual.Height));
        var changed = 0;
        for (var y = 0; y < diff.Height; y++)
        for (var x = 0; x < diff.Width; x++)
        {
            var inActual = x < actual.Width && y < actual.Height;
            var inExpected = x < expected.Width && y < expected.Height;
            var pixel = inActual ? actual.GetPixel(x, y) : SKColors.Transparent;
            var different = !inActual || !inExpected || pixel != expected.GetPixel(x, y);
            diff.SetPixel(x, y, different ? SKColors.Magenta : pixel.WithAlpha(60));
            if (different) changed++;
        }
        if (sameSize && changed == 0) return;
        File.Copy(baselinePath, Path.Combine(directory, "expected.png"), true);
        Save(actual, Path.Combine(directory, "actual.png"));
        Save(diff, Path.Combine(directory, "diff.png"));
        throw new InvalidOperationException($"Component differs from {baselineName}: {changed} pixels changed; " +
            $"expected={expected.Width}x{expected.Height}, actual={actual.Width}x{actual.Height}. Expected/actual/diff: {directory}");
    }

    private static void VerifyEnvironment(string baselinePath, string actualPath)
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(baselinePath));
        using var actual = JsonDocument.Parse(File.ReadAllText(actualPath));
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException($"Importance baselines require Windows; actual metadata: {actualPath}");
        foreach (var key in new[] { "Avalonia", "Skia", "Font", "FontSize", "Scale", "WindowWidth", "WindowHeight", "Theme", "Culture" })
        {
            var expectedValue = expected.RootElement.GetProperty(key).GetRawText();
            var actualValue = actual.RootElement.GetProperty(key).GetRawText();
            if (expectedValue != actualValue)
                throw new InvalidOperationException($"Baseline environment differs at {key}: {expectedValue} != {actualValue}. " +
                    $"Metadata: {baselinePath}; {actualPath}");
        }
    }
}
