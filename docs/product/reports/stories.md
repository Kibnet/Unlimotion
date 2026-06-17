# STORM Stories

Сгенерировано: 2026-06-18
Команда: `/storm:cover CV-0007 attachment workflow confirmation`

## Product-Entry Candidate Update

| Candidate | Status | Evidence | Notes |
| --- | --- | --- | --- |
| CV-0007: attachment workflow | blocked_pending_product_decision | `src/Unlimotion.Domain/Attachment.cs`, `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`, `src/Unlimotion.Server.ServiceModel/Attachment.cs`, `src/Unlimotion.Server/AppModelMapping.cs` | Backend/API code exists, but current product stories, AC, UI docs and Gherkin scenarios do not confirm user-facing attachment workflow. |

## Story Changes

No active story was created or changed for `CV-0007`.

Reason: code-only evidence can justify a product-entry/internal-contract candidate, but not a confirmed user workflow. Creating an active story, acceptance criteria, Gherkin scenario or test links requires a separate product decision.

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0014 / AC-0040 | Git timer/conflict-safety остаётся draft/gap. | Отдельная `/storm:bdd-implement ST-0014` SPEC, если поведение поддерживается продуктом. |
| Platform runtime | Android/browser/iOS runtime launch/release pipeline evidence не покрывались. | Отдельная platform validation SPEC при необходимости release support claims. |
| CV-0007 | Attachment workflow не подтверждён как актуальная продуктовая поверхность. | Product decision: подтвердить workflow или оставить attachment code internal/orphan contract candidate. |
