# SPEC: Исполняемый BDD-мост копирования task outline (SC-0013-001)

## 0. Метаданные
- Тип (профиль): `delivery-task`; `storm-product-development` + `dotnet-desktop-client` + `ui-automation-testing`.
- Владелец: STORM `/storm:cover` backlog.
- Масштаб: small.
- Целевое семейство / behavior baseline: central `model-behavior-baseline`; продуктовое поведение не меняется.
- Поверхность: Codex desktop, локальная .NET/Avalonia test surface.
- Effective runtime: не применимо; задача не меняет model/runtime output продукта.
- Eval baseline / evidence: неизменяемый scenario `SC-0013-001`, service/ViewModel/Avalonia.Headless tests и TUnit output.
- Целевой релиз / ветка: `storm-bootstrap`, локальный coverage commit.
- Ограничения: не менять production/UI code, `.feature`, existing tests или их annotations, `.csproj`, workflows, конфигурацию и clipboard ОС.
- Связанные ссылки: `ST-0013`, `AC-0037`, `GR-037`, `SC-0013-001`, `TS-0001`, `TS-0004`, `TS-0010`, `CN-0002`, `CN-0004`, `CN-0007`.

## 1. Overview / Цель
Связать неизменяемый Gherkin scenario `SC-0013-001` с фактическим evidence экспорта выбранной задачи или поддерева в Markdown outline с descriptions, доведя `ST-0013` до `1/2` и общий executable ratio до `42/45`.

Outcome contract:
- Success means: feature text выполняется через `SD-0163..SD-0166` и `TS-0067`; service, ViewModel и headless UI paths проходят независимо.
- Итоговый артефакт / output: test-only contract, step definitions, executable spec, `storm.json` и шесть STORM reports.
- Stop rules: остановиться и вынести отдельный delivery-task через QUEST, если для прохождения нужны production change, feature change, existing-test/annotation change или реальный clipboard OS access.

## 2. Текущее состояние (AS-IS)
- `SC-0013-001` имеет links `TS-0001/TS-0004/TS-0010`, но остаётся `automated` без executable step definitions.
- `TaskOutlineClipboardServiceTests.BuildOutline_MarkdownWithDescriptions_UsesChecklistAndIndentedDescriptions` проверяет Markdown checklist, descriptions и вложенность в изолированном storage.
- `MainWindowViewModelTests.CopyTaskOutline_UsesMarkdownAndDescriptionSettings` проверяет, что settings дают Markdown и description в скопированном поддереве; его temporary fixture освобождается через `Dispose()`.
- `MainControlTreeCommandsUiTests.TreeCommandUi_CopyTaskOutline_HotkeyAndContextMenu_Work` проверяет delivery поддерева через hotkey и context menu, сам создаёт и очищает headless session/window/fixture.
- `SC-0013-002` (preview/import) намеренно не входит в эту SPEC.

## 3. Проблема
Существующее evidence подтверждает AC-0037, но Gherkin scenario не исполняется как самостоятельная спецификация и не имеет Scenario -> Test -> Step Definition bridge.

## 4. Цели дизайна
- Повторно использовать три passing existing tests без изменения их кода.
- Сохранить contract тонким orchestration layer с явными independent flags.
- Сериализовать новый executable spec в `AvaloniaHeadless`, поскольку он вызывает ViewModel и headless UI test methods.
- Сохранить acceptance criteria отдельными от Gherkin и existing links `TS-0001/TS-0004/TS-0010`.

## 5. Non-Goals (чего НЕ делаем)
- Новое outline/clipboard/settings/UI behavior.
- Импорт outline, paste preview, реальный системный clipboard, настройки, локализацию или storage migration.
- Изменение production files, existing tests/annotations, проектов, workflows или full-suite stabilization.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `OutlineClipboardCopyContract.cs`: вызывает selected existing checks, освобождает `MainWindowViewModelTests`, возвращает flags.
- `OutlineClipboardCopyStepDefinitions.cs`: связывает exact feature steps с contract через `SD-0163..SD-0166`.
- `StormOutlineClipboardCopyExecutableSpecTests.cs`: парсит scenario/tags и исполняет steps в `AvaloniaHeadless` группе.
- `StormStepDefinition.cs`: получает minimal ephemeral context fields.
- `storm.json` и reports: получают canonical traceability/evidence/metrics sync.

