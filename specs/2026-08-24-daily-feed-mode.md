# Режим «Лента» для ежедневных Markdown-заметок

## 0. Метаданные

- Тип (профиль): `dotnet-desktop-client`
- Overlay profile: `product-system-design`
- Контексты: `testing-dotnet`; локальный `AGENTS.override.md` требует UI-тесты для нового UI-поведения
- Владелец: Unlimotion client / подсистема локальных заметок и Ленты
- Масштаб: `large`
- Целевое семейство / behavior baseline: `GPT-5.6`; влияет только на QUEST-процесс, не на runtime продукта
- Поверхность: Work / Codex
- Effective runtime: фактический model ID текущей Codex-сессии не предоставлен; отдельный model/runtime контракт для функции не требуется
- Eval baseline / evidence: согласованные пользовательские сценарии, интерактивный макет и проверенный PNG; before/after model eval не применим, потому что изменение не связано с model/prompt behavior
- Целевой релиз / ветка: после подтверждения SPEC; текущая ветка `main`, upstream `origin/main`
- Ограничения:
  - До фразы пользователя `Спеку подтверждаю` изменяется только эта рабочая SPEC.
  - Markdown-файлы существующего Obsidian vault остаются основным источником данных заметок; файлы не копируются в отдельный проприетарный формат.
  - MVP поддерживает прямой внешний vault на desktop. Browser, iOS и Android продолжают собираться, но внешний vault на них не заявляется как поддержанный до отдельного provider-контракта и platform validation.
  - Новый режим не заменяет существующие task-представления, карточку, статусную модель, связи или storage API задач.
  - Для UI-потока обязательны Avalonia.Headless/AppAutomation тесты и пропорциональная FlaUI/visual verification.
- Связанные ссылки:
  - `README.RU.md`
  - `src/Unlimotion/Views/MainScreen.axaml`
  - `src/Unlimotion/Views/MainControl.axaml`
  - `src/Unlimotion.ViewModel/MainWindowViewModel.cs`
  - `src/Unlimotion.Domain/TaskItem.cs`
  - `src/Unlimotion/TaskStatusPicker.cs`
  - `src/Unlimotion.ViewModel/TaskRelationEditorViewModel.cs`
  - `specs/2026-03-30-task-card-relation-blocks.md`
  - `specs/2026-06-29-storm-sc0001-multiple-parents-bdd.md`
  - `specs/2026-07-14-storm-sc0007-task-card-relations-bdd.md`
  - local-only visual concept: `../output/playwright/Unlimotion-Лента-концепт-v2.png`
  - local-only interactive concept: `C:\tmp\unlimotion-feed-concept.html`

## 1. Overview / Цель

Добавить в Unlimotion верхнеуровневый режим `Лента`, который позволяет быстро вести ежедневные свободные Markdown-записи, просматривать их хронологически и без потери исходного контекста превращать выбранные фрагменты в задачи, цели или постоянные заметки.

`Лента` дополняет существующий режим `Задачи`, а не становится ещё одной проекцией дерева задач. Пользователь должен иметь возможность записать мысль без предварительной классификации, а структуру добавить позже во время накопительного разбора.

Outcome contract:

- Success means:
  - пользователь работает прямо с существующим Obsidian vault и файлами `Ежедневные/YYYY-MM-DD.md`;
  - быстрая запись, Live Preview, хронологическая навигация, поиск и разбор доступны в одном режиме без обязательных модальных окон;
  - преобразование фрагмента переиспользует существующие задачу, статус и блок родительских связей, а не создаёт параллельные сущности;
  - внешние изменения и конфликты никогда не приводят к молчаливой перезаписи пользовательского текста;
  - основные сценарии подтверждены автоматическими UI-тестами и визуальным артефактом.
- Итоговый артефакт / output: работающий desktop MVP режима `Лента`, additive task contracts для `IsGoal`/`AreaIds`, локальный Markdown vault pipeline, автоматические тесты, обновлённая документация и проверенный визуальный evidence.
- Stop rules:
  - остановить EXEC и запросить решение, если реализация требует отказаться от прямой работы с исходным vault или от блочного Live Preview;
  - не продолжать запись при несовпадении ожидаемой ревизии файла; перейти в conflict flow;
  - не выбирать Markdown dependency, пока не проверены лицензия, `net10.0`, Avalonia desktop и round-trip неизвестного Markdown;
  - не заявлять готовность без passing targeted UI tests, affected build и полного обязательного serial test gate;
  - не заявлять поддержку внешнего vault на mobile/browser без отдельного platform evidence.

## 2. Текущее состояние (AS-IS)

- `MainWindow.axaml` содержит `MainScreen`, а `MainScreen.axaml` объединяет `DialogHost`, `MainControl`, toast и loading overlay.
- `MainControl.axaml` является целым режимом задач: breadcrumbs, создание задач, десять task-представлений, дерево/Roadmap и встроенная правая карточка задачи.
- В проекте отсутствуют Markdown AST/parser, Live Preview, YAML frontmatter contract, полнотекстовый индекс заметок и модель ежедневной Ленты.
- `TaskOutlineClipboardService` умеет ограниченный импорт/экспорт checklist outline, но не подходит для сохранения произвольного Markdown и блочных границ.
- Текущий поиск использует `SearchDefinition`/`FuzzyMatcher`, но фильтрует только `TaskItemViewModel` по title, description, emoji и ID.
- `FileDbWatcher` специализирован для JSON-задач: один каталог, без подпапок и `Renamed`, task-oriented events и ignore-cache по имени файла.
- `TaskItem` уже содержит статус, историю статуса, критерии завершения, даты и четыре набора связей. `ParentTasks` — `List<string>`; несколько родителей являются действующим контрактом продукта.
- Готовый `TaskStatusPicker` показывает допустимые переходы и вызывает существующую доменную статусную логику.
- UI родителей встроен в `MainControl`; `TaskRelationEditorViewModel` поддерживает поиск, fuzzy matching, несколько родителей, cycle validation и немедленные storage-мутации. Блок пока не является самостоятельным control и привязан к глобальному `CurrentTaskItem`.
- Task storage поддерживает local JSON и remote/server flows. `IsGoal` и `AreaIds` в текущих domain/interface/server контрактах отсутствуют.
- Desktop уже умеет выбирать каталог через Avalonia `StorageProvider`. У Browser нет persistent external-vault adapter; Android/iOS не имеют подтверждённого общего контракта прямого доступа и recursive watching существующего Obsidian vault.
- В репозитории есть прямые Avalonia.Headless тесты (`src/Unlimotion.Test`), общие AppAutomation сценарии и отдельные Headless/FlaUI runner-проекты.
- Визуальный концепт уже проверен пользователем. В нём статус задачи расположен отдельным контролом слева от названия.

## 3. Проблема

Свободная ежедневная запись и структурированное управление задачами живут в разных инструментах. Пользователь вынужден вручную перечитывать ежедневные файлы, переносить незавершённое в Unlimotion, выносить полезный текст в постоянные заметки и восстанавливать контекст по хронологии. Этот ручной handoff создаёт трение и риск потери связи между исходной мыслью, задачей и итоговой заметкой.

## 4. Цели дизайна

- Сделать быстрый захват самым коротким сценарием: классификация не обязательна во время записи.
- Сохранить Markdown и существующий vault как читаемый, переносимый source of truth.
- Разделить свободный текст, task domain и индекс/служебное состояние без дублирования сущностей.
- Переиспользовать существующие `TaskStatusPicker`, задачу и relation behavior.
- Оставить только два постоянных рабочих режима верхнего уровня: `Лента` и `Задачи`; файлы, области, цели и заметки доступны как фильтры, роли и временные панели.
- Сохранить существующие task-вкладки и карточку внутри режима `Задачи`.
- Обеспечить безопасный round-trip Markdown: неизвестная поддерживаемая приложением разметка не должна исчезать при чтении или несвязанном редактировании.
- Сделать внешний edit, recovery и conflict flow частью нормального поведения, а не исключением.
- Изолировать filesystem/indexing от UI и не блокировать UI thread.
- Обеспечить deterministic unit/integration/UI validation.

## 5. Non-Goals (чего НЕ делаем)

- Не создаём облачное хранилище заметок Unlimotion, совместное редактирование или обязательную синхронизацию через сервер.
- Не импортируем и не копируем весь Obsidian vault в task storage.
- Не создаём отдельные постоянные режимы `Цели`, `Области`, `Заметки` или `Разбор`.
- Не заменяем существующие task-вкладки, Roadmap, карточку задачи, status transition policy или relation semantics.
- Не ограничиваем задачу одним родителем и не создаём новый упрощённый parent picker.
- Не превращаем существующее дерево задач в дерево областей автоматически.
- Не назначаем области существующим задачам автоматически.
- Не добавляем AI-классификацию, автосуммаризацию или генерацию задач.
- Не реализуем attachments/upload для ежедневных заметок в MVP.
- Не обещаем полную совместимость со всеми Obsidian plugins, Dataview, Canvas или executable HTML/JavaScript. Неизвестный синтаксис сохраняется как raw fallback.
- Не добавляем посимвольное изменение границ во время разбора; единицей выбора остаётся Markdown-блок.
- Не заявляем внешний vault на Browser/iOS/Android в этом MVP.
- Не создаём автоматически новую identity для скопированного vault: clone с тем же `VaultId` считается той же логической базой; независимый fork требует отдельной будущей функции.
- Не добавляем глобальный system-wide quick-capture hotkey; локальная кнопка и клавиатурный flow внутри приложения входят в MVP.
- Не выполняем автоматический repair всех внешне сломанных wiki-links без явного безопасного действия пользователя.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент / файл | Ответственность |
| --- | --- |
| `MainScreen.axaml` / shell state | Верхний переключатель `Лента` / `Задачи`; сохранение контекста каждого режима. |
| `FeedControl.axaml(.cs)` | Композиция toolbar, review banner, виртуализированной хронологии, quick capture, поиска и временной файловой панели. |
| `FeedViewModel` | UI-state Ленты, выбранная область, день, review session, search mode, переходы к task/note. Не расширяет task-проекционный pipeline `MainWindowViewModel`. |
| `Unlimotion.Notes` (новый project) | Чистые контракты vault, Markdown document/block model, areas, review state, search index, mutation journal, revisions и conflicts. |
| `INoteVault` / filesystem implementation | Безопасное чтение, optimistic revision check, atomic write, recursive Markdown watch, rename и path confinement. |
| `ISidecarWatcher` / sidecar sync | Отдельно отслеживает переносимые `.unlimotion/vault.json`, `areas.json` и `review/`, не индексируя их как заметки; merge/conflict rules не зависят от Markdown watcher. |
| `IAppLocalFeedRecoveryStore` | Хранит drafts, pending journals, bounded revisions и rebuildable index вне vault, в namespace стабильной vault identity. |
| `IMarkdownDocumentParser` | Парсинг YAML и блоков с сохранением raw slices, line endings и неизвестного синтаксиса. |
| `FeedSearchIndex` | Инкрементальный rebuildable индекс daily/permanent notes; task adapter добавляет task results без копирования задач. |
| `AreaCatalog` | Иерархия областей, архивирование, стабильные ID и folder defaults. |
| `ReviewStateStore` | Решения `baseline-kept/kept/deferred/converted/moved`, block fingerprints и повторное появление изменённых блоков. |
| `TaskItem` + interface/server molds | Additive `IsGoal` и `AreaIds`; существующие status/relations остаются source of truth. |
| `TaskStorageCapabilities` / server hub capability | Версия optional task-classification protocol; защита mixed-version writes от silent field loss. |
| `TaskRelationsControl` (извлекаемый reusable control) | Тот же parent/relation UI и behavior в карточке и контексте Ленты; explicit target task, instance-scoped focus/AutomationId. |
| `TaskStatusPicker` | Без изменений семантики; в живой ссылке всегда расположен слева от task title. |
| `FeedDocumentConflictViewModel/Control` | Dirty-buffer conflict: версия редактора, версия диска, сохранить обе. Использует визуальный язык существующего conflict UI, но отдельную Markdown-семантику. |
| `src/Unlimotion.Test` | Parser/storage/index/review/task-reference и прямые Avalonia.Headless contracts. |
| AppAutomation Headless/FlaUI | Сквозные пользовательские сценарии и visual/UIA evidence. |

### 6.2 Детальный дизайн

#### 6.2.1 Верхняя навигация и layout

- Переключатель `Лента | Задачи` добавляется в `MainScreen`, а не в task `MainTabs`.
- Существующий `MainControl` целиком остаётся содержимым режима `Задачи`; его десять вкладок и responsive overflow не меняют семантику.
- `FeedControl` является соседним содержимым shell и не вкладывается в task `TabControl`.
- Выбранный верхний режим и внутренний контекст обоих режимов сохраняются при переключении.
- Нажатие названия живой task-ссылки переключает shell в `Задачи`, выбирает task по стабильному ID и открывает существующую правую карточку. Временную копию карточки поверх Ленты MVP не создаёт.
- Смена статуса выполняется на месте через `TaskStatusPicker` слева от названия и не переключает режим.

Durable wireframe внутри SPEC:

```text
┌ Unlimotion   [Лента] [Задачи]      [Область ▾] [Файлы] [Поиск] [Быстрая запись] ┐
│ Нужно разобрать: 3 за 2 пропущенных дня                              [Разобрать] │
│ ┌ Сегодня, 24 августа ─────────────────────────────────────────────────────────┐ │
│ │ Работа / Unlimotion                                                         │ │
│ │ Текст, Markdown-списки и чекбоксы в Live Preview                            │ │
│ │ [▶] Подготовить режим Ленты   ← status control всегда слева                 │ │
│ │ Здоровье                                                                    │ │
│ │ ...                                                                         │ │
│ │ [ продолжить сегодняшнюю заметку... ]                                       │ │
│ └─────────────────────────────────────────────────────────────────────────────┘ │
│ ┌ 23 августа ───────────────────────────────────────────────────── [Свернуть] ┐ │
│ │ ...                                                                         │ │
│ └─────────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────────┘
```

