# UX-доработка режима «Лента» и общих действий Unlimotion

## 0. Метаданные

- Тип (профиль): `dotnet-desktop-client`
- Overlay profile: `ui-automation-testing`
- Контексты: `testing-dotnet`; targeted session insights; локальный `AGENTS.override.md`
- Владелец: Unlimotion desktop shell, Feed UI и Markdown block workflow
- Масштаб: `large`
- Целевое семейство / behavior baseline: `GPT-5.6`; относится только к QUEST-процессу и не меняет runtime продукта
- Поверхность: Work / Codex
- Effective runtime: Codex desktop; точный model ID текущей сессии не предоставлен и не влияет на продуктовый контракт
- Eval baseline / evidence:
  - текущая реализация ветки `feat/daily-feed` на коммите `956e2b44`;
  - текущий desktop screenshot `chat-artifacts/unlimotion-feed-real-desktop.png` (`local-only`, untracked);
  - согласованный интерактивный макет `C:\Users\Kibnet\.codex\visualizations\2026\07\31\019fb9e3-bc7c-7393-8212-cd0f74a766e4\unlimotion-feed-ux.html` (`local-only`);
  - durable wireframes и user-observable scenarios в этой SPEC.
- Целевой релиз / ветка: текущая `feat/daily-feed`, upstream `origin/feat/daily-feed`; реализация только после отдельного подтверждения SPEC
- Ограничения:
  - До фразы пользователя `Спеку подтверждаю` изменяется только эта рабочая SPEC.
  - Существующие Markdown-файлы, task storage, causal review state, operation journals, revisions и conflict flow остаются source of truth.
  - Все UI-facing изменения сопровождаются Avalonia.Headless/AppAutomation coverage; desktop flow дополнительно проверяется FlaUI и визуальным evidence.
  - Существующие automation IDs сохраняются, когда смысл контрола не меняется; намеренные selector migrations перечислены в §6.2.11.
  - Горячие клавиши работают внутри активного окна Unlimotion; системный global hotkey вне приложения не входит в задачу.
- Связанные ссылки:
  - `specs/2026-08-24-daily-feed-mode.md`
  - `specs/2026-08-25-daily-note-filename-format.md`
  - `specs/2026-08-25-area-creation-localization.md`
  - `src/Unlimotion/Views/MainScreen.axaml`
  - `src/Unlimotion/Views/FeedControl.axaml`
  - `src/Unlimotion/Views/MarkdownBlockLivePreviewEditor.axaml`
  - `src/Unlimotion.ViewModel/Feed/FeedViewModel.cs`
  - `src/Unlimotion.Notes/Markdown/MarkdownMutationService.cs`
  - `src/Unlimotion/Views/TaskRelationsControl.axaml`

## 1. Overview / Цель

Перестроить пользовательский слой уже реализованной Ленты так, чтобы ежедневная работа ощущалась как один непрерывный поток: быстро записать из любого режима, читать и редактировать хронологию без обрезания контента, при необходимости разобрать блоки и только затем добавлять структуру.

Outcome contract:

- Success means:
  - общий app bar полезен и в `Ленте`, и в `Задачах`;
  - quick capture, review и Settings доступны из любого состояния приложения без перехода в специальный режим;
  - Лента имеет реальную вертикальную прокрутку и автоматически подгружает старые дни без скачка позиции;
  - review показывает выбранный текст, даёт предыдущий/следующий элемент, меняет область и после финального решения открывает следующий блок;
  - редактирование текста не меняет геометрию блока при входе в edit mode и не показывает рамку TextBox;
  - один или несколько выбранных Markdown-блоков можно атомарно перемещать drag-and-drop внутри одного daily-файла;
  - задача может быть создана прямо в daily-note без ввода Markdown checkbox и после создания использует существующий status/goal/areas/parents UI.
- Итоговый артефакт / output: обновлённый desktop UX Ленты и shell, новые безопасные операции quick-task capture и multi-block move, локализация, unit/Headless/AppAutomation/FlaUI coverage и проверенные до/после visual artifacts.
- Stop rules:
  - не начинать EXEC до exact `Спеку подтверждаю`;
  - остановить реализацию, если для UX требуется обход optimistic revision check, causal review state или operation journal;
  - не создавать второй task relation picker, отдельную task entity или параллельный Settings screen;
  - не завершать EXEC без green targeted tests, affected builds, serial full unit + Headless gates и post-EXEC review;
  - если desktop video capture объективно недоступен, зафиксировать причину и предоставить passing FlaUI/Headless + inspected screenshots как fallback.

## 2. Текущее состояние (AS-IS)

- `MainScreen.axaml` содержит отдельную малополезную строку только с `Лента | Задачи`; task create, task search и Settings живут внутри `MainControl`, а Feed actions — внутри `FeedControl`.
- Settings остаётся последней вкладкой task `TabControl`, поэтому из Ленты к нему можно попасть только через переключение в `Задачи`.
- Quick capture — крупная постоянная карточка в верхней части Feed. Она занимает первый экран и доступна только в режиме Ленты.
- Quick capture сохраняет raw Markdown, но не умеет создать обычную task и вставить live task link без ручного `- [ ]`.
- `FeedControl` использует виртуализированный `ListBox`, однако текущая компоновка не гарантирует видимый scroll viewport. Старые дни загружаются отдельной кнопкой, а `LoadOlderDaysCoreAsync` перестраивает весь snapshot от нулевой страницы, что создаёт риск скачка позиции.
- Feed search, review banner, review panel и chronology находятся в одном вертикальном layout. Review panel показывает automation-only `FeedReviewSelectionText` размером `1×1`, но пользователь не видит выбранный Markdown.
- Review уже поддерживает causal session, area assignment, task/note conversion, move-to-today и автоматическое продвижение для большинства решений. Нет явной previous/next navigation; после создания task требуется отдельный `Продолжить` из перегруженной панели.
- `FeedReviewAreaPicker` и кнопка `Назначить` существуют, но теряются среди expand/shrink, leave/skip/move, task и note controls.
- `MarkdownBlockLivePreviewEditor` заменяет preview на обычный TextBox. Из-за разных padding/border/measure блок визуально прыгает, а focus frame выглядит как отдельное поле ввода.
- Markdown blocks не имеют selection controller и drag-and-drop. `MarkdownBlockSelection` поддерживает только непрерывный диапазон и используется review-операциями.
- `FeedAreaAssignmentService` умеет безопасно переносить непрерывную selection между H2 areas с optimistic revision check; общей операции произвольного multi-block reorder пока нет.
- Созданная из review задача уже показывает реальный `TaskStatusPicker` слева, `IsGoal`, `AreaIds` и переиспользованный `TaskRelationsControl`.
- В репозитории есть TUnit unit/Headless tests, общий AppAutomation authoring layer, Headless и FlaUI runners, а также window recording script.

## 3. Проблема

Функционально Лента умеет хранить, искать и разбирать daily-notes, но интерфейс организован как набор отдельных административных панелей. Это замедляет главный сценарий «быстро записать → продолжить работу → разобрать позже», прячет контекст review и делает редактирование/перемещение блоков менее прямым, чем работа с обычной ежедневной заметкой.

## 4. Цели дизайна

- Сделать захват мысли доступным за одну команду из любого режима без предварительной классификации.
- Сохранить два постоянных рабочих режима: `Лента` и `Задачи`; quick capture, review и Settings являются временными поверхностями.
- Перенести app-wide actions в общий shell и оставить в Feed только контекстные действия daily-notes.
- Показать выбранный review fragment до решения и раскрывать task/note параметры только после выбора типа действия.
- Переиспользовать существующие Feed operations, search index, task status/classification/relations и SettingsControl.
- Выполнять каждую Markdown mutation атомарно с проверкой revision и без silent overwrite.
- Сохранить стабильный focus, keyboard alternative и automation contract для всех новых mouse interactions.
- Не блокировать UI thread при search, paging, task capture или block move.

## 5. Non-Goals (чего НЕ делаем)

