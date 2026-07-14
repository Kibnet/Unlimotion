# STORM Coverage Analysis

Сгенерировано: 2026-07-14
Команда: `/storm:cover -> /storm:bdd-implement SC-0013-002`

`SC-0013-002` исполняется через `TS-0068` и `SD-0167..SD-0170`. Contract подтверждает parse preview, confirmation и создание дерева под выбранной задачей. Production code, `.feature` и existing annotations не менялись.

| Метрика | Значение |
| --- | --- |
| Passing scenarios | 43 |
| Step definitions | 170 |
| Step-executable scenarios | 43/45 |
| ST-0013 executable coverage | 2/2 scenarios |
| Full suite gate | не подтверждён: предыдущий timeout без summary |

| Проверка | Результат |
| --- | --- |
| Build Release | прошло с 69 existing warnings, errors 0 |
| `StormOutlineClipboardPasteExecutableSpecTests` | прошло 1/1 |
| Parser + ViewModel preview/tree + tree-command UI | прошло 3/3 адресных проверок |
| Artifact validator | 0 errors, 17 known warnings, 43/45 executable |

Оставшиеся gaps: 2 scenarios without step definitions; следующий: `SC-0015-001`.
