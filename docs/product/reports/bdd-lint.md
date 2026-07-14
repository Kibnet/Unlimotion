# STORM BDD Lint

Сгенерировано: 2026-07-14
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0006-003`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0006-003` additionally has `TS-0050`. |
| Scenario -> Step Definition links | WARNING | 25/45 scenarios step-executable. |
| ST-0006 | PASS | `SC-0006-001`, `SC-0006-002` and `SC-0006-003` are step-executable. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Automation IDs | PASS | UI selectors/automation IDs не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| UI video evidence | NOT APPLICABLE | UI behavior/layout не менялись; preserved wanted/importance UI/headless tests used as next-best evidence. |
| Targeted gate | PASS | BDD and preserved wanted/importance UI gates passed. |
| Full suite gate | PASS | Full `Unlimotion.Test` passed 581/581 on escalated run after artifact sync. |

## Предупреждения

1. Duplicate shared `Дано` step text across scenario-specific step definitions, now including `SD-0095`; intentional shared task-set context.
2. Duplicate `ST-0006` story step text now includes `SD-0096`; intentional scenario-specific binding.
3. Duplicate `ST-0005` story step text remains from earlier scenarios.
4. Duplicate `ST-0002` status-change `Когда` step text remains from earlier lifecycle scenarios.
5. Duplicate search/filter `Когда` step text now includes `SD-0097`; intentional scenario-specific binding.
6. Duplicate `ST-0001` story step text remains from earlier task-graph scenarios.
7. Duplicate generic criterion-action `Когда` step text remains from earlier scenarios.
8. Duplicate `ST-0002` story step text remains from earlier lifecycle scenarios.
9. Duplicate `ST-0003` story step text remains from earlier availability scenarios.
10. Duplicate `ST-0004` story step text remains from earlier workspace-navigation scenarios.

Следующий `/storm:cover` candidate нужно выбрать вне `ST-0006`, потому что все три сценария story теперь step-executable. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
