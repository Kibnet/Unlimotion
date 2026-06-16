# STORM Coverage Analysis

Сгенерировано: 2026-06-16
Команда: `/storm:cover ST-0011 ServiceStack API smoke blocker`
Режим: delivery-task после утверждения SPEC; production code и tests изменялись в рамках approved SPEC, test annotations не менялись; attempted ServiceStack API smoke не сохранён как executable test из-за blocker

## Область

Этот отчёт обновляет requirements coverage после добавления `TS-0019` и проверки follow-up ServiceStack API smoke для `ST-0011`. Live delivery часть `AC-0033` закрыта; ServiceStack task API live smoke остаётся blocker, потому что minimal test-only AppHost с `TaskService` assembly падает на ServiceStack free-quota operation registration до endpoint assertions.

## Сводка

| Метрика | Значение |
|---|---:|
| Acceptance criteria всего | 43 |
| AC с тестовыми связями | 41 |
| AC с уровнем full/critical | 40 |
| AC с уровнем partial | 3 |
| AC без тестовых связей | 2 |
| AC улучшены текущим delivery evidence | 2 |
| Scenario -> Test links | 41/43 |
| Passing scenarios | 2 |

## Улучшено Текущим Delivery Evidence

| AC | Story | Новый уровень | Tests | Почему |
|---|---|---|---|---|
| AC-0032 | ST-0011: Использовать optional серверного хранилища с authentication и real-time updates | critical | TS-0017 | Contract-level tests подтвердили auth routes, refresh authentication requirement и client login/register/refresh references. |
| AC-0033 | ST-0011: Использовать optional серверного хранилища с authentication и real-time updates | critical | TS-0017, TS-0018, TS-0019 | Contract/security regression tests подтвердили authenticated endpoints, GetAll/BulkInsert/GetTask user-scope contract и SignalR handler mapping; live SignalR/RavenDB test подтвердил delivery между двумя authenticated clients. Попытка ServiceStack task API live smoke остановлена на ServiceStack LicenseException free-quota limit до проверки endpoints, поэтому уровень не full. |

## Оставшиеся Partial AC

| AC | Story | Tests | Причина |
|---|---|---|---|
| AC-0039 | ST-0014 | нет | Требуется отдельное coverage decision или новые тесты. |
| AC-0040 | ST-0014 | нет | Требуется отдельное coverage decision или новые тесты. |
| AC-0042 | ST-0015 | TS-0015 | Нужна product/platform policy confirmation для non-desktop shells. |

## AC Без Тестовых Связей

| AC | Story | Формулировка | Причина |
|---|---|---|---|
| AC-0039 | ST-0014 | Бот ограничивает доступ allowed users и поддерживает /start, /help, /search, /task и /root. | Новые Telegram automated tests не добавлялись в текущем ST-0011 delivery-task. |
| AC-0040 | ST-0014 | Callback-действия позволяют открыть задачу, создать sub/sibling, изменить статус, удалить, смотреть отношения и использовать file storage/Git timers при настройке. | Новые Telegram automated tests не добавлялись в текущем ST-0011 delivery-task. |

## Coverage Backlog

