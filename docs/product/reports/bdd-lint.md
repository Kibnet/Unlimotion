# STORM BDD Lint

Сгенерировано: 2026-06-17
Команда: `/storm:bdd-lint` after `/storm:cover ST-0014 CV-0004`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Required scenario tags | PASS | New `SC-0014-003` has feature/rule/scenario/story/need/coverage/test tags. |
| Scenario status | PASS | `SC-0014-003` is passing; `SC-0014-002` remains draft with `@gap:git-timers`. |
| Orphan scenarios | PASS | No orphan scenarios. |
| Scenario -> Test links | PASS_WITH_WARNING | 44/45 scenarios linked; only draft `SC-0014-002` has no test link. |
| Declarative language | PASS | New scenario describes product behavior, not internal class names. |
| Step definitions | WARNING | `step_definitions` remains empty by design; TUnit tests carry executable evidence. |

## Предупреждения

1. `SC-0014-002` remains draft because Git timer/conflict-safety behavior is not covered and was not implemented under `/storm:cover`.
2. `step_definitions` are empty; acceptable until repository adopts executable Gherkin runner.
