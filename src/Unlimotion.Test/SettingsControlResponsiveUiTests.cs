using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel]
public class SettingsControlResponsiveUiTests
{
    [Test]
    public async Task SettingsControl_NarrowViewport_DoesNotOverflowHorizontally()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            Window? window = null;

            try
            {
                var settings = fixture.MainWindowViewModelTest.Settings;
                settings.StorageModeIndex = 1;
                settings.ServerStorageUrl = "https://server.example.com";
                settings.Login = "user@example.com";
                settings.Password = "super-secret-password";
                settings.GitBackupEnabled = true;
                settings.GitRemoteUrl = "git@github.com:unlimotion/unlimotion-backup.git";
                settings.ShowAdvancedBackupSettings = true;
                settings.ShowServiceActions = true;
                settings.GitCommitterName = "Unlimotion Backup Bot";
                settings.GitCommitterEmail = "backup@example.com";

                var view = new SettingsControl
                {
                    DataContext = settings
                };

                window = CreateWindow(view, 360, 800);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var overflowingControls = view.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(IsVisibleAndArranged)
                    .Select(control => new
                    {
                        Control = control,
                        RightEdge = GetRightEdge(view, control)
                    })
                    .Where(item => item.RightEdge > view.Bounds.Width + 1)
                    .Select(item => $"{item.Control.GetType().Name}:{item.Control.Name} right={item.RightEdge:F1} width={item.Control.Bounds.Width:F1}")
                    .ToList();

                if (overflowingControls.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Visible settings controls overflow the narrow viewport: " +
                        string.Join("; ", overflowingControls));
                }

                var narrowInputs = view.GetVisualDescendants()
                    .OfType<InputElement>()
                    .Where(control => control is TextBox or ComboBox or NumericUpDown)
                    .Cast<Control>()
                    .Where(IsVisibleAndArranged)
                    .ToList();

                await Assert.That(narrowInputs.Count).IsGreaterThan(0);
                await Assert.That(narrowInputs.All(control => control.Bounds.Width >= 140)).IsTrue();
            }
            finally
            {
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

    private static bool IsVisibleAndArranged(Control control)
    {
        return control.IsVisible &&
               control.Bounds.Width > 0 &&
               control.Bounds.Height > 0;
    }

    private static double GetRightEdge(Visual relativeTo, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width, 0), relativeTo);
        if (!point.HasValue)
        {
            throw new InvalidOperationException($"Cannot translate point for control {control.GetType().Name}.");
        }

        return point.Value.X;
    }
}
