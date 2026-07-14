# STORM SC-0009-001: executable BDD для локальной JSON-сериализации

## 0. Метаданные
- Статус: `DONE` (post-SPEC и post-EXEC review `PASS` с residual full-suite risk)
- Тип: `delivery-task`, test-only executable BDD implementation + artifact sync
- Scope: `ST-0009 / AC-0025 / GR-025 / SC-0009-001`
- Central stack: `quest-governance`, `quest-mode`, `testing-dotnet`, `storm-product-development`, `review-loops`
- Ограничения: production/code behavior, feature wording, annotations, projects and workflows не меняются.

## 1. Цель
Сделать `SC-0009-001` исполняемым из feature text. Новый TUnit bridge должен доказать, что `FileStorage` сохраняет task в JSON-файл выбранной папки и загружает тот же contract обратно.

Success: exact scenario проходит через `SD-0123..SD-0126` и новый `TS-0057`; общий executable ratio повышается до `32/45`. Stop: если direct evidence потребует product/test annotation change, оформить отдельный QUEST delivery-task.

## 2. AS-IS
- Scenario linked to `TS-0014` but has no steps.
- `FileStorageTaskStatusTests.Save_WritesExplicitStatusHistoryAndCompletionCriteriaWithoutLegacyFields` creates a temp selected folder, saves a task, parses the written JSON and loads it back through `FileStorage`.
- `TS-0014` migration and JSON repair tests remain linked evidence for the story; they are not sufficient alone to prove this direct save/load rule.
- `SC-0009-002/003` stay out of scope.

## 3. Проблема
GR-025 has direct implementation/test evidence but no executable scenario trace.

## 4. Цели дизайна
1. Execute exact current Gherkin without mutation.
2. Use the direct Save/Load JSON contract and preserve `TS-0014`.
3. Keep migration and corrupt-file recovery separate.
4. Update artifacts only after passing evidence.

## 5. Non-Goals
No changes to storage format, migration, repair, selected-path UX, production code or existing test annotations.

## 6. Решение
| Файл | Ответственность |
| --- | --- |
| `StormLocalJsonStorageExecutableSpecTests.cs` | Parse and execute exact scenario. |
| `LocalJsonStorageStepDefinitions.cs` | `SD-0123..SD-0126`. |
| `LocalJsonStorageContract.cs` | Reuses direct FileStorage JSON Save/Load test. |
| `StormStepDefinition.cs` | Test-local context. |
| Artifacts | `TS-0057`, links and metrics. |

## 7. Бизнес-правила
- Chosen local folder receives task JSON.
- Serialized contract retains explicit status history and completion criteria.
- The task loads back with the persisted values.

## 8. Интеграция
`StormFeatureParser -> StormScenarioRunner -> LocalJsonStorageStepDefinitions -> LocalJsonStorageContract -> FileStorageTaskStatusTests`.

## 9. Состояние
Only additive test-local context; no persisted schema mutation.

## 10. Rollback
Revert isolated test/artifact commit.

## 11. Тестирование
```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/StormLocalJsonStorageExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/FileStorageTaskStatusTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски
This bridge is trace evidence, not a replacement for the storage test. Full suite remains unconfirmed after the earlier timeout.

## 13. План
Post-SPEC review; add bridge; run targeted gates; sync artifacts; post-EXEC review; commit.

## 14. Открытые вопросы
Нет.

## 15. Профиль
STORM preserves AC/Gherkin; TUnit targeted evidence meets testing profile.

## 16. Файлы
SPEC, three bridge files, context, `storm.json` and six reports.

## 17. Было -> станет
| Field | Было | Станет |
| --- | --- | --- |
| Steps | `[]` | `SD-0123..SD-0126` |
| Status | `automated` | `passing` after gate |
| Tests | `TS-0014` | Existing plus `TS-0057` |
| Ratio | 31/45 | 32/45 |

## 18. Альтернативы
Migration-only evidence and Gherkin changes are rejected: they do not directly prove Save/Load or violate scope.

## 19. Review
### SPEC Linter / Rubric
Completeness, design, safety, testability, autonomy and profile: PASS. Rubric 30/30.

### Role-Based Review
Business analyst, tester, developer and delivery: PASS. The direct contract covers stored JSON and reload; no UX or delivery state changes.

### Post-SPEC Review
| Finding | Action | Status |
| --- | --- | --- |
| Migration evidence is indirect for save/load. | Invoke FileStorage JSON Save/Load test directly. | FIXED |
| Scenario could include corrupt-file recovery. | Keep `SC-0009-003` separate. | FIXED |
| Existing test is not labeled TS-0014. | Preserve TS-0014 and record direct test as TS-0057 evidence without annotations. | FIXED |

Stop decision: PASS. BLOCKER/HIGH findings отсутствуют.

### Post-EXEC Review
| Pass | Result |
| --- | --- |
| Scope/Evidence | PASS: изменены только test bridge, test-local context, SPEC и STORM artifacts. |
| Contract | PASS: FileStorage JSON save/load in the chosen folder executes from scenario text. |
| Validation | PASS: build errors 0, BDD 1/1, FileStorage 1/1, validator 0 errors/12 warnings/32 of 45 executable. |
| Residual risk | ACCEPTED: full suite PASS не заявлен после previous timeout без summary. |

Итог: `SC-0009-001` PASS; `ST-0009` now 1/3 step-executable.

## Approval
Active user goal автоматически подтверждает SPEC после post-SPEC PASS; EXEC разрешён.

## 20. Журнал
1. После `f850207` выбрана базовая storage scenario outside ST-0008.
2. Прочитаны feature, STORM trace and direct FileStorage Save/Load test.
3. Добавлены `TS-0057`, `SD-0123..SD-0126`, storage contract и test-local context; production code и annotations не менялись.
4. Build прошёл без errors; BDD и FileStorage gates прошли 1/1; validator: 0 errors, 12 warnings, 32/45 executable scenarios.
