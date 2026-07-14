# STORM SC-0006-002: executable BDD для RepeaterPattern

## 0. Метаданные
- Тип (профиль): delivery-task, `/storm:cover`, `/storm:bdd-implement`, `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `storm-bootstrap`
- Ограничения: не менять product behavior; не менять `.feature` wording; не менять existing test annotations; не менять production code; продуктовые артефакты вести на русском
- Связанные ссылки: `ST-0006`, `AC-0016`, `GR-017`, `SC-0006-002`, `TS-0013`, `RepeaterPattern`, `RepeaterPatternExtensions`, `RepeaterPatternViewModel`, `TaskItemViewModel.Repeaters`

Если секция не применима, это указано явно в соответствующей секции.

## 1. Overview / Цель
Добавить executable BDD layer для `SC-0006-002`: `RepeaterPattern` поддерживает `none`, `daily`, `weekly`, `monthly`, `yearly` и `after-complete` режим.

Outcome contract:
- Success means: `SC-0006-002` получает новый executable BDD test `TS-0049`, step definitions `SD-0091..SD-0094`, passing evidence, а executable specification ratio увеличивается с 23/45 до 24/45.
- Итоговый артефакт / output: test-only executable spec + обновленные `storm.json` и reports.
- Stop rules: остановиться, если нужны изменения production behavior, `.feature` wording, UI layout/automation IDs, existing annotations или новое продуктовое решение по semantics повторений.

## 2. Текущее состояние (AS-IS)
- `SC-0006-002` связан с `AC-0016`, `GR-017`, `TS-0013`, status = `automated`, `step_definitions = []`.
- Existing evidence уже подтверждает части поведения:
  - `TaskItemRepeaterListMarkerTests` проверяет маркер активного повторения и уведомления `TaskItemViewModel`.
  - `MainControlTaskCardLayoutUiTests.CurrentTaskCard_DesktopRepeaterLayout_UsesCompactControls` проверяет UI карточки задачи с weekly/workdays repeater controls.
  - `TaskStatusTransitionTests.HandleTaskStatusChange_CompletedTaskWithRepeater_CreatesPreparedClone` проверяет clone flow для completed task с repeater.
- Domain/ViewModel contracts:
  - `RepeaterType` содержит `None`, `Daily`, `Weekly`, `Monthly`, `Yearly`.
  - `RepeaterPatternExtensions.GetNextOccurrence` рассчитывает следующее появление для domain model.
  - `RepeaterPatternViewModel.GetNextOccurrence`, `Model`, `SelectedRepeaterType`, `WorkDays`, `AfterComplete`, `Title` отражают UI-facing semantics.
  - `TaskItemViewModel.Repeaters` отдаёт пользовательский набор вариантов: none, daily, weekly workdays, weekly, monthly, yearly.
- Worktree чистый после коммита `75eb18e`.

## 3. Проблема
Для `SC-0006-002` нет исполняемой связи `Scenario -> Test -> Step Definition -> Domain/ViewModel code`, поэтому `/storm:cover` не может считать поддержку повторений закрытой на BDD layer.

## 4. Цели дизайна
- Проверить actual domain/viewmodel semantics, не меняя UI и production code.
- Проверить все пять типов `RepeaterType`.
- Проверить weekly pattern с выбранными weekdays и workdays aggregate.
- Проверить `after-complete` как отдельный supported mode без фиксации хрупкого absolute now assertion.
- Сохранить existing tests и annotations без изменений.
- Сохранить existing `TS-0013` link и добавить новый bridge test.

## 5. Non-Goals (чего НЕ делаем)
- Не менять алгоритм `GetNextOccurrence`.
- Не менять labels, localization, selectors, automation IDs или layout.
- Не добавлять video artifacts в репозиторий.
- Не расширять этот slice на `SC-0006-003` wanted/importance.
- Не запускать `/storm:full-cycle`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/RepeaterPatternScenarioContract.cs` -> test-only contract для domain/viewmodel behavior.
- `src/Unlimotion.Test/StormBdd/RepeaterPatternStepDefinitions.cs` -> `SD-0091..SD-0094`, binding product wording к contract.
- `src/Unlimotion.Test/StormTaskPlanningRepeaterExecutableSpecTests.cs` -> `TS-0049`, парсит existing `.feature` scenario и запускает шаги.
- `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` -> test-only result fields для передачи evidence между steps.
- `docs/product/storm.json` и `docs/product/reports/*` -> `/storm:bdd-sync`, `/storm:bdd-lint`, behavior metrics.

