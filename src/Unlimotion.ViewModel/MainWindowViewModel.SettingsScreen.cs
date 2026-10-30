using System;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Windows.Input;
using ReactiveUI;

namespace Unlimotion.ViewModel;

public partial class MainWindowViewModel
{
    public bool IsSettingsOpen { get; private set; }
    public bool IsSettingsDialogOpen { get; set; }
    public int SettingsPickerDepth { get; private set; }
    public bool IsSettingsPickerOpen => SettingsPickerDepth > 0;
    public ICommand OpenSettingsCommand { get; private set; } = null!;
    public ICommand CloseSettingsCommand { get; private set; } = null!;

    private void InitializeSettingsScreen()
    {
        this.WhenAnyValue(vm => vm.IsTasksLoading)
            .Subscribe(value => Settings.IsTaskDataLoading = value).AddToDispose(this);
        Settings.WhenAnyValue(settings => settings.IsTaskSpaceRecoveryRequired)
            .Subscribe(value => { if (value) CloseSettings(); }).AddToDispose(this);
        OpenSettingsCommand = new AdmissionGuardedCommand(ReactiveCommand.Create(OpenSettings),
            () => !IsSettingsDialogOpen && !IsSettingsPickerOpen && !Settings.IsTaskSpaceRecoveryRequired, this, Settings)
            .AddToDisposeAndReturn(this);
        CloseSettingsCommand = ReactiveCommand.Create(CloseSettings).AddToDisposeAndReturn(this);
    }

    public void OpenSettings()
    {
        if (!IsSettingsDialogOpen && !IsSettingsPickerOpen && !Settings.IsTaskSpaceRecoveryRequired) IsSettingsOpen = true;
    }

    public void CloseSettings() => IsSettingsOpen = false;

    public IDisposable BeginSettingsPicker()
    {
        SettingsPickerDepth++;
        return Disposable.Create(() => SettingsPickerDepth--);
    }

    private ICommand GuardTaskMutation(ICommand command) =>
        new AdmissionGuardedCommand(command, () => !Settings.IsSourceMutationBlocked, Settings)
            .AddToDisposeAndReturn(connectionDisposableList);

    private void GuardTaskMutationCommands()
    {
        CreateSibling = GuardTaskMutation(CreateSibling);
        CreateBlockedSibling = GuardTaskMutation(CreateBlockedSibling);
        CreateInner = GuardTaskMutation(CreateInner);
        Remove = GuardTaskMutation(Remove);
        DeleteSelectedTreeItemsCommand = GuardTaskMutation(DeleteSelectedTreeItemsCommand);
        PasteTaskOutlineTreeCommand = GuardTaskMutation(PasteTaskOutlineTreeCommand);
        PasteTaskOutlineCommand = GuardTaskMutation(PasteTaskOutlineCommand);
    }
}
