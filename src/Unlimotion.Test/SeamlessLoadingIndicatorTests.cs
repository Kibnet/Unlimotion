using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Unlimotion;

namespace Unlimotion.Test;

public sealed class SeamlessLoadingIndicatorTests
{
    [Test]
    public async Task NormalizePhase_WrapsExactlyAtEveryPeriod()
    {
        var period = SeamlessLoadingIndicator.AnimationPeriod;

        await Assert.That(SeamlessLoadingIndicator.NormalizePhase(TimeSpan.Zero, period))
            .IsEqualTo(0d);
        await Assert.That(SeamlessLoadingIndicator.NormalizePhase(period, period))
            .IsEqualTo(0d);
        await Assert.That(SeamlessLoadingIndicator.NormalizePhase(period * 4, period))
            .IsEqualTo(0d);
        await Assert.That(SeamlessLoadingIndicator.NormalizePhase(period / 4, period))
            .IsEqualTo(0.25d);
    }

    [Test]
    public async Task Streaks_HaveDarkEndsAndDifferentLengthsAndWidths()
    {
        foreach (var position in new[] { -1d, 0d, 1d, 2d })
            await Assert.That(SeamlessLoadingIndicator.GetStreakEnvelope(position)).IsEqualTo(0d);
        await Assert.That(SeamlessLoadingIndicator.GetStreakEnvelope(0.72)).IsEqualTo(1d);
        for (var i = 0; i < 2; i++)
        {
            var larger = SeamlessLoadingIndicator.GetStreakSize(i);
            var smaller = SeamlessLoadingIndicator.GetStreakSize(i + 1);
            await Assert.That(larger.Length > smaller.Length && larger.Width > smaller.Width).IsTrue();
            await Assert.That(larger.Length < 0.5).IsTrue();
        }
    }

    [Test]
    public async Task StreakAndFlare_ArePeriodicAndShareTheOffsetTrack()
    {
        for (var index = 0; index < SeamlessLoadingIndicator.StreakCount; index++)
        {
            var start = SeamlessLoadingIndicator.GetFlareState(0, index);
            var end = SeamlessLoadingIndicator.GetFlareState(1, index);
            await Assert.That(Math.Abs(start.Opacity - end.Opacity) < 0.000001).IsTrue();
            if (start.Opacity > 0)
            {
                var first = SeamlessLoadingIndicator.GetFilamentPoint(start.Position, index);
                var last = SeamlessLoadingIndicator.GetFilamentPoint(end.Position, index);
                await Assert.That(Math.Abs(first.X - last.X) + Math.Abs(first.Y - last.Y) < 0.000001).IsTrue();
            }
        }
    }

    [Test]
    public async Task Flares_TravelFromLeftToRightAndWrapOnlyWhileInvisible()
    {
        for (var index = 0; index < SeamlessLoadingIndicator.StreakCount; index++)
        {
            var launch = SeamlessLoadingIndicator.GetStreakLaunch(index, 0);
            var laps = 1 / launch.Duration;
            var offset = launch.Start;
            var entering = SeamlessLoadingIndicator.GetFlareState(offset, index);
            var left = SeamlessLoadingIndicator.GetFlareState(offset + 0.1 / laps, index);
            var middle = SeamlessLoadingIndicator.GetFlareState(offset + 0.5 / laps, index);
            var right = SeamlessLoadingIndicator.GetFlareState(offset + 0.9 / laps, index);
            var leaving = SeamlessLoadingIndicator.GetFlareState(offset + (1 - 0.00001) / laps, index);
            var wrapped = SeamlessLoadingIndicator.GetFlareState(offset + (1 + 0.00001) / laps, index);

            await Assert.That(entering.Opacity < 0.000001).IsTrue();
            await Assert.That(left.Position < middle.Position && middle.Position < right.Position).IsTrue();
            await Assert.That(middle.Opacity).IsEqualTo(1d);
            await Assert.That(left.Opacity < middle.Opacity && right.Opacity < middle.Opacity).IsTrue();
            await Assert.That(leaving.Opacity < 0.000001 && wrapped.Opacity < 0.000001).IsTrue();
            await Assert.That(leaving.Position > 0.99 && wrapped.Opacity == 0).IsTrue();
        }
    }

    [Test]
    public async Task TwelveStreaks_HaveDistinctSizesAndMeasuredSpeeds()
    {
        var sizes = new HashSet<(double, double)>();
        var speeds = new HashSet<double>();
        await Assert.That(SeamlessLoadingIndicator.StreakCount).IsEqualTo(12);
        for (var index = 0; index < SeamlessLoadingIndicator.StreakCount; index++)
        {
            sizes.Add(SeamlessLoadingIndicator.GetStreakSize(index));
            var launch = SeamlessLoadingIndicator.GetStreakLaunch(index, 0);
            var laps = 1 / launch.Duration;
            speeds.Add(laps);
            var phase = launch.Start + 0.4 / laps;
            var start = SeamlessLoadingIndicator.GetFlareState(phase, index);
            var next = SeamlessLoadingIndicator.GetFlareState(phase + 0.0001, index);
            await Assert.That(Math.Abs((next.Position - start.Position) / 0.0001 - laps) < 0.000001).IsTrue();
        }
        await Assert.That(sizes.Count).IsEqualTo(12);
        await Assert.That(speeds.Count).IsEqualTo(12);
    }

    [Test]
    public async Task Launches_HaveIrregularIntervalsAndFullyDarkPauses()
    {
        for (var index = 0; index < SeamlessLoadingIndicator.StreakCount; index++)
        {
            var intervals = new HashSet<double>();
            var count = SeamlessLoadingIndicator.GetStreakLaps(index);
            for (var lap = 0; lap < count; lap++)
            {
                var current = SeamlessLoadingIndicator.GetStreakLaunch(index, lap);
                var next = SeamlessLoadingIndicator.GetStreakLaunch(index, (lap + 1) % count);
                var interval = next.Start - current.Start + (lap == count - 1 ? 1 : 0);
                intervals.Add(interval);
                await Assert.That(interval > current.Duration).IsTrue();
                var pause = current.Start + (current.Duration + interval) / 2;
                await Assert.That(SeamlessLoadingIndicator.GetFlareState(pause, index).Opacity).IsEqualTo(0d);
            }
            await Assert.That(intervals.Count).IsEqualTo(count);
        }
    }

    [Test]
    public async Task ShouldAnimate_RequiresAttachedVisibleNonEmptyBounds()
    {
        await Assert.That(SeamlessLoadingIndicator.ShouldAnimate(true, true, new Size(160, 72)))
            .IsTrue();
        await Assert.That(SeamlessLoadingIndicator.ShouldAnimate(false, true, new Size(160, 72)))
            .IsFalse();
        await Assert.That(SeamlessLoadingIndicator.ShouldAnimate(true, false, new Size(160, 72)))
            .IsFalse();
        await Assert.That(SeamlessLoadingIndicator.ShouldAnimate(true, true, default))
            .IsFalse();
    }

}
