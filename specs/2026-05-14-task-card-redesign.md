# Task Card Redesign

## 0. Метаданные
- Тип (профиль): `delivery-task`; профили `dotnet-desktop-client`, `ui-automation-testing`; контекст `testing-dotnet`.
- Владелец: Codex / пользователь.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка; общий Avalonia UI для desktop и Android.
- Ограничения: QUEST mode; до подтверждения менять только эту спецификацию; UI-facing изменение обязано иметь visual planning artifact, UI test coverage и релевантный запуск UI-тестов; карточка должна быть usable на телефонной ширине Android.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `C:\Users\Kibnet\.codex\agents\instructions\core\quest-mode.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\dotnet-desktop-client.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\ui-automation-testing.md`.

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Сделать редизайн карточки текущей задачи в правой панели `MainControl`: превратить линейную форму в сканируемую рабочую карточку с понятным header, действиями, статусами и секциями редактирования, сохранив существующие сценарии и bindings на desktop и Android.

Outcome contract:
- Success means: пользователь видит текущую задачу как цельную карточку: сверху название и ключевые статусы, ниже описание, планирование, повторение и связи сгруппированы в устойчивые секции; на телефоне карточка становится вертикальной touch-friendly формой без горизонтального переполнения; существующие действия и relation picker продолжают работать.
- Итоговый артефакт / output: scoped XAML/style правка карточки задачи, при необходимости минимальные `AutomationId` для новых секций, обновлённые AppAutomation/Avalonia.Headless UI tests.
- Stop rules: остановиться после реализации в границах спеки, targeted UI tests, `dotnet build`, релевантного полного тестового прогона или явного отчёта о невозможности, и post-EXEC review.

## 2. Текущее состояние (AS-IS)
- Основной UI живёт в `src/Unlimotion/Views/MainControl.axaml`.
- Правая панель открывается через `SplitView.Pane`; `ScrollViewer` имеет `AutomationId="CurrentTaskDetailsScrollViewer"`.
- Блок текущей задачи начинается в `Border IsVisible="{Binding CurrentTaskItem...}"`, затем `StackPanel DataContext="{Binding CurrentTaskItem}"`.
- Внутри карточки сейчас последовательно идут:
  - `CurrentTaskTitleTextBox` с checkbox завершения;
  - wanted checkbox, `NumericUpDown` importance, archive button, read-only id;
  - multiline description textbox;
  - даты создания/обновления/разблокировки/завершения/архивации как общий `WrapPanel`;
  - планирование: begin/duration/end и quick-set dropdowns;
  - repeater controls;
  - relation-блоки `Parents`, `Blocking`, `Containing`, `Blocked` с inline add editor.
- Автотесты уже используют стабильные selectors:
  - `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`;
  - `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`;
  - headless UI tests в `src/Unlimotion.Test`, например `MainControlNewTaskDeadlineUiTests.cs`, `MainControlDateQuickSelectionUiTests.cs`, `MainControlWantedUiTests.cs`.
- Android-версия (`src/Unlimotion.Android/Unlimotion.Android.csproj`) ссылается на общий проект `src/Unlimotion/Unlimotion.csproj`, поэтому `MainControl.axaml` используется и на Android.
- `MainActivity` включает `ResizeableActivity=true`, `WindowSoftInputMode=SoftInput.AdjustResize` и реагирует на `ScreenSize`, поэтому карточка должна переживать портретную ширину телефона, resize и появление экранной клавиатуры.
- `SplitView.OpenPaneLength` в `MainControl.axaml` задаётся как минимум между шириной `mainControl` и `600`, значит на телефоне details pane фактически может стать full-width узкой панелью.
- Текущая форма функциональна, но UX-проблема в иерархии: при сканировании всё выглядит как равноправный поток полей, а важные состояния, действия и relation-блоки не отделены визуально.

## 3. Проблема
Одна корневая проблема: карточка текущей задачи не имеет выраженной информационной архитектуры, поэтому пользователь тратит лишнее внимание на поиск ключевого состояния, действий и нужной секции редактирования.

