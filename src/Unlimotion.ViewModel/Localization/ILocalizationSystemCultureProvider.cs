using System.Globalization;

namespace Unlimotion.ViewModel.Localization;

public interface ILocalizationSystemCultureProvider
{
    CultureInfo SystemUICulture { get; }
}
