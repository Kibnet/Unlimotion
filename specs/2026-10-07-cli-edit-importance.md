# Редактирование Importance через Unlimotion CLI

## 0. Метаданные
- Форма: expanded, medium. Меняется публичный request-контракт и общий application service, который пишет task storage.
- Владелец: этот чат; один writer для перечисленных файлов. Чужие workspace/AreaIds изменения не интегрируются.
- Профиль: dotnet-desktop-client; context testing-dotnet; overlay ui-automation-testing для проверки persisted поля в существующем UI.
- Central stack: routing-matrix; creator-vibe-lens (полный skill не нужен); model-behavior-baseline; tool-execution-baseline; collaboration-baseline; quest-governance; quest-mode; testing-baseline; spec-linter; spec-rubric; review-loops. Локальный AGENTS.override.md сохраняется.
- Поверхность: Codex desktop, Windows/PowerShell, фактический model ID в окружении не раскрыт; оценка model behavior неприменима, меняется .NET CLI.
- Рабочая SPEC: этот файл в существующем checkout. После approval реализация в отдельной ветке от проверенного main; подготовленный PR #318 остаётся отдельной задачей.
- Проверенный main: dce4e1961b3f29e33a2e439730cd09cac885a872. Текущая локальная установка: 1.32.1-local.20261007.dce4e196.
- Instruction source/template: C:/Users/Kibnet/.codex/agents/AGENTS.md и templates/specs/_template.md.
- Связанный исторический документ: specs/2026-10-04-cli-night-agent-context-and-preview.md, §2/§6: importance write был явно исключён из MVP. Эта SPEC расширяет именно этот outcome; прошлые approval не принимаются за её approval.

## 1. Overview / Цель
Агент меняет важность существующей или только что созданной задачи через существующий `apply setField`, предварительно читает ETag, показывает full preview и после применения видит сохранённое число в `task --include details`.

Outcome contract:
- Исходное поручение: «Мне тут агенты жалуются что ты так и не сделал редактирование importance через CLI».
- Симптом: importance читается, но отсутствует в writable fields. Установка актуального main сама по себе этого не исправила.
- Success means: обычный installed CLI принимает согласованный request, preview показывает изменение `/details/importance`, guarded apply сохраняет значение, retry корректен, unrelated поля остаются прежними.
- Output: код, тесты, schema/help/README/example и проверенный локальный CLI для агентов. Source PASS, full validation и installed smoke учитываются отдельно.
- Stop: invalid/stale/guard failure не вызывает новых task writes; новые shared prerequisites не копируются из чужого dirty checkout; обязательные gates не заменяются targeted PASS.

## 2. Текущее состояние (AS-IS)
- `TaskItem.Importance` — persisted `int`, default 0. Domain не объявляет диапазон 0..100. UI NumericUpDown имеет свои границы, они не определяют storage-контракт.
- `TaskApplicationCommandService.SetField` принимает title, descriptionUserText, plannedDuration, plannedBeginDateTime, plannedEndDateTime; importance отвергает default branch.
- `TaskApplicationOperation.Value` и CLI `ApplicationOperationInput.Value` — string. Request schema v1 также требует string.
- `TaskApplicationPreview.Project` уже включает числовой importance, task details/search/candidates уже читают его.
- `IsOperationAlreadyApplied` не распознаёт importance. `CreatedTaskMatches` моделирует последующие операции, но проверяет `task.Importance == 0` без учёта setter.
- Apply проверяет postconditions после настоящего commit. Неполное добавление setter способно привести к committed writes + outcomeUnknown без receipt.
- Установленный CLI проверен через `version` и `apply schema --kind request`: importance отсутствует в enum setField.
- Собственный PR #318 использует будущий AreaIds и сейчас не собирается. Importance не требует AreaIds и может быть реализован на самостоятельном main base.

## 3. Проблема
У существующего application protocol отсутствует один setter и его final-state verification, поэтому агент не может выполнить запрос владельца об изменении важности через штатный CLI.

## 4. Цели дизайна
Расширить действующий write protocol, сохранить ETag/guard/receipt/atomic staging, использовать существующее persisted поле, не менять сериализацию value у остальных operations. Не вводить вторую write-команду и отдельный storage path.

## 5. Non-Goals (чего НЕ делаем)
Wanted/repeater setters, автоматическое ранжирование, изменение UI layout/границ NumericUpDown, новые поля модели, clearField importance, createTask importance property, AreaIds/IsGoal cleanup, исправление известного combined parentIds-create bug, bulk backfill живых задач и публичный NuGet release. PR #318 не расширяется скрыто этим diff.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- Общий application service: один parser importance, staged SetField, inspection/postconditions и composed-create expected state.
- CLI schema/help/example/README: обнаруживаемый контракт, готовый request и путь preview → apply → read-back.
- Tests: реальные CLI процессы, service/preview/replay, UI persisted read-back, negative/no-write и install smoke.

### 6.2 Детальный дизайн
Операция request schema v1 остаётся существующим setField; value сохраняет тип string:

```json
{"operationId":"set-importance","kind":"setField","taskId":"task-id","field":"importance","value":"42"}
```

Разрешено целое Int32 в invariant decimal: canonical строка `0` либо необязательный минус и ненулевой первый digit, затем ASCII digits. Границы -2147483648..2147483647. `-0`, `+42`, leading zeros, whitespace, decimal/exponent, null, пустая строка и overflow запрещены. JSON numeric value не превращается молча в string. Это представление существующего int, а не новая шкала важности. Обычный пример — 42.

Общий parser используется SetField, IsOperationAlreadyApplied и CreatedTaskMatches. Ошибка — существующий InvalidArguments с operation/task context. Проверять staged graph до commit; invalid operation в середине batch оставляет все task files/receipt неизменными.

В CreatedTaskMatches expected importance начинается с 0 и меняется при последующем setField importance для этого newTaskId; сохраняются все остальные проверки created task. Повтор setField одного task/field в batch сохраняет существующую duplicate-write policy; не вводим last-write-wins.

Существующий numeric `/details/importance` участвует в full diff/effect hash/guard; новый projection version не нужен, request envelope version остаётся 1. Schema enum расширяется; для importance value добавляется conditional lexical pattern, диапазон проверяется service. Старые request payloads валидны; старый CLI новый field отклоняет.

`apply example set-importance --format json` возвращает полный v1 request с placeholder task-id/etag; старый set-field example остаётся прежним. Help apply перечисляет writable importance, string representation и диапазон. README показывает read ETag, request, full preview, guarded apply, read-back.

Visual planning artifact: существующая карточка с `CurrentTaskImportanceInput`, состояние до — 0, после fresh storage load — 42. Layout и другие controls остаются существующими; planned screenshot inspect подтверждает именно число. До UI write flow отсутствовал, failing baseline video неприменим. После — Headless UI test frame/log; native-window recording для Avalonia.Headless не применим (нет native window), fallback — сохранённый и просмотренный PNG actual control плюс assertion, команда в §11. При доступном безопасном recorder реального automated window сохраняется и passing video; evidence не выдаётся за физическую проверку.

