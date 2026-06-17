# Per-Tab Status Filters

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex / user approval
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка worktree `C:\Users\Kibnet\.codex\worktrees\3211\Unlimotion`
- Ограничения: до утверждения фразой "Спеку подтверждаю" менять только этот spec-файл; для UI-facing поведения обязательно добавить/обновить UI tests и запустить релевантные UI-тесты
- Связанные ссылки: `C:\Users\Kibnet\.codex\agents\AGENTS.md`, локальный `AGENTS.override.md`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Фильтр статуса должен хранить и применять отдельное значение для каждой основной вкладки, включая Roadmap, вместо одного общего `StatusFilters` для всех вкладок.

Outcome contract:
- Success means: изменение выбранных статусов в одной вкладке не меняет status-фильтр и результат фильтрации других вкладок; reset сбрасывает status-фильтр только текущей вкладки.
- Итоговый артефакт / output: обновлённая ViewModel/AXAML-привязка status-фильтров и regression UI coverage.
- Stop rules: остановиться после реализации, targeted UI tests, build/full test attempt, post-EXEC review и отчёта; если full test run технически недоступен или зависает, зафиксировать причину и next-best evidence.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs` создаёт один `StatusFilters = TaskStatusFilter.GetDefinitions(...)`.
- Этот общий `StatusFilters`:
  - загружается из `AllTasks:StatusFilters:{status}` с fallback на `AllTasks:ShowCompleted` / `AllTasks:ShowArchived`;
  - синхронизируется с legacy properties `ShowCompleted` / `ShowArchived`;
  - сохраняется обратно в `AllTasks:StatusFilters:{status}`;
  - используется в единственном observable `taskFilter`.
- Один `taskFilter` применяется ко всем projections: All Tasks, Unlocked, In Progress, Completed, Archived, Last Created, Last Updated, Roadmap roots/unlocked и Last Opened.
- `src/Unlimotion.ViewModel/GraphViewModel.cs` отдаёт `StatusFilters` из `MainWindowViewModel`, поэтому Roadmap использует тот же общий набор.
- `src/Unlimotion/Views/MainControl.axaml` привязывает status combobox каждой task-вкладки к `{Binding StatusFilters}`.
- `src/Unlimotion/Views/GraphControl.axaml` привязывает Roadmap status combobox к `{Binding StatusFilters}` из `GraphViewModel`.
- `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` уже покрывает наличие status combobox и reset, но текущие assertions проверяют общий `vm.StatusFilters`.

## 3. Проблема
Одна корневая проблема: status-фильтр является глобальным состоянием, поэтому выбор статусов или reset в одной вкладке меняет фильтрацию во всех остальных вкладках.

## 4. Цели дизайна
- Разделение ответственности: каждая вкладка владеет своим набором `TaskStatusFilter`.
- Повторное использование: использовать существующий `TaskStatusFilter`, текущий DataTemplate, существующий UI и helper-логику без нового control model.
- Тестируемость: добавить deterministic UI regression на независимость значений между вкладками и адаптировать reset assertions.
- Консистентность: все status combobox сохраняют текущий вид, automation ids и список статусов.
- Обратная совместимость: legacy `StatusFilters`, `ShowCompleted`, `ShowArchived`, `AllTasks:ShowCompleted`, `AllTasks:ShowArchived` сохраняются как All Tasks bridge.

## 5. Non-Goals (чего НЕ делаем)
- Не менять визуальный дизайн фильтр-панелей, layout, automation ids, локализацию или DataTemplate строки статусов.
- Не менять семантику search, emoji filters, date filters, wanted/unlocked filters.
- Не менять доменную модель task status, status transitions, storage schema задач или server contracts.
- Не добавлять новые вкладки и не менять порядок вкладок.
- Не коммитить video artifacts в репозиторий.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs` -> создаёт, хранит, сохраняет, сбрасывает и применяет status-фильтры по вкладкам.
- `src/Unlimotion.ViewModel/GraphViewModel.cs` -> отдаёт Roadmap-specific status filters в существующее свойство `StatusFilters` для GraphControl.
- `src/Unlimotion/Views/MainControl.axaml` -> привязывает каждый task-tab status combobox к своей collection.
- `src/Unlimotion/Views/GraphControl.axaml` -> остаётся визуально без изменений; binding `StatusFilters` начинает указывать на Roadmap collection через `GraphViewModel`.
- `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` -> UI regression на per-tab independence и reset текущей вкладки.
- `src/Unlimotion.Test/MainWindowViewModelTests.cs` -> при необходимости обновить model-level assertions/helper под All Tasks или per-tab properties.