- Дополнительный проверенный render: `../output/playwright/Unlimotion-Лента-концепт-v2.png`. Он local-only/untracked и служит supplemental evidence; durable acceptance задаётся wireframe и критериями этой SPEC.

#### 6.2.2 Vault и файловая структура

- Пользователь выбирает корень существующего Obsidian vault в настройках.
- Daily files находятся строго в `Ежедневные/YYYY-MM-DD.md` относительно корня. Папка создаётся только после явного включения Ленты или первой записи.
- Постоянные заметки остаются в произвольных тематических подпапках vault.
- Служебный каталог `.unlimotion/` содержит только переносимое между устройствами состояние:
  - `vault.json` — schema v1 и стабильный `VaultId` этой логической базы;
  - `areas.json` — каталог областей;
  - `review/` — события/решения разбора по daily file и append-only bootstrap operations.
  - `deleted/<operation-id>/<original-relative-path>` — durable safety quarantine для файла, удалённого только после проверки expected revision. Каталог сохраняет исходный относительный путь и не очищается автоматически: внешний POSIX writer может дописать в уже перемещённый inode после последней проверки. Очистка требует отдельного явного user-facing workflow; до него пользователь при необходимости может восстановить файл из vault filesystem.
- Незавершённые transactions, bounded revisions, drafts и rebuildable search index хранятся в app-local data, namespace которого включает `VaultId`. Они не синхронизируются как пользовательские заметки.
- `.unlimotion/` и app-local artifacts не показываются в Ленте, Files drawer или результатах поиска. `deleted/` также исключён из Markdown и sidecar watch scopes, чтобы safety quarantine не создавала новые candidates или review events.
- Dedicated sidecar watcher/rescan обрабатывает внешние изменения `vault.json`, `areas.json` и `review/`: review events merge по semantic key/decision precedence, clean area catalog reloads, а concurrent identity/area conflict получает explicit conflict. Общий Markdown watcher не индексирует эти файлы как заметки.
- Перемещение root вместе с `.unlimotion/vault.json` сохраняет логическую identity и перепривязывает app-local namespace к новому path. Копия с тем же `VaultId` считается той же логической базой; одновременно подключить две локальные roots с одним ID нельзя. Автоматическое создание независимой identity для clone не входит в MVP.
- `vault.json` создаётся atomically с create-if-absent. Два несовместимых `VaultId` после внешней синхронизации не объединяются автоматически: sidecar writes блокируются, Markdown остаётся read-only доступным, обе identity/review branches копируются в immutable app-local conflict bundle и пользователь выбирает корректную identity.
- Identity conflict actions: `Использовать identity текущего root`, `Переподключить другую root`, `Остаться только для чтения`. До confirmation ни одна branch не удаляется/перезаписывается. После выбора expected revisions проверяются повторно; losing branch остаётся в conflict bundle, её отличающиеся locators становятся safe-pending с возможностью просмотра, а не молча принимаются/теряются. App-local recovery namespace перепривязывается только после разрешения pending journals; старый namespace сохраняется read-only до отдельной очистки.
- Все paths нормализуются относительно выбранного root. Выход через `..`, symlink/junction escape или абсолютный destination блокируется до записи.

#### 6.2.3 Daily Markdown contract

- H2 (`##`) является границей основной области дня. H1 разрешён как заголовок документа; H3+ остаются частью содержимого текущей области.
- Текст до первого распознанного H2 отображается как виртуальная группа `Без области`; отдельный заголовок в файл не добавляется автоматически.
- Заголовок области, созданный Unlimotion, имеет вид:

```markdown
## Unlimotion <!-- unlimotion-area:01J... -->
```

- HTML-comment скрыт в Live Preview и обеспечивает стабильное разрешение при переименовании области. Внешний H2 без marker сопоставляется по уникальному имени; неоднозначное совпадение остаётся `Без области` и требует выбора пользователя.
- Фрагмент daily file имеет одну основную область, определяемую положением под H2. Задача, цель или постоянная заметка после преобразования могут иметь несколько `AreaIds`.
- Parser сохраняет исходную кодировку UTF-8/BOM state, line endings, YAML, whitespace и raw неизвестных блоков. Несвязанное изменение одного блока не форматирует весь документ.

#### 6.2.4 Block Live Preview и редактирование

- Документ отображается как последовательность Markdown-блоков. Неактивные блоки рендерятся, активный блок редактируется как raw Markdown на месте.
- Минимально обязательный render/edit contract: headings, paragraphs, emphasis, links/wiki-links, ordered/unordered lists, checkboxes, blockquotes, fenced code и horizontal rule.
- Unsupported/Obsidian-plugin block показывается как безопасный raw fallback и сохраняется byte-for-byte, пока пользователь не редактирует сам блок.
- Raw HTML/JavaScript не исполняется. Внешние URI проходят scheme validation.
- Autosave использует debounce и `expectedRevisionHash`; запись выполняется temp-file + flush + atomic replace в том же каталоге.
- Активный dirty block периодически сохраняется как app-local recovery draft. После аварийного старта пользователь может восстановить или удалить draft.
- Перед преобразованием/перемещением создаётся safety revision; retention ограничен последними 20 версиями каждого изменяемого файла.

#### 6.2.5 Быстрая запись

- Кнопка `Быстрая запись` фокусирует компактный многострочный Markdown editor внутри Ленты без модального окна.
- Area selector всегда видим, но не обязателен. Default: активный area filter, иначе последняя использованная область; пользователь может выбрать `Без области`.
- `Ctrl+Enter` добавляет введённые блоки в соответствующий раздел сегодняшнего файла; `Enter` создаёт новую строку.
- Если H2 выбранной области отсутствует, он создаётся в конце документа. `Без области` добавляется в root section без H2.
- При storage/conflict failure текст остаётся в quick-capture buffer/draft и показывается retry; успешный toast без подтверждённой записи запрещён.

#### 6.2.6 Хронологическая Лента и Files drawer

- Day cards идут от новых к старым; today открыт, состояние свёрнутости прошлых дней запоминается локально.
- Старые дни подгружаются порциями при прокрутке; UI virtualization обязательна.
- `Файлы` открывает временную панель дерева vault без `.unlimotion`; это не отдельный постоянный режим.
- Открытая постоянная заметка редактируется тем же block Live Preview и закрывается с возвратом к сохранённой позиции Ленты.
- Пустой vault показывает onboarding с выбором каталога и объяснением, что файлы не копируются.

#### 6.2.7 Накопительный разбор

- Закрывать день вручную не требуется. Banner показывает количество pending blocks и дней с pending content.
- В очередь входят все новые содержательные блоки с последнего принятого решения. Приоритет:
  1. незавершённые checkboxes;
  2. ранее `deferred` (`Пропустить`);
  3. блоки без области;
  4. остальные новые блоки.
- Выполненные checkboxes не входят в очередь.
- Для Markdown list review-candidate определяется на уровне синтаксического task-list item, а не всего list container:
  - каждый marker `- [ ]`/`* [ ]`/`+ [ ]` на любом уровне вложенности является отдельным unfinished candidate; inline-текст `[ ]` без list-item marker checkbox не считается;
  - completed parent не скрывает unfinished child, mixed completed/unfinished siblings разбираются независимо;
  - если расширенное contiguous selection полностью покрывает несколько nested/sibling candidates, итоговое decision применяется ко всем покрытым input locators, чтобы дочерний checkbox не появился дубликатом;
  - completed item исключён, пока его собственный marker completed, даже если рядом есть unfinished sibling.
- Разбор остаётся внутри day card; отдельное окно не открывается. Одновременно выделен один candidate.
- Нажатие `Разобрать` создаёт portable `ReviewSessionId` и session-open event. Session закрывается только explicit `Завершить разбор` либо success summary; crash/restart без close event возобновляет тот же session, а не делает deferred blocks доступными раньше времени.
- Если другое устройство видит foreign session без `SessionClosed`, оно не применяет timeout и показывает две явные recovery actions: `Продолжить этот разбор` записывает causally dependent `SessionTakenOver` и продолжает тот же `ReviewSessionId`; `Завершить незавершённый разбор` записывает `SessionAbandoned`, после чего deferred blocks доступны только в новой causally следующей session.
- После takeover события прежнего owner, не наблюдавшие takeover, не могут reopen session: conflicting actions сохраняются как pending conflict. Вернувшееся устройство сначала синхронизирует session state.
- Начальное выделение — минимальный candidate block. Пользователь может расширять/сужать contiguous selection целыми Markdown-блоками вверх/вниз. Area H2 является защитной границей; пересечение другой области требует явного дополнительного действия.
- Действия над selection:
  - `Оставить в ежедневной` — записать decision `kept`; неизменённый block больше не предлагается;
  - `Пропустить` — decision `deferred`; block снова появится при следующем разборе с повышенным приоритетом;
  - `Назначить область` — физически переместить выбранные блоки под H2 области в том же файле;
  - `Создать задачу` — создать существующий `TaskItem` и atomically заменить всё contiguous selection одной живой ссылкой;
  - `Создать заметку` — перенести selection в новый `.md` и оставить стандартную wiki-link;
  - `Перенести на сегодня` — для прошлого дня переместить selection в сегодняшний daily file и оставить source link.
- Существенно изменённый `baseline-kept/kept/converted/moved` block получает новый content hash и снова становится candidate. Перемещение без изменения контента обновляет locator, а не создаёт дубликат решения.

#### 6.2.8 Review state без загрязнения каждого блока

- MVP не добавляет hidden ID к каждому Markdown-блоку.
- `ReviewStateStore` использует locator: relative daily path, area marker/name, normalized content hash, тип блока и occurrence среди одинаковых соседей.
- При неоднозначном rematch после внешнего редактирования безопасный default — показать block снова, а не считать его обработанным.
- Каждый portable review event имеет `EventId`, стабильный per-install `DeviceId`, монотонный `DeviceSequence` и causal context (observed per-device sequence/vector). Wall-clock timestamp используется только для отображения и никогда не определяет победителя.
- Sidecar event хранит decision, causal metadata, input/output locators и resulting entity/link ID. Явные решения пользователя `kept/deferred/converted/moved` всегда сильнее служебного `baseline-kept`; baseline заполняет только locator без explicit decision. Если explicit event causally dominates другой, применяется потомок. Concurrent conflicting explicit decisions без доказанного happens-after сохраняются обеими версиями и переводят locator в pending conflict; одинаковые idempotent events/operation ID deduplicate.
- При первом подключении существующего vault создаётся согласованный bootstrap snapshot:
  - все существующие незавершённые checkboxes остаются без terminal decision и входят в первый разбор;
  - остальные существующие content blocks получают служебный terminal decision `baseline-kept`; это не имитирует ручное действие `Оставить` и может отдельно отображаться в диагностике;
  - completed checkboxes по общему правилу review не входят в очередь;
  - новый либо содержательно изменённый block не совпадает с baseline fingerprint и становится pending по обычным правилам.
- Bootstrap начинается после запуска watcher. Его точка линеаризации — immutable start snapshot: множество найденных daily paths и для каждого первый успешно прочитанный `startRevision` + fingerprints. Повторное чтение никогда не расширяет baseline: только неизменённые fingerprints из start snapshot могут получить `baseline-kept`; новый file, rename destination, новый/изменённый block и любой unmatched fingerprint остаются pending. Delete просто удаляет отсутствующий candidate. Потерянное/неоднозначное watcher event обнаруживается final rescan и даёт safe pending.
- Bootstrap хранится append-only в `.unlimotion/review/bootstrap/<operationId>/`: `manifest.json` schema v1 (`VaultId`, operation ID, state, started/completed time, список start paths/revisions и hashes file batches) плюс `files/<pathHash>.json` с baseline fingerprints. Complete-manifest записывается последним и действителен только при наличии всех referenced batches с совпавшими hashes; partial/out-of-order sync считается незавершённой и не скрывает blocks.
- Второе устройство с тем же `VaultId` переиспользует valid complete bootstrap и не пишет новый baseline. Если два bootstrap начались до синхронизации, explicit review decisions доминируют, а `baseline-kept` признаётся только для exact locator, присутствующего во всех valid concurrent manifests; различия остаются pending. Incomplete foreign operation не получает автоматического timestamp-based overwrite.
- Crash/retry продолжает тот же local operation ID идемпотентно и не редактирует Markdown. Quick capture во время scan разрешён, но собственная запись маркируется post-snapshot и не может попасть в baseline.
- После завершения onboarding показывает итог: сколько daily files проиндексировано и сколько незавершённых checkboxes попало в первый разбор; обычная история не отображается как внезапный backlog.

#### 6.2.9 Создание задачи/цели из выделения

- Первая содержательная строка selection становится title без checkbox/list marker; остальные блоки становятся description с сохранением Markdown.
- Task создаётся через существующий repository/storage flow с текущим default status и без автоматически выдуманных planning dates.
- Наследуется primary area daily fragment; дополнительные области доступны через общий area picker. `IsGoal` по умолчанию `false`.
- Действие `Создать задачу` является подтверждением создания: сначала task сохраняется и получает стабильный ID, затем source заменяется task-link. При ошибке task storage source остаётся неизменным.
- После успешного создания прямо в review context показывается существующая task editing surface. Parent relations доступны через извлечённый reusable `TaskRelationsControl`, уже работающий с сохранённой target task.
- Relation control сохраняет несколько родителей, поиск, fuzzy mode, cycle/self/direct-link validation и повторную проверку перед storage mutation. Он получает explicit target task; временная смена глобального `CurrentTaskItem` запрещена.
- Focus и AutomationId скоупятся instance/context, чтобы relation blocks карточки и Ленты не конфликтовали.
- Source task-link contract:

