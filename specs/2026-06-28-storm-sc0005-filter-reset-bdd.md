# STORM BDD: executable slice для SC-0005-002 reset filters

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / `/storm:bdd-implement SC-0005-002`.
- Владелец: Codex; пользователь заранее подтвердил автоматический переход SPEC -> EXEC в активной цели.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`.
- Instruction stack: central `AGENTS.md` -> `routing-matrix.md` -> `model-behavior-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `storm-product-development`; local `AGENTS.override.md` applied after central stack.
- Ограничения:
  - До завершения SPEC менять только этот файл.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие STORM artifacts.
  - Не менять product behavior, UI layout, selectors, `.feature` wording, production code, package/project/workflow files.
  - Разрешены только test-only BDD artifacts and STORM artifact/report sync после фактического evidence.
  - Если existing behavior не подтверждается test-only executable slice, остановиться и оформить отдельную bugfix SPEC.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `features/storm/st-0005-search-and-filters.feature`
  - `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs`
  - `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`
  - `src/Unlimotion.Test/StormBdd/StormScenarioRunner.cs`
  - `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Добавить executable BDD coverage для `ST-0005 / AC-0014 / SC-0005-002`: сценарий "Фильтры статуса, дат, длительности и wanted применяются вместе и могут быть сброшены" уже связан с `TS-0006` и `TS-0013`, но не имеет repo-local step definitions.

Outcome contract:
- Success means: `SC-0005-002` исполняется из `features/storm/st-0005-search-and-filters.feature` через step definitions и TUnit test, не меняя production behavior.
- Итоговый output: test-only executable slice, обновлённые STORM artifacts/reports, SPEC Post-EXEC evidence, отдельный commit.
- Stop rules:
  - Остановиться, если для прохождения сценария нужен production code/UI behavior change.
  - Остановиться, если нужно менять `.feature` wording или acceptance criteria.
  - Остановиться, если targeted UI/headless evidence нестабильно падает после одного корректного rerun на той же поверхности.
  - Не запускать широкие environment repair/build workload commands в этой SPEC.

## 2. Текущее состояние (AS-IS)

- `SC-0005-002` находится в `features/storm/st-0005-search-and-filters.feature`, строки 15-21.
- Scenario tags уже содержат `@scenario:SC-0005-002`, `@story:ST-0005`, `@need:ND-0003`, `@constraint:CN-0004`, `@test:TS-0006`, `@test:TS-0013`.
- В `storm.json` сценарий имеет `status = automated`, linked tests `TS-0006`, `TS-0013`, но `step_definitions = []`.
- Существующие UI tests уже проверяют reset behavior:
  - `MainControlResetFiltersUiTests.ResetFiltersButton_OnStatusFilteredTabs_ResetsStatusFilters`
  - `AllTasksResetFilters_AfterConfirmation_ResetsOnlyAllTasksFiltersToDefaults`
  - `LastCreatedResetFilters_AfterConfirmation_ResetsCurrentDateFilterToDefault`
  - Roadmap reset variants for hidden wanted/completion filters.
- Repo-local BDD pattern уже существует:
  - `StormFeatureParser.ParseScenario(...)`
  - `StormScenarioRunner`
  - `Storm*ExecutableSpecTests`
  - `StormBdd/*StepDefinitions.cs`
- Current executable ratio: 7/45 scenarios.

## 3. Проблема

`SC-0005-002` имеет product/test trace, но не имеет executable BDD binding `Scenario -> Step Definition -> Test`, поэтому `/storm:cover` не приближает living product spec к полному executable coverage для фильтров.

## 4. Цели дизайна

- Разделение ответственности: BDD step definitions связывают feature text с reusable test contract; existing UI tests остаются regression coverage.
- Повторное использование: вынести reset-filter проверки в reusable `FilterResetUiContract`, чтобы existing tests and BDD test могли использовать один contract.
- Тестируемость: новый `StormFilterResetExecutableSpecTests` должен исполнять `.feature` text через `StormScenarioRunner`.
- Консистентность: использовать следующий стабильный диапазон IDs `SD-0027..SD-0030`.
- Обратная совместимость: не менять UI behavior, selectors, `.feature` wording или acceptance criteria.

## 5. Non-Goals

- Не менять production code.
- Не менять `.feature` text, acceptance criteria, story wording или existing scenario semantics.
- Не менять UI layout, AutomationId, localization text, filters UX.
- Не добавлять массовые placeholder step definitions для всех оставшихся scenarios.
- Не пересчитывать ranking полностью; только синхронизировать текущий coverage/result.
- Не делать runtime/video artifact, потому что runner is Avalonia.Headless and repository has no established video capture for these tests; fallback evidence: targeted headless TUnit command output and executable scenario trace.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `src/Unlimotion.Test/FilterResetUiContract.cs` -> reusable contract for reset filter behavior used by existing UI regression tests and BDD steps.
- `src/Unlimotion.Test/StormBdd/FilterResetStepDefinitions.cs` -> binds exact `SC-0005-002` Given/When/Then text to the reusable contract.
- `src/Unlimotion.Test/StormFilterResetExecutableSpecTests.cs` -> parses feature file, executes scenario, asserts executed `SD-0027..SD-0030`.
- `docs/product/storm.json` -> canonical Scenario/Test/StepDefinition sync after evidence.
- `docs/product/reports/*` -> coverage, traceability, bdd-sync, bdd-lint, stories, ranking sync.
- This SPEC -> QUEST trace and Post-EXEC review.

### 6.2 Детальный дизайн

Data/test flow:
1. `StormFilterResetExecutableSpecTests` parses `SC-0005-002` from `features/storm/st-0005-search-and-filters.feature`.
2. `StormScenarioRunner` executes exact steps:
   - `Дано у пользователя открыт актуальный набор задач Unlimotion`
   - `И поведение относится к истории ST-0005`
   - `Когда пользователь меняет статус задачи или проверяет доступные переходы`
   - `Тогда Фильтры статуса, дат, длительности и wanted применяются вместе и могут быть сброшены.`
3. Step definitions record scenario context and call `FilterResetUiContract.ExecuteFilterResetScenarioAsync()`.
4. Contract verifies current behavior through Avalonia.Headless:
   - filter panel/reset action is available;
   - active filters can be applied;
   - confirmation is asked;
   - status/date/duration/wanted-related filters reset according to current tab contract;
   - no product behavior changes are required.

Output contract / evidence rules:
- Scenario status becomes `passing`.
- `SC-0005-002.step_definitions = SD-0027..SD-0030`.
- Add/refresh test evidence for `TS-0033` or next available test id.
- Do not claim full `ST-0005` executable coverage; only `SC-0005-002`.
- Remaining scenarios count must decrease from 38 to 37 without step definitions; executable ratio increases from 7/45 to 8/45.

Visual planning artifact:
- Не применимо как отдельный layout artifact: UI не меняется. State contract is existing reset-filter UI flow in `MainControlResetFiltersUiTests`.

UI test video evidence:
- Не применимо: Avalonia.Headless TUnit suite in this repo does not have established safe video recording. Fallback evidence: targeted headless TUnit output, scenario parser/runner trace, and full test command if feasible.

## 7. Бизнес-правила / Алгоритмы

| Rule | Verification |
| --- | --- |
| Reset требует confirmation | `NotificationManagerWrapperMock.AskCount == 1` |
| Cancel keeps filters | Existing `ResetFiltersButton_AsksConfirmation_AndCancelKeepsFilters` remains targeted regression evidence |
| All Tasks reset clears search/emoji and restores completion/wanted defaults | reusable contract assertion |
| Date tab reset resets only current date filter to default and leaves other date filters custom | reusable contract assertion |
| Unlocked/status tab reset restores status/duration/wanted-related filters by existing rules | reusable contract or targeted existing tests |
| Roadmap hidden filter behavior remains covered by existing targeted tests | targeted existing tests remain linked evidence |

## 8. Точки интеграции и триггеры

- `MainWindowViewModel.ResetCurrentTabFilters()` remains the behavior trigger.
- `MainControlResetFiltersUiTests` remains UI regression suite.
- `StormScenarioRunner` is the BDD execution trigger.
- No runtime app integration changes.

## 9. Изменения модели данных / состояния

- Product data model: не меняется.
- Persisted user settings: не меняются.
- Test-only state:
  - Add BDD context fields for filter reset scenario result.
  - Add reusable test result object.

## 10. Миграция / Rollout / Rollback

- Migration: не применимо, production state не меняется.
- Rollout: test-only and docs/artifacts.
- Rollback: revert commit; no user data impact.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
1. `SC-0005-002` parses from feature file and executes all 4 steps.
2. Step definitions `SD-0027..SD-0030` support only `SC-0005-002`.
3. BDD test proves reset-filter behavior through reusable contract, not placeholder assertions.
4. Existing targeted reset/filter tests still pass.
5. `storm.json` and reports are synchronized: `SC-0005-002` becomes step-executable/passing, executable ratio becomes 8/45.
6. Production code, `.feature` wording, project files and workflows remain unchanged.
7. `validate-artifacts.py`, `git diff --check`, and trailing whitespace scan pass.
8. Commit is created after review fixes and validation.

Tests to add/change:
- Add `src/Unlimotion.Test/FilterResetUiContract.cs`.
- Add `src/Unlimotion.Test/StormBdd/FilterResetStepDefinitions.cs`.
- Add `src/Unlimotion.Test/StormFilterResetExecutableSpecTests.cs`.
- Optionally refactor `MainControlResetFiltersUiTests` to call shared contract only if it reduces duplication without weakening tests.

Validation commands:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormFilterResetExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-28-storm-sc0005-filter-reset-bdd.md
```

Stop rules for tests:
- If full-suite fails on unrelated known environment/flaky issue, capture exact failing test and run targeted affected suites; do not hide it as pass.
- If targeted `StormFilterResetExecutableSpecTests` or `MainControlResetFiltersUiTests` fails, fix before commit or stop with blocker.

## 12. Риски и edge cases

- Existing feature `When` wording is broad and not perfectly aligned with reset-filter behavior; mitigation: keep feature text unchanged and bind to existing accepted behavior through contract, while recording this as a wording-improvement candidate only after product decision.
- Avalonia.Headless teardown can be flaky; mitigation: use existing `[NotInParallel("AvaloniaHeadless")]` and rerun isolated test once if teardown-only failure appears.
- Reusable contract can accidentally weaken existing tests if over-refactored; mitigation: avoid replacing broad existing assertions unless exact parity is preserved.
- Full-suite can be slow; mitigation: targeted-first, then full-suite per testing baseline.

## 13. План выполнения

1. Create SPEC and complete post-SPEC review.
2. Because active goal auto-approves specs, enter EXEC after review PASS.
3. Add reusable filter reset contract and scenario result.
4. Add BDD step definitions and executable scenario test.
5. Run targeted BDD test; fix invocation/contract issues.
6. Run targeted reset/filter UI suites.
7. Run full `Unlimotion.Test` if feasible; capture exact evidence.
8. Sync `storm.json` and reports.
9. Run artifact validator and diff hygiene checks.
10. Perform post-EXEC review and fix findings.
11. Commit results.
12. Start next iteration by selecting the next highest-value scenario without step definitions.

## 14. Открытые вопросы

Блокирующих вопросов нет. Активная цель пользователя содержит automatic approval: "я автоматически спеку подтверждаю".

## 15. Соответствие профилю

- Профиль: `storm-product-development` + `delivery-task` + `.NET desktop` + `ui-automation-testing`.
- Выполненные требования профиля:
  - `/storm:cover` continues from current stage, not `/storm:full-cycle`.
  - BDD/Gherkin remains layer between AC and tests; AC not replaced.
  - Test/code changes happen only under QUEST SPEC.
  - UI-facing behavior uses existing Avalonia.Headless test suite.
  - Product artifacts remain in Russian.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-28-storm-sc0005-filter-reset-bdd.md` | New SPEC + Post-EXEC evidence | QUEST trace |
| `src/Unlimotion.Test/FilterResetUiContract.cs` | New reusable UI contract | Shared BDD/existing evidence |
| `src/Unlimotion.Test/StormBdd/FilterResetStepDefinitions.cs` | New step definitions `SD-0027..SD-0030` | Bind `SC-0005-002` |
| `src/Unlimotion.Test/StormFilterResetExecutableSpecTests.cs` | New executable BDD test | Scenario runner evidence |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Add context/result fields | Carry BDD contract state |
| `docs/product/storm.json` | Sync `SC-0005-002`, metrics, process audit | Canonical STORM state |
| `docs/product/reports/*.md` | Sync coverage/trace/bdd/ranking/story reports | Companion reports |

Запрещено без новой SPEC: `src/Unlimotion/**`, `.feature`, `.csproj`, `.github/**`, package/source configuration.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| `SC-0005-002` | automated linked tests, no step definitions | passing executable BDD slice |
| Step definitions | 26 total, 7/45 scenarios executable | 30 total, 8/45 scenarios executable |
| Product behavior | existing reset-filter behavior | unchanged |
| UI test evidence | existing TS-0006/TS-0013 links | existing links + new executable scenario test |

## 18. Альтернативы и компромиссы

- Вариант A: только artifact sync без новых tests.
  - Плюсы: быстро.
  - Минусы: не приближает full executable coverage.
  - Не выбран: цель требует полного покрытия тестами.
- Вариант B: add BDD test with placeholder step assertions.
  - Плюсы: быстро повышает ratio.
  - Минусы: ложное покрытие.
  - Не выбран: нарушает STORM evidence requirements.
- Вариант C: reusable UI contract + BDD step definitions.
  - Плюсы: связывает scenario text with real existing behavior and keeps production unchanged.
  - Минусы: дороже, может потребовать careful headless setup.
  - Выбран.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, flow, rules, state и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Production/UI behavior changes запрещены; stop rules заданы. |
| D. Проверяемость | 14-16 | PASS | AC, test list and commands concrete. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, open questions and alternatives complete. |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/UI automation constraints reflected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один scenario slice, explicit Non-Goals. |
| 2. Понимание текущего состояния | 5 | Feature, storm links, existing tests and BDD runner inspected. |
| 3. Конкретность целевого дизайна | 5 | Files, IDs, flow and evidence rules specified. |
| 4. Безопасность (миграция, откат) | 5 | Test-only change; rollback is revert commit. |
| 5. Тестируемость | 5 | Targeted, UI and full-suite commands listed. |
| 6. Готовность к автономной реализации | 5 | No blocking questions; user auto-approval recorded. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Scope reviewed: this spec path, central STORM/QUEST/testing stack, `SC-0005-002` in `storm.json`, feature file, existing reset/filter tests, BDD runner pattern, planned changed files.
- Decision: можно выполнять EXEC по auto-approval.
- Review passes:
  - Scope/Evidence pass: selected scenario has no step definitions and is linked to existing reset/filter evidence.
  - Contract pass: implementation limited to test-only BDD binding and artifact sync; no production or feature wording changes.
  - Adversarial risk pass: placeholder-step risk mitigated by reusable contract; UI video requirement has objective fallback.
  - Re-review after fixes / Fix and re-review: initial spec included explicit fallback for UI video and stop rule for behavior change; no further fixes required.
  - Stop decision: PASS.
- Evidence inspected:
  - `features/storm/st-0005-search-and-filters.feature`
  - `docs/product/storm.json`
  - `src/Unlimotion.Test/MainControlResetFiltersUiTests.cs`
  - `src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs`
  - `src/Unlimotion.Test/StormBdd/StormScenarioRunner.cs`
  - `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs`
- Depth checklist:
  - Scope drift / unrelated changes: planned files are limited to tests, StormBdd, docs/product and this SPEC.
  - Acceptance criteria: concrete and measurable.
  - Validation evidence: commands listed, including targeted UI and artifact validation.
  - Unsupported claims: runtime/video/full-suite claims are gated by actual evidence.
  - Regression / edge case: headless flakiness and feature wording mismatch called out.
  - Comments/docs/changelog: no production comments/changelog needed; STORM reports sync required.
  - Hidden contract change: production/UI contract changes explicitly forbidden.
  - Manual-review challenge: reviewer should verify contract does not merely assert booleans and actually exercises reset behavior.
- No-findings justification: SPEC narrows one existing scenario gap and has direct evidence sources plus stop rules.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | UI video evidence is not produced for Avalonia.Headless runner. | Use explicit fallback: targeted TUnit output and BDD runner trace. | accepted-risk |

- Fixed before continuing: fallback evidence and stop rules included.
- Checks rerun: manual linter/rubric/review completed.
- Needs human: no; active goal includes automatic approval.
- Residual risks / follow-ups: feature `When` wording may deserve future product wording refinement, but this SPEC does not change `.feature`.

### Post-EXEC Review
- Статус: PASS.
- Scope reviewed: new test-only files, StormBdd context fields, docs/product artifact sync and this SPEC.
- Production code, feature wording, project files, workflows и existing test annotations не менялись.
- Evidence:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal` -> passed.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormFilterResetExecutableSpecTests/*" --output Detailed` -> passed 1/1.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*" --output Detailed` -> passed 8/8.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed` -> passed 14/14.
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed` -> sandbox run упал 563/564 на Windows ACL inheritance.
  - unsandboxed targeted ACL rerun -> passed 1/1.
  - full `Unlimotion.Test` вне sandbox -> прошёл 564/564.
  - `validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 1 known warning.
- Findings: none.
- Residual risk: managed sandbox full run имеет известное Windows ACL inheritance false failure, но targeted ACL rerun вне sandbox прошёл 1/1, а final full `Unlimotion.Test` вне sandbox прошёл 564/564.

## Approval

Получено автоматически из активной цели пользователя: "я автоматически спеку подтверждаю".

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего coverage slice | 0.86 | Нет | Написать SPEC | Нет | Да: auto-approval зафиксирован в цели | `SC-0005-002` имеет linked tests but no step definitions, поэтому directly improves executable coverage. | `docs/product/storm.json`, `features/storm/st-0005-search-and-filters.feature` |
| SPEC | Подготовка и review SPEC | 0.9 | Нет | Перейти к EXEC | Нет | Да: active goal says every spec is automatically approved | SPEC constrains change to test-only BDD binding and artifact sync. | `specs/2026-06-28-storm-sc0005-filter-reset-bdd.md` |
| EXEC | Реализация BDD slice SC-0005-002 | 0.88 | Нет | Синхронизировать artifacts и выполнить validation gates | Нет | Да: active goal auto-approval | Test-only BDD binding executed real reset-filter UI contract and improved step-executable ratio to 8/45. | `src/Unlimotion.Test/FilterResetUiContract.cs`, `src/Unlimotion.Test/StormBdd/FilterResetStepDefinitions.cs`, `src/Unlimotion.Test/StormFilterResetExecutableSpecTests.cs`, `docs/product/storm.json` |
