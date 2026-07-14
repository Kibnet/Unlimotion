# STORM BDD Sync

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-sync` after `/storm:bdd-implement SC-0009-002`

| Проверка | Результат |
| --- | --- |
| Scenario -> Test links | 45/45 |
| Scenario -> Step Definition links | 33/45 |
| Новая связь | `SC-0009-002 -> TS-0058 -> SD-0127..SD-0130`; existing `TS-0003/TS-0014` сохранены |
| ST-0009 | PARTIAL: 2/3 scenarios step-executable |
| Production/feature/annotations | no changes |

`TS-0058` связывает текущий Gherkin с регрессионными проверками восстановления reverse links, availability и status model. Full-suite PASS не заявляется.
