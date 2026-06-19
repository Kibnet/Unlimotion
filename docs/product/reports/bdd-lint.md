# STORM BDD Lint

Сгенерировано: 2026-06-19
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0015-002 executable step definitions`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Required scenario tags | PASS | `SC-0015-002` содержит story/rule/need/test/status tags. |
| Scenario status | PASS | Draft scenarios отсутствуют. |
| Orphan scenarios | PASS | Orphan scenarios не добавлялись. |
| Scenario -> Test links | PASS | 45/45 scenarios linked. |
| Scenario -> Step Definition links | WARNING | 1/45 scenarios step-executable; first slice is `SC-0015-002`. |
| Declarative language | PASS | `SC-0015-002` описывает поведение без деталей test implementation; build evidence вынесен в artifact evidence, а не в формулировку продукта. |
| Step definitions | PASS | `SD-0001..SD-0004` registered and linked to `SC-0015-002`. |
| Test annotations | PASS | Test annotations не менялись. |
| CV-0007 product claim | PASS | Вариант B prevents promotion of attachment code to active story/scenario without new product decision. |
| Platform evidence | WARNING | Browser Release build smoke прошел; Android/iOS build smoke blocked by `NETSDK1147`; runtime release support не заявляется. |

## Предупреждения

1. Step definitions покрывают только `SC-0015-002`; repo-local runner не является full Cucumber-style engine.
2. `SC-0015-002` имеет project-contract coverage, Browser Release build smoke evidence и executable step-definition slice; Android/iOS runtime release support не заявляется.
3. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