| ID | Target | Status | Route | Tests / Minimal tests | Результат / Prerequisite |
|---|---|---|---|---|---|
| CV-0001 | AC-0032 / ST-0011 | covered_by_contract_tests | delivery-task/QUEST | TS-0017 | Auth flow получил passing contract-level BDD evidence; live server auth integration не поднимался. |
| CV-0002 | AC-0033 / ST-0011 | blocked_service_stack_api_license_quota_gap_remaining | delivery-task/QUEST | TS-0017, TS-0018, TS-0019 | Contract/security и GetTask user-scope закрыты `TS-0017/TS-0018`; live SignalR delivery через real `ChatHub` и RavenDB service registration закрыт `TS-0019`. ServiceStack task API live smoke был попытан через minimal test-only AppHost и остановлен: startup падает с `ServiceStack LicenseException: free-quota limit on 10 ServiceStack Operations` до endpoint assertions. |
| CV-0003 | AC-0039 / ST-0014 | proposed | delivery-task/QUEST | TelegramBot_StartAndHelp_ReturnLocalizedHelpForAllowedUser<br>TelegramBot_Command_FromUnauthorizedUser_IsRejectedWithoutStorageAccess<br>TelegramBot_Search_ReturnsMatchingTaskButtonsForAllowedUser | Подтвердить, что Telegram bot входит в публичный продуктовый контракт. |
| CV-0004 | AC-0040 / ST-0014 | proposed | delivery-task/QUEST | TelegramBot_Callback_CreateSubTask_AddsChildToSelectedTask<br>TelegramBot_Callback_SetStatus_UpdatesTaskStatusAndHistory<br>TelegramBot_GitTimers_DoNotRunWhenConflictResolutionIsInProgress | Выбрать test seam для Telegram.Bot API и file/Git side effects. |
| CV-0005 | AC-0042 / ST-0015 | proposed | delivery-task/QUEST | PlatformProjects_RestoreAndCompileSharedUiReferences<br>AndroidProject_IncludesRequiredLibGit2NativeAssets<br>BrowserProject_StartsWithSharedApplicationViewModelSmoke | Определить, какие non-desktop shells считаются release-supported. |
| CV-0006 | PRODUCT-ENTRY / proposed_notification_error_ux | proposed | guided-artifact-workflow_then_delivery-task_if_tests_change | MainScreen_ErrorToast_RendersAndCloseButtonRemovesMessage already exists: decide story/constraint ownership<br>Notification_UX_SuccessAndErrorToasts_AreLocalizedAndDismissible | Решить, является ли уведомление об ошибках отдельным продуктовым контрактом или частью Settings/storage constraints. |
| CV-0007 | PRODUCT-ENTRY / proposed_attachment_workflow | proposed | guided-artifact-workflow_then_delivery-task_if_tests_change | AttachmentService_GetUploadDownload_RoundTripsUserAttachment<br>AttachmentMapping_PreservesAttachmentMetadataAcrossDomainAndApiMolds | Подтвердить, есть ли attachment workflow в актуальном продукте. |

## BDD Behavior Coverage

| Метрика | Значение |
|---|---:|
| Feature files | 15 |
| Gherkin Rules | 43 |
| Gherkin Scenarios | 43 |
| Active stories со scenarios | 15/15 |
| AC со Gherkin rules | 43/43 |
| AC со Gherkin scenarios | 43/43 |
| Automated or passing scenarios | 41 |
| Draft scenarios | 2 |
| Passing scenarios | 2 |
| Failing scenarios | 0 |
| Scenarios with linked tests | 41/43 |
| Step definitions | 0 |
| Executable specification ratio | 2/43 |

## Validation Evidence

| Проверка | Результат |
|---|---|
| `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore` | passed, только существующие warnings |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageBddContractTests/*" --output Detailed` | passed 7/7 |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed` | failed during attempted ServiceStack API smoke: `ServiceStack LicenseException` free-quota limit on 10 ServiceStack Operations before endpoint assertions; attempted test was not retained |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/ServerStorageLiveIntegrationTests/*" --output Detailed` | passed 1/1 after rollback to retained `TS-0019` scope |
| `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` | OK, 0 errors, 0 warnings |
| `git diff --check` | passed, no whitespace errors |
| `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --maximum-parallel-tests 1 --output Detailed` | timed out after 10 minutes without final summary; scoped server-storage checks remained green |

## Открытые Вопросы

1. Какой licensed/test-host strategy выбрать для ServiceStack task API live smoke, если minimal test-only AppHost тоже блокируется ServiceStack free-quota operation registration?
2. Подтверждать ли Telegram bot как следующую поддерживаемую продуктовую поверхность для `SC-0014-001/002`?

## Рекомендуемый Следующий Шаг

Рекомендуемый следующий шаг: отдельная SPEC/QUEST decision по licensed/test-host strategy для ServiceStack task API live smoke по `AC-0033`; если этот blocker не снимаем сейчас, продолжать `/storm:cover` со следующего ranked gap `ST-0014` Telegram bot.
