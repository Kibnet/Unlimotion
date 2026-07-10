# STORM SC-0004-002: executable BDD для breadcrumbs и last-opened

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `storm-bootstrap`
- Ограничения: не менять product behavior; не менять `.feature` wording; не менять existing test annotations; не менять production code; продуктовые артефакты вести на русском
- Связанные ссылки: `ST-0004`, `AC-0011`, `GR-011`, `SC-0004-002`, `TS-0001`, `TS-0004`, `TS-0011`, `TS-0016`, `MainControl`, `MainWindowViewModel`

## 1. Overview / Цель
Добавить executable BDD layer для `SC-0004-002`: breadcrumbs и last-opened контекст помогают пользователю вернуться к недавно открытым задачам.

Outcome contract:
- Success means: `SC-0004-002` получает новый executable BDD test `TS-0046`, step definitions `SD-0079..SD-0082`, passing UI/headless evidence, а `ST-0004` становится 2/3 step-executable.
- Итоговый артефакт / output: test-only executable spec + обновленные `storm.json` и reports.
- Stop rules: остановиться, если нужны изменения production behavior, persisted schema, `.feature` wording, UI layout/automation IDs, existing annotations или product decision по новому last-opened behavior.

## 2. Текущее состояние (AS-IS)
- `SC-0004-002` связан с `AC-0011`, `GR-011`, `TS-0001`, `TS-0004`, `TS-0011`, `TS-0016`, status = `automated`, `step_definitions = []`.
- `ST-0004` имеет 3 scenarios; после `SC-0004-001` coverage = 1/3 step-executable.
- `MainControl.axaml` содержит `BreadcrumbsTextBlock`, `LastOpenedTabItem`, `LastOpenedTree`.
- `MainWindowViewModel` наполняет `LastOpenedSource`, когда `DetailsAreOpen == true` и меняется `CurrentTaskItem`; `CurrentLastOpenedItem` синхронизирует выбранную задачу.
- Existing tests уже подтверждают части поведения: `BreadcrumbEmojiUiTests`, `ReadmeDemoHeadlessTests`, `MainControlTreeCommandsUiTests`, `MainWindowViewModelTests`.

## 3. Проблема
Для `SC-0004-002` нет исполняемой связи `Scenario -> Test -> Step Definition -> UI/ViewModel code`, поэтому `/storm:cover` не может считать breadcrumbs/last-opened поведение закрытым на BDD layer.

## 4. Цели дизайна
- Проверить existing behavior через Avalonia.Headless `MainControl`, а не через новый product pathway.
- Связать wording scenario с repo-local step definitions.
- Сохранить existing tests и annotations.
- Проверить два наблюдаемых аспекта `AC-0011`: breadcrumbs показывают путь текущей задачи; Last Opened tab позволяет выбрать недавно открытую задачу и восстановить контекст.

## 5. Non-Goals
- Не менять UI layout, labels, selectors или automation IDs.
- Не добавлять видео-артефакты в репозиторий.
- Не реализовывать новые breadcrumbs/last-opened behaviors.
- Не закрывать `SC-0004-003` в этой итерации.
- Не запускать `/storm:full-cycle`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `WorkspaceBreadcrumbsLastOpenedUiContract` -> test-only Avalonia.Headless flow для открытия `MainControl`, проверки breadcrumbs и выбора элемента в `LastOpenedTree`.
- `WorkspaceBreadcrumbsLastOpenedStepDefinitions` -> `SD-0079..SD-0082`, binding product wording к UI contract.
- `StormWorkspaceBreadcrumbsLastOpenedExecutableSpecTests` -> `TS-0046`, парсит existing `.feature` scenario и запускает шаги.
- `StormScenarioContext` -> test-only result fields для передачи evidence между steps.
- `storm.json` и reports -> `/storm:bdd-sync`, `/storm:bdd-lint`, behavior metrics.

