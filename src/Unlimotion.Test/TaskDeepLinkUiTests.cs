using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel]
public class TaskDeepLinkUiTests
{
    [Test]
    [Arguments("ru", false)]
    [Arguments("en", false)]
    [Arguments("ru", true)]
    public async Task ColdAndWarmActivation_ShowMainCard_PreserveFiltersAndMissingTask(string language, bool filtered)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ImportanceRenderedAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new MainWindowViewModelFixture();
            var vm = fixture.MainWindowViewModelTest;
            var previousLanguage = LocalizationService.Current.LanguageMode;
            Window? window = null;
            try
            {
                Localization.SetLanguage(language);
                var targetId = MainWindowViewModelFixture.RootTask2Id;
                using var source = new AvaloniaTaskDeepLinkActivationSource();
                source.ProcessActivation(new Avalonia.Controls.ApplicationLifetimes.ProtocolActivatedEventArgs(
                    new Uri("unlimotion://task/" + targetId)));
                using var coordinator = new TaskDeepLinkActivationCoordinator(
                    () => vm.IsInitialized,
                    link => App.ActivateTaskDeepLink(vm, link, window),
                    action => Dispatcher.UIThread.Post(action));
                coordinator.Attach(source);
                await Assert.That(vm.IsInitialized).IsFalse();
                await vm.Connect();
                var screen = new MainScreen { DataContext = vm };
                window = new Window { Content = screen, Width = 1280, Height = 800 };
                window.Show();
                if (filtered) vm.Search.SearchText = "deep-link-unmatched-fixture";
                coordinator.ProcessPending();
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                var target = TestHelpers.GetTask(vm, targetId);
                var title = screen.GetVisualDescendants().OfType<TextBox>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "CurrentTaskTitleTextBox");
                await Assert.That(title.IsEffectivelyVisible).IsTrue();
                await Assert.That(title.Text).IsEqualTo(target.Title);
                await Assert.That(vm.CurrentTaskItem!.Id).IsEqualTo(targetId);
                await Assert.That(vm.DetailsAreOpen).IsTrue();
                if (filtered) await Assert.That(vm.Search.SearchText).IsEqualTo("deep-link-unmatched-fixture");

                if (Environment.GetEnvironmentVariable("UNLIMOTION_DEEP_LINK_RENDERED_EVIDENCE") == "1")
                {
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("No rendered Headless frame.");
                    var directory = Path.GetFullPath(Path.Combine("artifacts", "deep-links", "screenshots"));
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, $"opened-{language}-filtered-{filtered}-{Guid.NewGuid():N}.png");
                    frame.Save(path);
                    Console.WriteLine("Deep link screenshot: " + path);
                }

                var nextId = MainWindowViewModelFixture.RootTask1Id;
                window.WindowState = WindowState.Minimized;
                source.ProcessActivation(new Avalonia.Controls.ApplicationLifetimes.ProtocolActivatedEventArgs(
                    new Uri("unlimotion://task/" + nextId)));
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(150);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(vm.CurrentTaskItem!.Id).IsEqualTo(nextId);
                await Assert.That(title.Text).IsEqualTo(TestHelpers.GetTask(vm, nextId).Title);
                await Assert.That(window.WindowState).IsEqualTo(WindowState.Normal);
                var taskCount = vm.taskRepository!.Tasks.Count;
                source.ProcessActivation(new Avalonia.Controls.ApplicationLifetimes.ProtocolActivatedEventArgs(
                    new Uri("unlimotion://task/missing-fixture")));
                Dispatcher.UIThread.RunJobs();
                await Assert.That(vm.CurrentTaskItem!.Id).IsEqualTo(nextId);
                await Assert.That(vm.taskRepository.Tasks.Count).IsEqualTo(taskCount);
                var notification = (NotificationManagerWrapperMock)vm.ManagerWrapper;
                await Assert.That(notification.LastErrorMessage).IsEqualTo(
                    Localization.Format("TaskDeepLinkTaskNotFound", "missing-fixture"));
                await Assert.That(notification.LastErrorMessage).Contains(language == "ru" ? "не найдена" : "was not found");
            }
            finally
            {
                window?.Close();
                Localization.SetLanguage(previousLanguage);
            }
        }, System.Threading.CancellationToken.None);
    }
}
