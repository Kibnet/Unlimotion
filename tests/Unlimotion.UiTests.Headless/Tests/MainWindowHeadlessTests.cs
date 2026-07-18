using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using TUnit.Assertions;
using TUnit.Core;
using Unlimotion.AppAutomation.TestHost;
using Unlimotion.UiTests.Authoring.Pages;
using Unlimotion.UiTests.Authoring.Tests;

namespace Unlimotion.UiTests.Headless.Tests;

[InheritsTests]
public sealed class MainWindowHeadlessTests
    : StatusContractScenariosBase<MainWindowHeadlessTests.HeadlessRuntimeSession>
{
    protected override HeadlessRuntimeSession LaunchSession()
    {
        var isStatusContract = IsStatusContractScenarioTest;
        return new HeadlessRuntimeSession(
            DesktopAppSession.Launch(
                UnlimotionAppLaunchHost.CreateHeadlessLaunchOptions(
                    isStatusContract
                        ? UnlimotionAutomationScenario.StatusContract
                        : UnlimotionAutomationScenario.Smoke,
                    language: isStatusContract ? StatusContractLanguage : null,
                    currentTaskId: isStatusContract ? StatusContractCurrentTaskId : null,
                    theme: isStatusContract ? StatusContractTheme : null)));
    }

    protected override MainWindowPage CreatePage(HeadlessRuntimeSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
    }

    protected override StatusContractWindowSnapshot GetStatusContractWindowSnapshot()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            return new StatusContractWindowSnapshot(
                Environment.ProcessId,
                window.Title ?? string.Empty,
                window.Position.X,
                window.Position.Y,
                window.Width,
                window.Height);
        });
    }

    protected override bool SupportsStatusContractScreenshotCapture => false;

    protected override void CaptureStatusContractScreenshot(string outputPath) =>
        throw new NotSupportedException(
            "The semantic Headless backend does not produce pixels; use FlaUI status-contract tests for screenshots.");

    protected override string DescribeStatusContractRuntimeState()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            var tasks = viewModel.taskRepository?.Tasks.Items
                .Select(task => $"{task.Id}:{task.Status}:{task.ArchiveDateTime:O}")
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray() ?? [];
            var archivedItems = viewModel.ArchivedItems
                .Select(item => $"{item.Id}:{item.TaskItem.Status}:{item.TaskItem.ArchiveDateTime:O}")
                .ToArray();
            return $"ArchivedMode={viewModel.ArchivedMode}; " +
                   $"Date={viewModel.ArchivedDateFilter.From:O}..{viewModel.ArchivedDateFilter.To:O}; " +
                   $"Tasks=[{string.Join(", ", tasks)}]; " +
                   $"ArchivedItems=[{string.Join(", ", archivedItems)}]";
        });
    }

    protected override bool IsArchivedContractTaskVisible()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            return viewModel.ArchivedItems.Any(item => string.Equals(
                item.Id,
                UnlimotionAutomationScenarioData.StatusContractArchivedTaskId,
                StringComparison.Ordinal));
        });
    }

    protected override void OpenArchivedTab()
    {
        HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            viewModel.ArchivedDateFilter.CurrentOption = Unlimotion.ViewModel.DateFilterDefinition.AllTime;
            viewModel.ArchivedDateFilter.SetDateTimes(Unlimotion.ViewModel.DateFilterDefinition.AllTime);
            Dispatcher.UIThread.RunJobs();
        });

        base.OpenArchivedTab();
    }

    protected override void SelectArchivedContractTask()
    {
        var tree = GetNativeControl<TreeView>(Page.ArchivedTree);
        HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            var item = viewModel.ArchivedItems.Single(wrapper => string.Equals(
                wrapper.Id,
                UnlimotionAutomationScenarioData.StatusContractArchivedTaskId,
                StringComparison.Ordinal));
            tree.SelectedItem = item;
            Dispatcher.UIThread.RunJobs();
        });
    }

    protected override void OpenStatusPicker()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        InvokeNativeButton(statusPicker);
    }

    protected override StatusContractOptionObservation ObserveOpenStatusOption(string automationId)
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        return HeadlessRuntime.Dispatch(() =>
        {
            Dispatcher.UIThread.RunJobs();
            var flyout = statusPicker.Flyout as MenuFlyout
                ?? throw new InvalidOperationException("Current task status flyout was not created.");
            var option = flyout.Items
                .OfType<MenuItem>()
                .SingleOrDefault(item => string.Equals(
                    AutomationProperties.GetAutomationId(item),
                    automationId,
                    StringComparison.Ordinal));
            if (option is null)
            {
                return StatusContractOptionObservation.Missing(automationId);
            }

            var header = option.Header as Control;
            var displayedText = string.Join(
                "\n",
                (header is null
                    ? Enumerable.Empty<ILogical>()
                    : header.GetLogicalDescendants().Prepend(header))
                    .OfType<TextBlock>()
                    .Where(static text => text.IsVisible && !string.IsNullOrWhiteSpace(text.Text))
                    .Select(static text => text.Text!));
            return new StatusContractOptionObservation(
                Visible: option.IsVisible,
                Enabled: option.IsEnabled,
                AutomationId: AutomationProperties.GetAutomationId(option) ?? string.Empty,
                HelpText: AutomationProperties.GetHelpText(option) ?? string.Empty,
                DisplayedText: displayedText,
                ShowOnDisabled: ToolTip.GetShowOnDisabled(option));
        });
    }

    protected override void CloseStatusPicker()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        HeadlessRuntime.Dispatch(() =>
        {
            statusPicker.Flyout?.Hide();
            Dispatcher.UIThread.RunJobs();
        });
    }

    protected override string GetRenderedStatusContractTheme()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        return HeadlessRuntime.Dispatch(() => statusPicker.ActualThemeVariant == ThemeVariant.Dark
            ? "Dark"
            : statusPicker.ActualThemeVariant == ThemeVariant.Light
                ? "Light"
                : statusPicker.ActualThemeVariant?.ToString() ?? string.Empty);
    }

    protected override string OpenActionsAndInvokeArchiveCommand()
    {
        var actionsButton = GetNativeControl<DropDownButton>(Page.CurrentTaskActionsMenuButton);
        InvokeNativeButton(actionsButton);
        return HeadlessRuntime.Session.Dispatch(async () =>
        {
            Dispatcher.UIThread.RunJobs();
            var flyout = actionsButton.Flyout as MenuFlyout
                ?? throw new InvalidOperationException("Current task actions flyout was not created.");
            var menuItem = flyout.Items
                .OfType<MenuItem>()
                .Single(item => string.Equals(
                    AutomationProperties.GetAutomationId(item),
                    "CurrentTaskArchiveMenuItem",
                    StringComparison.Ordinal));
            var taskViewModel = actionsButton.DataContext as Unlimotion.ViewModel.TaskItemViewModel
                ?? throw new InvalidOperationException("Current task actions button did not expose TaskItemViewModel.");
            var label = menuItem.Header?.ToString();
            if (string.IsNullOrWhiteSpace(label))
            {
                // A detached headless MenuFlyout does not materialize its header binding. The
                // desktop/FlaUI path still verifies the rendered UIA name; use the same binding
                // source here while exercising the native menu item's command contract below.
                label = taskViewModel.ArchiveCommandTitle;
            }
            // Detached headless flyouts do not materialize command bindings. FlaUI verifies the
            // rendered menu-item binding; the headless path invokes the same public binding source.
            var command = menuItem.Command ?? taskViewModel.ArchiveCommand
                ?? throw new InvalidOperationException("Current task archive command was unavailable.");
            if (!command.CanExecute(menuItem.CommandParameter))
            {
                throw new InvalidOperationException("Current task archive menu command could not execute.");
            }

            var reactiveCommand = command as ReactiveCommand<Unit, Unit>
                ?? throw new InvalidOperationException("Current task archive command was not a ReactiveCommand.");
            await reactiveCommand.Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(10));
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            return label;
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    protected override void SelectStatusContractTask(string taskId, string title)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            var task = viewModel.taskRepository?.Tasks.Items.Single(item => string.Equals(
                item.Id,
                taskId,
                StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Status-contract task '{taskId}' was not loaded.");
            viewModel.CurrentTaskItem = task;
            viewModel.SelectCurrentTask();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task StatusContract_RussianDarkFutureAndBlocker()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        var darkThemeApplied = HeadlessRuntime.Dispatch(() =>
            statusPicker.ActualThemeVariant == ThemeVariant.Dark);

        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractTerminalTaskTitle);
        OpenStatusPicker();
        var terminalInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var terminalArchived = ObserveOpenStatusOption("TaskStatusOptionArchived");
        CloseStatusPicker();

        SelectStatusContractTask(
            UnlimotionAutomationScenarioData.StatusContractFutureTaskId,
            UnlimotionAutomationScenarioData.StatusContractFutureTaskTitle);
        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractFutureTaskTitle);
        var futureOpacity = GetCurrentStatusPickerOpacity();
        OpenStatusPicker();
        var futureInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var futureCompleted = ObserveOpenStatusOption("TaskStatusOptionCompleted");
        CloseStatusPicker();

        SelectStatusContractTask(
            UnlimotionAutomationScenarioData.StatusContractBlockedTaskId,
            UnlimotionAutomationScenarioData.StatusContractBlockedTaskTitle);
        WaitForCurrentTaskTitle(UnlimotionAutomationScenarioData.StatusContractBlockedTaskTitle);
        var blockedOpacity = GetCurrentStatusPickerOpacity();
        OpenStatusPicker();
        var blockedInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        var blockedCompleted = ObserveOpenStatusOption("TaskStatusOptionCompleted");
        CloseStatusPicker();

        await Assert.That(darkThemeApplied)
            .IsTrue()
            .Because("The RU status-contract matrix must render under the configured dark theme.");
        await AssertDisabledStatusOption(
            terminalInProgress,
            "TaskStatusOptionInProgress",
            "Выполненную или архивную задачу нельзя запустить. Сначала верните задачу в активный статус.");
        await AssertDisabledStatusOption(
            terminalArchived,
            "TaskStatusOptionArchived",
            "Выполненную задачу нельзя архивировать. Сначала верните задачу в активный статус.");
        await AssertDisabledStatusOption(
            futureInProgress,
            "TaskStatusOptionInProgress",
            "Задачу нельзя начать раньше плановой даты начала.");
        await Assert.That(futureCompleted.Enabled)
            .IsTrue()
            .Because("A future planned begin blocks start only, not completion when other guards pass.");
        await Assert.That(Math.Abs(futureOpacity - 1d) < 0.001d)
            .IsTrue()
            .Because("A future planned begin must not dim a graph-available task.");
        await AssertDisabledStatusOption(
            blockedInProgress,
            "TaskStatusOptionInProgress",
            "Сначала выполните прямые блокирующие задачи.");
        await AssertDisabledStatusOption(
            blockedCompleted,
            "TaskStatusOptionCompleted",
            "Сначала выполните прямые блокирующие задачи.");
        await Assert.That(Math.Abs(blockedOpacity - 0.4d) < 0.001d)
            .IsTrue()
            .Because("An active graph blocker must dim the task to opacity 0.4.");

        var russianArchiveTitle = GetCurrentArchiveCommandTitle();
        var englishArchiveTitle = string.Empty;
        StatusContractOptionObservation? englishBlockedInProgress = null;
        OpenStatusPicker();
        try
        {
            SetStatusContractLanguage(Unlimotion.ViewModel.Localization.LocalizationService.EnglishLanguage);
            englishArchiveTitle = GetCurrentArchiveCommandTitle();
            englishBlockedInProgress = ObserveOpenStatusOption("TaskStatusOptionInProgress");
        }
        finally
        {
            CloseStatusPicker();
            SetStatusContractLanguage(Unlimotion.ViewModel.Localization.LocalizationService.RussianLanguage);
        }

        await Assert.That(russianArchiveTitle).IsEqualTo("Архивировать");
        await Assert.That(englishArchiveTitle).IsEqualTo("Archive");
        await Assert.That(englishBlockedInProgress).IsNotNull();
        await AssertDisabledStatusOption(
            englishBlockedInProgress!,
            "TaskStatusOptionInProgress",
            "Complete this task's direct blockers before starting or completing it.");
        await Assert.That(englishBlockedInProgress!.DisplayedText)
            .Contains("In progress")
            .Because("An already-created status option must refresh its visible title after a language switch.");
    }

    private double GetCurrentStatusPickerOpacity()
    {
        var statusPicker = GetNativeControl<TaskStatusPicker>(Page.CurrentTaskStatusButton);
        return HeadlessRuntime.Dispatch(() => statusPicker.Opacity);
    }

    private string GetCurrentArchiveCommandTitle()
    {
        return HeadlessRuntime.Dispatch(() =>
        {
            var viewModel = Session.Inner.MainWindow.DataContext as Unlimotion.ViewModel.MainWindowViewModel
                ?? throw new InvalidOperationException("Headless status-contract window did not expose MainWindowViewModel.");
            return viewModel.CurrentTaskItem?.ArchiveCommandTitle
                ?? throw new InvalidOperationException("Headless status-contract window did not expose a current task.");
        });
    }

    private void SetStatusContractLanguage(string language)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            Unlimotion.ViewModel.Localization.LocalizationService.Current.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private static TControl GetNativeControl<TControl>(IUiControl wrappedControl)
        where TControl : Control
    {
        var innerProperty = FindProperty(
            wrappedControl.GetType(),
            "Inner",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var automationElement = innerProperty?.GetValue(wrappedControl)
            ?? throw new InvalidOperationException(
                $"Headless wrapper for '{wrappedControl.AutomationId}' did not expose its native automation element.");
        var controlProperty = FindProperty(
            automationElement.GetType(),
            "Control",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var control = controlProperty?.GetValue(automationElement) as Control;
        return control as TControl
            ?? throw new InvalidOperationException(
                $"Headless control '{wrappedControl.AutomationId}' was not a {typeof(TControl).Name}; actual type: {control?.GetType().FullName ?? "<missing>"}.");
    }

    private static void InvokeNativeButton(Button button)
    {
        HeadlessRuntime.Dispatch(() =>
        {
            var onClick = FindMethod(
                button.GetType(),
                "OnClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (onClick is null)
            {
                throw new InvalidOperationException(
                    $"Headless button '{AutomationProperties.GetAutomationId(button)}' did not expose OnClick.");
            }

            onClick.Invoke(button, []);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private static PropertyInfo? FindProperty(Type type, string name, BindingFlags bindingFlags)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, bindingFlags);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name, BindingFlags bindingFlags)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(name, bindingFlags | BindingFlags.DeclaredOnly);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    public sealed class HeadlessRuntimeSession : IUiTestSession
    {
        public HeadlessRuntimeSession(DesktopAppSession inner)
        {
            Inner = inner;
        }

        public DesktopAppSession Inner { get; }

        public void Dispose()
        {
            Inner.Dispose();
        }
    }
}
