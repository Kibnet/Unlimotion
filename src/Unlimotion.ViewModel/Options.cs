using System;
using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public static class Options
{
    public static readonly Dictionary<string, DateFilterType> DateFilterOptions = new()
    {
        {"Today", DateFilterType.Today},
        {"Week", DateFilterType.Week},
        {"Month", DateFilterType.Month},
        {"Quarter", DateFilterType.Quarter},
        {"Year", DateFilterType.Year},
        {"Last Two Days", DateFilterType.LastTwoDays},
        {"Last Week", DateFilterType.LastWeek},
        {"Last Month", DateFilterType.LastMonth},
        {"Last Year", DateFilterType.LastYear},
        {"All Time", DateFilterType.AllTime},
    };

    public enum DateFilterType
    {
        Today,
        Week,
        Month,
        Quarter,
        Year,
        LastTwoDays,
        LastWeek,
        LastMonth,
        LastYear,
        AllTime,
    }
}