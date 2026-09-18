using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;

namespace Unlimotion.Cli;

internal sealed record ApplicationRequestInput
{
    public int SchemaVersion { get; init; }
    public string ApplicationId { get; init; } = string.Empty;
    public IReadOnlyList<ApplicationProposalInput> ProposalRefs { get; init; } = Array.Empty<ApplicationProposalInput>();
    public string Author { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<ApplicationPreconditionInput> Preconditions { get; init; } = Array.Empty<ApplicationPreconditionInput>();
    public IReadOnlyList<ApplicationOperationInput> Operations { get; init; } = Array.Empty<ApplicationOperationInput>();

    public TaskApplicationRequest ToDomain()
    {
        return new TaskApplicationRequest
        {
            SchemaVersion = SchemaVersion,
            ApplicationId = ApplicationId,
            ProposalRefs = ProposalRefs.Select(item => new TaskApplicationProposalReference(item.Id, item.Revision)).ToArray(),
            Author = Author,
            Reason = Reason,
            Preconditions = Preconditions.Select(item => new TaskApplicationPrecondition(item.TaskId, item.Etag, ParseStatus(item.Status, allowNull: true))).ToArray(),
            Operations = Operations.Select(ToDomainOperation).ToArray()
        };
    }

    private static TaskApplicationOperation ToDomainOperation(ApplicationOperationInput item) => new()
    {
        OperationId = item.OperationId,
        Kind = item.Kind?.ToLowerInvariant() switch
        {
            "setfield" => TaskApplicationOperationKind.SetField,
            "clearfield" => TaskApplicationOperationKind.ClearField,
            "addcriterion" => TaskApplicationOperationKind.AddCriterion,
            "replacecriterion" => TaskApplicationOperationKind.ReplaceCriterion,
            "removecriterion" => TaskApplicationOperationKind.RemoveCriterion,
            "setcriterionsatisfied" => TaskApplicationOperationKind.SetCriterionSatisfied,
            "addrelation" => TaskApplicationOperationKind.AddRelation,
            "removerelation" => TaskApplicationOperationKind.RemoveRelation,
            "createtask" => TaskApplicationOperationKind.CreateTask,
            "setstatus" => TaskApplicationOperationKind.SetStatus,
            _ => throw new CliException($"Unsupported application operation kind '{item.Kind}'.")
        },
        TaskId = item.TaskId,
        Field = item.Field,
        Value = item.Value,
        CriterionId = item.CriterionId,
        Text = item.Text,
        IsSatisfied = item.IsSatisfied,
        Relation = item.Relation,
        FromTaskId = item.FromTaskId,
        ToTaskId = item.ToTaskId,
        NewTaskId = item.NewTaskId ?? item.TaskId,
        Title = item.Title,
        DescriptionUserText = item.DescriptionUserText,
        PlannedDuration = ParseDuration(item.PlannedDuration),
        PlannedBeginDateTime = ParseDate(item.PlannedBeginDateTime),
        PlannedEndDateTime = ParseDate(item.PlannedEndDateTime),
        ParentIds = item.ParentIds,
        Criteria = (item.Criteria ?? Array.Empty<ApplicationCriterionInput>()).Select(criterion => new TaskApplicationCriterion(criterion.CriterionId, criterion.Text, criterion.IsSatisfied)).ToArray(),
        Status = ParseStatus(item.Status, allowNull: true),
        Justification = item.Justification,
        EvidenceLinks = item.EvidenceLinks
    };

    private static DomainTaskStatus? ParseStatus(string? value, bool allowNull)
    {
        if (string.IsNullOrWhiteSpace(value) && allowNull) return null;
        if (Enum.TryParse<DomainTaskStatus>(value, ignoreCase: true, out var status)) return status;
        throw new CliException($"Application status '{value}' is invalid.");
    }

    private static TimeSpan? ParseDuration(string? value)
    {
        if (value == null) return null;
        try { return System.Xml.XmlConvert.ToTimeSpan(value); }
        catch (FormatException) { throw new CliException($"Application duration '{value}' is invalid."); }
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (value == null) return null;
        if (DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var date)) return date;
        throw new CliException($"Application date '{value}' is invalid.");
    }
}

internal sealed record ApplicationProposalInput { public string Id { get; init; } = string.Empty; public int Revision { get; init; } }
internal sealed record ApplicationPreconditionInput { public string TaskId { get; init; } = string.Empty; public string Etag { get; init; } = string.Empty; public string? Status { get; init; } }
internal sealed record ApplicationCriterionInput { public string CriterionId { get; init; } = string.Empty; public string Text { get; init; } = string.Empty; public bool IsSatisfied { get; init; } }
internal sealed record ApplicationOperationInput
{
    public string OperationId { get; init; } = string.Empty;
    public string? Kind { get; init; }
    public string? TaskId { get; init; }
    public string? Field { get; init; }
    public string? Value { get; init; }
    public string? CriterionId { get; init; }
    public string? Text { get; init; }
    public bool? IsSatisfied { get; init; }
    public string? Relation { get; init; }
    public string? FromTaskId { get; init; }
    public string? ToTaskId { get; init; }
    public string? NewTaskId { get; init; }
    public string? Title { get; init; }
    public string? DescriptionUserText { get; init; }
    public string? PlannedDuration { get; init; }
    public string? PlannedBeginDateTime { get; init; }
    public string? PlannedEndDateTime { get; init; }
    public IReadOnlyList<string>? ParentIds { get; init; }
    public IReadOnlyList<ApplicationCriterionInput>? Criteria { get; init; }
    public string? Status { get; init; }
    public string? Justification { get; init; }
    public IReadOnlyList<string>? EvidenceLinks { get; init; }
}

internal static class TaskEtag
{
    public static string Create(TaskItem task)
    {
        var token = JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(TaskItemSnapshot.Clone(task)));
        var canonical = Canonicalize(token);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Canonicalize(JToken token) => token switch
    {
        JObject obj => "{" + string.Join(",", obj.Properties().OrderBy(static property => property.Name, StringComparer.Ordinal).Select(property => JsonSerializer.Serialize(property.Name) + ":" + Canonicalize(property.Value))) + "}",
        JArray array => "[" + string.Join(",", array.Select(Canonicalize)) + "]",
        _ => token.ToString(Newtonsoft.Json.Formatting.None)
    };
}
