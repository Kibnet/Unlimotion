# STORM BDD Lint

Сгенерировано: 2026-06-18
Команда: `/storm:bdd-lint` after `/storm:product-decision CV-0007 option B`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Required scenario tags | PASS | No CV-0007 scenario was created; existing scenario tags remain valid. |
| Scenario status | PASS | `SC-0014-002` remains draft with `@gap:git-timers`; passing scenarios unchanged. |
| Orphan scenarios | PASS | No orphan scenarios added. |
| Scenario -> Test links | PASS_WITH_WARNING | 44/45 scenarios linked; only draft `SC-0014-002` has no test link. |
| Declarative language | PASS | No new Gherkin text was added for attachment workflow. |
| Step definitions | WARNING | `step_definitions` remains empty by design; TUnit tests carry executable evidence. |
| CV-0007 product claim | PASS | Вариант B prevents promotion of attachment code to active story/scenario without new product decision. |

## Предупреждения

1. `SC-0014-002` remains draft because Git timer/conflict-safety behavior is not covered and was not implemented under `/storm:cover`.
2. `step_definitions` are empty; acceptable until repository adopts executable Gherkin runner.
3. `SC-0015-002` is intentionally limited to project-contract coverage; runtime release support for Android/browser/iOS is not claimed.
4. `CV-0007` remains without scenario/test links by decision: attachment code is internal/orphan contract candidate.
