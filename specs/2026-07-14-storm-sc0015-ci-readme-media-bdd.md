# SPEC: Исполняемый BDD-мост CI и README media automation (SC-0015-003)

## 0. Метаданные
- Тип (профиль): delivery-task; storm-product-development + dotnet-desktop-client + ui-automation-testing.
- Владелец: STORM /storm:cover backlog.
- Масштаб: small.
- Целевое семейство / behavior baseline: central model-behavior-baseline; продуктовый, CI и media workflow не меняются.
- Поверхность: Codex desktop, локальная .NET/Avalonia test surface.
- Effective runtime: не применимо; задача не меняет model/runtime output продукта.
- Eval baseline / evidence: неизменяемый scenario SC-0015-003, read-only CI/media source contract, локальный ReadmeDemo и headless responsiveness evidence.
- Целевой релиз / ветка: storm-bootstrap, локальный coverage commit.
- Ограничения: не менять production/UI code, .feature, existing tests или annotations, проекты, workflows, scripts, README, media или external CI state.
- Связанные ссылки: ST-0015, AC-0043, GR-043, SC-0015-003, TS-0011, TS-0015, CN-0004, CN-0007, CN-0008.

## 1. Overview / Цель
Связать неизменяемый Gherkin scenario SC-0015-003 с фактическими evidence CI smoke path, README media automation и UI responsiveness, доведя ST-0015 до 3/3 и общий executable ratio до 45/45.

Outcome contract:
- Success means: feature text выполняется через SD-0175..SD-0178 и TS-0070; contract подтверждает CI/headless/media automation source of truth, а существующие headless smoke checks проходят.
- Итоговый артефакт / output: test-only source contract, step definitions, executable spec, storm.json и шесть STORM reports.
- Stop rules: остановиться и вынести отдельный delivery-task через QUEST, если нужны изменения workflow, README/media generator, production/UI code, existing-test/annotation change, запуск media regeneration или external CI execution.

## 2. Текущее состояние (AS-IS)
- SC-0015-003 имеет links TS-0011/TS-0015, но остаётся automated без executable step definitions.
- .github/workflows/tests.yml job all-tests restores and runs src/Unlimotion.Test and tests/Unlimotion.UiTests.Headless, serialising headless tests through --maximum-parallel-tests 1.
- scripts/update-readme-media.ps1 builds Headless/FlaUI/media projects, runs both UI suites sequentially and invokes Unlimotion.ReadmeMedia with --copy-to-media and selected languages.
- tests/Unlimotion.ReadmeMedia/README.md documents deterministic ReadmeDemo, English/Russian outputs and media-copy boundary.
- Readme_demo_uses_capture_presentation_state passed locally in Unlimotion.UiTests.Headless 10/10 on 2026-07-14; MainScreen_Connect_KeepsUiResponsive_DuringBlockingInitialLoad is an existing headless UI assertion.

## 3. Проблема
CI and README media automation have source and smoke evidence, but the Gherkin scenario lacks its own Scenario -> Test -> Step Definition bridge. Source evidence must not be presented as a successful external CI run or regenerated media artifacts.

## 4. Цели дизайна
- Проверить CI, media script, media documentation and ReadmeDemo test contract read-only.
- Повторно использовать existing headless UI responsiveness test without changing its code.
- Сериализовать новый executable spec в AvaloniaHeadless.
- Сохранить acceptance criteria отдельно от Gherkin и existing links TS-0011/TS-0015.

## 5. Non-Goals (чего НЕ делаем)
- Изменение CI job, global test runner, README/media script, screenshots/GIF, source README, Release/publish or remote CI run.
- Запуск scripts/update-readme-media.ps1: он удаляет output root и копирует generated media.
- Изменение production files, existing tests/annotations, проектов, workflows или configuration.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- CiReadmeMediaContract.cs: read-only asserts workflow/script/README/ReadmeDemo source markers and invokes existing headless responsiveness test.
- CiReadmeMediaStepDefinitions.cs: связывает exact feature steps с contract через SD-0175..SD-0178.
- StormCiReadmeMediaExecutableSpecTests.cs: парсит scenario/tags и исполняет steps в AvaloniaHeadless группе.
- StormStepDefinition.cs: получает minimal ephemeral context fields.
- storm.json и reports: получают canonical traceability/evidence/metrics sync.

