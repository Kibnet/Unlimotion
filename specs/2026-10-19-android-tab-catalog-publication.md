# Пакетная публикация структуры emoji-каталога вкладок

## 0. Метаданные

Expanded SPEC по `C:/Users/Kibnet/.codex/agents/templates/specs/_template.md`: UI-facing Reactive/DynamicData state и performance. Delivery-task/QUEST, performance-optimization, dotnet-desktop-client/testing-dotnet/UI automation/review-loops; central stack и локальный MUST UI tests. Owner Codex, surface Codex desktop/Windows/PS7, effective model/effort runtime не раскрывает; model eval неприменим к C# изменению. Task1 `e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6`, branch `perf/android-e74c5d1a-task-loading`, worktree `C:/Users/Kibnet/.codex/worktrees/android-task-loading/Unlimotion`. HEAD `0d30ca2471e1839fece62a14fe1e3e65f4953887`, product/tests как43d95/2afd/59afd. DROOT=`D:/CodexArtifacts/android-task-loading-20261012`.

Статус: CLOSED / NO-RETAIN. Корректность кандидата подтверждена (новые6 UI +6дочерних, existing89, full1578/66), но qualified fixed host cohort показал readyC/B1.1241395144, allocationC/B1.1677346703, wins0/7 и responsiveness0/7. Кандидат архивирован и точечно откатан: product/tests равны Git0d30. Android/native этого эксперимента не выполнялись. Task1 InProgress/Active, parent5/1/5 открыты; следующие задачи и merge/release не начаты.

## 1. Overview / outcome

Устранить повторную перестройку UI после каждой вставки/удаления/перемещения внутри одного `TaskTabEmojiFilterState.Synchronize`. Объединять структурные notifications в один Reset, сохраняя локальные экземпляры/ShowTasks/порядок/метаданные и немедленный итог синхронного вызова.

Output: minimal one-product-file diff +new rendered UI tests, before/after workcount и qualified ready comparison; native только при host gain и полном correctness. Success parent — cold≤5s/resume+action≤1s/localfreshness≤5s, отдельная обязательная приёмка. Stop: regression или insufficient/inconclusive fixed gain → archive/own scoped rollback, без extra samples/threshold tuning.

## 2. AS-IS

MainWindowViewModel две catalog `.ToCollection()` подписки синхронно вызывают UpdateInclude/Exclude всех8other-tab states. Synchronize копирует catalog, строит keys, удаляет лишние, для каждого metadata ищет local поEmoji, создаёт/вставляет новые, копирует4metadata поля и двигает в нужный index. Каждый target.Insert/Remove/Move немедленно публикует CollectionChanged. Control collection handler синхронно RefreshFilterItems→resubscribe/hierarchy/displayed list. Metadata-only sync также делает repeated FirstOrDefault/IndexOf для всехN элементов.

SPEC9 coalesced только Source/CatalogRevision в control. Structural collection handler остался eager: actual whole-ready улучшился8.7%, не прошёл20%, полностью откатан/архивирован. Это новая поверхность, прежний control/Init/reader не возвращаются. Existing source `TaskTabEmojiFilterState.cs` около100строк, DynamicData9.4.31. Локальный XML library contract: `SuspendNotifications` при Dispose отправляет Reset; использовать его безусловно на metadata-only updates нельзя.

## 3. Проблема / evidence

Один логический catalog sync создаёт несколько промежуточных UI snapshots. Дорогой control Refresh присутствует в sampled stacks под TaskTab.Synchronize; inclusive1916.7ms/Refresh1428.0ms не складываются и не доказывают exact CPU/Android gain. SPEC9 eliminated metadata bursts, но не structural bursts. Actual baseline workcounts для нового механизма ещё не сняты — Stage1 обязан подтвердить collection/displayed Reset churn и final outcomes до product mutation.

## 4. Цели дизайна

Одна синхронная операция на owner; public collections остаются read-only, local items/ShowTasks не пересоздаются при обновлении существующего ключа. Не менять metadata event names, predicate semantics, primary exception identity или thread scheduler. Бounded synchronous structural batching допускает иную частоту промежуточных notifications; общей атомарности INPC/UI snapshots не обещает. Guard metadata-only path от лишнего Reset. Minimal state-level change использует существующий library suspender, без timer/async/publichook. Конкретный failure guard не утверждён до baseline characterization и affected design review.

## 5. Non-Goals

