# STORM BDD Lint

Сгенерировано: 2026-06-19
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0014-003 executable step definitions`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Required scenario tags | PASS | `SC-0014-003` сохраняет story/rule/need/test/status tags; новый `TS-0029` добавлен как Scenario -> Test link в artifacts без изменения `.feature` tags. |
| Scenario status | PASS | Draft scenarios отсутствуют. |
| Orphan scenarios | PASS | Orphan scenarios не добавлялись. |
| Scenario -> Test links | PASS | 45/45 scenarios linked. |
| Scenario -> Step Definition links | WARNING | 4/45 scenarios step-executable: `SC-0015-002`, `SC-0014-002`, `SC-0014-001`, `SC-0014-003`. |
| Declarative language | PASS | `SC-0014-003` описывает наблюдаемое callback behavior без реального Telegram API/polling/network в формулировке продукта. |
| Step definitions | PASS | `SD-0013..SD-0016` registered and linked to `SC-0014-003`; previous step definitions remain linked. |
| Test annotations | PASS | Test annotations не менялись. |
| CV-0007 product claim | PASS | Вариант B prevents promotion of attachment code to active story/scenario without new product decision. |
| Platform evidence | WARNING | Browser Release build smoke прошел; Android/iOS build smoke blocked by `NETSDK1147`; runtime release support не заявляется. |

## Предупреждения

1. Step definitions покрывают только `SC-0015-002`, `SC-0014-002`, `SC-0014-001` и `SC-0014-003`; repo-local runner не является full Cucumber-style engine.
2. `SD-0009` и `SD-0013` используют один общий Given step text для Telegram scenarios; это intentional reuse of product wording, не placeholder и не orphan step.
3. `SC-0015-002` имеет project-contract coverage, Browser Release build smoke evidence и executable step-definition slice; Android/iOS runtime release support не заявляется.
4. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
