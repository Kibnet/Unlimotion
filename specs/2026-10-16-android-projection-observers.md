# Дешёвые наблюдения обязательной root/emoji-проекции при запуске Android

## 0. Метаданные
- Task1 e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6; owner Павел; medium, Expanded (reactivity/public UI state/multi-file/performance).
- Worktree C:/Users/Kibnet/.codex/worktrees/android-task-loading/Unlimotion, branch perf/android-e74c5d1a-task-loading. Baseline62ff9a9b (single-read rollback); product storage files exactly08225864.
- Central stack: creator-vibe-lens/model/collaboration/tool/testing, QUEST governance/mode, spec-linter/rubric/review-loops; performance-optimization, dotnet-desktop-client, UI automation/local UI MUST; commit/GitHub policies. Canonical expanded template used.
- Codex surface; model evaluation not applicable: .NET/Avalonia mechanism, not model change. Runtime .NET10/Avalonia12.1.1. Windows diagnostic and API34x64 emulator evidence strictly separate.
- Standing user authority: reviewed/fixed SPEC autoapproved; Task1 ownbranch/worktree; merge only accepted parent before nexttask. No release/original installation replacement. No new phone prerequisite.
- DROOT D:/CodexArtifacts/android-task-loading-20261012. Prior evidence: single-read NO-RETAIN SPEC; startup-bottleneck-after-single-read1.json; startup-construction-probe1/result3.json (finalized; independent hash review before implementation).

## 1. Цель / outcome contract
Уменьшить стоимость обязательного построения списка задач на Android, сохранив актуализацию статусов/дат/emoji/родителей и прежний UI. Success bounded item: baseline equivalence tests +UI/full validation, затем significant measured projection gain и ordinary startup gain без переноса затрат в первое открытие карточки. Итог: один независимо проверенный retained или fully-reverted механизм и доказанный результат в parent/CLI/PR. Подготовленная SPEC, Windows allocations и instrumented gain не завершают задачу5/1/5.
Stop: functional mismatch/retention failure -> rollback exact owned delta; no trial substitution or rescue repetitions. Missing actual source/payload/protocol trace or expired/incomplete sample -> no PASS. No new unrelated optimization in this item.

## 2. AS-IS / evidence
MainWindowViewModel.cs:875/905 builds two Emoji WhenAny observations,1222 builds root3-property WhenAny (IsCanBeCompleted/Status/UnlockedDateTime). Result of root selector ignores date value, but a date change must still trigger refresh even when final bool unchanged. Emoji non-null A->B must refresh grouping despite selector false->false. Source filter/sort/group/batch/UI-delivery pipelines stay intact.
TaskItemViewModel.cs:137 already uses accepted direct INPC/property-value observation, attach-before-initial/lock/Distinct/dispose for its own already-retained contract; exact MainWindow WhenAny subscription order is separately characterized below. This item applies that mechanism at different, still expression-heavy MainWindow sites; it does not change TaskItemVM internals.
Unchanged baseline3 Android instrumented traces: median models15.037s/236.77MB; publication13.797s/544.86MB. Core postPublication0.818–0.972s (to Core init.end), full remaining VM tail0.928–1.151s (to loading.false); neither supports a standalone15% redundant-hydration optimization. Process-wide allocation intervals overlap background work, not exclusive causal attribution.
Finalized Windows synthetic result3:3runs of3094tasks, root allocations57.43–57.85MB and emoji37.79–38.62MB (combined95.21–96.29MB), wrapper23.00MB. Hash-bound13inputs copied immutably to snapshot3; snapshot-proof3 binds result/inputs/source/program/SDK/actual managed assemblies. Windows synthetic constructor/subscription diagnosis ranks root+emoji observation allocations; it omits actual AutoRefresh/Group/Transform/Bind and real relations, VM includes synthetic DTO/ID construction. Wrapper command cleanup is explicit. No Android timing prediction from that probe. Failed initial missingReactiveUI bootstrap preparation preserved, test-host bootstrap used subsequently.
Single-read363 failed ordinary fixed gate and is fully removed; no reintroduction. N6a direct INPC is retained from prior work. F/single cache-Edit, N6c Replay sharing, N7 SortAndBind, N8 lazy business subscriptions and lazy Save/Reload remain rejected.