```markdown
[Подготовить режим Ленты](unlimotion://task/01J...)
```

- Полное selection является input задачи и после successful conversion не остаётся рядом дубликатом. Проверяемый пример:

```markdown
<!-- before -->
- [ ] Подготовить режим Ленты
  Согласовать блочный разбор и работу с Obsidian.

<!-- after -->
[Подготовить режим Ленты](unlimotion://task/01J...)
```

- В Unlimotion label разрешается по ID и отображается как `TaskStatusPicker` слева + актуальный title справа. В Obsidian остаётся обычная Markdown-ссылка с fallback title.
- Удалённая/недоступная task не удаляет source line: показывается broken-reference state с сохранённым fallback title и действиями `Найти`, `Отвязать`, `Восстановить из revision`.
- Если task создан, но source rewrite не завершился, transaction journal хранит task ID; retry вставляет ссылку на тот же task и не создаёт дубликат.

#### 6.2.10 Создание постоянной заметки

- Inline form предлагает title по первой содержательной строке и thematic folder, запомненную для primary area. Оба значения редактируются.
- Новая заметка получает YAML:

```yaml
---
unlimotion-id: 01J...
unlimotion-areas:
  - 01J...
---
```

- Selection переносится с сохранением Markdown. В daily source остаётся Obsidian-compatible link и stable marker:

```markdown
[[Тематика/Название заметки|Название заметки]] <!-- unlimotion-note:01J... -->
```

- Permanent note может иметь несколько areas независимо от папки. Folder не является area и не создаёт её автоматически.
- Сначала atomically создаётся destination, затем при совпадении source revision заменяется source. При частичном failure данные не удаляются: transaction journal предлагает завершить замену или оставить обе копии.
- External rename/move сохраняет разрешение в Unlimotion по `unlimotion-id`; stale wiki path показывается с явным `Исправить ссылку`, если безопасный automatic rewrite невозможен.

#### 6.2.10a Перенос фрагмента на сегодня

- Действие доступно только для прошлого daily file. Selection переносится в сегодняшний файл под H2 той же primary area; для `Без области` — в root section.
- Destination получает стандартный Obsidian block anchor, уникальный для operation:

```markdown
Перенесённый Markdown-фрагмент.
^unlimotion-move-01J...
```

- В source весь selection заменяется ссылкой на target block:

```markdown
[[Ежедневные/2026-08-24#^unlimotion-move-01J...|Перенесено на 24 августа]]
```

- Flow destination-first и journaled: проверить обе revisions → создать operation/anchor → atomically записать destination → повторно проверить source → заменить selection → завершить review decision/journal.
- Retry ищет anchor/operation ID и не добавляет второй экземпляр. Если destination записан, а source изменился, обе версии сохраняются и recovery предлагает завершить source replacement либо оставить обе.

#### 6.2.10b Сериализация создаваемых ссылок и имён

- Task/wiki links создаются только dedicated serializer, а не строковой интерполяцией пользовательского текста.
- Task URI принимает только validated canonical task ID; label приводится к одной строке, control characters удаляются, а `\\`, `[`, `]`, `(` и `)` экранируются по CommonMark. Пользовательский title никогда не попадает в URI target.
- Оригинальный title постоянной заметки сохраняется внутри note (YAML/H1). Filename строится отдельно: path separators, control characters, Windows-invalid и wiki-delimiter symbols `<>:\"/\\|?*#[]` заменяются на `-`, trailing dots/spaces удаляются, reserved device names получают prefix `_`, empty result становится `Заметка`, collision — deterministic numeric suffix.
- Folder выбирается только из canonical existing vault path и не выводится из title. Wiki target использует безопасный relative filename. Alias добавляется только если title не содержит `|`, `#`, `[`, `]`, `\\` или newline; иначе используется wiki-link без alias, а полный исходный title остаётся внутри note.
- Move-to-today path, date alias и block anchor генерируются из fixed date/allowlisted operation ID, поэтому пользовательский текст не может изменить target/fragment.
- Golden tests обязаны покрывать `]`, `)`, `|`, `#`, `\\`, newline, Unicode, reserved Windows names, collision и path traversal input.

#### 6.2.11 Области и признак цели

- `AreaDefinition` образует строгое дерево: один `ParentId`, стабильный `Id`, `Name`, `IsArchived`, `SortOrder`, optional `DefaultNoteFolder`.
- Areas архивируются вместо физического удаления. Archived area остаётся разрешимой в истории/task/note metadata, но скрыта из default pickers.
- Tasks/goals и permanent notes имеют `AreaIds: List<string>` и могут принадлежать нескольким областям.
- Daily block имеет одну primary area по положению в файле.
- `Goal` — `TaskItem.IsGoal`, а не отдельная сущность. Все поля, status, criteria и relations идентичны задаче. Отдельный top-level режим не создаётся.
- Task card и inline task surface используют один reusable area picker и один toggle `Цель`.
- Area selector содержит действие `Управлять областями`, открывающее временную панель, а не постоянный режим. Панель позволяет создать root/child area, переименовать, изменить parent с cycle validation, настроить default note folder, архивировать и восстановить.
- В режиме `Задачи` существующий filter surface получает tri-state filter `Все / Цели / Обычные`; task card и task row показывают ненавязчивый goal indicator. Это не создаёт отдельную сущность или вкладку `Цели`.
- Смена vault не удаляет неизвестные `AreaIds`; они отображаются unresolved chips до возврата каталога/ручного переназначения.

#### 6.2.12 Поиск

- Поиск не открывает новый экран: непустой query временно заменяет day stream списком result fragments; очистка восстанавливает scroll/collapse state Ленты.
- Scope по умолчанию: daily notes, permanent notes и tasks. Доступны фильтры area, date range и type.
- Note result содержит matched block, несколько строк/блоков context, date/modified date, area и relative path.
- Sort default — newest first: daily date для daily, `UpdatedDateTime` для task, filesystem modified time для permanent note.
- Нажатие открывает точный текущий block; stale anchor вызывает transparent re-query и не открывает неверный фрагмент.
- Индекс incremental/rebuildable, не source of truth, обновляется на create/change/rename/delete и task change. Indexing выполняется off UI thread с cancellation/coalescing.
- UI может переиспользовать внешний вид `SearchControl`/normalization/fuzzy matcher, но note result pipeline остаётся отдельным от task tree filter.

#### 6.2.13 Внешние изменения, конфликты и версии

- Recursive Markdown watcher обрабатывает create/change/delete/rename во всех тематических папках и `Ежедневные`, но не индексирует `.unlimotion` как notes.
- Dedicated sidecar watcher/rescan применяет внешние `vault.json`, `areas.json` и `review/` updates; собственные sidecar writes подавляются по operation/hash, а app-local transactions/revisions/drafts этим watcher не принадлежат.
- Собственные atomic writes коррелируются operation ID/hash, а не только file name.
- Если активного dirty buffer нет, внешняя версия загружается и переиндексируется автоматически с восстановлением ближайшего block/scroll anchor.
- Если dirty buffer есть и disk revision отличается, autosave останавливается и показывается conflict surface:
  - `Использовать мою версию` — записать её поверх только после дополнительного safety snapshot;
  - `Использовать версию с диска` — сохранить dirty buffer в recovery draft и загрузить disk;
  - `Сохранить обе` — оставить disk original и записать editor version в явно показанный sibling file с conflict suffix.
- Любой вариант сохраняет доступ к отклонённой версии через draft/revision. Молчаливый last-write-wins запрещён.
- Watcher, index и delayed callbacks освобождаются при dispose/session restart.

#### 6.2.14 Производительность, accessibility и responsive behavior

- File IO, parsing и indexing не выполняются синхронно на UI thread.
- Feed virtualizes day cards и block lists; search/reparse cancel предыдущую работу.
- Большой vault показывает progressive indexing state, но quick capture сегодняшнего файла остаётся доступным после проверки root.
- Все actions имеют keyboard focus, accessible name и стабильный AutomationId.
- Wide layout следует render; narrow layout переносит toolbar actions в overflow, но `Лента/Задачи`, quick capture и review current action остаются доступны без горизонтальной прокрутки.
- Status picker всегда предшествует task title и в logical tree, и визуально; target size и tooltip соответствуют существующему `TaskStatusPicker`.

#### 6.2.15 Security / safety

