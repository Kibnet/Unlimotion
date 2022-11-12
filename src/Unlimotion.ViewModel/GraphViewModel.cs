using System.Collections.ObjectModel;
using PropertyChanged;
using Splat;

namespace Unlimotion.ViewModel;

[AddINotifyPropertyChangedInterface]
public class GraphViewModel
{
    MainWindowViewModel mwm;
    public GraphViewModel()
    {
        mwm = Locator.Current.GetService<MainWindowViewModel>();
    }
    public object MyGraph { get; set; }
    public ReadOnlyObservableCollection<TaskWrapperViewModel> Tasks { get; set; }


    public bool ShowCompleted { get=> mwm.ShowCompleted; set=> mwm.ShowCompleted=value; }

    public bool ShowArchived { get => mwm.ShowArchived; set => mwm.ShowArchived = value; }
}