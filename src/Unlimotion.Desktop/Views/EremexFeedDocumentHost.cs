using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Eremex.AvaloniaUI.Controls;
using Eremex.AvaloniaUI.Controls.Docking;
using Eremex.AvaloniaUI.Themes.DeltaDesign;
using Unlimotion.ViewModel.Feed;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Desktop.Views;

/// <summary>Desktop-only docking adapter. The feed owns document saves and session identity.</summary>
public sealed class EremexFeedDocumentHost : UserControl, IDisposable
{
    private readonly FeedViewModel owner;
    private readonly Control viewport;
    private readonly DockManager manager;
    private readonly DocumentGroup documents;
    private readonly DocumentPane chronology;
    private readonly Button tabList = new() { Content = "⌄", Padding = new Thickness(6, 2), MinWidth = 26 };
    private readonly Dictionary<FeedThematicDocumentViewModel, DocumentPane> panes = new();
    private DocumentPane? viewportPane;
    private bool synchronizing;
    private bool operationPending;
    private bool subscribed;
    private bool disposed;

    public EremexFeedDocumentHost(FeedViewModel owner, Control viewport)
    {
        this.owner = owner;
        this.viewport = viewport;
        DataContext = owner;
        viewport.DataContext = owner;

        // Never install the vendor theme in Application.Styles: task screens and
        // the global application shell retain their existing theme resources.
        Styles.Add(new DeltaDesignTheme());
        PreserveApplicationControlThemes();
        documents = new DocumentGroup
        {
            AllowFloat = false,
            ShowTabStripForSingleChild = false,
            TabStripLayoutType = TabStripLayoutType.Scroll,
            CloseButtonShowMode = TabControlCloseButtonShowMode.InAllTabs,
            SelectTabOnClose = SelectTabOnClose.Previous
        };
        chronology = CreatePane(L10n.Get("FeedTitle"), "FeedChronologyTab", null, canClose: false);
        AutomationProperties.SetAutomationId(tabList, "FeedDocumentTabList");
        tabList.Click += (_, _) => tabList.ContextMenu?.Open(tabList);
        documents.Loaded += (_, _) => InstallTabList();
        documents.Items.Add(chronology);
        var root = new DockGroup();
        root.Items.Add(documents);
        manager = new DockManager
        {
            Root = root,
            AllowFreeDocumentLayout = false,
            // FeedControl handles the same shortcuts through its asynchronous save gate.
            AllowDocumentSwitcher = false
        };
        AutomationProperties.SetAutomationId(manager, "FeedDesktopDocumentDock");
        manager.DockItemActivated += OnDockItemActivated;
        manager.DockOperationStarting += OnDockOperationStarting;
        manager.DockItemContextMenuOpening += OnDockItemContextMenuOpening;
        Content = manager;
        Subscribe();
        SynchronizeDocuments();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
        SynchronizeDocuments();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unsubscribe();
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe()
    {
        if (subscribed || disposed) return;
        owner.DocumentWorkspace.Documents.CollectionChanged += OnDocumentsChanged;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        foreach (var document in panes.Keys)
            DocumentNotifications(document).PropertyChanged += OnDocumentPropertyChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        owner.DocumentWorkspace.Documents.CollectionChanged -= OnDocumentsChanged;
        owner.PropertyChanged -= OnOwnerPropertyChanged;
        foreach (var document in panes.Keys)
            DocumentNotifications(document).PropertyChanged -= OnDocumentPropertyChanged;
        subscribed = false;
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SynchronizeDocuments();

    // Fody adds this interface to the implementation assembly, not its compile-time reference assembly.
    private static INotifyPropertyChanged DocumentNotifications(FeedThematicDocumentViewModel document) =>
        (INotifyPropertyChanged)(object)document;

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FeedViewModel.OpenedThematicFile)) SynchronizeActivePane();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed || !subscribed || sender is not FeedThematicDocumentViewModel document
            || !panes.TryGetValue(document, out var pane)) return;
        if (e.PropertyName is not (nameof(FeedThematicDocumentViewModel.RelativePath)
            or nameof(FeedThematicDocumentViewModel.FullPath))) return;
        UpdatePaneMetadata(pane, document);
        RebuildTabList();
    }

    private void SynchronizeDocuments()
    {
        if (disposed) return;
        synchronizing = true;
        try
        {
            foreach (var removed in panes.Keys.Where(document => !owner.DocumentWorkspace.Documents.Contains(document)).ToArray())
            {
                var pane = panes[removed];
                if (ReferenceEquals(viewportPane, pane))
                {
                    pane.Content = null;
                    viewportPane = null;
                }
                if (subscribed)
                    DocumentNotifications(removed).PropertyChanged -= OnDocumentPropertyChanged;
                documents.Items.Remove(pane);
                panes.Remove(removed);
            }
            foreach (var document in owner.DocumentWorkspace.Documents)
            {
                if (panes.TryGetValue(document, out var existing))
                {
                    UpdatePaneMetadata(existing, document);
                    continue;
                }
                var pane = CreatePane(document.RelativePath, "FeedDocumentTab-" + document.RelativePath,
                    document.FullPath, canClose: true);
                panes.Add(document, pane);
                if (subscribed)
                    DocumentNotifications(document).PropertyChanged += OnDocumentPropertyChanged;
                documents.Items.Add(pane);
            }
        }
        finally { synchronizing = false; }
        SynchronizeActivePane();
        RebuildTabList();
    }

    private static void UpdatePaneMetadata(DocumentPane pane, FeedThematicDocumentViewModel document)
    {
        var caption = document.RelativePath;
        var automationId = "FeedDocumentTab-" + caption;
        pane.Header = caption;
        if (pane.TabHeader is TextBlock header)
        {
            header.Text = caption;
            AutomationProperties.SetAutomationId(header, automationId);
            ToolTip.SetTip(header, document.FullPath ?? caption);
        }
        pane.DocumentSwitcherDescription = caption;
        pane.DocumentSwitcherFooterDescription = document.FullPath ?? caption;
        AutomationProperties.SetAutomationId(pane, automationId + "-Pane");
        AutomationProperties.SetName(pane, caption);
    }

    private void RebuildTabList()
    {
        var menu = new ContextMenu();
        var feedItem = new MenuItem { Header = L10n.Get("FeedTitle") };
        feedItem.Click += async (_, _) => await ActivateFromListAsync(null);
        menu.Items.Add(feedItem);
        foreach (var document in owner.DocumentWorkspace.Documents)
        {
            var item = new MenuItem { Header = document.RelativePath };
            ToolTip.SetTip(item, document.FullPath);
            item.Click += async (_, _) => await ActivateFromListAsync(document);
            menu.Items.Add(item);
        }
        tabList.ContextMenu = menu;
        InstallTabList();
    }

    private void InstallTabList()
    {
        if (disposed) return;
        var tabs = documents.GetVisualDescendants().OfType<MxTabControl>().FirstOrDefault();
        if (tabs is not null) tabs.ControlBoxContent = tabList;
    }

    private async Task ActivateFromListAsync(FeedThematicDocumentViewModel? document)
    {
        if (disposed || operationPending) return;
        operationPending = true;
        try { await owner.ActivateDocumentAsync(document); }
        finally
        {
            operationPending = false;
            SynchronizeActivePane();
        }
    }

    private void SynchronizeActivePane()
    {
        if (disposed) return;
        var selected = owner.OpenedThematicFile is { } active && panes.TryGetValue(active, out var pane)
            ? pane : chronology;
        synchronizing = true;
        try
        {
            if (!ReferenceEquals(viewportPane, selected))
            {
                if (viewportPane is not null) viewportPane.Content = null;
                selected.Content = viewport;
                viewportPane = selected;
            }
            documents.SelectedIndex = documents.Items.IndexOf(selected);
            selected.IsActive = true;
        }
        finally { synchronizing = false; }
    }

    private async void OnDockItemActivated(object? sender, DockItemActivatedEventArgs e)
    {
        if (synchronizing || disposed || e.NewItem is not DocumentPane requested) return;
        var document = panes.FirstOrDefault(pair => ReferenceEquals(pair.Value, requested)).Key;
        if (!ReferenceEquals(requested, chronology) && document is null) return;
        if (operationPending)
        {
            SynchronizeActivePane();
            return;
        }
        operationPending = true;
        // Eremex's activation event is synchronous. Restore the current pane while
        // an asynchronous save is pending, then follow the authoritative VM result.
        SynchronizeActivePane();
        try { await owner.ActivateDocumentAsync(document); }
        finally
        {
            operationPending = false;
            SynchronizeActivePane();
        }
    }

    private void OnDockOperationStarting(object? sender, DockOperationStartingEventArgs e)
    {
        e.Cancel = true;
        if (disposed || operationPending || e.DockOperation != DockOperation.Close) return;
        var document = panes.FirstOrDefault(pair => ReferenceEquals(pair.Value, e.Item)).Key;
        if (document is not null)
        {
            operationPending = true;
            // Do not mutate DockManager's item collection inside its cancelable operation event.
            Dispatcher.UIThread.Post(async () => await CloseDocumentAsync(document));
        }
    }

    private async Task CloseDocumentAsync(FeedThematicDocumentViewModel document)
    {
        if (disposed) return;
        operationPending = true;
        try { await owner.CloseDocumentAsync(document); }
        finally
        {
            operationPending = false;
            SynchronizeDocuments();
        }
    }

    private static void OnDockItemContextMenuOpening(object? sender, DockItemContextMenuOpeningEventArgs e) =>
        e.Cancel = true;

    private static DocumentPane CreatePane(string caption, string automationId, string? fullPath, bool canClose)
    {
        var header = new TextBlock { Text = caption, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetAutomationId(header, automationId);
        ToolTip.SetTip(header, fullPath ?? caption);
        var pane = new DocumentPane
        {
            Header = caption,
            TabHeader = header,
            AllowClose = canClose,
            AllowFloat = false,
            AllowAutoHide = false,
            AllowMinimize = false,
            AllowMaximize = false,
            DocumentSwitcherDescription = caption,
            DocumentSwitcherFooterDescription = fullPath ?? caption
        };
        AutomationProperties.SetAutomationId(pane, automationId + "-Pane");
        AutomationProperties.SetName(pane, caption);
        return pane;
    }

    private void PreserveApplicationControlThemes()
    {
        // DeltaDesign also supplies standard Avalonia templates. Keep the feed's
        // editors, menus and buttons consistent with the rest of the application.
        Type[] types = [typeof(Button), typeof(ToggleButton), typeof(CheckBox), typeof(TextBox),
            typeof(ComboBox), typeof(ComboBoxItem), typeof(ListBox), typeof(ListBoxItem),
            typeof(Menu), typeof(MenuItem), typeof(ContextMenu), typeof(ScrollViewer),
            typeof(ScrollBar), typeof(Separator), typeof(ToolTip), typeof(TabControl),
            typeof(TabItem), typeof(ProgressBar), typeof(RadioButton), typeof(ToggleSwitch), typeof(UserControl)];
        foreach (var type in types)
            if (Application.Current?.TryFindResource(type, out var resource) == true && resource is ControlTheme)
                Resources[type] = resource;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Unsubscribe();
        manager.DockItemActivated -= OnDockItemActivated;
        manager.DockOperationStarting -= OnDockOperationStarting;
        manager.DockItemContextMenuOpening -= OnDockItemContextMenuOpening;
        if (viewportPane is not null) viewportPane.Content = null;
        viewportPane = null;
        panes.Clear();
        documents.Items.Clear();
        Content = null;
    }
}
