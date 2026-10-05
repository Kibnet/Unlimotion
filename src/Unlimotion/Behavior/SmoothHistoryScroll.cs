using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Unlimotion.Behavior;

/// <summary>Animates wheel input within history without delaying direct scrollbar or keyboard navigation.</summary>
public sealed class SmoothHistoryScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<SmoothHistoryScroll, Control, bool>("IsEnabled");
    private static readonly AttachedProperty<ScrollState?> StateProperty =
        AvaloniaProperty.RegisterAttached<SmoothHistoryScroll, Control, ScrollState?>("State");

    static SmoothHistoryScroll() => IsEnabledProperty.Changed.AddClassHandler<Control>((host, _) =>
    {
        host.GetValue(StateProperty)?.Dispose();
        host.SetValue(StateProperty, GetIsEnabled(host) ? new ScrollState(host) : null);
    });

    public static bool GetIsEnabled(Control host) => host.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control host, bool value) => host.SetValue(IsEnabledProperty, value);

    private sealed class ScrollState : IDisposable
    {
        private readonly Control _host;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly Stopwatch _clock = new();
        private ScrollViewer? _scroll;
        private double _start;
        private double _target;
        private bool _settingOffset;

        public ScrollState(Control host)
        {
            _host = host;
            host.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
            host.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            host.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            host.DetachedFromVisualTree += OnDetached;
            host.PropertyChanged += OnHostChanged;
            _timer.Tick += OnTick;
        }

        private void OnWheel(object? sender, PointerWheelEventArgs args)
        {
            if (args.Delta.Y == 0 || args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                return;

            // The closest scrollable ancestor wins, including an expanded full-value text area.
            ScrollViewer? scroll = null;
            for (var visual = args.Source as Visual; visual is not null && visual != _host; visual = visual.GetVisualParent())
            {
                if (visual is ScrollViewer candidate && candidate.IsEffectivelyVisible &&
                    candidate.VerticalScrollBarVisibility is Avalonia.Controls.Primitives.ScrollBarVisibility.Auto or Avalonia.Controls.Primitives.ScrollBarVisibility.Visible &&
                    candidate.Extent.Height > candidate.Viewport.Height)
                {
                    scroll = candidate;
                    break;
                }
            }
            if (scroll is null)
                return;

            var target = ReferenceEquals(_scroll, scroll) ? _target : scroll.Offset.Y;
            if (!ReferenceEquals(_scroll, scroll))
            {
                Stop();
                _scroll = scroll;
                scroll.PropertyChanged += OnScrollChanged;
            }
            _start = scroll.Offset.Y;
            _target = Math.Clamp(target - args.Delta.Y * 50, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            _clock.Restart();
            _timer.Start();
            args.Handled = true;
        }

        private void OnTick(object? sender, EventArgs args)
        {
            if (_scroll is not { IsEffectivelyVisible: true } scroll || !_host.IsEffectivelyVisible)
            {
                Stop();
                return;
            }
            var progress = Math.Min(1, _clock.Elapsed.TotalMilliseconds / 160);
            var eased = 1 - Math.Pow(1 - progress, 3);
            var offset = Math.Clamp(_start + (_target - _start) * eased, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            _settingOffset = true;
            try
            {
                scroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(scroll.Offset.X, offset));
            }
            finally
            {
                _settingOffset = false;
            }
            if (progress >= 1)
                Stop();
        }

        private void OnScrollChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == ScrollViewer.OffsetProperty && !_settingOffset)
                Stop();
        }

        private void OnHostChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Visual.IsVisibleProperty || args.Property == Expander.IsExpandedProperty)
                Stop();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs args) => Stop();
        private void OnKeyDown(object? sender, KeyEventArgs args) => Stop();
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args) => Stop();

        private void Stop()
        {
            _timer.Stop();
            _clock.Stop();
            if (_scroll is not null)
                _scroll.PropertyChanged -= OnScrollChanged;
            _scroll = null;
        }

        public void Dispose()
        {
            Stop();
            _timer.Tick -= OnTick;
            _host.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
            _host.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            _host.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _host.DetachedFromVisualTree -= OnDetached;
            _host.PropertyChanged -= OnHostChanged;
        }
    }
}
