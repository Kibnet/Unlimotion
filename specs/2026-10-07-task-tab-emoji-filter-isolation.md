# Независимые emoji-фильтры вкладок задач

## 0. Метаданные

- Фаза: EXEC; пользователь подтвердил «Спеку подтверждаю» 2026-10-07.
- Тип: `delivery-task`; профиль `dotnet-desktop-client`, overlay `ui-automation-testing`, context `testing-dotnet`.
- Владелец: основной агент, без параллельных writers.
- Масштаб: medium; expanded SPEC по центральному `_template.md`: затрагиваются реактивные проекции девяти вкладок, их lifecycle и UI bindings.
- Behavior baseline: центральный `model-behavior-baseline`; поверхность Codex desktop. Фактические model/effort metadata не предоставлены; настройки агента не меняются. Model eval не применим: изменяется Avalonia-приложение.
- Ветка: `feat/task-tab-emoji-filters`; чистый managed worktree `emoji-tab-filters/Unlimotion` создан от свежего `origin/main`, SHA `6cf58f89479a8d45348f5c0d1c96af22ed81a579`.
- Источник для адаптации: `feat/daily-feed`, SHA `0ec4d33c59b719f61860a8e54c893ccda754d255`, прежде всего разделение фильтров списков из `c167c222` и последующие исправления. Это reference, не готовый cherry-pick.
- Instruction stack: central AGENTS, routing-matrix, creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance/mode, testing-baseline, testing-dotnet, dotnet-desktop-client, ui-automation-testing, spec-linter/rubric, review-loops, github-delivery-policy и локальный AGENTS.override. Скилл `appautomation`, включая `visual-evidence`, применён к плану UI-проверок.
- Ограничения: только выделенная фича; старые worktrees и незакоммиченные изменения не трогать. Commit/push/PR/merge/release этим поручением не разрешены.

## 1. Overview / Цель

Исходное поручение: начать с чистого worktree актуального main и перенести часть функции 10, чтобы каждая вкладка была самостоятельной в плане emoji-фильтров.

Success means: пользователь выбирает разные включения и исключения emoji в разных вкладках задач, переключается между ними и получает соответствующие независимые результаты. Сброс фильтров в одной вкладке не меняет emoji-выбор остальных.

Итог: небольшой изолированный diff поверх main, регрессионные UI-тесты и просмотренные кадры реального UI. Не перенос всей функции документов задач или новой оболочки.

Stop rules: до точного «Спеку подтверждаю» изменяется только эта SPEC. В EXEC остановиться при необходимости изменения хранения, навигации или общего поиска за пределами scope; не расширять перенос молча. Не считать задачу завершённой при красных обязательных проверках.

## 2. Текущее состояние (AS-IS)

- `MainWindowViewModel.Connect` создаёт общие `_emojiFilters` и `_emojiExcludeFilters` из групп задач по emoji. Все основные списки используют общие predicates.
- `MainControl.axaml` привязывает существующие emoji-панели к одним `EmojiFilters`/`EmojiExcludeFilters`. `GraphViewModel` получает те же коллекции для дорожной карты.
- `AllEmojiFilter` и `AllEmojiExcludeFilter` также общие. Каждый из девяти методов сброса вызывает один `ResetEmojiFilters()`.
- «Последние открытые» не имеет видимых emoji-полей; проекция требует отдельной проверки, а не только замены binding.
- Статусные фильтры уже разделены по вкладкам; текстовый поиск остаётся общим. Emoji-выбор runtime-only: настройки и task JSON его не сохраняют.
- В актуальном main уже есть исправления UI-thread delivery и восстановления выбранной задачи из PR #319. Они должны сохраниться; повторный перенос целого `5db0b05a` не нужен.
- В source-ветке разделение emoji перемешано с SearchDefinition, standalone task documents и workspace history. `SyncEmojiFilterCopies` пересоздаёт коллекции; нельзя переносить его без проверки metadata updates, локального выбора и подписок.

## 3. Проблема

Несколько разных рабочих списков разделяют mutable emoji-состояние: настройка одного меняет результаты других и лишает вкладки собственного контекста.

## 4. Цели дизайна

Отделить общий каталог доступных emoji от пользовательского выбора каждой вкладки, сохранить существующую семантику поиска/деревьев/дорожной карты, не менять компоновку и формат данных.

## 5. Non-Goals

