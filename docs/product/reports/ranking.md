# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-17
Команда: `/storm:rank` + `/storm:cover CV-0005 ST-0015 delivery sync`
Режим: delivery-task sync; ranking не пересчитан полностью, статус `CV-0005` актуализирован по evidence

## Практический Вывод

1. `CV-0006`, `CV-0001`, `CV-0002` и `CV-0003` покрыты.
2. `CV-0004` частично покрыт: callback subset закрыт `TS-0023`, Git timer/conflict-safety часть остаётся отдельным behavior gap.
3. `CV-0005` покрыт как conservative platform project-contract policy: `TS-0024` прошёл 3/3, runtime release support для Android/browser/iOS не заявляется.
4. Следующий uncovered coverage item — `CV-0007`, но он требует подтверждения актуального attachment workflow.
5. Если TelegramBot timers остаются supported surface, следующий инженерный шаг — отдельная `/storm:bdd-implement ST-0014` SPEC, а не обычный `/storm:cover`.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Status | Условие |
| --- | --- | --- | --- | --- | --- |
| 1 | CV-0006 | PRODUCT-ENTRY | ST-0016 | covered_by_product_story_and_existing_ui_test | Existing UI evidence linked. |
| 2 | CV-0001 | AC-0032 | ST-0011 | covered_by_contract_tests | Auth contract covered. |
| 3 | CV-0002 | AC-0033 | ST-0011 | covered_by_live_task_api_and_signalr_tests | Live API and SignalR covered. |
| 4 | CV-0003 | AC-0039 | ST-0014 | covered_by_telegram_command_auth_tests | Command/auth covered by TS-0022. |
| 5 | CV-0004 | AC-0040 | ST-0014 | partially_covered_callbacks_timer_gap | Callbacks covered by TS-0023; timer gap remains. |
| 6 | CV-0005 | AC-0042 | ST-0015 | covered_by_project_contract_tests | Conservative policy accepted; non-desktop shells are project-contract supported only. |
| 7 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | proposed | Needs product workflow confirmation. |

## Рекомендуемый Следующий Шаг

Для продолжения `/storm:cover` подготовить artifact-only confirmation SPEC по `CV-0007` attachment workflow или, если приоритетом становится engineering delivery, отдельную `/storm:bdd-implement ST-0014` SPEC для TelegramBot Git timer/conflict-safety behavior.
