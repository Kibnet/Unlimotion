using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace Unlimotion.Converters;

/// <summary>
/// Multiplies a double value by the numeric <c>ConverterParameter</c>. Used to turn a normalized avatar
/// pan offset (fraction of the circle) into pixels for a given circle diameter, so the same stored
/// offset frames the avatar identically at every render size.
/// </summary>
public class ScaleByConverter : MarkupExtension, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var factor = ToDouble(value);
        var multiplier = ToDouble(parameter);
        return factor * multiplier;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    private static double ToDouble(object? value)
    {
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0d,
        };
    }
}