- Лента, Markdown, области, разбор, quick capture, task URI.
- Докинг, новые документы/панели, истории переходов и перенос карточки задачи.
- Изоляция текстового поиска, wanted/duration/date/status filters сверх существующего поведения.
- Новая семантика AND/OR, новый переключатель режима фильтрации.
- Сохранение emoji-выбора после перезапуска, миграции конфигурации/задач, изменение server/CLI contracts.
- Массовая замена старых layout-тестов, удаление IsGoal, обновление пакетов.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- Небольшой `TaskTabEmojiFilterState` (рабочее имя): собственные include/exclude коллекции и элементы «Все», локальный сброс, обновление каталога без разделяемых mutable объектов.
- `MainWindowViewModel`: state для каждой из девяти существующих вкладок, соответствующие predicates и disposal; legacy AllTasks свойства сохраняют совместимость с текущими root-selection tests.
- `MainControl`: отдельные bindings. В «Последние открытые» добавить такую же компактную пару emoji-полей, как у других списков.
- `GraphViewModel`: собственные roadmap коллекции; обе ветви обычной/OnlyUnlocked дорожной карты используют их, а не выбор вкладки «Разблокированные».
- UI-тесты: проверяют клики, результаты, переключение и сброс, а не только отсутствие ReferenceEquals у коллекций.

### 6.2 Детальный дизайн

Вкладки: «Все задачи», «Последние созданные», «Последние изменённые», «Разблокированные», «Выполняются», «Завершённые», «Архив», «Последние открытые», «Дорожная карта».

У каждой свой выбор включаемых/исключаемых emoji, свой пункт «Все» и состояние представления emoji-иерархии. Каталог доступных emoji и его metadata берутся из одного пространства задач; ShowTasks и mutable hierarchy-parent ссылки между вкладками не разделяются.

Каталог синхронизируется по стабильному emoji-key: сохраняется выбор остальных существующих emoji при обновлении task title/источника, новые emoji не выбраны, исчезнувшие удаляются без «невидимого активного фильтра». Обновления названия, источника и иерархии доставляются всем локальным представлениям, не только CollectionChanged. Не добавлять полную пересборку UI при каждом пользовательском toggle; retain identity существующих локальных элементов, когда это возможно.

Пустой выбор сохраняет прежний смысл «не ограничивать по emoji». Множественное включение и исключение, особая root-семантика «Все задачи»/roadmap, наследование emoji от родителей и поиск по названию остаются как в main. Изменяется владелец состояния, не правила совпадения.

Сброс действует на emoji только активной вкладки. Остальные составляющие команды сброса сохраняют прежний контракт, включая общий поиск. Переключение вкладок не пересоздаёт state. При смене пространства/Disconnect старые подписки освобождаются, новая база не наследует старые emoji-состояния; обычный refresh в том же пространстве не сбрасывает независимый выбор сохранившихся emoji.

Visual planning artifact — storyboard без редизайна:

```text
Все задачи:      [Включить: 🧭] [Исключить: 🧪] [Фильтры] [Сброс]
                         переход на другую существующую вкладку
Разблокированные:[Включить: 🏠] [Исключить: — ] [Фильтры] [Сброс]
                         возврат
Все задачи:      [Включить: 🧭] [Исключить: 🧪] — выбор прежний

Сброс в Разблокированных: оба поля пустые; Все задачи не изменились.
Последние открытые: такая же пара полей; на узкой ширине штатный перенос.
```

Визуально сохраняются текущие контролы, отступы, темы и automation IDs. Общую панель и расположение вкладок не менять.

### 6.3 User-Observable Scenarios

| Сценарий | Действие | Видимый результат | Evidence / AC |
| --- | --- | --- | --- |
| S1 Независимые списки | Выбрать 🧭 во Всех, 🏠 в Разблокированных, несколько раз переключить | У каждой свой summary, галочки и набор задач | UI, PNG; AC1–2 |
| S2 Исключение и «Все» | Исключить emoji, переключить «Все» в одной вкладке | Только текущие include/exclude поля и её результаты изменены | UI; AC3 |
| S3 Сброс | Подтвердить штатный сброс активной вкладки | Её emoji пустые; остальные настройки emoji сохранены | UI; AC4 |
| S4 Обновление базы | Изменить emoji/название, добавить/удалить задачу | Каталог обновлён; другой существующий выбор не потерян | UI и controlled watcher; AC5 |
| S5 Редкие вкладки | Фильтровать Последние открытые и roadmap, переключить OnlyUnlocked | Фильтры работают и не заимствуют выбор соседнего списка | UI; AC1, AC6 |
| S6 Lifecycle | Refresh, Disconnect/Connect, смена пространства | Внутри пространства выбор устойчив; нет состояния или событий старой базы в новой | integration/UI; AC7 |

