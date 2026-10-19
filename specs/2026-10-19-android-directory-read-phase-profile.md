# Android: измерить состав initial graph

## 0. Метаданные /1. Outcome
Expanded по canonical C:/Users/Kibnet/.codex/agents/templates/specs/_template.md: multi-module diagnostic source/APK/native side effect. Task1 e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6, branch perf/android-e74c5d1a-task-loading, current product2afd4ab2/source43d95e1b. Owner Codex, surface Codex/local Windows, model ID/runtime hidden/unverified; model evaluation неприменима. Central stack как bootstrap SPEC19Oct: QUEST/review/linter/rubric/tool/performance/dotnet-desktop/task-context/result-tracking/local UI override. User разрешил эмулятор, последовательную работу, tracker CLI и autoapproval после review/fixes; original app/phone/release excluded.

Success: в одном диагностическом cold run разделить initial graph на очередь Task.Run, чтение bytes, создание serializer, JSON-read/repair и неразмеченный остаток. Выбрать следующую реализацию по измеренной основной доле. Это baseline diagnostic, не ускорение/paired comparison/parent5/1/5. Output private fixed-size file-trace summary +retained Core/VM/Android traces/native root PNG/taskSHA/process cleanup/audit, tracker interim result.

## 2. AS-IS /3. Проблема
Completed bootstrap diagnostic32: initialgraph12214.4733ms, processCPU13507.828ms,118104528allocated; models4699.3096ms/publication7388.3805ms. App base5387.1709ms, XAML261.2762ms; no performance claim. Native32full trace/PNG/root3094SHA/app+QEMUexit and advisory closure proved. Native31collector-shadowfailure remains closed INCOMPLETE, not counted. Windows modern reader75.8%coverage and unknown-preserving reader wall.73684>.60 both rejected, not integrated. Directory single-worker attempt15Oct profilePASS/plain conservative bounds failed, rejected; no unchanged repeat.

FileTaskStorage.ReadDirectoryCoreAsync sequentially awaits Task.Run(DeserializeTaskSnapshot(file)) per file; snapshot performs FileStream.CopyTo→MemoryStream.ToArray→local JsonSerializer(settings/converters/shared contract resolver)→original JsonRepairingReader→SHA256. Loop clones TaskItem and updates cache/file dictionaries. EnableLiveGraphAsync wraps directory read, ToGraphResult and PublishLiveGraph (CloneGraph/sourceHashes copy) under original lock. Existing12.214s span does not distinguish I/O, JSON and scheduling; no causal bottleneck conclusion from processCPU alone.

## 4. Design goals /5. Non-Goals
Reuse existing App/Core/VM/Android overlays and guarded collector2, same diagnostic package/SDK10.0.401/AOT/ABIs/fixture/API34engine. New observer aggregates integer ticks/counts only; no per-file JSON/processCPU sampling/path/text output. No storage/model/save/config/watch/lock/order/data migration/product behavior change. No serializer replacement/cache/lazy VM/public API change in product. No UI layout change, new UI test/video неприменим к diagnostic instrumentation; retained product UI/CI baseline unchanged, native root snapshots remain mandatory.

## 6. Предлагаемое решение /responsibilities
Private profile4, source truth frozen406 git-blob files with current clean src identity; CRLF checkout boundary preserved as bootstrap SPEC. FileTaskStorage.Profile.cs overlay +FileReadTrace.cs in Unlimotion.FileStorage only, public diagnostic helper namespace UnlimotionProfile so existing Core overlay can reference it (normal builds do not compile helper). Core initialgraph span adds BeginWindow immediately before original EnableLiveGraphAsync and EndWindow immediately after successful await, inside existing markers. No new Task/fallback/await in product flow beyond existing async observer write after window closes.

Helper window runs once per process; actual End enables summary only when call returns. No early flush guarantee: exception/hang before End may leave no file, report INCOMPLETE. Outer guest/host/watchdog bounded independently. FileTrace scopes exactly one initialgraph request, directoryCalls/returns recorded; any extra/missing directory/snapshot/admission phase makes full diagnostic fail. Counts limited4096, overflow/observerErrors reported, no per-task records or strings, expected3094 normal reads. No input filtering.