### 6.2 Детальный дизайн
- Contract reads .github/workflows/tests.yml, scripts/update-readme-media.ps1, tests/Unlimotion.ReadmeMedia/README.md and tests/Unlimotion.UiTests.Headless/Tests/ReadmeDemoHeadlessTests.cs.
- CI contract requires all-tests, Headless project restore/run, Run Headless UI Tests and --maximum-parallel-tests 1.
- Media contract requires sequential Headless/FlaUI test commands, Unlimotion.ReadmeMedia, --copy-to-media, default en,ru, ReadmeDemo and documented English/Russian output.
- Contract invokes only MainScreenLoadingUiTests.MainScreen_Connect_KeepsUiResponsive_DuringBlockingInitialLoad; separate direct run retains actual ReadmeDemo smoke evidence from its own project.
- Result contains CiSmokeContractPassed, ReadmeMediaAutomationContractPassed, UiResponsiveSmokePassed; AssertAsync requires all true.
- Visual planning/video evidence: не применимо; UI behavior/layout и media assets не меняются. Existing headless UI evidence is the next-best evidence.
- Производительность: local source reads plus one existing headless test; no script invocation, network CI, desktop capture or media copy.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| SC-0015-003 | Maintainer verifies delivery and README-media surfaces | CI/headless smoke path and documented media workflow remain connected to ReadmeDemo UI evidence | BDD 1/1, source contract, direct ReadmeDemo headless run, responsiveness UI test | AC-0043 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| CI workflow source | Contract read | Headless test project and serialized smoke path are present | Missing marker fails locally | Read-only, no remote CI claim |
| README media script/docs | Contract read | Sequential tests, deterministic scenario and language output are present | Missing marker fails locally | Script is not executed |
| Main screen initial load | Existing headless smoke | Overlay keeps UI responsive until connect completes | Fixture-local delayed load | Existing test owns cleanup |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| CI evidence boundary | agent | Source contract plus local UI smoke; do not claim remote CI passed | 0.98 | Configuration cannot prove GitHub execution | Нет |
| README media execution | agent | Inspect script/docs only; never invoke regeneration | 1.00 | Script has filesystem side effects | Нет |
| Headless direct evidence | agent | Re-run existing ReadmeDemo project test after restore | 0.95 | Test project must retain restored packages | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| CI smoke | .github/workflows/tests.yml | None; read-only assertion | None | all-tests and Headless UI command markers |
| README media | scripts/update-readme-media.ps1, media README | None; read-only assertion | None | sequential test and capture markers |
| ReadmeDemo UI | existing Headless and loading UI tests | None | Fixture-local only | Direct TUnit evidence |

## 7. Бизнес-правила / Алгоритмы
1. CI smoke workflow must restore and run the Headless UI test project serially.
2. README media automation must validate Headless and FlaUI UI paths before copying generated language-specific media.
3. ReadmeDemo must have deterministic English and Russian capture presentation coverage.
4. Feature scenario passes only after source contracts and local UI responsiveness evidence pass; it never asserts remote CI completion or regenerated media.

## 8. Точки интеграции и триггеры
- StormFeatureParser reads unchanged SC-0015-003.
- StormScenarioRunner invokes SD-0175..SD-0178.
- TUnit invokes new class inside AvaloniaHeadless serialization group.

## 9. Изменения модели данных / состояния
- Только ephemeral StormScenarioContext fields and contract result flags.
- Persisted product, CI, script, README and media state не меняется.

## 10. Миграция / Rollout / Rollback
- Миграция/rollout: не применимо.
- Rollback: удалить только new contract/steps/executable spec/SPEC, context fields и generated STORM links/reports.

## 11. Тестирование и критерии приёмки
1. Exact feature steps execute through SD-0175..SD-0178 and TS-0070.
2. Read-only workflow/script/docs/ReadmeDemo source and existing responsiveness UI path pass independently.
3. SC-0015-003 becomes passing; ST-0015 becomes 3/3; metrics become 45/45, 178 step definitions and reuse 181/181.
4. Test build, BDD 1/1, responsiveness UI 1/1, direct ReadmeDemo headless run, artifact validator and git diff --check pass.
5. Production, .feature, existing tests/annotations, projects, workflows, scripts, README and media have no diff.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-0043: CI smoke workflow | New CiReadmeMediaContract | New BDD step execution | TS-0070, source contract | Remote CI intentionally excluded |
| AC-0043: README media workflow | New CiReadmeMediaContract | Read-only script/docs contract | TS-0070, source contract | Media regeneration has side effects |
| AC-0043: UI smoke | MainScreen_Connect_KeepsUiResponsive_DuringBlockingInitialLoad | Headless run | TUnit output | - |
| AC-0043: ReadmeDemo presentation | Existing Readme_demo_uses_capture_presentation_state | Direct headless project run | TUnit output, 10/10 on 2026-07-14 | - |

