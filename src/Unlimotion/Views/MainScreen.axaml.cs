using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Unlimotion.Views
{
    public partial class MainScreen : UserControl
    {
        private readonly RotateTransform _tasksLoadingSpinnerTransform = new();
        private readonly DispatcherTimer _tasksLoadingSpinnerTimer;

        public MainScreen()
        {
            InitializeComponent();
            TasksLoadingSpinner.RenderTransform = _tasksLoadingSpinnerTransform;
            _tasksLoadingSpinnerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _tasksLoadingSpinnerTimer.Tick += TasksLoadingSpinnerTimerOnTick;
            AttachedToVisualTree += OnAttachedToVisualTree;
            DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _tasksLoadingSpinnerTimer.Start();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _tasksLoadingSpinnerTimer.Stop();
        }

        private void TasksLoadingSpinnerTimerOnTick(object? sender, EventArgs e)
        {
            if (!TasksLoadingOverlay.IsVisible)
            {
                return;
            }

            _tasksLoadingSpinnerTransform.Angle = (_tasksLoadingSpinnerTransform.Angle + 18d) % 360d;
        }
    }
}
