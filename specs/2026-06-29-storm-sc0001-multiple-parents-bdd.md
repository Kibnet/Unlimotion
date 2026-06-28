# STORM BDD: executable slice для SC-0001-002 multiple parents

## 0. Метаданные
- Тип (профиль): `delivery-task` / QUEST SPEC / `/storm:cover -> /storm:bdd-implement SC-0001-002`.
- Владелец: STORM product artifacts Unlimotion.
- Масштаб: small.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: `storm-bootstrap`.
- Instruction stack: central `AGENTS.md` -> `routing-matrix.md` -> `model-behavior-baseline` + `quest-governance` + `quest-mode` + `collaboration-baseline` + `testing-baseline` + `testing-dotnet` + `dotnet-desktop-client` + `ui-automation-testing` + `storm-product-development` + локальный `AGENTS.override.md`.
- Ограничения: product artifacts на русском; не менять production code, `.feature` wording, project files, workflows и existing test annotations; не заменять acceptance criteria на Gherkin.
- Связанные ссылки: `docs/product/storm.json`, `features/storm/st-0001-task-graph.feature`, `src/Unlimotion.Test/MainWindowViewModelTests.cs`, `src/Unlimotion.Test/MainControlRelationPickerUiTests.cs`, `src/Unlimotion.Test/StartupProjectionAndRelationsTests.cs`, `src/Unlimotion.Test/TaskMigratorTests.cs`.

## 1. Overview / Цель

Добавить executable BDD coverage для `SC-0001-002`: задача может иметь несколько родителей, а обратные связи `parent-child` остаются синхронизированными.

Outcome contract:
- Success means: `SC-0001-002` исполняется из `features/storm/st-0001-task-graph.feature` через repo-local BDD runner.
- Итоговый артефакт / output: `TS-0037`, `SD-0043..SD-0046`, обновленные `docs/product/storm.json` и `docs/product/reports/*`.
- Stop rules: остановиться, если потребуется менять production behavior, `.feature` wording, existing test annotations, project files/workflows или извлекать shared helpers из существующих тестов шире этой SPEC.

## 2. Текущее состояние (AS-IS)

- `SC-0001-002` уже есть в `features/storm/st-0001-task-graph.feature`, связан с `ST-0001`, `AC-0002`, `TS-0001`, `TS-0014`, но `step_definitions` пустой.
- `TS-0001` указывает на `MainWindowViewModelTests`, где уже есть поведение:
  - `CurrentItemParentsAdd_Success` синхронизирует `current.ParentTasks` и `parent.ContainsTasks`.
  - `CurrentItemContainsAdd_Success` синхронизирует `parent.ContainsTasks` и `child.ParentTasks`.
  - `MovingTaskWithTwoParentsToRootTask_Success` доказывает сохранение второго родителя при переносе relation.
  - `CopyBlockedTaskToNewParent_WithFileStorage_ShouldBlockNewParent` доказывает multi-parent containment после copy relation.
- `TS-0014` указывает на storage/migration tests:
  - `Migrate_BuildsParentsAndNormalizesChildren` строит reverse parent links из `ContainsTasks`.
  - `UnifiedTaskStorage_Init_ShouldRepairReverseLinks_WhenMigrationReportAlreadyExists` чинит reverse links при запуске.
- UI-level evidence есть в `MainControlRelationPickerUiTests.TaskCardRelationEditor_AddParentFromCard_UpdatesStorage`, который добавляет родителя через карточку и проверяет обе стороны связи.

## 3. Проблема

BDD layer пока не имеет исполняемой связи `Scenario -> Step Definition -> Test` для `SC-0001-002`, хотя существующие тесты уже доказывают поведение multiple-parent и reverse-link synchronization.

## 4. Цели дизайна

- Сохранить существующее поведение и тесты без изменения аннотаций.
- Связать scenario wording с existing evidence через test-only BDD bridge.
- Использовать UI evidence для UI-facing relation editor path согласно локальному override.
- Обновить STORM metrics без пересоздания существующих stories/tests/conflicts.

## 5. Non-Goals

