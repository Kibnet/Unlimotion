# STORM BDD Lint

Сгенерировано: 2026-06-29
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0002-001 + stability gate`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0002-001` additionally has `TS-0039`. |
| Scenario -> Step Definition links | WARNING | 14/45 scenarios step-executable. |
| ST-0002 | PARTIAL | `SC-0002-001` is step-executable; `SC-0002-002` and `SC-0002-003` remain linked automated tests without step definitions. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Production behavior | PASS | Scoped stability fix only suppresses autosave during `TaskItemViewModel.Update(TaskItem)` model-sync. |
| Targeted gate | PASS | BDD, TaskStatusPicker UI, paste/copy outline and package compatibility checks passed. |
| Full suite gate | PASS | Outside-sandbox full suite passed 570/570 with `C:\tmp\unlimotion-full-suite-sc0002-status-support-bdd-final2.log`. |

## Предупреждения

1. Step definitions покрывают только 14/45 scenarios; repo-local runner не является full Cucumber-style engine.
2. Validator may report duplicate Given step text across shared task-set steps; это intentional reuse of shared task-set context, now including `SD-0051`.
3. Validator may report duplicate shared story/action step text across scenario-specific step definitions; это intentional context reuse.
4. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
5. `SC-0002-002` и `SC-0002-003` остаются следующими `/storm:cover` candidates для `ST-0002`.