### 6.2 Детальный дизайн
- Добавить per-tab collections в `MainWindowViewModel`:
  - `StatusFilters` оставить как legacy alias для All Tasks.
  - Добавить `LastCreatedStatusFilters`, `LastUpdatedStatusFilters`, `UnlockedStatusFilters`, `InProgressStatusFilters`, `CompletedStatusFilters`, `ArchivedStatusFilters`, `LastOpenedStatusFilters`, `RoadmapStatusFilters`.
- Создать helper для построения observable predicate из конкретной collection:
  - `CreateStatusFilter(ReadOnlyObservableCollection<TaskStatusFilter> filters)`.
  - Каждый projection использует свой observable вместо единого `taskFilter`.
- Defaults:
  - All Tasks читает старые `AllTasks:StatusFilters:{status}` и `AllTasks:ShowCompleted` / `AllTasks:ShowArchived`.
  - Остальные вкладки читают новые keys `<Tab>:StatusFilters:{status}`; если ключей нет, используют базовый default: `NotReady`, `Prepared`, `InProgress` включены, `Completed` / `Archived` по legacy fallback.
  - Status-specific tabs получают forced default для собственного статуса: In Progress -> `InProgress`, Completed -> `Completed`, Archived -> `Archived`.
  - Roadmap использует `Roadmap:StatusFilters:{status}` с тем же базовым default.
- Persistence:
  - All Tasks продолжает писать legacy `AllTasks:StatusFilters:{status}` и legacy visibility keys через `ShowCompleted` / `ShowArchived`.
  - Остальные вкладки пишут только свой `<Tab>:StatusFilters:{status}`.
  - Изменение non-AllTasks status filters не меняет `ShowCompleted` / `ShowArchived`.
- Reset:
  - `ResetCompletionVisibilityFilters(...)` заменить/перегрузить так, чтобы принимать конкретную collection.
  - Reset каждой вкладки сбрасывает только её status collection.
  - Existing forced visible behavior сохранить для In Progress / Completed / Archived reset, но применять только к соответствующей вкладке.
  - Roadmap reset сбрасывает только `RoadmapStatusFilters`; при `Graph.OnlyUnlocked == true` сохраняет текущую hidden completion-filter логику, но не меняет All Tasks filters.
- Localization:
  - `RefreshLocalizedCollections()` обновляет title у всех status filter collections.
- Visual planning artifact для UI-facing изменений:

```text
Existing layout remains unchanged.

All Tasks flyout      -> AllTasks/StatusFilters collection
Last Created flyout   -> LastCreatedStatusFilters collection
Last Updated flyout   -> LastUpdatedStatusFilters collection
Unlocked flyout       -> UnlockedStatusFilters collection
In Progress flyout    -> InProgressStatusFilters collection
Completed flyout      -> CompletedStatusFilters collection
Archived flyout       -> ArchivedStatusFilters collection
Last Opened flyout    -> LastOpenedStatusFilters collection
Roadmap flyout        -> RoadmapStatusFilters via GraphViewModel.StatusFilters

User-visible invariant:
checking/unchecking a status inside one flyout changes only that tab's list/graph.
```

