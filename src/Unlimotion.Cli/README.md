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
unlimotion-cli task --tasks <task-dir> --id <task-id> --explain [--format text|json]
unlimotion-cli validate --tasks <task-dir> [--format text|json]
unlimotion-cli set-status --tasks <task-dir> --id <task-id> --status <status> [--author <name>] [--format text|json]
unlimotion-cli complete --tasks <task-dir> --id <task-id> [--author <name>] [--format text|json]
unlimotion-cli set-criterion --tasks <task-dir> --id <task-id> --criterion <criterion-id> --satisfied true|false [--format text|json]
unlimotion-cli satisfy-criterion --tasks <task-dir> --id <task-id> --criterion <criterion-id> [--format text|json]
```

## Availability semantics

The CLI uses `TaskAvailabilityAnalyzer`, which mirrors the current Unlimotion rules:

- incomplete `ContainsTasks` block the containing task;
- incomplete direct `BlockedByTasks` block the task;
- incomplete blockers from `ParentTasks` ancestors are inherited by child tasks;
- unsatisfied `CompletionCriteria` block completion, not startability;
- future `PlannedBeginDateTime` blocks startability, not completion availability;
- missing references are validation issues, but they are not treated as runtime blockers.

## Write behavior

Write commands update task JSON files in place and then recompute `IsCanBeCompleted` for every loaded task, saving only changed task files. Status transitions are guarded by Unlimotion availability rules: `InProgress` requires `canStart`, and `Completed` requires `canComplete`.

## Exit codes

- `0`: command completed successfully;
- `1`: validation/load issues were found;
- `2`: invalid arguments or unknown task id.
