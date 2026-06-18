# STORM Stories

Сгенерировано: 2026-06-18
Команда: `/storm:bdd-implement ST-0014 Telegram Git timer conflict-safety`

## Story Changes

| Story | Изменение | Evidence |
| --- | --- | --- |
| ST-0014 | `AC-0040` повышен с `partial` до `critical`: callbacks покрыты `TS-0023`, Telegram Git timer conflict-safety покрыт `TS-0025`. | `src/Unlimotion.TelegramBot/TelegramGitTimerHandler.cs`, `src/Unlimotion.Test/TelegramBotGitTimerConflictSafetyTests.cs` |

## Product-Entry Candidate Update

| Candidate | Status | Evidence | Notes |
| --- | --- | --- | --- |
| CV-0007: attachment workflow | internal_orphan_contract_candidate | `src/Unlimotion.Domain/Attachment.cs`, `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`, `src/Unlimotion.Server.ServiceModel/Attachment.cs`, `src/Unlimotion.Server/AppModelMapping.cs` | Вариант B: backend/API code остается traceable, но active product story, AC, UI workflow или Gherkin scenario не создаются. |

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0014 / AC-0040 | Нет active gap. | Поддерживать `TS-0022`, `TS-0023`, `TS-0025` при изменениях Telegram bot. |
| Platform runtime | Android/browser/iOS runtime launch/release pipeline evidence не покрывались. | Отдельная platform validation SPEC при необходимости release support claims. |
| BDD execution | `step_definitions` отсутствуют. | Отдельная `/storm:bdd-implement` SPEC, если нужен executable Gherkin runner. |
| CV-0007 | Нет active story gap после Варианта B. | Future revisit only after new product decision. |
