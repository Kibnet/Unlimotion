# STORM Coverage Analysis

Сгенерировано: 2026-06-18
Команда: `/storm:product-decision CV-0007 option B internal/orphan attachment candidate`
Режим: `guided-artifact-workflow` после утверждения decision SPEC; product artifacts изменялись, tests/code/test annotations не менялись

## Область

Этот отчёт фиксирует product decision по `CV-0007`: выбран Вариант B. Attachment backend/API code остается `internal_orphan_contract_candidate`. Это не product coverage и не подтвержденный пользовательский workflow.

В коде сохранено evidence: domain model, ServiceStack routes `POST /attachments` и `GET /attachments/{id}`, authenticated `AttachmentService`, file storage через `FilesPath` и AutoMapper mapping в API/hub molds.

## Сводка

| Метрика | Значение |
| --- | --- |
| Acceptance criteria всего | 44 |
| AC с тестовыми связями | 44 |
| AC с уровнем full/critical | 43 |
| AC с уровнем partial | 1 |
| AC без тестовых связей | 0 |
| Product-entry candidates выведены из active cover queue | 1 |
| Active cover/behavior gaps | 1 |
| Scenario -> Test links | 44/45 |
| Passing scenarios | 6 |

## Результат CV-0007

| Item | Было | Стало | Почему |
| --- | --- | --- | --- |
| CV-0007 | `blocked_pending_product_decision` | `internal_orphan_contract_candidate` | Product owner selected Вариант B: не подтверждать attachment workflow как текущую продуктовую поверхность, но сохранить code evidence для future revisit. |

## Оставшиеся Partial AC

| AC | Story | Tests | Причина |
| --- | --- | --- | --- |
| AC-0040 | ST-0014 | TS-0023 | Callback subset покрыт; Git timer/conflict-safety часть требует отдельного product decision или `/storm:bdd-implement`. |

## AC Без Тестовых Связей

Нет.

## Coverage Backlog

| ID | Target | Status | Route | Tests / Minimal tests | Результат / Prerequisite |
| --- | --- | --- | --- | --- | --- |
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests | delivery-task/QUEST | TS-0017 | Auth flow получил passing contract-level BDD evidence. |
| CV-0002 | AC-0033 / ST-0011 | covered_by_live_task_api_and_signalr_tests | delivery-task/QUEST | TS-0017<br>TS-0018<br>TS-0019<br>TS-0020 | ServiceStack task API и SignalR live paths covered. |
| CV-0003 | AC-0039 / ST-0014 | covered_by_telegram_command_auth_tests | delivery-task/QUEST completed | TS-0022 | TS-0022 passed 7/7; command/auth covered. |
| CV-0004 | AC-0040 / ST-0014 | partially_covered_callbacks_timer_gap | delivery-task/QUEST partially completed | TS-0023<br>TelegramBot_GitTimers_DoNotRunWhenConflictResolutionIsInProgress remains implementation gap | TS-0023 passed 7/7; callbacks covered. Timer/conflict-safety part is not current covered behavior and should move to separate `/storm:bdd-implement` if product-supported. |
| CV-0005 | AC-0042 / ST-0015 | covered_by_project_contract_tests | delivery-task/QUEST completed | TS-0024 | Conservative platform policy accepted by SPEC approval; Android/browser/iOS project contracts covered 3/3 without runtime release claim. |
| CV-0006 | PRODUCT-ENTRY / ST-0016 | covered_by_product_story_and_existing_ui_test | artifact-only completed | TS-0021 | Error-toast behavior linked to product story. |
| CV-0007 | PRODUCT-ENTRY / proposed_attachment_workflow | internal_orphan_contract_candidate | none for current cover loop; future delivery only after new product decision | AttachmentService_GetUploadDownload_RoundTripsUserAttachment<br>AttachmentMapping_PreservesAttachmentMetadataAcrossDomainAndApiMolds | Вариант B: attachment code сохранен как internal/orphan candidate, active story/scenario/test links не создавались. |

## BDD Behavior Coverage

| Метрика | Значение |
| --- | --- |
| Feature files | 16 |
| Gherkin Rules | 44 |
| Gherkin Scenarios | 45 |
| Active stories со scenarios | 16/16 |
| AC со Gherkin rules | 44/44 |
| AC со Gherkin scenarios | 44/44 |
| Automated or passing scenarios | 44 |
| Draft scenarios | 1 |
| Passing scenarios | 6 |
| Failing scenarios | 0 |
| Scenarios with linked tests | 44/45 |
| Step definitions | 0 |
| Executable specification ratio | 6/45 |

## Validation Evidence

| Проверка | Результат |
| --- | --- |
| `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` | OK: 0 errors, 0 warnings |
| `git diff --check` | passed; LF/CRLF warnings only |
| `rg -n "[ \t]+$" docs/product features/storm specs/2026-06-18-storm-attachment-workflow-product-decision.md` | no matches |

## Открытые Вопросы

1. Нужно ли реализовать TelegramBot Git timer conflict-safety как отдельный `/storm:bdd-implement` task?
2. Нужен ли отдельный `/storm:bdd-implement` или platform validation task для реального Android/browser/iOS runtime smoke/release pipeline evidence?

## Рекомендуемый Следующий Шаг

`CV-0007` больше не является активным `/storm:cover` gap. Следующий инженерный кандидат — `ST-0014/AC-0040` Git timer conflict-safety, но только через отдельную `/storm:bdd-implement ST-0014` SPEC, если это поведение подтверждается как supported.
