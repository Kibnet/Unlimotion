# STORM SC-0008-002: executable BDD для viewport и overlay Roadmap

## 0. Метаданные

- Статус: `DONE` (post-SPEC и post-EXEC review `PASS` с residual full-suite risk)
- Тип (профиль): `delivery-task`, test-only executable BDD implementation + artifact sync
- Масштаб: `small`
- Целевая модель: `gpt-5.5`
- Целевой релиз / ветка: локальная ветка `storm-bootstrap`
- STORM scope: `ST-0008 / AC-0023 / GR-023 / SC-0008-002`
- Central stack: `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `storm-product-development`, `review-loops`
- Ограничения: production/UI/XAML/feature wording/automation IDs/annotations/projects/workflows/persisted model не меняются.

## 1. Overview / Цель

Сделать `SC-0008-002` исполняемым из текущего feature text. Новый TUnit bridge должен подтвердить читаемое и управляемое Roadmap viewport/overlay state: minimap и controls доступны, а compact controls collapse и restore без потери интерактивности.

Outcome contract:

- Success means: точный scenario проходит через `SD-0115..SD-0118`, `TS-0055` и linked artifacts; `SC-0008-002` получает status `passing` только после фактического target evidence.
- Итоговый output: test-only contract и measured `30/45` executable coverage только после passing evidence.
- Stop rules: если evidence потребует product/UI/feature/annotation change, остановиться и оформить отдельный QUEST delivery-task.

## 2. Текущее состояние (AS-IS)

- `SC-0008-002` ссылается на `TS-0007`, но имеет `step_definitions: []`; `SC-0008-001` уже закрыт через `TS-0054`, coverage равна `29/45`.
- `RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls` проверяет mounted editor/minimap/toolbar, zoom, pan и reset viewport.
- `RoadmapGraph_ViewportOverlays_CollapseToCompactButtonsAndRestore` проверяет collapse/restore minimap и toolbar в узком окне, доступность compact buttons и сохранение pan/zoom/minimap binding.
- `SC-0008-003` остаётся отдельным gap для filters, inline rename, multi-selection и remaining overlay/minimap interaction contract.

## 3. Проблема

Viewport/overlay behavior имеет Headless UI evidence, но scenario `GR-023` не исполняется и не даёт собственной trace chain Scenario -> Test -> Steps.

## 4. Цели дизайна

1. Исполнять текущий `SC-0008-002` без изменения feature.
2. Разделить evidence на standard viewport controls и narrow-window compact overlay recovery.
3. Reuse existing isolated TUnit/UI tests через узкий BDD contract, не переписывая их annotations или behavior.
4. Обновить artifacts/reports только по фактическим results.

## 5. Non-Goals

- Не менять ViewportZoom, ViewportLocation, minimap, toolbar, compact layout или visual design.
- Не включать filters, inline rename, multi-selection, drag/drop или `SC-0008-003`.
- Не заявлять full suite passing без итоговой summary фактического run.

## 6. Предлагаемое решение

### 6.1 Распределение ответственности

| Файл | Ответственность |
| --- | --- |
| `StormRoadmapViewportOverlayExecutableSpecTests.cs` | Parse exact feature scenario, tags/rule/title и run steps. |
| `RoadmapViewportOverlayStepDefinitions.cs` | `SD-0115..SD-0118` только для `SC-0008-002`. |
| `RoadmapViewportOverlayContract.cs` | Собирает standard и compact overlay evidence. |
| `StormStepDefinition.cs` | Additive test-local flags/result. |
| `storm.json`, reports | `TS-0055`, links, actual metrics и next ranking. |

### 6.2 State / Interaction Matrix

| State | Trigger | Expected result | Evidence |
| --- | --- | --- | --- |
| Обычный Roadmap viewport | Open Roadmap and use controls | Minimap/toolbar present; zoom, pan and reset work. | Standard overlay UI test. |
| Узкое окно | Collapse then restore overlays | Compact expand controls stay available; toolbar/minimap recover and remain interactive. | Compact overlay UI test. |
| Scenario trace | Parse feature and run steps | Exact title/rule/tags and all four steps resolve uniquely. | New executable bridge. |

### 6.3 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Needs user before EXEC |
| --- | --- | --- | ---: | --- |
| Reuse existing UI tests | agent | Contract invokes the two focused `TS-0007` methods. | 0.95 | Нет |
| Claimed outcome | agent | Claim controls availability, compact recovery and viewport interaction; do not claim filter/rename/selection behavior. | 0.95 | Нет |
| Visual/video evidence | agent | Не применимо: UI behavior не меняется; passing Headless assertions are fallback. | 0.95 | Нет |
| Full suite | agent | Preserve prior timeout as residual risk; run only targeted gates. | 0.95 | Нет |

### 6.4 Runtime / Config / Data Contract Matrix

| Area | Source of truth | Change | Verification |
| --- | --- | --- |
| Roadmap viewport/overlays | Existing GraphControl and views | Нет | Existing tests through new bridge. |
| STORM trace | `storm.json` | Additive `TS-0055`/steps/status | Validator. |
| Config/CI/data migration | Не применимо | Нет | Diff review. |

## 7. Бизнес-правила

- Roadmap показывает minimap и viewport toolbar на обычном viewport.
- Zoom, pan и reset доступны через viewport controls.
- В узком окне minimap и toolbar сворачиваются в compact controls и восстанавливаются.
- После collapse/restore controls и minimap binding остаются интерактивными.

## 8. Точки интеграции и триггеры

`StormFeatureParser -> StormScenarioRunner -> RoadmapViewportOverlayStepDefinitions -> RoadmapViewportOverlayContract -> existing RoadmapGraph UI tests`.

## 9. Изменения модели данных / состояния

Только additive test-local `StormScenarioContext` fields. Production model и persisted data не меняются.

## 10. Миграция / Rollout / Rollback

Не применимо: test/artifact-only delivery. Rollback — revert isolated commit.

## 11. Тестирование и критерии приёмки

| Acceptance criterion | Automated evidence | Not tested rationale |
| --- | --- | --- |
| Viewport/minimap controls | `TS-0055`; standard overlay test checks controls, zoom, pan, reset. | Не применимо |
| Compact overlay recovery | `TS-0055`; narrow-window test checks collapse/restore and retained interaction. | Не применимо |
| Trace/metrics | Validator and BDD reports. | Не применимо |

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormRoadmapViewportOverlayExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/RoadmapGraphUiTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases

- Reusing existing tests makes this a trace bridge, not a replacement for UI tests; both remain visible in contract result.
- Roadmap tests use the existing `AvaloniaHeadless` limiter and must run sequentially.
- Full suite remains a separate residual environment risk and cannot be represented as PASS.
- `TS-0007` has unrelated graph interactions; contract invokes only its two viewport/overlay methods.

| Expected objection | Mitigation |
| --- | --- |
| «Minimap есть, но после collapse недоступен» | Contract includes explicit narrow-window collapse/restore evidence. |
| «Compact buttons есть, но controls не работают» | Existing test verifies pan and zoom after state changes. |
| «Нужен UI redesign» | Scope explicitly forbids it; no product behavior ships. |

## 13. План выполнения

1. Post-SPEC review, исправление однозначных findings.
2. Add test-only bridge/contract/steps/context.
3. Run build and targeted BDD/UI gates.
4. Sync artifacts/reports, validator and lint.
5. Post-EXEC review, fix findings and commit.

## 14. Открытые вопросы

Нет. Existing standard и narrow-window UI evidence однозначно определяет scope.

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
| `SC-0008-002.step_definitions` | `[]` | `SD-0115..SD-0118` после passing evidence |
| `SC-0008-002.status` | `automated` | `passing` только после target gate |
| `SC-0008-002.linked_tests` | `TS-0007` | existing link плюс `TS-0055` |
| Executable scenarios | 29/45 | 30/45 после validator |

## 18. Альтернативы и компромиссы

- Переиспользовать generic steps from another story: отклонено, поскольку story/feature text различаются и нужен scenario-specific trace.
- Переписать direct UI interaction: отклонено, поскольку established Headless UI tests уже проверяют standard/compact overlay semantics, а повторение расширяет risk без product change.
- Изменить Gherkin: отклонено по user constraint.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Статус | Обоснование |
| --- | --- | --- |
| A. Полнота | PASS | Цель, AS-IS, границы и result contract определены. |
| B. Дизайн | PASS | State matrix разделяет standard и compact overlay evidence. |
| C. Безопасность | PASS | Production/state/config changes исключены. |
| D. Проверяемость | PASS | AC matrix и exact target commands имеются. |
| E. Автономность | PASS | User-owned questions отсутствуют. |
| F. Профиль | PASS | STORM + UI test MUST соблюдены. |

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель и границы | 5 | Один scenario, no product change. |
| AS-IS | 5 | Existing standard and compact UI evidence inspected. |
| Дизайн | 5 | `TS-0055`, four steps, two viewport states. |
| Безопасность | 5 | Additive test/artifact scope. |
| Тестируемость | 5 | Targeted gates, validator и residual risk explicit. |
| Готовность | 5 | Нет blocker/open choice. |

Итог: 30/30, готово к автономному выполнению.

### Role-Based Review Result

| Роль | Verdict | Проверка |
| --- | --- | --- |
| Business analyst | PASS | AC-0023 отражён как readable/control-recoverable viewport outcome. |
| UX / designer | PASS | Standard и narrow overlay states проверяются; visual/video evidence не требуется без UI change. |
| Tester | PASS | Controls и recovery имеют complementary evidence. |
| Developer | PASS | Existing evidence reuse and test-local context keep scope small. |
| Delivery | PASS | No config/CI/secrets/runtime change; rollback is revert. |

### Post-SPEC Review

| Pass | Finding | Action | Status |
| --- | --- | --- | --- |
| Scope | Scenario could overclaim filters and node interactions from neighbouring rule. | Explicitly exclude `SC-0008-003` and invoke only two viewport/overlay methods. | FIXED |
| Evidence | Standard overlay test alone does not prove narrow-window recovery. | Require compact collapse/restore test in contract and acceptance matrix. | FIXED |
| Adversarial | Compact controls could be present but non-interactive after recovery. | Retain existing pan/zoom and minimap-binding assertions as required evidence. | FIXED |
| Role-based / re-review | Нет user-owned blocker. | Не требуется. | PASS |

Stop decision: PASS. BLOCKER/HIGH findings отсутствуют.

### Post-EXEC Review

| Pass | Result |
| --- | --- |
| Scope/Evidence | PASS: изменены только test bridge, test-local context, SPEC и STORM artifacts. |
| Contract | PASS: standard viewport controls и compact overlay recovery исполняются из scenario text. |
| Validation | PASS: build errors 0, BDD 1/1, preserved Roadmap UI 47/47, validator 0 errors/12 warnings/30 of 45 executable. |
| Lint delta | ACCEPTED: новый scenario-specific `ST-0008` step формирует одну ожидаемую duplicate-step warning group. |
| Residual risk | ACCEPTED: full suite PASS не заявлен после previous 304-second timeout без summary. |

Итог: `SC-0008-002` PASS; `ST-0008` now 2/3 step-executable.

## Approval

Active user goal автоматически подтверждает SPEC после post-SPEC `PASS`; EXEC разрешён без ожидания отдельного сообщения.

## 20. Журнал действий агента

1. После `c885317` прочитаны `SC-0008-002`, feature, current reports и exact standard/compact viewport UI tests.
2. Выбран следующий Roadmap gap; SPEC ограничила delivery test/artifact-only binding.
3. Post-SPEC review потребовал narrow-window recovery и исключил unrelated `SC-0008-003` interactions.
4. Добавлены `TS-0055`, `SD-0115..SD-0118`, Roadmap viewport/overlay contract и test-local context; production code и existing annotations не менялись.
5. Build прошёл без errors; targeted BDD прошёл 1/1, Roadmap UI class прошёл 47/47; artifact validator: 0 errors, 12 warnings, 30/45 executable scenarios.
