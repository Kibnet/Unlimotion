using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Unlimotion.ViewModel;

public enum BackupConflictResolution
{
    UseCurrent = 0,
    UseIncoming = 1
}

public enum BackupConflictFieldSource
{
    UseCurrent = 0,
    UseIncoming = 1,
    Merge = 2
}

public enum BackupConflictFieldChangeKind
{
    CurrentOnly = 0,
    IncomingOnly = 1,
    BothSame = 2,
    BothDifferent = 3,
    Unchanged = 4
}

public sealed record BackupConflictFieldSelection(
    string FieldPath,
    BackupConflictFieldSource Source,
    string? CustomValue = null);

public sealed record BackupConflictField(
    string FieldPath,
    string DisplayName,
    string AncestorValue,
    string CurrentValue,
    string IncomingValue,
    string MergedValue,
    bool CanMerge,
    BackupConflictFieldSource DefaultSource,
    BackupConflictFieldChangeKind ChangeKind,
    bool CanEditMergedValue = false)
{
    public BackupConflictField(
        string fieldPath,
        string displayName,
        string currentValue,
        string incomingValue,
        string mergedValue,
        bool canMerge,
        BackupConflictFieldSource defaultSource,
        BackupConflictFieldChangeKind changeKind)
        : this(
            fieldPath,
            displayName,
            string.Empty,
            currentValue,
            incomingValue,
            mergedValue,
            canMerge,
            defaultSource,
            changeKind)
    {
    }

    public bool IsRealConflict => ChangeKind == BackupConflictFieldChangeKind.BothDifferent;
}

public sealed class BackupConflictFieldDecision : INotifyPropertyChanged
{
    private BackupConflictFieldSource _selectedSource;
    private string _editedMergedValue;

    public BackupConflictFieldDecision(BackupConflictField field)
    {
        Field = field;
        _editedMergedValue = field.MergedValue;
        _selectedSource = field.DefaultSource == BackupConflictFieldSource.Merge && !field.CanMerge
            ? BackupConflictFieldSource.UseCurrent
            : field.DefaultSource;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BackupConflictField Field { get; }

    public string FieldPath => Field.FieldPath;

    public string DisplayName => Field.DisplayName;

    public string AncestorValue => Field.AncestorValue;

    public string CurrentValue => Field.CurrentValue;

    public string IncomingValue => Field.IncomingValue;

    public string MergedValue => Field.MergedValue;

    public bool CanMerge => Field.CanMerge;

    public bool CanEditMergedValue => Field.CanEditMergedValue;

    public BackupConflictFieldChangeKind ChangeKind => Field.ChangeKind;

    public bool IsRealConflict => Field.IsRealConflict;

    public bool IsNonConflictingChange => !Field.IsRealConflict;

    public BackupConflictFieldSource SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value == BackupConflictFieldSource.Merge && !CanMerge)
            {
                value = Field.DefaultSource;
            }

            if (_selectedSource == value)
            {
                return;
            }

            _selectedSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCurrentSelected));
            OnPropertyChanged(nameof(IsIncomingSelected));
            OnPropertyChanged(nameof(IsMergeSelected));
            OnPropertyChanged(nameof(IsEditableMergeSelected));
            OnPropertyChanged(nameof(IsSelectedValueTextVisible));
            OnPropertyChanged(nameof(SelectedValue));
            OnPropertyChanged(nameof(CompactValue));
        }
    }

    public bool IsCurrentSelected
    {
        get => SelectedSource == BackupConflictFieldSource.UseCurrent;
        set
        {
            if (value)
            {
                SelectedSource = BackupConflictFieldSource.UseCurrent;
            }
        }
    }

    public bool IsIncomingSelected
    {
        get => SelectedSource == BackupConflictFieldSource.UseIncoming;
        set
        {
            if (value)
            {
                SelectedSource = BackupConflictFieldSource.UseIncoming;
            }
        }
    }

    public bool IsMergeSelected
    {
        get => SelectedSource == BackupConflictFieldSource.Merge;
        set
        {
            if (value)
            {
                SelectedSource = BackupConflictFieldSource.Merge;
            }
        }
    }

    public string EditedMergedValue
    {
        get => _editedMergedValue;
        set
        {
            if (_editedMergedValue == value)
            {
                return;
            }

            _editedMergedValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedValue));
            OnPropertyChanged(nameof(CompactValue));
        }
    }

    public bool IsEditableMergeSelected => CanEditMergedValue && IsMergeSelected;

    public bool IsSelectedValueTextVisible => !IsEditableMergeSelected;

    public string SelectedValue => SelectedSource switch
    {
        BackupConflictFieldSource.UseIncoming => IncomingValue,
        BackupConflictFieldSource.Merge => CanEditMergedValue ? EditedMergedValue : MergedValue,
        _ => CurrentValue
    };

    public string CompactValue => SelectedValue;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record BackupConflictFile
{
    public BackupConflictFile(
        string path,
        bool hasCurrentVersion,
        bool hasIncomingVersion)
        : this(path, hasCurrentVersion, hasIncomingVersion, new List<BackupConflictField>())
    {
    }

    public BackupConflictFile(
        string path,
        bool hasCurrentVersion,
        bool hasIncomingVersion,
        IReadOnlyList<BackupConflictField>? fields)
    {
        Path = path;
        HasCurrentVersion = hasCurrentVersion;
        HasIncomingVersion = hasIncomingVersion;
        Fields = fields ?? new List<BackupConflictField>();
    }

    public string Path { get; init; }

    public bool HasCurrentVersion { get; init; }

    public bool HasIncomingVersion { get; init; }

    public IReadOnlyList<BackupConflictField> Fields { get; init; }

    public bool CanResolveByFields => HasCurrentVersion && HasIncomingVersion && Fields.Count > 0;
}

public sealed record BackupConflictStatus(
    bool IsInProgress,
    IReadOnlyList<BackupConflictFile> Conflicts)
{
    public static BackupConflictStatus None { get; } =
        new(false, new List<BackupConflictFile>());
}
