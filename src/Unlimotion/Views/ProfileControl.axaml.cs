using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Unlimotion.ViewModel;

namespace Unlimotion.Views;

public partial class ProfileControl : UserControl
{
    // Must match the editor circle's diameter in ProfileControl.axaml so a pixel drag maps to the
    // right fraction of the circle (offsets are stored as a fraction of the diameter).
    private const double EditorDiameter = 200d;
    private const double WheelZoomStep = 0.12d;

    private bool _dragging;
    private Point _lastPointerPosition;

    public ProfileControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private UserProfileViewModel? ViewModel => DataContext as UserProfileViewModel;

    private void AvatarEditor_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is not { HasAvatar: true } || sender is not Control control)
        {
            return;
        }

        _dragging = true;
        _lastPointerPosition = e.GetPosition(control);
        e.Pointer.Capture(control);
    }

    private void AvatarEditor_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var vm = ViewModel;
        if (!_dragging || vm == null || sender is not Control control)
        {
            return;
        }

        var position = e.GetPosition(control);
        var delta = position - _lastPointerPosition;
        _lastPointerPosition = position;

        vm.AvatarOffsetX += delta.X / EditorDiameter;
        vm.AvatarOffsetY += delta.Y / EditorDiameter;
        ClampOffsets();
    }

    private void AvatarEditor_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void AvatarEditor_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var vm = ViewModel;
        if (vm is not { HasAvatar: true })
        {
            return;
        }

        var factor = 1 + (e.Delta.Y * WheelZoomStep);
        vm.AvatarZoom = UserProfileSettings.ClampAvatarZoom(vm.AvatarZoom * factor);
        ClampOffsets();
        e.Handled = true;
    }

    private void AvatarZoom_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        // Zooming out shrinks how far the image may pan — keep the offsets inside the new bounds.
        ClampOffsets();
    }

    private void ClampOffsets()
    {
        var vm = ViewModel;
        if (vm == null)
        {
            return;
        }

        var (ratioX, ratioY) = GetImageAxisRatios();
        vm.AvatarOffsetX = UserProfileSettings.ClampAvatarOffset(vm.AvatarOffsetX, vm.AvatarZoom, ratioX);
        vm.AvatarOffsetY = UserProfileSettings.ClampAvatarOffset(vm.AvatarOffsetY, vm.AvatarZoom, ratioY);
    }

    private (double RatioX, double RatioY) GetImageAxisRatios()
    {
        if (this.FindControl<Image>("AvatarPreviewImage")?.Source is Bitmap bitmap)
        {
            var size = bitmap.PixelSize;
            if (size.Width > 0 && size.Height > 0)
            {
                double min = System.Math.Min(size.Width, size.Height);
                return (size.Width / min, size.Height / min);
            }
        }

        return (1d, 1d);
    }
}
