using System.Collections.Generic;
using System.Linq;
using ReactiveUI;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.ViewModel;

public sealed class WantedFilterOption : ReactiveObject
{
    public bool? Value { get; init; }

    public string ResourceKey { get; init; } = string.Empty;

    public string Title => L10n.Get(ResourceKey);

    public string DisplayText => Title;

    public void RefreshLocalization()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(DisplayText));
    }

    public override string ToString() => DisplayText;

    public override bool Equals(object? obj) =>
        obj is WantedFilterOption option && option.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static IReadOnlyList<WantedFilterOption> All { get; } =
    [
        new() { Value = null, ResourceKey = "WantedFilterAll" },
        new() { Value = true, ResourceKey = "WantedFilterWanted" },
        new() { Value = false, ResourceKey = "WantedFilterNotWanted" }
    ];

    public static WantedFilterOption Find(bool? value) =>
        All.First(option => option.Value == value);
}
