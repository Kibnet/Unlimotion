# SPEC: Исполняемый BDD-мост preview и вставки task outline (SC-0013-002)

## 0. Метаданные
- Тип (профиль): `delivery-task`; `storm-product-development` + `dotnet-desktop-client` + `ui-automation-testing`.
- Владелец: STORM `/storm:cover` backlog.
- Масштаб: small.
- Целевое семейство / behavior baseline: central `model-behavior-baseline`; продуктовое поведение не меняется.
- Поверхность: Codex desktop, локальная .NET/Avalonia test surface.
- Effective runtime: не применимо; задача не меняет model/runtime output продукта.
- Eval baseline / evidence: неизменяемый scenario `SC-0013-002`, service/ViewModel/Avalonia.Headless tests и TUnit output.
- Целевой релиз / ветка: `storm-bootstrap`, локальный coverage commit.
- Ограничения: не менять production/UI code, `.feature`, existing tests или их annotations, `.csproj`, workflows, конфигурацию и clipboard ОС.
- Связанные ссылки: `ST-0013`, `AC-0038`, `GR-038`, `SC-0013-002`, `TS-0001`, `TS-0004`, `TS-0010`, `CN-0002`, `CN-0004`, `CN-0007`.

## 1. Overview / Цель
Связать неизменяемый Gherkin scenario `SC-0013-002` с фактическим evidence preview вставки и создания дерева после подтверждения, доведя `ST-0013` до `2/2` и общий executable ratio до `43/45`.

Outcome contract:
- Success means: feature text выполняется через `SD-0167..SD-0170` и `TS-0068`; parser, ViewModel и headless UI paths проходят независимо.
- Итоговый артефакт / output: test-only contract, step definitions, executable spec, `storm.json` и шесть STORM reports.
- Stop rules: остановиться и вынести отдельный delivery-task через QUEST, если для прохождения нужны production change, feature change, existing-test/annotation change или реальный clipboard OS access.

## 2. Текущее состояние (AS-IS)
- `SC-0013-002` имеет links `TS-0001/TS-0004/TS-0010`, но остаётся `automated` без executable step definitions.
- `TaskOutlineClipboardServiceTests.ParseOutline_ReadsMarkdownChecklistStatusAndDescriptions` доказывает parse checklist, descriptions, hierarchy и completed state.
- `MainWindowViewModelTests.PasteTaskOutline_CreatesNestedTasksUnderCurrentTask` проверяет confirmation preview, destination label и создаваемую parent/child/grandchild структуру; temporary fixture освобождается через `Dispose()`.
- `MainControlTreeCommandsUiTests.TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` проверяет Ctrl+Shift+V, выбранную цель, preview, создание дерева и неинтерференцию обычного Ctrl+V; метод сам очищает headless session/window/fixture.
- `ST-0013` сейчас 1/2 executable после `SC-0013-001`; scope копирования остаётся сохранённым и не меняется.

## 3. Проблема
Существующее evidence подтверждает AC-0038, но Gherkin scenario не исполняется как самостоятельная спецификация и не имеет Scenario -> Test -> Step Definition bridge.

## 4. Цели дизайна
- Повторно использовать три passing existing tests без изменения их кода.
- Сохранить contract тонким orchestration layer с явными independent flags.
- Сериализовать новый executable spec в `AvaloniaHeadless`, поскольку он вызывает ViewModel и headless UI test methods.
- Сохранить acceptance criteria отдельными от Gherkin и existing links `TS-0001/TS-0004/TS-0010`.

## 5. Non-Goals (чего НЕ делаем)
- Новое outline/paste preview/clipboard/settings/UI behavior.
- Изменение текста preview, confirmation policy, реальный системный clipboard, copy/export, локализацию или storage migration.
- Изменение production files, existing tests/annotations, проектов, workflows или full-suite stabilization.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `OutlineClipboardPasteContract.cs`: вызывает selected existing checks, освобождает `MainWindowViewModelTests`, возвращает flags.
- `OutlineClipboardPasteStepDefinitions.cs`: связывает exact feature steps с contract через `SD-0167..SD-0170`.
- `StormOutlineClipboardPasteExecutableSpecTests.cs`: парсит scenario/tags и исполняет steps в `AvaloniaHeadless` группе.
- `StormStepDefinition.cs`: получает minimal ephemeral context fields.
- `storm.json` и reports: получают canonical traceability/evidence/metrics sync.