MainVM subscription/init order, EmojiFilter control, FileStorage/readers/cache/migrations, data schema, task search Task3, public API, layout, copy selection semantics, global schedulers не менять. Rejected prototypes native helpers не запускать без новой адаптации/review; SPEC9 future package-admission MEDIUM остаётся known limitation архивного prototype. Phone/original app/release excluded.

## 6. TO-BE

### 6.1 Ответственность / proposed design

Единственный product candidate: `src/Unlimotion.ViewModel/TaskTabEmojiFilterState.cs`, Synchronize и маленький private metadata copier при необходимости. Snapshot `catalog.ToArray()` как сейчас. Если count и ordered Emoji keys совпадают — скопировать Title/SortText/Source/CatalogRevision тем же порядком напрямую по индексу, без suspension/structural events; identity/ShowTasks сохраняются. Если структура отличается — выполнить прежний remove/insert/copy/move алгоритм в bounded `target.SuspendNotifications()` scope с явной primary/secondary exception policy, уточняемой Stage1. Dispose публикует структурный Reset в том же UI owner call. All item reuse/new selection=false прежние.

Это предварительный минимальный дизайн, не допуск product EXEC. Baseline structural/UI tests и concrete affected design review должны проверить count/noop/empty/reordering/copy/metadata pitfalls. При изменении дизайна повторить affected review. Не оптимизировать CreatePredicate: его влияние на startup пока не доказано, lazy вкладки могут вообще не иметь подписки на predicate.

### 6.2 Visual plan / evidence

Расположение/контролы прежние. State storyboard: closed empty → catalog fully populated → real popup/selected child → reorder/add/remove → same selection+updated hierarchy → search/keyboard → empty→repopulate. Требуется actual Skia window+popup PNG до/после, contents/identity/event-count JSON. Headless checkpoints вместо непрерывного видео: harness даёт deterministic render/input, Android speed этим не доказывается. Native ordinary roots/card обязательны отдельно после host PASS.

### 6.3 User-observable scenarios

| Сценарий | Trigger | Результат | Evidence / AC |
| --- | --- | --- | --- |
| Начальная публикация include/exclude | Реальный state.Update с множеством unique keys, bound control | Latest hierarchy/order/source, popup доступен сразу после возврата | Baseline/candidate rendered UI +actual collection/list workcounts A1 |
| Избранный item и структурная замена | Remove/reorder/add при selected local | Survivor identity/ShowTasks сохранены, new=false, removed отсутствует | Real input/search/selection/hierarchy A2 |
| Metadata/noop/empty | Same ordered keys с Source/title/revision update; then empty/repopulate | Metadata live; same-shape не публикует Reset; empty correct/All reused | Actual events +popup/search A3 |
| Настоящие вкладки/копирование | Switch9tabs/copy/reset/parent emoji external update | Local mutable items, predicates/results/selection не смешиваются | Existing89 method cases +full66 A4 |

### 6.4 State / interaction matrix

| State | Trigger | Result / edge |
| --- | --- | --- |
| Empty→N | Initial structural update | Один bounded structural batch C, baseline count characterizes eager behavior |
| Same keys/order | Metadata only / same values | No collection events B/C; actual metadata property events remain live |
| N→M/order changed | Add/remove/move | Один структурный Reset C; item INPC может публиковать промежуточные UI snapshots, surviving local identity unchanged |
| N→0→N | Clear then repopulate | No stale items, All/new-item ShowTasks semantics unchanged |
| Popup open/closed | Structural/metadata update | Final usable state synchronous; no queued callbacks introduced |
| Owner/reentrancy/error | Existing event consumers | No thread migration/catch; final callback scope and reentrancy checked by affected review/tests |

### 6.5 Decision ledger

| Решение | Owner / chosen | Confidence | Risk / mitigation | User needed |
| --- | --- | --- | --- | --- |
| Следующий hotspot | Agent, structural catalog publication | .75 | Confirm baseline workcounts before code; no guaranteed gain | Нет |
| Один product file | Agent, state Synchronize only | .9 | No MainVM/control/cache change; public output final same | Нет |
| Suspension conditional | Agent, only structure differs | .9 | Library Dispose resets even noop; metadata/noop event test mandatory | Нет |
| Восемь вкладок | Agent, same state class and exact local item reuse | .9 | All/copy/predicate regression scopes mandatory | Нет |
| Performance | Agent, frozen prospective gates before data | 1 | No relaxing old SPEC9 or resampling | Нет |
| Approval | User standing autoapproval after review/fixes | 1 | Separate stage gates/source proofs, no user question for routine choices | Нет |

