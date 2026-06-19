# STORM Stories

Сгенерировано: 2026-06-19
Команда: `/storm:bdd-implement SC-0015-002 executable step definitions`

## Story Changes

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0015 | `AC-0042` сохраняет coverage `critical`: project contracts покрыты `TS-0024`, а `SC-0015-002` исполняется через `SD-0001..SD-0004` и `TS-0026`. Android/iOS build smoke blocked by `NETSDK1147`, поэтому runtime release support не заявляется. | `features/storm/st-0015-platform-shells.feature`, `src/Unlimotion.Test/StormPlatformShellExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/PlatformShellStepDefinitions.cs`, `TS-0024`, `TS-0026` |

## Product-Entry Candidate Update

| Candidate | Status | Evidence | Notes |
| --- | --- | --- | --- |
| CV-0007: attachment workflow | internal_orphan_contract_candidate | `src/Unlimotion.Domain/Attachment.cs`, `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`, `src/Unlimotion.Server.ServiceModel/Attachment.cs`, `src/Unlimotion.Server/AppModelMapping.cs` | Вариант B: backend/API code остается traceable, но active product story, AC, UI workflow или Gherkin scenario не создаются. |

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0014 / AC-0040 | Нет active gap. | Поддерживать `TS-0022`, `TS-0023`, `TS-0025` при изменениях Telegram bot. |
| ST-0015 / AC-0042 | Android/iOS build smoke заблокированы `NETSDK1147`; Browser build smoke не равен runtime/release evidence. | Отдельная environment/setup SPEC при необходимости Android/iOS build evidence; отдельная runtime/release SPEC при необходимости release support claims. |
| BDD execution | Step definitions покрывают только `SC-0015-002`. | Расширять executable step definitions отдельными SPEC по high-value scenarios; не создавать placeholder steps массово. |
| CV-0007 | Нет active story gap после Варианта B. | Future revisit only after new product decision. |
