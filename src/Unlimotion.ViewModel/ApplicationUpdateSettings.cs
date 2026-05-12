using System;

namespace Unlimotion.ViewModel;

public enum ApplicationUpdateCheckIntervalUnit
{
    Days = 0,
    Hours = 1,
    Minutes = 2
}

public static class ApplicationUpdateSettings
{
    public const string SectionName = "Updates";
    public const string AutoCheckEnabledKey = "AutoCheckEnabled";
    public const string CheckIntervalValueKey = "CheckIntervalValue";
    public const string CheckIntervalUnitKey = "CheckIntervalUnit";

    public const bool DefaultAutoCheckEnabled = true;
    public const int DefaultCheckIntervalValue = 1;
    public const int MinCheckIntervalValue = 1;
    public const int MaxCheckIntervalValue = 999;

    public const ApplicationUpdateCheckIntervalUnit DefaultCheckIntervalUnit =
        ApplicationUpdateCheckIntervalUnit.Hours;

    public static int NormalizeCheckIntervalValue(int value) =>
        Math.Clamp(value, MinCheckIntervalValue, MaxCheckIntervalValue);

    public static ApplicationUpdateCheckIntervalUnit ParseCheckIntervalUnit(string? value)
    {
        return Enum.TryParse<ApplicationUpdateCheckIntervalUnit>(
                   value,
                   ignoreCase: true,
                   out var unit) &&
               Enum.IsDefined(unit)
            ? unit
            : DefaultCheckIntervalUnit;
    }

    public static string ToStoredCheckIntervalUnit(ApplicationUpdateCheckIntervalUnit unit) =>
        (Enum.IsDefined(unit)
            ? unit
            : DefaultCheckIntervalUnit).ToString();

    public static TimeSpan ToInterval(int value, ApplicationUpdateCheckIntervalUnit unit)
    {
        var normalizedValue = NormalizeCheckIntervalValue(value);
        return unit switch
        {
            ApplicationUpdateCheckIntervalUnit.Days => TimeSpan.FromDays(normalizedValue),
            ApplicationUpdateCheckIntervalUnit.Minutes => TimeSpan.FromMinutes(normalizedValue),
            _ => TimeSpan.FromHours(normalizedValue)
        };
    }
}
