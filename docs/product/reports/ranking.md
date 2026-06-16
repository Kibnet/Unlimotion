# STORM Ранжирование С Учётом Зависимостей

Сгенерировано: 2026-06-16
Команда: `/storm:rank` + `/storm:cover ST-0011 ServiceStack API smoke blocker` sync
Режим: delivery-task sync; ranking не пересчитан полностью, статусы `ST-0011` актуализированы по evidence и blocker

## Область

Ранжирование построено для backlog покрытия `CV-*`, сформированного на `/storm:cover`. Partial stories тоже перечислены, но основной порядок работ считается по backlog items, потому что именно они являются следующими исполнимыми решениями или задачами покрытия. После live integration delivery `CV-0001` покрыт `TS-0017`, а `CV-0002` частично закрыт `TS-0017/TS-0018/TS-0019`; follow-up ServiceStack task API live smoke заблокирован ServiceStack free-quota operation registration даже в minimal test-only AppHost.

## Метод

- Dependency graph: `from` должен быть сделан до `to`; для coverage backlog добавлены локальные зависимости `CV-0001 → CV-0002` и `CV-0003 → CV-0004`.
- RICE value: `reach * impact * confidence`.
- Effort cost: `architecture_blast_radius + verification_complexity + dependency_overhead + migration_or_rollout_risk`.
- `priority*`: `value* / cost*`, где `*` учитывает dependency closure.
- Значения являются экспертной оценкой по текущему репозиторию; они нужны для порядка действий, а не как абсолютная продуктовая метрика.

## Проверка Зависимостей

Циклы в ranking dependency graph не обнаружены.

## Ранжированный Backlog

| Ранг | Item | Цель | Story / область | Замыкание | Value* | Cost* | Priority* | Условие |
|---:|---|---|---|---|---:|---:|---:|---|
| 1 | CV-0006 | PRODUCT-ENTRY | proposed_notification_error_ux | CV-0006 | 9.6 | 3 | 3.2 | Можно начать как guided artifact clarification; тесты добавлять отдельным delivery-task |
| 2 | CV-0001 | AC-0032 | ST-0011 | CV-0001 | 10.5 | 7 | 1.5 | Covered by `TS-0017`; оставлено в таблице как historical rank |
| 3 | CV-0002 | AC-0033 | ST-0011 | CV-0001, CV-0002 | 19.6 | 16.5 | 1.1879 | Live SignalR/RavenDB covered by `TS-0019`; remaining gap is ServiceStack task API live smoke blocked by free-quota operation registration |
| 4 | CV-0003 | AC-0039 | ST-0014 | CV-0003 | 4.5 | 6.5 | 0.6923 | Если Telegram bot подтверждается как продуктовая поверхность |
| 5 | CV-0004 | AC-0040 | ST-0014 | CV-0003, CV-0004 | 9.3125 | 15 | 0.6208 | После CV-0003 и выбора test seam для Telegram.Bot/file/Git side effects |
| 6 | CV-0005 | AC-0042 | ST-0015 | CV-0005 | 3.75 | 9 | 0.4167 | После решения, какие non-desktop shells release-supported |
| 7 | CV-0007 | PRODUCT-ENTRY | proposed_attachment_workflow | CV-0007 | 1.35 | 8.5 | 0.1588 | Только после подтверждения attachment workflow как актуального продукта |

## Декомпозиция Effort

| Item | Reach | Impact | Confidence | Архитектура | Проверка | Зависимости | Rollout | Effort | Собственный priority |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CV-0006 | 4 | 3 | 0.8 | 1 | 1 | 0.5 | 0.5 | 3 | 3.2 |
| CV-0001 | 3.5 | 4 | 0.75 | 2 | 3 | 1 | 1 | 7 | 1.5 |
| CV-0002 | 3.5 | 4 | 0.65 | 3 | 3 | 1.5 | 2 | 9.5 | 0.9579 |
| CV-0003 | 2.5 | 3 | 0.6 | 2 | 2.5 | 1 | 1 | 6.5 | 0.6923 |
| CV-0004 | 2.5 | 3.5 | 0.55 | 2.5 | 3 | 1.5 | 1.5 | 8.5 | 0.5662 |
| CV-0005 | 2.5 | 2.5 | 0.6 | 2 | 3 | 2 | 2 | 9 | 0.4167 |
| CV-0007 | 1.5 | 2 | 0.45 | 2 | 3 | 2 | 1.5 | 8.5 | 0.1588 |

## Объяснения По Порядку

### 1. CV-0006

- Цель: PRODUCT-ENTRY
- Зависит от: нет
- Почему здесь: Уже есть orphan test про error toast; дешёвое продуктовое решение резко улучшает traceability без новой инфраструктуры.
- Предусловие: Решить, является ли уведомление об ошибках отдельным продуктовым контрактом или частью Settings/storage constraints.
- Минимальные тесты:
  - MainScreen_ErrorToast_RendersAndCloseButtonRemovesMessage already exists: decide story/constraint ownership
  - Notification_UX_SuccessAndErrorToasts_AreLocalizedAndDismissible

### 2. CV-0001