## 3–5. Проблема / дизайн / Non-Goals
Repeated expression observation construction costs allocation/CPU on obligatory projections. Reuse a small internal typed observation primitive; keep event delivery on original emitter thread and preserve value-level equivalence. No scheduler overrides, cache, file IO/serializer/migration, storage revisions, batching, sort/filter/group changes, business subscriptions, commands/editor laziness, status-option/repeater-template changes, search semantics or UI layout. No public API/schema/version migration.

## 6. TO-BE
### Responsibility / algorithm
- New internal TaskPropertyObservation.cs in ViewModel: Observe<TSource,TValue>(source,valueProvider,string propertyName), TSource class; runtime INotifyPropertyChanged cast because Fody adds that interface after C# compilation. Only internal source types implementing INPC are valid. Do not expose a public API.
- Use Observable.Defer +direct FromEventPattern(INPC) filtered by exact ordinal property name or null/empty broad event, Select(valueProvider), StartWith(valueProvider(source)), then per-property DistinctUntilChanged. Initial value must be delivered before attaching the event handler, matching exact original single-property WhenAny; the initial callback can mutate that property before attachment and that event is not delivered by the current baseline. Standard Rx manages termination/detachment/consumer exceptions; do not add a custom lock that changes concurrent Dispose blocking. Getter failures go to OnError and detach; preserve root cause/channel/terminal behavior. Reflection-only TargetInvocationException wrapping is absent in typed getters; the three actual nonvirtual TaskItemVM properties are nonthrowing, so no user getter failure behavior changes. Fault tests normalize the reflection wrapper only, not arbitrary errors or value traces. DistinctUntilChanged applies separately to each raw property value, before caller's projection/CombineLatest. No replay/shared connection/global scheduler.
- Emoji sites observe raw Emoji then Select(emoji=>emoji==null). Root site uses Observable.CombineLatest of three single-property direct observers (IsCanBeCompleted,Status,UnlockedDateTime), with the exact original selector (c,s,u)=>c&&s!=Archived. Each stream keeps its last notified property value, matching original WhenAny3; do not reread the entire source tuple on one named notification. Never Distinct on the final bool: it would lose non-null emoji transitions/date refreshes. Single property names remain exact; five Observe calls across three integration sites.
- Existing downstream pipelines unchanged. No coalescing/delay, no thread-affinity claim beyond unchanged emitter thread and existing owner delivery. Per-property direct event observation follows the measured MainWindow baseline contract; the accepted TaskItemVM adapter itself is unchanged.
- Characterize exact original bool-site observables first: root task.WhenAny(IsCanBeCompleted,Status,UnlockedDateTime,(c,s,u)=>c.Value&&s.Value!=Archived), emoji task.WhenAny(Emoji,c=>c.Value==null). Differential oracle is these exact original forms versus direct single-property observations/CombineLatest followed by the original selector; do not compare only against a different raw WhenAny oracle. Characterize existing WhenAny traces first. If actual broad/equality/reentrant/error behavior differs from this design, fix SPEC/adapter using evidence before integration; do not weaken tests or call mismatch intentional.

### User-observable scenarios
| Scenario | Action | Expected result | Evidence / AC |
|---|---|---|---|
| S1 Startup | load modern/legacy mixed tasks | same visible list/statuses, UI remains usable | startupUI +native Loading/root/card /A1,A2,A3 |
| S2 Emoji grouping | parent title emoji A->B with include/exclude filters | catalogs/groups/child+grandchild trail update, selection intact | existing real emoji UI +new regression if gap /A1,A2 |
| S3 Status/time | mutate availability/status/unlocked date while final bool stays same | existing root filtering refresh, no stale row | observer differential +projection/UI /A1,A2 |
| S4 Lifecycle | subscribe/unsubscribe, reconnect and late event | correct initial, no callbacks after completed Dispose | unit characterization +source switch UI /A1,A2 |
| S5 Thread/race | worker event, reentrant change/dispose | baseline-equivalent deterministic event trace/thread; existing UI-owner delivery retained | contract +EmojiProjectionUiAffinity /A1,A2 |
| S6 First action | open existing card/edit/save/reload after startup | ID/children stable, fresh persisted/external data | startupUI and native card guard /A2,A3 |

