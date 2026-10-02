using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion;

public enum TaskHistoryChangeType
{
    Added,
    Removed,
    Modified
}

public sealed record TaskHistoryValueReference(
    string RepositoryPath,
    string? CommitSha,
    string FilePath,
    string JsonPath,
    bool IsWorkingTree,
    string? ExpectedContentHash = null);

public sealed record TaskHistoryFieldChange(
    string FieldPath,
    string DisplayName,
    string OldValueDisplay,
    string NewValueDisplay,
    TaskHistoryChangeType ChangeType,
    bool IsMetadata,
    TaskHistoryValueReference? OldValueReference = null,
    TaskHistoryValueReference? NewValueReference = null)
{
    public string Symbol => ChangeType switch
    {
        TaskHistoryChangeType.Added => "+",
        TaskHistoryChangeType.Removed => "−",
        _ => "→"
    };

    public bool HasDetails =>
        OldValueDisplay.EndsWith("…", StringComparison.Ordinal) ||
        NewValueDisplay.EndsWith("…", StringComparison.Ordinal);
}

public sealed record TaskHistoryEntry(
    string? CommitSha,
    string Author,
    DateTimeOffset? ChangedAt,
    string Source,
    string Message,
    IReadOnlyList<TaskHistoryFieldChange> Changes,
    bool IsWorkingTree = false,
    bool IsPartial = false,
    string Notice = "")
{
    public string ShortSha =>
        string.IsNullOrWhiteSpace(CommitSha)
            ? string.Empty
            : CommitSha[..Math.Min(7, CommitSha.Length)];

    public string MessagePreview =>
        IsWorkingTree ? string.Empty : Message.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', 2, StringSplitOptions.None)[0];

    public string SourceTip => IsWorkingTree ? Message : CommitSha ?? string.Empty;

    public bool HasMessagePreview => !string.IsNullOrWhiteSpace(MessagePreview);

    public string Heading => HasMessagePreview ? MessagePreview : Source;

    public bool HasCommit => !string.IsNullOrEmpty(CommitSha);

    public string CommitToolTip => $"{Localization.Get("TaskHistoryCopyCommit")}\n{CommitSha}";

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    public string ChangedAtDisplay =>
        ChangedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
}

public sealed record TaskHistoryRequest(
    string? StoragePath,
    string SourceId,
    string TaskId,
    string? Cursor,
    int PageSize = 50);

public sealed record TaskHistoryPage(
    IReadOnlyList<TaskHistoryEntry> Entries,
    string? NextCursor,
    string StatusMessage,
    bool IsUnavailable = false,
    bool IsPartial = false);

public interface ITaskHistoryProvider
{
    Task<TaskHistoryPage> GetPageAsync(TaskHistoryRequest request, CancellationToken cancellationToken);

    Task<string> ReadValueAsync(TaskHistoryValueReference reference, CancellationToken cancellationToken);

    void ResetSession() { }
}

public sealed class TaskHistoryPaneViewModel : ReactiveObject, IDisposable
{
    private readonly ITaskHistoryProvider _provider;
    private readonly List<TaskHistoryEntry> _allEntries = [];
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _detailsCancellation;
    private string? _storagePath;
    private string _sourceId = string.Empty;
    private string _taskId = string.Empty;
    private string? _cursor;
    private bool _isLoading;
    private bool _hasMore;
    private bool _showMetadata;
    private bool _isGitMode = true;
    private string _statusMessage = string.Empty;
    private string _detailTitle = string.Empty;
    private string _detailOldValue = string.Empty;
    private string _detailNewValue = string.Empty;
    private bool _hasDetails;

    public TaskHistoryPaneViewModel(ITaskHistoryProvider provider)
    {
        _provider = provider;
    }