### 6.6 Runtime/config/data

Source truth JSON/task graph/storage unchanged. Private host real App/Skia/UIcontext/Unified+raw watcher,3094fixture/49batches15roots/backups+updatesfalse settings. .NET host402; native private401/AOT/API34x64 concrete readiness separately. Source/payload/runtime/settings/data before/after bound; live tracker writes only installed CLI/result/readback. No format migration/API change.

## 7. Бизнес-правила

Key identity current String/Emoji comparison, catalog keys unique by MainVM Group. Existing local objects survive by key; All per-state instance reused, new items ShowTasks=false. Metadata updates exactly existing4fields/order; no selected predicate semantics changed. Same order/count keys means no structural notification; metadata must still notify as before. Source order/removal/insertion final identical, include/exclude state isolation and final public read-only collection shape preserved.

## 8. Интеграции

Existing UpdateInclude/UpdateExclude callers unchanged, no new runtime trigger. DynamicData ReadOnlyObservableCollection consumers/control/ToObservableChangeSet/AutoRefresh/copy panel receive final Reset on structural sync; metadata INPC remains. Existing MainVM source switch/reconnect/disposal and task watchers untouched.

## 9. Данные / состояние

No persisted fields/cache. Temporary frozen array +bounded synchronous suspension only. Don't add global/static mutable cache/test hook or store VM references beyond state lifetime. Original subscriptions/disposal stay; secondary Reset-consumer exception must not replace captured primary metadata exception.

## 10. Rollout / rollback

Candidate remains uncommitted until qualified native gain. Failure→checked ZIP ownstate/test/SPEC/diff/raw→restore only ownstate exact savedB→remove own candidate-only UI tests; verify Git/product/source equality and tracker result. Preserve any unrelated concurrent changes. Useful generic tests retained only if baseline behavior proven and separately reviewed. No merge/next top-level task until parent5/1/5 accepted.

## 11. Acceptance-to-test / performance

| AC | Required check |
| --- | --- |
| A1 Actual structural work reduced | Stage1 B characterization:3methods×include/exclude=6discoverablecases planned, exact count fromdiscovery; collection events +bound List.ItemsSource resets before/after; C prospective structural update≤1 collection event (Reset), final same state |
| A2 Selection/identity/hierarchy/input | Real popup before await, search/keyboard, selected survivor reuse/newfalse/remove/order/All include/exclude, rendered checkpoints |
| A3 Noop/metadata/empty | Same-shape0structural events, latest Source/title/revision/hierarchy, empty→repopulate, no late callbacks/new async |
| A4 Regression | Exact89existing cases/57fresh method identities as prior validated form; fullMain1572+new6 planned and Headless66/allothercounters0/sourcebound. Count fixed fromactualdiscovery before product change |
| A5 Significant gain | Frozen common host whole-ready protocol below; only then native readiness/gates; no work shifted to first popup |

Baseline product/tests CI exact0d30 currently inprogress; don't claimPASS until actual. Before product mutation run new baseline rendered characterization. Then candidate targeted/new+affected, normal full Main/Headless, source review, fixed performance. Full tests mandatory; no performance gate substitution for correctness.

Host prospective: new frozen B/C executables same common Program as qualified Host6 ready endpoint; real UI owner, start→Connect→explicit Normal callback +Task.Yield FIFO guard→UpdateLayout endpoint, allocations through ready, sampler25ms through ready+500ms, raw connect diagnostic only. Concrete new runner/readiness/receipt guards before execution; four owner/provenance/postInit/postConnect600msstall controls before excluded B diagnostic. One B diagnostic before product change, then C correctness/full/source review, 2excludedwarmup +7fixedalternatingpairs/16freshPID. Dataset3094SHA everytrial/root15/49ownerbatches/runtime/source/payload stable.

Frozen gates median paired readyC/B≤.80, allocation≤.90, wins≥5/7, everyCgap≤max(1.05×B,250ms). Valid fail/inconclusive→NO-RETAIN, no extra pairs. New source set/receipts must bind current6testcases, not stale SPEC9 removed test paths. Prior Host6/7 timings do not serve as new comparison; immutable old binaries may only be source-equivalent control after explicit new readiness, default fresh B build preferred.