### 6.2 Детальный дизайн
- Contract вызывает `ParseOutline_ReadsMarkdownChecklistStatusAndDescriptions`, `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask` и `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask`.
- `MainWindowViewModelTests` создаётся один раз и освобождается в `finally`; service/UI instances non-disposable и self-contained.
- Result содержит `MarkdownOutlineParsingPassed`, `PreviewAndTreeCreationPassed`, `OutlinePasteUiPassed`; `AssertAsync` требует все true.
- Visual planning artifact: не применимо, существующий интерфейс и layout не меняются.
- UI test video evidence: не применимо; UI behavior/layout не меняется, а passing headless UI check служит next-best evidence.
- Производительность: не требуется; contract последовательно вызывает уже существующие isolated tests.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| `SC-0013-002` | Вставляет outline в выбранную задачу и подтверждает preview | Preview показывает будущие задачи и destination; подтверждение создаёт иерархию под выбранной задачей | BDD 1/1, parser, ViewModel и headless UI checks | `AC-0038` |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Markdown outline с status и descriptions | Parse for preview | Parsed nodes preserve state, descriptions и hierarchy | Isolated parser input | Service check |
| Выбранная задача, confirmation accepted | Paste outline | Preview count/destination shown, nested tree created | Temporary ViewModel fixture clears state | ViewModel check |
| Tree selection | Ctrl+Shift+V | Clipboard read once, preview shown, tree under selection; ordinary Ctrl+V unchanged | Headless session/window cleanup | UI check |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Evidence scope | agent | Three existing parser/ViewModel/UI methods | 0.97 | Preview layout beyond current UI test remains out of scope | Нет |
| UI serialisation | agent | New BDD class joins `AvaloniaHeadless` limiter | 0.99 | Parallel session race | Нет |
| Product change | agent | Prohibited; stop if required | 1.00 | Gap cannot close without separate SPEC | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
Не применимо: bridge uses fixture-local storage/config and mocked clipboard delegates only; persisted product data and OS clipboard are not mutated.

## 7. Бизнес-правила / Алгоритмы (если есть)
1. Markdown parse retains checklist status, descriptions and nesting for preview/import.
2. Paste preview names the destination and task count before creating tasks.
3. Accepted confirmation creates parent/child/grandchild relations under the selected destination.
4. Feature scenario passes only after all three contract flags pass.

## 8. Точки интеграции и триггеры
- `StormFeatureParser` reads unchanged `SC-0013-002`.
- `StormScenarioRunner` invokes `SD-0167..SD-0170`.
- TUnit invokes new class inside `AvaloniaHeadless` serialization group.

## 9. Изменения модели данных / состояния
- Только ephemeral `StormScenarioContext` fields and contract result flags.
- Persisted product state не меняется.

## 10. Миграция / Rollout / Rollback
- Миграция/rollout: не применимо.
- Rollback: удалить только new contract/steps/executable spec/SPEC, context fields и generated STORM links/reports.

## 11. Тестирование и критерии приёмки
1. Exact feature steps execute through `SD-0167..SD-0170` and `TS-0068`.
2. Parser, preview/tree creation and tree command paths pass independently.
3. `SC-0013-002` becomes `passing`; `ST-0013` becomes `2/2`; metrics become `43/45`, 170 step definitions and reuse `173/173`.
4. Build, BDD 1/1, parser 1/1, ViewModel 1/1, UI 1/1, artifact validator and `git diff --check` pass.
5. Production, `.feature`, existing tests/annotations, projects and workflows have no diff.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| `AC-0038`: future tasks in preview | `ParseOutline_ReadsMarkdownChecklistStatusAndDescriptions` | New BDD step execution | `TS-0068`, TUnit output | — |
| `AC-0038`: confirmation creates nested tree | `PasteTaskOutline_CreatesNestedTasksUnderCurrentTask` | New BDD step execution | `TS-0068`, TUnit output | — |
| `AC-0038`: selected destination command | `TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask` | Headless run | TUnit output | — |

