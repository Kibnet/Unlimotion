using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Vault;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedAreaRootTaskUiTests
{
    [Test]
    public async Task RootPickerClearAndSaveStayOutsidePortableCatalog()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            using var model = new AreaManagementViewModel(new AreaCatalogStore(vault)) { IsOpen = true };
            var roots = new Dictionary<string, AreaRootTaskReference?>();
            string? opened = null;
            model.ConfigureRootTasks(id => roots.GetValueOrDefault(id),
                (id, root) => { roots[id] = root; return Task.CompletedTask; },
                () => Task.FromResult<AreaRootTaskReference?>(new("root", "Рабочие задачи")), id => opened = id);
            await model.LoadAsync();
            var area = await model.CreateRootAsync("Работа");
            var view = new AreaManagement { DataContext = model };
            var window = new Window { Content = view, Width = 620, Height = 700 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var picker = Find<Button>(view, "AreaManagementPickRootTaskButton");
                await Assert.That(picker.IsEffectivelyVisible).IsTrue();
                await model.PickRootTaskCommand.Execute();
                await Assert.That(model.IsDraftDirty).IsTrue();
                await model.SaveSelectedAsync();
                await Assert.That(roots[area.Id]!.Id).IsEqualTo("root");
                await Assert.That(model.IsDraftDirty).IsFalse();
                await model.OpenRootTaskCommand.Execute();
                await Assert.That(opened).IsEqualTo("root");
                await model.ClearRootTaskCommand.Execute();
                await Assert.That(model.IsDraftDirty).IsTrue();
                await model.SaveSelectedAsync();
                await Assert.That(roots[area.Id]).IsNull();
                var catalog = await vault.ReadAsync(".unlimotion/areas.json");
                await Assert.That(catalog!.Text).DoesNotContain("rootTask");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task FailedRootSaveKeepsUnsavedDraftForRetry()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            using var model = new AreaManagementViewModel(new AreaCatalogStore(new FileNoteVault(directory.Path)));
            model.ConfigureRootTasks(_ => null, (_, _) => throw new System.IO.IOException("Disk full"),
                () => Task.FromResult<AreaRootTaskReference?>(null), _ => { });
            await model.LoadAsync();
            await model.CreateRootAsync("Работа");
            model.DraftRootTask = new("root", "Задачи");
            await Assert.That(() => model.SaveSelectedAsync()).Throws<System.IO.IOException>();
            await Assert.That(model.IsDraftDirty).IsTrue();
            await Assert.That(model.DraftRootTask!.Id).IsEqualTo("root");
        }, CancellationToken.None);
    }

    private static T Find<T>(Control view, string id) where T : Control =>
        view.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    [Test]
    public async Task AreaNavigationRequiresExplicitSaveDiscardOrCancel()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            using var model = new AreaManagementViewModel(new AreaCatalogStore(new FileNoteVault(directory.Path)));
            await model.LoadAsync();
            var first = await model.CreateRootAsync("Первая");
            var second = await model.CreateRootAsync("Вторая");
            await model.OpenAreaAsync(first.Id);
            model.DraftName = "Не сохранено";
            await model.OpenAreaAsync(second.Id);
            await Assert.That(model.HasPendingAreaSwitch).IsTrue();
            await Assert.That(model.SelectedArea!.Id).IsEqualTo(first.Id);
            await model.CancelAreaSwitchCommand.Execute();
            await Assert.That(model.DraftName).IsEqualTo("Не сохранено");
            await model.OpenAreaAsync(second.Id);
            await model.SaveAndSwitchAreaCommand.Execute();
            await Assert.That(model.SelectedArea!.Id).IsEqualTo(second.Id);
            await Assert.That(model.Areas.Single(area => area.Id == first.Id).Name).IsEqualTo("Не сохранено");
        }, CancellationToken.None);
    }

    [Test]
    public async Task AreaDraft_AutosavesWithoutTheRemovedSaveButton()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var store = new AreaCatalogStore(new FileNoteVault(directory.Path));
            using var model = new AreaManagementViewModel(store) { IsOpen = true };
            await model.LoadAsync();
            var area = await model.CreateRootAsync("Исходная");
            model.DraftName = "Сохранено автоматически";

            await Task.Delay(650);
            Dispatcher.UIThread.RunJobs();
            var saved = await store.LoadAsync();
            await Assert.That(saved.Catalog.Areas.Single(item => item.Id == area.Id).Name)
                .IsEqualTo("Сохранено автоматически");
            await Assert.That(model.IsDraftDirty).IsFalse();
        }, CancellationToken.None);
    }

    [Test]
    public async Task ReusedParentPickerEditsOnlyDraftAndKeepsManualOverride()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var owner = fixture.MainWindowViewModelTest;
            try
            {
                await owner.Connect();
                var initialCount = owner.taskRepository!.Tasks.Count;
                using var draft = new FeedTaskParentDraftViewModel(owner);
                var view = new TaskRelationsControl { Draft = draft, AutomationIdPrefix = "DraftTest" };
                var window = new Window { Content = view, Width = 620, Height = 700 };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    Find<Button>(view, "DraftTestAddButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    await Assert.That(view.IsEditorOpen).IsTrue();
                    await Assert.That(Find<TextBox>(view, "DraftTestAddInput").DataContext).IsEqualTo(draft.Editor);
                    draft.Editor.SelectedCandidate = draft.Editor.Suggestions.First();
                    draft.Editor.ConfirmCommand.Execute(null);
                    Dispatcher.UIThread.RunJobs();
                    await Assert.That(draft.Parents.Count).IsEqualTo(1);
                    var selected = draft.Parents[0];
                    draft.ApplyDefaults([new("other", "Другая")]);
                    await Assert.That(draft.Parents.Single()).IsEqualTo(selected);
                    draft.ApplyDefaults([new("other", "Другая")], force: true);
                    await Assert.That(draft.Parents.Single().Id).IsEqualTo("other");
                    await Assert.That(owner.taskRepository.Tasks.Count).IsEqualTo(initialCount);
                }
                finally { window.Close(); }
            }
            finally { await fixture.CleanTasksAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task LegacyRecoveryRequiresExplicitOriginalSourceConfirmation()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var verified = 0;
            using var item = new FeedPendingRecoveryViewModel("op", FeedPendingRecoveryKind.TaskConversion,
                "note.md", "", false, _ => Task.CompletedTask, _ => Task.CompletedTask);
            item.ConfigureLegacySourceVerification("space-a", () => { verified++; return Task.CompletedTask; });
            var view = new FeedPendingRecovery { DataContext = new { HasPendingRecoveries = true,
                PendingRecoveries = new[] { item }, HasIdentitySafePending = false } };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var confirm = Find<CheckBox>(view, "FeedLegacySourceConfirmation");
                var verify = Find<Button>(view, "FeedLegacyVerifySourceButton");
                await Assert.That(verify.IsEffectivelyEnabled).IsFalse();
                await Assert.That(verified).IsEqualTo(0);
                confirm.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(verify.IsEffectivelyEnabled).IsTrue();
                var center = verify.TranslatePoint(new Point(verify.Bounds.Width / 2, verify.Bounds.Height / 2), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(verified).IsEqualTo(1);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
