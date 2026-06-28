# STORM BDD Lint

Сгенерировано: 2026-06-29
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0001-002`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0001-002` additionally has `TS-0037`. |
| Scenario -> Step Definition links | WARNING | 12/45 scenarios step-executable. |
| ST-0001 | WARNING | `SC-0001-001` и `SC-0001-002` step-executable; `SC-0001-003` remains linked-existing-tests only. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |

## Предупреждения

1. Step definitions покрывают только 12/45 scenarios; repo-local runner не является full Cucumber-style engine.
2. Validator may report duplicate Given step text across shared task-set steps; это intentional reuse of shared task-set context, now including `SD-0043`.
3. Validator may report duplicate `И поведение относится к истории ST-0001` for `SD-0040` and `SD-0044`; это intentional story-context reuse.
4. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
5. Full-suite gate blocked outside BDD lint scope by unrelated flaky/order-sensitive tests; next stabilization SPEC should handle this before the next broad coverage slice.
