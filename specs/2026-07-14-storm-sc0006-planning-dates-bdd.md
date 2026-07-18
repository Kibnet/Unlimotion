# STORM SC-0006-001: executable BDD для плановых дат и быстрых контролов

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:cover`, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `storm-bootstrap`
- Ограничения: не менять product behavior; не менять `.feature` wording; не менять existing test annotations; не менять production code; продуктовые артефакты вести на русском
- Связанные ссылки: `ST-0006`, `AC-0016`, `GR-016`, `SC-0006-001`, `TS-0005`, `TS-0013`, `MainControl`, `TaskItemViewModel`, `DateCommands`, `SetDurationCommands`

Если секция не применима, это указано явно в соответствующей секции.

## 1. Overview / Цель
Добавить executable BDD layer для `SC-0006-001`: задачи поддерживают planned begin/end/duration и быстрые контролы дедлайна.

Outcome contract:
- Success means: `SC-0006-001` получает новый executable BDD test `TS-0048`, step definitions `SD-0087..SD-0090`, passing UI/headless evidence, а executable specification ratio увеличивается с 22/45 до 23/45.
- Итоговый артефакт / output: test-only executable spec + обновленные `storm.json` и reports.
- Stop rules: остановиться, если нужны изменения production behavior, `.feature` wording, UI layout/automation IDs, existing annotations или новое продуктовое решение по semantics плановых дат.

## 2. Текущее состояние (AS-IS)
- `SC-0006-001` связан с `AC-0016`, `GR-016`, `TS-0005`, `TS-0013`, status = `automated`, `step_definitions = []`.
- Existing evidence уже подтверждает части поведения:
  - `MainControlTaskCardLayoutUiTests` проверяет наличие planning controls и layout constraints.
  - `MainControlDateQuickSelectionUiTests` проверяет localized quick date menu labels.
  - `MainControlNewTaskDeadlineUiTests` проверяет date pickers, duration editor и поведение при создании новых задач.
- UI controls имеют стабильные automation IDs: `CurrentTaskPlannedBeginPicker`, `CurrentTaskSetBeginButton`, `CurrentTaskPlannedDurationTextBox`, `CurrentTaskSetDurationButton`, `CurrentTaskPlannedEndPicker`, `CurrentTaskSetEndButton`.
- ViewModel contracts: `TaskItemViewModel.PlannedBeginDateTime`, `PlannedEndDateTime`, `PlannedDuration`, `DateCommands`, `SetDurationCommands`.
- Worktree чистый после коммита `4bf9958`.

## 3. Проблема
Для `SC-0006-001` нет исполняемой связи `Scenario -> Test -> Step Definition -> UI/ViewModel code`, поэтому `/storm:cover` не может считать плановые даты и быстрые deadline controls закрытыми на BDD layer.

## 4. Цели дизайна
- Проверить actual `MainControl` task card UI через Avalonia.Headless.
- Проверить, что planning controls bind-ятся к текущей задаче.
- Выполнить быстрые команды begin/end/duration через существующие UI-bound menu commands.
- Сохранить existing tests и annotations без изменений.
- Зафиксировать fallback для video evidence: текущий Avalonia.Headless/TUnit runner не производит безопасный video artifact.

## 5. Non-Goals (чего НЕ делаем)
- Не менять правила вычисления дат или duration.
- Не менять labels, localization, selectors, automation IDs или layout.
- Не добавлять video artifacts в репозиторий.
- Не расширять этот slice на `SC-0006-002` repeater или `SC-0006-003` wanted/importance.
- Не запускать `/storm:full-cycle`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/TaskPlanningDatesUiContract.cs` -> test-only Avalonia.Headless flow для открытия `MainControl`, поиска planning controls и выполнения быстрых date/duration commands.
- `src/Unlimotion.Test/StormBdd/TaskPlanningDatesStepDefinitions.cs` -> `SD-0087..SD-0090`, binding product wording к UI contract.
- `src/Unlimotion.Test/StormTaskPlanningDatesExecutableSpecTests.cs` -> `TS-0048`, парсит existing `.feature` scenario и запускает шаги.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> test-only result fields для передачи evidence между steps.
- `docs/product/storm.json` и `docs/product/reports/*` -> `/storm:bdd-sync`, `/storm:bdd-lint`, behavior metrics.

