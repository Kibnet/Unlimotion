# Android: проверить ограниченное параллельное чтение

Статус: **CLOSED — FEASIBLE_PHASE_ONLY**. Продукт и Task1 не приняты этим экспериментом.

## 0. Метаданные /1. Outcome

Expanded по canonical C:/Users/Kibnet/.codex/agents/templates/specs/_template.md: private Android executable/build/runtime side effect. Task1 e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6, existing branch/worktree perf/android-e74c5d1a-task-loading/android-task-loading, product HEAD2afd4ab2/source43d95e1b. Owner Codex/local Windows; exact model/effort runtime hidden, no model evaluation. Central stack QUEST/review/linter/rubric/performance/tool/dotnet-desktop/task-context/result-tracking +local UI override. User approved emulator/autonomous sequence/CLI and autoapproval after review/fixes. Original user goal remains Android loading/cache/search; Task1 cold5s/resume1s/freshness5s open.

Outcome: one bounded read-only Android feasibility experiment decides whether at most4 simultaneous independent reads materially shorten the two expensive read phases on the fixed3094-file copy. Output source-bound host controls, private APK/proof, fixed paired raw trial journal/integrity/process cleanup/table and tracker result. No product integration or application-speed claim. If gates pass, design a separate tested implementation; if fail, close this exact candidate without unchanged retry or relaxed thresholds.

## 2. AS-IS /3. Проблема

Diagnostic33 completed PASS (SPEC directory-read-phase-profile): graph14905.1502ms; before-window8091.1373ms contains status migration prepass plus other unmarked work; typed file-window6813.9412ms includes bytes4531.6469/66.51%, JSON1004.6706/14.74%, queue290.3227/4.26%, serializer116.572/1.71%, remainder870.729. Source shows status prepass File.ReadAllBytes then HasModernTaskStatusJson, and typed snapshot FileStream(Read/ReadWrite|Delete)/MemoryStream CopyTo/ToArray before local Json.NET/Hash. Sequential independent file waits are a new hypothesis; status gap is NOT measured pure I/O. Process counters/diagnostic32→33 are not paired gains.

Actual Android default uses GetExternalFilesDir with internal fallback; user can choose another folder. Current controlled fixture path /storage/emulated/0/UnlimotionFold6Diagnostic20261012/Tasks is global shared storage. Do not assert FUSE cause, phone path equivalence, or benefit of moving storage. Minimal experiment retains this exact path. Rejected single-read363 shared migration bytes/profilePASS/plainNO-RETAIN, directory-single-worker, JSON readers and reactive candidates stay rejected. This candidate keeps two independent reads, changes bounded concurrency only; it is not a retry of their implementations.

## 4. Goals /5. Non-Goals

Fixed N1 vs N4, same copied bytes/APIs/parser, ordered consumption, bounded admission count and full source/byte integrity. No TaskStorage cache/graph/model/Git/status/data/Settings change, no legacy migration or writes, no original app/phone installation. No UI layout/feature/search implementation and no Unlimotion initialization in the probe. Tiny diagnostic activity may show running/done only; no app readiness acceptance. Product UI source unchanged/CI2afd already retained; native activity PNG is limited diagnostic presentation evidence.

## 6. Design /responsibilities /integration

Private net10.0-android minimal project in DROOT/file-read-feasibility1, SDK10.0.401/rollForward:disable, AOT Release, arm64+x64, minSDK23/target36 like current APK. New own package org.unlimotion.filereadbench, own signing/settings/output only; no existing package replaced. No new NuGet dependencies; existing Domain project may supply TaskItem names for actual copied classifier. No Avalonia/Git initialization and no comparison of this package's startup to Unlimotion.

Private reader methods:

- Prepass reader: original File.ReadAllBytes followed by exact source-extracted HasModernTaskStatusJson+ConsumeStatusJsonValue block. Original branch/classifier body bytes preserved and source SHA/span bound. No migrations, fallback parsing or status writes; collect modern true/false count identically B/C, including false files (do not filter them).
- Snapshot-bytes reader: exact FileStream/MemoryStream content-read block from DeserializeTaskSnapshot, same FileShare.ReadWrite|Delete/CopyTo/ToArray/dispose. JSON/model/cache construction excluded. Preserve original bytes API and exception behavior. This is a phase probe, not storage parity proof.
- Both compute SHA256 of each returned byte snapshot and verify expected exact names/hash3094 after every trial. Verification hashing is additional same work; inclusive read/classifier/hash wall is reported, not pure I/O. No task contents/paths per-file output, no per-file timers or process counters.
- B prepass runs original synchronous reads on one background worker. B snapshot-bytes awaits one Task.Run read at a time, matching original scheduling shape. C both use fixed4 Task.Run read operations with a bounded queue/ring; schedule no more than4 outstanding, consume results in original fixed order, drain all started work on error/disposal. All tasks capture error as data or are awaited; no unobserved exception/late work across trial boundaries. No runtime tuning of concurrency.

