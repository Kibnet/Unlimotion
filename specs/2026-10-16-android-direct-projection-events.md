# Прямые INPC-сигналы обязательных проекций Android

## 0. Метаданные
Task1 e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6; owner Павел. Medium / Expanded: reactivity, UI state and performance across helper/MainWindow/tests. Canonical C:/Users/Kibnet/.codex/agents/templates/specs/_template.md. Branch perf/android-e74c5d1a-task-loading, existing isolated worktree android-task-loading/Unlimotion. Baseline: forthcoming scoped rollback of rejected00a3447f; product byte-identical62ff9a9b. Record exact rollback SHA before EXEC. No new tracker task/branch before parent accepted and merged.

Instruction stack: central creator-vibe-lens/model/collaboration/tool/testing/QUEST governance and mode, performance-optimization context, dotnet-desktop-client, UI automation and local UI MUST, spec-linter/rubric/review-loops, Git/commit policies. Codex/Windows .NET10/Avalonia12.1.1; Android API34x64 emulator. Model eval not applicable: no model/prompt migration. Standing user authority: review/fix/autoapprove SPEC and sequential delivery; emulator replaces unavailable phone for ongoing validation, no physical-device claim. No release or replacement of installed originals. DROOT = D:/CodexArtifacts/android-task-loading-20261012.

Review runtime: collaboration inherits current danger-full-access; available child APIs do not expose a read-only sandbox override. Separate reviewer performed read-only calls and disclosed writable sandbox, so these are advisory checks, not technically isolated independent review. Per review-loops fallback owner adds a separate adversarial pass below; actor separation never substitutes for technical isolation. This corrects the previous label without changing validation evidence, thresholds or authorization.

## 1. Цель и outcome contract
Original symptom: slow Android loading and resume with Git tasks. This bounded experiment reduces subscription construction cost without changing task filters, events or data. Success requires equivalent contracts/UI, full checks, significant measured profile and ordinary launch gain; otherwise remove the product delta. Output is one reviewed retained or fully reverted mechanism with raw evidence and CLI result. Parent cold<=5s/resume<=1s/freshness<=5s is separate and remains open until actually demonstrated.

Stop on contract mismatch, unsupported provenance, incomplete trials or failed retention. No threshold adjustment, trial replacement or repeating rejected operator composition to obtain a favorable sample. No product integration until original/direct contracts and offline allocation admission pass. No native trials before actual APK proof and exact candidate CI.

## 2. Проверенный AS-IS
MainWindowViewModel.ConnectCore has two Emoji WhenAny sites and one mandatory root IsCanBeCompleted/Status/UnlockedDateTime WhenAny. Original expressions have per-property equality and last-notified values; final bool equality must not suppress emoji non-null transitions or date refreshes. TaskItemViewModel has a retained INPC adapter, but its lock/attach-before-initial semantics differ; leave it untouched.

Original baseline profile publication median13.797s/544.86MB, wholeVM78.239s/964.74MB (startup-bottleneck-after-single-read1.json). Windows synthetic original observations about95-96MB for3094 tasks (startup-construction-probe1/result3.json); diagnosis only, excludes full DynamicData and Android timing. Rejected standardRx composition00a3447f: publication wall ratio.893486 and allocation.915809, wholeVM wall1.192163; hard/stable gates fail, NO-RETAIN. It used Defer/FromEventPattern/Where/Select/StartWith/Distinct for each raw property. There is no evidence attributing wholeVM regression specifically to reflection or one operator. New hypothesis concerns removing the composition's per-subscription objects and event conversion; must prove allocation difference before costly Android trials.

## 3. Проблема
Mandatory property observations still create substantial subscription machinery. The previous mechanism reduced cost insufficiently. A direct typed event hook with standard Rx lifecycle can be materially cheaper, but equivalence and real Android benefit are unknown.

## 4. Цели дизайна
Single internal helper, unchanged downstream selectors and pipelines. Preserve initial-before-attach, emitting thread, per-property equality, reentrant/disposal/error contracts. No custom lock around consumer callbacks. Reuse standard Rx termination and DistinctUntilChanged; avoid recreating an observer framework.

## 5. Non-Goals
No cache/schema/storage/Git changes; no eager/lazy tab or model/command changes, batching, scheduling, replay/sharing or root-sort refactor. No edits to accepted TaskItemVM observer or native Git/EGL/late-save fixes. Do not reintroduce single-read, worker, lazy Save/Reload, F/N6c/N7/N8. No Task2/3, parent completion, merge or release from a relative gain alone.

