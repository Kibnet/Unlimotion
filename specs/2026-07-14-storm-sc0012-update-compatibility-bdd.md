# SPEC: Исполняемый BDD-мост update/compatibility Settings (SC-0012-003)

## 0. Метаданные
- Тип (профиль): `delivery-task`; `storm-product-development` + `dotnet-desktop-client` + `ui-automation-testing`.
- Владелец: STORM `/storm:cover` backlog.
- Масштаб: small.
- Целевое семейство / behavior baseline: central `model-behavior-baseline`; product behavior не меняется.
- Поверхность / runtime: Codex desktop, локальная .NET/Avalonia test surface; модель не влияет на продуктовый output.
- Eval baseline / evidence: feature `SC-0012-003`, existing VM и Avalonia.Headless tests; test output хранится в стандартном TUnit report.
- Целевой релиз / ветка: `storm-bootstrap`, локальный coverage commit.
- Ограничения: не менять production/UI code, `.feature`, existing tests или их annotations, `.csproj`, workflows, конфигурацию, внешние update/git data.
- Связанные ссылки: `ST-0012`, `AC-0036`, `GR-036`, `SC-0012-003`, `TS-0008`, `TS-0015`, `CN-0004`, `CN-0005`, `CN-0007`, `CN-0008`.

## 1. Overview / Цель
Связать неизменяемый Gherkin scenario `SC-0012-003` с проходящими update-control и package-compatibility evidence, доведя `ST-0012` до `3/3` и общий executable ratio до `41/45`.

Outcome contract:
- Success means: feature text выполняется через `SD-0159..SD-0162` и `TS-0066`; update controls и compatibility checks подтверждены независимыми existing tests.
- Итоговый артефакт / output: test-only contract, step definitions, executable spec, `storm.json` и шесть STORM reports.
- Stop rules: остановиться и вынести отдельный delivery-task через QUEST, если для прохождения нужны production change, feature change, existing-test/annotation change или external update/install access.

## 2. Текущее состояние (AS-IS)
- `SC-0012-003` linked-only: `TS-0008/TS-0015`, без step definitions, статус `automated`.
- `SettingsViewModelTests` содержит изолированные update-state checks и требует `Dispose()` для temporary config.
- `SettingsControlResponsiveUiTests.SettingsControl_UpdateSection_ShowsVersionAndDownloadsAvailableUpdate` сам создаёт и освобождает headless session/window/fixture.
- `PackageUpdateCompatibilityUiTests.RoadmapDropAndFolderPickerCompatibility_Work` сам создаёт и освобождает headless session/fixture и восстанавливает folder picker.
- Последняя итерация закрыла `SC-0012-002`; остаётся только этот scenario внутри `ST-0012`.

## 3. Проблема
Существующее update/compatibility evidence связано с AC, но feature text не является исполняемой спецификацией и не имеет собственного Scenario -> Test -> Step Definition bridge.

## 4. Цели дизайна
- Повторно использовать существующие isolated tests без изменения их кода.
- Оставить contract как тонкий orchestration layer с явными independent flags.
- Сериализовать новый executable spec в группе `AvaloniaHeadless`, поскольку он вызывает два headless UI test methods.
- Сохранить acceptance criteria отдельными от Gherkin и все existing links.

## 5. Non-Goals
- Новое update/install, compatibility, storage или UI behavior.
- Изменения package/platform projects, drag/drop, folder picker, app update service или release pipeline.
- Полный suite PASS, UI video, network/update download, тестовые annotations существующих классов.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `SettingsUpdateCompatibilityContract.cs`: вызывает existing VM/UI checks, освобождает только `SettingsViewModelTests`, возвращает flags.
- `SettingsUpdateCompatibilityStepDefinitions.cs`: связывает exact feature steps с contract через `SD-0159..SD-0162`.
- `StormSettingsUpdateCompatibilityExecutableSpecTests.cs`: парсит scenario/tags и исполняет steps; содержит standard `AvaloniaHeadless` serialization attributes только на новом test class.
- `StormStepDefinition.cs`: minimal context fields для этого scenario.
- `storm.json` и reports: canonical traceability/evidence/metrics sync.