### 6.2 Детальный дизайн
- Contract вызывает `BuildOutline_MarkdownWithDescriptions_UsesChecklistAndIndentedDescriptions`, `CopyTaskOutline_UsesMarkdownAndDescriptionSettings` и `TreeCommandUi_CopyTaskOutline_HotkeyAndContextMenu_Work`.
- `MainWindowViewModelTests` создаётся один раз и освобождается в `finally`; service/UI instances non-disposable и self-contained.
- Result содержит `MarkdownDescriptionFormatPassed`, `ViewModelSettingsPassed`, `OutlineCopyUiPassed`; `AssertAsync` требует все true.
- Visual planning artifact: не применимо, существующий интерфейс и layout не меняются.
- UI test video evidence: не применимо; UI behavior/layout не меняется, а passing headless UI check служит next-best evidence.
- Производительность: не требуется; contract последовательно вызывает уже существующие isolated tests.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| `SC-0013-001` | Копирует выбранную задачу/поддерево через команду дерева | Clipboard output сохраняет Markdown, descriptions и структуру поддерева | BDD 1/1, service, ViewModel и headless UI checks | `AC-0037` |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Задача с description и child | Copy as Markdown + description | Checklist Markdown и indented description | Isolated service storage | Service check |
| Выбранное поддерево, settings enabled | Copy outline | Clipboard получает Markdown поддерева | Temporary ViewModel fixture clears state | ViewModel check |
| Tree selection | Ctrl+Shift+C или context menu | Selected child/root copied; ordinary Ctrl+C remains unchanged | Headless session/window cleanup | UI check |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Evidence scope | agent | Three existing service/ViewModel/UI methods | 0.96 | UI path does not assert Markdown settings itself | Нет |
| UI serialisation | agent | New BDD class joins `AvaloniaHeadless` limiter | 0.99 | Parallel session race | Нет |
| Product change | agent | Prohibited; stop if required | 1.00 | Gap cannot close without separate SPEC | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
Не применимо: bridge uses fixture-local storage/config and mocked clipboard delegates only; persisted product data and OS clipboard are not mutated.

## 7. Бизнес-правила / Алгоритмы (если есть)
1. Markdown export uses checklist markers and indented descriptions when both settings are enabled.
2. Export source can be the selected task or its subtree.
3. Tree command reaches the same copy behavior through hotkey and context menu.
4. Feature scenario passes only after all three contract flags pass.

## 8. Точки интеграции и триггеры
- `StormFeatureParser` reads unchanged `SC-0013-001`.
- `StormScenarioRunner` invokes `SD-0163..SD-0166`.
- TUnit invokes new class inside `AvaloniaHeadless` serialization group.

## 9. Изменения модели данных / состояния
- Только ephemeral `StormScenarioContext` fields and contract result flags.
- Persisted product state не меняется.

## 10. Миграция / Rollout / Rollback
- Миграция/rollout: не применимо.
- Rollback: удалить только new contract/steps/executable spec/SPEC, context fields и generated STORM links/reports.

## 11. Тестирование и критерии приёмки
1. Exact feature steps execute through `SD-0163..SD-0166` and `TS-0067`.
2. Markdown+description, ViewModel settings and tree command paths pass independently.
3. `SC-0013-001` becomes `passing`; `ST-0013` becomes `1/2`; metrics become `42/45`, 166 step definitions and reuse `169/169`.
4. Build, BDD 1/1, service 1/1, ViewModel 1/1, UI 1/1, artifact validator and `git diff --check` pass.
5. Production, `.feature`, existing tests/annotations, projects and workflows have no diff.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| `AC-0037`: Markdown и descriptions | `BuildOutline_MarkdownWithDescriptions_UsesChecklistAndIndentedDescriptions` | New BDD step execution | `TS-0067`, TUnit output | — |
| `AC-0037`: выбранное поддерево и settings | `CopyTaskOutline_UsesMarkdownAndDescriptionSettings` | New BDD step execution | `TS-0067`, TUnit output | — |
| `AC-0037`: доступная команда дерева | `TreeCommandUi_CopyTaskOutline_HotkeyAndContextMenu_Work` | Headless run | TUnit output | — |