- Цель: AC-0032
- Зависит от: нет
- Почему здесь: Поток auth/refresh-token является входом во всё покрытие серверного хранилища и снимает риск с AC-0032.
- Предусловие: Подтвердить, что server storage является поддерживаемой продуктовой поверхностью.
- Минимальные тесты:
  - ServerStorage_Login_RegistersWhenUserMissing_StoresAccessAndRefreshTokens
  - ServerStorage_RefreshToken_UpdatesBearerTokenBeforeTaskRequests
  - AuthService_Refresh_RejectsInvalidOrExpiredRefreshToken

### 3. CV-0002

- Цель: AC-0033
- Зависит от: CV-0001
- Почему здесь: Покрытие CRUD и SignalR закрывает основной риск ST-0011, но стоит дороже auth-smoke проверки.
- Предусловие: Выбрать licensed/test-host strategy для ServiceStack task API live smoke без ServiceStack free-quota operation registration, внешнего trial сервиса и production license mutation.
- Минимальные тесты:
  - TaskService_GetAll_ReturnsOnlyAuthenticatedUserTasks
  - TaskService_BulkInsert_UpdatesExistingTaskAndPreservesUserScope
  - ServerStorage_SignalR_Update_RefreshesLocalCacheOrRaisesStorageUpdate
  - ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser (covered by `TS-0019`)
  - ServerStorage_LiveServiceStackTaskApi_BulkInsertGetAllAndGetTask_RoundTripsAuthenticatedUserTasks (blocked before retention by ServiceStack free-quota)

### 4. CV-0003

- Цель: AC-0039
- Зависит от: нет
- Почему здесь: Авторизация команд и базовые команды — минимальный safety contract для Telegram.
- Предусловие: Подтвердить, что Telegram bot входит в публичный продуктовый контракт.
- Минимальные тесты:
  - TelegramBot_StartAndHelp_ReturnLocalizedHelpForAllowedUser
  - TelegramBot_Command_FromUnauthorizedUser_IsRejectedWithoutStorageAccess
  - TelegramBot_Search_ReturnsMatchingTaskButtonsForAllowedUser

### 5. CV-0004

- Цель: AC-0040
- Зависит от: CV-0003
- Почему здесь: Callback-действия и sync timers имеют больше побочных эффектов, поэтому должны идти после command/auth coverage.
- Предусловие: Выбрать test seam для Telegram.Bot API и file/Git side effects.
- Минимальные тесты:
  - TelegramBot_Callback_CreateSubTask_AddsChildToSelectedTask
  - TelegramBot_Callback_SetStatus_UpdatesTaskStatusAndHistory
  - TelegramBot_GitTimers_DoNotRunWhenConflictResolutionIsInProgress

### 6. CV-0005

- Цель: AC-0042
- Зависит от: нет
- Почему здесь: Platform smoke coverage полезен, но без продуктовой политики легко закрепить неподдерживаемую поверхность.
- Предусловие: Определить, какие non-desktop shells считаются release-supported.
- Минимальные тесты:
  - PlatformProjects_RestoreAndCompileSharedUiReferences
  - AndroidProject_IncludesRequiredLibGit2NativeAssets
  - BrowserProject_StartsWithSharedApplicationViewModelSmoke

### 7. CV-0007

- Цель: PRODUCT-ENTRY
- Зависит от: нет
- Почему здесь: Attachment-код есть, но пользовательский workflow не подтверждён, поэтому value и confidence ниже.
- Предусловие: Подтвердить, есть ли attachment workflow в актуальном продукте.
- Минимальные тесты:
  - AttachmentService_GetUploadDownload_RoundTripsUserAttachment
  - AttachmentMapping_PreservesAttachmentMetadataAcrossDomainAndApiMolds

## Story-level Кандидаты

| Story | Coverage items | Рекомендация |
|---|---|---|
| ST-0011: Использовать optional серверного хранилища с authentication и real-time updates | CV-0001, CV-0002 | `CV-0001` покрыт, `CV-0002` имеет critical live SignalR coverage; для full coverage нужно сначала снять ServiceStack license/test-host blocker. |
| ST-0014: Получать доступ к задачам через Telegram bot | CV-0003, CV-0004 | Если bot публичный, начинать с command/auth coverage CV-0003. |
| ST-0015: Собирать, обновлять и проверять cross-platform application shells | CV-0005 | Сначала принять platform support policy, затем добавлять smoke coverage. |

## Практический Вывод

1. Сначала разобрать `CV-0006`: это дешёвое продуктовое решение по notification/error UX, уже связанное с существующим orphan test.
2. Если продолжать server storage, брать узкую follow-up SPEC по licensed/test-host strategy для ServiceStack task API live smoke; иначе переходить к `ST-0014` Telegram bot.
3. Если Telegram bot поддерживается, брать `CV-0003`, затем `CV-0004`; иначе не держать ST-0014 как долг покрытия.
4. `CV-0005` выполнять только после решения по non-desktop platform support.
5. `CV-0007` имеет самый низкий приоритет до подтверждения attachment workflow.

## Рекомендуемый Следующий Шаг

Следующий шаг — выбрать между узким `ST-0011` follow-up для снятия ServiceStack API smoke blocker и переходом к `ST-0014` command/auth coverage. Для добавления тестов по выбранному `CV-*` нужен `delivery-task` через QUEST gate.
