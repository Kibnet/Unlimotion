# Android startup: отложенное создание команд редактора

## 0. Метаданные
- Профиль: delivery / performance / UI, medium; владелец Codex.
- Parent: e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6; та же первая task branch/worktree, PR325. Следующая tracker-задача не начинается.
- Поверхность: Codex desktop, Windows PowerShell; exact model/runtime ID не предоставлен; model eval не применим.
- Зависимость EXEC: закончить оценку EGL12.1.1 candidate и выбрать проверенный baseline; текущая native10-case серия ещё идёт. Никаких сборок/тестов на measurement host во время её выполнения.
- Private evidence: `D:/CodexArtifacts/android-task-loading-20261012` (DROOT). Parent cold5/resume1/freshness5 остаются обязательными и не заменяются этим microfix.

## 1. Overview / Цель
Загруженная задача должна редактироваться/сохраняться/перечитываться как прежде, но unopened tasks не должны заранее создавать Save/Reload ReactiveCommand и связанные command-only subscriptions. Результат — узкий эксперимент, принятый только при измеренном выигрыше и сохранении обычных UI действий; при no-retain вернуть production code до следующего эксперимента.

Outcome: уменьшить стоимость построения task view models, затем продолжить parent до его абсолютных порогов. Stop: потеря autosave/reload/error/teardown semantics, неподтверждённые версии APK или отсутствующее визуальное evidence блокируют retain. Само наличие lazy getter успехом не считается.

## 2. Текущее состояние (AS-IS)
TaskItemViewModel.Init создаёт SaveItemCommand, ThrownExceptions subscription, ReloadTaskCommand с ObserveProperty(CanReloadTask), его error subscription для каждой задачи. В репозитории SaveItemCommand — public field, заменяется контролируемыми командами в fixture lifecycle tests; Reload — public get/private set property. Date/duration/archive/criteria команды уже используют GetOrCreateCommand и deferredCommandLifetime.

Сохранённый qualified profile12.0.3 baseline pair1:3094tasks, models16.556s/226.7MB allocations, cache.publish17.843s/545MB, status7.943s/livegraph18.896s. Это исторический профиль, а не новый12.1.1 A/B. Обычная candidate12.1.1 coldN1 list upper78.241s — parent5s не выполнен. Перед сравнением получить актуальный profile выбранного baseline.

## 3. Проблема
Команды редактора создаются для всех загружаемых задач до первого использования. Это проверяемая гипотеза лишних allocations/CPU; она не объясняет автоматически все78s startup.

## 4. Цели дизайна
Использовать существующие command lock/lifetime patterns, сохранить exception routing и editor revision tracking, первый реальный edit/reload и dispose. Не менять storage/граф/формат задач.

## 5. Non-Goals
Lazy task DTO/relations/projections, persisted cache, изменение миграций, переписывание autosave/статусов/Repeaters, возврат отклонённого directory-worker experiment. Пользовательские task2/3 остаются отдельными следующими задачами.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Responsibilities
TaskItemViewModel содержит nullable owned backing commands, lazy factories и существующий GetOrCreateCommand. Startup storage не меняется. Tests проверяют реальные actions/errors/lifetimes; external profile и ordinary APK подтверждают стоимость.

### 6.2 Design / visual planning
SaveItemCommand становится source-compatible get/set property: getter под существующим commandInitializationLock создаёт прежний command body; factory включает прежний error subscription в deferred lifetime. Setter сохраняет возможность injection borrowed command без смены ownership и без принудительного создания предыдущего command. Не добавлять observable notification ради этой технической замены. Reload getter аналогично создаёт прежний CanExecute/error handling только при обращении. Убрать eager creation и старую eager lifetime registration из Init; не менять прочие reactive subscriptions или сохранение pending edit revisions.

