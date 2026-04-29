using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel]
public class TaskImportanceUiTests
{
    [Test]
    public async Task WantedTaskTitle_ShouldBeBold_InAllTasksTreeAndParentRelationTree()
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
                vm.AllTasksMode = true;
                vm.DetailsAreOpen = true;

                var importantRoot = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                var currentTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.SubTask22Id);

                await Assert.That(importantRoot).IsNotNull();
                await Assert.That(currentTask).IsNotNull();

                importantRoot!.Wanted = true;
                vm.CurrentTaskItem = currentTask;
                vm.SelectCurrentTask();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var allTasksText = WaitForTaskTitleTextBlock(
                    view,
                    MainWindowViewModelFixture.RootTask2Id,
                    "AllTasksTree",
                    importantRoot.Title);
                var relationText = WaitForTaskTitleTextBlock(
                    view,
                    MainWindowViewModelFixture.RootTask2Id,
                    "CurrentItemParentsTree",
                    importantRoot.Title);

                await Assert.That(allTasksText.FontWeight).IsEqualTo(FontWeight.Bold);
                await Assert.That(relationText.FontWeight).IsEqualTo(FontWeight.Bold);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WantedTaskTitle_ShouldBeBold_InRoadmapGraph()
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
                vm.AllTasksMode = false;
                vm.GraphMode = true;

                var importantRoot = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(importantRoot).IsNotNull();

                importantRoot!.Wanted = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var graphControl = view.GetVisualDescendants().OfType<GraphControl>().FirstOrDefault();
                await Assert.That(graphControl).IsNotNull();

                var graphText = WaitForTaskTitleTextBlock(
                    graphControl!,
                    MainWindowViewModelFixture.RootTask2Id,
                    title: importantRoot.Title);

                await Assert.That(graphText.FontWeight).IsEqualTo(FontWeight.Bold);
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task WantedTaskTitle_WithMiddleEmoji_ShouldRenderEmojiRunNormal_InAllTasksTree()
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
                vm.AllTasksMode = true;

                var importantRoot = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RootTask2Id);
                await Assert.That(importantRoot).IsNotNull();

                importantRoot!.Title = "Important 📚 task";
                importantRoot.Wanted = true;

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var titleText = WaitForTaskTitleTextBlock(
                    view,
                    MainWindowViewModelFixture.RootTask2Id,
                    "AllTasksTree",
                    importantRoot.Title);

                var emojiText = titleText as EmojiTextBlock;
                await Assert.That(emojiText).IsNotNull();
                await Assert.That(titleText.FontWeight).IsEqualTo(FontWeight.Bold);

                var runs = emojiText!.Inlines.OfType<Run>().ToList();
                await Assert.That(string.Concat(runs.Select(run => run.Text))).IsEqualTo(importantRoot.Title);

                var emojiRun = runs.SingleOrDefault(run => run.Text == "📚");
                await Assert.That(emojiRun).IsNotNull();
                await Assert.That(emojiRun!.FontWeight).IsEqualTo(FontWeight.Normal);
                await Assert.That(emojiRun.FontFamily.ToString()).Contains("Noto Color Emoji");
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task TaskItemTemplate_ShouldUseEmojiTextBlockForTitle()
    {
        var xaml = File.ReadAllText(FindViewXamlPath("MainControl.axaml"));
        var titleLine = xaml
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Single(line => line.Contains("<unlimotion:EmojiTextBlock Grid.Column=\"2\"", StringComparison.Ordinal));

        await Assert.That(titleLine).Contains("EmojiText=\"{Binding Title}\"");
        await Assert.That(titleLine).Contains("Classes.IsWanted=\"{Binding Wanted}\"");
        await Assert.That(titleLine).Contains("Classes.IsCanBeCompleted=\"{Binding !IsCanBeCompleted}\"");
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

    private static TextBlock WaitForTaskTitleTextBlock(
        Control root,
        string taskId,
        string? ancestorAutomationId = null,
        string? title = null,
        int timeoutMilliseconds = 3000)
    {
        TextBlock? textBlock = null;
        var ready = SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            textBlock = FindTaskTitleTextBlock(root, taskId, ancestorAutomationId, title);
            return textBlock != null;
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));

        if (!ready || textBlock == null)
        {
            throw new InvalidOperationException($"Title TextBlock for task '{taskId}' was not found.");
        }

        return textBlock;
    }

    private static TextBlock? FindTaskTitleTextBlock(
        Control root,
        string taskId,
        string? ancestorAutomationId,
        string? title)
    {
        var descendants = root.GetVisualDescendants().OfType<TextBlock>();
        if (!string.IsNullOrWhiteSpace(ancestorAutomationId))
        {
            descendants = descendants.Where(textBlock =>
                textBlock.FindAncestorOfType<Control>(includeSelf: true) is { } control &&
                HasAncestorWithAutomationId(control, ancestorAutomationId));
        }

        return descendants.FirstOrDefault(textBlock =>
            textBlock.DataContext is TaskItemViewModel task &&
            task.Id == taskId &&
            MatchesTitle(textBlock, title));
    }

    private static bool MatchesTitle(TextBlock textBlock, string? title)
    {
        if (title == null || textBlock.Text == title)
        {
            return true;
        }

        if (textBlock is not EmojiTextBlock emojiTextBlock)
        {
            return false;
        }

        var inlines = emojiTextBlock.Inlines;
        return emojiTextBlock.EmojiText == title ||
               (inlines != null && string.Concat(inlines.OfType<Run>().Select(run => run.Text)) == title);
    }

    private static string FindViewXamlPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var sourceRootCandidate = Path.Combine(directory.FullName, "src", "Unlimotion", "Views", fileName);
            if (File.Exists(sourceRootCandidate))
            {
                return sourceRootCandidate;
            }

            var srcDirectoryCandidate = Path.Combine(directory.FullName, "Unlimotion", "Views", fileName);
            if (File.Exists(srcDirectoryCandidate))
            {
                return srcDirectoryCandidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot find {fileName} from test output directory.");
    }

    private static bool HasAncestorWithAutomationId(Control control, string automationId)
    {
        for (Control? current = control; current != null; current = current.Parent as Control)
        {
            if (Avalonia.Automation.AutomationProperties.GetAutomationId(current) == automationId)
            {
                return true;
            }
        }

        return false;
    }
}
