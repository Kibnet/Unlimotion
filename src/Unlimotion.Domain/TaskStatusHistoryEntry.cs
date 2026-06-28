using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unlimotion.Domain;

public record TaskStatusHistoryEntry
{
    public TaskStatus Status { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public string Author { get; set; } = "System";

    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtensionData { get; set; }
}
