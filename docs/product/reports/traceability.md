# STORM Traceability

Сгенерировано: 2026-06-17
Команда: `/storm:trace` sync after `/storm:cover CV-0005 ST-0015`

## New Trace

| Story | AC | Rule | Scenario | Test | Code Units | Status |
| --- | --- | --- | --- | --- | --- | --- |
| ST-0015 | AC-0042 | GR-042 | SC-0015-002 | TS-0024 | CU-0015 | passing project-contract coverage |

## Existing Trace Preserved

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0022 | passing |
| ST-0014 | AC-0040 | SC-0014-002 | none | draft timer gap |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0023 | passing callback subset |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011, TS-0015 | desktop/update evidence |
| ST-0015 | AC-0043 | SC-0015-003 | TS-0011, TS-0015 | CI/README media evidence |

## Evidence

- `src/Unlimotion.Test/PlatformShellProjectContractTests.cs` -> `PlatformShellProjectContractTests`
- `src/Unlimotion.Android/Unlimotion.Android.csproj` -> `net10.0-android`, shared UI reference, native Git assets
- `src/Unlimotion.Browser/Unlimotion.Browser.csproj` -> `net10.0-browser`, shared UI reference
- `src/Unlimotion.iOS/Unlimotion.iOS.csproj` -> `net10.0-ios`, shared UI reference
- `features/storm/st-0015-platform-shells.feature` -> `SC-0015-002`

## Residual Gap

`SC-0014-002` remains intentionally unlinked because it represents the Git timer/conflict-safety part that cannot be covered as current behavior. Android/browser/iOS runtime smoke and release pipeline evidence are also outside `TS-0024`; current claim is project-contract coverage only.
