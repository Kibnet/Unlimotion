using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public sealed class SettingsAppearanceContractTests : IDisposable
{
    private readonly string _configPath;
    private readonly List<IDisposable> _configurationDisposables = [];

    public SettingsAppearanceContractTests()
    {
        _configPath = Path.Combine(Environment.CurrentDirectory, $"SettingsAppearance_{Guid.NewGuid():N}.json");
        File.WriteAllText(_configPath, "{}");
    }

    public void Dispose()
    {
        foreach (var disposable in _configurationDisposables)
        {
            disposable.Dispose();
        }

        if (File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }
    }

    [Test]
    public async Task FuzzySearch_PersistsChoice()
    {
        IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(_configPath, reloadOnChange: false);
        if (configuration is IDisposable disposable)
        {
            _configurationDisposables.Add(disposable);
        }

        var settings = new SettingsViewModel(configuration);
        settings.IsFuzzySearch = true;

        await Assert.That(settings.IsFuzzySearch).IsTrue();
        await Assert.That(configuration.GetSection(nameof(SettingsViewModel.IsFuzzySearch)).Get<bool>()).IsTrue();
    }
}
