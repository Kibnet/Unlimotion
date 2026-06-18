# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-18
Команда: `/storm:rank` + `/storm:product-decision CV-0007 option B`
Режим: artifact-only sync; ranking не пересчитан полностью, статус `CV-0007` актуализирован по product decision

## Практический Вывод

1. `CV-0006`, `CV-0001`, `CV-0002`, `CV-0003` и `CV-0005` покрыты.
2. `CV-0007` выведен из active `/storm:cover` очереди по Варианту B: attachment code остается internal/orphan contract candidate.
3. `CV-0004` частично покрыт: callback subset закрыт `TS-0023`, Git timer/conflict-safety часть остаётся единственным текущим behavior gap.
4. Следующий engineering шаг — отдельная `/storm:bdd-implement ST-0014` SPEC, если TelegramBot timers/conflict-safety подтверждаются как supported behavior.
5. Если ST-0014 timer behavior не поддерживается продуктом, нужен отдельный artifact decision по de-scope/deprecation этого scenario gap.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Status | Условие |
| --- | --- | --- | --- | --- | --- |
| 1 | CV-0006 | PRODUCT-ENTRY | ST-0016 | covered_by_product_story_and_existing_ui_test | Existing UI evidence linked. |
| 2 | CV-0001 | AC-0032 | ST-0011 | covered_by_contract_tests | Auth contract covered. |
| 3 | CV-0002 | AC-0033 | ST-0011 | covered_by_live_task_api_and_signalr_tests | Live API and SignalR covered. |
| 4 | CV-0003 | AC-0039 | ST-0014 | covered_by_telegram_command_auth_tests | Command/auth covered by TS-0022. |
| 5 | CV-0004 | AC-0040 | ST-0014 | partially_covered_callbacks_timer_gap | Callbacks covered by TS-0023; timer gap remains. |
| 6 | CV-0005 | AC-0042 | ST-0015 | covered_by_project_contract_tests | Conservative policy accepted; non-desktop shells are project-contract supported only. |
| 7 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | internal_orphan_contract_candidate | Вариант B: not active cover candidate; future revisit requires new product decision. |

## Рекомендуемый Следующий Шаг

Подготовить отдельную `/storm:bdd-implement ST-0014` SPEC для Git timer conflict-safety только если TelegramBot timer behavior остается supported surface. Иначе подготовить artifact decision SPEC для `SC-0014-002`.
