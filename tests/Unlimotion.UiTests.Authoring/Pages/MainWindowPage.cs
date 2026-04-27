using AppAutomation.Abstractions;

namespace Unlimotion.UiTests.Authoring.Pages;

[UiControl("MainTabs", UiControlType.Tab, "MainTabs")]
[UiControl("AllTasksTabItem", UiControlType.TabItem, "AllTasksTabItem")]
[UiControl("AllTasksTree", UiControlType.Tree, "AllTasksTree")]
[UiControl("LastCreatedTabItem", UiControlType.TabItem, "LastCreatedTabItem")]
[UiControl("LastCreatedTree", UiControlType.Tree, "LastCreatedTree")]
[UiControl("LastUpdatedTabItem", UiControlType.TabItem, "LastUpdatedTabItem")]
[UiControl("LastUpdatedTree", UiControlType.Tree, "LastUpdatedTree")]
[UiControl("UnlockedTabItem", UiControlType.TabItem, "UnlockedTabItem")]
[UiControl("UnlockedTree", UiControlType.Tree, "UnlockedTree")]
[UiControl("CompletedTabItem", UiControlType.TabItem, "CompletedTabItem")]
[UiControl("CompletedTree", UiControlType.Tree, "CompletedTree")]
[UiControl("ArchivedTabItem", UiControlType.TabItem, "ArchivedTabItem")]
[UiControl("ArchivedTree", UiControlType.Tree, "ArchivedTree")]
[UiControl("LastOpenedTabItem", UiControlType.TabItem, "LastOpenedTabItem")]
[UiControl("LastOpenedTree", UiControlType.Tree, "LastOpenedTree")]
[UiControl("RoadmapTabItem", UiControlType.TabItem, "RoadmapTabItem")]
[UiControl("RoadmapRoot", UiControlType.AutomationElement, "RoadmapRoot")]
[UiControl("RoadmapZoomBorder", UiControlType.AutomationElement, "RoadmapZoomBorder")]
[UiControl("SettingsTabItem", UiControlType.TabItem, "SettingsTabItem")]
[UiControl("SettingsRoot", UiControlType.AutomationElement, "SettingsRoot")]
[UiControl("DetailsPaneToggleButton", UiControlType.ToggleButton, "DetailsPaneToggleButton")]
[UiControl("CurrentTaskTitleTextBox", UiControlType.TextBox, "CurrentTaskTitleTextBox")]
[UiControl("CurrentTaskParentsRelationAddButton", UiControlType.Button, "CurrentTaskParentsRelationAddButton")]
[UiControl("CurrentTaskParentsRelationAddInput", UiControlType.AutomationElement, "CurrentTaskParentsRelationAddInput")]
[UiControl("CurrentTaskParentsRelationAddCancelButton", UiControlType.Button, "CurrentTaskParentsRelationAddCancelButton")]
[UiControl("CurrentTaskBlockingRelationAddButton", UiControlType.Button, "CurrentTaskBlockingRelationAddButton")]
[UiControl("CurrentTaskBlockingRelationAddInput", UiControlType.AutomationElement, "CurrentTaskBlockingRelationAddInput")]
[UiControl("CurrentTaskBlockingRelationAddCancelButton", UiControlType.Button, "CurrentTaskBlockingRelationAddCancelButton")]
[UiControl("CurrentTaskContainingRelationAddButton", UiControlType.Button, "CurrentTaskContainingRelationAddButton")]
[UiControl("CurrentTaskContainingRelationAddInput", UiControlType.AutomationElement, "CurrentTaskContainingRelationAddInput")]
[UiControl("CurrentTaskContainingRelationAddCancelButton", UiControlType.Button, "CurrentTaskContainingRelationAddCancelButton")]
[UiControl("CurrentTaskBlockedRelationAddButton", UiControlType.Button, "CurrentTaskBlockedRelationAddButton")]
[UiControl("CurrentTaskBlockedRelationAddInput", UiControlType.AutomationElement, "CurrentTaskBlockedRelationAddInput")]
[UiControl("CurrentTaskBlockedRelationAddCancelButton", UiControlType.Button, "CurrentTaskBlockedRelationAddCancelButton")]
[UiControl("CurrentItemParentsTree", UiControlType.Tree, "CurrentItemParentsTree")]
[UiControl("CurrentItemBlockedByTree", UiControlType.Tree, "CurrentItemBlockedByTree")]
[UiControl("CurrentItemContainsTree", UiControlType.Tree, "CurrentItemContainsTree")]
[UiControl("CurrentItemBlocksTree", UiControlType.Tree, "CurrentItemBlocksTree")]
public sealed partial class MainWindowPage : UiPage
{
    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }
}
