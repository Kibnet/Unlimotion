# Android: ограничить параллельное чтение файлов при начальной загрузке

## 0. Метаданные

Expanded SPEC по central canonical template. Профили: delivery-task, QUEST, performance-optimization, dotnet-desktop-client, testing-dotnet, review-loops; локальный UI MUST. Владелец Codex; medium. Task1 e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6, тот же worktree android-task-loading и branch perf/android-e74c5d1a-task-loading. Baseline HEAD2afd4ab29632152789102c132244323d738933ad, product source43d95e1b. Релиз/установка оригинального приложения не входят. Effective model/effort скрыт runtime; модельные eval неприменимы к файловому коду.

**Статус: CLOSED / NO-RETAIN.** Эксперимент завершён, кандидат откатан без коммита. Родительская Task1 остаётся InProgress/Active.

Пользователь разрешил последовательный scope, CLI status/results, review/fixes/autoapproval SPEC, commit/push/draftPR/merge после принятия задачи. Здесь новая подзадача внутри Task1, а не начало Task2/3. Parent cold≤5s/resume≤1s/freshness≤5s остаётся отдельным незакрытым контрактом.

## 1. Overview / Outcome

Уменьшить ожидание множества небольших файлов на Android, сохранив упорядоченную миграцию и публикацию задач. В двух существующих проходах разрешить не более4 ограниченных предварительных чтений; большой файл читается существующим последовательным способом. Принять изменение только при сохранении контрактов и измеримом выигрыше обычного запуска приложения.

Output: небольшой product diff, meaningful storage/migration/UI tests, source-bound baseline/candidate native evidence, retained или reverted decision, CLI result/readback. Stop: correctness/UI/data/lock/drain failure; неподтверждённый или незначимый выигрыш → NO-RETAIN без ослабления порогов/подбора N. Принятие этой оптимизации не завершает Task1 автоматически.

## 2. AS-IS

UnifiedTaskStorage.BuildInitialTaskViewsAsync выполняет status prepass на Task.Run. MigrateTaskStatusModel последовательно File.ReadAllBytes/HasModernTaskStatusJson; legacy/nonstandard JSON остаётся за JObject, backup, WriteOwnedMigrationFileAsync/report. Общей directory lock на весь status prepass сейчас нет; добавление такой блокировки изменило бы доступность CLI и не входит.

FileTaskStorage.ReadDirectoryCoreAsync последовательно Task.Run(DeserializeTaskSnapshot): FileStream ShareReadWrite|Delete → MemoryStream/ToArray → локальный Newtonsoft serializer/repair/hash → ordered Id validation/model clones/cache/source mapping. Directory/graph entrypoints сохраняют существующую cooperative lock и reentrancy; live graph/source hashes/pending raw watcher/invalidation зависят от этого пути. EnumerateTaskFiles virtual; duplicate canonical file выбирается последним в исходном порядке, поэтому completion order не может менять publication order.

Diagnostic33: initialgraph14905.15ms, before-window8091.14ms (не pureIO), filewindow6813.94ms/bytes-read4531.65ms. Закрытая SPEC bounded-file-read-feasibility: minimal native36, same3094 bytes,32trials,7pairs, N4 median C/B prepass.347171/snapshot.362652/combined.370048/allocation1.140264, wins7/7 each. All task hashes before/after, PID/QEMU exit и source bindings подтверждены; это phase-only feasibility, не product gain. Native35 preflight failure сохранён отдельно, timings отсутствуют.

Baseline validation2afd: Main1572+Headless66, AndroidPkg/CodeQL PASS; local targeted51+Headless66 PASS. Product не менялся после этого validation. Тесты повторять до изменения только при обнаруженном drift; после изменения обязателен targeted→full.

## 3. Проблема

Последовательное ожидание небольших независимых файлов удлиняет начальную загрузку. Предварительное чтение должно оставаться ограниченным по количеству и дополнительной памяти, не переставлять записи, не распараллеливать миграционные записи/парсинг/публикацию.

## 4. Цели дизайна /5. Non-Goals

Один внутренний reader с максимум4 слотами, исходный порядок, обязательный drain, ограниченный read-ahead. Android default4; остальные платформы default1 с прежним последовательным чтением. Protected virtual concurrency seam позволяет принудительно проверить4 в desktop/Headless без глобального config/env override. Не добавлять пользовательскую настройку или CLI protocol.

Два независимых прохода сохраняются. Не возвращать rejected shared-byte/single-read/whole-directory-worker/JSON-parser/reactive candidates. Не менять watcher/graph/schema/unknown fields, миграционные правила/backup/report, Load/forced reload/Save, lock path/timeout, сортировку/duplicate policy, Git/Avalonia, поиск или layout. Не переносить task files и не создавать persisted cache.

## 6. TO-BE

### 6.1 Ответственности

