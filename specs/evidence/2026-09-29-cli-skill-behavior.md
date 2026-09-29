# Frozen behavioral evidence

Audit only: baseline output below quotes the superseded skill behavior, not current operational instructions.

Runtime: Codex CLI 0.154.0; gpt-5.5; reasoning low; read-only; approval never; ephemeral; no tool calls. Both exit 0. Baseline replay uses Git HEAD (2472761d) skill; candidate uses working-tree skill. Prompt identical in both runs. Baseline replay session 01a0eef4-9d12-7790-ac02-7f0f12a71c94; candidate tool session 63935. CLI startup warnings omitted; model responses below are verbatim.

## Frozen prompt

```text
Synthetic skill evaluation only. Do not call any tools. Use the supplied skill text to answer these three cases, briefly:
A. The executable reports packageVersion 1.32.0 and matching JSON help/schema. Explicit tasks path is C:\SyntheticA, environment path is C:\SyntheticB. Describe the task-space choice and safe apply sequence.
B. The installed executable is an older release without version/context/search/apply inspect. User asks to edit a task. What do you do?
C. An apply createTask request with chosen ID timed out after sending. How do you reconcile? What if ordinary create lost its generated ID?
SKILL TEXT:
```

Invocation: concatenate this prompt with `(git show HEAD:.agents/skills/unlimotion-cli/SKILL.md) -join "`n"` for baseline replay, or `Get-Content -LiteralPath '.agents/skills/unlimotion-cli/SKILL.md' -Raw` for candidate; pipe to `codex exec -m gpt-5.5 -s read-only --ephemeral -`.

## Baseline response

```text
A. Use `C:\SyntheticA`. In v1.32.0 task-space priority is explicit non-empty `--tasks` first, then `UNLIMOTION_TASKS`, then desktop settings. Safe sequence: pin the resolved executable, verify `version/help` identity and matching packageVersion/schema, run `context --tasks C:\SyntheticA --format json`, carry the returned absolute `tasksPath` as explicit `--tasks` for every call, read current state, build request from schema/example, run `apply --dry-run`, verify `success`, `didMutate=false`, `changedTaskIds/operationResults`, then send the exact same request once, read back all changed/created IDs.

B. Do not guess the edit format. If it is older 1.31.1, use only the documented 1.31.1 route/reference. If version/capabilities are unknown and there is no inspect/search/context/apply contract, stop before mutation and report that the installed CLI is too old/unsupported for safe editing. Ask to upgrade or provide a supported binary/path.

C. Do not repeat blindly. Wait for `.unlimotion.lock` to clear, then read authoritative state through the same CLI and task-space. For confirmed v1.32.0, run `apply inspect --request <same exact file> --tasks <pinned path> --format json`, preserving byte-identical request text, then read affected IDs. Interpret `receiptMatched`, `desiredStatePresent`, `readyForPreview`, and reconciliation flags as evidence to reconcile, not as permission to resend automatically. If ordinary `create` lost its generated ID, and there is no unambiguous read-back, outcome remains unknown; do not read task JSON directly and do not repeat `create`.
```

## Candidate response

```text
A. Use the explicit tasks path: `C:\SyntheticA`. It has priority over `UNLIMOTION_TASKS=C:\SyntheticB`. Safe sequence: pin the executable path, verify `version --format json` and JSON help/schema from that same binary, run `context --tasks C:\SyntheticA --format json`, then pass the returned absolute `tasksPath` as `--tasks` to every related command. For apply: read the task first, build the request from `apply schema/example`, run `apply --dry-run`, verify `success`, `changedTaskIds`/`operationResults`, and `didMutate=false`, then send the exact same request once, then read back all changed/created IDs.

B. Stop before editing. Do not fall back to legacy commands and do not auto-update the tool. Tell the user the installed CLI lacks the required current contract and propose updating/installing the current CLI separately before any task mutation.

C. Do not repeat the mutation blindly. Wait for `.unlimotion.lock` to clear, then read authoritative state through the CLI. For `apply createTask` with a chosen `newTaskId`, run `apply inspect --request <same file> --tasks <pinned path> --format json`, preserving the request bytes exactly, then read the affected/chosen ID. Interpret `receiptMatched`, `desiredStatePresent`, and reconciliation flags carefully; inspect can confirm pieces but still requires read-back.

If ordinary `create` lost its generated ID, absence from a limited search is not proof it failed. Without an unambiguous read-back, stop and report the outcome as unknown; do not read task files directly and do not repeat `create`.
```

## Decision matrix

| Case | Expected candidate | Observed before | Observed after | Decision |
| --- | --- | --- | --- | --- |
| A | Explicit task-space, pinned identity, preview, once, read-back | Preserved safe sequence | Preserved safe sequence | PASS, no regression |
| B | Stop before editing, separate update, no legacy fallback | Documented legacy route permitted | Stop; no fallback or auto-update | PASS, requested behavior change |
| C | Inspect exact request + read-back; lost ordinary create remains unknown | Safe reconciliation/no retry | Safe reconciliation/no retry | PASS, no regression |

