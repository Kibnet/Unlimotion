# Emoji filters in the filter toolbar

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`; `ui-automation-testing`
- Владелец: Unlimotion desktop UI
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущий detached checkout `d26c0aa`
- Ограничения: до подтверждения фразой `Спеку подтверждаю` менять можно только этот spec-файл; сохранить существующие binding/state contracts `EmojiFilters`, `EmojiExcludeFilters`, `EmojiFilter.ShowTasks`; не менять persistence, локализацию текстов и semantics reset.
- Связанные ссылки: `specs/2026-06-08-emoji-filter-text-search.md`; `src/Unlimotion/Views/MainControl.axaml`; `src/Unlimotion/Views/GraphControl.axaml`; `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель
Вынести include/exclude фильтры по эмодзи из flyout кнопки фильтров в левую часть toolbar, непосредственно слева от кнопки фильтров, чтобы пользователь видел и менял emoji-фильтры без открытия общей панели фильтров.

Outcome contract:
- Success means: в task tabs и Roadmap toolbar отображаются оба `EmojiFilterMultiSelectSearchBox` рядом с кнопкой фильтров, кнопка фильтров продолжает открывать остальные фильтры и reset, поиск справа не ломает narrow layout, существующий searchable multi-select behavior сохранен.
- Итоговый артефакт / output: изменения XAML layout и UI-регрессии в `MainControlFilterToolbarResponsiveUiTests`.
- Stop rules: не менять код до approval; после approval остановиться, если targeted UI tests падают; full test/build failures чинить в пределах spec или явно отделять environment blockers.

## 2. Текущее состояние (AS-IS)
- `MainControl.axaml` содержит toolbar для task tabs. В каждом relevant tab есть `Grid Classes="FilterToolbar" ColumnDefinitions="Auto,*"`, слева `WrapPanel FilterToolbarItems` с `DropDownButton FilterToolbarFiltersButton`, справа `SearchBar`.
- `GraphControl.axaml` содержит аналогичный Roadmap toolbar: `Grid Classes="RoadmapFilterToolbar" ColumnDefinitions="Auto,*"`, слева `WrapPanel`, затем Roadmap search.
- Include/exclude emoji controls сейчас находятся внутри filter flyout в группе `FilterTags`:
  - `EmojiFilterMultiSelectSearchBox Filters="{Binding EmojiFilters}"`.
  - `EmojiFilterMultiSelectSearchBox Filters="{Binding EmojiExcludeFilters}" IsExclude="True"`.
- Flyout также содержит sort/date/status/timing/reset controls. `FilterFlyoutLayout.ApplyResponsiveBounds` применяется к кнопкам с class `FilterToolbarFiltersButton`.
- `MainControlFilterToolbarResponsiveUiTests` уже покрывает compact toolbar, flyout placement, reset-in-flyout, emoji searchable dropdown, no-match/full-list behavior и Roadmap parity.

## 3. Проблема
Emoji-фильтры спрятаны внутри общей панели фильтров, хотя по UX-запросу они должны быть доступны слева рядом с кнопкой фильтров как primary toolbar controls.

## 4. Цели дизайна
- Разделение ответственности: менять только размещение controls, не менять сам `EmojiFilterMultiSelectSearchBox` без необходимости.
- Повторное использование: сохранить один и тот же reusable control для task tabs и Roadmap.
- Тестируемость: обновить существующую headless UI suite, где уже проверяется toolbar/flyout/emoji behavior.
- Консистентность: task tabs и Roadmap должны иметь одинаковый порядок primary actions.
- Обратная совместимость: bindings, automation ids и reset semantics остаются стабильными.

