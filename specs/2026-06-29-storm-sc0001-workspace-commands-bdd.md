# STORM BDD: executable slice для SC-0001-003 workspace commands

## 1. Метаданные

- Статус: Draft for review -> auto-approved by active goal.
- Тип: QUEST `delivery-task` / `/storm:cover -> /storm:bdd-implement SC-0001-003`.
- Дата: 2026-06-29.
- Автор: Codex.
- Story: `ST-0001`.
- Acceptance criteria: `AC-0003`.
- Scenario: `SC-0001-003`.
- Target test: `TS-0038`.
- Target step definitions: `SD-0047..SD-0050`.

## 2. Цель

Добавить executable BDD coverage для `SC-0001-003`: перетаскивание, команды дерева и редактор отношений позволяют прикреплять, перемещать, блокировать, обратно блокировать, клонировать, удалять и выбирать задачи из активных представлений.

Success means:

- `SC-0001-003` исполняется из `features/storm/st-0001-task-graph.feature`.
- Existing `TS-0004` сохраняется.
- Новый `TS-0038` связывает scenario text with real existing ViewModel/Avalonia.Headless evidence.
- STORM metrics advance from `12/45` to `13/45`.

## 3. AS-IS

- `SC-0001-003` существует и связан с `TS-0004`, но `step_definitions` пустой.
- `ST-0001` уже имеет step-executable `SC-0001-001` and `SC-0001-002`; `SC-0001-003` is the remaining linked-existing-tests-only scenario.
- Existing evidence includes:
  - `MainControlTreeCommandsUiTests`
  - `MainControlRelationPickerUiTests`
  - `MainWindowViewModelTests`
- Full `Unlimotion.Test` currently passes 568/568 outside managed sandbox after stability-gate retry.

## 4. Non-Goals

- Не менять production code.
- Не менять `.feature` wording.
- Не менять existing test annotations.
- Не заменять acceptance criteria на Gherkin.
- Не добавлять skip/ignore.
- Не пытаться покрыть все возможные drag/drop permutations beyond existing evidence.

## 5. Target Design

Add test-only files:

- `src/Unlimotion.Test/TaskGraphWorkspaceCommandContract.cs`
- `src/Unlimotion.Test/StormBdd/TaskGraphWorkspaceCommandStepDefinitions.cs`
- `src/Unlimotion.Test/StormTaskGraphWorkspaceCommandExecutableSpecTests.cs`

Update:

- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs`
- `docs/product/storm.json`
- `docs/product/reports/coverage.md`
- `docs/product/reports/bdd-sync.md`
- `docs/product/reports/bdd-lint.md`

The contract wrapper will execute existing tests for:

- relation editor attach parent from task card;
- move blocked task to a new parent;
- copy blocked task to a new parent;
- block link and reverse block link creation;
- clone into destination;
- task deletion;
- active-view selection sync;
- tree UI Shift+Delete and Ctrl+A active-tree behavior;
- drag-preparation multi-selection preservation.

## 6. Validation Plan

1. Build:
   - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal`
