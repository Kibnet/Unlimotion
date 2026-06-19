# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-19
Команда: `/storm:rank` + `/storm:bdd-implement SC-0016-001 executable step definitions`
Режим: delivery sync; ranking не пересчитан полностью, executable BDD evidence для `CV-0006` актуализирован

## Практический Вывод

1. `CV-0001`, `CV-0002`, `CV-0003`, `CV-0004`, `CV-0005` и `CV-0006` покрыты.
2. `CV-0006` теперь имеет и existing UI evidence `TS-0021`, и executable BDD evidence `TS-0030`.
3. `CV-0007` выведен из active `/storm:cover` очереди по Варианту B: attachment code остается internal/orphan contract candidate.
4. Текущих active `/storm:cover` behavior gaps не осталось.
5. Step-executable scenarios: `SC-0014-001`, `SC-0014-002`, `SC-0014-003`, `SC-0015-002`, `SC-0016-001`.
6. `ST-0015` сохраняет Browser Release build smoke evidence; Android/iOS build smoke blocked by `NETSDK1147` и требуют отдельной environment/setup task, если эти claims нужны.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Status | Условие |
| --- | --- | --- | --- | --- | --- |
| 1 | CV-0006 | PRODUCT-ENTRY | ST-0016 | covered_by_product_story_existing_ui_test_and_executable_bdd | Error-toast behavior покрыт TS-0021 и `SC-0016-001` step-executable через TS-0030. |
| 2 | CV-0001 | AC-0032 | ST-0011 | covered_by_contract_tests | Auth contract покрыт; `SC-0011-001` пока без step definitions. |
| 3 | CV-0002 | AC-0033 | ST-0011 | covered_by_live_task_api_and_signalr_tests | Live API и SignalR покрыты; `SC-0011-002` пока без step definitions. |
| 4 | CV-0003 | AC-0039 | ST-0014 | covered_by_telegram_command_auth_tests | Command/auth покрыты TS-0022 и `SC-0014-001` step-executable через TS-0028. |
| 5 | CV-0004 | AC-0040 | ST-0014 | covered_by_telegram_callback_and_timer_tests | Callbacks покрыты TS-0023 и `SC-0014-003` step-executable через TS-0029; Git timer conflict-safety покрыт TS-0025 и `SC-0014-002` step-executable через TS-0027. |
| 6 | CV-0005 | AC-0042 | ST-0015 | covered_by_project_contract_tests | Conservative policy принят; Browser Release build smoke прошел; `SC-0015-002` step-executable; Android/iOS build smoke blocked by `NETSDK1147`. |
| 7 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | internal_orphan_contract_candidate | Вариант B: не active cover candidate; future revisit требует нового product decision. |

## Рекомендуемый Следующий Шаг

Подготовить следующую SPEC на `SC-0011-001` или `SC-0011-002` для server-storage executable slice. Альтернатива: отдельная SPEC для Android/iOS environment/setup task, если нужно снять `NETSDK1147` blocker и получить build smoke evidence.