Производительность: один parse Int32 на operation, существующий staged graph/commit; дополнительных обходов всего storage и benchmark не требуется.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Expected output | Evidence | AC |
| --- | --- | --- | --- | --- |
| Существующая задача | ETag → setField importance 42 → full preview → guarded apply | Numeric diff, persisted details=42, receipt | CLI process + raw read-back | AC1–3 |
| Создать и установить importance | createTask, затем setField для newTaskId в одном request | Applied без outcomeUnknown, task=42 | Process + service inspection | AC4 |
| Повтор | Повтор request с receipt и после удаления только своего receipt | AlreadyApplied; без повторных task writes | bytes/mtime + inspection | AC5 |
| Ошибка или конкурентная запись | Invalid value / stale ETag / изменённый preview effect | Existing error; чужие данные сохранены | Negative process/guard tests | AC6 |
| Агент обнаруживает возможность | schema/help/example локально установленного CLI | Field и готовый request доступны | Fresh shell smoke | AC7–8 |
| Карточка задачи | Fresh load fixture после CLI save | Importance control=42 | Headless test, inspected frame | AC9 |

### 6.4 State / Interaction Matrix
| State | Trigger | Result | Edge case | Notes |
| --- | --- | --- | --- | --- |
| Current task, fresh ETag | Valid importance | Staged change, затем explicit apply | Same value: existing no-op behavior | No forced rewrite |
| No task | Set existing id | Existing task-not-found failure | Created earlier in batch supported | No implicit create |
| Any task | Bad value / bad batch | InvalidArguments, no commit | Int32 extremes valid | No clamping |
| External edit | Stale ETag/preview | Existing stale denial | Preserve external bytes | No implicit retry |
| Receipt matches | Retry | Historical AlreadyApplied | Later drift: inspect distinguishes state | Historical receipt semantics unchanged |
| Receipt missing, desired state all | Retry | Existing desired-state recovery | Unrelated task/receipt unchanged | Receipt-only write if existing protocol requires it |

### 6.5 Decision Ledger
| Decision | Owner | Chosen option | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Shape | agent | Existing setField, string value | 1.0 | Breaking DTO if numeric added | Нет |
| Range | agent | Existing persisted Int32, not inferred UI 0..100 | 0.95 | Unexpected range restriction otherwise | Нет |
| Lexical form | agent | Canonical signed decimal | 0.95 | Agent needs exact docs/example | Нет |
| Reset | agent | setField value=0, no clearField | 0.95 | Avoid redundant API | Нет |
| Base | agent | Separate branch from refreshed main | 1.0 | Avoid known broken #318 dependency | Нет |
| Local availability | user intent | Continue earlier requested local CLI installation after passing gates | 0.95 | Candidate must be validated before active update | Нет |
| Public delivery | boundary | This SPEC does not publish new PR/package automatically; previous #318 merge scope remains separate | 1.0 | Mixing approved scopes | Нет |
| New behavior approval | user | Exact approval of this SPEC required separately as phase gate | 1.0 | Old SPEC explicitly excluded setter | Нет нерешённого design choice; phase approval ожидается |

### 6.6 Runtime / Config / Data Contract Matrix
| Area | Source of truth | Change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Persisted value | TaskItem.Importance int | Setter only | No migration/new field | Raw JSON + domain read |
| Request | v1 schema + string DTO | New allowed field | Existing payloads unchanged | Schema/process validation |
| Preview | Existing numeric projection | Existing diff exercised | No projection bump | Independent replay oracle |
| Retry/commit | Common service + receipt | Final importance included | Old requests same expected 0 | Composed create/retry tests |
| Local tool | Global tool path | Approved feature candidate after full gates | Backup old nupkg/shim; no live task mutation | Fresh shell path/version/hash/smoke |

## 7. Бизнес-правила / Алгоритмы
Only explicit request value changes importance. No priority inference from status, Wanted, title, availability or rank. All int parsing is culture-independent. Existing ETag/transaction/duplicate-field guards stay intact. Domain value is numeric; wire value remains string.

## 8. Точки интеграции и триггеры
SetField for first application; IsOperationAlreadyApplied for postconditions/inspect/retry; CreatedTaskMatches for create+later-set. Test domain entry points as well as CLI parsing. Candidate uses shared service; CLI does not write JSON directly.

## 9. Изменения модели данных / состояния
New persisted fields: none. Only existing Importance changes as an explicit business edit. Preserve Wanted, status/history, relations, planned dates, duration, criteria, description/agent block, repeater and extension metadata. Existing audit timestamps and required availability normalization follow the common service and are identified separately in preview; do not promise byte identity of the legitimately updated task. AreaIds preservation on a future accepted base belongs to its common snapshot contract; do not add it here.

## 10. Миграция / Rollout / Rollback
No migration/backfill. After approval refresh main and verify exact base/source state, prepare separate branch, build/test/pack an isolated candidate, then isolated installed-tool smoke on disposable tasks. Continue the user's earlier local installation goal only after full mandatory validation; backup current nupkg/shim, resolve active command path and verify version/DLL hash, then repeat same smoke from a fresh shell. Do not touch user's real tasks for acceptance.

Rollback: restore previously backed-up local package with dotnet tool update --global --allow-downgrade from local feed; verify path/version and read-only help/schema. Reverting executable does not undo authorized task edits; a task rollback requires fresh ETag and separate explicitly desired request. No automatic overwrites/backfill. Git delivery for this new feature is separate from existing PR #318 and public NuGet publication excluded.

## 11. Тестирование и критерии приёмки
AC1: v1 string request accepts importance canonical Int32; schema/help/example agree, existing fields unchanged.
AC2: full preview has numeric before/after `/details/importance`; no task/receipt writes and guard checks exact effect.
AC3: guarded apply saves 42; authoritative task and subsequent task details match; unrelated fields preserved.
AC4: create+set importance has final postconditions all, successful apply/receipt and no outcomeUnknown.
AC5: inspect/retry with matching and missing receipt has existing expected semantics, no repeated task writes; drift is visible via inspect.
AC6: invalid lexical form, overflow, numeric/null value, missing task, duplicate field, invalid multi-op batch, stale ETag/preview all preserve task/receipt bytes and mtimes except protocol-owned cleanup explicitly characterised.
AC7: example set-importance runs after replacing placeholders; existing example set-field still works.
AC8: locally installed candidate reachable from fresh shell supports full normal agent flow on disposable fixtures; version/hash record identifies tested source.
AC9: freshly loaded Headless task card shows saved 42 at CurrentTaskImportanceInput, inspected PNG; UI regression tests pass.
AC10: affected builds and full Main/Headless suites green on identical final source; post-EXEC review/completion gate passes. If unrelated full failures occur, report exact evidence and readiness remains open.

Mandatory because public write contract/shared service: regression RED, targeted CLI/service/preview/replay/UI, build, full Main + Headless. No runtime checks are claimed in SPEC phase. Reuse existing test harness, do not create a parallel integration framework.

Commands planned from approved execution checkout, serial builds/UI:
```powershell
dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --list-tests
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter '/*/*/TaskApplicationImportanceTests/*'
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter '/*/*/UnlimotionCliIntegrationTests/*'
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter '/*/*/TaskApplicationPreviewTests/*'
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter '/*/*/TaskApplicationPreviewReplayTests/*'
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter '/*/*/TaskImportanceUiTests/*' --maximum-parallel-tests 1
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build -- --maximum-parallel-tests 1
dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release --no-build -- --maximum-parallel-tests 1
```
Confirm project paths/runner flags against refreshed base before launch; discovery must show new tests (nonzero). Logs, source SHA and installed-tool smoke evidence stored outside committed source artifacts. Preserve full gate on timeout; diagnose before retry, no same failed invocation without new evidence.

