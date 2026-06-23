using System;
using System.Threading.Tasks;

namespace Unlimotion;

public sealed class UnlimotionClientOptions
{
    public string? DefaultTaskStoragePath { get; set; }
    public Func<string?, Task>? PrepareFileStoragePathAsync { get; set; }
    public Func<string, string>? GetAbsolutePath { get; set; }
}