## 6. Решение
### 6.1 Ответственности и алгоритм
New internal TaskPropertySignals.Observe<TSource,T>(source, propertyName, typed getter), where TSource:class, with runtime cast to INotifyPropertyChanged inside Create. Fody adds INPC after C# compilation, so do not require the interface in the generic compile-time constraint or add it to TaskItemViewModel. Unsupported runtime source must fail explicitly through OnError, not silently ignore notifications. Return Observable.Create<T>(observer => ...).DistinctUntilChanged(). Inside Create:
1. Read initial value in a narrow try/catch. Getter failure calls OnError and returns Disposable.Empty. Call initial OnNext outside catch BEFORE attaching handler; initial consumer failure propagates and cannot leave a handler.
2. Attach one PropertyChangedEventHandler directly to source. Ignore unrelated ordinal property names; null/empty means broad notification. For matching events read getter in narrow try/catch, deliver getter failure via OnError, otherwise OnNext outside catch. Do not turn consumer failure into OnError.
3. Return Disposable.Create(detach same handler). Standard Observable.Create AutoDetach and Distinct handle termination, reentrant Dispose and a concurrently entered consumer. No monitor around OnNext/Dispose and no additional replay/delayed scheduler.

Keep raw Distinct before projection. Two Emoji calls Observe(nameof(Emoji), static task=>task.Emoji).Select(value=>value==null). Root CombineLatest of three independent single-property Observe calls with exact original selector(can,status,unlocked)=>can && status!=Archived. Do not reread other fields on a named event, or distinct the final bool. Exact5calls at the same3sites as rejected experiment; remaining3rootWhenAny and all other paths unchanged.

The typed actual getters are nonthrowing; synthetic getter faults compare original OnError channel/rootcause/detachment, normalizing only original reflection TargetInvocationException. Initial mutation occurs before event attach exactly as golden. Race tests must prove Dispose does not wait for an already entered callback and no subsequent event is delivered. Existing AutoDetach semantics, not a claim that callbacks already running disappear.

### 6.2 Visual plan / evidence
Layout unchanged. Storyboard: Loading -> populated root -> selected Unlimotion card with24children; title emoji A->B with include/exclude popup/descendants; reconnect -> edit -> fresh filters. Existing rendered reference36PNGs from frozen baseline. Unit harness PNG fallback (no native screen video in headless harness); Android before/after native video and ordered PNGs required. Raw video PTS is not latency evidence.

### 6.3 User-Observable Scenarios
|Scenario|Action|Expected|Evidence / AC|
|---|---|---|---|
|S1 startup|Open own Android copy with3094tasks|Same root/card/fields; faster usable list|profile and ordinary pairs / A4,A5|
|S2 emoji|A->B title, include/exclude, then reconnect|Updated filter and descendants, selection preserved|Wizard4 + affinity14,36PNGs / A1,A2|
|S3 root|status/date/availability including equal finalbool|Same filter refresh and rows|goldens/differential/projection7 / A1,A2|
|S4 lifetime|worker event, error, concurrent/reentrant disposal|Same delivery/termination; no retainedhandler|oracle30+ / A1|
|S5 parent|cold/resume/external edit|5/1/5s and correct freshdata only if measured|separate parent acceptance / A6|

### 6.4 State/interaction matrix
|State|Trigger|Result|Edge|
|---|---|---|---|
|new subscription|Subscribe|initial before attach|initial getter OnError, consumer throw no hook|
|active|named/broad event|per-property changed value on emitting thread|equal ignored; other silently changed field waits own notification|
|active|consumer/getter failure|propagate consumer / OnError getter, detach|no swallowed exception or converted consumer fault|
|active|Dispose/reconnect|detach, fresh subscription reads current|entered callback may finish; Dispose nonblocking|

### 6.5 Decision Ledger
|Decision|Owner|Chosen / confidence|Risk|Needs user|
|---|---|---|---|---|
|continue on emulator|user|explicit instruction /1.0|not Fold6 evidence|No|
|new single event hook instead of failed operators|agent|smaller resource mechanism /.8|behavior/real gain unknown, staged gates|No|
|only3sites/5calls|agent|same bounded hotpath /.95|small gain may fail and revert|No|
|offline admission before Android|agent|new measurable cost evidence /.9|synthetic gain not runtime proof|No|
|no lock around consumer|agent|original concurrent Dispose contract /1.0|use RxAutoDetach and oracle|No|

