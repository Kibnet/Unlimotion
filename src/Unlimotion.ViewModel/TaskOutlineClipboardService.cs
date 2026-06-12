using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unlimotion.Domain;

namespace Unlimotion.ViewModel;

public sealed class TaskOutlineNode
{
    public TaskOutlineNode(string title, string? description = null, bool? isCompleted = null, TaskStatus? status = null)
    {
        Title = title;
        Description = description ?? string.Empty;
        Status = status ?? (isCompleted switch
        {
            true => TaskStatus.Completed,
            false => TaskStatus.NotReady,
            _ => null
        });
    }

    public string Title { get; }

    public string Description { get; private set; }

    public TaskStatus? Status { get; }

    public bool? IsCompleted => Status switch
    {
        TaskStatus.Completed => true,
        TaskStatus.Archived => null,
        null => null,
        _ => false
    };

    public List<TaskOutlineNode> Children { get; } = new();

    internal void AppendDescriptionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Description = string.IsNullOrEmpty(Description)
            ? line.TrimEnd()
            : $"{Description}{Environment.NewLine}{line.TrimEnd()}";
    }
}

public sealed record TaskOutlineClipboardOptions(bool CopyAsMarkdown, bool CopyDescription)
{
    public static TaskOutlineClipboardOptions Default { get; } = new(false, false);
}

public sealed class TaskOutlinePastePreview
{
    public TaskOutlinePastePreview(
        string header,
        string destinationLabel,
        string taskCountText,
        string previewText,
        int taskCount)
    {
        Header = header;
        DestinationLabel = destinationLabel;
        TaskCountText = taskCountText;
        PreviewText = previewText;
        TaskCount = taskCount;
    }

    public string Header { get; }

    public string DestinationLabel { get; }

    public string TaskCountText { get; }

    public string PreviewText { get; }

    public int TaskCount { get; }
}

public static class TaskOutlineClipboardService
{
    private const int SpacesPerLevel = 4;

