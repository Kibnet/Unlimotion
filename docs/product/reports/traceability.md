# STORM Traceability

Сгенерировано: 2026-06-28
Команда: `/storm:trace` sync after `/storm:bdd-implement SC-0005-002`

## New Trace

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0005 | AC-0014 | SC-0005-002 | TS-0033 + SD-0027..SD-0030 | passing executable BDD slice from feature text |

## Existing Trace Preserved

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0005 | AC-0014 | SC-0005-002 | TS-0006, TS-0013 | passing existing reset/filter/planning evidence |
| ST-0011 | AC-0032 | SC-0011-001 | TS-0017 | passing auth-flow contract |
| ST-0011 | AC-0032 | SC-0011-001 | TS-0031 + SD-0022..SD-0025 | passing executable BDD slice from feature text |
| ST-0011 | AC-0033 | SC-0011-002 | TS-0017, TS-0018, TS-0019, TS-0020 | passing contract/security/live API and SignalR evidence |
| ST-0011 | AC-0033 | SC-0011-002 | TS-0032 + SD-0022..SD-0024 + SD-0026 | passing executable BDD slice from feature text |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0022 | passing command/auth |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0028 + SD-0009..SD-0012 | passing executable BDD slice from feature text |
| ST-0014 | AC-0040 | SC-0014-002 | TS-0027 + SD-0005..SD-0008 | passing executable BDD slice from feature text |
| ST-0014 | AC-0040 | SC-0014-002 | TS-0025 | passing Git timer conflict-safety |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0023 | passing callback subset |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0029 + SD-0013..SD-0016 | passing executable BDD slice from feature text |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011, TS-0015 | desktop/update evidence |
| ST-0015 | AC-0042 | SC-0015-002 | TS-0015, TS-0024, TS-0026 + SD-0001..SD-0004 | passing project-contract coverage plus Browser/iOS/Android build smoke; runtime/release support не заявляется |
| ST-0015 | AC-0043 | SC-0015-003 | TS-0011, TS-0015 | CI/README media evidence |
| ST-0016 | AC-0044 | SC-0016-001 | TS-0021 | passing error-toast UI evidence |
| ST-0016 | AC-0044 | SC-0016-001 | TS-0030 + SD-0017..SD-0021 | passing executable BDD slice from feature text |

## Internal/Orphan Candidate Trace

| Candidate | Code Units | Status | Next Action |
| --- | --- | --- | --- |
| CV-0007: proposed_attachment_workflow | `src/Unlimotion.Domain/Attachment.cs`; `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`; `src/Unlimotion.Server.ServiceModel/Attachment.cs`; `src/Unlimotion.Server/AppModelMapping.cs` | internal_orphan_contract_candidate | Future revisit only after new product decision. |

## Residual Gaps

`SC-0005-002` больше не имеет BDD-execution gap: reset-filter scenario связан с `TS-0033` и step definitions `SD-0027..SD-0030`, при этом existing `TS-0006`/`TS-0013` trace сохранён.

Оставшиеся non-cover gaps: step definitions покрывают 8/45 scenarios; `SC-0005-001` и `SC-0005-003` остаются ближайшими ST-0005 candidates для дальнейшего executable BDD coverage. Browser/iOS/Android build smoke for `SC-0015-002` сохранён; runtime launch, emulator/device validation и release pipeline evidence не заявляются.