### 6.2 Детальный дизайн
- UI flow:
  1. Открыть `MainControl` с `MainWindowViewModelFixture` в `HeadlessUnitTestSession`.
  2. Установить `DetailsAreOpen = true`, открыть две задачи через existing `CurrentTaskItem` flow.
  3. Проверить, что `BreadcrumbsTextBlock` показывает путь текущей вложенной задачи.
  4. Переключить `LastOpenedTabItem`, дождаться `LastOpenedItems`.
  5. Выбрать предыдущий wrapper через `LastOpenedTree.SelectedItem`.
  6. Подтвердить, что `CurrentTaskItem` и `CurrentLastOpenedItem` синхронизированы.
  7. Повторно выбрать вложенную задачу и подтвердить, что breadcrumbs возвращают её путь.
- Visual planning artifact: layout не меняется; reviewer artifact = текстовая state-map:
  - `CurrentTaskItem -> BreadScrumbs -> BreadcrumbsTextBlock`
  - `DetailsAreOpen + CurrentTaskItem changes -> LastOpenedSource -> LastOpenedItems`
  - `LastOpenedTree.SelectedItem -> CurrentLastOpenedItem -> CurrentTaskItem`
- UI test video evidence: fallback. Avalonia.Headless/TUnit runner в этом репозитории не сохраняет безопасное видео; next-best evidence = targeted headless test output, preserved linked tests, full `Unlimotion.Test` gate.
- Границы сохранения поведения: production code, `.feature` wording, test annotations, selectors не меняются.
- Обработка ошибок: missing controls, empty breadcrumbs or missing last-opened wrappers fail the executable spec with explicit assertion.

## 7. Бизнес-правила / Алгоритмы
- Breadcrumbs должны отражать текущий путь выбранной задачи.
- Last Opened должен накапливать задачи, которые пользователь открывал в detail context.
- Выбор элемента во вкладке Last Opened должен возвращать пользователя к соответствующей задаче.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario(..., "SC-0004-002")`.
- `StormScenarioRunner` executes four feature steps.
- UI contracts: `BreadcrumbsTextBlock`, `LastOpenedTabItem`, `LastOpenedTree`.
- ViewModel contracts: `DetailsAreOpen`, `CurrentTaskItem`, `LastOpenedMode`, `LastOpenedItems`, `CurrentLastOpenedItem`, `BreadScrumbs`.

## 9. Изменения модели данных / состояния
Production state не меняется. Test-only fixture использует стандартный набор задач `MainWindowViewModelFixture` и очищает задачи через `CleanTasks()`.

## 10. Миграция / Rollout / Rollback
Migration не требуется. Rollback: удалить `TS-0046`, `SD-0079..SD-0082`, `WorkspaceBreadcrumbsLastOpenedUiContract` и откатить artifact links/metrics.

## 11. Тестирование и критерии приёмки
- `SC-0004-002` исполняется через repo-local steps.
- Tags `@scenario:SC-0004-002`, `@story:ST-0004`, `@test:TS-0001`, `@test:TS-0004`, `@test:TS-0011`, `@test:TS-0016` проверены.
- Targeted BDD проходит 1/1.
- Targeted preserved evidence проходит: `BreadcrumbEmojiUiTests` и/или `ReadmeDemoHeadlessTests`.
- STORM validator проходит 0 errors.
- Full `Unlimotion.Test` проходит или, если headless teardown flake повторится, failing UI tests должны пройти isolated и controlled retry должен пройти.
- Stop rule для validation loop: максимум один controlled full retry после isolated proof для unrelated teardown failures; если повторяется тот же deterministic failure, остановиться и оформить отдельную stability SPEC.

## 12. Риски и edge cases
- Риск: Last Opened наполняется только при `DetailsAreOpen == true`. Смягчение: SPEC явно использует existing detail context.
- Риск: test станет слишком процедурным. Смягчение: assertions остаются на observable UI/ViewModel contract: rendered breadcrumbs, LastOpened projection and selected context.
- Риск: Headless teardown flake. Смягчение: использовать `NotInParallel`, `SharedUiStateParallelLimit`, existing dispose helper.
- Риск: video evidence requirement. Смягчение: явно зафиксирован fallback и next-best evidence.

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
| `src/Unlimotion.Test/WorkspaceBreadcrumbsLastOpenedUiContract.cs` | Новый UI contract helper | Проверить `AC-0011` через Avalonia.Headless |
| `src/Unlimotion.Test/StormBdd/WorkspaceBreadcrumbsLastOpenedStepDefinitions.cs` | Новый step definition набор | Исполнить `SC-0004-002` |
| `src/Unlimotion.Test/StormWorkspaceBreadcrumbsLastOpenedExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result fields | Передать evidence между steps |
| `docs/product/storm.json`, `docs/product/reports/*` | Links/metrics/reports | `/storm:bdd-sync`, `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0004-002` | `automated`, no steps | `passing`, `TS-0046`, `SD-0079..SD-0082` |
| `ST-0004` executable coverage | 1/3 | 2/3 |
| Step-executable scenarios | 20/45 | 21/45 |
| Product behavior | Existing breadcrumbs/Last Opened | Без изменений |

