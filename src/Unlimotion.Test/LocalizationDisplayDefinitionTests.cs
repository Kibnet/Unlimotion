using System.Globalization;
using System.Linq;
using System.ComponentModel;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.Test;

[NotInParallel]
public class LocalizationDisplayDefinitionTests
{
    [Test]
    public async System.Threading.Tasks.Task SortDefinitions_KeepStableIdAndLegacyNameWhileDisplayIsLocalized()
    {
        var previousLocalization = LocalizationService.Current;
        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
            LocalizationService.Current = localization;

            localization.SetLanguage(LocalizationService.EnglishLanguage);
            var sort = SortDefinition.GetDefinitions().First(definition => definition.Id == "created-ascending");

            await Assert.That(sort.Id).IsEqualTo("created-ascending");
            await Assert.That(sort.MatchesPersistedValue("Created Ascending")).IsTrue();
            await Assert.That(sort.ToString()).IsEqualTo("Created Ascending");

            localization.SetLanguage(LocalizationService.RussianLanguage);

            await Assert.That(sort.Id).IsEqualTo("created-ascending");
            await Assert.That(sort.MatchesPersistedValue("Created Ascending")).IsTrue();
            await Assert.That(sort.ToString()).IsEqualTo("Создание по возрастанию");
        }
        finally
        {
            LocalizationService.Current = previousLocalization;
        }
    }

    [Test]
    public async System.Threading.Tasks.Task DateFilterOptions_KeepStableIdsWhileDisplayIsLocalized()
    {
        var previousLocalization = LocalizationService.Current;
        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
            LocalizationService.Current = localization;

            var filter = DateFilterDefinition.FindById("Last Two Days");

            localization.SetLanguage(LocalizationService.EnglishLanguage);
            await Assert.That(filter.Id).IsEqualTo("Last Two Days");
            await Assert.That(filter.ToString()).IsEqualTo("Last Two Days");

            localization.SetLanguage(LocalizationService.RussianLanguage);
            await Assert.That(filter.Id).IsEqualTo("Last Two Days");
            await Assert.That(filter.ToString()).IsEqualTo("Последние два дня");
        }
        finally
        {
            LocalizationService.Current = previousLocalization;
        }
    }

    [Test]
    public async System.Threading.Tasks.Task MainWindowViewModel_LanguageRefreshToleratesTransientEmptySelections()
    {
        var previousLocalization = LocalizationService.Current;
        MainWindowViewModelFixture? fixture = null;
        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
            LocalizationService.Current = localization;
            fixture = new MainWindowViewModelFixture();
            var viewModel = fixture.MainWindowViewModelTest;

            viewModel.CurrentSortDefinition = null!;
            viewModel.CurrentSortDefinitionForUnlocked = null!;
            viewModel.CompletedDateFilter.CurrentOption = null!;
            viewModel.ArchivedDateFilter.CurrentOption = null!;
            viewModel.LastCreatedDateFilter.CurrentOption = null!;
            viewModel.LastUpdatedDateFilter.CurrentOption = null!;

            localization.SetLanguage(LocalizationService.RussianLanguage);

            await Assert.That(viewModel.CurrentSortDefinition).IsNotNull();
            await Assert.That(viewModel.CurrentSortDefinitionForUnlocked).IsNotNull();
            await Assert.That(viewModel.CompletedDateFilter.CurrentOption).IsNotNull();
            await Assert.That(viewModel.ArchivedDateFilter.CurrentOption).IsNotNull();
            await Assert.That(viewModel.LastCreatedDateFilter.CurrentOption).IsNotNull();
            await Assert.That(viewModel.LastUpdatedDateFilter.CurrentOption).IsNotNull();
        }
        finally
        {
            fixture?.MainWindowViewModelTest.Dispose();
            fixture?.CleanTasks();
            LocalizationService.Current = previousLocalization;
        }
    }

    [Test]
    public async System.Threading.Tasks.Task MainWindowViewModel_LanguageRefreshKeepsRuntimeFilterCollections()
    {
        var previousLocalization = LocalizationService.Current;
        MainWindowViewModelFixture? fixture = null;
        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
            LocalizationService.Current = localization;
            fixture = new MainWindowViewModelFixture();
            var viewModel = fixture.MainWindowViewModelTest;
            var unlockedFilters = viewModel.UnlockedTimeFilters;
            var durationFilters = viewModel.DurationFilters;
            var unlockedFilter = unlockedFilters.First(filter => filter.ResourceKey == "UnlockedTimeFilterToday");
            var durationFilter = durationFilters.First(filter => filter.ResourceKey == "DurationFilterNoDuration");

            unlockedFilter.ShowTasks = true;
            durationFilter.ShowTasks = true;
            await Assert.That(unlockedFilter.Title).IsEqualTo("Today");
            await Assert.That(durationFilter.Title).IsEqualTo("No duration");

            localization.SetLanguage(LocalizationService.RussianLanguage);

            await Assert.That(viewModel.UnlockedTimeFilters).IsSameReferenceAs(unlockedFilters);
            await Assert.That(viewModel.DurationFilters).IsSameReferenceAs(durationFilters);
            await Assert.That(unlockedFilter.ShowTasks).IsTrue();
            await Assert.That(durationFilter.ShowTasks).IsTrue();
            await Assert.That(unlockedFilter.Title).IsEqualTo("Сегодня");
            await Assert.That(durationFilter.Title).IsEqualTo("Без длительности");
        }
        finally
        {
            fixture?.MainWindowViewModelTest.Dispose();
            fixture?.CleanTasks();
            LocalizationService.Current = previousLocalization;
        }
    }

    [Test]
    public async System.Threading.Tasks.Task TaskRelationEditor_DisposeUnsubscribesFromCultureChanges()
    {
        var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
        var editor = new TaskRelationEditorViewModel(
            () => [],
            _ => null,
            () => false,
            (_, _, _) => true,
            (_, _, _) => System.Threading.Tasks.Task.FromResult(true),
            _ => string.Empty,
            new NotificationManagerWrapperMock(),
            localization);
        var watermarkChangeCount = 0;
        editor.PropertyChanged += OnPropertyChanged;

        localization.SetLanguage(LocalizationService.RussianLanguage);
        editor.Dispose();
        localization.SetLanguage(LocalizationService.EnglishLanguage);

        await Assert.That(watermarkChangeCount).IsEqualTo(1);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(TaskRelationEditorViewModel.Watermark))
            {
                watermarkChangeCount++;
            }
        }
    }

    [Test]
    public async System.Threading.Tasks.Task RepeaterOptions_KeepEnumValueWhileDisplayIsLocalized()
    {
        var previousLocalization = LocalizationService.Current;
        try
        {
            var localization = new LocalizationService(new FakeSystemCultureProvider("en-US"));
            LocalizationService.Current = localization;
            var repeater = new RepeaterPatternViewModel
            {
                Type = RepeaterType.Weekly,
                WorkDays = true,
                Period = 1
            };

            localization.SetLanguage(LocalizationService.EnglishLanguage);
            await Assert.That(repeater.SelectedRepeaterType.Value).IsEqualTo(RepeaterType.Weekly);
            await Assert.That(repeater.Title).IsEqualTo("Weekdays, every 1");

            localization.SetLanguage(LocalizationService.RussianLanguage);
            await Assert.That(repeater.SelectedRepeaterType.Value).IsEqualTo(RepeaterType.Weekly);
            await Assert.That(repeater.Title).IsEqualTo("По рабочим дням, каждые 1");
        }
        finally
        {
            LocalizationService.Current = previousLocalization;
        }
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