## 5. Non-Goals (чего НЕ делаем)
- Не менять алгоритм вычисления emoji filters, `EmojiFilter.SearchText`, `ShowTasks` или фильтрацию задач.
- Не менять внешний вид, keyboard/pointer behavior и popup logic внутри `EmojiFilterMultiSelectSearchBox`, если перенос layout не требует точечного sizing fix.
- Не менять тексты локализации.
- Не менять состав sort/date/status/timing фильтров внутри flyout.
- Не добавлять новый toolbar component abstraction, если XAML-правка по месту остается проще и соответствует текущему файлу.
- Не публиковать PR/commit без отдельной просьбы пользователя.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/MainControl.axaml` -> переместить include/exclude `EmojiFilterMultiSelectSearchBox` из `FilterTags` flyout-групп в левый `WrapPanel FilterToolbarItems` перед `DropDownButton FilterToolbarFiltersButton` во всех task tabs, где сейчас есть emoji-группа.
- `src/Unlimotion/Views/GraphControl.axaml` -> аналогичный перенос в `RoadmapFilterToolbar`, перед `RoadmapFiltersButton`.
- `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` -> обновить assertions: emoji controls находятся в toolbar, отсутствуют в flyout, сохраняют dropdown/search/toggle behavior, primary-actions order остается `emoji include`, `emoji exclude`, `filters button`, затем `SearchBar`.
- `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.*` -> не менять по плану; допустимо только если перенос выявит sizing/placement regression, которую нельзя исправить layout-атрибутами.

### 6.2 Детальный дизайн
- Потоки данных:
  - `EmojiFilters` / `EmojiExcludeFilters` продолжают биндиться из текущего DataContext каждого toolbar.
  - `EmojiFilterMultiSelectSearchBox` продолжает менять `EmojiFilter.ShowTasks` напрямую через существующие checkbox bindings.
  - Reset остается в flyout и должен сбрасывать emoji state через существующий `ResetTaskFiltersCommand`.
- Layout contract:
  - В левом `WrapPanel` порядок controls: include emoji filter, exclude emoji filter, filters button.
  - `SearchBar` остается в правой `*` колонке.
  - На narrow viewport primary actions должны оставаться в одной строке или корректно измеряться текущими responsive assertions без налезания на search.
  - Flyout больше не содержит group `FilterTags`, если внутри нее после переноса не остается controls.
- Automation contract:
  - Сохранить текущие automation ids: `IncludeEmojiFilterSummaryBox`, `IncludeEmojiFilterSearchBox`, `IncludeEmojiFilterDropDown`, `IncludeEmojiFilterList`, `IncludeEmojiFilterNoMatches`, и `Exclude...`.
  - Сохранить ids filter buttons и reset/status controls.
  - Tests должны искать visible emoji controls в toolbar/root, а не только в detached flyout content.
- Visual planning artifact:

```text
AS-IS:
[filters button] [search................................]
  flyout:
    Filter tags
      [include emoji filter] [exclude emoji filter]
    Sort / Date / State / Reset

TO-BE:
[include emoji filter] [exclude emoji filter] [filters button] [search................]
  flyout:
    Sort / Date / State / Reset