Per-file timing design:
- Original queue await expression becomes equivalent block lambda with one marker at worker entry and same return expression; caller stores one timestamp immediately before original Task.Run. Dedicated unique local, no existing identifier collision. Reversing exact substitutions restores frozen original bytes.
- Worker arrival sets thread-static queue timestamp/session; original synchronous DeserializeTaskSnapshot inserts BeginSnapshot at body entry, BytesReady after stream/buffer using block, SerializerReady after original serializer construction, JsonReady after original JsonRepairingReader returns. No exception handling/retry/serialization changes; original SHA return stays unchanged. Thread-static struct avoids per-file sample allocation; no async in snapshot.
- Aggregate queue residence = queue timestamp→worker entry; file-byte-read = snapshot entry→bytes ready (includes open/copy/dispose/ToArray); serializer = bytes ready→serializer ready; JSON = serializer ready→JsonReady (includes existing repair code). Worker entry→snapshot entry, SHA after JsonReady, await continuation, clones/cache updates/enumeration/graph publication remain honest unmarked remainder.
- Aggregate only active matching session and queued snapshot; unexpected/unmatched reads, duplicate/out-of-order hooks, wrong thread/session, negative durations or missing JsonReady invalidate completeness. No guessed attribution of missed calls. Current native flow has no external task writes; counts must match3094 and errors/duplicates0.
- DirectoryBegin/first-iteration/DirectoryEnd markers provide enumeration/loop interval; PublishLiveGraph outside directory is included in window remainder. All counts/ticks crosschecked against window. Sum disjoint components≤window wall; not CPU percentages. Queue/read/serializer/JSON components are sequential per file under original await, but host/runtime/observer work can overlap. Report inclusive timings, unknown hook cost; no numerical overhead subtraction.

EndWindow freezes snapshot and asynchronously writes startup-profile-file.jsonl in own diagnostic files folder, fixed bounded header/summary/footer≤64KiB, module=file/PID/frequency/windowticks/expected counts/errors/overflow/expiry/footer. Optional processCPU/allocated at two window endpoints only; process-wide overlap explicit. Writer failure/missing footer is INCOMPLETE. Actual observer writer completion tested; no force-flush or timer inserted into original storage.

### User-Observable Scenarios /state
| Trigger | Expected visible result/output | Evidence/AC |
| --- | --- | --- |
| One instrumented cold start | Existing Loading→same root; one aggregate summary splits storage window | A1-A4 |
| Read exception/hang/missing probe phase | Original tolerant/error behavior remains; diagnostic partial/INCOMPLETE, retained raw | A1/A2/A4 |
| End/timeout | Own app+QEMU exit and3094task bytes checked | A3/A4 |

State: planning review/fixes/autoapprove→frozen source overlays/host controls→native build/proof→guarded ownAVD33→preflight→one cold90s observation→cleanup→offline audit/visual+advisory closure→choose next candidate. At most one cause-specific helper correction may get fresh namespace34 after actual failure reproduction/fix/re-review; valid slow/inconclusive app run not repeated/extended. Preserve33 if failed, no best-case selection.

### Decision ledger
| Decision | Owner/chosen | Confidence | Risk / needs user |
| --- | --- | ---: | --- |
| Split measured12.214s before implementation | agent |.9|Extra diagnostic build; no |
| Aggregate StopWatch hooks, no per-task CPU/log | agent |.9|Unknown hook cost, explicitly inclusive; no |
| Same observers/fixture/engine, freshAPK profile4/AVD33 | agent |1|Diagnostic only, no comparative acceptance; no |
| One corrected helper attempt maximum | agent |1|If exhausted retain INCOMPLETE and select new causal direction; no |
| Autoapproval after review/fixes | user |1|No user-owned unresolved choice |

