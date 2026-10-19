# Android: разделить стоимость базового запуска App

## 0. Метаданные / 1. Outcome
Expanded по canonical C:/Users/Kibnet/.codex/agents/templates/specs/_template.md: native install в отдельный диагностический пакет, multi-module source/build provenance. Owner Codex, Task1 e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6, branch perf/android-e74c5d1a-task-loading, HEAD2afd4ab29632152789102c132244323d738933ad. Surface Codex/local Windows, actual model hidden/unverified; model eval неприменим к native timing. Central stack: routing/core behavior/tool/QUEST/review/linter/rubric, performance-optimization, dotnet-desktop-client, task-context/result-tracking и локальный UI override. User разрешил emulator, последовательное исполнение и autoapproval после review/fixes. Оригинальный пакет/телефон не трогаются, релиза нет.

Success: на неизменном product source определить, какие App stages входят в Android application.base.OnCreate и activity.base.OnCreate, сохранив source→APK→PID→trace цепочку и task bytes. Это diagnostic baseline, не оптимизация и не acceptance cold5/resume1/freshness5. Output: private overlays/build proof/one native cold trace/full PNG/root observation/cleanup receipt/analysis; tracker interim result. Stop after one complete cold process; no performance candidate, paired comparison or unchanged retry. При trace/native failure сохранить partial и INCOMPLETE, выбирать следующую работу по покрытым данным.

## 2. AS-IS / 3. Проблема
Existing B43 PID12296 profile: application.base.begin913766641000→end919299499000 при1GHz =5.532858s; processCPU≈4.603s,47MB allocations. Builder nested~.130s; core services~.232s; activity.base~.572s. Core storage Init26.821s/26.998s, initialgraph12.800/11.961, models5.721/6.112, publication6.927/7.351. Не paired gain и не ordinary lowerbound: observer/JIT/emulator и missing preFirstMark сохраняют ограничения. Windows preInit N10 не локализовал Android base. Проблема этого шага — непрозрачная стоимость внутри базового bootstrap, а не доказанная причина XAML.

## 4. Design goals / 5. Non-Goals
Reuse proven bounded Core/VM/Android observers and separate package. Only extra Mark calls in private App.axaml.cs overlay, no new product behavior/serialization/cache/watcher/lifecycle decisions, no data migration, network or task edits. No broad framework upgrade/font/style removal, no default changes. Ordinary CI/tests already passed exact2afd; unchanged product suites не повторять без delta. UI test requirement satisfied by retained existing coverage; diagnostic must not claim new UI fix. Native rendered root checked; no new visual layout design, before/after UI video неприменимы к неизменному product.

## 6. TO-BE / responsibilities
Private android-app-bootstrap-profile1 inherits frozen baseline43 overlays/storage deadline/VM and Android observer. Core StartupPhaseTrace already supports4096bounded records/process CPU/allocated, async close; App uses that same assembly observer, no cross-assembly import. Add unique markers only, stripping insertions restores exact original App bytes. Mark around Initialize begin/scheduler/XAML/localized resources/culture hook/theme/font/DialogHost/end; InitializeRuntime entry/completed; GetMainWindowViewModel entry/first noncached return; OnFrameworkInitializationCompleted entry/final and Android-specific GetVM/CreateMainView/InitializeStartup dispatch boundaries. No try/catch reordering, no changed returns/await/scheduling/lambdas. Error/early return lacks paired end and is INCOMPLETE for that stage.

Core closes after storage Init or fixed180s deadline; additional early markers remain in same trace. Android and Core use Stopwatch domain/frequency/PID; inspector must verify same PID/frequency and ordering before containment attribution. Nested inclusive durations must not be summed; compute measured children and uncovered intervals; process counters overlap and include observer/runtime threads. Inter-marker attribution excludes unmeasured pre-first-mark and sampling overhead. Bound observer additional40events, original4096cap; drop/expired/missing footer invalidates full diagnostic.

Publisher fresh namespace, exclusive existing shadow lock, frozen checkedFiles restored to shadow only, pinned installed SDK10.0.401/disable (verified present), unchanged AOTtrue/arm64+x64/package org.unlimotion.taskloadingbench/nondebuggable/extractNativeLibs. Product HEAD2afd source equal43. Source snapshot all product files, overlays/targets/helpers and pre/post SHA retained. Finalizer must verify actual packaged managed App contains new phase strings/calls, Core/VM/Android observers, native payload/ABIs/package/config; hashing inputs alone insufficient. Fresh build output, bounded clean5m/publish40m/proof60s each, own child cleanup. No emulator/media/timing during build.

