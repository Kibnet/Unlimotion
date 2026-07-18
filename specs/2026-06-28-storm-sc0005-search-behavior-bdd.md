# STORM BDD: executable slice для SC-0005-001 search/fuzzy

## 1. Идентификация

- Дата: 2026-06-28.
- Тип: delivery-task / QUEST SPEC / `/storm:bdd-implement SC-0005-001`.
- Route: `/storm:cover -> /storm:bdd-implement`.
- Story: `ST-0005`.
- Acceptance Criteria: `AC-0013`.
- Scenario: `SC-0005-001`.
- Next IDs: `TS-0035`, `SD-0035..SD-0038`.

## 2. Цель

Добавить executable BDD coverage для `SC-0005-001`: текстовый поиск поддерживает обычное и fuzzy-поведение согласно настройкам. Сценарий уже связан с `TS-0001`, `TS-0004`, `TS-0006`, но не имеет repo-local step definitions.

Success means:

1. `SC-0005-001` исполняется из `features/storm/st-0005-search-and-filters.feature` через repo-local BDD runner.
2. Existing links `TS-0001`, `TS-0004`, `TS-0006` preserved.
3. Новый `TS-0035` запускает real UI evidence for normal tree search and fuzzy roadmap search behavior.
4. Production code, `.feature` wording, project files, workflows и existing test annotations не меняются.
5. ST-0005 становится fully step-executable; overall ratio becomes 10/45.

## 3. AS-IS

- `SC-0005-001` находится в `features/storm/st-0005-search-and-filters.feature`, строки 7-13.
- Scenario status is `automated`, linked tests: `TS-0001`, `TS-0004`, `TS-0006`, step definitions: none.
- Direct existing UI evidence:
  - `MainControlTreeCommandsUiTests.TreeSearch_AllTasksSearchEditor_FiltersVisibleTree` checks visible All Tasks search editor filters tree and updates `SearchDefinition.SearchText`.
  - `RoadmapGraphUiTests.RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode` checks exact search, fuzzy miss while disabled, fuzzy highlight when enabled, and clearing search.
- `SC-0005-002` and `SC-0005-003` are already executable via `TS-0033` and `TS-0034`.

## 4. Non-Goals

- Не менять production search/fuzzy behavior.
- Не менять `.feature` text.
- Не менять existing test annotations.
- Не refactor existing UI test helper APIs in this scope.
- Не заявлять full project executable coverage; only ST-0005 becomes fully step-executable.

## 5. Target Design

| Файл | Изменение |
| --- | --- |
| `src/Unlimotion.Test/SearchBehaviorUiContract.cs` | Reusable contract executes existing tree search and roadmap fuzzy UI tests. |
| `src/Unlimotion.Test/StormBdd/SearchBehaviorStepDefinitions.cs` | `SD-0035..SD-0038` bind exact `SC-0005-001` steps. |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Add search behavior context/result fields. |
| `src/Unlimotion.Test/StormSearchBehaviorExecutableSpecTests.cs` | Parses and executes `SC-0005-001`. |
| `docs/product/storm.json` and reports | Sync `SC-0005-001 -> TS-0035 -> SD-0035..SD-0038`, metrics 10/45. |

The BDD contract may call public existing test methods directly. If that creates lifecycle coupling, stop and propose helper extraction as a separate SPEC.

## 6. Acceptance Criteria

1. `StormSearchBehaviorExecutableSpecTests` parses `SC-0005-001` and executes all 4 steps.
2. Step definitions `SD-0035..SD-0038` support only `SC-0005-001`.
3. `TS-0035` exercises normal search and fuzzy behavior through real UI evidence.
4. ST-0005 reports show `SC-0005-001`, `SC-0005-002`, `SC-0005-003` step-executable.
5. No production code, feature wording, existing test annotations, project files or workflows change.

## 7. Validation Plan

1. `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal`.
2. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormSearchBehaviorExecutableSpecTests/*" --output Detailed`.
3. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeSearch_AllTasksSearchEditor_FiltersVisibleTree" --output Detailed`.
4. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode" --output Detailed`.
5. `validate-artifacts.py docs\product\storm.json`, `git diff --check`, trailing whitespace scan.
6. Full `Unlimotion.Test` outside managed sandbox before commit.

## 8. Stop Rules

Stop if direct existing test reuse requires production changes, test annotation changes, feature wording changes, or broad extraction from existing UI tests.

## 9. SPEC Review

- Linter: PASS.
- Rubric: 30/30.
- Post-SPEC Review: PASS. The selected slice closes the remaining ST-0005 executable gap and uses real UI evidence. The only accepted risk is direct public test-method reuse; stop rule covers lifecycle brittleness.
- Needs human: no; active goal includes automatic approval.

## Approval

Получено автоматически из активной цели пользователя: "я автоматически спеку подтверждаю".

## 10. Post-EXEC Review

- Статус: PASS.
- Scope reviewed: new test-only files, StormBdd context fields, docs/product artifact sync and this SPEC.
- Production code, feature wording, project files, workflows и existing test annotations не менялись.
- Evidence:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal` -> прошло с existing warnings.
  - `StormSearchBehaviorExecutableSpecTests` -> прошло 1/1.
  - `MainControlTreeCommandsUiTests/TreeSearch_AllTasksSearchEditor_FiltersVisibleTree` -> прошло 1/1.
  - `RoadmapGraphUiTests/RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode` -> прошло 1/1.
  - `validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 3 known warnings.
  - full `Unlimotion.Test` вне managed sandbox -> прошло 566/566.
- Findings: none.
- Residual risk: validator reports 3 known shared-context warnings для reused ST-0005 steps; full-suite gate прошёл 566/566 вне sandbox.

## 11. Журнал действий агента

| Фаза | Намерение | Уверенность | Следующее действие | Нужен человек | Было решение человека | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего coverage slice | 0.86 | Написать SPEC | Нет | Да: auto-approval | `SC-0005-001` is remaining ST-0005 scenario without step definitions. | `storm.json`, `st-0005-search-and-filters.feature` |
| SPEC | Review SPEC | 0.9 | Перейти к EXEC | Нет | Да: active goal auto-approval | Scope limited to test-only BDD bridge and artifact sync. | `specs/2026-06-28-storm-sc0005-search-behavior-bdd.md` |
| EXEC | Реализация BDD slice SC-0005-001 | 0.88 | Синхронизировать artifacts и выполнить validation gates | Нет | Да: active goal auto-approval | Test-only BDD binding executed real search/fuzzy UI evidence and improved step-executable ratio to 10/45. | `src/Unlimotion.Test/SearchBehaviorUiContract.cs`, `src/Unlimotion.Test/StormBdd/SearchBehaviorStepDefinitions.cs`, `src/Unlimotion.Test/StormSearchBehaviorExecutableSpecTests.cs`, `docs/product/storm.json` |
