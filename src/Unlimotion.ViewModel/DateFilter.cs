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
        public string CurrentOption { get; set; } = "All";
        public bool IsCustom { get; set; }

        public void SetDateTimes(string typeString)
        {
            var dateFilterType = Options.DateFilterOptions[typeString];
            
            switch (dateFilterType)
            {
                case Options.DateFilterType.All:
                    To = null;
                    From = null;
                    break;
                case Options.DateFilterType.LastTwoDays:
                case Options.DateFilterType.LastWeek:
                case Options.DateFilterType.LastMonth:
                case Options.DateFilterType.LastYear:
                    var now = DateTime.Now.Date;
                    var dayDiff = (int) dateFilterType;
                    From = now.AddDays(-dayDiff);
                    To = now;
                    break;
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
