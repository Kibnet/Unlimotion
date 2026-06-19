# STORM BDD Lint

Сгенерировано: 2026-06-19
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0014-001 executable step definitions`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Required scenario tags | PASS | `SC-0014-001` содержит story/rule/need/test/status tags. |
| Scenario status | PASS | Draft scenarios отсутствуют. |
| Orphan scenarios | PASS | Orphan scenarios не добавлялись. |
| Scenario -> Test links | PASS | 45/45 scenarios linked. |
| Scenario -> Step Definition links | WARNING | 3/45 scenarios step-executable: `SC-0015-002`, `SC-0014-002`, `SC-0014-001`. |
| Declarative language | PASS | `SC-0014-001` описывает наблюдаемое command/auth behavior без реального Telegram API/polling в формулировке продукта. |
| Step definitions | PASS | `SD-0009..SD-0012` registered and linked to `SC-0014-001`; previous step definitions remain linked. |
| Test annotations | PASS | Test annotations не менялись. |
| CV-0007 product claim | PASS | Вариант B prevents promotion of attachment code to active story/scenario without new product decision. |
| Platform evidence | WARNING | Browser Release build smoke прошел; Android/iOS build smoke blocked by `NETSDK1147`; runtime release support не заявляется. |

## Предупреждения

1. Step definitions покрывают только `SC-0015-002`, `SC-0014-002` и `SC-0014-001`; repo-local runner не является full Cucumber-style engine.
2. `SC-0015-002` имеет project-contract coverage, Browser Release build smoke evidence и executable step-definition slice; Android/iOS runtime release support не заявляется.
3. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
