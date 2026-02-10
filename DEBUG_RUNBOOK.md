# Debug Runbook (.NET + MCP + CoreCLR)

## Purpose
Portable playbook for debugging .NET applications and tests through an MCP debug server with CoreCLR launch profiles in VS Code.

## Prerequisites
1. MCP debug server is running and exposes:
   - health endpoint: `<MCP_BASE_URL>/health`
   - JSON-RPC endpoint: `<MCP_BASE_URL>/mcp`
2. `.vscode/launch.json` contains CoreCLR profiles for:
   - app debug;
   - test debug (recommended: one reusable profile).
3. `preLaunchTask` builds the target project before attach/launch.

## Recommended Launch Profiles
1. App profile:
   - `type: coreclr`
   - `request: launch`
   - `program: <path to app dll/exe>`
2. Test profile (single reusable):
   - `type: coreclr`
   - `request: launch`
   - `program: <path to test exe>`
   - `args: ["--filter-uid", "<TEST_UID>"]`

## Pre-Flight Checklist
Run this before any debug session.

1. Verify MCP health:
   - `Invoke-WebRequest <MCP_BASE_URL>/health -UseBasicParsing`
2. Verify target config exists:
   - `debug_listConfigs`
3. Reset stale session state:
   - `debug_stop`
4. Verify breakpoint state:
   - `debug_listBreakpoints`
   - remove stale breakpoints if needed.

## Standard Debug Workflow
1. Start by config:
   - `debug_startWithConfig` with `configName = "<APP_OR_TEST_CONFIG_NAME>"`
2. Confirm session is active and paused/running state:
   - `debug_getStatus`
3. Inspect runtime in this order:
   - `debug_getStatus`
   - `debug_getStackTrace`
   - `debug_getVariables`
   - `debug_evaluate` (read-only expressions only)
4. Navigate execution:
   - prefer `debug_continue` + strategic breakpoints
   - use `debug_stepOver`/`debug_stepInto`/`debug_stepOut` only when needed
5. End session:
   - remove temporary breakpoints
   - `debug_stop`

## Test Debug: Selecting One Case
Use one test launch profile and only update its `--filter-uid` value.

1. Build tests.
2. List test names:
   - `dotnet test --project <tests.csproj> --list-tests`
3. Generate discovery diagnostics from test executable:
   - `& "<path-to-tests.exe>" --list-tests --diagnostic --diagnostic-output-directory <tmp-dir> --disable-logo --no-progress`
4. Extract UID for target case:
   - `rg -n "DisplayName = <Exact test display name>" <tmp-dir>\\*.diag -S`
   - copy value from `Uid = TestNodeUid { Value = ... }`
5. Update test profile in `.vscode/launch.json`:
   - `args[1] = "<copied uid>"`
6. Launch test config with `debug_startWithConfig`.

Note: UIDs are not stable across metadata changes (argument order, generated source changes, test structure refactors).

## Breakpoint Strategy
1. Set key breakpoints before starting when the path is known.
2. Prefer conditional breakpoints in noisy loops and hot paths.
3. Keep the number of active breakpoints small and task-focused.
4. Always verify placement with `debug_listBreakpoints`.

## Productive Stepping Rules
1. Prefer breakpoints over line-by-line stepping.
2. In loops, use conditional breakpoints or post-loop breakpoints.
3. After each continue/step, re-check position via `debug_getStackTrace`.
4. Capture only decision-making values (branch flags, counters, computed outputs).

## Error Map
Use this map for fast recovery.

| Symptom | Likely cause | Action |
|---|---|---|
| `No active debug session` | Session not started or already stopped | Start with `debug_startWithConfig` |
| `No active stack frame` / cannot inspect variables | Execution is not paused | Set breakpoint and `debug_continue`, or `debug_pause` |
| `debug_continue` / `debug_stepOver` connection errors | VS Code debug backend lost connection | `debug_stop`, restart C# Dev Kit backend processes, start fresh session |
| Config not found | Wrong name or missing `launch.json` entry | Check `debug_listConfigs`, fix config name or add profile |
| Breakpoint not hit | Wrong path/line or wrong process/config | Verify file/line, launch profile, and breakpoint list |

## Fallback If MCP Handshake/Tooling Fails
1. Use direct JSON-RPC calls to `<MCP_BASE_URL>/mcp`:
   - `tools/list`
   - `tools/call`
2. Keep the same launch config names and workflow.

## Debug Playbooks
Use these short templates for common investigations.

### Find A Bug
1. `debug_listConfigs`
2. Set one breakpoint at the suspected location.
3. `debug_startWithConfig`
4. On hit: inspect stack/locals and evaluate hypothesis.
5. Step only around suspicious lines.
6. Stop and record root cause.

### Trace Execution Flow
1. Set breakpoints at key function entry points.
2. Start session and `debug_continue` between breakpoints.
3. Capture stack trace at each stop.
4. Record actual path and skipped branches.

### Compare Before/After State
1. Set breakpoint before transformation.
2. Set breakpoint after transformation.
3. Inspect input state at first breakpoint.
4. Continue and inspect output state at second breakpoint.
5. Compare deltas and isolate divergence point.

## Session Hygiene
1. Remove all temporary breakpoints.
2. Stop active debug session.
3. Verify breakpoint list is empty.

## Optional VS Code Stability Settings
```json
{
  "dotnet.useLegacyDotnetResolution": true,
  "csharp.experimental.debug.hotReload": false,
  "csharp.debug.hotReloadOnSave": false
}
```