### 6.3 User-Observable Scenarios / 6.4 State
| Trigger | Expected | Evidence / AC |
| --- | --- | --- |
| One cold diagnostic launch, own emulator | Existing Loading→root with same fixture, no layout change; timings diagnostic only | A3 trace +fullPNG, A4 |
| XAML/runtime failure or missing trace | Partial output retained, INCOMPLETE, no gain or successful root claim | A2/A3/A4 |
| Finish/timeout | Own app and QEMU exit, task3094SHA unchanged | A3/A4 |

Prepared→reviewed→autoapproved→frozen overlays/controls→build/proof→ownAVD admission31→single cold launch→trace/root collection→cleanup→offline analysis. Empty/missing settings not a new app flow: use known offline fixture, backup/git network disabled. Any concurrent unexpected emulator/build/source drift fails admission. No warm resume, no edits/card mutation, no destructive task reset; only own diagnostic sandbox installation/restoration as prior harness.

### 6.5 Decision Ledger
| Decision | Owner/chosen | Confidence | Risk / needs user |
| --- | --- | ---: | --- |
| Profile bootstrap before next optimization | agent, existing5.533s interval | .85 | Emulator limitation; no |
| Reuse observer + mark-only overlay | agent | .9 | Small observer cost retained; no |
| One cold run, no gain threshold | agent | 1 | Diagnostic cannot establish performance improvement; no |
| Autoapprove after review/fixes | user explicit | 1 | No further confirmation |

## 7. Data / 8. Interfaces / 9. Errors
Input private copied Fold6 fixture3094 with immutable directory-worker-fixture-task-sha256.json. No task content in public outputs. Preserve original user settings/tasks; diagnostic package only. Pin exact ADB, own QEMU process+commandline identity, port5580/5581/AVD codex_loading_profile_20261013, engine37.2.12, API34/x64/3CPU/2560MiB/1080x2400/hostGPU/qt-hidden. Admission confirms launcher stable60s, source/APK/runtime SHA and independent watchdog before launch. Same existing ownAVD parent boundary31, no touching foreign devices/processes.
Install/init/one cold→root wait≤180s; guest command timeouts and total collection≤400s, boot≤180s, cleanup≤200s; independent watchdog45m protects app/QEMU. Raw stdout/stderr retained on nonzero/timeouts. Benign am start current-task warning not used to abort opaque parsing; any success admitted via exit/result/PID+trace+root state, not stderr alone. Failure ends this diagnostic, retains pulled outputs and no retry. Exact task inventory/SHA before+after, migration.report excluded and explicitly not whole-directory equality.

## 10. AC / 11. Acceptance-to-Test Matrix
| AC | Test/evidence | Stop |
| --- | --- | --- |
| A1 mark-only product preservation | reverse every insertion to original exact bytes; original/source snapshot SHA; no tracked src delta; actual DLL strings/calls +same baseline observers | any mismatch |
| A2 actual collector/oracle | existing actual observer harness normal/overflow and overlay/trace validator controls: missing/duplicate/out-of-order/PID/frequency/drop/expired/footer/containment | no native admission before PASS |
| A3 native bounds/provenance | pinned ownAVD31 runtime, one fresh PID/launch, full trace flush with expected app stages, root PNG inspected, raw partial retention, app/QEMUexit3094SHA proof | missing required evidence→INCOMPLETE |
| A4 honest disposition | measured stage table +uncovered/overlap/observer boundaries; postEXEC review; CLIresult/readback | no product/Android gain or parent completion |

Commands: private prepare script, absolute pinned dotnet for harness/build/proof, ownAVD launcher followed by existing guarded collection adapted to this fresh profile, then offline Python analysis. Relevant source-only controls run before any APK; no unchanged product full rerun. No new test framework/library. Build scripts reviewed actual source before running. APK package changes only disposable diagnostic namespace as already authorized.

