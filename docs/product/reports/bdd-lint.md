# STORM BDD Lint

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0007-002`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0007-002` additionally has `TS-0052`. |
| Scenario -> Step Definition links | WARNING | 27/45 scenarios step-executable. |
| ST-0007 | PASS | `SC-0007-001` и `SC-0007-002` step-executable through `TS-0051`/`TS-0052` and `SD-0099..SD-0106`. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Automation IDs | PASS | UI selectors/automation IDs не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| UI video evidence | NOT APPLICABLE | UI behavior/layout не менялись; targeted relation Avalonia.Headless evidence used as next-best evidence. |
| Targeted gate | PASS | BDD `1/1` and preserved relation picker UI class `5/5`. |
| Full suite gate | NOT RUN | Previous full `Unlimotion.Test` timed out after 304 seconds without a summary. |

## Предупреждения

1. Shared `Дано` step text now includes `SD-0103`; intentional scenario-specific task-set context.
2. Generic criterion-action `Когда` now includes `SD-0105`; intentional scenario-specific binding.
3. `ST-0007` story step text is shared by `SD-0100` and `SD-0104`; intentional scenario-specific binding.
4. Eight earlier duplicate groups for ST-0001..ST-0006 remain unchanged.

Validator result: `0 errors`, `11 warnings`, `step_reuse_ratio 109/109`.