### State / interaction matrix
| State | Trigger | Expected | Edge |
|---|---|---|---|
| new subscription | subscribe | one current value delivered | initial before event attach as exact original; mutation during initial characterized |
| active | selected property changes | last-notified per-property values then CombineLatest/projection | final bool equal still emits when observed relevant last-notified raw property differs |
| active | irrelevant/null/empty property | ignore irrelevant; broad recomputes each property | equal per-property values suppressed as baseline |
| active | reentrant event | baseline trace/order | do not queue or suppress without baseline evidence |
| active | dispose (also reentrant/other thread) | detach; no later completed-disposal callback | already-entered callback boundary explicitly tested |
| disposed | further event / new subscribe | old silent, new independent initial | no leaked/shared subscriber |
| getter/initial delivery fails | subscription setup | detach, propagate baseline-equivalent error | exact failure semantics tested before integration |

### Decision ledger
| Decision | Owner | Basis / confidence | Consequence / human? |
|---|---|---|---|
| one mechanism,3sites only | agent | measured allocation/projection +retained N6a /0.9 | no bundle with lazy UI; no human |
| per-property distinct before CombineLatest/bool select | agent | counterexamples A->B/date change /.99 | differential tests mandatory |
| same-thread direct event stream | agent | accepted adapter contract /.95 | no global scheduler; race tests |
| use emulator | user | explicit current instruction /1 | no Fold6 claim |
| retain only after ordinary gate | agent under performance owner | instrumented gain insufficient /.99 | failure rollback, no gate lowering |

## 7–10. Контракты / данные / совместимость / rollback
Internal observable helper only; no persisted fields. Task JSON, Git, TLS, settings and other projections unchanged. Exact typed snapshots unchanged; consumers get synchronous observations with baseline value/change ordering, errors and disposal boundaries. Runtime casts guarded/tested; source not INPC is programmer error, not silently ignored. Empty/null observed property name must be rejected at creation (broad event names still valid).
Rollback restore only new helper/integration/tests specific to helper to62ff9a9b, preserve generic UI/regression tests and experiment evidence/SPEC. Existing EGL/nativeGit/disposed-Save fixes retained. No rewrite history, no original task files/application replacement. Deletion of helper must leave no references; exact product baseline checked by Git blobs.

## 11. Acceptance / test matrix
| AC | Required outcome | Proof |
|---|---|---|
| A1 observer contract | baseline-equivalent final bool traces for initial/changed/equal/null/empty/irrelevant/reconnect/reentrancy/errors/threads/dispose, including silently changing another field before its own notification | new differential tests vs existing WhenAny +explicit golden critical cases; compare event payloads/threads/count/order and detach counters |
| A2 actual UI | S1–S6 pass; no missing task/filter/selection; first edit/save/reload correct | StartupProjectionAndRelations, EmojiProjectionUiAffinity, EmojiTitlePersistence/BreadcrumbEmoji, existing tab emoji/filter UI, SingleViewStartupUI; add/update actual UI coverage for raw non-null emoji A->B/date path if existing gap; inspect rendered PNG |
| A3 functional/full | affected Release build, required targeted and full Main+Headless/AndroidPkg/CodeQL PASS at actual candidate source | local rawTRX +current-head CI rawrootTRX/stage/invocation/treeaudit; source/APK10PE/12native proofs before native run |
| A4 profile significance | fixed3pair median paired C/B publication wall<=0.85 and allocation<=0.90; wholeVM wall/alloc<=1.05 | complete unexpired Core cache.publish.begin->end and VM connect.begin->loading.false; exact frozen source/protocol; process-wide allocations qualified |
| A5 ordinary retention | fixed3pair root conservative C_upper/B_lower median<=0.85; card median<=1.10,max<=1.25; all correctness/invariants | observer-free SDK401AOT2ABI ownAPK ordinary frozen cohort, owned window clocks +original orderedPNG/videos/3094SHA/PIDs/QEMU/cleanup |
| A6 parent | cold<=5s/resume<=1s/localfreshness<=5s including cold/resume | separate parent protocol, no claim from relative winner alone |

