using System;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData.Binding;
using PropertyChanged;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class DateFilter
    {
        public DateTime? From { get; set; } 
        public DateTime? To { get; set; }
        public DateFilterOption CurrentOption { get; set; } = DateFilterDefinition.Today;
        public bool IsCustom { get; set; }

        public void SetDateTimes(DateFilterOption option)
        {
            SetDateTimes(option.Id);
        }

        public void SetDateTimes(string typeString)
        {
            var dateFilterType = Options.DateFilterOptions[typeString];
            var now = DateTime.Now.Date;

            switch (dateFilterType)
            {
                case Options.DateFilterType.AllTime:
                    From = null; To = null; break;
                case Options.DateFilterType.LastTwoDays:
                    From = now.AddDays(-2); To = now; break;
                case Options.DateFilterType.LastWeek:
                    From = now.AddDays(-7); To = now; break;
                case Options.DateFilterType.LastMonth:
                    From = now.AddDays(-30); To = now; break;
                case Options.DateFilterType.LastYear:
                    From = now.AddDays(-365); To = now; break;
                case Options.DateFilterType.Today:
                    From = now; To = now; break;
                case Options.DateFilterType.Week:
                    From = now.AddDays(-(int)now.DayOfWeek); To = now; break;
                case Options.DateFilterType.Month:
                    From = new DateTime(now.Year, now.Month, 1); To = now; break;
                case Options.DateFilterType.Quarter:
                    int mon;
                    switch (now.Month)
                    {
                        case 1:
                        case 2:
                        case 3:
                            mon = 1; break;
                        case 4:
                        case 5:
                        case 6:
                            mon = 4; break;
                        case 7:
                        case 8:
                        case 9:
                            mon = 7; break;
                        default:
                            mon = 10; break;
                    }
                    From = new DateTime(now.Year, mon, 1); To = now; break;
                case Options.DateFilterType.Year:
                    From = new DateTime(now.Year, 1, 1); To = now; break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public static class DateFilterDefinition
    {
        public static readonly DateFilterOption Today = new("Today", "DateFilterToday");
        public static readonly DateFilterOption AllTime = new("All Time", "DateFilterAllTime");

        public static ReadOnlyObservableCollection<DateFilterOption> GetDefinitions()
        {
            return new ReadOnlyObservableCollection<DateFilterOption>(new ObservableCollectionExtended<DateFilterOption>
            {
                Today,
                new("Week", "DateFilterWeek"),
                new("Month", "DateFilterMonth"),
                new("Quarter", "DateFilterQuarter"),
                new("Year", "DateFilterYear"),
                new("Last Two Days", "DateFilterLastTwoDays"),
                new("Last Week", "DateFilterLastWeek"),
                new("Last Month", "DateFilterLastMonth"),
                new("Last Year", "DateFilterLastYear"),
                AllTime
            });
        }

        public static DateFilterOption FindById(string? id)
        {
            return GetDefinitions().FirstOrDefault(option => option.Id == id) ?? Today;
        }
    }

    public sealed class DateFilterOption
    {
        public DateFilterOption(string id, string resourceKey)
        {
            Id = id;
            ResourceKey = resourceKey;
        }

        public string Id { get; }

        public string ResourceKey { get; }

        public override string ToString() => L10n.Get(ResourceKey);
    }
}