## 7. Data /8. Contracts /9. Errors
Reuse immutable private Fold6 copied3094manifest, Settings with network backup/update disabled. Only own org.unlimotion.taskloadingbench, no original app/phone. Exact source→build→packaged PE/natives→installed SHA→QEMU PID/birth/port→app PID→trace provenance. API34/x64/3CPU/2560MiB/1080x2400/hostGPU/qt-hidden/privateengine37.2.12. Same existing launcher/watchdog and failure-retaining ADB wrapper; fresh runtime identity33. No foreign serial/process operations. Guard refreshed after install/SHA and fixture-before. Runtime identity/boot/stablelauncher≥60s required.

Retain raw stdout/stderr on nonzero/timeout, failed/partial captures and metadata, best-effort independent cleanup even metadata write failure. Keep checkpoint_seconds naming and static global-callable guard; actual whole observation loop checked with fakes. Collector3 adds file trace pull/validation; existing Core/VM/Android validator unchanged on its original three-module view, then file-window inclusion within Core initialgraph/PID/freq and aggregate invariants. Capture PNG30/90, root author/advisory visual check; no card interaction or≤1s claim.

Build clean≤5m/publish≤40m/proof≤60s each; no emulator during build; own exclusive shadow lease/source manifest/absolute dotnet pin. Native preflight240s/capture400s/cleanup200s/boot180s/outer900s/independent45mwatchdog. No host builds/tests/media during native capture. Missing trace/observer hook/aborted path/changed data/uncertain cleanup means corresponding AC incomplete. No own app force-stop without exact runtime guard/PID recovery; outer finally proves exact QEMU exit.

## 10. AC /11. Acceptance-to-Test matrix
| AC | Required evidence | Failure/stop |
| --- | --- | --- |
| A1 no behavior drift/provenance | reversible exact overlay substitutions; original queue return/exception/order preserved; syntax controls; actual packaged FileReadTrace and probe callsites in FileStorage/Core bothABI, App35 observers unchanged,10managed+12native proof | any mismatch |
| A2 actual observer+collector controls | host harness uses actual FileTaskStorage overlay/helper for valid known tasks, malformed/duplicate IDs/tolerant errors; same returned models/errors/hashes/sourcebytes vs ordinary baseline; counts/window/queue/thread mismatch/overflow/missing phase/writer/error controls; collector23guards+whole-loop/globalbinding controls applied to newsource | no build/native admission before relevant pass/review |
| A3 one bounded native diagnostic | exact freshAPK/QEMU33/runtime; one coldPID; file trace full/PID/freq/windows within Coreinitialgraph/count3094/error0/duplicates0/queue+IO+serializer+JSON≤window; CoreVMAndroid existing protocol; native rootPNG inspected;3094before/after SHA; app/QEMUexit; sources unchanged | invalid→INCOMPLETE |
| A4 actionable honest result/tracker | raw+summary table of four components/remainder with exact endpoints and no doublecount/CPU inference; postEXEC advisory+author fallback; tracker CLI result/readback/statusActive | no product gain/parent5/1/5 PASS |

Host harness uses private temp task fixtures only and existing pinned packages/source; no new dependencies. Baseline vs instrumented return state equality includes typed TaskItem/unknown JToken, file hashes/order/error/duplicates semantics; deterministic full task fields supplied for default nondeterminism. Compile actual library via private target/artifacts path; source/build/hash proof recorded. UI suites product unchanged so don't repeat them solely for instrumentation; full retained CI2afd Main1572/Headless66/AndroidPkg/CodeQL is existing evidence, not newly run.

## 12. Rollback /13. Risks /14. Alternatives
Rollback = no integration, stop own app/QEMU, preserve evidence; tracked product unchanged. Risks: extra wrapper/hooks perturb thread scheduling; aggregate component time inclusive; failure loses summary; unexpected watcher reads invalidate count; queue sum ignores continuation overhead; graph remainder not automatically cloning; emulator notFold6. Actual first-window containment required, not source assumption. Baseline loading remains measured under observer. Full stack profiler/per-fileCPU/I/O logging excluded as more intrusive. Old rejected reader/single-worker optimizations not revived by these measurements.

