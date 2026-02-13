using System;
using System.Linq;
using System.Reflection;
using Unlimotion.ViewModel;

namespace Unlimotion.Services;

public partial class AppNameDefinitionService : IAppNameDefinitionService
{
    private static readonly string AppNameSuffix = GetAppNameSuffix();
    private string AppName = $"Unlimotion{AppNameSuffix}";
    
    public string GetAppName()
    {
        return AppName;
    }

    private static string GetAppNameSuffix()
    {
        const string metadataKey = "UnlimotionAppNameSuffix";
        var assembly = typeof(AppNameDefinitionService).Assembly;
        var suffix = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == metadataKey)?.Value;
        return string.IsNullOrWhiteSpace(suffix) ? string.Empty : $" {suffix}";
    }
}
