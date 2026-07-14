# STORM Stories

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-implement SC-0007-001`

## Story Changes

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0007 | `AC-0019` поднят до `full`: existing `TS-0005` сохранён, `SC-0007-001` исполняется через `SD-0099..SD-0102` и `TS-0051`. | `TS-0005`, `TS-0051` |
| ST-0007 | Первый scenario стал step-executable; contract покрывает desktop и 360/390/430 narrow widths с parent relation editor. | `StormTaskCardLayoutExecutableSpecTests` |

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0007 | `SC-0007-002` — relation blocks | Подготовить отдельную SPEC для executable BDD bridge. |
| ST-0007 | `SC-0007-003` — completion criteria | Подготовить отдельную SPEC после relation scenario. |
| BDD execution | Step definitions покрывают 26/45 scenarios. | Выбрать следующий high-value scenario. |

Full `Unlimotion.Test` остаётся непроверенным для текущего slice из-за timeout 304 seconds; targeted BDD/UI evidence прошло.
