# STORM Traceability

Сгенерировано: 2026-06-18
Команда: `/storm:trace` sync after `/storm:cover CV-0007 attachment workflow confirmation`

## New Trace

No new Story -> AC -> Scenario -> Test trace was created for `CV-0007`.

Reason: attachment code exists, but current product artifacts do not confirm a user-facing attachment workflow. Creating active story/scenario/test links would be a false product claim.

## Product-Entry Candidate Trace

| Candidate | Code Units | Status | Next Action |
| --- | --- | --- | --- |
| CV-0007: proposed_attachment_workflow | `src/Unlimotion.Domain/Attachment.cs`; `src/Unlimotion.Server.ServiceInterface/AttachmentService.cs`; `src/Unlimotion.Server.ServiceModel/Attachment.cs`; `src/Unlimotion.Server/AppModelMapping.cs` | blocked_pending_product_decision | Confirm product workflow or leave as internal/orphan contract candidate. |

## Existing Trace Preserved

| Story | AC | Scenario | Test | Status |
| --- | --- | --- | --- | --- |
| ST-0014 | AC-0039 | SC-0014-001 | TS-0022 | passing |
| ST-0014 | AC-0040 | SC-0014-002 | none | draft timer gap |
| ST-0014 | AC-0040 | SC-0014-003 | TS-0023 | passing callback subset |
| ST-0015 | AC-0041 | SC-0015-001 | TS-0011, TS-0015 | desktop/update evidence |
| ST-0015 | AC-0042 | SC-0015-002 | TS-0015, TS-0024 | passing project-contract coverage |
| ST-0015 | AC-0043 | SC-0015-003 | TS-0011, TS-0015 | CI/README media evidence |

## Residual Gap

`SC-0014-002` remains intentionally unlinked because it represents the Git timer/conflict-safety part that cannot be covered as current behavior.

`CV-0007` remains intentionally unlinked from active stories/scenarios/tests because attachment workflow has not been confirmed as current product behavior.
