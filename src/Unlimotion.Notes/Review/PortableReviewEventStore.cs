using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Notes.Review;

public sealed record PortableReviewLoadResult(
    IReadOnlyList<ReviewDecisionEvent> Decisions,
    IReadOnlyList<ReviewSessionEvent> Sessions);

public sealed class PortableReviewEventStore(INoteVault vault)
{
    private const string EventsRoot = ".unlimotion/review/events";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public Task AppendAsync(ReviewDecisionEvent reviewEvent, CancellationToken cancellationToken = default) =>
        AppendCoreAsync(reviewEvent.Causality, reviewEvent.EventId, "decision", reviewEvent, cancellationToken);

    public Task AppendAsync(ReviewSessionEvent sessionEvent, CancellationToken cancellationToken = default) =>
        AppendCoreAsync(sessionEvent.Causality, sessionEvent.EventId, "session", sessionEvent, cancellationToken);

    public async Task<PortableReviewLoadResult> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var paths = await vault.ListFilesAsync(EventsRoot, "*.json", cancellationToken).ConfigureAwait(false);
        var decisions = new List<ReviewDecisionEvent>();
        var sessions = new List<ReviewSessionEvent>();
        foreach (var path in paths)
        {
            var document = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                continue;
            }
            if (path.EndsWith(".decision.json", StringComparison.Ordinal))
            {
                if (TryReadDecision(path, document.Text, out var decision))
                {
                    decisions.Add(decision!);
                }
            }
            else if (path.EndsWith(".session.json", StringComparison.Ordinal))
            {
                if (TryReadSession(path, document.Text, out var session))
                {
                    sessions.Add(session!);
                }
            }
        }

        return new PortableReviewLoadResult(decisions, sessions);
    }

    public async Task<IReadOnlyList<string>> QuarantineMismatchedVaultEventsAsync(
        string expectedVaultId,
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(expectedVaultId, nameof(expectedVaultId));
        ValidateId(quarantineId, nameof(quarantineId));
        var paths = await vault.ListFilesAsync(EventsRoot, "*.json", cancellationToken).ConfigureAwait(false);
        var quarantined = new List<string>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                continue;
            }

            var belongsToExpectedVault = path.EndsWith(".decision.json", StringComparison.Ordinal)
                ? TryReadDecision(path, document.Text, out var decision)
                    && string.Equals(decision!.VaultId, expectedVaultId, StringComparison.Ordinal)
                : path.EndsWith(".session.json", StringComparison.Ordinal)
                    ? TryReadSession(path, document.Text, out var session)
                        && string.Equals(session!.VaultId, expectedVaultId, StringComparison.Ordinal)
                    : true;
            if (belongsToExpectedVault)
            {
                continue;
            }

            var quarantinePath = CreateQuarantinePath(quarantineId, path, document.Revision);
            try
            {
                await vault.CreateAsync(
                        quarantinePath,
                        document.Text,
                        document.HasUtf8Bom,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (VaultRevisionConflictException)
            {
                var existing = await vault.ReadAsync(quarantinePath, cancellationToken).ConfigureAwait(false);
                if (existing is null
                    || !string.Equals(existing.Text, document.Text, StringComparison.Ordinal)
                    || existing.HasUtf8Bom != document.HasUtf8Bom)
                {
                    throw new InvalidDataException(
                        $"Review quarantine identity collision at '{quarantinePath}'.");
                }
            }

            await vault.DeleteAsync(path, document.Revision, cancellationToken).ConfigureAwait(false);
            quarantined.Add(path);
        }

        return quarantined;
    }

    private async Task AppendCoreAsync<T>(
        CausalEnvelope causality,
        string eventId,
        string kind,
        T value,
        CancellationToken cancellationToken)
    {
        ValidateId(causality.DeviceId, nameof(causality.DeviceId));
        ValidateId(eventId, nameof(eventId));
        if (causality.DeviceSequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(causality), "Device sequence must be positive.");
        }

        var path = $"{EventsRoot}/{causality.DeviceId}/{causality.DeviceSequence:D20}-{eventId}.{kind}.json";
        var json = JsonSerializer.Serialize(value, JsonOptions) + "\n";
        try
        {
            await vault.CreateAsync(path, json, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (VaultRevisionConflictException)
        {
            var existing = await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (existing is null || !string.Equals(existing.Text, json, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Review event identity collision at '{path}'.");
            }
        }
    }

    private static void ValidateId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Review identifiers must contain only letters, digits, dash or underscore.", parameterName);
        }
    }

    private static bool TryReadDecision(
        string path,
        string json,
        out ReviewDecisionEvent? decision)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            RequireProperties(document.RootElement, "vaultId", "eventId", "causality", "displayTimestamp", "input", "decision");
            decision = JsonSerializer.Deserialize<ReviewDecisionEvent>(json, JsonOptions)
                ?? throw new InvalidDataException($"Review decision event '{path}' is empty.");
            ValidateDecision(path, decision);
            return true;
        }
        catch (Exception exception) when (exception is JsonException
                                          or NotSupportedException
                                          or InvalidDataException
                                          or ArgumentException)
        {
            decision = null;
            return false;
        }
    }

    private static bool TryReadSession(
        string path,
        string json,
        out ReviewSessionEvent? session)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            RequireProperties(document.RootElement, "vaultId", "eventId", "reviewSessionId", "kind", "causality", "displayTimestamp");
            session = JsonSerializer.Deserialize<ReviewSessionEvent>(json, JsonOptions)
                ?? throw new InvalidDataException($"Review session event '{path}' is empty.");
            ValidateSession(path, session);
            return true;
        }
        catch (Exception exception) when (exception is JsonException
                                          or NotSupportedException
                                          or InvalidDataException
                                          or ArgumentException)
        {
            session = null;
            return false;
        }
    }

    private static void ValidateDecision(string path, ReviewDecisionEvent decision)
    {
        ValidateCommon(path, decision.VaultId, decision.EventId, decision.Causality, decision.DisplayTimestamp, "decision");
        if (!Enum.IsDefined(decision.Decision))
        {
            throw new InvalidDataException($"Review decision event '{path}' has an invalid outcome.");
        }

        ValidateLocator(decision.Input, path);
        foreach (var output in decision.Outputs ?? [])
        {
            ValidateLocator(output, path);
        }

        if (decision.ReviewSessionId is not null)
        {
            ValidateId(decision.ReviewSessionId, nameof(decision.ReviewSessionId));
        }
    }

    private static void ValidateSession(string path, ReviewSessionEvent session)
    {
        ValidateCommon(path, session.VaultId, session.EventId, session.Causality, session.DisplayTimestamp, "session");
        ValidateId(session.ReviewSessionId, nameof(session.ReviewSessionId));
        if (!Enum.IsDefined(session.Kind))
        {
            throw new InvalidDataException($"Review session event '{path}' has an invalid kind.");
        }
    }

    private static void ValidateCommon(
        string path,
        string vaultId,
        string eventId,
        CausalEnvelope causality,
        DateTimeOffset displayTimestamp,
        string kind)
    {
        ValidateId(vaultId, nameof(vaultId));
        ValidateId(eventId, nameof(eventId));
        if (causality is null || causality.Observed is null || causality.DeviceSequence <= 0)
        {
            throw new InvalidDataException($"Review {kind} event '{path}' has invalid causality.");
        }

        ValidateId(causality.DeviceId, nameof(causality.DeviceId));
        if (causality.Observed.Any(pair => !IsValidId(pair.Key) || pair.Value < 0)
            || displayTimestamp == default)
        {
            throw new InvalidDataException($"Review {kind} event '{path}' has invalid metadata.");
        }

        var expectedPath = $"{EventsRoot}/{causality.DeviceId}/{causality.DeviceSequence:D20}-{eventId}.{kind}.json";
        if (!string.Equals(path.Replace('\\', '/'), expectedPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Review {kind} event '{path}' is outside its canonical causal path.");
        }
    }

    private static void ValidateLocator(BlockLocator locator, string source)
    {
        if (locator is null
            || string.IsNullOrWhiteSpace(locator.RelativePath)
            || Path.IsPathRooted(locator.RelativePath)
            || locator.RelativePath.Replace('\\', '/').Split('/').Any(static part => part is "." or "..")
            || string.IsNullOrWhiteSpace(locator.ContentHash)
            || locator.Occurrence < 0
            || !Enum.IsDefined(locator.BlockKind))
        {
            throw new InvalidDataException($"Review event '{source}' has an invalid block locator.");
        }
    }

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object
            || names.Any(name => !element.EnumerateObject().Any(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("A review event is missing required properties.");
        }
    }

    private static string CreateQuarantinePath(string quarantineId, string sourcePath, string revision)
    {
        var identity = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath + "\n" + revision)))
            .ToLowerInvariant();
        return $".unlimotion/review/quarantine/{quarantineId}/{identity}-{Path.GetFileName(sourcePath)}";
    }

    private static bool IsValidId(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
