using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public class AppNameDefinitionService : IAppNameDefinitionService
{
    private string AppName { get; }
    
    public AppNameDefinitionService()
    {
        AppName = "Unlimotion ReleaseTag"; // DON'T Change. It's used for workflows and BeforeBuild.targets
    }
    
    public string GetVersion()
    {
        return AppName;
    }
}
