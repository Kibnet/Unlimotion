# Paste outline full-suite stability

## 1. Метаданные

- Статус: Draft for review -> auto-approved by active goal.
- Тип: QUEST `delivery-task` / production race stabilization.
- Дата: 2026-06-29.
- Затронутая область: `MainWindowViewModel.PasteTaskOutline`.

## 2. Проблема

После test-only стабилизации filter flyout полный `Unlimotion.Test` вне sandbox падает 568/569 на `MainWindowViewModelTests/PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`.

Evidence:

- Full suite: failed 568/569 with `C:\tmp\unlimotion-full-suite-filter-flyout-stability.log`.
- Isolated `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`: passed 1/1.
- Full `MainWindowViewModelTests`: passed 95/95.

Stack trace includes an unobserved `TaskTreeManager.UpdateTask` timeout. The paste outline implementation creates a task with `Add`/`AddChild`, mutates `TaskItemViewModel` properties (`Title`, `Description`, `Status`), and then explicitly calls `taskRepository.Update(created)`. Those property mutations can also schedule throttled autosaves through `TaskItemViewModel.PropertyChanged`, so full-suite load can run duplicate updates against the same freshly created task graph.

## 3. Цель

Remove the duplicate pre-update autosave path for outline paste import while preserving behavior:

- Populate a `TaskItem` snapshot for the newly created task.
- Perform one explicit `taskRepository.Update(TaskItem)` per imported node.
- Keep confirmation, destination capture, recursive parent/child creation, status/description import and selection behavior unchanged.

## 4. Non-Goals

- No changes to feature wording, existing test annotations, project files or workflows.
- No skip/ignore.
- No broad storage or `TaskItemViewModel` autosave refactor; only a narrow guard is allowed if model-sync still triggers duplicate autosave evidence.
- No change to normal interactive editing autosave behavior.

## 5. Target Design

Update `src/Unlimotion.ViewModel/MainWindowViewModel.cs`:

- In `CreateTaskFromOutlineNode`, after `Add`/`AddChild`, copy `created.Model` into a local `TaskItem`.
- Apply `node.Title`, optional `Description`, and optional `Status`/history to that local model.
- Call `taskRepository.Update(createdModel)` once.
- Refresh `created` from the update result or repository lookup before recursing into child nodes.

If full-suite validation still shows `TaskItemViewModel` autosave during `UpdateCache -> TaskItemViewModel.Update(...)`, update `src/Unlimotion.ViewModel/TaskItemViewModel.cs` narrowly:

- Reuse `_isUpdatingFromModel` to block autosave subscriptions while VM state is synchronized from a storage model.
- Keep normal user-edit autosave behavior unchanged.

If the remaining full-suite failure is limited to the reusable UI hotkey paste contract under load:

- Keep product behavior unchanged.
- Replace the 2-second/CPU-spin paste and relation readiness waits in `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` with bounded async polling using the existing `SearchExpansionWaitMilliseconds`.

If outline copy test setup still triggers the same duplicate autosave pattern:

- Keep assertions and product behavior unchanged.
- Prepare fixture task data through a `TaskItem` snapshot helper and one explicit repository update instead of VM property setters followed by explicit update.

## 6. Validation Plan

1. `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false`
2. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/PasteTaskOutline_CreatesNestedTasksUnderCurrentTask" --output Detailed`
3. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/*PasteTaskOutline*" --output Detailed`
4. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --output Detailed`
5. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed *> C:\tmp\unlimotion-full-suite-paste-outline-stability.log`
6. `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
7. `git diff --check`
8. `rg -n "[ \t]+$" src\Unlimotion.ViewModel src\Unlimotion.Test docs\product specs\2026-06-29-paste-outline-full-suite-stability.md`

## 7. Acceptance Criteria

1. Paste outline creates the same nested task graph under the selected destination.
2. Paste outline description/status preview behavior remains covered.
3. Existing UI hotkey paste flow still passes.
4. Full suite passes, or any remaining failure is new and documented separately.
5. STORM artifacts/reports reflect the actual full-suite gate state.

## 8. Review

- SPEC linter: PASS.
- Rubric: 30/30.
- Post-SPEC review: PASS; the change addresses the observed duplicate update race at the paste-outline command boundary, with narrow validation around ViewModel behavior and existing UI hotkey evidence.
- Approval: active goal auto-confirms execution.

## 9. Post-EXEC Review

- Статус: completed.
- Изменение product code: `CreateTaskFromOutlineNode` теперь заполняет `TaskItem` snapshot и делает один explicit `taskRepository.Update(TaskItem)` на imported node.
- Изменение product code: `TaskItemViewModel` autosave subscriptions now skip model-sync updates через существующий `_isUpdatingFromModel` guard; normal user-edit autosave remains enabled.
- Изменение tests: outline copy setup обновляет fixture tasks через `TaskItem` snapshot helper вместо VM setters + explicit update.
- Изменение UI tests: paste hotkey creation/relation readiness uses bounded async polling under full-suite load.
- `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal /nr:false`: passed, existing warnings only.
- `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/*TaskOutline*" --output Detailed`: passed 7/7.
- `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --output Detailed`: passed 1/1.
- `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskCreationGraphExecutableSpecTests/TaskCreationGraphScenario_ExecutesFeatureSteps" --output Detailed`: passed 1/1.
- Full `Unlimotion.Test` outside sandbox with `C:\tmp\unlimotion-full-suite-paste-outline-stability.log`: passed 569/569.
- `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`: OK, 0 errors, 4 intentional shared-step warnings.
- `git diff --check`: passed with LF-to-CRLF working-copy warnings only.
- `rg -n "[ \t]+$" src\Unlimotion.ViewModel src\Unlimotion.Test docs\product specs\2026-06-29-paste-outline-full-suite-stability.md specs\2026-06-29-filter-flyout-cleanup-stability.md`: no matches (`rg` exit 1).
- `.feature` wording, existing annotations, project files and workflows were not changed.
- Next action: commit this stability slice, then resume `/storm:cover` ranking from the next non-step-executable scenario.