Input filenames are frozen once from the expected manifest in fixed ordinal order for every trial; no path filtering, no enumeration timing claim. All3094 are read/checked each trial regardless of modern classifier result. Private output bounded<=256KiB, session/trial/footer PID/frequency/order/exact variant/phase/pair/count/hash checks; partial/failure retained and fails experiment. Only trial endpoint Stopwatch/process CPU/allocated counters; counters process-wide, no exclusive attribution.

Diagnostic UI test contract (local UI MUST): minimal Activity first renders Ready without starting readers. Guarded automated ADB test verifies own-package Ready text via UIAutomator XML and original full PNG before sending one explicit own Activity intent with run=true/singleTop. Activity accepts run exactly once, changes label Running and starts background experiment; duplicate trigger cannot create a second journal/trial sequence. After all trials/footer, it renders Done or Failed and writes an own completion marker. Collector polls that marker at fixed10s intervals (no UIAutomator during timed work), then verifies Done XML/PNG only after timing has ended. Raw intent-delivery warnings retained; known singleTop delivery warning is not automatically failure if return0/exact owned PID/component and journal identity prove delivery. Invalid intent/error/PID is failure. UI test covers diagnostic control states only, no Unlimotion feature/readiness claim; no before/after feature video because product UI unchanged.

## 7. Data/state /8. Runtime contract

Same own API34/x64/3CPU/2560MiB/1080x2400/hostGPU/qt-hidden private engine37.2.12 and same global copied fixture. New ownAVD attempt35 and fresh collector/output names. Source/build→packaged managed PE/AOT config→installed SHA→QEMU PID/birth/port→app PID→raw trace→ordered trial verification. 3094 expected hashes embedded as diagnostic resource and file SHA bound; input task text never embedded.

Manifest permission MANAGE_EXTERNAL_STORAGE only for own emulator probe; package-specific appops grant verified, no phone/original app operation. Own output in /storage/emulated/0/Android/data/org.unlimotion.filereadbench/files. Probe reads fixture only, writes own session journal. Before/after exact task names+hash mandatory, service files explicitly outside task-byte claim.

One benchmark process; independently guarded own app/QEMU cleanup and fixed runtime budgets. Reuse actual reviewed collector ownership/ADB/save-error/cleanup/global-callable mechanisms rather than ad-hoc unbounded runner. Boot180s separately, preflight240s, trials720s, cleanup200s, collector1250s (1160s summed inner stages+90s margin), independent45m watchdog. Outer orchestration additionally bounds launcher60s/runtime qualification180s/boot180s/collector1250s/shutdown90s; total1760s remains below45m ownership controller/watchdog limits. Guard actual remaining effective deadline before dependent work. Build clean5m/publish40m/proof bounded. No host builds/tests/media during capture.

## 9. Errors /10. Experiment design and stop rules

Before native, actual shared C# pipeline/readers compile+host controls: N1/N4 same ordered results/hashes/classifier flags on private fixtures; outstanding<=4 (not merely queue length), first/middle/final read exceptions, draining, no cross-trial work, zero/one/many files, missing/changed file, wrong expected hash/duplicate/missing/out-of-order records; classifier/source syntax/inverse controls. Use deterministic fake reads only for admission/order/failure controls, actual temp-file reads for byte/parsing fidelity. Host timings not an Android acceptance gate.

Frozen trial sequence:
1. Four excluded warmup trials: prepass B then C, snapshot-bytes B then C. Each still full3094 integrity checked.
2. Seven measured pairs, each pair contains both phases. Odd pair: prepass B,C then snapshot C,B. Even: prepass C,B then snapshot B,C. Exactly28 measured trials. No record/pair replacement, extension, variant tuning or early timing decision.
3. Each trial time includes read/classification/verification SHA and ordered result collection; expected-manifest comparison/table serialization outside endpoint interval. No forced GC/cache drop or observer-cost subtraction. CPU/allocation measured at endpoints with overlapping observer/UI costs explicit.