- Note content не отправляется в server/cloud подсистему Unlimotion.
- Renderer не исполняет raw HTML/script и не загружает remote resources автоматически.
- Все file operations проверяют confinement выбранным vault root, canonical path и destination collision.
- Symlink/junction escape, reserved device paths и write вне root отклоняются с понятной ошибкой.
- Cross-file/task transformations журналируются и идемпотентны по operation ID.
- Логи не содержат полного note text; допускаются relative path, block type, hash и operation ID.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Первый вход | Выбрать `Лента` без vault | Понятный onboarding; выбор существующего каталога без копирования | Headless + screenshot | AC-01, AC-18 |
| Первый вход в существующий vault | Завершить onboarding | Обычная история получает baseline, все незавершённые checkboxes попадают в первый разбор; показываются итоговые counts | Unit bootstrap + Headless | AC-23 |
| Быстрая запись | Ввести Markdown и `Ctrl+Enter` | Текст появляется в сегодняшнем разделе выбранной области и сохранён в `.md` | Unit storage + Headless | AC-02, AC-03 |
| Live Preview | Открыть/изменить block | Неактивные блоки render, активный редактируется; чужой Markdown не исчезает | Parser round-trip + Headless | AC-04 |
| Хронология | Прокручивать старые дни | Day cards идут newest-first, подгружаются без потери позиции | Headless/perf check | AC-05 |
| Разбор | Нажать `Разобрать` | В day card выделяется один candidate; границы меняются целыми блоками | Unit + Headless | AC-06, AC-07 |
| Оставить/пропустить | Выбрать действие | `Оставить` не возвращается без изменения; `Пропустить` возвращается позже | Unit review state + Headless | AC-08 |
| Назначить область | Выбрать area | Selection физически перемещается под H2 и остаётся активным для следующего review action | Storage integration + Headless | AC-09 |
| Создать задачу | Преобразовать selection | Создаётся обычная task; доступны existing parent relations; source получает live link | Unit/storage + Headless | AC-10, AC-11 |
| Изменить статус | Нажать icon слева от task title | Открывается существующий picker; task и все projections обновляются | Headless + FlaUI bounds | AC-12 |
| Создать заметку | Выбрать title/folder | Создаётся Markdown note с YAML; source заменён wiki-link | Integration + Headless | AC-13 |
| Перенести на сегодня | Выбрать fragment прошлого дня | В сегодняшнем файле появляется fragment с anchor, а source заменяется одной wiki block-link | Integration + Headless | AC-22 |
| Поиск | Ввести query/filter | Лента заменяется fragment results; очистка возвращает прежнюю позицию | Unit index + Headless | AC-15 |
| External edit | Изменить/переместить file или portable sidecar извне | Feed/index либо area/review state безопасно обновляются | Watcher integration + Headless | AC-16 |
| Concurrent edit | Изменить active file извне | Нет перезаписи; показаны три безопасных conflict actions | Unit + Headless | AC-17 |
| Task navigation | Нажать task title | Открывается эта task в существующей карточке режима `Задачи` | AppAutomation | AC-12 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Vault не настроен | Открыть Ленту | Onboarding | Cancel оставляет Tasks доступным | Файлы не создаются до выбора root |
| Existing vault, first connect | Завершить onboarding | Atomically создать baseline обычной истории; unfinished checkboxes оставить pending | Concurrent file re-read; crash resume; unstable file остаётся pending | Показываются indexed/pending counts |
| Same vault, second device | Получить complete bootstrap через sync | Проверить hashes и переиспользовать baseline | Partial/out-of-order batches ничего не скрывают и показывают sync/indexing state | Новый baseline не создаётся |
| Concurrent first connects | Синхронизировать два valid manifests | Explicit decisions сохраняются; baseline — safe intersection | Различающиеся locators pending | Timestamp не повышает baseline priority |
| Divergent vault identity | Sidecar watcher видит другой `VaultId` | Freeze writes, сохранить обе branches и показать три conflict actions | Revision drift отменяет confirm; losing decisions доступны как safe-pending | Recovery namespace rebind только после journals |
| Vault доступен, daily отсутствует | Quick capture | Создать `Ежедневные/YYYY-MM-DD.md` и записать | Write failure сохраняет draft | Atomic write |
| Feed idle | Ввести search | Search mode | Пустой query возвращает Feed state | Scroll/collapse preserved |
| Review pending | `Разобрать` | Первый candidate selected | Нет candidates — success summary | Без modal |
| Foreign review session open | Открыть разбор на другом устройстве | Предложить продолжить тот же session либо явно завершить незавершённый | Без выбора deferred остаются закрыты; timeout отсутствует | Takeover/abandon causal events |
| Candidate selected | Expand/shrink | Contiguous whole-block selection | Area boundary требует explicit override | Heading не режется |
| Candidate selected | `Оставить` | Decision kept, next candidate | State write failure не двигает очередь | Idempotent |
| Candidate selected | `Пропустить` | Decision deferred, next candidate | Не возвращается до causal close и новой session | Crash resume сохраняет session ID |
| Candidate selected | Assign area | Atomically move selection, remap locator и оставить тот же selection active | Failure оставляет source/selection прежними | Intermediate, не terminal |
| Candidate selected | Create task | Persist task → rewrite source → show task surface | Task failure: source unchanged; rewrite failure: journal/retry | Stable task ID first |
| Task live link | Change status | Existing transition policy | Denied transition показывает existing reason | Picker слева |
| Candidate selected | Create note | Create destination → rewrite source | Partial failure journal; no delete | Preserve both is safe default |
| Candidate прошлого дня | Move to today | Destination с anchor → source block-link → next candidate | Partial failure сохраняет обе копии; retry не дублирует destination | Journaled destination-first |
| Editor clean | External change | Auto reload/index | Malformed Markdown raw fallback | No dialog |
| Editor dirty | External change | Conflict state | Autosave disabled until choice | Three actions |
| Portable sidecar clean | External identity/area/review change | Validate identity/bootstrap, reload/semantic merge | Divergent identity блокирует sidecar writes; concurrent area edit открывает conflict | Отдельный sidecar watcher |
| Watcher/index disposed | Delayed event | No state change | No crash/leak | Lifecycle test |
| Area archived | Open old content | Historical resolution remains | Picker hides by default | No data deletion |
| Task missing | Render live link | Broken-reference state | Fallback title remains | Recovery actions |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Источник заметок | user | Прямой существующий Obsidian vault; Markdown source of truth | 1.0 | Копирование разрушит workflow | Нет |
| Верхние режимы | user | Только `Лента` и `Задачи`; task tabs остаются внутри `Задачи` | 0.95 | Лишние постоянные окна перегрузят UI | Нет |
| Платформа MVP | agent | Desktop-first; shared projects продолжают собираться, unsupported providers скрыты/disabled | 0.9 | Пользователь может ожидать mobile сразу | Нет; явно проверяется approval этой SPEC |
| Единица review selection | user | Целые Markdown-блоки с изменяемыми границами | 1.0 | Посимвольный выбор ломает структуру | Нет |
| Состав очереди | user | Все новые content blocks; checkbox/deferred/unassigned выше | 1.0 | Неотмеченная полезная мысль может потеряться | Нет |
| D-01: first-run review baseline | user | Выбран вариант 1: существующий обычный текст `baseline-kept`, все незавершённые checkboxes pending; всё новое/изменённое после включения pending | 1.0 | Atomic bootstrap/retry обязателен, иначе возможен неполный baseline | Нет |
| Семантика areas | user | Daily block — одна primary area; task/goal/note — несколько areas | 1.0 | Несовместимость физического H2 и multi-area | Нет |
| Родительские связи | user/repo contract | Переиспользовать existing block и поддержать несколько родителей | 1.0 | Новый picker разойдётся с task card | Нет |
| Lifecycle task conversion | agent | Сначала сохранить task с ID, затем показать extracted existing relation block | 0.9 | Source/task могут частично разойтись | Нет; transaction journal закрывает риск |
| Review persistence | agent | Sidecar fingerprints, без hidden marker в каждом block | 0.85 | Неоднозначный rematch | Нет; safe default повторно показывает block |
| Live Preview | agent | Block renderer + raw active block | 0.9 | Полный WYSIWYG существенно дороже/рискованнее | Нет |
| Task link | agent | `unlimotion://task/{id}` с Markdown fallback title | 0.9 | Custom URI не даёт full live status в Obsidian | Нет; Obsidian сохраняет читаемый fallback |
| D-02: поздняя запись при удалении | user | Выбран вариант 1: переносимая `.unlimotion/deleted/<operation-id>/<original-relative-path>` без автоматической очистки | 1.0 | Автоматическая очистка может unlink-нуть inode, в который поздно допишет POSIX writer | Нет; явный user-facing cleanup — отдельный будущий workflow |
| Note link | user/agent | Standard wiki-link + stable hidden note marker | 0.95 | Rename может сделать wiki path stale | Нет; ID resolution + explicit repair |
| Task defaults | agent | Existing default status, no inferred planning dates, `IsGoal=false` | 0.9 | Неожиданная дата/статус изменят смысл | Нет |
| Markdown dependency | agent | Выбрать в EXEC после compatibility/license/AOT check; contract не зависит от package | 0.8 | Не найдётся готовый безопасный renderer/parser | Нет; stop rule запрещает silent compromise |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Vault root | Отсутствует | `NoteVaultSettings.RootPath`, desktop folder picker | Не копировать; пустой setting скрывает Feed content | Settings + Headless onboarding |
| Vault identity | Отсутствует | `.unlimotion/vault.json`, schema v1, immutable `VaultId` | Root relocation сохраняет ID; duplicate local root blocked; divergent IDs conflict | Identity/relocation/sync tests |
| Day boundary | Отсутствует | Configurable local boundary, default `00:00` | Existing files определяются по filename | Unit effective-date tests |
| Daily note | External Markdown | `Ежедневные/YYYY-MM-DD.md`, H2 area sections | Existing Markdown preserved | Golden round-trip tests |
| Permanent note | External Markdown | YAML `unlimotion-id` + `unlimotion-areas` | Existing YAML keys preserved; metadata additive | Parser/storage tests |
| Area catalog | Отсутствует | `.unlimotion/areas.json`, schema v1 | Lazy create; no task-tree conversion | Serialization/migration tests |
| Review state | Отсутствует | `.unlimotion/review/*.json`, schema v1 + validated bootstrap operation batches | Explicit decision > baseline; partial/out-of-order/concurrent bootstrap safe-pending | Review rematch/bootstrap sync tests |
| Task classification | `TaskItem` без fields | `IsGoal: bool`, `AreaIds: List<string>` | Defaults false/empty; update all molds/mapping | Local/server round-trip tests |
| Remote classification capability | Отсутствует | `TaskClassificationSchemaVersion = 1`; absent/old server означает unsupported | Old-client writes preserve server values; new client disables unsafe edit against old server | Compatibility/Hub tests |
| Task live link | Отсутствует | `unlimotion://task/{id}` | Unknown apps retain readable Markdown label | Parser/render/navigation tests |
| Note durable link | Wiki-link only | Wiki-link + `unlimotion-note` marker | Marker optional for existing notes | Rename/repair tests |
| Search index | Task-only filter | Rebuildable local block index + task adapter | Delete cache → rebuild | Index lifecycle tests |
| Draft | Отсутствует | App-local dirty block draft keyed by vault/path | User can discard/recover | Crash recovery tests |
| Revision | Git/task backup only | Bounded note safety snapshots | Does not replace external sync/Git | Restore/retention tests |
| Verified delete safety | Direct file unlink | `.unlimotion/deleted/<operation-id>/<original-relative-path>` durable quarantine | Portable and excluded from Feed/watch; no automatic cleanup until a separate user-facing workflow | Pre-open writer / safe-conflict regression |

## 7. Бизнес-правила / Алгоритмы

### 7.1 Эффективный день

`effectiveDate = (localNow - configuredDayBoundaryOffset).Date`.

- Default boundary: `00:00` local time.
- DST/offset берётся из текущей local timezone; filename содержит только resulting calendar date.
- Если пользователь меняет boundary, существующие файлы не переименовываются автоматически.

### 7.2 Review eligibility

Block eligible, если одновременно:

1. block является content block, а не YAML/area marker;
2. checkbox не имеет состояние completed;
3. нет точного актуального decision с тем же content hash и terminal state `baseline-kept/kept/converted/moved`;
4. source file не находится в internal `.unlimotion`;
5. block не является существующим обычным content block, вошедшим в успешно завершённый first-connect baseline; это исключение не применяется к незавершённым checkboxes.

Для `deferred` действует дополнительное условие: текущая session должна causally observe `SessionClosed` либо `SessionAbandoned` той session, в которой создано deferred decision. Поэтому Skip и Move-to-Today destination исключены из исходной/возобновлённой после crash session и получают высокий приоритет только в действительно следующей session. Concurrent session на втором устройстве без observed close/abandon не считается следующей и оставляет block deferred; explicit `SessionTakenOver` позволяет безопасно продолжить и затем закрыть orphaned session.

Order key: `incomplete-checkbox` → `deferred` → `without-area` → `other`, затем day ascending внутри review session и document order внутри дня. Feed вне review остаётся newest-first.

### 7.3 Area assignment

- Selection перемещается как contiguous raw slices.
- Destination H2 определяется стабильным area ID; отсутствующий H2 создаётся в конце daily file.
- Empty H2 после move не удаляется автоматически, если он существовал до операции; созданный той же незавершённой transaction пустой H2 можно откатить.
- Task/note `AreaIds` не выводятся из folder path.
- `Назначить область` — intermediate review action: после successful atomic move input locator заменяется новым output locator в active session, selection остаётся выделенным и пользователь может создать task/note либо выбрать terminal action. Terminal `kept/deferred/converted/moved` автоматически не создаётся; выход из review оставляет remapped block pending.

### 7.4 Task conversion atomicity

1. Проверить source revision и selection locator.
2. Создать operation ID и pending journal entry.
3. Создать task через существующий storage с parsed fields.
4. Записать task ID в journal.
5. Повторно проверить source revision и atomically заменить всё исходное contiguous selection одной task-ссылкой.
6. Сохранить audit decision `converted` для всех покрытых input locators и terminal `converted` для output task-link locator; завершить journal.

Retry после шага 3 обязан использовать сохранённый task ID. Output link не входит в следующую review queue после success/restart. Автоматическое удаление созданной task при source failure запрещено.

### 7.5 Note extraction atomicity

1. Проверить paths/collision/source revision.
2. Создать revision и pending journal.
3. Atomically создать destination note с stable ID.
4. Повторно проверить source revision и заменить selection wiki-link.
5. Сохранить audit decision `converted` для всех input locators и terminal `converted` для output wiki-link locator; завершить journal.

Permanent destination не участвует в daily review queue; output source link не возвращается после success/restart. При failure после шага 3 обе копии сохраняются до явного recovery action.

### 7.6 Move-to-today atomicity

1. Проверить source/destination paths, revisions и source selection.
2. Создать app-local pending journal с stable operation/block-anchor ID.
3. Atomically добавить selection и anchor в destination area/root, не дублируя уже существующий anchor.
4. Повторно проверить source revision и atomically заменить всё selection wiki block-link.
5. Для всех input locators и source output-link записать terminal `moved`; destination content получает `deferredFromSessionId`. Он становится eligible только в session, causally наблюдающей `SessionClosed` либо `SessionAbandoned` исходной session, и только если к тому моменту не completed/converted/kept. Завершить journal.

Failure после шага 3 сохраняет обе копии и recovery state. Retry использует существующий anchor и восстанавливает те же output decisions; сразу после retry/restart source link и destination не дублируются в текущей queue. Автоматически удалять destination запрещено.

### 7.7 Leave / Skip

- `Оставить` — terminal для текущего content hash.
- `Пропустить` — non-terminal `deferredFromSessionId`; исключён из текущей и crash-resumed/taken-over session, eligible с повышенным приоритетом только после causal `SessionClosed`/`SessionAbandoned` и открытия следующей session.
- Любое изменение normalized content меняет hash и делает block eligible снова.
- Изменение только line ending/неcодержательного outer whitespace не считается существенным.

### 7.8 Stable references

- Task status/title всегда читаются из task repository по ID; Markdown label — fallback.
- Note ID читается из YAML; marker связывает source link с ID.
- Missing entity никогда не удаляет source markup автоматически.

## 8. Точки интеграции и триггеры

- App startup/settings change → validate vault root, start/replace watcher, build/rebuild index.
- Remote task-source connect → query task-storage capabilities; enable Goal/Area editing and Feed task conversion only при `TaskClassificationSchemaVersion >= 1`.
- Shell mode switch → activate/deactivate Feed subscriptions без dispose task context.
- Local time/day boundary crossing → переключить current daily file после завершения/сохранения активного edit.
- Quick capture/save → `INoteVault.MutateDocument(expectedRevision)` → watcher correlation → index update.
- Watcher create/change/rename/delete → coalesce → parse/index → update visible day/note if clean.
- Review action → transaction journal → note/task storage → review state → UI next candidate.
- Task repository change → update live links/search results/status icon.
- Task title click → set task mode/current task/details open.
- Area rename/archive → update catalog/projections; raw daily headings обновляются только через безопасную explicit operation.
- App dispose/session restart → cancel IO/index work, detach watcher and delayed callbacks, persist drafts.

## 9. Изменения модели данных / состояния

### 9.1 Task contracts

В `TaskItem`:

```csharp
public bool IsGoal { get; set; }
public List<string> AreaIds { get; set; } = new();
```

- Те же additive fields добавляются в `TaskItemHubMold`, `ReceiveTaskItem` и server `TaskItemMold`; AutoMapper configuration и round-trip tests обновляются.
- Existing local JSON: отсутствующие поля читаются как `false`/empty.
- Remote protocol вводит `TaskClassificationSchemaVersion = 1`:
  - new client сначала запрашивает capability у task source; отсутствие endpoint/version означает unsupported и блокирует Goal/Area editing и Feed task conversion с понятным предложением обновить server;
  - `TaskItemHubMold` использует nullable presence/version fields для classification update;
  - new server при запросе старого клиента без version/presence сохраняет существующие `IsGoal/AreaIds` вместо применения `false`/empty;
  - new client/new server выполняют полный round-trip;
  - old client может продолжать менять legacy task fields, не стирая classification.
- Existing `JsonExtensionData` и unknown fields должны продолжать сохраняться в local file flow.

### 9.2 Area catalog schema v1

