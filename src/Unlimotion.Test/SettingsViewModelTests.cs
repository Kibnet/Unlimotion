using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _configPath;

    public SettingsViewModelTests()
    {
        _configPath = Path.Combine(
            Environment.CurrentDirectory,
            $"ThemeSettings_{Guid.NewGuid():N}.json");
        File.WriteAllText(_configPath, "{}");
    }

    public void Dispose()
    {
        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }

    [Test]
    public async System.Threading.Tasks.Task IsDarkTheme_PersistsThemeChoice()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath);
        var settings = new SettingsViewModel(configuration, defaultIsDarkTheme: true);

        await Assert.That(settings.IsDarkTheme).IsTrue();

        settings.IsDarkTheme = false;

        await Assert.That(settings.IsDarkTheme).IsFalse();
        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.ThemeKey)
                .Get<string>())
            .IsEqualTo(AppearanceSettings.LightTheme);

        settings.IsDarkTheme = true;

        await Assert.That(configuration
                .GetSection(AppearanceSettings.SectionName)
                .GetSection(AppearanceSettings.ThemeKey)
                .Get<string>())
            .IsEqualTo(AppearanceSettings.DarkTheme);
    }
}