First access после VM teardown должен вернуть уже disposed owned command согласно существующему GetOrCreateCommand pattern и не записывать данные/не запускать реальный reload producer. Concurrent first access создаёт один экземпляр; UI bindings/VM autosave используют один command. Сохранить lock ordering: ExecuteSaveCommand уже держит pending-save lock перед getter; factory под command lock не берёт pending-save/editor locks и не запускает execution. Borrowed replacement не удаляет ранее созданный owned command из lifetime: тот остаётся до teardown. Field→property не должен добавлять Fody notifications; использовать явный DoNotNotify для технического Save property. Repository callers пересобираются вместе; public field→property меняет binary shape внутренней VM assembly, поэтому поиск repo/reflection consumers обязателен. Непредусмотренная внешняя ABI/API зависимость → новый reviewed design до EXEC.

Storyboard: список → первая открытая карточка → изменить Title → дождаться реального persisted значения → внешнее CLI/storage изменение → первый Reload → актуальная карточка; при save failure видна исходная ошибка, retry/reload работают; при закрытии pending write корректно дожидается/блокируется как прежде. Layout/тексты не меняются. Before/after rendered PNG/video выбранного сценария; Headless video fallback допустим только по существующему renderer ограничению с inspectable PNG+trace, native ordinary video отдельно.

### 6.3 User-observable scenarios
| Scenario | Trigger | Visible result | Evidence | AC |
| --- | --- | --- | --- | --- |
| Обычная загрузка | Start own APK | Root list и та же card доступны | Native list/card/video | A3 |
| Первый edit | First opened card, changeTitle | Изменение сохранено один раз без потери revision | UI test + storage read-back | A1 |
| Первый Reload/error | External change / save error | Актуальный Title или прежняя error/recovery semantics | UI reload/error cases | A1 |
| Закрытие | Dispose с pending save, затем late access | Pending producer semantics сохранены, новых writes нет | Fixture lifecycle/teardown tests | A2 |

### 6.4 State matrix
| State | Trigger | Result | Error/concurrency |
| --- | --- | --- | --- |
| No command | Getter/first autosave | One owned command, errors routed | Concurrent getters same instance |
| Owned initialized | Subsequent use | Existing command/body | Save/reload failure unchanged |
| Borrowed injection | Setter | Caller command used | Caller ownership retained |
| Disposed VM | Late getter | Owned command immediately disposed | No task write/reload producer |

### 6.5 Decision ledger
| Decision | Owner | Chosen | Confidence | Risk | Needs user |
| --- | --- | --- | ---: | --- | --- |
| Scope | agent | Only Save/Reload, existing deferred pattern | .9 | Startup gain may be too small | Нет |
| Preserve borrowed replacement | existing contract | Setter compatible with fixture lifecycle | .95 | Ownership regression | Нет |
| Measurement endpoint | agent | Actual usable-list bounds; card separate mandatory functional action | .9 | Earlier scripted-card interval was wide | Нет |
| Parent outcome | user | Original5/1/5 unchanged |1| Microfix not completion | Нет |

### 6.6 Runtime/data contracts
| Area | Truth | Change | Compatibility | Verify |
| --- | --- | --- | --- | --- |
| Task persisted data | Storage/CLI + own3094SHA | None | Exact original fixture restored | full SHA before/after |
| Command lifecycle | Existing VM APIs/tests | Creation timing only | Same bodies/errors/pending revisions; field shape disclosed | source consumers +behavior tests |
| Android candidate | Reviewed source/SDK401/AOT/default2ABI | VM delta only | Same framework/config/package/fixture | actual payload proof/trace phase lineage |

## 7. Правила / алгоритмы
Не создавать команды через constructor lifetime registration. Только owned factories добавляют command/subscription в deferred lifetime; borrowed setter не присваивает VM новое владение. Не ставить IsInitialized=true раньше и не менять autosave gate. Не инициировать writes при initial hydration.

## 8. Интеграция / триггеры
Existing ExecuteSaveCommand/explicit Save command access и first card Reload binding вызывают getter. CanReloadTask observable создаётся factory, читающая текущее состояние с первого subscription, последующие изменения CanExecute продолжаются. Остальные status/emoji/relations streams сохраняются.

