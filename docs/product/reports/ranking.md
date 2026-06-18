# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-18
Команда: `/storm:rank` + `/storm:platform-runtime-validation ST-0015`
Режим: artifact-only validation sync; ranking не пересчитан полностью, platform evidence для `CV-0005` актуализирован

## Практический Вывод

1. `CV-0001`, `CV-0002`, `CV-0003`, `CV-0004`, `CV-0005` и `CV-0006` покрыты.
2. `CV-0007` выведен из active `/storm:cover` очереди по Варианту B: attachment code остается internal/orphan contract candidate.
3. Текущих active `/storm:cover` behavior gaps не осталось.
4. `ST-0015` получил Browser Release build smoke evidence; Android/iOS build smoke blocked by `NETSDK1147` и требуют отдельной environment/setup task, если эти claims нужны.
5. Следующий engineering шаг зависит от цели: Android/iOS workload restore setup или executable Gherkin step definitions.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Status | Условие |
| --- | --- | --- | --- | --- | --- |
| 1 | CV-0006 | PRODUCT-ENTRY | ST-0016 | covered_by_product_story_and_existing_ui_test | Existing UI evidence linked. |
| 2 | CV-0001 | AC-0032 | ST-0011 | covered_by_contract_tests | Auth contract покрыт. |
| 3 | CV-0002 | AC-0033 | ST-0011 | covered_by_live_task_api_and_signalr_tests | Live API и SignalR покрыты. |
| 4 | CV-0003 | AC-0039 | ST-0014 | covered_by_telegram_command_auth_tests | Command/auth покрыты TS-0022. |
| 5 | CV-0004 | AC-0040 | ST-0014 | covered_by_telegram_callback_and_timer_tests | Callbacks покрыты TS-0023; Git timer conflict-safety покрыт TS-0025. |
| 6 | CV-0005 | AC-0042 | ST-0015 | covered_by_project_contract_tests | Conservative policy принят; Browser Release build smoke прошел; Android/iOS build smoke blocked by `NETSDK1147`. |
| 7 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | internal_orphan_contract_candidate | Вариант B: не active cover candidate; future revisit требует нового product decision. |

## Рекомендуемый Следующий Шаг

Подготовить отдельную SPEC для Android/iOS environment/setup task, если нужно снять `NETSDK1147` blocker и получить build smoke evidence. Альтернатива: SPEC на `/storm:bdd-implement` executable step definitions, если процесс должен перейти от linked TUnit evidence к исполняемому Gherkin runner.