- UI test video evidence для UI automation задач: fallback. Existing Avalonia.Headless/TUnit suites in this repo provide deterministic UI assertions but no repository runner/harness for safe video capture. Evidence will be targeted passing/failing UI test output; no video artifact will be committed.
- Границы сохранения поведения: existing automation ids remain stable; status list content remains `Enum.GetValues<DomainTaskStatus>()`.
- Обработка ошибок: no new user-facing errors; missing config keys use defaults.
- Производительность: each tab gets its own small 5-item status observable; overhead is negligible. Avoid duplicating expensive task streams beyond existing per-tab projection pattern.

## 7. Бизнес-правила / Алгоритмы
Status visibility predicate for a tab:

| Input | Rule |
| --- | --- |
| `TaskStatusFilter.ShowTasks == true` for task status | task passes that tab's status predicate |
| `ShowTasks == false` for task status | task is hidden in that tab only |
| Reset tab | resets that tab's status collection to its default/forced-default state |
| Toggle tab A status | tab B status collection and projection remain unchanged |

## 8. Точки интеграции и триггеры
- Constructor `MainWindowViewModel(...)` creates all status collections and subscribes to their changes.
- `Connect()` builds per-tab DynamicData predicates and applies them in each projection.
- `ResetCurrentTabFilters()` dispatches reset to current tab collection.
- XAML bindings select the collection for the tab that owns the flyout.
- `GraphControl.RegisterRoadmapFilterSubscriptions()` observes Roadmap status filters via `GraphViewModel.StatusFilters`.

## 9. Изменения модели данных / состояния
- Новые in-memory state fields/properties: per-tab `ReadOnlyObservableCollection<TaskStatusFilter>`.
- Persisted settings:
  - keep existing `AllTasks:*` keys;
  - add optional `<Tab>:StatusFilters:{status}` keys when users change non-AllTasks status filters.
- No task storage migration.
- No domain model changes.

## 10. Миграция / Rollout / Rollback
- Первый запуск после изменения:
  - All Tasks keeps current legacy saved behavior.
  - Other tabs initialize from their own saved values if present; otherwise defaults as described in 6.2.
- Rollback:
  - reverting code restores global `StatusFilters`;
  - extra `<Tab>:StatusFilters:*` config keys become inert and do not affect old builds.
- Backward compatibility:
  - legacy `ShowCompleted` / `ShowArchived` continues to reflect All Tasks only.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- AC1: Status combobox exists on every task tab and Roadmap with the same status options as before.
- AC2: Turning off `Prepared` in All Tasks hides prepared tasks only in All Tasks; Last Created/Roadmap keep their own `Prepared` value.
- AC3: Turning off a status in Last Created does not mutate All Tasks, Last Updated, Roadmap or other tabs.
- AC4: Turning off a status in Roadmap does not mutate task-tab status filters.
- AC5: Reset on a tab resets only that tab's status collection and leaves status values on other tabs unchanged.
- AC6: Existing reset behavior for status-specific tabs still forces their own status visible after reset.
- AC7: Existing config compatibility for All Tasks `ShowCompleted` / `ShowArchived` remains intact.

Какие тесты добавить/изменить:
- Add failing regression in `MainControlResetFiltersUiTests` before implementation:
  - e.g. `StatusFilterComboBox_SelectionIsIndependentPerTab`.
  - Toggle a status via one tab's collection/flyout and assert another tab's collection item remains unchanged.
  - Include Roadmap as a separate scope.
- Update existing reset tests in `MainControlResetFiltersUiTests` to assert current-tab collection reset rather than global `vm.StatusFilters`.
- Update `MainWindowViewModelTests.StatusFilters_AllTasksProjection_HidesAndShowsPreparedTasks` only if property names change; prefer keeping `StatusFilters` as All Tasks bridge to minimize churn.

Characterization tests / contract checks:
- Existing `StatusFilterComboBox_IsAvailableOnEveryTaskTab` remains a contract check for status options and automation ids.