## 9. Модель данных / состояние
Только transient backing fields/lazy command state; storage schema и task graph без изменений. Commands не сериализуются.

## 10. Migration / rollback
Обычная пересборка приложения; task migration отсутствует. Эксперимент в текущей первой task branch; если no-retain — отдельный revert production delta, полезные behavioral tests можно сохранить. Оригинальные apps/phone не заменяются, release не выполняется.

## 11. Тестирование / acceptance
| AC | Automated check | Visual/log | Artifact | Missing |
| --- | --- | --- | --- | --- |
| A1 first edit/save/reload/error preserved | Existing task reload/status observation tests + new first-open UI flow | Real persisted title/error, inspected card | TRX/PNG/read-back | No retain |
| A2 lifetime/ownership/concurrency | Existing fixture lifecycle/pending-save tests + first-use concurrent/after-dispose behavior | No new writes after teardown, errors propagated | Targeted TRX | No retain |
| A3 ordinary flow | Native uninstrumented B/C3pairs | Root usable + settled same card, no ANR/black/crash;3094SHA | videos/PNGs/host brackets | No retain |
| A4 useful perf gain | Same instrumented B/C3pairs | Models allocation and wall paired median each decrease≥15%; wholeVM wall AND total allocations paired medians≤+5%, max wholeVM allocation ratio≤1.15; phase transfer учитывается | raw phases/comparison | Otherwise no-retain |
| A5 no ordinary list regression | B/C3pairs list endpoint | Median(C list upper/B list lower)≤1.05, max≤1.15; positive/preceding negative native frames manually verified | root interval comparison | Otherwise no-retain |
| A6 broader compatibility | Exact-head full Main/Headless CI after targeted PASS | Actual packaged VM source/observer absence checked | CI root TRX/payload proof | Not ready |
| A7 first-use cost bounded | Same ordinary B/C cases | First card action request→settled capture upper-duration C/B median≤1.10 and max≤1.25; each action≤10s. Это controlled scripted-duration guard, не точный physical latency ratio | action clocks + inspected card | Otherwise no-retain |

Measurement before first trial: fresh current baseline profile (same framework as candidate), real3094 tasks, own package/AVD1080×2400/3CPU/2560MB/API34/GLES-host, SDK401/default2ABI/AOTtrue. Pre-implementation baseline profile is diagnostic only and does not replace warm-ups/pairs. Profile overlays are private/own package, identical before/after and excluded from ordinary APK; current framework references/actual payload must match, no stale12.0.3 reference. In EACH profile/plain cohort: one valid excluded B warm-up, one valid excluded C warm-up, then three pair orders B-C/C-B/B-C; continuous QEMU per cohort, identical settings. Separate profile/plain cohorts declared. No builds/tests on host during capture.

Predeclared formulas: paired r_i=C_i/B_i for positive scalar phase wall/allocation/wholeVM values; models benefit=1−median(r_i), wholeVM gates apply median(r_i) and allocation max(r_i). Ordinary list r_i=C_i.upper/B_i.lower (B.lower>0 mandatory); median/max over all pairs. Action guard uses its explicitly declared upper-duration C/B pairs. Relative baseline MAD=median(abs(B_i−median(B)))/median(B), computed separately for models wall, wholeVM wall and ordinary root-upper duration; never mix units/cohorts.

Disposition after3pairs: functional failure, A5 max>1.15, A7 max>1.25, wholeVM allocation median>1.05, wholeVM allocation max>1.15 or model allocation benefit<15% → no-retain, no extension. If all gates pass with baseline relativeMAD≤15%, retain eligible pending review. If a timing median is inconclusive/failed and corresponding baseline relativeMAD>15%, or timing gates pass but that MAD>15%, allow exactly one extension to5pairs ONLY when ALL allocation gates (including wholeVM allocation median≤1.05) and preceding hard checks already passed; record decision/reason and exact two additional orders C-B/B-C before pair4. Retain/reject uses ALL5, no dropping/reordering. After5, mandatory gates all pass and relativeMAD≤15% required; otherwise no-retain as insufficient stable evidence. No extension for wide capture bounds alone or desired small effect. Incomplete transports preserved/excluded; valid bad times not retried.