    public ObservableCollection<TaskHistoryEntry> Entries { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isLoading, value);
            this.RaisePropertyChanged(nameof(CanLoadMore));
        }
    }

    public bool HasMore
    {
        get => _hasMore;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasMore, value);
            this.RaisePropertyChanged(nameof(CanLoadMore));
        }
    }

    public bool CanLoadMore => HasMore && !IsLoading;

    public bool ShowMetadata
    {
        get => _showMetadata;
        set
        {
            if (_showMetadata == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _showMetadata, value);
            RebuildVisibleEntries();
        }
    }

    public bool IsGitMode
    {
        get => _isGitMode;
        set
        {
            if (_isGitMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isGitMode, value);
            this.RaisePropertyChanged(nameof(IsStatusMode));
        }
    }

    public bool IsStatusMode => !IsGitMode;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _statusMessage, value);
            this.RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string DetailTitle
    {
        get => _detailTitle;
        private set => this.RaiseAndSetIfChanged(ref _detailTitle, value);
    }

    public string DetailOldValue
    {
        get => _detailOldValue;
        private set => this.RaiseAndSetIfChanged(ref _detailOldValue, value);
    }

    public string DetailNewValue
    {
        get => _detailNewValue;
        private set => this.RaiseAndSetIfChanged(ref _detailNewValue, value);
    }

    public bool HasDetails
    {
        get => _hasDetails;
        private set => this.RaiseAndSetIfChanged(ref _hasDetails, value);
    }

    public void SelectTask(string? storagePath, string sourceId, string? taskId)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(storagePath) ? null : storagePath;
        var normalizedTaskId = taskId ?? string.Empty;
        if (string.Equals(_storagePath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_sourceId, sourceId, StringComparison.Ordinal) &&
            string.Equals(_taskId, normalizedTaskId, StringComparison.Ordinal))
        {
            return;
        }

        CancelLoad();
        _provider.ResetSession();
        _storagePath = normalizedPath;
        _sourceId = sourceId;
        _taskId = normalizedTaskId;
        _cursor = null;
        _allEntries.Clear();
        Entries.Clear();
        HasMore = false;
        StatusMessage = string.Empty;
        CancelDetailsOnly();
    }

    public async Task RefreshAsync()
    {
        CancelLoad();
        CancelDetailsOnly();
        _provider.ResetSession();
        _cursor = null;
        HasMore = false;
        await LoadNextPageCoreAsync(replaceEntries: true).ConfigureAwait(true);
    }

    public Task LoadMoreAsync() => LoadNextPageCoreAsync();

    public async Task ShowDetailsAsync(TaskHistoryFieldChange change)
    {
        CancelDetailsOnly();
        var cancellation = new CancellationTokenSource();
        _detailsCancellation = cancellation;
        var cancellationToken = cancellation.Token;
        var requestPath = _storagePath;
        var requestSource = _sourceId;
        var requestTask = _taskId;
        DetailTitle = change.DisplayName;
        try
        {
            var oldValue = await ReadValueOrDisplayAsync(
                change.OldValueReference,
                change.OldValueDisplay,
                cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            var newValue = await ReadValueOrDisplayAsync(
                change.NewValueReference,
                change.NewValueDisplay,
                cancellationToken).ConfigureAwait(true);
            if (cancellation.IsCancellationRequested ||
                !string.Equals(_storagePath, requestPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_sourceId, requestSource, StringComparison.Ordinal) ||
                !string.Equals(_taskId, requestTask, StringComparison.Ordinal))
            {
                return;
            }

            DetailOldValue = oldValue;
            DetailNewValue = newValue;
            HasDetails = true;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_detailsCancellation, cancellation))
            {
                _detailsCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    public void ClearDetails()
    {
        _detailsCancellation?.Cancel();
        _detailsCancellation = null;
        DetailTitle = string.Empty;
        DetailOldValue = string.Empty;
        DetailNewValue = string.Empty;
        HasDetails = false;
    }

    private async Task LoadNextPageCoreAsync(bool replaceEntries = false)
    {
        if (IsLoading || string.IsNullOrWhiteSpace(_taskId))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var requestPath = _storagePath;
        var requestSource = _sourceId;
        var requestTask = _taskId;
        var requestCursor = _cursor;

        IsLoading = true;
        StatusMessage = Localization.Get("TaskHistoryLoading");
        try
        {
            var page = await _provider.GetPageAsync(
                new TaskHistoryRequest(requestPath, requestSource, requestTask, requestCursor),
                cancellation.Token).ConfigureAwait(true);

            if (cancellation.IsCancellationRequested ||
                !string.Equals(_storagePath, requestPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_sourceId, requestSource, StringComparison.Ordinal) ||
                !string.Equals(_taskId, requestTask, StringComparison.Ordinal))
            {
                return;
            }

            if (replaceEntries)
                _allEntries.Clear();
            _allEntries.AddRange(page.Entries);
            _cursor = page.NextCursor;
            HasMore = page.NextCursor is not null;
            StatusMessage = page.StatusMessage;
            RebuildVisibleEntries();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested &&
                string.Equals(_storagePath, requestPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_sourceId, requestSource, StringComparison.Ordinal) &&
                string.Equals(_taskId, requestTask, StringComparison.Ordinal))
            {
                StatusMessage = Localization.Format("TaskHistoryLoadFailed", ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                IsLoading = false;
            }

            cancellation.Dispose();
        }
    }

    private async Task<string> ReadValueOrDisplayAsync(
        TaskHistoryValueReference? reference,
        string display,
        CancellationToken cancellationToken)
    {
        if (reference is null)
        {
            return display;
        }

        try
        {
            return await _provider.ReadValueAsync(reference, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Localization.Format("TaskHistoryValueLoadFailed", ex.Message);
        }
    }

    private void RebuildVisibleEntries()
    {
        Entries.Clear();
        foreach (var entry in _allEntries)
        {
            var changes = ShowMetadata
                ? entry.Changes
                : entry.Changes.Where(static change => !change.IsMetadata).ToArray();
            if (changes.Count == 0)
            {
                continue;
            }

            Entries.Add(entry with { Changes = changes });
        }

        if (Entries.Count > 0 &&
            string.Equals(StatusMessage, Localization.Get("TaskHistoryOnlyMetadata"), StringComparison.Ordinal))
        {
            StatusMessage = string.Empty;
        }

        if (Entries.Count == 0 && _allEntries.Count > 0 && !ShowMetadata)
        {
            StatusMessage = Localization.Get("TaskHistoryOnlyMetadata");
        }
        else if (Entries.Count == 0 && !IsLoading && string.IsNullOrWhiteSpace(StatusMessage))
        {
            StatusMessage = Localization.Get("TaskHistoryEmpty");
        }
    }

    private void CancelDetailsOnly()
    {
        ClearDetails();
    }

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        IsLoading = false;
    }

    public void Dispose()
    {
        CancelLoad();
        CancelDetailsOnly();
        _provider.ResetSession();
        _cursor = null;
        _allEntries.Clear();
        Entries.Clear();
        HasMore = false;
        StatusMessage = string.Empty;
    }
}