Hard native feasibility gates (all correctness/cleanup gates first): for each measured pair i, r_phase,i=C_phase,i/B_phase,i, q_i=(Cprepass,i+Csnapshot,i)/(Bprepass,i+Bsnapshot,i), a_i=(allocatedCprepass,i+allocatedCsnapshot,i)/(allocatedBprepass,i+allocatedBsnapshot,i). Use median of the7 paired ratios, never ratio of independent medians. Each phase C faster in>=5/7 pairs and median(r_phase)<=.75; median(q)<=.60; median(a)<=1.25. Baseline wall and allocation denominators strictly positive; required unavailable samplers→INCOMPLETE, not zero. All timed windows positive/ordered and counters monotonic, no hidden retry/expired/overlap. Common per-file work unit read→classifier (prepass only)→SHA is identical in B/C; only placement/concurrency differs, expected-manifest comparison outside both endpoint windows. Combined is an experiment score over two isolated phase trials, not actual graph/whole-app duration or projected exact gain. Fail any→REJECT; valid slow data never rerun. Invalid helper may get one cause-specific corrected fresh namespace/attempt36 after reproduction/fix/re-review; preserve35, no correction for a valid inconclusive/slow result.

## 11. AC /12. Acceptance-to-Test matrix

| AC | Required evidence | Failure/stop |
| --- | --- | --- |
| A1 source and isolation | source-extracted exact reader/classifier blocks, SDK/AOT/ABIs/package/managed PE/source proof, product src unchanged | any drift |
| A2 actual helper controls | compile actual shared pipeline/readers; host fidelity/order/bound/drain/error/validator/collector controls | no native before pass/review |
| A3 one complete fixed experiment | exact35 identity/installed SHA/all32 trials/order/count3094/full hash equality/no overlap/strict trace/footer/gates recomputed/root diagnostic PNG | missing/invalid→INCOMPLETE |
| A4 data/runtime/result | before/after3094 names/SHA, exact own app/QEMU exit, inputs unchanged, table/decision/postEXEC review/CLI result+readback Active | no completion/gain inference |

## User-Observable Scenarios

| Trigger | Visible/output expectation | Evidence |
| --- | --- | --- |
| Own probe launch and explicit run intent | automated Ready→Running→Done/error state checks, exactly one experiment; original app untouched | A1/A3 |
| Read/trace failure | original raw partial retained; no success aggregate | A2/A3 |
| End/deadline | own processes stopped, task bytes unchanged, result recorded Active | A4 |

## 13. Compatibility /14. Rollback /15. Risks and alternatives

No product change to roll back; stop own processes/preserve probe evidence. Controlled-data experiment does not prove migration ordering/cooperative locks/watcher/noncooperative write safety or real FileStorage return parity; successful feasibility requires a new implementation SPEC/tests/UI/paired full-flow validation before retention. N4 additional in-flight buffers/tasks and AOT/runtime/UI difference can distort extrapolation; max4 bound is count, not a universal byte limit, large-file memory policy belongs future implementation. Exact fixed dataset size and max file size recorded before admission. Extra SHA/classifier process cost means no pure I/O claim. Same-path cold/hot cache history is uncontrolled; alternating pairs/warmups expose this limit, no cache-state inference.

Alternative relocating data/cache/certificates changes user storage/freshness semantics and lacks current support; no implicit migration. Whole-directory worker/shared-byte handoff already rejected. Testing N2/N8 after seeing this result forbidden in this experiment; a later distinct hypothesis needs prospective scope.

## Decision ledger

| Decision | Owner/chosen | Confidence | User decision needed |
| --- | --- | ---: | --- |
| Test bounded independent reads | agent; new measured I/O share motivates |.8|no |
| Fixed4 vs1 | agent; overlap waits on3CPU, bounded outstanding |.7|no, ordinary reversible experiment |
| Two original byte APIs/classifier, no data writes | agent |1|no |
| Strong feasibility gates before integration | agent |1|no |
| Reviewed specs autoapprove | user |1|no |

## Expected User Review Objections

| Objection | Response/evidence |
| --- | --- |
| Another diagnostic instead of feature | finite32 trials and binary gate; runtime test determines whether concurrency is worth product work |
| Minimal app proves app speed | explicitly phase feasibility only; full app integration/ordinary evidence remains mandatory |
| Migration semantics lost | no migrations performed or claimed; source classifier false cases retained, future implementation gate separate |
| More parallel memory / unfinished workers | max4 actual outstanding/drain controls, fixed dataset size and no cross-trial work |
| Existing rejected optimization revived | two independent reads remain; fixed bounded concurrency differs from shared-buffer/one-worker candidates |

