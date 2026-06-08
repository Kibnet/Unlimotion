using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Unlimotion.Views
{
    internal static class FilterFlyoutLayout
    {
        private const double PopupEdgeGap = 8d;
        private const double MaxPanelWidth = 360d;
        private const double MaxPanelHeight = 520d;

        public static void ApplyResponsiveBounds(Control owner, DropDownButton filtersButton)
        {
            if (filtersButton.Flyout is not Flyout { Content: Control flyoutContent })
            {
                return;
            }

            var panel = FindFilterPanel(flyoutContent);
            if (panel == null)
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(owner);
            var viewport = topLevel?.Bounds.Size;
            if (viewport is not { Width: > 0, Height: > 0 })
            {
                viewport = owner.Bounds.Size;
            }

            if (viewport is not { Width: > 0, Height: > 0 })
            {
                return;
            }

            var relativeTo = topLevel as Visual ?? owner;
            var buttonTopLeft = filtersButton.TranslatePoint(new Point(0, 0), relativeTo) ?? default;
            var buttonBottom = buttonTopLeft.Y + filtersButton.Bounds.Height;
            var availableWidth = Math.Max(0, viewport.Value.Width - buttonTopLeft.X - PopupEdgeGap);
            var availableHeight = Math.Max(0, viewport.Value.Height - buttonBottom - PopupEdgeGap);

            panel.MinWidth = 0;
            panel.MaxWidth = Math.Min(MaxPanelWidth, availableWidth);
            panel.MaxHeight = Math.Min(MaxPanelHeight, availableHeight);

            foreach (var scrollViewer in panel.GetVisualDescendants()
                         .OfType<ScrollViewer>()
                         .Where(static control => control.Classes.Contains("FilterPanelScrollViewer")))
            {
                scrollViewer.MaxHeight = Math.Max(0, panel.MaxHeight - panel.Padding.Top - panel.Padding.Bottom);
            }
        }

        private static Border? FindFilterPanel(Control flyoutContent)
        {
            if (flyoutContent is Border border &&
                AutomationProperties.GetAutomationId(border)?.EndsWith("FilterPanel", StringComparison.Ordinal) == true)
            {
                return border;
            }

            return flyoutContent.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(static border =>
                    AutomationProperties.GetAutomationId(border)?.EndsWith("FilterPanel", StringComparison.Ordinal) == true);
        }
    }
}
