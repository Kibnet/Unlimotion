using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace Unlimotion.Converters;

/// <summary>
/// True when the bound value represents a non-empty count: a positive number,
/// or a non-empty collection. Used to hide zero-count badges (e.g. the green
/// "ready now" count pill in the sidebar) when there is nothing to show.
/// </summary>
public class CountToBoolConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            null => false,
            int i => i > 0,
            long l => l > 0,
            double d => d > 0,
            ICollection c => c.Count > 0,
            IEnumerable e => HasAny(e),
            _ => false,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool HasAny(IEnumerable enumerable)
    {
        foreach (var _ in enumerable)
        {
            return true;
        }

        return false;
    }

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
