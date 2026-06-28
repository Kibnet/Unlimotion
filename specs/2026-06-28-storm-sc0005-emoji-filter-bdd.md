# STORM BDD: executable slice для SC-0005-003 emoji filters

## 1. Идентификация

- Дата: 2026-06-28.
- Тип: delivery-task / QUEST SPEC / `/storm:bdd-implement SC-0005-003`.
- Профиль: `storm-product-development` через central stack.
- Route: `/storm:cover -> /storm:bdd-implement`.
- Story: `ST-0005`.
- Acceptance Criteria: `AC-0015`.
- Scenario: `SC-0005-003`.
- Next IDs: `TS-0034`, `SD-0031..SD-0034`.

## 2. Цель

Добавить executable BDD coverage для `SC-0005-003`: фильтр включения и исключения emoji поддерживает поиск по emoji/text и сохраняет семантику flyout. Сценарий уже имеет linked evidence `TS-0006`, но не имеет repo-local step definitions.

Success means:

1. `SC-0005-003` исполняется из `features/storm/st-0005-search-and-filters.feature` через repo-local BDD runner.
2. Existing `TS-0006` evidence сохранён.
3. Новый `TS-0034` реально запускает UI evidence для include/exclude emoji filters, а не placeholder assertions.
4. Production code, `.feature` wording, project files, workflows и existing test annotations не меняются.
5. Behavior coverage metrics повышаются с 8/45 до 9/45 step-executable scenarios.

## 3. AS-IS

- `SC-0005-003` расположен в `features/storm/st-0005-search-and-filters.feature`, строка 25.
- Scenario tags: `@scenario:SC-0005-003`, `@story:ST-0005`, `@need:ND-0003`, `@constraint:CN-0004`, `@test:TS-0006`.
- `storm.json` marks scenario as `automated`, linked to `TS-0006`, but `step_definitions=[]`.
- Existing UI evidence lives in `MainControlFilterToolbarResponsiveUiTests`:
  - `FilterFlyout_EmojiFilters_OpenFullListThenSearchAndToggleWithoutClosing` checks include/exclude controls, full list, search by text, selection toggle, dropdown stays open, flyout remains open, exclude dropdown opens.
  - `RoadmapFilterFlyout_EmojiFilters_UsesSearchableMultiSelectDropdown` checks roadmap filter flyout uses the same searchable include/exclude controls.
- Previous iteration made `SC-0005-002` step-executable via `TS-0033`; current ratio is 8/45.

## 4. Problem Statement

`SC-0005-003` имеет product/test trace, но не имеет executable binding `Scenario -> Step Definition -> Test`. Это оставляет ST-0005 частично living-spec only: evidence exists, but the Gherkin scenario itself is not executable.

## 5. Non-Goals

- Не менять production code.
- Не менять `.feature` text, даже если scenario title содержит старое сокращение с ellipsis.
- Не менять existing test annotations.
- Не переписывать `MainControlFilterToolbarResponsiveUiTests` и не вытаскивать из него private helpers в этом scope.
- Не заявлять full ST-0005 executable coverage: `SC-0005-001` останется без step definitions.
- Не менять визуальное поведение emoji controls.

## 6. Target Design

Добавить test-only слой:

| Файл | Изменение |
| --- | --- |
| `src/Unlimotion.Test/EmojiFilterUiContract.cs` | Reusable contract запускает existing TS-0006 UI evidence для include/exclude emoji filters и возвращает result flags. |
| `src/Unlimotion.Test/StormBdd/EmojiFilterStepDefinitions.cs` | `SD-0031..SD-0034` bind exact `SC-0005-003` steps to the contract. |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Add scenario context fields for emoji filter result. |
| `src/Unlimotion.Test/StormEmojiFilterExecutableSpecTests.cs` | Parses `SC-0005-003`, executes step definitions, asserts executed IDs. |
| `docs/product/storm.json` and reports | Sync `SC-0005-003 -> TS-0034 -> SD-0031..SD-0034`, metrics 9/45. |

The contract can reuse public existing test methods from `MainControlFilterToolbarResponsiveUiTests` because they already exercise the exact UI behavior. This avoids copying private helper logic and keeps the new BDD layer thin. If direct reuse proves brittle or creates TUnit lifecycle issues, stop and either extract helpers under a separate review or propose a new SPEC.

## 7. Acceptance Criteria

1. `StormEmojiFilterExecutableSpecTests` parses `SC-0005-003` from feature file and executes all 4 steps.
2. Step definitions `SD-0031..SD-0034` support only `SC-0005-003`.
3. `TS-0034` runs real UI evidence for include/exclude emoji filter search/flyout behavior.
4. Existing `TS-0006` remains linked; acceptance criteria are not replaced by Gherkin.
5. `storm.json` and reports show 9/45 step-executable scenarios.
6. No production code, `.feature` wording, existing test annotations, project files or workflows are changed.

