# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-16
Команда: `/storm:rank` + `/storm:cover ST-0014 CV-0003 delivery sync`
Режим: delivery-task sync; ranking не пересчитан полностью, статусы `CV-0003` актуализированы по evidence

## Область

Ранжирование построено для backlog покрытия `CV-*`. После текущего delivery `CV-0003` закрыт: `TelegramBotCommandAuthorizationTests` связаны с `ST-0014/AC-0039/SC-0014-001`. Следующий Telegram gap — `CV-0004`.

## Метод

- Dependency graph: `from` должен быть сделан до `to`; для coverage backlog добавлены локальные зависимости `CV-0001 -> CV-0002` и `CV-0003 -> CV-0004`.
- RICE value: `reach * impact * confidence`.
- Effort cost: `architecture_blast_radius + verification_complexity + dependency_overhead + migration_or_rollout_risk`.
- `priority*`: `value* / cost*`, где `*` учитывает dependency closure.

## Проверка Зависимостей

Циклы в ranking dependency graph не обнаружены.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Замыкание | Value* | Cost* | Priority* | Status | Условие |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | CV-0006 | PRODUCT-ENTRY | ST-0016 | CV-0006 | 9.6 | 3 | 3.2 | covered_by_product_story_and_existing_ui_test | Artifact-only product-entry decision accepted; existing UI test linked to ST-0016/SC-0016-001. |
| 2 | CV-0001 | AC-0032 | ST-0011 | CV-0001 | 10.5 | 7 | 1.5 | covered_by_contract_tests | Если server storage подтверждается как поддерживаемая поверхность |
| 3 | CV-0002 | AC-0033 | ST-0011 | CV-0001<br>CV-0002 | 19.6 | 16.5 | 1.1879 | covered_by_live_task_api_and_signalr_tests | После CV-0001; live SignalR/RavenDB и narrow ServiceStack task API smoke покрыты. |
| 4 | CV-0003 | AC-0039 | ST-0014 | CV-0003 | 4.5 | 6.5 | 0.6923 | covered_by_telegram_command_auth_tests | Covered by TS-0022; CV-0004 callbacks/Git timers remain follow-up. |
| 5 | CV-0004 | AC-0040 | ST-0014 | CV-0003<br>CV-0004 | 9.3125 | 15 | 0.6208 | proposed | После CV-0003 и выбора test seam для Telegram.Bot/file/Git side effects |
| 6 | CV-0005 | AC-0042 | ST-0015 | CV-0005 | 3.75 | 9 | 0.4167 | proposed | После решения, какие non-desktop shells release-supported |
| 7 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | CV-0007 | 1.35 | 8.5 | 0.1588 | proposed | Только после подтверждения attachment workflow как актуального продукта |

## Объяснения По Порядку

### 1. CV-0006

- Цель: PRODUCT-ENTRY
- Story / область: ST-0016
- Статус: covered_by_product_story_and_existing_ui_test
- Зависит от: нет
- Почему здесь: Существующий error-toast UI test теперь трассируется до продуктовой story, AC, Gherkin rule и scenario.
- Предусловие/результат: TS-0021 passed 1/1 on 2026-06-16; no test/code/annotation changes were made for CV-0006.
- Минимальные тесты:
  - MainScreen_ErrorToast_RendersAndCloseButtonRemovesMessage covered by TS-0021
  - Notification_UX_SuccessAndErrorToasts_AreLocalizedAndDismissible remains optional future expansion if product requires success toast localization contract

### 2. CV-0001

- Цель: AC-0032
- Story / область: ST-0011
- Статус: covered_by_contract_tests
- Зависит от: нет
- Почему здесь: Auth/refresh-token flow является входом в весь server storage coverage и снимает риск с AC-0032.
- Предусловие/результат: Подтвердить, что server storage является поддерживаемой продуктовой поверхностью.
- Минимальные тесты:
  - ServerStorage_Login_RegistersWhenUserMissing_StoresAccessAndRefreshTokens
  - ServerStorage_RefreshToken_UpdatesBearerTokenBeforeTaskRequests
  - AuthService_Refresh_RejectsInvalidOrExpiredRefreshToken

### 3. CV-0002

- Цель: AC-0033
- Story / область: ST-0011
- Статус: covered_by_live_task_api_and_signalr_tests
- Зависит от: CV-0001
- Почему здесь: CRUD и SignalR coverage закрывают основной риск ST-0011, но стоят дороже auth-smoke.
- Предусловие/результат: Выполнено через narrow TaskService registration без production license mutation.
- Минимальные тесты:
  - TaskService_GetAll_ReturnsOnlyAuthenticatedUserTasks
  - TaskService_BulkInsert_UpdatesExistingTaskAndPreservesUserScope
  - ServerStorage_SignalR_Update_RefreshesLocalCacheOrRaisesStorageUpdate
  - ServerStorage_LiveServiceStackTaskApi_BulkInsertGetAllAndGetTask_RoundTripsAuthenticatedUserTasks