Native only after hostPASS: new source/package/observer/actualPE readiness; fixed2warmup+3profilepairs wholeConnect ready wall≤.80/wins≥2/3/allocation≤.90, then observer-free ordinary fixed2warmup+3pairs conservative root CUpper/BLower median≤.85/max≤1.05 +usablecard/data. Fix any reused prototype provenance gap before native build; own newAVD runtime/QEMU/PID/exit only. Parent5/1/5 separately mandatory. No native execution currently admitted.

Bounds: build600s/targetedclass300s/child180s/fullMain2100s/fullHeadless900s/wholehostcohort1200s. No concurrent builds/tests/media during full Main or timed cohorts. Fresh paths/sourceproofs; failed raw/known backend contamination kept/diagnosed before repeats. Build SDK private root separated from repo. Test outcomes from archivedSPEC9 not current baseline/full.

## 12. Риски / expected objections

| Риск/возражение | Мера |
| --- | --- |
| Reset пересоздаст selection/objects | Identity assertions +real UI selected/search/All/copy across9tabs |
| Скрыли cost до открытия | Synchronous end of Sync +firstopen beforeawait +whole-ready/layout+nativeordinary |
| Metadataonly generates Reset | Conditional branch +0event characterization/guard; actual library contract read |
| Selection gets shared across tabs | Local instances unchanged/exact existingcopy/isolation tests |
| FullSync reentrant observer sees another snapshot | Baseline frozen input/review synchronous notification boundary; preserve exception handling, no new async |
| Another profile mistaken forgain | Workcount not timing; fixedB/C; old sampledinclusive windows not summed |
| Another tiny optimization despite parentgoal | Reject below20%, retain no dead code; parent5/1/5 remains until actualE2E |

## 13. План

Stage1spec review/fixes/autoapproval→baseline6UIcharacterization/helperpreparation→concretehelperreadiness+excludedBdiagnostic→affected product-designreview→minimalstatechange/newUIexpectation→newtargeted/existing89/full/source review→fixedhost→conditionalnative→retain-or-rollback/CLI. Maintain one top-level Task1 branch/worktree. Subsequent task only after accepted Task1 merged to latestmain.

## 14. Открытые вопросы

No user-owned ambiguity. Technical unknowns: actualB eventcounts, exact6discovery, reduction against native remaining I/O cost, suspension interactions. Resolve with baseline/evidence and affected review before dependent work; absence of data is not PASS. HardwareFold6 not claimed; emulator authorized.

## 15. Соответствие профилю

Performance before/after latency+allocations; targeted→full; fixed significance/rollback. UI actual rendered input/endstate, newtests and existing scopes mandatory. QUEST autoapproval only after review/fixes; stage-specific conduct/readiness recorded. No publicsideeffects beyond already authorized branch/PR/tracker workflow.

## 16. Файлы

Product only TaskTabEmojiFilterState.cs. New planned `src/Unlimotion.Test/TaskTabCatalogSynchronizationUiTests.cs`. This SPEC; private new B/C helper/runner/source proofs. Existing MainVM/control/storage untouched. No generatedartifacts/taskfiles committed.

## 17. Было → стало

Baseline exposes each intermediate list insert/remove/move to consumers; proposed structural change exposes completed list with one Reset. Metadata-only still synchronous property refresh, with same local objects; same-shape path avoids unnecessary full key/index searches. Final task/filter semantics unchanged.

## 18. Альтернативы

Global DynamicData notification suppression/storage publication change broader and affects taskgraph; rejected. Per-control asyncmetadata scheduling already failed significance and not revived. AlwaysSuspend would addReset to metadata/noop; reject. Incremental change-set propagation across MainVM/state adds contracts and reentrancy complexity before proving need; prefer one-state bounded operation. Full cachedsubsystem not selected merely from this hotspot hypothesis; existing rejectedN5/N9 constraints remain.

## 19. Quality / planning review

20canonical fields addressed; applicable roles Domain (selection/key/source nochange), UX (immediatepopup/metadata/All/identity), Tester (baseline/Cworkcounts+realframes+89/full/fixedgates), Architect (onefile/boundedSync/noasync), Ops (immutable/source/runtime/ownAVD/CLI). AuthorScope inspected TaskTab fullsource, MainVM2catalogsubscriptions/8states, realcontrolcollectionhandler, existingisolation/copy/affinity tests and libraryXMLactualSuspendNotificationscontract. Adversarial: alwaysReset would convert metadataflow into fullrebuild; same-shape branch mandatory, unknown gain not assumed. Need separate Scope/Contract/Adversarial/Role planningPASS and fixes before Stage1. No-findings/technical isolation not claimed without review. Author fallback required for writable reviewer runtime.

