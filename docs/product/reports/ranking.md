# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-25
Команда: `/storm:rank` + `/storm:cover validation stabilization`
Режим: delivery sync; ranking не пересчитан полностью, validation blocker актуализирован

## Практический Вывод

1. `CV-0001`, `CV-0002`, `CV-0003`, `CV-0004`, `CV-0005` и `CV-0006` покрыты.
2. `CV-0001` имеет existing auth-flow contract evidence `TS-0017` и executable BDD evidence `TS-0031`.
3. `CV-0002` имеет contract/security/live API/SignalR evidence `TS-0017..TS-0020` и executable BDD evidence `TS-0032`.
4. `CV-0007` выведен из active `/storm:cover` очереди по Варианту B: attachment code остается internal/orphan contract candidate.
5. Текущих active `/storm:cover` behavior gaps не осталось.
6. Step-executable scenarios: `SC-0011-001`, `SC-0011-002`, `SC-0014-001`, `SC-0014-002`, `SC-0014-003`, `SC-0015-002`, `SC-0016-001`.
7. Full-suite validation требует отдельной stabilization track: UI state/order failure закрыт targeted/class evidence, текущий blocker — live ServiceStack host cleanup/file watcher failure after 193 passing tests.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Status | Условие |
| --- | --- | --- | --- | --- | --- |
| 1 | CV-0006 | PRODUCT-ENTRY | ST-0016 | covered_by_product_story_existing_ui_test_and_executable_bdd | Error-toast behavior покрыт TS-0021 и `SC-0016-001` step-executable через TS-0030. |
| 2 | CV-0001 | AC-0032 | ST-0011 | covered_by_contract_tests_and_executable_bdd | Auth contract покрыт TS-0017; `SC-0011-001` step-executable через TS-0031. |
| 3 | CV-0002 | AC-0033 | ST-0011 | covered_by_live_task_api_signalr_tests_and_executable_bdd | Live API и SignalR покрыты; `SC-0011-002` step-executable через TS-0032. |
| 4 | CV-0003 | AC-0039 | ST-0014 | covered_by_telegram_command_auth_tests | Command/auth покрыты TS-0022 и `SC-0014-001` step-executable через TS-0028. |
| 5 | CV-0004 | AC-0040 | ST-0014 | covered_by_telegram_callback_and_timer_tests | Callbacks покрыты TS-0023 и `SC-0014-003` step-executable через TS-0029; Git timer conflict-safety покрыт TS-0025 и `SC-0014-002` step-executable через TS-0027. |
| 6 | CV-0005 | AC-0042 | ST-0015 | covered_by_project_contract_tests | Conservative policy принят; Browser Release build smoke прошел; `SC-0015-002` step-executable; Android/iOS build smoke blocked by `NETSDK1147`. |
| 7 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | internal_orphan_contract_candidate | Вариант B: не active cover candidate; future revisit требует нового product decision. |

## Рекомендуемый Следующий Шаг

Подготовить отдельную SPEC на live ServiceStack host cleanup/file watcher stabilization, чтобы вернуть full-suite как зеленый gate. После этого можно вернуться к Android/iOS `NETSDK1147` blocker или выбрать следующий high-value scenario для executable BDD coverage после product decision.