List timing rule differs from rejected worker experiment's scripted-card whole-flow interval: actual parent acceptance is usable root list. Lower = last root-negative captureBegin minus launchRequest; upper = first manually root-positive captureEnd minus launchRequest. Require fresh app PID, actual own non-starting MainActivity window and evidence of current app rendering (a preceding owned loading frame or explicitly verified post-launch native draw); Android cached task snapshot/transition image alone cannot qualify readiness. Preserve all pre-ready frames, finite165s deadline, no automatic acceptance from pixel heuristic. Card must still open/settle and show correct ID/content; its later scripted action clock is separate from loader latency and has A7 transfer guard. This methodology applies prospectively only; no reopening/relabeling the rejected worker experiment. Concrete helper implementation/negative controls for cached frames require review before capture.

Parent5s is still mandatory independent of relative gates; repeatable coldN5, resume and foreground/background freshness remain required before task completion. Profile gain+no regression may justify retaining a useful intermediate change, not merging/completing the parent.

Commands: repository Release builds / TUnit tree-node filters following run-tunit skill; actual first-open UI test following existing Headless/rendered-child patterns; full CI. Helpers/protocol and new phase provenance must be reviewed before any measurement; exact commands/hash/attempts in execution journal. Stop after failed gate to decide correction/rollback, no endless sampling to force PASS.

## 12. Риски / expected objections
| Likely objection | Why | Mitigation | Status |
| --- | --- | --- | --- |
| Autosave исчез после первого edit | Lazy command moved | First real edit/persistence/retry tests | planned |
| Команда не disposed / borrowed command disposed ошибочно | Ownership timing | Existing lifecycle controls + late access | planned |
| Это не5s | Intermediate optimization | Absolute parent retained, no completion/merge claim | explicit |
Rework checklist: flow, assumptions, AC/evidence, objections populated; reviewer/actual validation pending. No public UX redesign.

## 13. План
SPEC review/fix → autoapproval → wait native EGL cohort and select stable baseline → fresh profile baseline → bounded lazy command implementation + targeted/full remote CI → build/prove candidate profile/plain APK → paired measurements/review → retain/revert → continue parent.

## 14. Открытые вопросы
User-owned blocking questions отсутствуют. Технические hypotheses решаются измерением/проверками, не новым пользовательским выбором.

## 15. Профиль
Delivery/performance/UI: before/after, own isolated instrumentation, concrete ordinary flow, private real data, source/payload lineage, meaningful tests/review and explicit no-retain/rollback. SPEC expanded из-за pending-write/lifecycle risk.

## 16. Files
| File | Change | Reason |
| --- | --- | --- |
| src/Unlimotion.ViewModel/TaskItemViewModel.cs | Lazy owned Save/Reload commands | Reduce eager editor objects |
| Relevant first-open UI / fixture lifecycle / task reload tests | Behavior/lifetime coverage, no private-field-only assertions | Preserve original flow |
| This SPEC/parent execution SPEC | Evidence/decision | Audit |
| Private DROOT overlay/helpers | Current-base profiling/ordinary list timings/proofs | No diagnostics in product |

## 17. Before→after
| Area | Before | After |
| --- | --- | --- |
| Command construction | All tasks eagerly | On first real use/binding |
| Save/reload behavior | Existing pending revision/error body | Same behavior/body/ownership |
| Parent acceptance |5/1/5 unmet | Still pending until proved |

## 18. Альтернативы
Persisted cache или deferred full graph затрагивают freshness/initial correctness сильнее; исходные данные показывают несколько bottlenecks, поэтому сначала ограниченный VM allocation experiment. Shared mutable Repeater templates опасны и не нужны для этого diff. Ничего не менять предпочтительно, если измеренный выигрыш не оправдывает новую ленивость.