Visual acceptance:
- Existing visual layout must match the fallback wireframe in 6.2: same controls, same flyout grouping, same status combobox position.
- Verify through existing headless visual-tree assertions rather than screenshot/video.

UI video evidence:
- Fallback: no supported safe video recorder in the Avalonia.Headless/TUnit harness. Validation evidence will be TUnit output for targeted UI tests and build/full test attempt.

Команды для проверки:
```powershell
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*"
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/StatusFilters_AllTasksProjection_HidesAndShowsPreparedTasks"
dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --no-restore /nodeReuse:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*"
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj
```

Stop rules для test/retrieval/tool/validation loops:
- Run targeted UI test first, then build, then broader relevant UI tests.
- Attempt full test run unless it repeats known local hanging/environment behavior; if blocked, report exact blocker and targeted evidence.
- Do not broaden into unrelated UI redesign or storage migration.

## 12. Риски и edge cases
- Risk: Completed/Archived tabs can be empty if their per-tab status default hides their own status. Mitigation: force own status visible by default/reset for status-specific tabs.
- Risk: legacy `ShowCompleted` / `ShowArchived` may accidentally remain global. Mitigation: restrict their sync to All Tasks `StatusFilters` only.
- Risk: Roadmap graph rebuild subscriptions can miss per-tab status changes. Mitigation: GraphViewModel `StatusFilters` points at `RoadmapStatusFilters`, and `GraphControl` already subscribes to `StatusFilters` collection changes.
- Risk: reset tests currently assume global status filters. Mitigation: update assertions to inspect the collection for the active tab and verify other collections remain unchanged.
- Risk: too much duplication in projection filters. Mitigation: introduce small helper method for status predicate creation and per-tab reset/persistence.

## 13. План выполнения
1. Add/adjust UI regression tests in `MainControlResetFiltersUiTests` to prove status selection is per-tab and reset is scoped.
2. Run targeted test and confirm it fails on current implementation.
3. Implement per-tab status collections, persistence helpers, reset helpers, and per-projection predicates.
4. Update AXAML bindings for each task tab status combobox; keep Roadmap binding stable through `GraphViewModel.StatusFilters`.
5. Update GraphControl subscriptions only if needed for Roadmap status changes.
6. Run targeted UI tests, build, relevant filter toolbar tests, and full test attempt.
7. Perform post-EXEC review and fix findings with deterministic corrections.

## 14. Открытые вопросы
Нет блокирующих вопросов.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - UI behavior change is specified before implementation.
  - Stable automation ids are preserved.
  - Relevant Avalonia.Headless UI tests will be added/updated and run.
  - `dotnet build` and relevant `dotnet test` commands are part of validation.
  - Video evidence fallback is documented with objective reason.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Add per-tab status filter collections, helpers, per-projection predicates, scoped reset/persistence | Remove global status-filter state |
| `src/Unlimotion.ViewModel/GraphViewModel.cs` | Make Roadmap `StatusFilters` resolve to `RoadmapStatusFilters` | Roadmap gets independent status values |
| `src/Unlimotion/Views/MainControl.axaml` | Bind each status combobox to its tab-specific collection | UI controls edit the correct state |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Adjust Roadmap subscriptions if current code observes the wrong legacy properties | Ensure Roadmap rebuilds on its own status changes |
| `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` | Add per-tab independence regression and update reset assertions | Required UI test coverage |
| `src/Unlimotion.Test/MainWindowViewModelTests.cs` | Small helper/assertion updates if needed | Keep model tests aligned |
| `specs/2026-06-16-per-tab-status-filters.md` | Working QUEST spec and action log | Required SPEC gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Status filter state | One global `StatusFilters` | One collection per tab |
| All Tasks persistence | `AllTasks:StatusFilters:*` | unchanged |
| Other tabs persistence | shared All Tasks status values | `<Tab>:StatusFilters:*` |
| Roadmap | uses MainWindow global filters | uses Roadmap-specific filters |
| Reset | status reset mutates global values | status reset mutates current tab only |
| Tests | global assertions | per-tab independence assertions |

