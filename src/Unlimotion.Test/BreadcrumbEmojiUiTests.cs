using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Threading;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class BreadcrumbEmojiUiTests
{
    [Test]
    public async Task Breadcrumbs_ShouldRenderEmojiRunsWithEmojiFont()
    {
        using var phases = new TestScenarioPhases(nameof(Breadcrumbs_ShouldRenderEmojiRunsWithEmojiFont));
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            Window? window = null;

            try
            {
                // The executable breadcrumbs BDD covers real task selection/path binding.
                // This case isolates the production text renderer and its font runs.
                var breadcrumbs = new EmojiTextBlock
                {
                    EmojiText = "📚 Root Task 2 / 🧪 Sub Task 22",
                    FontWeight = Avalonia.Media.FontWeight.Bold
                };
                window = CreateWindow(breadcrumbs);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                phases.Next("body");

                await Assert.That(breadcrumbs).IsNotNull();
                await Assert.That(WaitFor(() => breadcrumbs!.Inlines.Count > 0)).IsTrue();

                var runs = breadcrumbs!.Inlines.OfType<Run>().ToList();
                var text = string.Concat(runs.Select(run => run.Text));
                var emojiRuns = runs.Where(run => run.Text is "📚" or "🧪").ToList();

                await Assert.That(text).IsEqualTo("📚 Root Task 2 / 🧪 Sub Task 22");
                await Assert.That(emojiRuns.Count).IsEqualTo(2);
                await Assert.That(emojiRuns.All(run => run.FontWeight == Avalonia.Media.FontWeight.Normal)).IsTrue();
                await Assert.That(emojiRuns.All(run =>
                    run.FontFamily?.ToString()?.Contains("Emoji", StringComparison.Ordinal) == true)).IsTrue();
            }
            finally
            {
                phases.Next("cleanup");
                window?.Close();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1400,
            Height = 900,
            Content = content
        };
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }
}
