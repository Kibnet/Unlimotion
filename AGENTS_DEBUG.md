# AGENTS Instructions

## Scope
This file defines agent routing rules. It intentionally does not duplicate debugging procedures.

## Debugging Rule
- For any runtime/test debugging task in a .NET + CoreCLR + VS Code MCP setup, use `DEBUG_RUNBOOK.md` as the single source of truth.
- Apply the runbook when the request includes at least one of:
  - launch/start debugging;
  - inspect stack/variables/evaluate expressions;
  - set/remove breakpoints;
  - debug failing tests;
  - troubleshoot MCP debug session issues.

## Non-Debug Tasks
- For non-debug tasks (code edits, refactors, docs, build/test without debugger), `DEBUG_RUNBOOK.md` is optional and should not drive the workflow.
