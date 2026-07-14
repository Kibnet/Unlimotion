# STORM SC-0007-001: executable BDD для адаптивной карточки задачи

## 0. Метаданные

- Статус: `DONE` (post-SPEC и post-EXEC review `PASS` с зафиксированным residual validation risk)
- STORM scope: `ST-0007 / AC-0019 / GR-019 / SC-0007-001`
- Тип: `delivery-task`, test-only executable BDD implementation + artifact sync
- Область: `src/Unlimotion.Test`, `docs/product/storm.json`, `docs/product/reports/*`, `specs/*`
- Central stack: `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `storm-product-development`, `review-loops`
- Локальное ужесточение: для UI-facing slice добавить и запустить релевантные Avalonia.Headless UI-тесты.

## 1. Overview / Цель

Сделать `SC-0007-001` исполняемым из `features/storm/st-0007-task-card.feature`: один TUnit/Avalonia.Headless BDD-мост должен доказать, что текущая карточка задачи остаётся читаемой и управляемой в desktop и narrow layouts.

Критерий успеха: feature scenario проходит через четыре repo-local step definitions, новый `TS-0051` и актуальные STORM links; production behavior не меняется.

## 2. Текущее состояние (AS-IS)

- `SC-0007-001` ссылается на `TS-0005`, имеет статус `automated`, но `step_definitions: []` и поэтому не участвует в executable BDD coverage.
- `MainControlTaskCardLayoutUiTests.CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` проверяет desktop-карточку, секции, ключевые контролы и доступность добавления parent relation.
- `MainControlTaskCardLayoutUiTests.CurrentTaskCard_PhoneWidthLayout_DoesNotOverflowAndKeepsRelationEditorUsable` проверяет widths `360`, `390`, `430`, отсутствие горизонтального overflow и usable relation editor.
- `storm.json` показывает `25/45` step-executable scenarios и `98` step definitions; `ST-0007` пока не имеет executable scenario.

## 3. Проблема

Существующее UI evidence подтверждает поведение, но слой Gherkin не исполняется. Связь между текстом feature и указанными UI-инвариантами остаётся декларативной.

## 4. Цели дизайна

1. Исполнять ровно текст `SC-0007-001` без изменения `.feature` wording.
2. Проверять desktop и все закреплённые narrow widths `360/390/430` в независимом BDD UI contract.
3. Сохранить `TS-0005` и добавить отдельный `TS-0051` для traceability BDD bridge.
4. Актуализировать derived STORM reports и coverage metrics только после passing evidence.

## 5. Non-Goals (чего НЕ делаем)

- Не изменяем `src/Unlimotion/**`, XAML, production behavior, accessibility semantics, automation IDs, `.csproj`, CI/workflows или `.feature` files.
- Не редактируем существующие test annotations и не удаляем stories, scenarios, tests, conflicts или existing links.
- Не заменяем acceptance criteria на Gherkin и не расширяем scope на `SC-0007-002`/`SC-0007-003`.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `StormTaskCardLayoutExecutableSpecTests`: читает scenario из feature, проверяет tags и запускает runner.
- `TaskCardLayoutStepDefinitions`: сопоставляет четыре русских Gherkin steps с BDD contract.
- `TaskCardLayoutUiContract`: открывает existing `MainControl` in Avalonia.Headless и проверяет desktop/narrow UI facts без production mutations.
- `storm.json` и reports: фиксируют `SC-0007-001 -> TS-0051 -> SD-0099..SD-0102` и passing status.

### 6.2 Детальный дизайн

1. Добавить `TaskCardLayoutScenarioResult` с явными фактами: desktop controls/sections visible, desktop relation entry usable, narrow widths horizontally contained, narrow relation editor usable.
2. В contract открыть desktop layout `1400x900`; подтвердить `CurrentTaskCard`, `CurrentTaskHeader`, `CurrentTaskDescriptionSection`, `CurrentTaskPlanningSection`, `CurrentTaskRelationsSection`, `CurrentTaskCompletionCriteriaSection`, `CurrentTaskStatusHistorySection`, `CurrentTaskTitleTextBox`, `CurrentTaskDescriptionTextBox` и `CurrentTaskParentsRelationAddButton`, затем открыть parent relation entry.
3. В contract отдельно проверить widths `360x844`, `390x844`, `430x844`: `CurrentTaskCard`, title, description, parent add button и открытые `CurrentTaskParentsRelationAddInput`, `CurrentTaskParentsRelationSuggestions`, `CurrentTaskParentsRelationAddCancelButton`, `CurrentTaskParentsRelationAddConfirmButton` не выходят за `CurrentTaskDetailsScrollViewer`.
4. Добавить properties в `StormScenarioContext` и четыре definitions `SD-0099..SD-0102` с точным text scenario.
5. Добавить `TS-0051`, затем синхронизировать JSON/reports только по фактическому evidence: scenario `passing`, AC `full`, ожидаемые `26/45` executable/passing scenarios и `102` step definitions должны быть подтверждены validator и targeted BDD gate.

### 6.3 User-Observable Scenarios

| Сценарий | Проверяемый результат | Evidence |
| --- | --- | --- |
| Desktop | Пользователь видит основные секции, элементы редактирования и вход в parent relation editor. | Новый BDD contract + preserved `TS-0005`. |
| Narrow `360/390/430` | Карточка не имеет горизонтального overflow; title, key controls и relation editor остаются управляемыми. | Новый BDD contract + preserved phone UI test. |

Visual planning artifact: не применимо. Эта delivery-итерация не меняет layout, UI state, navigation или visual acceptance; fallback-схема выше фиксирует уже существующие desktop и narrow состояния, которые только связываются с Gherkin.

### 6.4 State / Interaction Matrix

| Состояние | Действие | Ожидаемый результат |
| --- | --- | --- |
| Desktop `1400x900` | Открыть selected task и parent relation editor | Секции/controls arranged, relation entry доступен. |
| Narrow `360/390/430x844` | Открыть selected task и parent relation editor | Нет horizontal overflow, редактирование relation остаётся в viewport. |

### 6.5 Decision Ledger

| Решение | Причина | Needs user before EXEC |
| --- | --- | --- |
| Test-only bridge, без UI refactor | Existing UI behavior уже покрыт и passing; gap только в executable BDD trace. | Нет |
| Проверять три narrow widths | Они уже служат стабильной границей existing regression contract. | Нет |
| Новый `TS-0051`, не переименование `TS-0005` | Сохраняет historical test inventory и делает BDD execution evidence однозначным. | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Контракт | Изменение | Совместимость |
| --- | --- | --- |
| Production runtime/data/config | Нет | Полная: runtime не затрагивается. |
| Feature text/automation IDs/annotations | Нет | Полная: existing contracts сохраняются. |
| STORM trace | Добавляются scenario/test/step links | Additive, backward compatible. |

## 7. Бизнес-правила / Алгоритмы

- `SC-0007-001` считается passing только после исполнения feature steps и всех UI facts в result.
- Ошибка одного narrow width не маскируется остальными widths.
- `TS-0005` остаётся existing evidence, `TS-0051` — новый executable BDD evidence.

## 8. Точки интеграции и триггеры

- `features/storm/st-0007-task-card.feature`
- `StormFeatureParser`, `StormScenarioRunner`, `StormScenarioContext`
- `MainControl`, `MainWindowViewModelFixture`, Avalonia.Headless
- `/storm:bdd-sync`, `/storm:bdd-lint`, `validate-artifacts.py`

## 9. Изменения модели данных / состояния

Только test-local `StormScenarioContext` и STORM artifact metadata. Persistent model, storage schema и production state не меняются.

## 10. Миграция / Rollout / Rollback

Миграция и rollout не требуются. Rollback — удалить только добавленный BDD test bridge и соответствующие additive artifact links одним commit revert.

## 11. Тестирование и критерии приёмки

1. `SC-0007-001` parsed from feature with exact tags, rule title and four steps.
2. `SD-0099..SD-0102` execute in order and report all UI facts passing.
3. Desktop evidence confirms visible/arranged task-card sections, key editing controls and parent relation entry.
4. Narrow evidence confirms all widths `360/390/430` avoid horizontal overflow and keep relation editor usable.
5. Existing `MainControlTaskCardLayoutUiTests` class remains green.
6. STORM validator has zero errors; warnings may only be documented intentional duplicate shared steps.

### Acceptance-to-Test Matrix

| Acceptance / scenario | Новый evidence | Preserved evidence |
| --- | --- | --- |
| `AC-0019 / SC-0007-001` desktop and narrow task card | `TS-0051`: `StormTaskCardLayoutExecutableSpecTests` + `TaskCardLayoutUiContract` | `TS-0005`: `MainControlTaskCardLayoutUiTests` |

Проверки:

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskCardLayoutExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

Full `Unlimotion.Test` gate запускается после targeted gates, если нет инфраструктурного блокера.

## 12. Риски и edge cases

- Headless layout может требовать явного arrange/details-pane setup; contract повторяет repository-proven setup и не меняет product code.
- Запуск UI tests одновременно может создать file lock/shared-state interference; gates выполняются последовательно.
- Новый shared `Дано`/story step текст ожидаемо добавит lint warning; это не изменяет feature semantics.

### Expected User Review Objections

| Возможное замечание | Ответ / предотвращение |
| --- | --- |
| «BDD test дублирует layout tests» | Bridge читает feature text и сохраняет explicit scenario-to-step trace; UI facts остаются независимыми contract assertions. |
| «Покрыта только одна узкая ширина» | Contract проверяет 360, 390 и 430 px. |
| «В срез попал UI refactor» | File scope и Non-Goals запрещают production/XAML changes; review проверит diff. |
| «Нужен visual evidence» | UI не меняется; explicit fallback — passing Avalonia.Headless geometry assertions и preserved UI suite. |

### Rework Prevention Checklist

- [x] Scenario text, rule, tags и existing tests прочитаны из current artifacts.
- [x] Необходимый UI behavior уже существует; выбран test-only path.
- [x] Каждый user-observable state привязан к executable/new или preserved test evidence.
- [x] Нет user-owned decision blocker.

## 13. План выполнения

1. Выполнить post-SPEC review и устранить однозначные findings в этом файле.
2. Добавить BDD test, contract, definitions и context fields.
3. Запустить build, targeted BDD/UI gates и full suite.
4. Обновить STORM artifacts/reports с фактическим evidence, выполнить validator и bdd lint/sync.
5. Выполнить post-EXEC review, исправить findings, повторно проверить и закоммитить.

## 14. Открытые вопросы

Нет. Existing feature wording и UI evidence однозначно определяют slice.

## 15. Соответствие профилю

`storm-product-development` требует сохранить AC и feature wording, связать scenario -> test -> steps и обновить behavior coverage. `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing` требуют Avalonia.Headless coverage и последовательные TUnit gates. Локальный `AGENTS.override.md` выполняется добавлением и запуском UI test coverage.

## 16. Таблица изменений файлов

| Файл | Изменение |
| --- | --- |
| `specs/2026-07-14-storm-sc0007-task-card-layout-bdd.md` | Эта SPEC, review и execution evidence. |
| `src/Unlimotion.Test/StormTaskCardLayoutExecutableSpecTests.cs` | Новый executable feature test. |
| `src/Unlimotion.Test/TaskCardLayoutUiContract.cs` | Новый headless UI contract. |
| `src/Unlimotion.Test/StormBdd/TaskCardLayoutStepDefinitions.cs` | `SD-0099..SD-0102`. |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Test-local context fields. |
| `docs/product/storm.json` | Additive trace and measured metrics. |
| `docs/product/reports/*.md` | Derived sync/lint/coverage/trace/ranking/stories reports. |

## 17. Таблица соответствий (было -> стало)

| Было | Станет |
| --- | --- |
| `SC-0007-001.step_definitions = []` | `SD-0099..SD-0102` |
| `SC-0007-001.status = automated` | `passing` только после passing test evidence |
| `SC-0007-001.linked_tests = [TS-0005]` | `[TS-0005, TS-0051]` |
| Executable coverage `25/45` | `26/45` |

## 18. Альтернативы и компромиссы

- Reuse existing `[Test]` methods directly: отклонено, потому что BDD contract должен содержать собственные явные UI facts, а не вызывать tests из tests.
- Изменить feature на более конкретные шаги: отклонено, потому что user ограничил сохранение existing feature wording.
- Проверять только 390 px: отклонено, потому что это ослабляет proven narrow regression boundary.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, design goals и Non-Goals определены для одного scenario. |
| B. Качество дизайна | 6-10 | PASS | Ответственности, state matrix, decision ledger и test-local contracts определены. |
| C. Безопасность изменений | 11-13 | PASS | Production/runtime/data contracts явно исключены; rollback — commit revert. |
| D. Проверяемость | 14-16 | PASS | Acceptance-to-Test Matrix, commands и exact UI invariants добавлены. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, file scope и stop rules определены; user-owned questions отсутствуют. |
| F. Соответствие профилю | 20 | PASS | STORM + .NET desktop + UI automation stack и local override отражены. |

Итог: ГОТОВО.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | --- | --- |
| 1. Ясность цели и границ | 5 | Один scenario, exact AC/rule, test-only limits и forbidden changes зафиксированы. |
| 2. Понимание текущего состояния | 5 | Current feature, `storm.json`, reports и existing desktop/phone tests inspected. |
| 3. Конкретность целевого дизайна | 5 | New test/contract/steps, IDs, widths и trace links указаны. |
| 4. Безопасность (миграция, откат) | 5 | Additive test/artifact-only change, no persistent migration, revert path defined. |
| 5. Тестируемость | 5 | Targeted BDD/UI gates, validator и full suite plan сопоставлены с AC. |
| 6. Готовность к автономной реализации | 5 | Нет открытых решений или user-owned blockers. |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result

| Роль | Статус | Проверка |
| --- | --- | --- |
| UX / designer | PASS | Layout не меняется; fallback visual plan и exact desktop/narrow geometry evidence определены. |
| Tester / validation | PASS | Feature parsing, four step definitions, three phone widths, preserved UI class и artifact validator обязательны. |
| Developer / architect | PASS | Новый code ограничен test assembly и additive `StormScenarioContext`; public/runtime contracts не затронуты. |
| Business analyst / domain workflow | PASS | Gherkin и `AC-0019` сохраняются, scenario не расширяется на relations or completion rules. |
| Delivery / operations / security | PASS | Нет CI/config/secrets/runtime changes; commit scope ограничен. |

### Post-SPEC Review

| Pass | Evidence inspected | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| Scope/Evidence | Эта SPEC; `storm.json`; `st-0007-task-card.feature`; `MainControlTaskCardLayoutUiTests`; central stack | Initial draft называл UI facts только обобщённо, что ослабляло reviewability. | Добавить exact automation IDs и narrow relation-editor controls. | FIXED |
| Contract | `AC-0019`, `SC-0007-001`, Non-Goals, local UI-test MUST | Initial table «было -> станет» могла восприниматься как факт до проверки. | Уточнить evidence gate для `passing` и metrics. | FIXED |
| Adversarial risk | Existing phone widths and Headless setup | Проверка только одного phone width могла пропустить known narrow boundary. | Закрепить all `360/390/430` in contract and acceptance. | FIXED |
| Role-Based | UX, tester, developer, business analyst, delivery | Нет находок после amendments. | Не требуется. | PASS |
| Fix and re-review | Updated sections 6.2, 11, 17, 19 | Exact invariants and evidence timing now consistent. | Не требуется. | PASS |

Depth checklist: scope ограничен одним BDD gap; AC и feature wording сохраняются; UI visual change отсутствует и fallback задокументирован; no production/code/config mutation planned; validation covers feature execution, geometry and artifact integrity.

Manual-review challenge: отдельный reviewer мог бы возразить, что BDD bridge проверяет лишь 390 px или декларативно повторяет UI tests. SPEC требует independent contract facts и all current boundary widths `360/390/430`, поэтому это замечание закрыто до EXEC.

Stop decision: PASS. BLOCKER/HIGH findings отсутствуют, все однозначные findings исправлены, user-owned choice не требуется.

### Post-EXEC Review

| Pass | Evidence inspected | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| Scope/Evidence | `git diff`, file scope, `storm.json`, six derived reports | В `process_audit`, nested `coverage_analysis` и `ranking_analysis` остались производные ссылки на предыдущий `SC-0006-003`. | Синхронизировать current command, trace links, ranking candidate и validation summary с `SC-0007-001`; сохранить historical `SC-0006-003` entry. | FIXED |
| Contract | `StormTaskCardLayoutExecutableSpecTests`, `TaskCardLayoutUiContract`, `SD-0099..SD-0102`, feature text | Новый bridge читает точный scenario и проверяет desktop плюс widths `360/390/430`; production/XAML/feature/annotations не затронуты. | Не требуется. | PASS |
| Tester / UI | Build, targeted BDD `1/1`, `MainControlTaskCardLayoutUiTests` `15/15`, artifact validator | Full `Unlimotion.Test` run завершился timeout через 304 s без summary, поэтому global regression PASS нельзя заявлять. | Сохранить timeout как residual risk; не менять test hosts/processes без отдельного process-access approval. | ACCEPTED RISK |
| Adversarial | `git diff --check`, validator после исправления, повтор targeted BDD `1/1` | Нет whitespace errors; validator: `0 errors, 10 warnings`, все warnings — intentional duplicate shared step text. | Не требуется. | PASS |
| Fix and re-review | Updated artifacts and repeated targeted gate | Derived artifacts теперь называют `SC-0007-001`; evidence не содержит ложного full-suite PASS. | Не требуется. | PASS |

Итог post-EXEC: PASS для целевого scenario и artifact sync. Residual risk: полный `Unlimotion.Test` gate в этой среде не подтверждён, потому что запуск не выдал итоговую summary до timeout. Это не блокирует test-only BDD slice, но требует отдельного повторного полного gate перед заявлением глобального regression PASS.

## Approval

Пользовательский goal содержит автоматическое подтверждение каждой подготовленной и прошедшей review SPEC. После `PASS` post-SPEC review эта SPEC считается подтверждённой для EXEC без отдельного ожидания.

## 20. Журнал действий агента

1. Прочитаны `storm.json`, Gherkin feature, current reports, `MainControlTaskCardLayoutUiTests` и central instruction stack.
2. Выбран следующий незакрытый high-value executable BDD gap: `SC-0007-001`.
3. После post-SPEC review добавлены `TS-0051`, independent `TaskCardLayoutUiContract`, `SD-0099..SD-0102` и additive test-local context fields; production code, XAML, `.feature`, automation IDs и existing annotations не менялись.
4. Выполнены build без ошибок, targeted BDD gate `1/1`, preserved `MainControlTaskCardLayoutUiTests` `15/15`; повтор targeted BDD gate после artifact fix также `1/1`.
5. `/storm:bdd-sync`, `/storm:bdd-lint` и validator синхронизированы: `26/45` step-executable, `105/105` reused definitions, `0 errors, 10 warnings`.
6. Full `Unlimotion.Test` gate превысил 304 s без summary; результат не заявлен как passing evidence, а process cleanup не выполнялся без отдельного approval.