```json
{
  "schemaVersion": 1,
  "areas": [
    {
      "id": "01J...",
      "name": "Unlimotion",
      "parentId": "01J...",
      "isArchived": false,
      "sortOrder": 0,
      "defaultNoteFolder": "Работа/Unlimotion"
    }
  ]
}
```

- `id` immutable; `name`, `parentId`, archive/order/folder mutable.
- Cycle/self-parent запрещены.
- Unknown additive fields сохраняются при round-trip.

### 9.3 Review state schema v1

Review records содержат `VaultId`, `EventId`, `DeviceId`, monotonic `DeviceSequence`, causal context, display timestamp, relative path, area identity, block type, normalized content hash, occurrence/neighbor anchors, decision (`baseline-kept/kept/deferred/converted/moved`), `ReviewSessionId` и session events `Opened/TakenOver/Closed/Abandoned`, input locator, output locator(s), operation/result entity ID. `baseline-kept` всегда имеет меньший merge priority, чем explicit decision; concurrent explicit conflicts становятся pending. Bootstrap schema/path/batch validation заданы в 6.2.8; exact JSON layout остальных events может быть оптимизирован в EXEC, но causal semantics, decision precedence, orphan-session recovery, atomic completion и safe rematch являются contract.

### 9.4 Runtime-only state

- selected shell mode;
- feed scroll/collapse state;
- active document/block and dirty buffer;
- review session current candidate/selection range;
- search query/results/cancellation version;
- watcher correlation/expected revisions;
- unresolved transaction recovery prompts.

## 10. Миграция / Rollout / Rollback

### Rollout

1. Добавить backward-default task fields и round-trip coverage.
2. Добавить isolated Notes contracts/storage/parser/index без включения UI.
3. Добавить settings/onboarding и desktop vault provider.
4. Добавить shell mode и read-only Feed, затем editing/quick capture.
5. Добавить review actions и task/note conversions с journal.
6. Добавить conflict/recovery/revisions.
7. Включить feature после automated/visual gates.

### Первый запуск

- Existing tasks не меняются и получают `IsGoal=false`, `AreaIds=[]` при чтении.
- Vault не сканируется и `.unlimotion` не создаётся до выбора root/включения Feed.
- Existing Markdown не переписывается при первичном индексе.
- При первом подключении create-if-absent создаёт `.unlimotion/vault.json`; existing обычные content blocks atomically получают `baseline-kept`, все незавершённые task-list items остаются pending, а completed items исключаются по обычному правилу.
- Watcher запускается до start snapshot. Новые/изменённые/renamed после snapshot files/blocks не добавляются в baseline; повторное чтение может только подтвердить start fingerprints, но не расширить их.
- Bootstrap не меняет Markdown, показывает progress/result counts и безопасно resume после crash. Valid complete manifest переиспользуется вторым устройством; incomplete/out-of-order/concurrent state следует precedence/intersection rules 6.2.8 и не скрывает спорные blocks.
- Служебные metadata появляются только при соответствующем пользовательском действии.

### Rollback

- Feature flag/верхний режим можно отключить, не удаляя Markdown.
- `.unlimotion` sidecars не нужны Obsidian для чтения заметок; их удаление сбросит area/review state, но не содержание `.md`. App-local recovery artifacts удаляются отдельно только после явного подтверждения.
- New server сохраняет classification при old-client writes; new client отключает unsafe classification flow при отсутствии remote capability. Перед server downgrade всё равно требуется backup и завершение classification edits.
- App-local transaction recovery выполняется до отключения функции; unresolved journal нельзя молча удалять.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **AC-01 — Shell и onboarding.** В `MainScreen` доступны верхние режимы `Лента` и `Задачи`; до выбора vault Лента показывает onboarding, а существующий task context не теряется при переключениях.
- **AC-02 — Daily file contract.** Сегодня определяется через configurable boundary, файл создаётся только как `Ежедневные/YYYY-MM-DD.md`, а существующий файл читается без полного rewrite.
- **AC-03 — Быстрая запись.** `Ctrl+Enter` сохраняет многострочный Markdown в выбранную/последнюю область либо `Без области`; при failure buffer/draft не теряется.
- **AC-04 — Live Preview round-trip.** Обязательные Markdown blocks render/edit на месте; unsupported syntax сохраняется raw; изменение одного блока не форматирует несвязанные блоки/YAML/line endings.
- **AC-05 — Хронология и Files.** Feed показывает newest-first day cards, лениво загружает старые дни, сохраняет scroll/collapse state; Files drawer открывает thematic notes без отдельного постоянного режима.
- **AC-06 — Review queue.** Все новые content blocks входят в очередь по зафиксированному приоритету; каждый syntactic unfinished task-list item, включая child под completed parent и mixed sibling, является candidate, completed items исключены, а covered nested locators не дублируются после общего action; banner показывает pending blocks/days.
- **AC-07 — Изменяемое выделение.** В review одновременно один candidate; selection расширяется/сужается только contiguous Markdown-блоками и не режет area heading.
- **AC-08 — Leave/Skip.** `Оставить` исключает неизменённый block из следующих разборов; `Пропустить` не возвращается в исходной/crash-resumed/taken-over session и становится eligible только после causal close/abandon → new open. Foreign orphaned session можно явно продолжить или завершить без timeout; content change делает block eligible снова.
- **AC-09 — Назначение области.** Selection физически перемещается под выбранный H2, отсутствующий heading создаётся, source Markdown остаётся валидным; locator remap сохраняет selection активным как intermediate action, а выход без terminal action оставляет block pending.
- **AC-10 — Создание задачи.** Первая строка становится title, остальное description; task получает current default status, primary `AreaId`, `IsGoal=false`, не получает выдуманных planning dates; после successful task persistence всё исходное selection atomically заменяется ровно одной serialized live link, покрытые input locators и output link получают `converted`, поэтому retry/restart не создают task либо review-candidate повторно.
- **AC-11 — Родительские связи.** Inline task surface переиспользует извлечённый existing relation block; поддерживает несколько родителей, existing search/fuzzy/cycle validation и explicit target без переключения глобального `CurrentTaskItem`.
- **AC-12 — Живая task-ссылка.** `TaskStatusPicker` логически и визуально расположен слева от title; status change использует existing policy и виден в Feed и task projections; title click открывает существующую карточку в `Задачи`.
- **AC-13 — Создание заметки.** Selection переносится в выбранный thematic `.md`, original title сохраняется внутри note, filename/path безопасно сериализуются, daily source получает валидную wiki-link + note marker; input/output locators получают `converted`, partial failure сохраняет данные и recovery action.
- **AC-14 — Areas и Goal.** Area tree управляется во временной панели и архивируется без удаления; tasks/goals/notes поддерживают multiple areas; Goal является bool-признаком TaskItem; local/new-server round-trip сохраняет fields, old-client save на new server их не стирает, new client блокирует unsafe classification edit against old server.
- **AC-15 — Поиск.** Query заменяет Feed fragment results по daily/notes/tasks, поддерживает area/date/type filters, newest-first и открытие точного block; очистка возвращает сохранённую Feed position.
- **AC-16 — External changes.** Recursive Markdown watcher обрабатывает create/change/rename/delete, игнорирует собственные writes по operation/hash и обновляет Feed/index без active dirty buffer; отдельный sidecar watcher валидирует identity/bootstrap и применяет causal review/area changes. Concurrent explicit decisions становятся pending conflict. При divergent identity writes freeze, обе branches сохраняются, три recovery actions доступны, revision drift отменяет confirm, а losing decisions не теряются молча.
- **AC-17 — Conflict/recovery/revisions.** Dirty external change не перезаписывается; доступны три действия; crash draft восстанавливается; bounded revisions и transaction journal предотвращают data loss/duplicate conversion. Verified delete не unlink-ает поздние байты внешнего POSIX writer: они остаются в `.unlimotion/deleted/<operation-id>/<original-relative-path>` до отдельной явной очистки.
- **AC-18 — Path, link и rendering safety.** Запись вне canonical vault root, symlink/junction escape, executable HTML/script и unsafe URI блокируются; task/wiki serializers не позволяют special characters/path traversal изменить target, fragment или Markdown structure; логи не содержат note body.
- **AC-19 — Platform boundary.** Desktop provider проходит end-to-end tests; shared solution продолжает собираться для поддерживаемых repo targets, а UI не обещает/не активирует неподтверждённый external-vault provider.
- **AC-20 — Responsive/visual/accessibility.** Wide/narrow UI соответствует wireframe, не вводит modal review, status icon остаётся слева, controls доступны с клавиатуры/automation names и нет горизонтальной прокрутки основного потока.
- **AC-21 — Производительность/lifecycle.** IO/indexing не блокируют UI, Feed virtualized, stale work cancel/coalesce, dispose останавливает watcher/index callbacks без cross-session mutation.
- **AC-22 — Перенести на сегодня.** Для прошлого дня всё selection destination-first переносится под соответствующую область сегодняшнего файла, получает unique Obsidian block anchor, а source заменяется terminal `moved` block-link; destination получает `deferredFromSessionId` и становится eligible только после causally observed session close/abandon и новой session. Retry/restart/takeover не создают второй destination block и не возвращают source/destination в текущую queue.
- **AC-23 — First-connect baseline.** Для seeded existing vault обычные content blocks не создают backlog, каждый syntactic unfinished task-list item становится pending, completed items исключены. Watcher стартует раньше immutable start snapshot; create/change/rename после него остаются pending даже после stable re-read. Bootstrap не меняет `.md`, atomically завершается/возобновляется после crash, валидируется по `VaultId`/batch hashes и переиспользуется вторым устройством; partial/out-of-order/concurrent manifests и divergent identity не скрывают blocks. Onboarding показывает indexed files и pending checkbox counts.

### Автоматические тесты

Characterization до изменения:

- существующие task tabs и selection state;
- `TaskStatusPicker` layout/flyout/transition;
- `MainControlRelationPickerUiTests` и multiple-parents contract;
- task local/server mapping round-trip;
- watcher/session lifecycle patterns.

Новые/изменяемые test groups в `src/Unlimotion.Test`:

- `DailyMarkdownStorageTests` — path confinement, atomic writes, BOM/line endings, golden round-trip, YAML preservation;
- `DailyMarkdownParserTests` — block boundaries, area H2/markers, unsupported raw blocks, mixed/nested task-list item candidates;
- `FeedEffectiveDateTests` — boundary/timezone/DST;
- `FeedReviewQueueTests` — eligibility/priority, mixed siblings, completed parent + unfinished child, covered nested locators и rematch;
- `FeedFirstConnectBaselineTests` — ordinary-history baseline, mixed/nested checkboxes, immutable start snapshot, post-start create/change/rename/delete, stable re-read, atomic completion и crash resume;
- `VaultIdentityAndBootstrapSyncTests` — create-if-absent identity, relocation/duplicate root, second-device reuse, partial/out-of-order batches, concurrent bootstrap intersection, divergent-ID detection и трехвариантное resolution с сохранением losing branch/recovery namespace;
- `FeedBlockSelectionTests` — contiguous expansion, heading boundaries;
- `FeedReviewActionTests` — keep, durable session open/close/crash resume, cross-device orphan takeover/abandon, stale-owner conflict, defer eligibility, intermediate area move/locator remap и move today;
- `FeedTaskConversionTests` — field mapping, link escaping, input/output decisions, queue after retry/restart, no duplicate task;
- `FeedNoteExtractionTests` — YAML, safe filename/wiki serializer, special characters/path collision, input/output decisions и partial recovery;
- `FeedMoveToTodayTests` — destination/source revisions, anchor/link, source `moved`, destination next-session `deferred`, queue after retry/restart и partial recovery;
- `FeedAreasTests` — hierarchy/cycle/archive/unknown fields;
- `TaskClassificationRoundTripTests` — local, hub/server molds and mapping;
- `TaskClassificationCompatibilityTests` — capability negotiation, old-client preservation и new-client/old-server disabled state;
- `FeedSearchIndexTests` — context, sort, filters, stale anchors, CRUD/rename;
- `FeedExternalChangeTests` — recursive watcher, coalescing, own-write suppression;
- `FeedSidecarSyncTests` — external areas/review updates, own-write suppression, `DeviceId`/sequence/causal merge, offline out-of-order/clock-skew explicit conflicts и concurrent area conflict;
- `FeedDocumentConflictTests` — clean reload and three dirty choices;
- `FeedRevisionAndDraftTests` — retention/recovery;
- `FeedTaskReferenceUiTests` — actual `TaskStatusPicker`, logical/visual left-to-right order, navigation;
- `FeedControlResponsiveUiTests` — wide/narrow/keyboard/AutomationId;
- lifecycle extension around `HeadlessSessionStorageLifecycleTests`.

AppAutomation:

- добавить `UnlimotionAutomationScenario.Feed`, seed vault и `FeedScenariosBase<TSession>`;
- Headless: onboarding → quick capture → review → task with parent → status change → task card navigation → search → external edit/conflict;
- FlaUI в интерактивной Windows-сессии: status/title bounds, keyboard focus, narrow/wide top-level navigation и screenshot evidence.

### Visual acceptance

- Верхний switch находится над содержимым двух режимов, а не среди task tabs.
- Review не открывает отдельное окно и выделяет один block внутри day card.
- Task reference имеет реальный `TaskStatusPicker` слева и title справа с измеримым gap; postfix status вместо icon не допускается.
- Toolbar сохраняет `Лента/Задачи`, quick capture и review action в narrow state; вторичные actions уходят в overflow.
- Empty, indexing, storage error, conflict, broken link и no-search-results states имеют явный текст и recovery action.
- После EXEC подготовить inspected screenshot в `output/playwright/` или AppAutomation artifact directory и приложить к чату; не коммитить автоматически.

