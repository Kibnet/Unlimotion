using Unlimotion.Notes.Markdown;

namespace Unlimotion.Notes.Review;

public sealed record BlockLocator(
    string RelativePath,
    string? AreaIdentity,
    MarkdownBlockKind BlockKind,
    string ContentHash,
    int Occurrence,
    string? PreviousContentHash = null,
    string? NextContentHash = null)
{
    public string SemanticKey => string.Join(
        '|',
        RelativePath.Replace('\\', '/'),
        AreaIdentity,
        BlockKind,
        ContentHash,
        Occurrence,
        PreviousContentHash,
        NextContentHash);
}

public enum ReviewDecision
{
    BaselineKept,
    Kept,
    Deferred,
    Converted,
    Moved
}

public enum ReviewSessionEventKind
{
    Opened,
    TakenOver,
    Closed,
    Abandoned
}

public sealed record CausalEnvelope(
    string DeviceId,
    long DeviceSequence,
    IReadOnlyDictionary<string, long> Observed)
{
    public bool Observes(CausalEnvelope other)
    {
        if (string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal) && DeviceSequence >= other.DeviceSequence)
        {
            return true;
        }

        return Observed.TryGetValue(other.DeviceId, out var sequence) && sequence >= other.DeviceSequence;
    }
}

public sealed record ReviewDecisionEvent(
    string VaultId,
    string EventId,
    CausalEnvelope Causality,
    DateTimeOffset DisplayTimestamp,
    BlockLocator Input,
    ReviewDecision Decision,
    string? ReviewSessionId = null,
    IReadOnlyList<BlockLocator>? Outputs = null,
    string? OperationId = null,
    string? ResultEntityId = null);

public sealed record ReviewSessionEvent(
    string VaultId,
    string EventId,
    string ReviewSessionId,
    ReviewSessionEventKind Kind,
    CausalEnvelope Causality,
    DateTimeOffset DisplayTimestamp);

public sealed record EffectiveReviewDecision(
    ReviewDecisionEvent? Event,
    bool HasConflict,
    IReadOnlyList<ReviewDecisionEvent> ConflictingEvents)
{
    public bool IsTerminal => !HasConflict && Event?.Decision is ReviewDecision.BaselineKept
        or ReviewDecision.Kept
        or ReviewDecision.Converted
        or ReviewDecision.Moved;
}

public sealed class ReviewStateStore
{
    private readonly Dictionary<string, ReviewDecisionEvent> eventsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReviewSessionEvent> sessionEventsById = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ReviewDecisionEvent> DecisionEvents => eventsById.Values;

    public IReadOnlyCollection<ReviewSessionEvent> SessionEvents => sessionEventsById.Values;

    public bool Add(ReviewDecisionEvent reviewEvent)
    {
        ArgumentNullException.ThrowIfNull(reviewEvent);
        var sameEventId = eventsById.Values.FirstOrDefault(existing =>
            string.Equals(existing.EventId, reviewEvent.EventId, StringComparison.Ordinal));
        if (sameEventId is not null)
        {
            if (HasSameOutcome(sameEventId, reviewEvent)
                && HasSameCausality(sameEventId.Causality, reviewEvent.Causality))
            {
                return false;
            }

            eventsById.Add(CreateConflictKey(reviewEvent.EventId), reviewEvent);
            return true;
        }

        var sameOperation = !string.IsNullOrWhiteSpace(reviewEvent.OperationId)
            ? eventsById.Values.FirstOrDefault(existing =>
                string.Equals(existing.OperationId, reviewEvent.OperationId, StringComparison.Ordinal)
                && existing.Input.SemanticKey == reviewEvent.Input.SemanticKey
                && existing.Decision == reviewEvent.Decision)
            : null;
        if (sameOperation is not null)
        {
            if (HasSameOutcome(sameOperation, reviewEvent))
            {
                return false;
            }

            eventsById.Add(CreateConflictKey(reviewEvent.EventId), reviewEvent);
            return true;
        }

        eventsById.Add(reviewEvent.EventId, reviewEvent);
        return true;
    }

    public bool Add(ReviewSessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        return sessionEventsById.TryAdd(sessionEvent.EventId, sessionEvent);
    }

