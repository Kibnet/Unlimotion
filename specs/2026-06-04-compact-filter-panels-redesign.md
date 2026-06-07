# Compact Filter Panels Redesign

## 0. Метаданные
- Тип (профиль): `delivery-task`; профили `dotnet-desktop-client`, `ui-automation-testing`, `refactor-local`; контекст `testing-dotnet`; governance overlay `refactoring-policy`.
- Владелец: Codex / пользователь.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка.
- Ограничения: QUEST mode; до подтверждения менять только эту спецификацию; UI-facing изменение обязано иметь visual planning artifact, UI test coverage и релевантный запуск UI-тестов.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `C:\Users\Kibnet\.codex\agents\instructions\core\quest-mode.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\dotnet-desktop-client.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\ui-automation-testing.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\refactor-local.md`, `specs/2026-05-06-filter-toolbar-mobile-redesign.md`.

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Передизайнить панели фильтров во всех вкладках приложения с фильтрами, чтобы они были компактнее, понятнее по смысловым группам и устойчиво работали на узком мобильном разрешении.

Outcome contract:
- Success means: task-вкладки и Roadmap используют единый компактный filter toolbar pattern; поиск, фильтры и reset доступны без горизонтального overflow; фильтры сгруппированы по смыслу и визуально сканируются быстрее.
- Итоговый артефакт / output: scoped XAML/C# правки filter toolbar layout/styles, локальный helper/behavior для переиспользования adaptive layout, обновлённые Avalonia.Headless UI-тесты.
- Stop rules: остановиться после реализации в пределах этой спеки, targeted UI tests, `dotnet build`, полного доступного test run и post-EXEC review; если реализация требует изменить VM/API или продуктовую семантику фильтров, вернуться к пользователю.

## 2. Текущее состояние (AS-IS)
- Основные task-вкладки живут в `src/Unlimotion/Views/MainControl.axaml`.
- Для task-вкладок уже есть `Grid Classes="FilterToolbar"` с adaptive logic в `src/Unlimotion/Views/MainControl.axaml.cs`: desktop = фильтры слева / поиск справа, narrow = поиск сверху / фильтры снизу.
- Старая spec `specs/2026-05-06-filter-toolbar-mobile-redesign.md` закрыла narrow overflow для task-вкладок, но не меняла саму композицию фильтров.
- Сейчас элементы фильтров внутри `WrapPanel.FilterToolbarItems` остаются плоским списком: `ComboBox`, `CheckBox`, `Label`, `CalendarDatePicker`, `Button` с повторяющимися `Margin="0,0,10,0"`.
- Плоский список затрудняет сканирование: emoji include/exclude, date presets/custom range, state toggles, sort и reset визуально выглядят как один ряд равнозначных контролов.
- Roadmap filter panel живёт отдельно в `src/Unlimotion/Views/GraphControl.axaml` как простой `WrapPanel`; он не использует `Grid.FilterToolbar`, `SearchBar` и adaptive helper из `MainControl`.
- `src/Unlimotion/Views/SearchControl/SearchControlStyles.axaml` задаёт search min-width через `AppSearchBarMinWidth`; существующая narrow logic переопределяет min/max width только для task toolbar search.
- Существующие UI tests:
  - `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` проверяет adaptive layout task toolbar на narrow/wide/details-pane shrink.
  - `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` проверяет доступность reset button на task tabs и Roadmap, а также reset behavior.
  - Roadmap имеет богатое покрытие в `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, но нет отдельной проверки компактной панели фильтров.

## 3. Проблема
Одна корневая проблема: фильтры во вкладках представлены как разрозненные плоские ряды контролов, поэтому они занимают лишнее место, плохо читаются и не имеют единого compact/adaptive контракта во всех вкладках, особенно на узкой ширине.

## 4. Цели дизайна
- Разделение ответственности: оставить VM, фильтрацию, поиск, сортировку и reset-команды без изменения; менять presentation layer и локальный layout helper.
- Повторное использование: один shared toolbar pattern и один adaptive layout helper для task-вкладок и Roadmap.
- Тестируемость: закрепить headless UI tests на narrow/wide geometry, видимость reset/search/ключевых групп и отсутствие overflow.
- Консистентность: одинаковые отступы, высота контролов, группировка и search placement во всех вкладках.
- Обратная совместимость: сохранить bindings, reset semantics, automation id существующих reset buttons, публичный VM/API и persisted settings.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем алгоритмы фильтрации, поиска, fuzzy search, сортировки, roadmap projection или reset-команды.
- Не меняем persisted settings, model fields, storage format или миграции данных.
- Не меняем набор доступных фильтров и их бизнес-семантику.
- Не добавляем отдельный slide-out/filter drawer, modal или новый навигационный flow.
- Не удаляем существующие automation id; новые selectors можно добавлять только аддитивно.
- Не переделываем task tree, task details pane, roadmap canvas, viewport/minimap overlays.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/FilterToolbarLayout.cs` или близкий локальный helper -> shared adaptive behavior для всех `Grid.FilterToolbar`: desktop/narrow row/column placement, search width limits, bounds subscriptions/disposal.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> удалить дублируемую toolbar-specific layout логику и подключить helper к текущему `MainControl` lifecycle.
- `src/Unlimotion/Views/GraphControl.axaml.cs` -> подключить тот же helper к Roadmap lifecycle, без изменения roadmap graph logic.
- `src/Unlimotion/Views/MainControl.axaml` -> заменить плоские `WrapPanel` списки на компактные структурные группы внутри `FilterToolbarItems`; добавить shared classes и automation ids для групп при необходимости.
- `src/Unlimotion/Views/GraphControl.axaml` -> привести Roadmap panel к `Grid Classes="FilterToolbar"` с `SearchBar` и теми же compact group classes.
- `src/Unlimotion/ViewModel/Resources/Strings.resx` и `Strings.ru.resx` -> не менять по умолчанию; новые строки допустимы только для tooltips/accessibility names, если без них конкретный control остаётся непонятным.
- `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` -> расширить проверки task toolbar на компактные группы и отсутствие overflow.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` или новый focused test file -> добавить Roadmap toolbar responsive/compact coverage.

### 6.2 UX-варианты
Текущий low-chrome inline вариант признан недостаточным: он слишком похож на текущую панель и не даёт качественного выигрыша в мобильной плотности. Ниже реальные варианты, между которыми нужно выбрать продуктовый UX.

#### Вариант A. Dense inline toolbar
```text
Desktop:
| [include] [exclude] [sort] [completed] [archived] [Reset]      [Search] |