### 6.4 State / Interaction Matrix

| Состояние | Trigger | Результат / edge case |
| --- | --- | --- |
| Нет выбора | Выбрать include/exclude | Пересчёт только соответствующей проекции |
| Вкладка неактивна/ленивая | Обновить каталог, затем открыть | Актуальный каталог, её прежний выбор; соседний toggle не активирует её фильтр |
| Несколько выбранных | «Все» / сброс | Штатная семантика toggle; локальный state |
| Пустой каталог/нет результатов | Открыть поле | Штатное empty/no-match состояние, без исключений и чужих галочек |
| Task title меняется во время toggle | Reactive notification | Snapshot/owner-thread delivery; нет enumeration race |
| Удалён последний источник emoji | Catalog refresh | Пункт и его выбор удалены; остальные сохраняются |
| Новое пространство | Disconnect/Connect | Старые subscriptions disposed; девять чистых состояний |

### 6.5 Decision Ledger

| Решение | Owner | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Объём части функции 10 | user, конкретизация agent | Только emoji-самостоятельность существующих вкладок | 0.95 | Подмена запроса переносом всей оболочки | Нет, явно вынесено в approval |
| Область списка | agent | Все девять вкладок, roadmap включён | 0.95 | Пропустить менее заметные списки | Нет |
| Включение/исключение/«Все» | agent | Разделить вместе | 1.0 | Частичная изоляция оставляет общий state | Нет |
| Persistence | agent | Не добавлять: runtime-only как main | 0.95 | Не обещать сохранение после restart | Нет |
| Общий поиск | agent | Сохранить текущий контракт | 0.95 | Независимые emoji не означают независимость всех фильтров | Нет |
| Git delivery | user | Сейчас только local worktree и подготовка | 1.0 | Публикация без разрешения | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Контракт | Source of truth | Изменение | Совместимость / проверка |
| --- | --- | --- | --- |
| Каталог emoji | Tasks cache, текущие EmojiFilter metadata | Отдельный mutable выбор вкладок | Live title/hierarchy regression tests |
| Конфигурация и task JSON | main | Нет изменений | Diff audit; legacy fixtures |
| Runtime lifecycle | Connect/Disconnect и connectionDisposableList | Disposal всех новых подписок | Lifecycle/space-switch tests |
| Dependencies | main: AppAutomation 1.9.0, Avalonia 12.0.3, TUnit 1.44.0 | Не обновлять | Restore/build обычным способом |

## 7. Бизнес-правила / Алгоритмы

Изменение emoji состояния A не изменяет состояние B, включая toggle «Все», exclude, hierarchy expansion и reset. Предикат проекции A читает state A независимо от активной вкладки. Каталог общий, пользовательский выбор — нет. Root/child semantics и status availability остаются прежними.

## 8. Точки интеграции и триггеры

Connect создаёт catalog subscriptions и локальные predicates; lazy activation использует подготовленное состояние своей вкладки. Task title/Emoji/GetAllEmoji updates обновляют metadata и results. Все Reset*TabFilters адресуют соответствующий state. Roadmap ordinary/OnlyUnlocked pipelines проверяются отдельно. Disconnect/смена пространства освобождает owner-bound subscriptions.

## 9. Изменения модели данных / состояния

Только runtime объекты. Нет новых настроек, DTO, JSON полей и записи миграции. Не импортировать TaskListDocumentViewModel, WorkspaceLocation или SearchDefinition scope из source-ветки.

## 10. Миграция / Rollout / Rollback

Первый запуск: в каждой вкладке пустой emoji-выбор, как при прежнем запуске main. Перезапуск также начинает без выбранных emoji. Rollback: revert изолированного change set; файлы задач/настроек не требуют восстановления. Исходная большая ветка и dirty tests сохранены вне этого worktree. Публикация — отдельное поручение.

## 11. Тестирование и критерии приёмки

Все проверки ниже — план, не выполненное evidence. В EXEC сначала подтвердить воспроизводимый RED независимости на main, затем добавить исправление.

