﻿using System.Collections.ObjectModel;
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

    public ReadOnlyObservableCollection<TaskWrapperViewModel> Tasks { get; set; }
    public ReadOnlyObservableCollection<TaskWrapperViewModel> UnlockedTasks { get; set; }

    public bool OnlyUnlocked { get; set; } = true;
    public bool HideUnactual { get; set; }

    public bool? ShowWanted { get=> mwm.ShowWanted; set=> mwm.ShowWanted = value; }

    public bool ShowCompleted { get=> mwm.ShowCompleted; set=> mwm.ShowCompleted=value; }

    public bool ShowArchived { get => mwm.ShowArchived; set => mwm.ShowArchived = value; }
}