Mobile:
| [Search....................] |
| [include] [exclude] [sort]   |
| [completed] [archived] Reset |
```
- Плюсы: минимальный риск, все фильтры сразу видны, мало новой логики.
- Минусы: это почти текущая модель; date/custom вкладки всё равно раздувают toolbar; UX-выигрыш слабый.
- Итог review: не выбирать как основной вариант, потому что он плохо отвечает на ожидание "лучше и компактнее".

#### Вариант B. Priority toolbar + overflow filter panel (рекомендуется)
```text
Desktop:
| [Search................................] [Sort] [Filters] [Reset] |
| Active: #tag  excluding:#tag  completed  date:today                |

Mobile:
| [Search..............................] |
| [Filters] [Sort] [Reset]              |
| Active: completed, date:today          |

Filters panel / flyout:
| Tags        [include] [exclude]        |
| State       [completed] [archived]     |
| Date        [preset] [custom]          |
|             [from] [to]                |
```
- Плюсы: default mobile занимает 2-3 короткие строки вместо постоянного набора всех контролов; фильтры остаются понятными через одну кнопку `Filters` и summary активных фильтров; date/custom controls не раздувают вкладку, пока пользователь не открыл панель.
- Минусы: часть фильтров становится на один клик дальше; нужен flyout/dropdown и UI-тест открытия панели.
- Почему рекомендуется: это единственный вариант из списка, который заметно уменьшает занимаемое место и при этом сохраняет discoverability через явную кнопку `Filters` и active summary.

#### Вариант C. Active chips + Filters button
```text
Desktop:
| [Search................................] [Filters] [Reset] |
| [completed x] [archived x] [date:today x] [sort:created]   |

