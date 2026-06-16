# Комбобокс фильтра важности

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущий worktree `C:\Users\Kibnet\.codex\worktrees\aa3c\Unlimotion`; git сейчас в detached HEAD
- Ограничения: до фразы `Спеку подтверждаю` менять только этот spec-файл; UI behavior требует UI-тесты; дефолт нового фильтра должен быть `Все`
- Связанные ссылки: локальные инструкции `AGENTS.override.md`; central stack `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`

## 1. Overview / Цель
Заменить UI-фильтр `Только важные` с tri-state checkbox на комбобокс с вариантами `Все`, `Важные`, `Не важные`.

Outcome contract:
- Success means: в панели фильтров вместо checkbox отображается combobox; выбранный по умолчанию вариант `Все`; выбор `Важные` показывает только `Wanted=true`; выбор `Не важные` показывает только `Wanted=false`; reset возвращает значение по умолчанию.
- Итоговый артефакт / output: изменения ViewModel/XAML/resources/tests и validation evidence в финальном отчёте EXEC.
- Stop rules: остановиться до реализации до явного `Спеку подтверждаю`; после EXEC остановиться только после targeted UI tests, build и максимально доступного full test workflow либо объективного blocker report.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion/Views/MainControl.axaml:1182` показывает `<CheckBox Content="{DynamicResource OnlyWanted}" IsChecked="{Binding ShowWanted}" IsThreeState="True" .../>` во вкладке `Unlocked`.
- `src/Unlimotion/Views/GraphControl.axaml:249` показывает такой же tri-state checkbox для `Roadmap`, когда `OnlyUnlocked=true`.
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs:701-719` уже фильтрует `bool? ShowWanted`: `null` пропускает все задачи, `true` оставляет важные, `false` оставляет не важные.
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs:96` сейчас загружает `ShowWanted` как `_configuration?...Get<bool?>() == true`, поэтому отсутствие настройки превращается в `false`, а не в `null`.
- `src/Unlimotion.ViewModel/GraphViewModel.cs:52-61` проксирует `ShowWanted` в `MainWindowViewModel`.
- Существующие UI-тесты рядом: `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` и `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`.
- Встроенной записи video/trace в найденных Avalonia.Headless/FlaUI тестах не обнаружено: поиск по `video|record|trace|screenshot|artifact` нашёл только unrelated `record` types и cleanup comments.

## 3. Проблема
Текущий checkbox с текстом `Только важные` плохо выражает три состояния фильтра: `Все`, `Важные`, `Не важные`, а текущая загрузка default дополнительно схлопывает `Все` в `Не важные`.

## 4. Цели дизайна
- Разделение ответственности: ViewModel хранит существующий nullable filter contract, XAML только отображает options.
- Повторное использование: один option-list для `MainControl` и `GraphControl`.
- Тестируемость: UI-тесты должны проверять наличие options, default и mapping на `ShowWanted`.
- Консистентность: combobox должен быть рядом со статусным фильтром и использовать localization/resource patterns проекта.
- Обратная совместимость: сохранённый `AllTasks:ShowWanted=true/false` продолжает означать важные/не важные; отсутствие значения означает `Все`.

## 5. Non-Goals (чего НЕ делаем)
- Не менять доменную модель `TaskItem.Wanted`.
- Не менять алгоритм сортировки, status/date/emoji/duration filters.
- Не менять состав вкладок, reset-confirmation UX и фильтр `OnlyUnlocked`.
- Не делать широкую переразметку фильтр-панелей и не рефакторить unrelated XAML.
- Не добавлять новый persistence key, если достаточно существующего `AllTasks:ShowWanted`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/WantedFilterOption.cs` -> маленькая option-модель с `bool? Value`, `ResourceKey`, `Title/DisplayText`, `ToString()`, definitions `All/Wanted/NotWanted`.
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs` -> хранить `ShowWanted` как nullable config value; добавить `WantedFilterDefinitions`; добавить computed `CurrentWantedFilter` с getter по `ShowWanted` и setter, который пишет `ShowWanted`.
- `src/Unlimotion.ViewModel/GraphViewModel.cs` -> проксировать `WantedFilterDefinitions` и `CurrentWantedFilter` для `GraphControl`.
- `src/Unlimotion/Views/MainControl.axaml` -> заменить checkbox на `ComboBox` с `ItemsSource="{Binding WantedFilterDefinitions}"`, `SelectedItem="{Binding CurrentWantedFilter}"`, `AutomationProperties.AutomationId="UnlockedWantedFilterComboBox"`.
- `src/Unlimotion/Views/GraphControl.axaml` -> заменить checkbox на такой же `ComboBox`, сохранить `IsVisible="{Binding OnlyUnlocked}"`, `AutomationProperties.AutomationId="RoadmapWantedFilterComboBox"`.
- `src/Unlimotion.ViewModel/Resources/Strings.resx` и `.ru.resx` -> добавить resource keys для `WantedFilterAll`, `WantedFilterWanted`, `WantedFilterNotWanted`.
- `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` и/или `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` -> обновить headless UI coverage.

### 6.2 Детальный дизайн
- Поток данных: `ComboBox.SelectedItem -> CurrentWantedFilter setter -> ShowWanted -> existing wantedFilter predicate -> filtered task list / graph rebuild`.
- Reverse sync: изменения `ShowWanted` из reset или legacy code должны уведомлять `CurrentWantedFilter`, чтобы combobox визуально возвращался к правильному option. План: `[AlsoNotifyFor(nameof(CurrentWantedFilter))]` на `ShowWanted`.
- Config: читать `ShowWanted = _configuration?.GetSection("AllTasks:ShowWanted").Get<bool?>();`, без `== true`; писать nullable значение через существующий subscription.
- Output contract / evidence rules: тесты должны подтверждать UI options и nullable mapping.
- Visual planning artifact для UI-facing изменений:
  ```text
  FilterState group
  ┌───────────────────────────────────────┐
  │ [Status combo: ...] [Wanted combo: Все v] │
  │ Wanted combo menu: Все / Важные / Не важные │
  └───────────────────────────────────────┘
  ```
- UI test video evidence: `Не применимо` для локального EXEC, потому что существующие Avalonia.Headless/FlaUI тесты в репозитории не имеют найденного recorder/video artifact workflow. Fallback evidence: targeted headless UI tests + build/full test command output. Если во время EXEC обнаружится поддерживаемый recorder, использовать его.
- Границы сохранения поведения: существующее значение `true` продолжает показывать важные; `false` теперь явно доступно как `Не важные`; `null` становится дефолтом `Все`.
- Обработка ошибок: если config value отсутствует или unreadable, `ShowWanted=null`.
- Производительность: без существенного влияния; option-list из трёх элементов, predicate уже существует.

## 7. Бизнес-правила / Алгоритмы
| UI option | `ShowWanted` | Predicate |
| --- | --- | --- |
| `Все` | `null` | `return true` |
| `Важные` | `true` | `return task.Wanted` |
| `Не важные` | `false` | `return !task.Wanted` |

Default:
- Новый профиль / отсутствует `AllTasks:ShowWanted`: `ShowWanted=null`, выбран `Все`.
- Existing persisted `true`: выбран `Важные`.
- Existing persisted `false`: выбран `Не важные`.

## 8. Точки интеграции и триггеры
- `MainWindowViewModel` constructor читает config и инициализирует `ShowWanted`.
- `WhenAnyValue(m => m.ShowWanted)` сохраняет значение в config и обновляет существующий filter observable.
- `ResetUnlockedTabFilters()` и `ResetRoadmapTabFilters()` возвращают `ShowWanted` к `_defaultShowWanted`; после изменения default это `null`, если настройка отсутствовала.
- `GraphControl` rebuild subscriptions уже слушают `GraphViewModel.ShowWanted`; этот контракт сохраняется.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Новое runtime state: `WantedFilterOption` и `CurrentWantedFilter` как UI-adapter поверх `ShowWanted`.
- Влияние на хранилище: только корректное сохранение nullable `AllTasks:ShowWanted` в существующем config section.

## 10. Миграция / Rollout / Rollback
- Миграция не нужна.
- Rollout: existing users с `true/false` сохраняют прежнее поведение, но увидят явный selected option.
- Rollback: вернуть XAML checkbox и старую загрузку `== true`; удалить option-model/resources/tests.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В `Unlocked` filter flyout вместо `CheckBox` есть `ComboBox` `UnlockedWantedFilterComboBox`.
  - В `Roadmap` filter flyout при `OnlyUnlocked=true` вместо `CheckBox` есть `ComboBox` `RoadmapWantedFilterComboBox`.
  - Options ровно: `All/Все`, `Wanted/Важные`, `Not Wanted/Не важные` по текущей локали.
  - Default выбран `Все`, когда config value отсутствует.
  - Выбор `Важные` устанавливает `ShowWanted=true`; выбор `Не важные` устанавливает `ShowWanted=false`; выбор `Все` устанавливает `ShowWanted=null`.
  - Reset возвращает `ShowWanted` к default `null`, если config value отсутствует и wanted-filter видим на текущей вкладке; reset вкладок без видимого wanted-filter не меняет скрытый wanted state.
- Какие тесты добавить/изменить:
  - Обновить `StatusFilterComboBox_IsAvailableOnEveryTaskTab` или добавить соседний test для wanted combobox на `Unlocked` и `Roadmap`.
  - Добавить ViewModel/UI assertion на default `ShowWanted=null` без config.
  - Обновить reset assertions, которые сейчас ожидают `true/false` исходя из старого default.
- Characterization tests / contract checks:
  - До fix targeted test должен падать на отсутствии combobox automation id или на default `ShowWanted=false`.
- Visual acceptance:
  - В flyout group `FilterState` статусный combobox и wanted combobox помещаются в тот же `WrapPanel`; responsive viewport не выходит за bounds existing `FilterFlyouts_CompactViewport_ConstrainPopupSizeAndScrollVertically`.
- UI video evidence:
  - Fallback: headless UI tests и `dotnet build`; video не планируется, если recorder workflow не обнаружен.
- Performance baseline: не применимо; изменение не затрагивает heavy path.
- Команды для проверки:
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*"`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*"`
  - `dotnet build src\Unlimotion.sln`
  - `dotnet test src\Unlimotion.sln -- --max-parallel-test-modules 1` или repo-proven full test command if current runner reports a better workflow.
  - `git diff --check`