- Новый internal reader в Unlimotion.FileStorage: ordered bounded byte prefetch, stream lifecycle, фиксированные временные буферы и drain; без моделей/JSON/cache.
- FileTaskStorage: защищённый выбор1/4 (default OperatingSystem.IsAndroid), typed parse/hash/cache в прежнем порядке и под прежним lock.
- UnifiedTaskStorage: использовать prefetched bytes только для modern precheck. Legacy/oversize/error идут через прежнее последовательное чтение и полный прежний migration body.
- Internal visibility из FileStorage к Unlimotion и Unlimotion.Test; public members/CLI data contract не меняются. Добавляются protected virtual seams для выбора concurrency и открытия потока, используемые детерминированными тестами.

### 6.2 Детальный алгоритм

Reader получает исходный IEnumerable paths и FileShare, concurrency1/4. Для N4 выделяет один раз4 собственных byte[65537], по одному на активный слот; никаких общих статических/ArrayPool buffers. Читает поток до EOF или65537 байт без отдельного FileInfo/Length preflight. При <=65536 копирует ровно прочитанные bytes в result; при превышении возвращает NeedsSequential без полного чтения/большой аллокации. Рост файла во время чтения не обходит фактический cap read-loop. При finally буфер очищается, stream закрывается. Prefetch ShareRead для prepass; ShareReadWrite|Delete для snapshot. Small-file byte reader меняет внутренний byte API, поэтому phase-only probe не считается доказательством его product performance.

Не более4 начатых reads на enumeration; очередь в исходном порядке. Следующий слот допускается после потребления текущего результата; не более3 следующих результатов/reads во время последовательного fallback. Дополнительные fixed buffers262148bytes и консервативно максимум5 result arrays по65536bytes (включая предыдущий Current, временно удерживаемый async iterator/consumer) дают bounded read-ahead независимо от размера oversized fallback; stream/runtime overhead отдельно. Несколько независимых enumeration имеют собственные буферы/лимит, а не общий процессный лимит. Oversized файл читается один существующим original reader на consumer worker, остальные in-flight reads остаются маленькими/capped. Таким образом внутри enumeration не допускаются4 одновременных неограниченных больших буфера.

Compatibility policy enumeration: N4 FileTaskStorage разрешён только для конкретного materialized string[], как сейчас возвращает базовый EnumerateTaskFiles. Opaque/lazy overrides автоматически используют N1, даже если protected concurrency seam выбрал4: их следующий yield может зависеть от cache/consumer эффектов предыдущего элемента. Override может сознательно вернуть заранее materialized string[]; новый публичный opt-in API не нужен. Private status prepass может явно разрешить read-ahead для известного EnumerateTaskFilePaths, который не зависит от consumer cache/yield hooks; порядок/filter не менять, новый atomic directory snapshot не обещать.

Чтение errors передаётся с ExceptionDispatchInfo и path, не превращается в пустой task. Все начатые tasks наблюдаются/drain при нормальном завершении, раннем выходе, исключении reader/consumer/parser/enumerator. Для explicit read-ahead source Enumerator failure сохраняет позицию: ранее полученные элементы обрабатываются до выброса на следующем MoveNext, pending работы дренируются; ошибка перечисления не попадает в per-file catch и не маскируется. Для opaque override N1 сохраняет и порядок MoveNext относительно consumer/cache effects.

N1 не выделяет4 fixed buffers и использует прежнее полное чтение/расписание. Сама модель/model clone/cache/duplicate/hash publication по одному исходному файлу в прежнем порядке; JSON parser выполняется на Task.Run, не переносится на UI thread. Для small snapshot разделить byte-read и DeserializeTaskSnapshot(content,path), сохранив существующий full-path wrapper для остальных callsites. SourceHash всегда от того же byte snapshot, который распарсен.

Modern status prepass: complete bounded bytes → exact HasModernTaskStatusJson; true увеличивает processed и идёт дальше. Любой false/NeedsSequential/prefetch error → original File.ReadAllBytes и весь исходный migration body; speculative legacy bytes не используются для записи. Catch остаётся ровно вокруг raw read/classifier/StreamReader/JObject, как сейчас. TryMigrateTaskStatusJson, создание/запись backup, WriteOwnedMigrationFileAsync и финальный report находятся вне этого catch и остаются fatal при ошибке. Caller получает successful migration flag только после успешного report; ошибка не публикует initial graph/успех. Все записи последовательны и сохраняют прежнее время миграции. Отсутствует новая atomic-snapshot гарантия для noncooperative writers; миграция ранее тоже не была общим locked snapshot. RefreshPendingFileChanges/graph initialization после prepass не меняются.

Visual planning: layout не меняется; эталон обычный root list/current card baseline2afd. UI evidence required: actual Headless post-initialization root/card and native loading/root/card PNG/navigation trace. Existing Headless launch host awaits Connect before creating the window, so it cannot demonstrate a rendered loading frame; that observation belongs to the native collector, which requires a current owned loading frame before the root. Video fallback: headless harness не пишет MP4, а timed native runs не записываются screenrecord из-за observer effect; фиксированные PNG+raw navigation traces и отдельная UI suite являются next-best evidence, не доказательство непрерывного отсутствия black frame.