2. New executable scenario:
   - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskGraphWorkspaceCommandExecutableSpecTests/*" --output Detailed`
3. Targeted evidence filters:
   - `MainControlRelationPickerUiTests/TaskCardRelationEditor_AddParentFromCard_UpdatesStorage`
   - `MainWindowViewModelTests/MoveBlockedTaskToNewParent_WithFileStorage_ShouldBlockNewParent`
   - `MainWindowViewModelTests/CopyBlockedTaskToNewParent_WithFileStorage_ShouldBlockNewParent`
   - `MainWindowViewModelTests/AddBlokedByLinkTask_Success`
   - `MainWindowViewModelTests/AddReverseBlokedByLinkTask_Success`
   - `MainWindowViewModelTests/CloneTask_Success`
   - `MainWindowViewModelTests/CurrentTaskItemRemove_Success`
   - `MainWindowViewModelTests/SelectCurrentTaskMode_SyncsCorrectly`
   - `MainControlTreeCommandsUiTests/TreeCommandUi_ShiftDelete_RemovesSelectedMainTreeItems`
   - `MainControlTreeCommandsUiTests/TreeCommandUi_CtrlA_SelectsAllItemsInActiveTree`
   - `MainControlTreeCommandsUiTests/TreeDragUi_DragPreparation_PreservesExistingMultiSelectionVisualState`
4. Artifact gates:
   - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
   - `git diff --check`
   - `rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-29-storm-sc0001-workspace-commands-bdd.md`
5. Full suite outside managed sandbox:
   - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed *> C:\tmp\unlimotion-full-suite-sc0001-003.log`

## 7. Acceptance Criteria

1. `SC-0001-003.status = passing`.
2. `SC-0001-003.linked_tests` includes `TS-0038`, preserving `TS-0004`.
3. `SC-0001-003.step_definitions = SD-0047..SD-0050`.
4. `ST-0001` has 3/3 step-executable scenarios.
5. `step_definition_coverage_ratio = 13/45`.
6. Production code, feature wording and existing test annotations unchanged.

## 8. Risks

- This scenario is broad. Mitigation: reuse existing tests and report exact evidence boundaries.
- UI tests can be flaky/order-sensitive. Mitigation: targeted runs before full suite and no skip/ignore.
- Direct test-method reuse couples lifecycle. Mitigation: localized contract wrapper; helper extraction remains separate future refactor.

## 9. SPEC Linter Result

| Блок | Статус | Комментарий |
| --- | --- | --- |
| Полнота | PASS | Goal, AS-IS, target files, validation and AC listed. |
| Scope control | PASS | No production behavior and no annotations changes. |
| Safety | PASS | Stop rules and full-suite evidence required. |
| Testability | PASS | Concrete TUnit filters provided. |

Итог: ГОТОВО

## 10. SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Ясность цели | 5 | One scenario and fixed IDs. |
| AS-IS | 5 | Scenario, linked tests and full-suite state known. |
| Дизайн | 5 | Test-only contract plus artifact sync. |
| Безопасность | 5 | Production behavior and existing annotations protected. |
| Проверяемость | 5 | Targeted and full commands listed. |
| Автономность | 5 | Active goal auto-approves execution. |

Итоговый балл: 30 / 30

## 11. Post-SPEC Review

- Статус: PASS.
- Scope reviewed: `SC-0001-003`, `TS-0004`, feature text, selected existing tests, current full-suite evidence.
- Decision: proceed to EXEC.
- Findings requiring edits: none.
- Residual risk: broad scenario uses evidence composition rather than one end-to-end UI drag/drop test; this is acceptable for a test-only BDD bridge and should be described in artifacts.

## 12. Approval

Получено автоматически из активной цели пользователя: "я автоматически спеку подтверждаю".

## 13. Post-EXEC Review

- Статус: PASS for scoped `SC-0001-003` slice; full-suite gate blocked by unrelated filter-flyout UI test cleanup/order issue.
- Scope reviewed: `src/Unlimotion.Test/StormTaskGraphWorkspaceCommandExecutableSpecTests.cs`, `src/Unlimotion.Test/TaskGraphWorkspaceCommandContract.cs`, `src/Unlimotion.Test/StormBdd/TaskGraphWorkspaceCommandStepDefinitions.cs`, `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs`, STORM reports.
- Implemented: `SC-0001-003` now has `TS-0038` and `SD-0047..SD-0050`; existing `TS-0004` remains linked.
- Targeted validation passed:
  - Build passed after stopping stale `Unlimotion.Test.exe` file-lock process from an earlier timed-out run.
  - `StormTaskGraphWorkspaceCommandExecutableSpecTests` passed 1/1.
  - Relation editor evidence passed 1/1.
  - Move/copy/block/reverse-block/clone/delete/select ViewModel evidence passed.
  - Tree command Shift+Delete, Ctrl+A and drag-preparation UI evidence passed.
- Review passes:
  - Scope/Evidence pass: changed files match the approved test-only bridge and artifact sync.
  - Contract pass: production code, feature wording and existing annotations unchanged.
  - UI evidence pass: Avalonia.Headless tree and relation editor checks included.
  - Stop decision: PASS for scoped test-only BDD slice; do not change unrelated filter-flyout tests under this SPEC.
- Final validation:
  - Artifact validator passed with 0 errors and 4 intentional duplicate-step warnings.
  - `git diff --check` passed with LF-to-CRLF working-copy warnings only.
  - Trailing whitespace scan returned no matches (`rg` exit 1).
  - Full `Unlimotion.Test` outside managed sandbox failed 568/569 on unrelated `MainControlFilterToolbarResponsiveUiTests/FilterFlyout_EmojiFilters_AllItemTogglesEveryEmojiFilter`.
  - Full `MainControlFilterToolbarResponsiveUiTests` class failed 13/14 on that same test, while the individual test passed 1/1.
- Residual risks / follow-ups: scenario text remains broader than any single end-to-end drag/drop UI flow; artifacts describe the composed evidence boundary. Full-suite stability now needs a separate QUEST stabilization SPEC before broad `/storm:cover` expansion.

## 14. Журнал действий агента

| Фаза | Сценарий | Уверенность | Следующее действие | Нужен человек | Объяснение |
| --- | --- | ---: | --- | --- | --- |
| SPEC | `SC-0001-003` workspace command bridge | 0.84 | Реализовать test-only BDD bridge | Нет | Последний ST-0001 scenario без step definitions; full-suite gate восстановлен. |
| EXEC | Test-only BDD bridge | 0.86 | Commit scoped slice, then open stabilization SPEC | Нет | New `TS-0038` passes and targeted evidence filters pass; full-suite blocker is unrelated. |