### UI video evidence

- Обязательного baseline-видео для новой функции нет, поэтому `до` video не применимо.
- Если automated desktop capture доступен, записать `после` flow `quick capture → review → task status → back to Feed` из passing AppAutomation/FlaUI run.
- Если capture объективно недоступен, fallback: passing Headless/FlaUI geometry tests + inspected screenshot + точная команда запуска. Отсутствие видео не отменяет UI tests.

### Команды для проверки

Preflight/discovery:

```powershell
dotnet --info
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --list-tests
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -- --list-tests
```

Targeted examples (финальные class names сверяются через `--list-tests`):

```powershell
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/DailyMarkdownStorageTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/FeedReviewQueueTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/FeedFirstConnectBaselineTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/FeedTaskReferenceUiTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowHeadlessTests/Feed*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
```

Affected build и обязательный serial gate:

```powershell
dotnet build src/Unlimotion.sln -c Release
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
```

FlaUI (последовательно, только при доступной интерактивной Windows-сессии):

```powershell
dotnet test --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowFlaUiTests/Feed*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
```

### Stop rules для validation

- `--list-tests` не считается passing evidence targeted selection.
- Headless/FlaUI/stateful suites запускаются последовательно.
- После timeout identical command не повторяется без progress/root-cause evidence и изменённой гипотезы.
- Restore/auth/SDK/lock failure фиксируется как environment blocker, а не маскируется изменением product code.
- Full gate должен завершиться green; skipped/blocked FlaUI требует objective reason и next-best screenshot/Headless evidence.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-01, AC-05 | Shell/Feed Headless + AppAutomation | Inspect top modes/day cards/files drawer | after screenshot | — |
| AC-02, AC-03 | EffectiveDate + DailyStorage + quick-capture Headless | Inspect created vault files | temp-vault test output | — |
| AC-04 | Parser golden/round-trip + Live Preview Headless | Compare source before/after unrelated edit | golden fixtures/diff | — |
| AC-06, AC-07, AC-08 | ReviewQueue mixed/nested + durable Session/Action tests + Headless | Inspect nested case and device-A defer → loss → device-B takeover/abandon → next-session return | Headless/session-event logs | — |
| AC-09 | Area move/locator-remap integration + Headless | Inspect resulting Markdown, active selection and pending state after exit | temp-vault/session artifact | — |
| AC-10, AC-11 | TaskConversion outcome/escaping + existing relation contracts + Headless | Verify multiple parents and no post-restart link candidate | task JSON + queue/Headless evidence | — |
| AC-12 | FeedTaskReference UI + status contract + AppAutomation | FlaUI bounds/screenshot | screenshot/video if available | — |
| AC-13 | NoteExtraction serializer/outcome integration + Headless | Inspect YAML/wiki-link with delimiter-heavy title and no repeated candidate | temp-vault artifact | — |
| AC-14 | Area schema + task classification round-trip/compatibility tests | Inspect task card chips/toggle и old-server disabled copy | serialized fixtures/capability log | — |
| AC-15 | SearchIndex unit + search Headless | Inspect context/sort/filter/return state | Headless evidence | — |
| AC-16 | Markdown + causal sidecar + identity-resolution integration | Inject clock skew/concurrent decisions; resolve divergent ID with revision race | both-branch bundle + watcher/merge/conflict log | — |
| AC-17 | Conflict/Draft/Revision/Transaction tests + `FileNoteVault` delete-race regression + Headless | Exercise three conflict actions and a pre-open external writer | recovery fixtures/safety quarantine path + screenshot | Tombstone cleanup is intentionally not automatic |
| AC-18 | Path/symlink/renderer + generated-link golden security tests | Inspect error copy and special-character output | test logs/golden Markdown | Symlink capability may be platform-gated but Windows path confinement remains tested |
| AC-19 | Desktop AppAutomation + shared solution build | Verify unsupported provider state | build/test logs | External mobile vault intentionally not implemented |
| AC-20 | Responsive Headless + FlaUI/AppAutomation | Compare wireframe, keyboard and bounds | screenshot/video fallback | — |
| AC-21 | Cancellation/lifecycle/perf contract tests | Inspect indexing progress and no stale callbacks | timing/lifecycle logs | Exact large-vault threshold measured, not hard-coded in product copy |
| AC-22 | MoveToToday integration + Headless review action | Inspect source `moved`, destination deferred-next-session and retry/restart queue | temp-vault artifact/journal log | — |
| AC-23 | FirstConnectBaseline + VaultIdentity/BootstrapSync + onboarding Headless | Seed mixed old/new blocks; race create/change/rename; simulate second device/partial sync/conflicting ID | manifests/batches + unchanged Markdown diff + Headless counts | — |

## 12. Риски и edge cases

- **Parser rewrite risk:** Markdown parser может нормализовать исходник. Mitigation: raw slices, block-level mutation и golden byte/line-ending tests.
- **Live Preview scope:** полноценный Obsidian WYSIWYG слишком широк. Mitigation: block Live Preview с safe raw fallback; output contract важнее конкретной package.
- **Duplicate blocks:** одинаковый текст затрудняет sidecar rematch. Mitigation: occurrence + neighbor anchors; ambiguity → pending.
- **Partial conversion:** task/note может создаться до source rewrite. Mitigation: idempotent operation journal, stable resulting ID и explicit recovery.
- **External sync races:** watcher events могут дублироваться/приходить не по порядку. Mitigation: revision hash, coalescing, operation correlation и re-read before mutation.
- **Late POSIX writer during delete:** внешняя программа может удерживать исходный file descriptor и дописать данные уже после revision check. Mitigation: verified file атомарно переносится в `.unlimotion/deleted/<operation-id>/<original-relative-path>` и не unlink-ается автоматически; обычный source path исчезает, но поздние байты остаются в recoverable safety quarantine. Детерминированный test проверяет pre-open writer и safe-conflict fallback платформы.
- **Clock skew/concurrent decisions:** wall-clock не задаёт happens-after. Mitigation: `DeviceId` + monotonic sequence + causal context; concurrent explicit conflicts сохраняются и становятся pending.
- **Identity resolution loss:** выбор одной `VaultId` branch может скрыть решения другой. Mitigation: immutable both-branch conflict bundle, revision recheck, safe-pending losing locators и delayed recovery-namespace cleanup.
- **Bootstrap sync/identity:** partial or concurrent sync может повторно скрыть новый block либо смешать две копии vault. Mitigation: immutable `VaultId`, append-only validated manifests, explicit-decision precedence, concurrent intersection и safe pending/conflict.
- **Generated Markdown injection:** title/path delimiters могут сломать task/wiki link. Mitigation: dedicated serializers, safe filename contract, fixed targets и golden special-character tests.
- **Broken wiki path:** external rename без Obsidian auto-update. Mitigation: stable note ID marker, dynamic resolution и explicit repair.
- **Mixed-version remote server:** old endpoint не понимает `IsGoal/AreaIds`, а old client может прислать whole-task update. Mitigation: capability version, nullable presence semantics, server preservation для old client и disabled unsafe edit на new-client/old-server.
- **Area rename ambiguity:** внешний H2 без marker совпадает с несколькими names. Mitigation: не угадывать; virtual `Без области` + choose area.
- **Vault relocation/offline:** configured root исчез. Mitigation: read-only unavailable state, drafts remain, Tasks mode works.
- **Large vault:** initial indexing/memory pressure. Mitigation: incremental/lazy index, cancellation, progress, quick capture independent from full index.
- **Unsafe links/HTML:** Markdown может содержать executable content. Mitigation: non-executing renderer and scheme allowlist.
- **Relation control extraction:** singleton `CurrentRelationEditor`, global focus и duplicate AutomationId. Mitigation: explicit target/context and instance-scoped IDs; existing behavior tests stay green.
- **Recursive parent remove:** existing tree permits operations на раскрытых ancestors. Mitigation: extraction preserves current contract; новые destructive semantics не добавляются этой SPEC.
- **Top-level TabControl collision:** existing tests выбирают первый TabControl. Mitigation: shell switch не обязан быть `TabControl`; tests target `MainTabs` by AutomationId.
- **Cross-platform compile:** shared Avalonia view видна всем targets, но filesystem provider desktop-only. Mitigation: provider capability/visibility contract and full solution build.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Снова слишком много вложенных окон» | Пользователь уже отметил перегруженность | Review и editors остаются inline; только два top-level режима; Files временный drawer | mitigated |
| «Статус задачи всё ещё непонятен» | В первом mockup status был postfix text | Реальный `TaskStatusPicker` обязателен слева; geometry AC/test | mitigated |
| «Зачем новый выбор родителя, если блок уже есть?» | Пользователь потребовал reuse | Existing block извлекается; несколько родителей/search/validation сохраняются | mitigated |
| «Unlimotion испортит мои Obsidian-файлы» | Vault — личный source of truth | Raw round-trip, minimal additive markers, atomic writes, revisions/conflicts | mitigated |
| «Полезная мысль не попадёт в разбор без метки» | Capture должен быть быстрым | В очередь входят все новые content blocks | mitigated |
| «Внешнее редактирование перезапишет мой текст» | Obsidian и Unlimotion работают с одними файлами | Expected hash + dirty conflict + preserve both/draft/revision | mitigated |
| «Почему в vault осталась удалённая копия?» | Поздний POSIX writer иначе может потерять дописанные байты | Пользователь выбрал durable `.unlimotion/deleted` quarantine с сохранённым исходным путём и без automatic cleanup | accepted-by-user D-02 |
| «Почему нет Android/Browser сразу?» | Unlimotion multiplatform | Desktop MVP назван явно; provider abstraction и shared build сохраняют будущий путь | accepted-risk for MVP |
| «Задача создастся дважды после сбоя» | Cross-store operation не атомарна | Journal хранит resulting task ID; retry идемпотентен | mitigated |

### Rework Prevention Checklist

- [x] SPEC называет все основные пользовательские действия и видимые состояния.
- [x] Каждый user-visible scenario имеет automated/manual evidence.
- [x] Agent/user decisions перечислены в Decision Ledger.
- [x] Вероятные objections перечислены и закрыты либо явно оставлены как MVP risk.
- [x] Role-based review предусмотрен для business workflow, UX, testing и architecture.
- [x] Acceptance criteria являются проверяемыми результатами, а не подготовительными шагами.
- [x] EXEC имеет команды и artifact paths для доказательства сценариев.

## 13. План выполнения

### Этап 1 — Contracts и characterization

- Зафиксировать/расширить существующие status/relation/navigation tests до рефакторинга UI.
- Добавить additive task fields, versioned capability/presence semantics и server-side old-client preservation во все local/remote contracts.
- Условие остановки: existing task tests, classification round-trip и mixed-version compatibility tests green; новый клиент не отправляет unsafe classification write старому server.

### Этап 2 — Notes core

- Создать `Unlimotion.Notes` project: paths, parser/raw blocks, atomic storage, portable vault identity/areas/review/bootstrap sidecars, отдельный sidecar watcher, app-local journal/revisions/drafts и index interfaces.
- Добавить golden fixtures и pure/integration tests.
- Условие остановки: round-trip/path/concurrency tests green; выбранная dependency прошла compatibility/license check.

### Этап 3 — Desktop vault и read-only Feed

- Добавить settings/onboarding/provider/watcher/index.
- Добавить shell switch и read-only virtualized chronology/Files/search.
- Условие остановки: existing task tabs сохраняют state; read-only Feed/rename/search Headless green.

### Этап 4 — Editing и quick capture

- Добавить block Live Preview, autosave, quick capture, draft recovery.
- Условие остановки: one-block edit не меняет соседний raw Markdown; write/conflict tests green.

### Этап 5 — Review и conversions

- Добавить queue/selection/actions.
- Извлечь reusable task relation control с explicit target; встроить status picker/task link.
- Добавить task/note/move transactions и recovery.
- Условие остановки: full review scenario Headless green, duplicate task retry test green.

### Этап 6 — Hardening и delivery evidence

- Добавить responsive/accessibility/FlaUI/AppAutomation coverage, performance/lifecycle checks.
- Обновить RU/EN docs и settings help.
- Запустить affected build, serial full gates, visual inspection и post-EXEC review.
- Условие остановки: все AC evidence собрано; blockers/high review findings отсутствуют.

## 14. Открытые вопросы

Блокирующих продуктовых вопросов нет.

### D-01 — First-connect review baseline — решено

Пользователь выбрал вариант 1: существующая обычная история считается просмотренной через отдельный `baseline-kept`, все незавершённые checkboxes попадают в первый разбор, completed checkboxes исключены, а всё новое/содержательно изменённое после bootstrap работает по обычным правилам. Atomic/resumable алгоритм, observable counts, AC-23 и test mapping зафиксированы.

- Конкретная Markdown parsing/rendering dependency является agent-owned implementation choice. До выбора обязательны license, `net10.0`, Avalonia desktop, safe rendering и raw round-trip checks; failure включает stop rule.
- Поддержка внешнего vault на Android/iOS/Browser требует отдельных provider/platform SPEC и evidence и не блокирует desktop MVP.
- Mixed-version remote classification защищена capability/presence contract; внешний old server не получает unsafe write от нового клиента.

## 15. Соответствие профилю

- Профиль: `dotnet-desktop-client`
- Overlay: `product-system-design`
- Выполненные требования:
  - UI thread не используется для blocking IO/indexing.
  - Navigation, state restoration, errors и conflicts описаны.
  - Существующие task status/relation contracts переиспользуются.
  - Architecture/data/API/compatibility/security boundaries зафиксированы.
  - Visual planning artifact доступен прямо в SPEC; local-only render помечен.
  - UI tests обязательны по локальному override; TUnit commands используют `--treenode-filter`.
  - Rollout/rollback и unsupported platform boundary заданы.

