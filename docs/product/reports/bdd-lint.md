# STORM BDD Lint

Сгенерировано: 2026-06-18
Команда: `/storm:bdd-lint` after `/storm:bdd-implement ST-0014 Telegram Git timer conflict-safety`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Required scenario tags | PASS | `SC-0014-002` содержит story/rule/need/test/status tags. |
| Scenario status | PASS | Draft scenarios отсутствуют. |
| Orphan scenarios | PASS | Orphan scenarios не добавлялись. |
| Scenario -> Test links | PASS | 45/45 scenarios linked. |
| Declarative language | PASS | `SC-0014-002` описывает поведение без деталей test implementation. |
| Step definitions | WARNING | `step_definitions` остаются пустыми by design; executable evidence дают TUnit tests. |
| Test annotations | PASS | Test annotations не менялись. |
| CV-0007 product claim | PASS | Вариант B prevents promotion of attachment code to active story/scenario without new product decision. |

## Предупреждения

1. `step_definitions` пусты; это допустимо, пока repository не принимает executable Gherkin runner.
2. `SC-0015-002` намеренно ограничен project-contract coverage; runtime release support для Android/browser/iOS не заявляется.
3. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
