// Approved E: circular bowls and a crisp white underlay; light phase inherited from D.
// Historical type/file name retained; independent of the live animation.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Unlimotion.IconGenerator;

public sealed class FrozenIconD : Control
{
    internal const int StreakCount = 12;

    private const double DesignWidth = 480;
    private const double DesignHeight = 250;
    private const int RibbonSegments = 64;
    private static readonly double[] FilamentOffsets = [-17, 8, -5, 17, -11, 2, 12, -15, 5, -1, 15, -8];
    private static readonly (double Start, double Duration)[][] Launches = CreateLaunches();

    private Geometry? _baseGeometry;
    private Geometry? _rearGeometry;
    private IBrush[]? _ribbonBrushes;
    private Pen? _bodyPen;
    private Pen? _edgePen;
    private Pen? _rearPen;
    private Pen? _bevelPen;
    private IBrush? _glowBrush;
    private IBrush? _whiteGlowBrush;

    public FrozenIconD()
    {
        IsHitTestVisible = false;
    }

    private const double CurrentPhase = 0.4375;
    internal bool SmallIcon { get; set; }
    internal bool CompactCanvas { get; set; } = true;
    private const double Underlay = 20;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsNaN(Width) ? 160 : Width;
        var height = double.IsNaN(Height) ? 84 : Height;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // B keeps the geometry intact: 426 units of silhouette plus 9 units per side.
        var scale = Math.Min(Bounds.Width / (CompactCanvas ? 444 : DesignWidth), Bounds.Height / DesignHeight);
        if (scale <= 0)
        {
            return;
        }

        var originX = (Bounds.Width - DesignWidth * scale) / 2;
        var originY = (Bounds.Height - DesignHeight * scale) / 2;
        var transform = Matrix.CreateScale(scale, scale) *
                        Matrix.CreateTranslation(originX, originY);
        EnsureDrawingResources();