Numerics: use existing reviewed median-of-paired-ratios and baseline relativeMAD algorithms, but new explicit named publication metric/gates/controls (do not mislabel as initialGraph). No arithmetic ratio-of-medians. Extension only failed timing gate with same baseline relativeMAD>0.15 and all allocation/correctness/cardmax hardgates pass; decision recorded before pair4; order4C/B then5B/C, both required; all5 same gates. Any stable timing failure/hard failure -> NO-RETAIN, no extra pairs. A4 precedes A5; all A1–A5 required for retention. A6 required for parent merge/completion/next task.
Packaging provenance for changed MainWindowViewModel is mandatory: old profile.targets removes the normal MainWindowViewModel.cs and includes a generated MainWindowViewModel.Profile.cs. Generate a fresh separate per-variant overlay from exact B/C commit; mechanically removing only approved diagnostic insertions must reproduce that exact variant source byte-for-byte (including encoding/newline contract). Bind base Gitblob/source SHA, approved insertions/patch, overlay hash and inverse comparison in frozen prebuild/final proofs. Never reuse the baseline overlay for C or mutate prior frozen overlays. Candidate compile-items must include new helper and variant-correct3integration sites. Independently inspect actual packaged/decoded ViewModel PE: baseline helper type/direct calls absent and original WhenAny sites retained; candidate helper type and exact5Observe calls across3integration sites present in actual method bodies, alongside proof overlay inverse equals candidate source. Linked snapshot equality alone is insufficient. Ordinary compile-items must use exact normal sources, no profile overlay/observers/Probe; actual decoded PE must show the same B/C behavioral difference. All actual source/module/native manifests and helper paths get a fresh namespace and are frozen before first trial.

Native protocol retains already reviewed safety design but fresh namespace/freezes/helpers: excluded p0B/C; fixed p1B/C,p2C/B,p3B/C; one continuous ownQEMU per cohort; fresh appPID each trial; actual API34x64/3CPU/2560MiB/1080x2400/hostGPU admission;7stable ownpackage checks>=60s; no host build/tests/media during capture. Global stale2048 label not runtime proof. Deadlines root140/UI165/card25/Core180/VM120; controller45m bounded; no incomplete trial substitution. Full3094 sourceSHA before/after, exact ID/24children, no observer/Probe in ordinary actual decoded managed PE, native bytes unchanged; ownQEMU absence before build/media. Manual original-order PNG/root/card and full native-last review; all-video strict integrity decode, no allframes watch claim; PTS not wallclock. Old measured files immutable; no mutation of prior frozen inputs or APKs.

## 12. Риски / ожидаемые возражения
| Risk / objection | Mitigation | Disposition |
|---|---|---|
| bool result equal hides real source change | distinct raw/tuple before selector; A1 golden A->B/date cases | mitigated by planned tests |
| broad/reentrant/thread/dispose differs | baseline differential traces +detach/race tests; fix design on mismatch | mandatory before integration |
| generic helper scope grows | internal,3sites only; no old TaskItemVM adapter refactor | bounded |
| microprobe predicts device benefit | diagnostic rank only, actual frozen emulator paired gates | no unsupported claim |
| old rejected idea repeated | no F/N6c/N7/N8/cache/lazy command changes | source diff reviewed |
| quicker Loading hides slow first card | actual ordinary card median/max gates and edit/reload UI | mandatory |
| user sees cached stale data | storage/reconcile untouched; source SHA/external update UI | mandatory |
Rework gate: six observable scenarios/matrix/ledger/AC proof/risks defined; role review below. No human-owned alternatives unresolved.

## 13–15. План / вопросы / profile
Baseline62 fullHeadless66 and targeted113+rename8 passed; further local baseline projection/emoji UI24/24 (StartupProjection7,EmojiAffinity14,EmojiTitle2,Breadcrumb1) passed before product changes; newhead fullCI pending and must be recorded. First finalize/hash-review diagnostic result3; add characterization tests before product delta and run baseline relevant UI. Review this SPEC/fix/autoapprove. Implement helper/tests first; differential correctness before3site integration. Targeted/UI/build/full candidate CI; independent post-EXEC/source review. Freeze fresh profile inputs/proofs/protocol and gate controls; profile then ordinary only on PASS; retain or exact rollback; update parent/CLI/PR. All local test/build run sequentially to avoid locked outputs. Native captures exclude host competing work.
Commands (fresh DROOT/projection-observer-baseline1 or candidate1 paths; each class separate exact filter):
```powershell
$classes=@('StartupProjectionAndRelationsTests','EmojiProjectionUiAffinityTests','EmojiTitlePersistenceTests','BreadcrumbEmojiUiTests','SingleViewStartupUiTests')
foreach($c in $classes){ dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --treenode-filter "/*/*/$c/*" --report-trx --results-directory "$evidence/$c"; if($LASTEXITCODE){throw $c} }
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --treenode-filter '/*/*/TaskPropertyObservationTests/*' --report-trx --results-directory "$evidence/observer"
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release --maximum-parallel-tests 1 --minimum-expected-tests 66 --report-trx --results-directory "$evidence/headless"
```
Full CI authoritative commands in scripts/ci/Invoke-TestStage.ps1 restore/build/test Main+Headless; current run rawartifact download/audit as existing62 workflow. Check actual expected counts, no zero-test success. Set UNLIMOTION_LOADING_MIGRATION_EVIDENCE for renderedstartup PNG; new targeted observer/UI evidence paths declared before execution. Native command helpers must be finalized/reviewed/frozen after candidate commit before first app launch; no executable unreviewed numeric policy. No walltime assertions in ordinary unit tests.
Open questions: no human decisions required; actual baseline notification edge behavior, candidate allocation/Android speed remain empirical gates. Failure is an outcome, not authority to lower criteria.

