using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using Unlimotion.ViewModel.Localization;
using Unlimotion.ViewModel.Search;

namespace Unlimotion.ViewModel;

public sealed class TaskRelationEditorViewModel : ReactiveObject, IDisposable
{
    private const int EmptyQuerySuggestionLimit = 30;
    private const int SearchSuggestionLimit = 100;

    private readonly Func<IEnumerable<TaskItemViewModel>> _getAllTasks;
    private readonly Func<string, TaskItemViewModel?> _findTaskById;
    private readonly Func<bool> _getIsFuzzySearch;
    private readonly Func<TaskRelationKind, TaskItemViewModel, TaskItemViewModel, bool> _isCandidateValid;
    private readonly Func<TaskRelationKind, TaskItemViewModel, TaskItemViewModel, Task<bool>> _addRelationAsync;
    private readonly Func<TaskItemViewModel, string> _getContextText;
    private readonly INotificationManagerWrapper _notificationManager;
    private readonly ILocalizationService _localization;
    private readonly EventHandler _cultureChangedHandler;
    private bool _isDisposed;

    private string? _currentTaskId;
    private TaskRelationKind? _kind;
    private string _query = string.Empty;
    private TaskRelationCandidateViewModel? _selectedCandidate;
    private IReadOnlyList<TaskRelationCandidateViewModel> _suggestions = Array.Empty<TaskRelationCandidateViewModel>();
    private long _focusRequestVersion;

    public TaskRelationEditorViewModel(
        Func<IEnumerable<TaskItemViewModel>> getAllTasks,
        Func<string, TaskItemViewModel?> findTaskById,
        Func<bool> getIsFuzzySearch,
        Func<TaskRelationKind, TaskItemViewModel, TaskItemViewModel, bool> isCandidateValid,
        Func<TaskRelationKind, TaskItemViewModel, TaskItemViewModel, Task<bool>> addRelationAsync,
        Func<TaskItemViewModel, string> getContextText,
        INotificationManagerWrapper notificationManager,
        ILocalizationService? localizationService = null)
    {
        _getAllTasks = getAllTasks;
        _findTaskById = findTaskById;
        _getIsFuzzySearch = getIsFuzzySearch;
        _isCandidateValid = isCandidateValid;
        _addRelationAsync = addRelationAsync;
        _getContextText = getContextText;
        _notificationManager = notificationManager;
        _localization = localizationService ?? LocalizationService.Current;

        CancelCommand = ReactiveCommand.Create(Cancel);
        ConfirmCommand = ReactiveCommand.CreateFromTask(ConfirmAsync, this.WhenAnyValue(vm => vm.CanConfirm));
        _cultureChangedHandler = (_, _) => RaiseLocalizationPropertiesChanged();
        _localization.CultureChanged += _cultureChangedHandler;
    }

    public bool IsOpen => !string.IsNullOrWhiteSpace(_currentTaskId) && _kind.HasValue;

    public TaskRelationKind? Kind => _kind;

    public bool IsOpenForParents => IsOpen && _kind == TaskRelationKind.Parents;

    public bool IsOpenForContaining => IsOpen && _kind == TaskRelationKind.Containing;

    public bool IsOpenForBlocking => IsOpen && _kind == TaskRelationKind.Blocking;

    public bool IsOpenForBlocked => IsOpen && _kind == TaskRelationKind.Blocked;