- Не меняем Markdown parser, daily filename format, vault identity, sidecar schema, causal merge protocol или supported-platform boundary без отдельной необходимости, обнаруженной на EXEC.
- Не добавляем OS-wide/system global hotkeys, background tray capture или mobile share target.
- Не превращаем Settings в третий workspace mode и не создаём второй SettingsViewModel.
- Не заменяем local task filters единым search result screen; общий search остаётся additive app-wide lookup.
- Не реализуем drag-and-drop между разными daily-файлами, тематическими notes и задачами. Междневный перенос остаётся явным review action `На сегодня`.
- Не разрешаем перемещение frontmatter, H1/H2 area headings, generated terminal links или recovery blocks как обычного content selection.
- Не добавляем kanban, rich-text WYSIWYG, AI classification, автосуммаризацию или автоматическое назначение родителей.
- Не меняем task relation semantics и не ограничиваем количество родителей.
- Не удаляем domain-команду `Deferred`; если она не является primary action, она может остаться в overflow/keyboard flow и causal state.
- Не коммитим local-only screenshots/videos без отдельного repository policy или запроса пользователя.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент / файл | Ответственность |
| --- | --- |
| `MainScreen.axaml(.cs)` | Общий app bar, global hotkeys, create/search/review/settings entry points и shell-level overlays. |
| `MainWindowViewModel` | Shell commands/state, возврат в предыдущий mode после overlay, task navigation и app-wide search routing. |
| `FeedViewModel` quick-capture state | Временное note/task capture state, area, task conversion result, busy/error и сохранение черновика в памяти до успешной записи. |
| `MainScreen` quick-capture overlay | Note/Task modes, raw input, area picker, созданная task surface и переиспользованный `TaskRelationsControl`; отдельная dialog entity не создаётся. |
| `FeedReviewDialog` (новый control) | Visible source fragment, progress, previous/next, block area, four primary actions и staged task/note details. |
| `FeedControl.axaml(.cs)` | Feed-local toolbar, dismissible reminder, area filter, chronology, infinite scroll trigger, files/areas/today/refresh overflow. |
| `FeedViewModel` | Paging cursor, feed area filter state, review cursor, immediate review-area mutation, quick capture orchestration and shell events. |
| `MarkdownLivePreviewEditorViewModel` | Block selection state, visible-area projection, active edit state и move completion remap. |
| `MarkdownBlockLivePreviewEditor.axaml(.cs)` | Stable preview/editor slot, drag handles, pointer/keyboard selection, drop target and keyboard move alternative. |
| `FeedTaskCaptureService` (новый Notes operation) | Создать task через existing target, вставить live task link в daily note и journal/rollback partial failure. |
| `FeedMarkdownBlockMoveService` (новый Notes operation) | Атомарно переместить ordered set content blocks в пределах одного document с revision check. |
| `TaskRelationsControl` / `TaskStatusPicker` | Переиспользуются без новой семантики для уже созданной task в quick capture/review. |
| `SettingsControl` / `SettingsViewModel` | Единственный settings UI/model; control переносится из task tab в shell overlay. |
| `src/Unlimotion.Test` | Notes contracts, ViewModel state и Avalonia.Headless regression coverage. |
| AppAutomation Headless/FlaUI | Сквозные app-wide hotkey, overlay, scroll, review, block edit/drag и visual geometry scenarios. |

### 6.2 Детальный дизайн

#### 6.2.1 Общий app bar

Постоянная строка `ShellAppBar` заменяет отдельный shell mode selector, Feed header и task-only create button:

```text
┌ [+ ▾]  [Лента | Задачи]  [Поиск по задачам и заметкам........]  [Разбор 7] [⚙] [⋯] ┐
└────────────────────────────────────────────────────────────────────────────────────┘
```

- `+` доступен в обоих режимах и содержит:
  - `Новая запись`;
  - `Задача в ежедневной заметке`;
  - `Новая задача`;
  - task-context actions (`соседняя`, `вложенная`, `блокирующая`) только когда они валидны для текущей task selection.
- Search использует уже существующий `FeedSearchIndex`, который индексирует daily notes, thematic notes и tasks. Results открываются как shell flyout из обоих режимов:
  - task result открывает существующую task card в `Задачах`;
  - daily/note result переключает shell в `Лента`, закрывает search flyout и навигирует к блоку/файлу;
  - type/area/date filters находятся в компактной раскрываемой части search flyout, а не занимают постоянную строку Feed.
- Review action показывает pending count и доступен даже при нуле как menu item в global overflow. При pending count > 0 компактная icon-button может оставаться в app bar.
- Settings gear и пункт `Настройки` в overflow вызывают один shell overlay.
- Feed-local actions `Области`, `Файлы`, `Сегодня`, `Обновить` не занимают app-wide bar; они находятся в компактной локальной строке/overflow Feed.
- App bar сохраняется при переключении режимов; открытый search/create flyout закрывается при mode change.

#### 6.2.2 Global quick capture

```text
┌ Быстрая запись                                      Esc ┐
│ [Запись] [Задача]                                      │
│ Область [последняя использованная ▾]                   │
│ Текст    [..........................................]  │
│                                [Отмена] [Добавить]      │
└─────────────────────────────────────────────────────────┘
```

- `Ctrl+Shift+Space` и `+ → Новая запись` открывают overlay поверх текущего mode; default tab — `Запись`.
- `Запись` вызывает существующий safe append path. Markdown вводить не обязательно; raw Markdown сохраняется как сейчас.
- `Задача` создаёт обычную task и вставляет в выбранную area сегодняшней заметки `FeedLinkSerializer.Task`, а не checkbox marker.
- Task capture является journaled two-resource operation:
  1. создать task через `IFeedTaskCreationTarget`;
  2. атомарно вставить live link в daily note с expected revision;
  3. при partial failure использовать существующий task conversion journal/recovery contract, не оставляя silent orphan.
- После успешного task capture overlay остаётся на created-task surface: status icon слева от title, goal/areas и существующий `TaskRelationsControl`. Пользователь может сразу назначить одного или нескольких родителей и закрыть overlay кнопкой `Готово`.
- При закрытии до сохранения текст остаётся в quick-capture state `FeedViewModel` до успешной записи или явной очистки в текущем app session.
- Если vault/task storage недоступен или возник conflict, overlay остаётся открытым, input не очищается, показывается локализованная recovery/error action.

#### 6.2.3 Лента, viewport и подгрузка

- После удаления постоянной quick-capture card основную высоту получает `FeedChronologyList`.
- Каждый parent Grid/ScrollViewer в цепочке задаёт корректный star row и `MinHeight=0`; chronology имеет `VerticalScrollBarVisibility=Auto`.
- При приближении к нижней границе (не позиционный magic number, а threshold около одной viewport height) `FeedControl` вызывает idempotent `LoadOlderDaysCommand`.
- Loading guard: одновременно выполняется не более одного page request; повторные `ScrollChanged` во время `IsLoadingOlderDays` игнорируются.
- Paging использует `ListDaysPageAsync(skip: LoadedDayCount, take: DayPageSize)` и добавляет только новые `FeedDayViewModel` в конец. Уже отображённые day/editor instances не пересоздаются, поэтому offset, dirty edit, selection и focus сохраняются.
- При внешнем refresh остаётся общий safe snapshot path; append-only paging не подменяет watcher/conflict behavior.
- Loading row сообщает `Загружаю…`; manual retry показывается только после ошибки. Когда `HasMoreDays=false`, дополнительный контрол не занимает место.
- Empty vault, all-areas-filtered and paging-error states имеют отдельные тексты.

#### 6.2.4 Feed area filter

- Локальная строка Feed содержит multi-select `Все области` с иерархическими display labels из `AreaCatalog` и отдельным `Без области`.
- Default — все active areas. Выбор хранится как ephemeral Feed UI state и не меняет Markdown/task classification.
- Фильтр скрывает content blocks и пустые area headings; day card скрывается, если после фильтра не осталось content blocks.
- Search filters и Feed chronology filter — разные состояния: изменение одного не переписывает другое.
- Review overlay всегда показывает source fragment независимо от текущего Feed filter; закрытие review не сбрасывает выбранные areas.

#### 6.2.5 Dismissible review reminder