## 20. Журнал

| Фаза | Факт | Next / evidence |
| --- | --- | --- |
| Priorclosure | SPEC9 qualifiedNO-RETAIN/ZIP1105payload+manifest/ownrollback/PostEXECreviewPASS/CLI19exactreadback | Docs0d30 pushed, PR325body8 exactOPEN/DRAFT/noautomerge; current3CI workflows PASS (ci-head0d30ca24-workflows2.json) |
| Analysis | Structural updates still eager; source-backed proposed one-state notification transaction +same-shape path | No product/test mutation; Stage1 planning review requested |
| Stage1 affected review / autoapproval | Оба planning MEDIUM уточнены, advisory Scope/Contract/Adversarial/roles PASS: только6baseline rendered cases и helper sourcepreparation. Author fallback: noatomicUIclaim, primaryexceptionidentity обязательна, mixed counters не подменять finalenum | Writable runtime/read-only conduct; user standing autoapproval применяется к Stage1. Product/benchmark/native/retention отдельно, не допущены |
| Planning MEDIUM1 | Suspension не подавляет item INPC: survivor Source/Revision может заставить control перечитать промежуточный target до Reset | Stage1 смешанный remove/reorder/add +changed Source/Revision survivor в одном Update; раздельные collection/item/displayedReset counts и снимки увиденных структур. Обещание ограничено одним structural notification, общей атомарности UI нет |
| Planning MEDIUM2 | Dispose Reset-consumer error может маскировать primary metadata setter error; прежнее «нет новых catch» недостаточно | Stage1 controlled primary Source-INPC throw +secondary Reset throw: exactprimary identity, partial state, detached throwing handlers и successful retry. Дополнительный collection-consumer throw/recovery сохраняет observed state как characterization, не обещает прежнюю partial event sequence |
| Stage1 actual | Build4/targeted-baseline3 PASS exact6root+6child1/1/allothers0, build/testinputs equal/sourceunchanged. Initial8collectionadds→50displayedresets; mixed8collection/126itemevents/18displayed, фактические промежуточные snapshots сохранены, primary/collectionfailure+retry PASS | `tab-catalog-baseline3-audit1.json`, savedsource `tab-catalog-ui-baseline3.cs`; code product не менялся |
| Test strengthening / review | Author moved firstopen beforeawait; actual visible updatedchild search добавлен. Reviewer MEDIUM stackassertmethodname мог пропустить stackreset: NoInliningThrowMetadataConsumer sentinel+exactframe/instance, affectedsourcePASS. AuthorvisualfixtureSortText обновлён вместе сTitle, actualrenderedTextBlock assertion | Historicalbaseline1/2sources+raw preserved; final6 baseline3 подтверждает этиfixes, не product workaround |
| Baseline visual / CLI20 | 22actualPNG/11unique hashes, каждыйunique лично просмотрен; popup/include/exclude/changedchild Revisedrepair/selection/empty/repopulate/failure-retry соответствуютscenarios. Empty narrow popup — существующая baseline presentation, не исправлялась | `tab-catalog-baseline3-visual-review1.json`; PNG/JSON explicitvideofallback. CLI20 exactreadback InProgress/Active/lease/usertext/criteria/relations preserved |
| Helper readiness / autoapproval | Advisory newH1/H2common sources byteidenticalqualifiedH6; runner delta onlynamespace/ref, provenance/owner/NormalFIFO+600msstall/bounds preserved. Послеactualfinal6+visual условий выполненauthorfallback, userstageautoapproval | Допускается freshBbuild→4controls→1excludedBdiagnostic. C/product/native пока нет. Writable runtime/read-only conduct |
| Actual baseline host | FreshBbuild+4controls+excludedBdiagnostic PASS/sourceunchanged:3094tasks49batches15roots/owner, NormalFIFO/postConnect600ms stall входят вready иmaxgap, staleSource/control/payload иnoowner reject | `tab-catalog-host1/evidence/*-final.json`, receiptSHA d23d1442554472640ef219f23a8301919728607fb6c28e94d9aa8df4f63f4d5a. ActualrootPNG personallyviewed/noLoading/list15roots; timings excluded, не gain |
| Stage2 design review / autoapproval | Advisory actual6/22PNG/source binding/all5052Binputs/controls/diagnostic PASS; sameorderedkeys privatecopy4fields/noShowTasks/noReset, structural originalorder+scope, EDIprimary/secondaryTrace policy Scope/Contract/Adversarial/roles PASS, noBLOCKER/HIGH/MEDIUM | Userstandingautoapproval: допускаются minimaloneproductfile+testflagflip и new6/89/full1578+66/source review. Ccohort/native/retention отдельноне допущены. Authorfallback согласован сguard/partialeventlimitations; writable/read-onlyconduct |