## 12. Rollback / 13. Risks / 14. Alternatives
Rollback: no product code installed, stop own app/QEMU and leave frozen diagnostic evidence. Product diff remains empty. Risks: instrumentation shifts timing, one run not representative, XAML/AOT calls include JIT/type init, shared CPU counters overlap, emulator differs Fold6, bootstrap may not dominate final cold time. Report all. Static-only inspection insufficient to assign5.533s; full CPU sampling/new profiler excluded before mark decomposition. Rejected modern reader experiments remain closed. No unchanged repeat of failed surface diagnostic.

## 15. Expected User Review Objections
| Objection | Response / proof |
| --- | --- |
| Another experiment without result | One bounded run decomposes previously opaque5.533s; select next change from observed dominant span, not new serializer guess |
| Marks altered behavior | Reversible insertion-only source and packaged method proof; timing observer boundary explicit |
| Root means ≤5s | Root proves usable render only; no absolute/paired/performance acceptance |
| Tasks could change |3094exactSHA/readback/own package; migration.report separately excluded |

## 16. Files / 17. Decisions / 18. Validation scope
Tracked only this SPEC. Private overlays/scripts/receipts/builds. No public API/config migration/changelog/product change in this diagnostic. All user-owned decisions already authorized, no open equal-choice ambiguity. Parent still Active/PR325draft; Task2/Task3 not started.

## 19. Review result
Author planning Scope/Evidence:39existing Android records, actual App.Initialize/InitializeRuntime/GetVM/OnFramework/MainActivity/targets/publisher/finalizer/Core observer read. Contract: A1-A4 cover source binding, native proof and tracker. Adversarial: missing end/clock domain/dropped rows/observer cost/orphan process/task drift invalidate corresponding claim; preserve partial. Roles Developer instrumentation-only/source reversal, Tester actual observer+validator negative controls/native state, UX unchanged root PNG, Operations exact own process+watchdog+APK/task SHA; Business workflow no new rule. Author linter20 coverage complete/rubric30/30 planning; advisory review pending. Writable reviewer will be advisory, author fallback separately, no technical read-only isolation claim. No EXEC before review/fixes/autoapproval.

## 20. Journal
19Oct SPEC prepared from preserved measured Android interval after two closed JSON feasibility rejections. Next: focused separate planning review, fixes, autoapproval, actual-source helper review/controls, then one bounded diagnostic. No source/native change yet.

Planning clarification from inspected App constructor2123 and pinned Avalonia e33eaed9c106846b200680751022385d9cc5dc6f AppBuilder.SetupUnsafe: factory precedes Initialize. Include constructor-body begin/end, keeping base/field-initializer cost uncovered; Runtime checkpoint marks after existing settings/localization/mapper/storageFactory/backup/initialStorage/Git/completed log calls. Total new markers≤40; no new observer or control flow. Framework source supports ordering but is not timing evidence.

Author expanded linter evidence:
| № | Result | Basis |
| --- | --- | --- |
|1|PASS|Diagnostic stage table/output, no parent acceptance|
|2|PASS|Actual PID12296 records +source callbacks inspected|
|3|PASS|Uncovered Android base interval|
|4|PASS|Mark-only/reuse/bounds|
|5|PASS|No product behavior/gain/release|
|6|PASS|App overlay/observer/publisher/collector responsibilities|
|7|PASS|Same assembly Core observer +APK import guards|
|8|PASS|Exact-byte inverse,unique labels,PID/frequency containment|
|9|PASS|Partial retention/INCOMPLETE/independent cleanup|
|10|PASS|Diagnostic wall+process counters,overlap/observer caveat|
|11|PASS|Immutable3094task inventory/SHA|
|12|PASS|No model/settings migration; SDK/ABI pinned|
|13|PASS|Own app/QEMU exit,product unchanged|
|14|PASS|A1-A4 explicit success/failure|
|15|PASS|Actual observer +negative trace/source controls/native PNG|
|16|PASS|Commands/budgets/fresh namespaces/no retries|
|17|PASS|Review→freeze→build→admission→native→cleanup→audit|
|18|PASS|Decision ledger/no user-owned unknown|
|19|PASS|Expanded native/multi-module proof risk|
|20|PASS|QUEST/performance/UI boundary/CLI lifecycle|

Rubric5/5/5/5/5/5: bounded outcome; measured AS-IS; explicit markers/provenance; own sandbox/taskSHA/rollback; A1-A4+negative controls; authorized choices. This is author planning readiness, not executed validation or advisory closure.