- Stop rules для validation loops:
  - Если targeted UI tests fail из-за изменения, исправить до финала.
  - Если full test suite fail unrelated/flaky, зафиксировать exact failing tests/logs and run nearest targeted confirmation.

## 12. Риски и edge cases
- Nullable config persistence может отличаться от ожидаемого writer behavior. Mitigation: проверить default и persisted `false` behavior тестом.
- `GraphViewModel` proxy может не отправить `PropertyChanged` для `CurrentWantedFilter`. Mitigation: проксировать computed property и notify via existing `ShowWanted` path or explicit property notification if needed.
- Resource localization может не обновиться без RefreshLocalization. Mitigation: options read `L10n.Get(ResourceKey)` dynamically and tests validate displayed strings.
- Existing reset tests encoded old default. Mitigation: обновить только wanted assertions; status/date/emoji checks оставить.

## 13. План выполнения
1. EXEC TDD: добавить/обновить UI tests на combobox existence/options/default/mapping; targeted run должен показать red на текущем checkbox/default.
2. Добавить `WantedFilterOption` и properties in `MainWindowViewModel`/`GraphViewModel`.
3. Исправить config loading `ShowWanted` на nullable default.
4. Заменить checkbox на combobox в `MainControl.axaml` и `GraphControl.axaml`.
5. Добавить localized resource keys.
6. Запустить targeted tests, build, full tests/diff check.
7. Выполнить post-EXEC review-loop and report.

