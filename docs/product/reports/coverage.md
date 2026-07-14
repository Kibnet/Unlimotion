# STORM Coverage Analysis

Сгенерировано: 2026-07-14
Команда: `/storm:cover -> /storm:bdd-implement SC-0015-001`

`SC-0015-001` исполняется через `TS-0069` и `SD-0171..SD-0174`. Контракт подтверждает неизменяемые маркеры WinExe/Avalonia/Velopack и packaging workflow, а существующие headless-тесты подтверждают startup, deferred update attach и package compatibility. Production code, `.feature`, проекты, workflows и existing annotations не менялись.

| Метрика | Значение |
| --- | --- |
| Прошедшие сценарии | 44 |
| Определения шагов | 174 |
| Сценарии, исполняемые через шаги | 44/45 |
| Исполняемое покрытие ST-0015 | 2/3 сценария |
| Full suite gate | не подтверждён: предыдущий timeout без summary |

| Проверка | Результат |
| --- | --- |
| Test Release build | прошло с 69 существующими предупреждениями, ошибок 0 |
| Desktop Release build | прошло, ошибок 0 |
| `StormDesktopShellPackagingExecutableSpecTests` | прошло 1/1 |
| Startup/update/package UI | прошло 3/3 адресных проверок |
| Artifact validator | 0 errors, 17 известных предупреждений, 44/45 исполняемых сценариев |

Оставшийся разрыв: `SC-0015-003` без определений шагов; следующий `/storm:cover` candidate: `SC-0015-003`.