Mobile:
| [Search..............................] |
| [Filters] [Reset]                     |
| [completed x] [date:today x]          |
```
- Плюсы: самый чистый default view; пользователь видит только активные условия.
- Минусы: если фильтры не активны, доступность хуже: всё спрятано за `Filters`; нужно аккуратно определить active summary для каждого типа фильтра.
- Когда выбирать: если приоритетом является максимально чистый мобильный экран, даже ценой меньшей discoverability.

#### Вариант D. Horizontal scroll filter strip
```text
Mobile:
| [Search..............................] |
| < [include] [exclude] [sort] [state] [date] [Reset] > |
```
- Плюсы: минимальная высота.
- Минусы: горизонтальная прокрутка toolbar в desktop/mobile приложении плохо читается, ухудшает keyboard/accessibility и легко прячет reset/важные фильтры.
- Итог review: не выбирать.

Выбранный вариант: B (`Priority toolbar + overflow filter panel`). Пользователь выбрал этот вариант 2026-06-05.

Причина выбора: вариант B даёт реальное уплотнение без превращения фильтров в полностью скрытый drawer.

### 6.3 Детальный дизайн для варианта B
- Потоки данных: все bindings остаются текущими (`Search.SearchText`, `EmojiFilters`, `EmojiExcludeFilters`, `ShowCompleted`, `ShowArchived`, `ShowWanted`, date filters, sort definitions, `ResetTaskFiltersCommand`).
- Контракты / API: persisted model/storage не меняются; допустимы calculated UI-only properties/helpers для active summary, если без них нельзя сделать понятный compact toolbar.
- Output contract / evidence rules:
  - геометрия UI подтверждается Avalonia.Headless assertions;
  - проверяется отсутствие horizontal overflow относительно toolbar bounds;
  - проверяется видимость search, reset, `Filters` button, sort control where applicable and active summary;
  - проверяется открытие filter panel/flyout и наличие tab-specific filter controls внутри панели;
  - video evidence заменён на screenshot evidence по требованию пользователя;
  - `before` screenshots: снять текущий UI до реализации для representative states;
  - `after` screenshots: снять те же states после реализации;
  - agent visual review loop: сравнить `before`/`after`, зафиксировать вывод, дорабатывать UI до удовлетворительного результата или до явного blocker;
  - screenshot artifacts сохранять как local-only evidence under `artifacts/filter-toolbar-redesign/`, не коммитить по умолчанию.
- Visual planning artifact для UI-facing изменений:

```text
Desktop / wide (>= 900px), recommended option B
+--------------------------------------------------------------------------------+
| [ Search task ...................................... ] [Sort] [Filters] [Reset] |
| Active: completed, archived, date:today                                      |
+--------------------------------------------------------------------------------+

Narrow / mobile (~320-420px), recommended option B
+------------------------------------+
| [ Search task ....................] |
| [Filters] [Sort] [Reset filters]   |
| Active: completed, date:today      |
+------------------------------------+

