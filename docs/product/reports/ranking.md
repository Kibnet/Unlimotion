# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-17
Команда: `/storm:rank` + `/storm:cover ST-0014 CV-0004 delivery sync`
Режим: delivery-task sync; ranking не пересчитан полностью, статус `CV-0004` актуализирован по evidence

## Практический Вывод

1. `CV-0006`, `CV-0001`, `CV-0002` и `CV-0003` покрыты.
2. `CV-0004` частично покрыт: callback subset закрыт `TS-0023`, Git timer/conflict-safety часть остаётся отдельным behavior gap.
3. Следующий ranked coverage item без product implementation — `CV-0005`, но он требует platform support policy.
4. Если TelegramBot timers остаются supported surface, следующий инженерный шаг — отдельная `/storm:bdd-implement ST-0014` SPEC, а не `/storm:cover`.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Status | Условие |
| --- | --- | --- | --- | --- | --- |
| 1 | CV-0006 | PRODUCT-ENTRY | ST-0016 | covered_by_product_story_and_existing_ui_test | Existing UI evidence linked. |
| 2 | CV-0001 | AC-0032 | ST-0011 | covered_by_contract_tests | Auth contract covered. |
| 3 | CV-0002 | AC-0033 | ST-0011 | covered_by_live_task_api_and_signalr_tests | Live API and SignalR covered. |
| 4 | CV-0003 | AC-0039 | ST-0014 | covered_by_telegram_command_auth_tests | Command/auth covered by TS-0022. |
| 5 | CV-0004 | AC-0040 | ST-0014 | partially_covered_callbacks_timer_gap | Callbacks covered by TS-0023; timer gap remains. |
| 6 | CV-0005 | AC-0042 | ST-0015 | proposed | Needs non-desktop platform policy. |
| 7 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | proposed | Needs product workflow confirmation. |

## Рекомендуемый Следующий Шаг

Выбрать между двумя ветками: принять platform policy для `ST-0015/CV-0005` и продолжить `/storm:cover`, либо подготовить отдельную `/storm:bdd-implement ST-0014` SPEC для TelegramBot Git timer/conflict-safety behavior.