### 6.2 Детальный дизайн
- Contract checks:
  1. Проверить `RepeaterTypeOption.Definitions` содержит `None`, `Daily`, `Weekly`, `Monthly`, `Yearly` в UI-facing options.
  2. Проверить `TaskItemViewModel.Repeaters` содержит none, daily, weekly workdays, weekly, monthly, yearly.
  3. Проверить `RepeaterPatternViewModel.Model` round-trip сохраняет `Type`, `Period`, `AfterComplete` и weekday pattern.
  4. Проверить `GetNextOccurrence` для `None`, `Daily`, `Weekly` без pattern, `Weekly` с pattern, `Monthly`, `Yearly`.
  5. Проверить `AfterComplete = true` поддерживается как mode и рассчитывает occurrence от текущего календарного дня с допустимым интервалом между `before` и `after`.
  6. Проверить active repeater marker через существующий `TaskItemViewModel` contract без dependency на существующие tests.
- UI-facing evidence: existing preserved UI tests остаются связанными через `TS-0013` и targeted layout test запускается как regression evidence.
- UI video evidence: `Не применимо` для этой SPEC, потому что implementation не меняет UI behavior; fallback evidence: preserved UI test run для repeater layout.
- Ошибки: если contract не подтверждает existing behavior, EXEC останавливается; production code не исправляется внутри этой spec.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Repeater pattern support | Пользователь выбирает режим повторения задачи и настраивает период / дни недели / after-complete | Модель задачи хранит режим, карточка может показать активное повторение, следующий occurrence считается по выбранному режиму | `StormTaskPlanningRepeaterExecutableSpecTests`, preserved repeater UI tests | `AC-0016` |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Repeater отсутствует или `None` | Проверка marker/occurrence | Нет активного marker; next occurrence равен base date | Null repeater не считается active | Preserved user-facing semantics |
| `Daily`, `Weekly`, `Monthly`, `Yearly` | Расчёт next occurrence | Date advances на period по типу | Period берётся из model/viewmodel | Domain и viewmodel должны совпадать по core semantics |
| Weekly с weekday pattern | Расчёт от понедельника | Следующий выбранный weekday выбирается внутри недели | Empty pattern fallback: `7 * period` days | Проверяется конкретный deterministic base date |
| `AfterComplete = true` | Расчёт next occurrence | Base date заменяется текущим календарным днём, затем применяется type/period | Absolute date не фиксируется, используется before/after window | Снижает хрупкость теста |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Не менять `.feature` wording | agent | Использовать existing `SC-0006-002` text как canonical wording | 0.95 | Низкий | Нет |
| Проверять domain/viewmodel contract, а не UI flow | agent | Новый bridge helper без UI mutation | 0.84 | Средний: меньше end-to-end UI, но scenario про semantics RepeaterPattern | Нет |
| Сохранять UI evidence через existing repeater UI tests | agent | Запустить targeted preserved layout test | 0.86 | Низкий | Нет |
| Проверять after-complete через date window | agent | Учитывать текущую дату до/после вызова | 0.78 | Средний: DateTimeOffset.Now остаётся implicit dependency в product code | Нет |