### 6.6 Runtime/config/data contracts
|Area|Source of truth|Change|Compatibility / verification|
|---|---|---|---|
|tasks|frozen3094file hashes|none|all SHA before/after each trial|
|SDK/APK|pinned10.0.401, AOT2ABI, Avalonia12.1.1|helper+5calls only|fresh sources/inverse overlays/actual decoded10PE+12native manifests|
|measurement|fresh frozen scripts/thresholds, one ownedQEMU|new namespace direct-projection-events-*|runtimeAPI34/3CPU/2560MiB/hostGPU/1080x2400 proofs|
|tracker/delivery|CLI task/lease and exactheadCI|interim results only|readback; draftPR remains open|

## 7–9. Invariants, integration, data
No user/business rule changes. Only INPC subscription construction at3ConnectCore sites changes. Source, cache, root delivery, grouping/sorting, actions, persistence and event-triggered refresh remain unchanged. One subscription owns one direct handler per raw property; raw equality occurs before selector. No new persisted state, migration or public API. Internal helper available to existing test assembly convention. Errors and disposal as§6.

## 10. Rollback
Record previous rollback baseline SHA after local/full review and commit. Local A0/A1 and, after separate advisory A1PASS, the reversible3site integration/A2/A3 localchecks may proceed while remote rollbackCI runs. Candidate commit/push and native source packaging require separately verified previous43 exactCI closure, preventing cancellation of its active workflow. Every new localstage has separate source-bound receipts; oldCI never validates uncommitted helper/integration. If rollbackCI fails, preserve newdelta separately and fix/review its ownedbaseline scope before candidate delivery. This is one tracker task/worktree, not parallel tracker execution. Remove only new helper and3site delta if gates fail; keep useful general original behavior/UI regressions, remove helper-specific tests. Preserve accepted fixes and other concurrent work. No original package/task changes; own diagnostic package only. Failed evidence remains immutable. New rollback gets affected/UI/full/exactheadCI checks and review.

## 11. Acceptance criteria and test matrix
|AC|Required evidence|Failure action|
|---|---|---|
|A0 new allocation premise|Fresh source/DLL-bound Windows probe with exact original B, rejected standardRx R, new direct D; actual woven3094TaskVMs,100warmup, fixed3 measured orders B/R/D,D/R/B,B/R/D; report eachallocation and median pairedD/B<=.65 AND D/R<=.80 for sum root+twoemoji setup; no task writes|no production integration/native trials; re-evaluate bottleneck|
|A1 equivalence|Existing14originalgoldens PASS, new direct variants14 +2differentialscripts +2initialconsumerfailure original/direct +1unsupported-runtime-source OnError (>=33), initial/error/autodetach/equality/delayedotherfield/worker/reentrant/concurrentDispose; resource counts before explicitcleanup|fix/re-review or reject, never weaken golden|
|A2 UI|projection7+affinity14+title2+breadcrumb1+startupUI9+Wizard4 PASS, baseline/current36PNG comparison and full keyframes inspected|fix or reject|
|A3 full|full localHeadless66; new candidate exactCI main expected baseline+19=1591 andHeadless66, no skips; rawTRX/stages/invocation/tree plus AndroidPkg/CodeQL|no native admission|
|A4 instrumented|fixed3pairedpublication wall<=.85 and allocation<=.90; wholeVMwall/alloc<=1.05. Median paired ratios. Existing predeclared noise policy: no allocationhardfail/stabletiming rescue; max5 only if eligible|NO-RETAIN, no ordinary trials|
|A5 ordinary|observer-free ownAPKs, fixed3pairs conservative rootCupper/Blower median<=.85; cardmedian<=1.10,max<=1.25; same invariant/nativeUI checks; extension only predeclared eligibility|NO-RETAIN|
|A6 parent|actual cold<=5s/resume<=1s/freshness<=5s with ordinary flow|stay Task1Active even if bounded mechanism retained|

A0 is allocation admission, not Android latency prediction; record Windows walltimes descriptively, no physical-device claim. Use fresh namespace/snapshots, never mutate old probe inputs. Measure setup only, cleanup outside window, allsubscriptions/VM disposed, no forcedGC. Diagnostic program may compile helper privately before production integration; exact source hash and compiled method checked.

