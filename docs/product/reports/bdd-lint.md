# STORM BDD Lint

Сгенерировано: 2026-06-29
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0002-003`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0002-003` additionally has `TS-0041`. |
| Scenario -> Step Definition links | WARNING | 16/45 scenarios step-executable. |
| ST-0002 | PASS | `SC-0002-001`, `SC-0002-002` and `SC-0002-003` are step-executable. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| Targeted gate | PASS | BDD and `TaskStatusMigrationTests` checks passed. |
| Full suite gate | PASS | Outside-sandbox full suite passed 572/572 with `C:\tmp\unlimotion-full-suite-sc0002-status-migration-bdd.log`. |

## Предупреждения

1. Step definitions покрывают только 16/45 scenarios; repo-local runner не является full Cucumber-style engine.
2. Validator may report duplicate Given step text across shared task-set steps; это intentional reuse of shared task-set context, now including `SD-0059`.
3. Validator may report duplicate shared status-change `Когда` step text; это intentional reuse for ST-0002 status scenarios, now including `SD-0061`.
4. Validator may report duplicate shared ST-0002 story step text; это intentional reuse for lifecycle scenarios, now including `SD-0060`.
5. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
