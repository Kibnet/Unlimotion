using System.ComponentModel;
using System.Globalization;
using PropertyChanged;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.ViewModel.Feed;

public sealed partial class FeedViewModel
{
    private SettingsViewModel? datePresentationSettings;

    public void ReportClipboardUnavailable() =>
        ErrorMessage = Unlimotion.ViewModel.Localization.Localization.Get("FeedClipboardUnavailable");

    public void AttachDateSettings()
    {
        var settings = TaskOwner?.Settings;
        if (!ReferenceEquals(datePresentationSettings, settings))
        {
            DetachDateSettings();
            datePresentationSettings = settings;
            if (settings is not null)
                ((INotifyPropertyChanged)settings).PropertyChanged += OnDateSettingsChanged;
        }
        RefreshDatePresentation();
    }

    private void DetachDateSettings()
    {
        if (datePresentationSettings is not null)
            ((INotifyPropertyChanged)datePresentationSettings).PropertyChanged -= OnDateSettingsChanged;
        datePresentationSettings = null;
    }

    private void OnDateSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (ReferenceEquals(sender, datePresentationSettings) &&
            args.PropertyName == nameof(SettingsViewModel.DisplayDatePresentationVersion))
            RefreshDatePresentation();
    }

    public void RefreshDatePresentation()
    {
        var format = datePresentationSettings?.DisplayDateFormat ?? FeedDateDisplaySettings.DefaultFormat;
        var culture = datePresentationSettings?.DisplayDateCultureName ?? LocalizationService.Current.CurrentCulture.Name;
        foreach (var day in Days)
        {
            day.DisplayDateFormat = format;
            day.DisplayDateCultureName = culture;
            day.FullPath = vault?.ResolveSafePath(day.RelativePath);
        }
        if (CurrentReview is { } review)
        {
            review.DisplayDateFormat = format;
            review.DisplayDateCultureName = culture;
        }
    }
}

public sealed partial class FeedDayViewModel
{
    [AlsoNotifyFor(nameof(FormattedDisplayDate), nameof(DisplayDate), nameof(AutomationName))]
    public string DisplayDateFormat { get; set; } = FeedDateDisplaySettings.DefaultFormat;

    [AlsoNotifyFor(nameof(FormattedDisplayDate), nameof(DisplayDate), nameof(AutomationName))]
    public string DisplayDateCultureName { get; set; } = CultureInfo.CurrentCulture.Name;

    public string FormattedDisplayDate => FeedDateDisplaySettings.Format(Date, DisplayDateFormat,
        CultureInfo.GetCultureInfo(DisplayDateCultureName));

    public string? FullPath { get; set; }
}

public sealed partial class FeedReviewSelectionViewModel
{
    [AlsoNotifyFor(nameof(FormattedDisplayDate), nameof(DisplayDate))]
    public string DisplayDateFormat { get; set; } = FeedDateDisplaySettings.DefaultFormat;

    [AlsoNotifyFor(nameof(FormattedDisplayDate), nameof(DisplayDate))]
    public string DisplayDateCultureName { get; set; } = CultureInfo.CurrentCulture.Name;

    public string FormattedDisplayDate => FeedDateDisplaySettings.Format(Date, DisplayDateFormat,
        CultureInfo.GetCultureInfo(DisplayDateCultureName));
}