## 16. Files /17. Plan /18. Open questions

Only this SPEC tracked. Private project/readers/pipeline/activity/manifest/resource, host harness, publisher/finalizer/PE proof, validator/controls, guarded owner/collector35, raw journal/audit/PNG and CLI recorders under DROOT. No product source changes. Plan review/fixes/autoapprove→source/control implementation review→actual host controls→APK/proof/package review→freeze/run35→audit/gates/postEXEC/CLI. Exact generated commands and input hashes required actual-helper admission; build/native gate not granted by planning. No unresolved user-owned choice; feasibility deliberately unknown.

## 19. Post-SPEC review /20. Журнал действий агента

Author initial Scope/Evidence: source reader blocks/prepass/default Android storage path, old single-read NO-RETAIN and actual completed33 table inspected. Contract A1-A4 map isolated read experiment and data/process/CLI boundaries. Adversarial: original classifier false files, concurrency bound vs queue length, error drain and cross-trial work, different app context/caches/inclusive verification cost, frozen sequence/gates and no global-app extrapolation. Role-Based: Developer read source fidelity/bounded scheduler; Tester actual source controls/strict journal; Ops private package/permission/runtime ownership/bounds; UX diagnostic state only; Domain no migration semantics accepted. Linter/rubric/separate advisory planning review pending; no EXEC/new probe files yet.

Planning corrections: collector1100s was below1160s sum of inner reservations; increased only outer to1250s with90s margin, fixed trial/performance gates unchanged. Exact paired wall/allocation formulas/common work unit now explicit; no ratio-of-medians ambiguity. Read-only host fixture check:3094files,4489511total bytes,max21992bytes, everySHA matches expected; no task text printed. These known small buffers do not establish arbitrary large-file memory policy. Deadline checks may sample ticks before a read solely for abort bounds, never per-file latency/CPU records.

### Post-SPEC review and EXEC transition

Separate advisory planning PASS after targeted re-review; runtime writable/read-only conduct, no technical isolation. Author fallback agrees. Full Scope/Evidence: approved source blocks/default storage path/completed33/old rejected candidates/currentSPEC+central profile/AC/sequence inspected. Contract: fixed32 phase-only trials, source/API/integrity/drain/runtime/UI and CLI boundaries. Adversarial: budget sum and ratio-formula ambiguities fixed; caches/context/counter limits remain explicit. Role-Based Developer/Tester/Ops/UX/Domain covered above; no migration/feature/gain accepted by this probe. Depth: scope/AC/scenarios/ledger/objections nonempty and bounded, failure controls/source gates planned, no unsupported phone/app projection, no hidden task writes; docs/spec only until EXEC. Manual challenge: this probe cannot establish safe migration/locking semantics; future implementation needs its own scope. Two MEDIUM fixed/re-reviewed; no other findings.

Linter20 PASS planning (1 outcome;2 measuredASIS;3 attribution problem;4 goals;5 exclusions;6 reader/pipeline/runtime responsibilities;7 private Android/shared host integrations;8 outstanding/order/drain invariants;9 partial/error cleanup;10 frozen ratios;11 exact copied fixture;12 no product migration;13 own-process rollback;14 A1-A4;15 tests/negatives;16 prospective commands and fixed stops, actual commands required before build/native;17 dependencies;18 no human decision;19 Expanded;20 performance/dotnet/UI profile). Rubric30/30: goals/bounds5 finite phase experiment; ASIS5 source+measured33; design5 N1/N4/fixedwork/formulas; safety5 ownpkg/read-only bytes/drain/cleanup; verification5 mappedAC/negatives/gates; autonomy5 fixed selection and correction limit. This is readiness for helper preparation only.

User autoapproval after review/fixes applies: EXEC private helpers authorised, build/native remain separately gated. Planned exact helpers DROOT/prepare-file-read-feasibility1.py, run-file-read-feasibility-host1.py, publish-/finalize-file-read-feasibility1.ps1, guarded owner/collector35 and offline validators/controls. Product HEAD unchanged. Native UI checking will verify Ready and Done via real XML/PNG; Running state/controller error/repeated trigger checked by shared state-controller tests and actual same-intent-after-Done test with unchanged journal. No UIAutomator during timing.


### EXEC: source, host and private build checkpoint

