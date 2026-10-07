using System;
using System.Collections.Generic;
using System.Linq;

namespace Unlimotion;

public sealed record TaskDeepLink(string TaskId)
{
    public const string Scheme = "unlimotion";
    public const string TaskHost = "task";
    public const int MaximumTaskIdLength = 160;

    public Uri Uri => new($"{Scheme}://{TaskHost}/{TaskId}", UriKind.Absolute);

    public static TaskDeepLink? FindInArguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments
            .Select(argument => TryParse(argument, out var link) ? link : null)
            .FirstOrDefault(static link => link is not null);
    }

    public static bool TryParse(string? value, out TaskDeepLink? link)
    {
        link = null;
        if (string.IsNullOrWhiteSpace(value)
            || !System.Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, TaskHost, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
        var pathStart = schemeSeparator < 0 ? -1 : value.IndexOf('/', schemeSeparator + 3);
        if (pathStart < 0
            || !string.Equals(
                value[(schemeSeparator + 3)..pathStart],
                TaskHost,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var escapedPath = value[(pathStart + 1)..];
        if (escapedPath.Length == 0 || escapedPath.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        string taskId;
        try
        {
            taskId = System.Uri.UnescapeDataString(escapedPath);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (taskId.Length is 0 or > MaximumTaskIdLength
            || taskId.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        link = new TaskDeepLink(taskId);
        return true;
    }
}

public sealed class TaskDeepLinkActivationEventArgs(TaskDeepLink link) : EventArgs
{
    public TaskDeepLink Link { get; } = link;
}

public interface ITaskDeepLinkActivationSource
{
    event EventHandler<TaskDeepLinkActivationEventArgs>? ActivationRequested;

    IReadOnlyList<TaskDeepLink> DrainPending();
}