- Не менять production code.
- Не менять `.feature` wording.
- Не менять existing tests или test annotations.
- Не добавлять новую product behavior.
- Не закрывать весь `ST-0001`; только `SC-0001-002` становится step-executable.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент/файл | Ответственность |
| --- | --- |
| `src/Unlimotion.Test/MultipleParentsRelationContract.cs` | Test-only contract запускает existing VM/storage/UI evidence и собирает результат сценария. |
| `src/Unlimotion.Test/StormBdd/MultipleParentsRelationStepDefinitions.cs` | `SD-0043..SD-0046` связывают точные шаги `SC-0001-002` с contract assertions. |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Добавляет context/result fields для этого scenario. |
| `src/Unlimotion.Test/StormMultipleParentsRelationExecutableSpecTests.cs` | Парсит `SC-0001-002` из `.feature` и исполняет все шаги. |
| `docs/product/storm.json`, `docs/product/reports/*` | Фиксируют `SC-0001-002 -> TS-0037 -> SD-0043..SD-0046`, статус `passing`, метрики `12/45`. |

### 6.2 Детальный дизайн

- BDD runner читает существующий `.feature` file и не меняет Gherkin wording.
- Contract запускает existing public test methods:
  - `MainWindowViewModelTests.CurrentItemParentsAdd_Success`.
  - `MainWindowViewModelTests.CurrentItemContainsAdd_Success`.
  - `MainWindowViewModelTests.MovingTaskWithTwoParentsToRootTask_Success`.
  - `MigrateTests.Migrate_BuildsParentsAndNormalizesChildren`.
  - `UnifiedTaskStorageMigrationRegressionTests.UnifiedTaskStorage_Init_ShouldRepairReverseLinks_WhenMigrationReportAlreadyExists`.
  - `StartupProjectionAndRelationsTests.TaskRelationsIndex_ShouldSynchronizeRelationCollectionsWithIds`.
  - `MainControlRelationPickerUiTests.TaskCardRelationEditor_AddParentFromCard_UpdatesStorage`.
- Visual planning artifact для UI-facing изменений: `Не применимо` — UI не меняется; используется existing headless UI test как behavior evidence.
- UI test video evidence: `Не применимо`; изменение test-only BDD bridge без UI изменения, fallback evidence: Avalonia.Headless targeted test output.
- Обработка ошибок: если любой existing evidence test падает, `TS-0037` падает.
- Производительность: bounded targeted test slice; full suite остается финальным gate.

## 7. Бизнес-правила / Алгоритмы

- Одна задача может иметь больше одного родителя.
- Добавление родителя обязано обновлять `child.ParentTasks` и `parent.ContainsTasks`.
- Добавление child relation обязано обновлять `parent.ContainsTasks` и `child.ParentTasks`.
- Migration/storage repair должны восстанавливать reverse links из canonical relation ids.

## 8. Точки интеграции и триггеры

- Repo-local BDD runner вызывает `MultipleParentsRelationStepDefinitions.Create()`.
- Step `Когда` запускает `MultipleParentsRelationContract.ExecuteMultipleParentsRelationScenarioAsync()`.
- Step `Тогда` проверяет `MultipleParentsRelationScenarioResult`.

## 9. Изменения модели данных / состояния

Новых persisted fields нет. Меняется только test-only context в `StormScenarioContext` и product traceability artifacts.

## 10. Миграция / Rollout / Rollback

- Rollout: commit test-only BDD bridge и STORM artifact sync.
- Rollback: revert commit; production behavior не затронуто.
- Backward compatibility: existing test names, annotations and `.feature` wording сохраняются.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
1. `StormMultipleParentsRelationExecutableSpecTests` парсит `SC-0001-002` и исполняет 4 шага.
2. `SD-0043..SD-0046` support only `SC-0001-002`.
3. `TS-0037` запускает existing VM/storage/UI evidence for multiple parents and reverse-link sync.
4. `SC-0001-002` получает `status = passing`, linked test `TS-0037`, step definitions `SD-0043..SD-0046`.
5. Production code, feature wording, existing test annotations, project files и workflows не меняются.

