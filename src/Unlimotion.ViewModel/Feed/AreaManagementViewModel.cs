using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.Notes.Areas;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed class AreaManagementViewModel : ReactiveObject, IDisposable
{
    private readonly AreaCatalogStore store;
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private AreaCatalogSnapshot? snapshot;
    private AreaCatalogSnapshot? pendingExternalSnapshot;
    private AreaManagementAreaViewModel? selectedArea;
    private AreaParentOptionViewModel? selectedParent;
    private string draftName = string.Empty;
    private string draftDefaultNoteFolder = string.Empty;
    private string newAreaName = string.Empty;
    private bool isOpen;
    private bool isBusy;
    private string? errorMessage;
    private string loadedDraftName = string.Empty;
    private string loadedDraftDefaultNoteFolder = string.Empty;
    private string? loadedParentId;
    private bool isLoadingDraft;
    private bool isDraftDirty;
    private bool hasExternalConflict;
    private int disposed;

    public AreaManagementViewModel(AreaCatalogStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        RefreshCommand = ReactiveCommand.CreateFromTask(() => ExecuteSafelyAsync(LoadAsync));
        CreateRootCommand = ReactiveCommand.CreateFromTask(() => ExecuteSafelyAsync(CreateRootFromDraftAsync));
        CreateChildCommand = ReactiveCommand.CreateFromTask(() => ExecuteSafelyAsync(CreateChildFromDraftAsync));
        SaveSelectedCommand = ReactiveCommand.CreateFromTask(() => ExecuteSafelyAsync(SaveSelectedAsync));
        ToggleArchiveCommand = ReactiveCommand.CreateFromTask(() => ExecuteSafelyAsync(ToggleSelectedArchiveAsync));
        UseLocalDraftCommand = ReactiveCommand.CreateFromTask(() => ExecuteSafelyAsync(UseLocalDraftAsync));
        UseExternalAreasCommand = ReactiveCommand.Create(() => UseExternalAreas());
        CloseCommand = ReactiveCommand.Create(() => { IsOpen = false; });
        ParentOptions.Add(AreaParentOptionViewModel.NoParent);
        SelectedParent = ParentOptions[0];
    }

    public ObservableCollection<AreaManagementAreaViewModel> Areas { get; } = new();

    public ObservableCollection<AreaParentOptionViewModel> ParentOptions { get; } = new();

    public ObservableCollection<TaskClassificationAreaDefinition> ClassificationAreas { get; } = new();

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> CreateRootCommand { get; }

    public ReactiveCommand<Unit, Unit> CreateChildCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveSelectedCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleArchiveCommand { get; }

    public ReactiveCommand<Unit, Unit> UseLocalDraftCommand { get; }

    public ReactiveCommand<Unit, Unit> UseExternalAreasCommand { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public bool IsOpen
    {
        get => isOpen;
        set => this.RaiseAndSetIfChanged(ref isOpen, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => this.RaiseAndSetIfChanged(ref isBusy, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public AreaManagementAreaViewModel? SelectedArea
    {
        get => selectedArea;
        set
        {
            if (ReferenceEquals(selectedArea, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref selectedArea, value);
            LoadDraft(value);
            this.RaisePropertyChanged(nameof(HasSelectedArea));
            this.RaisePropertyChanged(nameof(SelectedAreaIsArchived));
            this.RaisePropertyChanged(nameof(ArchiveActionTitle));
        }
    }

    public bool HasSelectedArea => SelectedArea is not null;

    public bool SelectedAreaIsArchived => SelectedArea?.IsArchived == true;

    public string ArchiveActionTitle => SelectedAreaIsArchived
        ? L10n.Get("AreaRestore")
        : L10n.Get("AreaArchive");

    public string DraftName
    {
        get => draftName;
        set
        {
            this.RaiseAndSetIfChanged(ref draftName, value ?? string.Empty);
            UpdateDraftDirtyState();
        }
    }

    public string DraftDefaultNoteFolder
    {
        get => draftDefaultNoteFolder;
        set
        {
            this.RaiseAndSetIfChanged(ref draftDefaultNoteFolder, value ?? string.Empty);
            UpdateDraftDirtyState();
        }
    }

    public string NewAreaName
    {
        get => newAreaName;
        set => this.RaiseAndSetIfChanged(ref newAreaName, value ?? string.Empty);
    }

    public AreaParentOptionViewModel? SelectedParent
    {
        get => selectedParent;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedParent, value);
            UpdateDraftDirtyState();
        }
    }

    public bool IsDraftDirty
    {
        get => isDraftDirty;
        private set => this.RaiseAndSetIfChanged(ref isDraftDirty, value);
    }

    public bool HasExternalConflict
    {
        get => hasExternalConflict;
        private set => this.RaiseAndSetIfChanged(ref hasExternalConflict, value);
    }

    public Task LoadAsync() => LoadAsync(CancellationToken.None);

    /// <summary>
    /// Loads the portable area catalog with a caller-scoped cancellation token.
    /// This keeps an uncommitted Feed candidate interruptible when another root
    /// selection supersedes it.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.Token,
            cancellationToken);
        await mutationGate.WaitAsync(cancellation.Token);
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var loaded = await store.LoadAsync(cancellation.Token);
            if (snapshot is not null
                && IsDraftDirty
                && !string.Equals(loaded.Revision, snapshot.Revision, StringComparison.Ordinal))
            {
                pendingExternalSnapshot = loaded;
                HasExternalConflict = true;
                return;
            }

            snapshot = loaded;
            pendingExternalSnapshot = null;
            HasExternalConflict = false;
            ApplySnapshot(snapshot, SelectedArea?.Id);
        }
        finally
        {
            IsBusy = false;
            mutationGate.Release();
        }
    }

    public Task<AreaManagementAreaViewModel> CreateRootAsync(
        string name,
        string? defaultNoteFolder = null) =>
        CreateAsync(name, parentId: null, defaultNoteFolder);

    public Task<AreaManagementAreaViewModel> CreateChildAsync(
        string parentId,
        string name,
        string? defaultNoteFolder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        return CreateAsync(name, parentId, defaultNoteFolder);
    }

    public Task RenameAsync(string areaId, string name) =>
        MutateAsync(areaId, area => area.Name = RequireName(name));

    public Task ReparentAsync(string areaId, string? parentId) =>
        MutateAsync(areaId, area => area.ParentId = NormalizeOptional(parentId));

    public Task SetDefaultNoteFolderAsync(string areaId, string? folder) =>
        MutateAsync(areaId, area => area.DefaultNoteFolder = NormalizeOptional(folder));

    public Task ArchiveAsync(string areaId) => MutateAsync(areaId, area => area.IsArchived = true);

    public Task RestoreAsync(string areaId) => MutateAsync(areaId, area => area.IsArchived = false);

    public async Task SaveSelectedAsync()
    {
        var areaId = SelectedArea?.Id ?? throw new InvalidOperationException("Select an area before saving it.");
        var parentId = SelectedParent?.Id;
        await MutateAsync(areaId, area =>
        {
            area.Name = RequireName(DraftName);
            area.ParentId = NormalizeOptional(parentId);
            area.DefaultNoteFolder = NormalizeOptional(DraftDefaultNoteFolder);
        });
    }

    public async Task ToggleSelectedArchiveAsync()
    {
        var area = SelectedArea ?? throw new InvalidOperationException("Select an area before changing its archive state.");
        if (area.IsArchived)
        {
            await RestoreAsync(area.Id);
        }
        else
        {
            await ArchiveAsync(area.Id);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        lifetime.Dispose();
        RefreshCommand.Dispose();
        CreateRootCommand.Dispose();
        CreateChildCommand.Dispose();
        SaveSelectedCommand.Dispose();
        ToggleArchiveCommand.Dispose();
        UseLocalDraftCommand.Dispose();
        UseExternalAreasCommand.Dispose();
        CloseCommand.Dispose();
    }

    private async Task<AreaManagementAreaViewModel> CreateAsync(
        string name,
        string? parentId,
        string? defaultNoteFolder)
    {
        var newId = Guid.NewGuid().ToString("N");
        await MutateAsync(newId, _ => { }, catalog =>
        {
            var normalizedParent = NormalizeOptional(parentId);
            if (normalizedParent is not null && catalog.Areas.All(area => area.Id != normalizedParent))
            {
                throw new KeyNotFoundException($"Area '{normalizedParent}' does not exist.");
            }

            catalog.Areas.Add(new AreaDefinition
            {
                Id = newId,
                Name = RequireName(name),
                ParentId = normalizedParent,
                DefaultNoteFolder = NormalizeOptional(defaultNoteFolder),
                SortOrder = NextSortOrder(catalog, normalizedParent)
            });
        });

        return Areas.Single(area => area.Id == newId);
    }

    private async Task MutateAsync(
        string areaId,
        Action<AreaDefinition> mutation,
        Action<AreaCatalog>? catalogMutation = null)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        await mutationGate.WaitAsync(lifetime.Token);
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            snapshot ??= await store.LoadAsync(lifetime.Token);
            var candidate = Clone(snapshot.Catalog);
            catalogMutation?.Invoke(candidate);
            var area = candidate.Areas.SingleOrDefault(value => value.Id == areaId);
            if (area is null)
            {
                throw new KeyNotFoundException($"Area '{areaId}' does not exist.");
            }

            mutation(area);
            candidate.Validate();
            snapshot = await store.SaveAsync(candidate, snapshot.Revision, lifetime.Token);
            ApplySnapshot(snapshot, areaId);
        }
        finally
        {
            IsBusy = false;
            mutationGate.Release();
        }
    }

    private async Task CreateRootFromDraftAsync()
    {
        var created = await CreateRootAsync(NewAreaName);
        NewAreaName = string.Empty;
        SelectedArea = created;
    }

    private async Task UseLocalDraftAsync()
    {
        var external = pendingExternalSnapshot
            ?? throw new InvalidOperationException("There is no external area change to resolve.");
        var areaId = SelectedArea?.Id
            ?? throw new InvalidOperationException("Select an area before keeping the local form.");
        await mutationGate.WaitAsync(lifetime.Token);
        try
        {
            IsBusy = true;
            var candidate = Clone(external.Catalog);
            var area = candidate.Areas.SingleOrDefault(value => value.Id == areaId)
                ?? throw new InvalidOperationException("The selected area was removed externally; choose the external version.");
            area.Name = RequireName(DraftName);
            area.ParentId = NormalizeOptional(SelectedParent?.Id);
            area.DefaultNoteFolder = NormalizeOptional(DraftDefaultNoteFolder);
            candidate.Validate();
            snapshot = await store.SaveAsync(candidate, external.Revision, lifetime.Token);
            pendingExternalSnapshot = null;
            HasExternalConflict = false;
            ApplySnapshot(snapshot, areaId);
        }
        finally
        {
            IsBusy = false;
            mutationGate.Release();
        }
    }

    private void UseExternalAreas()
    {
        var external = pendingExternalSnapshot;
        if (external is null)
        {
            return;
        }

        var selectedId = SelectedArea?.Id;
        snapshot = external;
        pendingExternalSnapshot = null;
        HasExternalConflict = false;
        ApplySnapshot(external, selectedId);
    }

    private async Task CreateChildFromDraftAsync()
    {
        var parent = SelectedArea ?? throw new InvalidOperationException("Select a parent area first.");
        var created = await CreateChildAsync(parent.Id, NewAreaName);
        NewAreaName = string.Empty;
        SelectedArea = created;
    }

    private async Task ExecuteSafelyAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void ApplySnapshot(AreaCatalogSnapshot next, string? selectedAreaId)
    {
        const string rootKey = "\0";
        var byParent = next.Catalog.Areas
            .GroupBy(static area => area.ParentId ?? rootKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static area => area.SortOrder)
                    .ThenBy(static area => area.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal);
        var flattened = new List<AreaManagementAreaViewModel>();
        Append(rootKey, depth: 0);

        Areas.Clear();
        foreach (var area in flattened)
        {
            Areas.Add(area);
        }

        ClassificationAreas.Clear();
        foreach (var area in flattened)
        {
            ClassificationAreas.Add(new TaskClassificationAreaDefinition(
                area.Id,
                area.Name,
                area.IsArchived,
                ClassificationAreas.Count));
        }

        SelectedArea = Areas.FirstOrDefault(area => area.Id == selectedAreaId);

        void Append(string parentId, int depth)
        {
            if (!byParent.TryGetValue(parentId, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                flattened.Add(new AreaManagementAreaViewModel(
                    child.Id,
                    child.Name,
                    child.ParentId,
                    child.DefaultNoteFolder,
                    child.IsArchived,
                    depth));
                Append(child.Id, depth + 1);
            }
        }
    }

    private void LoadDraft(AreaManagementAreaViewModel? area)
    {
        isLoadingDraft = true;
        try
        {
            DraftName = area?.Name ?? string.Empty;
            DraftDefaultNoteFolder = area?.DefaultNoteFolder ?? string.Empty;
            RebuildParentOptions(area);
            loadedDraftName = DraftName;
            loadedDraftDefaultNoteFolder = DraftDefaultNoteFolder;
            loadedParentId = SelectedParent?.Id;
            IsDraftDirty = false;
        }
        finally
        {
            isLoadingDraft = false;
        }
    }

    private void UpdateDraftDirtyState()
    {
        if (isLoadingDraft)
        {
            return;
        }

        IsDraftDirty = SelectedArea is not null
            && (!string.Equals(DraftName, loadedDraftName, StringComparison.Ordinal)
                || !string.Equals(DraftDefaultNoteFolder, loadedDraftDefaultNoteFolder, StringComparison.Ordinal)
                || !string.Equals(SelectedParent?.Id, loadedParentId, StringComparison.Ordinal));
    }

    private void RebuildParentOptions(AreaManagementAreaViewModel? selected)
    {
        var excluded = selected is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : GetDescendantIds(selected.Id);
        if (selected is not null)
        {
            excluded.Add(selected.Id);
        }

        ParentOptions.Clear();
        ParentOptions.Add(AreaParentOptionViewModel.NoParent);
        foreach (var area in Areas.Where(area => !excluded.Contains(area.Id)))
        {
            ParentOptions.Add(new AreaParentOptionViewModel(area.Id, area.DisplayName));
        }

        SelectedParent = ParentOptions.FirstOrDefault(option => option.Id == selected?.ParentId)
                         ?? ParentOptions[0];
    }

    private HashSet<string> GetDescendantIds(string areaId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(areaId);
        while (pending.TryDequeue(out var parentId))
        {
            foreach (var child in Areas.Where(area => area.ParentId == parentId))
            {
                if (result.Add(child.Id))
                {
                    pending.Enqueue(child.Id);
                }
            }
        }

        return result;
    }

    private static AreaCatalog Clone(AreaCatalog catalog) => new()
    {
        SchemaVersion = catalog.SchemaVersion,
        ExtensionData = catalog.ExtensionData is null
            ? null
            : new Dictionary<string, System.Text.Json.JsonElement>(catalog.ExtensionData, StringComparer.Ordinal),
        Areas = catalog.Areas.Select(area => new AreaDefinition
        {
            Id = area.Id,
            Name = area.Name,
            ParentId = area.ParentId,
            IsArchived = area.IsArchived,
            SortOrder = area.SortOrder,
            DefaultNoteFolder = area.DefaultNoteFolder,
            ExtensionData = area.ExtensionData is null
                ? null
                : new Dictionary<string, System.Text.Json.JsonElement>(area.ExtensionData, StringComparer.Ordinal)
        }).ToList()
    };

    private static int NextSortOrder(AreaCatalog catalog, string? parentId) =>
        catalog.Areas.Where(area => area.ParentId == parentId)
            .Select(static area => area.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;

    private static string RequireName(string? value) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidDataException(L10n.Get("AreaNameRequired"))
        : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref disposed) != 0,
        this);
}

public sealed record AreaManagementAreaViewModel(
    string Id,
    string Name,
    string? ParentId,
    string? DefaultNoteFolder,
    bool IsArchived,
    int Depth)
{
    public string DisplayName => $"{new string('\u00A0', Depth * 2)}{Name}";
}

public sealed record AreaParentOptionViewModel(string? Id, string Name)
{
    public static AreaParentOptionViewModel NoParent { get; } = new(null, "—");
}
