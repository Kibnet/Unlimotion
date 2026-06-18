# STORM Stories

Сгенерировано: 2026-06-18
Команда: `/storm:product-decision CV-0007 option B`

## Product-Entry Candidate Update

| Candidate | Status | Evidence | Notes |
| --- | --- | --- | --- |
| CV-0007: attachment workflow | internal_orphan_contract_candidate | `src/Unlimotion.Domain/Attachment.cs`, `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`, `src/Unlimotion.Server.ServiceModel/Attachment.cs`, `src/Unlimotion.Server/AppModelMapping.cs` | Вариант B: backend/API code remains traceable, but no active product story, AC, UI workflow or Gherkin scenario is created. |

## Story Changes

No active story was created or changed for `CV-0007`.

Reason: product owner selected Вариант B. Attachment code is retained as internal/orphan contract candidate, not promoted to current user-facing behavior.

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0014 / AC-0040 | Git timer/conflict-safety остаётся draft/gap. | Отдельная `/storm:bdd-implement ST-0014` SPEC, если поведение поддерживается продуктом. |
| Platform runtime | Android/browser/iOS runtime launch/release pipeline evidence не покрывались. | Отдельная platform validation SPEC при необходимости release support claims. |
| CV-0007 | Нет active story gap после Варианта B. | Future revisit only after new product decision. |
