using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class ProfileControlUiTests
{
    // 1x1 transparent PNG so the avatar Bitmap actually decodes in the headless preview.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Test]
    public async Task UserAvatar_Click_OpensProfileTab()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var view = new MainControl { DataContext = vm };
                window = CreateWindow(view, 1800, 1000);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var avatar = FindControlByAutomationId<Border>(view, "UserAvatar");
                var tabs = FindControlByAutomationId<TabControl>(view, "MainTabs");

                await Assert.That(tabs.SelectedIndex).IsNotEqualTo(10);

                await ClickControlAsync(window, avatar);

                var switched = WaitFor(() => tabs.SelectedIndex == 10);
                await Assert.That(switched).IsTrue();
                await Assert.That(vm.ProfileMode).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ProfileControl_EditDisplayNameAndSave_PersistsToFile()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var profile = fixture.MainWindowViewModelTest.Profile;
                var view = new ProfileControl { DataContext = profile };
                window = CreateWindow(view, 720, 900);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var nameInput = FindControlByAutomationId<TextBox>(view, "ProfileDisplayNameInput");
                var saveButton = FindControlByAutomationId<Button>(view, "ProfileSaveButton");

                nameInput.Text = "UI Test Name";
                Dispatcher.UIThread.RunJobs();

                await Assert.That(profile.DisplayName).IsEqualTo("UI Test Name");

                await ClickControlAsync(window, saveButton);

                var verifyStorage = new FileUserProfileStorage(fixture.DefaultTasksFolderPath);
                var persisted = WaitFor(() =>
                    verifyStorage.Load(profile.CurrentUserId).GetAwaiter().GetResult()?.DisplayName == "UI Test Name");

                await Assert.That(persisted).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ProfileControl_InvalidEmail_DisablesSaveAndShowsError()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var profile = fixture.MainWindowViewModelTest.Profile;
                var view = new ProfileControl { DataContext = profile };
                window = CreateWindow(view, 720, 900);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var nameInput = FindControlByAutomationId<TextBox>(view, "ProfileDisplayNameInput");
                var emailInput = FindControlByAutomationId<TextBox>(view, "ProfileEmailInput");
                var saveButton = FindControlByAutomationId<Button>(view, "ProfileSaveButton");
                var emailError = FindControlByAutomationId<TextBlock>(view, "ProfileEmailError");
                var nameError = FindControlByAutomationId<TextBlock>(view, "ProfileDisplayNameError");

                nameInput.Text = "Alex";
                emailInput.Text = "not-an-email";
                Dispatcher.UIThread.RunJobs();

                // A command whose CanExecute is false disables the button via IsEffectivelyEnabled.
                var disabled = WaitFor(() => saveButton.IsEffectivelyEnabled == false);
                await Assert.That(disabled).IsTrue();
                await Assert.That(emailError.IsVisible).IsTrue();
                // The display-name error must NOT fire — the name is present; only the email is bad.
                await Assert.That(nameError.IsVisible).IsFalse();

                emailInput.Text = "alex@example.com";
                Dispatcher.UIThread.RunJobs();

                var enabled = WaitFor(() => saveButton.IsEffectivelyEnabled);
                await Assert.That(enabled).IsTrue();
            }
            finally
            {
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ProfileControl_PickAvatar_ShowsAvatarImage()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var previousPicker = Dialogs.PlatformOpenFileDialogAsync;
            var avatarSource = Path.Combine(fixture.DefaultTasksFolderPath, "avatar-source.png");
            Window? window = null;

            try
            {
                File.WriteAllBytes(avatarSource, Convert.FromBase64String(TinyPngBase64));
                Dialogs.PlatformOpenFileDialogAsync = (_, _) => Task.FromResult<string?>(avatarSource);

                var profile = fixture.MainWindowViewModelTest.Profile;
                profile.Dialogs = new Dialogs();

                var view = new ProfileControl { DataContext = profile };
                window = CreateWindow(view, 720, 900);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var pickButton = FindControlByAutomationId<Button>(view, "ProfilePickAvatarButton");
                var avatarImage = FindControlByAutomationId<Image>(view, "ProfileAvatarImage");
                var initials = FindControlByAutomationId<TextBlock>(view, "ProfileInitials");

                await Assert.That(profile.HasAvatar).IsFalse();
                await Assert.That(initials.IsVisible).IsTrue();

                await ClickControlAsync(window, pickButton);

                var shown = WaitFor(() => profile.HasAvatar);
                await Assert.That(shown).IsTrue();
                Dispatcher.UIThread.RunJobs();

                await Assert.That(avatarImage.IsVisible).IsTrue();
                await Assert.That(initials.IsVisible).IsFalse();
                await Assert.That(profile.AvatarRelativePath!.StartsWith("Users/avatars/", StringComparison.Ordinal)).IsTrue();
            }
            finally
            {
                Dialogs.PlatformOpenFileDialogAsync = previousPicker;
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ProfileControl_AdjustAvatarZoomAndPan_Persist()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var previousPicker = Dialogs.PlatformOpenFileDialogAsync;
            var avatarSource = Path.Combine(fixture.DefaultTasksFolderPath, "crop-source.png");
            Window? window = null;

            try
            {
                File.WriteAllBytes(avatarSource, Convert.FromBase64String(TinyPngBase64));
                Dialogs.PlatformOpenFileDialogAsync = (_, _) => Task.FromResult<string?>(avatarSource);

                var profile = fixture.MainWindowViewModelTest.Profile;
                profile.Dialogs = new Dialogs();
                profile.DisplayName = "Alex";

                var view = new ProfileControl { DataContext = profile };
                window = CreateWindow(view, 720, 900);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var pickButton = FindControlByAutomationId<Button>(view, "ProfilePickAvatarButton");
                await ClickControlAsync(window, pickButton);
                var shown = WaitFor(() => profile.HasAvatar);
                await Assert.That(shown).IsTrue();
                Dispatcher.UIThread.RunJobs();

                // Zoom in via the slider (two-way bound to the view-model).
                var zoomSlider = FindControlByAutomationId<Slider>(view, "ProfileAvatarZoomSlider");
                zoomSlider.Value = 3;
                Dispatcher.UIThread.RunJobs();
                await Assert.That(profile.AvatarZoom).IsEqualTo(3);

                // Pan by dragging the circular editor; at 3x the image can pan, so the offset moves.
                var editor = FindControlByAutomationId<Border>(view, "ProfileAvatarPreview");
                var start = editor.TranslatePoint(new Point(editor.Bounds.Width / 2, editor.Bounds.Height / 2), window)!.Value;
                var move = new Point(start.X + 40, start.Y);

                window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                window.MouseMove(move, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                window.MouseUp(move, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();

                await Assert.That(profile.AvatarOffsetX > 0).IsTrue();

                // Persist and verify the framing survives a round-trip.
                var saveButton = FindControlByAutomationId<Button>(view, "ProfileSaveButton");
                await ClickControlAsync(window, saveButton);

                var verifyStorage = new FileUserProfileStorage(fixture.DefaultTasksFolderPath);
                var persisted = WaitFor(() =>
                {
                    var p = verifyStorage.Load(profile.CurrentUserId).GetAwaiter().GetResult();
                    return p is { AvatarZoom: 3 } && p.AvatarOffsetX > 0;
                });
                await Assert.That(persisted).IsTrue();
            }
            finally
            {
                Dialogs.PlatformOpenFileDialogAsync = previousPicker;
                window?.Close();
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }

    private static Window CreateWindow(Control content, double width, double height)
    {
        return new Window
        {
            Width = width,
            Height = height,
            Content = content
        };
    }

    private static T FindControlByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        var control = root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    automationId,
                    StringComparison.Ordinal));

        return control ?? throw new InvalidOperationException($"Control with AutomationId '{automationId}' was not found.");
    }

    private static async Task ClickControlAsync(Window window, Control control)
    {
        if (control is Button { Command: { } command } button)
        {
            if (!command.CanExecute(button.CommandParameter))
            {
                throw new InvalidOperationException($"Button command for {button.GetType().Name} cannot execute.");
            }

            command.Execute(button.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            await Task.CompletedTask;
            return;
        }

        Dispatcher.UIThread.RunJobs();

        var point = control.TranslatePoint(
            new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window);

        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await Task.CompletedTask;
    }

    private static bool WaitFor(Func<bool> predicate, int timeoutMilliseconds = 3000)
    {
        return SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return predicate();
        }, TimeSpan.FromMilliseconds(timeoutMilliseconds));
    }
}
