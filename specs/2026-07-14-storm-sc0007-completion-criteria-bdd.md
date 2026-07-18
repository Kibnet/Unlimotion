# STORM SC-0007-003: executable BDD для критериев завершения

## 0. Метаданные

- Статус: `DONE` (post-SPEC и post-EXEC review `PASS` с residual full-suite risk)
- Тип (профиль): `delivery-task`, test-only executable BDD implementation + artifact sync
- Масштаб: `small`
- Целевая модель: `gpt-5.5`
- Целевой релиз / ветка: локальная ветка `storm-bootstrap`
- STORM scope: `ST-0007 / AC-0021 / GR-021 / SC-0007-003`
- Central stack: `quest-governance`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `storm-product-development`, `review-loops`
- Ограничения: production/UI/XAML/feature wording/automation IDs/annotations/projects/workflows/persisted model не меняются.

## 1. Overview / Цель

Сделать `SC-0007-003` исполняемым из существующего feature text. Новый TUnit bridge должен доказать add/edit completion criterion в task card, переход Completed после удовлетворения критерия и запрет редактирования completion criteria у completed task.

Outcome contract:

- Success means: exact scenario проходит через `SD-0107..SD-0110`, `TS-0053` и linked artifacts; `ST-0007` получает `3/3` executable scenarios.
- Итоговый output: test-only contract и measured `28/45` executable coverage только после passing evidence.
- Stop rules: если evidence потребует product/UI/feature/annotation change, остановиться и оформить отдельный QUEST delivery-task.

## 2. Текущее состояние (AS-IS)

- `SC-0007-003` ссылается на `TS-0003` и `TS-0005`, но имеет `step_definitions: []`; после предыдущей итерации coverage равна `27/45`.
- `MainControlTaskCardLayoutUiTests.CurrentTaskCard_AddCompletionCriterion_FocusesNewCriterionTextBox` проверяет active add action, focus и new criterion text box.
- `MainControlTaskCardLayoutUiTests.CurrentTaskCard_CompletionCriterionRow_UsesBorderlessCompactEditing` подтверждает visible/editable criterion controls, а `CurrentTaskCard_CompletedTask_DisablesCompletionCriteriaEditing` запрещает add/items editing после Completed.
- `MainControlTaskStatusIconUiTests.TaskItemViewModel_CompletionCriterionChange_SavesOnMainThreadAfterThrottle` подтверждает persisted edit, а `TaskStatusPickerFlyout_EnablesCompletedOptionAfterCriterionIsSatisfied` открывает Completed только после satisfied criterion.
- Existing executable `SC-0002-002` покрывает общий completion-block lifecycle, но не связывает task-card scenario `SC-0007-003` с feature text.

## 3. Проблема

Task-card completion-criteria behavior имеет evidence, однако scenario `GR-021` не исполняется и не даёт собственной trace chain Scenario -> Test -> Steps.

## 4. Цели дизайна

1. Исполнять текущий `SC-0007-003` без изменения feature.
2. Разделить evidence на add/edit, satisfied-to-Completed и completed-lock states.
3. Reuse existing isolated TUnit/UI tests через узкий BDD contract, не переписывая их annotations или behavior.
4. Обновить artifacts/reports только по фактическим results.

## 5. Non-Goals

- Не менять task status rules, completion criterion model, storage/throttle semantics, visual card design или copy.
- Не расширять scenario на status-history, relations, migration или `SC-0008-*`.
- Не заявлять full suite passing без итоговой summary фактического run.

## 6. Предлагаемое решение

### 6.1 Распределение ответственности

| Файл | Ответственность |
| --- | --- |
| `StormTaskCardCompletionCriteriaExecutableSpecTests.cs` | Parse exact feature scenario, tags/rule/title и run steps. |
| `TaskCardCompletionCriteriaStepDefinitions.cs` | `SD-0107..SD-0110` только для `SC-0007-003`. |
| `TaskCardCompletionCriteriaContract.cs` | Собирает add/edit, satisfied-to-Completed и completed-lock evidence. |
| `StormStepDefinition.cs` | Additive test-local flags/result. |
| `storm.json`, reports | `TS-0053`, links, actual metrics и next ranking. |

### 6.2 State / Interaction Matrix