### 6.2 Детальный дизайн
- Contract вызывает `Updates_AreDisabled_WhenUpdateServiceIsUnsupported`, `DownloadUpdateAsync_SetsReadyToApply_WhenUpdateWasFound`, `ApplyUpdateAsync_CallsUpdateServiceRestart_WhenUpdateIsReady`, `SettingsControl_UpdateSection_ShowsVersionAndDownloadsAvailableUpdate` и `RoadmapDropAndFolderPickerCompatibility_Work`.
- `SettingsViewModelTests` создаётся один раз и освобождается в `finally`; UI test instances non-disposable and self-contained.
- Результат содержит flags `UpdateControlStatePassed`, `UpdateControlsUiPassed`, `CompatibilityUiPassed`; `AssertAsync` требует все true.
- Visual planning artifact / UI video: не применимо, visual behavior не меняется; passing existing headless UI checks являются next-best evidence.
- Performance: не требуется, только sequential reuse local tests.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| `SC-0012-003` | Открывает Settings и проверяет update/compatibility flow | Disabled/ready update controls отражают state, package-compatible drag/drop/folder picker сохраняют работу | BDD 1/1, VM update checks, two existing headless UI checks | `AC-0036` |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Update service unsupported | Open Settings | All update controls disabled | Disabled state remains safe | VM check |
| Update available | Check/download/apply | Ready-to-apply and install transition | User-action-required keeps install action | VM + Settings UI |
| Platform compatibility flow | Drop relation/open folder picker | Link and selected path work | Headless fixture cleans state | Existing compatibility UI |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Evidence scope | agent | 3 VM + 2 self-contained UI methods | 0.92 | Existing evidence may miss untested platform runtime | Нет |
| UI serialization | agent | New BDD test joins `AvaloniaHeadless` limiter | 0.98 | Parallel session race | Нет |
| Production changes | agent | Prohibited; stop if required | 1.00 | Cannot close gap without separate SPEC | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
Не применимо: bridge uses fixture-local temporary config and headless sessions only; no external config/data is mutated.

## 7. Бизнес-правила / Алгоритмы
1. Unsupported update service disables check/download/apply.
2. Available update can be downloaded and applied; user-action-required retains retryable ready state.
3. Compatibility flow preserves relation link and folder picker suggested path.
4. Feature scenario passes only after all three contract flags pass.

## 8. Точки интеграции и триггеры
- `StormFeatureParser` reads unchanged scenario.
- `StormScenarioRunner` invokes `SD-0159..SD-0162`.
- TUnit invokes new class inside `AvaloniaHeadless` serialization group.

## 9. Изменения модели данных / состояния
- Только ephemeral `StormScenarioContext` fields and contract result flags.
- Persisted product state не меняется.

## 10. Миграция / Rollout / Rollback
- Миграция/rollout: не применимо.
- Rollback: удалить только new contract/steps/executable spec/SPEC, context fields и generated STORM links/reports.

## 11. Тестирование и критерии приёмки
1. Exact feature steps execute through `SD-0159..SD-0162` and `TS-0066`.
2. Unsupported, download-ready, apply and both UI compatibility paths pass independently.
3. `SC-0012-003` becomes `passing`; `ST-0012` becomes `3/3`; metrics become `41/45`, 162 step definitions, reuse `165/165`.
4. Build, BDD 1/1, VM 3/3, Settings update UI 1/1, package compatibility UI 1/1, artifact validator and `git diff --check` pass.
5. Production, `.feature`, existing tests/annotations, projects and workflows have no diff.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| `AC-0036`: safe update states | Three selected `SettingsViewModelTests` | New BDD step execution | `TS-0066`, TUnit output | — |
| `AC-0036`: Settings controls | `SettingsControl_UpdateSection_ShowsVersionAndDownloadsAvailableUpdate` | Headless run | TUnit output | — |
| `AC-0036`: compatibility | `RoadmapDropAndFolderPickerCompatibility_Work` | Headless run | TUnit output | — |

