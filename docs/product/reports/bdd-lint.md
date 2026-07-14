# STORM BDD Lint

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0006-001`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0006-001` additionally has `TS-0048`. |
| Scenario -> Step Definition links | WARNING | 23/45 scenarios step-executable. |
| ST-0006 | PARTIAL | `SC-0006-001` is step-executable; `SC-0006-002` and `SC-0006-003` remain linked-existing-tests only. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Automation IDs | PASS | UI selectors/automation IDs не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| UI video evidence | FALLBACK | Avalonia.Headless/TUnit runner does not emit video artifacts; targeted headless output and full-suite gate are next-best evidence. |
| Targeted gate | PASS | BDD/UI headless and preserved planning UI checks passed. |
| Full suite gate | PASS | Full `Unlimotion.Test` passed 579/579 on controlled escalated retry after sandbox ACL and unrelated teardown conditions were isolated. |

## Предупреждения

1. Duplicate shared `Дано` step text across scenario-specific step definitions, now including `SD-0087`; intentional shared task-set context.
2. Duplicate `ST-0005` story step text remains from earlier scenarios.
3. Duplicate `ST-0002` status-change `Когда` step text remains from earlier lifecycle scenarios.
4. Duplicate search/filter `Когда` step text remains from earlier scenarios.
5. Duplicate `ST-0001` story step text remains from earlier task-graph scenarios.
6. Duplicate generic criterion-action `Когда` step text now includes `SD-0089`; intentional scenario-specific binding.
7. Duplicate `ST-0002` story step text remains from earlier lifecycle scenarios.
8. Duplicate `ST-0003` story step text remains from earlier availability scenarios.
9. Duplicate `ST-0004` story step text remains from earlier workspace-navigation scenarios.

Следующий `/storm:cover` candidate: `SC-0006-002` или `SC-0006-003`, с приоритетом по rank/effort. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