Extension owner: specs/2026-10-16-android-projection-observers.md §11 numerics and frozen DROOT/projection_observer_retention_gates.py SHA256 e4449a6062c3533dd8b57e654475e7f02541c98c7eb879634a648fdb5d5cb705. Eligibility requires MAD(B)/median(B)>0.15 for EACH failed timing metric, with all allocation/correctness/cardmax hardgates passed. Record decision before pair4; run BOTH4C/B and5B/C and re-evaluate all5 using unchanged thresholds. Any hard or stabletiming failure rejects immediately; no extra trials. New gate implementation must match this owner with positive/negativecontrols and be frozen before capture.

For A4/A5 reuse reviewed protocol with fresh namespace: excluded warmupB/C then p1B/C,p2C/B,p3B/C; one continuous ownedQEMU, fresh appPID each, seven stable launcher observations>=60s. No host builds/tests/media during captures. Explicit ownedprocess guards/45mincontroller, root140/UI165/card25/Core180/VM120 deadlines. All3094SHA exact, actual root/selected UUID/0parents/0blocking/24children. Record ordered rawPNG/video, ownexit proof before full strictvideo integrity decode and native-last/fullroot/card inspection. All frozen helper/control/APK/source inputs SHA-bound before first warmup. Missing or incomplete proof fails admission; no favorable replacement trials.

Actual APK proof: baseline helper/calls absent; candidate TaskPropertySignals with exactly5call/callvirt instructions across3sites, property fingerprints Emoji/Emoji/root3 and unchanged other3root sites. Profile targets exclude real MainWindow, so fresh variant overlay inverse must equal exact Gitblob. Fresh Core/VM/Android compiled sources and actual10packageddecodedPE,12native equality. Ordinary must have no diagnostic Probe/instrumentation and use correct normal3sites. Do not copy old helper-specific IL validator without changing required type/name and negative controls. Wrongvariant/malformed/truncated/noncallopcode/wrongproperty cases must reject; positive B/C controls. Candidate builds start only without ownQEMU.

Commands: TUnit dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --treenode-filter '/*/*/ClassName/*' --maximum-parallel-tests 1 --minimum-expected-tests N --report-trx --results-directory fresh. Full Headless same project path tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj with minimum66. Build/CI before native; bounded process tree cleanup; tests only rerun after relevant changes/failure/new concern.

## 12. Риски / expected objections
|Objection|Risk|Mitigation|Disposition|
|---|---|---|---|
|Again the rejected idea|standardRx already insufficient|different resource mechanism, A0 must prove cost improvement vs failed composition before integration|mitigated|
|Lost date/emoji transitions|finalbooldistinct or tuple reread|rawper-propertyDistinct+lastnotifiedCombineLatest; golden14/differential/UI|mitigated|
|Fast benchmark but slow phone|synthetic/profile overhead|A0onlypremise, profile then separate ordinary launch gate, parentseparate|mitigated|
|Unsafe lifecycle or errors|manual eventhook|narrowgettercatch, RxAutoDetach, initialconsumer/concurrentDispose/resource tests|mitigated|
|Wrong APK actually measured|profileoverlaystale|inversebyte+actualPE exactsites, negativecontrols/freeze|mitigated|
|Public completion overclaim|parent remains much slower|CLI/PR distinguish bounded experiment and absolute5/1/5, emulator/physical|mitigated|

Rework checklist: visible flows§6.3; each mappedAC§11; decisions§6.5; objections above; role/review§19; verifiable gates, no preparation mistaken for completion; required evidence feasible with functioning ownAVD.

## 13. План / dependencies
Close previous rollback local validation/review/commit and dispatch exactCI. NewSPEC separate advisoryreview/fix/autoapprove. A0 -> separatelyreviewed A1 -> reversible3site integration/A2/A3 localchecks can proceed with separate source-bound evidence while remote43CI runs. Candidate commit/push waits oldCI separatelyclosed, so it cannot cancel the unfinished rollback run. Separate advisory implementationreview/currentcandidateCI still required, and native source packaging waits actual committedC. Fresh source/APK/control/freeze reviews -> excludedwarmups/manualadmission -> A4 -> onlyifPASS A5. Separately review verdict; retain or scopedrollback, update tracker/PR/spec. Parent completion onlyA6, else select next bottleneck. No parallel tracker execution.

## 14. Открытые вопросы
No user-owned decision. Unknown allocations/behavior/performance are test gates, not assumptions. Baseline43d95e1b recorded. Previous rollback exactCI must pass before candidate commit/push/native packaging. Local implementation/tests use separate receipts and prior localbaseline+A0/A1 gates; remoteCI is isolated from the workspace.

