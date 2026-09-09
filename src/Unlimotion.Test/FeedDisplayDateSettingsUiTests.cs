using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class FeedDisplayDateSettingsUiTests
{
    [Test]
    public async Task SettingsExposeIndependentDateDisplayFormatAndPreview()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var settings = new SettingsViewModel(new ConfigurationBuilder().AddInMemoryCollection().Build());
            var view = new SettingsControl { DataContext = settings };
            var window = new Window { Content = view, Width = 720, Height = 800 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var input = Find<TextBox>(view, "FeedDisplayDateFormatTextBox");
                await Assert.That(input).IsNotNull();
                await Assert.That(input!.Text).IsEqualTo("d MMMM yyyy, dddd");
                await Assert.That(Find<TextBlock>(view, "FeedDisplayDateFormatPreview")).IsNotNull();
                await Assert.That(Find<Button>(view, "ResetFeedDisplayDateFormatButton")).IsNotNull();

                input.Text = "dd.MM.yyyy";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(settings.DisplayDateFormat).IsEqualTo("dd.MM.yyyy");
                await Assert.That(Find<TextBlock>(view, "FeedDisplayDateFormatPreview")!.Text)
                    .IsEqualTo(settings.DisplayDateFormatPreview);
                input.Text = "HH:mm";
                Dispatcher.UIThread.RunJobs();
                await Assert.That(settings.DisplayDateFormat).IsEqualTo("dd.MM.yyyy");
                await Assert.That(Find<TextBlock>(view, "FeedDisplayDateFormatValidation")!.IsVisible).IsTrue();
                var reset = Find<Button>(view, "ResetFeedDisplayDateFormatButton")!;
                reset.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var point = reset.TranslatePoint(new Point(reset.Bounds.Width / 2, reset.Bounds.Height / 2), window);
                await Assert.That(point.HasValue).IsTrue();
                window.MouseDown(point!.Value, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(input.Text).IsEqualTo("d MMMM yyyy, dddd");
                await Assert.That(settings.IsDisplayDateFormatValidationVisible).IsFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static T? Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants()
        .OfType<T>().FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == id);
}
