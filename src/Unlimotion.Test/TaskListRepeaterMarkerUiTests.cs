using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;

namespace Unlimotion.Test;

[ParallelLimiter<SharedUiStateParallelLimit>]
public class TaskListRepeaterMarkerUiTests
{
    [Test]
    public async Task TaskListRepeaterMarker_AllTasksTree_ShowsMarkerForRepeaterTask()
    {
        var localization = LocalizationService.Current;
        var culture = CultureSnapshot.Capture();

        try
        {
            CultureSnapshot.Apply(CultureInfo.GetCultureInfo(LocalizationService.RussianLanguage));

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

                    var repeatedTask = TestHelpers.GetTask(vm, MainWindowViewModelFixture.RepeateTask9Id);
                    await Assert.That(repeatedTask).IsNotNull();
                    await Assert.That(repeatedTask!.IsHaveRepeater).IsTrue();

                    var view = new MainControl { DataContext = vm };
                    window = CreateWindow(view);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();

                    TextBlock? marker = null;
                    var markerReady = WaitFor(() =>
                    {
                        marker = view.GetVisualDescendants()
                            .OfType<TextBlock>()
                            .FirstOrDefault(textBlock =>
                                AutomationProperties.GetAutomationId(textBlock) == "TaskRepeaterListMarker"
                                && textBlock.DataContext is TaskItemViewModel task
                                && task.Id == MainWindowViewModelFixture.RepeateTask9Id);

                        return marker != null;
                    });

                    await Assert.That(markerReady).IsTrue();
                    await Assert.That(marker!.Text).IsEqualTo("↻");
                    await Assert.That(marker.IsVisible).IsTrue();
                }
                finally
                {
                    window?.Close();
                    fixture.CleanTasks();
                }
            }, CancellationToken.None);
        }
        finally
        {
            LocalizationService.Current = localization;
            culture.Restore();
        }
    }

    [Test]
    public async Task TaskListRepeaterMarker_XamlTemplates_AddMarkerBeforeEveryInlineTitle()
    {
        var xaml = File.ReadAllText(FindViewXamlPath("MainControl.axaml"));

        var inlineTitleCount = CountOccurrences(xaml, "EmojiText=\"{Binding TaskItem.Title}\"");
        var inlineMarkerCount = CountOccurrences(xaml, "Text=\"{Binding TaskItem.RepeaterListMarker}\"");

        await Assert.That(xaml).Contains("Text=\"{Binding RepeaterListMarker}\"");
        await Assert.That(xaml).Contains("ToolTip.Tip=\"{Binding RepeaterListMarkerToolTip}\"");
        await Assert.That(inlineTitleCount).IsEqualTo(6);
        await Assert.That(inlineMarkerCount).IsEqualTo(inlineTitleCount);
    }

    [Test]
    public async Task TaskRepeaterMarker_GraphTemplate_AddsMarkerBeforeTitle()
    {
        var xaml = File.ReadAllText(FindViewXamlPath("GraphControl.axaml"));

        await Assert.That(xaml).Contains("Text=\"{Binding RepeaterListMarker}\"");
        await Assert.That(xaml).Contains("IsVisible=\"{Binding IsHaveRepeater}\"");
        await Assert.That(xaml).Contains("ToolTip.Tip=\"{Binding RepeaterListMarkerToolTip}\"");
        await Assert.That(xaml).Contains("AutomationProperties.AutomationId=\"TaskRepeaterGraphMarker\"");
        await Assert.That(xaml).Contains("nodify:NodifyEditor");
        await Assert.That(xaml.IndexOf("Text=\"{Binding RepeaterListMarker}\"", StringComparison.Ordinal))
            .IsLessThan(xaml.IndexOf("Text=\"{Binding TitleWithoutEmoji}\"", StringComparison.Ordinal));
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

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class CultureSnapshot
    {
        private readonly CultureInfo _currentCulture;
        private readonly CultureInfo _currentUiCulture;
        private readonly CultureInfo? _defaultThreadCurrentCulture;
        private readonly CultureInfo? _defaultThreadCurrentUiCulture;

        private CultureSnapshot()
        {
            _currentCulture = Thread.CurrentThread.CurrentCulture;
            _currentUiCulture = Thread.CurrentThread.CurrentUICulture;
            _defaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
            _defaultThreadCurrentUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        }

        public static CultureSnapshot Capture() => new();

        public static void Apply(CultureInfo culture)
        {
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        public void Restore()
        {
            CultureInfo.DefaultThreadCurrentCulture = _defaultThreadCurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultThreadCurrentUiCulture;
            Thread.CurrentThread.CurrentCulture = _currentCulture;
            Thread.CurrentThread.CurrentUICulture = _currentUiCulture;
        }
    }
}