## 15–18. Profile, files, mapping, alternatives
Performance profile: baseline/post sameconditions, wall+allocations, targeted thenfull, significantgainonly, separate commit per optimization item. UI profile/local MUST: tests+actualrenderedstate and nativevideo. Relevant files: new src/Unlimotion.ViewModel/TaskPropertySignals.cs (onehook); MainWindowViewModel.cs (5calls/3sites); TaskProjectionObservationContractTests.cs (parametrize original/direct and differential, preservedgoldens); existing Wizard4 retained, extraUI coverage onlyifactualgap; currentSPEC/executionSPEC result. Private probe/build/capture helpers outsideGit in freshnamespace. Mapping originalexpression -> directrawsignal+sameSelect/CombineLatest. Alternatives: previousoperatorsRejected; acceptedTaskItemVMhook differs contract (no blindreuse); broaderlazy/cachearchitecture has separate risks; changeallroot/emoji sites now unjustified. Narroweventhook+cheapA0 is smallest discriminating next check.

## 19. Quality gate / review
Author linter1–20:1PASS§1outcome;2PASS§2actualsources/traces;3PASS§3premise;4PASS§4;5PASS§5;6PASS§6onehelper;7PASS3sites;8PASSrawDistinct/CombineLatest/initial;9PASSnarrowgettercatch;10PASSA0/A4/A5;11PASSnodata;12PASSsameUI;13PASS§10;14PASSnumericgates;15PASSA0–A6/S1–S5;16PASScommands/nativeprotocol;17PASS§13;18PASSledger/noquestions;19PASSmediumExpanded;20PASS§15profiles. Rubric author: goal5,ASIS5,design5,safety5,testability5,autonomy5=30/30 planningonly.

Role authorreview: Domain PASSplanned samefilters/data; UX PASSplanned samevisibleflows/reconnect; Tester PASSplanned original/differential/resource/UI/full/native; Architect PASSplanned singleinternalhelper/noownobserverframework; Delivery PASSplanned freshproofs/negativecontrols/draft/nooriginals. These author checks do not constitute separate advisory approval.

Post-SPEC review: PENDING separate advisory Scope/Contract/Adversarial/Role passes and fixes/re-review. Scope for reviewer: thisSPEC, prior rejectedSPEC/rawA4, actual3MainWindow sites/acceptedTaskVMhook, retained14contracts, proposedA0/gates/provenance. Depth: boundaries/AC/scenarios/ledger/objections/unsupportedAndroidcausality/errors/races/sourcepayload/rollback. Manual challenge: getter catch accidentally catches consumer exception; synchronousinitialfailure leaks handler; helper body proof doesn't establish actualsitecalls. Mandatory tests/packaging checks address each. No-findings/autoapproval not yet claimed. Post-EXEC notrun; parentopen.

## 20. Журнал действий агента
16Oct03:06 SPEC prepared only; no newhelper/probe/product integration. Prior standardRx experiment is NO-RETAIN, rollback fullHeadless underway. Next separate advisoryplanningreview after rollback localvalidation and commit; implementation depends exactbaseline/approval. No humanquestion needed.

### Post-SPEC final16Oct03:10 — PASS / autoapproved
Separate advisory startup_profile_review inspected thisSPEC, actualMainWindow/TaskVM, retained14goldens, priorrawA4 and source-boundWindowsprobe. Scope/Contract/Adversarial/Role passes PASS after twoMEDIUM fixes and targeted re-review: compiletimeINPCconstraint replacedclass/runtimecast forFody, extensionowner exact§11/SHA+allfailedtimingMAD>.15/hardgates/both4+5. Linter1–20eachPASS; rubric30/30 planningonly. Domain/UX/Tester/Architect/Delivery PASSplanned. Evidence and depth: scenario/matrix/ledger/objections, initial/getter/consumer/reentrant/concurrentDispose, rawDistinct/lastnotified, noAndroidcausalityclaim, actualAPKsites/overlay and rollback/CI dependencies. No-findings justification: bothMEDIUMclosed, no open planning gap; actualbehavior/gain explicitly pendinggates. Reviewer no mutation/tests/ADB, behavioralread-only in writablesandbox. Stop planningPASS, automatically approved under standing userinstruction; no humanquestion.

Previous scopedrollback baseline43d95e1b1aa9fd16816f2617eb4a577047a87b5e committed/pushed after separately reviewed51targeted+66fullHeadless. Productexact62ff. NewSPEC excludedfromrollbackcommit. RemoteexactCI pending; begin privateA0 only, source/DLLsnapshots must bindthisbaseline. A1/productintegration blockeduntilpreviousCIpass. No retainedproductchange/parentcompletionclaim.

