// Frozen snapshot of approved D (phase 0.4375), independent of the live renderer.
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
    private IBrush? _shadowBrush;

    public FrozenIconD()
    {
        IsHitTestVisible = false;
    }

    private const double CurrentPhase = 0.4375;
    internal bool SmallIcon { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsNaN(Width) ? 160 : Width;
        var height = double.IsNaN(Height) ? 84 : Height;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var scale = Math.Min(Bounds.Width / DesignWidth, Bounds.Height / DesignHeight);
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
            context.DrawEllipse(_shadowBrush, null, new Point(240, 223), 185, 22);
            var rear = _rearGeometry ??= Geometry.Parse(
                "M 191,48 C 223,62 239,98 242,130 C 247,157 266,184 286,196");
            var front = _baseGeometry ??= CreateBaseGeometry();
            context.DrawGeometry(null, _edgePen, rear);
            context.DrawGeometry(null, _rearPen, rear);
            context.DrawGeometry(null, _edgePen, front);
            context.DrawGeometry(null, _bodyPen, front);
            using (context.PushTransform(Matrix.CreateTranslation(0, -1.4)))
            {
                context.DrawGeometry(null, _bevelPen, front);
            }
            DrawTrail(context, CurrentPhase);
        }

    }

    private static Geometry CreateBaseGeometry()
    {
        // Coordinates trace the draft's circular bowls and diagonal cut faces.
        return Geometry.Parse("M 123,48 C 86,68 59,120 86,162 " +
            "C 108,200 154,214 190,190 C 232,162 236,104 268,69 " +
            "C 298,35 338,35 369,58 C 409,88 422,151 365,195");
    }

    internal static Point GetRibbonPoint(double position)
    {
        var segment = Math.Min(4, (int)(Math.Clamp(position, 0, 1) * 5));
        var t = Math.Clamp(position * 5 - segment, 0, 1);
        var (a, b, c, d) = segment switch
        {
            0 => (new Point(123, 48), new Point(86, 68), new Point(59, 120), new Point(86, 162)),
            1 => (new Point(86, 162), new Point(108, 200), new Point(154, 214), new Point(190, 190)),
            2 => (new Point(190, 190), new Point(232, 162), new Point(236, 104), new Point(268, 69)),
            3 => (new Point(268, 69), new Point(298, 35), new Point(338, 35), new Point(369, 58)),
            _ => (new Point(369, 58), new Point(409, 88), new Point(422, 151), new Point(365, 195))
        };
        var u = 1 - t;
        return new Point(u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X,
            u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
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

        _edgePen = new Pen(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.7, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Color.Parse("#858CAB"), 0), new(Color.Parse("#3D435C"), 0.4),
                new(Color.Parse("#24263A"), 0.7), new(Color.Parse("#677092"), 1)
            }
        }, SmallIcon ? 48 : 46, lineCap: PenLineCap.Flat);
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
        _shadowBrush = CreateGlow(Color.Parse("#4530214B"));
        _ribbonBrushes = new IBrush[9];
        for (var layer = 0; layer < _ribbonBrushes.Length; layer++)
        {
            var (alpha, red, green, blue) = LightColor(layer);
            _ribbonBrushes[layer] = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        }
    }

    private LinearGradientBrush CreateMetalBrush() => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.7, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(Color.Parse(SmallIcon ? "#727B9C" : "#555E7C"), 0),
            new(Color.Parse(SmallIcon ? "#4C536E" : "#30354B"), 0.4),
            new(Color.Parse(SmallIcon ? "#343A53" : "#1C2032"), 0.75),
            new(Color.Parse(SmallIcon ? "#626B8B" : "#454E6B"), 1)
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