- Reminder остаётся внутри Feed и показывает pending blocks/days, кнопку `Начать` и `×`.
- `×` скрывает reminder для текущего review queue version. Если появляются новые pending locators или vault reconnect создаёт новую queue version, reminder появляется снова.
- Скрытие reminder не меняет review state и не уменьшает app-bar count.
- Review всегда доступен через `Ctrl+Shift+R`, app-bar action и global overflow menu.

#### 6.2.6 Review overlay и навигация

```text
┌ Разбор записей                              3 из 7 [×] ┐
│ [← Предыдущий]                         [Следующий →]   │
│ ┌ выбранный Markdown видим полностью .............. ┐ │
│ └───────────────────────────────────────────────────┘ │
│ Область блока [Лента ▾]                               │
│ [Задача] [Заметка] [Оставить] [На сегодня]            │
│   staged details only for selected decision           │
└────────────────────────────────────────────────────────┘
```

- `FeedReviewPanel` переносится в shell-level `FeedReviewDialog`, но сохраняет automation ID.
- `FeedReviewSelectionText` становится обычным видимым read-only source fragment с wrap и max-height scroll только для очень больших selections.
- Progress показывает позицию в текущем unresolved snapshot.
- Previous/Next меняют текущий candidate без decision и без causal event. Boundary buttons disabled.
- Review cursor хранит stable locator текущего candidate. После refresh он remap-ится; если candidate исчез, выбирается элемент на том же индексе, а затем предыдущий при выходе за конец.
- Финальное решение удаляет/преобразует current candidate и автоматически открывает следующий unresolved candidate. Если очередь пуста, overlay показывает completion state и закрывается по `Готово`.
- `Задача` и `Заметка` сначала раскрывают только соответствующие параметры. `Оставить` и `На сегодня` выполняются сразу. `Отложить` остаётся secondary overflow action, а не пятая primary кнопка.
- Task flow:
  - stage 1: goal, task areas, `Создать задачу`;
  - stage 2 после journaled conversion: status/title/parents и `Готово и далее`;
  - `Готово и далее` является финальным принятием task decision и открывает следующий candidate.
- Note flow: title/folder и `Создать заметку`; успешная operation сразу открывает следующий candidate.
- Expand/shrink selection доступны как compact secondary controls рядом с source fragment, а не отдельная постоянная toolbar.

#### 6.2.7 Изменение области во время review

- `Область блока` всегда видна под source fragment и отражает physical Markdown area текущей selection.
- Смена value немедленно вызывает существующий `FeedAreaAssignmentService`; отдельная кнопка `Назначить` удаляется.
- Пока mutation выполняется, picker disabled и показывает progress; review cursor не меняется.
- После успеха selection remap-ится на новый locator, task/note defaults обновляются, source fragment остаётся текущим.
- При conflict/error picker возвращается к подтверждённому value, current candidate и user selection сохраняются, показывается safe conflict/error flow.

#### 6.2.8 Stable borderless inline edit

- Preview и editor находятся в одном stable container и занимают одну grid cell.
- При начале edit control запоминает measured preview height как editor `MinHeight`. Рост текста разрешён, но вход в edit mode не уменьшает/увеличивает исходный block bounds.
- TextBox использует transparent background, `BorderThickness=0` и те же horizontal padding/typography, что preview.
- Focus остаётся различимым через тонкий left accent/drag handle active state, а не рамку TextBox.
- Commit/cancel/error не создают второй текстовый экземпляр и не прокручивают chronology, если edited block остаётся в viewport.
- Generated task/note/moved terminal links и unsafe raw fallback остаются не редактируемыми по существующим правилам.

#### 6.2.9 Multi-selection и drag-and-drop

- Content block выбирается кликом по drag handle/selection gutter:
  - обычный click — один block;
  - `Ctrl` — добавить/убрать block;
  - `Shift` — непрерывный диапазон от anchor;
  - `Esc` — очистить selection.
- Selection ограничена одним Markdown document. Порядок выбранных блоков всегда соответствует source order, даже если Ctrl selection non-contiguous.
- Drag любого selected block перемещает весь selected set. Drop indicator показывает точную insertion boundary или target area.
- `FeedMarkdownBlockMoveService` получает source path/revision, ordered stable locators и insertion anchor. Он:
  - повторно разрешает locators на актуальном document;
  - отклоняет headings/frontmatter/terminal/recovery blocks и drop внутрь selection;
  - удаляет selected blocks в descending source order;
  - вставляет их одной группой в исходном порядке;
  - выполняет один atomic write с expected revision;
  - возвращает output locators для восстановления selection/highlight.
- DnD работает внутри section и между areas одного daily-file. Между днями drop запрещён с понятным cursor/help text.
- Keyboard/context-menu alternatives `Переместить выше`, `Переместить ниже`, `Переместить в область…` используют тот же service.
- При conflict/error файл остаётся без partial write, selection сохраняется и показывается существующий conflict/error UI.

#### 6.2.10 Settings как app-wide overlay

- `SettingsControl` удаляется из task `TabControl` и размещается один раз в shell overlay.
- `Ctrl+,`, gear и overflow item открывают Settings поверх текущего mode; закрытие возвращает прежний mode, scroll/focus context по возможности сохраняется.
- Все task, Feed/vault, appearance, backup, update и task-space настройки остаются в одном существующем `SettingsViewModel`.
- Deep navigation к конкретной Settings section допускается как follow-up API, но не требуется для этой итерации.

#### 6.2.11 Automation/selectors и localization

- Сохраняются: `FeedRoot`, `FeedChronologyList`, `FeedReviewPanel`, `FeedReviewSelectionText`, `FeedReviewAreaPicker`, task relation/status IDs, day/block IDs.
- Переносятся без смены смысла: `FeedQuickCaptureTextBox`, `FeedAreaPicker`, review decision IDs.
- Намеренно заменяются:
  - `ShellModeSelector` остаётся container ID внутри нового `ShellAppBar`;
  - `GlobalTaskCreateMenuButton` → `GlobalCreateMenuButton` из-за расширения app-wide contract;
  - `SettingsTabItem` → `GlobalSettingsButton` + `GlobalSettingsOverlay`;
  - `FeedSearchBox` → `GlobalSearchBox`.
