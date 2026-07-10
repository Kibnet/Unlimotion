# STORM BDD Lint

Сгенерировано: 2026-07-10
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0004-002`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked; `SC-0004-002` additionally has `TS-0046`. |
| Scenario -> Step Definition links | WARNING | 21/45 scenarios step-executable. |
| ST-0004 | PARTIAL | `SC-0004-001` and `SC-0004-002` are step-executable; `SC-0004-003` remains linked automated test without step definitions. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Feature wording | PASS | `.feature` wording не менялся. |
| Automation IDs | PASS | UI selectors/automation IDs не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |
| UI video evidence | FALLBACK | Avalonia.Headless/TUnit runner does not emit video artifacts; targeted headless output and full-suite gate are next-best evidence. |
| Targeted gate | PASS | BDD/UI headless, BreadcrumbEmoji and Last Opened tree command checks passed. |
| Full suite gate | PASS | Full `Unlimotion.Test` passed 577/577 on controlled retry after unrelated teardown flake was isolated. |

## Предупреждения

1. Duplicate shared `Дано` step text across scenario-specific step definitions, now including `SD-0075` and `SD-0079`; intentional shared task-set context.
2. Duplicate `ST-0005` story step text remains from earlier scenarios.
3. Duplicate `ST-0002` status-change `Когда` step text remains from earlier lifecycle scenarios.
4. Duplicate search/filter `Когда` step text remains from earlier scenarios.
5. Duplicate `ST-0001` story step text remains from earlier task-graph scenarios.
6. Duplicate generic criterion-action `Когда` step text now includes `SD-0081`; intentional scenario-specific binding.
7. Duplicate `ST-0002` story step text remains from earlier lifecycle scenarios.
8. Duplicate `ST-0003` story step text across `SD-0064`, `SD-0068` and `SD-0072`; intentional scenario-specific binding.
9. Duplicate `ST-0004` story step text across `SD-0076` and `SD-0080`; intentional scenario-specific binding.

Следующий `/storm:cover` candidate для `ST-0004`: `SC-0004-003`. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
