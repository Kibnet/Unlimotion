using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public static class Options
{
    public static readonly Dictionary<string, DateFilterType> DateFilterOptions = new()
    {
        {"All", DateFilterType.All},
        {"Last two days", DateFilterType.LastTwoDays},
        {"Last week", DateFilterType.LastWeek},
        {"Last month", DateFilterType.LastMonth},
        {"Last year", DateFilterType.LastYear},
    };
    
    public enum DateFilterType
    {
        All = 0,
        LastTwoDays = 2,
        LastWeek = 7,
        LastMonth = 30,
        LastYear = 365
    }
}