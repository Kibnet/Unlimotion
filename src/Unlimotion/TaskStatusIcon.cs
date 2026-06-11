using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Unlimotion.Domain;

namespace Unlimotion;

public class TaskStatusIcon : Control
{
    private const double DesignSize = 20;
    private const double BoxInset = 0.5;
    private const double BoxSize = 19;

    private static readonly IBrush AccentBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD));
    private static readonly IBrush AccentBrushDisabled = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0x0F, 0x6C, 0xBD));
    private static readonly IBrush PreparedBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x85, 0x75));
    private static readonly IBrush PreparedBrushDisabled = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0x00, 0x85, 0x75));
    private static readonly IBrush BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x8A, 0x93, 0x9E));
    private static readonly IBrush BorderBrushDisabled = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0x8A, 0x93, 0x9E));
    private static readonly IBrush CompletedFillBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD));
    private static readonly IBrush CompletedFillBrushDisabled = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0x0F, 0x6C, 0xBD));
    private static readonly IBrush CompletedMarkBrush = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IBrush ArchivedMarkBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x6B, 0x74, 0x80));
    private static readonly IBrush ArchivedMarkBrushDisabled = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0x6B, 0x74, 0x80));

    private static readonly string[] UncheckedBorderResourceKeys =
    [
        "CheckBoxCheckBackgroundStrokeUnchecked",
        "CheckBoxBorderBrush",
        "ThemeControlHighBrush",
        "ThemeControlMidBrush"
    ];

    private static readonly string[] UncheckedPointerOverBorderResourceKeys =
    [
        "CheckBoxCheckBackgroundStrokeUncheckedPointerOver",
        "CheckBoxBorderBrushPointerOver",
        "ThemeControlHighBrush",
        "ThemeControlMidBrush"
    ];

    private static readonly string[] UncheckedDisabledBorderResourceKeys =
    [
        "CheckBoxCheckBackgroundStrokeUncheckedDisabled",
        "CheckBoxBorderBrushDisabled",
        "ThemeControlDisabledBrush",
        "ThemeControlMidBrush"
    ];

    private static readonly string[] CheckedFillResourceKeys =
    [
        "CheckBoxCheckBackgroundFillChecked",
        "AccentButtonBackground",
        "SystemAccentColor"
    ];

    private static readonly string[] CheckedGlyphResourceKeys =
    [
        "CheckBoxCheckGlyphForegroundChecked",
        "SystemControlForegroundChromeWhiteBrush"
    ];

    public static readonly StyledProperty<TaskStatus> StatusProperty =
        AvaloniaProperty.Register<TaskStatusIcon, TaskStatus>(nameof(Status));

    static TaskStatusIcon()
    {
        AffectsRender<TaskStatusIcon>(StatusProperty, IsEnabledProperty, IsPointerOverProperty);
    }

    public TaskStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsNaN(Width) ? DesignSize : Width;
        var height = double.IsNaN(Height) ? DesignSize : Height;
        return new Size(width, height);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (string.Equals(change.Property.Name, "ActualThemeVariant", StringComparison.Ordinal))
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var scale = Math.Min(Bounds.Width, Bounds.Height) / DesignSize;
        if (scale <= 0)
        {
            return;
        }

        var originX = (Bounds.Width - DesignSize * scale) / 2;
        var originY = (Bounds.Height - DesignSize * scale) / 2;
        Point p(double x, double y) => new(originX + x * scale, originY + y * scale);
        Rect r(double x, double y, double width, double height) =>
            new(originX + x * scale, originY + y * scale, width * scale, height * scale);

        var isEnabled = IsEffectivelyEnabled;
        var borderBrush = GetBorderBrush(Status, isEnabled);
        var boxFillBrush = Status == TaskStatus.Completed
            ? ResolveBrush(CheckedFillResourceKeys, isEnabled ? CompletedFillBrush : CompletedFillBrushDisabled)
            : null;
        var borderPen = new Pen(borderBrush, GetBorderThickness(scale));
        var boxRect = r(BoxInset, BoxInset, BoxSize, BoxSize);

        context.DrawRectangle(boxFillBrush, borderPen, boxRect, 3 * scale, 3 * scale);

        switch (Status)
        {
            case TaskStatus.Prepared:
                DrawPreparedMark(context, p, isEnabled ? PreparedBrush : PreparedBrushDisabled, scale);
                break;
            case TaskStatus.InProgress:
                DrawInProgressMark(context, p, isEnabled ? AccentBrush : AccentBrushDisabled);
                break;
            case TaskStatus.Completed:
                DrawCompletedMark(context, p, ResolveBrush(CheckedGlyphResourceKeys, CompletedMarkBrush));
                break;
            case TaskStatus.Archived:
                DrawArchivedMark(context, r, isEnabled, scale);
                break;
        }
    }

    private IBrush GetBorderBrush(TaskStatus status, bool isEnabled)
    {
        if (!isEnabled)
        {
            return status switch
            {
                TaskStatus.Prepared => PreparedBrushDisabled,
                TaskStatus.InProgress or TaskStatus.Completed => AccentBrushDisabled,
                _ => ResolveBrush(UncheckedDisabledBorderResourceKeys, BorderBrushDisabled)
            };
        }

        return status switch
        {
            TaskStatus.Prepared => PreparedBrush,
            TaskStatus.InProgress => AccentBrush,
            TaskStatus.Completed => ResolveBrush(CheckedFillResourceKeys, CompletedFillBrush),
            _ => ResolveBrush(
                IsPointerOver ? UncheckedPointerOverBorderResourceKeys : UncheckedBorderResourceKeys,
                BorderBrush)
        };
    }

    private IBrush ResolveBrush(IReadOnlyList<string> resourceKeys, IBrush fallback)
    {
        foreach (var resourceKey in resourceKeys)
        {
            if (TryGetResource(resourceKey, ActualThemeVariant, out var resource) ||
                Application.Current?.TryGetResource(resourceKey, ActualThemeVariant, out resource) == true)
            {
                if (resource is IBrush brush)
                {
                    return brush;
                }

                if (resource is Color color)
                {
                    return new ImmutableSolidColorBrush(color);
                }
            }
        }

        return fallback;
    }

    private static void DrawPreparedMark(
        DrawingContext context,
        Func<double, double, Point> point,
        IBrush brush,
        double scale)
    {
        var pen = new Pen(brush, 1.85 * scale);
        context.DrawLine(pen, point(10, 5.2), point(10, 11.7));
        context.DrawEllipse(brush, null, point(10, 15), 1.1 * scale, 1.1 * scale);
    }

    private static void DrawInProgressMark(
        DrawingContext context,
        Func<double, double, Point> point,
        IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(point(8, 6.1), true);
            geometryContext.LineTo(point(14, 10));
            geometryContext.LineTo(point(8, 13.9));
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private static double GetBorderThickness(double scale) => scale;

    private static void DrawCompletedMark(
        DrawingContext context,
        Func<double, double, Point> point,
        IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(point(8.35, 13.55), true);
            geometryContext.LineTo(point(4.65, 9.85));
            geometryContext.LineTo(point(6.2, 8.3));
            geometryContext.LineTo(point(8.35, 10.45));
            geometryContext.LineTo(point(14.3, 4.5));
            geometryContext.LineTo(point(15.85, 6.05));
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private static void DrawArchivedMark(
        DrawingContext context,
        Func<double, double, double, double, Rect> rect,
        bool isEnabled,
        double scale)
    {
        var fill = isEnabled ? ArchivedMarkBrush : ArchivedMarkBrushDisabled;
        context.DrawRectangle(fill, null, rect(4.8, 5.9, 10.4, 2.5), 0.7 * scale, 0.7 * scale);
        context.DrawRectangle(fill, null, rect(5.8, 8.8, 8.4, 6.4), 1.1 * scale, 1.1 * scale);
    }
}
