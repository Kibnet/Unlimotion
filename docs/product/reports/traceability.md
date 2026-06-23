# STORM Traceability

Сгенерировано: 2026-06-23
Команда: `/storm:trace` sync after `/storm:bdd-implement SC-0011-001 executable step definitions`

## New Trace

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0011 | AC-0032 | SC-0011-001 | TS-0031 + SD-0022..SD-0025 | passing executable BDD slice from feature text |

## Existing Trace Preserved

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0011 | AC-0032 | SC-0011-001 | TS-0017 | passing auth-flow contract |
| ST-0011 | AC-0033 | SC-0011-002 | TS-0017, TS-0018, TS-0019, TS-0020 | passing contract/security/live API and SignalR evidence |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0022 | passing command/auth |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0028 + SD-0009..SD-0012 | passing executable BDD slice from feature text |
| ST-0014 | AC-0040 | SC-0014-002 | TS-0027 + SD-0005..SD-0008 | passing executable BDD slice from feature text |
| ST-0014 | AC-0040 | SC-0014-002 | TS-0025 | passing Git timer conflict-safety |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0023 | passing callback subset |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0029 + SD-0013..SD-0016 | passing executable BDD slice from feature text |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011, TS-0015 | desktop/update evidence |
| ST-0015 | AC-0042 | SC-0015-002 | TS-0015, TS-0024, TS-0026 + SD-0001..SD-0004 | passing project-contract coverage plus Browser Release build smoke; Android/iOS build smoke blocked by `NETSDK1147` |
| ST-0015 | AC-0043 | SC-0015-003 | TS-0011, TS-0015 | CI/README media evidence |
| ST-0016 | AC-0044 | SC-0016-001 | TS-0021 | passing error-toast UI evidence |
| ST-0016 | AC-0044 | SC-0016-001 | TS-0030 + SD-0017..SD-0021 | passing executable BDD slice from feature text |

## Internal/Orphan Candidate Trace

| Candidate | Code Units | Status | Next Action |
| --- | --- | --- | --- |
| CV-0007: proposed_attachment_workflow | `src/Unlimotion.Domain/Attachment.cs`; `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`; `src/Unlimotion.Server.ServiceModel/Attachment.cs`; `src/Unlimotion.Server/AppModelMapping.cs` | internal_orphan_contract_candidate | Future revisit only after new product decision. |

## Residual Gaps

`SC-0011-001` больше не имеет BDD-execution gap: сценарий связан с `TS-0017`, `TS-0031` и `SD-0022..SD-0025`. `CV-0003`, `CV-0004`, `CV-0005` и `CV-0006` сохраняют ранее созданный executable BDD trace.

Оставшиеся non-cover gaps: step definitions покрывают 6/45 scenarios, `SC-0011-002` остается passing server-storage scenario без step definitions, Android/iOS требуют отдельной environment/setup task из-за `NETSDK1147`, runtime/release evidence не заявляется, а full-suite validation имеет отдельный UI state/order risk.