### Acceptance-to-Test Matrix
| AC | Automated test/check | Evidence | If not tested |
| --- | --- | --- | --- |
| 1,6 | TaskApplicationImportanceTests26/26 + actual CLI86/86, invalid bytes/mtime snapshots | Final-source complement primary TRX; installed importance37 checks | PASS |
| 2,3 | Preview13/13 + independent replay1/1 + guarded process/raw read-back | Final-source primary TRX; candidate and installed numeric full preview/read-back | PASS |
| 4,5 | Composed-create/inspect/receipt tests and three service lifecycle paths | Primary TRX; installed composed42/all postconditions, matching/missing receipt with inventories | PASS |
| 7 | Help/schema/examples process round trip, old example retained | CLI86/86; candidate/installed help/schema/set-importance example | PASS |
| 8 | Isolated package then active fresh-shell flow | install-manifest: version60a9af2e, DLL/package SHA, actual path,37+24 checks | PASS |
| 9 | Semantic4/4 + rendered4/4, including fresh CLI fixture | Final-source primary TRX; root and fallback reviewer viewed PNG42 | PASS; physical native/FlaUI not performed |
| 10 | Release builds and exact full Main1394 + Headless55 on source13 | Five actual Completed/native0 gates; exact four-part Main IDs; expanded post-EXEC below | PASS: final delivery review closed |

## 12. Риски и edge cases
Most material: setter-only fix causes post-commit outcomeUnknown; address all three service paths. Locale-dependent parse or numeric/string confusion causes silent coercion; test explicit canonical input and ru/en culture. Raw unknown metadata preservation and stale guard must remain intact. No use of dirty shared source to make build pass.

### Expected User Review Objections
| Objection | Why likely | Mitigation | Status |
| --- | --- | --- | --- |
| «Опять есть в исходниках, но агенты не могут пользоваться» | Earlier install missed write feature | Active fresh-shell end-to-end AC8 mandatory | mitigated |
| «Почему importance нельзя применить к новой задаче?» | Existing hardcoded created value | Explicit composed-create AC4 | mitigated |
| «Зачем опять ждать AreaIds?» | #318 broken dependency | Separate main base, no AreaIds dependency | mitigated |
| «Ты поменял другие поля задачи» | Common write pipeline | Unrelated field/raw metadata assertions + preview | mitigated |

Rework Prevention Checklist: original complaint preserved; six observable scenarios mapped; parser/range/base/authorization decisions explicit; four objections addressed; applicable roles reviewed in §19; AC verify behavior rather than preparation; installed flow remains mandatory.

## 13. План выполнения
Exact approval → verify/refresh main and separate checkout without unrelated candidate → add regression tests confirming rejected importance → implement shared setter/matching/schema/help/docs/example → targeted and UI checks → required full validation and post-EXEC review → candidate pack/install smoke → active local update and repeated smoke. Git/public delivery remains separately scoped.

## 14. Открытые вопросы
No unresolved product decision. Approval of this new public setter is required by QUEST; previous CLI scope explicitly excluded it. Runtime readiness of execution base and mandatory tests is verified in EXEC, not invented in SPEC.

## 15. Соответствие профилю
Persisted UI field verified by existing Headless test/automation-id. No UI thread writes introduced; production UI selectors/layout untouched. Build/tests required, exact TUnit filters/discovery, full public-contract regression, observed control PNG fallback and active installation evidence specified.

## 16. Таблица изменений файлов
| File | Change | Reason |
| --- | --- | --- |
| src/Unlimotion.TaskTreeManager/TaskApplicationCommandService.cs | Parser, SetField, IsOperationAlreadyApplied, CreatedTaskMatches | One complete setter contract |
| src/Unlimotion.Cli/apply-request-v1.schema.json | Enum + importance lexical conditional | Discoverable syntax |
| src/Unlimotion.Cli/CliIntrospection.cs | Help and set-importance example | Agent usability |
| src/Unlimotion.Cli/README.md | Full importance flow | Correct usage |
| src/Unlimotion.Test/TaskApplicationImportanceTests.cs | New focused service edge cases | Typed/atomic contract |
| src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs | Process/receipt/guard/schema/examples | Actual command behavior |
| src/Unlimotion.Test/TaskApplicationPreviewTests.cs | Numeric diff/no-write importance | Reviewed effect |
| src/Unlimotion.Test/TaskApplicationPreviewReplayTests.cs | Independent numeric replay | Completeness oracle |
| src/Unlimotion.Test/TaskImportanceVisualUiTests.cs | Saved CLI value shown after fresh load in existing rendered fixture | Existing UI contract and isolated rendered process |
| src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs | Bounded awaited actual layout readiness before existing assertions | Runtime-confirmed test harness gap; production UI and assertions preserved |
| src/Unlimotion.Test/TaskCardLayoutUiContract.cs | Await bounded readiness of card controls and narrow scrollViewer before old assertions | Repeated card.Width=0; no new forced sizes, same visibility/containment/relations assertions |
| tests/Unlimotion.UiTests.Headless/Infrastructure/HeadlessSessionHooks.cs | Existing persistent lifecycle also covers whole live refresh class | Multi-dispatch fixture must retain dispatcher while watchers/window live |
| tests/Unlimotion.UiTests.Headless/Tests/CliLiveRefreshHeadlessTests.cs | Actual asynchronous UI context and bounded dispatch/worker fault propagation | Heap-confirmed Headless harness lifetime failure; assertions preserved |
| specs/2026-10-07-cli-edit-importance.md | Approval/audit journal | This SPEC only before EXEC |

## 17. Таблица соответствий (было -> стало)
| Area | Before | After |
| --- | --- | --- |
| Write | Importance unsupported | Explicit setField importance |
| Create postconditions | Always importance=0 | Expected composed final importance |
| Preview | Importance readable | Same numeric field changes via setter |
| Local agents | Read only | Validated installed write workflow |

## 18. Альтернативы и компромиссы
Separate `set-importance` command duplicates guards/receipts and is unnecessary. JSON numeric value would require changing shared string DTO/protocol; string preserves v1. Restricting to UI 0..100 would add an unsupported domain policy; persisted Int32 retains storage semantics. Adding to #318 couples an independent feature to absent prerequisites; separate main branch provides a valid build path.

## 19. Результат quality gate и review
### SPEC Linter Result
| № | Block / criterion | Status | Evidence |
| --- | --- | --- | --- |
| 1 | A outcome | PASS | §1 original complaint, installed agent flow |
| 2 | A AS-IS | PASS | §2 installed version/schema + three service paths inspected |
| 3 | A problem | PASS | §3 missing setter/final-state contract |
| 4 | A design goals | PASS | §4 reuse application protocol |
| 5 | A boundaries | PASS | §5 excludes classification/other setters/live backfill |
| 6 | B responsibilities | PASS | §6.1 CLI/common service/tests ownership |
| 7 | B integration | PASS | §8 planning/commit/inspect/retry/create paths |
| 8 | B invariants | PASS | §6.2/§7 canonical Int32, shared parser, duplicate policy |
| 9 | B errors/recovery | PASS | §6.4 invalid/stale/receipt drift |
| 10 | B performance | PASS | §6.2 constant parser, existing graph path |
| 11 | C data/state | PASS | §9 business/audit field distinction |
| 12 | C compatibility | PASS | §6.6 v1/string/int unchanged, no migration |
| 13 | C rollback | PASS | §10 backed-up package, explicit task rollback distinction |
| 14 | D AC | PASS | §11 ten verifiable behavior criteria |
| 15 | D evidence | PASS | §11 AC matrix includes negative, process/UI/install/full checks |
| 16 | D commands/stop | PASS | §11 TUnit discovery/filter/full suite, timeout diagnosis |
| 17 | E dependencies | PASS | §13 independent main base, no dirty import |
| 18 | E decisions | PASS | §6.5/§14 no unresolved product choice, phase approval separate |
| 19 | E scale/form | PASS | §0 public/storage expanded medium |
| 20 | F profile | PASS | §15 fresh-loaded control, serial .NET validation |
Итог linter: ГОТОВО для SPEC; это не runtime readiness.

