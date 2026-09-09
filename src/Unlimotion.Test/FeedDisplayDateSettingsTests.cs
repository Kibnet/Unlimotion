using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedDisplayDateSettingsTests
{
    [Test]
    public async Task DefaultMatchesRequestedRussianDate()
    {
        await Assert.That(FeedDateDisplaySettings.Format(new DateOnly(2026, 9, 9), null,
            CultureInfo.GetCultureInfo("ru-RU"))).IsEqualTo("9 сентября 2026, среда");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("  ")]
    [Arguments("yyyy\nMM")]
    [Arguments("yyyy\rMM")]
    [Arguments("yyyy\u2028MM")]
    [Arguments("yyyy '")]
    [Arguments("HH:mm")]
    [Arguments("%")]
    public async Task InvalidFormatsFallBackWithoutThrowing(string? format)
    {
        await Assert.That(FeedDateDisplaySettings.IsValidFormat(format)).IsFalse();
        await Assert.That(FeedDateDisplaySettings.Format(new DateOnly(2026, 9, 9), format,
            CultureInfo.GetCultureInfo("ru-RU"))).IsEqualTo("9 сентября 2026, среда");
    }

    [Test]
    public async Task FormatLengthHasExplicitBoundary()
    {
        await Assert.That(FeedDateDisplaySettings.IsValidFormat("'" + new string('a', 126) + "'")).IsTrue();
        await Assert.That(FeedDateDisplaySettings.IsValidFormat("'" + new string('a', 127) + "'")).IsFalse();
    }

    [Test]
    public async Task OnlyValidDraftPersistsAcrossReloadWithoutChangingFilenameSetting()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"feed-display-{Guid.NewGuid():N}.json");
        File.WriteAllText(configPath, "{\"NoteVault\":{\"DailyFileNameFormat\":\"yyyy.MM.dd\"}}");
        try
        {
            var config = WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false);
            using var configLifetime = config as IDisposable;
            var settings = new SettingsViewModel(config);
            settings.DisplayDateFormatDraft = "dd.MM.yyyy";
            var version = settings.DisplayDatePresentationVersion;
            settings.DisplayDateFormatDraft = "yyyy '\n";
            await Assert.That(settings.DisplayDateFormat).IsEqualTo("dd.MM.yyyy");
            await Assert.That(settings.IsDisplayDateFormatValidationVisible).IsTrue();
            await Assert.That(settings.DisplayDatePresentationVersion).IsEqualTo(version);
            var reloaded = WritableJsonConfigurationFabric.Create(configPath, reloadOnChange: false);
            using var reloadedLifetime = reloaded as IDisposable;
            await Assert.That(new SettingsViewModel(reloaded).DisplayDateFormat).IsEqualTo("dd.MM.yyyy");
            await Assert.That(reloaded["NoteVault:DailyFileNameFormat"]).IsEqualTo("yyyy.MM.dd");
            settings.ResetDisplayDateFormat();
            await Assert.That(settings.DisplayDateFormat).IsEqualTo(FeedDateDisplaySettings.DefaultFormat);
            await Assert.That(settings.IsDisplayDateFormatValidationVisible).IsFalse();
            await Assert.That(config["FeedAppearance:DisplayDateFormat"]).IsEqualTo(FeedDateDisplaySettings.DefaultFormat);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Test]
    public async Task CorruptConfigurationUsesDefaultWithoutRewritingConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeedAppearance:DisplayDateFormat"] = "HH:mm"
        }).Build();
        var settings = new SettingsViewModel(config);
        await Assert.That(settings.DisplayDateFormat).IsEqualTo(FeedDateDisplaySettings.DefaultFormat);
        await Assert.That(config["FeedAppearance:DisplayDateFormat"]).IsEqualTo("HH:mm");
        settings.ResetDisplayDateFormat();
        await Assert.That(config["FeedAppearance:DisplayDateFormat"]).IsEqualTo(FeedDateDisplaySettings.DefaultFormat);
    }

    [Test]
    public async Task LanguageSwitchRefreshesPreviewAndObservablePresentationVersion()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var localization = new LocalizationService();
        var settings = new SettingsViewModel(config, localizationService: localization);
        settings.LanguageMode = "ru";
        var changes = 0;
        ((INotifyPropertyChanged)settings).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.DisplayDatePresentationVersion)) changes++;
        };
        await Assert.That(settings.FormatFeedDate(new DateOnly(2026, 9, 9))).IsEqualTo("9 сентября 2026, среда");
        settings.LanguageMode = "en";
        await Assert.That(settings.FormatFeedDate(new DateOnly(2026, 9, 9))).IsEqualTo("9 September 2026, Wednesday");
        await Assert.That(settings.DisplayDateFormatPreview.StartsWith("Preview: ", StringComparison.Ordinal)).IsTrue();
        await Assert.That(changes).IsGreaterThan(0);
    }
}