Actual Core/SourceReaders shared by host+Android; exact frozen original classifier/read-block span SHA, no product changes. Deterministic gated error-drain control replaced nondeterministic sleep before acceptance. Host1 actual62controls/full3094 parity PASS. Lifecycle source review found Activity-owned once guard could reset and completion target obsolete Activity: fixed actual shared process-static ProbeUiSession with bind/unbind/current renderer, static UI dispatch and no Activity capture. Six actual shared UI lifecycle controls added. Host2 compile CS0136 (delegates a/b/c colliding with corpus locals) failed before controls/native; raw retained in host2-final-disposition.json. Renamed renderA/B/C; Host3 PASS68/full3094 parity,413 inputs+8 payloads unchanged. Advisory lifecycle/Host3 PASS, resources Host1==Host3 bytes.

Publisher source review three MEDIUM fixed: terminating no-QEMU CIM+stderr, fresh all stage receipts, cleanup uncertainty across all stages. Actual publisher1 stopped before publish because canonical evaluated min23.0 vs erroneous expected23; raw preserved, no build/out/APK/native. Freshpublisher2 correction reviewedPASS exact23.0, source min23 unchanged. Actual SDK401/AOTRelease arm64+x64 build2 PASS APK20ed1b2e5f97af35ab6fdcfd937420d46f9574c265b4b6158d87ed3b998c3c80, source unchanged/cleanupfalse.

Validator first synthetic41controls missed actual producer zero-based ordinal. Advisory MEDIUM fixed validator+fixtures to0..31; added actual Host3 source SHA/fixed-plan literal/ordinal binding and rejection of one-based trace. LOW formula fixture corrected to distinguish medianpaired2 from independent-mediansratio1. Fresh validator-controls3 PASS43 and targeted advisory PASS; previous receipts retained. These are helper checks, no native or parent acceptance.

Finalizer/privatecollector35 being prepared; source finalizer review found early-vs-late APK identity gap, fixed by early frozen copy/stateSHA and using only it for tools/decode, source+copy SHA checked afterproof. Native admission not granted here. No timings/no prototype integration/Task1 completion/next task/merge/release.


### Actual package / admitted35 / preflight failure and one correction

Actual finalization PASS: APK20ED... frozen/source/state exact; both ELF/XALZ PE SHA59FAD6279A5916001AFE0536B53A89097F36D8542B78942BFA8004208DBE6EAB equal linked+immutable snapshots, exact embedded resources and two AOT native payloads. Advisory independently decoded/checked actual package; build420source bindings current. Collector ownership/admitted dependency findings fixed,24guard/7wholeflow/43validator/7admission controls passed. Actual review receipt and461-input freeze admitted35.

Native35 INCOMPLETE before timing: PID4216/QEMU40644. At Ready, cmd0050 uiautomator claimed dump success; cmd0051 exec-outcat returned rc0 with missing-file text. Parsing correctly failed, but helper had allowed an unverified dump target and remote exit masking. No run=true intent, no trial journal/marker, no performance sample. Directory absence vs Android access restriction not proven. Cleanup pull/analysis missing-file errors retained, own app/exactQEMU exits proven,3094taskSHA before/after exact,462inputs inclruntime current. Author and advisory agree INCOMPLETE, not slow result.

Prospective sole allowed correction36 (unchanged APK/phase32plan/gates): UI XML goes to unique /sdcard/unlimotion-filereadbench36-STATE.xml, first require absent to avoid overwrite, then use shellcat and its remote exit; exact owned label/package checked. No diagnostics during timed trials. Reset only already-installed disposable org.unlimotion.filereadbench from35 after fresh36 owner guard, currentPIDnone and exactinstalledAPK20ED verification, then uninstallthisownpackage and reinstall the same unchanged APK. No original package or user files targeted; retained raw35 is not overwritten. Readiness now requires correctedcollector36/owner/wrappers plus actual UI raw-replay controls, verified prior35metadata/exit/disposition and freshadmission2. No further correction after36, no tuning/extension of valid timings.

Actual helper UI controls9: original35missingraw failure reproduced, corrected remote rc1 and masked rc0 missingraw both rejected, validXML/ownedlabel pass, wrongstate/foreignpkg/foreignlabel-ownedother/malformed/existingpath rejected. Current guards24/wholeflow7/admission7 PASS. Initialguard-harness1 failure was fake SHA expectingPath vs actualstr; retained, next harness corrected adapter only and passed. Native36 review/admission pending at this checkpoint.

### Actual corrected36 technical result (post-EXEC review/CLI pending)

