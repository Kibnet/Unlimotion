using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Unlimotion.Storage;
using Unlimotion.TaskTree;

namespace Unlimotion.Cli;

public static class NightAgentSnapshotCommands
{
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] != "snapshot") return null;
        // Introspection owns the ordinary help surface and requires no storage resolution.
        if (args.Contains("--help") || args.Contains("-h")) return null;
        var format = "json";
        try
        {
            if (args.Length < 2) throw new NightAgentSnapshotException("invalidArguments", "A snapshot subcommand is required.", 2);
            var options = ParseOptions(args[2..]);
            format = options.GetValueOrDefault("format", "json");
            if (format is not ("json" or "text")) Invalid("format must be json or text.");
            object response;
            switch (args[1])
            {
                case "schema":
                    Allow(options, "kind", "format");
                    if (format != "json") Invalid("Schema requires --format json.");
                    response = Schema(options.GetValueOrDefault("kind", "selection"));
                    break;
                case "capture":
                    Allow(options, "selection", "output", "tasks", "format");
                    using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                        response = await CaptureAsync(Required(options, "selection"), Required(options, "output"), Required(options, "tasks"), deadline.Token);
                    break;
                case "read":
                    Allow(options, "snapshot", "view", "page-size", "cursor", "format");
                    response = NightAgentSnapshotCodec.ReadPage(await NightAgentSnapshotCodec.LoadArtifactAsync(Required(options, "snapshot")),
                        options.GetValueOrDefault("view", "targets"), PageSize(options), options.GetValueOrDefault("cursor"));
                    break;
                case "diff":
                    Allow(options, "before", "after", "page-size", "cursor", "format");
                    var beforePath = Required(options, "before");
                    NightAgentSnapshotArtifact before;
                    try { before = await NightAgentSnapshotCodec.LoadArtifactAsync(beforePath); }
                    catch (Exception error) when (error is NightAgentSnapshotException or IOException or UnauthorizedAccessException)
                    { throw new NightAgentSnapshotException("baselineUnavailable", "Before snapshot is unavailable or invalid: " + error.Message); }
                    response = NightAgentSnapshotCodec.DiffPage(before, await NightAgentSnapshotCodec.LoadArtifactAsync(Required(options, "after")),
                        PageSize(options), options.GetValueOrDefault("cursor"));
                    break;
                default: throw new NightAgentSnapshotException("invalidArguments", "Unknown snapshot subcommand.", 2);
            }
            Console.WriteLine(format == "json" ? JsonSerializer.Serialize(response, NightAgentSnapshotCodec.JsonOptions) : RenderText(response));
            return 0;
        }
        catch (NightAgentSnapshotException error) { return Error(format, error.Kind, error.Message, error.ExitCode); }
        catch (TaskGraphObservationException error) { return Error(format, error.Kind, error.Message, 1); }
        catch (OperationCanceledException) { return Error(format, "observationFailed", "Snapshot capture was cancelled or exceeded its 60 second deadline.", 1); }
        catch (JsonException error) { return Error(format, "invalidArguments", "Selection JSON is invalid: " + error.Message, 2); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Error(format, "observationFailed", error.Message, 1); }
    }

    public static async Task<object> CaptureAsync(string selectionPath, string outputPath, string tasksPath, CancellationToken cancellationToken = default)
    {
        var root = NightAgentSnapshotCodec.ResolvePhysicalDirectory(tasksPath);
        var destination = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) Invalid("Output parent directory must already exist.");
        var physicalParent = NightAgentSnapshotCodec.ResolvePhysicalDirectory(parent);
        destination = Path.Combine(physicalParent, Path.GetFileName(destination));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (IsOutputWithinSourceRoot(destination, root))
            Invalid("Snapshot output must be outside the task source root.");
        if (File.Exists(destination) || Directory.Exists(destination)) Invalid("Snapshot output already exists; artifacts cannot be overwritten.");
        var selectionFile = new FileInfo(Path.GetFullPath(selectionPath));
        if (!selectionFile.Exists) Invalid("Selection file does not exist.");
        if (selectionFile.Length > 1024 * 1024) Invalid("Selection exceeds the 1 MiB input limit.");
        var physicalSelection = selectionFile.ResolveLinkTarget(true)?.FullName ?? Path.Combine(
            NightAgentSnapshotCodec.ResolvePhysicalDirectory(selectionFile.DirectoryName!), selectionFile.Name);
        if (destination.Equals(physicalSelection, comparison)) Invalid("Snapshot output intersects the selection input.");
        var selection = NightAgentSnapshotCodec.NormalizeSelection(JsonSerializer.Deserialize<NightAgentSnapshotSelection>(
            await File.ReadAllTextAsync(selectionFile.FullName, cancellationToken), NightAgentSnapshotCodec.JsonOptions) ?? throw new JsonException("Missing selection."));
        var sourceKey = NightAgentSnapshotCodec.CreateSourceKey(root);
        var storage = new FileTaskStorage(new FileTaskStorageOptions
        { Path = root, CreateDirectoryIfMissing = false, UseDirectoryLock = true, PreserveUnknownJson = true });
        var observation = await storage.ReadObservationAsync(cancellationToken);
        var artifact = NightAgentSnapshotCodec.CreateArtifact(observation, selection, sourceKey, cancellationToken);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, NightAgentSnapshotCodec.JsonOptions);
        cancellationToken.ThrowIfCancellationRequested();
        var temp = Path.Combine(physicalParent, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Recheck directory aliases before publication; no caller-supplied path is deleted.
            if (!NightAgentSnapshotCodec.ResolvePhysicalDirectory(parent).Equals(physicalParent, comparison) ||
                !NightAgentSnapshotCodec.ResolvePhysicalDirectory(tasksPath).Equals(root, comparison))
                throw new NightAgentSnapshotException("snapshotUnstable", "Source/output directory alias changed during capture.");
            File.Move(temp, destination, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return new { success = true, artifact.SnapshotId, artifact.ArtifactHash, outputPath = destination,
            counts = new { targets = artifact.Targets.Count, payload = artifact.Nodes.Count, catalog = artifact.Catalog.Count }, artifact.Acquisition };
    }

    public static JsonNode Schema(string kind)
    {
        var type = kind switch
        {
            "selection" => typeof(NightAgentSnapshotSelection), "artifact" => typeof(NightAgentSnapshotArtifact),
            "page" => typeof(NightAgentSnapshotPage), "delta" => typeof(NightAgentSnapshotDeltaPage),
            _ => throw new NightAgentSnapshotException("invalidArguments", "Schema kind must be selection, artifact, page, or delta.", 2)
        };
        var schema = NightAgentSnapshotCodec.JsonOptions.GetJsonSchemaAsNode(type);
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        schema["title"] = "Unlimotion snapshot " + kind + " v1";
        if (kind != "artifact") schema["properties"]!["schemaVersion"]!["const"] = 1;
        else schema["properties"]!["snapshotFormatVersion"]!["const"] = 1;
        if (kind == "selection")
        {
            var properties = schema["properties"]!;
            properties["schemaVersion"]!["const"] = 1;
            properties["context"]!["enum"] = new JsonArray("none", "night-v1");
            properties["missingSelection"]!["enum"] = new JsonArray("error", "report");
            properties["include"]!["items"]!["enum"] = new JsonArray("details", "criteria", "history", "execution");
            properties["select"]!["properties"]!["mode"]!["enum"] = new JsonArray("all", "unlocked", "ids");
            properties["select"]!["properties"]!["statuses"]!["items"]!["enum"] = new JsonArray(
                Enum.GetNames<Unlimotion.Domain.TaskStatus>().Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
            properties["include"]!["uniqueItems"] = true;
            foreach (var key in new[] { "rootIds", "taskIds", "statuses" }) properties["select"]!["properties"]![key]!["uniqueItems"] = true;
        }
        if (kind == "page")
        {
            var nodeSchema = NightAgentSnapshotCodec.JsonOptions.GetJsonSchemaAsNode(typeof(NightAgentSnapshotNode));
            var targetSchema = NightAgentSnapshotCodec.JsonOptions.GetJsonSchemaAsNode(typeof(NightAgentSnapshotTargetNode));
            var catalogSchema = NightAgentSnapshotCodec.JsonOptions.GetJsonSchemaAsNode(typeof(NightAgentSnapshotCatalogEntry));
            PrefixReferences(nodeSchema, "#/$defs/node"); PrefixReferences(targetSchema, "#/$defs/target"); PrefixReferences(catalogSchema, "#/$defs/catalog");
            schema["$defs"] = new JsonObject { ["node"] = nodeSchema, ["target"] = targetSchema, ["catalog"] = catalogSchema };
            schema["properties"]!["items"]!["items"] = new JsonObject { ["oneOf"] = new JsonArray(
                new JsonObject { ["$ref"] = "#/$defs/node" }, new JsonObject { ["$ref"] = "#/$defs/target" }, new JsonObject { ["$ref"] = "#/$defs/catalog" }) };
            schema["properties"]!["view"]!["enum"] = new JsonArray("targets", "context", "manifest");
            schema["properties"]!["complete"]!["const"] = true;
        }
        if (kind == "delta")
        {
            schema["properties"]!["coverage"]!["const"] = "stateDifference";
            schema["properties"]!["baselineUsable"]!["const"] = true;
            schema["properties"]!["items"]!["items"]!["properties"]!["kind"]!["enum"] = new JsonArray(
                "created", "deleted", "updated", "relationAdded", "relationRemoved", "membershipChanged", "availabilityChanged", "targetInvalidated");
        }
        PermitLegacyNullableStrings(schema);
        return schema;
    }

    public static bool IsOutputWithinSourceRoot(string outputPath, string sourceRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        var output = Path.GetFullPath(outputPath);
        var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return output.Equals(root, comparison) || output.StartsWith(rootPrefix, comparison);
    }

    private static void PrefixReferences(JsonNode? node, string prefix)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var value) && value.StartsWith('#')) obj["$ref"] = prefix + value[1..];
            foreach (var property in obj.ToArray()) PrefixReferences(property.Value, prefix);
        }
        else if (node is JsonArray array) foreach (var item in array) PrefixReferences(item, prefix);
    }

    private static void PermitLegacyNullableStrings(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            // Existing task DTO annotations predate nullable persisted legacy fields.
            if (obj["properties"] is JsonObject properties)
                foreach (var name in new[] { "description", "descriptionUserText", "text", "author" })
                    if (properties[name] is JsonObject field && field["type"] is JsonValue type && type.TryGetValue<string>(out var value) && value == "string")
                        field["type"] = new JsonArray("string", "null");
            foreach (var property in obj.ToArray()) PermitLegacyNullableStrings(property.Value);
        }
        else if (node is JsonArray array) foreach (var item in array) PermitLegacyNullableStrings(item);
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) Invalid("Expected --option value.");
            if (!result.TryAdd(args[i][2..], args[i + 1])) Invalid("Duplicate option " + args[i] + ".");
        }
        return result;
    }
    private static string Required(IReadOnlyDictionary<string, string> options, string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new NightAgentSnapshotException("invalidArguments", $"--{key} is required.", 2);
    private static int PageSize(IReadOnlyDictionary<string, string> options) => !options.TryGetValue("page-size", out var value) ? 100 :
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : throw new NightAgentSnapshotException("invalidArguments", "Invalid page-size.", 2);
    private static void Allow(IReadOnlyDictionary<string, string> options, params string[] names)
    { if (options.Keys.Any(key => !names.Contains(key, StringComparer.Ordinal))) Invalid("Unsupported option for this snapshot subcommand."); }
    private static void Invalid(string message) => throw new NightAgentSnapshotException("invalidArguments", message, 2);
    private static int Error(string format, string kind, string message, int code)
    {
        if (format == "json") Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = new { kind, message } }, NightAgentSnapshotCodec.JsonOptions));
        else Console.Error.WriteLine(EscapeText(kind + ": " + message));
        return code;
    }
    public static string EscapeText(string value) => string.Concat(value.EnumerateRunes().Select(rune => Rune.IsControl(rune) ? "\\u" + rune.Value.ToString("x4", CultureInfo.InvariantCulture) : rune.ToString()));
    private static string RenderText(object response)
    {
        if (response is NightAgentSnapshotPage page)
        {
            var text = new StringBuilder().AppendLine($"Snapshot {page.SnapshotId}; {page.View}; total {page.TotalCount}");
            foreach (var item in page.Items)
            {
                if (item is NightAgentSnapshotNode node)
                {
                    text.AppendLine(EscapeText($"[{node.Task.Status}] {node.Id}: {node.Task.Title}"));
                    foreach (var parent in node.ParentTasks) text.AppendLine(EscapeText($"  contains {parent} -> {node.Id} (shared node reference)"));
                    text.AppendLine(EscapeText(JsonSerializer.Serialize(node, NightAgentSnapshotCodec.JsonOptions)));
                }
                else text.AppendLine(EscapeText(JsonSerializer.Serialize(item, NightAgentSnapshotCodec.JsonOptions)));
            }
            text.AppendLine(page.NextCursor == null ? "End of view." : "Continue with --cursor " + page.NextCursor);
            return text.ToString();
        }
        if (response is NightAgentSnapshotDeltaPage delta)
            return string.Join(Environment.NewLine, delta.Items.Select(item => EscapeText(JsonSerializer.Serialize(item, NightAgentSnapshotCodec.JsonOptions)))) +
                Environment.NewLine + (delta.NextCursor == null ? "End of state difference." : "Continue with --cursor " + delta.NextCursor);
        return EscapeText(JsonSerializer.Serialize(response, NightAgentSnapshotCodec.JsonOptions));
    }
}
