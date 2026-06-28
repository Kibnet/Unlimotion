# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-28
Команда: `/storm:rank` + `/storm:bdd-implement SC-0005-003`
Режим: artifact sync after test-only executable BDD slice

## Практический Вывод

1. `SC-0005-003` теперь step-executable через `TS-0034` и `SD-0031..SD-0034`.
2. `SC-0005-002` остается step-executable через `TS-0033`.
3. Текущих active cover/behavior gaps по AC нет, но до full executable BDD coverage остаётся 36 scenarios without step definitions.
4. Step-executable scenarios: `SC-0005-002`, `SC-0005-003`, `SC-0011-001`, `SC-0011-002`, `SC-0014-001`, `SC-0014-002`, `SC-0014-003`, `SC-0015-002`, `SC-0016-001`.
5. Targeted BDD/emoji filter suites прошли; final full `Unlimotion.Test` вне sandbox прошёл 565/565.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Status | Условие |
| --- | --- | --- | --- | --- | --- |
| 1 | SC-0005-001 | AC-0013 | ST-0005 | next_executable_bdd_candidate | Search/fuzzy behavior уже имеет linked TS-0001/TS-0004/TS-0006 evidence и закрывает оставшийся ST-0005 executable gap. |
| 2 | CV-0006 | PRODUCT-ENTRY | ST-0016 | covered_by_product_story_existing_ui_test_and_executable_bdd | Error-toast behavior покрыт TS-0021, а `SC-0016-001` step-executable через TS-0030. |
| 3 | CV-0001 | AC-0032 | ST-0011 | covered_by_contract_tests_and_executable_bdd | Auth contract покрыт TS-0017; `SC-0011-001` step-executable через TS-0031. |
| 4 | CV-0002 | AC-0033 | ST-0011 | covered_by_live_task_api_signalr_tests_and_executable_bdd | Live API и SignalR покрыты; `SC-0011-002` step-executable через TS-0032. |
| 5 | CV-0003 | AC-0039 | ST-0014 | covered_by_telegram_command_auth_tests | Command/auth покрыт TS-0022, а `SC-0014-001` step-executable через TS-0028. |
| 6 | CV-0004 | AC-0040 | ST-0014 | covered_by_telegram_callback_and_timer_tests | Callback и timer safety покрыты; `SC-0014-002`/`SC-0014-003` step-executable. |
| 7 | CV-0005 | AC-0042 | ST-0015 | covered_by_project_contract_tests | Browser/iOS/Android build smoke прошёл; runtime/release support не заявляется. |
| 8 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | internal_orphan_contract_candidate | Вариант B: not active cover candidate; future revisit requires new product decision. |

## Рекомендуемый Следующий Шаг

Продолжить `/storm:cover` через SPEC на `SC-0005-001`, не меняя production code без отдельного evidence-driven stop/review.