Filter panel / flyout opened
+------------------------------------+
| Tags:   [include] [exclude]        |
| State:  [completed] [archived]     |
| Date:   [preset] [custom]          |
|         [from date] [to date]      |
+------------------------------------+
```

- Visual contract:
  - toolbar itself remains unframed page UI, not a large card;
  - default toolbar shows only primary actions: search, filters entry point, sort where applicable, reset and active summary;
  - secondary filter controls live in the filter panel/flyout, not in the default toolbar;
  - active summary is compact text/chips, not a second full filter row;
  - narrow default targets search + one command row + optional one-line active summary;
  - filter panel may be taller because it is temporary and user-requested, but it must fit narrow width and avoid horizontal overflow.
- Границы сохранения поведения:
  - search, sort, filter and reset semantics remain unchanged;
  - Roadmap gains the same compact toolbar entry point and filter panel pattern but keeps existing filter semantics;
  - reset buttons remain visible and use existing confirmation behavior.
- Обработка ошибок:
  - if toolbar width is not measured (`Bounds.Width <= 0`), helper defers mode change until next layout/bounds pass;
  - helper ignores malformed toolbar without `FilterToolbarItems` or search child, so unrelated grids are unaffected.
- Производительность:
  - visual tree traversal remains bounded by one screen/control;
  - helper should update only on mode change or measured-width change that affects search width;
  - no background graph rebuild or task query should be triggered by toolbar layout changes.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Toolbar priority taxonomy for option B:
  - `Primary`: search, `Filters` entry point, reset, and sort where sort is a common high-frequency action for the current tab.
  - `Summary`: compact active-filter text/chips; hidden or very short when no filters are active.
  - `Panel filters`: emoji include/exclude, state toggles, date/timing controls and less-frequent tab-specific controls.
- Desktop adaptive rule:
  - `FilterToolbar` has two columns `*,Auto`;
  - search gets the widest stable area;
  - primary actions stay in the auto area;
  - active summary can use a second low-height row only when non-empty.
- Narrow adaptive rule:
  - threshold remains around existing `520px` unless tests show a clear need to adjust;
  - `SearchBar` row 0 / column 0, stretch, max width = toolbar width;
  - primary actions row 1 / column 0;
  - active summary row 2 / column 0 only if non-empty.
- Compact sizing rule:
  - default toolbar should not host every filter control;
  - keep minimum touch-friendly height comparable to current controls/search height;
  - avoid dedicated label-only rows in default toolbar;
  - avoid text clipping in buttons and controls at Russian and English resource lengths.
- Filter panel rule:
  - panel/flyout may use short section labels because it is opened on demand;
  - panel width must fit the narrow viewport;
  - tab-specific controls must remain discoverable inside the panel.

## 8. Точки интеграции и триггеры
- `MainControl` lifecycle: attach/detach, bounds change, tab selection change, details pane resize.
- `GraphControl` lifecycle: attach/detach and bounds change for Roadmap toolbar.
- XAML classes: only controls inside `Grid.FilterToolbar` and `WrapPanel.FilterToolbarItems` receive compact styling.
- Tests instantiate `MainControl` and/or `GraphControl` through existing headless patterns and run dispatcher layout jobs.

## 9. Изменения модели данных / состояния
- Новых VM/model fields нет.
- Persisted state не меняется.
- UI-only state:
  - helper may store observed toolbar subscriptions and current narrow class;
  - classes such as `NarrowFilterToolbar`, `FilterToolbarPrimaryActions`, `FilterToolbarSummary`, `FilterPanelGroup` are presentation-only;
  - active summary/count can be calculated UI-only state if implementation needs it; it must not be persisted.

## 10. Миграция / Rollout / Rollback
- Первый запуск: без миграции.
- Обратная совместимость: bindings, commands, automation ids and persisted settings remain intact.
- Rollback:
  - revert `MainControl.axaml`, `GraphControl.axaml`, code-behind/helper and related tests;
  - no data cleanup needed.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - На ширине около `320-420px` во всех task-вкладках default toolbar показывает search, `Filters`, reset, sort where applicable and optional active summary without horizontal overflow.
  - Roadmap на узкой ширине использует такой же compact toolbar entry point: search сверху, `Filters`/reset рядом ниже, active summary не ломает ширину.
  - Default narrow toolbar не становится выше search + one command row + one optional active-summary row.
  - Открытая filter panel/flyout показывает tab-specific filters and fits narrow width without horizontal overflow.
  - На desktop width toolbar remains compact: search receives primary width and secondary controls do not push content into multi-row clutter unless active summary is non-empty.
  - Existing reset behavior tests continue passing.
  - Search/filter/sort bindings remain unchanged.
- Какие тесты добавить/изменить:
  - Обновить `MainControlFilterToolbarResponsiveUiTests`:
    - проверять default compact toolbar height/geometry;
    - проверять `Filters` button visibility and opening panel;
    - проверять tab-specific controls inside panel;
    - проверять narrow no-overflow для всех task tabs;
    - сохранить wide layout assertion.
  - Добавить Roadmap compact toolbar UI test:
    - открыть Roadmap tab or instantiate `GraphControl`;
    - проверить search/reset/`Filters` on narrow width;
    - открыть panel and verify Roadmap-specific controls;
    - проверить no-overflow and visibility.
  - При необходимости расширить reset availability test only for selectors stability, не меняя behavior assertions.
- Characterization tests / contract checks для текущего поведения:
  - существующие reset behavior tests и current responsive tests являются safety net для unchanged behavior;
  - helper extraction должен проходить existing `MainControlFilterToolbarResponsiveUiTests` before/after behavior.
- Visual acceptance для UI-facing изменений:
  - сравнить фактическую геометрию с wireframe option B: search first row in narrow, command row below, optional summary row only if non-empty;
  - verify all secondary filters moved out of default toolbar into the panel/flyout;
  - check visible button/control text is not clipped by bounds in English/Russian via headless geometry where feasible.
- UI video evidence для UI-facing фич:
  - Не использовать video evidence.
  - Обязательный visual evidence: `before` и `after` screenshots для одинаковых states.
  - Минимальный набор screenshots:
    - task tab narrow default toolbar;
    - task tab narrow filter panel opened;
    - date-heavy tab with custom period opened in filter panel;
    - Roadmap narrow default toolbar;
    - Roadmap narrow filter panel opened;
    - desktop wide toolbar for regression sanity.
  - Agent обязан сам визуально проверить screenshots и повторять дизайн-итерацию, пока результат не удовлетворяет acceptance criteria или пока не найден blocker, требующий решения пользователя.
  - Пути к screenshots и краткий visual review вывод фиксировать в EXEC journal/final report.
- Базовые замеры до/после для performance tradeoff: не применимо; layout-only change with bounded visual traversal.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --no-progress`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*" --no-progress`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*Filter*" --no-progress` or exact new Roadmap toolbar test filter after creation
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --no-progress`
- Stop rules для test/retrieval/tool/validation loops:
  - First run targeted UI tests until green.
  - Then run build.
  - Then run full test project if locally feasible.
  - If full run fails due unrelated known stale test or environment blocker, capture exact failing test/error and report next-best passing evidence.

## 12. Риски и edge cases
- Риск: hiding filters behind a `Filters` button makes them less immediately visible. Смягчение: button is always visible, active summary shows applied constraints, panel tests verify discoverability.
- Риск: helper extraction could break existing task toolbar reflow. Смягчение: keep existing responsive tests green before expanding Roadmap behavior.
- Риск: Roadmap GraphControl has its own lifecycle and expensive graph updates. Смягчение: layout helper must not touch graph VM properties or trigger projection rebuild; test existing roadmap search no-rebuild coverage remains relevant.
- Риск: active summary/count requires computed UI state and can grow too long. Смягчение: cap summary to one line with ellipsis or `+N`; full details remain in panel.
- Риск: date custom range has more controls than other groups. Смягчение: date controls live inside on-demand panel; narrow tests should cover custom mode for at least one date tab.
- Риск: filter panel could become a large card-like block. Смягчение: panel is transient, functional, compact and not a page section; no decorative nested card look.
- Риск: full solution test run may be slow or affected by known unrelated failures. Смягчение: targeted UI evidence is mandatory; full run outcome is reported precisely.

