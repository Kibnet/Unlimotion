using System;
using PropertyChanged;
using System.Text;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class SearchDefinition
{
    public const int DefaultThrottleMs = 600;
    public static string NormalizeText(string s) => (s ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormKC);
    public string? SearchText { get; set; } = "";
}
