# STORM BDD Lint

Сгенерировано: 2026-07-10
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0003-003`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0003-003` additionally has `TS-0044`. |
| Scenario -> Step Definition links | WARNING | 19/45 scenarios step-executable. |
| ST-0003 | PASS | `SC-0003-001`, `SC-0003-002` and `SC-0003-003` are step-executable. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| Targeted gate | PASS | BDD, `TaskStatusTransitionTests` и `TaskAvailabilityCalculationTests` checks passed. |
| Full suite gate | PASS | Full `Unlimotion.Test` passed 575/575 on controlled retry after unrelated isolated UI teardown checks passed. |

## Предупреждения

1. Duplicate shared `Дано` step text across scenario-specific step definitions, now including `SD-0067` and `SD-0071`; intentional shared task-set context.
2. Duplicate `ST-0005` story step text remains from earlier scenarios.
3. Duplicate `ST-0002` status-change `Когда` step text remains from earlier lifecycle scenarios.
4. Duplicate search/filter `Когда` step text remains from earlier scenarios.
5. Duplicate `ST-0001` story step text remains from earlier task-graph scenarios.
6. Duplicate generic criterion-action `Когда` step text across `SD-0045`, `SD-0065`, `SD-0069` and `SD-0073`; intentional scenario-specific binding.
7. Duplicate `ST-0002` story step text remains from earlier lifecycle scenarios.
8. Duplicate `ST-0003` story step text across `SD-0064`, `SD-0068` and `SD-0072`; intentional scenario-specific binding.

Следующий `/storm:cover` candidate после закрытия `ST-0003`: `ST-0004 / SC-0004-001`. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