## 13. План выполнения
1. Add/adjust targeted UI test coverage for recommended option B: compact default toolbar, `Filters` button, opened panel, Roadmap narrow toolbar.
2. Extract existing `MainControl` filter toolbar adaptive logic into a local helper/behavior with disposal if still useful after option B layout.
3. Rewire `MainControl` to use compact priority toolbar without changing filter behavior.
4. Move secondary task filters into on-demand filter panel/flyout while keeping sort/reset/search immediately reachable.
5. Convert Roadmap top filter panel to the same compact entry point and filter panel pattern.
6. Add active summary/count UI-only logic only as much as needed for clarity.
7. Run targeted tests, build, full available test run.
8. Perform post-EXEC review, fix unambiguous findings, rerun affected checks and update this spec journal.

## 14. Открытые вопросы
Нет блокирующих UX-вопросов. Пользователь выбрал вариант B и заменил video evidence на before/after screenshot evidence with agent visual review loop.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`, `refactor-local`; контекст `testing-dotnet`.
- Выполненные требования профиля:
  - UI thread не блокируется долгими синхронными операциями; helper только меняет layout properties.
  - UI-facing изменение планируется с visual planning artifact и UI tests.
  - Существующие automation ids сохраняются; новые selectors добавляются аддитивно.
  - Refactor bounded: выделяется только текущая toolbar layout logic для повторного использования в Roadmap.
  - Перед завершением EXEC планируется targeted UI tests, `dotnet build` и полный доступный test run.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/FilterToolbarLayout.cs` | Новый локальный helper/behavior для adaptive toolbar layout | Переиспользовать существующий task toolbar behavior в Roadmap |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Подключить helper и удалить дублируемую toolbar layout логику | Снизить дублирование, сохранить behavior |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Подключить helper к Roadmap toolbar lifecycle | Roadmap получает тот же compact/adaptive contract |