### Конкретный минимальный дизайн Stage2 — review PASS, реализован для проверки

1. В existing Synchronize сохранить `source=catalog.ToArray()` перед модификацией. Если target.Count==source.Length и каждый ordered Emoji key совпадает прежним String equality, скопировать4metadata поля существующему target[index] тем же порядком, return; не создавать suspension/Reset. New private CopyMetadata helper допустим, ShowTasks не копировать.
2. При отличающейся структуре взять existing target.SuspendNotifications и выполнить прежний keys/removal/insert/copy/move цикл без изменения порядка/identity/newfalse. Только структурные collection callbacks объединяются, INPC существующихitems остаётся немедленным; ни общей атомарности, ни одного displayed-reset при mixed update не заявлять.
3. Source snapshot/shape guard errors до suspension и metadata-only errors выходят прежним способом. В структурной ветке сохранить primary body Exception через ExceptionDispatchInfo. В finally всегда Dispose scope. Если primary отсутствует, Reset-consumer error выходит как есть; при наличии primary secondary Reset error не заменяет его. Best-effort System.Diagnostics.Trace.TraceError для secondary ограничить собственным narrow try/catch, чтобы ошибка diagnostic listener не заменила original. После disposal capturedEDI.Throw() сохраняет exact instance и NoInlining throw-origin frame. Не добавлять broadcatch вокругвсегометода/async/новыхpublichooks.
4. Flip только source-bound ExpectBatchedStructure false→true: initial structural1/displayed1, mixed collection1 (item/displayed snapshots отдельно), primary+secondary scope path требуетsecondaryCalledtrue и originalNIframe, no-primary collection error exact и retry. Прочие semantic assertions одинаковы B/C. Source tests/targeted/full1578+66/89methods сначала, source review затем fixed16 performance.

Author challenge: весь выигрыш не переносится на firstopen, потому что finalReset выполняется доreturn Sync; same-shape metadata не получает artificialReset; Reset delivery входит в measured Connect+Normal/layout. Source cache/task data/root Init не меняются. Raw baseline mixed/error snapshots подтвердили, что intermediate event sequence нельзя использовать как final UI contract. Product design требует affected advisory PASS и fixes до исполнения.

### Обязательное расширение Stage1 / ещё не product admission

Три rendered метода ×include/exclude =6cases (actual discovery до исполнения): initialbulk/firstopen/input; mixed structural+survivor metadata/noop/empty/repopulate; controlled failure/retry. В mixed update observer отдельно записывает **фактически увиденные** source-key snapshots на CollectionChanged, Source/Revision INPC и displayed-list Reset, а не только финальный enum.

Для future product design primary failure из metadata/body должен выйти тем же exception instance/stack даже если notification-scope Dispose дополнительно бросит. Secondary не замещает primary; без primary ошибка Reset-consumer должна выйти как есть. Это может потребовать ограниченного EDI/try-finally guard, несмотря на исходное предположение о простом using; конкретный код только после фактического baseline и affected re-review. Не обещать одинаковые промежуточные callbacks или partial collection при намеренной ошибке; обязательны явный original failure и успешное восстановление следующего Update без скрытой порчи выбора/источника. Нет новых catch вокруг ordinary metadata-only branch без необходимости.

### Stage2: фактическая реализация и целевые проверки

Изменён только TaskTabEmojiFilterState.cs, в новом UI-тесте переключено ожидание объединённого структурного уведомления. Build-candidate1 и targeted-candidate1 прошли: 6 основных случаев и 6 дочерних запусков 1/1, остальные счётчики нулевые, исходники неизменны. Начальное заполнение даёт один structural Reset и один displayed Reset вместо baseline8/50; mixed metadata остаётся синхронной, общей атомарности UI не обещаем.

