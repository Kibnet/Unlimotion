# STORM SC-0008-003: executable BDD для взаимодействий Roadmap

## 0. Метаданные
- Статус: `DONE` (post-SPEC и post-EXEC review `PASS` с residual full-suite risk)
- Тип: `delivery-task`, test-only executable BDD implementation + artifact sync
- Scope: `ST-0008 / AC-0024 / GR-024 / SC-0008-003`
- Central stack: `quest-governance`, `quest-mode`, `testing-dotnet`, `ui-automation-testing`, `storm-product-development`, `review-loops`
- Ограничения: production/UI/XAML/feature wording/automation IDs/annotations/projects/workflows/persisted model не меняются.

## 1. Цель
Сделать `SC-0008-003` исполняемым из current feature text. Новый TUnit bridge должен доказать Roadmap filters, inline rename, modifier multi-selection и viewport/minimap controls через existing Headless UI evidence.

Success: exact scenario проходит через `SD-0119..SD-0122` и `TS-0056`; после фактического PASS `ST-0008` получает `3/3` executable scenarios и общий ratio `31/45`. Stop: если evidence потребует product/UI/feature/annotation change, остановиться и оформить отдельный QUEST delivery-task.

## 2. AS-IS
- `SC-0008-003` linked to `TS-0006/TS-0007`, but has no steps; current ratio is `30/45`.
- `RoadmapFilterToolbar_NarrowViewport_UsesCompactPrimaryActions` checks responsive toolbar, filter flyout and reset.
- `RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode`, `RoadmapGraph_InlineTitleEdit_CreatesEditorForF2OrRepeatedTitleClick`, `RoadmapGraph_NodeClickSelection_AppliesModifierSemanticsAndVisualState`, and `RoadmapGraph_ViewportOverlay_ProvidesMinimapAndControls` cover the four Roadmap outcomes.
- Compact overlay recovery remains owned by `SC-0008-002 / TS-0055`.

## 3. Проблема
GR-024 has existing evidence but no executable Gherkin trace for its own scenario.

## 4. Цели дизайна
1. Execute exact scenario wording without feature mutation.
2. Map all four outcome groups to five focused tests.
3. Keep compact recovery out of this contract to avoid duplicate claims.
4. Sync artifacts only after passing evidence.

## 5. Non-Goals
No product/UI/config/annotation changes; no drag/drop; no full-suite PASS claim.

## 6. Решение
| Файл | Ответственность |
| --- | --- |
| `StormRoadmapInteractionsExecutableSpecTests.cs` | Parse scenario and execute exact steps. |
| `RoadmapInteractionsStepDefinitions.cs` | `SD-0119..SD-0122` only for this scenario. |
| `RoadmapInteractionsContract.cs` | Invoke the five focused existing UI tests. |
| `StormStepDefinition.cs` | Additive test-local context only. |
| STORM artifacts | `TS-0056`, trace and measured metrics. |

## 7. Бизнес-правила
- Responsive Roadmap toolbar exposes filters and reset.
- Search highlights matching tasks without graph rebuild.
- Inline rename and Ctrl/Shift/Alt multi-selection work.
- Minimap and viewport controls are available.

## 8. Интеграция
`StormFeatureParser -> StormScenarioRunner -> RoadmapInteractionsStepDefinitions -> RoadmapInteractionsContract -> existing FilterToolbar/RoadmapGraph tests`.

## 9. Состояние
Только additive test-local `StormScenarioContext` fields. Production and persisted state do not change.

## 10. Rollout / Rollback
Не применимо; rollback -- revert isolated commit.

## 11. Тестирование
| Outcome | Evidence |
| --- | --- |
| Filters/reset | `TS-0056` invokes responsive Roadmap filter toolbar test. |
| Search/rename/selection | `TS-0056` invokes three named RoadmapGraph tests. |
| Viewport/minimap | `TS-0056` invokes standard viewport test; compact recovery remains `TS-0055`. |
| Trace | Validator and reports. |

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormRoadmapInteractionsExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/RoadmapGraphUiTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/MainControlFilterToolbarResponsiveUiTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски
Bridge is trace evidence, not a replacement for UI tests. Headless runs remain sequential. Full suite timeout remains unconfirmed. The contract records individual behavior flags to avoid overclaiming broad scenario wording.

## 13. План
1. Post-SPEC review.
2. Add bridge, contract, steps and context.
3. Run targeted BDD and relevant UI gates.
4. Sync artifacts and validate.
5. Post-EXEC review and commit.

## 14. Открытые вопросы
Нет.

## 15. Профиль
STORM preserves AC/Gherkin; local UI-test MUST is met through existing Avalonia.Headless suites.

## 16. Файлы
SPEC, three new test-local bridge files, `StormStepDefinition.cs`, `storm.json` and six reports.

## 17. Было -> станет
| Field | Было | Станет |
| --- | --- | --- |
| Steps | `[]` | `SD-0119..SD-0122` |
| Status | `automated` | `passing` after target PASS |
| Tests | `TS-0006/TS-0007` | Existing plus `TS-0056` |
| ST-0008 | 2/3 | 3/3 executable |

## 18. Альтернативы
Generic steps, reimplemented UI interactions and Gherkin changes are rejected: they weaken scenario-specific trace, duplicate proven tests, or violate scope.

## 19. Review
### SPEC Linter / Rubric
Completeness, design, safety, testability, autonomy and profile: PASS. Rubric: 30/30.

### Role-Based Review
Business analyst, UX/designer, tester, developer and delivery: PASS. Five named methods cover filters, search, rename, selection and controls without product change.

### Post-SPEC Review
| Finding | Action | Status |
| --- | --- | --- |
| Broad Then could overclaim compact recovery. | Keep recovery owned by `SC-0008-002`; use standard controls only. | FIXED |
| Search wording could omit filter responsiveness. | Include focused `TS-0006` Roadmap filter test. | FIXED |
| Rename/selection need independent evidence. | Require separate named tests and result flags. | FIXED |

Stop decision: PASS. BLOCKER/HIGH findings отсутствуют.

### Post-EXEC Review
| Pass | Result |
| --- | --- |
| Scope/Evidence | PASS: изменены только test bridge, test-local context, SPEC и STORM artifacts. |
| Contract | PASS: filters, search, rename, modifier selection и standard controls исполняются из scenario text. |
| Validation | PASS: build errors 0, BDD 1/1, Roadmap UI 47/47, filter toolbar UI 14/14, validator 0 errors/12 warnings/31 of 45 executable. |
| Residual risk | ACCEPTED: full suite PASS не заявлен после previous 304-second timeout без summary. |

Итог: `SC-0008-003` PASS; `ST-0008` now 3/3 step-executable.

## Approval
Active user goal автоматически подтверждает SPEC после post-SPEC PASS; EXEC разрешён.

## 20. Журнал
1. После `d45861b` прочитаны feature, scenario, `TS-0006/TS-0007` и targeted UI methods.
2. SPEC связывает ровно пять existing evidence paths и не меняет product behavior.
3. Добавлены `TS-0056`, `SD-0119..SD-0122`, interaction contract и test-local context; production code и existing annotations не менялись.
4. Build прошёл без errors; BDD прошёл 1/1, Roadmap UI 47/47, filter toolbar UI 14/14; artifact validator: 0 errors, 12 warnings, 31/45 executable scenarios.
