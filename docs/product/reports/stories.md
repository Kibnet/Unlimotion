# STORM Stories

Сгенерировано: 2026-06-28
Команда: `/storm:bdd-implement SC-0005-002`

## Story Changes

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0005 | `AC-0014` сохраняет coverage `critical`: existing reset/filter/planning evidence `TS-0006` и `TS-0013` сохранено, а `SC-0005-002` теперь исполняется через `SD-0027..SD-0030` и `TS-0033`. Production code, `.feature` wording и existing test annotations не менялись. | `features/storm/st-0005-search-and-filters.feature`, `src/Unlimotion.Test/StormFilterResetExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/FilterResetStepDefinitions.cs`, `src/Unlimotion.Test/FilterResetUiContract.cs`, `TS-0006`, `TS-0013`, `TS-0033` |
| ST-0011 | `AC-0033` сохраняет coverage `full`: CRUD/SignalR contract, security regression, live SignalR и live ServiceStack API покрыты `TS-0017..TS-0020`, а `SC-0011-002` исполняется через `SD-0022..SD-0024`, `SD-0026` и `TS-0032`. | `TS-0017`, `TS-0018`, `TS-0019`, `TS-0020`, `TS-0032` |
| ST-0011 | `AC-0032` сохраняет coverage `critical`: login/register/refresh-token contract покрыт `TS-0017`, а `SC-0011-001` исполняется через `SD-0022..SD-0025` и `TS-0031`. | `TS-0017`, `TS-0031` |
| ST-0016 | `AC-0044` сохраняет coverage `full`: error-toast rendering and close UX покрыт `TS-0021`, а `SC-0016-001` исполняется через `SD-0017..SD-0021` и `TS-0030`. | `TS-0021`, `TS-0030` |
| ST-0014 | `AC-0039` сохраняет coverage `full`, `AC-0040` сохраняет coverage `critical`; все три `ST-0014` scenarios step-executable. | `TS-0022`, `TS-0028`, `TS-0023`, `TS-0029`, `TS-0025`, `TS-0027` |
| ST-0015 | `AC-0042` сохраняет coverage `critical`: project contracts покрыты `TS-0024`, а `SC-0015-002` исполняется через `SD-0001..SD-0004` и `TS-0026`. Browser/iOS/Android build smoke evidence есть; runtime release support не заявляется. | `TS-0024`, `TS-0026` |

## Product-Entry Candidate Update

| Candidate | Status | Evidence | Notes |
| --- | --- | --- | --- |
| CV-0007: attachment workflow | internal_orphan_contract_candidate | `src/Unlimotion.Domain/Attachment.cs`, `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`, `src/Unlimotion.Server.ServiceModel/Attachment.cs`, `src/Unlimotion.Server/AppModelMapping.cs` | Вариант B: backend/API code remains traceable, but active product story, AC, UI workflow or Gherkin scenario are not created. |

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0005 / AC-0014 | Нет BDD-execution gap; `SC-0005-002` step-executable. | Поддерживать `TS-0006`, `TS-0013` и `TS-0033` при изменениях filter reset flow. |
| ST-0005 / AC-0013, AC-0015 | `SC-0005-001` и `SC-0005-003` have linked tests but no step definitions. | Следующая SPEC может взять search/fuzzy or emoji filter scenario. |
| ST-0015 / AC-0042 | Browser/iOS/Android build smoke прошли; это не равно runtime/release evidence. | Отдельная runtime/release SPEC нужна только для launch/package/release support claims. |
| BDD execution | Step definitions покрывают 8/45 scenarios. | Расширять executable step definitions отдельными SPEC по high-value scenarios; не создавать placeholder steps массово. |
| CV-0007 | Нет active story gap после Варианта B. | Future revisit only after new product decision. |