- Новые critical IDs: `GlobalCreateNoteMenuItem`, `GlobalCreateFeedTaskMenuItem`, `GlobalReviewButton`, `GlobalReviewMenuItem`, `FeedReviewPreviousButton`, `FeedReviewNextButton`, `FeedAreaFilterButton`, `FeedReviewReminderCloseButton`, `FeedQuickTaskCreatedSurface`.
- Все новые strings добавляются в fallback EN и RU resources; parity test остаётся обязательным.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| App-wide capture | В `Задачах` нажать `Ctrl+Shift+Space` | Поверх текущего mode открыт quick capture; после записи mode/context не сброшен | Headless + AppAutomation video | AC-01, AC-03 |
| Task in note | Выбрать tab `Задача`, ввести title/area, создать | В daily-note live link без checkbox; созданная task показывает status слева и existing parents control | Unit/integration + Headless/FlaUI | AC-04, AC-14 |
| Infinite chronology | Прокрутить Feed вниз | Есть scrollbar; следующая page появляется автоматически без скачка/потери edit state | Headless geometry + AppAutomation | AC-05 |
| Area filtering | Снять одну/несколько areas | В day cards остаются только выбранные sections; search/review state не сброшен | VM + Headless | AC-06 |
| Dismiss reminder | Нажать `×` | Reminder скрыт, global review count/command остаются; новый pending queue возвращает reminder | VM + Headless | AC-07 |
| Review from Tasks | Нажать `Ctrl+Shift+R` в `Задачах` | Открыт review overlay с видимым source fragment и progress | Headless + AppAutomation | AC-08, AC-09 |
| Review navigation | Нажать Next/Previous без решения | Candidate меняется, unresolved state не меняется | Unit + Headless | AC-09 |
| Review decision | Применить Leave/Move/Note или закончить Task stage | Автоматически открыт следующий unresolved candidate | Unit + Headless | AC-09, AC-11 |
| Review area | Сменить `Область блока` | Selection атомарно перемещена в Markdown и остаётся текущей | Integration + Headless | AC-10 |
| Stable edit | Открыть длинный block на редактирование | Bounds не прыгают; рамки поля нет; focus заметен | Headless bounds + FlaUI screenshot | AC-12 |
| Multi-block drag | Ctrl/Shift выбрать несколько blocks и перетащить | Все blocks перемещены одним действием в исходном порядке | Unit operation + AppAutomation | AC-13 |
| Global Settings | Нажать gear в Feed, затем закрыть | Открыт тот же SettingsControl; после закрытия виден прежний Feed position | Headless + AppAutomation | AC-02 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Any mode, no overlay | Quick capture hotkey | `QuickCaptureOpen` | Vault disabled: onboarding/error action, draft retained | Hotkey app-scoped |
| Quick capture note | Save | Append → close/clear | Revision conflict: stay open, no clear | Existing safe append |
| Quick capture task | Create | Journaled task + link → created-task stage | Task/link partial failure: recovery, no silent orphan | Parents after actual task exists |
| Feed scroll near end | ScrollChanged | `LoadingPage` → append days | Busy/no more: no-op; error: retry row | One in-flight page |
| Review unresolved | Previous/Next | Select candidate only | Boundary disabled; missing locator remap | No decision event |
| Review unresolved | Change area | Busy → same candidate/new locator | Conflict restores confirmed area | Immediate mutation |
| Review unresolved | Primary action | Expand action or apply decision | Unsupported task capability disabled with copy | Only one staged action open |
| Review task created | `Готово и далее` | Apply terminal review state → next | Relation save error keeps current stage | No hidden auto-close |
| Block idle | Start edit | Stable edit | Non-editable terminal/raw fallback disabled | Focus left accent |
| Blocks selected | Drag/drop | Atomic move → remapped selection | Cross-day/invalid target rejected | One document only |
| Settings closed | Gear / `Ctrl+,` | Settings overlay | Already open: focus existing | Previous mode retained |
| Any overlay | Esc | Close topmost overlay | Dirty quick draft retained | Does not close underlying mode |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Hotkeys scope | agent | Active Unlimotion window, not OS-global | 0.99 | Пользователь мог ожидать tray/global capture | Нет; original product boundary and wording `из любого состояния приложения` support app scope |
| Search semantics | agent | App-wide lookup across tasks + notes via existing Feed index; local task filters remain | 0.90 | Может восприниматься как дублирование task filter | Нет; matches approved app-bar mockup and avoids dead toolbar space |
| Review area save | user + approved mockup | Apply immediately on select, remain on same candidate | 0.98 | Accidental change | Нет; optimistic revision + disabled busy + error restore mitigate |
| Reminder dismissal lifetime | agent | Until review queue version changes | 0.94 | Banner may return sooner than expected | Нет; new pending work must become discoverable |
| DnD boundary | agent | Same daily document, including cross-area; no cross-day drop | 0.96 | Пользователь может хотеть cross-day drag | Нет; explicit `На сегодня` is safer date-changing workflow |
| Non-contiguous selection | agent | Supported; preserves source order | 0.93 | More complex mutation/remap | Нет; directly satisfies Ctrl multi-selection and is safely atomic |
| Task parent timing | prior user decision + existing control contract | Parent is assigned after real task creation on created-task stage | 0.97 | One extra final action | Нет; only way to reuse exact persisted `TaskRelationsControl` without parallel picker |
| Settings presentation | user | Shell overlay, not third mode or task tab | 0.99 | Existing SettingsTab selector changes | Нет; migration explicitly tested |
| Video evidence | governance | Record meaningful before and after flows when technically available | 0.95 | Environment may block recorder | Нет; documented fallback allowed |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Daily Markdown | Vault files + `DailyNoteNaming` | New task-link capture and block reorder mutations | Existing files unchanged until explicit action | Golden/integration tests |
| Task entity | Existing task storage / `IFeedTaskCreationTarget` | No new fields; quick capture uses existing task contract | Backward compatible | Task storage + mapping tests |
| Review state | Causal review sidecars/coordinator | Add ephemeral cursor; decisions unchanged | No sidecar schema change expected | Review cursor/causal tests |
| UI settings | Existing `SettingsViewModel` | Presentation moves to shell overlay | No config migration | Headless settings flow |
| Area filter | Feed UI state | New ephemeral multi-selection | No persisted data | VM/UI tests |
| Block selection | Editor UI state | New ephemeral ordered selected locators | No persisted schema | Selection/move tests |
| Operation recovery | Existing journals/revisions | Reuse/extend operation kind for quick-task capture; block move is one-file atomic write | Journal reader must ignore/understand additive kind | Recovery tests |
| Automation | Existing AutomationId contracts | Narrow selector migration table in §6.2.11 | Tests/page objects updated atomically | Headless/FlaUI discovery |

## 7. Бизнес-правила / Алгоритмы

1. Raw capture remains the default; choosing task/note type is optional until user explicitly changes tab/action.
2. A final review decision advances automatically. Navigation without decision never changes causal review state.
3. Review area change is a Markdown mutation, not task classification; task areas remain a separate multi-select.
4. Quick task capture creates a real task and a live link. A Markdown checkbox is neither required nor inserted.
5. Parent relations are edited only through existing `TaskRelationsControl` against an already persisted task.
6. Block move preserves byte-level raw block text, line endings, relative order and unknown Markdown inside each selected block.
7. One DnD operation performs at most one daily-file write. Revision mismatch prevents the entire operation.
8. Area headings/frontmatter are structural boundaries and never part of user content selection.
9. Auto paging cannot run concurrently and cannot discard dirty editors, selection, focus or scroll anchor.
10. Hidden reminder never hides review availability or pending count.

## 8. Точки интеграции и триггеры

- `MainScreen` key handling: `Ctrl+Shift+Space`, `Ctrl+Shift+R`, `Ctrl+,`, `Escape` topmost overlay.
- `MainScreen` app bar commands route to quick-capture/review/search state `FeedViewModel` and existing task commands.
- `FeedControl` chronology `ScrollChanged` triggers `LoadOlderDaysCommand` near end.
- `FeedReviewDialog` binds current review cursor and staged action state; area selection triggers command after confirmed value change.
- `MarkdownBlockLivePreviewEditor` pointer/key handlers call selection controller and `MoveBlocksCommand` only on drop/keyboard confirmation.
- `App.WireNoteVaultFeed` continues wiring vault/task target/resolver and additionally wires shell navigation/search events.
- Localization change refreshes new shell/review/quick-capture option labels without resetting state.

## 9. Изменения модели данных / состояния

- Persisted task schema: без изменений.
- Persisted area/settings schema: без изменений.
- Review sidecar schema: без изменений, если cursor остаётся ephemeral; если текущий coordinator требует additive session cursor для crash resume, это отдельный EXEC stop/ASK-HUMAN, а не silent schema expansion.
- Новое ephemeral state:
  - shell overlay stack/current overlay;
  - quick capture kind/draft/created task;
  - selected Feed area identities;
  - review cursor/staged action;
  - ordered selected block locators and drag anchor.
- Additive recovery/journal operation kind для quick-task capture допускается только с backward-compatible reader behavior и тестом unknown/additive record handling.

## 10. Миграция / Rollout / Rollback

