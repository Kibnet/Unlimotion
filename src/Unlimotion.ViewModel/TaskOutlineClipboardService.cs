using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unlimotion.ViewModel;

public sealed class TaskOutlineNode
{
    public TaskOutlineNode(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public List<TaskOutlineNode> Children { get; } = new();
}

public static class TaskOutlineClipboardService
{
    private const int SpacesPerLevel = 4;

    public static string BuildOutline(TaskItemViewModel root)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var builder = new StringBuilder();
        AppendTask(builder, root, 0, new HashSet<string>(StringComparer.Ordinal));
        return builder.ToString().TrimEnd('\r', '\n');
    }

    public static string BuildOutline(TaskWrapperViewModel root)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var builder = new StringBuilder();
        AppendWrapper(builder, root, 0, new HashSet<string>(StringComparer.Ordinal));
        return builder.ToString().TrimEnd('\r', '\n');
    }

    public static IReadOnlyList<TaskOutlineNode> ParseOutline(string? outline)
    {
        if (string.IsNullOrWhiteSpace(outline))
        {
            return Array.Empty<TaskOutlineNode>();
        }

        var roots = new List<TaskOutlineNode>();
        var stack = new List<TaskOutlineNode>();

        foreach (var rawLine in SplitLines(outline))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var (level, title) = ParseLine(rawLine);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var node = new TaskOutlineNode(title);
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

    private static void AppendTask(
        StringBuilder builder,
        TaskItemViewModel task,
        int level,
        ISet<string> path)
    {
        if (task == null || string.IsNullOrWhiteSpace(task.Id) || !path.Add(task.Id))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        AppendTaskLine(builder, task, level);

        foreach (var child in task.ContainsTasks)
        {
            AppendTask(builder, child, level + 1, path);
        }

        path.Remove(task.Id);
    }

    private static void AppendWrapper(
        StringBuilder builder,
        TaskWrapperViewModel wrapper,
        int level,
        ISet<string> path)
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

        AppendTaskLine(builder, task, level);

        foreach (var child in wrapper.SubTasks)
        {
            AppendWrapper(builder, child, level + 1, path);
        }

        path.Remove(task.Id);
    }

    private static void AppendTaskLine(StringBuilder builder, TaskItemViewModel task, int level)
    {
        builder.Append('\t', level);
        builder.Append(NormalizeTitle(task.Title));
    }

    private static string NormalizeTitle(string? title)
    {
        return (title ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .TrimEnd();
    }

    private static IEnumerable<string> SplitLines(string outline)
    {
        return outline
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static (int Level, string Title) ParseLine(string line)
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

        var title = StripBulletMarker(line[index..]).Trim();
        return (columns / SpacesPerLevel, title);
    }

    private static string StripBulletMarker(string text)
    {
        if (text.Length >= 2 && text[1] == ' ' && (text[0] == '-' || text[0] == '*' || text[0] == '+'))
        {
            return text[2..];
        }

        return text;
    }
}