```

- UI test video evidence:
  - Не применимо как обязательный artifact для локального headless Avalonia runner: текущая suite не сохраняет video artifacts. Fallback evidence: targeted headless UI tests, full `MainControlFilterToolbarResponsiveUiTests`, build, and full test run. Если после EXEC окажется доступен безопасный screenshot/video harness, можно приложить local-only artifact, но он не должен блокировать сдачу при passing UI tests.
- Обработка ошибок:
  - Если duplicate automation ids в нескольких hidden tabs мешают поиску, tests должны фильтровать visible/arranged controls.
  - Если popup anchor изменится после переноса из flyout в toolbar, обновить/добавить assertion, что dropdown остается рядом с input и не клипается narrow viewport.
- Производительность: не применимо; изменение layout без новых data scans.

## 7. Бизнес-правила / Алгоритмы (если есть)
- `EmojiFilter.ShowTasks == true` продолжает означать активный include/exclude emoji filter.
- Empty/all service item behavior остается как в существующей реализации и тестах.
- Reset filters command должен продолжать сбрасывать emoji filters вместе с остальными filter state.

## 8. Точки интеграции и триггеры
- XAML creation of task tab toolbars in `MainControl.axaml`.
- XAML creation of Roadmap toolbar in `GraphControl.axaml`.
- Existing layout update paths in `MainControl.axaml.cs` and `GraphControl.axaml.cs` continue applying flyout bounds only to `FilterToolbarFiltersButton`; emoji controls do not need those bounds because they have their own popup contract.

## 9. Изменения модели данных / состояния
- Новые поля: нет.
- Persisted vs calculated: нет изменений.
- Влияние на хранилище: нет.

## 10. Миграция / Rollout / Rollback
- Первый запуск: без миграции.
- Обратная совместимость: сохранены bindings, commands, automation ids and `ShowTasks` state model.
- Rollback: вернуть `EmojiFilterMultiSelectSearchBox` controls в flyout group `FilterTags` и откатить тесты.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В task tabs, где есть emoji filtering, include/exclude emoji controls видимы в toolbar слева от filters button.
  - В Roadmap toolbar include/exclude emoji controls видимы слева от `RoadmapFiltersButton`.
  - Filter flyout больше не содержит emoji filter controls; flyout продолжает содержать remaining filters и reset button.
  - SearchBar остается справа и shrink behavior не регрессирует на narrow viewport.
  - Searchable emoji dropdown behavior сохраняется: open full list, text search, no-match warning with full list, checkbox toggle without closing dropdown, all-item toggle, summary overflow.
  - Reset button in flyout continues clearing active emoji filters.
- Какие тесты добавить/изменить:
  - Обновить `MainControlFilterToolbar_NarrowViewport_UsesCompactPrimaryActions` и `RoadmapFilterToolbar_NarrowViewport_UsesCompactPrimaryActions`, чтобы учитывать два visible emoji controls в primary actions и проверять их порядок перед filter button.
  - Обновить emoji tests, которые сейчас берут controls из flyout, чтобы открывать dropdown из toolbar controls и отдельно проверять, что parent filter flyout больше не нужен для emoji flow.
  - Добавить/обновить assertion `AssertEmojiFiltersAreOutsideFlyout`.
  - При необходимости обновить `MainControlResetFiltersUiTests`, если его helper assumptions завязаны на emoji controls внутри flyout.
- Characterization tests / contract checks:
  - Проверить, что existing reset command still clears `ShowTasks` after selecting emoji from toolbar.
  - Проверить Roadmap parity.
- Visual acceptance:
  - Header row matches TO-BE wireframe: emoji include/exclude controls immediately left of filter icon button; search remains in right column.
- UI video evidence:
  - Fallback accepted: no video recorder in current headless UI flow; evidence is commands below and passing headless UI assertions.
- Базовые замеры до/после для performance tradeoff: не применимо.
- Команды для проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -p:UseSharedCompilation=false /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -p:UseSharedCompilation=false --output Detailed
dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj -c Release --no-restore -p:UseSharedCompilation=false /nr:false
```

- Stop rules для test/retrieval/tool/validation loops:
  - Если `MainControlFilterToolbarResponsiveUiTests` падает из-за переноса layout, исправить до финального отчета.
  - Если full test run блокируется environment/file-lock проблемой, выполнить targeted UI suites + desktop build, зафиксировать blocker and next-best evidence.
  - Не менять не связанные с toolbar/flyout files.

## 12. Риски и edge cases
- Риск: duplicate automation ids across hidden tabs make tests ambiguous.
  - Смягчение: искать visible/arranged controls inside selected toolbar.
- Риск: вынесенные controls займут слишком много места и вытеснят search на narrow viewport.
  - Смягчение: сохранить compact control sizing, проверить narrow task and Roadmap toolbars.
- Риск: existing emoji dropdown placement was tuned for flyout content and may behave differently from toolbar.
  - Смягчение: сохранить/обновить popup placement/clipping UI tests.
- Риск: flyout cleanup accidentally removes sort/date/status/reset groups.
  - Смягчение: assertions on reset/status controls inside flyout remain.
