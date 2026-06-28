# Unlimotion CLI

Read-only command-line client for inspecting Unlimotion task directories without starting the UI. It is intended for agents and automation that need the same task availability semantics as Unlimotion.

## Install from a local package

```powershell
dotnet pack src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -o artifacts\tools
dotnet tool install --tool-path C:\tmp\unlimotion-cli-tool --add-source artifacts\tools Unlimotion.Cli --version 0.1.0
C:\tmp\unlimotion-cli-tool\unlimotion-cli status --tasks C:\Projects\ТОС\Knowledge.TOC\Tasks --format json
```

Use a dedicated `--tool-path` for automation so agent runs do not mutate the user's global .NET tool state.

## Commands

```powershell
unlimotion-cli status --tasks <task-dir> [--format text|json]
unlimotion-cli unlocked --tasks <task-dir> [--format text|json]
unlimotion-cli task --tasks <task-dir> --id <task-id> --explain [--format text|json]
unlimotion-cli validate --tasks <task-dir> [--format text|json]
```

## Availability semantics

The CLI uses `TaskAvailabilityAnalyzer`, which mirrors the current Unlimotion rules:

- incomplete `ContainsTasks` block the containing task;
- incomplete direct `BlockedByTasks` block the task;
- incomplete blockers from `ParentTasks` ancestors are inherited by child tasks;
- unsatisfied `CompletionCriteria` block completion, not startability;
- future `PlannedBeginDateTime` blocks startability, not completion availability;
- missing references are validation issues, but they are not treated as runtime blockers.

## Exit codes

- `0`: command completed successfully;
- `1`: validation/load issues were found;
- `2`: invalid arguments or unknown task id.
