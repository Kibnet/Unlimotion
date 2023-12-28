using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public partial class AppNameDefinitionService : IAppNameDefinitionService
{
    public string GetAppName()
    {
        return AppName;
    }
}
