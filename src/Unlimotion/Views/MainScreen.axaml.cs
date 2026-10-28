using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.ComponentModel;
using Unlimotion.ViewModel;

namespace Unlimotion.Views
{
    public partial class MainScreen : UserControl
    {
        private INotifyPropertyChanged? _observedViewModel;
        private INotifyPropertyChanged? _observedSettings;

        public MainScreen()
        {
            InitializeComponent();
            SettingsHotkeyHelpPanel.CloseRequested += (_, _) => HideSettingsHotkeyHelp();
            AddHandler(KeyDownEvent, OnSettingsKeyDown, RoutingStrategies.Tunnel);
            DataContextChanged += (_, _) => ObserveViewModel();
            AttachedToVisualTree += (_, _) => ObserveViewModel();
            DetachedFromVisualTree += (_, _) => StopObservingViewModel();
        }

        private void ObserveViewModel()
        {
            StopObservingViewModel();
            _observedViewModel = DataContext as INotifyPropertyChanged;
            if (_observedViewModel != null) _observedViewModel.PropertyChanged += ViewModelPropertyChanged;
            _observedSettings = (DataContext as MainWindowViewModel)?.Settings as INotifyPropertyChanged;
            if (_observedSettings != null) _observedSettings.PropertyChanged += SettingsPropertyChanged;
        }

        private void StopObservingViewModel()
        {
            if (_observedViewModel != null) _observedViewModel.PropertyChanged -= ViewModelPropertyChanged;
            _observedViewModel = null;
            if (_observedSettings != null) _observedSettings.PropertyChanged -= SettingsPropertyChanged;
            _observedSettings = null;
        }

        private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsTaskSpaceRecoveryRequired) &&
                DataContext is MainWindowViewModel vm && vm.Settings.IsTaskSpaceRecoveryRequired)
            {
                SettingsHotkeyHelpOverlayHost.IsVisible = false;
                MainControl.HideHotkeyHelp();
            }
        }

        private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (e.PropertyName is nameof(MainWindowViewModel.IsSettingsDialogOpen) or nameof(MainWindowViewModel.SettingsPickerDepth))
            {
                if (vm.IsSettingsDialogOpen || vm.IsSettingsPickerOpen)
                {
                    SettingsHotkeyHelpOverlayHost.IsVisible = false;
                    MainControl.HideHotkeyHelp();
                }
                return;
            }
            if (e.PropertyName != nameof(MainWindowViewModel.IsSettingsOpen)) return;
            if (!vm.IsSettingsOpen) SettingsHotkeyHelpOverlayHost.IsVisible = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (vm.IsSettingsOpen) this.FindControl<Button>("GlobalSettingsCloseButton")?.Focus();
                else if (!vm.Settings.IsTaskSpaceRecoveryRequired && !vm.IsSettingsDialogOpen)
                    MainControl.FindControl<Button>("GlobalSettingsButton")?.Focus();
            });
        }

        private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Handled || DataContext is not MainWindowViewModel vm || vm.IsSettingsDialogOpen || vm.IsSettingsPickerOpen) return;
            if (e.Key == Key.OemComma && e.KeyModifiers == KeyModifiers.Control)
            {
                vm.OpenSettings();
                e.Handled = true;
            }
            else if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None && vm.IsSettingsOpen)
            {
                if (SettingsHotkeyHelpOverlayHost.IsVisible) HideSettingsHotkeyHelp();
                else ShowSettingsHotkeyHelp();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && vm.IsSettingsOpen)
            {
                if (SettingsHotkeyHelpOverlayHost.IsVisible) HideSettingsHotkeyHelp();
                else vm.CloseSettings();
                e.Handled = true;
            }
        }

        internal void ShowSettingsHotkeyHelp()
        {
            if (DataContext is not MainWindowViewModel vm || !vm.IsSettingsOpen || vm.IsSettingsDialogOpen ||
                vm.IsSettingsPickerOpen || vm.Settings.IsTaskSpaceRecoveryRequired) return;
            SettingsHotkeyHelpOverlayHost.IsVisible = true;
            SettingsHotkeyHelpOverlayHost.Focus();
        }

        private void HideSettingsHotkeyHelp()
        {
            SettingsHotkeyHelpOverlayHost.IsVisible = false;
            this.FindControl<Button>("GlobalSettingsCloseButton")?.Focus();
        }

        internal bool TryHandleHotkeyHelpKey(KeyEventArgs e)
        {
            return MainControl.TryHandleHotkeyHelpKey(e);
        }
    }
}