## 19. Quality gate / review
Post-SPEC advisory review PASS,15Oct. Scope: эта SPEC, TaskItemViewModel.Init/GetOrCreateCommand/ExecuteSaveCommand, Save/Reload consumers, fixture lifecycle/reload tests, central expanded linter/rubric/review owner. Reviewer `/root/startup_profile_review`, writable sandbox с фактическим поведением только чтения; техническая read-only isolation недоступна. Отдельный adversarial pass и targeted re-review выполнены; residual risk — отсутствие технического запрета записи.

| Severity | Area | Finding | Action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Sampling | Profile warm-ups/formulas/MAD/extension неоднозначны | Both cohorts B/Cwarmups; paired math; exact3→5 disposition/all5 | fixed/re-reviewed |
| MEDIUM | Allocation transfer | Models benefit мог скрыть wholeVM allocation increase | WholeVM allocation median/max guard +first-card A7 | fixed/re-reviewed |
| MEDIUM | Extension | WholeVM allocation median failure не был hard stop | ALL allocation gates PASS prerequisite | fixed/re-reviewed |

Scope/Evidence pass подтвердил реальные consumers и исторический характер старого profile. Contract pass сохранил parent5/1/5, old-worker rejection и ordinary flow. Adversarial pass: borrowed ownership/owned replacement, lock order, after-dispose real producers, Fody notifications, cache-snapshot false readiness, allocation phase shifting и sampling selection проверены. Fix/re-review закрыл все находки, новых нет. Role-Based: domain PASS (revisions/autosave/reload), UX PASS (first-open/edit/error), tester PASS (persisted result/late/concurrent/ownership), architect PASS (existing pattern/lock/Fody), delivery PASS (current-base provenance/private data/rollback).

Linter1–20 PASS; rubric цель/AS-IS/design/safety/tests/autonomy по5,30/30. Depth: scope drift, AC/evidence/gates, assumptions/objections, unsupported claims, lifecycle/edge cases, docs, hidden contracts и manual challenge проверены. No-findings justification после fixes: перенос allocation и расширение выборки больше не обходят mandatory gates; нет ref/out/reflection-field consumers в repo; source ABI граница раскрыта. Stop decision: SPEC готова к autoapproval; EXEC prerequisites reviewed/stable EGL baseline +fresh pre-implementation profile сохраняются. Actual validation/retention не выполнены, Post-EXEC не выполнен.

## Approval
Автоматически подтверждено15Oct после review/fixes/targeted re-review PASS по прямому поручению пользователя. Implementation ещё не начата; сначала обязательные EGL/stable-baseline/profile prerequisites.

## 20. Journal
| Phase | Decision | Evidence / remaining | Next | Human | Artifacts |
| --- | --- | --- | --- | --- | --- |
| Draft15Oct | Bounded command-allocation experiment proposed | Historical model phase heavy; newbaseline profile and EGL acceptance pending | Advisory review; no builds during native cohort | Standing authorized scope | This SPEC |
| SPEC review15Oct | All findings fixed/re-reviewed PASS; autoapproved | Sampling/wholeVM transfer/Fody/ownership tightened | Finish EGL baseline acceptance, collect fresh profile before product edit | Standing autoapproval | This SPEC |

## 21. EXEC clarification: disposed Save delegates

Current baseline12.1.1 selected and independently qualified: CI47b9f6c1 Main1541/Headless66, EGL10 functional/FG5. Fresh instrumented PID4296 profile has complete3094/49batches/footers; models17.740s/251225224bytes, wholeVM81.948s/1051806072bytes. Root/card/native last inspected, original3094 restored; single diagnostic only, not a warm-up/pair. OwnAVD16 stopped before implementation.

Initial lazy implementation Release build PASS (0 errors/117warnings). New actual producer controls exposed ReactiveCommand behavior: disposing a command does not prevent a later explicit Execute from entering its delegate. Results `lazy-editor-command-results1` contain20 actual tests,17passed/3failed: initialized-owned late Save, first-access-after-dispose Save, and owned Save retained after borrowed replacement all wrote to test storage after VM disposal. These are real producer observations, not private backing-field assertions. Reload already rejects disposed VM before a storage read. No retained performance claim.

