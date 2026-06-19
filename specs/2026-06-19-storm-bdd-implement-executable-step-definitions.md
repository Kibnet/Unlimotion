# STORM BDD Implement: Executable Step Definitions для SC-0015-002

## 0. Метаданные
- Тип (профиль): `storm-product-development` + `delivery-task/QUEST`.
- Владелец: product/engineering.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая ветка `storm-bootstrap`.
- Instruction stack:
  - `C:\Users\Kibnet\.codex\agents\AGENTS.md`
  - `instructions/governance/routing-matrix.md`
  - `instructions/core/model-behavior-baseline.md`
  - `instructions/core/quest-governance.md`
  - `instructions/core/quest-mode.md`
  - `instructions/core/testing-baseline.md`
  - `instructions/contexts/testing-dotnet.md`
  - `instructions/profiles/storm-product-development.md`
  - `instructions/governance/spec-linter.md`
  - `instructions/governance/spec-rubric.md`
  - `instructions/governance/review-loops.md`
  - локальный `AGENTS.override.md`
- Ограничения:
  - До явного утверждения SPEC не менять production code, tests, test annotations, STORM artifacts, `.feature` files и поведение продукта.
  - После утверждения менять только минимальный BDD executable slice для `SC-0015-002` и связанные artifacts/reports.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие артефакты.
  - Не заменять acceptance criteria на Gherkin.
  - Не менять test annotations без отдельного подтверждения.
  - Не расширять runtime/release support claims для Android/iOS.
  - Не чинить `NETSDK1147`, workloads, Android SDK/iOS host или package-source environment в рамках этой SPEC.
  - Не вводить внешний SpecFlow/Reqnroll/Cucumber dependency без отдельной остановки и явного решения, если repo-local runner достаточен для первого slice.
  - UI behavior не меняется; local UI-testing override не требует UI tests для этой SPEC.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/coverage.md`
  - `features/storm/st-0015-platform-shells.feature`
  - `src/Unlimotion.Test/PlatformShellProjectContractTests.cs`
  - `src/Unlimotion.Browser/Unlimotion.Browser.csproj`
  - `src/Unlimotion.Android/Unlimotion.Android.csproj`
  - `src/Unlimotion.iOS/Unlimotion.iOS.csproj`

## 1. Overview / Цель
Снять главный BDD-lint warning `step_definitions_total=0` через первый исполняемый вертикальный slice: `SC-0015-002` должен выполняться из `.feature` текста через repo-local step definitions и TUnit evidence.

Outcome contract:
- Success means:
  - `SC-0015-002` имеет linked `SD-*` step definitions в `storm.json`;
  - хотя бы один TUnit test читает `features/storm/st-0015-platform-shells.feature`, находит `@scenario:SC-0015-002`, сопоставляет шаги с registered step definitions и выполняет проверки;
  - существующий `TS-0024` project-contract coverage сохраняется;
  - Browser build smoke evidence остается build-only, Android/iOS blockers остаются environment blockers;
  - `bdd-lint` больше не говорит, что весь BDD layer не имеет ни одной step definition.
- Итоговый артефакт / output:
  - lightweight `StormBdd` test harness в `src/Unlimotion.Test`;
  - step definitions для Given/When/Then текста `SC-0015-002`;
  - passing TUnit evidence для executable scenario slice;
  - synced `docs/product/storm.json` and reports;
  - Post-EXEC review with validation evidence.
- Stop rules:
  - Остановиться, если для корректной реализации нужен внешний BDD framework dependency, а repo-local runner не покрывает цель первого slice.
  - Остановиться, если feature parsing требует полноценной Gherkin grammar support вместо narrow parser for current feature style.
  - Остановиться, если реализация начинает менять production code или platform runtime behavior.
  - Остановиться, если нужно чинить Android/iOS workload restore blocker.

## 2. Текущее состояние (AS-IS)
- STORM BDD layer уже содержит:
  - `metadata.feature_root = features/storm`;
  - 16 feature files;
  - 44 Gherkin rules;
  - 45 scenarios;
  - `Scenario -> Test links = 45/45`.
- `step_definitions` top-level в `storm.json` пустой.
- У каждого scenario поле `step_definitions` сейчас пустое.
- `bdd-lint.md` фиксирует warning: BDD layer связан с TUnit tests напрямую, но не является executable Cucumber-style specification.
- `SC-0015-002` уже имеет:
  - статус `passing`;
  - linked tests `TS-0015`, `TS-0024`;
  - Browser Release build smoke evidence;
  - Android/iOS `NETSDK1147` blocker evidence;
  - корректный no-runtime-release claim.
- `src/Unlimotion.Test/PlatformShellProjectContractTests.cs` уже содержит атомарные проверки Android/browser/iOS project contracts, но не исполняет шаги из `.feature`.

## 3. Проблема
Gherkin слой существует как traceability artifact, но ни один сценарий не исполняется из `.feature` текста через step definitions. Из-за этого `step_definitions_total=0`, а executable specification ratio остается ограниченным linked TUnit evidence, а не BDD step execution.

## 4. Цели дизайна
- Разделение ответственности: parsing/step matching отдельно от domain assertions.
- Повторное использование: переиспользовать проверки `PlatformShellProjectContractTests` через small helper/extractor, не дублировать XML/file assertions в двух местах.
- Тестируемость: executable scenario test должен быть обычным TUnit test без внешнего runner process.
- Консистентность: wording `.feature` остается продуктовым, без internal implementation details.
- Обратная совместимость: production code, platform projects, feature tags and existing tests keep behavior.

## 5. Non-Goals
- Не внедряем полноценный SpecFlow/Reqnroll/Cucumber pipeline.
- Не покрываем все 45 scenarios step definitions в первой итерации.
- Не меняем acceptance criteria, stories, needs, constraints or existing Scenario -> Test links.
- Не меняем test annotations.
- Не запускаем Android emulator, iOS simulator, browser runtime or release pipeline.
- Не исправляем `NETSDK1147`.
- Не трогаем `CV-0007`.
- Не меняем UI или product behavior.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/StormBdd/StormFeatureParser.cs` -> narrow parser for repo feature files: tags, rule title, scenario title and step lines.
- `src/Unlimotion.Test/StormBdd/StormScenarioRunner.cs` -> executes scenario steps by exact normalized text match against registered definitions.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> small model for `id`, keyword, text, scenario support and async action.
- `src/Unlimotion.Test/StormBdd/PlatformShellStepDefinitions.cs` -> step definitions for `SC-0015-002`.
- `src/Unlimotion.Test/PlatformShellProjectContractTests.cs` -> may extract reusable assertion helper if needed; existing test names and behavior stay intact.
- `src/Unlimotion.Test/StormPlatformShellExecutableSpecTests.cs` -> TUnit test that executes `SC-0015-002` from feature file.
- `docs/product/storm.json` -> add `SD-0001..SD-0004` or equivalent and link them to `SC-0015-002`.
- `docs/product/reports/*` -> update BDD sync/lint/coverage metrics based on actual evidence.