| AC | Automated test / check | Visual / artifact |
| --- | --- | --- |
| AC1 Девять вкладок имеют собственные рабочие include/exclude | Новый TaskTabEmojiFilterIsolationUiTests: parameterized cases каждой вкладки против контрольной; результат по task IDs | Summary и открытый popup |
| AC2 Переключение сохраняет разные выборы | Реальные клики controls + tab switching, выбор двух разных emoji и результаты | PNG состояний S1 |
| AC3 «Все», исключение и раскрытие иерархии локальны | Включение/выключение «Все», exclude и hierarchy; empty/no-result/двойной toggle | Проверка галочек открытого popup |
| AC4 Сброс не меняет emoji других вкладок | Обновить MainControlResetFiltersUiTests под локальное поведение, сохранить прочие reset assertions | Подтверждение сброса и поля после |
| AC5 Catalog updates не теряют другой выбор и актуализируют metadata | Controlled add/rename/delete, смена emoji родителя, live title edit; существующие emoji regression tests | Кадр после metadata update |
| AC6 Roadmap оба режима и LastOpened действительно фильтруются | Сравнение результата с выбранными emoji; roadmap no-shared-filter check | LastOpened popup и roadmap |
| AC7 Нет утечки между пространствами/после reconnect | Refresh сохраняет surviving selection; Disconnect/Connect/switch очищает старую связь | Trace task source IDs |
| AC8 Сохранились обычная сборка и существующие сценарии | Полные core и AppAutomation Headless suites; affected desktop build | Build/test logs + TRX |
| AC9 Внешний вид прежний, UI-фича доказана | Headless Skia кадры с открытым фильтром, ширины 900 и 390; inspect PNG | Before/after screenshots; video при поддержке harness |

Обязательный набор: targeted новые проверки, MainControlFilterToolbarResponsiveUiTests, MainControlResetFiltersUiTests, EmojiProjectionUiAffinityTests, StormEmojiFilterExecutableSpecTests, StormFilterResetExecutableSpecTests; обычная desktop сборка; полные `Unlimotion.Test` и `Unlimotion.UiTests.Headless`. Основание полного набора: общий MainWindowViewModel управляет девятью проекциями и связанной selection/lifecycle логикой. Native FlaUI smoke по доступности интерактивного рабочего стола, с точным отчётом об ограничении, не как замена Headless.

Команды из корня worktree (перед EXEC сверить SDK/runner):

```powershell
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --treenode-filter "/*/*/TaskTabEmojiFilterIsolationUiTests/*" --maximum-parallel-tests 1 --report-trx
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --maximum-parallel-tests 1 --report-trx
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --maximum-parallel-tests 1 --report-trx
git diff --check
```

Сборки shared projects и UI suites выполняются последовательно. До long run сообщить progress/log path; длительность определить по baseline этой среды, не выдумывать ETA. Логи/PNG/TRX в ignored `chat-artifacts/task-tab-emoji-filters/`. Нельзя сокращать mandatory suite после failure или использовать диагностические build overrides как доказательство обычной сборки.

Video evidence: meaningful baseline — переключение вкладок с общими emoji; after — независимые фильтры. В EXEC проверить recorder актуального harness. Если безопасная запись технически не поддерживается, назвать точную причину и команду, оставить next-best trace + просмотренные PNG; это не отменяет behavioral assertions.

## 12. Риски и edge cases

- Shared mutable EmojiFilter или HierarchyParent: отдельные экземпляры и parent links в каждой вкладке.
- Пересоздание фильтров теряет выбор/фокус и вызывает races: key-based update, проверки metadata и live edits, UI-thread delivery из main не отменять.
- Predicate/root restoration читает старую глобальную коллекцию: audit всех emoji pipelines, особенно roadmap и lazy LastOpened.
- Подписки остаются после reconnect: ownership через connectionDisposableList и lifecycle regression.
- Изменение исходного списка во время «Все»: snapshot и controlled notification тесты.

### Expected User Review Objections

| Возможное замечание | Mitigation | Статус |
| --- | --- | --- |
| Снова перенесена половина ленты и докинга | Только emoji state/projections/bindings, никаких Notes/WorkspaceDocument зависимостей | mitigated |
| Галочки разные, но результаты всё ещё общие | Assertions по task IDs, roadmap обоих режимов и lazy activation | mitigated |
| Сброс/«Все» продолжает менять соседей | Отдельные AC3–4 и UI regression | mitigated |
| После редактирования названия пропал выбор | AC5: сохранить surviving selections, актуальные metadata | mitigated |
| Под самостоятельностью ожидался ещё поиск/история | Явно объявлен narrow scope; общий поиск и старая компоновка сохраняются | mitigated, вынесено на approval |