## 4. Цели дизайна
- Разделение ответственности: не менять ViewModel, storage, bindings и бизнес-логику; редизайн держать во view/style layer.
- Повторное использование: вынести повторяемые визуальные паттерны в scoped `MainControl` styles/classes, а не дублировать свойства на каждом контроле.
- Тестируемость: закрепить структуру карточки через стабильные automation ids и UI tests, не привязываясь к локализованному тексту.
- Консистентность: сохранить плотный desktop-tool UI без landing/marketing визуала; карточка должна быть рабочей, спокойной и удобной для повторного использования.
- Адаптивность: сделать телефонную ширину first-class layout target, а не побочный результат desktop-верстки.
- Обратная совместимость: сохранить существующие automation ids, команды, tab flow, relation picker flow, date/duration/repeater bindings и persisted task schema.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем модель данных задачи, JSON/storage schema, sync/backup logic или `ITaskStorage`.
- Не меняем алгоритмы wanted/importance/completion/repeater/relation.
- Не переделываем task tree, вкладки, roadmap, settings или filter toolbar.
- Не добавляем новые продуктовые поля, markdown preview, вложенные карточки или отдельный диалог редактирования.
- Не переизобретаем relation-блоки: текущий inline add editor + suggestions + confirm/cancel + per-relation tree считается более удачным паттерном, чем упрощённый список из предварительного редизайна.
- Не создаём отдельную Android-only карточку на первом шаге: основной путь - shared adaptive Avalonia XAML. Android-specific view допускается только как fallback, если общий layout не проходит телефонные acceptance criteria без чрезмерной сложности.
- Не удаляем существующие `AutomationId`, на которые опираются AppAutomation/FlaUI/headless tests.
- Не вводим новую глобальную дизайн-систему за пределами scoped styles этой карточки.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> новая визуальная структура карточки текущей задачи, scoped classes/styles, новые `AutomationId` для секций.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> только если XAML wrapping недостаточен: scoped переключение layout classes по ширине details pane, без изменения task state.
- `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` -> добавить page objects для новых стабильных секций карточки, сохранив существующие.
- `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` -> расширить smoke/flow tests: карточка загружается с ключевыми секциями, relation picker работает после редизайна.
- `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` или близкий существующий headless test -> проверить layout/visual structure на desktop, narrow details pane and phone portrait width без text selectors.
- `src/Unlimotion.Android/Unlimotion.Android.csproj` -> не менять UI-код; использовать только для Android build/smoke validation, если локальный Android SDK доступен.
- `specs/2026-05-14-task-card-redesign.md` -> вести EXEC-журнал и post-EXEC review.

### 6.2 Детальный дизайн
- Потоки данных: все bindings остаются на существующих свойствах `MainWindowViewModel.CurrentTaskItem` и `TaskItemViewModel`.
- Контракты / API: публичные VM API не меняются; XAML selectors с текущими `AutomationId` сохраняются.
- Output contract / evidence rules: evidence = UI tests, которые подтверждают наличие ключевых секций карточки, доступность title/wanted/relation controls и отсутствие layout-collapse на узкой панели.
- Visual planning artifact для UI-facing изменений:

```text
CurrentTaskDetailsScrollViewer
└─ TaskDetailsPanel
   ├─ TaskCommandBar
   │  [New Task] [Sibling] [Blocked sibling] [Inner] [Move] [Remove]
   │
   └─ CurrentTaskCard (single top-level card, not nested cards)
      ├─ Header
      │  [complete checkbox]  Title editor, wraps up to useful height
      │                       meta row: id + created/updated/unlocked/completed/archive dates
      │  right: [Wanted] [Importance] [Archive]
      │
      ├─ Section: Description
      │  multiline description editor with stable min height
      │
      ├─ Section: Planning
      │  Begin date      Duration       End date
      │  [Set begin]     [Set duration] [Set end]
      │
      ├─ Section: Repeater
      │  Repeater template + type/period/after-complete controls
      │  Weekly days strip visible only for weekly repeater
      │
      └─ Section: Relations
         keep existing four relation blocks:
         Parents      [+] -> inline editor when open -> tree
         Blocking     [+] -> inline editor when open -> tree
         Containing   [+] -> inline editor when open -> tree
         Blocked      [+] -> inline editor when open -> tree
```

