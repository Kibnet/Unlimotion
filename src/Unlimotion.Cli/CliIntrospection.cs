using System.Reflection;
using System.Text.Json;

namespace Unlimotion.Cli;

internal static class CliIntrospection
{
    private static readonly Assembly Assembly = typeof(Program).Assembly;
    public static string PackageVersion => (Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0").Split('+')[0];
    public static string ApplicationVersion => Assembly.GetName().Version?.ToString() ?? PackageVersion;
    public static string BuildKind => Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "UnlimotionBuildKind")?.Value ?? "local";

    private sealed record CommandHelp(string Command, string Description, string Usage,
        string[] RequiredOptions, string[] OptionalOptions, string Defaults, string AllowedValues,
        string RepeatableOptions, string Effect, string[] ErrorKinds, string Example, string Notes);

    private static readonly CommandHelp[] Commands =
    [
        new("snapshot", "Capture and read immutable task context artifacts.", "snapshot capture|read|diff|schema [options]", ["subcommand"], [], "none", "subcommand=capture|read|diff|schema", "none", "capture reads source and creates a new artifact; other subcommands are offline", ["invalidArguments"], "help snapshot capture", "Artifacts are caller-owned, have no TTL, and never silently refresh. Delta compares states, not an event stream."),
        new("snapshot capture", "Capture selected tasks, their complete context and a compact source catalog.", "snapshot capture --selection <file> --output <new-file> --tasks <path> [--format text|json]", ["--selection", "--output", "--tasks"], ["--format"], "format=json; selection schema v1", "context=night-v1|none; mode=unlocked|all|ids", "none", "read tasks without recovery; create a new artifact outside task source", ["invalidArguments", "notFound", "recoveryRequired", "snapshotUnstable", "snapshotTooLarge", "observationFailed"], "snapshot capture --selection selection.json --output run.snapshot.json --tasks C:\\Tasks --format json", "One verified observation, all parent branches, bounded closure. Missing roots never broaden selection. A pending transaction requires explicit write-path recovery. The cooperative lock plus verified manifest is not atomic against bypassing editors/sync tools."),
        new("snapshot read", "Read a pinned artifact page without task settings or live storage.", "snapshot read --snapshot <file> --view targets|context|manifest [--page-size <1..500>] [--cursor <token>] [--format text|json]", ["--snapshot", "--view"], ["--page-size", "--cursor", "--format"], "page-size=100; format=json", "view=targets|context|manifest; page-size=1..500", "none", "offline artifact read", ["invalidArguments", "snapshotInvalid", "cursorMismatch", "recordTooLarge"], "snapshot read --snapshot run.snapshot.json --view context --format json", "Pages are ordered by ID and bound to artifact integrity/scope/view/page size. Payload records occur once; retain every parent edge."),
        new("snapshot diff", "Compare two compatible observations and invalidate changed target contexts.", "snapshot diff --before <file> --after <file> [--page-size <1..500>] [--cursor <token>] [--format text|json]", ["--before", "--after"], ["--page-size", "--cursor", "--format"], "page-size=100; format=json", "page-size=1..500", "none", "offline artifact read", ["invalidArguments", "baselineUnavailable", "deltaIncompatible", "cursorMismatch", "recordTooLarge"], "snapshot diff --before previous.snapshot.json --after run.snapshot.json --format json", "State delta cannot observe transient changes between captures. Deletion means absentFromObservedNamespace. On missing/incompatible baseline, preserve external decisions/materials/queues, mark baselineReset and revalidate full context."),
        new("snapshot schema", "Print a versioned snapshot JSON Schema without a task source.", "snapshot schema [--kind selection|artifact|page|delta] --format json", [], ["--kind", "--format"], "kind=selection; format=json", "kind=selection|artifact|page|delta; format=json", "none", "none", ["invalidArguments"], "snapshot schema --kind selection --format json", "Runtime also enforces graph, integrity, closure and byte limits."),
        new("version", "Show package, application and build-kind versions.", "version [--format text|json]", [], ["--format"], "format=text", "format=text|json", "none", "none", ["invalidArguments"], "version --format json", "Also available as --version in text form; neither reads task settings."),
        new("status", "Count tasks and availability by status.", "status [--tasks <path>] [--format text|json]", [], ["--tasks", "--format"], "format=text; tasks=UNLIMOTION_TASKS or desktop local path", "format=text|json", "none", "read", ["settingsNotFound", "settingsInvalid", "settingsUnsupported", "operationFailed"], "status --format json", "No task mutation."),
        new("context", "Show the resolved local task directory and its source.", "context [--tasks <path>] [--format text|json]", [], ["--tasks", "--format"], "format=text; tasks=UNLIMOTION_TASKS or desktop local path", "format=text|json; sourceKind=explicitTasks|environmentTasks|desktopSettings", "none", "read; checks directory existence but does not load tasks", ["settingsNotFound", "settingsInvalid", "settingsUnsupported", "invalidArguments"], "context --format json", "Use the returned tasksPath as explicit --tasks in every related command."),
        new("search", "Find tasks by ID/title or opt-in Unicode content search.", "search [--snapshot <file> | --tasks <path>] [--fields id,title,description,criteria] [--query <text>] [--status <status>] [--root <id>]... [--limit <1..100>] [--cursor <token>] [--format text|json]", [], ["--snapshot", "--fields", "--query", "--status", "--root", "--limit", "--cursor", "--tasks", "--format"], "fields=id,title; limit=20; all statuses; format=text", "fields=unique subset id,title,description,criteria; limit=1..100", "--root", "read", ["invalidArguments", "notFound", "sectionNotCaptured", "scopeNotCaptured", "invalidCursor", "operationFailed"], "search --snapshot run.snapshot.json --fields title,description,criteria --query plan --format json", "Without new flags the legacy JSON/matching contract is preserved. V2 uses literal NFC/OrdinalIgnoreCase; no trimming or regex. Snapshot search covers payload only, with pinned cursors; live v2 is liveUnpinned. Description excludes protected execution text, malformed markers produce incomplete-search warnings. Follow nextCursor until null."),
        new("unlocked", "List startable tasks, optionally under roots.", "unlocked [--root <id>]... [--tasks <path>] [--format text|json]", [], ["--root", "--tasks", "--format"], "format=text; tasks=UNLIMOTION_TASKS or desktop local path", "format=text|json", "--root", "read", ["notFound", "operationFailed"], "unlocked --format json", "Only currently startable tasks are returned."),
        new("candidates", "Rank bounded startable candidates.", "candidates --limit <1..100> [--status <status>] [--startable true|false] [--sort default] [--tasks <path>] [--format text|json]", ["--limit"], ["--status", "--startable", "--sort", "--tasks", "--format"], "status=Prepared; startable=true; sort=default; format=text", "status=NotReady|Prepared|InProgress|Completed|Archived; limit=1..100; sort=default", "none", "read", ["invalidArguments", "operationFailed"], "candidates --limit 20 --format json", "No cursor; use search for complete discovery."),
        new("task", "Read a task analysis and optional expanded snapshot.", "task --id <id> [--include details,relations,criteria,history,execution] [--tasks <path>] [--format text|json]", ["--id"], ["--include", "--tasks", "--format"], "no include; format=text", "include=details|relations|criteria|history|execution", "--include", "read", ["invalidArguments", "notFound", "operationFailed"], "task --id example-task-id --include details,relations,criteria --format json", "Expanded snapshot includes etag. Text sections appear only when requested."),
        new("validate", "Check task graph references and availability.", "validate [--tasks <path>] [--format text|json]", [], ["--tasks", "--format"], "format=text", "format=text|json", "none", "read", ["operationFailed"], "validate --format json", "Exit 1 when graph is invalid."),
        new("apply", "Preview or atomically apply an approved declarative graph request.", "apply --request <path|-> [--dry-run --diff full] [--expect-preview <file>] [--tasks <path>] [--format text|json]", ["--request"], ["--dry-run", "--diff", "--expect-preview", "--tasks", "--format"], "dry-run=false; format=text", "request=JSON schema v1; format=text|json", "none", "write unless --dry-run", ["invalidArguments", "notFound", "preconditionFailed", "conflictingOperations", "descriptionMarkerConflict", "businessRuleDenied", "validationFailed", "idempotencyConflict", "reconciliationRequired", "outcomeUnknown", "operationFailed", "previewInvalid", "previewStale", "previewTooLarge", "recoveryRequired", "unstablePrecondition", "unstableSource"], "apply --request request.json --dry-run --format json", "Run apply schema/example for syntax. --diff full requires --dry-run and returns the full before/after witness with derived effects. Save complete JSON and apply the identical request with --expect-preview to guard raw source and semantic effects. Projection 2 includes areaIds and excludes the retired goal field; obtain a new full preview instead of reusing projection 1. A supplied witness is validated before the historical receipt shortcut. Unknown flags on an older CLI require upgrading; never silently downgrade. Preview does not replay pending journals. Generated timestamps are typed placeholders; receipt retries still require read-back. Request text is capped at 4 MiB. Status transitions obey graph rules; terminal status needs justification and absolute evidence links. ETags guard writes. Request hash includes decoded JSON whitespace. Receipt identifies a prior application, while desired state alone does not prove history. Preview before write; after uncertain outcome use apply inspect and read-back. For a create whose generated ID might be lost, use an apply request with a createTask operation and chosen newTaskId."),
        new("apply inspect", "Inspect receipt and current postconditions without applying a request.", "apply inspect --request <path|-> [--tasks <path>] [--format text|json]", ["--request"], ["--tasks", "--format"], "format=text", "format=text|json", "none", "read without journal replay", ["invalidArguments", "operationFailed", "recoveryRequired"], "apply inspect --request request.json --format json", "receiptMatched proves a prior matching application; desiredStatePresent only describes current state. Do not blindly retry a mixed outcome."),
        new("apply schema", "Print request schema v1 or full preview envelope v1 / projection 2 schema.", "apply schema [--kind request|preview] --format json", [], ["--kind", "--format"], "kind=request; format=json", "kind=request|preview; format=json", "none", "none", ["invalidArguments"], "apply schema --format json", "Semantic graph, ETag, protected marker and status rules also require preview."),
        new("apply example", "Print a complete application request template.", "apply example <set-field|add-relation|create-task> --format json", ["template"], ["--format"], "format=json", "template=set-field|add-relation|create-task", "none", "none", ["invalidArguments"], "apply example create-task --format json", "IDs and ETags are illustrative; replace them before preview."),
        new("create", "Create a task with an automatically chosen ID.", "create --title <text> [--description <text>] [--parent <id>]... [--author <name>] [--tasks <path>] [--format text|json]", ["--title"], ["--description", "--parent", "--author", "--tasks", "--format"], "description empty; format=text", "format=text|json", "--parent", "write", ["invalidArguments", "operationFailed"], "create --title 'New task' --format json", "Use an apply request with a createTask operation and chosen ID when an uncertain response must be reconciled."),
        new("claim", "Claim a prepared task for agent execution.", "claim --id <id> --agent <id> --expected-status Prepared [--tasks <path>] [--format text|json]", ["--id", "--agent", "--expected-status"], ["--tasks", "--format"], "format=text", "expected-status=Prepared", "none", "write", ["invalidArguments", "operationFailed"], "claim --id example-task-id --agent example-agent --expected-status Prepared --format json", "Claim is for actual agent execution, not task inspection."),
        new("execution", "Record lease-bound agent work through a subcommand.", "execution question|answer|result|complete [options]", ["subcommand"], [], "none", "subcommand=question|answer|result|complete", "none", "write", ["invalidArguments", "operationFailed"], "help execution question", "Read help execution <subcommand> for its exact arguments."),
        new("execution question", "Add an execution question under a lease.", "execution question --id <id> --agent <id> --lease <id> --text <text> [--tasks <path>] [--format text|json]", ["--id", "--agent", "--lease", "--text"], ["--tasks", "--format"], "format=text", "format=text|json", "none", "write", ["invalidArguments", "operationFailed"], "execution question --id example-task-id --agent example-agent --lease example-lease --text 'Need input' --format json", "Requires active matching lease."),
        new("execution answer", "Answer an execution question.", "execution answer --id <id> --agent <id> --lease <id> --question-id <id> --text <text> [--tasks <path>] [--format text|json]", ["--id", "--agent", "--lease", "--question-id", "--text"], ["--tasks", "--format"], "format=text", "format=text|json", "none", "write", ["invalidArguments", "operationFailed"], "execution answer --id example-task-id --agent example-agent --lease example-lease --question-id example-question --text 'Answer' --format json", "Requires matching lease and question."),
        new("execution result", "Record an execution result.", "execution result --id <id> --agent <id> --lease <id> --summary <text> [--link <absolute-uri>]... [--tasks <path>] [--format text|json]", ["--id", "--agent", "--lease", "--summary"], ["--link", "--tasks", "--format"], "format=text", "link=absolute URI", "--link", "write", ["invalidArguments", "operationFailed"], "execution result --id example-task-id --agent example-agent --lease example-lease --summary 'Result' --format json", "Requires matching lease."),
        new("execution complete", "Complete agent execution.", "execution complete --id <id> --agent <id> --lease <id> --summary <text> [--link <absolute-uri>]... [--tasks <path>] [--format text|json]", ["--id", "--agent", "--lease", "--summary"], ["--link", "--tasks", "--format"], "format=text", "link=absolute URI", "--link", "write", ["invalidArguments", "operationFailed"], "execution complete --id example-task-id --agent example-agent --lease example-lease --summary 'Done' --format json", "Requires matching lease and task rules."),
        new("release", "Release an execution lease.", "release --id <id> --agent <id> --lease <id> --reason <text> [--tasks <path>] [--format text|json]", ["--id", "--agent", "--lease", "--reason"], ["--tasks", "--format"], "format=text", "format=text|json", "none", "write", ["invalidArguments", "operationFailed"], "release --id example-task-id --agent example-agent --lease example-lease --reason 'Paused' --format json", "Requires matching lease."),
        new("set-status", "Change a task status.", "set-status --id <id> --status <status> [--author <name>] [--tasks <path>] [--format text|json]", ["--id", "--status"], ["--author", "--tasks", "--format"], "format=text", "status=NotReady|Prepared|InProgress|Completed|Archived", "none", "write", ["invalidArguments", "operationFailed"], "set-status --id example-task-id --status Prepared --format json", "Business rules can deny transitions."),
        new("complete", "Set a task to Completed.", "complete --id <id> [--author <name>] [--tasks <path>] [--format text|json]", ["--id"], ["--author", "--tasks", "--format"], "format=text", "format=text|json", "none", "write", ["invalidArguments", "operationFailed"], "complete --id example-task-id --format json", "Business rules can deny completion."),
        new("set-criterion", "Set criterion satisfaction.", "set-criterion --id <id> --criterion <id> --satisfied true|false [--tasks <path>] [--format text|json]", ["--id", "--criterion", "--satisfied"], ["--tasks", "--format"], "format=text", "satisfied=true|false", "none", "write", ["invalidArguments", "operationFailed"], "set-criterion --id example-task-id --criterion example-criterion --satisfied true --format json", "Task and criterion must exist."),
        new("satisfy-criterion", "Mark criterion satisfied.", "satisfy-criterion --id <id> --criterion <id> [--tasks <path>] [--format text|json]", ["--id", "--criterion"], ["--tasks", "--format"], "format=text", "format=text|json", "none", "write", ["invalidArguments", "operationFailed"], "satisfy-criterion --id example-task-id --criterion example-criterion --format json", "Task and criterion must exist.")
    ];

