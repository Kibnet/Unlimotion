using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.Notes.Identity;
using Unlimotion.Notes.Markdown;
using Unlimotion.Notes.Review;
using Unlimotion.Notes.Vault;
using Unlimotion.ViewModel.Feed;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public sealed class FeedRecoveryUiTests
{
    [Test]
    [Arguments(true, "FeedReviewRecoveryContinueButton")]
    [Arguments(false, "FeedReviewRecoveryAbandonButton")]
    public async Task ForeignReviewRecoveryRequiresOneOfTwoExplicitActions(
        bool takeOver,
        string automationId)
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var directory = new TempNotesDirectory();
            var vault = new FileNoteVault(directory.Path);
            var owner = new FeedReviewSessionCoordinator(
                "vault1", "device-a", new PortableReviewEventStore(vault), new ReviewStateStore());
            await owner.InitializeAsync();
            var sessionId = await owner.OpenOrResumeAsync();
            var recovering = new FeedReviewSessionCoordinator(
                "vault1", "device-b", new PortableReviewEventStore(vault), new ReviewStateStore());
            await recovering.InitializeAsync();
            var foreign = recovering.GetForeignOpenSessions().Single();
            using var viewModel = new FeedReviewRecoveryViewModel(recovering, foreign);
            bool? callbackTakeOver = null;
            viewModel.ResolvedCallbackAsync = value =>
            {
                callbackTakeOver = value;
                return Task.CompletedTask;
            };
            var view = new FeedReviewRecovery { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 280, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                var continueButton = FindButton(view, "FeedReviewRecoveryContinueButton");
                var abandonButton = FindButton(view, "FeedReviewRecoveryAbandonButton");
                RaiseClick(automationId == "FeedReviewRecoveryContinueButton" ? continueButton : abandonButton);

                await Assert.That(WaitFor(() => callbackTakeOver is not null)).IsTrue();
                await Assert.That(callbackTakeOver).IsEqualTo(takeOver);
                await Assert.That(viewModel.IsOpen).IsFalse();
                await Assert.That(takeOver ? recovering.CurrentSessionId : sessionId)
                    .IsEqualTo(sessionId);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task IdentityRecoveryShowsThreeActionsAndKeepsReadOnlyChoiceExplicit()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var vaultDirectory = new TempNotesDirectory();
            using var recoveryDirectory = new TempNotesDirectory();
            var vault = new FileNoteVault(vaultDirectory.Path);
            await vault.CreateAsync(
                VaultIdentityService.ManifestPath,
                "{\"schemaVersion\":1,\"vaultId\":\"vault-current\"}\n");
            var coordinator = new VaultIdentityConflictCoordinator(
                vault,
                new FileVaultIdentityConflictStore(recoveryDirectory.Path));
            var conflict = await coordinator.DetectAndPreserveAsync(new VaultIdentityBranchSnapshot(
                "vault-accepted",
                "{\"schemaVersion\":1,\"vaultId\":\"vault-accepted\"}\n",
                "accepted-revision",
                new Dictionary<string, string>(),
                [new BlockLocator(
                    "Ежедневные/2026-08-24.md", null, MarkdownBlockKind.Paragraph, "accepted", 0)]));
            using var viewModel = new FeedVaultIdentityConflictViewModel(coordinator, conflict!);
            VaultIdentityConflictResolutionResult? result = null;
            viewModel.ResolvedCallbackAsync = value =>
            {
                result = value;
                return Task.CompletedTask;
            };
            var view = new FeedVaultIdentityConflict { DataContext = viewModel };
            var window = new Window { Width = 1000, Height = 320, Content = view };
            try
            {
                window.Show();
                RunLayoutJobs();
                _ = FindButton(view, "FeedIdentityConflictUseCurrentButton");
                _ = FindButton(view, "FeedIdentityConflictReconnectButton");
                var readOnlyButton = FindButton(view, "FeedIdentityConflictReadOnlyButton");
                RaiseClick(readOnlyButton);

                await Assert.That(WaitFor(() => result is not null)).IsTrue();
                await Assert.That(result!.Resolution).IsEqualTo(VaultIdentityConflictResolution.StayReadOnly);
                await Assert.That(result.IsReadOnly).IsTrue();
                await Assert.That(viewModel.IsOpen).IsFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Button FindButton(Control root, string automationId) =>
        root.GetVisualDescendants()
            .OfType<Button>()
            .Single(value => string.Equals(
                AutomationProperties.GetAutomationId(value),
                automationId,
                StringComparison.Ordinal));

    private static void RaiseClick(Button button)
    {
        if (button.Command is { } command && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
        else
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        RunLayoutJobs();
    }

    private static bool WaitFor(Func<bool> predicate) => SpinWait.SpinUntil(() =>
    {
        Dispatcher.UIThread.RunJobs();
        return predicate();
    }, TimeSpan.FromSeconds(5));

    private static void RunLayoutJobs()
    {
        for (var index = 0; index < 20; index++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