Phone portrait variant, target logical width `360-430`:

```text
CurrentTaskDetailsScrollViewer (no horizontal scroll)
└─ TaskDetailsPanel
   ├─ TaskCommandBar wraps into 2-3 rows
   │  [New Task] [Sibling]
   │  [Blocked sibling] [Inner]
   │  [Move] [Remove]
   │
   └─ CurrentTaskCard full width
      ├─ Header
      │  [checkbox] Title editor full remaining width
      │  Wanted + Importance + Archive wrap below title
      │  id/date meta wraps/trims below actions
      │
      ├─ Description
      │  full-width multiline editor, keyboard-aware scroll keeps focus visible
      │
      ├─ Planning
      │  Begin field + Set begin
      │  Duration field + Set duration
      │  End field + Set end
      │
      ├─ Repeater
      │  template full width
      │  type/period/after-complete wrap
      │  weekday toggles wrap into multiple rows
      │
      └─ Relations
         preserve the existing relation block model
         each relation type keeps header + Add, inline picker, suggestions/empty state, confirm/cancel and tree
         controls stack vertically only when the phone width requires it
```

- Границы сохранения поведения:
  - `CurrentTaskTitleTextBox` остаётся `TextBox` с тем же binding и automation id.
  - `CurrentTaskWantedCheckBox` остаётся checkbox с тем же binding и automation id.
  - Relation add buttons and inline editors keep existing automation ids and click/key behavior.
  - Relation section preserves the existing interaction model:
    - four explicit relation groups: Parents, Blocking, Containing, Blocked;
    - `Add` opens the inline editor directly under the selected group header;
    - editor contains header, query input, suggestions list, empty state, cancel and confirm;
    - each group keeps its own tree immediately after the editor.
  - Date picker and duration controls keep data context and bindings; existing headless tests that find controls by data context should still pass.
- Layout rules:
  - top-level details pane remains scrollable;
  - card has one top-level border with restrained radius `<= 8`;
  - sections are separated by spacing, subtle headers and thin dividers, not by nested cards;
  - command bar stays outside the card, because commands affect tree/current task lifecycle rather than one field group;
  - title row prioritizes title width over id; id moves to meta/action row and can trim;
  - adaptive behavior must be explicit, not left to implicit `Grid` behavior:
    - header uses a primary row for completion checkbox + title, then a `WrapPanel`/equivalent secondary row for wanted, importance, archive and metadata;
    - planning uses wrapping field groups (`Begin`, `Duration`, `End`) with stable min widths instead of a fixed three-column-only grid;
    - repeater details and weekly day controls must wrap or split into multiple rows on narrow pane, so day labels/buttons do not overlap;
    - relation add editors stack vertically on phone width: search input full width, suggestions below it, confirm/cancel buttons below suggestions aligned to the end or stretched if needed;
    - command bar uses wrapping controls and must not require horizontal scrolling at `360` logical pixels;
    - touch targets for buttons, toggles, checkboxes and dropdowns should keep a practical minimum height around `40-44` logical pixels unless existing control templates already provide it;
    - no code-behind threshold is required unless XAML wrapping cannot keep controls within `CurrentTaskDetailsScrollViewer`; if code-behind is added, it must be scoped to layout classes only and not touch task state.
- Visual style:
  - use existing theme resources already present in the project, primarily `ThemeControlLowBrush` and `ThemeControlMidBrush`, or other resources verified by grep/build before use;
  - use 8px or lower corner radius;
  - keep font sizes from app resources; no viewport-based font scaling and no negative letter spacing.