Команды проверки:
```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/StormOutlineClipboardCopyExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/TaskOutlineClipboardServiceTests/BuildOutline_MarkdownWithDescriptions_UsesChecklistAndIndentedDescriptions" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/MainWindowViewModelTests/CopyTaskOutline_UsesMarkdownAndDescriptionSettings" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/MainControlTreeCommandsUiTests/TreeCommandUi_CopyTaskOutline_HotkeyAndContextMenu_Work" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Риски и edge cases
- UI test parallelism: mitigated by new class attributes.
- Fixture cleanup: ViewModel test is disposed in `finally`; UI method retains its own cleanup.
- Existing UI test validates routing/selection, while ViewModel/service tests validate format/settings; combined bridge intentionally needs all three.
- Full suite may timeout without summary; it is not passing evidence.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Bridge may change clipboard behavior | It calls copy flows | Only existing fixtures and mocked delegates; production diff prohibited | mitigated |
| UI bridge can be flaky in parallel | Headless UI test runs inside bridge | Match repository `AvaloniaHeadless` limiter | mitigated |
| Scenario says copy or paste | This scenario's Then and AC-0037 concern export | Contract only evidences copy; paste remains separate `SC-0013-002` | mitigated |

### Rework Prevention Checklist
- User-visible copy scenario and state matrix are explicit.
- Every AC-0037 aspect maps to a named passing check.
- Decision ledger has no user-owned blocking choice.
- Expected objections distinguish export from deferred import.
- Role-based review is completed below.
- EXEC has direct BDD and independent evidence gates.

## 13. План выполнения
1. Create test-only contract, step definitions, scenario test and minimal context fields.
2. Run build and BDD/direct evidence gates sequentially.
3. Update canonical artifacts/reports; run `/storm:bdd-sync`, `/storm:bdd-lint` evidence through validator.
4. Post-EXEC review, correct findings, commit separately.

## 14. Открытые вопросы
Нет. Реальный OS clipboard и import/paste scope намеренно вне границ.

## 15. Соответствие профилю
- Профили: `storm-product-development`, `dotnet-desktop-client`, `ui-automation-testing`.
- Выполненные требования профиля: Gherkin remains a layer between AC and tests; traceability uses stable IDs; test/code scope goes through QUEST; UI-backed bridge preserves headless serialisation.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-14-storm-sc0013-outline-copy-bdd.md` | New QUEST spec | Auditable delivery gate |
| `src/Unlimotion.Test/OutlineClipboardCopyContract.cs` | New | Existing evidence orchestration |
| `src/Unlimotion.Test/StormBdd/OutlineClipboardCopyStepDefinitions.cs` | New | `SD-0163..SD-0166` |
| `src/Unlimotion.Test/StormOutlineClipboardCopyExecutableSpecTests.cs` | New | Scenario runner |
| `src/Unlimotion.Test/StormBdd/StormStepDefinition.cs` | Modify | Minimal ephemeral context |
| `docs/product/storm.json`; six reports | Modify | Canonical BDD traceability/metrics |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `SC-0013-001` | linked-only, `automated`, no steps | `passing`, `TS-0067`, `SD-0163..SD-0166` |
| `ST-0013` | 0/2 executable | 1/2 executable |
| Behavior coverage | 41/45 | 42/45 |

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
| 1. Ясность цели и границ | 5 | One export scenario, explicit stop rules |
| 2. Понимание текущего состояния | 5 | Existing evidence and lifecycle inspected |
| 3. Конкретность целевого дизайна | 5 | IDs, files, flags and sequence fixed |
| 4. Безопасность (миграция, откат) | 5 | Test-only rollback and no OS clipboard access |
| 5. Тестируемость | 5 | Direct and BDD gates are named |
| 6. Готовность к автономной реализации | 5 | No user-owned decision remains |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does export preserve expected Markdown, descriptions and subtree scope? | PASS | Three complementary checks named |
| UX / designer | applicable | Do hotkey/context-menu copy and text output remain protected? | PASS | Existing headless UI evidence retained |
| Tester / validation | applicable | Are format, settings and command paths independently evidenced? | PASS | Exact three methods and BDD command named |
| Developer / architect | applicable | Are fixture ownership and BDD boundary coherent? | PASS | `finally` + limiter specified |
| Delivery / operations / security | not applicable | No deploy/config/secret/runtime change | PASS | No external clipboard access |

### Post-SPEC Review
- Статус: PASS после исправления.
- Scope reviewed: this SPEC; central instruction stack and local UI override; `storm-product-development`, `dotnet-desktop-client`, `ui-automation-testing`; unchanged `SC-0013-001`, `AC-0037`, `GR-037`; three selected tests and fixture cleanup; planned files plus reports.
- Decision: active user auto-approval permits EXEC.
- Review passes:
  - Scope/Evidence: PASS. Scenario, tags, existing test names, feature wording and planned IDs are exact.
  - Contract: PASS. New bridge only orchestrates existing isolated tests; `MainWindowViewModelTests.Dispose()` and UI-method self-cleanup are explicit.
  - Adversarial risk: PASS after fix. Contract covers format/settings/command separately; new class joins `AvaloniaHeadless`.
  - Role-Based: PASS. Domain/UX/tester/developer reviews are above; delivery is not applicable.
  - Fix and re-review: PASS. Draft originally risked treating the UI check as proof of Markdown settings; specification now requires independent service and ViewModel checks.
  - Stop decision: PASS; no user-owned decision and no external-state requirement.
