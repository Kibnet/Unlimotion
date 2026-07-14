# STORM Coverage Analysis

Сгенерировано: 2026-07-14
Команда: `/storm:cover -> /storm:bdd-implement SC-0010-003`

`SC-0010-003` исполняется через `TS-0062` и `SD-0143..SD-0146`. Contract подтверждает file-level и field-level Git conflict resolution before commit/push на временных local Git repositories. Production code, `.feature` и existing annotations не менялись.

| Метрика | Значение |
| --- | --- |
| Passing scenarios | 37 |
| Step definitions | 146 |
| Step-executable scenarios | 37/45 |
| ST-0010 executable coverage | 3/4 scenarios |
| Full suite gate | не подтверждён: предыдущий timeout без summary |

| Проверка | Результат |
| --- | --- |
| Build Release | прошло с 69 existing warnings, errors 0 |
| `StormGitConflictResolutionExecutableSpecTests` | прошло 1/1 |
| `BackupViaGitServiceTests` file/field conflict resolution | прошло 2/2 адресных метода |
| Artifact validator | 0 errors, 15 known warnings, 37/45 executable |

Оставшиеся gaps: 8 scenarios without step definitions; следующий внутри `ST-0010`: `SC-0010-004`.