### SPEC Rubric Result
| Criterion | Score | Reason |
| --- | ---: | --- |
| Goal/boundaries | 5 | One setter usable by local agents, explicit non-goals |
| AS-IS | 5 | Installed schema and source matcher/commit behavior verified |
| Design | 5 | Parser, input/output/version/errors/inspection defined |
| Safety/rollback | 5 | No migration, staged writes, ETag/guard, package rollback |
| Testability | 5 | Process/create/retry/invalid/UI/installation/full matrix |
| Autonomy | 5 | Representation/base/scope decisions fixed; exact phase approval pending |
Итог: 30/30, ready as a plan. Mandatory EXEC evidence remains pending.

### Role-Based Review Result
| Role | Applicability | Question checked | Verdict | Required changes |
| --- | --- | --- | --- | --- |
| Business analyst/domain | applicable | Can agent explicitly change priority without automatic inference? | PASS | Int32 source of truth rather than inferred UI bounds |
| UX/designer | applicable | Can agent discover command and owner see saved 42? | PASS | Help/example + real Headless control read-back/PNG |
| Tester | applicable | Do invalid/bounds/create/retry/stale/installed flows have evidence paths? | PASS | AC1–10 matrix, future runtime not claimed |
| Developer/architect | applicable | Does new setter include final postconditions and preserve v1? | PASS | All three branches, existing numeric projection |
| Delivery/operations/security | applicable | Can broken #318 or global update contaminate result? | PASS | Independent base, pack/smoke/backup/active verification; separate public delivery |

### Post-SPEC Review
- Root Scope/Evidence pass: current SPEC, canonical template/owners/profiles/local override, TaskItem.Importance, SetField/ValidateRequest/CreatedTaskMatches/IsOperationAlreadyApplied/EvaluatePostconditions, RunApply receipt order/ReadApplicationRequest, string input DTO, request schema, CLI examples, existing integration composed-create/retry tests, preview/replay source and TaskImportanceUiTests inspected. Installed `version` and emitted request schema inspected; exact paths/classes verified.
- Contract pass: complaint maps to AC8 installed agent usability, not source-only delivery; preview uses existing numeric field; shared service handles all application lifecycle paths; old CLI MVP and #318 remain distinct.
- Adversarial risk pass: tested design against created task default=0, receipt-missing replay, malformed/coerced input, duplicate write targets, external ETag drift, raw metadata loss, accidental UI-range restriction, source-only install claim and future AreaIds dependency.
- Role-Based pass: five applicable roles above. UI planning describes existing control/state; new flow lacks meaningful pre-feature baseline video, Headless native video fallback explicitly bounded.
- Fix and re-review: phase approval initially appeared as unresolved Decision Ledger choice; corrected as separate gate. "Unrelated dates unchanged" initially overpromised normal audit timestamps; §9 now explicitly permits existing UpdatedDateTime/availability effects, retains planned fields. Rechecked against `UpdatedDateTime = NextUpdated(...)` and duplicate target validation in service.
- Depth checklist: scope/unrelated v1 and #318 preserved; all AC mapped; evidence lists actual reads versus future tests; no build/install/publication claim; parse/post-commit/retry/guard edges covered; docs/example and output type fixed; no hidden schema/projection bump; manual challenge "setter writes then outcomeUnknown" addressed by composed-create and receipt tests.
- No-findings justification for root after fixes: no unowned source edit proposed, no unverified runtime PASS, no undefined representation or final state, all requested user flows have mandatory verification paths. Tests/install readiness are explicitly future EXEC gates.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Phase contract | Approval looked like unresolved product choice | Separate approval from design questions | fixed/root re-reviewed |
| MEDIUM | Data preservation | Audit dates initially included in blanket unchanged claim | Preserve planned dates, retain normal service audit effects | fixed/root re-reviewed |

- Separate adversarial fallback: reviewer /root/importance_spec_review вернул PASS для плана, без новых contract findings. Проверены installed version/schema, int model/string DTO, отсутствие number→string converter, setter/created matcher/retry branches, post-commit outcomeUnknown counterexample, duplicate-field policy, audit/no-op ETag semantics, independent main base, install AC8 и полномочия. Reviewed SPEC SHA256 до добавления этой audit записи: 88633CBA3BB81D4D4F99EA99EA27D51147C7F33DD94BC86F6F147A033381701C. Effective child sandbox danger-full-access: технической read-only изоляции нет; это writable adversarial fallback, не независимый technical read-only review. Reviewer ничего не изменял и не запускал build/tests/install/Git mutation.
- Final post-SPEC stop decision: PASS для плана; можно предъявить и запросить exact approval. Runtime readiness этим не подтверждается. No code/test/install mutations performed; `git status --short` shows only this new SPEC plus unchanged historical untracked v1 (SHA256 0272E803F13416A2108466E66CC909A29FED7FAE777DD400F81DA89BC7ABB136). `git diff --check` checks tracked candidate only; отдельная structural check подтвердила 21 numbered sections, 20 linter rows и все пять pre-approval matrices. Добавление финальной audit записи не меняет reviewed design/AC.
- Residual risk: runtime acceptance/candidate pack/global update require EXEC and full mandatory checks; source evidence does not close them.

### Post-EXEC Review
**Root post-EXEC: PASS, 08.10.2026.** Реализация и локальная установка завершены; отдельный финальный delivery review PASS, AC1–10 закрыты. Root inspected actual source/diff, primary TRX, manifests, installed smoke artifacts and PNG; future execution is not counted as evidence.

**Scope / evidence pass.** Approved design SHA256 FD1B2B726F817381AAB9DF8974DFA0FC4DE0DB000D7AA06F0D2F5FD1266E7D21. Branch feat/cli-edit-importance starts at main dce4e1961b3f29e33a2e439730cd09cac885a872; own12 tracked edits plus new importance test and this SPEC, no unrelated edit. Final13-file source hash 60a9af2e9fd6931c86c19761228d8954b26f9d747da28fc9cf070d1a0aea8a43; unchanged through build, complete tests, pack, candidate and install. Main DLL23069FDA510D9C8B1950D2CF238D58DAC9006064A05F618E2E17EB48E42F0F77. Source state remains uncommitted feature diff on pinned main; immutable source archive/patch, file hashes and base are retained outside the repository.

Artifact root: C:/Users/Kibnet/.codex/cli-installs/20261008-importance-final5. Primary pointers: [complete gates](C:/Users/Kibnet/.codex/cli-installs/20261008-importance-final5/validated-gates.json), [exact coverage](C:/Users/Kibnet/.codex/cli-installs/20261008-importance-final5/main-coverage.json), [feature groups](C:/Users/Kibnet/.codex/cli-installs/20261008-importance-final5/feature-test-evidence.json), [installation manifest](C:/Users/Kibnet/.codex/cli-installs/20261008-importance-final5/install-manifest.json), [installation report](C:/Users/Kibnet/.codex/cli-installs/20261008-importance-final5/installation.md), [root PNG inspection](C:/Users/Kibnet/.codex/cli-installs/20261008-importance-final5/ui-inspection.json).

