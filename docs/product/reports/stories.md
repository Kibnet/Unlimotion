# STORM Stories

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-implement SC-0006-003`

## Story Changes

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0006 | `AC-0018` поднят до `full`: existing `TS-0005`/`TS-0013` evidence сохранено, а `SC-0006-003` теперь исполняется через `SD-0095..SD-0098` и `TS-0050`. | `TS-0005`, `TS-0013`, `TS-0050` |
| ST-0006 | Все три scenarios теперь step-executable. | `TS-0048`, `TS-0049`, `TS-0050` |

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0006 | Нет BDD-execution gap. | Поддерживать `TS-0048..TS-0050` при изменениях planning/wanted/importance flow. |
| BDD execution | Step definitions покрывают 25/45 scenarios. | Выбрать следующий high-value scenario вне ST-0006. |