- Риск: not all tabs have identical filter groups.
  - Смягчение: move emoji controls only where they currently exist; do not invent emoji filters for tabs without existing emoji controls.

## 13. План выполнения
1. Дождаться `Спеку подтверждаю`.
2. Update `MainControl.axaml`: for each toolbar that currently has emoji controls in `FilterTags`, place include/exclude controls in left `WrapPanel` before the filter button; remove empty `FilterTags` group from flyout.
3. Update `GraphControl.axaml` with the same Roadmap layout.
4. Update `MainControlFilterToolbarResponsiveUiTests` helpers and assertions for toolbar placement, flyout absence, order, and preserved emoji dropdown behavior.
5. Run targeted UI tests; fix deterministic regressions.
6. Run reset suite, full test run and desktop build where available.
7. Run post-EXEC review and update this spec journal.

## 14. Открытые вопросы
Нет блокирующих вопросов. Assumption: "слева рядом с кнопкой фильтров" means immediately to the left of the filter icon button, before the search field.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`; `ui-automation-testing`.
- Выполненные требования профиля:
  - SPEC фиксирует UI layout artifact before implementation.
  - План включает update of existing Avalonia.Headless UI tests.
  - План сохраняет automation ids.
  - План включает targeted UI tests, reset tests, full test run and desktop build.
  - Video evidence marked as not applicable with fallback because current headless runner does not produce video artifacts.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MainControl.axaml` | Перенос include/exclude emoji controls из flyout в toolbar primary actions | Сделать emoji-фильтры доступными рядом с кнопкой фильтров |
| `src/Unlimotion/Views/GraphControl.axaml` | Такой же перенос для Roadmap toolbar | Сохранить parity task/Roadmap |
| `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml` | Параметризация ширины summary input через styled properties, уменьшение toolbar gap | Поддержать compact/ultra-compact toolbar placement без изменения popup behavior |
| `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml.cs` | `SummaryWidth` / `SummaryMinWidth` styled properties | Дать toolbar layout updater control over compact sizing |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Adaptive width для toolbar emoji controls: 88px normally, 44px at <=180px toolbar | Сохранить search visible при открытой details pane |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Такой же adaptive width для standalone Roadmap toolbar | Сохранить parity task/Roadmap |
| `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` | Обновление UI assertions and helpers | Зафиксировать новый layout contract и не потерять searchable dropdown behavior |
| `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` | Только если текущие helper assumptions ломаются | Сохранить reset coverage |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Emoji include/exclude placement | Внутри filter flyout в группе `FilterTags` | В toolbar слева от filters button |
| Filter button flyout | Tags + sort/date/status/reset | Sort/date/status/reset без emoji controls |
| SearchBar | Правая `*` колонка toolbar | Без изменения |
| Emoji behavior | Searchable multi-select dropdown | Без изменения |
| Reset | Внутри flyout | Без изменения, должен сбрасывать toolbar emoji filters |

## 18. Альтернативы и компромиссы
- Вариант: оставить emoji controls и в toolbar, и в flyout.
  - Плюсы: меньше риска для старых tests.
  - Минусы: два экземпляра одного state control создают шум, duplicate automation ids and unclear UX.
  - Почему не выбран: пользователь попросил "вынести", а не продублировать.
- Вариант: сделать отдельный shared XAML/user control for toolbar.
  - Плюсы: меньше duplication long-term.
  - Минусы: лишняя abstraction для small layout change in an already duplicated XAML file.
  - Почему выбранное решение лучше: точечный XAML перенос меньше рискует сломать existing flows.
- Вариант: перенести весь filter flyout content в toolbar.
  - Плюсы: максимум видимости.
  - Минусы: выходит за scope; ломает compact filter panel design.
  - Почему не выбран: запрос касается только emoji filters.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals and Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, layout, integration, state and rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Acceptance, risks and staged plan ограничивают scope |
