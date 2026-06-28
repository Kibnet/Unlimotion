# STORM BDD Lint

Сгенерировано: 2026-06-28
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0005-003`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Required scenario tags | PASS | `SC-0005-003` сохраняет story/rule/need/constraint/test/status tags из `.feature`; новый `TS-0034` добавлен в artifacts без изменения `.feature` tags. |
| Scenario status | PASS | Draft scenarios отсутствуют; `SC-0005-003` отмечен passing по фактическому TUnit evidence. |
| Orphan scenarios | PASS | Orphan scenarios не добавлялись. |
| Scenario -> Test links | PASS | 45/45 scenarios linked. |
| Scenario -> Step Definition links | WARNING | 9/45 scenarios step-executable: `SC-0005-002`, `SC-0005-003`, `SC-0011-001`, `SC-0011-002`, `SC-0014-001`, `SC-0014-002`, `SC-0014-003`, `SC-0015-002`, `SC-0016-001`. |
| Declarative language | PASS | `SC-0005-003` остается product-level emoji filter outcome; UI mechanics скрыты в reusable test contract. |
| Step definitions | PASS | `SD-0031..SD-0034` registered and linked only to `SC-0005-003`. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| CV-0007 product claim | PASS | Вариант B prevents promotion of attachment code to active story/scenario without new product decision. |

## Предупреждения

1. Step definitions покрывают только 9 selected scenarios; repo-local runner не является full Cucumber-style engine.
2. Validator warning: duplicate Given step text across `SD-0009`, `SD-0013`, `SD-0022`, `SD-0027`, `SD-0031`; это intentional reuse of shared task-set context.
3. Validator warning: duplicate And step text across `SD-0028`, `SD-0032`; это intentional reuse of ST-0005 story context.
4. `SC-0005-001` пока не step-executable, хотя имеет linked tests.
5. `SC-0015-002` имеет project-contract coverage, Browser Release build smoke, iOS Debug build smoke and Android Debug build smoke; runtime release support не заявляется.
6. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
