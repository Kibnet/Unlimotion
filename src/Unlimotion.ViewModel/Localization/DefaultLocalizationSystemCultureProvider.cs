using System.Globalization;

namespace Unlimotion.ViewModel.Localization;

public sealed class DefaultLocalizationSystemCultureProvider : ILocalizationSystemCultureProvider
{
    public DefaultLocalizationSystemCultureProvider()
    {
        SystemUICulture = CultureInfo.CurrentUICulture;
    }

    public CultureInfo SystemUICulture { get; }
}
