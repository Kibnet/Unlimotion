using System;
using System.Linq;
using Avalonia.Controls;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.ViewModel.Workspace;

namespace Unlimotion.Views;

/// <summary>One opening vocabulary for rail, links and touch action buttons.</summary>
internal static class WorkspaceOpenMenu
{
    public static object[] TakeItems(ContextMenu menu)
    {
        // A reused MenuItem must leave its original logical owner before another
        // presenter can materialize it as a visible submenu or flyout command.
        var items = menu.Items.Cast<object>().ToArray();
        menu.Items.Clear();
        return items;
    }

    public static ContextMenu Create(MainWindowViewModel owner, WorkspaceLocation location,
        Func<WorkspaceOpenDisposition, System.Threading.Tasks.Task>? open = null)
    {
        var menu = new ContextMenu();
        if (owner.WorkspaceNavigation.Panes.Any(pane => pane.Tabs.Any(tab =>
                tab.CurrentLocation?.ObjectKey == location.ObjectKey)))
        {
            Add(location.HasExplicitLocator ? "WorkspaceShowIndicatedPlace" : "WorkspaceShowExisting", WorkspaceOpenDisposition.CurrentTab);
            return menu;
        }
        Add("WorkspaceOpenHere", WorkspaceOpenDisposition.CurrentTab);
        Add("WorkspaceOpenInNewTab", WorkspaceOpenDisposition.NewTab);
        Add("WorkspaceOpenBeside", WorkspaceOpenDisposition.AdjacentPane);
        return menu;

        void Add(string key, WorkspaceOpenDisposition disposition)
        {
            var item = new MenuItem { Header = Localization.Get(key), MinHeight = 44 };
            item.Click += async (_, _) =>
            {
                if (open is not null) await open(disposition);
                else await owner.OpenWorkspaceLocationAsync(location, disposition);
            };
            menu.Items.Add(item);
        }
    }
}
