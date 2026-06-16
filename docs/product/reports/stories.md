# STORM Bootstrap Stories

Сгенерировано: 2026-06-12
Репозиторий: Kibnet/Unlimotion
Маршрут: guided-artifact-workflow
Язык продуктового артефакта: русский

## Область

`/storm:bootstrap` восстановил текущую продуктовую спецификацию по evidence из отслеживаемых файлов репозитория без изменений функционального кода и тестов.

Доказательства инвентаризации:

- 682 отслеживаемых файла были инвентаризованы через `git ls-files`.
- Основные источники: `README.md`, `README.RU.md`, `.qoder/repowiki/en/content/**/*.md`, `specs/*.md`, `src/**/*.cs`, `src/**/*.axaml`, `tests/**/*.cs`, `.github/workflows/*.yml`.
- Текущие README, код и тесты считаются более авторитетными, чем старые сгенерированными страницами repowiki, если источники расходятся. Главное найденное расхождение: часть repowiki всё ещё описывает старую модель из четырёх статусов, а текущие README, код и тесты используют пять статусов с `Prepared`.

## Продуктовая гипотеза

Unlimotion — менеджер задач с local-first подходом для сложных графов работы. Текущий продуктовый контракт строится вокруг неограниченной вложенности, нескольких родительских контекстов, блокирующих зависимостей, доступности с учётом планирования, нескольких рабочих представлений, roadmap-визуализации, локального JSON-хранилища, необязательной Git/server-синхронизации, разрешения конфликтов и кроссплатформенных Avalonia-оболочек.

Уверенность: средне-высокая для локального desktop-поведения, графа задач и Git-синхронизации; средняя или низкая для серверного хранилища, Telegram bot и зрелости non-desktop платформ до подтверждения владельцем продукта и добавления runtime-доказательств.

## Потребности

| ID | Потребность | Статус | Уверенность |
|---|---|---|---:|
| ND-0001 | Представлять сложную декомпозицию работы без принудительной единственной иерархии | inferred | 0.86 |
| ND-0002 | Показывать, над чем можно работать сейчас | inferred | 0.84 |
| ND-0003 | Смотреть один набор задач через разные рабочие контексты | inferred | 0.83 |
| ND-0004 | Сохранять пользовательские данные надёжно, локально и восстанавливаемо | inferred | 0.82 |
| ND-0005 | Комфортно пользоваться приложением на разных ширинах, платформах и языках | inferred | 0.79 |
| ND-0006 | Открывать задачи для автоматизации и внешних каналов | needs_review | 0.62 |

## Истории

| ID | История | Статус | Основные доказательства | Покрытие |
|---|---|---|---|---|
| ST-0001 | Управлять задачами как гибкой иерархией с несколькими родителями и зависимостями | implemented | `TaskItem`, `TaskTreeManager`, README-документация иерархии | critical/full через `TS-0001`, `TS-0004`, `TS-0008`, `TS-0014` |
| ST-0002 | Вести жизненный цикл задачи через явные статусы, историю и критерии завершения | implemented | `TaskStatus`, `TaskItem`, спецификация статусной модели | critical/full через `TS-0003`, `TS-0005` |
| ST-0003 | Вычислять доступность задач и очередь Unlocked | implemented | `CalculateAndUpdateAvailability`, тесты доступности | critical/full через `TS-0002`, `TS-0003`, `TS-0005` |
| ST-0004 | Навигировать задачи через вкладки рабочих представлений | implemented | README-вкладки, `MainWindowViewModel` modes, `MainControl` | partial/critical/full через UI suites |
| ST-0005 | Искать и фильтровать задачи по тексту, статусу, датам, длительности, wanted и emoji | implemented | `SearchDefinition`, панель фильтров, спеки emoji-фильтра | critical через `TS-0004`, `TS-0006`, `TS-0013` |
| ST-0006 | Планировать задачи через даты, длительность, повторения, wanted и importance | implemented | `TaskItem`, `RepeaterPattern`, спеки планирования | critical через `TS-0013`, `TS-0005` |
| ST-0007 | Редактировать детальную карточку задачи и блоки отношений на десктопных и узких компоновках | implemented | `MainControl` карточка задачи, спеки карточки задачи | critical через `TS-0005`, `TS-0008` |
| ST-0008 | Визуализировать и менять граф задач через Roadmap | implemented | `GraphControl`, `RoadmapGraphBuilder`, спеки roadmap | critical через `TS-0006`, `TS-0007` |
| ST-0009 | Сохранять локальные задачи и безопасно мигрировать legacy-данные | implemented | `FileStorage`, `UnifiedTaskStorage`, migrations | critical через `TS-0003`, `TS-0014` |
| ST-0010 | Делать backup и синхронизацию локального task storage через Git | implemented | `BackupViaGitService`, разрешение конфликтов, Git specs | critical через `TS-0008`, `TS-0009` |
| ST-0011 | Использовать optional серверного хранилища с authentication и real-time updates | partial | `ServerStorage`, ServiceStack services, SignalR hub | critical по auth/security/live SignalR через `TS-0017`, `TS-0018`, `TS-0019`; ServiceStack API live smoke остаётся blocker из-за ServiceStack free-quota operation registration |
| ST-0012 | Настраивать appearance, storage, backup, updates и localization из Settings | implemented | `SettingsViewModel`, `SettingsControl`, resources | critical через `TS-0008`, `TS-0012`, `TS-0015` |
| ST-0013 | Копировать и вставлять task outlines через clipboard | implemented | `TaskOutlineClipboardService`, paste preview | critical через `TS-0010`, `TS-0004` |
| ST-0014 | Получать доступ к задачам через Telegram bot | partial | `Unlimotion.TelegramBot` | partial, только ручную/архитектурную проверку |
| ST-0015 | Собирать, обновлять и проверять cross-platform application shells | partial | platform csproj-файлы, workflows, Velopack service | smoke/partial/critical в зависимости от platform |

