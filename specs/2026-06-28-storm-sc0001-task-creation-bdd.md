# STORM BDD: executable slice для SC-0001-001 task creation graph

## 1. Идентификация

- Дата: 2026-06-28.
- Тип: delivery-task / QUEST SPEC / `/storm:bdd-implement SC-0001-001`.
- Route: `/storm:cover -> /storm:bdd-implement`.
- Story: `ST-0001`.
- Acceptance Criteria: `AC-0001`.
- Scenario: `SC-0001-001`.
- Next IDs: `TS-0036`, `SD-0039..SD-0042`.

## 2. Цель

Добавить executable BDD coverage для `SC-0001-001`: задачу можно создать в корне, рядом с выбранной задачей, как заблокированного соседа или внутри выбранной задачи. Сценарий уже связан с `TS-0001` и `TS-0004`, но не имеет repo-local step definitions.

Success means:

1. `SC-0001-001` исполняется из `features/storm/st-0001-task-graph.feature` через repo-local BDD runner.
2. Existing links `TS-0001` и `TS-0004` preserved.
3. Новый `TS-0036` запускает real evidence для root/sibling/blocked/inner creation и UI создания вложенного дерева через выбранную задачу.
4. Production code, `.feature` wording, project files, workflows и existing test annotations не меняются.
5. Step-executable coverage increases from 10/45 to 11/45.

## 3. AS-IS

- `SC-0001-001` находится в `features/storm/st-0001-task-graph.feature`, строки 7-13.
- Scenario status is `automated`, linked tests: `TS-0001`, `TS-0004`, step definitions: none.
- Direct existing evidence:
  - `MainWindowViewModelTests.CreateRootTask_Success` confirms root task creation.
  - `MainWindowViewModelTests.CreateSiblingTask_Success` confirms sibling creation next to selected task.
  - `MainWindowViewModelTests.CreateBlockedSibling_Success` confirms blocked sibling creation.
  - `MainWindowViewModelTests.CreateInnerTask_Success` confirms inner child task creation.
  - `MainControlTreeCommandsUiTests.TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` confirms UI-level creation under a selected task through an available interface action.

## 4. Non-Goals

- Не менять production task creation behavior.
- Не менять `.feature` text.
- Не менять existing test annotations.
- Не refactor existing test helper APIs in this scope.
- Не закрывать всю `ST-0001`; only `SC-0001-001` становится step-executable.

## 5. Target Design

| Файл | Изменение |
| --- | --- |
| `src/Unlimotion.Test/TaskCreationGraphUiContract.cs` | Reusable contract executes existing creation and UI evidence tests. |
| `src/Unlimotion.Test/StormBdd/TaskCreationGraphStepDefinitions.cs` | `SD-0039..SD-0042` bind exact `SC-0001-001` steps. |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Add task creation context/result fields. |
| `src/Unlimotion.Test/StormTaskCreationGraphExecutableSpecTests.cs` | Parses and executes `SC-0001-001`. |
| `docs/product/storm.json` and reports | Sync `SC-0001-001 -> TS-0036 -> SD-0039..SD-0042`, metrics 11/45. |

The BDD contract may call public existing test methods directly. If that creates lifecycle coupling, stop and propose helper extraction as a separate SPEC.

## 6. Acceptance Criteria

1. `StormTaskCreationGraphExecutableSpecTests` parses `SC-0001-001` and executes all 4 steps.
2. Step definitions `SD-0039..SD-0042` support only `SC-0001-001`.
3. `TS-0036` exercises root, sibling, blocked sibling, inner creation and UI creation-under-selected evidence.
4. `SC-0001-001` reports `passing` with `TS-0036` and `SD-0039..SD-0042`.
5. No production code, feature wording, existing test annotations, project files or workflows change.

## 7. Validation Plan

1. `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal`.
2. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskCreationGraphExecutableSpecTests/*" --output Detailed`.
3. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/CreateRootTask_Success" --output Detailed`.
4. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/CreateSiblingTask_Success" --output Detailed`.
5. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/CreateBlockedSibling_Success" --output Detailed`.
6. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/CreateInnerTask_Success" --output Detailed`.
7. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --output Detailed`.
8. `validate-artifacts.py docs\product\storm.json`, `git diff --check`, trailing whitespace scan.
9. Full `Unlimotion.Test` outside managed sandbox before commit.

## 8. Stop Rules

Stop if direct existing test reuse requires production changes, test annotation changes, feature wording changes, project file changes, or broad extraction from existing tests.

## 9. SPEC Review

- Linter: PASS.
- Rubric: 30/30.
- Post-SPEC Review: PASS. The selected slice is the highest-ranked remaining cover candidate and uses existing VM/UI evidence without changing product behavior.
- Needs human: no; user confirmed SPEC in chat.

## Approval

Подтверждено пользователем: "спеку подтверждаю".

## 10. Post-EXEC Review

- Статус: PASS.
- Scope reviewed: new test-only files, StormBdd context fields, docs/product artifact sync and this SPEC.
- Production code, feature wording, project files, workflows и existing test annotations не менялись.
- Evidence:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal` -> прошло с existing warnings.
  - `StormTaskCreationGraphExecutableSpecTests` -> прошло 1/1.
  - `MainWindowViewModelTests/CreateRootTask_Success` -> прошло 1/1.
  - `MainWindowViewModelTests/CreateSiblingTask_Success` -> прошло 2/2.
  - `MainWindowViewModelTests/CreateBlockedSibling_Success` -> прошло 2/2.
  - `MainWindowViewModelTests/CreateInnerTask_Success` -> прошло 2/2.
  - `MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` -> прошло 1/1.
  - `validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 3 known warnings.
  - full `Unlimotion.Test` вне managed sandbox -> прошло 567/567; sandboxed run reproduces known ACL-only failure in `BackupViaGitServiceTests.GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows`.
- Findings: none.
- Residual risk: direct public test-method reuse remains accepted until a later helper-extraction SPEC; full-suite gate passed outside sandbox.

## 11. Журнал действий агента

| Фаза | Намерение | Уверенность | Следующее действие | Нужен человек | Было решение человека | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего coverage slice | 0.9 | Написать SPEC | Нет | Да | `SC-0001-001` is the highest-ranked remaining scenario without step definitions. | `storm.json`, `st-0001-task-graph.feature` |
| SPEC | Review SPEC | 0.9 | Перейти к EXEC | Нет | Да | Scope limited to test-only BDD bridge and artifact sync. | `specs/2026-06-28-storm-sc0001-task-creation-bdd.md` |

| EXEC | Реализация BDD slice SC-0001-001 | 0.88 | Синхронизировать artifacts и выполнить validation gates | Нет | Да | Test-only BDD binding executed real task creation evidence and improved step-executable ratio to 11/45. | `src/Unlimotion.Test/TaskCreationGraphUiContract.cs`, `src/Unlimotion.Test/StormBdd/TaskCreationGraphStepDefinitions.cs`, `src/Unlimotion.Test/StormTaskCreationGraphExecutableSpecTests.cs`, `docs/product/storm.json` |