Native36 completed once on same unchanged APK20ED..., actual PID4282/QEMU11256. All four outer stages exit0/processExitProven, exact own QEMU exit after[]; app stopped, no cleanup/write errors.466admission inputs plus runtime receipt unchanged. All3094 task names/SHA before/after exact; every one of32trials verified full3094 bytes/classifier ordered fingerprints. Four warmups excluded,28 measured, seven pairs per phase, zero replacements/extensions. Ready/Done/duplicate/recreated XML+original full PNG checked by root; same PID/journal hashes, instance2 visible after recreation. No UIAutomation inside timed trials. Offline validator recomputation equals captured analysis; author audit PASS.

| Pair | Prepass B ms | C ms | C/B | Snapshot B ms | C ms | C/B | Combined ratio | Allocation ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 4428.333300 | 1537.389400 | 0.347171 | 4222.058500 | 1473.860700 | 0.349086 | 0.348106 | 1.236266 |
| 2 | 4786.365400 | 1474.294800 | 0.308020 | 5231.639500 | 2701.092200 | 0.516299 | 0.416788 | 1.246420 |
| 3 | 5122.229400 | 1722.047200 | 0.336191 | 4439.599900 | 1549.656200 | 0.349053 | 0.342163 | 0.785002 |
| 4 | 4907.413500 | 1731.556100 | 0.352845 | 4919.247500 | 1904.783600 | 0.387210 | 0.370048 | 0.998212 |
| 5 | 4473.494000 | 1404.386300 | 0.313935 | 4472.474600 | 1382.163200 | 0.309038 | 0.311487 | 1.140264 |
| 6 | 3580.753900 | 1540.185600 | 0.430129 | 4285.689700 | 1590.261800 | 0.371063 | 0.397950 | 0.998609 |
| 7 | 3902.291300 | 1641.054100 | 0.420536 | 4390.945500 | 1592.386900 | 0.362652 | 0.389889 | 1.231032 |

Medians of paired ratios: prepass 0.3471711128880023, snapshot 0.3626523945696889, combined 0.3700483511133639, allocation 1.1402644644391913. Wins7/7 eachphase; all fixed numeric gates pass. Decision FEASIBLE_PHASE_ONLY, not product retention or Task1 acceptance. Inclusive read/classifier/hash/ordered collection, process counters overlap; no pureIO/cache-state/wholegraph extrapolation. Minimal app has no model/graph/Git/Avalonia initialization. Future product implementation requires separate SPEC preserving migration/locking/cache/watch/error semantics and UI/full-flow validation. Rejected old single-read/shared-byte/one-worker candidates remain rejected.

Native35 remains INCOMPLETE beforeRun, no timing. Prelaunch36 stale engine37-35.log finding fixed tocurrent36 in owner+generator; freshguardcontrols5PASS25 checks binding, admissionwriter now checksguardownerSHA; admissioncontrols3PASS7. Correction36 actual PASS does not erase35. No further trials or product edits in this experiment.

### Полное закрытие диагностического scope

Separate advisory technical Post-EXEC PASS and narrow A4 PASS received. Actual CLI result/readback file-read-feasibility36-final-cli-{before1,result1,readback1}.json: success, exact summary, InProgress/Active/sameagent/lease, user fields/criteria/relations/history preserved. Summary SHA077d81695a4ac85aaaac8b9781bfff47a438886056a7e0c923f6b83823cdf541. Author fallback agrees. Scope/Evidence: SPEC, source/host/native package, actual raw35 and36, all controls/proof/467input bindings/runtime→exit/4originalPNG and CLI inspected. Contract A1 source isolation, A2 actual helper controls, A3 fixed native/full integrity/state evidence, A4 cleanup/result readback fulfilled for diagnostic only. Adversarial: oversized-file/migration/cooperative lock safety deliberately unproven; allocation+14.03%, overlapping counters and isolated-phase context remain explicit. Role-Based Developer/Tester/Ops/UX/Domain PASS within these limits; no human choice needed.

Post-EXEC linter20 PASS: outcome/ASIS/problem/goals/bounds; exact readers and pipeline/runtime responsibilities; integration/order/drain/state/error/invariants; fixed ratios/budgets/fixture; no product migration; rollback own processes; A1-A4 mapped controls/raw/UI; exact executed commands/source proofs; dependencies/decisions/review/profile. Rubric30/30 applies only completed finite feasibility: bounds5, measuredASIS5, concrete design5, isolation/cleanup5, actual verification5, autonomous stop5. Manual challenge: 3x phase speed is not a whole-app claim and no product acceptance follows without new SPEC. No open findings, no next task/merge/release; parent5/1/5 open.