## 14. Открытые вопросы
Нет блокирующих вопросов. `Не важные` трактуется как `Wanted=false`, исходя из существующего predicate line `return !task.Wanted`.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля:
  - UI behavior change покрывается existing Avalonia.Headless tests.
  - Stable automation ids будут добавлены для combobox controls.
  - Build/test commands зафиксированы.
  - Visual planning artifact и video fallback зафиксированы.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/WantedFilterOption.cs` | Новый option type/definitions | Явная model для трех UI options |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Nullable default, definitions/current option | Bind combobox без изменения predicate |
| `src/Unlimotion.ViewModel/GraphViewModel.cs` | Proxy definitions/current option | Roadmap flyout uses `GraphViewModel` DataContext |
| `src/Unlimotion/Views/MainControl.axaml` | Checkbox -> combobox | UI request for Unlocked filters |
| `src/Unlimotion/Views/GraphControl.axaml` | Checkbox -> combobox | UI request for Roadmap filters |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | English resource keys | Localization |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Russian resource keys | Localization |
| `src/Unlimotion.Test/TestSettings.json` | Убрать default `AllTasks:ShowWanted=False` | Test fixture должен отражать новый default `Все` |
| `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` | Reset/default assertions | Regression coverage |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | Combobox options/UI assertions | UI coverage |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| UI control | Tri-state checkbox `Только важные` | ComboBox `Все / Важные / Не важные` |
| Default | Missing config -> `false` | Missing config -> `null` / `Все` |
| Important filter | `ShowWanted=true` | `ShowWanted=true` |
| Not important filter | Checkbox unchecked `ShowWanted=false` | Explicit option `Не важные` |
| All filter | Checkbox indeterminate `ShowWanted=null`, but not default | Explicit option `Все`, default |

## 18. Альтернативы и компромиссы
- Вариант: использовать `ComboBox.SelectedIndex` 0/1/2.
- Плюсы: минимум классов.
- Минусы: magic index, хуже тестируемость и localization coupling.
- Почему выбранное решение лучше: option-model сохраняет nullable business contract, readable tests and XAML, easier Roadmap proxy.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заполнены |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, rules, rollback and perf описаны |
| C. Безопасность изменений | 11-13 | PASS | Нет schema migration; persisted compatibility и rollback описаны |
| D. Проверяемость | 14-16 | PASS | Acceptance, UI tests, commands and fallback evidence указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | План есть; блокирующих вопросов нет; scope small |
| F. Соответствие профилю | 20 | PASS | UI automation, visual artifact, video fallback and stable selectors зафиксированы |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Требуемые options/default/Non-Goals проверяемы |
| 2. Понимание текущего состояния | 5 | Указаны XAML, VM predicate, config-loading и tests |
| 3. Конкретность целевого дизайна | 5 | Option model, bindings, resources and reset behavior описаны |
| 4. Безопасность (миграция, откат) | 5 | Existing true/false compatibility and rollback path есть |
| 5. Тестируемость | 5 | Targeted UI tests, default/mapping assertions and commands указаны |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов; план по шагам |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-wanted-filter-combobox.md`; instruction stack listed in metadata; selected profiles `dotnet-desktop-client` and `ui-automation-testing`; open questions section; planned changed files table
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: просмотрены central QUEST/testing/UI docs, local override, current XAML lines, current VM predicate/default lines, reset tests and responsive/filter tests.
  - Contract pass: spec matches user request for combo options and default `Все`; Non-Goals exclude unrelated filter redesign.
  - Adversarial risk pass: главный hidden risk found and handled: current default collapses missing config to `false`; spec requires nullable default and tests.
  - Re-review after fixes / Fix and re-review: fixes not needed after this pass.
  - Stop decision: PASS, because no blocker/high findings and acceptance is testable.