### A0 execution16Oct03:17 — preliminary PASS, separate advisory audit pending
Fresh private DROOT/direct-projection-events-probe1:746baseline sourcefiles Gitblob/rawSHA bound43d95e1b; originalB exactWhenAny forms, rejectedR exact00helper exceptclassname, directD proposed Create/INPC helper. Fresh --no-incremental build SDK10.0.402, actual wovenbaselineVM and probe loadedmoduleSHA frozen before execution2 in18immutablecopies/freeze2. Three fixed B/R/D,D/R/B,B/R/D rows use3094actualVMs,100warmup, setup-only processwideallocations, no forcedGC, cleanup outsidewindow/no taskfiles. result2 medianpairedD/B=.1429670466 (gate.65),D/R=.2698524574 (gate.80), exit0/empty stderr. This is Windowsallocationadmission only, notAndroidgain or exclusive attribution. No production integration.

Execution1 exited0 but finalizer found missing loaded localizationresourceDLL inimmutablecopies; rawstdout1/freeze1/snapshot1 preserved, noadmissiondecision recorded. Fresh freeze2/snapshot2 includesnested DLLs before execution2; same source/binary/thresholds, no native trial repeated. Preparationerrorreceipt audit-preparation-failure1.json. Separate advisory source/snapshot/rawrow/gate audit requested; A1 remains blocked on previousrollbackCI and auditclosure.

Separate advisory A0 review PASS nofindings: reviewer rederived nine rawrows/order/ratios, verified746source/Gitblobs/rawSHA,5privateinputs,18snapshots,90runtimeDLLhashes/sevenloadedassemblies, actualcompiledbothhelpers andwovenTaskVM. Rejectedhelper exact00exceptclassname, stdout2/result2 correspondence andempty stderr/build0errors. Source/binary hashes identical acrossfreeze1/2; only localization snapshots added, no mechanism/gate change. Directhelper algorithmscope PASS, behavioral equivalence stillA1pending. Reviewer read-onlyconduct/writablesandbox, no tests/build/ADB. Stop A0 admissionPASS, nextA1 afterpreviousrollbackCIpass; no Androidgain/retention/parentclaim.

Editorial A1 coverage clarification before execution: explicitly add original/direct initialconsumerthrow (2cases) and unsupportedruntime-source OnError (1case), both already required by approved§6 contract but not separately enumerated. Forecast33cases/+19Main=1591; actual rawcounts govern. No productscope/algorithm/gate relaxation. CLI result recorded with exactreadback, Task1InProgress/Active. Initial CLI request rejected invalidArguments because newline is a forbidden controlchar; corrected singleparagraph afterfreshread, no bypass/directtaskfilewrite.

Future pipeline preparer DROOT/prepare-direct-projection-events-pipeline1.py separately reviewed PASS, preparationonly/no actualgeneration/build/AVD. It requires actualcommittedC, carriescurrenthostGPU/provenance/5actualcalls checks, excludesprevioushelper fromnew406/407source lists and removesonlyknownSHA atcheckedshadowpath. PureinmemorygeneratorcontrolsPASS; futuregeneratedoutputs/actualfreezes/APKs stillrequire scopedreview. This preparation does not permit A1/integration before rollbackCI.

### Dependency clarification16Oct03:22 — scoped review requested
Localrollback51+66/source/UI and A0 separatelyPASS. Remote43 CI is isolated from localworktree. Allow A1 unusedhelper/tests afterA0 while remoteCI completes, keeping MainWindow exact43/62 and source/testreceipts separate. Integration/nativepackaging stillwait43CI closure+A1PASS; no use of oldCI to validate newhelper. This ordering does not start another tracker task or relax correctness/performance gates. Targeted reviewer re-review before A1execution; no dependent productintegration yet. Earlier A1wait notes superseded only if this re-reviewPASS.

Separate advisory dependency re-reviewPASS, nofindings: localstatusonlySPEC atreview; unusedhelper/A1 allowed withunchangedMainWindow/separatereceipts, previousCIstillmandatorybeforeintegration/nativepackaging; source/testcountsclarification33/+19=1591valid. Reviewerreadonly/writablesandbox/notests. Clarification autoapproved under standing userinstruction, beginA1. Earlier A1-beforeCI prohibitions superseded bythis reviewedordering; all correctness/performancegates unchanged.

