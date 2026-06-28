# Unlimotion CLI

Command-line client for inspecting and updating Unlimotion task directories without starting the UI. It is intended for agents and automation that need the same task availability semantics as Unlimotion.

## Install from a local package

```powershell
dotnet pack src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -o artifacts\tools
dotnet tool install --tool-path C:\tmp\unlimotion-cli-tool --add-source artifacts\tools Unlimotion.Cli --version 0.3.0
C:\tmp\unlimotion-cli-tool\unlimotion-cli status --tasks C:\Projects\ТОС\Knowledge.TOC\Tasks --format json
```

Use a dedicated `--tool-path` for automation so agent runs do not mutate the user's global .NET tool state.

## Commands

```powershell
unlimotion-cli status --tasks <task-dir> [--format text|json]
unlimotion-cli unlocked --tasks <task-dir> [--format text|json]
unlimotion-cli task --tasks <task-dir> --id <task-id> [--format text|json]
unlimotion-cli validate --tasks <task-dir> [--format text|json]
unlimotion-cli set-status --tasks <task-dir> --id <task-id> --status <status> [--author <name>] [--format text|json]
unlimotion-cli complete --tasks <task-dir> --id <task-id> [--author <name>] [--format text|json]
unlimotion-cli set-criterion --tasks <task-dir> --id <task-id> --criterion <criterion-id> --satisfied true|false [--format text|json]
unlimotion-cli satisfy-criterion --tasks <task-dir> --id <task-id> --criterion <criterion-id> [--format text|json]
```

## Availability semantics

Read commands use the shared file storage and `TaskAvailabilityAnalyzer` to explain the current graph:

- incomplete `ContainsTasks` block the containing task;
- incomplete direct `BlockedByTasks` block the task;
- incomplete blockers from `ParentTasks` ancestors are inherited by child tasks;
- unsatisfied `CompletionCriteria` block completion, not startability;
- future `PlannedBeginDateTime` blocks startability, not completion availability;
- missing references are validation issues, but they are not treated as runtime blockers.

## Write behavior

Write commands are a thin wrapper over the shared Unlimotion engine. The CLI loads tasks through `Unlimotion.Storage.FileTaskStorage`, applies the requested change to a detached task model, and calls `TaskTreeManager.UpdateTask(...)`. Status transitions, affected-task recalculation, repeating-task cloning, reverse-link updates, status history, and `--author` handling are owned by `TaskTreeManager`.

Before any write command, the CLI validates the loaded graph and fails fast on load errors, duplicate task ids, or relation/reference issues. Availability mismatches remain diagnostic validation output; normal write commands do not silently repair them. Writes use a directory-level lock and atomic file replacement in the task directory.

In JSON mode, command errors use a stable envelope:

```json
{
  "success": false,
  "error": {
    "kind": "validationFailed",
    "message": "Task graph is not safe for write commands: ..."
  }
}
```

## Exit codes

- `0`: command completed successfully;
- `1`: validation/load issues, business-rule denied, unknown task id, or operation failure;
- `2`: invalid arguments.