- Evidence inspected: `MainControl.axaml:1182`, `GraphControl.axaml:249`, `MainWindowViewModel.cs:96`, `MainWindowViewModel.cs:701-719`, `GraphViewModel.cs:52-61`, `MainControlResetFiltersUiTests.cs`, `MainControlFilterToolbarResponsiveUiTests.cs`, search for video/record/trace artifacts.
- Depth checklist:
  - Scope drift / unrelated changes: only wanted filter UI/default planned.
  - Acceptance criteria: cover control replacement, options, default, mapping and reset.
  - Validation evidence: commands listed; video fallback reason recorded.
  - Unsupported claims: current behavior tied to inspected code lines.
  - Regression / edge case: persisted true/false compatibility and missing config default covered.
  - Comments/docs/changelog: no production comments planned; changelog not needed for small UI fix unless release process requests it.
  - Hidden contract change: explicit change is missing config default from false to null, required by user default `Все`.
  - Manual-review challenge: reviewer would likely check GraphControl path and reset tests; both included.
- No-findings justification: spec identifies the nullable-default issue and includes concrete tests, rollback and UI profile evidence rules.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Local video evidence is not planned because no recorder workflow was found in existing UI tests | Use headless UI test logs as fallback; re-check recorder availability during EXEC if needed | accepted-risk |

- Fixed before continuing: Не применимо
- Checks rerun: SPEC linter/rubric manual pass recorded above
- Needs human: требуется `Спеку подтверждаю` для EXEC
- Residual risks / follow-ups: Full suite may expose existing UI parallelism; if so, report exact failure and run nearest deterministic targeted checks.

