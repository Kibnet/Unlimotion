# STORM SC-0008-001: executable BDD для построения Roadmap

## 0. Метаданные

- Статус: `DONE` (post-SPEC и post-EXEC review `PASS` с residual full-suite risk)
- Тип (профиль): `delivery-task`, test-only executable BDD implementation + artifact sync
- Масштаб: `small`
- Целевая модель: `gpt-5.5`
- Целевой релиз / ветка: локальная ветка `storm-bootstrap`
- STORM scope: `ST-0008 / AC-0022 / GR-022 / SC-0008-001`
- Central stack: `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `storm-product-development`, `review-loops`
- Ограничения: production/UI/XAML/feature wording/automation IDs/annotations/projects/workflows/persisted model не меняются.

## 1. Overview / Цель

Сделать `SC-0008-001` исполняемым из текущего feature text. Новый TUnit bridge должен подтвердить, что Roadmap строит узлы и типизированные связи из текущей модели задач и отображает их в настоящем Headless Roadmap view.

Outcome contract:

- Success means: точный scenario проходит через `SD-0111..SD-0114`, `TS-0054` и linked artifacts; `SC-0008-001` получает status `passing` только после фактического target evidence.
- Итоговый output: test-only contract и measured `29/45` executable coverage только после passing evidence.
- Stop rules: если evidence потребует product/UI/feature/annotation change, остановиться и оформить отдельный QUEST delivery-task.

## 2. Текущее состояние (AS-IS)

- `SC-0008-001` ссылается на `TS-0007`, но имеет `step_definitions: []`; после предыдущей итерации coverage равна `28/45`.
- `RoadmapGraphUiTests.RoadmapGraphProjection_BuildsNodesAndTypedConnections` строит projection из актуального fixture, проверяет root/subtask nodes, `Contains` и `Blocks` connections, отсутствие self-loop и left-to-right layout.
- `RoadmapGraphUiTests.RoadmapGraph_NodifyView_RendersTasksAndKeepsAutomationIds` открывает настоящий Headless Roadmap, ждёт nodes, проверяет `Contains`/`Blocks` connections и связность rendered node с task state.
- `SC-0008-002` и `SC-0008-003` остаются отдельными gaps: viewport/overlay и filters/inline rename/multi-selection не входят в эту SPEC.

## 3. Проблема

Базовое Roadmap behavior имеет UI and projection evidence, но scenario `GR-022` не исполняется и не даёт собственной trace chain Scenario -> Test -> Steps.

## 4. Цели дизайна

1. Исполнять текущий `SC-0008-001` без изменения feature.
2. Разделить evidence на построение projection и отображение current task model в Roadmap view.
3. Reuse existing isolated TUnit/UI tests через узкий BDD contract, не переписывая их annotations или behavior.
4. Обновить artifacts/reports только по фактическим results.

## 5. Non-Goals

- Не менять алгоритм layout, graph model, UI controls, automation IDs или copy.
- Не расширять scenario на viewport overlay, filters, rename, selection, drag/drop или `SC-0008-002`/`SC-0008-003`.
- Не заявлять full suite passing без итоговой summary фактического run.

## 6. Предлагаемое решение

### 6.1 Распределение ответственности

| Файл | Ответственность |
| --- | --- |
| `StormRoadmapProjectionExecutableSpecTests.cs` | Parse exact feature scenario, tags/rule/title и run steps. |
| `RoadmapProjectionStepDefinitions.cs` | `SD-0111..SD-0114` только для `SC-0008-001`. |
| `RoadmapProjectionContract.cs` | Собирает projection и rendered Roadmap evidence. |
| `StormStepDefinition.cs` | Additive test-local flags/result. |
| `storm.json`, reports | `TS-0054`, links, actual metrics и next ranking. |

### 6.2 State / Interaction Matrix

| State | Trigger | Expected result | Evidence |
| --- | --- | --- | --- |
| Актуальная task model | `RoadmapGraphBuilder.Build` | Есть expected root/subtask nodes и `Contains`/`Blocks` typed connections. | Projection test. |
| Roadmap tab с task model | Open Roadmap Headless view | Рендерятся nodes и both connection kinds; node привязан к текущему task state. | Avalonia.Headless UI test. |
| Scenario trace | Parse feature and run steps | Exact title/rule/tags and all four steps resolve uniquely. | New executable bridge. |

### 6.3 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Needs user before EXEC |
| --- | --- | --- | ---: | --- |
| Reuse existing projection/UI tests | agent | Contract invokes the two focused `TS-0007` evidence methods, consistent with prior STORM BDD contracts. | 0.95 | Нет |
| Scope of rendered-view evidence | agent | Treat automation-ID assertion as a retained guard inside existing UI test, but claim only rendered nodes/connections and task binding for this scenario. | 0.95 | Нет |
| Visual/video evidence | agent | Не применимо: UI behavior не меняется; passing Headless assertions are fallback. | 0.95 | Нет |
| Full suite | agent | Preserve prior timeout as residual risk; run only targeted gates. | 0.95 | Нет |

### 6.4 Runtime / Config / Data Contract Matrix

| Area | Source of truth | Change | Verification |
| --- | --- | --- |
| Roadmap projection and view | Existing graph builder and controls | Нет | Existing tests through new bridge. |
| STORM trace | `storm.json` | Additive `TS-0054`/steps/status | Validator. |
| Config/CI/data migration | Не применимо | Нет | Diff review. |

## 7. Бизнес-правила

- Roadmap строит nodes из актуальной task model.
- Roadmap формирует typed `Contains` и `Blocks` connections без self-loop.
- Connections ориентированы слева направо.
- Open Roadmap view отображает nodes и оба вида connections, связанные с task state.

## 8. Точки интеграции и триггеры

`StormFeatureParser -> StormScenarioRunner -> RoadmapProjectionStepDefinitions -> RoadmapProjectionContract -> existing RoadmapGraph UI/projection tests`.

## 9. Изменения модели данных / состояния

Только additive test-local `StormScenarioContext` fields. Production model и persisted data не меняются.

## 10. Миграция / Rollout / Rollback

Не применимо: test/artifact-only delivery. Rollback — revert isolated commit.

## 11. Тестирование и критерии приёмки

| Acceptance criterion | Automated evidence | Not tested rationale |
| --- | --- | --- |
| Nodes и typed relations from current model | `TS-0054`; projection test checks expected nodes, `Contains` and `Blocks`. | Не применимо |
| Roadmap view presents graph from task state | `TS-0054`; Headless Roadmap view waits for nodes and both connection kinds. | Не применимо |
| Trace/metrics | Validator and BDD reports. | Не применимо |

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormRoadmapProjectionExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/RoadmapGraphUiTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases

- Reusing existing tests makes this a trace bridge, not a replacement for component/UI tests; both remain visible in contract result.
- Roadmap tests use the existing `AvaloniaHeadless` limiter and must run sequentially.
- Full suite remains a separate residual environment risk and cannot be represented as PASS.
- `TS-0007` contains overlay and interaction tests outside `GR-022`; contract invokes only the two evidence methods relevant to this scenario.

| Expected objection | Mitigation |
| --- | --- |
| «Узел есть, но связь не проверена» | Projection and UI evidence both assert `Contains` and `Blocks` connections. |
| «Projection не доказывает rendered state» | Contract includes `RoadmapGraph_NodifyView_RendersTasksAndKeepsAutomationIds`. |
| «Нужен UI redesign» | Scope explicitly forbids it; no product behavior ships. |

## 13. План выполнения

1. Post-SPEC review, исправление однозначных findings.
2. Add test-only bridge/contract/steps/context.
3. Run build and targeted BDD/UI gates.
4. Sync artifacts/reports, validator and lint.
5. Post-EXEC review, fix findings and commit.

## 14. Открытые вопросы

Нет. Existing projection и Headless UI evidence однозначно определяют scope.

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
| `SC-0008-001.step_definitions` | `[]` | `SD-0111..SD-0114` после passing evidence |
| `SC-0008-001.status` | `automated` | `passing` только после target gate |
| `SC-0008-001.linked_tests` | `TS-0007` | existing link плюс `TS-0054` |
| Executable scenarios | 28/45 | 29/45 после validator |

## 18. Альтернативы и компромиссы

- Переиспользовать generic steps from another story: отклонено, поскольку story/feature text различаются и нужен scenario-specific trace.
- Переписать direct UI interaction: отклонено, поскольку established projection and Headless UI tests уже проверяют node/connection semantics, а повторение расширяет risk без product change.
- Изменить Gherkin: отклонено по user constraint.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Статус | Обоснование |
| --- | --- | --- |
| A. Полнота | PASS | Цель, AS-IS, границы и result contract определены. |
| B. Дизайн | PASS | State matrix разделяет projection, rendered view и trace evidence. |
| C. Безопасность | PASS | Production/state/config changes исключены. |
| D. Проверяемость | PASS | AC matrix и exact target commands имеются. |
| E. Автономность | PASS | User-owned questions отсутствуют. |
| F. Профиль | PASS | STORM + UI test MUST соблюдены. |

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель и границы | 5 | Один scenario, no product change. |
| AS-IS | 5 | Existing builder and rendered-view evidence inspected. |
| Дизайн | 5 | `TS-0054`, four steps, three evidence states. |
| Безопасность | 5 | Additive test/artifact scope. |
| Тестируемость | 5 | Targeted gates, validator и residual risk explicit. |
| Готовность | 5 | Нет blocker/open choice. |

Итог: 30/30, готово к автономному выполнению.

### Role-Based Review Result

| Роль | Verdict | Проверка |
| --- | --- | --- |
| Business analyst | PASS | AC-0022 отражён как node/typed-relation outcome из current model. |
| UX / designer | PASS | Rendered Roadmap view проверяется; visual/video evidence не требуется без UI change. |
| Tester | PASS | Projection и real view дают complementary evidence. |
| Developer | PASS | Existing evidence reuse and test-local context keep scope small. |
| Delivery | PASS | No config/CI/secrets/runtime change; rollback is revert. |

### Post-SPEC Review

| Pass | Finding | Action | Status |
| --- | --- | --- | --- |
| Scope | Existing rendered-view test also guards automation IDs, which is outside the business rule. | Design ledger and claims explicitly limit new scenario outcome to nodes, typed connections and task binding. | FIXED |
| Evidence | Projection alone would not prove mounted Roadmap state. | Require a second Headless view test in the contract and acceptance matrix. | FIXED |
| Adversarial | `TS-0007` also contains unrelated overlay and interaction behavior. | Invoke only two named relevant methods and keep `SC-0008-002`/`003` out of scope. | FIXED |
| Role-based / re-review | Нет user-owned blocker. | Не требуется. | PASS |

Stop decision: PASS. BLOCKER/HIGH findings отсутствуют.

### Post-EXEC Review

| Pass | Result |
| --- | --- |
| Scope/Evidence | PASS: изменены только test bridge, test-local context, SPEC и STORM artifacts. |
| Contract | PASS: current-model projection и mounted Headless Roadmap nodes/typed connections исполняются из scenario text. |
| Validation | PASS: build errors 0, BDD 1/1, preserved Roadmap UI 47/47, validator 0 errors/11 warnings/29 of 45 executable. |
| Residual risk | ACCEPTED: full suite PASS не заявлен после previous 304-second timeout без summary. |

Итог: `SC-0008-001` PASS; `ST-0008` now 1/3 step-executable.

## Approval

Active user goal автоматически подтверждает SPEC после post-SPEC `PASS`; EXEC разрешён без ожидания отдельного сообщения.

## 20. Журнал действий агента

1. После `31e0535` прочитаны `SC-0008-001`, feature, current coverage evidence и focused `RoadmapGraphUiTests` methods.
2. Выбран базовый Roadmap gap `ST-0008`; SPEC ограничила delivery test/artifact-only binding.
3. Post-SPEC review отделил automation-ID guard от claimed business outcome и потребовал complementary rendered-view evidence.
4. Добавлены `TS-0054`, `SD-0111..SD-0114`, Roadmap projection contract и test-local context; production code и existing annotations не менялись.
5. Build прошёл без errors; targeted BDD прошёл 1/1, Roadmap UI class прошёл 47/47; artifact validator: 0 errors, 11 warnings, 29/45 executable scenarios.
