# STORM SC-0006-003: executable BDD для Wanted и Importance

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:cover`, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `storm-bootstrap`
- Ограничения: не менять product behavior; не менять `.feature` wording; не менять existing test annotations; не менять production code; продуктовые артефакты вести на русском
- Связанные ссылки: `ST-0006`, `AC-0018`, `GR-018`, `SC-0006-003`, `TS-0005`, `TS-0013`, `CurrentTaskWantedCheckBox`, `CurrentTaskImportanceInput`, `ShowWanted`, `GraphViewModel.OnlyUnlocked`, `SortDefinition`

Если секция не применима, это указано явно в соответствующей секции.

## 1. Overview / Цель
Добавить executable BDD layer для `SC-0006-003`: `Wanted` и `Importance` доступны в UI и участвуют в представлении и фильтрации задач.

Outcome contract:
- Success means: `SC-0006-003` получает новый executable BDD test `TS-0050`, step definitions `SD-0095..SD-0098`, passing UI/headless evidence, а executable specification ratio увеличивается с 24/45 до 25/45.
- Итоговый артефакт / output: test-only executable spec + обновленные `storm.json` и reports.
- Stop rules: остановиться, если нужны изменения production behavior, `.feature` wording, UI layout/automation IDs, existing annotations или новое продуктовое решение по wanted/importance semantics.

## 2. Текущее состояние (AS-IS)
- `SC-0006-003` связан с `AC-0018`, `GR-018`, `TS-0005`, `TS-0013`, status = `automated`, `step_definitions = []`.
- Existing evidence уже подтверждает части поведения:
  - `MainControlTaskCardLayoutUiTests` проверяет наличие `CurrentTaskWantedCheckBox` и `CurrentTaskImportanceInput` в карточке задачи.
  - `MainControlWantedUiTests` проверяет UI-only cascade для `WantedFromUi`.
  - `TaskImportanceUiTests` проверяет bold-представление wanted-задач в дереве и Roadmap.
  - `FilterResetUiContract` и `MainControlResetFiltersUiTests` проверяют reset/ShowWanted filter behavior.
- Code contracts:
  - `TaskItem.Wanted` и `TaskItem.Importance` являются persisted domain fields.
  - `TaskItemViewModel.WantedFromUi` вызывает UI-only change path и `Wanted` участвует в `AlsoNotifyFor`.
  - `MainWindowViewModel.ShowWanted` участвует в фильтре задач.
  - `SortDefinition` содержит `importance-ascending` и `importance-descending`.
- Worktree чистый после коммита `0f6d766`.

## 3. Проблема
Для `SC-0006-003` нет исполняемой связи `Scenario -> Test -> Step Definition -> UI/ViewModel code`, поэтому `/storm:cover` не может считать wanted/importance закрытыми на BDD layer.

## 4. Цели дизайна
- Проверить actual `MainControl` task card UI через Avalonia.Headless.
- Проверить stable automation IDs для wanted/importance controls.
- Проверить, что `WantedFromUi` и `Importance` изменяются через UI-bound controls.
- Проверить user-visible wanted presentation: wanted title получает bold state в All Tasks tree.
- Проверить filter route: `ShowWanted = true/false` включает wanted filter для Roadmap `OnlyUnlocked` представления, где этот фильтр фактически подключён.
- Проверить importance route: `SortDefinition` содержит ascending/descending sort definitions по importance.
- Сохранить existing tests и annotations без изменений.

## 5. Non-Goals (чего НЕ делаем)
- Не менять UX cascade-вопроса для `Wanted`.
- Не менять фильтр-панели, sort UI, labels, localization, selectors или layout.
- Не добавлять video artifacts в репозиторий.
- Не расширять этот slice за пределы `SC-0006-003`.
- Не запускать `/storm:full-cycle`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/WantedImportanceUiContract.cs` -> test-only Avalonia.Headless flow для карточки задачи, wanted presentation/filter и importance binding/sort contract.
- `src/Unlimotion.Test/StormBdd/WantedImportanceStepDefinitions.cs` -> `SD-0095..SD-0098`, binding product wording к UI contract.
- `src/Unlimotion.Test/StormTaskPlanningWantedImportanceExecutableSpecTests.cs` -> `TS-0050`, парсит existing `.feature` scenario и запускает шаги.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> test-only result fields для передачи evidence между steps.
- `docs/product/storm.json` и `docs/product/reports/*` -> `/storm:bdd-sync`, `/storm:bdd-lint`, behavior metrics.

