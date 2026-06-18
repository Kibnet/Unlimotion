# STORM BDD Lint

Сгенерировано: 2026-06-18
Команда: `/storm:bdd-lint` after `/storm:platform-runtime-validation ST-0015`

## Статус

passed_with_warnings

## Проверки

| Проверка | Результат | Комментарий |
| --- | --- | --- |
| Required scenario tags | PASS | `SC-0015-002` содержит story/rule/need/test/status tags. |
| Scenario status | PASS | Draft scenarios отсутствуют. |
| Orphan scenarios | PASS | Orphan scenarios не добавлялись. |
| Scenario -> Test links | PASS | 45/45 scenarios linked. |
| Declarative language | PASS | `SC-0015-002` описывает поведение без деталей test implementation; build evidence вынесен в artifact evidence, а не в формулировку продукта. |
| Step definitions | WARNING | `step_definitions` остаются пустыми by design; executable evidence дают TUnit tests. |
| Test annotations | PASS | Test annotations не менялись. |
| CV-0007 product claim | PASS | Вариант B prevents promotion of attachment code to active story/scenario without new product decision. |
| Platform evidence | WARNING | Browser Release build smoke прошел; Android/iOS build smoke blocked by `NETSDK1147`; runtime release support не заявляется. |

## Предупреждения

1. `step_definitions` пусты; это допустимо, пока repository не принимает executable Gherkin runner.
2. `SC-0015-002` имеет project-contract coverage и Browser Release build smoke evidence; Android/iOS runtime release support не заявляется.
3. `CV-0007` остается без scenario/test links по решению: attachment code является internal/orphan contract candidate.
