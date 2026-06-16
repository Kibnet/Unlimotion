# STORM Coverage Analysis

Сгенерировано: 2026-06-16
Команда: `/storm:cover ST-0014 CV-0003 Telegram command/auth coverage`
Режим: delivery-task после утверждения SPEC; production code и tests изменялись в рамках approved SPEC, test annotations не менялись

## Область

Этот отчёт обновляет requirements coverage после добавления `TS-0022` для `ST-0014/CV-0003`. Telegram bot command/auth поведение покрыто через testable handler seam без реального Telegram API, credentials, polling, timers или Git side effects. Прежние evidence по `ST-0011` и `ST-0016` сохранены.

## Сводка

| Метрика | Значение |
| --- | --- |
| Acceptance criteria всего | 44 |
| AC с тестовыми связями | 43 |
| AC с уровнем full/critical | 42 |
| AC с уровнем partial | 2 |
| AC без тестовых связей | 1 |
| AC улучшены текущим delivery evidence | 1 |
| Scenario -> Test links | 43/44 |
| Passing scenarios | 4 |

## Улучшено Текущим Delivery Evidence

| AC | Story | Новый уровень | Tests | Почему |
| --- | --- | --- | --- | --- |
| AC-0039 | ST-0014: Получать доступ к задачам через Telegram bot | full | TS-0022 | TelegramBotCommandAuthorizationTests прошли 7/7 и подтверждают allowed-user gate, /start, /help, /search, /task, /root без Telegram credentials, polling, timers или Git side effects. |

## Оставшиеся Partial AC

| AC | Story | Tests | Причина |
| --- | --- | --- | --- |
| AC-0040 | ST-0014 | нет | Требуется отдельное coverage decision или новые тесты. |
| AC-0042 | ST-0015 | TS-0015 | Нужна product/platform policy confirmation для non-desktop shells. |

## AC Без Тестовых Связей

| AC | Story | Формулировка | Причина |
| --- | --- | --- | --- |
| AC-0040 | ST-0014 | Callback-действия позволяют открыть задачу, создать sub/sibling, изменить статус, удалить, смотреть отношения и использовать file storage/Git timers при настройке. | Новые Telegram automated tests не добавлялись в текущем ST-0011 delivery-task. |

## Coverage Backlog

| ID | Target | Status | Route | Tests / Minimal tests | Результат / Prerequisite |
| --- | --- | --- | --- | --- | --- |
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests | delivery-task/QUEST | TS-0017 | Auth flow получил passing contract-level BDD evidence; live server auth integration не поднимался. |
| CV-0002 | AC-0033 / ST-0011 | covered_by_live_task_api_and_signalr_tests | delivery-task/QUEST | TS-0017<br>TS-0018<br>TS-0019<br>TS-0020 | TS-0017/TS-0018 закрывают contract/security и GetTask user-scope; TS-0019 подтверждает live SignalR delivery через real ChatHub и RavenDB service registration; TS-0020 подтверждает live ServiceStack HTTP task API через narrow TaskService registration, authenticated JsonServiceClient requests, real RavenDB services and cross-user non-leak assertions. |
| CV-0003 | AC-0039 / ST-0014 | covered_by_telegram_command_auth_tests | delivery-task/QUEST completed | TS-0022 | TS-0022 passed 7/7 on 2026-06-16; tests do not require Telegram credentials, network polling, timers or Git side effects. |
| CV-0004 | AC-0040 / ST-0014 | proposed | delivery-task/QUEST | TelegramBot_Callback_CreateSubTask_AddsChildToSelectedTask<br>TelegramBot_Callback_SetStatus_UpdatesTaskStatusAndHistory<br>TelegramBot_GitTimers_DoNotRunWhenConflictResolutionIsInProgress | Выбрать test seam для Telegram.Bot API и file/Git side effects. |
| CV-0005 | AC-0042 / ST-0015 | proposed | delivery-task/QUEST | PlatformProjects_RestoreAndCompileSharedUiReferences<br>AndroidProject_IncludesRequiredLibGit2NativeAssets<br>BrowserProject_StartsWithSharedApplicationViewModelSmoke | Определить, какие non-desktop shells считаются release-supported. |
| CV-0006 | PRODUCT-ENTRY / ST-0016 | covered_by_product_story_and_existing_ui_test | artifact-only completed; delivery-task only for optional success-toast/localization expansion | TS-0021 | TS-0021 passed 1/1 on 2026-06-16 and verifies toast text plus close-button removal. |
| CV-0007 | PRODUCT-ENTRY / proposed_attachment_workflow | proposed | guided-artifact-workflow_then_delivery-task_if_tests_change | AttachmentService_GetUploadDownload_RoundTripsUserAttachment<br>AttachmentMapping_PreservesAttachmentMetadataAcrossDomainAndApiMolds | Подтвердить, есть ли attachment workflow в актуальном продукте. |

## BDD Behavior Coverage

| Метрика | Значение |
| --- | --- |
| Feature files | 16 |
| Gherkin Rules | 44 |
| Gherkin Scenarios | 44 |
| Active stories со scenarios | 16/16 |
| AC со Gherkin rules | 44/44 |
| AC со Gherkin scenarios | 44/44 |
| Automated or passing scenarios | 43 |
| Draft scenarios | 1 |
| Passing scenarios | 4 |
| Failing scenarios | 0 |
| Scenarios with linked tests | 43/44 |
| Step definitions | 0 |
| Executable specification ratio | 4/44 |

## Validation Evidence

| Проверка | Результат |
| --- | --- |
| dotnet restore src/Unlimotion.Test/Unlimotion.Test.csproj | passed; restored new Unlimotion.TelegramBot project reference assets |
| dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore | passed, только существующие warnings |
| dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TelegramBotCommandAuthorizationTests/*" --output Detailed | passed 7/7 |
| python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json | OK после финальной проверки |
| git diff --check | OK после финальной проверки |

## Открытые Вопросы

1. Выбирать ли CV-0004 как следующий delivery-task для Telegram callbacks, status/relation flows и Git timers?
2. Подтвердить, есть ли attachment workflow в актуальном продукте.

## Рекомендуемый Следующий Шаг

Продолжать `/storm:cover` со следующим ranked gap: `CV-0004` Telegram callbacks/status/relation/Git timer coverage, если Telegram bot остаётся supported surface. Альтернатива — принять platform policy для `ST-0015/CV-0005`.