Команды проверки:
```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/StormOutlineClipboardPasteExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/TaskOutlineClipboardServiceTests/ParseOutline_ReadsMarkdownChecklistStatusAndDescriptions" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/MainWindowViewModelTests/PasteTaskOutline_CreatesNestedTasksUnderCurrentTask" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_PasteTaskOutline_Hotkey_CreatesTreeUnderSelectedTask" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases
- UI test parallelism: mitigated by new class attributes.
- Fixture cleanup: ViewModel test is disposed in `finally`; UI method retains its own cleanup.
- Parser evidence proves preview input while ViewModel/UI tests prove confirmation and placement; combined bridge intentionally needs all three.
- Full suite may timeout without summary; it is not passing evidence.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Bridge may create persisted tasks | It calls paste flow | Only existing temporary fixtures; production diff prohibited | mitigated |
| UI bridge can be flaky in parallel | Headless UI test runs inside bridge | Match repository `AvaloniaHeadless` limiter | mitigated |
| Parser test alone does not prove confirmation | Parse and write are distinct phases | ViewModel/UI tests separately require preview and created relations | mitigated |

### Rework Prevention Checklist
- User-visible preview/import scenario and state matrix are explicit.
- Every AC-0038 aspect maps to a named passing check.
- Decision ledger has no user-owned blocking choice.
- Expected objections distinguish parse evidence from confirmed tree creation.
- Role-based review is completed below.
- EXEC has direct BDD and independent evidence gates.

## 13. План выполнения
1. Create test-only contract, step definitions, scenario test and minimal context fields.
2. Run build and BDD/direct evidence gates sequentially.
3. Update canonical artifacts/reports; run `/storm:bdd-sync`, `/storm:bdd-lint` evidence through validator.
4. Post-EXEC review, correct findings, commit separately.

## 14. Открытые вопросы
Нет. Реальный OS clipboard и изменения preview UX намеренно вне границ.

## 15. Соответствие профилю
- Профили: `storm-product-development`, `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля: Gherkin remains a layer between AC and tests; traceability uses stable IDs; test/code scope goes through QUEST; UI-backed bridge preserves headless serialisation.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-14-storm-sc0013-outline-paste-bdd.md` | New QUEST spec | Auditable delivery gate |
| `src/Unlimotion.Test/OutlineClipboardPasteContract.cs` | New | Existing evidence orchestration |
| `src/Unlimotion.Test/StormBdd/OutlineClipboardPasteStepDefinitions.cs` | New | `SD-0167..SD-0170` |
| `src/Unlimotion.Test/StormOutlineClipboardPasteExecutableSpecTests.cs` | New | Scenario runner |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Modify | Minimal ephemeral context |
| `docs/product/storm.json`; six reports | Modify | Canonical BDD traceability/metrics |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0013-002` | linked-only, `automated`, no steps | `passing`, `TS-0068`, `SD-0167..SD-0170` |
| `ST-0013` | 1/2 executable | 2/2 executable |
| Behavior coverage | 42/45 | 43/45 |

