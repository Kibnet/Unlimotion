# STORM Coverage Analysis

Сгенерировано: 2026-07-14
Команда: `/storm:cover -> /storm:bdd-implement SC-0013-001`

`SC-0013-001` исполняется через `TS-0067` и `SD-0163..SD-0166`. Contract подтверждает Markdown outline с descriptions, применённые settings и tree-command copy поддерева. Production code, `.feature` и existing annotations не менялись.

| Метрика | Значение |
| --- | --- |
| Passing scenarios | 42 |
| Step definitions | 166 |
| Step-executable scenarios | 42/45 |
| ST-0013 executable coverage | 1/2 scenarios |
| Full suite gate | не подтверждён: предыдущий timeout без summary |

| Проверка | Результат |
| --- | --- |
| Build Release | прошло с 69 existing warnings, errors 0 |
| `StormOutlineClipboardCopyExecutableSpecTests` | прошло 1/1 |
| Markdown format + ViewModel settings + tree-command UI | прошло 3/3 адресных проверок |
| Artifact validator | 0 errors, 16 known warnings, 42/45 executable |

Оставшиеся gaps: 3 scenarios without step definitions; следующий: `SC-0013-002`.
