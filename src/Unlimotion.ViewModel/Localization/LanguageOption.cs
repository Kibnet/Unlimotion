namespace Unlimotion.ViewModel.Localization;

public sealed class LanguageOption
{
    public LanguageOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