## 15. Expected user review objections
| Objection | Response/proof |
| --- | --- |
| Yet another unbounded diagnostic | One measured12.214s window, four concrete components, bounded33(+only one actual helper correction), next candidate selected by dominant share |
| Queue wrapper changes behavior | Exact inverse + actual library/harness baseline parity and exception path tests |
| Metrics hide clone/hash costs | Explicit nonnegative remainder; don't assign it without new evidence |
| Snapshot root means ready in5s | Root confirms render only; parent/ordinary/phone/paired acceptance remains false |
| Probe writes task data | Helper output ownfiles only;3094taskSHA before/after mandatory |

## 16. Files /17. Plan /18. Open questions
Tracked only this SPEC; private profile4 FileTaskStorage/Core overlays/FileReadTrace/helperharness/publisher/proof/collector3/controls/native33/audits. No product source/public API/config/data migration. Read contracts source frozen2afd/43. Next: separate planning review, fixes/autoapproval, actual implementation review/controls then build/native. No unresolved user decision; confidence about dominant share deliberately unknown.

## 19. Review /20. Journal
Author Scope/Evidence: actual FileTaskStorage.ReadDirectoryCoreAsync/DeserializeTaskSnapshot/EnableLiveGraphAsync/ToGraphResult/PublishLiveGraph/TaskItemSnapshot and native32phase evidence inspected. Contract: A1-A4 cover wrapper compatibility/strict counters/runtime/cleanup/tracker. Adversarial: concurrent unrelated read/session contamination, exception missingJsonReady, aggregate sum vs window, callback capture and global Python name collision; all reject claims, preserve partial. Roles Developer source/return/exception fidelity, Tester actual source harness +negative probes, Operations pinnedownAPK/process/bounds, UX existing root render only, Domain no graph rule change. Applicable20linter items covered by sections1-18; rubric5/5/5/5/5/5 for bounded planning. Separate advisory pending, runtime writable +author fallback, no technical read-only isolation. No EXEC/native33 yet.

## Planning MEDIUM correction: two explicit nested intervals

Source review found existing Core initialgraph.begin precedes MigrateTaskStatusModel; that method independently performs File.ReadAllBytes for modern files before EnableLiveGraphAsync. Thus3094typed snapshot hooks do NOT count all raw reads of initialgraph.

Keep narrow BeginWindow immediately before EnableLiveGraphAsync, EndWindow immediately after its await. Success/output/A3/A4 mean **two-level measurement**, superseding the broader first-paragraph wording:

| Interval | Exact endpoints | Attribution |
| --- | --- | --- |
| Full initialgraph | existing Core initialgraph.begin→end | measured parent span, includes status prepass |
| File-window | FileReadTrace.BeginWindow ticks→EndWindow ticks around EnableLiveGraphAsync | four measured components +nonnegative file-window remainder only |
| Before-window gap | Core initialgraph.begin→File-window start | status-prepass-inclusive unmarked work +branch/hooks; not pure status/I/O time |
| After-window gap | File-window end→Core initialgraph.end | unmarked continuation/branch/hooks; no assumed cause |

Full span = before gap +file-window +after gap; each boundary must share actual PID/frequency and ordered ticks. Inside file-window queue/read/serializer/JSON remain disjoint and sum≤file-window wall; file-window remainder includes SHA/await/clones/cache/enumeration/graph publication and unknown observer cost, without naming the dominant subcause. Outside gaps are never merged into that remainder, typed component percentages use only the file-window denominator.3094 typed snapshot count is explicitly NOT all directory/raw I/O, nor a claim to eliminate the status pass. No extra status hooks/optimization/unchanged old candidate retry. Report all three intervals and both gaps even when small.

