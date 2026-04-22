using System.Globalization;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.Test;

public class LocalizationSettingsTests
{
    [Test]
    public async System.Threading.Tasks.Task SystemLanguage_UsesRussian_WhenSystemCultureIsRussian()
    {
        var localization = new LocalizationService(new FakeSystemCultureProvider("ru-RU"));

        localization.SetLanguage(LocalizationService.SystemLanguage);

        await Assert.That(localization.CurrentCulture.Name).IsEqualTo("ru");
        await Assert.That(localization.Get("Settings")).IsEqualTo("Настройки");
    }

    [Test]
    public async System.Threading.Tasks.Task SystemLanguage_FallsBackToEnglish_WhenSystemCultureIsUnsupported()
    {
        var localization = new LocalizationService(new FakeSystemCultureProvider("de-DE"));

        localization.SetLanguage(LocalizationService.SystemLanguage);

        await Assert.That(localization.CurrentCulture.Name).IsEqualTo("en");
        await Assert.That(localization.Get("Settings")).IsEqualTo("Settings");
    }

    [Test]
    public async System.Threading.Tasks.Task ManualLanguage_OverridesSystemCulture()
    {
        var localization = new LocalizationService(new FakeSystemCultureProvider("de-DE"));

        localization.SetLanguage(LocalizationService.RussianLanguage);

        await Assert.That(localization.CurrentCulture.Name).IsEqualTo("ru");
        await Assert.That(localization.LanguageMode).IsEqualTo(LocalizationService.RussianLanguage);
    }

    [Test]
    public async System.Threading.Tasks.Task SystemLanguage_UsesCapturedSystemCultureAfterManualSwitch()
    {
        var localization = new LocalizationService(new FakeSystemCultureProvider("ru-RU"));

        localization.SetLanguage(LocalizationService.EnglishLanguage);
        localization.SetLanguage(LocalizationService.SystemLanguage);

        await Assert.That(localization.CurrentCulture.Name).IsEqualTo("ru");
        await Assert.That(localization.Get("Settings")).IsEqualTo("Настройки");
    }

    [Test]
    public async System.Threading.Tasks.Task RussianResources_HaveSameKeysAsFallbackResources()
    {
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));

        var fallbackKeys = localization.GetResourceKeys(CultureInfo.InvariantCulture);
        var russianKeys = localization.GetResourceKeys(CultureInfo.GetCultureInfo("ru"));

        await Assert.That(russianKeys).IsEquivalentTo(fallbackKeys);
    }

    private sealed class FakeSystemCultureProvider : ILocalizationSystemCultureProvider
    {
        public FakeSystemCultureProvider(string cultureName)
        {
            SystemUICulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public CultureInfo SystemUICulture { get; }
    }
}