    public void ReplaceBootstrapBaseline(IEnumerable<ReviewDecisionEvent> baselineEvents)
    {
        ArgumentNullException.ThrowIfNull(baselineEvents);
        foreach (var key in eventsById
                     .Where(static pair => pair.Value.Decision == ReviewDecision.BaselineKept)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            eventsById.Remove(key);
        }

        foreach (var reviewEvent in baselineEvents)
        {
            if (reviewEvent.Decision != ReviewDecision.BaselineKept)
            {
                throw new ArgumentException("Only bootstrap baseline events can replace the baseline.", nameof(baselineEvents));
            }

            Add(reviewEvent);
        }
    }

    public EffectiveReviewDecision Resolve(BlockLocator locator)
    {
        var matches = eventsById.Values
            .Where(candidate => candidate.Input.SemanticKey == locator.SemanticKey)
            .ToArray();
        return ResolveMatches(matches);
    }

    public EffectiveReviewDecision Resolve(
        BlockLocator locator,
        IReadOnlyList<BlockLocator> currentLocators)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(currentLocators);
        var exact = eventsById.Values
            .Where(candidate => candidate.Input.SemanticKey == locator.SemanticKey)
            .ToArray();
        if (exact.Length > 0)
        {
            return ResolveMatches(exact);
        }

        var historicalLocators = eventsById.Values
            .Select(static candidate => candidate.Input)
            .DistinctBy(static candidate => candidate.SemanticKey)
            .Where(candidate => CanRematch(candidate, locator))
            .ToArray();
        if (historicalLocators.Length != 1)
        {
            return EmptyDecision();
        }

        var historical = historicalLocators[0];
        var competingCurrent = currentLocators
            .Where(candidate => CanRematch(historical, candidate))
            .DistinctBy(static candidate => candidate.SemanticKey)
            .ToArray();
        if (competingCurrent.Length != 1
            || competingCurrent[0].SemanticKey != locator.SemanticKey)
        {
            return EmptyDecision();
        }

