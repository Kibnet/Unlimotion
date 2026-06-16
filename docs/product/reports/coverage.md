# STORM Coverage Analysis

Сгенерировано: 2026-06-17
Команда: `/storm:cover ST-0014 CV-0004 Telegram callbacks coverage`
Режим: delivery-task после утверждения SPEC; production code и tests изменялись в рамках approved SPEC, test annotations не менялись

## Область

Этот отчёт обновляет requirements coverage после добавления `TS-0023` для callback subset истории `ST-0014/AC-0040`. Callback-действия Telegram bot покрыты через testable handler seam без реального Telegram API, bot token, polling, network или Git side effects. Git timer/conflict-safety часть `AC-0040` не изменялась и оставлена отдельным gap, потому что текущий `TelegramBot.StartTimers` запускает `GitService` напрямую без conflict-resolution guard.

## Сводка

| Метрика | Значение |
| --- | --- |
| Acceptance criteria всего | 44 |
| AC с тестовыми связями | 44 |
| AC с уровнем full/critical | 42 |
| AC с уровнем partial | 2 |
| AC без тестовых связей | 0 |
| AC улучшены текущим delivery evidence | 1 |
| Scenario -> Test links | 44/45 |
| Passing scenarios | 5 |

## Улучшено Текущим Delivery Evidence

| AC | Story | Новый уровень | Tests | Почему |
| --- | --- | --- | --- | --- |
| AC-0040 | ST-0014: Получать доступ к задачам через Telegram bot | partial | TS-0023 | TelegramBotCallbackCoverageTests прошли 7/7 и подтверждают unauthorized callback gate, open, status, delete, create sub/sibling prompts и relation lists без Telegram credentials, polling или Git side effects. Full не выставлен, потому что timer/conflict-safety часть остаётся gap. |

## Оставшиеся Partial AC

| AC | Story | Tests | Причина |
| --- | --- | --- | --- |
| AC-0040 | ST-0014 | TS-0023 | Callback subset покрыт; Git timer/conflict-safety часть требует отдельного product decision или /storm:bdd-implement. |
| AC-0042 | ST-0015 | TS-0015 | Нужна product/platform policy confirmation для non-desktop shells. |

## AC Без Тестовых Связей

Нет.

## Coverage Backlog

| ID | Target | Status | Route | Tests / Minimal tests | Результат / Prerequisite |
| --- | --- | --- | --- | --- | --- |
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests | delivery-task/QUEST | TS-0017 | Auth flow получил passing contract-level BDD evidence. |
| CV-0002 | AC-0033 / ST-0011 | covered_by_live_task_api_and_signalr_tests | delivery-task/QUEST | TS-0017<br>TS-0018<br>TS-0019<br>TS-0020 | ServiceStack task API и SignalR live paths covered. |
| CV-0003 | AC-0039 / ST-0014 | covered_by_telegram_command_auth_tests | delivery-task/QUEST completed | TS-0022 | TS-0022 passed 7/7; command/auth covered. |
| CV-0004 | AC-0040 / ST-0014 | partially_covered_callbacks_timer_gap | delivery-task/QUEST partially completed | TS-0023<br>TelegramBot_GitTimers_DoNotRunWhenConflictResolutionIsInProgress remains implementation gap | TS-0023 passed 7/7; callbacks covered. Timer/conflict-safety part is not current covered behavior and should move to separate /storm:bdd-implement if product-supported. |
| CV-0005 | AC-0042 / ST-0015 | proposed | delivery-task/QUEST | PlatformProjects_RestoreAndCompileSharedUiReferences<br>AndroidProject_IncludesRequiredLibGit2NativeAssets<br>BrowserProject_StartsWithSharedApplicationViewModelSmoke | Определить, какие non-desktop shells считаются release-supported. |
| CV-0006 | PRODUCT-ENTRY / ST-0016 | covered_by_product_story_and_existing_ui_test | artifact-only completed | TS-0021 | Error-toast behavior linked to product story. |
| CV-0007 | PRODUCT-ENTRY / proposed_attachment_workflow | proposed | guided-artifact-workflow_then_delivery-task_if_tests_change | AttachmentService_GetUploadDownload_RoundTripsUserAttachment<br>AttachmentMapping_PreservesAttachmentMetadataAcrossDomainAndApiMolds | Подтвердить, есть ли attachment workflow в актуальном продукте. |

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
| Passing scenarios | 5 |
| Failing scenarios | 0 |
| Scenarios with linked tests | 44/45 |
| Step definitions | 0 |
| Executable specification ratio | 5/45 |

## Validation Evidence

| Проверка | Результат |
| --- | --- |
| `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` | passed, только существующие warnings |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCallbackCoverageTests/*" --output Detailed` | passed 7/7 |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed` | passed 7/7 |

## Открытые Вопросы

1. Нужно ли реализовать TelegramBot Git timer conflict-safety как отдельный `/storm:bdd-implement` task?
2. Какие non-desktop shells считаются release-supported для `ST-0015/CV-0005`?
3. Есть ли attachment workflow в актуальном продукте?

## Рекомендуемый Следующий Шаг

Продолжать `/storm:cover` с `CV-0005` только после platform support policy либо открыть отдельную `/storm:bdd-implement ST-0014` SPEC для TelegramBot Git timer/conflict-safety, если timer часть `AC-0040` остаётся поддерживаемой.
