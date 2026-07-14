# SPEC: Исполняемый BDD-мост desktop shell и packaging (SC-0015-001)

## 0. Метаданные
- Тип (профиль): `delivery-task`; `storm-product-development` + `dotnet-desktop-client` + `ui-automation-testing`.
- Владелец: STORM `/storm:cover` backlog.
- Масштаб: small.
- Целевое семейство / behavior baseline: central `model-behavior-baseline`; продукт, CI и packaging behavior не меняются.
- Поверхность: Codex desktop, локальная .NET/Avalonia test surface.
- Effective runtime: не применимо; задача не меняет model/runtime output продукта.
- Eval baseline / evidence: неизменяемый scenario `SC-0015-001`, desktop project/workflow contract, startup/update/package Avalonia.Headless tests и TUnit output.
- Целевой релиз / ветка: `storm-bootstrap`, локальный coverage commit.
- Ограничения: не менять production/UI code, `.feature`, existing tests или их annotations, `.csproj`, workflows, конфигурацию, release assets или external update data.
- Связанные ссылки: `ST-0015`, `AC-0041`, `GR-041`, `SC-0015-001`, `TS-0011`, `TS-0015`, `CN-0004`, `CN-0007`, `CN-0008`.

## 1. Overview / Цель
Связать неизменяемый Gherkin scenario `SC-0015-001` с фактическим evidence desktop WinExe, Windows Velopack packaging contract и startup/update/package compatibility, доведя `ST-0015` до `2/3` и общий executable ratio до `44/45`.

Outcome contract:
- Success means: feature text выполняется через `SD-0171..SD-0174` и `TS-0069`; project/workflow contract и existing headless checks проходят.
- Итоговый артефакт / output: test-only contract, step definitions, executable spec, `storm.json` и шесть STORM reports.
- Stop rules: остановиться и вынести отдельный delivery-task через QUEST, если для прохождения нужны production/project/workflow change, existing-test/annotation change, publish/release execution или network access.

## 2. Текущее состояние (AS-IS)
- `SC-0015-001` имеет links `TS-0011/TS-0015`, но остаётся `automated` без executable step definitions.
- `Unlimotion.Desktop.csproj` declares `OutputType=WinExe`, `Avalonia.Desktop` и `Velopack`; `windows-packaging.yml` publishes the desktop project and packs `Unlimotion.Desktop.exe` through Velopack.
- `SingleViewStartupUiTests.SingleViewStartup_ConnectsExistingTaskStorage` verifies desktop single-view startup and connected task storage; `SingleViewStartup_ReplaysStartupUpdateCheck_WhenUpdateServiceAttachesAfterStartup` verifies deferred update attachment.
- `PackageUpdateCompatibilityUiTests.RoadmapDropAndFolderPickerCompatibility_Work` verifies compatibility-sensitive desktop UI paths and cleans its own session/fixture.
- Release publication and real updates intentionally have no local claim.

## 3. Проблема
Существующее project/workflow and UI evidence подтверждает AC-0041, но Gherkin scenario не исполняется как самостоятельная specification и не имеет Scenario -> Test -> Step Definition bridge.

## 4. Цели дизайна
- Проверить project/workflow contract read-only, без запуска publish/release.
- Повторно использовать existing headless startup/update/package checks без изменения их кода.
- Сериализовать новый executable spec в `AvaloniaHeadless`.
- Сохранить acceptance criteria отдельными от Gherkin и existing links `TS-0011/TS-0015`.

## 5. Non-Goals (чего НЕ делаем)
- Изменение desktop project, Windows workflow, Velopack, release tag, package output или update service.
- Реальный release/publish/upload, network update access, UI design or platform runtime claim.
- Изменение production files, existing tests/annotations, проектов или workflows.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `DesktopShellPackagingContract.cs`: read-only asserts project/workflow markers and invokes selected existing headless tests.
- `DesktopShellPackagingStepDefinitions.cs`: связывает exact feature steps с contract через `SD-0171..SD-0174`.
- `StormDesktopShellPackagingExecutableSpecTests.cs`: парсит scenario/tags и исполняет steps в `AvaloniaHeadless` группе.
- `StormStepDefinition.cs`: получает minimal ephemeral context fields.
- `storm.json` и reports: получают canonical traceability/evidence/metrics sync.