## 7. Бизнес-правила / Алгоритмы
- `None` не создаёт активное повторение и не сдвигает дату.
- `Daily`, `Weekly`, `Monthly`, `Yearly` сдвигают дату на `Period`.
- Weekly pattern выбирает следующий включенный день недели; без pattern используется период в неделях.
- `AfterComplete` переключает базу расчёта на текущий календарный день.
- `TaskItemViewModel.Repeaters` должен давать пользователю варианты для всех supported types, включая workdays shortcut для weekly.

## 8. Точки интеграции и триггеры
- `StormFeatureParser.ParseScenario(..., "SC-0006-002")`.
- `StormScenarioRunner` executes four feature steps.
- Domain contracts: `RepeaterPattern`, `RepeaterType`, `RepeaterPatternExtensions`.
- ViewModel contracts: `RepeaterPatternViewModel`, `RepeaterTypeOption`, `TaskItemViewModel.Repeaters`, active repeater marker.

## 9. Изменения модели данных / состояния
Production state не меняется. Test-only contract создаёт in-memory domain/viewmodel objects.

## 10. Миграция / Rollout / Rollback
Migration не требуется. Rollback: удалить `TS-0049`, `SD-0091..SD-0094`, `RepeaterPatternScenarioContract` и откатить artifact links/metrics.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `SC-0006-002` исполняется через repo-local steps.
  - Tags `@scenario:SC-0006-002`, `@story:ST-0006`, `@test:TS-0013` проверены.
  - Contract подтверждает support для none/daily/weekly/monthly/yearly и after-complete mode.
  - `storm.json` и reports синхронизированы: scenario status `passing`, linked test `TS-0049`, step definitions `SD-0091..SD-0094`, executable ratio 24/45.
- Какие тесты добавить/изменить: добавить `StormTaskPlanningRepeaterExecutableSpecTests`, `RepeaterPatternStepDefinitions`, `RepeaterPatternScenarioContract`; не менять existing tests/annotations.
- Characterization tests / contract checks: target bridge проверяет existing behavior; preserved UI tests запускаются как regression evidence.
- Visual acceptance: UI layout не меняется; preserved repeater layout test служит UI-facing regression evidence.
- UI video evidence: не применимо, так как UI behavior/layout не меняются; fallback evidence фиксируется в reports.
- Команды для проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskPlanningRepeaterExecutableSpecTests/*" --output Detailed`
  - `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/TaskItemRepeaterListMarkerTests/*|/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCard_DesktopRepeaterLayout_UsesCompactControls|/*/*/TaskStatusTransitionTests/HandleTaskStatusChange_CompletedTaskWithRepeater_CreatesPreparedClone" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - full `dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --output Detailed`
- Stop rules для validation loops: если full suite падает на unrelated Avalonia.Headless flake, изолировать failing test, запустить controlled retry и зафиксировать evidence; если падает новый BDD contract, исправить или остановиться, если требуется behavior change.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| `AC-0016`: RepeaterPattern поддерживает none/daily/weekly/monthly/yearly и after-complete режим | `StormTaskPlanningRepeaterExecutableSpecTests`, preserved repeater tests | `storm.json` scenario/test/step links | TUnit output, STORM validator output, reports | Не применимо |

## 12. Риски и edge cases
- Риск: `AfterComplete` использует `DateTimeOffset.Now`, что может сделать assertion хрупким. Смягчение: проверять результат относительно `before.Date` и `after.Date`.
- Риск: domain extension и viewmodel method частично дублируют algorithm. Смягчение: contract проверяет оба пути на representative cases.
- Риск: UI override требует UI evidence для UI-facing behavior. Смягчение: production UI не меняется, но preserved repeater UI test запускается как regression gate.
- Риск: существующий weekly algorithm имеет edge cases за пределами story. Смягчение: SPEC покрывает declared support, не расширяет behavior.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| "Это слишком внутренний тест, а не пользовательский BDD" | Scenario описывает пользовательский выбор repeat mode | Contract проверяет UI-facing options и marker, а existing UI repeater test сохраняется как evidence | mitigated |
| "Не трогай существующие тесты/аннотации" | Пользователь задавал это ограничение | Existing tests не изменяются; добавляется новый BDD bridge и artifact links | mitigated |
| "After-complete зависит от текущей даты" | Product code использует `DateTimeOffset.Now.Date` | Assertion через before/after window, без фиксации absolute date | mitigated |