## 18. Альтернативы и компромиссы
- Использовать только `BreadcrumbEmojiUiTests`: отклонено, потому что scenario также требует Last Opened возврат.
- Использовать только README demo flow: отклонено как слишком широкий smoke для одного BDD bridge.
- Покрыть `SC-0004-003` вместе с этим тестом: отклонено, нарушает small-slice `/storm:cover`.

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
| 5. Тестируемость | 5 | Headless UI, targeted BDD, preserved evidence, STORM validator, full suite |
| 6. Готовность к автономной реализации | 5 | Нет blockers; fallback evidence определен |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-07-10-storm-sc0004-breadcrumbs-last-opened-bdd.md`, central stack, локальный `AGENTS.override.md`, `ST-0004`, `AC-0011`, `GR-011`, `SC-0004-002`, planned changed files.
- Decision: можно выполнять; active goal задаёт auto approval.
- Review passes:
  - Scope/Evidence pass: проверены `storm.json`, feature file, `MainControl.axaml`, `MainWindowViewModel` last-opened/breadcrumb contracts, existing UI/ViewModel tests.
  - Contract pass: спецификация не меняет behavior, acceptance criteria, `.feature`, annotations или selectors; UI evidence предусмотрен.
  - Adversarial risk pass: проверен риск video evidence, headless teardown flakes, procedural overfit и scope creep на `SC-0004-003`.
  - Re-review after fixes / Fix and re-review: не требовалось; detail-context precondition и video fallback включены до review.
  - Stop decision: PASS.
- Evidence inspected: clean worktree after commit `39ffc54`, `features/storm/st-0004-workspace-navigation.feature`, `docs/product/storm.json`, relevant `MainControl`/`MainWindowViewModel` snippets, existing linked tests.
- Depth checklist:
  - Scope drift / unrelated changes: отсутствует; план test/artifact only.
  - Acceptance criteria: `AC-0011` covered by breadcrumbs + Last Opened return assertions.
  - Validation evidence: команды заданы; full gate обязателен.
  - Unsupported claims: video fallback явно ограничен.
  - Regression / edge case: teardown flake stop rule задан.
  - Comments/docs/changelog: новые comments не планируются; changelog не нужен.
  - Hidden contract change: production/UI selectors не меняются.
  - Manual-review challenge: reviewer будет искать, не подменён ли Last Opened только breadcrumbs test; spec требует оба аспекта.
- No-findings justification: small test-only BDD slice, объективные UI/video ограничения зафиксированы.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence не создается текущим runner | Зафиксировать fallback и использовать targeted headless/full-suite evidence | accepted-risk |

- Fixed before continuing: Не применимо.
- Checks rerun: SPEC linter/rubric self-check.
- Needs human: Нет; active goal auto-approves SPEC.
- Residual risks / follow-ups: `SC-0004-003` остается следующим `/storm:cover` gap.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, changed test/artifact files, `WorkspaceBreadcrumbsLastOpenedUiContract`, `WorkspaceBreadcrumbsLastOpenedStepDefinitions`, `StormWorkspaceBreadcrumbsLastOpenedExecutableSpecTests`, `StormStepDefinition`, `storm.json`, reports and validation evidence.
- Decision: можно коммитить.
- Review passes:
  - Scope/Evidence pass: изменения ограничены test-only BDD bridge, SPEC and STORM artifacts.
  - Contract pass: `SC-0004-002` получил `TS-0046` and `SD-0079..SD-0082`; production code, `.feature`, automation IDs and existing annotations unchanged.
  - Adversarial risk pass: проверены risks procedural overfit, missing Last Opened evidence, video fallback, and full-suite stability.
  - Re-review after fixes / Fix and re-review: nullable warning in new helper fixed; artifacts re-synced with final controlled retry evidence.
  - Stop decision: PASS.
- Evidence inspected:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false` => passed, existing warnings.
  - `StormWorkspaceBreadcrumbsLastOpenedExecutableSpecTests` => passed 1/1.
  - `BreadcrumbEmojiUiTests` => passed 1/1.
  - `MainControlTreeCommandsUiTests/TreeCommandUi_NonAllTasksTabs_CurrentAndAllCommands_Work` => passed 4/4.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` => OK: 0 errors, 9 warnings.
  - Initial full `Unlimotion.Test` => 576/577 because unrelated `Avalonia.Headless.DisposeAsync` teardown NRE in `CurrentTaskCard_DarkTheme_UsesThemeAwareAccentButtonChrome`; isolated rerun passed 1/1.
  - Controlled full `Unlimotion.Test` retry => passed 577/577 with `C:\tmp\unlimotion-full-suite-sc0004-breadcrumbs-last-opened-bdd-retry.log`.
- Depth checklist:
  - Scope drift / unrelated changes: no production, feature, selector or annotation changes.
  - Acceptance criteria: `AC-0011` covered by rendered breadcrumbs path and Last Opened selection restoring task context.
  - Validation evidence: targeted, preserved UI evidence, STORM validator and controlled full suite present.
  - Unsupported claims: video evidence marked fallback, not claimed as produced.
  - Regression / edge case: unrelated teardown flake isolated and full retry passed.
  - Comments/docs/changelog: no code comments/changelog needed; product reports updated.
  - Hidden contract change: none; UI contract uses existing automation IDs and bindings.
  - Manual-review challenge: reviewer would check whether Last Opened is real UI evidence; `MainControl` opens in Avalonia.Headless and selects `LastOpenedTree`.
- No-findings justification: implementation follows existing STORM BDD/UI patterns, validates via headless UI, and changes only tests/artifacts.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video artifact not produced by current Avalonia.Headless runner | Preserve explicit fallback and use targeted/full-suite evidence | accepted-risk |
| LOW | validation | First full-suite run hit unrelated headless teardown NRE | Isolate failing test, run controlled full retry, record evidence | resolved |

- Fixed before final report: Nullable guard added for `BreadcrumbsTextBlock.Inlines`; artifacts updated from pending to final full-suite evidence.
- Checks rerun: build, targeted BDD, preserved UI evidence, STORM validator, isolated teardown proof, full-suite controlled retry.
- Validation evidence: listed above.
- Unrelated changes: none observed in task scope.
- Needs human: Нет.
- Residual risks / follow-ups: `SC-0004-003` remains the next ST-0004 `/storm:cover` gap.
## Approval
Подтверждено активной целью пользователя: SPEC auto-approved for execution.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0004-002` | 0.88 | Нет | Перейти к EXEC | Нет | Да, active goal auto approval | UI-facing scenario требует headless UI executable bridge без product-code changes | `specs/2026-07-10-storm-sc0004-breadcrumbs-last-opened-bdd.md` |
| EXEC | executable BDD UI slice | 0.92 | Нет | Commit и перейти к следующему `/storm:cover` candidate | Нет | Нет | Targeted/full gates passed; `SC-0004-002` закрыт step-executable | `src/Unlimotion.Test/StormWorkspaceBreadcrumbsLastOpenedExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/WorkspaceBreadcrumbsLastOpenedStepDefinitions.cs`, `src/Unlimotion.Test/WorkspaceBreadcrumbsLastOpenedUiContract.cs`, `docs/product/storm.json`, `docs/product/reports/*` |