**Contract pass.** Root re-read setter, CreatedTaskMatches and IsOperationAlreadyApplied, shared canonical parser, schema/help/example/README, independent numeric replay oracle and real CLI fixture. All three lifecycle paths use the same Int32 parser; creation defaults0 and final composed value42 is matched. v1 request value stays a string, projection stays numeric/version1, reset is string0, clearField unchanged. Invalid lexical/range inputs cannot silently coerce. ETag/exact-preview guards, receipts, no-op/drift semantics and ordinary audit dates retain existing contracts. Other raw business fields/unknown JSON survive successful writes; invalid batches remain atomic. No classification, AreaIds, server storage, other setter or live backfill entered scope.

**Adversarial / edge pass.** Final service26 and CLI86 cover full Int32 bounds, ru/en culture, null/numeric/string confusion, leading plus/zero, -0, whitespace/newline, decimal/exponent/Unicode/overflow, missing target, duplicate field, later invalid operation after valid title, stale ETag/preview, matching and missing receipt, no-op and external drift. Preview13 and independent replay1 verify numeric before/after and omitted-importance counterexample; no task/receipt write on preview/refusal. Candidate and installed importance37 include guarded numeric42, raw read-back/business equality, receipt inventories/mtimes, missing-receipt reconciliation and create+importance without outcomeUnknown. Baseline24 separately checks snapshot/content/offline search, exact guards, retry, explicit relations and both parent branches. All writes use disposable fixtures.

**User-observable scenarios / AC result.**
| Scenario | Actual evidence | Verdict |
| --- | --- | --- |
| Existing task: read ETag, preview, guarded apply42, read-back | Final CLI86; candidate37 and installed37 | AC1–3 PASS |
| Create plus final importance | Service and real process tests, installed composed request/all postconditions | AC4 PASS |
| Retry and missing receipt | Primary tests + actual inventories/no task rewrite/expected receipt only | AC5 PASS |
| Invalid input or competing source | Atomic bytes/mtime assertions, stale ETag/preview refusals, negative lexical cases | AC6 PASS |
| Agent discovers and uses installed command | Fresh shell resolves C:/Users/Kibnet/.dotnet/tools/unlimotion-cli.exe; new version, schema/help/example and full flow | AC7–8 PASS |
| Fresh card after CLI save | Semantic4/4, rendered4/4 including actual fresh CLI42; root and reviewer viewed new PNG | AC9 PASS |
| Full final-source validation | Both Release builds; Headless55/55 and Main97+9+17+1271=1394/1394, every expected ID once | AC10 PASS, final delivery review closed |

**Role-based pass.**
| Role | What was challenged | Result |
| --- | --- | --- |
| Business analyst/domain | Explicit importance, original agent usability, no inferred priority or unnecessary range policy | PASS: installed workflow and persisted Int32 |
| UX/designer | Discoverability, complete usable example, visible saved value and safe reset instructions | PASS: installed help/schema/example + inspected42; rendered Headless boundary stated |
| Tester | Meaningful negative/no-write oracle, lifecycle/replay edges and full exact coverage | PASS:26/86/13/1/4/4 feature groups, complete1394+55, fresh-shell37+24 |
| Developer/architect | Shared parser across all lifecycle paths, preserved v1 types and unknown data | PASS: implementation/diff and independent replay reviewed |
| Delivery/operations/security | Tested source really packaged/installed, active path/hash, rollback and foreign state | PASS: package/DLL match, fresh-shell checks, backed-up old package; only fixtures mutated |

**Fix and re-review pass.** Preserved prior failed/incomplete runs rather than relabeling them. Schema trailing-newline edge closed before final freeze. Actual layout readiness waits added only after repeated width0 failures; existing assertions/viewport sizes preserved, no new forced card sizes. The pre-existing card harness already contains manual geometry setup; its entire geometry is not claimed to be natural layout. Live-refresh multi-dispatch lifetime/real UI context/worker fault propagation corrected after primary dump; RunContinuationsAsynchronously fixed the separately confirmed inline self-dispatch. No production UI/factory change. Runner backend-mixing diagnosis replaced invalid per-class order with complete whole partitions. Observation60s failure kept source/deadline/assertions unchanged; whole17-case isolation and remaining1271 retain exact1394 coverage, without claiming CPU causality. Root4 incomplete interruption has unknown cause/native=null, not a fabricated process exit; retained complete gates have matching source/binary. LOW missing Root4 provenance pointer was fixed and fallback re-reviewed CLOSED before final manifests.

**Depth checklist.** Scope and every AC/scenario checked against actual files/results; source-only, targeted-only, candidate-only and installed claims kept distinct. No unverified full/CI/native claim; skipped, abort, timeout, failed and unfinished gates excluded. Guards independently revalidate source/binary/TRX/coverage hashes and sequence before pack; global update verifies previous active version/DLL and rollback package. Docs/schema/help/example agree; reset0 and canonical representation visible. Created matcher/retry/post-commit/manual outcomeUnknown challenge addressed. Existing composed create with newly created parentIds remains a separate known limitation; explicit addRelation workaround passed. Unrelated business metadata, receipt authority and hidden source/projection behavior examined. No pending human product choice or new permission is needed within existing local install authorization.

**Separate adversarial fallback.** /root/importance_spec_review source/contract PASS, fresh PNG42 personally viewed, final-source runtime checkpoint PASS: independently checked primary1394+55, exact IDs/no skips/all five Completed/native0 gates,13 file hashes, binary/TRX/coverage hashes and sequential intervals. Effective sandbox danger-full-access/approval never: technically writable, so this is a writable adversarial fallback, not technically isolated read-only independent review. Reviewer only read/checked artifacts and viewed pixels; no tests/build/install/Git mutation. Final actual installation/report/expanded-block checkpoint PASS: active global path/version, package/DLL/gates hashes, raw42/composed postconditions and installed37+24 independently checked. Reviewer inspected SPEC SHA C879FF24F71F685A2FBCB686D79AE0AD47EB55058FCDE2C022C00984304C3668 before this audit-only closure. LOW report quoting was a serialization false positive: raw writer/report both contain zero backslash characters in the two value examples; no source/report fix required.

**No-findings justification / stop decision.** Root found no unresolved behavior or provenance gap after the recorded fixes: original omission is reproduced, all lifecycle paths are implemented, final-source full gates and rendered value pass, and the tested package is actually installed. Package1.32.1-local.20261007.importance.60a9af2e SHA2569D3194875E203907BE5899BAC7851EB7A9B48B0C17BC8B9B7BF44BBE5C3B1CD1; installed DLL7967237D9D00248F84356F79D2A414823ABBD59EE6BE588AA9715EFA022E4A81 matches candidate. Fresh-shell37+24 PASS at2026-10-08T11:17:42Z. Prior all-in-one UI instability and unknown interruption remain transparent evidence boundaries; physical native/FlaUI and CI not performed. No importance PR/merge/public release/live task mutation. Final stop decision PASS: separate final delivery checkpoint returned no open findings; all AC1–10 closed. No required work remains for the authorized importance implementation/local installation. No new human permission or product decision is needed. This audit-only closure does not change reviewed source, package, outcomes or limits.

## Approval
Получена фраза «Спеку подтверждаю» 07.10.2026 для предъявленной SPEC SHA256 FD1B2B726F817381AAB9DF8974DFA0FC4DE0DB000D7AA06F0D2F5FD1266E7D21. Фаза EXEC для своего importance scope; нового публичного PR/release это не разрешает.

