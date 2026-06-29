# STORM BDD Lint

Сгенерировано: 2026-06-29
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0002-002`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0002-002` additionally has `TS-0040`. |
| Scenario -> Step Definition links | WARNING | 15/45 scenarios step-executable. |
| ST-0002 | PARTIAL | `SC-0002-001` and `SC-0002-002` are step-executable; `SC-0002-003` remains linked automated test without step definitions. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| Targeted gate | PASS | BDD, TaskStatusTransition domain/ViewModel and TaskStatusPicker UI checks passed. |
| Full suite gate | PASS | Outside-sandbox full suite passed 571/571 with `C:\tmp\unlimotion-full-suite-sc0002-completed-block-bdd.log`. |

## Предупреждения

1. Step definitions покрывают только 15/45 scenarios; repo-local runner не является full Cucumber-style engine.
2. Validator may report duplicate Given step text across shared task-set steps; это intentional reuse of shared task-set context, now including `SD-0055`.
3. Validator may report duplicate shared status-change `Когда` step text; это intentional reuse for ST-0002 status scenarios.
4. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
5. `SC-0002-003` остаётся следующим `/storm:cover` candidate для `ST-0002`.