## Ограничения

| ID | Ограничение | Статус |
|---|---|---|
| CN-0001 | Локальные данные задач остаются JSON-based, восстанавливаемыми и migration-aware | active |
| CN-0002 | Согласованность графа задач двунаправленная и защищена от self-relation | active |
| CN-0003 | Переходы статусов учитывают доступность и критерии завершения | active |
| CN-0004 | UI-поведение остаётся responsive и покрытым UI-автоматизацией | active |
| CN-0005 | Remote sync защищает существующие локальные и удалённые данные | active |
| CN-0006 | Пользовательские тексты и option labels локализуются | active |
| CN-0007 | Requirement-level поведение защищено serial TUnit и UI test suites | active |
| CN-0008 | Platform shells используют одну Avalonia task UI модель | active |

## Технические опоры

| ID | Техническая опора | Статус |
|---|---|---|
| EN-0001 | Общая Avalonia MVVM архитектура | active |
| EN-0002 | Единая storage abstraction над file и server backends | active |
| EN-0003 | Детерминированная UI-автоматизация и README media pipeline | active |
| EN-0004 | Release и update pipeline | active |

## Наборы тестов

| ID | Набор | Основное покрытие |
|---|---|---|
| TS-0001 | `MainWindowViewModelTests` | task graph и view model behavior |
| TS-0002 | `TaskAvailabilityCalculationTests` | availability/unlocked semantics |
| TS-0003 | `TaskStatus*Tests`, `FileStorageTaskStatusTests` | status domain, transition, mapping, migration |
| TS-0004 | `MainControlTreeCommandsUiTests` | tree commands, hotkeys, search restore, outline copy/paste |
| TS-0005 | карточка задачи/status/availability UI tests | карточка задачи layout и status UI |
| TS-0006 | панель фильтров/reset UI tests | search/filter responsiveness |
| TS-0007 | `RoadmapGraphUiTests` | graph projection, roadmap interactions, viewport |
| TS-0008 | settings ViewModel/UI tests | settings, backup, conflict resolver |
| TS-0009 | Git backup/SSH tests | Git sync, connect, SSH, conflicts |
| TS-0010 | outline clipboard tests | copy/paste outline behavior |
| TS-0011 | headless/FlaUI scenario suites | end-to-end UI smoke и README demo |
| TS-0012 | localization tests | language и display definitions |
| TS-0013 | planning metadata tests | dates, wanted, importance, repeaters |
| TS-0014 | storage/migration/Восстановление JSON tests | local storage durability |
| TS-0015 | startup/loading/update/platform tests | update и platform-adjacent behavior |

## Конфликты

| ID | Статус | Конфликт |
|---|---|---|
| CF-0001 | resolved | Local-first владение данными vs удобство cross-device sync |
| CF-0002 | resolved | Гибкость multi-parent graph vs сложность consistency и availability |
| CF-0003 | resolved | Rich карточка задачи editing vs compact/narrow UI usability |
| CF-0004 | needs_review | Cross-platform/external access vs риск завысить зрелость неподтверждённых поверхностей |

## Открытые вопросы

1. Считать ли серверного хранилища поддерживаемой cloud-поверхностью или экспериментальным/вторичным backend?
2. Должно ли поведение Telegram bot входить в публичный продуктовый контракт и получать автоматические тесты?
3. Какие non-desktop shells сейчас release-supported: Android, browser, iOS или ни один?
4. Какая явная продуктовая метрика должна определять успех roadmap: размер графа, читаемость layout, скорость rebuild или точность планирования?
5. Нужно ли показывать пользователю explainability доступности: blockers, planned date и невыполненные criteria?

## Рекомендуемый следующий STORM-шаг

Следующий шаг — `/storm:trace`. Бутстрап связывает истории с агрегированными наборами тестов; трассировка должна уточнить доказательства на уровне acceptance criteria, найти orphan tests и разделить широкие наборы на более точное продуктовое покрытие.
