# STORM SC-0004-001: executable BDD для вкладок рабочих представлений

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `storm-bootstrap`
- Ограничения: не менять product behavior; не менять `.feature` wording; не менять existing test annotations; не менять production code; продуктовые артефакты вести на русском
- Связанные ссылки: `ST-0004`, `AC-0010`, `GR-010`, `SC-0004-001`, `TS-0001`, `TS-0004`, `TS-0011`, `MainControl`, `MainWindowViewModel`

## 1. Overview / Цель
Добавить executable BDD layer для `SC-0004-001`: вкладки рабочих представлений показывают соответствующие подмножества задач и синхронизируют выбранный пользовательский контекст.

Outcome contract:
- Success means: `SC-0004-001` получает новый executable BDD test `TS-0045`, step definitions `SD-0075..SD-0078`, passing UI/headless evidence, а `ST-0004` становится 1/3 step-executable.
- Итоговый артефакт / output: test-only executable spec + обновленные `storm.json` и reports.
- Stop rules: остановиться, если нужны изменения production behavior, persisted schema, `.feature` wording, UI layout/automation IDs, existing annotations или product decision по новому tab behavior.

## 2. Текущее состояние (AS-IS)
- `SC-0004-001` связан с `AC-0010`, `GR-010`, `TS-0001`, `TS-0004`, `TS-0011`, status = `automated`, `step_definitions = []`.
- `ST-0004` имеет 3 scenarios и пока 0/3 step-executable.
- `MainControl.axaml` содержит вкладки `AllTasksTabItem`, `LastCreatedTabItem`, `LastUpdatedTabItem`, `UnlockedTabItem`, `InProgressTabItem`, `CompletedTabItem`, `ArchivedTabItem`, `LastOpenedTabItem`, `RoadmapTabItem`, `SettingsTabItem`.
- `MainWindowViewModel` содержит mode flags, projection collections и current wrapper properties для вкладок.
- Existing tests уже проверяют части поведения: `SelectCurrentTaskMode_SyncsCorrectly`, projection/filter tests, `MainControlTreeCommandsUiTests`, AppAutomation/FlaUI suite metadata.

## 3. Проблема
Для `SC-0004-001` нет исполняемой связи `Scenario -> Test -> Step Definition -> UI/ViewModel code`, поэтому `/storm:cover` не может считать вкладочную навигацию закрытой на BDD layer.

## 4. Цели дизайна
- Проверить existing behavior через Avalonia.Headless UI contract, а не через скрытую подмену свойств без view.
- Связать scenario wording с repo-local step definitions.
- Сохранить existing tests и annotations.
- Проверить минимум два наблюдаемых аспекта `AC-0010`: вкладки показывают разные projection subsets; выбранный контекст синхронизируется при переключении активной вкладки.

## 5. Non-Goals
- Не менять UI layout, labels, selectors или automation IDs.
- Не добавлять видео-артефакты в репозиторий.
- Не реализовывать новые tab behaviors.
- Не закрывать `SC-0004-002` и `SC-0004-003` в этой итерации.
- Не запускать `/storm:full-cycle`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `WorkspaceNavigationTabsUiContract` -> test-only Avalonia.Headless flow для открытия `MainControl`, переключения вкладок и проверки projection/current context.
- `WorkspaceNavigationTabsStepDefinitions` -> `SD-0075..SD-0078`, binding product wording к UI contract.
- `StormWorkspaceNavigationTabsExecutableSpecTests` -> `TS-0045`, парсит existing `.feature` scenario и запускает шаги.
- `StormScenarioContext` -> test-only result fields для передачи evidence между steps.
- `storm.json` и reports -> `/storm:bdd-sync`, `/storm:bdd-lint`, behavior metrics.

### 6.2 Детальный дизайн
- UI flow:
  1. Открыть `MainControl` с `MainWindowViewModelFixture` в `HeadlessUnitTestSession`.
  2. Подтвердить наличие main tab automation IDs и непустую `AllTasks` projection.
  3. Создать/подготовить задачи для `In Progress` и `Last Created` projection.
  4. Переключить вкладку `In Progress` и проверить, что `InProgressItems` содержит только InProgress target и не содержит prepared task.
  5. Установить `CurrentTaskItem`, переключить вкладку `In Progress`, подтвердить `CurrentInProgressItem`.
  6. Переключить `All Tasks`, подтвердить, что текущая задача восстанавливается в `CurrentAllTasksItem`.
