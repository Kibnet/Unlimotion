# STORM Stories

Сгенерировано: 2026-06-28
Команда: `/storm:bdd-implement SC-0005-003`

## Story Changes

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0005 | `AC-0015` сохраняет coverage `critical`: existing emoji filter evidence `TS-0006` сохранено, а `SC-0005-003` теперь исполняется через `SD-0031..SD-0034` и `TS-0034`. Production code, `.feature` wording и existing test annotations не менялись. | `features/storm/st-0005-search-and-filters.feature`, `src/Unlimotion.Test/StormEmojiFilterExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/EmojiFilterStepDefinitions.cs`, `src/Unlimotion.Test/EmojiFilterUiContract.cs`, `TS-0006`, `TS-0034` |
| ST-0005 | `AC-0014` сохраняет coverage `critical`: reset/filter/planning evidence `TS-0006`, `TS-0013` и executable `TS-0033` сохранены. | `TS-0006`, `TS-0013`, `TS-0033` |
| ST-0011 | `AC-0032`/`AC-0033` сохраняют executable BDD evidence `TS-0031`/`TS-0032`. | `TS-0017..TS-0020`, `TS-0031`, `TS-0032` |
| ST-0016 | `AC-0044` сохраняет executable BDD evidence `TS-0030`. | `TS-0021`, `TS-0030` |
| ST-0014 | `AC-0039` и `AC-0040` сохраняют executable BDD evidence across all three scenarios. | `TS-0022`, `TS-0028`, `TS-0023`, `TS-0029`, `TS-0025`, `TS-0027` |
| ST-0015 | `AC-0042` сохраняет project-contract + build-smoke evidence; runtime release support не заявляется. | `TS-0024`, `TS-0026` |

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0005 / AC-0015 | Нет BDD-execution gap; `SC-0005-003` step-executable. | Поддерживать `TS-0006` и `TS-0034` при изменениях emoji filter flow. |
| ST-0005 / AC-0013 | `SC-0005-001` имеет linked tests, но не имеет step definitions. | Следующая SPEC может взять search/fuzzy scenario. |
| BDD execution | Step definitions покрывают 9/45 scenarios. | Расширять executable step definitions отдельными SPEC по high-value scenarios. |
| CV-0007 | Нет active story gap после Варианта B. | Future revisit only after new product decision. |