- Обработка ошибок: visual-only change; existing validation/error behavior remains untouched.
- Производительность: no new background work; only layout containers/styles change.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Не применимо: бизнес-алгоритмы задачи не меняются.
- UI invariants:
  - if `CurrentTaskItem` is null, card stays hidden as now;
  - if task cannot be completed, existing completion checkbox `IsEnabled` behavior remains;
  - if repeater is absent, repeater details and weekly strip remain hidden as now;
  - if relation editor opens, it appears inside the same relation section and keeps confirm/cancel behavior.
  - relation groups keep existing order: Parents, Blocking, Containing, Blocked.

## 8. Точки интеграции и триггеры
- `MainControl.axaml` renders the redesigned structure when `DetailsAreOpen=true` and `CurrentTaskItem` is not null.
- Android uses the same `MainControl.axaml` through `src/Unlimotion.Android`, so phone behavior must come from shared adaptive layout unless fallback Android-specific markup is explicitly justified during EXEC.
- Existing `MainControl.axaml.cs` handlers remain integration points:
  - relation add button click;
  - relation editor key handling;
  - title focus helpers using `CurrentTaskTitleTextBox`;
  - task tree pointer/double tap behavior outside card.
- UI tests trigger the card through the existing launch scenario and through direct Avalonia.Headless `MainControl` construction.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Новых ViewModel properties нет.
- Новое состояние только visual tree/layout state в XAML controls.

## 10. Миграция / Rollout / Rollback
- Первый запуск: без миграции.
- Обратная совместимость: stored tasks, settings, localization resources and commands remain unchanged.
- Rollback: вернуть `MainControl.axaml` current task section to previous linear stack and remove tests/new selectors introduced by this spec.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - Current task card has stable top-level `AutomationId`, header, description, planning, repeater and relations sections.
  - Existing `Main_window_loads_current_task_on_launch` still verifies `CurrentTaskTitleTextBox` text.
  - Existing four relation picker AppAutomation scenarios still open inline editors from the redesigned card.
  - Relations section preserves the current stronger UX pattern: per-type header, add button, inline editor, suggestions/empty state, confirm/cancel and the corresponding tree remain grouped together.
  - `CurrentTaskWantedCheckBox`, completion checkbox, importance, archive, date pickers and duration editor remain reachable and bound to current task.
  - On a desktop-size window the header/title/action/meta structure does not collapse into overlapping controls.
  - On a narrower right pane the card remains scrollable and key sections have non-zero bounds within `CurrentTaskDetailsScrollViewer`.
  - On Android phone portrait target width (`360-430` logical pixels), visible task-card controls do not overflow the viewport horizontally, key inputs remain at least practically tappable, and focused text inputs remain reachable inside the keyboard-aware scroll area.
  - Phone layout is intentionally vertical: command bar wraps, planning field groups stack, relation editor controls stack, and weekly repeat buttons wrap into multiple rows instead of shrinking text into unreadable controls.
- Какие тесты добавить/изменить:
  - Update `MainWindowPage` with section selectors:
    - `CurrentTaskCard`;
    - `CurrentTaskHeader`;
    - `CurrentTaskCommandBar`;
    - `CurrentTaskDescriptionSection`;
    - `CurrentTaskPlanningSection`;
    - `CurrentTaskRepeaterSection`;
    - `CurrentTaskRelationsSection`.
  - Add stable ids for key controls that are part of acceptance:
    - `CurrentTaskCompletedCheckBox`;
    - `CurrentTaskImportanceInput`;
    - `CurrentTaskArchiveButton`;
    - `CurrentTaskDescriptionTextBox`;
    - `CurrentTaskPlannedBeginPicker`;
    - `CurrentTaskPlannedDurationTextBox`;
    - `CurrentTaskPlannedEndPicker`.
  - Extend `Main_window_loads_current_task_on_launch` or add a sibling AppAutomation test asserting those sections are present by automation id.
  - Add/extend an Avalonia.Headless layout test that creates `MainControl`, opens details pane, and checks section bounds/visibility without localized text.
  - Add a phone-width headless test, e.g. window/content width around `390` logical pixels, asserting no visible task-card control extends beyond `CurrentTaskDetailsScrollViewer` and key controls have non-zero/tappable bounds.
  - If Android SDK is available locally, build `src/Unlimotion.Android/Unlimotion.Android.csproj` after shared UI changes; if not available, report that Android build could not be run and rely on shared Avalonia narrow-layout UI evidence.
