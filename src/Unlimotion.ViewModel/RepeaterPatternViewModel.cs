using System;
using System.Collections.Generic;
using System.Linq;
using PropertyChanged;
using Unlimotion.Domain;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class RepeaterPatternViewModel
{
    public RepeaterPatternViewModel(RepeaterPattern repeater)
    {
        Model = repeater;
    }

    public RepeaterPatternViewModel()
    {
        Model = new RepeaterPattern();
    }

    public RepeaterPattern Model
    {
        get
        {
            var pattern = new List<int>();
            if (Monday) pattern.Add(0);
            if (Tuesday) pattern.Add(1);
            if (Wednesday) pattern.Add(2);
            if (Thursday) pattern.Add(3);
            if (Friday) pattern.Add(4);
            if (Saturday) pattern.Add(5);
            if (Sunday) pattern.Add(6);
            return new RepeaterPattern
            {
                Type = Type,
                AfterComplete = AfterComplete,
                Period = Period,
                Pattern = pattern,
            };
        }
        set
        {
            Type = value.Type;
            AfterComplete = value.AfterComplete;
            Period = value.Period;
            Monday = false;
            Tuesday = false;
            Wednesday = false;
            Thursday = false;
            Friday = false;
            Saturday = false;
            Sunday = false;
            if (value.Pattern != null)
            {
                foreach (var i in value.Pattern)
                {
                    switch (i)
                    {
                        case 0: Monday = true; break;
                        case 1: Tuesday = true; break;
                        case 2: Wednesday = true; break;
                        case 3: Thursday = true; break;
                        case 4: Friday = true; break;
                        case 5: Saturday = true; break;
                        case 6: Sunday = true; break;
                    }
                }
            }
        }
    }
    
    public RepeaterType Type { get; set; }

    public IReadOnlyList<RepeaterTypeOption> RepeaterTypes => RepeaterTypeOption.Definitions;

    public RepeaterTypeOption SelectedRepeaterType
    {
        get => RepeaterTypeOption.Find(Type);
        set
        {
            if (value != null)
            {
                Type = value.Value;
            }
        }
    }

    public bool WorkDays
    {
        get => Monday && Tuesday && Wednesday && Thursday && Friday;
        set
        {
            Monday = value;
            Tuesday = value;
            Wednesday = value;
            Thursday = value;
            Friday = value;
        }
    }

    public bool AnyWeekDays => Monday || Tuesday || Wednesday || Thursday || Friday || Saturday || Sunday;

    public bool[] WeekDaysArray => new[] { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday };

    public DateTimeOffset GetNextOccurrence(DateTimeOffset start)
    {
        var prev = start;
        if (AfterComplete)
        {
            prev = DateTimeOffset.Now.Date;
        }
        switch (Type)
        {
            case RepeaterType.None:
                return prev;
            case RepeaterType.Daily:
                return prev.AddDays(Period);
            case RepeaterType.Weekly:
                if (AnyWeekDays)
                {
                    var index = ((int)prev.DayOfWeek + 6) % 7;
                    var newindex = index + 1;
                    while (newindex < 7)
                    {
                        if (WeekDaysArray[newindex])
                        {
                            return prev.AddDays(newindex - index);
                        }

                        newindex++;
                    }

                    var newprev = prev.AddDays(7 - index + (Period - 1) * 7);
                    newindex = 0;
                    while (newindex < 7)
                    {
                        if (WeekDaysArray[newindex])
                        {
                            return newprev.AddDays(newindex);
                        }

                        newindex++;
                    }
                    throw new NotImplementedException();
                }

                return prev.AddDays(7 * Period);
                break;
            case RepeaterType.Monthly:
                return prev.AddMonths(Period);
            case RepeaterType.Yearly:
                return prev.AddYears(Period);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }


    public int Period { get; set; } = 1;
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public bool Sunday { get; set; }
    public bool AfterComplete { get; set; }

    public string Title
    {
        get
        {
            var typeText = Type == RepeaterType.Weekly && WorkDays
                ? L10n.Get("RepeaterTypeWeeklyWorkDays")
                : RepeaterTypeOption.Find(Type).ToString();
            return L10n.Format("RepeaterPatternTitle", typeText, Period);
        }
    }
}

public sealed record RepeaterTypeOption(RepeaterType Value, string ResourceKey)
{
    public static readonly IReadOnlyList<RepeaterTypeOption> Definitions =
    [
        new(RepeaterType.None, "RepeaterTypeNone"),
        new(RepeaterType.Daily, "RepeaterTypeDaily"),
        new(RepeaterType.Weekly, "RepeaterTypeWeekly"),
        new(RepeaterType.Monthly, "RepeaterTypeMonthly"),
        new(RepeaterType.Yearly, "RepeaterTypeYearly")
    ];

    public static RepeaterTypeOption Find(RepeaterType value) =>
        Definitions.FirstOrDefault(option => option.Value == value) ?? Definitions[0];

    public override string ToString() => L10n.Get(ResourceKey);
}