### 6.3 User-Observable Scenarios

| Trigger | Видимый результат | Evidence | AC |
| --- | --- | --- | --- |
| Android cold launch, copied Git-backed3094 data | тот же root/list/current card и тексты/status/relations, меньше ожидание | paired ordinary native+PNG; tests | A1/A3 |
| mixed legacy/large/corrupt/duplicate files | прежние migration/diagnostics/canonical choice, UI не зависает из-за parser onUI | storage/migration+Headless | A1/A2 |
| CLI write/refresh после начальной загрузки | обновлённая задача появляется по прежнему watcher contract | lock/reentrancy/CLI live-refresh UI | A2/A3 |
| disposal async iterator/exception во время prefetch | все начатые readers завершены до выхода из iterator, новых reads после выхода нет | deterministic iterator drain tests | A2 |

### 6.4 State matrix

| State | Event | Result |
| --- | --- | --- |
| N1/nonAndroid | enumerate/read | последовательный оригинальный путь |
| N4 filling | <=4 admitted | отдельный slot на reader, ordered queue |
| consume small | bytes ready | serial parse/modern check; затем следующий admission |
| consume oversized/legacy | fallback | original single full read; bounded remaining readers |
| expected file error | consumer catches original types | прежний load error/skip |
| unexpected/enumerator/consumer failure | abort | preserve primary error, drain all started tasks |
| late external change | existing watcher/generation mechanism | прежняя invalidation/freshness semantics |

### 6.5 Decision ledger

| Decision | Owner/default | Confidence | Risk | Needs user |
| --- | --- | ---: | --- | --- |
| Android N4, others N1 | agent, measured phase evidence |.9| product effect ещё неизвестен | Нет |
| cap64KiB +4 reusable slots | agent, dataset max21992; large fallback |.85| новая read-loop implementation | Нет |
| serial legacy re-read/typed parse/cache | agent |.95| дополнительные calls для legacy | Нет |
| no whole-prepass lock | agent, preserve CLI availability |.9| сохраняется non-atomic external race | Нет |
| fixed product gates before retention | agent |.9| возможен NO-RETAIN даже при phase win | Нет |
| autoapproval after review/fixes | user |1| нет | Нет |

### 6.6 Runtime/config/data

| Contract | Source | Change / verification |
| --- | --- | --- |
| Task bytes/model/unknown fields | existing Newtonsoft/source tests | unchanged; full typed/hash parity |
| Directory lock/reentrancy | FileTaskStorage | same entrypoint scope; writer blocking and nested tests |
| Raw watcher/live graph | existing generation/pending queue | no policy change; live-refresh tests |
| Default concurrency | runtime Android check/protected seam |4 Android/1 others; no global config |
| New buffers | per enumeration | fixed slots/cap/drain/clear controls |
| Native | same owned API34/x64/3CPU/2560MiB/SDK401/AOT | exact package/source→PE→PID/raw/cleanup proof |

## 7. Бизнес-правила /8. Интеграции /9. Данные

Id validation, duplicate last-in-enumeration, defensive snapshots and source-file mapping остаются authoritative. Не менять filesystem enumeration/filter/order. Migration reports/backups/JSON authoritative parsing неизменны. Никаких новых persisted fields/versions, миграций пользовательских данных или публичного API.

## 10. Rollout / rollback

Только рабочая ветка Task1. До performance acceptance новый код считается candidate; отдельный Conventional Commit для retained optimization, rollback при NO-RETAIN с сохранением independent regression tests, если они полезны исходному контракту. DraftPR325 обновляется по фактическому diff/validation; точный HEAD CI перед готовностью. Original app/phone/release не устанавливать и не публиковать.

## 11. Acceptance / testing

| AC | Automated | Native/manual/evidence |
| --- | --- | --- |
| A1 same data/order/migration | byte/hash/typed/unknown/BOM/repair/legacy backup-report/duplicate parity N1/N4; zero/one/4/5/many | copied3094 before/after each native cohort |
| A2 bounded scheduling/memory/lifecycle | gated actual max4/noN5; distinct buffers; cap/growing/truncated/huge; first/middle/final IO/unauthorised/unexpected; enumeration/consumer failure; lazy override second-yield depends on first cache effect→N1; drain and no cross-trial work; lock/reentrant/read-only hash; forcedN4 fatal migration cases; existing watcher suites | source/actual package proof; no pending app/QEMU |
| A3 user-visible and product gain | add/update relevant AppAutomation Headless forcedN4 startup/root/currentcard and postinit external edit; full existing Headless; targeted→full Main | fixed baseline/candidate profile and ordinary cold/root/card, actual PNG/trace |
| A4 evidence/delivery | exact source/PE/ABI/runtime and raw offline recomputation; CI | review/fixes, retained/reverted, CLI result/readback Active; Task1 only when parent5/1/5 separately met |