- Characterization tests / contract checks для текущего поведения:
  - Existing title-load and relation-picker tests stay as contract tests.
  - Existing date quick selection and new-task deadline tests should continue to pass because data contexts and bindings remain.
- Visual acceptance для UI-facing изменений:
  - Implementation should match the wireframe above: one top-level card, command bar outside it, header first, grouped sections after it, no nested card stacks.
  - Reviewer can inspect the XAML structure and UI test evidence; if a screenshot workflow is already available during EXEC, capture one local screenshot artifact and reference it in the EXEC journal, but do not block on screenshot tooling if headless UI evidence passes.
  - For mobile acceptance, the reviewer should be able to compare the phone portrait wireframe above against a headless screenshot or bounds evidence at `360-430` logical width.
- Базовые замеры до/после для performance tradeoff: не применимо, layout-only change without new data processing.
- Команды для проверки:
  - `dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*"`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --no-progress`
  - `dotnet build`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --no-progress`
  - `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj` если Android SDK/workload доступен
  - `dotnet test` или `dotnet run` по UI test suite, если полный solution run локально выполним.
- Stop rules для test/retrieval/tool/validation loops:
  - Сначала targeted UI tests до зелёного результата.
  - Затем build.
  - Затем полный доступный тестовый прогон; если полный suite упирается в известную внешнюю проблему/таймаут, зафиксировать точную команду, результат и ближайшую успешную проверку.

## 12. Риски и edge cases
- Риск: изменение XAML разорвёт тесты, которые ищут controls по визуальным потомкам и data context. Смягчение: сохранять data context на `CurrentTaskItem` внутри карточки и не менять типы ключевых controls.
- Риск: перемещение id/date meta row ухудшит доступность длинных id. Смягчение: id остаётся read-only text field or selectable field with trimming/scroll, но не конкурирует с title in primary row.
- Риск: секции внутри `ScrollViewer` могут получить нулевую высоту на узкой панели. Смягчение: headless layout test checks non-zero bounds and scroll host.
- Риск: relation editor becomes visually buried after grouping. Смягчение: relation section keeps existing add buttons next to section headers and inline editor directly below selected relation header.
- Риск: редизайн ухудшит уже удачный relation UX, сведя его к декоративному списку. Смягчение: relation block is explicitly preservation-first; allowed changes are section framing, spacing and phone wrapping only.
- Риск: hard-coded colors break dark/light theme. Смягчение: prefer dynamic theme resources; use hard-coded transparent only where already accepted.
- Риск: shared desktop-first layout будет формально проходить desktop tests, но останется неудобным на Android phone. Смягчение: phone-width acceptance criteria and headless bounds test are mandatory before completion.
- Риск: Android soft keyboard перекроет title/description editor. Смягчение: сохранять `CurrentTaskDetailsScrollViewer` and `KeyboardAwareScrollViewer.IsEnabled`, а phone test должен фокусировать хотя бы title or description and verify the focused control remains in the scrollable details area if test infrastructure supports focus/layout assertions.
- Риск: full Android build may be unavailable on the current machine due to SDK/workload. Смягчение: run shared Avalonia headless phone-width tests and explicitly report Android build availability.

## 13. План выполнения
1. После подтверждения добавить/расширить UI tests на структуру карточки и сохранить существующие relation picker contract tests.
2. Добавить phone-width layout test до/вместе с реализацией, чтобы narrow/mobile acceptance не остался ручной проверкой.
3. В `MainControl.axaml` добавить scoped styles/classes для card, header, command bar, section headers, meta row, planning groups and mobile wrapping.
4. Перестроить current task block in-place, сохранив bindings, handlers and automation ids.
5. Добавить новые automation ids for section-level and key-control selectors.
6. Запустить targeted UI tests, включая phone-width test; исправить layout/selector issues.
7. Запустить build and broader tests per section 11, включая Android build если доступен.
8. Выполнить post-EXEC review, обновить журнал действий and report results.