| `src/Unlimotion/Views/MainControl.axaml` | Compact priority toolbar + on-demand filter panel for task tabs | Реально уменьшить default toolbar вместо перекладки всех контролов |
| `src/Unlimotion/Views/GraphControl.axaml` | Roadmap panel -> compact priority toolbar + on-demand filter panel | Унифицировать вкладки приложения |
| `src/Unlimotion/ViewModel/Resources/Strings.resx` | Только если нужны `Filters`, active summary, tooltips/accessibility names | Английская локализация новых visible/action strings |
| `src/Unlimotion/ViewModel/Resources/Strings.ru.resx` | Только если нужны `Filters`, active summary, tooltips/accessibility names | Русская локализация новых visible/action strings |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | Расширить compact/responsive assertions | UI coverage task-вкладок |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` или новый test file | Добавить Roadmap compact toolbar test | UI coverage Roadmap |
| `specs/2026-06-04-compact-filter-panels-redesign.md` | Журнал EXEC и review | QUEST traceability |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Task filter layout | Flat `WrapPanel` of all controls | Priority toolbar + on-demand filter panel |
| Roadmap filter layout | Separate plain `WrapPanel` with all controls | Same priority toolbar + filter panel pattern |
| Narrow width | Task tabs reflow but still carry many controls; Roadmap not unified | Search + command row + optional active summary; secondary controls in panel |
| Scannability | Controls visually равнозначны | Primary actions visible; secondary filters grouped inside explicit `Filters` panel |
| Behavior | Existing bindings/commands | Same bindings/commands |
| UI tests | Task responsive + reset tests | Task/Roadmap compact toolbar + filter panel open/contents + reset tests |

## 18. Альтернативы и компромиссы
- Варианты A-D зафиксированы в section 6.2 как основные UX alternatives.
- Выбранная рекомендация: вариант B.
- Почему не A: слишком похож на текущую панель и не решает замечание пользователя.
- Почему не C по умолчанию: самый чистый default view, но хуже discoverability для неактивных фильтров.
- Почему не D: horizontal scroll экономит высоту, но прячет фильтры и ухудшает accessibility/keyboard flow.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, дизайн-цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | UX-варианты, recommendation, ответственность, интеграция, UI contract, ошибки, perf и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Данные и VM/API не меняются, риски и rollback понятны. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, UI tests, visual acceptance и команды указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Вариант B выбран пользователем; план scoped, alternatives covered. |
| F. Соответствие профилю | 20 | PASS | .NET desktop, UI automation и local refactor требования учтены. |

Итог: ГОТОВО К ПОДТВЕРЖДЕНИЮ EXEC

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна UI-проблема, explicit output, stop rules and Non-Goals. |
| 2. Понимание текущего состояния | 5 | Указаны конкретные XAML/code-behind/test files и отличие task/Roadmap panels. |
| 3. Конкретность целевого дизайна | 5 | Есть UX options, recommended option B, wireframes and file responsibilities. |
| 4. Безопасность (миграция, откат) | 5 | Нет data/VM migration, rollback by reverting scoped files. |
| 5. Тестируемость | 5 | Targeted UI tests, reset safety net, visual acceptance and commands specified. |
| 6. Готовность к автономной реализации | 5 | Option B selected; implementation sequence and risk controls are concrete. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению после формального подтверждения спеки

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-04-compact-filter-panels-redesign.md`; instruction stack `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `refactor-local`, `refactoring-policy`, local `AGENTS.override.md`; selected profiles `dotnet-desktop-client`, `ui-automation-testing`, `refactor-local`; open questions none; planned files listed in section 16.
- Decision: можно запрашивать формальное подтверждение EXEC.
- Review passes:
  - Scope/Evidence pass: inspected existing May filter toolbar spec, `MainControl.axaml`, `MainControl.axaml.cs`, `GraphControl.axaml`, `SearchControl`, current responsive/reset/Roadmap tests.
  - Contract pass: spec keeps code changes blocked until approval, preserves VM/API and automation ids, includes UI tests and visual artifact as required.
  - Adversarial risk pass: checked Roadmap separate lifecycle, date custom overflow, Russian label clipping, helper extraction risk, visual evidence gap and user challenge that inline low-chrome still looked like current toolbar.
  - Re-review after fixes / Fix and re-review: added UX options A-D, user selected option B, changed acceptance to priority toolbar + filter panel, replaced video evidence with before/after screenshots and agent visual review loop.
  - Stop decision: PASS because UX choice is made, required evidence, acceptance criteria and validation plan are concrete.
- Evidence inspected: file search output for `FilterToolbar`, current XAML snippets for task tabs and Roadmap, existing responsive/reset test files, central instruction docs.
- Depth checklist:
  - Scope drift / unrelated changes: spec limits implementation to filter panels/helper/tests/resources.
  - Acceptance criteria: covers all task tabs, Roadmap, narrow, desktop, filter panel, reset, screenshot visual evidence and binding preservation for option B.
  - Validation evidence: commands listed; no EXEC validation yet by design.
  - Unsupported claims: current-state claims grounded in inspected files.
  - Regression / edge case: roadmap lifecycle, filter discoverability, active summary length, date custom controls, toolbar height, screenshot comparability, helper extraction and full test instability captured.
  - Comments/docs/changelog: no changelog required; comments only if hidden helper lifecycle context needs it.
  - Hidden contract change: public VM/API/persisted state explicitly unchanged.
  - Manual-review challenge: likely review pressure would target "this is still current UI" or missing visual evidence; spec now uses selected option B and mandatory before/after screenshots.
- No-findings justification: UX choice is resolved; screenshot loop and UI tests provide concrete evidence.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | visual density | Initial wireframe used visible group headers (`Tags`, `State`, `Actions`) that would add label-only rows and likely make mobile toolbar taller and uglier. | Replace with structural grouping, no visible group headers by default, and update wireframe. | fixed |
| HIGH | UX alternatives | Revised low-chrome inline design still stayed too close to the current toolbar and did not present meaningful alternatives. | Add real alternatives A-D and recommend option B. | fixed |
| HIGH | human decision | Option B and C are both viable but optimize different product tradeoffs: discoverability vs maximum compactness. | Ask user to choose before EXEC. | fixed: option B selected |
| MEDIUM | scope | Planned `FilterTags` / `FilterState` / `FilterActions` resources made visible copy likely and increased localization/test surface. | Remove default visible label resource work; allow resources only for tooltips/accessibility names if needed. | fixed |
| MEDIUM | acceptance | Original acceptance checked no-overflow but did not cap narrow toolbar height, so a technically passing layout could still be bulky. | Add default narrow budget: search plus at most two filter rows, with one extra row only for custom date. | fixed |
| LOW | evidence | Headless UI suite does not provide video artifacts required by ideal UI automation policy. | Replace video with mandatory before/after screenshots and agent visual review loop per user request. | fixed |

- Fixed before continuing: added Roadmap scope, refactor-local profile, UX options A-D, selected option B, filter panel acceptance and mandatory before/after screenshot review loop.
- Checks rerun: manual SPEC linter/rubric review repeated after fixes; result is PASS / 30.
- Needs human: формальное подтверждение спеки фразой `Спеку подтверждаю` для перехода в EXEC.
- Residual risks / follow-ups: final visual polish may need a screenshot sanity check during EXEC if headless geometry cannot catch aesthetics.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.ViewModel/Resources/Strings*.resx`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`, `tests/Unlimotion.ReadmeMedia/Program.cs`, screenshot artifacts under `artifacts/filter-toolbar-redesign/`.
- Decision: реализация варианта B принята. Helper extraction and active summary were intentionally not added because the existing `MainControl` adaptive toolbar behavior was sufficient after moving secondary controls into flyouts; adding a shared helper would have increased scope without improving the selected compact UX.
- Review passes:
  - Scope/Evidence pass: verified no VM/storage/filter semantics changes; changes are XAML/resources/tests/screenshot utility only.
  - Contract pass: existing reset automation ids and commands preserved; new filter button/panel automation ids are additive.
  - Adversarial risk pass: tests caught remaining wrapping with details pane open; sort moved into overflow and filter button changed to compact icon with tooltip.
  - Re-review after fixes / Fix and re-review: open flyout screenshots initially showed panel clipped left; `Placement="BottomEdgeAlignedLeft"` added and verified by screenshot and UI test assertion.
  - Stop decision: PASS after build, targeted UI tests, reset tests and before/after screenshot review.
- Evidence inspected:
  - Before: `artifacts/filter-toolbar-redesign/before/task-narrow-alltasks.png`, `task-narrow-lastcreated.png`, `roadmap-narrow.png`, `task-wide-alltasks.png`.
  - After closed: `artifacts/filter-toolbar-redesign/after/task-narrow-alltasks.png`, `task-narrow-lastcreated.png`, `roadmap-narrow.png`, `task-wide-alltasks.png`.
  - After open: `artifacts/filter-toolbar-redesign/after/task-narrow-alltasks-open.png`, `task-narrow-lastcreated-open.png`, `roadmap-narrow-open.png`.
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated tracked source changes; temporary failed screenshot tests and generated fixture dirs were removed.
  - Acceptance criteria: task tabs and Roadmap use compact search + icon filter + icon reset default row; secondary controls are grouped inside flyouts.
  - Validation evidence: targeted UI and reset tests passed; desktop build passed; full solution build with `--no-restore` was not used as final gate because unrelated projects lacked restored `project.assets.json`.
  - Unsupported claims: visual claims are backed by real desktop screenshots and repeated visual inspection.
  - Regression / edge case: details-pane shrink, Roadmap narrow, flyout placement, reset behavior and nonblank screenshot capture are covered.
  - Comments/docs/changelog: no product docs/changelog required; spec journal updated.
  - Hidden contract change: sort controls moved from primary row into filter panel, but bindings and semantics unchanged.
  - Manual-review challenge: icon-only filter/reset buttons could be challenged for discoverability; tooltips and grouped flyout labels mitigate this, and narrow density improves substantially.
- No-findings justification: the one detected open-flyout placement issue was fixed and revalidated before final report.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | open flyout | Initial after-open screenshots showed the flyout centered too far left and clipped by the window capture. | Set `Placement="BottomEdgeAlignedLeft"` on filter flyouts and assert placement in UI tests. | fixed |
| LOW | scope | Planned helper extraction and active summary were unnecessary after moving secondary controls to overflow. | Keep existing adaptive helper; document deviation in Post-EXEC review. | fixed |

- Fixed before final report: sort moved into filter panel; filter/reset became compact icon buttons with tooltips; flyout placement fixed; screenshot helper can capture overlay popups for visual review.
- Checks rerun:
  - `dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --no-restore` -> PASS with existing warnings.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-restore -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --no-progress` -> PASS, 4/4.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj --no-restore -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*" --no-progress` -> PASS, 6/6.
  - `dotnet run --project tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -- --ux-review filter-toolbar --language ru --output-root artifacts\filter-toolbar-redesign\after --no-build-before-launch` -> PASS.