Prospective correction to the previous "same Save body" constraint: add only a disposal admission check at the beginning of the Save execution delegate, under `_pendingSavesLock`, returning when `_isDisposed` is true. Do not reject merely `_acceptingSaves == false`: sealing pending saves intentionally drains final editor revisions after closing new autosave admission. Do not add pending/editor locks to the command factory, do not cancel commands already admitted/running, and leave snapshot/revision/error/update logic unchanged. This meets the already approved A2 no-new-late-producer contract. Existing pending-save/fixture cleanup tests must prove admitted saves/final drain still finish; both initialized and never-initialized late Save controls and caller-owned replacement must pass.

Independent focused design review PASS; automatically approved under standing user instruction. Execution guard now authorized. Reviewer found a test evidence MEDIUM: replace swallowed outcomes/100ms sleeps in late-owned controls with bounded terminal completion/error (timeout fails) before asserting no producers; fixed with Materialize/LastAsync/WaitAsync5s. Parent5/1/5 and experiment allocation/ordinary-flow gates remain unchanged.

## 22. Implementation checkpoint15Oct19:32

Lazy owned Save/Reload factories and the reviewed disposal admission guard implemented. Borrowed setter retains caller ownership; previously initialized owned command remains in the VM lifetime. Factories keep the existing command lock and error subscriptions; pending-save lock is released before snapshot/await. No storage/schema/reader/Repeater change.

Release build4 PASS, zero errors. Targeted raw TRX: storage updates20, fixture lifecycle4, task reload9, file status47, status commands44 —124/124; separate source-switch/cleanup suite36/36. Existing12 card UI cases passed. New first-use rendered UI case passed after correcting a test that inspected a menu item detached by flyout dismissal: it now reopens the busy menu and verifies the currently displayed action. Actual typing persisted the title to the file; explicit Reload read the external title, performed its read off the UI thread and kept the same card. Both final PNGs inspected. No failures/skips in these final targeted runs. Earlier actual late-write failures and the detached-menu assertion failure remain preserved, not relabelled as passing runs.

Evidence private root `D:/CodexArtifacts/android-task-loading-20261012`: `lazy-editor-targeted3-audit.json`, `lazy-editor-source-manager-results1`, `lazy-editor-ui-results2`, `lazy-editor-ui-evidence2/first-{save,reload}.png`, build/test logs. UI video fallback: rendered-child test produces inspected PNGs and persisted read-back rather than a video recorder; Android before/after native videos remain mandatory for the performance cohort.

Focused independent implementation review `/root/startup_profile_review` PASS, no actionable findings: locks/lifecycle, owned/borrowed replacement, Fody/API consumers, terminal late-producer tests and first-use UI inspected; raw124 TRX independently audited. Behavioral read-only reviewer in writable sandbox, no technical isolation. This checkpoint authorizes packaging/measurement, not retention or parent completion. Exact-new-head full CI, actual candidate APK/source/payload proof, paired gains/no-regression and parent5/1/5 remain open.

## 23. Packaging and measurement readiness15Oct20:12

Implementation committed/pushed as `c9456d012421ce64c55d6632bdbae4e8146d5300`. Exact-head CI38769921303 PASS: Main1547/1547, Headless66/66, no failures/skips; rootTRX, all stage exit0, invocation IDs, clean checkout3528cea6 and tree882c7d2b==head verified, independently audited. AndroidPkg38769921426/CodeQL38769921281 also success. `ci-c9456d01-root-audit.json` and downloaded raw artifacts retained privately.