| State | Trigger | Expected result | Evidence |
| --- | --- | --- | --- |
| Active task without criterion | Add criterion | New text box appears and receives focus. | Task-card UI test. |
| Active task with criterion | Edit text / satisfy | Edit persists after throttle; Completed becomes enabled only after satisfaction. | Status icon tests. |
| Completed task | Open task card | Add button and criteria items are disabled. | Task-card UI and VM tests. |

### 6.3 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Needs user before EXEC |
| --- | --- | --- | ---: | --- |
| Reuse existing tests | agent | Contract invokes four existing evidence methods, consistent with prior STORM BDD contracts. | 0.9 | Нет |
| Visual/video evidence | agent | Не применимо: UI behavior не меняется; passing headless assertions are fallback. | 0.95 | Нет |
| Full suite | agent | Preserve prior timeout as residual risk; run only targeted gates. | 0.95 | Нет |

### 6.4 Runtime / Config / Data Contract Matrix

| Area | Source of truth | Change | Verification |
| --- | --- | --- | --- |
| Completion behavior | Existing ViewModel/domain/storage | Нет | Existing tests through new bridge. |
| STORM trace | `storm.json` | Additive `TS-0053`/steps/status | Validator. |
| Config/CI/data migration | Не применимо | Нет | Diff review. |

## 7. Бизнес-правила

- Active task допускает add/edit completion criterion.
- Criterion change persists after throttle on UI thread.
- Unsatisfied criterion не допускает Completed; satisfied criterion делает Completed available.
- Completed task не допускает add/edit criteria.

## 8. Точки интеграции и триггеры

`StormFeatureParser -> StormScenarioRunner -> TaskCardCompletionCriteriaStepDefinitions -> TaskCardCompletionCriteriaContract -> existing task-card/status tests`.

## 9. Изменения модели данных / состояния

Только additive test-local `StormScenarioContext` fields. Production model и persisted data не меняются.

## 10. Миграция / Rollout / Rollback

Не применимо: test/artifact-only delivery. Rollback — revert isolated commit.

## 11. Тестирование и критерии приёмки

| Acceptance criterion | Automated evidence | Not tested rationale |
| --- | --- | --- |
| Add/edit criterion | `TS-0053`; add/focus, editable-row and throttle-save tests | Не применимо |
| Completed after satisfied criterion | `TS-0053`; status picker test | Не применимо |
| Lock criteria after completion | `TS-0053`; completed-card test and VM guard | Не применимо |
| Trace/metrics | Validator and bdd reports | Не применимо |

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormTaskCardCompletionCriteriaExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases

- Reusing existing tests makes this a trace bridge, not a replacement for component tests; both remain visible in contract result.
- Headless focus/throttle tests must remain sequential under the existing `AvaloniaHeadless` limiter.
- Full suite remains a separate residual environment risk and cannot be represented as PASS.

| Expected objection | Mitigation |
| --- | --- |
| «Есть только completed lock, но не edit» | Contract combines add/focus, row editing and throttled persisted edit. |
| «Satisfied criterion bypasses user UI» | Status picker test verifies UI availability and selection. |
| «Нужен UI redesign» | Scope explicitly forbids it; headless evidence is appropriate because no visual change ships. |

## 13. План выполнения

1. Post-SPEC review, исправление однозначных findings.
2. Add test-only bridge/contract/steps/context.
3. Run build and targeted BDD/UI gates.
4. Sync artifacts/reports, validator and lint.
5. Post-EXEC review, fix findings and commit.

## 14. Открытые вопросы

Нет. Existing UI and status evidence однозначно определяет scope.

## 15. Соответствие профилю

`storm-product-development` сохраняет AC/Gherkin и требует trace links. `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing` и local override требуют relevant passing UI coverage; новая SPEC использует установленный Avalonia.Headless path.

## 16. Таблица изменений файлов

| Файл | Изменение |
| --- | --- |
| Эта SPEC | Governance, reviews и evidence. |
| Новый BDD test/contract/steps | Feature execution and scenario-specific trace. |
| `StormStepDefinition.cs` | Test-local fields only. |
| `storm.json`, reports | Additive sync and measured metrics. |

## 17. Было -> станет