## 18. Альтернативы и компромиссы
- Вариант: add only transient per-tab state without persistence.
- Плюсы: меньше кода.
- Минусы: regression from existing persisted status-filter behavior; user choices outside All Tasks disappear after restart.
- Почему выбранное решение лучше: per-tab persistence preserves the established settings contract while fixing the global-state bug.

- Вариант: remove `ShowCompleted` / `ShowArchived`.
- Плюсы: cleaner state model.
- Минусы: higher compatibility risk with existing config/tests/AppAutomation scenario data.
- Почему выбранное решение лучше: keeping them as All Tasks bridge narrows the change and avoids unrelated migration work.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals описаны |
| B. Качество дизайна | 6-10 | PASS | Ответственность, persistence, reset, compatibility и performance раскрыты |
| C. Безопасность изменений | 11-13 | PASS | Нет task-storage migration; rollback и edge cases указаны |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, UI tests и команды проверки указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | План, alternatives и отсутствие blocking questions зафиксированы |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client` и `ui-automation-testing` требования отражены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна проблема и явные Non-Goals |
| 2. Понимание текущего состояния | 5 | Указаны конкретные файлы, bindings и shared predicate |
| 3. Конкретность целевого дизайна | 5 | Перечислены properties, defaults, persistence и reset rules |
| 4. Безопасность (миграция, откат) | 5 | Backward compatibility и inert new keys описаны |
| 5. Тестируемость | 5 | Есть UI regression, targeted commands и full test attempt |
| 6. Готовность к автономной реализации | 5 | Нет blocking questions; план executable |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-16-per-tab-status-filters.md`; instruction stack `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, локальный `AGENTS.override.md`; open questions; planned changed files
- Decision: можно запрашивать подтверждение
- Review passes:
  - Scope/Evidence pass: просмотрены `MainWindowViewModel.cs`, `GraphViewModel.cs`, `MainControl.axaml`, `GraphControl.axaml`, `GraphControl.axaml.cs`, `MainControlResetFiltersUiTests.cs`, `MainWindowViewModelTests.cs`, `TestSettings.json`; worktree clean before spec
  - Contract pass: spec covers per-tab state, UI test requirement, visual planning fallback, video fallback, acceptance criteria and validation commands
  - Adversarial risk pass: checked likely regressions around `ShowCompleted` / `ShowArchived`, status-specific tabs, Roadmap rebuilds, reset scope and config compatibility
  - Re-review after fixes / Fix and re-review: no post-review fixes required
  - Stop decision: PASS; ask user for SPEC approval before EXEC
- Evidence inspected: code search for `StatusFilters`, `ShowCompleted`, `ShowArchived`, status combobox automation ids, reset tests and Roadmap subscriptions
- Depth checklist:
  - Scope drift / unrelated changes: implementation not changed; only current spec planned
  - Acceptance criteria: AC1-AC7 cover state independence, reset and compatibility
  - Validation evidence: commands are concrete; no test run yet because SPEC phase only
  - Unsupported claims: all claims tied to inspected files or explicit planned behavior
  - Regression / edge case: status-specific tabs, Roadmap and legacy config covered
  - Comments/docs/changelog: no comments/changelog planned unless implementation reveals need
  - Hidden contract change: per-tab persistence is explicit; legacy All Tasks bridge preserved
  - Manual-review challenge: likely review question is whether Completed/Archived defaults should force visible; spec answers yes for status-specific tabs to avoid empty status tabs
- No-findings justification: spec contains concrete files, test plan, rollback, visual/test evidence fallback and no blocking open questions

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video evidence is not planned because current Avalonia.Headless/TUnit harness has no safe video artifact workflow | Use deterministic UI test output as fallback and report it in final validation | accepted-risk |

- Fixed before continuing: Не применимо
- Checks rerun: SPEC linter/rubric reviewed in this section
- Needs human: требуется фраза "Спеку подтверждаю"
- Residual risks / follow-ups: full test run may be slow or flaky; targeted UI evidence remains mandatory

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: implementation diff for `MainWindowViewModel.cs`, `GraphViewModel.cs`, `MainControl.axaml`, `GraphControl.axaml.cs`, `MainControlResetFiltersUiTests.cs`, `MainWindowViewModelTests.cs`; validation output; process cleanup after full-test timeout
- Decision: implementation satisfies SPEC acceptance criteria with targeted UI/build evidence; full suite remains blocked by local timeout
- Review passes:
  - Scope/Evidence pass: changed files match planned file table; no unrelated code files were edited
  - Contract pass: each task tab has a dedicated status collection; Roadmap resolves `GraphViewModel.StatusFilters` to `RoadmapStatusFilters`; All Tasks legacy bridge is preserved
  - Adversarial risk pass: checked likely regressions around Roadmap rebuild subscriptions, LastOpened wrapper predicate, status-specific reset defaults and non-AllTasks reset mutating All Tasks
  - Re-review after fixes / Fix and re-review: fixed mechanical test assertion placement before final validation; added projection-level regression coverage after review finding
  - Stop decision: PASS with documented full-suite timeout
- Evidence inspected: `rg` scans for old `taskFilter` and status bindings; `git diff --check`; targeted TUnit outputs; build output; `Get-CimInstance Win32_Process` for leftover full-test process tree
- Depth checklist:
  - Scope drift / unrelated changes: only planned implementation/test/spec files changed
  - Acceptance criteria: AC1-AC7 covered by new independence test, reset suite, All Tasks model test, non-AllTasks projection model test, binding scan and build
  - Validation evidence: targeted UI tests and build pass; full suite timed out after 5 minutes and was stopped
  - Unsupported claims: final behavior claims are backed by tests or code scan
  - Regression / edge case: status-specific tabs force own status on reset/default; Roadmap uses independent collection; All Tasks legacy config remains scoped to All Tasks
  - Comments/docs/changelog: no code comments/changelog needed; QUEST spec updated with execution log
  - Hidden contract change: new per-tab config keys are additive and inert for rollback; legacy `AllTasks:*` keys unchanged
  - Manual-review challenge: leaving `GraphViewModel.ShowCompleted` / `ShowArchived` proxies is intentional compatibility; Graph rebuild no longer observes them
- No-findings justification: implementation is scoped, validated by relevant UI tests, and the only incomplete validation is a documented local full-suite timeout

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj` exceeded 5 minutes and left a process tree | Stopped PID 41292 and child MSBuild/Avalonia processes; rely on targeted UI/build evidence and report timeout | accepted-risk |

