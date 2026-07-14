# STORM Coverage Analysis

Сгенерировано: 2026-07-14
Команда: `/storm:cover -> /storm:bdd-implement SC-0010-002`

`SC-0010-002` исполняется через `TS-0061` и `SD-0139..SD-0142`. Contract подтверждает SSH/token remote, SSH credentials и configured key storage на временных local Git repositories. Production code, `.feature` и existing annotations не менялись.

| Метрика | Значение |
| --- | --- |
| Passing scenarios | 36 |
| Step definitions | 142 |
| Step-executable scenarios | 36/45 |
| ST-0010 executable coverage | 2/4 scenarios |
| Full suite gate | не подтверждён: предыдущий timeout без summary |

| Проверка | Результат |
| --- | --- |
| Build Release | прошло с 69 existing warnings, errors 0 |
| `StormGitRemoteAuthExecutableSpecTests` | прошло 1/1 |
| `BackupViaGitServiceTests` SSH/token/key-storage | прошло 4/4 адресных метода |
| Artifact validator | 0 errors, 15 known warnings, 36/45 executable |

Оставшиеся gaps: 9 scenarios without step definitions; следующий внутри `ST-0010`: `SC-0010-003`.