### 6.2 Детальный дизайн
- UI flow:
  1. Открыть `MainControl` с `MainWindowViewModelFixture` в `HeadlessUnitTestSession`.
  2. Включить `AllTasksMode`, `DetailsAreOpen`, выбрать листовую задачу без descendants, чтобы не запускать cascade modal.
  3. Найти `CurrentTaskWantedCheckBox` и `CurrentTaskImportanceInput`.
  4. Проверить, что controls bind-ятся к `CurrentTaskItem`.
  5. Через checkbox выставить `Wanted = true`.
  6. Через `NumericUpDown` выставить `Importance = 42`.
  7. Проверить, что title текущей wanted-задачи в `AllTasksTree` получает bold presentation.
  8. Переключиться в Roadmap, включить `Graph.OnlyUnlocked`, проверить `ShowWanted = true` показывает wanted-задачу и скрывает non-wanted; `ShowWanted = false` скрывает wanted-задачу.
  9. Проверить `SortDefinition` содержит `importance-ascending` и `importance-descending`.
- Visual evidence: UI layout не меняется; preserved UI/headless assertions и existing `TaskImportanceUiTests`/`MainControlWantedUiTests` остаются regression evidence.
- UI video evidence: fallback/не применимо как repository artifact; текущий Avalonia.Headless/TUnit runner не пишет безопасные видео, а layout не меняется. Evidence: targeted headless output + full-suite gate.
- Ошибки: если control/filter/sort contract не подтверждается, BDD test падает; production code не исправляется внутри этой spec.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Wanted/importance controls | Пользователь открывает карточку задачи, ставит `Wanted` и меняет `Importance` | Карточка меняет поля текущей задачи; wanted title выделяется; wanted filter включает/исключает задачу | `StormTaskPlanningWantedImportanceExecutableSpecTests`, preserved wanted/importance UI tests | `AC-0018` |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Current task `Wanted = false`, `Importance = 0` | UI checkbox + numeric input | `Wanted = true`, `Importance = 42` | Если control не найден, BDD fails | Проверяется через actual controls |
| All Tasks view without wanted filter | Wanted task visible | Title bold state applied | Если visual tree не материализовался, wait fails | Сохраняет existing presentation contract |
| Roadmap `OnlyUnlocked`, `ShowWanted = true` | Apply wanted filter | Roadmap nodes are wanted and include target | Empty view fails для выбранного target | Existing ViewModel/Graph filter route |
| Roadmap `OnlyUnlocked`, `ShowWanted = false` | Apply not-wanted filter | Target wanted task hidden | Other tasks должны остаться visible | Confirms both filter states |
| Sort definitions loaded | Inspect `SortDefinitions` | importance ascending/descending present | Missing definition fails | Covers importance participation beyond raw field |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Не менять `.feature` wording | agent | Использовать existing `SC-0006-003` text как canonical wording | 0.95 | Низкий | Нет |
| Делать UI/headless bridge | agent | Scenario явно говорит "доступны в UI" | 0.88 | Средний: больше runtime, но лучше соответствует AC | Нет |
| Выбирать leaf task для checkbox | agent | Избежать cascade modal в этом slice | 0.82 | Низкий: cascade уже покрыт existing tests | Нет |
| Filter проверять через `ShowWanted`, а не кликом в filter panel | agent | Existing filter UI reset tests сохранены; BDD bridge проверяет behavior route | 0.80 | Средний: меньше UI-panel coverage, но меньше flaky setup | Нет |