- Evidence inspected: feature lines 9-14; service Markdown+description test; ViewModel settings copy test and `BaseModelTests.Dispose`; tree command headless session plus `try/finally`; existing `AvaloniaHeadless` attributes.
- Depth checklist:
  - Scope drift / unrelated changes: only this SPEC before EXEC.
  - Acceptance criteria: `AC-0037` remains unchanged and maps to three checks.
  - User-observable scenarios / Decision ledger / Expected objections: explicit, no blocking choice.
  - Validation evidence: exact commands added.
  - Unsupported claims: no full-suite, UI-video or real clipboard claim.
  - Regression / edge case: settings composition, hotkey/context-menu selection, fixture cleanup and parallelism covered.
  - Comments/docs/changelog: only Russian SPEC and later STORM reports are needed; no changelog impact.
  - Hidden contract change: prohibited; bridge has only ephemeral context/result state.
  - Manual-review challenge: missing independent format/settings evidence would overclaim coverage; it is now mandatory.
- No-findings justification: after the evidence-scope correction every required pre-approval section, lifecycle boundary and reproducible evidence link is present.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | evidence | Draft could have treated UI command evidence as Markdown/settings evidence. | Require separate service and ViewModel gates. | fixed |
| — | scope/design/risk | Нет находок после повторного review. | — | closed |

- Fixed before continuing: independent service and ViewModel evidence is explicit.
- Checks rerun: spec linter, rubric and review-loop checklist reviewed against amended SPEC.
- Needs human: нет; approval already active.
- Residual risks / follow-ups: historic full-suite timeout remains outside this slice.

### Post-EXEC Review
- Статус: PASS.
- Scope reviewed: approved SPEC; clean pre-EXEC worktree; new contract, step definitions, executable spec and context fields; `storm.json`, six reports and exact feature scenario; production/UI/feature/existing-test diffs are empty.
- Decision: changes satisfy the approved test-only scope and can be committed.
- Review passes:
  - Scope/Evidence: PASS. `TS-0067`, `SD-0163..SD-0166`, three direct checks and metrics `42/45` agree.
  - Contract: PASS. Existing checks all pass before flags are set; `MainWindowViewModelTests` fixture is disposed; new BDD class serializes ViewModel/UI methods.
  - Adversarial risk: PASS. Markdown+description formatting, enabled settings, child/root selection, hotkey/context-menu routing and normal Ctrl+C non-interference are exercised by complementary existing tests.
  - Role-Based: PASS. UI state evidence is headless fallback because no UI behavior/layout changed; domain and developer contracts are preserved.
  - Fix and re-review: PASS. Draft artifact evidence said validator was pending; after validation it now records the actual `0 errors, 16 warnings, 42/45` result.
  - Stop decision: PASS; no separate delivery-task is required.
- Evidence inspected: Build Release errors 0 (69 existing warnings); BDD 1/1; service Markdown-format 1/1; ViewModel settings 1/1; tree-command UI 1/1; validator 0 errors/16 warnings; `git diff --check`.
- Depth checklist:
  - Scope drift / unrelated changes: only planned files; no production/UI/feature/existing-test change.
  - Acceptance criteria: `AC-0037` remains unchanged and maps to three complementary checks.
  - User-observable scenarios / Acceptance-to-test matrix / Expected objections: all checks pass; import/paste remains explicitly deferred to `SC-0013-002`.
  - Validation evidence: direct and bridge TUnit output plus validator recorded.
  - Unsupported claims: full suite and real OS clipboard runtime remain unclaimed.
  - Regression / edge case: BDD class serialisation, fixture cleanup, Markdown descriptions and ordinary Ctrl+C non-interference checked.
  - Comments/docs/changelog: Russian SPEC and reports updated; no changelog needed.
  - Hidden contract change: none; only test-local context/result fields added.
  - Manual-review challenge: bridge must not turn the UI routing test into a claim about Markdown settings; service and ViewModel tests independently carry those assertions.
- No-findings justification: code follows the established BDD contract pattern, targeted evidence passes and canonical traceability is structurally validated.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | evidence | Draft artifact entry said validator was pending after it had run. | Record actual validator result. | fixed |
| — | spec compliance / regression / tests / docs | Нет находок после re-review. | — | closed |

- Fixed before final report: validator evidence wording updated.
- Checks rerun: artifact validator and `git diff --check`.
- Validation evidence: build errors 0; BDD 1/1; service/VM/UI 3/3; validator 0/16.
- Unrelated changes: none.
- Needs human: нет.
- Residual risks / follow-ups: historic full-suite timeout remains outside this slice.

## Approval
Active workflow auto-approval after PASS review; canonical phrase: "Спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Select outline-copy BDD bridge | 0.96 | Нет | Review SPEC | Нет | User auto-approval already active | Existing service/ViewModel/UI evidence covers AC-0037 without product changes | This SPEC |
| SPEC | Review and correct evidence boundary | 0.98 | Нет | EXEC | Нет | User auto-approval already active | Separate format/settings/command checks avoid a weak UI-only claim | This SPEC |
| EXEC | Implement and validate BDD bridge | 0.97 | Нет | Post-EXEC review and commit | Нет | User auto-approval already active | New bridge reuses three passing complementary checks; validator confirms 42/45 | Test files, storm artifacts, reports |