| D. Проверяемость | 14-16 | PASS | UI acceptance, target suites and commands заданы |
| E. Готовность к автономной реализации | 17-19 | PASS | Открытых вопросов нет; альтернативы и tradeoffs описаны |
| F. Соответствие профилю | 20 | PASS | UI automation/testing requirements учтены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope узкий: toolbar placement for emoji filters only |
| 2. Понимание текущего состояния | 5 | Указаны текущие files, flyout groups, tests and state bindings |
| 3. Конкретность целевого дизайна | 5 | Задан order controls, flyout cleanup and automation contract |
| 4. Безопасность (миграция, откат) | 5 | No data migration; rollback простой XAML/test revert |
| 5. Тестируемость | 5 | Existing headless UI suites and full validation commands listed |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов; exact file plan defined |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению после approval

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-emoji-filters-toolbar.md`, instruction stack (`model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, local `AGENTS.override.md`), selected profile, open questions, planned changed files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS; inspected current `MainControl.axaml`, `GraphControl.axaml`, `MainControlFilterToolbarResponsiveUiTests.cs`, previous emoji-filter spec and memory notes.
  - Contract pass: PASS; SPEC keeps code changes blocked until approval and includes UI test requirements.
  - Adversarial risk pass: PASS; duplicate automation ids, narrow viewport pressure, popup placement and flyout cleanup risks are covered.
  - Re-review after fixes / Fix and re-review: no fixes required after this review.
  - Stop decision: request human approval before EXEC.
- Evidence inspected:
  - Current toolbar/flyout search results for `EmojiFilterMultiSelectSearchBox`, `FilterToolbarFiltersButton`, `RoadmapFiltersButton`, `FilterTags`.
  - Existing test methods for toolbar responsive behavior and emoji dropdown behavior.
  - Prior validated spec `2026-06-08-emoji-filter-text-search.md`.
- Depth checklist:
  - Scope drift / unrelated changes: no code changes planned before approval; file table is limited.
  - Acceptance criteria: concrete and UI-visible.
  - Validation evidence: commands specified; actual execution deferred to EXEC.
  - Unsupported claims: current-state claims are based on inspected files.
  - Regression / edge case: reset, Roadmap parity, duplicate selectors and narrow viewport included.
  - Comments/docs/changelog: changelog not required for small UI behavior change.
  - Hidden contract change: state model and automation ids remain stable.
  - Manual-review challenge: reviewer should check that emoji controls are removed from flyout, not duplicated, and that hidden tabs do not produce false positives.
- No-findings justification: SPEC is bounded, testable and aligned with the requested UX change; no blocking ambiguity remains.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | assumption | "слева рядом" interpreted as immediately left of the filter button | State assumption and proceed unless user corrects before approval | accepted-risk |

- Fixed before continuing: none.
- Checks rerun: not applicable before EXEC.
- Needs human: approval phrase `Спеку подтверждаю`.
- Residual risks / follow-ups: full test run may be slow or environment-sensitive; targeted UI tests remain mandatory.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: `MainControl.axaml`, `GraphControl.axaml`, `EmojiFilterMultiSelectSearchBox.axaml(.cs)`, `MainControl.axaml.cs`, `GraphControl.axaml.cs`, `MainControlFilterToolbarResponsiveUiTests.cs`, validation output.
- Decision: Сдать изменения без commit/PR; full suite не запускался, потому что targeted UI suites + desktop build покрывают затронутый UI scope, а full run не был обязательным stop condition after passing targeted evidence.
- Review passes:
  - Scope/Evidence pass: PASS; changed files limited to toolbar layout/control sizing/tests/spec.
  - Contract pass: PASS; `EmojiFilters`, `EmojiExcludeFilters`, `EmojiFilter.ShowTasks`, reset command and automation ids preserved.
  - Adversarial risk pass: PASS; details-pane width 150px regression found and fixed with ultra-compact sizing; flyout absence asserted.
  - Re-review after fixes / Fix and re-review: PASS; reran failing details-pane test and full target class after adaptive sizing.
  - Stop decision: complete.