- Visual planning artifact: layout не меняется; reviewer artifact = текстовая state-map:
  - `All Tasks tab -> AllTasksTree -> CurrentAllTasksItems / CurrentAllTasksItem`
  - `In Progress tab -> InProgressTree -> InProgressItems / CurrentInProgressItem`
  - `Last Created tab -> LastCreatedTree -> LastCreatedItems / CurrentLastCreated`
- UI test video evidence: fallback. Avalonia.Headless/TUnit runner в этом репозитории не сохраняет безопасное видео; next-best evidence = targeted headless test output, isolated UI contract execution, full `Unlimotion.Test` gate.
- Границы сохранения поведения: production code, `.feature` wording, test annotations, selectors не меняются.
- Обработка ошибок: missing controls/projections fail the executable spec with explicit assertion.
- Производительность: targeted UI contract должен оставаться small и использовать existing fixture/throttle helpers.

## 7. Бизнес-правила / Алгоритмы
- Активная вкладка определяет projection collection и current wrapper, через которые пользователь видит и выбирает задачу.
- `CurrentTaskItem` синхронизируется с current wrapper активной вкладки.
- При переключении в вкладку с projection, содержащей текущую задачу, соответствующий current wrapper должен восстановиться.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario(..., "SC-0004-001")`.
- `StormScenarioRunner` executes four feature steps.
- UI contracts: `MainControl` tab items/tree controls by `AutomationProperties.AutomationId`.
- ViewModel contracts: `AllTasksMode`, `InProgressMode`, `CurrentAllTasksItems`, `InProgressItems`, `CurrentAllTasksItem`, `CurrentInProgressItem`.

## 9. Изменения модели данных / состояния
Production state не меняется. Test-only fixture создает/обновляет временные задачи в тестовом хранилище и очищает их через `MainWindowViewModelFixture.CleanTasks()`.

## 10. Миграция / Rollout / Rollback
Migration не требуется. Rollback: удалить `TS-0045`, `SD-0075..SD-0078`, `WorkspaceNavigationTabsUiContract` и откатить artifact links/metrics.

## 11. Тестирование и критерии приёмки
- `SC-0004-001` исполняется через repo-local steps.
- Tags `@scenario:SC-0004-001`, `@story:ST-0004`, `@test:TS-0001`, `@test:TS-0004`, `@test:TS-0011` проверены.
- Targeted BDD проходит 1/1.
- Targeted existing UI/ViewModel evidence проходит: `MainWindowViewModelTests` relevant methods or class-level targeted run, plus new UI contract.
- STORM validator проходит 0 errors.
- Full `Unlimotion.Test` проходит или, если headless teardown flake повторится, failing UI tests должны пройти isolated и controlled retry должен пройти.
- Stop rule для validation loop: максимум один controlled full retry после isolated proof для unrelated teardown failures; если повторяется тот же deterministic failure, остановиться и оформить отдельную stability SPEC.

## 12. Риски и edge cases
- Риск: test станет слишком процедурным и начнет проверять implementation details. Смягчение: assertions остаются на observable tab projections/current selection.
- Риск: Headless teardown flake. Смягчение: использовать `NotInParallel`, `SharedUiStateParallelLimit`, existing dispose helper там, где нужен manual dispose.
- Риск: video evidence requirement. Смягчение: явно зафиксирован fallback и next-best evidence, потому что runner не производит видео.

## 13. План выполнения
1. Создать SPEC и post-SPEC review.
2. Добавить test-only UI contract, context fields, step definitions, executable spec.
3. Обновить STORM artifacts/reports.
4. Запустить targeted BDD/UI/domain checks, STORM validator, full suite.
5. Post-EXEC review и commit.

## 14. Открытые вопросы
Нет блокирующих.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: QUEST gate, Scenario -> Test -> Step Definition -> Code/UI, TUnit `--treenode-filter`, UI/headless evidence, visual artifact fallback, product artifacts на русском.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/WorkspaceNavigationTabsUiContract.cs` | Новый UI contract helper | Проверить `AC-0010` через Avalonia.Headless |
| `src/Unlimotion.Test/StormBdd/WorkspaceNavigationTabsStepDefinitions.cs` | Новый step definition набор | Исполнить `SC-0004-001` |
| `src/Unlimotion.Test/StormWorkspaceNavigationTabsExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result fields | Передать evidence между steps |
| `docs/product/storm.json`, `docs/product/reports/*` | Links/metrics/reports | `/storm:bdd-sync`, `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0004-001` | `automated`, no steps | `passing`, `TS-0045`, `SD-0075..SD-0078` |
| `ST-0004` executable coverage | 0/3 | 1/3 |
| Step-executable scenarios | 19/45 | 20/45 |
| Product behavior | Existing tab navigation | Без изменений |

## 18. Альтернативы и компромиссы
- Использовать только ViewModel unit test: отклонено, потому что story UI-facing и локальный override требует UI tests.
- Записать video artifact: отклонено как обязательный output, потому что current Avalonia.Headless runner не сохраняет видео; fallback evidence достаточен для test-only BDD bridge.
- Покрыть все `ST-0004` scenarios одним тестом: отклонено, нарушает small-slice `/storm:cover` и смешивает breadcrumbs/tree commands.

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
| 2. Понимание текущего состояния | 5 | Existing artifacts, UI controls, ViewModel contracts и tests указаны |
| 3. Конкретность целевого дизайна | 5 | IDs/files/checks заданы |
| 4. Безопасность (миграция, откат) | 5 | Test-only, rollback перечислен |
| 5. Тестируемость | 5 | Headless UI, targeted BDD, STORM validator, full suite |
| 6. Готовность к автономной реализации | 5 | Нет blockers; fallback evidence определен |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-07-10-storm-sc0004-workspace-tabs-bdd.md`, central stack `model-behavior-baseline + quest-governance + quest-mode + testing-baseline + testing-dotnet + dotnet-desktop-client + ui-automation-testing + storm-product-development`, локальный `AGENTS.override.md`, `ST-0004`, `AC-0010`, `GR-010`, `SC-0004-001`, planned changed files.
- Decision: можно выполнять; active goal задаёт auto approval.
- Review passes:
  - Scope/Evidence pass: проверены `storm.json`, feature file, `MainControl.axaml`, `MainWindowViewModel` mode/current contracts, existing UI/ViewModel tests.
  - Contract pass: спецификация не меняет behavior, acceptance criteria, `.feature`, annotations или selectors; UI evidence предусмотрен.
  - Adversarial risk pass: проверен риск video evidence, headless teardown flakes, overspecified implementation details и scope creep на `SC-0004-002/003`.
  - Re-review after fixes / Fix and re-review: не требовалось; визуальный fallback и video fallback включены до review.
  - Stop decision: PASS.
- Evidence inspected: current worktree clean, `features/storm/st-0004-workspace-navigation.feature`, `docs/product/storm.json`, relevant `MainControl`/`MainWindowViewModel` snippets, existing BDD patterns.
- Depth checklist:
  - Scope drift / unrelated changes: отсутствует; план test/artifact only.
  - Acceptance criteria: `AC-0010` covered by projection + current-context assertions.
  - Validation evidence: команды заданы; full gate обязателен.
  - Unsupported claims: video fallback явно ограничен.
  - Regression / edge case: teardown flake stop rule задан.
  - Comments/docs/changelog: новые comments не планируются; changelog не нужен.
  - Hidden contract change: production/UI selectors не меняются.
  - Manual-review challenge: reviewer будет искать, не подменён ли UI flow чистым ViewModel test; spec требует `MainControl` + headless UI.
- No-findings justification: small test-only BDD slice, объективные UI/video ограничения зафиксированы.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence не создается текущим runner | Зафиксировать fallback и использовать targeted headless/full-suite evidence | accepted-risk |

- Fixed before continuing: Не применимо.
- Checks rerun: SPEC linter/rubric self-check.
- Needs human: Нет; active goal auto-approves SPEC.
- Residual risks / follow-ups: `SC-0004-002` и `SC-0004-003` остаются следующими `/storm:cover` gaps.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, changed test/artifact files, `WorkspaceNavigationTabsUiContract`, `WorkspaceNavigationTabsStepDefinitions`, `StormWorkspaceNavigationTabsExecutableSpecTests`, `StormStepDefinition`, `storm.json`, reports and validation evidence.
- Decision: можно коммитить.
- Review passes:
  - Scope/Evidence pass: изменения ограничены test-only BDD bridge, SPEC and STORM artifacts.
  - Contract pass: `SC-0004-001` получил `TS-0045` and `SD-0075..SD-0078`; production code, `.feature`, automation IDs and existing annotations unchanged.
  - Adversarial risk pass: проверены risks overspecified UI internals, missing UI evidence, video fallback and full-suite stability.
  - Re-review after fixes / Fix and re-review: fixes не потребовались после validation; artifacts re-synced with final full-suite evidence.
  - Stop decision: PASS.
- Evidence inspected:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false` => passed, existing warnings.
  - `StormWorkspaceNavigationTabsExecutableSpecTests` => passed 1/1.
  - `MainWindowViewModelTests/SelectCurrentTaskMode_SyncsCorrectly` => passed 1/1.
  - `MainControlTreeCommandsUiTests/TreeCommandUi_LastCreatedTab_CurrentCommands_WorkOnClickedItem` => passed 1/1.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` => OK: 0 errors, 8 warnings.
  - Full `Unlimotion.Test` => passed 576/576 with `C:\tmp\unlimotion-full-suite-sc0004-workspace-tabs-bdd.log`.
- Depth checklist:
  - Scope drift / unrelated changes: no production, feature, selector or annotation changes.
  - Acceptance criteria: `AC-0010` covered by tab projection and selected-context sync assertions.
  - Validation evidence: targeted, preserved UI evidence, STORM validator and full suite present.
  - Unsupported claims: video evidence marked fallback, not claimed as produced.
  - Regression / edge case: full suite passed; no teardown retry needed.
  - Comments/docs/changelog: no code comments/changelog needed; product reports updated.
  - Hidden contract change: none; UI contract uses existing automation IDs.
  - Manual-review challenge: reviewer would check whether this is real UI evidence; `MainControl` opens in Avalonia.Headless and switches tab controls.
- No-findings justification: implementation follows existing STORM BDD/UI patterns, validates via headless UI, and changes only tests/artifacts.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video artifact not produced by current Avalonia.Headless runner | Preserve explicit fallback and use targeted/full-suite evidence | accepted-risk |

- Fixed before final report: Artifacts updated from pending to final full-suite evidence.
- Checks rerun: STORM validator after sync; full suite after targeted checks.
- Validation evidence: listed above.
- Unrelated changes: none observed in task scope.
- Needs human: Нет.
- Residual risks / follow-ups: `SC-0004-002` and `SC-0004-003` remain ST-0004 `/storm:cover` gaps.
## Approval
Подтверждено активной целью пользователя: SPEC auto-approved for execution.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0004-001` | 0.89 | Нет | Перейти к EXEC | Нет | Да, active goal auto approval | UI-facing scenario требует headless UI executable bridge без product-code changes | `specs/2026-07-10-storm-sc0004-workspace-tabs-bdd.md` |
| EXEC | executable BDD UI slice | 0.92 | Нет | Commit и перейти к следующему `/storm:cover` candidate | Нет | Нет | Targeted/full gates passed; `SC-0004-001` закрыт step-executable | `src/Unlimotion.Test/StormWorkspaceNavigationTabsExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/WorkspaceNavigationTabsStepDefinitions.cs`, `src/Unlimotion.Test/WorkspaceNavigationTabsUiContract.cs`, `docs/product/storm.json`, `docs/product/reports/*` |
