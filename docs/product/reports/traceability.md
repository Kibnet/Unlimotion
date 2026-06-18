# STORM Traceability

Сгенерировано: 2026-06-18
Команда: `/storm:trace` sync after `/storm:platform-runtime-validation ST-0015`

## New Trace

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0015 | AC-0042 | SC-0015-002 | TS-0015, TS-0024 | passing project-contract coverage plus Browser Release build smoke; Android/iOS build smoke blocked by `NETSDK1147` |

## Existing Trace Preserved

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0022 | passing command/auth |
| ST-0014 | AC-0040 | SC-0014-002 | TS-0025 | passing Git timer conflict-safety |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0023 | passing callback subset |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011, TS-0015 | desktop/update evidence |
| ST-0015 | AC-0043 | SC-0015-003 | TS-0011, TS-0015 | CI/README media evidence |

## Internal/Orphan Candidate Trace

| Candidate | Code Units | Status | Next Action |
| --- | --- | --- | --- |
| CV-0007: proposed_attachment_workflow | `src/Unlimotion.Domain/Attachment.cs`; `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`; `src/Unlimotion.Server.ServiceModel/Attachment.cs`; `src/Unlimotion.Server/AppModelMapping.cs` | internal_orphan_contract_candidate | Future revisit only after new product decision. |

## Residual Gaps

У `CV-0004` больше нет traceability gap. `ST-0015 / AC-0042` получил Browser build smoke evidence, но Android/iOS build smoke остаются environment-blocked.

Оставшиеся non-cover gaps: executable `step_definitions` отсутствуют, Android/iOS требуют отдельной environment/setup task из-за `NETSDK1147`, а runtime/release evidence не заявляется.