Author targeted re-review: outcome now matches actual coverage; status prepass no longer silently attributed to JSON/cloning. A3 validates both nested boundaries and disjoint sums; A4 table keeps exact denominators. Linter1/8/10/14 corrections above; command concretization16 planned prepare→host baseline+profile harness→pinned build+proof→guarded native33→offline analysis/CLI, exact generated commands mandatory actual-helper review before execution. Other reviewed items unchanged. Rubric readiness depends on advisory re-review, earlier30/30 author claim not closure.

## Журнал действий агента

19Oct planning targeted advisory re-review PASS: MEDIUM two-level coverage fixed; linter1/8/10/14 PASS, remaining prior results preserved,16 PARTIAL nonblocking for planning only (actual commands required before build/native). Rubric30/30 planning. Scope/Evidence actual storage/status migration boundaries inspected; Contract A3/A4 nested intervals match source; Adversarial status-prepass attribution now explicitly prohibited; roles Developer/Tester/Ops/UX/Domain constraints retained. Author fallback agrees, no new findings, writable reviewer without technical read-only isolation. User instruction autoapproval after review/fixes applies: transition EXEC for private helpers, build/native gate still pending actual-source controls/review. Product/task acceptance remains open.

Actual-helper lifecycle clarification: summary is an immutable snapshot frozen by EndWindow. Duplicate Begin/End and phase calls during the active window invalidate it; it is not a lifetime monitor of calls after freeze. No late-error-channel claim. Exactly-once native admission additionally requires actual packaged Core IL one BeginWindow/one EndWindow callsite and existing raw Core trace exactly one initialgraph.begin/end containing the one file-window. A second initialgraph or unmatched native interval rejects acceptance. Host negative repeat-window occurs before EndWindow. This narrows observer lifetime honestly without changing diagnostic scope or product code.

Actual commands (DROOT=D:/CodexArtifacts/android-task-loading-20261012): Python314 -X utf8 prepare-directory-read-profile1.py succeeded (8 exact reversible FileStorage substitutions, 406 frozen source hashes, Core wrapper inverse); prepare-file-read-host1.py succeeded. Before native build, run-file-read-host1.py builds baseline/profile actual library through private host.targets with SDK10.0.401 and separate artifacts paths, then 5 parity scenarios + actual-helper negative controls. No new dependencies. Actual-source advisory review pending; host controls may execute after source disposition, build/native gate still separate.

19Oct EXEC actual-source review/fixes: closed MEDIUM runner partial-output/owned-tree cleanup, actual-library summary validation and PE wrong-callsite/count-only holes. Source guards/syntax9, file validator28, collector guard23/whole-loop10, actual runner fakes6 PASS; no children in runner fakes/no native launch. Host1 ordinary build succeeded, first valid fixture run failed because harness precreated lock but actual WithDirectoryLockCoreAsync finally deletes it. Both author/reviewer missed full lifecycle initially; no product regression. All raw retained in file-read-host1 and final-disposition. Cause-specific host2 recovery removes precreated lock only and asserts absence after return while retaining exact all-file SHA equality; fresh prepare/run-file-read-host2.py namespace, template/generated Program match. Separate targeted advisory PASS +author fallback for recovery, host2 executing. PE regex corrected from source-assumed >d__ to actual old PE async-lambda >d / >d__N; caller/order checks remain. APK/native gate still open, actual PE positives/negative controls pending.

Host2 completed PASS: actual ordinary/profile builds (SDK401) and 5 full typed return/hash/error/duplicate/graph scenarios equal, actual integrated summaries validated; 11 helper probes passed. Only 2 nullable warnings in private fixture filename dictionary,0 errors. Immutable input/payload hashes and all-file fixture inventories preserved. Actual hard-cap test separately compiled same helper:4097 complete admissions produced overflow1/errors>0; earlier probe-overflow only exercises expected-count bound. PE checker controls11 PASS: actual host FileStorage IL positive, same-root wrong caller/lambda/order/duplicates mutations rejected; Core controls use explicitly synthetic calls with typename observed in old Core PE, actual new Core positive still required in APK. Root author inspected these receipts/source and actual phase/order rows; no unsupported native claim.