Both own-package candidate APKs built SDK401/defaultarm64+x64/AOT with explicit clean;405 product sources match the frozen baseline except approved TaskItemViewModel and unchanged12.1.1 package props. Frozen candidate VM source SHA767A61F46B4FCB4BE6DFB6DB29CBC256E8D009DE0BC17215B4694816C97CCF6F binds source lock, invocation and finalization. Profile SHA B12C18098E2D9D2BC9B07AD22C9ABDF17650C0F422ABD32C8B6DD8E70CCEA7B9; plain SHA C2AC31842CCE8C10515700B13ADF62267CD52AC9084D4D5BAD98068B5B864FF9. Six actual final managed payloads per APK match inspected linked PE: profile observers1/1/0, plain all0; lazy factories/property shape present, exact12.1.1 OpenGL. Independent packaging review PASS. Initial finalizer assertion incorrectly used case-insensitive PowerShell membership, conflating `SaveItemCommand`/`saveItemCommand`; corrected to case-sensitive checks, preserved failure and separately successful final-payload verification. Product unchanged.

New measurement helpers reviewed/fixed/re-reviewed PASS before capture. Endpoint requires prior owned loading modal in the fresh PID and actual focused BASE_APPLICATION/HAS_DRAWN/sessionPID/no animation leash/no visible Starting/Snapshot/Splash Screen window bracketing the root capture. All pre-ready native frames/raw dumps retained; manual positive/negative review required. Twelve isolated window controls plus four bracket controls PASS; exact qualifier/control-proof hashes recorded and frozen across warmups/pairs. Actual driver order/paths/labels/timestamps/PIDs/QEMU/config/fixture/APK signatures and raw trace/analysis/manual-review metadata SHA binding audited. Cleanup has bounded child-tree exit verification and explicit uncertainty. Sampling/retention gates in§11 unchanged.

First original-namespace B profile warmup aborted before any observations: immediate `pidof` after `am start` ran before Android created PID4148; own cleanup subsequently stopped it. Zero observations/driver records, no valid timing/warmup. Full3094 original hashes verified afterwards, ownAVD17 exited; failed metadata/logs/disposition preserved. Prospective correction reviewed PASS: bounded5s PID polling with remaining-time timeout, saved begin/end, rejection after deadline and unchanged original launch clock. Fresh `lazy-editor-v2-*` evidence namespace on ownAVD18/QEMU119916; no valid bad time retried/replaced.

V2 excluded profile warmups B0/C0 completed with PID4242/7887 on same QEMU, full3094 before/after SHA, complete traces/footers. All six root sequence sheets and two loading/root/card/native-last sheets inspected, same task a5ef0164-8def-417b-9b7d-d235516dd8c7/24children, actual loading→root qualified; independent review PASS. Manual review/audit artifacts saved. Warmups excluded from statistics. Three profile pairs in fixed B-C/C-B/B-C order collecting; ordinary cohort and retention pending. No performance gain/parent acceptance claim.

Shared native Git issue explicitly preserved: both warmups show transient LibGit2Sharp.Core.NativeMethods TypeInitialization notifications and188 runtime dlopen `libssl.so.3` resolution messages, no matching ANR/fatal/black/surface failure; final root/card clear. Both APKs DO contain libssl.so.3; all five x64 Git/OpenSSL entries byte-identical. Packaging/extraction/resolution cause unproved, no claim that the library is absent from APK. Under the original local Git-disabled A3 protocol these warmups remain eligible; no new warning-free gate introduced. This does not establish functional Git sync, and the issue needs investigation before parent completion. Own original phone/apps/task-space untouched by diagnostics. Task1 Active/InProgress, PR draft, no merge/task2/3.

## 24. Three-pair profile disposition15Oct20:23

All six profile captures completed in fixed order B1/C1/C2/B2/B3/C3, sameQEMU119916; all originals3094 before/after exact, trace/analysis/footer/source hashes valid, no collector error/cleanup uncertainty. OwnAVD18 stopped after driver exit0. All native observations inspected in original order using lossless contact sheets plus full loading/root/card/video-last sheets. Explicit root/last-loading indices B1 22/21,C1 33/32,B2 34/33,C2 35/34,B3 31/30,C3 34/33; each root fresh actual own window, correct card/24children and final native frame. Manual raw-window/clock/manifest audits saved per trial; videos retain VFR timing limitation. Shared startup Git messages preserved, not treated as working Git sync.