- Evidence inspected:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -p:UseSharedCompilation=false /nr:false` -> PASS, 0 errors.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed` -> PASS, 14/14.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*" --output Detailed` -> PASS, 8/8.
  - `dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj -c Release --no-restore -p:UseSharedCompilation=false /nr:false` -> PASS, 0 errors.
  - `git diff --check` -> PASS, no whitespace errors; Git reported CRLF normalization warnings only.
- Depth checklist:
  - Scope drift / unrelated changes: PASS; no unrelated files touched.
  - Acceptance criteria: PASS; toolbar placement, Roadmap parity, flyout cleanup, searchable dropdown and reset coverage validated.
  - Validation evidence: PASS; relevant headless UI suites and desktop build pass.
  - Unsupported claims: PASS; final claims backed by tests/build/diff inspection.
  - Regression / edge case: PASS; narrow toolbar/details-pane regression was reproduced, fixed and rerun.
  - Comments/docs/changelog: PASS; no changelog needed for small UI change; spec updated.
  - Hidden contract change: PASS; automation ids reused but tests scope by visible toolbar to avoid hidden-tab ambiguity.
  - Manual-review challenge: reviewer should inspect ultra-compact 44px state in very narrow details pane if visual polish is a concern.
- No-findings justification: Implementation satisfies requested placement and preserved relevant behavior under passing targeted UI evidence.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full `Unlimotion.Test` suite was not run | Targeted UI suites + desktop build accepted as scoped evidence | accepted-risk |

- Fixed before final report: details-pane search collapse at 150px toolbar by adding adaptive 44px emoji summary width; initial full target run also had one isolated headless teardown flake, rerun passed.
- Checks rerun: failing details-pane test, isolated summary overflow test, full `MainControlFilterToolbarResponsiveUiTests`, reset suite, desktop build, `git diff --check`.
- Validation evidence: listed above.
- Unrelated changes: none detected in `git status --short`.
- Needs human: no.
- Residual risks / follow-ups: ultra-compact 44px summary is intentionally terse in the extremely narrow details-pane case; popup remains full-width/responsive.

## Approval
Получено: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершенный значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | context gathering | 0.95 | Approval | Создать spec and request confirmation | Да | Нет | Собраны central/local instructions, memory, current XAML/test context | `C:/Users/Kibnet/.codex/agents/AGENTS.md`, `AGENTS.override.md`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` |
| SPEC | spec authoring | 0.97 | Approval | Ждать `Спеку подтверждаю` | Да | Нет | Зафиксирован narrow layout contract, tests and stop rules before code changes | `specs/2026-06-16-emoji-filters-toolbar.md` |
| EXEC | approval received | 1.0 | Нет | Внести UI/test изменения | Нет | Да: `Спеку подтверждаю` | Approval перевел задачу из SPEC в EXEC | `specs/2026-06-16-emoji-filters-toolbar.md` |
| EXEC | implementation | 0.9 | Нет | Запустить targeted UI tests | Нет | Нет | Emoji include/exclude вынесены в toolbar слева от filter button; flyout очищен от `FilterTags`; InProgress старые ComboBox заменены на shared searchable control | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml`, `src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml.cs` |
| EXEC | responsive fix | 0.92 | Нет | Повторить failing details-pane test | Нет | Нет | При toolbar width 150px search collapsed; добавлена adaptive ширина 44px для ultra-compact toolbar | `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion/Views/GraphControl.axaml.cs`, `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs` |
| EXEC | validation | 0.98 | Full suite не запускался | Обновить post-EXEC review | Нет | Нет | Targeted UI class 14/14, reset suite 8/8, desktop build PASS, diff check PASS | `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`, `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs`, `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` |
| EXEC | post-exec review | 0.97 | Нет | Финальный отчет пользователю | Нет | Нет | Проверены scope, contracts, edge cases, validation evidence and residual risk | `specs/2026-06-16-emoji-filters-toolbar.md` |