Planning MEDIUM fixes (supersede ambiguous sentences above):

1. Core observer has no sampleEndTicks. All Core inter-mark intervals are inclusive wall with unknown/unseparated Gate/sampling cost; no numerical subtraction or "observer excluded" claim. Android sampleEndTicks gives only its own partial sampler cost. Process CPU/allocation overlap remains explicit. Uncovered means no finer marks, not zero observer overhead.
2. Keep mark-only design. Core180s deadline starts at existing storage init.begin, not App entry. A hang/exception before that may produce NO Core file, losing early marks; only raw log/PNG/Android flushed prefix may survive. Report early-bootstrap INCOMPLETE and retain available artifacts; no guaranteed early Core snapshot. No new deadline/flush invocation. Outer host/ownAVD watchdog bounds entire run, not Core completeness.
3. Exact phase topology below:35 unique source calls (32 on normal Android first-instance path),≤40; no loops/new control flow. Validator separates app-prefixed records from unchanged storage Core subsequence. Enforce original VM.storage.init.begin<=Core.init.begin specifically, never VM<=new early Corefirst. Keep original storage order/count/error/drop/footer checks on filtered storage subsequence; full Core record monotonicity/PID/frequency/session bounds apply to ALL records. App and Android containment checked only with same PID/frequency and valid monotonic sessions. Missing expected phase rejects full diagnostic; early/cached/error paths are explicit incomplete/alternate classification, never silently discarded.

| Method | Exact new phase suffixes (all prefixed app.) in call order | Topology |
| --- | --- | --- |
| App ctor | ctor.begin,ctor.datacontext.end,ctor.end | runtime.begin…runtime.end nested between last2; base/field init before ctor.begin uncovered |
| InitializeRuntime | runtime.begin,runtime.settings.end,runtime.localization.end,runtime.mapper.end,runtime.dialogs.end,runtime.storagefactory.end,runtime.backup.end,runtime.initialstorage.end,runtime.git.end,runtime.end | normal10; failed TryLoadSettings has no settings.end/end; existing catch branches emit runtime.recovery OR runtime.error then no runtime.end |
| Initialize | initialize.begin,initialize.scheduler.end,initialize.xaml.end,initialize.localized.end,initialize.culture.end,initialize.theme.end,initialize.font.end,initialize.dialog.end,initialize.end | normal9; consecutive stages inclusive; no exception wrapping |
| GetMainWindowViewModel | getvm.begin,getvm.cached OR getvm.end | first normal begin/end; cached begin/cached is alternate, not first full result |
| OnFrameworkInitializationCompleted | framework.begin,framework.getvm.begin,framework.getvm.end,framework.mainview.begin,framework.mainview.end,framework.startup.begin,framework.startup.end,framework.end | normal Android8; getvm begin/end nested in framework.getvm interval; Core Init may start after synchronous startup dispatch before framework.end, so no false strictly-nonoverlap assertion |

InitializeRuntime source has12calls, ctor3,Initialize9,GetVM3,framework8 =35. Normal sequence32. runtime.settings.end after original TryLoadSettings guard, alternate failed return unpaired. Runtime error/recovery are diagnostic labels only and do not change exception propagation. Android base contains ctor/Initialize/framework normal intervals; Android activity.base may instead contain framework depending exact lifetime implementation: inspector reports actual containment, never assumes which without timestamp proof. Cross-module worker events may overlap; monotonic record order within serialized observer is required, not global no-overlap.

Source preflight:406frozen baseline files verify SHA, tracked src diff43→2afd and working src diff both empty.344working-tree files (including App.axaml.cs) differ in bytes only by CRLF checkout normalization. Source truth for overlays/build is immutable git-blob snapshot with its own exact SHA; strip markers restores that frozen original exactly. Do not compare LF overlay to CRLF working file as byte-equal or rewrite working files. Separate clean git/source-tree identity proves logical current product source; record newline normalization boundary in build proof.

PostSPEC closure: separate advisory PASS after3MEDIUM fixes; Scope/Contract/Adversarial/Developer/Tester/UX/Operations passes and linter20/20/rubric30/30 planning inspected. Author fallback agrees, no open mandatory finding. Reviewer writable/read-only conduct, no technical isolation. Automatically approved under explicit user instruction; private EXEC allowed, actual helpers/controls require focused review before native admission.