## 18. Альтернативы и компромиссы
- Вариант: изменить existing tests или добавить real system clipboard flow.
- Плюсы: шире integration evidence.
- Минусы: выходит за artifact-gap scope и нарушает constraints.
- Почему выбранное решение лучше в контексте этой задачи: thin bridge over three existing complementary checks закрывает executable-spec gap без behavior change.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Scope, stop rules, no open questions |
| B. Качество дизайна | 6-10 | PASS | Evidence ownership and lifecycle explicit |
| C. Безопасность изменений | 11-13 | PASS | No production/existing-test mutation |
| D. Проверяемость | 14-16 | PASS | AC-to-test matrix and commands specified |
| E. Готовность к автономной реализации | 17-19 | PASS | Files and exact methods named |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST/UI requirements included |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | One preview/import scenario, explicit stop rules |
| 2. Понимание текущего состояния | 5 | Existing evidence and lifecycle inspected |
| 3. Конкретность целевого дизайна | 5 | IDs, files, flags and sequence fixed |
| 4. Безопасность (миграция, откат) | 5 | Test-only rollback and no OS clipboard access |
| 5. Тестируемость | 5 | Direct and BDD gates are named |
| 6. Готовность к автономной реализации | 5 | No user-owned decision remains |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does preview explain the intended imported tree and confirmation create it? | PASS | Parser, preview and relations checks named |
| UX / designer | applicable | Do selected-target hotkey and preview/normal Ctrl+V states remain protected? | PASS | Existing headless UI evidence retained |
| Tester / validation | applicable | Are parser, confirmation and command paths independently evidenced? | PASS | Exact three methods and BDD command named |
| Developer / architect | applicable | Are fixture ownership and BDD boundary coherent? | PASS | `finally` + limiter specified |
| Delivery / operations / security | not applicable | No deploy/config/secret/runtime change | PASS | No external clipboard access |

### Post-SPEC Review
- Статус: PASS после исправления.
- Scope reviewed: this SPEC; central instruction stack and local UI override; `storm-product-development`, `dotnet-desktop-client`, `ui-automation-testing`; unchanged `SC-0013-002`, `AC-0038`, `GR-038`; three selected tests and fixture cleanup; planned files plus reports.
- Decision: active user auto-approval permits EXEC.
- Review passes:
  - Scope/Evidence: PASS. Scenario, tags, existing test names, feature wording and planned IDs are exact.
  - Contract: PASS. New bridge only orchestrates existing isolated tests; `MainWindowViewModelTests.Dispose()` and UI-method self-cleanup are explicit.
  - Adversarial risk: PASS after fix. Contract keeps parser evidence separate from preview/confirmation/tree evidence; new class joins `AvaloniaHeadless`.
  - Role-Based: PASS. Domain/UX/tester/developer reviews are above; delivery is not applicable.
  - Fix and re-review: PASS. Draft could have treated parser evidence as confirmation evidence; specification now requires separate ViewModel and UI gates.
  - Stop decision: PASS; no user-owned decision and no external-state requirement.
- Evidence inspected: feature lines 17-22; parser status/description test; ViewModel paste preview/relations test and `BaseModelTests.Dispose`; tree command headless session plus `try/finally`; existing `AvaloniaHeadless` attributes.
- Depth checklist:
  - Scope drift / unrelated changes: only this SPEC before EXEC.
  - Acceptance criteria: `AC-0038` remains unchanged and maps to three checks.
  - User-observable scenarios / Decision ledger / Expected objections: explicit, no blocking choice.
  - Validation evidence: exact commands added.
  - Unsupported claims: no full-suite, UI-video or real clipboard claim.
  - Regression / edge case: parsing status/descriptions, confirmation, selected destination, normal Ctrl+V and parallelism covered.
  - Comments/docs/changelog: only Russian SPEC and later STORM reports are needed; no changelog impact.
  - Hidden contract change: prohibited; bridge has only ephemeral context/result state.
  - Manual-review challenge: missing confirmation/relations evidence would overclaim preview coverage; it is now mandatory.
- No-findings justification: after the evidence-scope correction every required pre-approval section, lifecycle boundary and reproducible evidence link is present.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | evidence | Draft could have treated parser output as proof of confirmed tree creation. | Require separate ViewModel and UI gates. | fixed |
| — | scope/design/risk | Нет находок после повторного review. | — | closed |