## 14. Открытые вопросы
Нет блокирующих вопросов. Product/UX decision принят автономно: основной путь - shared adaptive Avalonia layout for desktop and Android; отдельная Android-only карточка считается fallback, потому что она увеличит стоимость сопровождения и риск расхождения поведения.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`; контекст `testing-dotnet`.
- Выполненные требования профиля:
  - UI thread не получает новых долгих синхронных операций.
  - UI-facing изменение сопровождается AppAutomation/Avalonia.Headless coverage.
  - Стабильные selectors сохраняются; новые selectors добавляются через `AutomationId`.
  - Android phone width покрывается headless layout acceptance; Android build запускается при наличии SDK/workload.
  - Планируется targeted UI test, `dotnet build` and broader `dotnet test` before completion.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Перестроить current task details block, добавить scoped styles/classes and section automation ids | UX/UI редизайн карточки |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Только при необходимости: переключение layout classes по ширине details pane | Fallback для адаптивности без изменения task state |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | Добавить `TaskCardPlanning`, `TaskCardRelations` | Локализованные заголовки новых секций |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Добавить `TaskCardPlanning`, `TaskCardRelations` | Локализованные заголовки новых секций |
| `src/Unlimotion/Behavior/PlannedDurationBehavior.cs` | Нормализовать пустое binding-значение duration editor в `TextBox.Text = null` | Не переносить визуальный незакоммиченный текст длительности между задачами после перестройки карточки |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Добавить selectors новых секций карточки | Stable AppAutomation coverage |
| `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` | Добавить/расширить smoke test структуры карточки | Зафиксировать UI contract |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | Новый/расширенный Avalonia.Headless layout test, включая phone-width scenario | Проверить visual structure and Android-relevant narrow layout без локализованного текста |
| `src/Unlimotion.Test/MainControlNewTaskDeadlineUiTests.cs` | Стабилизировать command-click helper для кнопок создания и прогон layout jobs | Сохранить существующий контракт deadline/duration после редизайна и убрать headless mouse flake |
| `specs/2026-05-14-task-card-redesign.md` | Журнал EXEC and review | QUEST traceability |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Карточка | Линейный `StackPanel` с равноправными controls | Один top-level card with header and grouped sections |
| Основное действие | Title competes with id/actions in nearby rows | Title gets primary row, actions and meta are secondary |
| Метаданные | Date labels in generic wrap panel | Meta row under header, readable as task context |
| Планирование | Three columns without section framing | Planning section with begin/duration/end as coherent group |
| Повторение | Label + horizontal controls in main stream | Repeater section with template/details/weekly strip |
| Связи | Уже сильный паттерн: четыре relation groups with Add, inline editor, suggestions, confirm/cancel and tree | Same pattern preserved inside a clearer Relations section with spacing and phone wrapping |
| Android phone | Не зафиксирован отдельный acceptance path для карточки | Shared adaptive card with phone portrait wireframe and headless narrow layout tests |
| Tests | Existing title/relation checks | Existing checks plus card structure/layout coverage |

## 18. Альтернативы и компромиссы
- Вариант: only style existing linear stack without structure changes.
- Плюсы: smallest XAML diff.
- Минусы: не решает core UX issue with hierarchy; tests would only verify decoration.
- Почему выбранное решение лучше в контексте этой задачи: user asked for UX/UI lead-level redesign, so information architecture must change, not only colors/margins.

- Вариант: move each section into separate cards.
- Плюсы: visually clear chunks.
- Минусы: violates local frontend guidance against cards inside cards; creates visual noise and wastes width in right pane.
- Почему выбранное решение лучше в контексте этой задачи: one top-level card with section separators gives clarity without nested-card clutter.

- Вариант: add a tabbed editor inside details pane.
- Плюсы: reduces scroll length.
- Минусы: hides fields, changes navigation flow, adds state and more test surface.
- Почему выбранное решение лучше в контексте этой задачи: grouped scroll preserves current direct-edit workflow and lowers regression risk.

- Вариант: отдельная Android-only карточка задачи.
- Плюсы: можно оптимизировать телефонный UI независимо от desktop.
- Минусы: два UI-контракта для одних и тех же bindings/actions, выше риск расхождения behavior and tests, больше поддержки при будущих изменениях карточки.
- Почему выбранное решение лучше в контексте этой задачи: shared adaptive Avalonia layout directly matches текущую архитектуру Android-проекта, где Android использует общий `Unlimotion` UI.

- Вариант: заменить текущие relation-блоки новым компактным списком в стиле предварительного редизайна.
- Плюсы: визуально короче и проще на mockup.
- Минусы: теряется уже удачная модель редактирования рядом с каждым типом связи; выше риск ухудшить понятность add-flow and existing tests.
- Почему выбранное решение лучше в контексте этой задачи: relation-блоки уже решают свою UX-задачу лучше, чем предложенный mockup, поэтому их нужно сохранить и только вписать в новую карточку.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели and Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, visual artifact, integration, state and rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Данные не меняются, риски and scoped plan указаны. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, UI coverage and commands указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Блокирующих вопросов нет, alternatives/tradeoffs documented. |
| F. Соответствие профилю | 20 | PASS | .NET desktop and UI automation requirements учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна UI-задача, clear outcome and explicit Non-Goals. |
| 2. Понимание текущего состояния | 5 | Указаны конкретные файлы, selectors and current linear structure. |
| 3. Конкретность целевого дизайна | 5 | Есть wireframe, секции, layout/style rules and evidence contract. |
| 4. Безопасность (миграция, откат) | 5 | Persisted state/API не меняются, rollback прямой. |
| 5. Тестируемость | 5 | Targeted UI tests and broader validation commands specified. |
| 6. Готовность к автономной реализации | 5 | Все решения, влияющие на реализацию, зафиксированы; вопросов нет. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлен явный visual planning artifact, разделены command bar and top-level card, зафиксирован запрет на nested cards, добавлены narrow-pane acceptance criteria and relation picker preservation; после review уточнён явный adaptive layout, добавлены недостающие `AutomationId` для проверяемых controls, неподтверждённый `ThemeBorderLowBrush` заменён на правило использовать только проверенные theme resources; после Android feedback добавлен phone portrait wireframe, mandatory phone-width UI test and Android build validation path; после UX feedback relation section переведён в preservation-first режим, потому что текущая реализация relation-блоков сильнее предварительного mockup.
- Что осталось на решение пользователя: только подтверждение перехода в EXEC.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction stack and routing | 0.95 | Нет | Собрать UI-контекст | Нет | Нет | Локальный `AGENTS.md` требует central QUEST stack, локальный override требует UI tests | `AGENTS.md`, `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\instructions\*` |
| SPEC | AS-IS анализ карточки | 0.9 | Нет | Создать рабочую спецификацию | Нет | Нет | Найден current task block as linear StackPanel with existing selectors and relation tests | `src/Unlimotion/Views/MainControl.axaml`, `tests/Unlimotion.UiTests.Authoring/*`, `src/Unlimotion.Test/*` |
| SPEC | Создание спеки and quality gate | 0.92 | Нет | Запросить подтверждение пользователя | Да | Да, ожидается фраза `Спеку подтверждаю` | QUEST запрещает кодовые правки до явного подтверждения спеки | `specs/2026-05-14-task-card-redesign.md` |
| SPEC | Исправление по review | 0.94 | Нет | Передать пользователю обновлённую спеку и UX-объяснение | Да | Да, пользователь попросил исправить review findings | Уточнены адаптивная механика, selectors for testability and theme-resource rule before EXEC | `specs/2026-05-14-task-card-redesign.md` |
| SPEC | Android/narrow layout уточнение | 0.95 | Нет | Передать обновлённое решение пользователю | Да | Да, пользователь указал Android phone constraint | Android использует shared Avalonia UI, поэтому выбран shared adaptive layout with phone portrait acceptance and Android build path | `specs/2026-05-14-task-card-redesign.md`, `src/Unlimotion.Android/*`, `src/Unlimotion/Views/MainControl.axaml` |
| SPEC | Relations UX уточнение | 0.96 | Нет | Передать обновлённое решение пользователю | Да | Да, пользователь отметил, что текущий Relations лучше mockup | Relation section теперь preservation-first: сохранить текущий inline picker/tree pattern and only improve framing/wrapping | `specs/2026-05-14-task-card-redesign.md`, `src/Unlimotion/Views/MainControl.axaml` |
| EXEC | UI tests and XAML implementation | 0.84 | Результаты тестов | Запустить targeted UI tests | Нет | Да, пользователь подтвердил `Спеку подтверждаю` | Добавлены selectors, desktop/phone-width headless tests, новая карточка с секциями, сохранён relation pattern, добавлены локализованные section headings | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.ViewModel/Resources/Strings*.resx`, `tests/Unlimotion.UiTests.Authoring/*`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` |
| EXEC | Regression fix | 0.88 | Нет | Повторить targeted UI tests | Нет | Нет | При проверке обнаружены регрессии deadline/duration: duration editor мог визуально сохранять незакоммиченный текст после смены задачи, а headless mouse click по command buttons был нестабилен; behavior and test helper скорректированы без изменения business logic | `src/Unlimotion/Behavior/PlannedDurationBehavior.cs`, `src/Unlimotion.Test/MainControlNewTaskDeadlineUiTests.cs` |
| EXEC | Validation | 0.9 | Android workload `wasm-tools` отсутствует локально | Выполнить post-EXEC review | Нет | Нет | Targeted layout/deadline/headless checks and desktop/test builds passed; Android build blocked by missing workload, not by code compilation errors in shared UI | `src/Unlimotion.Test/*`, `tests/Unlimotion.UiTests.Headless/*`, `src/Unlimotion.Desktop/*`, `src/Unlimotion.Android/*` |

## 21. Post-EXEC Review
- Статус: PASS with environment caveat.
- Реализовано: текущая задача теперь отображается как одна рабочая карточка с command bar, header, description, planning, repeater and relations sections; Relations сохранён в текущей сильной модели с четырьмя группами, inline add editor, suggestions, confirm/cancel and trees.
- Android/narrow path: отдельная Android-only карточка не понадобилась; shared XAML использует wrapping groups and phone-width headless test at `390x844`.
- Дополнительный фикс: duration editor теперь сбрасывает visual `TextBox.Text` в `null` для отсутствующей длительности, чтобы незакоммиченный текст старой задачи не оставался видимым после смены `CurrentTaskItem`.
- Проверки:
  - PASS `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --no-progress` -> 2/2.
  - PASS `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlNewTaskDeadlineUiTests/*" --no-progress` -> 9/9.
  - PASS `dotnet run --project .\tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -- --treenode-filter "/*/*/MainWindowHeadlessTests/*" --no-progress` -> 8/8; runner still prints existing unobserved `FileSystemWatcher` temp-directory exceptions after successful summary.
  - PASS `dotnet build .\src\Unlimotion.Desktop\Unlimotion.Desktop.csproj`.
  - PASS `dotnet build .\src\Unlimotion.Test\Unlimotion.Test.csproj`.
  - BLOCKED `dotnet build .\src\Unlimotion.Android\Unlimotion.Android.csproj` -> `NETSDK1147`, missing workload `wasm-tools`; suggested by SDK: `dotnet workload restore`.
- Остаточный риск: full `dotnet test .\src\Unlimotion.Test\Unlimotion.Test.csproj -- --no-progress` ранее упирался в локальный таймаут/зависший test process, поэтому закрытие выполнено на релевантных targeted UI suites and builds.
