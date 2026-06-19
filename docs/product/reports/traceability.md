# STORM Traceability

Сгенерировано: 2026-06-19
Команда: `/storm:trace` sync after `/storm:bdd-implement SC-0014-003 executable step definitions`

## New Trace

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0029 + SD-0013..SD-0016 | passing executable BDD slice from feature text |

## Existing Trace Preserved

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0022 | passing command/auth |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0028 + SD-0009..SD-0012 | passing executable BDD slice from feature text |
| ST-0014 | AC-0040 | SC-0014-002 | TS-0027 + SD-0005..SD-0008 | passing executable BDD slice from feature text |
| ST-0014 | AC-0040 | SC-0014-002 | TS-0025 | passing Git timer conflict-safety |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0023 | passing callback subset |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011, TS-0015 | desktop/update evidence |
| ST-0015 | AC-0042 | SC-0015-002 | TS-0015, TS-0024, TS-0026 + SD-0001..SD-0004 | passing project-contract coverage plus Browser Release build smoke; Android/iOS build smoke blocked by `NETSDK1147` |
| ST-0015 | AC-0043 | SC-0015-003 | TS-0011, TS-0015 | CI/README media evidence |

## Internal/Orphan Candidate Trace

| Candidate | Code Units | Status | Next Action |
| --- | --- | --- | --- |
| CV-0007: proposed_attachment_workflow | `src/Unlimotion.Domain/Attachment.cs`; `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`; `src/Unlimotion.Server.ServiceModel/Attachment.cs`; `src/Unlimotion.Server/AppModelMapping.cs` | internal_orphan_contract_candidate | Future revisit only after new product decision. |

## Residual Gaps

У `CV-0003` больше нет traceability gap: `SC-0014-001` имеет и unit evidence `TS-0022`, и executable BDD evidence `TS-0028`. `CV-0004` больше не имеет BDD-execution gap внутри `ST-0014`: `SC-0014-002` имеет `TS-0025/TS-0027`, а `SC-0014-003` имеет `TS-0023/TS-0029`. `ST-0015 / AC-0042` сохраняет Browser build smoke evidence и executable step-definition trace для `SC-0015-002`, но Android/iOS build smoke остаются environment-blocked.

Оставшиеся non-cover gaps: step definitions покрывают 4/45 scenarios, Android/iOS требуют отдельной environment/setup task из-за `NETSDK1147`, а runtime/release evidence не заявляется.
