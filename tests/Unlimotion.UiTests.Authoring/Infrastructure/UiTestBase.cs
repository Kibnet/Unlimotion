using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.TUnit;

public interface IUiTestSession : IDisposable
{
}

public static class UiAssert
{
    private static readonly UiWaitOptions DefaultWaitOptions = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
        PollInterval = TimeSpan.FromMilliseconds(100)
    };

    public static async Task TextEqualsAsync(
        Func<string> actualFactory,
        string expected,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var actual = await UiWait.UntilAsync(
            actualFactory,
            value => string.Equals(value, expected, StringComparison.Ordinal),
            ResolveOptions(timeout),
            $"Text did not become '{expected}'.",
            cancellationToken);

        await Assert.That(actual).IsEqualTo(expected);
    }

    public static async Task TextContainsAsync(
        Func<string> actualFactory,
        string expectedPart,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var actual = await UiWait.UntilAsync(
            actualFactory,
            value => value.Contains(expectedPart, StringComparison.Ordinal),
            ResolveOptions(timeout),
            $"Text did not contain '{expectedPart}'.",
            cancellationToken);

        await Assert.That(actual.Contains(expectedPart, StringComparison.Ordinal)).IsTrue();
    }

    public static async Task NumberAtLeastAsync(
        Func<int> actualFactory,
        int expectedMin,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var actual = await UiWait.UntilAsync(
            actualFactory,
            value => value >= expectedMin,
            ResolveOptions(timeout),
            $"Number did not reach {expectedMin}.",
            cancellationToken);

        await Assert.That(actual).IsGreaterThanOrEqualTo(expectedMin);
    }

    private static UiWaitOptions ResolveOptions(TimeSpan? timeout)
    {
        return timeout.HasValue
            ? DefaultWaitOptions with { Timeout = timeout.Value }
            : DefaultWaitOptions;
    }
}

public abstract class UiTestBase<TSession, TPage>
    where TSession : class, IUiTestSession
    where TPage : class
{
    protected const string DesktopUiConstraint = "DesktopUi";

    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private TSession? session;
    private TPage? page;

    protected TSession Session =>
        session ?? throw new InvalidOperationException("UI test session is not initialized.");

    protected TPage Page =>
        page ?? throw new InvalidOperationException("Page is not initialized.");

    protected abstract TSession LaunchSession();

    protected abstract TPage CreatePage(TSession session);

    protected static void WaitUntil(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        WaitUntil(
            condition,
            success => success,
            timeout,
            pollInterval,
            timeoutMessage,
            cancellationToken);
    }

    protected static T WaitUntil<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        return UiWait.Until(
            valueFactory,
            condition,
            CreateWaitOptions(timeout, pollInterval),
            timeoutMessage,
            cancellationToken);
    }

    protected static Task<T> WaitUntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        return UiWait.UntilAsync(
            valueFactory,
            condition,
            CreateWaitOptions(timeout, pollInterval),
            timeoutMessage,
            cancellationToken);
    }

    protected static void RetryUntil(
        Func<bool> attempt,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        WaitUntil(
            attempt,
            success => success,
            timeout,
            pollInterval,
            timeoutMessage,
            cancellationToken);
    }

    [Before(Test)]
    public void SetupUiSession()
    {
        try
        {
            session = LaunchSession();
            page = CreatePage(session);
        }
        catch
        {
            session?.Dispose();
            session = null;
            page = null;
            throw;
        }
    }

    [After(Test)]
    public void CleanupUiSession()
    {
        session?.Dispose();
        session = null;
        page = null;
    }

    private static UiWaitOptions CreateWaitOptions(TimeSpan? timeout, TimeSpan? pollInterval)
    {
        return new UiWaitOptions
        {
            Timeout = timeout ?? DefaultWaitTimeout,
            PollInterval = pollInterval ?? DefaultPollInterval
        };
    }
}