### Post-EXEC Review
- Статус: Выполнен после EXEC
- Scope reviewed: XAML replacement, nullable default, wanted option model/localization, GraphViewModel roadmap proxy, DataTemplate rendering, test fixture default, reset expectations, screenshot capture harness and validation evidence.
- Decision: PASS with accepted environmental/full-suite risks listed below.
- Review passes:
  - Scope/Evidence pass: changed files match requested wanted-filter combobox only; no unrelated repo files edited.
  - Contract pass: options are `Все`, `Важные`, `Не важные`; default is `Все`/`null`; `true` and `false` mappings preserved.
  - Adversarial risk pass: found roadmap proxy issue where `CurrentWantedFilter` setter bypassed `GraphViewModel.ShowWanted`; fixed by routing through `ShowWanted`.
  - Screenshot evidence pass: runtime capture initially revealed missing DataTemplate (`WantedFilterOption` rendered as a type-not-found fallback instead of `Все`); fixed with explicit `WantedFilterOption` DataTemplates in `MainControl.axaml` and `GraphControl.axaml`.
  - Re-review after fixes / Fix and re-review: targeted `MainControlResetFiltersUiTests` rerun after proxy/DataTemplate fixes and passed; screenshot capture rerun produced nonblank final PNGs.
  - Stop decision: PASS, because blocker/high findings are absent and relevant UI tests pass on final code.
- Evidence inspected: production diff, targeted UI test output, responsive UI test output, runtime screenshot PNGs, screenshot capture command output, desktop build output, solution-build workload blocker, full test runner failure/abort behavior, `git diff --check`, `git status --short`.
- Depth checklist:
  - Scope drift / unrelated changes: none found in final status beyond spec/new option file, screenshot artifacts, evidence harness and intended source/test/resource edits.
  - Acceptance criteria: covered by UI test for both Unlocked and Roadmap comboboxes plus reset default assertion; runtime screenshots show `Все`, `Важные`, `Не важные`.
  - Validation evidence: targeted UI tests passed; screenshot capture passed; desktop build passed; diff check passed; full solution/test limitations documented.
  - Unsupported claims: final report limited to commands actually run and observed outputs.
  - Regression / edge case: persisted `true`/`false` still maps to `Важные`/`Не важные`; missing config maps to `Все`.
  - Comments/docs/changelog: no production comments added; no changelog needed for this scoped UI change.
  - Hidden contract change: test fixture default changed by removing explicit legacy `ShowWanted=False`; automation scenario data with explicit false was left intact.
  - Manual-review challenge: reviewer likely checks roadmap path, reset behavior and default config; each has code/test evidence.
- No-findings justification: после proxy fix нет blocker/high/medium findings; remaining items are environmental validation limitations.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | `dotnet build src\Unlimotion.sln` blocked on missing `wasm-tools` workload for Android/iOS projects | Use nearest build evidence for affected desktop app and report exact blocker | accepted-risk |
| LOW | tests | Full `Unlimotion.Test` run did not finish cleanly: first attempt hit unrelated RoadmapGraph Avalonia.Headless teardown NRE, isolated failing test passed, retry aborted without useful summary | Keep relevant targeted UI tests as primary evidence and report full-suite limitation | accepted-risk |

- Fixed before final report: roadmap proxy setter now routes selected `CurrentWantedFilter` through `GraphViewModel.ShowWanted`; explicit DataTemplates render wanted options as localized text; targeted UI test now checks rendered selected text.
- Checks rerun: `MainControlResetFiltersUiTests` after final proxy/DataTemplate fix; screenshot capture after visual selection fix; responsive UI tests; desktop build; diff check.
- Validation evidence:
  - PASS: `dotnet run --project tests\Unlimotion.ReadmeMedia\Unlimotion.ReadmeMedia.csproj -c Debug -- --ux-review wanted-filter --language ru --output-root C:\Users\Kibnet\.codex\worktrees\aa3c\Unlimotion\artifacts\wanted-filter-evidence` -> generated nonblank PNG evidence:
    - `C:\Users\Kibnet\.codex\worktrees\aa3c\Unlimotion\artifacts\wanted-filter-evidence\unlocked-filter-all.png`
    - `C:\Users\Kibnet\.codex\worktrees\aa3c\Unlimotion\artifacts\wanted-filter-evidence\unlocked-filter-options.png`
    - `C:\Users\Kibnet\.codex\worktrees\aa3c\Unlimotion\artifacts\wanted-filter-evidence\unlocked-filter-wanted.png`
    - `C:\Users\Kibnet\.codex\worktrees\aa3c\Unlimotion\artifacts\wanted-filter-evidence\unlocked-filter-not-wanted.png`
  - PASS: `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*"` -> 9/9 passed.
  - PASS: `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*"` -> 14/14 passed.
  - PASS: `dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --no-restore`.
  - PASS: `git diff --check`.
  - BLOCKED: `dotnet build src\Unlimotion.sln` -> `NETSDK1147` missing `wasm-tools` for Android/iOS projects.
  - LIMITED: full `src\Unlimotion.Test` run with `--maximum-parallel-tests 1` hit unrelated RoadmapGraph headless teardown NRE; isolated `RoadmapGraph_RectangleHitTesting_IgnoresMinimapItems` passed 1/1; retry returned no useful summary/report.
