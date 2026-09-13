using System.Text;
using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public static class AgentExecutionDescriptionRenderer
{
    public const string MarkerStart = "<!-- unlimotion-agent-execution:v1:start -->";
    public const string MarkerEnd = "<!-- unlimotion-agent-execution:v1:end -->";

    public static bool ContainsReservedMarker(string value) =>
        value.Contains(MarkerStart, StringComparison.Ordinal) ||
        value.Contains(MarkerEnd, StringComparison.Ordinal);

    public static AgentExecutionMarkerState Inspect(string? description, out string? error)
    {
        var text = description ?? string.Empty;
        if (!TryLocateSingleBlock(text, out var start, out _, out error))
        {
            return AgentExecutionMarkerState.Invalid;
        }

        return start < 0 ? AgentExecutionMarkerState.None : AgentExecutionMarkerState.Single;
    }

    public static bool TryRender(
        string? description,
        AgentExecutionRecord execution,
        out string rendered,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var text = description ?? string.Empty;
        if (!TryLocateSingleBlock(text, out var start, out var endAfter, out error))
        {
            rendered = text;
            return false;
        }

        var block = RenderBlock(execution);
        if (start < 0)
        {
            rendered = text.Length == 0
                ? block
                : text + (text.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n") + block;
            return true;
        }

        rendered = text[..start] + block + text[endAfter..];
        return true;
    }

    public static bool TryRemove(
        string? description,
        out string rendered,
        out string? error)
    {
        var text = description ?? string.Empty;
        if (!TryLocateSingleBlock(text, out var start, out var endAfter, out error))
        {
            rendered = text;
            return false;
        }

        if (start < 0)
        {
            rendered = text;
            return true;
        }

        rendered = text[..start] + text[endAfter..];
        return true;
    }

    private static bool TryLocateSingleBlock(
        string text,
        out int start,
        out int endAfter,
        out string? error)
    {
        start = text.IndexOf(MarkerStart, StringComparison.Ordinal);
        var firstEnd = text.IndexOf(MarkerEnd, StringComparison.Ordinal);
        if (start < 0 && firstEnd < 0)
        {
            endAfter = -1;
            error = null;
            return true;
        }

        if (start < 0 || firstEnd < start)
        {
            endAfter = -1;
            error = "Description contains an unpaired agent execution marker.";
            return false;
        }

        endAfter = firstEnd + MarkerEnd.Length;
        if (text.IndexOf(MarkerStart, start + MarkerStart.Length, StringComparison.Ordinal) >= 0 ||
            text.IndexOf(MarkerEnd, endAfter, StringComparison.Ordinal) >= 0)
        {
            error = "Description contains more than one agent execution marker block.";
            return false;
        }

        error = null;
        return true;
    }

    private static string RenderBlock(AgentExecutionRecord execution)
    {
        var builder = new StringBuilder()
            .AppendLine(MarkerStart)
            .Append("Исполнитель: ").AppendLine(execution.AgentId)
            .Append("Состояние: ").AppendLine(execution.State.ToString());

        if (execution.Questions.Count > 0)
        {
            builder.AppendLine("Вопросы:");
            foreach (var question in execution.Questions)
            {
                builder.Append("- [").Append(question.Id).Append("] ").AppendLine(question.Text);
                if (question.Answer != null)
                {
                    builder.Append("  Ответ: ").AppendLine(question.Answer);
                }
            }
        }

        if (execution.Result != null)
        {
            builder.Append("Результат: ").AppendLine(execution.Result.Summary);
            if (execution.Result.Links.Count > 0)
            {
                builder.AppendLine("Ссылки:");
                foreach (var link in execution.Result.Links)
                {
                    builder.Append("- ").AppendLine(link);
                }
            }
        }

        if (!string.IsNullOrEmpty(execution.ReleaseReason))
        {
            builder.Append("Причина освобождения: ").AppendLine(execution.ReleaseReason);
        }

        builder.Append(MarkerEnd);
        return builder.ToString();
    }
}

public enum AgentExecutionMarkerState
{
    None,
    Single,
    Invalid
}
