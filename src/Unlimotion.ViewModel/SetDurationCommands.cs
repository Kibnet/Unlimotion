using System;
using System.Windows.Input;
using PropertyChanged;
using ReactiveUI;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class SetDurationCommands
{
    TaskItemViewModel taskItemViewModel;

    public SetDurationCommands(TaskItemViewModel item)
    {
        taskItemViewModel = item;
        var hasDuration = taskItemViewModel.WhenAny(m => m.PlannedDuration, time => time.Value != null);
        var any = taskItemViewModel.WhenAny(m => m.PlannedDuration, time => true);

        OneMinCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromMinutes(1), any);
        FiveMinutesCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromMinutes(5), any);
        TenMinutesCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromMinutes(10), any);
        TwentyMinutesCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromMinutes(20), any);
        FortyMinutesCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromMinutes(40), any);
        OneHourCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromHours(1), any);
        TwoHoursCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromHours(2), any);
        FourHoursCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromHours(4), any);
        OneDayCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromDays(1), any);
        TwoDaysCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromDays(2), any);
        FourDaysCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromDays(4), any);
        EightDaysCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = TimeSpan.FromDays(8), any);
        NoneCommand = ReactiveCommand.Create(() => taskItemViewModel.PlannedDuration = null, hasDuration);
    }
    
    public ICommand OneMinCommand { get; set; }
    public ICommand FiveMinutesCommand { get; set; }
    public ICommand TenMinutesCommand { get; set; }
    public ICommand TwentyMinutesCommand { get; set; }
    public ICommand FortyMinutesCommand { get; set; }
    public ICommand OneHourCommand { get; set; }
    public ICommand TwoHoursCommand { get; set; }
    public ICommand FourHoursCommand { get; set; }
    public ICommand OneDayCommand { get; set; }
    public ICommand TwoDaysCommand { get; set; }
    public ICommand FourDaysCommand { get; set; }
    public ICommand EightDaysCommand { get; set; }
    public ICommand NoneCommand { get; set; }
}