| Область | Было | Стало |
| --- | --- | --- |
| `SC-0007-003.step_definitions` | `[]` | `SD-0107..SD-0110` после passing evidence |
| `SC-0007-003.status` | `automated` | `passing` только после target gate |
| `SC-0007-003.linked_tests` | `TS-0003`, `TS-0005` | existing links плюс `TS-0053` |
| ST-0007 | 2/3 executable | 3/3 executable после validator |

## 18. Альтернативы и компромиссы

- Переиспользовать `SC-0002-002` steps: отклонено, поскольку story/feature text различаются и нужен scenario-specific trace.
- Переписать direct UI interaction: отклонено, поскольку established isolated tests уже проверяют add/edit/lock semantics, а повторение расширяет risk без product change.
- Изменить Gherkin: отклонено по user constraint.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Статус | Обоснование |
| --- | --- | --- |
| A. Полнота | PASS | Цель, AS-IS, границы и result contract определены. |
| B. Дизайн | PASS | State matrix и responsibilities разделяют UI/status/trace evidence. |
| C. Безопасность | PASS | Production/state/config changes исключены. |
| D. Проверяемость | PASS | AC matrix и exact target commands имеются. |
| E. Автономность | PASS | User-owned questions отсутствуют. |
| F. Профиль | PASS | STORM + UI test MUST соблюдены. |

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель и границы | 5 | Один scenario, no product change. |
| AS-IS | 5 | Existing UI, status и lifecycle evidence inspected. |
| Дизайн | 5 | `TS-0053`, four steps, three behavioral states. |
| Безопасность | 5 | Additive test/artifact scope. |
| Тестируемость | 5 | Targeted gates, validator и residual risk explicit. |
| Готовность | 5 | Нет blocker/open choice. |

Итог: 30/30, готово к автономному выполнению.

### Role-Based Review Result

| Роль | Verdict | Проверка |
| --- | --- | --- |
| Business analyst | PASS | Add/edit/completion lock полностью отражают AC-0021. |
| UX / designer | PASS | UI states and non-applicability of visual/video change evidence explicit. |
| Tester | PASS | Add, edit/persist, satisfy-to-complete и completed-lock имеют evidence. |
| Developer | PASS | Existing evidence reuse and test-local context keep scope small. |
| Delivery | PASS | No config/CI/secrets/runtime change; rollback is revert. |

### Post-SPEC Review

| Pass | Finding | Action | Status |
| --- | --- | --- | --- |
| Scope/Evidence | Initial scope мог ограничиться completed lock и пропустить edit/persist. | Добавить add/focus, editable-row и throttled-save evidence. | FIXED |
| Contract | General lifecycle scenario имеет другой story/feature. | Создать separate ST-0007 steps, не reuse SC-0002-002 IDs. | FIXED |
| Adversarial | Full suite не имеет current PASS summary. | Заявлять только targeted evidence. | FIXED |
| Role-based / re-review | Нет user-owned blocker. | Не требуется. | PASS |

Stop decision: PASS. BLOCKER/HIGH findings отсутствуют.

### Post-EXEC Review

| Pass | Result |
| --- | --- |
| Scope/Evidence | PASS: изменены только test bridge, test-local context, SPEC и STORM artifacts. |
| Contract | PASS: add/focus, edit/persist, satisfied-to-Completed и completed-lock evidence исполняются из scenario text. |
| Validation | PASS: build errors 0, BDD 1/1, preserved task-card UI 15/15, validator 0 errors/11 warnings. |
| Residual risk | ACCEPTED: full suite PASS не заявлен после previous 304-second timeout без summary. |

Итог: `SC-0007-003` PASS; `ST-0007` now 3/3 step-executable.

## Approval

Active user goal автоматически подтверждает SPEC после post-SPEC `PASS`; EXEC разрешён без ожидания отдельного сообщения.

## 20. Журнал действий агента

1. После `981a304` прочитаны `SC-0007-003`, feature, current reports, task-card completion tests и existing lifecycle executable steps.
2. Выбран последний gap `ST-0007`; SPEC ограничила delivery test/artifact-only binding.
3. Добавлены `TS-0053`, `SD-0107..SD-0110`, completion-criteria contract и test-local context; production code и existing annotations не менялись.
4. Build прошёл без errors; targeted BDD прошёл 1/1, task-card UI class прошёл 15/15; artifact validator: 0 errors, 11 warnings, 28/45 executable scenarios.
