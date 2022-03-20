using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Unlimotion;

public static class ControlExtensions
{
    public static T FindParentDataContext<T>(this IControl control)
    {
        foreach (var descendant in control.GetVisualAncestors())
        {
            if (descendant is Control desControl && desControl.DataContext is T dc)
            {
                return dc;
            }
        }
        return default;
    }
}