### 6.2 Детальный дизайн
- Contract reads `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` and `.github/workflows/windows-packaging.yml`; requires `WinExe`, `Avalonia.Desktop`, `Velopack`, desktop `dotnet publish`, `vpk pack` and `--mainExe Unlimotion.Desktop.exe`.
- Contract invokes `SingleViewStartup_ConnectsExistingTaskStorage`, `SingleViewStartup_ReplaysStartupUpdateCheck_WhenUpdateServiceAttachesAfterStartup` and `RoadmapDropAndFolderPickerCompatibility_Work`.
- Result contains `DesktopPackagingContractPassed`, `StartupAndUpdatePassed`, `PackageCompatibilityUiPassed`; `AssertAsync` requires all true.
- Visual planning/video evidence: не применимо; UI behavior/layout не меняется, targeted headless tests are next-best evidence.
- Performance: project/workflow reads are local; no publish/release/network operation.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| `SC-0015-001` | Maintainer builds/tests desktop delivery surface | Desktop entry remains WinExe with packaging/update contract and startup UI paths | BDD 1/1, source contract, startup/update/package UI checks | `AC-0041` |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Desktop project/workflow source | Contract read | WinExe/Avalonia/Velopack/publish/main exe markers present | Missing marker fails locally | Read-only contract |
| Single-view shell | Startup and late update-service attach | Storage connects once, update check replays safely | Fixture-local fake service | Existing startup tests |
| Compatibility-sensitive desktop UI | Package compatibility action | Relation drop and folder picker remain functional | Headless fixture cleanup | Existing UI test |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Packaging evidence | agent | Read-only project/workflow contract plus existing UI tests | 0.95 | Does not prove external release upload | Нет |
| UI serialisation | agent | New BDD class joins `AvaloniaHeadless` limiter | 0.99 | Parallel session race | Нет |
| Real release | agent | Excluded; stop if required | 1.00 | Cannot claim published artifact | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Desktop entry | `Unlimotion.Desktop.csproj` | None; read-only assertion | None | `WinExe`, packages |
| Windows packaging | `windows-packaging.yml` | None; read-only assertion | None | publish + Velopack main exe markers |
| Startup/update UI | existing Headless tests | None | Fixture-local fakes only | Targeted TUnit |

## 7. Бизнес-правила / Алгоритмы (если есть)
1. Desktop shell declares the WinExe/Avalonia/Velopack contract.
2. Windows workflow packages that desktop executable through the release pipeline.
3. Startup connects storage and safely replays an update check when service attaches.
4. Feature scenario passes only after source contract and all UI flags pass.

## 8. Точки интеграции и триггеры
- `StormFeatureParser` reads unchanged `SC-0015-001`.
- `StormScenarioRunner` invokes `SD-0171..SD-0174`.
- TUnit invokes new class inside `AvaloniaHeadless` serialization group.

## 9. Изменения модели данных / состояния
- Только ephemeral `StormScenarioContext` fields and contract result flags.
- Persisted product, project and workflow state не меняется.

## 10. Миграция / Rollout / Rollback
- Миграция/rollout: не применимо.
- Rollback: удалить только new contract/steps/executable spec/SPEC, context fields и generated STORM links/reports.

## 11. Тестирование и критерии приёмки
1. Exact feature steps execute through `SD-0171..SD-0174` and `TS-0069`.
2. Read-only project/workflow, startup/update and package compatibility paths pass independently.
3. `SC-0015-001` becomes `passing`; `ST-0015` becomes `2/3`; metrics become `44/45`, 174 step definitions and reuse `177/177`.
4. Build, BDD 1/1, startup/update 2/2, package UI 1/1, artifact validator and `git diff --check` pass.
5. Production, `.feature`, existing tests/annotations, projects and workflows have no diff.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| `AC-0041`: WinExe/package contract | New `DesktopShellPackagingContract` | New BDD step execution | `TS-0069`, source contract | No publish needed |
| `AC-0041`: startup/update flow | Two selected `SingleViewStartupUiTests` | Headless run | TUnit output | — |
| `AC-0041`: package compatibility | `RoadmapDropAndFolderPickerCompatibility_Work` | Headless run | TUnit output | — |