Tests before implementation: accepted exact2afd baseline above; changed source triggers targeted then full Main+Headless. Tests use real small/large/growing files for byte fidelity and deterministic stream/gates for admission/error/drain, not sleeps. Existing FileTaskStorageReadContractTests allocation/lock/source-isolation and all migration regressions mandatory. UI actual forcedN4 path via protected test subclass, no Android-only branch left untested on host.

Exact local test commands (cwd C:/Users/Kibnet/.codex/worktrees/android-task-loading/Unlimotion, pinned executable C:/Program Files/dotnet/dotnet.exe; repository SDK10.0.402 for host tests, native SDK401 separately):

    & 'C:/Program Files/dotnet/dotnet.exe' build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --disable-build-servers -p:UseSharedCompilation=false
    & 'C:/Program Files/dotnet/dotnet.exe' build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --disable-build-servers -p:UseSharedCompilation=false
    & 'C:/Program Files/dotnet/dotnet.exe' test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter '/*/*/BoundedTaskFileReaderTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx --results-directory=D:/CodexArtifacts/android-task-loading-20261012/bounded-task-file-validation1/targeted-reader

Same exact test command with filters /*/*/FileTaskStorageReadContractTests/*, /*/*/TaskStatusMigrationTests/*, /*/*/UnifiedTaskStorageMigrationRegressionTests/*, output directories targeted-storage/targeted-migration/targeted-regression respectively. ForcedN4 UI new class filter /*/*/BoundedTaskLoadingHeadlessTests/* in Headless project, output targeted-ui. Class names fixed for new tests, zero discovery is failure. Full runs use existing repository harness:

    & 'C:/Program Files/PowerShell/7/pwsh.exe' -NoProfile -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project main -ResultsRoot D:/CodexArtifacts/android-task-loading-20261012/bounded-task-file-validation1/full -DotnetCommand 'C:/Program Files/dotnet/dotnet.exe'
    & 'C:/Program Files/PowerShell/7/pwsh.exe' -NoProfile -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project headless -ResultsRoot D:/CodexArtifacts/android-task-loading-20261012/bounded-task-file-validation1/full -DotnetCommand 'C:/Program Files/dotnet/dotnet.exe'

Bounded private runner DROOT/run-bounded-task-file-validation1.py will preserve exact commands/source SHA/stdout/stderr/TRX; build10min, each targeted5min, full Main20min/Headless10min, own child-tree cleanup mandatory. Fresh output root per real corrected rerun, no overwrites. Exact native build/cohort commands and runtime identities remain a separate prospective helper/admission gate, not authorised by these host commands.

ForcedN4 migration failures mandatory: actual backup failure preserves original fatal error and prevents initial graph/success flag; actual owned migration write failure while later capped reads are held additionally proves completion waits for drain and active0. Backup failure with held later reads is covered compositionally, not by a direct combined integration scenario: forcedN4 backup-fatal regression + deterministic shared iterator consumer-failure/held-read drain + actual owned-write failure/held-read integration. Both fatal operations unwind the same await-foreach iterator outside the raw/classifier catch. No direct backup-held test PASS is claimed. Report failure occurs after enumeration, so assert all readers already inactive before report plus original fatal error/no graph/success flag. Existing Windows-default N1 BackupOrReportFailure coverage alone does not exercise this path.

Lifecycle scope: disposal means disposal of the bounded async iterator, including exception unwinding and early break. This change does not introduce cancellation of UnifiedTaskStorage.Init by UnifiedTaskStorage.Dispose or a new guarantee preventing publication after concurrent Unified.Dispose; those broader lifecycle contracts are unchanged and are not claimed by A2.

Product perf prospective protocol (exact executable commands/proofs require later helper gate):

1. Frozen baseline2afd full source snapshot before edits. Same source-built private package/SDK/AOT/ABIs/settings/data/AVD configuration for B/C; a prior APK may be reused only if all actual product source/payload/config bindings match this baseline, not merely its filename.
2. Instrumented full-app cohort: one excluded full warmup each variant, then3 pairs B,C / C,B / B,C. Minimal endpoint observers for status-prepass and initial directory window + init allocation; no per-file events. All task hashes/outputs/model counts exact. Gates: median paired sum-of-two-phase wall C/B≤.65, each phase median≤.75 and C faster≥2/3; init allocation median paired C/B≤1.25, available process counters mandatory. This remains instrumented evidence.
3. Ordinary full-app cohort: same warmup+3pair order, identical uninstrumented build/config and existing conservative root-ready bracketing method. Median of paired conservative C-upper/B-lower≤.85, max≤1.05; root/currentcard identity+same data and usable action required. No graph-time estimate replaces ordinary result. No profile-vs-plain comparison.
4. Fixed counts/no extension/replacement/N tuning/cache flush/forcedGC. Gate failure→NO-RETAIN; valid ambiguous/noisy result→INCONCLUSIVE/NO-RETAIN. Invalid helper: preserve actual artifacts, one prospective cause-specific correction after reproduction/review, not correction for a slow valid candidate. Per-command/per-stage/own-process budgets and all exact helper hashes must be reviewed before native admission; no host builds/tests/media during timed runs.

These gates accept only one product optimization. Parent cold≤5s/resume≤1s/freshness≤5s still require separate full scenario evidence before completing Task1/merge/next task.

## 12. Risks / expected objections

| Objection/risk | Mitigation |
| --- | --- |
| Ещё один microbenchmark вместо приложения | mandatory ordinary full-app gate before retention |
| Параллельные большие файлы увеличат memory | cap4fixed slots, bounded results; one original large fallback, growing-file controls |
| Миграция перезапишет устаревшие speculative bytes | legacy всегда original serial re-read; no writes from prefetched legacy content |
| Completion order меняет duplicates/cache | consume original enumeration order, exact parity tests |
| UI parse станет синхронным | typed JSON Task.Run preserved, real Headless forcedN4 coverage |
| Windows/CLI ухудшатся | defaultN1, full lock/read/allocation/live-refresh regressions |
| Новая оптимизация завершит Task1 без5/1/5 | acceptance explicitly separate; keep Active |

Rework checklist: visible scenarios/AC/evidence and decisions explicit; relevant roles in review; no human choice unresolved. Manual challenge: source byte-read API differs from minimal probe and can erase its advantage; only new product measurements decide retention.

## 13. План /14. Открытые вопросы

Review/fix/autoapprove→small implementation+tests→targeted/full/UI→source-bound native helper preparation/admission→fixed product cohorts→postEXEC/retain-or-revert/CLI→continue Task1 parent goals. Нерешённых пользовательских решений нет; выбранный cap/API/perf effects проверяются, не предполагаются фактами.

## 15. Profile /16. Files /17. Before→after

Performance: measured hypothesis/baseline, latency+allocation, full tests, independent retention/commit. Dotnet/UI: shared storage contracts, actual UI suite/rendered evidence, no new dependencies.

| File/area | Change |
| --- | --- |
| FileStorage new internal bounded reader + assembly visibility | reusable capped ordered prefetch/drain |
| FileTaskStorage | Android-default concurrency seam; snapshot byte overload; ordered integration |
| UnifiedTaskStorage | modern precheck read-ahead; original legacy body retained |
| Main tests / Headless tests | parity/bounds/errors/locks/migration/UI |
| specs / private DROOT evidence | prospective gates, actual outcomes/commands/source artifacts |

Было N1 full-file wait перед каждым consumer; стало Android N4 capped read-ahead with ordered consumer/serial fallback. Other platforms remain N1. Persisted schema и user flow unchanged.

## 18. Альтернативы

N4 целых DeserializeTaskSnapshot добавляет parallel JSON/model/cache and arbitrary large buffers — хуже для изоляции и памяти. Unbounded Task.WhenAll по каталогу отвергнут. Shared full-directory cache/single-read/one-worker прежних кандидатов rejected и не возвращаются. Сохранить N1 — baseline/rollback, если product gates не пройдут.

## 19. Quality/review /20. Журнал

Separate planning advisory PASS after two MEDIUM fixed: lazy override compatibility, fatal migration/write/report semantics and forcedN4 tests. Read-only conduct in writable runtime, no technical isolation. Original linter19PASS/16PARTIAL and rubric27/30 reflected missing exact commands; above host commands/outputs/budgets now concretized before implementation. Native exact commands remain intentionally separate staged gate before native execution, not a claim of readiness for that stage.

Author fallback agrees. Scope/Evidence: canonical template, central performance/QUEST/review/UI/TUnit profiles, FileTaskStorage ReadDirectory/Deserialize/locks, UnifiedTaskStorage prepass/caller/enumerator, AfterEnumeratedFileStorage existing hook, migration failure tests, AppAutomation storage factory seam, accepted2afd CI and closed36 raw/evidence. Contract: bounded bytes-only N4 Android/materialized, N1 lazy/nonAndroid, ordered parse/cache, original serial legacy writes/fatal report. Adversarial: previous Current retention counted; growing-file cap without Length races; lazy yield/cache hooks preserved; no stale legacy write; no wholeprepass lock; no whole-app gain inferred from phase probe. Developer/Tester/Ops/UX/Domain PASS within future validation obligations. All ReadDirectoryCoreAsync callers, including write-lock rebuild, must be re-inspected at implementation review.

Planning linter20 PASS for implementation/test preparation: outcome/ASIS/problem/goals/bounds; responsibilities/integration/order/error/drain/performance; capped data and backward-compatible fallback/rollback; A1-A4/tests/commands+stops; dependencies/decisions/Expanded/profiles. Author rubric30/30 for this staged readiness: goals5, ASIS5, design5, safety5, testability5 after exact host commands, autonomy5. Native helper readiness not evaluated. Manual challenge: new capped API may lose probe speed, ordinary full-app cohort must decide retention; no human-owned unresolved choice.

User instruction autoapproves reviewed/fixed SPEC: **EXEC implementation and host tests authorised**. Native build/cohort execution still gated by concrete helper/source/proof review. Product source unchanged at this transition. Post-EXEC не выполнен.

Implementation checkpoint 19 Oct: product candidate remains uncommitted. Separate source advisory PASS; targeted re-review PASS after making backup-held coverage explicitly compositional and narrowing disposal to iterator drain. Additive protected seams and fallback comment clarified. No broader Unified.Dispose cancellation guarantee claimed.

Host evidence DROOT/bounded-task-file-validation4: Main/UI build PASS; reader15/15, storage7/7 (including IO/Unauthorized load errors vs original unexpected fatal exception), rendered UI1/1 PASS, zero skipped. Earlier targeted read-contract12/12, migration38/38, bounded migration2/2, regression8/8 PASS. Current full Main and full Headless still running at this checkpoint. Accepted baseline CI1572+66 does not validate the dirty candidate.

Root viewed both actual startup-card.png/external-edit-card.png under targeted-ui/trace/bounded-task-loading/95b0f40be1e34dcdb1281d60e5ab9c09. List and editor title agree before/after actual external write, current selection/details rendered; UI test exercised close/reopen and persisted readback. DROOT/bounded-task-file-validation4/ui-visual-review1.json records image SHA and scope. This is rendered host post-initialization evidence, not Android loading-state/latency evidence.

Preserved failures: reader validation1 fake MemoryStream constructor invoked virtual Length; validation2 opaque IEnumerable collection expression correctly chose N1. Corrected test fixtures, product unchanged for those fixes. UI validation3 failed before screenshot because window was not shown and its child flag was absent from rendered session hook; existing HeadlessWindowPresentation and explicit rendered-child registration corrected validation4. Failed raw/stages remain separate.

Native preparation only: immutable inputs3 has baseline406/candidate408 product files and reversible six-endpoint-only Core overlays. First preparation stopped before writing a source file because git archive exported CRLF while prior SHA-verified Git blobs were LF; normalized archive comparison now accompanies canonical byte snapshot. Helper host1 VALID trace/PE guard passed but second build included prior generated sources via implicit glob; fresh host2 uses explicit sources. Host2 five controls PASS (actual trace counters/completion, valid profile accepted, missing mark rejected, wrong label rejected, plain accepted). Trace2 uses explicit Utf8JsonWriter for trimming/AOT. These are helper controls, not native performance results. Build helpers require prospective review and full host results before execution; cohort helper/admission remains separate.

Host full-run amendment before fresh validation5: validation4 full Headless67/67 PASS, zero skipped/failed/errors, source unchanged. Full Main4 hit its declared20min host deadline at1283 finished lifecycle records; cleanup treeKillExit0/direct child exited/writeErrors0/source unchanged. No final TRX and no full Main PASS. Its captured stdout also contains ServerStorageCrudRealtimeScenario_ExecutesFeatureSteps failing with WebServiceException/Failed to execute bulk insert (71.946s), so this is not described as a timeout-only run. Same unchanged binaries isolated targeted-server validation5:1/1 PASS, zero skipped; no cause or permanent repair is inferred from that retry.

Prospective correction is one fresh FULL Main run with the same1597 expected cases and no exclusions, unchanged code/binaries, no parallel builds/tests/media, budget35min. Evidence root DROOT/bounded-task-file-validation5/full/main, runner run-bounded-task-file-validation5.py full-main. Budget rationale: baseline exact2afd CI Main17m25.8s/Headless3m55.9s; actual local Headless6m26.0s gives a rough hardware/load scaling estimate28.5min Main, bounded35min includes margin. This is a test-runner budget correction, not a performance-trial extension or change to retention gates. Existing validation4 build/targeted/fullUI evidence remains distinct and source-verified. Full Main must still finish with1597/1597/zero skipped; isolated1/1 does not replace it. If another actual test failure appears, diagnose it rather than retry until green. Native builds remain blocked until this full gate passes.

Separate advisory amendment review PASS before fresh full-main5 execution; author agrees and user autoapproval applies. Read-only advisory conduct in writable runtime, no technical isolation. No product/source/binary changes, exclusions or performance gate changes introduced. Full-main5 started only after that review and after other host workloads ended. CLI progress2 readback confirmed Task1 InProgress/Active and preserved user fields/criteria/relations.

Prospective native helper stage (not yet admitted/executed): fresh Bplain/Bprofile/Cprofile/Cplain SDK401/AOT2ABI builds; old Bplain reuse abandoned because a new build permits equally strong actual AOT provenance. Each finalizer verifies state variant/path/SHA, makes an early immutable APK, runs aapt/ZIP against it, checks10 actual managed PEs and12 retained Git/native binaries,16 AOT images against generated counterparts, snapshots PEs/AOT and rechecks whole APK SHA. Build-only separate advisory PASS after APK-binding/transitive-runner findings fixed; actual admission still requires completed full Main5.

Draft full-app cohorts retain exact warmup2+measured6 per mode and all numeric thresholds. Profile uses own session37; ordinary uses independent own session38 after qualified profile gate PASS, with identical AVD config. No screenrecord/MP4 is acquired in timed runs. Source review required fresh full CIM+AVD checks after long operations before mutations, now present; manual warmup and measured visual receipts are separate. Offline analyzer must bind six measured metadata/raw fixture SHA/windows/PNG, source controls and actual admission, recompute current-draw qualification/root intervals and require unique fresh PIDs/order/labels/chronology; it cannot issue a qualified gate from timing summaries alone. Helper controls and actual source-bound package proof are still pending at this checkpoint.

Draft absolute native session budget amendment before any cohort execution:150min maximum per own session, unchanged3CPU/2560MiB/hostGPU/API34. Reason is fixed worst-case scheduling of8 declared trials×(preflight300s+capture600s)=120min, two manual review waits≤5min each, bounded launch/boot/runtime/audit/analyze and exact shutdown. Both independent controller/watchdog share one absolute deadline; every stage/review is capped to its remainder. Two-minute cleanup reserve, refusal to begin another trial unless its full900s budget+reserve fits, fresh binding check after preflight, and exact shutdown≤90s. This replaces draft45min independent stage limits, whose sum could not fit, before any real data exists. It does not add trials, relax latency/gain gates or promise that slow/incomplete trials will be replaced. Source/admission review and controls must verify the changed deadlines before launch.

19 Oct actual full host gate: validation5/full/main finished13:26:21UTC with1597/1597 PASS/all other TRX counters0, source unchanged. The earlier failed/incomplete run remains preserved; this single prospective full run took30min00s within its35min bound. Full Headless remains validation4 67/67 PASS. BUILD ONLY admission1 subsequently verified871 exact inputs and admitted four fresh APK builds; the sequential driver started Bplain first. No native cohort or product retention claim at this point. CLI progress3 exact readback confirms Task1 InProgress/Active and unchanged user fields/criteria/relations.

Offline helper controls executed after the solo full Main ended: trace19/19, session-budget10/10, raw evidence24/24 PASS. Evidence controls include synthetic full loading/root/card flow, fixture missing/changed/extra/duplicate/path cases, raw window PID tampering, raw PNG tampering, root interval mismatch, missing loading, stale PID, chronology and stability/spacing errors. Synthetic PNGs are explicitly not app evidence. Eight prospective controller/start/watch/orchestration PowerShell sources parsed without errors. Separate source/control advisory PASS with SHA-verified receipts; read-only conduct in writable runtime, no technical isolation. Author fallback agrees for this stage; actual source-bound native package and admission remain required.

Final native admission/visual-writer source review: two MEDIUM fixed before execution. Plain-stage exact shutdown now binds expected/before QEMU to the prior profile runtime, whose SHA is in qualified profile evidence. Warmup visual review now requires singleton Core trace/raw SHA/complete flag and recomputed six-event validation; plain explicitly requires no traces and observer-absence proof. Targeted re-review PASS, no new findings. Visual writer first produces a raw-bound frame plan without PASS; root must inspect each actual loading/root/card image and explicitly record concrete observations against an unchanged plan. No human-owned choice unresolved; autoapproval applies to this source-ready stage, not to future measurement results. Actual Bplain/Bprofile build finalization subsequently passed10 managed PE/12 native/16 AOT checks each; candidate builds pending.

All four fresh APK builds completed with unchanged frozen inputs and the declared10 PE/12 native/16 AOT proofs each. Actual admission37 bound1035 inputs. Attempt37 then failed BEFORE runtime qualification/app launch/preflight/warmup: ConvertFrom-Json materialized an ISO deadline as DateTime, and DateTime.Parse converted it through culture-dependent text (`10/19/2026 16:17:36`), rejected under ru-RU. Its watchdog failed on the same conversion; the separate original launcher controller still retained its150min bound. Orchestration37/raw stderr/old sources/admission are preserved, no valid trial or performance result exists.

Cleanup: exact metadata/ready/CIM/engine/parent/port/AVD identity proved QEMU170480; own adb emu kill returned OK. The cleanup helper then failed parsing empty JSON after process disappearance, so that first receipt correctly retains exitProven=false. Separate fail-closed read-only zero-CIM audit (`bounded-loading-failed37-exit-audit1.json`) now proves no matching own QEMU remains and binds the original expected identity/receipt SHA. No qualified-runtime claim is inferred from this fallback.

Sole prospective cause-specific helper correction: new profile39/plain40 helpers and fresh r2 trial/driver/review/gate namespaces, unchanged four APKs, actual hardware, metrics/thresholds, fixed2+6 trials and150min absolute bound. Deadline conversion uses a typed DateTime cast without implicit culture-dependent Parse. Actual date-controls2: ru-RU/en-US × JSON DateTime/ISO string4 controls PASS, eight corrected PS sources parsed; receipt source SHA unchanged. A preparatory overly broad numeric rename was caught before admission/execution and preserved under correction2-preparation-incomplete; token/prefix substitutions now preserve the exact baseline HEAD and engine37.2.12, and actual collector diff changes only attempt ids/r2 namespace. Corrected native admission was subsequently approved by targeted source review; no valid trial was replaced or added.

## 21. Фактический результат и Post-EXEC

Fresh admission39 проверил1039 исходных bindings. Эмулятор QEMU172652: API34/x64,3CPU/2560MiB/hostGPU/1080×2400. Пройден ровно фиксированный протокол: исключённые p0-b/p0-c и измеряемые p1-b/p1-c/p2-c/p2-b/p3-b/p3-c. Восемь разных свежих app PID, восемь полных Core traces по6 endpoints, шестнадцать inventories по3094 неизменным SHA. Все шесть orchestration stages завершились exit0 с подтверждённым child exit; точное expected/before QEMU172652 после shutdown отсутствует. Runtime/error/fixture/observer/data gates PASS.

Root просмотрел все24 оригинальных PNG: loading/root/card для двух разминок и шести измеряемых запусков. Во всех сериях одинаковый All Tasks и карточка Unlimotion a5ef0164-8def-417b-9b7d-d235516dd8c7, Wanted=0, связи/дочерние строки и Containing tasks=24. В просмотренных состояниях нет пустого/чужого окна или нового различия расположения B/C. Это выбранные кадры, не доказательство непрерывного отсутствия black surface и не phone validation. Warmup/measured review2 receipts связывают raw windows/PNG/fixture/metadata/runtime/build/control SHA.

| Метрика, медиана парных C/B | Фактически | Порог | Итог |
| --- | ---: | ---: | --- |
| Status phase |0.6301370733|≤0.75, wins≥2/3|PASS, wins2/3|
| Directory phase |0.7015263942|≤0.75, wins≥2/3|PASS, wins3/3|
| Combined read phases |**0.6755133205**|**≤0.65**|**FAIL**|
| Init process allocation |0.9775127129|≤1.25|PASS|

Combined paired ratios:0.8949063211/0.6755133205/0.6234612376. Медианный выигрыш по сумме фаз32.4487% меньше требуемых35%. Первый status ratio1.1881 хуже baseline, остальные две пары быстрее; это не универсальное ускорение. Профиль включает concurrent work/observer overhead, не pureIO. Ordinary40/admission40/ordinary gate не запускались по принятому stop rule. Доказательства запуска полного приложения с профилированием не заменяют отдельный ordinary gate. Ни один порог/число попыток после результатов не менялся.

Post-EXEC review PASS **для disposition NO-RETAIN**, не для performance acceptance или родительской Task1. Scope/Evidence: SPEC/A1-A4, фактический source diff/storage/migration/UI contracts, targeted/full1597+67 candidate evidence, четыре APK/10PE/12native/16AOT proofs, admission39, raw2+6 traces,16 inventories,24 PNG/root receipts, author audit, runtime/orchestration/exact exit. Contract: bounded4/cap/order/serial fallback/migration/drain/parity/UI прошли тесты, но обязательный combined gate не выполнен; candidate не может оставаться. Adversarial: не путать qualified recomputation `passed=true` с `gate.passed=false`; не выбирать удачную p3; не расширять выборку; не переносить minimal36 result на product; не считать old baseline CI проверкой candidate.

Роли Developer/Architect и Tester подтвердили contracts и stop; UX сверил фактические состояния, не latency; Domain подтвердил сохранность задач и незавершённость5/1/5; Ops проверил отдельный package, immutable payloads, source/fixture bindings и exact shutdown. Separate advisory PASS без находок: reviewer пересчитал8 traces,16 inventories,1750 inputs, fixed order/PID/exit, лично просмотрел B1/C3 root/card, остальные кадры связаны с author review. Read-only conduct в writable runtime, технической read-only изоляции нет; отдельный author adversarial fallback выполнен. Нерешённых user-owned решений нет.

Rollback выполнен только после архивации12 собственных code/test files, patch, пяти baseline Git blobs и SPEC snapshot. Архив DROOT/bounded-loading-rejected-candidate1.zip SHA256 `0a2ad9260c175ef9987ab7a17d2a09fd4781ca0fc94213f086ec52c253c49bcc`, все entries reread/hash PASS. Пять tracked files восстановлены из HEAD, семь собственных untracked files удалены по точным проверенным путям; `git diff HEAD -- src tests` и untracked src/tests пусты. DROOT/bounded-loading-candidate-rollback1.json подтверждает product/tests equal2afd. Candidate1597+67 относится к архивированному кандидату; восстановленный код соответствует ранее принятому baseline1572+66 CI. Полный suite после точного восстановления неизменного baseline не повторялся. SPEC/evidence сохранены; parent Task1 Active, merge/release/Task2/Task3 не выполнены.

Артефакты: bounded-loading-native39-author-audit1.json; bounded-loading-profile-gate2.json; bounded-loading-profile-{warmup,measured}-review2.json; bounded-loading-orchestration39.json; bounded-loading-ownavd39-{runtime,exit}1.json; bounded-loading-rejected-candidate1.json; bounded-loading-candidate-rollback1.json. Старые admission/proof bindings на product paths описывают состояние ДО rollback; archive+pre-rollback audit сохраняют это evidence, текущие restored paths намеренно отличаются от candidate.
