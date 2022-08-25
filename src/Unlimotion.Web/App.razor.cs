using Avalonia.ReactiveUI;
using Avalonia.Web.Blazor;

namespace Unlimotion.Web;

public partial class App
{
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        
        WebAppBuilder.Configure<Unlimotion.App>()
            .UseReactiveUI()
            .SetupWithSingleViewLifetime();
    }
}