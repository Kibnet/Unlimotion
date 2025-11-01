using PropertyChanged;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class SearchDefinition
{
    public string? SearchText { get; set; } = "";
}