### 6.2 Детальный дизайн
- UI flow:
  1. Открыть `MainControl` с `MainWindowViewModelFixture` в `HeadlessUnitTestSession`.
  2. Включить `AllTasksMode`, открыть details pane, выбрать `RootTask2`.
  3. Найти begin/end `CalendarDatePicker`, duration `TextBox`, begin/end/duration `DropDownButton`.
  4. Проверить, что controls привязаны к `CurrentTaskItem`.
  5. Через flyout/menu command выполнить begin quick action `Tomorrow` и проверить `PlannedBeginDateTime`.
  6. Через duration quick action выполнить `TwoHours` и проверить `PlannedDuration`.
  7. Через end quick action выполнить `FiveDays` и проверить, что `PlannedEndDateTime` относительно begin выставлен на `begin + 4 days`.
  8. Выполнить `None` для begin/end/duration и проверить очистку.
- Visual planning artifact для UI-facing изменений: текстовая fallback-схема, потому что layout не меняется:
  - `CurrentTaskItem -> CalendarDatePicker.SelectedDate`
  - `CurrentTaskItem -> TextBox LostFocusUpdateBindingBehavior.Text`
  - `DropDownButton.Flyout.MenuItem.Command -> DateCommands / SetDurationCommands -> TaskItemViewModel planning fields`
- UI test video evidence: `Не применимо` как обязательный артефакт в репозитории. Объективная причина: Avalonia.Headless/TUnit runner в текущем проекте не сохраняет безопасное видео. Fallback evidence: targeted headless BDD test, preserved existing UI tests, STORM validator, full `Unlimotion.Test` gate or controlled retry with isolated flake proof.
- Ошибки: если control/command не найден, BDD contract падает с assertion/exception; production code не исправляется внутри этой spec.
- Производительность: не применимо; test-only slice.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Planning quick controls | Пользователь открывает карточку задачи и выбирает быстрые begin/end/duration actions | В карточке текущей задачи обновляются плановое начало, окончание и длительность; `None` очищает поля | `StormTaskPlanningDatesExecutableSpecTests`, preserved planning UI tests | `AC-0016` |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Current task без обязательного planning state | Begin `Tomorrow` quick action | `PlannedBeginDateTime = DateEx.Tomorrow` | Если command недоступна, test fails и EXEC останавливается | Проверяется через UI-bound menu command |
| Begin уже задан | End `FiveDays` quick action | `PlannedEndDateTime = begin + 4 days` | Relative command требует begin | Сохраняет existing `DateCommands` semantics |
| Duration пустая или задана | Duration `TwoHours` quick action | `PlannedDuration = 2h` | `None` очищает только когда есть duration | Сохраняет existing `SetDurationCommands` semantics |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Не менять `.feature` wording | agent | Использовать existing `SC-0006-001` text как canonical wording | 0.95 | Низкий, пользователь ранее запретил менять wording без отдельного подтверждения | Нет |
| Проверять UI-bound commands, а не вызывать старые tests | agent | Новый bridge helper без test-to-test dependency | 0.88 | Средний, helper дублирует часть setup; снижает coupling | Нет |
| Video evidence fallback | agent | Не коммитить видео; использовать headless output/full suite | 0.82 | Низкий, runner не сохраняет видео | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
Не применимо: нет runtime/config/storage schema changes. Test-only fixture меняет временные planning fields в тестовом storage и очищает их через `CleanTasks()`.