    public static string BuildOutline(TaskItemViewModel root, TaskOutlineClipboardOptions? options = null)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var builder = new StringBuilder();
        AppendTask(builder, root, 0, new HashSet<string>(StringComparer.Ordinal), options ?? TaskOutlineClipboardOptions.Default);
        return builder.ToString().TrimEnd('\r', '\n');
    }

    public static string BuildOutline(TaskWrapperViewModel root, TaskOutlineClipboardOptions? options = null)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var builder = new StringBuilder();
        AppendWrapper(builder, root, 0, new HashSet<string>(StringComparer.Ordinal), options ?? TaskOutlineClipboardOptions.Default);
        return builder.ToString().TrimEnd('\r', '\n');
    }

    public static IReadOnlyList<TaskOutlineNode> ParseOutline(string? outline)
    {
        if (string.IsNullOrWhiteSpace(outline))
        {
            return Array.Empty<TaskOutlineNode>();
        }

        var lines = SplitLines(outline).ToList();
        var markedOutlineMode = lines.Any(line =>
        {
            var (_, content) = ParseIndent(line);
            return TryParseTaskContent(content).HasMarker;
        });

        var roots = new List<TaskOutlineNode>();
        var stack = new List<TaskOutlineNode>();

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var (level, content) = ParseIndent(rawLine);
            var parsed = TryParseTaskContent(content);

            if (markedOutlineMode && !parsed.HasMarker && stack.Count > 0 && level > 0)
            {
                var descriptionTargetIndex = Math.Min(level, stack.Count) - 1;
                if (descriptionTargetIndex >= 0)
                {
                    stack[descriptionTargetIndex].AppendDescriptionLine(content.Trim());
                }

                continue;
            }

            var title = parsed.Title.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var node = new TaskOutlineNode(title, status: parsed.Status);
            var normalizedLevel = Math.Min(level, stack.Count);

            if (normalizedLevel == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack[normalizedLevel - 1].Children.Add(node);
            }

            if (stack.Count > normalizedLevel)
            {
                stack.RemoveRange(normalizedLevel, stack.Count - normalizedLevel);
            }

            stack.Add(node);
        }

        return roots;
    }

    public static int CountNodes(IReadOnlyList<TaskOutlineNode> nodes)
    {
        if (nodes == null)
        {
            return 0;
        }

        return nodes.Sum(node => 1 + CountNodes(node.Children));
    }

    public static string BuildPreviewText(IReadOnlyList<TaskOutlineNode> nodes)
    {
        if (nodes == null || nodes.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            AppendPreviewNode(builder, node, 0);
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static void AppendTask(
        StringBuilder builder,
        TaskItemViewModel task,
        int level,
        ISet<string> path,
        TaskOutlineClipboardOptions options)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.Id) || !path.Add(task.Id))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        AppendTaskLine(builder, task, level, options);
        AppendDescription(builder, task.Description, level, options);

        foreach (var child in task.ContainsTasks)
        {
            AppendTask(builder, child, level + 1, path, options);
        }

        path.Remove(task.Id);
    }

    private static void AppendWrapper(
        StringBuilder builder,
        TaskWrapperViewModel wrapper,
        int level,
        ISet<string> path,
        TaskOutlineClipboardOptions options)
    {
        var task = wrapper.TaskItem;
        if (task == null || string.IsNullOrWhiteSpace(task.Id) || !path.Add(task.Id))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        AppendTaskLine(builder, task, level, options);
        AppendDescription(builder, task.Description, level, options);

        foreach (var child in wrapper.SubTasks)
        {
            AppendWrapper(builder, child, level + 1, path, options);
        }

        path.Remove(task.Id);
    }

    private static void AppendTaskLine(
        StringBuilder builder,
        TaskItemViewModel task,
        int level,
        TaskOutlineClipboardOptions options)
    {
        AppendIndent(builder, level, options.CopyAsMarkdown || options.CopyDescription);
        if (options.CopyAsMarkdown)
        {
            builder.Append("- ");
            builder.Append(task.Status.ToLegacyMarker());
            builder.Append(' ');
        }
        else if (options.CopyDescription)
        {
            builder.Append("- ");
        }

        builder.Append(NormalizeTitle(task.Title));
    }

    private static void AppendDescription(
        StringBuilder builder,
        string? description,
        int taskLevel,
        TaskOutlineClipboardOptions options)
    {
        if (!options.CopyDescription)
        {
            return;
        }

        foreach (var line in NormalizeDescriptionLines(description))
        {
            builder.AppendLine();
            AppendIndent(builder, taskLevel + 1, true);
            builder.Append(line);
        }
    }

    private static void AppendPreviewNode(StringBuilder builder, TaskOutlineNode node, int level)
    {
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        AppendIndent(builder, level, true);
        builder.Append(node.Status.HasValue
            ? $"- {node.Status.Value.ToLegacyMarker()} "
            : "- ");
        builder.Append(NormalizeTitle(node.Title));

        foreach (var line in NormalizeDescriptionLines(node.Description))
        {
            builder.AppendLine();
            AppendIndent(builder, level + 1, true);
            builder.Append(line);
        }

        foreach (var child in node.Children)
        {
            AppendPreviewNode(builder, child, level + 1);
        }
    }

    private static void AppendIndent(StringBuilder builder, int level, bool spaces)
    {
        if (spaces)
        {
            builder.Append(' ', Math.Max(level, 0) * SpacesPerLevel);
            return;
        }

        builder.Append('\t', Math.Max(level, 0));
    }

    private static string NormalizeTitle(string? title)
    {
        return (title ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .TrimEnd();
    }

    private static IReadOnlyList<string> NormalizeDescriptionLines(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Array.Empty<string>();
        }

        var lines = description
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static IEnumerable<string> SplitLines(string outline)
    {
        return outline
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static (int Level, string Content) ParseIndent(string line)
    {
        var index = 0;
        var columns = 0;

        while (index < line.Length)
        {
            if (line[index] == '\t')
            {
                columns += SpacesPerLevel;
                index++;
                continue;
            }

            if (line[index] == ' ')
            {
                columns++;
                index++;
                continue;
            }

            break;
        }

        return (columns / SpacesPerLevel, line[index..]);
    }

    private static ParsedTaskContent TryParseTaskContent(string text)
    {
        if (TryParseMarkdownChecklist(text, out var checklistTitle, out var status))
        {
            return new ParsedTaskContent(checklistTitle, status, true);
        }

        if (text.Length >= 2 && text[1] == ' ' && (text[0] == '-' || text[0] == '*' || text[0] == '+'))
        {
            return new ParsedTaskContent(text[2..], null, true);
        }

        return new ParsedTaskContent(text, null, false);
    }

    private static bool TryParseMarkdownChecklist(string text, out string title, out TaskStatus status)
    {
        title = string.Empty;
        status = TaskStatus.NotReady;

        if (text.Length < 6 ||
            text[0] != '-' ||
            text[1] != ' ' ||
            text[2] != '[' ||
            text[4] != ']' ||
            text[5] != ' ')
        {
            return false;
        }

        switch (text[3])
        {
            case ' ':
                title = text[6..];
                status = TaskStatus.NotReady;
                return true;
            case '!':
                title = text[6..];
                status = TaskStatus.Prepared;
                return true;
            case '>':
                title = text[6..];
                status = TaskStatus.InProgress;
                return true;
            case 'x':
            case 'X':
                title = text[6..];
                status = TaskStatus.Completed;
                return true;
            case '#':
                title = text[6..];
                status = TaskStatus.Archived;
                return true;
            default:
                return false;
        }
    }

    private readonly record struct ParsedTaskContent(string Title, TaskStatus? Status, bool HasMarker);
}