### A1 final16Oct03:25 — PASS; integration still pending rollbackCI
Actual producthelper copiedbyte-exact from admitted A0source; MainWindow unchanged43/62. Retained14goldens now original/direct28 +2differentials +initialconsumer2 +unsupportedsource1=33/33 rawPASS, failures/skips0. Fresh testbuild succeeded; rawTRX SHA d1fdc691... and8immutable source/actualVM/TestDLL/TRX/log copies in direct-projection-events-a1-audit1.json/snapshot1. No AndroidorfullUIgainclaim.

Separate advisory focusedcode/contract/evidence reviewPASS/nofindings: prior30oracle cases preserved withoutweakening (onlyclass/helpername+argumentorder), newinitialconsumer exactexceptionidentity/noOnError/zeroHandlerbeforecleanup, unsupportedsource OnErrorbeforegetter. RuntimeINPC/initial/narrowcatch/AutoDetach/rawDistinct/lifetime implementation matchesSPEC; actualcompiledhelper/testmethods andall8snapshots verified. Source/MainWindow exact43/62, no unrelateddelta. Reviewerreadonly/writablesandbox/noexecution. Stop A1PASS; integration requires separately verified rollbackCI43 andthenA2/A3/currentcandidateCI/nativegates. Parent5/1/5open.

### Local-work/delivery dependency clarification16Oct03:28 — scoped review requested
Remote43CI executes an immutable committed tree; it has no input from this localworktree. Localbaseline51+66/source/UI and A0/A1 already separatelyPASS. Permit reversible integration and local70/UI/full66 during that remote run, with separate source-bound artifacts. Hold candidatecommit/push until43CI separatelyclosed (also avoids workflow cancel-in-progress), native packaging untilrealCcommit and captures untilnewexactcandidateCI. On baselineCI failure preserve ownnewpatch and address its ownedscope first. No requiredchecks/thresholds waived, no shipping/merge/newtracker work; only unrelated wait removed. Reviewer re-review before execution; previous stricter sequencing notes remain historical untilPASS.

Separate advisory focused sequencing re-reviewPASS/nofindings, all delivery/native/currentCI gates retained; local integration autoapproved under standinguserinstruction. Begin3site delta thenlocal70/UI/full66, separateevidence. Candidatecommit/push remainsblocked43CIclosure; oldCI is never newsourcevalidation. Reviewerreadonly/writablesandbox/noexecution. Current§10/13/14 supersede earlier stricter historicalwaitnotes.

### A2 local16Oct03:31 — targeted/UI PASS, full/review pending
Only3MainWindow sites/5Observe calls integrated, source derived exact00sites with helpername/argumentorder adjustment; restMainWindow unchanged. Postintegration70/70 rawPASS, failures/skips0:33contracts+7projection+14affinity+2title+1breadcrumb+9startupUI+4Wizard. ActualcompiledVM CBC8683F... contains1TaskPropertySignals/5actualcall instructions at3sites with propertyfingerprints Emoji/Emoji/root3; remaining3rootWhenAny retained. LocalPEchecker is priorreviewed opcodechecker with onlytype/functionrename; AndroidactualPEproof/negativecontrols remainfuturegates.

All36currentbaseline PNG pixel-equal; root inspected36contactobservations+4fullreconnectinclude/excludepopup/selectedframes; preservedfilters/descendants/card, PNGfallback explicitlynotnativevideo (direct-projection-events-ui-manual-review1.json). Targeted/UI raw+imageproof direct-projection-events-targeted-audit1.json, sourceintegration1.json andlocal-pe1.json. Separate advisory focusedintegrationreviewrequested. Full localHeadless66 running with12minbound/ownedcleanup; previousrollback43CIstillMainRun. No newcommit/push/nativepackaging untilitsclosure, no native/parentgainclaim.

### Local implementation closure16Oct03:38 — PASS, delivery gate pending
Separate advisory integrationreview confirms exact5calls/3sites, same selectors/downstream, helperbyteequalA0/A1, actualcompiledIL/propertyfingerprints/rest3WhenAny, raw70/70 nofail/skips and36byteequalPNGpairs/4fullreconnectframes. Full localHeadless66/66 rawPASS (TRX18293e9f...), actualstatecomplete/exit0/cleanupUncertainfalse; separately parsed and compared to direct-projection-events-headless-audit1.json. Localdecoder controls2positive+7negative PASS: wrongvariant, malformed/truncated, noncalljmp/ldftn, wrongproperty preserving5calls/1,1,3; originalsunchanged/inmemorymutationsonly, checkerexactpriorfunction/type rename. These are Windowsmanageddecodercontrols, notactualAndroidAPKproof.

