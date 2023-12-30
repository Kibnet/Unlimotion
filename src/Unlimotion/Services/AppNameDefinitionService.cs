using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public partial class AppNameDefinitionService : IAppNameDefinitionService
{
    private string AppName = "Unlimotion";
    
    public string GetAppName()
    {
        return AppName;
    }
}