### Rework Prevention Checklist
- Does the spec name what the user will see or operate? Да: выбор режима повторения в карточке задачи и marker активного повторения.
- Does every user-visible scenario have evidence? Да: BDD contract + preserved UI repeater tests + STORM validator.
- Did the agent list decisions it assumed? Да: Decision Ledger.
- Did the agent predict likely objections and mitigate them? Да.
- Did role-based review run for the relevant task type? Да, см. секцию 19.
- Are acceptance criteria verifiers, not preparation steps? Да.
- Does EXEC have a path to prove the scenarios before final? Да.

## 13. План выполнения
1. Создать SPEC и post-SPEC review.
2. Добавить test-only contract, context fields, step definitions, executable spec.
3. Обновить STORM artifacts/reports.
4. Запустить targeted BDD/domain/UI checks, STORM validator, full suite or controlled retry.
5. Post-EXEC review и commit.

## 14. Открытые вопросы
Нет блокирующих.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, `testing-dotnet`, `dotnet-desktop-client`
- Выполненные требования профиля: QUEST gate, Scenario -> Test -> Step Definition -> Domain/ViewModel code, TUnit `--treenode-filter`, preserved UI evidence, product artifacts на русском.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/RepeaterPatternScenarioContract.cs` | Новый contract helper | Проверить `AC-0016` RepeaterPattern semantics |
| `src/Unlimotion.Test/StormBdd/RepeaterPatternStepDefinitions.cs` | Новый step definition набор | Исполнить `SC-0006-002` |
| `src/Unlimotion.Test/StormTaskPlanningRepeaterExecutableSpecTests.cs` | Новый executable BDD test | Связать scenario с steps |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-only context/result fields | Передать evidence между steps |
| `docs/product/storm.json`, `docs/product/reports/*` | Links/metrics/reports | `/storm:bdd-sync`, `/storm:bdd-lint` |

## 17. Альтернативы
- UI-only test через `MainControl`: отклонено для этой итерации, потому что scenario про core RepeaterPattern semantics, а existing UI layout tests уже покрывают карточку.
- Расширить existing tests annotations: отклонено пользователем; annotations не меняем без отдельного подтверждения.
- Изменить `.feature` wording на более конкретный Gherkin: отклонено, artifact-only wording уже утвержден и не должен меняться.

## 18. SPEC review
- Coverage: PASS. SPEC покрывает scenario, evidence, artifacts, validation и rollback.
- Feasibility: PASS. Все проверки можно выполнить test-only без product changes.
- Risk: PASS with notes. Основной риск `AfterComplete`/current date закрыт window assertion.
- Scope: PASS. `SC-0006-003` и unrelated STORM gaps явно исключены.

## 19. Role-Based Review
| Role | Finding | Resolution |
| --- | --- | --- |
| Product / STORM | Scenario остается на русском, AC не заменяется Gherkin wording | Учтено |
| QA / BDD | Нужно доказать `Scenario -> Test -> Step Definition -> Code Unit` | Новый `TS-0049` + `SD-0091..SD-0094` + contract |
| Desktop UI | UI behavior не меняется, но UI-facing route должен иметь evidence | Targeted preserved repeater UI test в validation |
| .NET maintainer | Не плодить flaky assertions на текущую дату | Проверка через before/after calendar window |

## 20. EXEC authorization
Пользователь подтвердил SPEC переходом `спеку подтверждаю`. EXEC можно выполнять без дополнительного изменения scope.