### Expanded linter final planning/command concretization

| # | Status | Concrete evidence/contract |
| --- | --- | --- |
| 1 | PASS | two-level outcome and root visual boundary |
| 2 | PASS | native32 actual parent interval plus inspected source |
| 3 | PASS | attribution unknown, four components plus honest gaps |
| 4 | PASS | aggregate bounded observer only |
| 5 | PASS | no product integration/Task2/phone/release |
| 6 | PASS | File observer/Core wrapper/collector/offline validator responsibilities |
| 7 | PASS | actual FileStorage project import and existing Core direct project reference |
| 8 | PASS | count/phase/thread/once-active-window invariants and exact Core callsite requirement |
| 9 | PASS | raw retain, missing summary INCOMPLETE, independent bounded cleanup |
| 10 | PASS | diagnostic only, unknown overhead and process counter overlap, no gain acceptance |
| 11 | PASS |3094 copied task bytes, own package/process/runtime |
| 12 | PASS | inverse overlays+actual typed parity5, original repair/SHA/migration unchanged |
| 13 | PASS | no integration; stop own processes, preserve evidence |
| 14 | PASS | A1-A4 explicit success/failure |
| 15 | PASS | source9/validator28/guards23/loop10/runner6/host5+11/cap4097/PE11 |
| 16 | PASS planning | commands below with bounded stage budgets; actual APK/native results pending |
| 17 | PASS | source→controls→APK→proof→fresh33→audit→CLI |
| 18 | PASS | no user-owned unresolved choice |
| 19 | PASS | Expanded multi-module native diagnostic |
| 20 | PASS | performance/dotnet/UI unchanged source baseline and data/provenance boundaries |

Exact next commands: pwsh7 -NoProfile -File DROOT/publish-android-directory-read-profile4.ps1 (guarded shadow401/exclusive lease, clean5m/publish40m); then same shell finalize-android-directory-read-profile4.ps1 (10PE/12native, App35 calls + File/Core exact callsites bothABI). After actual package review+file-read-native-admission1.json SHA freeze only: pwsh7 -NoProfile -File DROOT/run-file-read-ownavd33.ps1 (180s boot/240preflight/400capture/200cleanup/900collector/45mwatchdog). Native collector imports unchanged existing3module validator plus new file validator. Offline exact-source audit and two full PNG inspections then postEXEC advisory/authorfallback, CLI execution result and exact readback. No ordinary performance acceptance from this diagnostic.

19Oct APK completed13:33Moscow: clean/publish success, cleanupUncertainfalse, SDK401/AOT/arm64+x64. APK7D3A95C8F4F2C126D6EBA425EA71010607C8BDED1667EC05BAFD7040E4929644; final-proofSHA4106d684df3ca43a20251b47b2f2f43667391f90968e2c453e355b27f13066c0. Actual10decodedPE equal linked/immutable snapshots;12native hashes unchanged; bothABI App35/File9/Core2 callsites correct, actual Core async-lambda order307<411;30prebuild inputs unchanged. Known native duplicate/page-size warnings retained. Author audit +separate actual-package/native-plan advisory PASS. Fixed admission stale-review lineage and postrun raw trace/PNG/runtime→exit identity bindings; actual admission validation-prefix controls11PASS, synthetic review for controls only/no admission write. Actual review record now binds exact proof/APK/audit, freeze70inputs successful. One guarded run33 started; outcome still pending. No builds/tests/media during capture.

### Diagnostic33 result — PASS, Task1 remains open

Actual one run completed13:41Moscow, collector212.87s, every orchestration stage exit0. AppPID4172/QEMU8332. Core246=32App+214storage, VM174, Android39; all original protocol constraints valid. File3094 queued/arrived/snapshots/completed, directoryCalls/returns1, errors0/overflow0. All70admission+13collector hashes unchanged. Actual trace metadata SHA and PNG capture SHA bound to raw files and inspected images; runtime→metadata→before/expected exit exact identity, after[]; own app stop observed. Exact3094task filenames/SHA before/after; no all-service-file equality claim.