## 20. Журнал действий агента
| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC: reported omission | New narrow importance setter outcome; historical MVP explicitly excluded writes | Installed schema/version and source confirm omission; no implementation/runtime claim | Full post-SPEC review, then show specification | Current complaint; new SPEC approval not received | This SPEC; existing candidates untouched |
| SPEC: reviewed plan | Root full passes + отдельный writable adversarial fallback PASS; исправлены phase-choice и audit-date claims | Linter20 PASS, rubric30/30, scenarios/AC/roles/rollback/install plan checked; runtime pending | Показать SPEC, получить exact approval, затем реализация на отдельном main base | Approval ещё не получено | Только эта рабочая SPEC |
| EXEC: approval/base | Создана отдельная feat/cli-edit-importance от main dce4e196, remote main перепроверен | Managed create_worktree вернул Not a git repository из-за архивированного calling checkout; git worktree add выполнен из actual main после metadata/dirty preflight, unrelated файл main не тронут | Перенести эту SPEC в execution checkout, regression RED → реализация | «Спеку подтверждаю» для этой SPEC | C:/Users/Kibnet/.codex/scratch-worktrees/cli-importance-20261007/Unlimotion |
| EXEC: test harness choice | UI assertion размещается в existing TaskImportanceVisualUiTests с ImportanceCardFixture/ImportanceRenderedProcess вместо semantic-only TaskImportanceUiTests | Existing helper изолирует Skia backend в дочернем процессе и сохраняет real PNG; source paths/filters inspected. Это editorial implementation choice при прежнем outcome/риске, новое approval не требуется | Добавить rendered CLI→fresh-load assertion и targeted filter к обязательным проверкам | Прежнее exact approval действует | Own tests и эта SPEC |
| EXEC: regression RED | До production edits выполнен валидный service regression: 4/4 отказа Field is not writable by application requests; установленная старая версия отдельно отвергла setter без записи | red-2.log/TRX; installed-old-red/smoke-report.json (4 checks). Первые ошибки компиляции test helpers исправлены и не считаются RED | Implement common parser/setter/postconditions | Прежнее exact approval | External artifacts root 20261007-importance-dce4e196 |
| EXEC: implementation/source checks | Setter, created final-state matcher и retry matcher используют один canonical invariant Int32 parser; v1/string DTO сохранены; help/schema/example/README и process/preview/replay/rendered tests добавлены | Service 26/26 PASS; Release Main build 0 errors; 22 schema syntax cases PASS via Test-Json; discovery 1394 tests, все новые методы присутствуют; git diff --check PASS | Process/UI → full Main/Headless → installed candidate | Прежнее exact approval | service-green.log, build-main-2.log, schema-smoke-report.json, importance-discovery.log |
| EXEC: targeted runtime/UI | Whole CLI integration 86/86, preview13/13, replay1/1, semantic importance UI4/4, new rendered CLI fresh-load1/1 PASS, все без skips; Headless Release build 0 errors | Root просмотрел real card.png: selected CLI-created task и видимое число42 в NumericUpDown. Это rendered Headless evidence, не native/FlaUI claim | Full Main1394 + Headless55 на frozen source, затем pack/install | Прежнее exact approval | test-summary.json; targeted-ui/5fe992e875b84fecb404ff35abdd91ee/cli-importance-fresh-load/card.png; source hash f124f7cea2103560567d138f9c30104d1723c2256e034dacc09f35958853db47 |
| EXEC: runtime reviewer checkpoint/fix | Reviewer сам проверил primary TRX, schema22, PNG42 и 9 frozen hashes; targeted runtime подтверждён. MEDIUM в external delivery gate: passed/total без overall outcome/runner counters недостаточно | Исправлено assert-test-gate.ps1: Completed + minimum + all passed + zero non-pass counters. Actual service26 TRX принят; synthetic InProgress, error1, aborted1 при passed=total отклонены. Production/test freeze не менялся, full продолжается | Re-review external gate; дождаться full и installation | Прежнее exact approval | assert-test-gate.ps1, candidate-stage.ps1, synthetic-incomplete/runner-error/run-aborted.trx |
| EXEC: full Main first failure/diagnosis | Full first run 1391/1394, 3 failures, no skips, 57m37s; gate не пройден, pack/install не выполнялись | RenameTask_Success: no title diff; NewTask_TitleNotResetAfterFileSave: save predicate false; RoadmapGraph_DropWithControl: Avalonia collection modified during Show. Все3 отдельно в fresh process прошли без изменений source. Source hash прежний. Primary failure evidence сохранено, standalone PASS не заменяет full | Закончить отдельный full Headless; повторить full Main на тех же исходниках после диагностики, не расширяя feature в UI repair | Прежнее exact approval | full-main.log/TRX; rename/new-title/roadmap-diagnostic logs/TRX |
| EXEC: full Headless / fresh Main retry | Full Headless Completed55/55, native exit0, без пропусков; запущен один свежий полный Main на frozen source после трёх успешных диагностик | Reviewer подтвердил primary Headless TRX и три isolated Completed1/1. Причина первых failures не доказана; первый TRX показывает пересечение UI persistence и CLI subprocess test несмотря на maximum-parallel-tests1, fully serial claim не делается | Ждать полного Main; выбранные completed TRX/SHA/native exit/source hash фиксируются в validated-gates.json до pack | Прежнее exact approval | full-headless.log/TRX; full-main-attempt2.log; retry-full-main.ps1 |
| EXEC: prepared delivery review | Reviewer проверил candidate/install/fresh-shell stages и оба disposable smoke; новых BLOCKER/HIGH/MEDIUM нет. LOW baseline count исправлен: numeric checkCount, имена checks сохраняются отдельно | Это review подготовленных scripts, не execution PASS; pack/global update ещё не выполнялись. Rollback package/shim сохранены; active path/version/DLL проверяются до изменения | После full gates проверить isolated package, затем global tool и fresh shell; relevant final re-review actual evidence | Прежнее exact approval | candidate-stage.ps1, install-stage.ps1, fresh-shell-smoke.ps1, rollback/ |
| EXEC: repeated failure / full-suite isolation plan | В общем attempt2 title-save failure повторился, Rename прошёл. Primary lifecycle trace подтверждает одновременные CLI tests во время title test; причинность не доказана | Подготовлено минимальное editorial runner изменение: весь MainWindowViewModelTests97 и все остальные142класса1297 выполняются последовательно в fresh processes на одинаковом binary/source. Reviewer подтвердил сохранение AC10: каждый исходный test ID ровно раз, exact disjoint union1394, native0/Completed/allPassed/no skip каждого настоящего TRX. Assertions и исходники не меняются; standalone diagnostics не включаются в aggregate | Закончить текущий failed run, затем полный набор в двух процессах. Если целый97-класс снова падает, диагностировать, не выделять только failing test ради PASS | Прежнее exact approval; меняется runner isolation, не outcome/AC/поведение | partition-full-main.ps1, assert-main-coverage.ps1, main-partition-plan.json при запуске; оба общих failed logs/TRX сохраняются |
| EXEC: combined result / complete class PASS | Attempt2 завершён1392/1394, два failures: title-save и existing task-card layout width0; no skips,52m11s. Layout fresh diagnostic Completed1/1 PASS. Целый MainWindowViewModelTests Completed97/97, native0, no skips,3m45s | Оба unfiltered full TRX имеют идентичные1394 stableTestIDs. Source f124f7ce и Main test binary B8BC7FF80A76311B0D0DD42C3246F3377643E99D2895CB3A69E974FC567822B6 неизменны; diagnostics не заменяют полный набор | Выполнить весь complement1297 и exact coverage before pack | Прежнее approval | full-main-attempt2-results; layout-diagnostic-results; full-main-partition-window-results/execution.json |
| EXEC: runner filter correction | Первый OR complement выбрал0/native8 — invocation failure, не test failure. Primary source TUnit1.44 commit42e3be6d99bb637d21e1dac711d76991a99e49c3 показал erroneous literal class prefilter для OR без wildcard | OR Class* отключает этот hint; actual runtime probe двух классов Completed39/39/native0 подтвердил syntax. Запущен complement-v2 с теми же142классами/min1297; успешные97 не повторяются, их source/binary/native/TRX проверены. Exact ID union остаётся mandatory; zero-selection evidence сохранено | Дождаться complement-v2; проверить оба genuine TRX/coverage + Headless55, затем candidate/install | Прежнее approval; source/assertions не менялись | runner-source/MetadataFilterMatcher.cs; filter-runtime-probe-results (не acceptance); resume-main-complement.ps1; main-complement-v2-plan.json |
| EXEC: full-suite isolation revision | Complement-v2 содержит три отказа существующей status recovery matrix: error.Bounds.Width=0. Source inspection показал WaitForAutomationControl ждёт наличие control, не завершение layout; это readiness gap, причинность всех full failures не доказана | Reviewer подтвердил альтернативу без source changes: все143 целых класса в fresh последовательных процессах, exact1394 IDs каждое ровно раз, любой whole-class failure останавливает acceptance. Старые failed evidence сохраняются, standalone diagnostics не включаются. Runner/assert-coverage/candidate144gates/report re-reviewed без новых findings | Завершить текущий failed complement, затем выполнить полный suite по классам; AC8/AC10 всё ещё открыты, pack/global update не выполнены | Прежнее approval; editorial isolation сохраняет outcome/assertions/полноту, не требует нового решения | full-main-by-class.ps1; assert-main-coverage.ps1; candidate-stage.ps1; reviewer writable fallback danger-full-access, read-only поведение без технической sandbox изоляции |
| EXEC: complement completed / all classes started | Complement-v2 Completed1294/1297, native1, no skips,25m50s; три существующих status recovery width0 failures. Acceptance не закрыт и failed TRX сохранён | После его завершения запущен полный runner143freshclasses, первым целый MainControlTaskStatusIconUiTests34. Ни production, ни test assertions не менялись; source hash f124f7ce | Любой failed class остановит acceptance; после успешных143 нужна exact union1394 и Headless55 перед pack/install | Прежнее approval | full-main-partition-complement-v2-results primaryTRX13_55_59; full-main-by-class.log; full-main-class-processes/plan.json |

