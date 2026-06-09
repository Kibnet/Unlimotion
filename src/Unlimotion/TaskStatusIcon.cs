using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Unlimotion.Domain;

namespace Unlimotion;

public class TaskStatusIcon : Control
{
    private const double DesignSize = 16;
    private const double BoxSize = 14;

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

    public static readonly StyledProperty<TaskStatus> StatusProperty =
        AvaloniaProperty.Register<TaskStatusIcon, TaskStatus>(nameof(Status));

    static TaskStatusIcon()
    {
        AffectsRender<TaskStatusIcon>(StatusProperty, IsEnabledProperty);
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
            ? isEnabled ? CompletedFillBrush : CompletedFillBrushDisabled
            : null;
        var borderPen = new Pen(borderBrush, 1.45 * scale);
        var boxRect = r(1.25, 1.25, BoxSize - 0.5, BoxSize - 0.5);

        context.DrawRectangle(boxFillBrush, borderPen, boxRect, 2 * scale, 2 * scale);

        switch (Status)
        {
            case TaskStatus.Prepared:
                DrawPreparedMark(context, p, isEnabled ? PreparedBrush : PreparedBrushDisabled, scale);
                break;
            case TaskStatus.InProgress:
                DrawInProgressMark(context, p, isEnabled ? AccentBrush : AccentBrushDisabled);
                break;
            case TaskStatus.Completed:
                DrawCompletedMark(context, p, CompletedMarkBrush, scale);
                break;
            case TaskStatus.Archived:
                DrawArchivedMark(context, p, r, isEnabled, scale);
                break;
        }
    }

    private static IBrush GetBorderBrush(TaskStatus status, bool isEnabled)
    {
        return status switch
        {
            TaskStatus.Prepared => isEnabled ? PreparedBrush : PreparedBrushDisabled,
            TaskStatus.InProgress or TaskStatus.Completed => isEnabled ? AccentBrush : AccentBrushDisabled,
            _ => isEnabled ? BorderBrush : BorderBrushDisabled
        };
    }

    private static void DrawPreparedMark(
        DrawingContext context,
        Func<double, double, Point> point,
        IBrush brush,
        double scale)
    {
        var pen = new Pen(brush, 1.85 * scale);
        context.DrawLine(pen, point(8, 4.2), point(8, 9.1));
        context.DrawEllipse(brush, null, point(8, 11.9), 1 * scale, 1 * scale);
    }

    private static void DrawInProgressMark(
        DrawingContext context,
        Func<double, double, Point> point,
        IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(point(6.2, 4.8), true);
            geometryContext.LineTo(point(11.5, 8));
            geometryContext.LineTo(point(6.2, 11.2));
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private static void DrawCompletedMark(
        DrawingContext context,
        Func<double, double, Point> point,
        IBrush brush,
        double scale)
    {
        var pen = new Pen(brush, 2.2 * scale);
        context.DrawLine(pen, point(4.1, 8.4), point(6.8, 11));
        context.DrawLine(pen, point(6.8, 11), point(12.2, 5));
    }

    private static void DrawArchivedMark(
        DrawingContext context,
        Func<double, double, Point> point,
        Func<double, double, double, double, Rect> rect,
        bool isEnabled,
        double scale)
    {
        var fill = isEnabled ? ArchivedMarkBrush : ArchivedMarkBrushDisabled;
        var lidPen = new Pen(fill, 1.2 * scale);
        context.DrawLine(lidPen, point(5.2, 5.2), point(10.8, 5.2));
        context.DrawRectangle(fill, null, rect(5, 7, 6, 4.2), 0.75 * scale, 0.75 * scale);
    }
}