Post-EXEC focused Scope/Contract/Adversarial/Role passes locallyPASS: thisSPEC/A0/A1/2productfiles/contracttest/raw70/full66/renderedPNG/localIL. Domain/UX currentfilters/relations/status/date/reconnect/card intact inactualtestedflows; Tester originalanddirect/lifetime/errors/currentUI/full evidence; Architect smallinternalonehook/noAPIorstoragechange; Delivery old43CIisolated/draft/nooriginals/no prematurecandidatepush. Depth includes scope/AC/scenarios/matrix/ledger/objections/unsupportedAndroidgain/edgecases/comments/sourcepayload/manualchallenge. No-findings justification: requiredlocalproofs inspected, sourceconfined andallscopedfindingsclosed; A3remote/currentCI/A4/A5/A6 remainexplicitmandatorygates, notacceptedrisks. Reviewerreadonlyconduct/writablesandbox/noexecution. Stop localPASS only; candidatecommit/push awaits43CI raw/root/tree separate advisoryclosure, no retainedmechanismorparentcompletion yet.

CLI interimresult recorded inRussian via installed1.33 shim, exactreadback verifies InProgress/Active, originaluserdescription/criteria/relations/importance/dates/repeater preserved (direct-projection-events-local-cli-readback1.json). Raw/privateevidence names given, newsourceuncommitted, nextCI/native/5/1/5 boundaries explicit. Previous43Main teststepSUCCESS00:35:41Z, Headlessstillrunning; AndroidPkg38827885037/CodeQL38827885041 exact43success attested, no wholeCIclaim yet.

### Review isolation correction / author adversarial fallback16Oct03:43
Area review-claims, MEDIUM: writable reviewer cannot be labelled independent under owner review-loops. Fixed currentSPEC terminology and explicit runtime disclosure; request targeted advisory re-review of correction. Actual reviewer evidence remains useful, but no technical read-only guarantee is asserted. Residual risk: permissions did not prohibit mutation; source/snapshot/DLL hashes, Git scope and actual read-only tool history provide evidence, not enforced isolation.

Author's separate adversarial fallback scope: approvedSPEC/current staged2productfiles+contracttests, original14 and old30 oracles, A0/rawnine/snapshots, A1/raw33, integratedraw70,36renderedpairs/4fullframes, fullraw66, localIL/2+7negativecontrols, CLI readback. Contract pass: same three sites/selectors, per-property last-notified equality/CombineLatest, initial-beforehook, getter-only catch and consumer exceptions outside catch; no schema/command/storage/scheduler delta. Counterexamples challenged: nonnullemoji false->false, date true->true, silently modified otherproperty, null/empty notification, mutation inside initial delivery, getter faults beforecleanup, initial consumer failure, reentrant/concurrentDispose. Source/actualtests cover each; raw33/70/full66 and unchangedPNG corroborate requiredlocalbehavior. Actualcalls propertyfingerprints prevent wrongoverlay/ldftn/jmp/fivewrongproperty admission. Domain/UX/tester/architect/delivery roles and S1–S5/A0–A6 checked: S1Android/A4/A5 and S5/A6 pending, no boundedretention or parentPASS. No unrelated source/unsafe original-data mutation found; currentCI does not validate newlocalsource. Stop authorfallback localreadinessPASS, findings0 after thislabelcorrection; remoteCI/actualAPK/native/parent mandatory gates remainopen. Future capture/sourcefreeze gets advisory review plus adversarial fallback with thissame disclosed boundary.

Targeted advisory re-review oflabelcorrection/authorfallback PASS, findings0 after3LOW accidentalnonreviewwordings fixed (streams, ordinarygate, separatecommit). No algorithm/gatechange. Baseline43CI nowSUCCESS1572+66; rawroot/stages/invocation/tree authorauditPASS, separateadvisoryCIclosure pending. Snapshot/source/reference/controls unchanged sincepassedlocalchecks. Candidatecommitnotyetcreated.

Baseline43 finaladvisoryclosurePASS16Oct03:45: actualraw1572+66/no skips, stage/invocation/attempt/cleanworktree/head/checkoutb367tree53ccc==43head viaGET, exactcompanions; nofindings. AuthorfallbackPASS. Deliverydependencyclosed, current6files reviewed/local70+full66 support scopedcandidatecommit/push. Native/currentcandidateCI/retention/A6 stillpending. Next: immediatelypreparefreshsourcepipeline from actualC whileclean, then build/proveAPKs; never use43CI asCvalidation.
