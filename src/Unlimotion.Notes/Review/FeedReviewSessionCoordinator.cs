namespace Unlimotion.Notes.Review;

public sealed record ForeignReviewSession(
    string ReviewSessionId,
    string OwnerDeviceId);

public sealed class ForeignReviewSessionRequiresResolutionException(
    IReadOnlyList<ForeignReviewSession> sessions)
    : InvalidOperationException("Another device has an unfinished review session. Continue or abandon it before starting a new review.")
{
    public IReadOnlyList<ForeignReviewSession> Sessions { get; } = sessions;
}

public sealed class ReviewSessionOwnershipConflictException(string sessionId)
    : InvalidOperationException($"Review session '{sessionId}' is now owned by another device. Synchronize review state before continuing.")
{
    public string SessionId { get; } = sessionId;
}

public sealed class FeedReviewSessionCoordinator(
    string vaultId,
    string deviceId,
    PortableReviewEventStore eventStore,
    ReviewStateStore state,
    Func<DateTimeOffset>? now = null)
{
    private readonly Dictionary<string, long> observed = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> nowProvider = now ?? (() => DateTimeOffset.UtcNow);
    private long deviceSequence;

    public string? CurrentSessionId { get; private set; }

    public ReviewStateStore State => state;

    public CausalEnvelope CurrentObserver => new(
        deviceId,
        Math.Max(1, deviceSequence),
        new Dictionary<string, long>(observed, StringComparer.Ordinal));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await eventStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var decision in loaded.Decisions)
        {
            EnsureVault(decision.VaultId);
            state.Add(decision);
            Observe(decision.Causality);
        }

        foreach (var sessionEvent in loaded.Sessions)
        {
            EnsureVault(sessionEvent.VaultId);
            state.Add(sessionEvent);
            Observe(sessionEvent.Causality);
        }

        deviceSequence = Math.Max(
            deviceSequence,
            loaded.Decisions.Select(static value => value.Causality)
                .Concat(loaded.Sessions.Select(static value => value.Causality))
                .Where(value => string.Equals(value.DeviceId, deviceId, StringComparison.Ordinal))
                .Select(static value => value.DeviceSequence)
                .DefaultIfEmpty(0)
                .Max());
    }

    public async Task<string> OpenOrResumeAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentSessionId is not null)
        {
            if (!IsSessionTerminal(CurrentSessionId) && IsOwnedByCurrentDevice(CurrentSessionId))
            {
                return CurrentSessionId;
            }

            // A synchronized takeover/abandon/close invalidates the local in-memory pointer.
            // Clearing it here lets the caller surface the effective foreign owner instead of
            // reopening a stale session and failing only after the next user decision.
            CurrentSessionId = null;
        }

        var localOpen = state.SessionEvents
            .Where(value => value.Kind is ReviewSessionEventKind.Opened or ReviewSessionEventKind.TakenOver)
            .Where(value => string.Equals(value.Causality.DeviceId, deviceId, StringComparison.Ordinal))
            .OrderByDescending(static value => value.Causality.DeviceSequence)
            .FirstOrDefault(value =>
                !IsSessionTerminal(value.ReviewSessionId)
                && IsOwnedByCurrentDevice(value.ReviewSessionId));
        if (localOpen is not null)
        {
            CurrentSessionId = localOpen.ReviewSessionId;
            return CurrentSessionId;
        }

        var foreignOpen = GetForeignOpenSessions();
        if (foreignOpen.Count > 0)
        {
            throw new ForeignReviewSessionRequiresResolutionException(foreignOpen);
        }

        CurrentSessionId = "review-" + Guid.NewGuid().ToString("N");
        await AppendSessionEventAsync(CurrentSessionId, ReviewSessionEventKind.Opened, cancellationToken)
            .ConfigureAwait(false);
        return CurrentSessionId;
    }

    public async Task TakeOverAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        if (IsSessionTerminal(sessionId))
        {
            throw new InvalidOperationException("A completed review session cannot be taken over.");
        }

        CurrentSessionId = sessionId;
        await AppendSessionEventAsync(sessionId, ReviewSessionEventKind.TakenOver, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task AbandonAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        if (string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            CurrentSessionId = null;
        }

        return AppendSessionEventAsync(sessionId, ReviewSessionEventKind.Abandoned, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentSessionId is null)
        {
            return;
        }

        var sessionId = CurrentSessionId;
        EnsureCurrentSessionOwnership();
        await AppendSessionEventAsync(sessionId, ReviewSessionEventKind.Closed, cancellationToken)
            .ConfigureAwait(false);
        CurrentSessionId = null;
    }

    public async Task ApplyDecisionAsync(
        IReadOnlyList<BlockLocator> inputs,
        ReviewDecision decision,
        IReadOnlyList<BlockLocator>? outputs = null,
        string? operationId = null,
        string? resultEntityId = null,
        CancellationToken cancellationToken = default)
    {
        if (CurrentSessionId is null)
        {
            throw new InvalidOperationException("A review session must be open before applying a decision.");
        }


        EnsureCurrentSessionOwnership();

        if (inputs.Count == 0)
        {
            throw new ArgumentException("A review decision requires at least one input locator.", nameof(inputs));
        }

        var outputList = outputs?.DistinctBy(static value => value.SemanticKey).ToArray() ?? [];
        foreach (var input in inputs.DistinctBy(static value => value.SemanticKey))
        {
            await AppendDecisionAsync(
                    input,
                    decision,
                    outputList,
                    operationId,
                    resultEntityId,
                    CurrentSessionId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (decision is ReviewDecision.Converted or ReviewDecision.Moved)
        {
            foreach (var output in outputList)
            {
                await AppendDecisionAsync(
                        output,
                        decision,
                        [],
                        operationId,
                        resultEntityId,
                        CurrentSessionId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Replays a durable operation's causal review intent after a crash. Unlike an interactive
    /// decision this does not claim ownership of, reopen, or take over the original session.
    /// The operation ID makes every locator event idempotent, while the newly appended causal
    /// envelope observes any synchronized terminal session event.
    /// </summary>
    public async Task ApplyRecoveredDecisionAsync(
        string reviewSessionId,
        IReadOnlyList<BlockLocator> inputs,
        ReviewDecision decision,
        IReadOnlyList<BlockLocator>? outputs,
        string operationId,
        string? resultEntityId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(reviewSessionId);
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("A recovered review decision requires its durable operation ID.", nameof(operationId));
        }

        if (inputs.Count == 0)
        {
            throw new ArgumentException("A recovered review decision requires at least one input locator.", nameof(inputs));
        }

        var outputList = outputs?.DistinctBy(static value => value.SemanticKey).ToArray() ?? [];
        foreach (var input in inputs.DistinctBy(static value => value.SemanticKey))
        {
            await AppendDecisionAsync(
                    input,
                    decision,
                    outputList,
                    operationId,
                    resultEntityId,
                    reviewSessionId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (decision is ReviewDecision.Converted or ReviewDecision.Moved)
        {
            foreach (var output in outputList)
            {
                await AppendDecisionAsync(
                        output,
                        decision,
                        [],
                        operationId,
                        resultEntityId,
                        reviewSessionId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Makes identity-conflict locators explicitly pending without tying them to a live session.
    /// A later terminal review decision causally supersedes this durable marker.
    /// </summary>
    public async Task MarkSafePendingAsync(
        IReadOnlyList<BlockLocator> locators,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("A safe-pending marker requires a durable operation ID.", nameof(operationId));
        }

        foreach (var locator in locators.DistinctBy(static value => value.SemanticKey))
        {
            await AppendDecisionAsync(
                    locator,
                    ReviewDecision.Deferred,
                    [],
                    operationId,
                    resultEntityId: null,
                    reviewSessionId: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task AppendDecisionAsync(
        BlockLocator input,
        ReviewDecision decision,
        IReadOnlyList<BlockLocator> outputs,
        string? operationId,
        string? resultEntityId,
        string? reviewSessionId,
        CancellationToken cancellationToken)
    {
        var duplicate = state.DecisionEvents.Any(existing =>
            existing.Input.SemanticKey == input.SemanticKey
            && existing.Decision == decision
            && (operationId is not null
                ? string.Equals(existing.OperationId, operationId, StringComparison.Ordinal)
                : string.Equals(existing.ReviewSessionId, reviewSessionId, StringComparison.Ordinal)));
        if (duplicate)
        {
            return;
        }

        var envelope = NextEnvelope();
        var reviewEvent = new ReviewDecisionEvent(
            vaultId,
            CreateEventId(envelope.DeviceSequence),
            envelope,
            nowProvider(),
            input,
            decision,
            reviewSessionId,
            outputs,
            operationId,
            resultEntityId);
        await eventStore.AppendAsync(reviewEvent, cancellationToken).ConfigureAwait(false);
        state.Add(reviewEvent);
        Observe(envelope);
    }

    private async Task AppendSessionEventAsync(
        string sessionId,
        ReviewSessionEventKind kind,
        CancellationToken cancellationToken)
    {
        var envelope = NextEnvelope();
        var sessionEvent = new ReviewSessionEvent(
            vaultId,
            CreateEventId(envelope.DeviceSequence),
            sessionId,
            kind,
            envelope,
            nowProvider());
        await eventStore.AppendAsync(sessionEvent, cancellationToken).ConfigureAwait(false);
        state.Add(sessionEvent);
        Observe(envelope);
    }

    private CausalEnvelope NextEnvelope()
    {
        deviceSequence++;
        return new CausalEnvelope(
            deviceId,
            deviceSequence,
            new Dictionary<string, long>(observed, StringComparer.Ordinal));
    }

    private void Observe(CausalEnvelope envelope)
    {
        observed[envelope.DeviceId] = Math.Max(
            observed.GetValueOrDefault(envelope.DeviceId),
            envelope.DeviceSequence);
        foreach (var (seenDevice, sequence) in envelope.Observed)
        {
            observed[seenDevice] = Math.Max(observed.GetValueOrDefault(seenDevice), sequence);
        }
    }

    private bool IsSessionTerminal(string sessionId) => state.SessionIsTerminal(sessionId);

    public IReadOnlyList<ForeignReviewSession> GetForeignOpenSessions()
    {
        return state.SessionEvents
            .Where(static value => value.Kind is ReviewSessionEventKind.Opened or ReviewSessionEventKind.TakenOver)
            .GroupBy(static value => value.ReviewSessionId, StringComparer.Ordinal)
            .Where(group => !IsSessionTerminal(group.Key))
            .Select(group => state.EffectiveSessionOwners(group.Key))
            .Where(static owners => owners.Count > 0)
            .SelectMany(static owners => owners)
            .Where(owner => !string.Equals(owner.Causality.DeviceId, deviceId, StringComparison.Ordinal))
            .Select(static owner => new ForeignReviewSession(owner.ReviewSessionId, owner.Causality.DeviceId))
            .DistinctBy(static value => value.ReviewSessionId)
            .OrderBy(static value => value.ReviewSessionId, StringComparer.Ordinal)
            .ToArray();
    }

    private void EnsureCurrentSessionOwnership()
    {
        var sessionId = CurrentSessionId
            ?? throw new InvalidOperationException("A review session must be open before applying a decision.");
        if (IsSessionTerminal(sessionId))
        {
            throw new ReviewSessionOwnershipConflictException(sessionId);
        }

        var effectiveOwners = state.EffectiveSessionOwners(sessionId);
        if (effectiveOwners.Count != 1
            || !string.Equals(effectiveOwners[0].Causality.DeviceId, deviceId, StringComparison.Ordinal))
        {
            throw new ReviewSessionOwnershipConflictException(sessionId);
        }
    }

    private bool IsOwnedByCurrentDevice(string sessionId)
    {
        var effectiveOwners = state.EffectiveSessionOwners(sessionId);
        return effectiveOwners.Count == 1
            && string.Equals(effectiveOwners[0].Causality.DeviceId, deviceId, StringComparison.Ordinal);
    }

    private void EnsureVault(string candidateVaultId)
    {
        if (!string.Equals(candidateVaultId, vaultId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Portable review state belongs to another vault identity.");
        }
    }

    private string CreateEventId(long sequence) => $"{deviceId}-{sequence:D20}";

    private static void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)
            || sessionId.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Review session IDs must be safe portable identifiers.", nameof(sessionId));
        }
    }
}
