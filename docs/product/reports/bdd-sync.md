# STORM BDD Sync

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0015-001`

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 44/45 исполняемы через шаги |
| Новая связь | `SC-0015-001 -> TS-0069 -> SD-0171..SD-0174`; existing `TS-0011/TS-0015` сохранены |
| ST-0015 | PARTIAL: 2/3 сценария исполняемы через шаги |
| Production/feature/annotations/projects/workflows | изменений нет |

`TS-0069` связывает текущий Gherkin с неизменяемым контрактом desktop project/workflow и прошедшими startup/update/package UI-проверками. Full-suite PASS и published release не заявляются.