Команды проверки:
```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/StormSettingsUpdateCompatibilityExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/SettingsViewModelTests/Updates_AreDisabled_WhenUpdateServiceIsUnsupported" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/SettingsViewModelTests/DownloadUpdateAsync_SetsReadyToApply_WhenUpdateWasFound" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/SettingsViewModelTests/ApplyUpdateAsync_CallsUpdateServiceRestart_WhenUpdateIsReady" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/SettingsControlResponsiveUiTests/SettingsControl_UpdateSection_ShowsVersionAndDownloadsAvailableUpdate" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases
- UI test parallelism: mitigated by new class attributes.
- Fixture cleanup: VM disposed in `finally`; UI methods retain their own cleanup.
- Full suite may timeout without summary; it is not passing evidence.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Bridge may change product behavior | It calls update/install flows | Only existing fakes/fixtures; no production diff allowed | mitigated |
| UI bridge can be flaky in parallel | Two headless test methods run from one test | Match repository `AvaloniaHeadless` limiter | mitigated |
| AC becomes Gherkin | Scenario wording is broad | AC stays unchanged; Gherkin only gains executable links | mitigated |

### Rework Prevention Checklist
- User-visible scenario and state matrix are explicit.
- Every AC rule has a named check.
- Decision ledger has no user-owned blocking choice.
- Review roles and objections are completed below.

## 13. План выполнения
1. Create test-only contract, step definitions, scenario test and minimal context fields.
2. Run build and BDD/direct evidence gates sequentially.
3. Update canonical artifacts/reports; run `/storm:bdd-sync`, `/storm:bdd-lint` evidence through validator.
4. Post-EXEC review, correct findings, commit separately.

## 14. Открытые вопросы
Нет. External update/install runtime и full-suite PASS намеренно вне scope.

## 15. Соответствие профилю
- Профили: `storm-product-development`, `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования: Gherkin remains layer between AC and tests; traceability uses stable IDs; test/code scope goes through QUEST; UI-backed bridge preserves UI test serialisation.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-14-storm-sc0012-update-compatibility-bdd.md` | New QUEST spec | Auditable delivery gate |
| `src/Unlimotion.Test/SettingsUpdateCompatibilityContract.cs` | New | Existing evidence orchestration |
| `src/Unlimotion.Test/StormBdd/SettingsUpdateCompatibilityStepDefinitions.cs` | New | `SD-0159..SD-0162` |
| `src/Unlimotion.Test/StormSettingsUpdateCompatibilityExecutableSpecTests.cs` | New | Scenario runner |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Modify | Minimal ephemeral context |
| `docs/product/storm.json`; six reports | Modify | Canonical BDD traceability/metrics |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0012-003` | linked-only, `automated`, no steps | `passing`, `TS-0066`, `SD-0159..SD-0162` |
| `ST-0012` | 2/3 executable | 3/3 executable |
| Behavior coverage | 40/45 | 41/45 |

## 18. Альтернативы и компромиссы
- Вариант: изменить existing tests или добавить real update/network flow.
- Плюсы: потенциально шире coverage.
- Минусы: выходит за artifact-gap scope и нарушает constraints.
- Выбор: thin bridge over isolated existing tests, потому что это закрывает executable-spec gap без behavior change.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Scope, stop rules, no open questions |
| B. Качество дизайна | 6-10 | PASS | Contract ownership and lifecycle explicit |
| C. Безопасность изменений | 11-13 | PASS | No production/existing-test mutation |
| D. Проверяемость | 14-16 | PASS | AC-to-test matrix and commands specified |
| E. Готовность к автономной реализации | 17-19 | PASS | Files and exact methods named |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/UI requirements included |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | One scenario, explicit stop rules |
| 2. Понимание текущего состояния | 5 | Existing evidence and lifecycle inspected |
| 3. Конкретность целевого дизайна | 5 | IDs, files, flags and sequence fixed |
| 4. Безопасность (миграция, откат) | 5 | Test-only rollback and no external data |
| 5. Тестируемость | 5 | Direct and BDD gates are named |
| 6. Готовность к автономной реализации | 5 | No user-owned decision remains |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does update/compatibility flow remain protected? | PASS | None |
| UX / designer | applicable | Do visible controls retain disabled/ready/error states? | PASS | Existing headless UI evidence retained |
| Tester / validation | applicable | Are negative and UI paths independently evidenced? | PASS | Exact five methods named |
| Developer / architect | applicable | Are fixture ownership and BDD boundary coherent? | PASS | `finally` + limiter specified |
| Delivery / operations / security | not applicable | No deploy/config/secret/runtime change | PASS | No external update/install access |

### Post-SPEC Review
- Статус: PASS после исправления.
- Scope reviewed: this SPEC; central `AGENTS.md`, routing matrix, `quest-mode`, `quest-governance`, spec linter/rubric/review-loop; `storm-product-development`, `dotnet-desktop-client`, `ui-automation-testing`; unchanged `SC-0012-003`, `AC-0036`, `GR-036`; five selected tests and their fixture cleanup; planned six files plus reports. Worktree contains only this new SPEC.
- Decision: active user auto-approval permits EXEC.
- Review passes:
  - Scope/Evidence: PASS. Scenario, tags, existing test names, feature wording and planned IDs are exact.
  - Contract: PASS. New bridge only orchestrates existing fakes/fixtures; `SettingsViewModelTests.Dispose()` and both UI-method `try/finally` boundaries are explicit.
  - Adversarial risk: PASS after fix. New BDD class will join `AvaloniaHeadless`; external update/network/product code are excluded.
  - Role-Based: PASS. Domain/UX/tester/developer reviews are above; delivery is not applicable.
  - Fix and re-review: PASS. Added exact validation commands, then rechecked scope, contract and risks.
  - Stop decision: PASS; no user-owned decision and no external-state requirement.