### 6.2 Детальный дизайн
- Parser:
  - reads `features/storm/st-0015-platform-shells.feature`;
  - locates scenario by tag `@scenario:SC-0015-002`;
  - collects immediately following `Дано` / `И` / `Когда` / `Тогда` lines until next scenario/rule/tag block;
  - preserves product text for exact matching after whitespace normalization.
- Step matching:
  - `Дано desktop остаётся основной release-supported оболочкой Unlimotion`;
  - `И non-desktop shells должны переиспользовать общую Avalonia task UI модель`;
  - `Когда maintainer проверяет platform project contracts для Android, browser и iOS`;
  - `Тогда каждый non-desktop shell ссылается на общую UI-модель и нужный Avalonia platform package, а Android содержит native Git assets без заявления runtime release support`.
- Execution:
  - Given steps create in-memory context only.
  - When step loads project files or invokes reusable contract assertions.
  - Then step verifies project references/packages/native Git assets and no runtime-release claim boundary.
- Evidence rules:
  - Scenario can stay `passing`; `automation_status` gains executable step-definition evidence only after test passes.
  - Top-level `step_definitions_total` increases from `0` to the number actually added.
  - `executable_specification_ratio` must distinguish step-executable scenarios from merely linked TUnit scenarios.
- Visual planning artifact: `Не применимо`; no UI-facing behavior.
- UI test video evidence: `Не применимо`; no UI automation change.
- Error handling:
  - Missing scenario tag or unmatched step fails the TUnit test with actionable message.
  - Duplicate step text with different actions fails registration.
  - Parser remains intentionally narrow; unsupported grammar should fail clearly rather than silently pass.
