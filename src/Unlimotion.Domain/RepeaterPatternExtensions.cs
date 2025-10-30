using System;
using System.Collections.Generic;
using System.Linq;

namespace Unlimotion.Domain;

public static class RepeaterPatternExtensions
{
    public static DateTimeOffset GetNextOccurrence(this RepeaterPattern repeater, DateTimeOffset start)
    {
        var prev = start;
        if (repeater.AfterComplete)
        {
            prev = DateTimeOffset.Now.Date;
        }
        switch (repeater.Type)
        {
            case RepeaterType.None:
                return prev;
            case RepeaterType.Daily:
                return prev.AddDays(repeater.Period);
            case RepeaterType.Weekly:
                // Convert pattern to weekday flags
                bool monday = false, tuesday = false, wednesday = false, thursday = false, friday = false, saturday = false, sunday = false;
                if (repeater.Pattern != null)
                {
                    foreach (var i in repeater.Pattern)
                    {
                        switch (i)
                        {
                            case 0: monday = true; break;
                            case 1: tuesday = true; break;
                            case 2: wednesday = true; break;
                            case 3: thursday = true; break;
                            case 4: friday = true; break;
                            case 5: saturday = true; break;
                            case 6: sunday = true; break;
                        }
                    }
                }

                bool AnyWeekDays() => monday || tuesday || wednesday || thursday || friday || saturday || sunday;
                bool[] WeekDaysArray() => new[] { monday, tuesday, wednesday, thursday, friday, saturday, sunday };

                if (AnyWeekDays())
                {
                    var index = ((int)prev.DayOfWeek + 6) % 7;
                    var newindex = index + 1;
                    while (newindex < 7)
                    {
                        if (WeekDaysArray()[newindex])
                        {
                            return prev.AddDays(newindex - index);
                        }

                        newindex++;
                    }

                    var newprev = prev.AddDays(7 - index + (repeater.Period - 1) * 7);
                    newindex = 0;
                    while (newindex < 7)
                    {
                        if (WeekDaysArray()[newindex])
                        {
                            return newprev.AddDays(newindex);
                        }

                        newindex++;
                    }
                    throw new NotImplementedException();
                }
                else
                {
                    return prev.AddDays(7 * repeater.Period);
                }
            case RepeaterType.Monthly:
                return prev.AddMonths(repeater.Period);
            case RepeaterType.Yearly:
                return prev.AddYears(repeater.Period);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}