    private static readonly string[] CommonTaskErrorKinds =
        ["invalidArguments", "settingsNotFound", "settingsPathMissing", "settingsInvalid", "settingsUnsupported", "operationFailed"];

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0)
        {
            PrintGeneral(Console.Out);
            return true;
        }
        var json = args.Zip(args.Skip(1)).Any(pair => pair.First == "--format" && pair.Second == "json");
        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine($"Unlimotion.Cli {PackageVersion} ({BuildKind} build)");
            return true;
        }
        if (args[0] == "version")
        {
            if (args.Length != 1 && !(args.Length == 3 && args[1] == "--format" && args[2] is "json" or "text"))
                throw new CliException("Usage: version [--format text|json].");
            if (json) Write(new { packageVersion = PackageVersion, applicationVersion = ApplicationVersion, buildKind = BuildKind });
            else Console.WriteLine($"Unlimotion.Cli {PackageVersion} ({BuildKind} build)");
            return true;
        }
        if (args[0] == "apply" && args.Length >= 2 && args[1] is "schema" or "example" &&
            !args.Contains("--help") && !args.Contains("-h"))
        {
            if (args[1] == "schema")
            {
                var kind = "request";
                var supplied = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 2; i < args.Length; i += 2)
                {
                    if (i + 1 >= args.Length || !supplied.Add(args[i]) ||
                        args[i] is not ("--kind" or "--format"))
                        throw new CliException("Usage: apply schema [--kind request|preview] --format json.");
                    if (args[i] == "--kind") kind = args[i + 1];
                    else if (args[i + 1] != "json") throw new CliException("Schema format must be json.");
                }
                if (kind is not ("request" or "preview")) throw new CliException("Schema kind must be request or preview.");
                using var stream = Assembly.GetManifestResourceStream($"Unlimotion.Cli.apply-{kind}-v1.schema.json")
                    ?? throw new InvalidOperationException("Embedded apply schema is missing.");
                using var reader = new StreamReader(stream);
                Console.WriteLine(reader.ReadToEnd());
                return true;
            }
            if (args.Length != 3 && !(args.Length == 5 && args[3] == "--format" && args[4] == "json"))
                throw new CliException("Usage: apply example <set-field|add-relation|create-task> --format json.");
            Write(Example(args[2]));
            return true;
        }
        var help = args[0] == "help" || args.Contains("--help") || args.Contains("-h");
        if (!help) return false;
        var parts = args[0] == "help" ? args.Skip(1).TakeWhile(part => !part.StartsWith('-')).ToArray()
            : args.TakeWhile(part => !part.StartsWith('-')).ToArray();
        var name = string.Join(' ', parts).ToLowerInvariant();
        if (name.Length == 0)
        {
            if (json) Write(new { version = PackageVersion, commands = Commands.Select(command => new { command.Command, command.Description, command.Effect }), exitCodes = "0 success; 1 operation/validation failure; 2 invalid arguments" });
            else PrintGeneral(Console.Out);
            return true;
        }
        var selected = Commands.FirstOrDefault(command => command.Command == name)
            ?? throw new CliException($"Unknown help command '{name}'.");
        if (json) Write(new { selected.Command, selected.Description, selected.Usage, selected.RequiredOptions,
            selected.OptionalOptions, selected.Defaults, selected.AllowedValues, selected.RepeatableOptions,
            selected.Effect, exitCodes = "0 success; 1 operation/validation failure; 2 invalid arguments",
            selected.ErrorKinds, commonTaskErrorKinds = selected.Effect == "none" ? Array.Empty<string>() : CommonTaskErrorKinds,
            selected.Example, selected.Notes });
        else
        {
            Console.WriteLine($"Usage: unlimotion-cli {selected.Usage}");
            Console.WriteLine(selected.Description);
            Console.WriteLine($"Required: {string.Join(", ", selected.RequiredOptions.DefaultIfEmpty("none"))}");
            Console.WriteLine($"Optional: {string.Join(", ", selected.OptionalOptions.DefaultIfEmpty("none"))}");
            Console.WriteLine($"Defaults: {selected.Defaults}");
            Console.WriteLine($"Allowed: {selected.AllowedValues}; repeatable: {selected.RepeatableOptions}");
            Console.WriteLine($"Effect: {selected.Effect}; exit: 0 success, 1 operation/validation failure, 2 invalid arguments");
            Console.WriteLine($"Errors: {string.Join(", ", selected.ErrorKinds)}");
            if (selected.Effect != "none")
                Console.WriteLine($"Common task-source errors: {string.Join(", ", CommonTaskErrorKinds)}");
            Console.WriteLine($"Example: unlimotion-cli {selected.Example}");
            Console.WriteLine(selected.Notes);
        }
        return true;
    }

    public static void PrintGeneral(TextWriter writer)
    {
        writer.WriteLine("Usage: unlimotion-cli <command> [options]");
        writer.WriteLine("Task directory: --tasks <path>, then UNLIMOTION_TASKS, then active local desktop settings.");
        writer.WriteLine("Discover: version, help <command>, context, search, task, apply schema, apply example, apply inspect.");
        foreach (var command in Commands) writer.WriteLine($"  unlimotion-cli {command.Usage}");
    }

    private static object Example(string kind)
    {
        var operation = kind switch
        {
            "set-field" => (object)new { operationId = "example-set-title", kind = "setField", taskId = "example-task-id", field = "title", value = "Example new title" },
            "add-relation" => new { operationId = "example-add-relation", kind = "addRelation", relation = "contains", fromTaskId = "example-parent-id", toTaskId = "example-child-id" },
            "create-task" => new { operationId = "example-create-task", kind = "createTask", newTaskId = "example-new-task-id", title = "Example task", descriptionUserText = "Replace this description", parentIds = Array.Empty<string>() },
            _ => throw new CliException("Example must be set-field, add-relation, or create-task.")
        };
        var preconditions = kind switch
        {
            "set-field" => new[] { new { taskId = "example-task-id", etag = "sha256:" + new string('0', 64) } },
            "add-relation" => new[] { new { taskId = "example-parent-id", etag = "sha256:" + new string('0', 64) }, new { taskId = "example-child-id", etag = "sha256:" + new string('0', 64) } },
            _ => Array.Empty<object>()
        };
        return new { schemaVersion = 1, applicationId = "example-application-id", proposalRefs = new[] { new { id = "example-proposal-id", revision = 1 } },
            author = "example-agent", reason = "Example approved change; replace all example IDs and ETags.", preconditions, operations = new[] { operation } };
    }

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}
