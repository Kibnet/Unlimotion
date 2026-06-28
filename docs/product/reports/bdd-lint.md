# STORM BDD Lint

Сгенерировано: 2026-06-28
Команда: `/storm:bdd-lint` after `/storm:bdd-implement SC-0005-001`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Scenario -> Test links | PASS | 45/45 scenarios linked. |
| Scenario -> Step Definition links | WARNING | 10/45 scenarios step-executable. |
| ST-0005 | PASS | Все три ST-0005 scenarios step-executable. |
| Test annotations | PASS | Existing test annotations не менялись. |
| Production code | PASS | Production code, project files and workflows не менялись. |

## Предупреждения

1. Step definitions покрывают только 10/45 scenarios; repo-local runner не является full Cucumber-style engine.
2. Validator warning: duplicate Given step text across `SD-0009`, `SD-0013`, `SD-0022`, `SD-0027`, `SD-0031`, `SD-0035`; это intentional reuse of shared task-set context.
3. Validator warning: duplicate And step text across `SD-0028`, `SD-0032`, `SD-0036`; это intentional reuse of ST-0005 story context.
4. Validator warning: duplicate When step text across `SD-0033`, `SD-0037`; это intentional reuse of ST-0005 search/filter action wording.
5. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