        return ResolveMatches(eventsById.Values
            .Where(candidate => candidate.Input.SemanticKey == historical.SemanticKey)
            .ToArray());
    }

    public bool SessionIsTerminal(string sessionId)
    {
        var ownershipEvents = sessionEventsById.Values
            .Where(candidate => string.Equals(candidate.ReviewSessionId, sessionId, StringComparison.Ordinal))
            .Where(static candidate => candidate.Kind is ReviewSessionEventKind.Opened or ReviewSessionEventKind.TakenOver)
            .ToArray();
        var effectiveOwners = EffectiveOwners(ownershipEvents);
        return sessionEventsById.Values.Any(candidate =>
            string.Equals(candidate.ReviewSessionId, sessionId, StringComparison.Ordinal)
            && candidate.Kind is ReviewSessionEventKind.Closed or ReviewSessionEventKind.Abandoned
            && effectiveOwners.Count > 0
            && effectiveOwners.All(owner => candidate.Causality.Observes(owner.Causality)));
    }

    public IReadOnlyList<ReviewSessionEvent> EffectiveSessionOwners(string sessionId)
    {
        var ownershipEvents = sessionEventsById.Values
            .Where(candidate => string.Equals(candidate.ReviewSessionId, sessionId, StringComparison.Ordinal))
            .Where(static candidate => candidate.Kind is ReviewSessionEventKind.Opened or ReviewSessionEventKind.TakenOver)
            .ToArray();
        return EffectiveOwners(ownershipEvents);
    }

    private static EffectiveReviewDecision ResolveMatches(IReadOnlyList<ReviewDecisionEvent> matches)
    {
        if (matches.Count == 0)
        {
            return EmptyDecision();
        }

        var explicitEvents = matches.Where(static candidate => candidate.Decision != ReviewDecision.BaselineKept).ToArray();
        var candidates = explicitEvents.Length > 0 ? explicitEvents : matches;
        var maximal = candidates
            .Where(candidate => !candidates.Any(other => !ReferenceEquals(candidate, other)
                && other.Causality.Observes(candidate.Causality)
                && !candidate.Causality.Observes(other.Causality)))
            .ToArray();
        if (maximal.Length == 1)
        {
            return new EffectiveReviewDecision(maximal[0], false, []);
        }

        var sameOutcome = maximal.All(candidate => HasSameOutcome(candidate, maximal[0]));
        return sameOutcome
            ? new EffectiveReviewDecision(maximal.OrderByDescending(static value => value.Causality.DeviceSequence).First(), false, [])
            : new EffectiveReviewDecision(null, true, maximal);
    }

    public bool SessionIsClosedBefore(string sessionId, CausalEnvelope observer)
    {
        var effectiveOwners = EffectiveSessionOwners(sessionId);
        return sessionEventsById.Values.Any(candidate =>
            string.Equals(candidate.ReviewSessionId, sessionId, StringComparison.Ordinal)
            && candidate.Kind is ReviewSessionEventKind.Closed or ReviewSessionEventKind.Abandoned
            && effectiveOwners.Count > 0
            && effectiveOwners.All(owner => candidate.Causality.Observes(owner.Causality))
            && observer.Observes(candidate.Causality));
    }

    private static IReadOnlyList<ReviewSessionEvent> EffectiveOwners(
        IReadOnlyList<ReviewSessionEvent> ownershipEvents) => ownershipEvents
        .Where(candidate => !ownershipEvents.Any(other =>
            !ReferenceEquals(candidate, other)
            && other.Causality.Observes(candidate.Causality)
            && !candidate.Causality.Observes(other.Causality)))
        .ToArray();

    private static bool CanRematch(BlockLocator historical, BlockLocator current)
    {
        if (!string.Equals(historical.AreaIdentity, current.AreaIdentity, StringComparison.Ordinal)
            || historical.BlockKind != current.BlockKind
            || !string.Equals(historical.ContentHash, current.ContentHash, StringComparison.Ordinal)
            || historical.Occurrence != current.Occurrence)
        {
            return false;
        }

        var samePath = string.Equals(
            NormalizePath(historical.RelativePath),
            NormalizePath(current.RelativePath),
            StringComparison.Ordinal);
        var matchingContext = 0;
        if (historical.PreviousContentHash is not null
            && string.Equals(historical.PreviousContentHash, current.PreviousContentHash, StringComparison.Ordinal))
        {
            matchingContext++;
        }

        if (historical.NextContentHash is not null
            && string.Equals(historical.NextContentHash, current.NextContentHash, StringComparison.Ordinal))
        {
            matchingContext++;
        }

        if (matchingContext > 0)
        {
            return true;
        }

        return !samePath
            && historical.PreviousContentHash is null
            && historical.NextContentHash is null
            && current.PreviousContentHash is null
            && current.NextContentHash is null;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static EffectiveReviewDecision EmptyDecision() => new(null, false, []);

    private string CreateConflictKey(string eventId)
    {
        var suffix = 1;
        string key;
        do
        {
            key = $"{eventId}#conflict-{suffix++}";
        }
        while (eventsById.ContainsKey(key));

        return key;
    }

    private static bool HasSameOutcome(ReviewDecisionEvent left, ReviewDecisionEvent right) =>
        left.Decision == right.Decision
        && string.Equals(left.OperationId, right.OperationId, StringComparison.Ordinal)
        && string.Equals(left.ResultEntityId, right.ResultEntityId, StringComparison.Ordinal)
        && string.Equals(left.ReviewSessionId, right.ReviewSessionId, StringComparison.Ordinal)
        && LocatorSequenceEqual(left.Outputs, right.Outputs);

    private static bool HasSameCausality(CausalEnvelope left, CausalEnvelope right) =>
        string.Equals(left.DeviceId, right.DeviceId, StringComparison.Ordinal)
        && left.DeviceSequence == right.DeviceSequence
        && left.Observed.Count == right.Observed.Count
        && left.Observed.All(pair => right.Observed.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static bool LocatorSequenceEqual(
        IReadOnlyList<BlockLocator>? left,
        IReadOnlyList<BlockLocator>? right)
    {
        var leftKeys = (left ?? []).Select(static value => value.SemanticKey)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var rightKeys = (right ?? []).Select(static value => value.SemanticKey)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
    }
}
