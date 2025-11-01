using System;
using System.Windows.Input;
using ReactiveUI;

namespace Unlimotion.ViewModel;

/// <summary>
/// Класс с командами для работы с датами
/// </summary>
public class DateCommands
{
    public DateCommands(TaskItemViewModel item)
    {
        //Begin
        SetBeginToday = ReactiveCommand.Create(() => item.PlannedBeginDateTime = DateTime.Today);
        SetBeginTomorrow = ReactiveCommand.Create(() => item.PlannedBeginDateTime = DateEx.Tomorrow);
        SetBeginNextMonday = ReactiveCommand.Create(() => item.PlannedBeginDateTime = DateEx.NextMonday);
        SetBegin1DayNextMonth = ReactiveCommand.Create(() => item.PlannedBeginDateTime = DateEx.FirstDayNextMonth);

        var hasBegin = item.WhenAny(m => m.PlannedBeginDateTime, time => time.Value != null);
        SetBeginNone = ReactiveCommand.Create(() => item.PlannedBeginDateTime = null, hasBegin);

        //End
        SetEndToday = ReactiveCommand.Create(() => item.PlannedEndDateTime = DateTime.Today,
            item.WhenAny(m => m.PlannedBeginDateTime, time => time.Value == null || time.Value <= DateTime.Today));
        SetEndTomorrow = ReactiveCommand.Create(() => item.PlannedEndDateTime = DateEx.Tomorrow,
            item.WhenAny(m => m.PlannedBeginDateTime, time => time.Value == null || time.Value <= DateEx.Tomorrow));
        SetEndNextFriday = ReactiveCommand.Create(() => item.PlannedEndDateTime = DateEx.NextFriday,
            item.WhenAny(m => m.PlannedBeginDateTime, time => time.Value == null || time.Value <= DateEx.NextFriday));

        var hasEnd = item.WhenAny(m => m.PlannedEndDateTime, time => time.Value != null);
        SetEnd1Days = ReactiveCommand.Create(() => item.PlannedEndDateTime = item.PlannedBeginDateTime, hasBegin);
        SetEnd5Days = ReactiveCommand.Create(() => item.PlannedEndDateTime = item.PlannedBeginDateTime.Value.AddDays(4), hasBegin);
        SetEnd7Days = ReactiveCommand.Create(() => item.PlannedEndDateTime = item.PlannedBeginDateTime.Value.AddDays(6), hasBegin);
        SetEnd10Days = ReactiveCommand.Create(() => item.PlannedEndDateTime = item.PlannedBeginDateTime.Value.AddDays(9), hasBegin);
        SetEnd1Month = ReactiveCommand.Create(() => item.PlannedEndDateTime = item.PlannedBeginDateTime.Value.AddMonths(1), hasBegin);
        SetEndNone = ReactiveCommand.Create(() => item.PlannedEndDateTime = null, hasEnd);
    }

    public ICommand SetBeginToday { get; }
    public ICommand SetBeginTomorrow { get; }
    public ICommand SetBeginNextMonday { get; }
    public ICommand SetBegin1DayNextMonth { get; }
    public ICommand SetBeginNone { get; }

    public ICommand SetEndToday { get; }
    public ICommand SetEndTomorrow { get; }
    public ICommand SetEndNextFriday { get; }
    public ICommand SetEnd1Days { get; }
    public ICommand SetEnd5Days { get; }
    public ICommand SetEnd7Days { get; }
    public ICommand SetEnd10Days { get; }
    public ICommand SetEnd1Month { get; }
    public ICommand SetEndNone { get; }
}