- Fixed before final report: corrected test assertions so All Tasks reset checks `StatusFilters` and non-AllTasks reset checks the active tab collection; added `StatusFilters_AllTasksSelectionDoesNotFilterLastCreatedOrRoadmapProjections`
- Checks rerun:
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/StatusFilterComboBox_SelectionIsIndependentPerTab"` -> PASS 1/1
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*"` -> PASS 10/10
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/StatusFilters_AllTasksProjection_HidesAndShowsPreparedTasks"` -> PASS 1/1
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/StatusFilters_AllTasksSelectionDoesNotFilterLastCreatedOrRoadmapProjections"` -> PASS 1/1
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/StatusFilters_*"` -> PASS 2/2
  - `dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj --no-restore /nodeReuse:false` -> PASS
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*"` -> PASS 14/14
  - `git diff --check` -> PASS
- Validation evidence: targeted TUnit reports generated under `src\Unlimotion.Test\bin\Debug\net10.0\TestResults\Unlimotion.Test-windows-net10.0-report.html`; full suite timed out after 304 seconds with no summary
- Unrelated changes: none observed beyond current spec and planned implementation/test files
- Needs human: нет
- Residual risks / follow-ups: local full suite may still need a longer dedicated run or CI validation because the 5-minute full-run attempt timed out locally