## 8. Validation Plan

Run:

1. `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal`.
2. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormEmojiFilterExecutableSpecTests/*" --output Detailed`.
3. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed`.
4. `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`.
5. `git diff --check`.
6. `rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-28-storm-sc0005-emoji-filter-bdd.md`.
7. Full `Unlimotion.Test` outside managed sandbox if sandbox run reproduces Windows ACL inheritance false failure.

## 9. Stop Rules

Stop and propose separate SPEC if:

- direct reuse of existing UI tests requires changing existing test annotations or runner lifecycle;
- BDD binding requires production behavior changes;
- `.feature` wording mismatch cannot be handled without editing feature text;
- existing emoji filter evidence fails for reasons unrelated to the BDD wrapper.

## 10. Risk Review

| Risk | Mitigation |
| --- | --- |
| Placeholder BDD coverage | Contract must execute existing UI evidence; no bool-only pass. |
| Coupling to existing test method names | Acceptable for test-only bridge; if brittle, stop for helper extraction SPEC. |
| Headless UI shared state | Use `NotInParallel("AvaloniaHeadless")` and existing `SharedUiStateParallelLimit`. |
| Full-suite sandbox ACL false failure | Валидировать targeted ACL/full suite вне sandbox по образцу предыдущей итерации. |

## 11. SPEC Review

### Linter Result

| Блок | Статус | Комментарий |
| --- | --- | --- |
| Полнота | PASS | Цель, AS-IS, gap, scope and validation defined. |
| Дизайн | PASS | Test-only BDD bridge with explicit IDs and files. |
| Безопасность | PASS | Production/feature/test-annotation changes forbidden. |
| Проверяемость | PASS | Targeted, artifact and full-suite validation listed. |
| Автономность | PASS | No blocking questions; user goal includes auto-approval. |

Итог: ГОТОВО.

### Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Ясность цели и границ | 5 | One scenario slice with explicit Non-Goals. |
| Понимание AS-IS | 5 | Feature, storm links and TS-0006 evidence inspected. |
| Target design | 5 | Concrete files, IDs and flow. |
| Safety | 5 | Test-only changes and stop rules. |
| Testability | 5 | Commands and expected evidence concrete. |
| Autonomous readiness | 5 | Auto-approval recorded. |

Итог: 30/30.

### Post-SPEC Review

- Статус: PASS.
- Scope reviewed: `SC-0005-003`, feature file, `storm.json`, `MainControlFilterToolbarResponsiveUiTests`, current STORM reports.
- Finding: direct call into existing public test methods is acceptable only because this is test-only BDD bridge and the called methods contain real UI assertions. If it fails due lifecycle coupling, stop instead of broad refactor.
- Needs human: no; active goal includes automatic approval.

## Approval

Получено автоматически из активной цели пользователя: "я автоматически спеку подтверждаю".

## 12. Post-EXEC Review

- Статус: PASS.
- Scope reviewed: new test-only files, StormBdd context fields, docs/product artifact sync and this SPEC.
- Production code, feature wording, project files, workflows и existing test annotations не менялись.
- Evidence:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal` -> прошло с existing warnings.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormEmojiFilterExecutableSpecTests/*" --output Detailed` -> прошло 1/1.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed` -> прошло 14/14.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed` вне managed sandbox -> прошло 565/565.
  - `validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 2 known warnings.
- Findings: none.
- Residual risk: validator reports 2 known shared-context warnings для reused ST-0005 steps; full-suite gate прошёл 565/565 вне sandbox.

## 13. Журнал действий агента

| Фаза | Намерение | Уверенность | Следующее действие | Нужен человек | Было решение человека | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего coverage slice | 0.86 | Написать SPEC | Нет | Да: auto-approval | `SC-0005-003` имеет linked TS-0006, но не имеет step definitions; закрывает следующий ST-0005 executable gap. | `docs/product/storm.json`, `features/storm/st-0005-search-and-filters.feature` |
| SPEC | Review SPEC | 0.9 | Перейти к EXEC | Нет | Да: active goal auto-approval | Scope limited to test-only BDD bridge and artifact sync. | `specs/2026-06-28-storm-sc0005-emoji-filter-bdd.md` |
| EXEC | Реализация BDD slice SC-0005-003 | 0.88 | Синхронизировать artifacts и выполнить validation gates | Нет | Да: active goal auto-approval | Test-only BDD binding executed real emoji filter UI evidence and improved step-executable ratio to 9/45. | `src/Unlimotion.Test/EmojiFilterUiContract.cs`, `src/Unlimotion.Test/StormBdd/EmojiFilterStepDefinitions.cs`, `src/Unlimotion.Test/StormEmojiFilterExecutableSpecTests.cs`, `docs/product/storm.json` |
