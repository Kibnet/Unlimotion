using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Unlimotion;

public static class ControlExtensions
{
    public static T FindParentDataContext<T>(this Control control)
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

    public static T FindParent<T>(this Control control)
    {
        foreach (var descendant in control.GetVisualAncestors())
        {
            if (descendant is T parent)
            {
                return parent;
            }
        }
        return default;
    }
}