Rework prevention: сценарии, границы, визуальный storyboard, decisions, AC→evidence и роли заполнены. Ни assertion по состоянию VM, ни кадр закрытого popup не доказывают весь исходный сценарий.

## 13. План выполнения

1. Подтверждение этой SPEC; до него source code не переносится.
2. Regression RED на реальном main UI и baseline evidence.
3. Адаптация минимальной emoji-части source-ветки, отдельные state/predicates/bindings; без целого cherry-pick.
4. Targeted checks, ordinary build, полные mandatory suites, rendered UI inspection и post-EXEC review.
5. Отчёт с маленьким фактическим diff, результатами и ограничениями; Git delivery только отдельно.

## 14. Открытые вопросы

Блокирующих решений нет. Предлагаемый scope явно ограничен emoji, runtime-only; утверждение относится именно к нему.

## 15. Соответствие профилю

UI-поток не блокируется каталогом/синхронным I/O; metadata delivery сохраняет owner-thread правила main. UI tests и visual evidence обязательны; automation IDs сохраняются. AppAutomation 1.9.0 уже в main; используем принятые fixture/harness patterns, пакеты не обновляем.

## 16. Таблица изменений файлов

| Файл | Планируемое изменение | Причина |
| --- | --- | --- |
| src/Unlimotion.ViewModel/TaskTabEmojiFilterState.cs (новый) | Runtime-local include/exclude state и catalog synchronization | Изоляция без workspace coupling |
| src/Unlimotion.ViewModel/MainWindowViewModel.cs | Девять владельцев, predicates, resets/lifecycle | Перенос узкой части функции 10 |
| src/Unlimotion.ViewModel/GraphViewModel.cs | Roadmap bindings/state | Не заимствовать AllTasks/Unlocked emoji |
| src/Unlimotion/Views/MainControl.axaml | Per-tab bindings, LastOpened поля | Видимый доступ к отдельному выбору |
| src/Unlimotion/Views/GraphControl.axaml | Только необходимая wiring корректировка | Сохранение roadmap UI |
| src/Unlimotion.Test/TaskTabEmojiFilterIsolationUiTests.cs (новый) | Сценарии S1–S6 | Проверяемая фича |
| src/Unlimotion.Test/MainControlResetFiltersUiTests.cs | Локальные ожидания emoji; остальные проверки сохранены | Не оставлять устаревший reset контракт |
| tests/Unlimotion.UiTests.Authoring и Headless | Узкий пользовательский сценарий и screenshot adapter по необходимости | AppAutomation evidence без общего редизайна |

Имена нового helper/test окончательно выбираются в EXEC; это не разрешение менять scope.

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Emoji selection | Общий у вкладок | Собственный include/exclude state |
| Каталог | Общий источник | Общий источник, локальные mutable представления |
| Сброс | Сбрасывает общие emoji | Emoji только активной вкладки |
| Text search / layout / storage | main | Без изменения |

## 18. Альтернативы и компромиссы

- Целый cherry-pick функции 10: отклонён — вносит документы, history и новую навигацию.
- Snapshot при переключении одной общей коллекции: отклонён — неактивные reactive projections всё равно читают чужое состояние; повышенный риск transient reset.
- Новые persisted settings: отклонены — не запрошены и усложняют независимую поставку.
- Выбрана отдельная runtime модель emoji без полной TaskListFilterScope/SearchDefinition из большой ветки.

## 19. Результат quality gate и review

### SPEC Linter Result

| Пункты | Статус | Evidence |
| --- | --- | --- |
| 1–5 | PASS | Исходное поручение, AS-IS main, narrow outcome и Non-Goals (§1–5) |
| 6–10 | PASS | Owners/predicates/catalog/lifecycle, UI-thread delivery и error/empty states (§6–8,12) |
| 11–13 | PASS | Runtime-only; нет storage migration; rollback изолированного diff (§9–10) |
| 14–16 | PASS | AC1–9, test matrix, команды и mandatory set, visual/video boundaries (§11) |
| 17–19 | PASS | План, decisions, medium expanded applicability; questions закрыты (§0,6,13–14) |
| 20 | PASS | Desktop/UI automation profiles и локальный UI gate (§15) |

Итог: ГОТОВО к запросу approval, не доказательство готовой реализации.

