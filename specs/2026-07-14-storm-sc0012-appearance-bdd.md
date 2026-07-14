# SPEC: Исполняемый BDD-мост appearance settings (SC-0012-001)

## 0. Метаданные
- Профиль: `storm-product-development`, small test-only executable BDD delivery.
- Baseline: `ST-0012`, `AC-0034`, `GR-034`, `SC-0012-001`; ветка `storm-bootstrap`.
- Ограничения: не менять production/UI code, `.feature`, existing tests или annotations; не менять persisted user configuration вне temporary fixture-local JSON.

## 1. Цель и границы
Сделать сценарий appearance settings исполняемым и доказать язык, тему, масштаб шрифта и fuzzy search без изменения product behavior. `TS-0064` исполняет `SD-0151..SD-0154`; `ST-0012` получает 1/3 executable scenarios, общий ratio становится 39/45. При потребности изменить product code, existing test или annotation остановиться и создать отдельную QUEST SPEC.

## 2. AS-IS / Проблема
- Theme, font size и language имеют точные `SettingsViewModelTests`.
- `SettingsViewModel.IsFuzzySearch` и Settings UI binding существуют, но отдельного settings-persistence test нет.
- Existing `RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode` доказывает UI effect `Search.IsFuzzySearch`, но не persistence setting.
- `SC-0012-001` linked-only, без step definitions.

## 3. TO-BE
- `SettingsAppearanceContract` вызовет неизменённые Theme/Font/Language methods через disposable `SettingsViewModelTests` fixture.
- Contract добавит only test-local fuzzy persistence check: temporary writable JSON config, `SettingsViewModel.IsFuzzySearch = true`, property/config assertions, dispose/delete in `finally`.
- Existing `RoadmapGraphUiTests.RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode` запускается как UI effect evidence без изменений.
- Новый contract/result, step definitions, executable spec test и minimal context; `storm.json`/six reports получают `TS-0064`.

## 4. Evidence Matrix
| Behavior | Evidence | Boundary |
| --- | --- | --- |
| Theme | `ThemeMode_PersistsChoiceAndCompatibilityShimReflectsSelection` | Existing VM test |
| Font | `FontSize_PersistsAndNormalizesConfiguredValue` | Existing VM test |
| Language | `LanguageMode_PersistsChoiceAndUpdatesLocalizedStatusText` | Existing VM test |
| Fuzzy setting | New contract persistence check plus existing RoadmapGraph UI test | Separates setting storage from search effect |

## 5. Non-Goals, Risks, Rollback
- Не менять UI layout/behavior, production, feature wording, annotations, config migration, external services или visual design.
- UI video не применимо: UI flow не меняется; existing headless test is next-best evidence.
- Full-suite PASS не заявляется из-за historic timeout without summary.
- Four flags are asserted independently; all fixtures/configs dispose in `finally`. Rollback deletes only new test/artifact files.

## 6. Acceptance and Validation
1. Exact four feature steps execute through `SD-0151..SD-0154`.
2. Theme, font, language and fuzzy persistence flags must all pass.
3. Existing fuzzy UI effect test passes unchanged.
4. Build, BDD 1/1, four VM/persistence checks 4/4, fuzzy UI 1/1, validator and `git diff --check` pass.
5. Production/feature/existing annotations diffs remain empty.

Commands: `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false`; individual `--treenode-filter` commands for `StormSettingsAppearanceExecutableSpecTests`, `ThemeMode_PersistsChoiceAndCompatibilityShimReflectsSelection`, `FontSize_PersistsAndNormalizesConfiguredValue`, `LanguageMode_PersistsChoiceAndUpdatesLocalizedStatusText`, the new `SettingsAppearanceContractTests.FuzzySearch_PersistsChoice`, and `RoadmapGraph_SearchText_HighlightsAndClearsMatchingNode`; `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`; `git diff --check`.

## 7. Files and Alternatives
New: this SPEC, `SettingsAppearanceContract.cs`, `SettingsAppearanceStepDefinitions.cs`, `StormSettingsAppearanceExecutableSpecTests.cs`. Modified: `StormStepDefinition.cs`, `storm.json`, six reports. Rejected alternative: alter Settings UI or feature text; no behavior change requires either.

## 8. Quality Gate and Review
| Area | Status | Comment |
| --- | --- | --- |
| Scope, evidence, safety, testability | PASS | Exact scope, files, validation and stop rule are defined |
| Domain | PASS | Persistence and UI effect are explicitly separate evidence layers |
| UX | Не применимо | No UI change |
| Tester | PASS | Exact individual methods and headless UI evidence are named |
| Developer / delivery | PASS | Temporary config cleanup and no external state are explicit |

### Post-SPEC Review
- Статус: PASS после исправления.
- Finding fixed: draft named command categories rather than all concrete methods; exact filters are now listed.
- Scope/Evidence, Contract, Adversarial risk and Role-based passes: PASS.
- Decision: active workflow auto-approval permits EXEC.

### Post-EXEC Review
- Статус: PASS.
- Evidence: Release build с 69 existing warnings и 0 errors; BDD 1/1; theme/font/language/fuzzy persistence 4/4; existing headless fuzzy UI 1/1; STORM validator 0 errors/15 warnings/39 of 45; production/feature diff empty.
- Scope/Evidence, Contract, Adversarial risk and Role-based passes: PASS.
- No findings: new fuzzy check is fixture-local, disposes writable configuration and complements rather than replaces the existing UI effect test.

## Approval and Action Log
Автоматическое подтверждение active workflow после PASS review; явная фраза: "Спеку подтверждаю".

| Phase | Decision | Next action |
| --- | --- | --- |
| SPEC | Use four independent appearance flags | Review, correct, auto-EXEC |
| SPEC review | List exact target methods | Execute test-only bridge |
| EXEC review | Verify test-only bridge and artifact sync | Commit SC-0012-001 |
