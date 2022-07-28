using Microsoft.Extensions.Configuration;
using PropertyChanged;
using System.Windows.Input;

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
    
    public string ServerStorageUrl
    {
        get => _configuration.GetSection("TaskStorage:URL").Get<string>();
        set => _configuration.GetSection("TaskStorage:URL").Set(value);
    }

    public string Login
    {
        get => _configuration.GetSection("TaskStorage:Login").Get<string>();
        set => _configuration.GetSection("TaskStorage:Login").Set(value);
    }

    //TODO стоит подумать над шифрованным хранением
    public string Password
    {
        get => _configuration.GetSection("TaskStorage:Password").Get<string>();
        set => _configuration.GetSection("TaskStorage:Password").Set(value);
    }

    public bool IsServerMode
    {
        get => _configuration.GetSection("TaskStorage:IsServerMode").Get<bool>();
        set => _configuration.GetSection("TaskStorage:IsServerMode").Set(value);
    }

    public ICommand ConnectCommand { get; set; }
    public ICommand MigrateCommand { get; set; }
}