Prebuild closure: preparation1 ambiguous anchor stopped before App write; preparation2 misplaced framework.end rejected by syntax, neither built/launched. Retained failed namespaces. Actual profile3 mark-only overlay exact inverse35, syntaxPASS;9actual source guard controlsPASS,4actual observer earlyprefix modesPASS,25validator controlsPASS including storage/framework interleaving. Separate advisory+author fallback prebuildPASS after fixing sourcebinding: pinned manifest SHA+original/overlaySHA+exact35-line inverse run before build and finalization. App original frozen gitblob equals2afd; CRLF boundary retained. Starting profile3 SDK10.0.401/AOT build. Native acceptance not admitted.

EXEC build/proof: profile3 complete, SDK10.0.401/AOTtrue/arm64+x64; known native page-size/duplicate packaging warnings,12 native payload hashes remain baseline-identical. Final APK SHA2564EAE0DEDDEB36CCAA3654DE681CDE7F146E40DEEAAFAD93C21BC11080701F44E. Actual extracted payloads10managed+12native; both Unlimotion ABI DLLs show35Mark calls in5methods/order matching source manifest; author package auditPASS. No product src delta. Native helper actual review fixed2MEDIUM: metadata write failure cannot abort subsequent cleanup attempts; fresh identity guards after install/installedSHA and after fixture-before. Controls2 current23PASS including save-failure tests; validator25 unchanged, observer4/source9PASS. Budget clarified: preflight240s, capture400s (fixed30/90s PNG), cleanup200s, outer collector900s, boot180s, independent45m ownAVD watchdog. Parent Active, native31 not yet launched; final package advisory/admission freeze pending.

Native31 final: package/nativeplan advisory PASS,49input admission freeze, qualified own QEMU111952/API34/x64/3CPU/2560MiB/1080x2400, stable launcher7checks≥60s, installed APK SHA matches. One cold PID4276 launched. Collector then failed beforefirst30s capture: module-level loop variable checkpoint overwrites def checkpoint, first guard's ADB metadata checkpoint raises TypeError. Same collision also aborted inner cleanup. Raw metadata is partial: empty cleanupErrors does NOT prove app stop/fixture-after; no new App trace/root PNG/performance conclusion. Outerfinally worked: exact owner exit proof QEMU111952 absent, no own emulator remains.3094 fixture-before verified; after not obtained. app-bootstrap-native31-final-disposition1.json records honest INCOMPLETE. Attempt31 closed, never restarted/extended. Author acknowledges missed global-scope collision in AST-only function controls. Product unchanged; new prospective collector correction/review gate below required before any fresh diagnostic.

Prospective recovery scope: keep same approved APK4EAE…44E and all35App markers/analysis/thresholds, replace only collector module-loop target checkpoint→checkpoint_seconds. Preserve failed collector1/native31/allraw. Newcollector2/diagnostic2 and ownAVD32 namespaces; freshguard/admissionfreeze. Add actual whole-loop execution control with fakes that reproduces collector1 TypeError and executes both collector2 checkpoints, plus static global callable-name rebinding control (including explicit injected collisions). Re-run affected23 guard controls against collector2/owner32; observer/source/validator/package unchanged, no rebuild or repeated successful suites. One fresh cold launch32 only after advisory fix/re-review + user-authorized autoapproval. This is a changed, cause-specific helper correction with new output, not extension/reinterpretation/retry of failed31. Budgets/ownPID/watchdog/taskSHA/rawretention/root review/A1-A4 unchanged; fixture-before32 will additionally provide delayed taskSHA verification after31, explicitly timestamped, not retrospective proof at31exit. If32 fails, close it INCOMPLETE without unchanged rerun.

Recovery postSPEC author Scope/Contract/Adversarial/roles: precise actual TypeError and bare-module Python loop binding; unchanged product/package; new whole-loop control closes omitted integration boundary and static checker prevents callable shadowing; Operations keeps outerfinally exactQEMUexit. No new UX/business rule or user-owned choice. Existing20linter/30rubric contract remains, affected acceptanceA2/A3 updated above; separate advisory review pending. Native32 not yet admitted or started.