    public string Query
    {
        get => _query;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (!string.Equals(_query, normalizedValue, StringComparison.Ordinal))
            {
                this.RaiseAndSetIfChanged(ref _query, normalizedValue);
                RefreshSuggestions();
            }
        }
    }

    public TaskRelationCandidateViewModel? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (!ReferenceEquals(_selectedCandidate, value))
            {
                this.RaiseAndSetIfChanged(ref _selectedCandidate, value);
                this.RaisePropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public IReadOnlyList<TaskRelationCandidateViewModel> Suggestions
    {
        get => _suggestions;
        private set
        {
            if (!ReferenceEquals(_suggestions, value))
            {
                this.RaiseAndSetIfChanged(ref _suggestions, value);
                this.RaisePropertyChanged(nameof(HasSuggestions));
                this.RaisePropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool CanConfirm => ResolveCandidateForConfirm() != null;

    public string Watermark => _localization.Get("FindTask");

    public string EmptyStateText => _localization.Get("NoRelationCandidates");

    public string Header => _kind switch
    {
        TaskRelationKind.Parents => _localization.Get("AddParentRelation"),
        TaskRelationKind.Containing => _localization.Get("AddContainingRelation"),
        TaskRelationKind.Blocking => _localization.Get("AddBlockingRelation"),
        TaskRelationKind.Blocked => _localization.Get("AddBlockedRelation"),
        _ => string.Empty
    };

    public string InputAutomationId => _kind switch
    {
        TaskRelationKind.Parents => "CurrentTaskParentsRelationAddInput",
        TaskRelationKind.Containing => "CurrentTaskContainingRelationAddInput",
        TaskRelationKind.Blocking => "CurrentTaskBlockingRelationAddInput",
        TaskRelationKind.Blocked => "CurrentTaskBlockedRelationAddInput",
        _ => "CurrentTaskRelationAddInput"
    };

    public string ConfirmButtonAutomationId => _kind switch
    {
        TaskRelationKind.Parents => "CurrentTaskParentsRelationAddConfirmButton",
        TaskRelationKind.Containing => "CurrentTaskContainingRelationAddConfirmButton",
        TaskRelationKind.Blocking => "CurrentTaskBlockingRelationAddConfirmButton",
        TaskRelationKind.Blocked => "CurrentTaskBlockedRelationAddConfirmButton",
        _ => "CurrentTaskRelationAddConfirmButton"
    };

    public string CancelButtonAutomationId => _kind switch
    {
        TaskRelationKind.Parents => "CurrentTaskParentsRelationAddCancelButton",
        TaskRelationKind.Containing => "CurrentTaskContainingRelationAddCancelButton",
        TaskRelationKind.Blocking => "CurrentTaskBlockingRelationAddCancelButton",
        TaskRelationKind.Blocked => "CurrentTaskBlockedRelationAddCancelButton",
        _ => "CurrentTaskRelationAddCancelButton"
    };

    public string SuggestionsAutomationId => _kind switch
    {
        TaskRelationKind.Parents => "CurrentTaskParentsRelationSuggestions",
        TaskRelationKind.Containing => "CurrentTaskContainingRelationSuggestions",
        TaskRelationKind.Blocking => "CurrentTaskBlockingRelationSuggestions",
        TaskRelationKind.Blocked => "CurrentTaskBlockedRelationSuggestions",
        _ => "CurrentTaskRelationSuggestions"
    };

    public long FocusRequestVersion => _focusRequestVersion;

    public ICommand CancelCommand { get; }

    public ICommand ConfirmCommand { get; }

    public void Open(TaskRelationKind kind, TaskItemViewModel? currentTask)
    {
        if (currentTask == null || string.IsNullOrWhiteSpace(currentTask.Id))
        {
            Cancel();
            return;
        }

        _kind = kind;
        _currentTaskId = currentTask.Id;
        ResetTransientState();

        this.RaisePropertyChanged(nameof(IsOpen));
        this.RaisePropertyChanged(nameof(Kind));
        this.RaisePropertyChanged(nameof(IsOpenForParents));
        this.RaisePropertyChanged(nameof(IsOpenForContaining));
        this.RaisePropertyChanged(nameof(IsOpenForBlocking));
        this.RaisePropertyChanged(nameof(IsOpenForBlocked));
        this.RaisePropertyChanged(nameof(Header));
        this.RaisePropertyChanged(nameof(InputAutomationId));
        this.RaisePropertyChanged(nameof(ConfirmButtonAutomationId));
        this.RaisePropertyChanged(nameof(CancelButtonAutomationId));
        this.RaisePropertyChanged(nameof(SuggestionsAutomationId));

        RefreshSuggestions();
        RequestFocus();
    }

    public void SyncCurrentTask(TaskItemViewModel? currentTask)
    {
        if (!IsOpen)
        {
            return;
        }

        if (currentTask == null ||
            string.IsNullOrWhiteSpace(currentTask.Id) ||
            !string.Equals(_currentTaskId, currentTask.Id, StringComparison.Ordinal))
        {
            Cancel();
        }
    }

    public void Close()
    {
        Cancel();
    }

    private void Cancel()
    {
        if (_kind == null && string.IsNullOrWhiteSpace(_currentTaskId) && !IsOpen)
        {
            return;
        }

        _currentTaskId = null;
        _kind = null;
        ResetTransientState();

        this.RaisePropertyChanged(nameof(IsOpen));
        this.RaisePropertyChanged(nameof(Kind));
        this.RaisePropertyChanged(nameof(IsOpenForParents));
        this.RaisePropertyChanged(nameof(IsOpenForContaining));
        this.RaisePropertyChanged(nameof(IsOpenForBlocking));
        this.RaisePropertyChanged(nameof(IsOpenForBlocked));
        this.RaisePropertyChanged(nameof(Header));
        this.RaisePropertyChanged(nameof(InputAutomationId));
        this.RaisePropertyChanged(nameof(ConfirmButtonAutomationId));
        this.RaisePropertyChanged(nameof(CancelButtonAutomationId));
        this.RaisePropertyChanged(nameof(SuggestionsAutomationId));
    }

    private async Task ConfirmAsync()
    {
        var currentTask = ResolveCurrentTask();
        var candidate = ResolveCandidateForConfirm();
        var candidateTask = candidate != null ? _findTaskById(candidate.Task.Id) : null;

        if (currentTask == null || candidateTask == null)
        {
            return;
        }

        if (!_kind.HasValue || !_isCandidateValid(_kind.Value, currentTask, candidateTask))
        {
            _notificationManager.ErrorToast(_localization.Get("InvalidRelation"));
            RefreshSuggestions();
            return;
        }

        try
        {
            var added = await _addRelationAsync(_kind.Value, currentTask, candidateTask);
            if (!added)
            {
                _notificationManager.ErrorToast(_localization.Get("AddRelationFailed"));
                RefreshSuggestions();
                return;
            }
        }
        catch (Exception ex)
        {
            _notificationManager.ErrorToast(_localization.Format("AddRelationFailedWithError", ex.Message));
            RefreshSuggestions();
            return;
        }

        Cancel();
    }

    private void RefreshSuggestions()
    {
        var currentTask = ResolveCurrentTask();
        if (!IsOpen || currentTask == null || !_kind.HasValue)
        {
            Suggestions = Array.Empty<TaskRelationCandidateViewModel>();
            SelectedCandidate = null;
            return;
        }

        var query = (Query ?? string.Empty).Trim();
        var isFuzzySearch = _getIsFuzzySearch();

        IEnumerable<TaskItemViewModel> tasks = _getAllTasks() ?? Enumerable.Empty<TaskItemViewModel>();
        tasks = tasks
            .Where(static task => task != null && !string.IsNullOrWhiteSpace(task.Id))
            .Where(task => _isCandidateValid(_kind.Value, currentTask, task))
            .OrderBy(GetSortTitle)
            .ThenBy(task => task.Id, StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(query))
        {
            tasks = tasks.Where(task => Matches(task, query, isFuzzySearch)).Take(SearchSuggestionLimit);
        }
        else
        {
            tasks = tasks.Take(EmptyQuerySuggestionLimit);
        }

        var suggestions = tasks
            .Select(CreateCandidate)
            .ToList();

        Suggestions = suggestions;

        if (SelectedCandidate != null &&
            suggestions.All(candidate => candidate.Task.Id != SelectedCandidate.Task.Id))
        {
            SelectedCandidate = null;
        }

        if (SelectedCandidate == null && suggestions.Count == 1)
        {
            var candidate = suggestions[0];
            if (IsExactMatch(query, candidate))
            {
                SelectedCandidate = candidate;
            }
        }
    }

    private TaskItemViewModel? ResolveCurrentTask()
    {
        return string.IsNullOrWhiteSpace(_currentTaskId) ? null : _findTaskById(_currentTaskId);
    }

    private TaskRelationCandidateViewModel? ResolveCandidateForConfirm()
    {
        if (SelectedCandidate != null)
        {
            return SelectedCandidate;
        }

        if (Suggestions.Count == 1)
        {
            return Suggestions[0];
        }

        var query = (Query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        return Suggestions.FirstOrDefault(candidate => IsExactMatch(query, candidate));
    }

    private void ResetTransientState()
    {
        _query = string.Empty;
        _selectedCandidate = null;
        _suggestions = Array.Empty<TaskRelationCandidateViewModel>();

        this.RaisePropertyChanged(nameof(Query));
        this.RaisePropertyChanged(nameof(SelectedCandidate));
        this.RaisePropertyChanged(nameof(Suggestions));
        this.RaisePropertyChanged(nameof(HasSuggestions));
        this.RaisePropertyChanged(nameof(CanConfirm));
    }

    private TaskRelationCandidateViewModel CreateCandidate(TaskItemViewModel task)
    {
        var title = GetDisplayTitle(task);
        var context = _getContextText(task);
        return new TaskRelationCandidateViewModel(task, title, context);
    }

    private void RequestFocus()
    {
        _focusRequestVersion++;
        this.RaisePropertyChanged(nameof(FocusRequestVersion));
    }

    private void RaiseLocalizationPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(Watermark));
        this.RaisePropertyChanged(nameof(EmptyStateText));
        this.RaisePropertyChanged(nameof(Header));
    }

    private static bool Matches(TaskItemViewModel task, string userText, bool fuzzySearch)
    {
        var words = SearchDefinition.NormalizeText(userText)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return true;
        }

        var source = SearchDefinition.NormalizeText($"{task.OnlyTextTitle} {task.Description} {task.GetAllEmoji} {task.Id}");
        if (fuzzySearch)
        {
            foreach (var word in words)
            {
                var maxDist = FuzzyMatcher.GetMaxDistanceForWord(word);
                if (!FuzzyMatcher.IsFuzzyMatch(source, word, maxDist))
                {
                    return false;
                }
            }

            return true;
        }

        return words.All(source.Contains);
    }

    private static bool IsExactMatch(string query, TaskRelationCandidateViewModel candidate)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var normalizedQuery = SearchDefinition.NormalizeText(query);
        return SearchDefinition.NormalizeText(candidate.Title) == normalizedQuery ||
               SearchDefinition.NormalizeText(candidate.Task.Id) == normalizedQuery;
    }

    private static string GetSortTitle(TaskItemViewModel task)
    {
        return string.IsNullOrWhiteSpace(task.Title) ? task.Id : task.Title;
    }

    private static string GetDisplayTitle(TaskItemViewModel task)
    {
        return string.IsNullOrWhiteSpace(task.Title) ? task.Id : task.Title;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _localization.CultureChanged -= _cultureChangedHandler;
        _isDisposed = true;
    }
}