| EXEC: whole-class failure / harness correction | Fresh целый MainControlTaskStatusIconUiTests34 завершён30/34/native1. Isolation stopped as promised; ошибочные acceptance manifests не созданы | Добавлено bounded awaited actual layout readiness после expansion: RunJobs/UpdateLayout/ForceRenderTimerTick +16ms yield, максимум5s, timeout с ancestor diagnostics. Existing visibility/text/wrap/positive width/window width assertions сохранены, никакого fake MeasureArrange или production UI edit. Это исправление обнаруженного test harness gap, не ослабление AC | Новая source freeze10files85b369575e77c4c0eb1c00c02644bf9a7e71402350d9a2d8efdcafaa042b2565; обе сборки PASS; wholeclass34 → fullMain1394 + fullHeadless55 на finalsource → package/install. Все oldsource results только diagnostics/history | Прежнее approval; reversible validation harness fix в пределах достижения mandatory tests | Новый artifact root 20261007-importance-dce4e196-layout; MainControlTaskStatusIconUiTests.cs helper WaitForTaskOperationErrorLayoutAsync; build-main.log/build-headless-corrected.log/status-layout.log |
| EXEC: corrected harness validation / full final-source run | Whole status UI class34/34 Completed/native0/no skips после awaited actual layout,1m06s. Main/Headless builds PASS; fresh Main discovery1394 unchanged | Reviewer checked helper/all10hashes и primary34TRX; MEDIUM Headless provenance исправлен: реальный process exit/source/binary/times/TRXSHA captures и проверяется до composegate. Новый full Headless55 → Main1394 in143wholeclasses запущен последовательно. Старый inventory используется только как exact expected IDs, не runtime evidence | Дождаться actual full gates; затем новый candidate85b36957 и installed freshshellsmoke. Final runtime/install PASS ещё открыт | Прежнее exact approval | final-source-full-gates.ps1/log; full-main-by-class.ps1; final-discovery.log; reviewer fix-and-re-review closed |