- Validation evidence: before/after screenshots show narrow default filter area reduced from 2-4 rows of controls to one compact command row plus on-demand grouped flyout.
- Unrelated changes: none retained; generated fixture dirs and failed black screenshot artifacts removed.
- Needs human: none for this scoped implementation.
- Residual risks / follow-ups: optional future improvement could add active filter summary chips, but it is not required for the selected compact redesign.

## Approval
Вариант выбран: B.

EXEC выполнен после подтверждения спеки пользователем.

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction stack и routing | 0.95 | Нет | Собрать UI-контекст | Нет | Нет | Central QUEST stack требует spec-first; local override требует UI tests for UI changes | `AGENTS.md`, `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\instructions\*` |
| SPEC | AS-IS анализ filter panels | 0.9 | Нет | Создать рабочую spec | Нет | Нет | Task toolbar уже adaptive, но плоские группы остаются неудобными; Roadmap имеет отдельный WrapPanel | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.Test/*` |
| SPEC | Создание spec и quality gate | 0.92 | Подтверждение пользователя | Запросить переход в EXEC | Да | Да, ранее ожидалась фраза `Спеку подтверждаю` | SPEC включает visual artifact, acceptance criteria, UI tests, refactor scope and fallback evidence | `specs/2026-06-04-compact-filter-panels-redesign.md` |
| SPEC | Design review по критике пользователя | 0.9 | UX-выбор пользователя | Запросить выбор варианта | Да | Да, пользователь указал риск громоздкого, неудобного и слишком похожего на текущий UI макета | Spec revised: added UX options A-D, recommendation B, filter-panel acceptance and ASK-HUMAN status | `specs/2026-06-04-compact-filter-panels-redesign.md` |
| SPEC | Выбор UX-варианта и screenshot evidence | 0.95 | Формальное подтверждение спеки | Запросить переход в EXEC | Да | Да, пользователь выбрал вариант B и потребовал before/after screenshots вместо видео | Spec revised: option B selected, screenshot evidence and agent visual review loop are mandatory | `specs/2026-06-04-compact-filter-panels-redesign.md` |
| EXEC | Baseline screenshot capture | 0.88 | Нет | Реализовать вариант B | Нет | Нет | Реальные before screenshots сняты через `Unlimotion.ReadmeMedia --ux-review filter-toolbar`; visual review подтвердил громоздкие 2-4 строки фильтров на 390px и неудачное размещение Roadmap search/reset | `artifacts/filter-toolbar-redesign/before/task-narrow-alltasks.png`, `artifacts/filter-toolbar-redesign/before/task-narrow-lastcreated.png`, `artifacts/filter-toolbar-redesign/before/roadmap-narrow.png`, `tests/Unlimotion.ReadmeMedia/Program.cs` |
| EXEC | Compact toolbar implementation | 0.9 | Нет | Запустить build/UI tests | Нет | Нет | Secondary filters moved into grouped flyouts; default toolbar uses search + compact filter/reset icon buttons; sort moved into flyout after tests caught wrapping with details pane open | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.ViewModel/Resources/Strings.resx`, `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` |
| EXEC | UI test update and validation | 0.93 | Нет | Снять after screenshots | Нет | Нет | Responsive tests now verify compact primary row, no exposed checkbox/combobox controls in primary row, Roadmap compact toolbar and left-aligned flyout placement; reset tests verify command behavior survived icon button changes | `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` |
| EXEC | After screenshot capture and visual review | 0.9 | Нет | Fix open flyout placement | Нет | Нет | Closed screenshots showed density improvement; open screenshots initially exposed clipped flyout placement, then `BottomEdgeAlignedLeft` fixed it and open screenshots became readable | `artifacts/filter-toolbar-redesign/after/task-narrow-alltasks.png`, `artifacts/filter-toolbar-redesign/after/task-narrow-lastcreated.png`, `artifacts/filter-toolbar-redesign/after/roadmap-narrow.png`, `artifacts/filter-toolbar-redesign/after/task-narrow-alltasks-open.png`, `artifacts/filter-toolbar-redesign/after/task-narrow-lastcreated-open.png`, `artifacts/filter-toolbar-redesign/after/roadmap-narrow-open.png`, `tests/Unlimotion.ReadmeMedia/Program.cs` |
| EXEC | Post-EXEC review | 0.94 | Нет | Финальный отчет | Нет | Нет | Build, targeted UI tests, reset tests and screenshot loop passed; temporary failed capture tests/generated artifacts removed; full solution build with `--no-restore` was not used as final gate due unrelated missing project assets | `specs/2026-06-04-compact-filter-panels-redesign.md` |
| EXEC | Visual polish for filter/reset buttons | 0.92 | After screenshot validation | Run UI tests and capture screenshots | Нет | Нет | Follow-up request keeps option B but makes the compact buttons more polished: filter gets a wider menu-sized button and filter-list glyph; reset switches from text glyph to vector refresh icon; spacing is tightened consistently | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` |
| EXEC | Visual polish validation | 0.95 | Нет | Финальный отчет | Нет | Нет | Responsive/reset/tree-command/full tests passed; screenshot review of `after-polished-icons` confirmed cleaner filter glyph, vector reset icon and aligned spacing on narrow and wide captures | `artifacts/filter-toolbar-redesign/after-polished-icons/task-narrow-alltasks.png`, `artifacts/filter-toolbar-redesign/after-polished-icons/task-wide-alltasks.png`, `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` |
| EXEC | Filter icon padding polish | 0.96 | Нет | Финальный отчет | Нет | Нет | Added horizontal margin to filter `PathIcon` through shared styles in task and Roadmap toolbars; responsive UI test now asserts the icon spacing; screenshot review and full test run passed | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`, `artifacts/filter-toolbar-redesign/after-filter-icon-padding/task-narrow-alltasks.png` |
