using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Unlimotion.Cli;

internal sealed class TaskApplicationReceiptStore
{
    private readonly string _root;

    public TaskApplicationReceiptStore(string taskRoot)
    {
        var fullRoot = Path.GetFullPath(taskRoot);
        _root = Path.Combine(fullRoot, ".unlimotion.applies", "v1");
    }

    public async Task<TaskApplicationReceipt?> ReadAsync(string applicationId)
    {
        var path = GetPath(applicationId);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<TaskApplicationReceipt>(await File.ReadAllTextAsync(path));
    }

    public async Task WriteAsync(TaskApplicationReceipt receipt)
    {
        Directory.CreateDirectory(_root);
        var path = GetPath(receipt.ApplicationId);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(receipt));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string GetPath(string applicationId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(applicationId))).ToLowerInvariant();
        var path = Path.GetFullPath(Path.Combine(_root, hash + ".json"));
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Application receipt path escapes the task space.");
        return path;
    }
}

internal sealed record TaskApplicationReceipt
{
    public string ApplicationId { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public DateTimeOffset AppliedAt { get; init; }
    public IReadOnlyList<ApplicationReceiptProposalReference> ProposalRefs { get; init; } = Array.Empty<ApplicationReceiptProposalReference>();
    public IReadOnlyList<string> ChangedTaskIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CreatedTaskIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OperationIds { get; init; } = Array.Empty<string>();
}

internal sealed record ApplicationReceiptProposalReference
{
    public string Id { get; init; } = string.Empty;
    public int Revision { get; init; }
}
