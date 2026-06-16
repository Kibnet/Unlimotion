# STORM Bootstrap Stories

Сгенерировано: 2026-06-16
Репозиторий: Kibnet/Unlimotion
Маршрут: guided-artifact-workflow
Язык продуктового артефакта: русский

## Область

Отчёт отражает текущие product artifacts после delivery sync для `ST-0014/CV-0003`. Исходные Vision, Product Goal, Needs, Constraints, Stories, AC, Tests, Code Units, conflicts, dependencies и предыдущие coverage findings сохранены.

## Продуктовая гипотеза

Unlimotion — менеджер задач с local-first подходом для сложных графов работы. Текущий продуктовый контракт строится вокруг неограниченной вложенности, нескольких родительских контекстов, блокирующих зависимостей, доступности с учётом планирования, нескольких рабочих представлений, roadmap-визуализации, локального JSON-хранилища, необязательной Git/server/Telegram-синхронизации, notification/error UX и кроссплатформенных Avalonia-оболочек.

## Потребности

| ID | Потребность | Статус | Уверенность |
| --- | --- | --- | --- |
| ND-0001 | Представлять сложную декомпозицию работы без принудительной единственной иерархии | inferred | 0.86 |
| ND-0002 | Показывать, над чем можно работать сейчас | inferred | 0.84 |
| ND-0003 | Смотреть один набор задач через разные рабочие контексты | inferred | 0.83 |
| ND-0004 | Сохранять пользовательские данные надёжно, локально и восстанавливаемо | inferred | 0.82 |
| ND-0005 | Комфортно пользоваться приложением на разных ширинах, платформах и языках | inferred | 0.79 |
| ND-0006 | Открывать задачи для автоматизации и внешних каналов | needs_review | 0.62 |

## Истории

| ID | История | Статус | Needs | Tests | Scenarios | Покрытие |
| --- | --- | --- | --- | --- | --- | --- |
| ST-0001 | Управлять задачами как гибкой иерархией с несколькими родителями и зависимостями | implemented | ND-0001 | TS-0001<br>TS-0004<br>TS-0014 | SC-0001-001<br>SC-0001-002<br>SC-0001-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0002 | Вести жизненный цикл задачи через явные статусы, историю и критерии завершения | implemented | ND-0002 | TS-0003<br>TS-0005<br>TS-0014 | SC-0002-001<br>SC-0002-002<br>SC-0002-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0003 | Вычислять доступность задач и очередь Unlocked | implemented | ND-0002 | TS-0002<br>TS-0003<br>TS-0005<br>TS-0014 | SC-0003-001<br>SC-0003-002<br>SC-0003-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0004 | Навигировать задачи через вкладки рабочих представлений | implemented | ND-0002<br>ND-0003 | TS-0001<br>TS-0004<br>TS-0011<br>TS-0016 | SC-0004-001<br>SC-0004-002<br>SC-0004-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0005 | Искать и фильтровать задачи по тексту, статусу, датам, длительности, wanted и emoji | implemented | ND-0003 | TS-0001<br>TS-0004<br>TS-0006<br>TS-0013 | SC-0005-001<br>SC-0005-002<br>SC-0005-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0006 | Планировать задачи через даты, длительность, повторения, wanted и importance | implemented | ND-0002 | TS-0005<br>TS-0013 | SC-0006-001<br>SC-0006-002<br>SC-0006-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0007 | Редактировать детальную карточку задачи и блоки отношений на десктопных и узких компоновках | implemented | ND-0001<br>ND-0005 | TS-0003<br>TS-0005<br>TS-0008 | SC-0007-001<br>SC-0007-002<br>SC-0007-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0008 | Визуализировать и менять граф задач через Roadmap | implemented | ND-0003 | TS-0006<br>TS-0007 | SC-0008-001<br>SC-0008-002<br>SC-0008-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0009 | Сохранять локальные задачи и безопасно мигрировать legacy-данные | implemented | ND-0004 | TS-0003<br>TS-0014 | SC-0009-001<br>SC-0009-002<br>SC-0009-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0010 | Делать backup и синхронизацию локального task storage через Git | implemented | ND-0004 | TS-0008<br>TS-0009 | SC-0010-001<br>SC-0010-002<br>SC-0010-003<br>SC-0010-004 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0011 | Использовать optional серверного хранилища с authentication и real-time updates | partial | ND-0004 | TS-0017<br>TS-0018<br>TS-0019<br>TS-0020 | SC-0011-001<br>SC-0011-002 | SC-0011-001/002 имеют passing contract/security evidence; TS-0019 добавляет live SignalR/RavenDB delivery evidence, а TS-0020 закрывает live ServiceStack HTTP task API smoke через narrow TaskService registration. |
| ST-0012 | Настраивать appearance, storage, backup, updates и localization из Settings | implemented | ND-0005 | TS-0008<br>TS-0009<br>TS-0012<br>TS-0015 | SC-0012-001<br>SC-0012-002<br>SC-0012-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0013 | Копировать и вставлять task outlines через clipboard | implemented | ND-0001 | TS-0001<br>TS-0004<br>TS-0010 | SC-0013-001<br>SC-0013-002 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0014 | Получать доступ к задачам через Telegram bot | partial | ND-0006 | TS-0022 | SC-0014-001<br>SC-0014-002 | SC-0014-001 имеет passing command/auth evidence через TS-0022; SC-0014-002 callbacks/Git timers остаётся draft для CV-0004. |
| ST-0015 | Собирать, обновлять и проверять cross-platform application shells | partial | ND-0005<br>ND-0006 | TS-0011<br>TS-0015 | SC-0015-001<br>SC-0015-002<br>SC-0015-003 | BDD-сценарии связаны с существующими тестами; tests не запускались в artifact-only BDD sync. |
| ST-0016 | Понимать ошибки операций через in-app уведомления | implemented | ND-0005 | TS-0021 | SC-0016-001 | SC-0016-001 связан с существующим UI test TS-0021; targeted UI run passed 1/1 2026-06-16. |