`tab-catalog-candidate1-audit1.json` связывает 7 TRX и 22 PNG; все 13 уникальных кадров лично просмотрены, запись `tab-catalog-candidate1-visual-review1.json`. Реальные клавиатурный выбор, поиск обновлённой подписи Revised repair, сохранение выбранного элемента, empty/repopulate и recovery соответствуют assertions. При намеренно бросающем consumer промежуточное popup-состояние может отличаться; обязательны original exception и успешный следующий Update. PNG/JSON — явно указанный fallback вместо видео, не Android performance evidence. CLI21 exact readback подтвердил InProgress/Active и сохранность пользовательского описания/критериев/связей.

Сейчас выполняются 89 существующих регрессионных случаев в свежих процессах; полные 1578+66, фактическое code review и fixed host cohort ещё не завершены. Native и retention не допущены.

Stage2 actual code/targeted affected advisory review PASS, actionable findings нет; author fallback повторно подтвердил onefile scope и EDI/metadata/identity contracts. `tab-catalog-stage2-code-review1.json` связывает точные bytes state/test, raw-аудит и visual receipt. Runtime writable, поведение reviewer только чтение; техническая изоляция не заявляется. Допуск остаётся на correctness89/full1578+66.

Regression runner1 остановился на проверке уникальности PID после32методов/41засчитанного случая: m33 повторил Windows PID184284 из m14, оба actualтеста 1/1PASS/exit0. Старые отчёты сохранены как incomplete, не89PASS. Fresh runner2 использует PID+kernel creation FILETIME, захваченный немедленно из livePopen handle и записанный в invocation/progress. Два actualprocess controls и samePID/differentcreation/duplicateidentity controls PASS, затем affected advisory review PASS; authorfallback подтвердил один ASTanchor/cleanup/same57methods89exactIDs. Full runner dependency переведена на complete regression2; product/test bytes не менялись. `tab-catalog-regressions1-runner-failure-audit1.json`, `tab-catalog-regressions2-identity-controls1.json`.

Fresh regression2 complete PASS57методов/89случаев, exactgoldenIDs и нулевые остальные outcomes,57уникальных пар PID/creationFILETIME, sourceunchanged. `tab-catalog-regressions2-audit1.json`. Начата full-ui build; full66/1578 остаются pending, performance ещё не выполнялась.

Полные обычные наборы PASS: Headless66/66 и Main1578/1578, остальные outcomes0; fullMain18:55:13→19:24:48 UTC, duration1774311.6627ms, exit0. Expected CLI benchmark3000tasks/600reads завершился;35мин bound не достигнут. `tab-catalog-full1-audit1.json` связывает TRX/inputs и подтверждает одинаковые P product/test bytes сtargeted6/regression89/sourceunchanged. Actual fullUI43PNG/37unique:22новыхunique лично просмотрены,15unique byte-identical предыдущим лично просмотренным; `tab-catalog-full-ui-visual1/review1.json`. Полный Main выполнялся без параллельных build/test/media/cohort.

Prospective `run-tab-catalog-pairs2.py` учитывает PID+kernelcreationFILETIME после actualWindowsPIDreuse в старом regressionrunner. ExactAST функций совпадает сreviewedregression2/его actualcontrols, фиксированные16процессов/пороговые значения/ready/gap/layout/Normal fence не менялись. Source readiness affected review PASS, `tab-catalog-pairs2-source-review1.json`; ещё не Cbuild/cohort admission. Итоговое actualcorrectness review запрошено перед freshCbuild. Native/retention остаются отдельно.

### Сбой ресурса и новое размещение host-сравнения

Actualcorrectness review PASS без findings; разрешён freshCbuild. Первый Cbuild (oldR/tab-catalog-host1/paired2) завершился только MSB3021/MSB3027 ENOSPC при копировании nativeassets, sourceunchanged, successfulCpayloadreceipt отсутствует. Rawfailure сохранён, `C:\Users\Kibnet\.codex\artifacts\android-tab-catalog-20261019\candidate-build-enospc-audit1.json`.

Новый writable artifact root A=`C:\Users\Kibnet\.codex\artifacts\android-tab-catalog-20261019`; oldR D используется только для чтения старых доказательств/fixture/sourcehelpers. На A/hosts побайтово проверены5002+358файлов H1/H2. Автопроверка отклонила guarded recursiveRemoval+junction command без подробной причины; безопасный fallback — оригиналыD не удаляются/не перемещаются, junctions не создаются. Сравнение использует Bcopy наC и freshHost3 наC, trialTasks/новые reports тожеC; A/global.json и3Host3commonfiles byte-identical B. `layout-transfer-receipt1.json` связывает исходные4controls/Breceipt и копии; source/runtime/payload/metrics/gates не менялись, compare относится к новомуC-layout, старые absolute timing не подменяют новыйbaseline.