## 16–18. Файлы / соответствия / альтернативы
| File | Change | Purpose |
|---|---|---|
| ViewModel/TaskPropertyObservation.cs | new internal typed observer | scoped direct-INPC |
| ViewModel/MainWindowViewModel.cs |3observer sites only | same downstream projection |
| Test/TaskPropertyObservationTests.cs | baseline differential/golden/race | observable contract |
| relevant existing UI tests | required additional critical regression if missing | real changed flow |
| this SPEC/parent | decisions/evidence | audit/continuation |
Before expression observers -> after direct per-property observers/CombineLatest; downstream dataflow unchanged. Alternatives: built-in AutoRefresh might reduce setup but initial/equal/broad behavior not established; sharing Replay repeats rejected mechanism and adds lifetime risk; lazy StatusOptions/template objects needs separate measurements and public identity/localization analysis; wrapper RemoveCommand eager cost smaller (~23MB synthetic) and not selected. Redundant initial hydration upperbound~2% cannot meet standalone gate.

## 19. Quality / review
### Linter (author planned-evidence check)
| # | Status | Concrete evidence |
|---|---|---|
|1|PASS|§1startup goal and retained/reverted output|
|2|PASS|§2actual source and raw baseline3traces|
|3|PASS|§3expression setup at3requiredsites|
|4|PASS|§3preserved delivery/value/lifetime|
|5|PASS|§5exclusions incl rejected experiments|
|6|PASS|§6helper/MainWindow/tests responsibilities|
|7|PASS|§6exact875/905/1222integration|
|8|PASS|§6per-property distinctbeforeCombineLatest/bool,gate/attach/dispose|
|9|PASS|§6/7errors and no silent unsupportedsource|
|10|PASS|§11paired profile/ordinary gates|
|11|PASS|§7no persisteddata/schema|
|12|PASS|§7old downstream/lifecyclecontracts|
|13|PASS|§10exact-owned rollback62|
|14|PASS|§11numericA4/A5,parentA6separate|
|15|PASS|§11A1–A3/S1–S6negative/actualUI|
|16|PASS|§13exactclasscommands/nativefreezeadmission|
|17|PASS|§13baseline->contract->integration->native->decision|
|18|PASS|ledger/§14nohumanquestion|
|19|PASS|§0mediumExpandedjustified|
|20|PASS|profiles/localUI/fullreview/measurementbeforeafter|
Rubric author: goal5,AS-IS5,design5,rollback5,testability5,autonomy5=30/30 readiness only; independent review and actual gates override scores.
Role-based author review: domain workflow PASS planned (samefilter/status/date/emoji); UX PASS planned (unchangedlayout/storyboard launch->Loading->root->card;A->Bfilter; workerstatus); tester PASS planned (differential negatives/UI/full/native); architect PASS planned (3sites/internal/noAPI drift); delivery PASS planned (ownworktree/draft/newheadCI/freezes/nooriginals). Before independent review these are author assertions, not independent approval.
Post-SPEC: PENDING independent scope/contract/adversarial/role review; fix/re-review before autoapproval. Evidence inspected by author: actual3sites,acceptedadapter,raw3trace reanalysis,priorF/N6a/N6c/N7/N8 outcomes,probe preparations/result subject finalization,rollbacktests/rawTRX/renderedPNG. Manual challenge: tuple/finalbool distinction can silently stalegroup/date; broad/initial/exception equivalence must be demonstrated, not asserted. Residual realAndroidgain unknown. No independent no-findings claim yet.
Post-EXEC: implementation and rejection/rollback checkpoints below; A6parent remains open regardless bounded experiment result.