### SPEC Rubric Result

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель/границы | 5 | Emoji-самостоятельность без ленты/workspace redesign |
| AS-IS | 5 | Свежий main, общие bindings и pipelines inspected |
| Дизайн | 5 | Owners, каталог vs selection, nine-tab wiring и lifecycle |
| Безопасность/rollback | 5 | Нет persisted изменений; старые dirty worktrees сохранены |
| Проверяемость | 5 | Observable UI и results, negative/reset/live-update cases |
| Автономность | 5 | Неопределённости явно ограничены; approval gate остаётся |

30/30 по содержанию SPEC; не test PASS.

### Role-Based Review Result

| Роль | Применимость | Review / verdict |
| --- | --- | --- |
| Domain workflow | Да | Разные списки сохраняют свой emoji-контекст; общий поиск не переименован в независимый. PASS |
| UX / designer | Да | Знакомые dropdowns, девять вкладок, storyboard и узкая ширина. PASS |
| Tester | Да | Results assertions, reset/all/metadata/lifecycle, полные regression suites. PASS |
| Developer / architect | Да | Не копируется workspace coupling, state не делится, metadata не stale. PASS |
| Delivery / operations | Да | Чистый base SHA, старый dirty state не тронут; никаких push/merge. PASS |

### Post-SPEC Review

- Scope reviewed: эта SPEC; central stack и template; main `MainWindowViewModel`, `GraphViewModel`, `MainControl/GraphControl.axaml`, EmojiFilter class; source-ветка `TaskListFilterScope`, TaskListDocumentViewModel и SyncEmojiFilterCopies; действующие reset/emoji/affinity tests и Headless hooks.
- Scope/Evidence pass: новый worktree до создания SPEC чистый; HEAD равен fetched origin/main. Source-ветка прочитана как reference, не перенесена.
- Contract pass: all nine tabs, include/exclude/all/reset явно охвачены; поиск/persistence/документы исключены; ordinary build и UI validation предусмотрены.
- Adversarial pass: проверены counterexamples lazy tabs, roadmap OnlyUnlocked, metadata без collection change, reset соседнего списка, parent links и reconnect. Внесены AC5–7 и запрет snapshot-only решения.
- Role-Based pass: таблица выше; review основной агент, не независимый. Отдельный adversarial fallback без субагентов; технически read-only reviewer не использовался. Риск: self-review не заменяет последующий внешний review.
- Findings: MEDIUM scope — целый перенос source Scope захватывает SearchDefinition/workspace; fixed в §5,9,18. MEDIUM correctness — CollectionChanged-only copies пропускают metadata; fixed в §6, AC5. MEDIUM coverage — LastOpened/roadmap могли быть пропущены; fixed в nine-tab AC1,6.
- Fix and re-review: повторно сверены source helper, main bindings, predicates и resets; уточнён LastOpened control plan и runtime-only persistence.
- Depth checklist: scope/unrelated changes — отделены; AC/evidence — §11; scenarios/decisions/objections — заполнены; unsupported claims — никаких test-green/native claims; regression/edge cases — §12; docs/changelog — только рабочая SPEC; hidden contracts — поиск и JSON не меняются.
- Manual-review challenge: не ограничиться двумя вкладками и VM dictionaries; проверить реальное меню/галочки/results и все lazy projections, roadmap оба режима.
- Stop decision: PASS для SPEC. Следующий gate — точное подтверждение пользователя. EXEC и его AC ещё не выполнены.

### Post-EXEC Review

**PASS, реализация и обязательные проверки завершены.** Source реализован: отдельные include/exclude owners восьми вкладок плюс сохранённый owner AllTasks, актуальная metadata, local resets, LastOpened и оба roadmap режима. Исходный RED 2/2 воспроизведён на base main. Окончательные targeted на process-isolated rendered adapter: Isolation 12/12 и отдельно подтверждённый copy scope 16/16, объединённый запуск 28/28. Existing CLI Headless после test-harness correction 5/5. Ordinary Desktop build 0 errors / 11 warnings, 4.21s. Полный Headless 58/58, 0 failed/skipped, 3m35.6s; rendered emoji сценарий включён. Native navigation smoke 1/1, 37.8s. Окончательный полный core **1427/1427**, 0 failed/skipped/aborted, 42m52.6s, TRX counters проверены напрямую.

Первый полный core сохраняется как честный failed evidence: 1417/1427 passed, 10 failed, 0 skipped, 47m09s. Existing mandatory regressions прошли: responsive 22/22, reset 10/10, affinity 14/14, StormEmoji 1/1, StormReset 1/1. Среди новых checks: 8 pointer failures до готовности hit-test сцены, 1 проверка закрытого popup и 1 дополнительный date/search case с ошибочной local-offset fixture после 21:00. Исправлены только тестовые ожидания input/видимости и UTC fixtures, assertions поведения не ослаблены. Existing CLI Headless deadlock и потеря background callbacks в PerTest dispatcher исправлены минимальным тестовым адаптером и существующим persistent-per-case механизмом; production CLI/storage/date projection не менялись. Source код оставлен стабильным на период окончательного full run.