        using (context.PushTransform(transform))
        {
            // Exact circular holes; the opaque underlay closes both letter gaps.
            var radius = 103 + Underlay;
            Geometry outer = new CombinedGeometry(GeometryCombineMode.Union,
                new EllipseGeometry(new Rect(150 - radius, 125 - radius, 2 * radius, 2 * radius)),
                new EllipseGeometry(new Rect(330 - radius, 125 - radius, 2 * radius, 2 * radius)));
            var holeRadius = 57 - Underlay;
            foreach (var cx in new[] { 150d, 330d })
                outer = new CombinedGeometry(GeometryCombineMode.Exclude, outer,
                    new EllipseGeometry(new Rect(cx - holeRadius, 125 - holeRadius, 2 * holeRadius, 2 * holeRadius)));
            context.DrawGeometry(Brushes.White, null, outer);
            var rear = _rearGeometry ??= SamplePath(GetRearPoint);
            context.DrawGeometry(null, _edgePen, rear);
            context.DrawGeometry(null, _rearPen, rear);
            var front = _baseGeometry ??= CreateBaseGeometry();
            context.DrawGeometry(null, _edgePen, front);
            context.DrawGeometry(null, _bodyPen, front);
            using (context.PushTransform(Matrix.CreateTranslation(0, -1.4)))
            {
                context.DrawGeometry(null, _bevelPen, front);
            }
            DrawTrail(context, CurrentPhase);
        }

    }

    private const double Radius = 80;
    private static readonly double TangentAngle = Math.Acos(80d / 90) * 180 / Math.PI;
    private static Point Circle(double cx, double degrees) =>
        new(cx + Radius * Math.Cos(degrees * Math.PI / 180), 125 + Radius * Math.Sin(degrees * Math.PI / 180));
    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static Geometry SamplePath(Func<double, Point> point)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(point(0), false);
            for (var i = 1; i <= 1536; i++) path.LineTo(point(i / 1536d));
            path.EndFigure(false);
        }
        return geometry;
    }
    private static Geometry CreateBaseGeometry() => SamplePath(GetRibbonPoint);
    internal static Point GetRibbonPoint(double position)
    {
        // Circular bowls joined by their common internal tangent.
        var p = Math.Clamp(position, 0, 1);
        if (p < 0.44) return Circle(150, 250 - (250 - TangentAngle) * (p / 0.44));
        if (p < 0.56) return Lerp(Circle(150, TangentAngle), Circle(330, 180 + TangentAngle), (p - 0.44) / 0.12);
        return Circle(330, 180 + TangentAngle + (430 - 180 - TangentAngle) * ((p - 0.56) / 0.44));
    }
    private static Point GetRearPoint(double p)
    {
        if (p < 0.25) return Circle(150, -70 + (70 - TangentAngle) * (p / 0.25));
        if (p < 0.75) return Lerp(Circle(150, -TangentAngle), Circle(330, 180 - TangentAngle), (p - 0.25) / 0.5);
        return Circle(330, 180 - TangentAngle + (110 - 180 + TangentAngle) * ((p - 0.75) / 0.25));
    }

    internal static (double Length, double Width) GetStreakSize(int index) => index switch
    {
        0 => (0.32, 2.6),
        1 => (0.21, 1.6),
        2 => (0.12, 0.8),
        3 => (0.16, 1.1),
        4 => (0.08, 0.6),
        5 => (0.25, 1.9),
        6 => (0.10, 0.7),
        7 => (0.18, 1.3),
        8 => (0.065, 0.45),
        9 => (0.14, 0.95),
        10 => (0.23, 1.45),
        _ => (0.09, 0.55)
    };

    internal static int GetStreakLaps(int index) => 9 + index * 7 % StreakCount;

    internal static (double Start, double Duration) GetStreakLaunch(int index, int lap) => Launches[index][lap];

    private static (double Start, double Duration)[][] CreateLaunches()
    {
        var result = new (double Start, double Duration)[StreakCount][];
        uint seed = 0x81D3A72B;
        double Next()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return seed / (double)uint.MaxValue;
        }
        for (var index = 0; index < StreakCount; index++)
        {
            var count = GetStreakLaps(index);
            var intervals = new double[count];
            var total = 0d;
            for (var lap = 0; lap < count; lap++)
                total += intervals[lap] = 0.65 + Next() * 0.7;
            var start = Next();
            result[index] = new (double, double)[count];
            for (var lap = 0; lap < count; lap++)
            {
                var interval = intervals[lap] / total;
                result[index][lap] = (start, interval * (0.82 + Next() * 0.12));
                start += interval;
            }
        }
        return result;
    }

    // A compact pulse: no residual light outside the moving streak. Its peak is
    // also the flare anchor, so neither the flare nor the tail can drift away.
    internal static double GetStreakEnvelope(double relativePosition) =>
        relativePosition < 0 || relativePosition > 1 ? 0 :
        relativePosition <= 0.72 ? SmoothStep(relativePosition / 0.72) :
        SmoothStep((1 - relativePosition) / 0.28);

    internal static (double Position, double Opacity) GetFlareState(double phase, int index)
    {
        foreach (var launch in Launches[index])
        {
            var elapsed = phase - launch.Start;
            elapsed -= Math.Floor(elapsed);
            if (elapsed >= launch.Duration) continue;
            var position = elapsed / launch.Duration;
            return (position, SmoothStep(position / 0.12) * SmoothStep((1 - position) / 0.12));
        }
        return (0, 0);
    }

    private static double SmoothStep(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private void DrawTrail(DrawingContext context, double phase)
    {
        for (var filament = 0; filament < FilamentOffsets.Length; filament++)
        {
            var (peak, strength) = GetFlareState(phase, filament);
            if (strength <= 0) continue;
            var (length, width) = GetStreakSize(filament);
            if (SmallIcon && width < 1.3) continue;
            if (SmallIcon) width *= 1.55;
            var start = peak - length * 0.72;
            using var opacity = context.PushOpacity(strength);
            // Nested continuous tapered ribbons avoid the beads produced by
            // overlapping round-capped segments. All layers vanish together.
            for (var layer = 0; layer < 9; layer++)
            {
                var spread = layer switch { 0 => 24d, 1 => 19d, 2 => 14d, 3 => 10d, 4 => 7d, 5 => 4.5, 6 => 2.8, 7 => 1.6, _ => 1d };
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    for (var side = 0; side < 2; side++)
                        for (var sample = 0; sample <= RibbonSegments; sample++)
                        {
                            var u = (side == 0 ? sample : RibbonSegments - sample) / (double)RibbonSegments;
                            var t = Math.Clamp(start + u * length, 0, 1);
                            var envelope = GetStreakEnvelope((t - start) / length);
                            var halfWidth = width * spread * envelope / 2;
                            var point = GetFilamentPoint(t, filament, (side == 0 ? 1 : -1) * halfWidth);
                            if (side == 0 && sample == 0) path.BeginFigure(point, true);
                            else path.LineTo(point);
                        }
                    path.EndFigure(true);
                }
                context.DrawGeometry(_ribbonBrushes![layer], null, geometry);
            }
            var anchor = GetFilamentPoint(peak, filament);
            var radius = 4 + width * 7;
            context.DrawEllipse(_glowBrush, null, anchor, radius * 1.5, radius);
            context.DrawEllipse(_whiteGlowBrush, null, anchor, radius * 2.4, width);
            context.DrawEllipse(_whiteGlowBrush, null, anchor, width * 3, width * 1.5);
        }
    }

    internal static Point GetFilamentPoint(double position, int index, double normalOffset = 0)
    {
        var p = GetRibbonPoint(position);
        var tangent = GetRibbonPoint(Math.Min(1, position + 0.001)) - GetRibbonPoint(Math.Max(0, position - 0.001));
        var length = Math.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
        var offset = FilamentOffsets[index] + normalOffset;
        return new Point(p.X - tangent.Y / length * offset, p.Y + tangent.X / length * offset);
    }

    private void EnsureDrawingResources()
    {
        if (_bodyPen != null) return;

        _edgePen = new Pen(new SolidColorBrush(Color.Parse(SmallIcon ? "#898599" : "#57545D")), 46, lineCap: PenLineCap.Flat);
        _rearPen = new Pen(CreateMetalBrush(), 43, lineCap: PenLineCap.Flat);
        _bodyPen = new Pen(CreateMetalBrush(), 44, lineCap: PenLineCap.Flat);
        _bevelPen = new Pen(new SolidColorBrush(Color.Parse("#40383C56")), 40, lineCap: PenLineCap.Flat);
        _glowBrush = new RadialGradientBrush
        {
            GradientStops = new GradientStops
            {
                new(Color.Parse("#FFE0E4FF"), 0),
                new(Color.Parse("#B08070FF"), 0.18),
                new(Color.Parse("#454A28FF"), 0.5),
                new(Color.Parse("#004A28FF"), 1)
            }
        };
        _whiteGlowBrush = CreateGlow(Color.Parse("#FFE2E8FF"));
        _ribbonBrushes = new IBrush[9];
        for (var layer = 0; layer < _ribbonBrushes.Length; layer++)
        {
            var (alpha, red, green, blue) = LightColor(layer);
            _ribbonBrushes[layer] = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        }
    }

    private static LinearGradientBrush CreateMetalBrush() => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.7, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(Color.Parse("#292A30"), 0), new(Color.Parse("#13121C"), 0.4),
            new(Color.Parse("#08090D"), 0.75), new(Color.Parse("#202027"), 1)
        }
    };

    private static RadialGradientBrush CreateGlow(Color color) => new()
    {
        GradientStops = new GradientStops
        {
            new(color, 0), new(Color.FromArgb((byte)(color.A * 0.35), color.R, color.G, color.B), 0.35),
            new(Color.FromArgb(0, color.R, color.G, color.B), 1)
        }
    };

    private static (byte Alpha, byte Red, byte Green, byte Blue) LightColor(int layer) => layer switch
    {
        0 => (2, 70, 35, 255),
        1 => (4, 70, 35, 255),
        2 => (6, 75, 40, 255),
        3 => (10, 80, 45, 255),
        4 => (17, 85, 50, 255),
        5 => (28, 95, 60, 255),
        6 => (55, 110, 75, 255),
        7 => (210, 135, 115, 255),
        _ => (255, 205, 215, 255)
    };

}