Команды проверки:
1. `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal`.
2. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormMultipleParentsRelationExecutableSpecTests/*" --output Detailed`.
3. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/CurrentItemParentsAdd_Success" --output Detailed`.
4. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainWindowViewModelTests/CurrentItemContainsAdd_Success" --output Detailed`.
5. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlRelationPickerUiTests/TaskCardRelationEditor_AddParentFromCard_UpdatesStorage" --output Detailed`.
6. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MigrateTests/Migrate_BuildsParentsAndNormalizesChildren" --output Detailed`.
7. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/UnifiedTaskStorageMigrationRegressionTests/UnifiedTaskStorage_Init_ShouldRepairReverseLinks_WhenMigrationReportAlreadyExists" --output Detailed`.
8. `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StartupProjectionAndRelationsTests/TaskRelationsIndex_ShouldSynchronizeRelationCollectionsWithIds" --output Detailed`.
9. `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`.
10. `git diff --check`.
11. `rg -n "[ \t]+$" src\Unlimotion.Test docs\product specs\2026-06-29-storm-sc0001-multiple-parents-bdd.md`.
12. Full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed` вне managed sandbox with log capture.

Stop rules для validation: если full suite в managed sandbox падает на known ACL-only `BackupViaGitServiceTests.GetCredentials_HardensConfiguredPrivateKeyPermissionsOnWindows`, повторить outside sandbox and capture log; если падает новый/связанный test, исправить до коммита.

## 12. Риски и edge cases

- Direct public test-method reuse creates lifecycle coupling. Смягчение: one contract wrapper, stop rule for helper extraction if lifecycle breaks.
- UI headless tests can be sensitive to shared state. Смягчение: `[NotInParallel("AvaloniaHeadless")]`, targeted UI check and full suite outside sandbox.
- Duplicate Given step warning likely increases with `SD-0043`. Это accepted BDD-lint warning until shared step normalization is handled separately.

## 13. План выполнения

1. Добавить test-only contract, step definitions, executable spec test and context fields.
2. Запустить build and targeted BDD/evidence tests.
3. Выполнить `/storm:bdd-sync` and `/storm:bdd-lint` artifact updates.
4. Запустить STORM validator, hygiene checks and full suite.
5. Выполнить post-EXEC review, исправить findings, закоммитить.
6. Выбрать следующий `/storm:cover` candidate.

## 14. Открытые вопросы

Нет блокирующих вопросов.

## 15. Соответствие профилю

- Профиль: `storm-product-development` + delivery-task route через QUEST/testing stack.
- Выполненные требования профиля: canonical `docs/product/storm.json`, Gherkin не заменяет AC, Scenario -> Test -> Step Definition sync, Russian product artifacts, UI evidence for UI-facing path, no production behavior change.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-29-storm-sc0001-multiple-parents-bdd.md` | Новая QUEST SPEC | Зафиксировать scope и gates. |
| `src/Unlimotion.Test/MultipleParentsRelationContract.cs` | Новый test-only contract | Связать scenario с existing evidence tests. |
| `src/Unlimotion.Test/StormBdd/MultipleParentsRelationStepDefinitions.cs` | Новый step definition набор | Исполнить `SC-0001-002`. |
| `src/Unlimotion.Test/StormMultipleParentsRelationExecutableSpecTests.cs` | Новый executable spec test | Проверить `.feature` wording through runner. |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Context fields | Хранить результат scenario execution. |
| `docs/product/storm.json` | Traceability/metrics sync | `SC-0001-002 -> TS-0037 -> SD-0043..SD-0046`. |
| `docs/product/reports/*` | Coverage/BDD reports | Отразить новую метрику и gaps. |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| `SC-0001-002.step_definitions` | `[]` | `SD-0043..SD-0046` |
| `SC-0001-002.linked_tests` | `TS-0001`, `TS-0014` | `TS-0001`, `TS-0014`, `TS-0037` |
| Step-executable scenarios | `11/45` | `12/45` |
| ST-0001 executable coverage | `1/3` | `2/3` |

## 18. Альтернативы и компромиссы

- Вариант: написать новый standalone test без reuse existing tests.
  - Плюсы: меньше lifecycle coupling.
  - Минусы: дублирует уже существующее evidence и шире меняет тестовую поверхность.
- Вариант: извлечь shared helpers из existing tests.
  - Плюсы: чище долгосрочно.
  - Минусы: refactor старых tests вне текущего scope.
- Выбранный вариант: test-only BDD contract запускает existing tests. Он минимален, сохраняет старые аннотации и улучшает traceability.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals проверяемы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, правила и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Production behavior и existing annotations protected by stop rules. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria and concrete commands listed. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, альтернативы, open questions and file table present. |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/testing route зафиксирован. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один scenario, explicit non-goals. |
| 2. Понимание текущего состояния | 5 | Указаны feature, scenario, linked tests and evidence methods. |
| 3. Конкретность целевого дизайна | 5 | Target files and IDs fixed. |
| 4. Безопасность (миграция, откат) | 5 | Test-only change, rollback by commit revert. |
| 5. Тестируемость | 5 | Targeted, artifact, hygiene and full-suite commands defined. |
| 6. Готовность к автономной реализации | 5 | Нет open blockers; user goal auto-approves SPEC. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-29-storm-sc0001-multiple-parents-bdd.md`, central stack, local override, `features/storm/st-0001-task-graph.feature`, `docs/product/storm.json`, selected planned files.
- Decision: можно переходить к EXEC по активной цели пользователя.
- Review passes:
  - Scope/Evidence pass: просмотрены scenario, linked tests, owner-documents and planned changed files.
  - Contract pass: Non-Goals protect production code, feature wording and existing annotations.
  - Adversarial risk pass: identified lifecycle coupling and duplicate-step warning; both bounded by stop/follow-up rules.
  - Re-review after fixes / Fix and re-review: findings requiring edits not found.
  - Stop decision: PASS.
- Evidence inspected: feature text, `storm.json` scenario/test entries, `MainWindowViewModelTests`, `MainControlRelationPickerUiTests`, `TaskMigratorTests`, `UnifiedTaskStorageMigrationRegressionTests`.
- Depth checklist:
  - Scope drift / unrelated changes: none planned.
  - Acceptance criteria: mapped to `TS-0037` and `SD-0043..SD-0046`.
  - Validation evidence: commands listed before EXEC.
  - Unsupported claims: none; all claims tied to existing tests or future validation.
  - Regression / edge case: UI headless and ACL sandbox risks documented.
  - Comments/docs/changelog: no code comments/changelog planned.
  - Hidden contract change: none; `.feature` wording unchanged.
  - Manual-review challenge: likely concern is direct test-method reuse; accepted as scoped pattern already used in prior slices.
- No-findings justification: SPEC has one bounded objective, exact files/IDs, explicit stop rules and concrete validation.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | risk | Direct existing test-method reuse creates lifecycle coupling. | Keep wrapper scoped; extract helpers only in separate SPEC if needed. | accepted-risk |

- Fixed before continuing: none.
- Checks rerun: manual linter/rubric/review.
- Needs human: no; active goal says SPEC is automatically confirmed.
- Residual risks / follow-ups: later shared-step normalization could reduce duplicate step warnings.

### Post-EXEC Review
- Статус: PASS for scoped `SC-0001-002` slice; full-suite gate blocked by unrelated flaky/order-sensitive tests.
- Scope reviewed: `src/Unlimotion.Test/StormMultipleParentsRelationExecutableSpecTests.cs`, `src/Unlimotion.Test/MultipleParentsRelationContract.cs`, `src/Unlimotion.Test/StormBdd/MultipleParentsRelationStepDefinitions.cs`, `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs`, `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md`.
- Implemented: `SC-0001-002` now has `TS-0037` and `SD-0043..SD-0046`; existing `TS-0001` and `TS-0014` remain linked.
- Targeted validation passed:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -v minimal` passed with existing warnings, errors 0.
  - `StormMultipleParentsRelationExecutableSpecTests` passed 1/1.
  - `MainWindowViewModelTests/CurrentItemParentsAdd_Success` passed 1/1.
  - `MainWindowViewModelTests/CurrentItemContainsAdd_Success` passed 1/1.
  - `MainWindowViewModelTests/MovingTaskWithTwoParentsToRootTask_Success` passed 1/1.
  - `MainControlRelationPickerUiTests/TaskCardRelationEditor_AddParentFromCard_UpdatesStorage` passed 1/1.
  - `MigrateTests/Migrate_BuildsParentsAndNormalizesChildren` passed 1/1.
  - `UnifiedTaskStorageMigrationRegressionTests/UnifiedTaskStorage_Init_ShouldRepairReverseLinks_WhenMigrationReportAlreadyExists` passed 1/1.
  - `StartupProjectionAndRelationsTests/TaskRelationsIndex_ShouldSynchronizeRelationCollectionsWithIds` passed 1/1.
- Review passes:
  - Scope/Evidence pass: changed files match the approved test-only BDD slice and artifact sync.
  - Contract pass: production code, `.feature` wording, existing test annotations, `.csproj`, workflows were not changed.
  - Adversarial risk pass: direct existing-test reuse is still localized to the new contract wrapper; no shared helper refactor was introduced.
  - UI evidence pass: headless UI relation-editor evidence is included through `MainControlRelationPickerUiTests/TaskCardRelationEditor_AddParentFromCard_UpdatesStorage` and the new executable BDD test.
  - Re-review after fixes / Fix and re-review: no blocking findings found after targeted validation.
  - Stop decision: PASS for scoped test-only BDD slice; do not change unrelated tests/code under this SPEC.
- Final validation:
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` passed with 0 errors and 4 intentional duplicate-step warnings.
  - `git diff --check` passed with LF-to-CRLF working-copy warnings only.
  - Trailing whitespace scan returned no matches (`rg` exit 1).
  - Full `Unlimotion.Test` outside managed sandbox failed 566/568 on unrelated `FilterFlyout_EmojiFilters_SummaryShowsSelectedEmojiAndOverflowInListOrder` and `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask`.
  - Targeted retry for `FilterFlyout_EmojiFilters_SummaryShowsSelectedEmojiAndOverflowInListOrder` passed 1/1.
  - Targeted retry for `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask` failed once and then passed 1/1, indicating flaky/order-sensitive behavior outside `SC-0001-002` scope.
  - Full-suite retry timed out after 604 seconds before progress beyond test-run start; leftover `dotnet` runner process was stopped.
- Residual risks / follow-ups: duplicate shared Given/And warnings remain intentional until a separate shared-step normalization SPEC; `SC-0001-003` remains the next ST-0001 linked-existing-tests-only scenario; full-suite stability now needs a separate QUEST stabilization SPEC before broad `/storm:cover` expansion.

## Approval

Получено автоматически из активной цели пользователя: "я автоматически спеку подтверждаю".

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего coverage slice | 0.9 | Нет | Написать SPEC | Нет | Да: active goal auto-approval | `SC-0001-002` highest-ranked remaining scenario без step definitions. | `docs/product/storm.json`, `features/storm/st-0001-task-graph.feature` |
| SPEC | Review SPEC | 0.9 | Нет | Перейти к EXEC | Нет | Да: active goal auto-approval | Scope ограничен test-only BDD bridge и artifact sync. | `specs/2026-06-29-storm-sc0001-multiple-parents-bdd.md` |
| EXEC | Реализация BDD bridge | 0.85 | Нет | Прогнать targeted tests | Нет | Да: active goal auto-approval | Добавлены `TS-0037`, `SD-0043..SD-0046` and contract wrapper без изменения production behavior. | `src/Unlimotion.Test/StormMultipleParentsRelationExecutableSpecTests.cs`, `src/Unlimotion.Test/MultipleParentsRelationContract.cs`, `src/Unlimotion.Test/StormBdd/MultipleParentsRelationStepDefinitions.cs`, `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` |
| EXEC | `/storm:bdd-sync` и `/storm:bdd-lint` artifact sync | 0.9 | Full-suite blocker outside this slice | Закоммитить scoped slice, затем открыть stabilization SPEC | Нет | Да: active goal auto-approval | `SC-0001-002` переведен в `passing`; метрики обновлены до `12/45`; full-suite blocker зафиксирован как отдельный QUEST follow-up. | `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/bdd-lint.md` |