- При первом запуске после обновления vault/tasks не переписываются.
- Settings view использует тот же `SettingsViewModel`; config migration не нужна.
- Review sessions, pending locators и existing task/note links продолжают открываться новым dialog.
- Старые automation selectors из §6.2.11 обновляются вместе с page objects/tests в одном change set.
- Rollback приложения оставляет созданные quick-task links совместимыми с текущим renderer и task navigation.
- Block move не создаёт новый syntax, поэтому результаты читаются старой версией и Obsidian.
- При откате UX causal review state и settings остаются валидными; ephemeral UI state теряется допустимо.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **AC-01 Shared bar:** один `ShellAppBar` видим в Feed/Tasks и содержит create, modes, app search, review и Settings actions без duplicate Feed/task create headers.
- **AC-02 Global Settings:** Settings открывается из обоих режимов через gear, menu и `Ctrl+,`; закрытие сохраняет прежний mode и не создаёт второй Settings model.
- **AC-03 Global note capture:** quick capture открывается из обоих режимов через menu/`Ctrl+Shift+Space`, сохраняет raw input в выбранную area и не очищает draft при failure.
- **AC-04 Quick task capture:** обычная task и live link создаются без checkbox; partial failure recoverable; созданная task surface использует status-left, goal/areas и existing parents control.
- **AC-05 Infinite scroll:** chronology имеет видимую вертикальную прокрутку; near-end автоматически append-ит page, не создаёт concurrent loads и сохраняет scroll/edit/selection state.
- **AC-06 Area filter:** multi-select фильтрует chronology by areas/No area, скрывает empty sections/days и не меняет Markdown/search/review state.
- **AC-07 Review reminder:** reminder dismissible до новой queue version; global review action/count остаются доступны.
- **AC-08 Global review entry:** review открывается из Feed/Tasks через menu/app-bar/`Ctrl+Shift+R` и показывает текущий source fragment.
- **AC-09 Review navigation:** progress, Previous/Next и boundaries корректны; navigation не принимает decision; финальное decision открывает следующий unresolved candidate.
- **AC-10 Review area:** смена area immediate/atomic, remap-ит locator и остаётся на текущем candidate; failure не теряет selection.
- **AC-11 Staged decisions:** видны четыре primary actions; task/note details не показываются одновременно; task final action и note creation продвигают review.
- **AC-12 Stable edit:** entry edit не меняет initial bounds, не показывает TextBox border, сохраняет visible focus and commit/cancel semantics.
- **AC-13 Multi-block move:** Ctrl/Shift/non-contiguous selection и same-day cross-area DnD перемещают blocks одной atomic write в source order; invalid/cross-day/conflict cases do not mutate.
- **AC-14 Reuse:** task status находится слева от title, parent UI — реальный `TaskRelationsControl`, relation semantics и multiple parents неизменны.
- **AC-15 Accessibility/localization:** keyboard alternatives, focus order, live status и RU/EN resources работают; critical AutomationIds из migration table доступны.
- **AC-16 Performance/lifecycle:** paging, search, move and capture не блокируют UI thread; stale async completions после vault/mode/dispose не меняют новую session.
- **AC-17 Safety/compatibility:** unknown Markdown, BOM/newline, external revision conflicts, watcher refresh, causal review and recovery contracts сохраняются.

### Characterization / TDD

До production change добавить failing checks для:

- source fragment bounds/visibility в review;
- quick capture/review/settings entry из Tasks mode;
- scroll-to-end auto-load + scroll anchor;
- immediate review area change;
- preview/editor bounds and borderless state;
- ordered multi-block move and revision conflict;
- quick task live-link transaction and partial failure recovery.

### Planned automated coverage

- `FeedShellUiTests`: shared app bar, global hotkeys, overlay precedence, Settings return context.
- `FeedControlUiTests`: local toolbar, dismiss reminder, area filter, infinite scroll and review source visibility.
- `FeedReviewWorkflowTests` / new `FeedReviewNavigationTests`: cursor, previous/next, decisions and area remap.
- `DailyMarkdownBlockMoveTests` (new): contiguous/non-contiguous/cross-area, invalid targets, newline/BOM/raw preservation, revision conflict.
- `FeedQuickTaskCaptureTests` (new): success, task-create failure, note-write conflict, journal recovery/idempotency.
- `MarkdownLivePreviewEditorUiTests`: selection, keyboard alternative, bounds, border/focus, commit/cancel.
- AppAutomation Headless/FlaUI shared scenario: Tasks → quick note → Feed scroll/filter → review prev/next/area/task+parent → Settings → return.
- Existing task relation/status, daily storage, review causal/recovery, localization parity and settings tests remain green.

### Visual acceptance

- App bar visually matches durable wireframe and approved local interactive mockup at wide and narrow desktop widths.
- Quick capture/review/settings overlays have one clear primary surface, opaque background and topmost Esc behavior.
- Review source fragment is readable on first screen; primary decision controls are not pushed below viewport at 736 px content width.
- Edit mode retains block top/left/width and initial height within 1 device-independent pixel tolerance; no TextBox outline, focus left accent visible.
- Drag selection and drop indicator are visible in light/dark themes; selected state does not rely only on color.
- Before evidence: record current meaningful flow before source edits if recorder works.
- After evidence: record the same successful AppAutomation/FlaUI flow and inspect at least one wide and one narrow screenshot.
- Planned local-only paths: `chat-artifacts/feed-ux-before.mp4`, `chat-artifacts/feed-ux-after.mp4`, `chat-artifacts/feed-ux-after-wide.png`, `chat-artifacts/feed-ux-after-narrow.png`.
- Fallback: if safe video capture is unavailable, store exact runner/recorder failure, passing Headless/FlaUI logs and inspected before/after screenshots.

### Команды для проверки

Preflight/discovery:

```powershell
dotnet --info
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --list-tests
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -- --list-tests
```

Targeted TUnit examples (final node names verify through `--list-tests`):

```powershell
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/FeedShellUiTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/FeedControlUiTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/DailyMarkdownBlockMoveTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/FeedQuickTaskCaptureTests/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowHeadlessTests/Feed*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
```

Affected builds and mandatory serial gates:

```powershell
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release
dotnet build tests/Unlimotion.AppAutomation.TestHost/Unlimotion.AppAutomation.TestHost.csproj -c Release
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
```

Desktop/FlaUI and evidence (sequential, interactive Windows session):

```powershell
dotnet test --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowFlaUiTests/Feed*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed
pwsh -File scripts/record-app-window-per-monitor-dpi.ps1
```

Final static checks:

```powershell
git diff --check
rg -n "GlobalCreateMenuButton|GlobalSearchBox|GlobalSettingsOverlay|FeedReviewPreviousButton|FeedReviewNextButton" src tests
```

### Stop rules для validation

- `--list-tests` не является passing test evidence.
- Stateful unit/Headless/FlaUI suites и recorder запускаются последовательно.
- После timeout identical command не повторяется без progress/root-cause evidence и изменённой гипотезы.
- Full solution build с отсутствующими mobile workloads не маскируется; desktop/shared affected builds остаются обязательными, blocker фиксируется отдельно.
- Любой failing UI test блокирует completion; flaky retry без root cause не считается green.
- Video capture failure не меняет product code; используется documented fallback.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-01 | Shell Headless + AppAutomation | Wide/narrow app bar inspection | before/after screenshots/video | — |
| AC-02 | Shell Settings Headless + AppAutomation | Return mode/scroll/focus | after video/log | — |
| AC-03 | Quick capture VM + Headless | Inspect resulting daily file and retained failure draft | temp vault + video | — |
| AC-04, AC-14 | QuickTask operation + task relation/status Headless/FlaUI | Inspect live link, status/title bounds, multiple parents | task JSON + vault + screenshot | — |
| AC-05 | Paging VM + chronology Headless/AppAutomation | Scrollbar/offset/dirty editor before-after | UI log/video | — |
| AC-06 | Filter VM + Headless | Inspect selected/hidden sections in both themes | screenshot | — |
| AC-07 | Review reminder VM + Headless | Dismiss and new queue version | UI log | — |
| AC-08, AC-09, AC-11 | Review cursor/workflow + Headless/AppAutomation | Source/progress/auto-next | video + causal state log | — |
| AC-10 | Area assignment integration + Headless | Markdown diff and same candidate locator | temp vault | — |
| AC-12 | Editor Headless bounds + FlaUI | Border/focus screenshot in light/dark | bounds log + screenshots | — |
| AC-13 | Block move unit + Headless/AppAutomation | File diff/order/drop indicator | temp vault + video | — |
| AC-15 | Resource parity + keyboard Headless/FlaUI | RU/EN and focus order | test logs | — |
| AC-16 | Cancellation/lifecycle/paging performance tests | No stale callback / UI freeze | timing log | — |
| AC-17 | Existing storage/conflict/recovery regression suites | Inspect conflict and unknown Markdown fixtures | test logs/temp vault | — |

## 12. Риски и edge cases

