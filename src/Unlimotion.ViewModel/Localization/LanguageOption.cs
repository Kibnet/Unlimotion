namespace Unlimotion.ViewModel.Localization;

[PropertyChanged.AddINotifyPropertyChangedInterface]
public sealed class LanguageOption
{
    public LanguageOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }

    public string DisplayName { get; set; }

    public override string ToString() => DisplayName;
}