### Pre-approval adversarial correction16Oct01:29
One proposed tuple getter could observe another silently modified field before that field's own INPC and then suppress its later event through tupleDistinct. Replaced with three independent single-property direct streams +CombineLatest, preserving each last-notified value and original selector. Three integration sites contain five actual helper calls. This is smaller behavior contract and reuse of accepted per-property mechanism; no implementation began. Add explicit delayed-own-notification oracle case. Profile provenance proof must distinguish5actualcalls across3sites. Review/re-review pending.


### Post-SPEC final16Oct01:30 — PASS / autoapproved
Independent /root/startup_profile_review inspected current SPEC/owners/3sites/acceptedadapter/profile.targets/priorF,N6a,N6c,N7,N8/rawtraces/probe result3+13immutableinputs/baselineUI24. Scope/Contract/Adversarial/Role passes PASS after fixes: MEDIUM staleMainWindow overlay -> variantinversebyte+actualPE5calls proof; MEDIUM premature tuple reread -> independent oneproperty streams/CombineLatest+delayedownnotification regression. LOW wording clarified to per-property/CombineLatest. Targeted re-review closed bothMEDIUM. Linter1–20 eachPASS, rubric30/30 readiness only. Relevant Domain/UX/Tester/Architect/Delivery roles PASS for plan; no actual candidate behavior/performance claim.
Depth: source/scope/AC/scenarios/ledger/expectedobjections/validation/unsupportedclaims/regression/lifetime/errors/delivery all reviewed; no persisted/publicAPI/hiddenpipelinechange. Manual challenges silentotherfield/staleoverlay converted to mandatory gates. No-findings justification: no open blocking planning findings; unknownWhenAny edge semantics remain explicit A1 gate, not assumed satisfied. Reviewer performed no tests/mutations, writable sandbox without technical read-only isolation. Stop PASS to autoapprove under standing user instruction, EXEC begins characterization only; integrate3sites only afterA1. Parent/CI/native gates remain open. No human question.


### A1 characterization correction16Oct01:33 — integration still blocked
Baseline1 test build failed: unnecessary DoNotNotify attribute not referenced by the test project (which has no Fody weaver); removed without adding dependencies. Baseline2:10/11 PASS. Only mismatch was our expectation for mutation inside initial single-Emoji OnNext: original delivers [false], then attaches (mutation event lost); root3reentrancy passed [true,false]. No product change/failing product claim. Corrected golden to observed original contract. Root date/status boolsame trace [true,true,false,false,false,true]; delayedotherfield [true,true,false]; broadnull/empty [true,false,false]. Getterinitial/active trace shows OnError(TargetInvocationException with rootcauseInvalidOperationException getterfault); baseline2/3 handler count was checked after explicit cleanup, so automatic detach not yet proven at that checkpoint. Direct typed internal getter failure compares unwrappedrootcause/channel/detach, no copiedreflectionwrapper. Standard Rx Defer/FromEventPattern/Select/StartWith/Distinct chosen to preserve actual initial ordering and error/lifetime behavior, avoiding manual-lock disposal differences. Scoped independent re-review pending before helper implementation.


### Re-review amendment16Oct01:35
Independent reviewer confirmed standardRx algorithm/initial-beforeattach/3CombineLatest/5call provenance; found MEDIUM validation gap: old getter test could hide missing auto-detach through explicit finallyDispose and accepted either throw orOnError. Corrected actual test to require exact OnError normalized onlyTargetInvocationException.InnerException, no synchronousFailure, correct trace, and HandlerCount0 BEFORE explicitDispose. Baseline4 rerun pending; baseline3/11 does not prove newly added assertions. Added two actual existing wizard->jellyfish UI cases that reconnect before editing, to verify observer disposal/newsubscription through real titleinput/include/excludeflyout/descendant/renderedrows. UI baseline/candidate images get separate reconnect prefixes; no new product mechanism.