## Approval
Получена фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Контекст и маршрутизация | 0.95 | Нет | Создать рабочую спецификацию | Да, после post-SPEC review | Нет | Центральный QUEST требует SPEC-first для delivery-task | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `AGENTS.override.md`, memory context |
| SPEC | Анализ текущей реализации | 0.9 | Нет | Зафиксировать per-tab design и tests | Да, после post-SPEC review | Нет | Код показывает один глобальный `StatusFilters`, применяемый ко всем projections | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.ViewModel/GraphViewModel.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` |
| SPEC | SPEC quality gate | 0.9 | Подтверждение пользователя | Запросить фразу "Спеку подтверждаю" | Да | Да, запрос подтверждения в финальном ответе | Linter/rubric/review PASS; EXEC запрещён без явного утверждения | `specs/2026-06-16-per-tab-status-filters.md` |
| EXEC | Подготовка актуального main | 0.95 | Нет | Перечитать актуальный код и добавить regression-тест | Нет | Да, пользователь подтвердил SPEC и попросил актуальный main | Создана ветка `fix/status-filters-per-tab` от свежего `origin/main` `db7c064` без потери spec-файла | git branch, `specs/2026-06-16-per-tab-status-filters.md` |
| EXEC | Reproducing UI regression | 0.95 | Нет | Реализовать per-tab status filter state | Нет | Нет | Новый `StatusFilterComboBox_SelectionIsIndependentPerTab` падает: All Tasks и Last Created используют один `ReadOnlyObservableCollection<TaskStatusFilter>` | `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs`, `dotnet test ... --treenode-filter "/*/*/MainControlResetFiltersUiTests/StatusFilterComboBox_SelectionIsIndependentPerTab"` |
| EXEC | Реализация per-tab status filters | 0.9 | Нет | Запустить targeted UI/build проверки | Нет | Нет | `StatusFilters` сохранён как All Tasks bridge; остальные вкладки получили свои collections, persistence, reset и predicates; Roadmap проксируется через `RoadmapStatusFilters` | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.ViewModel/GraphViewModel.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/GraphControl.axaml.cs` |
| EXEC | Обновление UI regression/reset coverage | 0.9 | Нет | Выполнить validation | Нет | Нет | Тесты теперь проверяют independent references, scoped reset active tab и отсутствие мутации All Tasks при non-AllTasks reset | `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs` |
| EXEC | Validation | 0.85 | Full suite не завершился за 5 минут | Выполнить post-EXEC review | Нет | Нет | Targeted UI/model/build checks прошли; full `dotnet test` timeout after 304s, оставшийся PID 41292 и child processes остановлены | `dotnet test` targeted commands, `dotnet build`, `git diff --check`, process cleanup |
| EXEC | Post-EXEC review | 0.9 | Нет | Сообщить результат пользователю | Нет | Нет | Diff соответствует SPEC; старый shared `taskFilter` удалён из projections; Roadmap status binding использует independent collection; residual risk ограничен full-suite timeout | `specs/2026-06-16-per-tab-status-filters.md`, changed implementation/test files |
| EXEC | Review finding fix | 0.9 | Нет | Сообщить результат пользователю | Нет | Да, пользователь попросил исправить review finding | Добавлен model-level regression, который проверяет не только independent state, но и что All Tasks status selection не фильтрует Last Created/Roadmap projections | `src/Unlimotion.Test/MainWindowViewModelTests.cs`, `dotnet test ... StatusFilters_AllTasksSelectionDoesNotFilterLastCreatedOrRoadmapProjections`, `dotnet test ... StatusFilters_*` |
