# STORM SC-0004-003: executable BDD для команд дерева

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `storm-bootstrap`
- Ограничения: не менять product behavior; не менять `.feature` wording; не менять existing test annotations; не менять production code; продуктовые артефакты вести на русском
- Связанные ссылки: `ST-0004`, `AC-0012`, `GR-012`, `SC-0004-003`, `TS-0004`, `TS-0011`, `MainControl`, `MainWindowViewModel`

## 1. Overview / Цель
Добавить executable BDD layer для `SC-0004-003`: команды дерева поддерживают раскрытие, сворачивание, выбор, удаление, копирование и вставку в рабочих представлениях.

Outcome contract:
- Success means: `SC-0004-003` получает новый executable BDD test `TS-0047`, step definitions `SD-0083..SD-0086`, passing UI/headless evidence, а `ST-0004` становится 3/3 step-executable.
- Итоговый артефакт / output: test-only executable spec + обновленные `storm.json` и reports.
- Stop rules: остановиться, если нужны изменения production behavior, `.feature` wording, UI layout/automation IDs, existing annotations или product decision по новому tree command behavior.

## 2. Текущее состояние (AS-IS)
- `SC-0004-003` связан с `AC-0012`, `GR-012`, `TS-0004`, `TS-0011`, status = `automated`, `step_definitions = []`.
- `ST-0004` после `SC-0004-001` и `SC-0004-002` имеет 2/3 step-executable scenarios.
- `MainControl.axaml` содержит hotkeys/menu bindings для `ExpandAll`, `CollapseAll`, `CopyTaskOutline`, `PasteTaskOutline`, `DeleteSelectedTreeItems` и tree controls.
- `MainControl.axaml.cs` routes tree commands через `ExecuteTreeCommandAction` и active/focused `TreeView`.
- Existing `MainControlTreeCommandsUiTests` уже проверяет copy/paste/delete/current/all tree command flows; новый bridge не должен менять annotations этих tests.

## 3. Проблема
Для `SC-0004-003` нет исполняемой связи `Scenario -> Test -> Step Definition -> UI/ViewModel code`, поэтому `/storm:cover` не может считать команды дерева закрытыми на BDD layer.

## 4. Цели дизайна
- Проверить existing behavior через Avalonia.Headless `MainControl` и bound `MainWindowViewModel` commands.
- Связать scenario wording с repo-local step definitions.
- Сохранить existing tests и annotations.
- Проверить минимальный представительский набор `AC-0012`: выбор, expand/collapse all/current, copy outline, paste outline, delete selection.

## 5. Non-Goals
- Не менять UI layout, labels, selectors или automation IDs.
- Не добавлять видео-артефакты в репозиторий.
- Не реализовывать новые tree command behaviors.
- Не исправлять truncation scenario title в `.feature`; это отдельный artifact/product decision, если понадобится.
- Не запускать `/storm:full-cycle`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `WorkspaceTreeCommandsUiContract` -> test-only Avalonia.Headless flow для открытия `MainControl` и выполнения bound tree commands.
- `WorkspaceTreeCommandsStepDefinitions` -> `SD-0083..SD-0086`, binding product wording к UI contract.
- `StormWorkspaceTreeCommandsExecutableSpecTests` -> `TS-0047`, парсит existing `.feature` scenario и запускает шаги.
- `StormScenarioContext` -> test-only result fields для передачи evidence между steps.
- `storm.json` и reports -> `/storm:bdd-sync`, `/storm:bdd-lint`, behavior metrics.

### 6.2 Детальный дизайн
- UI flow:
  1. Открыть `MainControl` с `MainWindowViewModelFixture` в `HeadlessUnitTestSession`.
  2. Активировать `AllTasksMode`, сфокусировать `AllTasksTree`, выбрать wrapper.
  3. Выполнить `ExpandAllTreeNodesCommand` и `CollapseAllTreeNodesCommand`, проверить recursive expanded/collapsed state.
  4. Выполнить `ExpandCurrentNestedCommand` и `CollapseCurrentNestedCommand` для выбранного wrapper.
  5. Настроить test clipboard delegates, выполнить `CopyTaskOutlineTreeCommand`, проверить copied outline.
  6. Настроить paste clipboard + confirmation mock, выполнить `PasteTaskOutlineTreeCommand`, проверить созданное дерево.
  7. Создать временную задачу, выбрать её в `AllTasksTree`, выполнить `DeleteSelectedTreeItemsCommand`, проверить удаление из storage.