Команды проверки:
```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet build src\Unlimotion.Desktop\Unlimotion.Desktop.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/StormDesktopShellPackagingExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/SingleViewStartupUiTests/SingleViewStartup_ConnectsExistingTaskStorage" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/SingleViewStartupUiTests/SingleViewStartup_ReplaysStartupUpdateCheck_WhenUpdateServiceAttachesAfterStartup" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/PackageUpdateCompatibilityUiTests/RoadmapDropAndFolderPickerCompatibility_Work" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases
- Source contract can prove configuration, not a published artifact; no release claim is made.
- UI test parallelism is mitigated by new class attributes.
- Full suite may timeout without summary; it is not passing evidence.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Source read is not a package build | Workflow contract alone could be stale | Require local Desktop Release build plus source contract | mitigated |
| Bridge may trigger real update/release | Update/package names imply external access | Existing fixture fakes only; publish/upload prohibited | mitigated |
| UI bridge can be flaky in parallel | Three headless methods run from bridge | Match repository limiter | mitigated |

### Rework Prevention Checklist
- Desktop delivery scenario and state matrix are explicit.
- Every AC-0041 aspect has source or test evidence.
- Decision ledger has no user-owned blocking choice.
- Role-based review is completed below.
- EXEC has direct BDD and independent evidence gates.

## 13. План выполнения
1. Create test-only source/UI contract, step definitions, scenario test and minimal context fields.
2. Run test and desktop builds plus BDD/direct evidence gates sequentially.
3. Update canonical artifacts/reports; run `/storm:bdd-sync`, `/storm:bdd-lint` evidence through validator.
4. Post-EXEC review, correct findings, commit separately.

## 14. Открытые вопросы
Нет. External release publication и update download намеренно вне границ.

## 15. Соответствие профилю
- Профили: `storm-product-development`, `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля: Gherkin remains a layer between AC and tests; stable IDs; QUEST gate; UI-backed bridge preserves headless serialisation; delivery evidence does not overclaim a release.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-14-storm-sc0015-desktop-shell-bdd.md` | New QUEST spec | Auditable delivery gate |
| `src/Unlimotion.Test/DesktopShellPackagingContract.cs` | New | Project/workflow and existing evidence orchestration |
| `src/Unlimotion.Test/StormBdd/DesktopShellPackagingStepDefinitions.cs` | New | `SD-0171..SD-0174` |
| `src/Unlimotion.Test/StormDesktopShellPackagingExecutableSpecTests.cs` | New | Scenario runner |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Modify | Minimal ephemeral context |
| `docs/product/storm.json`; six reports | Modify | Canonical BDD traceability/metrics |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0015-001` | linked-only, `automated`, no steps | `passing`, `TS-0069`, `SD-0171..SD-0174` |
| `ST-0015` | 1/3 executable | 2/3 executable |
| Behavior coverage | 43/45 | 44/45 |

## 18. Альтернативы и компромиссы
- Вариант: запустить real publish/release или изменить workflow/tests.
- Плюсы: потенциально сильнее release evidence.
- Минусы: network/release side effects, выходит за artifact-gap scope и constraints.
- Почему выбранное решение лучше в контексте этой задачи: read-only contract plus existing startup/update/package tests closes executable-spec gap without release behavior change.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Scope, stop rules, no open questions |
| B. Качество дизайна | 6-10 | PASS | Source/UI responsibilities and lifecycle explicit |
| C. Безопасность изменений | 11-13 | PASS | No production/project/workflow/existing-test mutation |
| D. Проверяемость | 14-16 | PASS | AC-to-test matrix and commands specified |
| E. Готовность к автономной реализации | 17-19 | PASS | Files and exact methods/markers named |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/UI requirements included |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | One desktop delivery scenario, explicit stop rules |
| 2. Понимание текущего состояния | 5 | Project/workflow and existing evidence inspected |
| 3. Конкретность целевого дизайна | 5 | IDs, markers, files, flags and sequence fixed |
| 4. Безопасность (миграция, откат) | 5 | Test-only rollback and no release side effect |
| 5. Тестируемость | 5 | Build, direct and BDD gates are named |
| 6. Готовность к автономной реализации | 5 | No user-owned decision remains |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does desktop delivery retain explicit packaging/update boundaries? | PASS | Real release remains excluded |
| UX / designer | applicable | Do startup and compatibility UI paths retain behaviour? | PASS | Existing headless evidence retained |
| Tester / validation | applicable | Are source contract, startup/update and package paths independent? | PASS | Exact checks and build named |
| Developer / architect | applicable | Are read-only parsing and UI lifecycle coherent? | PASS | `finally` ownership and limiter specified |
| Delivery / operations / security | applicable | Are release/network/secrets risks excluded? | PASS | No publish/upload or credential use |