Recovery planning closure: separate advisory PASS for honest INCOMPLETE31 and prospective changed-helper32 plan, author agrees. Autoapproved under explicit userinstruction. Prepared collector2 by AST-verified alpha-renaming only inside actual observation loop, plus fresh namespaces/owner32; old49input sources remain unchanged. Actual code/control review and admission still required before32.

Recovery code/control closure: separate advisory PASS, alpha-only loop rename/freshnamespace32 verified; whole-loop10controls reproduceoldTypeError and bothnewsyntheticcaptures,6callablecollisionnegatives; guard23currentPASS. Low stale docstring31 retained as nonbehavioral residual; actualowner/runtime32 guarded. Nativeadmission2 freezes53inputs, sameAPK; onefresh32 is now admitted under reviewed recovery scope. Original31closedINCOMPLETE/raw preserved. No performance/native outcome claim yet.

## Диагностика32 завершена19Oct

Один холодный процесс4258, собственный QEMU163280. Все orchestration stages exit0, длительность collector216.135s.53admission и12collector inputs SHA неизменны. Core246records =32App+214storage, VM154, Android39; PID/frequency1GHz/полные footer/drop0/expiredfalse/chronology/containment проверены.3094task names+SHA точны до и после, собственное приложение остановлено; отдельный exactQEMUexit proof подтверждает отсутствие процесса. После31 это поздняя новая проверка fixture-before32, а не восстановленный proof на момент31exit.

Оба исходных PNG1080x2400 просмотрены автором и отдельным advisory reviewer: checkpoint30 показывает Loading, checkpoint90 — заполненный root list безLoading. Это две точки наблюдения, не полный UI flow, непрерывная отзывчивость или момент readiness.

| Измеренный интервал | мс | Граница |
| --- | ---: | --- |
| Android application.base begin→end |5387.1709|inclusive весь базовый вызов|
| App ctor begin→end |1784.5110|вложен в application.base|
| Runtime begin→end |1541.2827|вложен в ctor|
| Runtime localization.end→mapper.end |588.7352|вложен в Runtime|
| Runtime dialogs.end→storagefactory.end |805.3066|вложен в Runtime|
| App Initialize begin→end |283.7931|вложен в application.base|
| scheduler.end→xaml.end |261.2762|вложен в Initialize|
| Framework begin→end |2563.1706|вложен в application.base|
| Framework GetVM begin→end |901.7180|вложен в Framework|
| Framework MainView begin→end |1643.3690|вложен в Framework|
| Framework startup dispatch begin→end |17.2447|вложен в Framework|
| application.base минус непересекающиеся ctor/Initialize/Framework |755.6962|неразмеченный inclusive остаток; не чистый framework CPU|
| Core init.begin→init.end |25718.3743|только инициализация storage, не cold readiness|
| Core init.begin→header.closedTicks |25718.5115|другая правая граница, не подмена init.end|
| VM storage.init.begin→end |25719.4960|внешний await interval|
| Core initialgraph begin→end |12214.4733|processCPU13507.828ms/118104528allocated|
| Core models begin→end |4699.3096|processCPU7805.626ms/248630304allocated|
| Core cache.publish begin→end |7388.3805|processCPU7681.312ms/542170896allocated|

Вложенные интервалы не складываются повторно; CPU/allocation process-wide и перекрываются с другими потоками, inter-mark wall включает неизвестную стоимость Core sampling/Gate. Один instrumented emulator run не устанавливает paired gain, ordinary cold≤5/resume≤1/freshness≤5 или результат на Fold6. Основной остаток — initial graph/models/publication; XAML0.261s не объясняет storage25.718s. Следующее решение требует детализации конкретного storage hot path, а не изменения стилей.

PostEXEC advisory PASS и author fallback: Scope/Evidence сверили raw49/53source bindings, выводы/PNG/manifest/sourceproduct; ContractA1-A4 закрыты для diagnostic32, не для parent; Adversarial проверил nested sums, storage endpoints LOW исправлен точными границами, delayed31verification, partial31metadata, no failed-case reuse; роли Developer/Tester/UX/Operations подтверждены перечисленными rawартефактами. Нет открытого обязательного finding для завершённой диагностики. Writable reviewer/read-only conduct, не техническая read-only изоляция. app-bootstrap-native32-final-disposition1.json фиксирует результат. Task1 Active/PR325draft, goals5/1/5 открыты; product remains2afd/43, merge/Task2/Task3/release не выполнялись.