Команды проверки:
~~~powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/StormCiReadmeMediaExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/MainScreenLoadingUiTests/MainScreen_Connect_KeepsUiResponsive_DuringBlockingInitialLoad" --output Detailed
dotnet test tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -c Debug --no-restore --treenode-filter "/*/*/ReadmeDemoEnglishHeadlessTests/*" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
~~~

## 12. Риски и edge cases
- Source contracts prove configured workflow and automation behaviour, not a successful remote CI run or copied media; no such claim is made.
- The media script deletes its output root and is therefore not run.
- ReadmeDemo direct test requires restored packages; current restored assets are used with --no-restore.
- Full suite may timeout without summary; it is not passing evidence.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| CI source is not a green CI run | Workflow text can be stale or fail remotely | Require actual local ReadmeDemo TUnit result and no remote CI claim | mitigated |
| Media automation could mutate files | Script deletes output and copies media | Read only script/docs; execution prohibited | mitigated |
| New BDD test could race headless UI | Contract invokes a UI test | Match repository AvaloniaHeadless limiter | mitigated |

### Rework Prevention Checklist
- Delivery scenario and state matrix are explicit.
- Every AC-0043 aspect has source or direct test evidence.
- Decision ledger has no user-owned blocking choice.
- Role-based review is completed below.
- EXEC has direct BDD and independent evidence gates.

## 13. План выполнения
1. Create test-only CI/media source/UI contract, step definitions, scenario test and minimal context fields.
2. Run Test Release build plus BDD/direct evidence gates sequentially.
3. Update canonical artifacts/reports; run /storm:bdd-sync and /storm:bdd-lint evidence through validator.
4. Post-EXEC review, correct findings, commit separately.

## 14. Открытые вопросы
Нет. Remote CI completion and media regeneration intentionally remain outside scope.

## 15. Соответствие профилю
- Профили: storm-product-development, dotnet-desktop-client, ui-automation-testing.
- Выполненные требования профиля: Gherkin remains a layer between AC and tests; stable IDs; QUEST gate; UI-backed bridge preserves headless serialisation; delivery evidence does not overclaim CI or media output.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| specs/2026-07-14-storm-sc0015-ci-readme-media-bdd.md | New QUEST spec | Auditable delivery gate |
| src/Unlimotion.Test/CiReadmeMediaContract.cs | New | CI/media source and existing UI evidence orchestration |
| src/Unlimotion.Test/StormBdd/CiReadmeMediaStepDefinitions.cs | New | SD-0175..SD-0178 |
| src/Unlimotion.Test/StormCiReadmeMediaExecutableSpecTests.cs | New | Scenario runner |
| src/Unlimotion.Test/StormBdd/StormStepDefinition.cs | Modify | Minimal ephemeral context |
| docs/product/storm.json; six reports | Modify | Canonical BDD traceability/metrics sync |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| SC-0015-003 | linked-only, automated, no steps | passing, TS-0070, SD-0175..SD-0178 |
| ST-0015 | 2/3 executable | 3/3 executable |
| Behavior coverage | 44/45 | 45/45 |

## 18. Альтернативы и компромиссы
- Вариант: запустить media regeneration or remote CI, либо менять CI/test configuration.
- Плюсы: stronger external delivery evidence.
- Минусы: filesystem/network side effects and scope expansion.
- Почему выбранное решение лучше в контексте этой задачи: read-only contract plus existing local UI smoke closes the executable-spec gap without changing delivery behaviour.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Scope, stop rules, no open questions |
| B. Качество дизайна | 6-10 | PASS | Source/UI responsibilities and lifecycle explicit |
| C. Безопасность изменений | 11-13 | PASS | No production/project/workflow/script/media mutation |
| D. Проверяемость | 14-16 | PASS | AC-to-test matrix and commands specified |
| E. Готовность к автономной реализации | 17-19 | PASS | Files, markers, IDs and sequence fixed |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/UI requirements included |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | One CI/media scenario with explicit stop rules |
| 2. Понимание текущего состояния | 5 | CI, script, docs and direct ReadmeDemo test inspected |
| 3. Конкретность целевого дизайна | 5 | IDs, markers, files, flags and sequence fixed |
| 4. Безопасность (миграция, откат) | 5 | Test-only rollback and no CI/media side effect |
| 5. Тестируемость | 5 | Build, direct and BDD gates are named |
| 6. Готовность к автономной реализации | 5 | No user-owned decision remains |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does CI/media evidence retain explicit delivery boundaries? | PASS | Remote CI and generated media remain excluded |
| UX / designer | applicable | Do ReadmeDemo and responsiveness UI checks retain behaviour without visual asset change? | PASS | Existing headless evidence retained |
| Tester / validation | applicable | Are workflow, script, docs, ReadmeDemo and loading evidence independent? | PASS | Exact source markers and direct run named |
| Developer / architect | applicable | Are source reads and headless lifecycle coherent? | PASS | finally ownership and limiter specified |
| Delivery / operations / security | applicable | Are CI/network/media side effects excluded? | PASS | No remote CI, script execution or credential use |

