using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace Unlimotion.Converters;

public class MinValueConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var number = value is double ? (double)value : double.MaxValue;
        if (!double.TryParse(parameter.ToString(), out var number2))
        {
            number2 = double.MaxValue;
        }
        if (number2>number)
        {
            return number;
        }
        return number2;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return this;
    }
}