- Fixed before continuing: independent preview/confirmation/tree evidence is explicit.
- Checks rerun: spec linter, rubric and review-loop checklist reviewed against amended SPEC.
- Needs human: нет; approval already active.
- Residual risks / follow-ups: historic full-suite timeout remains outside this slice.

### Post-EXEC Review
- Статус: PASS.
- Scope reviewed: approved SPEC; clean pre-EXEC worktree; new contract, step definitions, executable spec and context fields; `storm.json`, six reports and exact feature scenario; production/UI/feature/existing-test diffs are empty.
- Decision: changes satisfy the approved test-only scope and can be committed.
- Review passes:
  - Scope/Evidence: PASS. `TS-0068`, `SD-0167..SD-0170`, three direct checks and metrics `43/45` agree.
  - Contract: PASS. Existing checks all pass before flags are set; `MainWindowViewModelTests` fixture is disposed; new BDD class serializes ViewModel/UI methods.
  - Adversarial risk: PASS. Parser retention, preview count/destination, confirmation-created relations, selected target, Ctrl+Shift+V and ordinary Ctrl+V non-interference are covered by complementary existing tests.
  - Role-Based: PASS. UI state evidence is headless fallback because no UI behavior/layout changed; domain and developer contracts are preserved.
  - Fix and re-review: PASS. Root metrics initially retained `bdd_lint_issues: 16`; it was corrected to the actual validator result `17` and validation rerun.
  - Stop decision: PASS; no separate delivery-task is required.
- Evidence inspected: Build Release errors 0 (69 existing warnings); BDD 1/1; parser 1/1; ViewModel preview/tree 1/1; tree-command UI 1/1; validator 0 errors/17 warnings; `git diff --check`.
- Depth checklist:
  - Scope drift / unrelated changes: only planned files; no production/UI/feature/existing-test change.
  - Acceptance criteria: `AC-0038` remains unchanged and maps to three complementary checks.
  - User-observable scenarios / Acceptance-to-test matrix / Expected objections: all checks pass; copy/export remains preserved by `SC-0013-001`.
  - Validation evidence: direct and bridge TUnit output plus validator recorded.
  - Unsupported claims: full suite and real OS clipboard runtime remain unclaimed.
  - Regression / edge case: BDD class serialisation, fixture cleanup, selected destination, confirmation and normal Ctrl+V non-interference checked.
  - Comments/docs/changelog: Russian SPEC and reports updated; no changelog needed.
  - Hidden contract change: none; only test-local context/result fields added.
  - Manual-review challenge: bridge must not turn parser evidence into a confirmation/tree claim; ViewModel and UI tests independently carry those assertions.
- No-findings justification: code follows the established BDD contract pattern, targeted evidence passes and canonical traceability is structurally validated after metric correction.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Root metric retained 16 lint warnings after validator reported 17. | Synchronize `bdd_lint_issues` with validator. | fixed |
| — | spec compliance / regression / tests / docs | Нет находок после re-review. | — | closed |

- Fixed before final report: `bdd_lint_issues` updated from 16 to 17.
- Checks rerun: artifact validator and `git diff --check`.
- Validation evidence: build errors 0; BDD 1/1; parser/VM/UI 3/3; validator 0/17.
- Unrelated changes: none.
- Needs human: нет.
- Residual risks / follow-ups: historic full-suite timeout remains outside this slice.

## Approval
Active workflow auto-approval after PASS review; canonical phrase: "Спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Select outline-paste BDD bridge | 0.97 | Нет | Review SPEC | Нет | User auto-approval already active | Existing parser/ViewModel/UI evidence covers AC-0038 without product changes | This SPEC |
| SPEC | Review and correct evidence boundary | 0.98 | Нет | EXEC | Нет | User auto-approval already active | Separate parser/confirmation/command checks avoid a weak parser-only claim | This SPEC |
| EXEC | Implement and validate BDD bridge | 0.97 | Нет | Post-EXEC review and commit | Нет | User auto-approval already active | New bridge reuses three passing complementary checks; validator confirms 43/45 | Test files, storm artifacts, reports |