### Post-SPEC Review
- Статус: PASS после исправления.
- Scope reviewed: this SPEC; central instruction stack and local UI override; `storm-product-development`, `dotnet-desktop-client`, `ui-automation-testing`; unchanged `SC-0015-001`, `AC-0041`, `GR-041`; project/workflow markers and selected tests; planned files plus reports.
- Decision: active user auto-approval permits EXEC.
- Review passes:
  - Scope/Evidence: PASS. Scenario, tags, existing test names, project/workflow markers and planned IDs are exact.
  - Contract: PASS. New source assertions are read-only; selected headless methods own fixture cleanup.
  - Adversarial risk: PASS after fix. Source contract is backed by a Desktop Release build and startup/update/package UI tests; external release is excluded.
  - Role-Based: PASS. All relevant roles are above, including delivery/security because packaging workflow is inspected.
  - Fix and re-review: PASS. Draft risked treating workflow text as build proof; local desktop build is now mandatory.
  - Stop decision: PASS; no user-owned decision and no external-state requirement.
- Evidence inspected: feature lines 9-14; desktop project `WinExe`/packages; workflow publish/Velopack commands; single-view startup/update tests; package compatibility fixture and cleanup; existing `AvaloniaHeadless` attributes.
- Depth checklist:
  - Scope drift / unrelated changes: only this SPEC before EXEC.
  - Acceptance criteria: `AC-0041` remains unchanged and maps to source/build/UI checks.
  - User-observable scenarios / Decision ledger / Expected objections: explicit, no blocking choice.
  - Validation evidence: exact build and test commands added.
  - Unsupported claims: no published release, UI video or real update claim.
  - Regression / edge case: missing packaging marker, startup update attachment, dialog/drop compatibility and headless parallelism covered.
  - Comments/docs/changelog: only Russian SPEC and later STORM reports are needed; no changelog impact.
  - Hidden contract change: prohibited; bridge has only ephemeral context/result state.
  - Manual-review challenge: source-only evidence would overclaim a build; the desktop build command is now required.
- No-findings justification: after adding the build gate every pre-approval section, boundary and reproducible evidence link is present.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | evidence | Draft could have claimed workflow configuration as a successful desktop build. | Add local Desktop Release build gate and avoid release claim. | fixed |
| — | scope/design/risk | Нет находок после повторного review. | — | closed |

- Fixed before continuing: Desktop Release build added to validation gate.
- Checks rerun: spec linter, rubric and review-loop checklist reviewed against amended SPEC.
- Needs human: нет; approval already active.
- Residual risks / follow-ups: historic full-suite timeout remains outside this slice.

### Post-EXEC Review
- Статус: PASS.
- Scope reviewed: test-only `SC-0015-001` bridge, generated STORM artifacts and six reports; production/UI code, `.feature`, project files, workflows and existing test annotations.
- Findings: нет. `DesktopShellPackagingContract` only reads source contracts and reuses existing headless checks. `StormDesktopShellPackagingExecutableSpecTests` uses the repository `AvaloniaHeadless` serialization pattern; context fields are ephemeral.
- Evidence: Test Release build passed with 69 existing warnings and 0 errors; Desktop Release build passed with 0 errors; BDD passed 1/1; startup/update/package UI checks passed 3/3; artifact validator passed with 0 errors and 17 known duplicate-step warnings; `git diff --check` passed.
- Residual risk: the contract proves local project/workflow configuration and isolated headless paths, not a published release or external update download. Historic full-suite timeout has no summary and is not treated as PASS evidence.
- Decision: no corrective change is needed; the slice is ready for its isolated commit.

## Approval
Active workflow auto-approval after PASS review; canonical phrase: "Спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Select desktop-shell BDD bridge | 0.95 | Нет | Review SPEC | Нет | User auto-approval already active | Existing project/workflow/startup/package evidence covers AC-0041 without delivery change | This SPEC |
| SPEC | Review and add build gate | 0.98 | Нет | EXEC | Нет | User auto-approval already active | Desktop build prevents a configuration-only coverage claim | This SPEC |
| EXEC | Implement and validate SC-0015-001 | 0.98 | Нет | Commit and continue with SC-0015-003 | Нет | User auto-approval already active | Test-only bridge passed its BDD, build, UI and artifact gates without product behavior change | Contract, step definitions, executable spec, artifacts and reports |
