using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using Unlimotion.ViewModel.Localization;
using Unlimotion.ViewModel.Search;

namespace Unlimotion.ViewModel;

public sealed class TaskRelationPickerViewModel : ReactiveObject, IDisposable
{
    private const int EmptyQuerySuggestionLimit = 30;
    private const int SearchSuggestionLimit = 100;

    private readonly TaskRelationKind _kind;
    private readonly Func<TaskItemViewModel?> _getCurrentTask;
    private readonly Func<IEnumerable<TaskItemViewModel>> _getAllTasks;
    private readonly Func<bool> _getIsFuzzySearch;
    private readonly Func<TaskRelationKind, TaskItemViewModel, TaskItemViewModel, bool> _isCandidateValid;
    private readonly Func<TaskRelationKind, TaskItemViewModel, TaskItemViewModel, Task<bool>> _addRelationAsync;
    private readonly Func<TaskItemViewModel, string> _getContextText;
    private readonly INotificationManagerWrapper _notificationManager;
    private readonly ILocalizationService _localization;
    private readonly EventHandler _cultureChangedHandler;
    private bool _isDisposed;

    private bool _isExpanded;
    private string _query = string.Empty;
    private TaskRelationCandidateViewModel? _selectedCandidate;
    private IReadOnlyList<TaskRelationCandidateViewModel> _suggestions = Array.Empty<TaskRelationCandidateViewModel>();

    public TaskRelationPickerViewModel(
        TaskRelationKind kind,
        Func<TaskItemViewModel?> getCurrentTask,
        Func<IEnumerable<TaskItemViewModel>> getAllTasks,
        Func<bool> getIsFuzzySearch,
        Func<TaskRelationKind, TaskItemViewModel, TaskItemViewModel, bool> isCandidateValid,
        Func<TaskRelationKind, TaskItemViewModel, TaskItemViewModel, Task<bool>> addRelationAsync,
        Func<TaskItemViewModel, string> getContextText,
        INotificationManagerWrapper notificationManager,
        ILocalizationService? localizationService = null)
    {
        _kind = kind;
        _getCurrentTask = getCurrentTask;
        _getAllTasks = getAllTasks;
        _getIsFuzzySearch = getIsFuzzySearch;
        _isCandidateValid = isCandidateValid;
        _addRelationAsync = addRelationAsync;
        _getContextText = getContextText;
        _notificationManager = notificationManager;
        _localization = localizationService ?? LocalizationService.Current;

        OpenCommand = ReactiveCommand.Create(Open);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ConfirmCommand = ReactiveCommand.CreateFromTask(ConfirmAsync, this.WhenAnyValue(vm => vm.CanConfirm));
        _cultureChangedHandler = (_, _) => this.RaisePropertyChanged(nameof(Watermark));
        _localization.CultureChanged += _cultureChangedHandler;
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                this.RaiseAndSetIfChanged(ref _isExpanded, value);
                if (value)
                {
                    RefreshSuggestions();
                }
                else
                {
                    ResetTransientState(clearExpansion: false);
                }
            }
        }
    }

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
                this.RaisePropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public bool CanConfirm => ResolveCandidateForConfirm() != null;

    public string Watermark => _localization.Get("FindTask");

    public string KindName => _kind.ToString();

    public string AddButtonAutomationId => $"CurrentTask{KindName}RelationAddButton";

    public string InputAutomationId => $"CurrentTask{KindName}RelationAddInput";

    public string ConfirmButtonAutomationId => $"CurrentTask{KindName}RelationAddConfirmButton";

    public string CancelButtonAutomationId => $"CurrentTask{KindName}RelationAddCancelButton";

    public ICommand OpenCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ConfirmCommand { get; }

    private void Open()
    {
        IsExpanded = true;
        Query = string.Empty;
        SelectedCandidate = null;
        RefreshSuggestions();
    }

    private void Cancel()
    {
        IsExpanded = false;
    }

    private async Task ConfirmAsync()
    {
        var currentTask = _getCurrentTask();
        var candidate = ResolveCandidateForConfirm();

        if (currentTask == null || candidate == null)
        {
            return;
        }

        if (!_isCandidateValid(_kind, currentTask, candidate.Task))
        {
            _notificationManager.ErrorToast(_localization.Get("InvalidRelation"));
            RefreshSuggestions();
            return;
        }

        try
        {
            var added = await _addRelationAsync(_kind, currentTask, candidate.Task);
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

        IsExpanded = false;
    }

    private void RefreshSuggestions()
    {
        var currentTask = _getCurrentTask();
        if (!IsExpanded || currentTask == null)
        {
            Suggestions = Array.Empty<TaskRelationCandidateViewModel>();
            SelectedCandidate = null;
            return;
        }

        var query = (Query ?? string.Empty).Trim();
        var isFuzzySearch = _getIsFuzzySearch();

        IEnumerable<TaskItemViewModel> tasks = _getAllTasks() ?? Enumerable.Empty<TaskItemViewModel>();
        tasks = tasks
            .Where(task => task != null && !string.IsNullOrWhiteSpace(task.Id))
            .Where(task => _isCandidateValid(_kind, currentTask, task))
            .OrderBy(task => GetSortTitle(task))
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

    private void ResetTransientState(bool clearExpansion)
    {
        _query = string.Empty;
        _selectedCandidate = null;
        _suggestions = Array.Empty<TaskRelationCandidateViewModel>();

        this.RaisePropertyChanged(nameof(Query));
        this.RaisePropertyChanged(nameof(SelectedCandidate));
        this.RaisePropertyChanged(nameof(Suggestions));
        this.RaisePropertyChanged(nameof(CanConfirm));

        if (clearExpansion && _isExpanded)
        {
            _isExpanded = false;
            this.RaisePropertyChanged(nameof(IsExpanded));
        }
    }

    private TaskRelationCandidateViewModel CreateCandidate(TaskItemViewModel task)
    {
        var title = GetDisplayTitle(task);
        var context = _getContextText(task);
        return new TaskRelationCandidateViewModel(task, title, context);
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
