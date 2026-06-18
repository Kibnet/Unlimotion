# STORM Traceability

Сгенерировано: 2026-06-18
Команда: `/storm:trace` sync after `/storm:bdd-implement ST-0014 Telegram Git timer conflict-safety`

## New Trace

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0014 | AC-0040 | SC-0014-002 | TS-0025 | passing Git timer conflict-safety |

## Existing Trace Preserved

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0022 | passing command/auth |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0023 | passing callback subset |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011, TS-0015 | desktop/update evidence |
| ST-0015 | AC-0042 | SC-0015-002 | TS-0015, TS-0024 | passing project-contract coverage |
| ST-0015 | AC-0043 | SC-0015-003 | TS-0011, TS-0015 | CI/README media evidence |

## Internal/Orphan Candidate Trace

| Candidate | Code Units | Status | Next Action |
| --- | --- | --- | --- |
| CV-0007: proposed_attachment_workflow | `src/Unlimotion.Domain/Attachment.cs`; `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`; `src/Unlimotion.Server.ServiceModel/Attachment.cs`; `src/Unlimotion.Server/AppModelMapping.cs` | internal_orphan_contract_candidate | Future revisit only after new product decision. |

## Residual Gaps

У `CV-0004` больше нет traceability gap.

Оставшиеся non-cover gaps: executable `step_definitions` отсутствуют, а Android/browser/iOS runtime release evidence не заявляется.