| EXEC: Headless interrupted / primary heap diagnosis | Промежуточный Headless source85b перестал выдавать results после1PASS; сняты Mini/Heap dumps. Heap подтвердил глобальный session worker FAULTED с InvalidOperationException wrong dispatcher owner в DefaultRenderLoop.Add/EnsureIsolatedApplication; CTS не отменён, callers ждут orphaned queue | Только собственный verified PID717544 остановлен, incomplete не засчитан. Live refresh fixture держит окно/VM/watchers между Dispatch, а PerTest сбрасывает application каждый Dispatch. Распространён existing persistent PerAssembly hook lifecycle на весь этот класс; остальные PerTest. Убран synthetic synchronous Post; actual UIcontext связывается наworker, awaited dispatch bounded15s racesworker и сохраняетprimaryfault. Production/factory/assertions не менялись | Новая freeze12files5d27814f14de3adf72760b833b0e78dd80d516cd264c79c390360c1a80c8f620; build → new fullHeadless55 → allMain1394; AC8/10 остаются открытыми | Прежнее exact approval; обратимый минимальный validation harness fix для mandatory gate, без нового поведения product | artifacts 20261007-importance-dce4e196-layout/headless-primary-worker-exception.log/interrupted-execution.json; newroot20261007-importance-final; primary Avalonia12.0.4 source commit a8dd6417fd8918570edefdbecd92d16ac7620069 |
| EXEC: inline continuation diagnosis/fix | Reviewer обнаружил continuation hazard, второй Mini dump подтвердил self-dispatch: test page resolver выполнялся прямо из completion наHeadlessworker. Только own verified PID942696 остановлен, incomplete evidence сохранено | DispatchUiAsync возвращает TCS RunContinuationsAsynchronously; local bounded observer публикует success/fault через него. После Bind есть runtime assertion caller неUIworker; syntheticPost отсутствует, actualUIcontext остаётся. Existing product/assertions/factory не менялись | Новая final2 freeze12files91ffbe61faf702bf60d63b4f0ca9e35b3795aff8ac0ed20b374c3b6d1c1c73de, builds → wholeCLI-live class4 → fullHeadless55 + fullMain1394. Старые результаты только diagnosis, ни pack ни global update не выполнены | Прежнее approval; re-review finding исправляется перед accept/install | previous20261007-importance-final/headless-inline-stacks.log/interrupted-execution.json; final2root scripts/source-manifest/reviewed-source |
| EXEC: final-source full Headless PASS | Final2 Headless55/55 Completed/native0/noerrors/noskips,3m38s; targeted whole live-refresh4/4 PASS23s incl off-worker caller assertion | RealHeadless process provenance source91ffbe61/binarySHA/TRXSHA/start-end captured. Main143wholeclass full запущен после Headless. Active CLI всё ещёold1.32.1-local.20261007.dce4e196 | CompleteMain1394exact coverage → candidatepack/smokes → installedfreshshell → finalpostEXEC actualruntime review | Прежнее approval | final2/full-headless-results primaryTRX/execution.json; cli-live-refresh-results; final-source-full-gates.log |
| EXEC: renderer backend diagnosis / complete two-part correction | Per-class runner остановлен на28классе: первые4 Skia emoji cases PASS, затем InvalidCast HeadlessPlatformTypeface→SkiaTypeface и orphanedworker. Source class смешивает Skia и stub builders; Process-wide renderer selection делает выбранный runner порядок некорректным | Own verified Main PID948864 остановлен сMini dump/record/nativefailure; первые27classPASS не входят вacceptance. Reviewer подтвердил обоснованный возврат к fresh MainWindow97 + весьcomplement1297 нанеизменённом12source91ffbe61: все1394cases заново, exact IDs ровно раз, nativeCompleted/no skips. Существующий Headless55 сохраняется сrealexecution provenance | Новый complete two-part runner26572; afterallPASS кандидат3gates runtime re-review → pack/install. Production/test source больше не меняется | Прежнее approval; structuralrunner correction поconcretebackendfailure, без case-levelretry/исключений/ослабленияAC10 | final2/main-class-processes-interrupted.json/toolbar-backend-mix.dmp/fontlog; final-main-two-partitions.ps1/log; candidate-stage.ps1 re-reviewed PASS |
| EXEC: complete two-part failed / narrow readiness fix | Final2 Main97PASS + complement1295/1297, native2: repeated card.Width=0 and restored-date persisted wait5s in RepeaterStartDate(1400,False). All failures retained; Headless55 and renderedCLI42 were PASS on source91, not final-source acceptance | Source inspection/reviewer confirms scrollparent readiness did not imply childcard readiness. Added bounded real frame wait for existing Desktop/Narrow controls plus scroll; old geometry/assertions kept, no new forced card sizes. Source13 freeze60a9af2e9fd6931c86c19761228d8954b26f9d747da28fc9cf070d1a0aea8a43; both buildsPASS. Shared-state cause of date wait unproven | Whole class9 and layoutclass1 diagnostics, then complete new Main97+9+1288 exact1394 and new Headless55; AC8/10 still pending, no package/install yet | Previous exact approval; necessary narrow test readiness fix and whole-class structural isolation retain all AC | final2/final-main-complement-results TRX/execution; final3/source-manifest/reviewed-source; final-main-three-partitions.ps1 and candidate4gates |
| EXEC: new source readiness diagnostics / full suite plan reviewed | Whole RepeaterStartDate9/9 and layoutclass1/1 Completed/native0/no skips on source13 60a9af2e; real file-restored-date assertion remains unchanged. This supports whole-class isolation, not proven shared-state causality or full acceptance | Reviewer inspected new source and concrete3part runner, exact coverage3 and candidate4gates/provenance/intervals/report: no new findings. Old failed and diagnostic runs excluded from acceptance | Start new full Headless55 then fresh whole Main97+9+1288; candidate/global install only after all PASS | Previous exact approval, no new product choice | final3/readiness-diagnostics/results/execution; both build logs; prepared scripts re-reviewed writable fallback |
| EXEC: unchanged-source observation deadline / preserved complete gates | Source13 Headless55, MainWindow97 and Repeater9 Completed/native0/no skips confirmed by reviewer. In1288 complement Synthetic3000Task failed at observation60sec deadline; high CPU load observed, causality unproven | Only own verified PID956084 was stopped after concrete failure, real native-1/interrupted record/logSHA preserved. Failed partial excluded. Source/deadline/assertions unchanged; pre-stop open-log hash denied sharing, final closed log hashed after exit. Reviewer approved complete four-part coverage97+9+17+1271 with retained complete3gates on identical source/binary and fresh whole Night17 + remaining140classes1271 | Concrete resume/candidate5gates review; execute fresh17 and1271; AC8/10 pending, no package/install yet | Previous approval; structural whole-class isolation and reuse of successful immutable evidence, no narrowed test retry or waiver | final3/native execution/interrupted.log; final4/source manifest/reviewed source/resume-full-gates.ps1/candidate-stage.ps1 |
| EXEC: session continuation 08.10 / final remaining full partition | Complete NightSnapshots17/17 native0 retained on source13 60a9af2e with matching DLL23069FDA; previous1271 has no finalTRX/execution and no own Main process, cause of interruption not established. Global tool still olddce | Incomplete result excluded and recorded with null nativeexit, closed log SHA and processCount0. Source13 unchanged; retained complete55/97/9/17 revalidated, deadline unchanged. Only full remaining1271 reruns fresh, exact1394 coverage and candidate5gates remain | Review updated retention guard, then execute1271 → candidate → global freshshell install; AC8/10 pending | Existing spec/install authorization persists; no publication or live task writes | final4/interrupted-20261008 record; final5/source-manifest/resume/candidate/report |
| EXEC: complete final-source full gates | Final5 complement Completed1271/1271/native0/no skips in21m50s; exact four-part coverage97+9+17+1271=1394 and Headless55 independently checked by fallback |13 source hashes/DLL/TRX/coverage match; features26/86/13/1/4/4 and status34 in actual primary TRX. New rendered CLI42 PNG viewed by root and reviewer; prior failed/incomplete evidence retained outside acceptance | Pack and smoke then authorized global update | Existing exact approval | final5/validated-gates.json/main-coverage.json/feature-test-evidence.json/ui-inspection.json |
| EXEC: actual candidate and installed delivery | Candidate package built, isolated importance37+baseline24 PASS. Global old-path/version/DLL/rollback guards passed; update to1.32.1-local.20261007.importance.60a9af2e succeeded; actual fresh shell37+24 PASS | Installed DLL7967237D matches candidate, package9D319487 pinned to13source60a9; actual path/version/hash and no live writes/public Git actions recorded. Root expanded post-EXEC PASS; final separate delivery checkpoint pending | Reviewer actual installation/report/expanded block, then close final stop decision | Existing local install authorization; no repeated approval | final5/candidate-manifest.json/install-manifest.json/installation.md/fresh-shell-smoke.log |

| EXEC: final post-EXEC stop | Separate writable adversarial fallback final delivery PASS, no open findings; all AC1–10 closed. Root/fallback checked real version/path/hash, installed37+24, full1394+55 and PNG42 | Reviewed SPEC C879FF24; subsequent edits only record final checkpoint. Report quote LOW was serialization false positive (zero backslashes), closed without changing valid text. Source13/code/package unchanged; no live/PR/merge/CI/native claims | Authorized importance implementation and local installation complete | No further decision required | final5/post-exec-review.json/executed-spec.md/installation.md |
| DELIVERY 09.10.2026: user-authorized rebase/merge | Получено прямое «Отребейзь на мейн и влей»: разрешены commit/rebase/push/PR/merge для importance scope. Main e987023d включает #319 emoji UI affinity и #320 deep links | Прежняя установка и source13 validation остаются историческими; rebased source требует новой сборки/full validation. Local installed CLI пока не заменяется. Preserve both Headless hook changes and all original importance contracts | Commit own changes, rebase, new tests/review/PR/CI then merge | Direct current user request; no extra approval | This SPEC + delivery artifacts outside repo |
