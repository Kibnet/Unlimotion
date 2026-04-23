using AppAutomation.Abstractions;

namespace Unlimotion.UiTests.Authoring.Pages;

[UiControl("MainTabs", UiControlType.Tab, "MainTabs")]
[UiControl("AllTasksTabItem", UiControlType.TabItem, "AllTasksTabItem")]
[UiControl("AllTasksTree", UiControlType.Tree, "AllTasksTree")]
[UiControl("DetailsPaneToggleButton", UiControlType.ToggleButton, "DetailsPaneToggleButton")]
[UiControl("CurrentTaskTitleTextBox", UiControlType.TextBox, "CurrentTaskTitleTextBox")]
[UiControl("CurrentTaskParentsRelationAddButton", UiControlType.Button, "CurrentTaskParentsRelationAddButton")]
[UiControl("CurrentTaskParentsRelationAddInput", UiControlType.AutomationElement, "CurrentTaskParentsRelationAddInput")]
[UiControl("CurrentTaskParentsRelationAddCancelButton", UiControlType.Button, "CurrentTaskParentsRelationAddCancelButton")]
[UiControl("CurrentItemParentsTree", UiControlType.Tree, "CurrentItemParentsTree")]
public sealed partial class MainWindowPage : UiPage
{
    public MainWindowPage(IUiControlResolver resolver) : base(resolver)
    {
    }
}
