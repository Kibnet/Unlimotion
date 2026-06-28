# STORM BDD Lint

Сгенерировано: 2026-06-28
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0001-001`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0001-001` additionally has `TS-0036`. |
| Scenario -> Step Definition links | WARNING | 11/45 scenarios step-executable. |
| ST-0001 | WARNING | `SC-0001-001` step-executable; `SC-0001-002` и `SC-0001-003` remain linked-existing-tests only. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |

## Предупреждения

1. Step definitions покрывают только 11/45 scenarios; repo-local runner не является full Cucumber-style engine.
2. Validator may report duplicate Given step text across shared task-set steps; это intentional reuse of shared task-set context, now including `SD-0039`.
3. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