### 4. CV-0003

- Цель: AC-0039
- Story / область: ST-0014
- Статус: covered_by_telegram_command_auth_tests
- Зависит от: нет
- Почему здесь: Command authorization и базовые команды получили deterministic TUnit evidence без real Telegram API.
- Предусловие/результат: TS-0022 passed 7/7 on 2026-06-16.
- Минимальные тесты:
  - TelegramBotCommand_UnauthorizedUser_DoesNotSendMessagesOrQueryTasks
  - TelegramBotCommand_StartAndHelp_ReturnRussianCommandTextForAllowedUser
  - TelegramBotCommand_SearchWithResults_ShowsTaskListForAllowedUser
  - TelegramBotCommand_TaskCommand_RoutesTaskIdToResponderForAllowedUser
  - TelegramBotCommand_RootWithResults_ShowsRootTaskListForAllowedUser

### 5. CV-0004

- Цель: AC-0040
- Story / область: ST-0014
- Статус: proposed
- Зависит от: CV-0003
- Почему здесь: Callbacks и sync timers имеют больше side effects, поэтому должны идти после command/auth coverage.
- Предусловие/результат: Выбрать test seam для Telegram.Bot API и file/Git side effects.
- Минимальные тесты:
  - TelegramBot_Callback_CreateSubTask_AddsChildToSelectedTask
  - TelegramBot_Callback_SetStatus_UpdatesTaskStatusAndHistory
  - TelegramBot_GitTimers_DoNotRunWhenConflictResolutionIsInProgress

### 6. CV-0005

- Цель: AC-0042
- Story / область: ST-0015
- Статус: proposed
- Зависит от: нет
- Почему здесь: Platform smoke coverage полезен, но без product policy легко закрепить неподдерживаемую поверхность.
- Предусловие/результат: Определить, какие non-desktop shells считаются release-supported.
- Минимальные тесты:
  - PlatformProjects_RestoreAndCompileSharedUiReferences
  - AndroidProject_IncludesRequiredLibGit2NativeAssets
  - BrowserProject_StartsWithSharedApplicationViewModelSmoke

### 7. CV-0007

- Цель: PRODUCT-ENTRY
- Story / область: proposed_attachment_workflow
- Статус: proposed
- Зависит от: нет
- Почему здесь: Attachment-код есть, но пользовательский workflow не подтверждён, поэтому value и confidence ниже.
- Предусловие/результат: Подтвердить, есть ли attachment workflow в актуальном продукте.
- Минимальные тесты:
  - AttachmentService_GetUploadDownload_RoundTripsUserAttachment
  - AttachmentMapping_PreservesAttachmentMetadataAcrossDomainAndApiMolds

## Story-level Кандидаты

| Story | Coverage items | Рекомендация |
| --- | --- | --- |
| ST-0011: Использовать optional серверного хранилища с authentication и real-time updates | CV-0001<br>CV-0002 | CV-0001 и CV-0002 покрыты; для следующего /storm:cover выбирать другой ranked gap. |
| ST-0016: Понимать ошибки операций через in-app уведомления | CV-0006 | CV-0006 закрыт artifact-only; optional success-toast/localization coverage оформлять отдельной SPEC только при продуктовой необходимости. |
| ST-0014: Получать доступ к задачам через Telegram bot | CV-0003<br>CV-0004 | CV-0003 покрыт; если Telegram bot остаётся supported, следующим брать CV-0004 callbacks/status/relation/Git timer coverage. |
| ST-0015: Собирать, обновлять и проверять cross-platform application shells | CV-0005 | Сначала принять platform support policy, затем добавлять smoke coverage. |

## Практический Вывод

1. `CV-0006`, `CV-0001`, `CV-0002` и `CV-0003` покрыты.
2. Если Telegram bot остаётся supported, следующий логичный delivery — `CV-0004`: callbacks, status/relation flows и Git timers.
3. `CV-0005` выполнять только после решения по non-desktop platform support.
4. `CV-0007` остаётся низким приоритетом до подтверждения attachment workflow.

## Рекомендуемый Следующий Шаг

Продолжать /storm:cover с CV-0004 Telegram callbacks/status/relation/Git timer coverage либо принять platform policy для ST-0015/CV-0005; для test/code changes нужен QUEST delivery-task.
