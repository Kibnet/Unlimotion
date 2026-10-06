using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
using Unlimotion.Views;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class CliStatusJournalReloadUiTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StrictObservation_LeavesPendingJournalUntouched_UiReloadRecoversOwnCard(bool committed)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            FileStorage storage = null!;
            var fixture = new MainWindowViewModelFixture(path =>
                new UnifiedTaskStorage(new TaskTreeManager(storage = new FileStorage(path))));
            Window? window = null;
            try
            {
                var owner = fixture.MainWindowViewModelTest;
                await owner.Connect();
                var card = TestHelpers.GetTask(owner, MainWindowViewModelFixture.RootTask2Id);
                owner.CurrentTaskItem = card;
                owner.DetailsAreOpen = true;
                owner.SelectCurrentTask();
                var view = new MainControl { DataContext = owner };
                window = new Window { Width = 1400, Height = 900, Content = view };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var source = Path.Combine(fixture.DefaultTasksFolderPath, card.Id);
                var original = JsonConvert.DeserializeObject<TaskItem>(await File.ReadAllTextAsync(source))!;
                var before = JsonConvert.SerializeObject(original with { Title = "Before pending CLI mutation" });
                var after = JsonConvert.SerializeObject(original with
                {
                    Title = "After committed CLI mutation", Status = DomainTaskStatus.Completed
                });
                await File.WriteAllTextAsync(source, JsonConvert.SerializeObject(original with { Title = "Partial mutation" }));
                var journals = Path.Combine(fixture.DefaultTasksFolderPath, ".unlimotion.transactions");
                Directory.CreateDirectory(journals);
                var journal = Path.Combine(journals, "pending.json");
                await File.WriteAllTextAsync(journal, JsonConvert.SerializeObject(new
                {
                    Id = "pending", Committed = committed,
                    Entries = new[] { new
                    {
                        TaskId = card.Id, FilePath = source, BeforeExists = true, AfterExists = true,
                        BeforeBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(before)),
                        AfterBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(after))
                    } }
                }));
                var receipts = Path.Combine(fixture.DefaultTasksFolderPath, ".unlimotion.applies", "v1");
                Directory.CreateDirectory(receipts);
                var receipt = Path.Combine(receipts, "unrelated.json");
                await File.WriteAllTextAsync(receipt, "{\"applicationId\":\"unrelated\"}");
                var paths = new[] { source, journal, receipt };
                foreach (var path in paths)
                    File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                var bytes = paths.ToDictionary(path => path, File.ReadAllBytes);
                var times = paths.ToDictionary(path => path, File.GetLastWriteTimeUtc);

                try
                {
                    await storage.ReadObservationAsync();
                    throw new InvalidOperationException("Strict observation unexpectedly accepted a pending journal.");
                }
                catch (TaskGraphObservationException exception)
                {
                    await Assert.That(exception.Kind).IsEqualTo("recoveryRequired");
                }
                foreach (var path in paths)
                {
                    await Assert.That(File.ReadAllBytes(path).SequenceEqual(bytes[path])).IsTrue();
                    await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(times[path]);
                }

                var actions = view.GetVisualDescendants().OfType<DropDownButton>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "CurrentTaskActionsMenuButton");
                var flyout = (MenuFlyout)actions.Flyout!;
                flyout.ShowAt(actions);
                Dispatcher.UIThread.RunJobs();
                var reload = flyout.Items.OfType<MenuItem>().Single(item =>
                    AutomationProperties.GetAutomationId(item) == "CurrentTaskReloadButton");
                await Assert.That(reload.IsEffectivelyVisible && reload.IsEnabled).IsTrue();
                await Assert.That(reload.Command).IsSameReferenceAs(card.ReloadTaskCommand);
                reload.BringIntoView();
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
                var inputRoot = TopLevel.GetTopLevel(reload) ?? window;
                inputRoot.UpdateLayout();
                var point = reload.TranslatePoint(new Point(reload.Bounds.Width / 2, reload.Bounds.Height / 2), inputRoot)
                    ?? throw new InvalidOperationException("Reload menu is not attached to an input surface.");
                inputRoot.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                inputRoot.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                var expectedTitle = committed ? "After committed CLI mutation" : "Before pending CLI mutation";
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (card.Title != expectedTitle || !card.CanReloadTask)
                {
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("UI Reload did not recover its card.");
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                }
                window.UpdateLayout();
                var title = view.GetVisualDescendants().OfType<TextBox>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == "CurrentTaskTitleTextBox");
                await Assert.That(title.Text).IsEqualTo(expectedTitle);
                await Assert.That(card.Status).IsEqualTo(committed ? DomainTaskStatus.Completed : original.Status);
                await Assert.That(owner.CurrentTaskItem).IsSameReferenceAs(card);
                await Assert.That(File.Exists(journal)).IsFalse();
                await Assert.That(await File.ReadAllTextAsync(source)).IsEqualTo(committed ? after : before);
                await Assert.That(File.ReadAllBytes(receipt).SequenceEqual(bytes[receipt])).IsTrue();
                await Assert.That(File.GetLastWriteTimeUtc(receipt)).IsEqualTo(times[receipt]);
            }
            finally
            {
                window?.Close();
                await fixture.CleanTasksAsync();
            }
        }, CancellationToken.None);
    }
}