- Visual planning artifact: layout не меняется; reviewer artifact = текстовая state-map:
  - `AllTasksTree.SelectedItem -> CurrentAllTasksItem -> CurrentTaskItem`
  - `ICommand -> ExecuteTreeCommandAction -> MainControl tree route -> TaskWrapperViewModel roots`
  - `Copy/Paste delegates -> TaskOutlineClipboardService -> taskRepository`
- UI test video evidence: fallback. Avalonia.Headless/TUnit runner в этом репозитории не сохраняет безопасное видео; next-best evidence = targeted headless test output, preserved linked tests, full `Unlimotion.Test` gate.

## 7. Бизнес-правила / Алгоритмы
- Tree commands должны применяться к активному или сфокусированному дереву текущего рабочего представления.
- Copy/paste outline должны работать через текущий выбранный wrapper/task.
- Delete selection должен удалять выбранные main-tree задачи после подтверждения.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario(..., "SC-0004-003")`.
- `StormScenarioRunner` executes four feature steps.
- UI contracts: `MainControl`, `AllTasksTree`, bound tree `ICommand` properties.
- ViewModel contracts: `CurrentAllTasksItems`, `CurrentAllTasksItem`, `ExecuteTreeCommandAction`, clipboard delegates, `taskRepository`.

## 9. Изменения модели данных / состояния
Production state не меняется. Test-only fixture создает/вставляет/удаляет временные задачи в тестовом storage и очищает их через `CleanTasks()`.

## 10. Миграция / Rollout / Rollback
Migration не требуется. Rollback: удалить `TS-0047`, `SD-0083..SD-0086`, `WorkspaceTreeCommandsUiContract` и откатить artifact links/metrics.

## 11. Тестирование и критерии приёмки
- `SC-0004-003` исполняется через repo-local steps.
- Tags `@scenario:SC-0004-003`, `@story:ST-0004`, `@test:TS-0004`, `@test:TS-0011` проверены.
- Targeted BDD проходит 1/1.
- Targeted preserved evidence проходит: relevant `MainControlTreeCommandsUiTests`.
- STORM validator проходит 0 errors.
- Full `Unlimotion.Test` проходит или, если unrelated headless teardown flake повторится, failing UI tests должны пройти isolated и controlled retry должен пройти.

## 12. Риски и edge cases
- Риск: commands route зависит от active/focused tree. Смягчение: contract явно фокусирует `AllTasksTree` и синхронизирует selection/current wrapper.
- Риск: copy/paste async execution. Смягчение: ждать clipboard delegate/taskRepository state через `TestHelpers.WaitUntilAsync`.
- Риск: Headless teardown flake. Смягчение: использовать `NotInParallel`, `SharedUiStateParallelLimit`, existing dispose helper.
- Риск: video evidence requirement. Смягчение: явно зафиксирован fallback и next-best evidence.

## 13. План выполнения
1. Создать SPEC и post-SPEC review.
2. Добавить test-only UI contract, context fields, step definitions, executable spec.
3. Обновить STORM artifacts/reports.
4. Запустить targeted BDD/UI checks, STORM validator, full suite.
5. Post-EXEC review и commit.

## 14. Открытые вопросы
Нет блокирующих.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: QUEST gate, Scenario -> Test -> Step Definition -> Code/UI, TUnit `--treenode-filter`, UI/headless evidence, visual artifact fallback, product artifacts на русском.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/WorkspaceTreeCommandsUiContract.cs` | Новый UI contract helper | Проверить `AC-0012` через Avalonia.Headless |
| `src/Unlimotion.Test/StormBdd/WorkspaceTreeCommandsStepDefinitions.cs` | Новый step definition набор | Исполнить `SC-0004-003` |
| `src/Unlimotion.Test/StormWorkspaceTreeCommandsExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result fields | Передать evidence между steps |
| `docs/product/storm.json`, `docs/product/reports/*` | Links/metrics/reports | `/storm:bdd-sync`, `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0004-003` | `automated`, no steps | `passing`, `TS-0047`, `SD-0083..SD-0086` |
| `ST-0004` executable coverage | 2/3 | 3/3 |
| Step-executable scenarios | 21/45 | 22/45 |
| Product behavior | Existing tree commands | Без изменений |

## 18. Альтернативы и компромиссы
- Вызвать existing UI test methods из BDD step: отклонено, чтобы не строить test-to-test dependency.
- Покрывать все рабочие вкладки в новом bridge: отклонено, потому что existing `MainControlTreeCommandsUiTests` уже сохраняют broader tab coverage; BDD bridge проверяет representative command route.
- Исправить truncated scenario title: отклонено в этой SPEC, потому что `.feature` wording менять нельзя без отдельного artifact decision.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals есть |
| B. Качество дизайна | 6-10 | PASS | UI contract, integration points, rollback и state-map описаны |
| C. Безопасность изменений | 11-13 | PASS | Test-only, без product behavior/schema/UI layout changes |
| D. Проверяемость | 14-16 | PASS | Targeted/full checks, UI fallback evidence и stop rules заданы |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет open questions; small slice |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/UI/TUnit требования отражены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один scenario и явные non-goals |
| 2. Понимание текущего состояния | 5 | Existing artifacts, UI controls, command routes and tests указаны |
| 3. Конкретность целевого дизайна | 5 | IDs/files/checks заданы |
| 4. Безопасность (миграция, откат) | 5 | Test-only, rollback перечислен |
| 5. Тестируемость | 5 | Headless UI, targeted BDD, preserved evidence, STORM validator, full suite |
| 6. Готовность к автономной реализации | 5 | Нет blockers; fallback evidence определен |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-07-10-storm-sc0004-tree-commands-bdd.md`, central stack, локальный `AGENTS.override.md`, `ST-0004`, `AC-0012`, `GR-012`, `SC-0004-003`, planned changed files.
- Decision: можно выполнять; active goal задаёт auto approval.
- Review passes:
  - Scope/Evidence pass: проверены `storm.json`, feature file, `MainControl.axaml`, `MainControl.axaml.cs`, `MainWindowViewModel` tree command contracts, existing `MainControlTreeCommandsUiTests`.
  - Contract pass: спецификация не меняет behavior, acceptance criteria, `.feature`, annotations или selectors; UI evidence предусмотрен.
  - Adversarial risk pass: проверен риск video evidence, headless teardown flakes, async copy/paste timing, test-to-test dependency and scenario title truncation.
  - Re-review after fixes / Fix and re-review: не требовалось; direct UI contract выбран вместо вызова existing tests.
  - Stop decision: PASS.
- Evidence inspected: clean worktree after commit `44e2929`, `features/storm/st-0004-workspace-navigation.feature`, `docs/product/storm.json`, relevant `MainControl`/`MainWindowViewModel` snippets, existing linked tests.
- No-findings justification: small test-only BDD slice, объективные UI/video ограничения зафиксированы.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence не создается текущим runner | Зафиксировать fallback и использовать targeted headless/full-suite evidence | accepted-risk |

- Needs human: Нет; active goal auto-approves SPEC.
- Residual risks / follow-ups: после этого slice `ST-0004` должен стать 3/3 step-executable.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, changed test/artifact files, `WorkspaceTreeCommandsUiContract`, `WorkspaceTreeCommandsStepDefinitions`, `StormWorkspaceTreeCommandsExecutableSpecTests`, `StormStepDefinition`, `storm.json`, reports and validation evidence.
- Decision: можно коммитить.
- Review passes:
  - Scope/Evidence pass: изменения ограничены test-only BDD bridge, SPEC and STORM artifacts.
  - Contract pass: `SC-0004-003` получил `TS-0047` and `SD-0083..SD-0086`; production code, `.feature`, automation IDs and existing annotations unchanged.
  - Adversarial risk pass: проверены risks test-to-test dependency, active tree route, async copy/paste timing, video fallback and full-suite stability.
  - Re-review after fixes / Fix and re-review: direct UI contract validated; artifacts re-synced with final controlled retry evidence.
  - Stop decision: PASS.
- Evidence inspected:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false` => passed, existing warnings.
  - `StormWorkspaceTreeCommandsExecutableSpecTests` => passed 1/1.
  - `MainControlTreeCommandsUiTests` class => 42/43 due unrelated `TreeSearch_ClearSearch_RestoresExpansionState(CompletedTree)` storage timeout; isolated rerun passed 7/7.
  - `TreeCommandUi_CopyTaskOutline_HotkeyAndContextMenu_Work` => passed 1/1.
  - `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` => passed 1/1.
  - `TreeCommandUi_ShiftDelete_RemovesSelectedMainTreeItems` => passed 1/1.
  - `TreeCommandUi_LastCreatedTab_HotkeyAndContextMenu_Work` => passed 1/1.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` => OK: 0 errors, 9 warnings.
  - Initial full `Unlimotion.Test` => 577/578 because unrelated `Avalonia.Headless.DisposeAsync` teardown NRE in `SearchBehaviorScenario_ExecutesFeatureSteps`; isolated rerun passed 1/1.
  - Controlled full `Unlimotion.Test` retry => passed 578/578 with `C:\tmp\unlimotion-full-suite-sc0004-tree-commands-bdd-retry.log`.
- Depth checklist:
  - Scope drift / unrelated changes: no production, feature, selector or annotation changes.
  - Acceptance criteria: `AC-0012` covered by representative expand/collapse, selection, copy, paste and delete command assertions.
  - Validation evidence: targeted, preserved UI evidence, STORM validator and controlled full suite present.
  - Unsupported claims: video evidence marked fallback, not claimed as produced.
  - Regression / edge case: unrelated class/full-suite failures isolated and controlled retry passed.
  - Comments/docs/changelog: no code comments/changelog needed; product reports updated.
  - Hidden contract change: none; UI contract uses existing `AllTasksTree` and bound commands.
  - Manual-review challenge: reviewer would check whether this is real command-route evidence; `MainControl` opens in Avalonia.Headless and executes `ICommand` routes through `ExecuteTreeCommandAction`.
- No-findings justification: implementation follows existing STORM BDD/UI patterns, validates through headless UI command routes, and changes only tests/artifacts.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video artifact not produced by current Avalonia.Headless runner | Preserve explicit fallback and use targeted/full-suite evidence | accepted-risk |
| LOW | validation | Preserved class/full-suite gates hit unrelated timeout/teardown flakes | Isolate failing tests, run direct command checks and controlled full retry, record evidence | resolved |

- Fixed before final report: Artifacts updated from pending to final full-suite evidence.
- Checks rerun: build, targeted BDD, preserved tree command evidence, STORM validator, isolated flake proofs, full-suite controlled retry.
- Validation evidence: listed above.
- Unrelated changes: none observed in task scope.
- Needs human: Нет.
- Residual risks / follow-ups: `ST-0004` is now 3/3 step-executable; next `/storm:cover` candidate should be selected outside `ST-0004`.
## Approval
Подтверждено активной целью пользователя: SPEC auto-approved for execution.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0004-003` | 0.87 | Нет | Перейти к EXEC | Нет | Да, active goal auto approval | UI-facing scenario требует headless UI executable bridge без product-code changes | `specs/2026-07-10-storm-sc0004-tree-commands-bdd.md` |
| EXEC | executable BDD UI slice | 0.92 | Нет | Commit и перейти к следующему `/storm:cover` candidate | Нет | Нет | Targeted/full gates passed; `SC-0004-003` закрыт step-executable | `src/Unlimotion.Test/StormWorkspaceTreeCommandsExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/WorkspaceTreeCommandsStepDefinitions.cs`, `src/Unlimotion.Test/WorkspaceTreeCommandsUiContract.cs`, `docs/product/storm.json`, `docs/product/reports/*` |