- Unrelated changes: none detected.
- Needs human: нет.
- Residual risks / follow-ups: install `wasm-tools` before relying on full solution build; investigate full-test runner abort separately if full-suite evidence is required.

## Approval
Получено: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction/context discovery | 0.95 | Нет | Создать spec | Нет | Нет | Прочитаны central/local instructions; задача попадает под QUEST + UI testing | `AGENTS.override.md`, central `AGENTS.md`, central governance/profile docs |
| SPEC | AS-IS inspection | 0.9 | Нет | Зафиксировать TO-BE | Нет | Нет | Найдены checkbox controls, nullable predicate and default-loading defect | `MainControl.axaml`, `GraphControl.axaml`, `MainWindowViewModel.cs`, `GraphViewModel.cs`, UI test files |
| SPEC | Spec creation and quality gate | 0.9 | Требуется EXEC approval | Запросить подтверждение спеки | Да | Нет | Spec готова, linter/rubric/review PASS; реализация заблокирована до `Спеку подтверждаю` | `specs/2026-06-16-wanted-filter-combobox.md` |
| EXEC | Approval and TDD test update | 0.88 | Red-run result | Запустить targeted test red-run | Нет | Да, пользователь подтвердил spec | Добавлен UI test contract для wanted combobox и обновлены reset expectations под default `Все` | `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs`, `specs/2026-06-16-wanted-filter-combobox.md` |
| EXEC | Production implementation | 0.86 | Targeted test result | Запустить targeted UI tests | Нет | Нет | Добавлены option-model, nullable default, combobox bindings and localized option labels | `WantedFilterOption.cs`, `MainWindowViewModel.cs`, `GraphViewModel.cs`, `MainControl.axaml`, `GraphControl.axaml`, `Strings.resx`, `Strings.ru.resx` |
| EXEC | Test fixture default alignment | 0.88 | Targeted test result | Повторить targeted UI tests | Нет | Нет | TestSettings had explicit `ShowWanted=False`; removed it so fixture covers new default `Все` instead of legacy persisted false | `src/Unlimotion.Test/TestSettings.json`, `specs/2026-06-16-wanted-filter-combobox.md` |
| EXEC | Reset contract correction | 0.9 | Targeted test result | Повторить targeted UI tests | Нет | Нет | Сохранён existing contract: reset вкладки без видимого wanted-filter не сбрасывает hidden wanted state | `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs`, `specs/2026-06-16-wanted-filter-combobox.md` |
| EXEC | Roadmap proxy self-review fix | 0.9 | Нет | Повторить targeted UI tests | Нет | Нет | Исправлен setter `CurrentWantedFilter`, чтобы Roadmap выбор шёл через `GraphViewModel.ShowWanted` and triggered existing graph subscriptions | `src/Unlimotion.ViewModel/GraphViewModel.cs`, `specs/2026-06-16-wanted-filter-combobox.md` |
| EXEC | Validation and post-EXEC review | 0.9 | Нет | Финальный отчёт | Нет | Нет | Relevant UI tests/build/diff-check passed; full solution/full suite limitations documented with exact blockers | `specs/2026-06-16-wanted-filter-combobox.md`, test/build outputs |
| EXEC | Screenshot evidence and DataTemplate fix | 0.92 | Нет | Финальный отчёт со скриншотами | Нет | Нет | Runtime screenshots exposed missing `WantedFilterOption` DataTemplate; fixed rendering, added rendered-text UI assertion, regenerated nonblank PNG evidence and reran targeted checks | `MainControl.axaml`, `GraphControl.axaml`, `MainControlResetFiltersUiTests.cs`, `tests/Unlimotion.ReadmeMedia/Program.cs`, `artifacts/wanted-filter-evidence/*.png`, `specs/2026-06-16-wanted-filter-combobox.md` |
