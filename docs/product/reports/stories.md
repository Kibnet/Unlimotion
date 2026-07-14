# STORM Stories

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-implement SC-0007-002`

## Story Changes

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0007 | `AC-0020` поднят до `full`: existing `TS-0005`/`TS-0008` сохранены, `SC-0007-002` исполняется через `SD-0103..SD-0106` и `TS-0052`. | `TS-0005`, `TS-0008`, `TS-0052` |
| ST-0007 | Второй scenario стал step-executable; contract покрывает четыре picker routes и reciprocal links для parents/containing/blocked-by/blocked. | `StormTaskCardRelationsExecutableSpecTests` |

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0007 | `SC-0007-003` — completion criteria | Подготовить отдельную SPEC для executable BDD bridge. |
| BDD execution | Step definitions покрывают 27/45 scenarios. | Выбрать следующий high-value scenario. |

Full `Unlimotion.Test` остаётся непроверенным для текущего slice из-за предыдущего timeout 304 seconds; targeted BDD/UI evidence прошло.