| Interval/component | ms | Denominator/meaning |
| --- | ---: | --- |
| Full initialgraph |14905.1502|existing Core begin→end |
| Before-window gap |8091.1373|status prepass plus unmarked branch/hooks; not pure I/O |
| File-window |6813.9412|BeginWindow→EndWindow around EnableLiveGraph |
| Queue |290.3227|4.2607% of file-window |
| Read bytes |4531.6469|66.5055%; open/copy/ToArray/dispose inclusive |
| Serializer |116.5720|1.7108% |
| JSON/repair |1004.6706|14.7443% |
| Unmarked file-window remainder |870.7290|12.7786%; no specific cause assigned |
| After-window gap |0.0717|unmarked |

Root and separate advisory personally inspected both original native PNGs:30s Loading with controls,90s populated root/noLoading. No interaction, precise readiness cutoff, continuous black-frame absence, physical Fold6, ordinary paired gain/regression or parent5/1/5 claim. Full=before+window+after exactly; within window components+remainder exactly. Native33 not repeated; no correction34 needed.

### Post-EXEC full review

Scope/Evidence: approved SPEC, git status/diff (5untracked in-scope diagnostic SPECs, src clean), private overlays/helper/publisher/finalizer/collector/validators and source-bound controls; actual package proof, admission70, collector13, raw4traces, original2PNG/visualreceipt, runtime/app/QEMU exit, exact3094task hashes, author-audit and CLI receipts inspected. Contract A1 sourceinverse/actualtypedparity/PE bothABI; A2 actual source/negative/collector controls; A3 one bounded completed native33 +root+data+cleanup; A4 actual table/advisory/CLI satisfied.

Adversarial pass corrected phase-boundary attribution, stale-receipt admission, raw/PNG/exit identity chain; absence of post-freeze lifetime monitoring explicit and Core exactly-once proved. No gain inference from separate diagnostic32 vs33, no outside-gap denominator inflation, no hidden JSON/worker retry. Role-Based: Developer exact original read/parser/SHA/return/order and reversible hooks; Tester actual library parity/negative/control failures retained; Ops package/runtime/source/input/cleanup bounds; UX actual native Loading/root only; Domain no task/status/graph rule change. Depth: scope/AC/scenarios/ledger/objections verified; validation bound to current files; unsupported claims excluded; errors/duplicate/writer/timeout/callable/race edges covered; docs private specs only; no public API/runtime behavior delivery change. Manual challenge:66.51% is within file-window only and8.09s is not a pure status-I/O measurement. Root fallback agrees with separate advisory, writable runtime/no technical isolation.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | planning/measurement | Status prepass outside proposed window | two-level intervals/gaps | fixed/re-reviewed |
| MEDIUM | host fixture/runner | full lock lifecycle, typed path normalization, bounded cleanup/raw preservation | source fixes; failed host1 retained, fresh host2 proof | fixed/re-reviewed |
| MEDIUM | evidence | callsite vs counts, bool/int flags, stale receipt and capture/exit bindings | exact checks/current-source negative controls | fixed/re-reviewed |
| LOW | perturbation | inclusive observer/host/emulator timing limits | keep explicit, no subtraction/phone/gain claim | residual, contract satisfied |

Separate technical postEXEC advisory PASS after raw independent recompute/visuals; A4 initially pending, then narrow CLI re-review PASS. CLI actual execution result succeeded, exact readback InProgress/Active/agent+lease retained/usertext+dates+criteria+relations preserved: file-read-native33-final-cli-before1/result1/readback1.json. No product source change, merge/Task2/3/release. Stop decision PASS for this diagnostic only; next primary work must address measured file-operation/prepass cost, not repeat rejected single-read/worker/JSON candidates unchanged. Parent Task1 remains open.