## 7. Бизнес-правила / Алгоритмы
- `Wanted` доступен через `CurrentTaskWantedCheckBox` и меняет `TaskItemViewModel.WantedFromUi`.
- Wanted-задачи визуально выделяются в списках/деревьях.
- `ShowWanted` фильтрует Roadmap `OnlyUnlocked` задачи по `Wanted = true/false`.
- `Importance` доступен через `CurrentTaskImportanceInput` и участвует в sort definitions.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario(..., "SC-0006-003")`.
- `StormScenarioRunner` executes four feature steps.
- UI contracts: `MainControl`, `AllTasksTree`, `CurrentTaskWantedCheckBox`, `CurrentTaskImportanceInput`.
- ViewModel contracts: `TaskItemViewModel.WantedFromUi`, `TaskItemViewModel.Importance`, `MainWindowViewModel.ShowWanted`, `GraphViewModel.OnlyUnlocked`, `SortDefinition`.

## 9. Изменения модели данных / состояния
Production state не меняется. Test-only fixture меняет `Wanted` и `Importance` у временных задач и очищает их через `CleanTasks()`.

## 10. Миграция / Rollout / Rollback
Migration не требуется. Rollback: удалить `TS-0050`, `SD-0095..SD-0098`, `WantedImportanceUiContract` и откатить artifact links/metrics.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `SC-0006-003` исполняется через repo-local steps.
  - Tags `@scenario:SC-0006-003`, `@story:ST-0006`, `@constraint:CN-0003`, `@constraint:CN-0004`, `@test:TS-0005`, `@test:TS-0013` проверены.
  - Wanted/importance controls найдены и bind-ятся к текущей задаче.
  - Wanted and importance fields меняются через UI-bound controls.
  - Wanted presentation and `ShowWanted` Roadmap `OnlyUnlocked` filter route подтверждены.
  - Importance sort definitions подтверждены.
  - `storm.json` и reports синхронизированы: scenario status `passing`, linked test `TS-0050`, step definitions `SD-0095..SD-0098`, executable ratio 25/45.
- Какие тесты добавить/изменить: добавить `StormTaskPlanningWantedImportanceExecutableSpecTests`, `WantedImportanceStepDefinitions`, `WantedImportanceUiContract`; не менять existing tests/annotations.
- Characterization tests / contract checks: target bridge проверяет existing behavior; preserved UI tests запускаются как regression evidence.
- Visual acceptance: layout не меняется; проверяется bold state wanted-title в actual visual tree.
- UI video evidence: fallback/не применимо как repository artifact; evidence commands фиксируются в post-EXEC.
- Команды для проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskPlanningWantedImportanceExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlWantedUiTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TaskImportanceUiTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
- Stop rules для validation loops: если full suite падает на unrelated AvaloniaHeadless/ACL flake, изолировать failing test, запустить controlled retry и зафиксировать evidence; если падает новый BDD contract, исправить или остановиться, если требуется behavior change.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| `AC-0018`: Wanted и importance доступны в UI и участвуют в представлении и фильтрации задач | `StormTaskPlanningWantedImportanceExecutableSpecTests`, preserved wanted/importance UI tests | `storm.json` scenario/test/step links | TUnit output, STORM validator output, reports | Не применимо |

## 12. Риски и edge cases
- Риск: changing `Wanted` on parent triggers cascade modal. Смягчение: выбрать leaf task; cascade remains covered by existing tests.
- Риск: visual tree async materialization. Смягчение: wait helpers с `Dispatcher.UIThread.RunJobs()`.
- Риск: filter result depends on default `ShowWanted`. Смягчение: contract explicitly sets `ShowWanted = null/true/false`.
- Риск: full suite duration. Смягчение: сначала targeted gates, full suite после artifact sync.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| "Это UI behavior, нужен UI test" | Local override требует UI tests | Новый BDD bridge открывает actual `MainControl` в Avalonia.Headless | mitigated |
| "Не трогай существующие тесты/аннотации" | Пользователь задавал это ограничение | Existing tests не изменяются; добавляется новый bridge и artifact links | mitigated |
| "Wanted cascade не проверен" | WantedFromUi может открыть modal | Cascade уже покрыт `MainControlWantedUiTests`/`MainWindowViewModelTests`; slice проверяет AC-0018 route | mitigated |

### Rework Prevention Checklist
- Does the spec name what the user will see or operate? Да: карточка задачи, wanted checkbox, importance input, All Tasks presentation и Roadmap filter.
- Does every user-visible scenario have evidence? Да: BDD UI test + preserved UI tests + STORM validator.
- Did the agent list decisions it assumed? Да: Decision Ledger.
- Did the agent predict likely objections and mitigate them? Да.
- Did role-based review run for the relevant task type? Да, см. секцию 19.
- Are acceptance criteria verifiers, not preparation steps? Да.
- Does EXEC have a path to prove the scenarios before final? Да.

## 13. План выполнения
1. Создать SPEC и post-SPEC review.
2. Добавить test-only UI contract, context fields, step definitions, executable spec.
3. Обновить STORM artifacts/reports.
4. Запустить targeted BDD/UI checks, STORM validator, full suite or controlled retry.
5. Post-EXEC review и commit.

## 14. Открытые вопросы
Нет блокирующих.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: QUEST gate, Scenario -> Test -> Step Definition -> UI/ViewModel code, TUnit `--treenode-filter`, UI/headless evidence, product artifacts на русском.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/WantedImportanceUiContract.cs` | Новый UI contract helper | Проверить `AC-0018` через Avalonia.Headless |
| `src/Unlimotion.Test/StormBdd/WantedImportanceStepDefinitions.cs` | Новый step definition набор | Исполнить `SC-0006-003` |
| `src/Unlimotion.Test/StormTaskPlanningWantedImportanceExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result fields | Передать evidence между steps |
| `docs/product/storm.json`, `docs/product/reports/*` | Links/metrics/reports | `/storm:bdd-sync`, `/storm:bdd-lint` |

## 17. Альтернативы
- Reuse existing UI tests by calling them from BDD contract: отклонено, чтобы избежать test-to-test dependency.
- Domain/viewmodel-only test без UI: отклонено, потому что scenario explicitly requires UI availability.
- Изменить `.feature` wording на более детальный Gherkin: отклонено, wording уже в artifact layer и не меняется без отдельного подтверждения.

## 18. SPEC review
- Coverage: PASS. SPEC покрывает UI availability, presentation, filter and importance participation.
- Feasibility: PASS. Все проверки можно выполнить test-only без product changes.
- Risk: PASS with notes. Основной риск headless materialization закрыт wait helpers.
- Scope: PASS. Ограничено `SC-0006-003`.

## 19. Role-Based Review
| Role | Finding | Resolution |
| --- | --- | --- |
| Product / STORM | Scenario остается на русском, AC не заменяется Gherkin wording | Учтено |
| QA / BDD | Нужно доказать `Scenario -> Test -> Step Definition -> UI/ViewModel Code` | Новый `TS-0050` + `SD-0095..SD-0098` + UI contract |
| Desktop UI | UI behavior требует headless evidence | Новый contract открывает `MainControl` и проверяет controls/presentation |
| .NET maintainer | Не создавать зависимость от старых test methods | Новый helper повторно использует behavior route без вызова старых tests |

## 20. EXEC authorization
Текущий workflow продолжения `/storm:cover` уже находится в approved SPEC -> EXEC режиме. EXEC можно выполнять без дополнительного изменения scope.
