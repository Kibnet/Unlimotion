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

[ParallelLimiter<SharedUiStateParallelLimit>]
public class BreadcrumbEmojiUiTests
{
    [Test]
    public async Task Breadcrumbs_ShouldRenderEmojiRunsWithEmojiFont()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
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

                var breadcrumbs = view.FindControl<EmojiTextBlock>("BreadcrumbsTextBlock");

                await Assert.That(breadcrumbs).IsNotNull();
                await Assert.That(WaitFor(() => breadcrumbs!.Inlines.Count > 0)).IsTrue();

                var runs = breadcrumbs!.Inlines.OfType<Run>().ToList();
                var text = string.Concat(runs.Select(run => run.Text));
                var emojiRuns = runs.Where(run => run.Text is "📚" or "🧪").ToList();

                await Assert.That(text).IsEqualTo("📚 Root Task 2 / 🧪 Sub Task 22");
                await Assert.That(emojiRuns.Count).IsEqualTo(2);
                await Assert.That(emojiRuns.All(run => run.FontWeight == Avalonia.Media.FontWeight.Normal)).IsTrue();
                await Assert.That(emojiRuns.All(run =>
                    run.FontFamily?.ToString()?.Contains("Noto Color Emoji", StringComparison.Ordinal) == true)).IsTrue();
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