## Ограничения

| ID | Ограничение | Статус |
| --- | --- | --- |
| CN-0001 | Локальные данные задач остаются JSON-based, восстанавливаемыми и migration-aware | active |
| CN-0002 | Согласованность графа задач двунаправленная и защищена от self-relation | active |
| CN-0003 | Переходы статусов учитывают доступность и критерии завершения | active |
| CN-0004 | UI-поведение остаётся responsive и покрытым UI-автоматизацией | active |
| CN-0005 | Remote sync защищает существующие локальные и удалённые данные | active |
| CN-0006 | Пользовательские тексты и option labels локализуются | active |
| CN-0007 | Requirement-level поведение защищено serial TUnit и UI test suites | active |
| CN-0008 | Platform shells используют одну Avalonia task UI модель | active |

## Наборы Тестов

| ID | Набор | Path | Stories | AC | Scenarios |
| --- | --- | --- | --- | --- | --- |
| TS-0001 | MainWindowViewModelTests | src/Unlimotion.Test/MainWindowViewModelTests.cs | ST-0001<br>ST-0004<br>ST-0005<br>ST-0013 | AC-0001<br>AC-0002<br>AC-0010<br>AC-0011<br>AC-0013<br>AC-0037<br>AC-0038 | SC-0001-001<br>SC-0001-002<br>SC-0004-001<br>SC-0004-002<br>SC-0005-001<br>SC-0013-001<br>SC-0013-002 |
| TS-0002 | TaskAvailabilityCalculationTests | src/Unlimotion.Test/TaskAvailabilityCalculationTests.cs | ST-0003 | AC-0007<br>AC-0008<br>AC-0009 | SC-0003-001<br>SC-0003-002<br>SC-0003-003 |
| TS-0003 | Task status domain, transition, mapping, migration, and storage tests | src/Unlimotion.Test/TaskStatus*Tests.cs; src/Unlimotion.Test/FileStorageTaskStatusTests.cs | ST-0002<br>ST-0003<br>ST-0007<br>ST-0009 | AC-0004<br>AC-0005<br>AC-0006<br>AC-0007<br>AC-0009<br>AC-0021<br>AC-0026 | SC-0002-001<br>SC-0002-002<br>SC-0002-003<br>SC-0003-001<br>SC-0003-003<br>SC-0007-003<br>SC-0009-002 |
| TS-0004 | MainControlTreeCommandsUiTests | src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs | ST-0001<br>ST-0004<br>ST-0005<br>ST-0013 | AC-0001<br>AC-0003<br>AC-0010<br>AC-0011<br>AC-0012<br>AC-0013<br>AC-0037<br>AC-0038 | SC-0001-001<br>SC-0001-003<br>SC-0004-001<br>SC-0004-002<br>SC-0004-003<br>SC-0005-001<br>SC-0013-001<br>SC-0013-002 |
| TS-0005 | Task card, status icon, and related MainControl UI tests | src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs; src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs; src/Unlimotion.Test/MainControlAvailabilityUiTests.cs | ST-0002<br>ST-0003<br>ST-0006<br>ST-0007 | AC-0004<br>AC-0005<br>AC-0007<br>AC-0016<br>AC-0018<br>AC-0019<br>AC-0020<br>AC-0021 | SC-0002-001<br>SC-0002-002<br>SC-0003-001<br>SC-0006-001<br>SC-0006-003<br>SC-0007-001<br>SC-0007-002<br>SC-0007-003 |
| TS-0006 | Filter toolbar, reset filter, and responsive filter tests | src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs; src/Unlimotion.Test/MainControlResetFiltersUiTests.cs | ST-0005<br>ST-0008 | AC-0013<br>AC-0014<br>AC-0015<br>AC-0024 | SC-0005-001<br>SC-0005-002<br>SC-0005-003<br>SC-0008-003 |
| TS-0007 | RoadmapGraphUiTests | src/Unlimotion.Test/RoadmapGraphUiTests.cs | ST-0008 | AC-0022<br>AC-0023<br>AC-0024 | SC-0008-001<br>SC-0008-002<br>SC-0008-003 |
| TS-0008 | SettingsViewModel and SettingsControl responsive tests | src/Unlimotion.Test/SettingsViewModelTests.cs; src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs | ST-0007<br>ST-0010<br>ST-0012 | AC-0020<br>AC-0028<br>AC-0029<br>AC-0030<br>AC-0034<br>AC-0035<br>AC-0036 | SC-0007-002<br>SC-0010-001<br>SC-0010-002<br>SC-0010-003<br>SC-0012-001<br>SC-0012-002<br>SC-0012-003 |
| TS-0009 | Git backup and SSH sync tests | src/Unlimotion.Test/BackupViaGitServiceTests.cs; src/Unlimotion.Test/GitBackupJobTests.cs; src/Unlimotion.Test/GitSafeDirectoryConfigTests.cs | ST-0010<br>ST-0012 | AC-0028<br>AC-0029<br>AC-0030<br>AC-0031<br>AC-0035 | SC-0010-001<br>SC-0010-002<br>SC-0010-003<br>SC-0010-004<br>SC-0012-002 |
| TS-0010 | TaskOutlineClipboardServiceTests and paste UI coverage | src/Unlimotion.Test/TaskOutlineClipboardServiceTests.cs; src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs | ST-0013 | AC-0037<br>AC-0038 | SC-0013-001<br>SC-0013-002 |
| TS-0011 | AppAutomation Headless and FlaUI scenario suites | tests/Unlimotion.UiTests.Headless/**/*.cs; tests/Unlimotion.UiTests.FlaUI/**/*.cs; tests/Unlimotion.UiTests.Authoring/**/*.cs | ST-0004<br>ST-0015 | AC-0010<br>AC-0011<br>AC-0012<br>AC-0041<br>AC-0043 | SC-0004-001<br>SC-0004-002<br>SC-0004-003<br>SC-0015-001<br>SC-0015-003 |
| TS-0012 | Localization tests | src/Unlimotion.Test/LocalizationSettingsTests.cs; src/Unlimotion.Test/LocalizationDisplayDefinitionTests.cs | ST-0012 | AC-0034 | SC-0012-001 |
| TS-0013 | Planning metadata tests | src/Unlimotion.Test/MainControlDateQuickSelectionUiTests.cs; src/Unlimotion.Test/MainControlWantedUiTests.cs; src/Unlimotion.Test/TaskImportanceUiTests.cs; src/Unlimotion.Test/TaskItemRepeaterListMarkerTests.cs; src/Unlimotion.Test/TaskListRepeaterMarkerUiTests.cs | ST-0005<br>ST-0006 | AC-0014<br>AC-0016<br>AC-0017<br>AC-0018 | SC-0005-002<br>SC-0006-001<br>SC-0006-002<br>SC-0006-003 |
| TS-0014 | Storage, migration, startup projection, and Восстановление JSON tests | src/Unlimotion.Test/UnifiedTaskStorageMigrationRegressionTests.cs; src/Unlimotion.Test/StartupProjectionAndRelationsTests.cs; src/Unlimotion.Test/TaskMigratorTests.cs; src/Unlimotion.Test/JsonRepairingReaderTests.cs | ST-0001<br>ST-0002<br>ST-0003<br>ST-0009 | AC-0002<br>AC-0006<br>AC-0008<br>AC-0025<br>AC-0026<br>AC-0027 | SC-0001-002<br>SC-0002-003<br>SC-0003-002<br>SC-0009-001<br>SC-0009-002<br>SC-0009-003 |
| TS-0015 | Startup, loading, update, keyboard inset, and packaging compatibility tests | src/Unlimotion.Test/SingleViewStartupUiTests.cs; src/Unlimotion.Test/MainScreenLoadingUiTests.cs; src/Unlimotion.Test/PackageUpdateCompatibilityUiTests.cs; src/Unlimotion.Test/KeyboardAwareScrollViewerUiTests.cs | ST-0012<br>ST-0015 | AC-0036<br>AC-0041<br>AC-0042<br>AC-0043 | SC-0012-003<br>SC-0015-001<br>SC-0015-002<br>SC-0015-003 |
| TS-0016 | BreadcrumbEmojiUiTests and last-opened README demo checks | src/Unlimotion.Test/BreadcrumbEmojiUiTests.cs; tests/Unlimotion.UiTests.Headless/Tests/ReadmeDemoHeadlessTests.cs | ST-0004 | AC-0011 | SC-0004-002 |
| TS-0017 | ServerStorageBddContractTests | src/Unlimotion.Test/ServerStorageBddContractTests.cs | ST-0011 | AC-0032<br>AC-0033 | SC-0011-001<br>SC-0011-002 |
| TS-0018 | TaskService_GetTask_PreservesAuthenticatedUserScope | src/Unlimotion.Test/ServerStorageBddContractTests.cs | ST-0011 | AC-0033 | SC-0011-002 |
| TS-0019 | ServerStorage_LiveSignalR_SaveTask_DeliversUpdateToSecondClientForSameUser | src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs | ST-0011 | AC-0033 | SC-0011-002 |
| TS-0020 | ServerStorage_LiveServiceStackTaskApi_BulkInsertGetAllAndGetTask_RoundTripsAuthenticatedUserTasks | src/Unlimotion.Test/ServerStorageLiveIntegrationTests.cs | ST-0011 | AC-0033 | SC-0011-002 |
| TS-0021 | ToastNotificationUiTests | src/Unlimotion.Test/ToastNotificationUiTests.cs | ST-0016 | AC-0044 | SC-0016-001 |
| TS-0022 | TelegramBotCommandAuthorizationTests | src/Unlimotion.Test/TelegramBotCommandAuthorizationTests.cs | ST-0014 | AC-0039 | SC-0014-001 |

## Конфликты

| ID | Статус | Конфликт |
| --- | --- | --- |
| CF-0001 | resolved |  |
| CF-0002 | resolved |  |
| CF-0003 | resolved |  |
| CF-0004 | needs_review |  |

## Открытые Вопросы

1. Брать ли `CV-0004` как следующий Telegram callback coverage task?
2. Какие non-desktop shells сейчас release-supported: Android, browser, iOS или ни один?
3. Подтвердить, есть ли attachment workflow в актуальном продукте.
4. Нужно ли расширять `ST-0016` на success-toast localization/dismissibility отдельным delivery-task?

## Рекомендуемый следующий STORM-шаг

Продолжать `/storm:cover` с `CV-0004` либо сначала принять product/platform decision для `ST-0015/CV-0005`.