- Avalonia `ScrollChanged` может срабатывать несколько раз при append; VM-level in-flight guard обязателен.
- Пересоздание day VMs на paging может уничтожить dirty editor/selection; paging обязан append-ить page, а не применять полный snapshot.
- Drag locators могут устареть между pointer-down и drop из-за watcher; drop повторно проверяет revision и locators.
- Non-contiguous move рядом с selection легко даёт off-by-one; mutation рассчитывает insertion после удаления выбранных indices и покрывается permutation tests.
- Quick task partial failure может оставить task без link; journal/recovery/idempotency обязательны.
- Shell overlay поверх hidden Feed может получить navigation event до визуализации; mode switch должен завершиться до `ScrollIntoView`.
- Перенос SettingsControl из TabControl может раскрыть implicit ancestor binding; inspect/Headless tests должны проверить commands and DataContext.
- Immediate area change может быть случайной; disabled busy state, optimistic conflict and visible confirmation mitigate without extra modal.
- Borderless editor не должен терять accessible focus. Left accent/handle state обязателен и проверяется keyboard-only.
- Global search flyout не должен удерживать stale result после vault/task-space switch; generation/session guard already used by Feed search must remain.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Верхняя панель снова перегружена» | Раньше несколько схожих панелей занимали экран | App bar содержит только app-wide actions; Feed-local actions уходят в одну compact row/overflow; narrow state tested | mitigated |
| «Quick capture всё ещё заставляет думать о типе» | Task mode добавляет структуру | Default — raw `Запись`; task tab выбирается только явно | mitigated |
| «После решения review остаётся лишний Continue» | Текущая task conversion требует Continue | `Готово и далее` является финальным task decision; все финальные actions auto-advance | mitigated |
| «Область review меняется, но непонятно сохранилась ли» | Immediate mutation может выглядеть как filter | Picker disabled during save, visible status, same candidate and updated locator after success | mitigated |
| «Редактор без рамки — непонятно, где фокус» | Убирается стандартный TextBox border | Active left accent/handle preserves focus cue without textbox frame | mitigated |
| «Drag-and-drop между днями тоже нужен» | Пользователь может воспринимать ленту как один список | Cross-day drop intentionally blocked; explicit journaled `На сегодня` keeps date change safe | accepted-risk |
| «Настройки должны быть везде, но не ещё одним режимом» | Current tab semantics wrong | One shell overlay reuses existing control/model and returns to prior context | mitigated |
| «Фильтр areas не должен портить search/review» | Схожие area controls могут быть спутаны | Three states are independent and mapped in tests | mitigated |

### Rework Prevention Checklist

- [x] Spec names the app bar, overlays, Feed viewport, review, inline editor and drag controls the user will operate.
- [x] Every user-visible scenario maps to automated and visual evidence.
- [x] Agent-owned assumptions are listed in Decision Ledger.
- [x] Likely objections are predicted and mitigated or explicitly accepted.
- [x] Business/UX/tester/developer/delivery roles are reviewed in §19.
- [x] Acceptance criteria describe completed behavior, not preparation steps.
- [x] EXEC has before/after evidence and staged TDD/validation path.

## 13. План выполнения

### Этап 1 — Characterization и shell foundation

- Записать meaningful before video/screenshot до source edit, если recorder доступен.
- Добавить failing Headless/AppAutomation checks для shared app bar, global overlay entry and visible review source.
- Ввести shell overlay state/commands и перенести SettingsControl без изменения SettingsViewModel.
- Условие остановки: app bar и Settings overlay проходят targeted UI tests; task/feed mode state не регрессировал.

### Этап 2 — Global quick capture и search

- Вынести quick capture state/control, сохранить old automation IDs.
- Реализовать journaled quick-task capture, created task surface и relation reuse.
- Перенести app search UI в shell flyout и проверить navigation between modes.
- Условие остановки: note/task capture and search pass unit/Headless; partial failure recovery proven.

### Этап 3 — Feed viewport, paging, filter and reminder

- Исправить layout/scroll constraints.
- Перевести paging на append page with in-flight guard and automatic threshold trigger.
- Добавить multi-area chronology filter and queue-version reminder dismissal.
- Условие остановки: scroll anchor/dirty editor/filter/dismissal tests green.

### Этап 4 — Review redesign

- Перенести review в shell dialog, показать source, добавить cursor/navigation/progress.
- Сделать staged actions и immediate area mutation with remap/error state.
- Сохранить causal decisions, task/note/move journals and relation UI.
- Условие остановки: review workflow and causal/recovery regression suites green.

### Этап 5 — Stable edit и multi-block move

- Стабилизировать editor container/focus geometry.
- Добавить selection controller, drag/drop + keyboard alternatives.
- Реализовать atomic move service and locator remap.
- Условие остановки: permutation/conflict/raw-preservation tests and UI bounds/drag scenarios green.

### Этап 6 — Localization, full validation and visual evidence

- Обновить RU/EN resources, selector page objects and hotkey help.
- Выполнить targeted → affected builds → full serial unit/Headless → FlaUI.
- Записать/проверить after video and wide/narrow screenshots; выполнить post-EXEC review.
- Условие остановки: все AC доказаны, BLOCKER/HIGH/MEDIUM findings исправлены, residual LOW явно согласован/зафиксирован.

## 14. Открытые вопросы

Блокирующих user-owned решений нет. Decision Ledger содержит только обратимые implementation defaults в границах согласованного макета и текущих safety contracts.

## 15. Соответствие профилю

- Профиль: `dotnet-desktop-client` + `ui-automation-testing`.
- Выполненные требования профиля на SPEC:
  - UI thread boundary and cancellation/lifecycle risks описаны.
  - Existing controls, commands and AutomationIds переиспользуются или имеют explicit migration.
  - Visual planning artifact доступен как durable wireframe + approved local interactive mockup.
  - Characterization/TDD, targeted UI tests, full unit/Headless, FlaUI and video/fallback evidence mapped.
  - `dotnet build` and `dotnet test` gates included.

## 16. Таблица изменений файлов