- Performance:
  - Feature parsing is single-file and runs only in the targeted test; no meaningful performance risk.

## 7. Бизнес-правила / Алгоритмы
- `.feature` text is the source for executable step sequence.
- Step definitions must match the product wording exactly after whitespace normalization.
- Step definitions must not assert hidden implementation details unless those details are already the public contract of `SC-0015-002`.
- Android/iOS `NETSDK1147` blockers must remain blockers, not failing product behavior.
- Browser build smoke evidence must remain separate from runtime UX parity.

## 8. Точки интеграции и триггеры
- TUnit discovery in `src/Unlimotion.Test`.
- Existing feature file `features/storm/st-0015-platform-shells.feature`.
- Existing project contract checks in `PlatformShellProjectContractTests`.
- STORM artifact sync:
  - `SC-0015-002.step_definitions`;
  - top-level `step_definitions`;
  - BDD lint/sync/coverage metrics.

## 9. Изменения модели данных / состояния
- Persisted product data: no changes.
- Production runtime state: no changes.
- Test-only state:
  - optional small BDD context object for scenario execution.
- STORM artifact state:
  - add `SD-*` entries with `path`, `symbol`, `framework`, `step_text`, `supports_scenarios`, `evidence`;
  - link `SD-*` IDs from `SC-0015-002`.

## 10. Миграция / Rollout / Rollback
- Migration: not applicable.
- Rollout: normal test/artifact commit after approved SPEC.
- Rollback: remove test harness/step definitions/artifact links; existing `TS-0024` contract tests and feature files remain valid.
- Backward compatibility: no production behavior changes.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. `SC-0015-002` can be executed from `features/storm/st-0015-platform-shells.feature` by a TUnit test.
  2. Every step in `SC-0015-002` has a registered step definition with stable `SD-*` id.
  3. The executable scenario reuses or preserves `TS-0024` project-contract assertions.
  4. Existing `PlatformShellProjectContractTests` still pass.
  5. Production code, test annotations and platform projects are unchanged.
  6. STORM validator passes after artifact sync.
- Tests to add/change:
  - Add `StormPlatformShellExecutableSpecTests` or equivalent.
  - Add `StormBdd` test helper files under `src/Unlimotion.Test`.
  - Refactor `PlatformShellProjectContractTests` only if needed to avoid assertion duplication; test behavior and names must remain.
- Validation commands:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormPlatformShellExecutableSpecTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/PlatformShellProjectContractTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - `rg -n "[ \t]+$" docs/product features/storm specs/2026-06-19-storm-bdd-implement-executable-step-definitions.md src/Unlimotion.Test`
- Stop rules:
  - If package restore is required by new dependencies, stop before changing package files and present an explicit dependency decision.
  - If `--no-restore` build fails only because assets are stale after test-only csproj changes, report exact blocker and ask for separate restore approval/decision.
  - If full Gherkin grammar is needed, stop and propose a dedicated BDD infrastructure SPEC.

## 12. Риски и edge cases
- Narrow parser may be mistaken for full Gherkin support. Mitigation: name it repo-local/narrow and document supported grammar.
- Duplicating project assertions could make tests drift. Mitigation: extract reusable helpers or keep executable test delegating to the same contract assertion methods.
- Exact Russian step text matching can be brittle. Mitigation: this is intentional for product wording drift detection; failures should be actionable.
- Introducing external runner too early could cause package/version churn. Mitigation: avoid external dependencies in first slice.
- Step definitions for only one scenario will not make all BDD executable. Mitigation: metrics must show partial executable step coverage.

