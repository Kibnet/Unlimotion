using PropertyChanged;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class GraphViewModel
{
    public object MyGraph { get; set; }
}