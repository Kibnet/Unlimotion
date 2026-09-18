using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class TaskRelationsAutomationIdUiTests
{
    [Test]
    public async Task DraftPickerScopesDoNotShadowTheCurrentTaskParentsTree()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var prefixes = new[] { "FeedReviewDraftParents", "AreaRootTaskPicker", "GlobalQuickCaptureDraftParents" };
            var panel = new StackPanel();
            panel.Children.Add(new TaskRelationsControl());
            foreach (var prefix in prefixes)
                panel.Children.Add(new TaskRelationsControl { AutomationIdPrefix = prefix, IsVisible = false });
            var window = new Window { Width = 800, Height = 550, Content = panel };
            try
            {
                window.Show();
                // Headless automation searches can see hidden controls too. Each scope must
                // have its own identity even before its Draft binding becomes non-null.
                var ids = panel.Children.OfType<TaskRelationsControl>()
                    .Select(control => control.FindControl<TreeView>("ParentsTree")!)
                    .Select(AutomationProperties.GetAutomationId).ToArray();
                await Assert.That(ids.Count(id => id == TaskRelationsControl.CurrentTaskParentsTreeAutomationId)).IsEqualTo(1);
                foreach (var prefix in prefixes) await Assert.That(ids).Contains(prefix + "Tree");
                await Assert.That(ids.Distinct().Count()).IsEqualTo(4);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ExplicitTreeOverrideSurvivesPrefixChangesAndClearRestoresDerivedIdentity()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var control = new TaskRelationsControl { AutomationIdPrefix = "DraftScope", ParentsTreeAutomationId = "ExplicitTree" };
            var tree = control.FindControl<TreeView>("ParentsTree")!;
            await Assert.That(AutomationProperties.GetAutomationId(tree)).IsEqualTo("ExplicitTree");
            control.AutomationIdPrefix = "OtherDraftScope";
            await Assert.That(AutomationProperties.GetAutomationId(tree)).IsEqualTo("ExplicitTree");
            control.ParentsTreeAutomationId = TaskRelationsControl.CurrentTaskParentsTreeAutomationId;
            await Assert.That(AutomationProperties.GetAutomationId(tree)).IsEqualTo(TaskRelationsControl.CurrentTaskParentsTreeAutomationId);
            control.ClearValue(TaskRelationsControl.ParentsTreeAutomationIdProperty);
            await Assert.That(AutomationProperties.GetAutomationId(tree)).IsEqualTo("OtherDraftScopeTree");
        }, CancellationToken.None);
    }
}