## 13. План выполнения
1. Confirm approved SPEC and clean worktree.
2. Create small `StormBdd` helper model/parser/runner in `src/Unlimotion.Test`.
3. Extract reusable platform shell contract assertion helper only if needed.
4. Add `PlatformShellStepDefinitions` for `SC-0015-002`.
5. Add TUnit executable scenario test.
6. Run targeted executable scenario test.
7. Run existing `PlatformShellProjectContractTests`.
8. Build test project.
9. Sync `docs/product/storm.json` and reports with actual `SD-*` evidence.
10. Run STORM validator and hygiene checks.
11. Fill Post-EXEC review.

## 14. Открытые вопросы
- Блокирующих вопросов нет до EXEC: фраза `Спеку подтверждаю` authorizes the repo-local executable step-definition slice described here.
- If the product/process owner specifically wants a standard external BDD framework instead of a repo-local runner, do not approve this SPEC; request a separate dependency/framework decision.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`.
- Выполненные требования профиля:
  - `/storm:bdd-implement` goes through delivery-task/QUEST.
  - Gherkin remains between AC and tests; AC are not replaced.
  - Step definitions are linked through stable `SD-*` IDs.
  - Product artifacts remain Russian.
  - Test/code changes are gated until approval.
  - TUnit validation uses `--treenode-filter`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-19-storm-bdd-implement-executable-step-definitions.md` | New SPEC and EXEC journal | QUEST audit trail |
