using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace Unlimotion
{
    /// <summary>
    /// Utility to convert between <see cref="string"/> and <see cref="TimeSpan"/>.
    /// Examples of valid strings: "1d, 5h, 20m, 50s, 300ms", "300ms", "50s", "2m"
    /// </summary>
    public class TimeSpanStringConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var span = value as TimeSpan?;
            if (span == null)
            {
                return "";
            }
            TimeSpan timeSpan = span.Value;
            StringBuilder sb = new StringBuilder();
            var list = new List<string>();

            if (timeSpan == TimeSpan.Zero)
            {
                sb.Append("0");
            }
            else
            {
                Action<int, string> addTimeValue = (timeValue, description) =>
                {
                    if ((timeValue > 0))
                    {
                        list.Add($"{timeValue}{description}");
                    }
                };

                addTimeValue(timeSpan.Days, "d");
                addTimeValue(timeSpan.Hours, "h");
                addTimeValue(timeSpan.Minutes, "m");
                addTimeValue(timeSpan.Seconds, "s");
                addTimeValue(timeSpan.Milliseconds, "ms");
                sb.AppendJoin(", ", list);
            }

            return sb.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Got a string ??
            var text = value as string;
            if (text == null)
            {
                if (value != null)
                    throw new ArgumentException("value must be a string");
            }

            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Get the comma-seperated parts
            var parts = text.Split(',');
            
            // Helper using a regular expression to find the integer value from one
            // of the comma-seperated parts for a particular time unit type
            Func<string, int> extractPart = (unit) =>
            {
                int result = 0;

                var regexPattern = $"^([\\d]+)({unit})$";

                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();
                    var groups = Regex.Match(input: trimmedPart, pattern: regexPattern);
                    if (groups.Groups.Count == 3)
                    {
                        var numberAsText = groups.Groups[1].Value;
                        int.TryParse(numberAsText, out result);
                    }
                }

                return result;
            };


            var days = extractPart("d");
            var hours = extractPart("h");
            var minutes = extractPart("m");
            var seconds = extractPart("s");
            var milliseconds = extractPart("ms");
            var daysString = (parts.Length == 5) ? parts[0] : "0";

            var newTimeSpan = new TimeSpan(days, hours, minutes, seconds, milliseconds);
            return newTimeSpan;
        }
    }
}