`lazy-editor-v2-profile-three-pair-gates.json`: paired models wall ratios1.184529/1.153330/0.862204, allocated ratios0.816297/0.761212/0.808523; wholeVM wall1.321380/1.223204/0.951183, allocated1.023854/0.936753/0.955142. Model allocation benefit median19.1477% passes; model wall median+15.3330% fails required−15%; wholeVM wall median+22.3204% fails≤+5%. WholeVM allocation median−4.4858%/max+2.3854% pass. Baseline relativeMAD models1.0682%, wholeVM5.8550%, both≤15%; no timing-noise extension is allowed. These are observations of this instrumented cohort, not a general causal latency claim.

Disposition: NO-RETAIN under existing§11 timing gates. No pair4/5 and no ordinary cohort: an already failed mandatory profile gate cannot be rescued by an ordinary APK. No ordinary performance/acceptance claim. Raw failed/successful attempts retained; no valid bad time retried or dropped. Parent task remains Active/InProgress and absolute5/1/5 unmet.

Prospective rollback clarification for independent review before edit: restore eager Save field/Reload property, original Init command/error/lifetime wiring and remove lazy factories/backing fields. Retain only the independently observed disposal-admission bug fix in the original eager Save delegate: under pending-save lock, return if `_isDisposed`, release lock before snapshot/await; sealing final drain still allowed. Actual initial tests exposed that initialized owned Save after VM Dispose could write to storage even without the new late-getter path; this is a bounded lifecycle correction, not a retained allocation optimization. Keep behavioral tests that assert actual no-late-write/concurrency/borrowed ownership and first-edit/persisted/Reload UI; run affected model/lifecycle/source-switch/card UI tests after rollback and full new-head CI. No performance claim for the guard, no partial lazy implementation. If review finds no independent baseline lifecycle defect, revert the guard with the experiment rather than keep unsupported scope.

## 25. Rollback verification15Oct20:34

Independent raw/cohort/math/visual review PASS confirmed NO-RETAIN and§24 rollback design; automatically approved under standing user instruction. Source first restored exactly to47b9f6c1 eager implementation without guard for an independent baseline repro. Release build PASS; both actual late-Save cases failed (2/2) by writing a task after Dispose, including the already initialized owned command. Raw `eager-disposal-baseline-results1` and build/test logs retained. This establishes a pre-existing lifecycle defect independently of lazy allocation.

Then only the approved pending-lock `_isDisposed` admission check added to original eager Save delegate; lazy fields/properties/factories/error/lifetime changes fully removed. Compared with47b9f6c1 product source differs only by this six-line guard/comment and whitespace on the restored public field. Final build PASS zero errors; raw TRX audited targeted160/160 across the six affected classes, card UI13/13, no failures/skips. `eager-disposal-validation-audit.json`, individual class logs/TRX and rendered `eager-disposal-ui-evidence1/first-{save,reload}.png` retained; both PNGs inspected. Final drain/source switch/borrowed ownership/first real edit and explicit Reload remain working. New rollback-head fullCI pending; c945 CI is prior experimental-head evidence only. No retained optimization or ordinary/parent acceptance claim.

Focused independent post-EXEC review PASS before commit: approved design, final diff vs47, actual eager-baseline failures, all final rawTRX/SHA and two rendered PNGs inspected; no findings. Scope/evidence and contract passes confirm full eager restoration and guard-only retained outcome; adversarial pass checked hidden lazy residue, ownership/error subscriptions, lock admission/final drain and unsupported performance claims. Relevant roles domain/UX/tester/architect/delivery PASS for this bounded rollback checkpoint. Depth checks covered scope, criteria, edge cases, docs/comments, hidden API/lifetime and manual native challenge; no-findings basis is the exact baseline diff and failing-before/passing-after real storage/UI behavior. Stop: experiment NO-RETAIN closed, lifecycle correction locally verified; new-head full CI and parent acceptance remain mandatory. Reviewer `/root/startup_profile_review`, read-only behavior in writable sandbox, no technical isolation.