| Файл / группа | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainScreen.axaml(.cs)` | Shared app bar, overlays, hotkeys | App-wide access |
| `src/Unlimotion/Views/MainControl.axaml` | Remove duplicate create button and Settings tab placement | Eliminate task-only app actions |
| `src/Unlimotion/Views/FeedControl.axaml(.cs)` | Local toolbar, reminder, filter, auto paging | Feed viewport and context actions |
| `src/Unlimotion/Views/MainScreen.axaml(.cs)` quick-capture overlay | Inline shell overlay over shared state | Capture from any mode without a duplicate dialog entity |
| `src/Unlimotion/Views/FeedReviewDialog.axaml(.cs)` | New staged review surface | Visible source and global review |
| `src/Unlimotion/Views/MarkdownBlockLivePreviewEditor.axaml(.cs)` | Stable edit, selection, DnD | Direct block manipulation |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Shell commands/state/navigation | Shared behavior |
| `src/Unlimotion.ViewModel/Feed/FeedViewModel.cs` | Paging/filter/review cursor and orchestration | UX state |
| `src/Unlimotion.ViewModel/Feed/FeedViewModel.cs` quick-capture state | Overlay state and safe capture orchestration | Reuse one Feed lifecycle and avoid duplicate state ownership |
| `src/Unlimotion.ViewModel/Feed/MarkdownLivePreviewEditorViewModel.cs` | Selection/filter/move remap | Block interaction state |
| `src/Unlimotion.Notes/Operations/FeedTaskCaptureService.cs` | Journaled task + link operation | Checkbox-free task capture |
| `src/Unlimotion.Notes/Operations/FeedMarkdownBlockMoveService.cs` | Atomic same-document multi-move | Safe DnD |
| RU/EN resource files | New shell/review/drag/status strings | Localization |
| `src/Unlimotion.Test/*Feed*Tests.cs` | Unit/Avalonia.Headless coverage | Regression proof |
| `tests/Unlimotion.UiTests.Authoring/**` | Page objects/shared scenario | Stable app automation |
| `tests/Unlimotion.UiTests.Headless/**` | End-to-end headless | Mandatory UI flow |
| `tests/Unlimotion.UiTests.FlaUI/**` | Bounds/focus/drag/visual flow | Desktop evidence |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Shell | Mode-only row | Functional app bar in both modes |
| Quick capture | Large Feed-only card | Global temporary overlay |
| Task capture | Manual checkbox/review conversion | Direct real task + live link + parents |
| Settings | Task tab | App-wide overlay |
| Review | Embedded overloaded panel, hidden source | Global staged dialog, visible source, prev/next |
| Review area | Picker + separate button among controls | Always-visible immediate picker |
| Reminder | Always visible while pending | Dismissible until queue changes |
| Chronology | Manual load button / clipping risk | Scroll viewport + automatic append paging |
| Area view | Search-only single filter | Multi-area chronology filter |
| Text edit | Preview replaced by framed TextBox | Stable borderless edit with focus accent |
| Block order | No direct manipulation | Multi-select atomic same-day DnD |

## 18. Альтернативы и компромиссы

### Оставить quick capture и review внутри Feed

- Плюсы: меньше shell changes.
- Минусы: не выполняет app-wide access, продолжает занимать первый экран и требует mode switching.
- Почему не выбран: противоречит основному пользовательскому сценарию.

### Создать отдельные simplified parent/Settings/search controls

- Плюсы: локально проще layout.
- Минусы: дублирует семантику, validation and storage contracts; быстро расходится с task card/app settings.
- Почему не выбран: существующие controls уже покрывают требуемое behavior.

### Cross-day drag-and-drop

- Плюсы: визуально прямой перенос.
- Минусы: меняет date semantics, требует two-file journal/anchors/conflict recovery и конкурирует с явным `На сегодня`.
- Почему не выбран: same-document DnD покрывает reorder/areas, а date change остаётся безопасным review action.

### Rebuild full day snapshot on every page

- Плюсы: reuse existing `ApplySnapshot`.
- Минусы: риск scroll jump, dirty editor/focus/selection loss and unnecessary parsing.
- Почему не выбран: append page лучше соответствует infinite scroll contract.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, root problem, design goals и Non-Goals конкретны. |
| B. Качество дизайна | 6-10 | PASS | Shell/UI/domain responsibilities, flows, errors and performance описаны. |
| C. Безопасность изменений | 11-13 | PASS | Compatibility, revision checks, journals, migration and rollback explicit. |
| D. Проверяемость | 14-16 | PASS | 17 AC mapped to automated/visual evidence and exact commands. |
| E. Готовность к автономной реализации | 17-19 | PASS | Staged plan, no blockers, large-scope stop rules and risks specified. |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation/visual evidence requirements covered. |

Итог: ГОТОВО.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | One UX root problem, explicit non-goals and stop rules. |
| 2. Понимание текущего состояния | 5 | Current XAML/ViewModel/Notes/test boundaries traced. |
| 3. Конкретность целевого дизайна | 5 | App bar, overlays, paging, review, edit and DnD contracts explicit. |
| 4. Безопасность (миграция, откат) | 5 | Existing files stay compatible; revision/journal/rollback paths defined. |
| 5. Тестируемость | 5 | Each AC maps to unit/Headless/FlaUI/manual artifact. |
| 6. Готовность к автономной реализации | 5 | Ordered stages and no unresolved user-owned decisions. |

Итоговый балл: 30 / 30.
Зона: готово к автономному выполнению.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Capture-first flow preserved, final decisions advance and Settings remains one model? | PASS | None. |
| UX / designer | applicable | Are primary surfaces compact, source visible and interactions discoverable in repeated use? | PASS | Durable wireframes, staged review and narrow-state criteria included. |
| Tester / validation | applicable | Does every AC have deterministic automated/visual evidence including negative cases? | PASS | TDD, full gates, video/fallback and matrix included. |
| Developer / architect | applicable | Are shell/domain boundaries, atomic operations and selector migrations maintainable? | PASS | New services limited to missing task-capture/multi-move contracts. |
| Delivery / operations / security | applicable | Are Git/runtime/secrets risks introduced? | PASS | No deploy/config/secret change; artifacts local-only, rollback explicit. |

### Post-SPEC Review

- Статус: `PASS` после adversarial self-review; independent reviewer не запускался, потому что текущий multi-agent policy запрещает proactive subagents без явного запроса пользователя.
- Scope reviewed: эта SPEC; central QUEST/testing/review/profile instructions; local `AGENTS.override.md`; existing Feed specs; current `MainScreen`, `FeedControl`, Markdown editor/control, FeedViewModel, daily/mutation/area services, task relations control, unit/Headless/FlaUI structure; current and approved visual artifacts.
- Decision: можно запрашивать exact approval.
- Review passes:
  - Scope/Evidence pass: planned files and current evidence enumerated; only this new SPEC is modified.
  - Contract pass: all nine explicit user changes plus review-area addition map to scenarios, AC and tests; prior vault/task safety contracts preserved.
  - Adversarial risk pass: challenged paging rebuild, non-contiguous index math, quick-task orphan, hidden Feed navigation, Settings ancestor binding and focus-without-border.
  - Role-Based pass: business workflow, UX, tester, developer/architect and delivery roles completed above.
  - Re-review after fixes / Fix and re-review: initial draft was reviewed before approval; any objective findings are recorded below and corrected before final request.
  - Stop decision: `PASS`; no BLOCKER/HIGH/MEDIUM finding or user-owned decision remains.
- Evidence inspected:
  - branch `feat/daily-feed`, commits through `956e2b44`, dirty state containing only pre-existing untracked evidence/build directories;
  - `specs/2026-08-24-daily-feed-mode.md` and two follow-up specs;
  - `FeedControl.axaml` current quick capture/review/list layout;
  - `MarkdownBlockLivePreviewEditor` preview/TextBox swap;
  - `FeedViewModel` paging, capture, review area/task/note/move paths;
  - `DailyNoteService.ListDaysPageAsync`, `MarkdownMutationService`, `FeedAreaAssignmentService`, `TaskRelationsControl`;
  - current screenshot and approved interactive mockup.
- Depth checklist:
  - Scope drift / unrelated changes: only new follow-up SPEC; no product/artifact mutation.
  - Acceptance criteria: all explicit requirements mapped.
  - User-observable scenarios / Decision ledger / Expected objections: complete.
  - Validation evidence: TDD, targeted, affected builds, full serial, visual/video fallback specified.
  - Unsupported claims: current screenshot/implementation facts verified; local-only artifacts marked.
  - Regression / edge case: paging, revision race, partial task failure, cursor remap, non-contiguous move, overlay navigation and focus covered.
  - Comments/docs/changelog: no change during SPEC; EXEC impact decided after implementation diff.
  - Hidden contract change: selector migrations and journal compatibility explicit; no task/settings schema change.
  - Manual-review challenge: strongest likely findings are accidental cross-day semantics, hidden focus, task orphan and scroll jump; each has an invariant/test.
- No-findings justification: after tracing current code, new domain scope is limited to two missing operations; all other behavior is a presentation/state reorganization over existing tested contracts.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | paging | Reusing full `ApplySnapshot` for auto-load could recreate visible days and jump scroll/lose dirty state. | Specify append-only page load with in-flight guard and instance preservation. | fixed |
| MEDIUM | quick task safety | Creating task then appending link can leave an orphan on conflict. | Require journaled two-resource operation, recovery and idempotency tests. | fixed |
| MEDIUM | DnD correctness | Non-contiguous move can shift insertion indices and corrupt order. | Define locator re-resolution, descending removal, adjusted insertion and permutation tests. | fixed |
| LOW | accessibility | Removing TextBox border could remove visible keyboard focus. | Require left accent/handle focus cue and keyboard/FlaUI checks. | fixed |
| LOW | selector compatibility | Moving Settings/search/create changes existing test selectors. | Add explicit selector migration table and atomic page-object updates. | fixed |
| BLOCKER/HIGH | scope/design/acceptance/risk/evidence/profile | Нет находок после fixes. | — | — |

- Fixed before continuing: append-only paging; quick-task recovery; multi-move algorithm; focus cue; selector migration.
- Checks rerun: manual SPEC linter/rubric; scenario-to-AC and AC-to-test mapping; instruction/profile compliance; source/evidence re-read.
- Needs human: exact SPEC approval only.
- Residual risks / follow-ups: OS-global capture and cross-day DnD intentionally remain outside scope.

### Post-EXEC Review

- Статус: `PASS` после adversarial self-review; independent reviewer не запускался, потому что текущий multi-agent policy запрещает proactive subagents без явного запроса пользователя.
- Scope reviewed: product diff shell/Feed/review/editor/Notes operations, RU/EN resources, unit/Avalonia.Headless/AppAutomation/FlaUI tests, README media selectors и isolated visual artifacts.
- Decision: реализация соответствует утверждённым user-observable scenarios; BLOCKER/HIGH/MEDIUM открытых находок нет.
- Review passes:
  - Scope/Evidence: изменения ограничены UX Ленты и необходимыми shell/test migrations; local-only input/output и build directories не включены в продуктовый scope.
  - Contract: сохранены revision checks, operation journals, raw Markdown, task relation semantics и единый Settings model.
  - Adversarial: проверены zero-size/transparent overlays, overlay stacking, terminal created-task state, stale selectors, popup lifecycle, scroll paging, multi-selection move и конфликт записи.
  - Role-based: product/UX, accessibility, developer/architecture, tester и delivery risks проверены по фактическому diff и живому приложению.
  - Fix and re-review: все объективные находки ниже исправлены; затронутые targeted suites повторены после исправлений.
- Evidence inspected:
  - isolated desktop profile с Markdown vault под `output/feed-ux-visual/`;
  - visual baseline `chat-artifacts/unlimotion-feed-real-desktop.png` и after video `output/feed-ux-visual/feed-ux-after.mp4` (`local-only`);
  - live computer-use walkthrough: оба режима, paging, quick capture, review source/area/actions, global Settings;
  - финальные build/test results, перечисленные ниже.
- Depth checklist:
  - paging сохраняет существующие day instances и editor state;
  - quick task создаёт real task + live link через journaled operation;
  - review terminal task surface содержит status, goal, areas и reusable parents control;
  - source mutation недоступна после terminal decision, предыдущие action controls скрыты;
  - same-document multi-block move revision-checked и сохраняет BOM/newlines/raw blocks;
  - новые overlays имеют непрозрачный фон, валидные bounds, правильный Z-order и UIA control exposure;
  - global Settings/create/search/review selectors синхронно перенесены в AppAutomation и README media.
- No-findings justification: после fixes основной unit, полный Headless и целевые desktop suites зелёные; просмотр diff не выявил незакрытых safety/UX contract violations.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | review layout | Self-bound max size давал review dialog нулевые bounds в реальном desktop. | Ограничить dialog bounds родительским overlay и добавить regression assertion. | fixed |
| MEDIUM | terminal task UX | После создания task отсутствовал явный список областей, а старые decision actions оставались видимыми. | Показать status/goal/areas/parents created-task surface и скрыть source actions до `Продолжить`. | fixed |
| MEDIUM | UI automation | AppAutomation и README media искали удалённую Settings tab и Feed-only capture/search selectors. | Перенести page objects/scenarios на global controls и обновить popup lifecycle assertions. | fixed |
| LOW | modal presentation | Review/quick capture/Settings могли выглядеть прозрачными; Settings title дублировался. | Использовать opaque theme background и один title. | fixed |
| LOW | overlay order | Quick capture мог оказаться под активным review overlay. | Поднять Z-order и покрыть attached-property assertion. | fixed |

- Fixed before final report: review sizing/UIA surface; opaque modal backgrounds; Settings duplicate title; quick-capture stacking; terminal task areas/action visibility; stale AppAutomation/README selectors; test assertion for Avalonia attached Z-index.
- Checks rerun:
  - `Unlimotion.Test` full serial: `1261/1261` passed (`14m09s`); after final XAML/test fixes `FeedShellUiTests 3/3` and `FeedControlUiTests 23/23` passed on rebuilt binaries.
  - `Unlimotion.UiTests.Headless` full serial: `47/47` passed; Feed subset `7/7` passed.
  - `Unlimotion.UiTests.FlaUI` Feed subset: `6/6` passed; task-space Settings regression: `1/1` passed on retry after one pre-existing removal timing flake.
  - affected builds: `Unlimotion.Test`, Headless, FlaUI and README media projects succeeded; `git diff --check` has no whitespace errors.
- Validation evidence: after video covers chronology scroll/paging and quick capture; review and Settings were additionally inspected live through computer-use. Baseline video отсутствует, потому что implementation уже была начата; baseline screenshot используется как честный fallback.
- Unrelated changes: `.codex-remote-attachments/`, `chat-artifacts/`, `output/` и `obj-codex-area-localization/` оставлены untracked и не изменялись как product files.
- Needs human: продуктовая приёмка и отдельное решение о Git commit/push.
- Residual risks / follow-ups: desktop task-space suite один раз флейкнул на unrelated catalog-removal timeout и сразу прошёл повторно; before/after video pair недоступна, поэтому evidence состоит из baseline screenshot + after video + live walkthrough.

## Approval

Подтверждено пользователем фразой `Спеку подтверждаю` 2026-08-27. Фаза: `EXEC`.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Repo/current-flow inspection | 0.99 | Нет | Draft follow-up SPEC | Нет | Нет | Existing Feed already has safe storage/review/task contracts; UX layer is the primary gap | Existing specs/source/tests/visual evidence |
| SPEC | Implementation-ready design | 0.96 | Нет | Run full post-SPEC review | Нет | Нет | Shared shell + two narrow missing operations cover the accepted design without parallel entities | This SPEC |
| SPEC | Adversarial review and rework | 0.99 | Independent sandbox reviewer unavailable under active policy | Request exact approval | Да | Предстоит | Fixed paging, orphan, DnD index, focus and selector risks; linter/rubric 30/30 | This SPEC |
| EXEC | Approval received and implementation resumed | 0.99 | Нет | Validate the partially applied shell/review patch, then finish staged implementation and UI tests | Нет | Да: пользователь подтвердил SPEC точной фразой | QUEST gate открыт; реализация ограничена утвержденными user-observable scenarios и Non-Goals | Shell, Feed, review, capture, paging/filter and tests |
| EXEC | Shared shell and global surfaces | 0.98 | Нет | Validate create/search/review/settings from both modes | Нет | Нет | App-wide actions moved to `MainScreen`; duplicate task create and Settings tab removed while existing commands/models are reused | `MainScreen`, `MainControl`, `MainWindowViewModel`, selector tests |
| EXEC | Feed viewport, paging, filter and reminder | 0.98 | Нет | Exercise real scroll and queue reminder behavior | Нет | Нет | Chronology owns the remaining viewport, older pages append without replacing visible day instances, area selection is ephemeral, reminder dismissal is queue-version scoped | `FeedControl`, `FeedViewModel`, `FeedControlUiTests` |
| EXEC | Review and quick task capture | 0.97 | Нет | Validate staged surfaces, task relations and safe mutations | Нет | Нет | Review source/navigation/area mutation moved to one shell dialog; quick capture creates either raw note content or a real task plus live link and reuses task status/parents UI | `FeedReviewDialog`, `FeedTaskCaptureService`, ViewModel and UI/unit tests |
| EXEC | Stable edit and multi-block move | 0.98 | Нет | Run raw preservation, conflict and keyboard/mouse UI tests | Нет | Нет | Borderless editor preserves initial geometry; same-day ordered multi-selection moves through one revision-checked atomic operation | Markdown editor ViewModel/control, `FeedMarkdownBlockMoveService`, tests |
| EXEC | Live visual QA and fix/re-review | 0.99 | Baseline video unavailable because implementation was already in progress | Run final serial test gate and Post-EXEC review | Нет | Нет | Isolated desktop profile exposed a zero-sized review dialog, transparent modal surfaces and duplicate Settings title; all were fixed and rechecked in the real app | Shell/review XAML, regression assertion, `output/feed-ux-visual/feed-ux-after.mp4` local-only |
| EXEC | AppAutomation migration and final quality gate | 0.99 | Нет | Hand implementation back for product acceptance; wait for separate Git instruction | Да | Нет | Full Headless/FlaUI runs exposed stale selectors and an incomplete terminal task surface; fixes now preserve global overlay lifecycle and show status/goal/areas/parents without legacy actions | AppAutomation/README media selectors, `FeedReviewDialog`, tests, Post-EXEC review |