Диагностика длинного core ожидания показала активный FileTaskStorage reader, а не deadlock: benchmark 3000 tasks × 600 reads прошёл за 7m57s. Для отдельного настоящего CLI Headless deadlock сохранены Mini dump и clrstack; остановлен только свой проверенный тестовый процесс. Screenshots открытого меню и feedback RU 390/900 и EN 900 просмотрены. Video fallback подтверждён rendered Headless run: runtime capabilities `CaptureScreenshot`; установленный AppAutomation Recorder — interactive authoring recorder, не video API этого harness. Behavioral assertions + PNG + logs/TRX сохранены, это не утверждение о native visual parity или ручном полном прохождении всех сценариев.

Повторный full core до процессной изоляции также не скрыт: промежуточный лог имел 1340 passed и 7 failed новых pointer cases. Он остановлен как superseded только в своём проверенном процессе после изменения test adapter, не является завершённым PASS. Дальнейшее увеличение задержек не использовано: как existing importance-rendered tests, новые rendered cases запускаются в свежем процессе по stable UID. Каждый child обязан реально выполнить ровно один исходный UI case и предоставить passed TRX; отсутствие тестов и любой child failure проваливают parent. Input root определяется после рендера. Все реальные keyboard/mouse действия и результаты сохранены. После этого targeted 28/28 и полный core 1427/1427 прошли.

Role-based self-review и adversarial re-review scope: все девять UI bindings/predicates; local master/reset; metadata revisions/hierarchy; Source snapshot/Undo и lifecycle; localization; изменённый CLI test dispatcher hook и rendered process adapter; целостность Git diff. Domain/UX/tester/architect/delivery: PASS в утверждённом scope. Source owners не разделяют mutable EmojiFilter, non-emoji filters/JSON/settings не перенесены. State/panels зарегистрированы в Dispose владельца; detached source больше не влияет на новое пространство. Negative findings выше исправлены и повторно проверены targeted/full suites. Это review основного исполнителя, не независимое ревью; работа без делегирования.

| AC | Финальное evidence / verdict |
| --- | --- |
| AC1–3 | Все девять вкладок, include/exclude/All и переключение; Isolation 12/12, Copy 16/16. PASS |
| AC4 | MainControlResetFiltersUiTests 10/10; соседние emoji сохраняются, прежние non-emoji assertions оставлены. PASS |
| AC5 | Catalog/hierarchy/live-title case, EmojiProjectionUiAffinityTests 14/14 и StormEmoji 1/1. PASS |
| AC6 | LastOpened и roadmap actual task IDs/оба режима; responsive 22/22. PASS |
| AC7 | Reconnect/detach/reset, удаление и surviving metadata/selection; новые UI cases и Dispose wiring. PASS |
| AC8 | Ordinary build 0 errors; full core 1427/1427, full Headless 58/58, 0 failed/skipped. StormReset 1/1 сохранён. PASS |
| AC9 | Просмотренные rendered PNG источников/feedback/Undo RU 390/900 и EN 900; реальные assertions. Подтверждённый PNG/trace fallback вместо video. PASS в заявленной visual evidence границе |

Финальный core report: `chat-artifacts/task-tab-emoji-filters/full-core-rendered-isolated/Kibnet_DESKTOP-AUDO1TJ_2026-10-09_20_09_53.1106994.trx`; Headless: `full-headless-final/Kibnet_DESKTOP-AUDO1TJ_2026-10-09_18_43_44.6777784.trx`. Скриншоты AppAutomation: `appautomation-final/a6199ce57209406d9fd6eb670be16601/`. Артефакты ignored, в Git не включены. Остающийся out-of-scope finding: существующий date-only predicate может сдвинуть non-UTC stored timestamp дважды; production dates не изменены, это отдельная кандидатная правка. Stop: outcome complete в local implementation scope; commit/push/PR/merge/CI/release не выполнялись.

## Approval

