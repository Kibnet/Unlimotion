using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel]
public class MainControlDateQuickSelectionUiTests
{
    [Test]
    public async Task DateQuickSelectionUi_UsesLocalizedTodayAndTomorrowLabels()
    {
        var previousLocalization = LocalizationService.Current;
        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("ru-RU"));
            LocalizationService.Current = localization;
            localization.SetLanguage(LocalizationService.RussianLanguage);

            using var session = HeadlessUnitTestSession.StartNew(typeof(App));
            await session.Dispatch(async () =>
            {
                MainWindowViewModelFixture? fixture = null;
                Window? window = null;

                try
                {
                    fixture = new MainWindowViewModelFixture();
                    var vm = fixture.MainWindowViewModelTest;
                    await vm.Connect();
                    vm.AllTasksMode = true;
                    vm.DetailsAreOpen = true;
                    TestHelpers.SetCurrentTask(vm, MainWindowViewModelFixture.RootTask2Id);

                    var view = new MainControl { DataContext = vm };
                    window = CreateWindow(view);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();

                    var beginButton = GetDropDownButton(view, localization.Get("SetBegin"));
                    var endButton = GetDropDownButton(view, localization.Get("SetEnd"));

                    await AssertMenuHeaders(beginButton, "Сегодня", "Завтра");
                    await AssertMenuHeaders(endButton, "Сегодня", "Завтра");
                }
                finally
                {
                    window?.Close();
                    fixture?.CleanTasks();
                }
            }, CancellationToken.None);
        }
        finally
        {
            LocalizationService.Current = previousLocalization;
        }
    }

    private static async Task AssertMenuHeaders(DropDownButton button, string expectedToday, string expectedTomorrow)
    {
        var flyout = button.Flyout as MenuFlyout;
        await Assert.That(flyout).IsNotNull();

        var items = flyout!.Items.OfType<MenuItem>().ToArray();
        await Assert.That(items.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(items[0].Header?.ToString()).IsEqualTo(expectedToday);
        await Assert.That(items[1].Header?.ToString()).IsEqualTo(expectedTomorrow);
    }

    private static DropDownButton GetDropDownButton(Control root, string content)
    {
        var button = root.GetVisualDescendants()
            .OfType<DropDownButton>()
            .FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), content, StringComparison.Ordinal));

        if (button == null)
        {
            throw new InvalidOperationException($"DropDownButton with content '{content}' was not found.");
        }

        return button;
    }

    private static Window CreateWindow(Control content)
    {
        return new Window
        {
            Width = 1600,
            Height = 2200,
            Content = content
        };
    }

    private sealed class FakeSystemCultureProvider : ILocalizationSystemCultureProvider
    {
        public FakeSystemCultureProvider(string cultureName)
        {
            SystemUICulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public CultureInfo SystemUICulture { get; }
    }
}
