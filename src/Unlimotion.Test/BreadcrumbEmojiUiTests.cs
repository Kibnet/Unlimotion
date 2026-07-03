using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var parentTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id)!;
                var childTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id)!;

                parentTask.Title = "📚 Root Task 2";
                childTask.Title = "🧪 Sub Task 22";
                vm.AllTasksMode = false;
                vm.CurrentTaskItem = childTask;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var breadcrumbs = view.FindControl<ItemsControl>("BreadcrumbsTextBlock");

                await Assert.That(breadcrumbs).IsNotNull();
                // The breadcrumb is now a list of clickable crumbs — one EmojiTextBlock per ancestor.
                await Assert.That(WaitFor(() => breadcrumbs!.GetVisualDescendants()
                    .OfType<EmojiTextBlock>()
                    .SelectMany(crumb => crumb.Inlines ?? new InlineCollection())
                    .OfType<Run>()
                    .Any())).IsTrue();

                var crumbs = breadcrumbs!.GetVisualDescendants().OfType<EmojiTextBlock>().ToList();
                var titles = crumbs.Select(crumb =>
                    string.Concat(crumb.Inlines!.OfType<Run>().Select(run => run.Text)));
                var runs = crumbs.SelectMany(crumb => crumb.Inlines!.OfType<Run>()).ToList();
                var emojiRuns = runs.Where(run => run.Text is "📚" or "🧪").ToList();

                await Assert.That(string.Join(" / ", titles)).IsEqualTo("📚 Root Task 2 / 🧪 Sub Task 22");
                await Assert.That(emojiRuns.Count).IsEqualTo(2);
                await Assert.That(emojiRuns.All(run => run.FontWeight == Avalonia.Media.FontWeight.Normal)).IsTrue();
                await Assert.That(emojiRuns.All(run =>
                    run.FontFamily?.ToString()?.Contains("Emoji", StringComparison.Ordinal) == true)).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
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