### Post-SPEC Review
- Статус: PASS после исправления.
- Scope reviewed: this SPEC; central instruction stack and local UI override; storm-product-development, dotnet-desktop-client, ui-automation-testing; unchanged SC-0015-003, AC-0043, GR-043; workflow/script/docs/ReadmeDemo and loading test evidence; planned files plus reports.
- Decision: active user auto-approval permits EXEC.
- Review passes:
  - Scope/Evidence: PASS. Scenario, tags, existing test names, workflow/script markers and planned IDs are exact.
  - Contract: PASS. New source assertions are read-only; selected headless method owns fixture cleanup.
  - Adversarial risk: PASS after fix. Draft could have treated source contract as a green remote CI run; direct local ReadmeDemo smoke is now mandatory and release/CI claims are prohibited.
  - Role-Based: PASS. All relevant roles are above, including delivery/security because CI and media script are inspected.
  - Fix and re-review: PASS. Direct Headless project test was restored and passed 10/10, including Readme_demo_uses_capture_presentation_state.
  - Stop decision: PASS; no user-owned decision and no external-state requirement.
- Evidence inspected: feature lines 25-30; CI all-tests restore/run; media script build/test/copy sequence; media README ReadmeDemo/language contract; ReadmeDemo source and direct TUnit 10/10; MainScreenLoadingUiTests lifecycle; existing AvaloniaHeadless attributes.
- Depth checklist: scope drift is excluded; AC-0043 remains unchanged; user-visible scenario, decision ledger and objections are explicit; validation commands are reproducible; no remote CI/media claim; script side effects and headless parallelism are covered; only Russian SPEC/reports change; context is ephemeral.
- No-findings justification: every AC-0043 surface has independent static or local executable evidence and the side-effect boundary is explicit.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | evidence | Draft could have claimed configured CI as successful remote execution. | Require local ReadmeDemo smoke and prohibit remote CI claim. | fixed |
| - | scope/design/risk | Нет находок после повторного review. | - | closed |

- Fixed before continuing: local direct ReadmeDemo TUnit evidence added to the validation gate.
- Checks rerun: spec linter, rubric and review-loop checklist reviewed against amended SPEC.
- Needs human: нет; approval already active.
- Residual risks / follow-ups: historic full-suite timeout remains outside this slice.

### Post-EXEC Review
- Статус: PASS.
- Scope reviewed: test-only SC-0015-003 bridge, generated STORM artifacts and six reports; production/UI code, .feature, projects, workflows, scripts, README, media and existing test annotations.
- Findings: нет. CiReadmeMediaContract only reads workflow/script/docs/test source and reuses a self-contained headless UI check. StormCiReadmeMediaExecutableSpecTests uses the repository AvaloniaHeadless serialization pattern; context fields are ephemeral.
- Evidence: Test Release build passed with 69 existing warnings and 0 errors; BDD passed 1/1; loading responsiveness UI passed 1/1; ReadmeDemo headless project passed 10/10 including capture presentation; artifact validator passed with 0 errors and 18 known duplicate-step warnings; git diff --check passed.
- Residual risk: the contract proves local CI/media configuration and local headless evidence, not a remote CI run or regenerated media. Historic full-suite timeout has no summary and is not treated as PASS evidence.
- Decision: no corrective change is needed; the slice is ready for its isolated commit.

## Approval
Active workflow auto-approval after PASS review; canonical phrase: Спеку подтверждаю.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Select CI/media BDD bridge | 0.98 | Нет | Review SPEC | Нет | User auto-approval already active | Existing CI/script/docs/ReadmeDemo evidence covers AC-0043 without delivery change | This SPEC |
| SPEC | Review and add direct ReadmeDemo gate | 0.98 | Нет | EXEC | Нет | User auto-approval already active | Local smoke prevents a configuration-only CI claim | This SPEC |
| EXEC | Implement and validate SC-0015-003 | 0.98 | Нет | Commit and audit 45/45 | Нет | User auto-approval already active | Test-only bridge passed BDD, UI and artifact gates without CI/media side effects | Contract, step definitions, executable spec, artifacts and reports |
