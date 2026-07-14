# STORM BDD Lint

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0007-001`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0007-001` additionally has `TS-0051`. |
| Scenario -> Step Definition links | WARNING | 26/45 scenarios step-executable. |
| ST-0007 | PASS | `SC-0007-001` step-executable through `TS-0051` and `SD-0099..SD-0102`. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Automation IDs | PASS | UI selectors/automation IDs не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| UI video evidence | NOT APPLICABLE | UI behavior/layout не менялись; targeted task-card Avalonia.Headless evidence used as next-best evidence. |
| Targeted gate | PASS | BDD `1/1` and preserved task-card UI class `15/15`. |
| Full suite gate | NOT RUN | Current full `Unlimotion.Test` timed out after 304 seconds without a summary. |

## Предупреждения

1. Duplicate shared `Дано` step text now includes `SD-0099`; shared task-set context is intentional.
2. Duplicate `ST-0005` story step text remains from earlier scenarios.
3. Duplicate `ST-0002` status-change `Когда` step text remains from earlier lifecycle scenarios.
4. Duplicate search/filter `Когда` step text remains from earlier scenarios.
5. Duplicate `ST-0001` story step text remains from earlier task-graph scenarios.
6. Duplicate generic criterion-action `Когда` step text now includes `SD-0101`; scenario-specific binding is intentional.
7. Duplicate `ST-0002`, `ST-0003`, `ST-0004` and `ST-0006` story-step texts remain from earlier scenarios.

Validator result: `0 errors`, `10 warnings`, `step_reuse_ratio 105/105`.