Получено точное подтверждение «Спеку подтверждаю», EXEC разрешён. На этом gate commit/push/PR/merge не поручены. Отдельное поручение 2026-10-12 разрешило commit/rebase/push/PR; merge/release по-прежнему не поручены.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| Подготовка | Не очищать dirty detached worktree; создать новый от main | main 6cf58f89, git status нового worktree чистый | Исследовать узкий перенос | Явно поручен чистый worktree | worktree emoji-tab-filters, branch feat/task-tab-emoji-filters |
| SPEC | Только emoji самостоятельность девяти вкладок; не переносить всё workspace | Main/source pipelines и UI bindings inspected, §19 review | Получить exact approval | Ещё не получено | Эта SPEC |
| EXEC | Scope утверждён без расширения; работа одним агентом | Реализация и mandatory проверки ещё впереди | Regression RED на main UI | «Спеку подтверждаю», 2026-10-07 | Эта SPEC, source/tests после RED |
| EXEC, 2026-10-09, продолжение | Ворктри был отребейжен внешним действием: reflog rebase finish на e987023d (main с PR #320); собственный diff сохранён | Начальная ordinary desktop build: 0 errors; исходный RED 2/2 failed, первый узкий GREEN 2/2 passed. Расширенные/full проверки ещё не завершены; старый прерванный процесс остановлен только в своём process tree | Пересобрать и выполнить весь утверждённый набор на фактическом HEAD | «Продолжай» | build.log, red/green TRX, test-build-continued.log |
| EXEC, расширение и финальная проверка | Дополнительный copy/Undo scope отдельно утверждён, новая SPEC не ослабляет gates изоляции | Copy 15/15 и rendered AppAutomation 1/1; скриншоты открытых panels RU на 900/390 просмотрены. Full core и Headless идут последовательно после обычной сборки 0 errors; native desktop preflight available | Закрыть AC1–9 фактическими full/native результатами и full review | «Спеку подтверждаю» для copy SPEC | 2026-10-09-copy-tab-emoji-filters.md, full-core.log, full-headless.log, build-final.log, appautomation/ |
| EXEC, повторный full / rendered adapter | Build 0 errors / 11 warnings, full Headless 58/58. Core rerun: 1340 passed и 7 failed новых pointer cases на момент остановки; не завершённый PASS. Остановлен только проверенный собственный exe 647968 после изменения test helper | Новые 28 cases изолируются свежими child processes по stable UID с обязательным ровно 1 passed в child TRX, как существующий importance-rendered pattern; input root определяется после рендера. UI actions/results не заменены VM commands; product source стабилен | Targeted 28, затем обязательный полный core | Дополнительных решений нет | full-core-final.log, TaskTabEmojiFilterRenderedProcess.cs |
| EXEC, завершение | Проверки окончательного adapter зелёные, production source не менялся во время последнего полного прогона | Targeted 28/28; full core 1427/1427, 42m52.6s; full Headless 58/58; Desktop 0 errors / 11 warnings; native navigation 1/1. TRX counters/per-class regressions проверены; diff check PASS | Handoff локального результата, без Git delivery | Новых решений не требуется | rendered-isolated-targeted-v2/, full-core-rendered-isolated/, full-headless-final/, appautomation-final/ |
| Delivery, 2026-10-12 | Коммиты сохранены и перебазированы на актуальный fetched main fa1bfc03; при конфликтах оставлены новые main Git/settings и async dispatcher. Feature range-diff сохранён | На d4298e31 Desktop 0 errors / 4 warnings; full core 1501/1501; full Headless 65/65, без failed/skipped. Copy 16/16, Isolation 12/12 и existing regressions зелёные; свежие wide/narrow PNG просмотрены. Опубликован и прикреплён draft PR #323 | Проверить CI перед ready, без merge/release | «Отребейзь на мейн и оформи ветку» + «Также push и PR» | delivery-2026-10-12/, https://github.com/Kibnet/Unlimotion/pull/323 |
| Delivery, CI fixture fix | Первый CI core green / Headless 64/65: existing Git fixture пытается Stage исчезающий .unlimotion.lock. Исправлен только fixture exclude, добавлен deterministic RED→GREEN regression; product source tree не меняется | RED-v2 1/1 по IndexEntry вместо null; Git 5/5; повторный полный Headless 66/66, без failed/skipped. Cleanup первой RED попытки маскировал assertion, это отдельно сохранено в логах | Отдельные test/docs commits и повторный CI; итоговый CI/ready status — в PR. При red остаётся draft | Утверждённая validation-scope correction, без нового product behavior | git-lock-red-v2/, git-lock-green/, headless-fixed/, CI run 38219319481 |
