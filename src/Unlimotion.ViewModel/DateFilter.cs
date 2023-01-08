using System;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData.Binding;
using PropertyChanged;

namespace Unlimotion.ViewModel
{
    [AddINotifyPropertyChangedInterface]
    public class DateFilter
    {
        public DateTime? From { get; set; } 
        public DateTime? To { get; set; }
        public string CurrentOption { get; set; } = "Today";
        public bool IsCustom { get; set; }

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
        public static ReadOnlyObservableCollection<string> GetDefinitions()
        {
            return new ReadOnlyObservableCollection<string>(new ObservableCollectionExtended<string>(Options.DateFilterOptions.Keys.ToList()));
        }
    }
}