`run-tab-catalog-pairs3.py` source/layout affected review PASS. Один заранее объявленный excluded layout-diagnostic1 PASS:3094/49/15roots/owner/Normal/QPC/data/runtime/payload; actualrootPNG лично просмотрен. Его ready5257.9841ms исключено изstatistics, не gain. `layout-diagnostic1-author-audit.json`. Перенос4controls допустим по идентичным source/input/payload/DLL bytes;1578/66 не повторяются из-за locationchange. Допущен freshCbuild; cohort только после новогоCpayload/receipt review, fixed2warmup+7pairs без дополнительныхtrials.

### Fixed host cohort: фактический NO-RETAIN

ActualCbuild/Ppayload review PASS, но первое cohort-admission review нашло MEDIUM: перечитывание manifest позволяло согласованную замену manifest+DLL внутри серии. Новый orchestration-only pairs4 закрепляет external admission SHA, literalreviewedCreceipt, B/Creceipt/manifest pins и immutablepayloadmaps; originalpairs3/build-input map сохраняется отдельным pinned input, product/Program/metrics/schedule не меняются, Cbuild не повторялся. Actualpositive/restoration+6negativecontrols PASS, включая coherentB/C/admissionreplacement; affectedre-review PASS.

Единственная fixedC-layout серия завершена:2excludedwarmups+7alternatingpairs,16уникальных PID/FILETIME, runtimebefore/after совпадают, Source/admission/payload pins неизменны. Gate: pairedmedian readyC/B=1.1241395143678734, allocatedC/B=1.1677346702740927, wins0/7, responsivenessPASS0/7. Это ухудшение примерно12.4% времени и16.8% выделенной памяти, не выигрыш; решение NO-RETAIN. Все16rows/root15/Normal/QPC/3094/49/owner/data подтверждены; author независимо пересчитал ratios и49504cloneTaskSHA. `A/pairs4-cohort1-author-audit.json`;16PNG1uniqueSHA390144... personallyviewedp7C, visualreceipt. Absolutehosttiming относится к новомуClayout, не Android. Native/AVD/APK этого SPEC не запускались.

Результат проходит retrospective review; далее immutableZIP с исходниками/тестами/rawcorrectness+cohort/B-Cpayloads/fixtureonce и manifest, затем только verified rollback TaskTabEmojiFilterState.cs и удаление собственного нового6-case test. До фактического rollback он не заявляется завершённым. Task1 InProgress/Active и parent5/1/5 остаются открыты, следующие задачи/merge/release не начаты.

### Фактическое закрытие и граница доказательств

Retrospective advisory review PASS: квалифицированный host regression, без вывода о причине или скорости Android/Fold6. Создан A/tab-catalog-rejected-candidate1.zip, SHA256 `0f46260edb8634ff2b292937b686bebfef62c2c4c2ed612cf8b13b66869bb5d2`:5268hashed payload entries +manifest =5269ZIPentries; CRC и каждыйSHA перепроверены, original files совпадали на момент архивации. Внутри source/test/spec/diff, baseline, raw UI/normalfull/cohort/helper/provenance proofs и154B+154Cpayloadfiles. Fixture3094файла сохранён один раз вevidence-old/fold6-dataset; исключённые16trialTasks восстанавливаются копированием этой fixture с проверкой directory-worker-fixture-task-sha256.json. Это сохранение доказательств/входных bytes, не полный filesystem snapshot.

Только после проверки архива восстановлен TaskTabEmojiFilterState.cs в exactbaselineSHA `22534281090e0c25c2e812494603a679c24c27d50fd10517c2f50352cb106bbc`; normalizedbytes совпадают сHEAD0d30. Удалён только собственный TaskTabCatalogSynchronizationUiTests.cs. `tab-catalog-candidate-rollback1.json` подтверждает source/testGit equality и отсутствие прочих untrackedsrc/tests.1578/66 относятся к измеренному C до rollback; новый baselinefull-run не заявляется. Исходные архивные Csourceproof теперь намеренно отличаются от restoredP. Ни APK/AVD/phone, ни Task2/3/merge/release не выполнялись. Parent5/1/5 не принят.
