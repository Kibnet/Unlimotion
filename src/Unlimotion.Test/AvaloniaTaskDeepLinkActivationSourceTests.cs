using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;

namespace Unlimotion.Test;

public class AvaloniaTaskDeepLinkActivationSourceTests
{
    private static readonly TaskDeepLink Link = new("feed-12422d3acca249db950bccce95f0d723");

    [Test]
    public async Task ActivationBeforeSubscriber_IsDrainedExactlyOnce()
    {
        using var source = new AvaloniaTaskDeepLinkActivationSource();

        source.ProcessActivation(new ProtocolActivatedEventArgs(Link.Uri));

        await Assert.That(source.DrainPending()).IsEquivalentTo(new[] { Link });
        await Assert.That(source.DrainPending()).IsEmpty();
    }

    [Test]
    public async Task WarmActivation_IsPublishedExactlyOnce()
    {
        using var source = new AvaloniaTaskDeepLinkActivationSource();
        var count = 0;
        TaskDeepLink? received = null;
        source.ActivationRequested += (_, args) =>
        {
            count++;
            received = args.Link;
        };

        source.ProcessActivation(new ProtocolActivatedEventArgs(Link.Uri));

        await Assert.That(received).IsEqualTo(Link);
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(source.DrainPending()).IsEmpty();
    }

    [Test]
    public async Task InvalidAndNonProtocolActivations_AreIgnored()
    {
        using var source = new AvaloniaTaskDeepLinkActivationSource();
        var count = 0;
        source.ActivationRequested += (_, _) => count++;

        source.ProcessActivation(new ProtocolActivatedEventArgs(new Uri("unlimotion://task/invalid?query=true")));
        source.ProcessActivation(new ActivatedEventArgs(ActivationKind.Background));

        await Assert.That(count).IsEqualTo(0);
        await Assert.That(source.DrainPending()).IsEmpty();
    }

    [Test]
    public async Task DotSegmentNormalization_DoesNotBypassSingleTaskPathValidation()
    {
        using var source = new AvaloniaTaskDeepLinkActivationSource();
        source.ProcessActivation(new ProtocolActivatedEventArgs(new Uri("unlimotion://task/other/../target")));
        await Assert.That(source.DrainPending()).IsEmpty();
    }

    [Test]
    public async Task Dispose_IgnoresFurtherActivationProcessing()
    {
        var source = new AvaloniaTaskDeepLinkActivationSource();
        source.Dispose();

        source.ProcessActivation(new ProtocolActivatedEventArgs(Link.Uri));

        await Assert.That(source.DrainPending()).IsEmpty();
    }
}