### A1 admission16Oct01:38
Baseline4 source-bound raw11/11 proved strengthened getter OnError/auto-detach before cleanup; independent MEDIUM gap closed. New internal standardRxhelper +exactoriginal/direct goldens/differential/reentrant/worker/concurrentDispose/consumererror tests30/30PASS, rawTRX/source frozen DROOT/projection-observer-a1-proof1.json. BaselineactualUI4cases including2newreconnect cases PASS; root inspected8firsttransition contactthumbnails +4fullreconnect popup/selected frames; not all36PNG reviewed at native resolution. No MainWindow integration existed during these runs. A1 gate passed;3site/5call integration now applied, targeted/UI/full/currentheadCI/native retention still pending. Helper test-design/code independent review requested separately.


### Post-EXEC implementation checkpoint16Oct01:44 — targeted/UI PASS; retention pending
Scope/Evidence: approvedSPEC plus amendedinitial/Rxcontract, actual helper35lines and MainWindow3sites/5calls diff, test30case original/direct/error/lifetime oracle, updated2reconnectUIcases, rawTRX/a1snapshot/sourceproof and postintegration67targeted results. DROOT/projection-observer-targeted-audit1.json verifies30observer+7projection+14affinity+2emojiTitle+1breadcrumb+9startupUI+4Wizard. Actual wovenTaskItemVM used; Release affectedbuild passed through testbuild. Baseline4exacterror/auto-detach11PASS, candidateA1/30PASS independently reviewed beforeintegrationadmission. Prior baseline24UI+113rollback/fullHeadless66 and CI62Main1556+Headless66 remain baseline-only evidence.
Contract pass: A1passed; A2local flows and their assertions/renderedpixels passed; A3fullpostintegrationHeadless/newheadCI pending, A4/A5nativegates not executed, A6parentopen. Helper per-property last-notified values/standardRxinitial/error/dispose preserve actualobservedcontract, typednonthrowinggetters need no reflectionwrapper. Business subscriptions/commands/storage/thread delivery/filter/group/sort/batches unchanged. No sourceexpansion beyond approved two productfiles/two testfiles/SPEC.
Adversarial/Role passes: initialsingleEmoji mutation capture mismatch corrected BEFOREhelper, delayedotherfield preserved via3streams rather than fulltupleread; broad/equal/worker/reentrant/concurrentDispose/consumer/getter errors characterized vs exact original. Domain/UX/TTester/Architect/Delivery scope reviewed: filter/status/date/title/grouping/descendants/reconnect/currentcard paths remain intact, no new sourcecache or scheduler/API/persisteddata changes. Actual36baseline/candidate PNG pairs pixel-identical (separate fresh outputs); root inspected8firsttransitioncontactthumbs and4fullreconnect popup/selected frames pervariant, not all36native frames. No UIlayout change; existingunit harness emits PNGfallback, future Androidpairedflow requiresnativevideo.
Independent source/A1review PASS nofindings and integrationreview confirms exact5calls/3sites. New helper does not refactor acceptedTaskItemVM adapter. Scope/AC/scenarios/matrix/ledger/objections/evidence/unsupportedclaims/edgecases/docs/hiddencontracts/manualchallenge depth checked; no blocking scoped implementation finding. Manual challenge stale profileoverlay remains mandatoryfuture packagingproof, not declaredpassed now. Stop focused implementationreviewPASS for draftcandidatecommit; do not mark itemretained/parentcomplete. Fulltests/currentCI/packaging/frozenpairedprofile/ordinary remain mandatory before retention. No human decision needed.


### Independent implementation/UI checkpoint16Oct01:46
Reviewer /root/startup_profile_review confirmed focused code/lifetime/test/UI PASS, no findings: actual5calls/3sites, helper/30exactcontracts, integrated67targeted rawTRX incl Wizard4/reconnect, all36PNGpairs SHA-equal and4fullreconnectframes inspected independently. Scope includes helper/tests/MainWindow/UItest/SPEC; untrackedfiles must be committed. Reviewer no mutations/tests, sandbox writable. Draftcandidatecommit admitted; localfullHeadless/newheadCI/nativepackaging/profile/ordinary/retention remain open. No use of CI62 as candidatevalidation.


