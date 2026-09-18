using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Eremex.AvaloniaUI.Controls.Docking;
using Unlimotion.Desktop.Views;
using Unlimotion.Notes.Vault;
using Unlimotion.ViewModel.Feed;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class EremexFeedDocumentHostUiTests
{
    [Test]
    public async Task DesktopDock_PreservesViewportNavigationOverflowAndFailedSave()
    {
        // Vendor SVG resources are process-cached and thread-affine. Exercise the
        // lifecycle on one dispatcher, as in the real desktop application.
        await using var ui = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await WithHost(ui, async (feed, host, viewport, window) =>
        {
            await feed.OpenVaultLinkAsync("Первая", null);
            Layout();
            var manager = host.GetVisualDescendants().OfType<DockManager>().Single();
            var group = (DocumentGroup)manager.Root!.Items.Single();
            var chronology = (DocumentPane)group.Items[0];
            var document = (DocumentPane)group.Items[1];
            await Assert.That(group.Items.Count).IsEqualTo(2);
            await Assert.That(document.Content).IsSameReferenceAs(viewport);
            await Assert.That(chronology.AllowClose).IsFalse();
            await Assert.That(manager.Float(document)).IsFalse();
            await Assert.That(manager.FloatGroups.Count).IsEqualTo(0);
            Capture(window, "desktop-document-tabs");

            var renamed = feed.OpenedThematicFile!;
            var renamedFullPath = Path.Combine(Path.GetDirectoryName(renamed.FullPath!)!, "Переименованная.md");
            renamed.RelativePath = "Переименованная.md";
            renamed.FullPath = renamedFullPath;
            Layout();
            await Assert.That(group.Items[1]).IsSameReferenceAs(document);
            await Assert.That(document.Header).IsEqualTo("Переименованная.md");
            var renamedHeader = (TextBlock)document.TabHeader!;
            await Assert.That(renamedHeader.Text).IsEqualTo("Переименованная.md");
            await Assert.That(ToolTip.GetTip(renamedHeader)).IsEqualTo(renamedFullPath);
            var updatedList = host.GetVisualDescendants().OfType<Button>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "FeedDocumentTabList");
            await Assert.That(((MenuItem)updatedList.ContextMenu!.Items[1]!).Header).IsEqualTo("Переименованная.md");

            var tab = host.GetVisualDescendants().OfType<TextBlock>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "FeedChronologyTab");
            var point = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Layout();
            await Assert.That(feed.OpenedThematicFile).IsNull();
            await Assert.That(chronology.Content).IsSameReferenceAs(viewport);
            await Assert.That(document.Content).IsNull();

            for (var index = 0; index < 6; index++)
                await feed.OpenVaultLinkAsync($"Тема {index} с длинным названием", null);
            window.Width = 360;
            Layout();
            var list = host.GetVisualDescendants().OfType<Button>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "FeedDocumentTabList");
            await Assert.That(list.IsEffectivelyVisible).IsTrue();
            var listPoint = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window)!.Value;
            window.MouseDown(listPoint, MouseButton.Left);
            window.MouseUp(listPoint, MouseButton.Left);
            Layout();
            await Assert.That(list.ContextMenu!.IsOpen).IsTrue();
            await Assert.That(list.ContextMenu.Items.Count).IsEqualTo(8);
            Capture(window, "desktop-tab-overflow");
            var feedItem = (MenuItem)list.ContextMenu.Items[0]!;
            feedItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Layout();
            await Assert.That(feed.OpenedThematicFile).IsNull();
            list.ContextMenu.Close();
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Layout();
            Capture(window, "desktop-tabs-dark");
            host.Dispose();
            await Assert.That(chronology.Content).IsNull();
        });
        await FailedDocumentSave_CancelsVendorCloseAndActivation(ui);
    }

    private static async Task FailedDocumentSave_CancelsVendorCloseAndActivation(SafeHeadlessUnitTestSession ui)
    {
        await WithHost(ui, async (feed, host, viewport, _) =>
        {
            await feed.OpenVaultLinkAsync("Первая", null);
            Layout();
            var manager = host.GetVisualDescendants().OfType<DockManager>().Single();
            var group = (DocumentGroup)manager.Root!.Items.Single();
            var chronology = (DocumentPane)group.Items[0];
            var document = (DocumentPane)group.Items[1];
            var session = feed.OpenedThematicFile!;
            session.MarkdownEditor.CommitBlockAsync = (_, _) =>
                Task.FromResult(MarkdownBlockCommitResult.Rejected("Synthetic save failure"));
            session.MarkdownEditor.BeginEdit(session.MarkdownEditor.Blocks[0]);
            session.MarkdownEditor.ActiveBlock!.EditorText = "Несохранённый текст";

            await Assert.That(manager.Close(document)).IsFalse();
            Layout();
            await Assert.That(feed.DocumentWorkspace.Documents.Contains(session)).IsTrue();
            await Assert.That(document.Content).IsSameReferenceAs(viewport);
            chronology.IsActive = true;
            Layout();
            await Assert.That(feed.OpenedThematicFile).IsSameReferenceAs(session);
            await Assert.That(manager.ActiveDockItem).IsSameReferenceAs(document);
            await Assert.That(session.MarkdownEditor.ActiveBlock!.EditorText).IsEqualTo("Несохранённый текст");
        });
    }

    private static async Task WithHost(SafeHeadlessUnitTestSession ui,
        Func<FeedViewModel, EremexFeedDocumentHost, Control, Window, Task> action)
    {
        await ui.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            await vault.CreateAsync("Первая.md", "Текст первой заметки\n");
            for (var index = 0; index < 6; index++)
                await vault.CreateAsync($"Тема {index} с длинным названием.md", $"Заметка {index}\n");
            using var feed = new FeedViewModel();
            await feed.InitializeVaultAsync(directory.Path);
            var viewport = new Border { Child = new TextBlock { Text = "Document viewport" } };
            var applicationStyles = Application.Current!.Styles.ToArray();
            using var host = new EremexFeedDocumentHost(feed, viewport);
            await Assert.That(Application.Current.Styles.SequenceEqual(applicationStyles)).IsTrue();
            var window = new Window { Width = 700, Height = 450, Content = host };
            try
            {
                window.Show();
                Layout();
                await action(feed, host, viewport, window);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Layout()
    {
        for (var index = 0; index < 20; index++) Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_FEED_INLINE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) throw new InvalidOperationException("Headless renderer returned no frame.");
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