- Evidence inspected: feature lines 25-31; `SettingsViewModelTests` update checks and `Dispose`; Settings update section headless session plus `finally`; package compatibility session plus `finally`; existing `AvaloniaHeadless` attributes.
- Depth checklist:
  - Scope drift / unrelated changes: only this SPEC before EXEC.
  - Acceptance criteria: `AC-0036` remains unchanged and maps to five checks.
  - User-observable scenarios / Decision ledger / Expected objections: explicit, no blocking choice.
  - Validation evidence: exact commands added.
  - Unsupported claims: no full-suite, visual-video or real update/install claim.
  - Regression / edge case: unsupported service, user-action-required state, headless cleanup and parallelism covered.
  - Comments/docs/changelog: only Russian SPEC and later STORM reports are needed; no changelog impact.
  - Hidden contract change: prohibited; bridge has only ephemeral context/result state.
  - Manual-review challenge: missing `AvaloniaHeadless` serialization would create a flaky UI bridge; it is now explicit.
- No-findings justification: after the validation-command correction every required pre-approval section, scope boundary and reproducible evidence link is present.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | evidence | Draft named checks but omitted exact commands. | Add individual TUnit/build/validator commands. | fixed |
| — | scope/design/risk | Нет находок после повторного review. | — | closed |

- Fixed before continuing: exact command gate added.
- Checks rerun: spec linter, rubric and review-loop checklist reviewed against amended SPEC.
- Needs human: нет; approval already active.
- Residual risks / follow-ups: full suite remains unconfirmed by historical timeout without summary.

### Post-EXEC Review
- Статус: PASS.
- Scope reviewed: approved SPEC; clean pre-EXEC worktree; new contract, step definitions, executable spec and context fields; `storm.json`, six reports and exact feature scenario; production/UI/feature/existing-test diffs are empty.
- Decision: changes satisfy the approved test-only scope and can be committed.
- Review passes:
  - Scope/Evidence: PASS. `TS-0066`, `SD-0159..SD-0162`, five direct checks and metrics `41/45` agree.
  - Contract: PASS. All existing calls pass before flags are set; VM fixture is disposed; new BDD class serializes both headless UI calls.
  - Adversarial risk: PASS. Unsupported service, update download/apply state, permission-required UI state and package folder-picker cleanup are exercised; no real network/install path is claimed.
  - Role-Based: PASS. UI state evidence is headless fallback because no UI behavior/layout changed; domain and developer contracts are preserved.
  - Fix and re-review: PASS. No post-EXEC code finding; artifact validation wording was corrected from expected to actual and validator rerun.
  - Stop decision: PASS; no separate delivery-task is required.
- Evidence inspected: Build Release errors 0 (69 existing warnings); BDD 1/1; VM update 3/3; Settings update UI 1/1; package compatibility UI 1/1; validator 0 errors/16 warnings; `git diff --check`.
- Depth checklist:
  - Scope drift / unrelated changes: no unrelated files.
  - Acceptance criteria: `AC-0036` remains unchanged and maps to five checks.
  - User-observable scenarios / Acceptance-to-test matrix / Expected objections: all checks pass; no product behavior is added.
  - Validation evidence: direct and bridge TUnit output plus validator recorded.
  - Unsupported claims: full suite and real package/update runtime remain unclaimed.
  - Regression / edge case: test class serialisation, fixture cleanup and disabled/error states checked.
  - Comments/docs/changelog: Russian SPEC and reports updated; no changelog needed.
  - Hidden contract change: none; only test-local context/result fields added.
  - Manual-review challenge: bridge must not allow parallel headless sessions; attributes match existing repository pattern.
- No-findings justification: code follows established BDD contract pattern, targeted evidence passes and canonical traceability is structurally validated.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Draft artifact report said validator was expected, though it had run. | Record actual validator result. | fixed |
| - | spec compliance / regression / tests / docs | Нет находок после re-review. | - | closed |

- Fixed before final report: validator evidence wording updated.
- Checks rerun: artifact validator and `git diff --check`.
- Validation evidence: build errors 0; BDD 1/1; VM 3/3; UI 2/2; validator 0/16.
- Unrelated changes: none.
- Needs human: нет.
- Residual risks / follow-ups: historic full-suite timeout remains outside this slice.

## Approval
Active workflow auto-approval after PASS review; canonical phrase: "Спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Select update/compatibility BDD bridge | 0.92 | Нет | Review SPEC | Нет | User auto-approval already active | Existing VM/UI evidence covers AC-0036 without product changes | This SPEC |
| SPEC | Review and correct validation gate | 0.98 | Нет | EXEC | Нет | User auto-approval already active | Exact commands and headless lifecycle evidence close the only actionable finding | This SPEC |
| EXEC | Implement and validate BDD bridge | 0.97 | Нет | Post-EXEC review and commit | Нет | User auto-approval already active | New bridge reuses five passing checks; validator confirms 41/45 | Test files, storm artifacts, reports |