## 16. Таблица изменений файлов

Точные имена новых leaf-файлов могут уточняться внутри указанных ownership boundaries без изменения публичного контракта.

| Файл / область | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Notes/**` (new project) | Vault/domain/parser/index/review/transaction contracts | Изоляция Markdown subsystem |
| `src/Unlimotion.sln`, project references, central packages | Подключить new project и выбранную parser dependency | Build composition |
| `src/Unlimotion.Domain/TaskItem.cs` | `IsGoal`, `AreaIds` | Task/goal/area contract |
| `src/Unlimotion.Interface/TaskItemHubMold.cs` | Additive fields | Hub sync |
| `src/Unlimotion.Interface/ReceiveTaskItem.cs` | Additive fields | Client receive/update contract |
| `src/Unlimotion.Interface/IChatHub.cs` и capability contract | Запрос `TaskClassificationSchemaVersion` и nullable presence semantics | Mixed-version negotiation |
| `src/Unlimotion.Server.ServiceModel/Molds/Tasks/TaskItemMold.cs` | Additive persisted/service fields | Server round-trip |
| `src/Unlimotion/AppModelMapping.cs`, `src/Unlimotion.Server/AppModelMapping.cs` | Client/server mapping новых optional fields | Полный round-trip |
| `src/Unlimotion.Server/hubs/ChatHub.cs` | Capability endpoint и preserve-on-absent update semantics | Старый клиент не стирает classification |
| `src/Unlimotion.ViewModel/Feed/**` | `FeedViewModel`, block/search/review view models | Не раздувать `MainWindowViewModel` |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` и settings models | Vault root/day boundary/provider state | Configuration |
| `src/Unlimotion/Views/MainScreen.axaml(.cs)` | Top-level modes и hosting | Shell boundary |
| `src/Unlimotion/Views/FeedControl.axaml(.cs)` | Feed UI | Новый режим |
| `src/Unlimotion/Views/TaskRelationsControl.axaml(.cs)` | Extract reusable relation block | User-required reuse |
| `src/Unlimotion/Views/MainControl.axaml(.cs)` | Подключить extracted relation control без behavior drift | Shared task card block |
| `src/Unlimotion.ViewModel/TaskRelationEditorViewModel.cs` | Explicit target/context и instance focus contract | Reuse вне global current task |
| `src/Unlimotion/TaskStatusPicker.cs` | Только необходимые instance/automation hooks, без новой status logic | Живая ссылка |
| `src/Unlimotion/Views/FeedDocumentConflictControl.axaml(.cs)` | Markdown conflict UI | Safe external edit |
| `src/Unlimotion/Services/**` / app composition | Desktop vault provider, Markdown/sidecar watchers, app-local recovery storage, lifecycle, DI | Runtime integration и разделение portable/local state |
| `src/Unlimotion.ViewModel/Resources/Strings*.resx` | RU/EN UI strings | Localization |
| `src/Unlimotion.Test/**Feed*Tests.cs` + fixtures | Unit/integration/Headless coverage | AC evidence |
| `tests/Unlimotion.UiTests.Authoring/**` | Page objects/shared Feed scenarios | Shared automation |
| `tests/Unlimotion.UiTests.Headless/**` | Headless end-to-end | Mandatory UI tests |
| `tests/Unlimotion.UiTests.FlaUI/**` | UIA/geometry flow | Desktop visual evidence |
| `README.md`, `README.RU.md` | User-facing mode/settings/platform boundary | Documentation during EXEC |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Capture | Obsidian отдельно | Quick capture в daily Markdown через Ленту |
| Просмотр | Ручная прокрутка файлов | Виртуализированная chronological Feed + Files drawer |
| Разбор | Ручной перенос | One-block inline review с изменяемым selection |
| Задача из записи | Ручное создание/копирование | Existing TaskItem + live source link |
| Родители | Existing card block | Тот же extracted block в card и Feed, multiple parents |
| Статус в ссылке | Отсутствует | Existing picker слева от актуального title |
| Постоянная заметка | Ручной cut/paste/link | Atomic extraction + YAML ID + wiki-link |
| Области | Отсутствуют в task model | Stable tree; daily primary; task/note multiple |
| Цель | Отдельного contract нет | Bool role обычной task |
| Поиск | Только tasks/projections | Block search по daily/notes/tasks |
| External edit | Нет note conflict contract | Watch/reload или explicit three-choice conflict |
| Платформы | Общий task UI | External vault desktop MVP; capability boundary для остальных |

## 18. Альтернативы и компромиссы

### Добавить Ленту как одиннадцатый task TabItem

- Плюсы: меньше shell changes.
- Минусы: смешивает capture с task projections, ломает semantic boundary и рискованно взаимодействует с responsive task tab overflow.
- Выбор: top-level shell mode в `MainScreen`.

### Хранить заметки в JSON/database Unlimotion

- Плюсы: проще stable IDs и transactions.
- Минусы: ломает прямой Obsidian workflow, portability и ownership пользователя.
- Выбор: Markdown source of truth + минимальные sidecars.

### Полный WYSIWYG editor

- Плюсы: визуально ближе к reading mode.
- Минусы: высокий риск потери unsupported Markdown и существенно больший scope.
- Выбор: block Live Preview с raw active block/fallback.

### Hidden ID в каждом block

- Плюсы: простой устойчивый review locator.
- Минусы: массово загрязняет пользовательские daily files.
- Выбор: sidecar fingerprint/anchors, ambiguity → pending.

### Новый compact parent field

- Плюсы: проще initial implementation.
- Минусы: дублирует behavior, теряет multiple parents/validation и прямо противоречит решению пользователя.
- Выбор: extract/reuse existing relation block.

### Неперсистентный task draft с накоплением parent IDs

- Плюсы: единый final commit.
- Минусы: existing relation editor требует ID и немедленные storage mutations; потребуется новый transactional relation layer.
- Выбор: task сохраняется до показа relation block; source update journaled/idempotent.

### Синхронный поиск без индекса

- Плюсы: нет cache/index state.
- Минусы: блокирует UI на большом vault и повторно читает файлы.
- Выбор: incremental rebuildable index.

### Один MVP сразу для всех платформ

- Плюсы: единый marketing contract.
- Минусы: browser/iOS/Android имеют разные filesystem/persistent permission guarantees, не подтверждённые текущим кодом.
- Выбор: desktop-first provider abstraction и честно disabled unsupported state.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Проверка | Статус | Комментарий |
|---:|---|---|---|
| 1 | Цель | PASS | Root outcome и пользовательская ценность определены. |
| 2 | AS-IS | PASS | Реальный Obsidian workflow и текущие repo contracts зафиксированы. |
| 3 | Проблема | PASS | Разрыв capture → processing → retrieval сформулирован. |
| 4 | Design goals | PASS | Измеримые продуктовые и технические цели перечислены. |
| 5 | Non-Goals | PASS | Mobile vault, AI, attachments и full WYSIWYG исключены из MVP. |
| 6 | Responsibilities | PASS | Shell, Notes core, watchers, task reuse и recovery разделены. |
| 7 | Integration | PASS | Startup/settings/shell/task server/watcher triggers описаны. |
| 8 | Business rules | PASS | Review, selection, conversions, move и stable references заданы. |
| 9 | Error handling | PASS | Dirty conflicts, partial failures, drafts и recovery имеют safe outcomes. |
| 10 | Performance | PASS | Virtualization, incremental index, cancellation и lifecycle заданы. |
| 11 | Data contracts | PASS | Markdown, sidecars, app-local state и task capability versioning описаны. |
| 12 | Migration | PASS | Additive fields и existing-vault preservation определены. |
| 13 | Rollback | PASS | Markdown остаётся читаемым; sidecars/recovery/server downgrade ограничены. |
| 14 | Acceptance Criteria | PASS | 23 AC, включая atomic first-connect baseline, проверяемы. |
| 15 | Test plan | PASS | Все contracts mapped на unit/integration/Headless/FlaUI/manual evidence. |
| 16 | Validation commands | PASS | Repo-proven serial TUnit/Headless/FlaUI commands заданы. |
| 17 | Execution plan | PASS | Large scope разбит на stages со stop conditions. |
| 18 | Open questions | PASS | D-01 решён пользователем; implementation-owned dependency имеет stop rule. |
| 19 | Scale / risk | PASS | Scale `large`, security/data-loss/platform risks и mitigations явны. |
| 20 | Profile compliance | PASS | `dotnet-desktop-client`, product overlay и mandatory UI tests отражены. |

Итог: `ГОТОВО` к финальному post-SPEC review; EXEC всё ещё запрещён до exact approval phrase.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна root problem, desktop MVP и Non-Goals явны. |
| 2. Понимание текущего состояния | 5 | Текущие shell/task/search/watcher/relation/test contracts сверены с кодом. |
| 3. Конкретность целевого дизайна | 5 | UI, storage schemas, flows, atomicity и states описаны. |
| 4. Безопасность (миграция, откат) | 5 | Markdown-preserving writes, conflicts, journal, revisions и rollback заданы. |
| 5. Тестируемость | 5 | AC-to-test mapping и repo-proven TUnit/UI commands полны. |
| 6. Готовность к автономной реализации | 5 | Blocking decisions закрыты; large scope разбит на stages с проверяемыми stop gates. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению после Pre-Approval

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Сохраняет ли flow быстрый capture → review → task/note → retrieval? | PASS | First-connect baseline не создаёт исторический backlog и не скрывает unfinished checkboxes |
| UX / designer | applicable | Не перегружен ли UI и предсказуем ли первый запуск? | PASS | Inline review/two modes приняты; onboarding показывает bootstrap counts |
| Tester / validation | applicable | Каждый AC имеет test/check/evidence и edge coverage? | PASS | Baseline/sync, causal sessions, conversions, conflicts и UI geometry mapped |
| Developer / architect | applicable | Связны ли boundaries, migrations, atomicity и reuse? | PASS | Notes boundaries, identity/bootstrap, causal review, relations и journals зафиксированы |
| Delivery / operations / security | partially applicable | Есть ли external path/data/rollback risks? | PASS | Path confinement, non-executing renderer, local-only content и rollback включены |

### Post-SPEC Review

- Статус: `PASS`.
- Scope reviewed: `specs/2026-08-24-daily-feed-mode.md`; central QUEST stack; profiles `dotnet-desktop-client` + `product-system-design`; текущие task/relation/status/search/watcher/test contracts; local-only visual artifacts.
- Reviewer boundary: отдельный reviewer role выполнил несколько фактически read-only adversarial passes (`Get-Content`/`rg`/hash only, без edits/build/tests), но effective sandbox оставался `workspace-write`. Поэтому evidence дисциплинарно независимое по роли и содержанию, но не sandbox-enforced read-only; ограничение явно сохраняется как residual.
- Decision: D-01 закрыт вариантом 1; после fix-and-re-review отсутствуют BLOCKER/HIGH/MEDIUM findings, SPEC готова к отдельному Pre-Approval.
- Review passes:
  - Scope/Evidence pass: shell, task status/relations, mappings, watcher и test layers сверены с репозиторием.
  - Contract pass: требования интервью отражены; task/note/move source replacement теперь относится ко всему selection.
  - Adversarial risk pass: проверены partial conversion, duplicate blocks, mixed-version writes, portable/local state, unsafe paths, platform mismatch, bootstrap races, causal merge, identity recovery и orphan sessions.
  - Role-Based pass: business/UX/testing/architecture/security verdicts `PASS`.
  - Re-review after fixes: проведены повторные passes после каждого набора findings; финальный verdict `PASS` без BLOCKER/HIGH/MEDIUM.
  - Stop decision: Pre-Approval gate открыт; EXEC запрещён до exact phrase.
- Evidence inspected:
  - `MainScreen.axaml`, `MainControl.axaml(.cs)`, `MainWindowViewModel.cs`, `TaskItem.cs`, task molds/mapping и `ChatHub.SaveTask` behavior;
  - `TaskStatusPicker`, `TaskRelationEditorViewModel`, relation/status/navigation specs/tests;
  - `FileDbWatcher`, search contracts, Headless/FlaUI/AppAutomation test layers;
  - interactive mockup и inspected PNG.
- Depth checklist:
  - Scope drift / unrelated changes: только текущая SPEC изменена; existing untracked `.codex-remote-attachments/` и `output/` не относятся к SPEC mutation.
  - Acceptance criteria: 23 outcomes mapped, включая first-connect baseline, causal review sessions и identity recovery.
  - User-observable scenarios / Decision ledger / Expected objections: заполнены; blocking product decisions отсутствуют.
  - Validation evidence: текущий этап docs-only; content/structure/path/whitespace/status checks выполняются без build/test.
  - Unsupported claims: mobile/browser support явно исключена; visual render помечен local-only.
  - Regression / edge case: task tabs, relations/status, watcher lifecycle, conflicts, mixed-version round-trip, nested checkboxes, idempotent moves, cross-device bootstrap/session/identity включены.
  - Comments/docs/changelog: implementation docs planned; CHANGELOG impact решается в EXEC по repo policy.
  - Hidden contract change: task fields/capability API и desktop platform boundary названы явно.
  - Manual-review challenge: portable sidecars отделены от app-local recovery; source replacement и retry semantics явны.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | Product / first run | Не было определено, считать ли existing history pending при первом подключении | Вариант 1; atomic `baseline-kept`, unfinished pending, AC-23/tests | fixed |
| HIGH | Task conversion | Формулировка могла оставить часть selection рядом с task link | Заменять всё contiguous selection ровно одной live link | fixed |
| HIGH | Remote compatibility | Old-client full update мог стереть `IsGoal/AreaIds` | Capability/version/presence contract + server preservation + compatibility tests | fixed |
| HIGH | Move to today | Не было полного atomic/idempotent flow и source-link contract | Destination-first journal, stable anchor, recovery, AC-22/tests | fixed |
| MEDIUM | Sidecar ownership | `.unlimotion` назывался portable, но watcher исключал его целиком | Отдельный sidecar watcher; app-local recovery вынесен из vault | fixed |
| MEDIUM | Bootstrap/identity | Не были заданы stable identity, second-device, start snapshot и partial/concurrent sync | `VaultId`, validated append-only manifests, immutable snapshot, safe intersection/pending и tests | fixed |
| MEDIUM | Review outcomes | Input/output links и Move destination могли немедленно вернуться в queue | Input/output decisions, source terminal state, destination causal defer и restart tests | fixed |
| MEDIUM | Candidate/serializer safety | Nested checkboxes и delimiter-heavy generated links были неоднозначны | Per-task-item candidates, coverage rules, dedicated serializers/golden tests | fixed |
| MEDIUM | Causal session/merge | Timestamp ordering, Assign Area и orphan session recovery были недоопределены | Causal event envelope, intermediate area remap, takeover/abandon и conflict tests | fixed |
| MEDIUM | Identity resolution | Detection divergent `VaultId` не сохраняла losing branch/recovery state | Both-branch bundle, three actions, revision checks, safe-pending decisions | fixed |
| INFO | Review isolation | Reviewer effective sandbox был `workspace-write`, хотя фактически использовались только read-only commands | Не выдавать evidence за sandbox-enforced read-only; сохранить residual disclosure | accepted-residual |
| LOW | Spec lint | Aggregate linter скрывал незакрытый first-run contract | Развернуть все 20 пунктов и выставить `ASK-HUMAN` | fixed |

- Fixed before continuing: все объективные product/architecture/data-loss/test findings встроены в design, algorithms, AC и test matrix.
- Checks rerun: reviewer verdict `PASS`; финальные docs-only checks перечислены в журнале; code build/tests намеренно не запускаются в SPEC.
- Needs human: отдельная exact approval phrase для перехода в EXEC.
- Residual risks / follow-ups: parser dependency остаётся agent-owned EXEC choice под stop rule; reviewer sandbox isolation не была технически enforced, что не скрывается в evidence.

### Post-EXEC Review

- Статус: `PASS` после реализации, исправлений и повторной проверки.
- Scope reviewed: desktop MVP режима `Лента`, Markdown vault/review/recovery pipeline, additive task contracts `IsGoal`/`AreaIds`, shell/settings/task integration, RU/EN resources, unit/Headless/FlaUI coverage и visual evidence.
- Decision: открытых BLOCKER/HIGH/MEDIUM findings нет; один LOW по строгой изоляции будущих unattended screenshots принят как follow-up. Реализация соответствует подтверждённой SPEC в заявленной desktop-first границе.
- Review passes:
  - основной adversarial post-EXEC pass проверил source-of-truth, atomic writes, rollback/recovery, markerless H2, task/note/move transactions, mixed-version task mapping и terminal task-link behavior;
  - повторный pass после исправлений подтвердил atomic existing-file replace и rollback для исходного/целевого файла, сохранение recovery draft в `Use disk`, stable-only area persistence и отсутствие повторных действий у terminal task links;
  - отдельный узкий pass проверил UI Automation groups, desktop launch без вложенной build-race и тестовый screenshot helper;
  - финальный adversarial pass проверил краткое day accessibility name, stable day ID, отсутствие raw Markdown в ListBoxItem UIA name, opt-in DPI setup и визуально inspected capture.
- Evidence inspected: production/test diffs, targeted unit suites, полный serial Headless run, Feed FlaUI scenarios, scoped Desktop/Server builds, локализация и inspected real Desktop screenshot `chat-artifacts/unlimotion-feed-real-desktop.png`.
- Depth checklist: сохранены unknown Markdown/YAML/task fields; операции ограничены vault root; conflicts не перезаписывают данные молча; source replacement и retry idempotency покрыты; status icon визуально и через UIA находится слева от task title; overlays не блокируют основной UI в закрытом состоянии.
- No-findings justification: найденные в review дефекты исправлялись до повторного review; финальный adversarial verdict — `PASS` с одним принятым LOW. Effective sandbox reviewer был `workspace-write`, но фактическая проверка оставалась read-only.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | storage atomicity | Existing destination мог быть заменён до гарантированного durable backup/rollback | Ввести replace/backup transaction и recovery для каждой mutation branch | fixed |
| HIGH | recovery | `Use disk` мог потерять dirty editor draft | Сохранять losing draft в recovery namespace до принятия disk revision | fixed |
| MEDIUM | areas | Markerless H2 смешивал физическое Markdown-направление и catalog classification | Разделить physical destination и stable area identity; сохранять только однозначное active mapping | fixed |
| MEDIUM | task links | Terminal task reference допускал повторные conversion actions и неполные goal/area surfaces | Ввести terminal guards и единый reusable task/relation behavior | fixed |
| MEDIUM | rollback race | Partial source/destination failure оставлял окно между backup и journal recovery | Усилить B/C/D rollback branches и добавить regression tests | fixed |
| LOW | desktop automation | Вложенная FlaUI build могла конкурировать за Fody outputs; mode/review containers были слабо видимы в UIA | Запускать уже собранный TestHost и обозначить контейнеры как automation groups | fixed |
| MEDIUM | accessibility / automation | UIA name day card содержало полный raw Markdown, YAML и markers | Ввести concise date name и stable `FeedDay-YYYYMMDD`; marker проверять через vault, не через accessibility name | fixed |
| LOW | screenshot isolation | FlaUI `Capture.Element` делает desktop BitBlt по bounds и теоретически может захватить чужой overlay | Текущий PNG inspected и чистый; для future unattended/published capture при необходимости добавить fail-closed `PrintWindow/PW_RENDERFULLCONTENT` | accepted-follow-up |

- Fixed before final report: все objective post-EXEC findings выше исправлены; повторные reviewer passes вернули `PASS`.
- Checks rerun:
  - `DailyMarkdownStorageTests`: `12/12`;
  - полный Headless Release: `44/44`, skipped `0`; финальный Feed subset: `7/7`;
  - Feed FlaUI scenarios: `6/6`; отдельный opt-in screenshot scenario: `1/1`;
  - full unit serial gate: `1080/1081`, единственный stateful Avalonia Roadmap failure повторно прошёл изолированно `1/1`;
  - Desktop, Server, Headless и unit test project Release builds: успешно.
- Validation evidence: RU/EN resource keys `685/685`; реальный Desktop PNG `1252×1921` визуально проверен и содержит только окно приложения; `git diff --check` выполняется в final delivery gate.
- Unrelated changes: существующие пользовательские `.codex-remote-attachments/`, `output/` и `specs/2026-08-24-fdroid-dotnet-support.md` не изменялись и не удалялись.
- Needs human: для реализации больше нет; commit/push/PR/merge/release/deploy не разрешены этой approval phrase и не выполнялись.
- Residual risks / follow-ups: полная `src/Unlimotion.sln` в текущей среде блокируется `NETSDK1147` из-за отсутствующих Android/wasm workloads; desktop/shared affected projects и обязательные UI gates прошли. Будущий строго изолированный unattended screenshot должен использовать `PrintWindow/PW_RENDERFULLCONTENT` без desktop-capture fallback.

## Approval

SPEC подтверждена пользователем точной фразой `Спеку подтверждаю` 2026-08-24. EXEC разрешён в границах этой SPEC; внешняя доставка/release остаётся отдельным approval boundary.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Product discovery | 0.98 | Нет по core workflow | Оформить SPEC | Да, после quality gate | Пользователь поэтапно подтвердил capture/review/selection/areas/task/note/search/conflict и попросил SPEC | Сначала сохранён реальный workflow, затем формализован дизайн | Концепт, local-only mockup/PNG |
| SPEC | Instruction stack | 0.98 | Нет | Применить canonical template/gates | Нет | Нет | Выбраны `dotnet-desktop-client`, `product-system-design`, `testing-dotnet`, creator-vibe lens/skill и local UI-test override | Эта SPEC |
| SPEC | Repo/evidence inventory | 0.95 | Конкретная Markdown dependency не выбрана | Спроектировать boundaries и stop rule | Нет | Нет | Проверены shell, task model/status/relations, search/watcher, server molds и test runners; три read-only explorer lanes | Эта SPEC; production files не менялись |
| SPEC | Draft authoring | 0.9 | First-run baseline D-01 | Получить ограниченный product choice | Да | Пользователь выбрал вариант `1` | Создан large SPEC с visual/data/API/test contracts; objective reviewer findings исправлены | `specs/2026-08-24-daily-feed-mode.md` |
| SPEC | Adversarial review | 0.94 | Strict read-only repeat pass | Дополнить AC/test и повторить review | Нет | Пользователь закрыл D-01 вариантом 1 | Fallback reviewer нашёл full-selection, mixed-version, move, sidecar и bootstrap gaps; исправления внесены, reviewer sandbox не выдаётся за независимый | `specs/2026-08-24-daily-feed-mode.md` |
| SPEC | First-connect decision | 1.0 | Нет | Провести финальный post-SPEC review | Нет | Пользователь ответил `1` | Existing ordinary blocks получают `baseline-kept`; unfinished checkboxes pending; new/changed blocks обрабатываются обычно | `specs/2026-08-24-daily-feed-mode.md` |
| SPEC | Final adversarial re-review | 0.99 | Sandbox-enforced read-only недоступен | Открыть Pre-Approval gate | Да | Нет | После fixes bootstrap/identity/outcomes/nested lists/serializers/causal sessions/orphan recovery финальный reviewer verdict `PASS`; фактические команды read-only, effective sandbox `workspace-write` | `specs/2026-08-24-daily-feed-mode.md` |
| SPEC | Docs-only validation | 0.99 | Нет | Запросить exact approval phrase | Да | Нет | 1161 lines; 28 fences balanced; trailing whitespace/tabs 0; missing evidence paths 0; status содержит только новую SPEC и ранее существовавшие untracked artifact directories; build/tests не запускались на SPEC-фазе | `specs/2026-08-24-daily-feed-mode.md` |
| EXEC | Pre-Approval | 1.0 | Нет | Начать staged implementation | Нет | Пользователь написал exact `Спеку подтверждаю` | Разрешение относится к реализации SPEC, но не к release/push/deploy | Эта SPEC и planned production/test files |
| EXEC | Baseline build | 0.99 | В среде нет Android/wasm workloads | Использовать desktop/shared builds; full solution повторить только при наличии workloads | Нет | Нет | `dotnet build src/Unlimotion.sln -c Release` остановился на NETSDK1147/Mobile AOT SDK resolution до feature compilation; это environment baseline blocker | Build evidence, production files не изменены этим command |
| EXEC | Contracts и local-first storage | 0.96 | Нет | Интегрировать Feed VM/UI | Нет | Нет | Добавлены additive `IsGoal`/`AreaIds`, vault identity/bootstrap, daily Markdown parser/storage, causal review events, revisions/drafts/journals и recovery | `src/Unlimotion.Notes/**`, domain/interface/server mappings, unit tests |
| EXEC | Feed UI и task integration | 0.96 | Нет | Добавить end-to-end UI evidence | Нет | Нет | Реализованы `Лента/Задачи`, day cards, quick capture, Live Preview, search/review, multiple areas, note/task/move actions, reusable parent relations и status-left task reference | `FeedControl`, `FeedViewModel`, shell/settings/task controls, resources |
| EXEC | Adversarial hardening | 0.99 | Нет | Повторить targeted tests/review | Нет | Нет | Review findings по atomic replace, losing draft, markerless H2, terminal links и rollback race исправлены до финального PASS | Notes mutations/recovery, Feed/task guards, regression tests |
| EXEC | Automated validation | 0.99 | Только mobile workloads для full solution | Завершить visual evidence и delivery audit | Нет | Нет | Headless `44/44` + final Feed `7/7`; Feed FlaUI `6/6`; storage `12/12`; scoped Release builds green; serial unit gate `1080/1081`, isolated Roadmap rerun `1/1`; full solution blocked только NETSDK1147 | Test reports/build logs |
| EXEC | Visual acceptance | 1.0 | Нет | Закрыть post-EXEC gate | Нет | Нет | Реальный Desktop capture визуально проверен: task status control расположен слева от title; артефакт не предназначен для commit | `chat-artifacts/unlimotion-feed-real-desktop.png` |
| EXEC | Post-EXEC Review | 1.0 | Нет | Передать результат пользователю без внешней доставки | Нет | Нет | Основной и финальный adversarial review вернули `PASS`; BLOCKER/HIGH/MEDIUM отсутствуют, один screenshot-isolation LOW принят как follow-up; effective reviewer sandbox был workspace-write, без edits/tests | Эта SPEC, production/test diffs, screenshot evidence |
| EXEC | Follow-up review: delete safety | 1.0 | Локально отсутствует stable SDK 10.0.400 | Commit/push и дождаться authoritative CI | Да | Пользователь выбрал вариант `1` | Portable safety quarantine `deleted/` включена в contract; independent re-review `PASS`; pre-open writer regression добавлен, а local TUnit остановлен до discovery из-за SDK | Эта SPEC, `FileNoteVault`, `DailyMarkdownStorageTests` |