## 7. Бизнес-правила / Алгоритмы
- Begin quick actions задают `PlannedBeginDateTime`.
- End quick actions задают `PlannedEndDateTime`; relative actions используют уже выбранный begin.
- Duration quick actions задают `PlannedDuration`.
- `None` actions очищают соответствующее поле, когда значение есть.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario(..., "SC-0006-001")`.
- `StormScenarioRunner` executes four feature steps.
- UI contracts: `MainControl`, `CurrentTaskPlanningSection`, planning automation IDs.
- ViewModel contracts: `TaskItemViewModel.Commands`, `TaskItemViewModel.SetDurationCommands`, planning fields.

## 9. Изменения модели данных / состояния
Production state не меняется. Test-only fixture меняет плановые поля тестовой задачи и очищает данные через `CleanTasks()`.

## 10. Миграция / Rollout / Rollback
Migration не требуется. Rollback: удалить `TS-0048`, `SD-0087..SD-0090`, `TaskPlanningDatesUiContract` и откатить artifact links/metrics.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `SC-0006-001` исполняется через repo-local steps.
  - Tags `@scenario:SC-0006-001`, `@story:ST-0006`, `@test:TS-0005`, `@test:TS-0013` проверены.
  - Planning controls bind-ятся к текущей задаче и quick actions обновляют begin/end/duration.
  - `storm.json` и reports синхронизированы: scenario status `passing`, linked test `TS-0048`, step definitions `SD-0087..SD-0090`, executable ratio 23/45.
- Какие тесты добавить/изменить: добавить `StormTaskPlanningDatesExecutableSpecTests`, `TaskPlanningDatesStepDefinitions`, `TaskPlanningDatesUiContract`; не менять existing tests/annotations.
- Characterization tests / contract checks: target bridge проверяет existing behavior; preserved UI tests запускаются как regression evidence.
- Visual acceptance: layout не меняется; fallback state-map в 6.2 должен соответствовать actual UI binding route.
- UI video evidence: fallback по причине отсутствия безопасной video support в runner; evidence commands фиксируются в post-EXEC.
- Команды для проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskPlanningDatesExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlDateQuickSelectionUiTests/*|/*/*/MainControlNewTaskDeadlineUiTests/*|/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
- Stop rules для validation loops: если full suite падает на unrelated Avalonia.Headless flake, изолировать failing test, запустить controlled retry и зафиксировать evidence; если падает новый BDD/UI contract, исправить или остановиться, если требуется behavior change.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| `AC-0016`: Задачи поддерживают planned begin/end/duration и быстрые контролы дедлайна | `StormTaskPlanningDatesExecutableSpecTests`, preserved planning UI tests | `storm.json` scenario/test/step links | TUnit output, STORM validator output, reports | Не применимо |

## 12. Риски и edge cases
- Риск: quick command availability зависит от текущего begin/end/duration state. Смягчение: сценарий задаёт значения в порядке begin -> duration -> end -> none.
- Риск: controls не материализуются без открытой details pane. Смягчение: `DetailsAreOpen = true`, большой headless window, layout jobs.
- Риск: date assertions зависят от текущей даты. Смягчение: вычислять expected values через `DateEx`/`DateTime.Today` в момент выполнения.
- Риск: video evidence requirement. Смягчение: явно зафиксирован fallback и next-best evidence.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| "Это слишком внутренний тест, а не пользовательский BDD" | Проверка использует ViewModel fields | Bridge открывает actual `MainControl`, ищет UI controls и выполняет commands из flyout/menu bindings | mitigated |
| "Не трогай существующие тесты/аннотации" | Пользователь задавал это ограничение | Existing tests не изменяются; добавляется новый BDD bridge и artifact links | mitigated |
| "Где видео для UI?" | UI profile требует video evidence при поддержке runner | Зафиксирован объективный fallback: Avalonia.Headless/TUnit runner не сохраняет безопасное видео | accepted-risk |

### Rework Prevention Checklist
- Does the spec name what the user will see or operate? Да: карточка задачи, planning controls, quick actions.
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
- Выполненные требования профиля: QUEST gate, Scenario -> Test -> Step Definition -> UI/ViewModel code, TUnit `--treenode-filter`, UI/headless evidence, visual artifact fallback, product artifacts на русском.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/TaskPlanningDatesUiContract.cs` | Новый UI contract helper | Проверить `AC-0016` через Avalonia.Headless |
| `src/Unlimotion.Test/StormBdd/TaskPlanningDatesStepDefinitions.cs` | Новый step definition набор | Исполнить `SC-0006-001` |
| `src/Unlimotion.Test/StormTaskPlanningDatesExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result fields | Передать evidence между steps |
| `docs/product/storm.json`, `docs/product/reports/*` | Links/metrics/reports | `/storm:bdd-sync`, `/storm:bdd-lint` |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0006-001` | `automated`, no steps | `passing`, `TS-0048`, `SD-0087..SD-0090` |
| `ST-0006` executable coverage | 0/3 | 1/3 |
| Step-executable scenarios | 22/45 | 23/45 |
| Product behavior | Existing planning controls | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: вызвать existing UI test methods из BDD step. Плюсы: меньше кода. Минусы: test-to-test dependency и слабая трассируемость steps. Почему не выбран: новый bridge лучше соответствует `Scenario -> Test -> Step Definition -> UI`.
- Вариант: использовать только ViewModel commands без UI. Плюсы: стабильнее. Минусы: не проверяет UI quick controls. Почему не выбран: scenario связан с UI-facing planning controls.
- Вариант: покрыть `SC-0006-002` и `SC-0006-003` в одном slice. Плюсы: быстрее закрыть ST-0006. Минусы: больший blast radius. Почему не выбран: текущая итерация закрывает один scenario и сохраняет review/validation управляемыми.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals есть |
| B. Качество дизайна | 6-10 | PASS | UI contract, integration points, state-map, rollback and data impact described |
| C. Безопасность изменений | 11-13 | PASS | Test-only, без product behavior/schema/UI layout changes |
| D. Проверяемость | 14-16 | PASS | AC, commands, acceptance-to-test matrix and video fallback defined |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет open questions; small slice; alternatives reviewed |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/UI/TUnit requirements reflected |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один scenario и явные non-goals |
| 2. Понимание текущего состояния | 5 | Existing artifacts, controls, ViewModel commands and tests указаны |
| 3. Конкретность целевого дизайна | 5 | IDs/files/checks заданы |
| 4. Безопасность (миграция, откат) | 5 | Test-only, rollback перечислен |
| 5. Тестируемость | 5 | Headless UI, targeted BDD, preserved evidence, STORM validator, full suite |
| 6. Готовность к автономной реализации | 5 | Нет blockers; fallback evidence определен |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does planning quick-control behavior match the product story? | PASS | Нет |
| UX / designer | applicable | Would visible planning controls and state handling pass review? | PASS | Нет; layout не меняется, fallback state-map задан |
| Tester / validation | applicable | Does every AC map to test/check/evidence? | PASS | Нет |
| Developer / architect | applicable | Are boundaries, data contracts and maintainability coherent? | PASS | Нет |
| Delivery / operations / security | not applicable | No deploy/config/secrets/runtime access changes | PASS | Нет |

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-07-14-storm-sc0006-planning-dates-bdd.md`, central stack (`AGENTS.md`, routing matrix, model baseline, QUEST, testing, STORM, UI automation), локальный `AGENTS.override.md`, `ST-0006`, `AC-0016`, `GR-016`, `SC-0006-001`, planned changed files.
- Decision: можно выполнять; пользователь подтвердил SPEC и active goal задаёт auto approval.
- Review passes:
  - Scope/Evidence pass: проверены `storm.json`, feature file, `MainControl.axaml`, `DateCommands`, `SetDurationCommands`, `TaskItemViewModel`, existing planning UI tests.
  - Contract pass: spec не меняет behavior, acceptance criteria, `.feature`, annotations или selectors; UI evidence и fallback предусмотрены.
  - Adversarial risk pass: проверены risks current-date assertions, command availability, hidden layout dependency, video fallback, test-to-test dependency.
  - Role-Based pass: BA, UX, Tester, Developer применимы и PASS; Delivery не применим к коду/артефактам без config/deploy.
  - Re-review after fixes / Fix and re-review: не требовалось; в черновике нет BLOCKER/HIGH findings.
  - Stop decision: PASS.
- Evidence inspected: clean worktree after commit `4bf9958`, `features/storm/st-0006-calendar-planning.feature`, `docs/product/storm.json`, `MainControl.axaml` planning section, `DateCommands`, `SetDurationCommands`, `TaskItemViewModel`, existing linked tests.
- Depth checklist:
  - Scope drift / unrelated changes: ограничено one-scenario test/artifact slice.
  - Acceptance criteria: `AC-0016` mapped to new BDD bridge and preserved UI tests.
  - User-observable scenarios / Decision ledger / Expected objections: заполнены.
  - Validation evidence: commands specified; actual evidence будет в post-EXEC.
  - Unsupported claims: no video claim; fallback explicitly stated.
  - Regression / edge case: command availability and current-date risks mitigated.
  - Comments/docs/changelog: no comments/changelog planned.
  - Hidden contract change: none planned; no production/API changes.
  - Manual-review challenge: reviewer would check whether UI quick controls are actually exercised, not only ViewModel fields; spec requires MenuFlyout command execution from actual `MainControl`.
- No-findings justification: small test-only BDD slice follows existing STORM BDD pattern and has explicit validation plan.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Video evidence не создается текущим runner | Зафиксировать fallback и использовать targeted headless/full-suite evidence | accepted-risk |

- Fixed before continuing: Не требовалось.
- Checks rerun: SPEC linter/rubric/review performed manually against central documents.
- Needs human: Нет.
- Residual risks / follow-ups: после этого slice `ST-0006` будет 1/3 step-executable; `SC-0006-002` и `SC-0006-003` остаются отдельными gaps.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, changed test/artifact files, `TaskPlanningDatesUiContract`, `TaskPlanningDatesStepDefinitions`, `StormTaskPlanningDatesExecutableSpecTests`, `StormStepDefinition`, `storm.json`, reports and validation evidence.
- Decision: можно коммитить.
- Review passes:
  - Scope/Evidence pass: изменения ограничены test-only BDD bridge, SPEC and STORM artifacts.
  - Contract pass: `SC-0006-001` получил `TS-0048` and `SD-0087..SD-0090`; production code, `.feature`, automation IDs and existing annotations unchanged.
  - Adversarial risk pass: full suite выявил instability в новом cleanup order; fix применён в helper and revalidated.
  - Role-Based pass: BA/UX/Tester/Developer relevant checks PASS; Delivery risk только sandbox ACL, isolated and escalated.
  - Re-review after fixes / Fix and re-review: после fix `IsInitializedProvider` suppression and begin-before-end cleanup повторены build, targeted BDD and controlled full retry.
  - Stop decision: PASS.
- Evidence inspected:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false` => passed, existing warnings.
  - `StormTaskPlanningDatesExecutableSpecTests` => passed 1/1; after stability fix passed 1/1.
  - `MainControlDateQuickSelectionUiTests` => passed 1/1.
  - `MainControlNewTaskDeadlineUiTests` => passed 9/9.
  - `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` => passed 1/1.
  - Initial sandbox full suite => 577/579 due sandbox ACL inherited rule and unrelated Avalonia.Headless DisposeAsync NRE.
  - Isolated `InProgressTree_DisplaysStartedDateTimeInLocalTime` => passed 1/1.
  - Isolated ACL test => failed in sandbox, passed 1/1 escalated.
  - First escalated full retry => 578/579, exposed new `EndNoneActionWorked` order/autosave instability.
  - Controlled escalated full retry after fix => passed 579/579.
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` => OK: 0 errors, 9 warnings.
- Depth checklist:
  - Scope drift / unrelated changes: no production, feature, selector, annotation, project or workflow changes.
  - Acceptance criteria: `AC-0016` covered by UI-bound quick begin/end/duration command assertions.
  - User-observable scenarios / Acceptance-to-test matrix / Expected objections: BDD bridge and preserved UI gates cover the planned controls scenario; video fallback documented.
  - Validation evidence: targeted, preserved UI gates, STORM validator and full suite present.
  - Unsupported claims: no video artifact claimed; no product behavior change claimed.
  - Regression / edge case: date cleanup order/autosave risk found in full suite and fixed before commit.
  - Comments/docs/changelog: no code comments/changelog needed; product reports updated.
  - Hidden contract change: none; helper uses existing `MainControl` controls and commands.
  - Manual-review challenge: reviewer would ask whether this truly exercises UI quick controls; helper opens `MainControl`, finds automation IDs and executes MenuFlyout commands.
- No-findings justification: after the stability finding was fixed, implementation follows existing STORM BDD/UI patterns and validates through actual Avalonia.Headless UI command routes.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | tests | Full suite exposed order/autosave instability in new `EndNoneActionWorked` check | Suppress autosave for transient planning mutations and clear begin before end | fixed |
| LOW | environment | Sandbox ACL invalidates Windows private-key hardening test | Isolate failure and run ACL/full suite outside sandbox | resolved |
| LOW | evidence | Video artifact not produced by current Avalonia.Headless runner | Preserve explicit fallback and use targeted/full-suite evidence | accepted-risk |

- Fixed before final report: `TaskPlanningDatesUiContract` now suppresses autosave during transient UI mutations and clears begin before end.
- Checks rerun: build, targeted BDD, preserved planning UI gates, STORM validator, isolated failures and controlled full retry.
- Validation evidence: listed above.
- Unrelated changes: none observed in task scope.
- Needs human: Нет.
- Residual risks / follow-ups: `ST-0006` remains 1/3 step-executable; continue with `SC-0006-002` or `SC-0006-003`.

## Approval
Подтверждено пользователем и active goal: SPEC auto-approved for execution.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | `/storm:bdd-implement SC-0006-001` | 0.87 | Нет | Перейти к EXEC | Нет | Да, пользователь подтвердил SPEC / active goal auto approval | UI-facing scenario требует headless UI executable bridge без product-code changes | `specs/2026-07-14-storm-sc0006-planning-dates-bdd.md` |
| EXEC | executable BDD UI slice | 0.91 | Нет | Commit и перейти к следующему `/storm:cover` candidate | Нет | Нет | Targeted/full gates passed after test-only stability fix; `SC-0006-001` закрыт step-executable | `src/Unlimotion.Test/StormTaskPlanningDatesExecutableSpecTests.cs`, `src/Unlimotion.Test/StormBdd/TaskPlanningDatesStepDefinitions.cs`, `src/Unlimotion.Test/TaskPlanningDatesUiContract.cs`, `docs/product/storm.json`, `docs/product/reports/*` |