| `src/Unlimotion.Test/StormBdd/StormFeatureParser.cs` | New narrow parser | Read scenario steps from feature file |
| `src/Unlimotion.Test/StormBdd/StormScenarioRunner.cs` | New runner | Execute matched steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | New model | Register stable step definitions |
| `src/Unlimotion.Test/StormBdd/PlatformShellStepDefinitions.cs` | New definitions | Bind `SC-0015-002` Given/When/Then to checks |
| `src/Unlimotion.Test/StormPlatformShellExecutableSpecTests.cs` | New TUnit test | Passing executable BDD evidence |
| `src/Unlimotion.Test/PlatformShellProjectContractTests.cs` | Optional helper extraction only | Avoid duplicated assertions |
| `docs/product/storm.json` | Add/link `SD-*`; update metrics/evidence | STORM canonical sync |
| `docs/product/reports/*` | Update BDD sync/lint/coverage/traceability/ranking/stories | Human-readable sync |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0015-002.step_definitions` | `[]` | Linked `SD-*` IDs for Given/When/Then steps |
| Top-level `step_definitions` | `[]` | First executable step definitions for platform shell contract |
| BDD lint | warning: no step definitions at all | warning narrows to partial step-definition coverage |
| Tests | TUnit checks linked to scenario, but feature text is not executed | TUnit executes the scenario steps from `.feature` for one slice |
| Production behavior | unchanged | unchanged |

## 18. Альтернативы и компромиссы
- Вариант: add Reqnroll/SpecFlow and a full runner now.
  - Плюсы: closer to standard BDD tooling.
  - Минусы: package/version/restore risk, larger scope, possible generated-code churn.
  - Почему не выбран: first slice can prove the process with a small repo-local runner.
- Вариант: keep `step_definitions` empty and rely on linked TUnit tests.
  - Плюсы: no code change.
  - Минусы: leaves the known BDD-lint warning and weakens executable specification goal.
  - Почему не выбран: user requested the new BDD/Gherkin layer and current reports identify step definitions as the next process gap.
- Вариант: implement step definitions for all scenarios.
  - Плюсы: broad coverage.
  - Минусы: high blast radius and many generic placeholder steps.
  - Почему не выбран: vertical slice avoids low-value mechanical inflation.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, problem, goals/non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Parser/runner/step definitions boundaries described. |
| C. Безопасность изменений | 11-13 | PASS | No production/platform/runtime behavior change; dependency stop rule included. |
| D. Проверяемость | 14-16 | PASS | TUnit validation commands and artifact sync rules listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | File table, before/after and alternatives are concrete. |
| F. Соответствие профилю | 20 | PASS | STORM + QUEST + BDD/Gherkin requirements covered. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope is one executable scenario slice, not a full BDD framework migration. |
| 2. Понимание текущего состояния | 5 | Captures current links, empty step definitions and ST-0015 platform evidence. |
| 3. Конкретность целевого дизайна | 5 | Parser, runner, step registry, tests and artifact changes are explicit. |
| 4. Безопасность (миграция, откат) | 5 | Test-only change with no production/runtime claim expansion. |
| 5. Тестируемость | 5 | Commands and passing evidence rules are concrete. |
| 6. Готовность к автономной реализации | 5 | EXEC can proceed after approval with clear stop rules. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed:
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/ranking.md`
  - `docs/product/storm.json`
  - `features/storm/st-0015-platform-shells.feature`
  - `src/Unlimotion.Test/PlatformShellProjectContractTests.cs`
  - `src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `src/Directory.Packages.props`
  - central STORM schema for `step_definition`
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: current evidence supports `SC-0015-002`; missing part is executable step definitions.
  - Contract pass: production code, platform projects, test annotations and runtime claims are excluded.
  - Adversarial risk pass: external BDD framework and Android/iOS setup are explicit stop/defer decisions.
  - Re-review after fixes / Fix and re-review: no blocking fixes required.
  - Stop decision: PASS.
- Evidence inspected:
  - `step_definitions` top-level is `[]`.
  - `SC-0015-002.step_definitions` is `[]`.
  - `PlatformShellProjectContractTests` has existing project-contract assertions for Android/browser/iOS.
  - `Unlimotion.Test.csproj` uses TUnit and has no existing BDD runner dependency.
  - `storm-artifacts.schema.json` supports `SD-*` objects with `path`, `symbol`, `framework`, `step_text`, `supports_scenarios`.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, excludes Android/iOS setup and all-product step-definition rollout.
  - Acceptance criteria: PASS, executable scenario from `.feature` is the central acceptance.
  - Validation evidence: PASS, targeted TUnit/build/STORM validator commands listed.
  - Unsupported claims: PASS, no runtime/release support expansion.
  - Regression / edge case: PASS, existing `PlatformShellProjectContractTests` must remain green.
  - Comments/docs/changelog: PASS, no changelog required; product artifacts stay Russian.
  - Hidden contract change: PASS, feature wording remains product-level.
  - Manual-review challenge: reviewer may ask why not Reqnroll; answer is dependency risk and first-slice scope.
- No-findings justification: SPEC is bounded to a test-only executable BDD slice and has explicit stop rules for framework/dependency expansion.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | process-scope | Repo-local runner is not a full Cucumber-style engine and should not be presented as complete BDD infrastructure. | Keep metrics honest: first executable slice only, full framework is separate decision. | accepted-risk |

- Fixed before continuing: не применимо.
- Checks rerun: Manual SPEC review and `git status --short`.
- Needs human: `Спеку подтверждаю`.
- Residual risks / follow-ups: Android/iOS `NETSDK1147` remains a separate environment/setup task.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed:
  - approved SPEC
  - `git status --short`
  - `src/Unlimotion.Test/StormBdd/*`
  - `src/Unlimotion.Test/StormPlatformShellExecutableSpecTests.cs`
  - `src/Unlimotion.Test/PlatformShellProjectContracts.cs`
  - `src/Unlimotion.Test/PlatformShellProjectContractTests.cs`
  - `docs/product/storm.json`
  - `docs/product/reports/*`
- Decision: EXEC completed; ready for final report.
- Review passes:
  - Scope/Evidence pass: `SC-0015-002` now links to `SD-0001..SD-0004` and `TS-0026`.
  - Contract pass: production code, test annotations, feature wording and platform runtime claims were not changed.
  - Adversarial risk pass: repo-local runner remains a narrow first slice; no external BDD dependency was introduced.
  - Re-review after fixes / Fix and re-review: no blocking fixes after validation.
  - Stop decision: PASS.
- Evidence inspected:
  - `StormFeatureParser` locates `@scenario:SC-0015-002` in `features/storm/st-0015-platform-shells.feature`.
  - `StormScenarioRunner` fails on unmatched or unsupported steps.
  - `PlatformShellStepDefinitions` binds the four scenario steps to `PlatformShellProjectContracts`.
  - `StormPlatformShellExecutableSpecTests` passed 1/1.
  - Existing `PlatformShellProjectContractTests` passed 3/3 after helper extraction.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, only approved test/artifact/spec files changed.
  - Acceptance criteria: PASS, feature text is executed through registered stable `SD-*` ids.
  - Validation evidence: PASS, build, targeted TUnit tests, STORM validator and hygiene checks passed.
  - Unsupported claims: PASS, Android/iOS runtime release support remains unclaimed and `NETSDK1147` remains a separate blocker.
  - Regression / edge case: PASS, exact step matching will fail on product wording drift.
  - Comments/docs/changelog: PASS, no changelog required; product artifacts remain Russian.
  - Hidden contract change: PASS, production code and `.feature` wording unchanged.
  - Manual-review challenge: reviewer may ask whether repo-local runner is a full BDD engine; report and artifacts mark it as first executable slice only.
- No-findings justification: implementation is test-only, validation passed, and artifacts match actual evidence.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| INFO | process-scope | Step definitions cover only `SC-0015-002`; repo-local runner is not a full Cucumber-style engine. | Continue by scenario-specific SPECs or approve a separate full BDD framework decision. | accepted |
| INFO | platform | Android/iOS build smoke remains blocked by `NETSDK1147`. | Separate environment/setup SPEC if Android/iOS build evidence is required. | accepted |

- Fixed before final report: no blocking fixes required.
- Checks rerun:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormPlatformShellExecutableSpecTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/PlatformShellProjectContractTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - `rg -n "[ \t]+$" docs/product features/storm specs/2026-06-19-storm-bdd-implement-executable-step-definitions.md src/Unlimotion.Test`
- Validation evidence:
  - build passed with existing warnings;
  - executable BDD slice test passed 1/1;
  - platform project contract tests passed 3/3;
  - STORM validator OK, 0 errors, 0 warnings;
  - hygiene checks passed, trailing-space scan had no matches.
- Unrelated changes: none detected before EXEC; current diff is limited to approved test/artifact/spec scope.
- Needs human: no further approval needed for completed SPEC scope.
- Residual risks / follow-ups: expand step definitions by high-value scenario; Android/iOS setup remains separate.

## Approval
Получено: `Спеку подтверждаю`.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор current BDD evidence | 0.9 | Approval | Создать SPEC | Нет | Нет | Reports identify `step_definitions` as remaining BDD process gap; `SC-0015-002` is a narrow high-signal slice. | `docs/product/reports/*`, `docs/product/storm.json`, `features/storm/st-0015-platform-shells.feature`, `src/Unlimotion.Test/PlatformShellProjectContractTests.cs` |
| SPEC | Выбор design path | 0.84 | Approval | Передать SPEC пользователю | Да | Нет | Repo-local runner avoids package churn while producing real executable step-definition evidence for one scenario. | `specs/2026-06-19-storm-bdd-implement-executable-step-definitions.md` |
| EXEC | Test-only BDD runner implementation | 0.86 | None | Validate targeted tests | Нет | Да, approval получен | Added narrow parser/runner/step definitions and reused platform contract assertions without production changes. | `src/Unlimotion.Test/StormBdd/*`, `src/Unlimotion.Test/StormPlatformShellExecutableSpecTests.cs`, `src/Unlimotion.Test/PlatformShellProjectContracts.cs`, `src/Unlimotion.Test/PlatformShellProjectContractTests.cs` |
| EXEC | Validation | 0.9 | None | Sync STORM artifacts | Нет | Да, approval получен | Build and targeted TUnit tests passed; no external BDD dependency or restore change was needed. | test output only |
| EXEC | STORM artifact sync | 0.88 | None | Run validator/hygiene | Нет | Да, approval получен | Added `TS-0026`, `SD-0001..SD-0004`, scenario links and partial step-definition metrics. | `docs/product/storm.json`, `docs/product/reports/*` |
| EXEC | Post-EXEC review | 0.88 | None | Final report | Нет | Да, approval получен | Validator/hygiene passed; residual gaps are explicitly documented. | `specs/2026-06-19-storm-bdd-implement-executable-step-definitions.md` |
