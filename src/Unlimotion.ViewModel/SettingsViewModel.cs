using Microsoft.Extensions.Configuration;
using PropertyChanged;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class SettingsViewModel
{
    private readonly IConfiguration _configuration;

    public SettingsViewModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string TaskStoragePath
    {
        get => _configuration.GetSection("TaskStorage:Path").Get<string>();
        set => _configuration.GetSection("TaskStorage:Path").Set(value);
    }
}