### Native profile verdict16Oct02:55 — NO-RETAIN / rollback validation pending
Текущий candidate00a3447f прошёл targeted67/67, полный localHeadless66/66 и exactheadCI38819438902 Main1588/1588+Headless66/66 без skips. RawTRX/stages/invocations/tree независимо сверены: checkout9de5351f/tree272f57ff равен candidate tree. AndroidPkg38819438891 и CodeQL38819438988 PASS. Два свежих profile AOTAPK B62ff/C00 независимо проверены по406/407Gitblobs, exact inverse-byte overlays, actual10decodedPE и12unchangednative; VM обеих ABI содержит правильные B0/C5calls3sites. Все это readiness/correctness, не performance acceptance.

Actual freeze45inputs SHAaaa317d20893f8219067186054773ccd35ea86068695525c260c35532d9ca1f4; AVD27 API34x64/3CPU/actual2560MiB/hostGPU/1080x2400. Два excludedwarmup и fixed3pairs B/C,C/B,B/C на одном QEMU176904, шесть freshPIDs, без подмен/добавлений. Во всех trial3094SHA до/после точны, полные traces и ordinary UI flow в instrumented APK получены. Root просмотрел276PNG observations какcontacts (84warmup+192measured),16полныхroot/cardframes и8native-lastframes. Все8video полностью integritydecoded после доказанного clean ownAVD27exit; не утверждается ручной просмотр каждого видеокадра. PTS не измеряют latency.

| A4 metric | Median paired C/B | Limit | Verdict |
|---|---:|---:|---|
| cache.publish wall |0.8934864199|<=0.85|FAIL (-10.65%)|
| cache.publish allocated |0.9158088094|<=0.90|Hard FAIL (-8.42%)|
| wholeVM wall |1.1921633319|<=1.05|FAIL (+19.22%)|
| wholeVM allocated |0.9536978828|<=1.05|PASS (-4.63%)|

Baseline relativeMAD publication4.36%/wholeVM10.95%; и hard allocation failure, и stable timing failures запрещают extension4/5. Ordinary APK этап A5 не строился/не запускался, потому что A4 отклонён. +19.22% относится к этому instrumented cohort, не доказывает ordinary/Fold6 regression; process-wide allocations не устанавливают исключительную причинность. Parent5/1/5 не выполнен.

Independent /root/startup_profile_review повторно разобрал6rawtracesets/174rawwindowbrackets/order/PIDs/QEMU/freeze/sourceproofs/3094hashes/формулыMAD,192measuredPNG SHA и8video/nativeLast bindings: evidence PASS, disposition NO-RETAIN, findings0. Автоматический scoped rollback: MainWindow восстановлен побайтно до62ff, candidate helper удалён. Сохранены14 originalWhenAny characterization cases и4Wizard UI cases (2из них reconnect). Rollback tests/currentheadCI/closure review ещё pending. Ни merge, ни task2/3, ни release/installation не начаты.

### Rollback local validation16Oct03:09 — PASS; new exacthead CI pending
Actual rawTRX:14 original contracts+7projection+14affinity+2title+1breadcrumb+9startupUI+4Wizard=51/51, failures/skips0 (projection-observer-rollback-audit1.json). All36rollback PNG pixel-equal to frozenbaseline; independent reviewer additionally confirmed byte-equal candidate pairs and inspected4full reconnectinclude/exclude popup/selectedframes. Root inspected36contactobservations+2fullframes (projection-observer-rollback-manual-review1.json). Existing headless screenshot harness provides PNGfallback, not nativevideo; nativefailedcandidate8videos already audited separately.

Full local Headless66/66 failures/skips0, exit0, boundedprocesscomplete/cleanupUncertainfalse, actualrawTRX+state proof projection-observer-rollback-headless-audit1.json. Product MainWindow exact62ff, helperabsent; no other productiondelta from62. Retained14originalcontract cases replace30candidate/oracle cases; useful Wizard4remain. Independent source/targeted/UI rollbackreview PASS, fullraw66 closure being reviewed. Scope/AC/scenarios/objections/disposal/source/payload/manualchallenge inspected; no pending functional finding. Stop localrollbackchecksPASS to scopedcommit/push; exactnewheadCI Main expected1572+Headless66 plus companionworkflows remains mandatory. No parentcompletion/merge/nexttask/release; no Fold6claim.

16Oct03:10 independent reviewer confirmed fullraw66/states and complete localrollbackgate PASS, no findings. Read-only conduct in writable sandbox. Scoped rollbackcommit admitted; exactnewhead CI still pending, parentopen.
