# Один буфер для проверки статусов и начального графа задач

## 0. Метаданные
- Delivery/performance/migration-sensitive; owner Codex по поручению пользователя; medium, expanded canonical _template.md. Codex desktop/Windows/PowerShell/.NET/Avalonia; изменение модели агента не проводится, model eval не применим. Фактическая модель/режим не являются переменной измерения.
- Task1 e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6; branch perf/android-e74c5d1a-task-loading; worktree C:/Users/Kibnet/.codex/worktrees/android-task-loading/Unlimotion. Product baseline08225864 (src identical dd1f61c1), exact-head CI38786383125 Main1547/Headless66/rawTRX/stages/tree PASS, AndroidPkg/CodeQL PASS.
- Central stack: quest-governance/mode, collaboration/model/tool/testing, performance-optimization, dotnet-desktop-client, ui-automation-testing, review-loops, Git delivery/commit policies; local UI MUST. SPEC в repo/specs; user standing autoapproval after review/fixes, sequential tasks/ownbranch/worktree/merge-before-next unchanged.
- Sources: parent2026-10-12-android-task-loading-execution; prior2026-09-14 task-loading-speedup (no retained pre-migration snapshot writes);2026-09-15 remaining-experiments (N4+N6a retained/N5,N8,N9 rejected); status-fast-path12Oct; Core deadline snapshot15Oct. DROOT=D:/CodexArtifacts/android-task-loading-20261012; raw traces private.

## 1. Overview / цель
Убрать повторное физическое чтение modern task files между проверкой status schema и построением initial live graph. Список/поиск/карточка/внешние изменения остаются прежними. Это один новый bounded experiment внутри task1, а не новая tracker task.

Outcome: correct single-buffer modern path, same legacy/backups/report/diagnostics/freshness, measured Android paired benefit, inspectable tests/diff/SPEC/evidence. Retain только после §11; иначе rollback. Parent cold5s/resume1s/freshness5s остаётся обязательным и не закрывается относительным выигрышем. Никакого merge/task2/3 до полного task1 acceptance.

## 2. AS-IS
UnifiedTaskStorage.BuildInitialTaskViewsAsync уже выполняется в Task.Run. Сейчас MigrateTaskStatusModel читает каждый файл через File.ReadAllBytes и проверяет HasModernTaskStatusJson; затем EnableLiveGraphAsync повторно читает/десериализует/хеширует task files через FileTaskStorage.ReadDirectoryCoreAsync/DeserializeTaskSnapshot.

Actual PID4336 diagnostic: status17.069557s/28,978,856 process-allocated bytes; livegraph27.502451s/96,139,608; migrations1.665969s; DTO0.077082s. Core cutoff60s застал models.begin открытым13.659s; VM позже завершила Init. Root[93.176668,95.696442]s/card5.394815s/3094unchanged/24children. Один diagnostic, не paired benefit. Старый PID4725/140s failure сохранён отдельно; в среднее его не подмешивать и не удалять.

Current invariants: canonical sorted file ordering/last duplicate wins; per-file SHA exact source bytes; local serializers/shared immutable resolvers; JsonRepairingReader only in-memory repair; watcher pending generations/own-write confirmation/liveGraph invalidation. Guarded reverse-link/availability writes recheck exact source hash; status migration WriteOwnedMigrationFileAsync uses atomic replacement without expected-SHA compare. Directory lock не глобальный атомарный snapshot для внешних файловых writers.

## 3. Проблема
У современного файла raw status check и typed graph read оплачивают два отдельных I/O snapshots, хотя schema check можно выполнить над буфером graph reader до десериализации.

## 4. Цели дизайна
Single modern buffer; legacy writes только из отдельного свежего legacy read, без удержанного pre-migration snapshot; no persistent cache/certificate/mtime shortcut. Storage владеет bytes/parse/hash/publication, UnifiedTaskStorage владеет migration policy/report; FileStorage — narrow bridge. Old readers без preparation сохраняют свой путь.

## 5. Non-Goals
Не менять Json.NET/repair policy, schema, statuses, commands/lazy editor, graph topology, projection batching, UI layout, search, lifecycle, CLI semantics, global schedulers, installed apps или credentials. Не повторять directory worker/parallel reader/lazy Save/Reload/N5 certificate/N9 runtime cache. Не вводить claims physical Fold6 или готовности5/1/5 на основании instrumented run.

## 6. TO-BE
### 6.1 Ответственность
- FileTaskStorage: protected preparation-enabled initial graph path, fresh bytes read, typed parse/hash from returned buffer, existing ordering/errors/publication/generation boundary. Existing public EnableLiveGraphAsync unchanged.
- FileStorage: internal bridge to protected path, canonical graph filename predicate for migration selection, existing owned atomic writer unchanged.
- UnifiedTaskStorage: migration scan context (migrationTime/gitRoot/processed/changed/backup/report), modern predicate unchanged, fresh legacy fallback, report before graph publication; raw-watcher startup selects combined path.

### 6.2 Детальный дизайн
1. Только FileStorage с IRawDatabaseWatcher заменяет separate status→EnableLiveGraph на combined initialization. Non-raw/no-watcher сохраняет прежний migration flow. All work остаётся на existing background worker; не переносить IO/JSON на UI.
2. Storage protected operation принимает per-file `Func<string, byte[], ValueTask<byte[]?>>` и before-publication completion callback. Default public graph read/enable не подключают processor. Никакого mutable global/per-instance processor field. Existing directory lock/invalidation generation охватывают prepared read, completion и publication.
3. Directory reader получает свежий raw byte[] тем же FileStream/share behavior, что typed reader сейчас. Modern predicate true: processed++, вернуть тот же reference; parse и SHA выполняются именно над этим buffer, без File.ReadAllBytes из migration path.
4. Predicate false: initial probe НЕ используется для migration write. Existing legacy per-file routine делает отдельный свежий File.ReadAllBytes, сохраняет BOM/encoding/JObject/property casing/duplicate/history semantics. Если свежий snapshot modern — вернуть его без write. Если legacy changed — прежние backup/owned atomic write; вернуть UTF8 bytes фактически успешно записанного JSON. Если parse/fallback не меняет файл — вернуть свежий исходный buffer. Если свежий legacy read недоступен — вернуть null: storage делает fresh authoritative read и сохраняет прежнюю read diagnostic/recovery, не использует первоначальный stale probe. Это два чтения для обычного legacy файла, как status-read+graph-read раньше; при read failure дополнительный retry — явно диагностируемый fallback, не цикл.
5. Read/parse recognized failures превращаются в те же load errors; backup/migration write/report exceptions остаются fatal и не попадают в общий catch графового JSON/IO. Реализовать маленький internal read-result boundary: read/typed-parse errors отдельно, processor exceptions проходят наружу с исходным типом/stack. Не маскировать failed persistence как corrupt-file skip.
6. Legacy enumerator и graph enumerator различаются: migration допускает некоторые hidden JSON/extensionless files, graph исключает hidden/empty. Сохранить migration processing этих non-graph files отдельным проходом с прежним fresh helper; graph files мигрируются processor до typed ID validation, включая malformed task-shaped/no-ID cases. Empty graph-shaped files остаются no-parse/no-change. Non-JSON/report exclusions прежние. Report counts на неизменном fixture совпадают; при concurrent writer это число реально обработанных выбранных snapshots, не атомарный inventory claim.
7. Migration report и flag публикуются completion callback ПОСЛЕ всех successful per-file preparation, ДО PublishLiveGraph. Report format/version/git rollback/backup paths unchanged; changed==0 не создаёт report/backup. Flag true только после успешных migration writes/report. Fatal path не публикует новый graph и не объявляет loading ready; существующая error/recovery semantics сохраняется.
8. После combined read прежние pending refresh/reverse-links/availability migrations/DTO/VM/relations/batches/reconcile/cutoff идут в прежнем порядке. Own-write confirmation/physical source SHA guards не отключать. Pending external edit/delete/rename/error replay не теряется. No retained pre-migration snapshot перед migration writes: legacy fallback всегда fresh; initial modern buffer используется только для read/parse/hash.

Visual plan: existing Loading→same root list→same task card/24children→first useful action. Layout unchanged; new wireframe не применим. Actual prior before video at DROOT/android-core-deadline1-native-run1/flow.mp4 and inspected root/card PNG. Paired acceptance uses ordinary baseline/candidate APK videos; private real data never commit/publish. UI tests cover modern and legacy first load, first edit/reload and external update. Existing SingleView mixed-status fixture uses watcher:false; parameterize watcher:false/true with modern1/130 so the new raw-watcher combined path is exercised explicitly.

Concurrency boundary: status migration retains its pre-existing fresh-read→atomic-replace race with a bypassing external writer; no CAS protection is claimed or introduced. The new initial probe is discarded before legacy fallback and therefore adds no retained-probe write window. Tests deterministically preserve an edit made before fresh fallback, and replay an edit after migration persistence. An edit during fresh-read→replace can still be overwritten as in baseline. Guarded reverse/availability writes and their physical-source conflict protection remain separate and unchanged.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Expected visible result | Evidence | AC |
|---|---|---|---|---|
| S1 Modern3094 Git | cold ordinary launch | Same usable root/card sooner; all tasks/relations/statuses preserved | paired profile+ordinary native videos/raw hashes | A1,A4,A5 |
| S2 Legacy/mixed/new legacy later | first/next launch | Same status/history, backup/report; no lost unknown fields | migration contracts+Headless UI | A2,A3,A4 |
| S3 Corrupt/BOM/UTF16/duplicates/noID | startup | Same repair/read diagnostics and canonical mapping; damaged data not silently overwritten | storage parity/fault injection | A2,A3 |
| S4 External writer during init | edit/delete/rename/atomic replace before/after snapshot or migration persist | Newer content not silently overwritten by retained probe; existing raw replay/source guards preserved | deterministic watcher races+UI refresh | A3,A4 |
| S5 IO/migration persistence failure | read/backup/write/report denied | Prior diagnostic or fatal recovery, no successful ready claim | faults and UI loading/error checks | A3,A4 |

### 6.4 State / Interaction Matrix
| State | Trigger | Result | Edge |
|---|---|---|---|
| Loading modern | raw snapshot proves modern | one buffer→typed graph | bytes remain unchanged |
| Loading unknown/legacy | predicate cannot prove modern | independent fresh legacy read→old migrate/skip→typed buffer | stale initial probe never writes |
| Fresh fallback unavailable | read error | fresh authoritative read/diagnostic | never treat missing as old cached task |
| Preparing | fatal backup/write/report | abort, no new graph publication/ready | no swallowed failure |
| Snapshot captured | external raw change | existing invalidation/replay and hash conflict checks | no lost later generation |
| Graph ready | ordinary activation/first edit | previous UI/auto-save/reload | all eager Save/Reload behavior retained |

### 6.5 Decision Ledger
| Decision | Owner | Chosen | Confidence | Risk | Needs user before EXEC |
|---|---|---|---:|---|---|
| combine modern check+typed read | agent | per-file preparation, same buffer | .85 | parser/read ordering, mitigated contracts | No |
| legacy migration snapshot | agent | separate fresh legacy read, never initial probe | .95 | concurrent writer, keep old guards/replay | No |
| hidden/empty and errors | agent | preserve existing distinct selection/semantics | .9 | accidental migration/report regression | No |
| persistent cache/certificate | agent | not chosen; prior N5/N9 rejected | .99 | complexity/TOCTOU | No |
| absolute5/1/5 | user standing | unchanged; relative item not parent completion | 1 | false completion | No |
| shipping/merge | user standing | sequential scope only, no release/original install | 1 | premature delivery | No |

### 6.6 Runtime / Data Contract
| Area | Source of truth | Delta | Compatibility | Check |
|---|---|---|---|---|
| Modern JSON | exact graph-reader raw buffer | shared with predicate | full old modern classifier unchanged | parity + buffer/hash identity |
| Legacy | independent fresh read + existing JObject/atomic writer | preparation integration | backups/counts/history/unknowns preserved | byte/report and fault tests |
| Graph | existing canonical enumeration/SHA/generation | one prepared buffer | aliases/duplicates/repair/diagnostics unchanged | graph/watcher/race suites |
| Android | own APK/fixture/runtime manifests | source delta only | SDK401/AOTbothABI/Avalonia12.1.1/3CPU/2560MB/GPUhost | actual final PE/native/ordinary UI |
| Original task-space | CLI only | execution results only | data never edited by benchmark | CLI read-back |

## 7. Алгоритмы / инварианты
Modern true exactly preserves existing HasModernTaskStatusJson behavior; no classifier optimization here. Processor executes once for each graph snapshot before typed validation, sequential canonical order. Source hash is from the same bytes typed parser consumed (original bytes for in-memory repair, fresh legacy bytes or successful normalized persisted bytes). No second source read on modern path. No global atomic snapshot promise; CLI lock and physical watcher/raw-event boundaries remain distinct.

## 8. Интеграция
Combined call only in raw-watcher initial BuildInitialTaskViewsAsync; no graph callback reuse for later saves/reloads/reconcile. Old private migration wrapper remains callable with same signature for existing tests/non-raw flow. Existing final reverse/availability migrations retain fresh graph/guarded write behavior.

## 9. Модель состояния
Only ephemeral per-initialization migration context and transient byte buffer/read result. No new persisted metadata/cache/journal/config or public task API. Protected storage extension is narrowly scoped; no public new end-user feature.

## 10. Migration / rollout / rollback
Candidate branch/source builds/private own package only. Existing migrations remain their sole writers; no backfill/activation on original installed apps. Revert this experiment on failed correctness/retention gates; retain only useful generic regression tests that still reflect baseline. No merge/release while parent acceptance open.

## 11. Acceptance / validation
- A1: modern graph initialization shares one raw buffer with predicate/typed parser/hash; processed task set/order/count/relations/error mappings unchanged, no task writes.
- A2: legacy/mixed/new legacy, BOM/UTF16/comments/trailing data/case/duplicate history/invalidnumber/noID/hidden/empty/aliases preserve old status predicate/migration/report/backup/repair semantics.
- A3: modern snapshot/hash conflicts and raw generation replay preserve their existing protections for external update/delete/rename/replace/corrupt/unreadable changes. Legacy edits before fresh fallback and after migration persistence are covered deterministically; the existing fresh-read→atomic-replace race is explicitly unchanged (no CAS promise). Stale initial probe never supplies a migration write; backup/write/report failures remain fatal and unpublished. No unknown partial result called success.
- A4: relevant existing+updated Headless UI tests and native ordinary Loading/root/card/edit/reload flows pass with actual rendered evidence; current exact candidate full CI Main+Headless/Android packaging/CodeQL pass. Targeted baseline first; after correctness full runs once, repeat only on new failure/change.
- A5 retention: fixed three profile pairs B/C,C/B,B/C with full comparable snapshots (not the old60s-truncated prefix). For every scalar metric use paired r_i=C_i/B_i and median(r_i), never ratio of medians. Primary combined status+graph wall median(r)<=.85, combined process allocation median(r)<=1.05; wholeVM wall/alloc median(r)<=1.05. Then three ordinary pairs same order: usable-root conservative r_i=C_upper_i/B_lower_i from retained host brackets, median(r)<=.85; first-card duration r_i=C_i/B_i, median(r)<=1.10 and max(r)<=1.25; all actual root/card/3094/cleanup verified. Missing primary phase, real failed UI or correctness failure not replaced by retry. Individual process counters overlap, never sum nested spans.

Extension decision is recorded before pair4: pairs4/5 (C/B,B/C) are allowed only if a timing-median gate failed and MAD(B_i)/median(B_i)>.15 for that same failed timing metric (root uses B_lower; other durations use exact B_i). All correctness/completeness/allocation gates and first-card max(r)<=1.25 must already PASS; an allocation hard-fail, missing trace or failed UI forbids extension. If several timing-median gates failed, each must satisfy its own baseline noise condition. Both added pairs are mandatory and all the original thresholds are re-evaluated on all five paired ratios; no optional stopping/subset selection. Without permitted extension, reject on any failed gate. If five still fail or are incomplete, no retention.
- A6: absolute cold<=5s/live resume<=1s/available local changes<=5s including cold/resume remain task-level acceptance; unmet means no completion/merge/task2/3, even if A5 passes.

Paired protocol before captures: same own package/3094fixture/settings/SDK401/default2ABI/AOT/framework/hardware; fixed warmup pair0 outside stats; no forced GC/host build/tests/media during capture. Preinstall each APK before stable live foreground/no ANR qualification; newPID per measured cold. Actual PE/native hash/source manifests before capture; own AVD/watchdog bounded45m, no phone requirement. Profile marks combined phase consistently in baseline/candidate; full Core deadline/no dropped/expired/missing primary phase accepted. Ordinary final APK must contain no observer/Probe. All failed/incomplete attempts retained. Native video PTS is not host latency; current owned draw brackets/full manual root/card inspection required.

### Acceptance-to-Test Matrix
| AC | Automated test/check | Native/manual | Evidence | If missing |
|---|---|---|---|---|
| A1 | modern no-write; buffer identity; read/preparation count; source-hash same snapshot even physical file changes after read |3094same root/card | targeted TRX + source/profiler audit | blocks retention |
| A2 | TaskStatusMigration/FileTaskStorageReadContract/Unified migration parity incl nonraw+hidden/empty/reports | legacy modern UI startup | TRX/PNG | blocks retention |
| A3 | deterministic read→external change, persist→external replace, raw generation during init; injected source/backup/write/report errors | external update after initial load | race tests/raw hashes/UI | blocks retention |
| A4 | relevant SingleViewStartup + migration UI/edit/reload coverage; full exact candidate CI | inspected before/after ordinary video/root/card | TRX/CI/PNG/MP4 paths | no UI PASS without native evidence |
| A5 | frozen-source profile/ordinary3pairs/raw metrics/MAD/retention script | full native flows in original order | rawJSONL/metadata/audit | no gain if incomplete |
| A6 | final task-level cold/resume/freshness protocol | emulator-only, no Fold6 claim | parentSPEC/taskresult | prevents task closure |

## 12. Риски / возражения
| Likely objection | Why | Mitigation | Status |
|---|---|---|---|
| Stale snapshot could overwrite agent edit | new migration/graph integration | initial probe read-only; fresh legacy fallback; raw replay and separately guarded reverse/availability writes; before-fallback/after-persist race tests. Status fresh-read→atomic-replace race remains baseline, without CAS | bounded unchanged residual risk |
| IO exception silently skips migration write | graph loop catches IOException | separate read-result boundary; processor persistence errors fatal | mitigated plan |
| Hidden/empty/malformed files or report counts change | enumerators differ | explicit non-graph migration path + parity cases | mitigated plan |
| Less allocation but slower first useful action | prior lazy experiment failed | mandatory profile then ordinary A5 gates, rollback | mitigated plan |
| Full5s still not reached | platform+VM costs remain | no parent closure/merge until A6 | mitigated |

Rework checklist: user scenario/evidence/assumed decisions/objections mapped above; role review below pending. AC are verifiers, not authoring checklist. No implementation until review/fixes PASS.

## 13. План
Characterize relevant baseline; implement isolated combined path/tests; targeted+Headless+full candidate CI and independent implementation review; freeze packaging/source/protocol; paired profile gates; only if pass ordinary gates; retain or revert; update CLI/SPEC/PR. Next tracker task only after parent completion/merge/current main.

### Planned commands / evidence
PowerShell in this worktree; `$evidence` is a fresh absolute DROOT/single-read-candidate-tests directory. For baseline use the unchanged HEAD and separate baseline paths. First class-OR filter produced zero tests and is retained as failed invocation; exact-class commands below were verified individually.
```powershell
$evidence = 'D:\CodexArtifacts\android-task-loading-20261012\single-read-candidate-tests'
foreach ($testClass in @('TaskStatusMigrationTests', 'FileTaskStorageReadContractTests', 'UnifiedTaskStorageMigrationRegressionTests', 'FileStorageTaskStatusTests')) {
    dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --treenode-filter "/*/*/$testClass/*" --report-trx --results-directory "$evidence\$testClass"
    if ($LASTEXITCODE -ne 0) { throw "Targeted test failure: $testClass" }
}
$env:UNLIMOTION_LOADING_MIGRATION_EVIDENCE = "$evidence\ui"
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --treenode-filter '/*/*/SingleViewStartupUiTests/*' --report-trx --results-directory "$evidence\startup-ui"
if ($LASTEXITCODE -ne 0) { throw 'Startup UI failure' }
```
New coverage: prepared-buffer snapshot/hash/parse and null reread; fatal processor/completion errors; legacy before-fallback and after-persist raw replay; hidden/no-ID/report parity. Existing SingleView mixed-status cases modern1/130 will run both watcher:false and watcher:true; add first edit/reload/external refresh to the combined-path UI case. Baseline actual PASS31+12+7 at DROOT/single-read-baseline-tests2/3/4; baseline UI2 at single-read-ui-baseline1. Zero-test invocation1 is not a pass.

Full candidate follows actual CI owner commands in `.github/workflows/tests.yml`:
```powershell
foreach ($testProject in @('main', 'headless')) {
    foreach ($stage in @('restore', 'build', 'test')) {
        ./scripts/ci/Invoke-TestStage.ps1 -Stage $stage -Project $testProject -ResultsRoot "$evidence\full"
        if ($LASTEXITCODE -ne 0) { throw "Full stage failure: $testProject/$stage" }
    }
}
git diff --check
git rev-parse HEAD
git rev-parse 'HEAD^{tree}'
gh run list --branch perf/android-e74c5d1a-task-loading --limit 12 --json databaseId,headSha,name,conclusion
```
After candidate commit/push, require current-head All tests/AndroidPkg/CodeQL; download exact run via `gh run download RUN_ID --dir FRESH_ABSOLUTE_DIRECTORY`, inspect raw Main/Headless TRX, stage exit/invocation/checkout tree equivalence (same audit method as DROOT/audit-ci-08225864.py, with candidate pins). No previous-head CI substitutes. Local source/build failures stop native captures.

Android packaging/collectors are prepared after candidate source exists and reviewed/frozen before capture: dedicated baseline/candidate source archives; SDK10.0.401 shadow; Release/AOT both ABI/own package; exact raw source+helper+APK+actual8PE/native hashes. Adapt the already reviewed DROOT/publish-android-core-deadline1.ps1/finalize/collector helpers to full comparable combined-phase traces and no-observer ordinary APKs; do not execute an old helper against new source without new pins/review. Exact new helper invocation and output names are recorded in this journal before first capture. Build with own QEMU stopped; no host builds/tests/media during native capture. Missing hash/phase/PID/root/card/fixture/cleanup proof blocks acceptance; preserve failures, no replacement trial.

## 14. Открытые вопросы
User-owned questions none; callback exception/source-byte/report ordering must pass reviewer before EXEC. Implementation details may be tightened without changing outcome; new persistence/CAS architecture would require SPEC amendment.

## 15. Соответствие профилю
.NET desktop+UI/performance/migration: measured baseline, old-data parity, negative fault/race tests, native UI evidence, paired retention/rollback, full CI, independent review. No unsolicited deployment/security work.

## 16. Файлы
| File | Delta | Purpose |
|---|---|---|
| src/Unlimotion.FileStorage/FileTaskStorage.cs | protected prepared initial graph read; shared bytes parse/hash; read error boundary | remove duplicate modern IO without affecting default reads |
| src/Unlimotion/FileStorage.cs | internal preparation bridge/canonical predicate | keep migration policy out of storage |
| src/Unlimotion/UnifiedTaskStorage.cs | migration context/fresh legacy path/combined raw-watcher init | preserve legacy/report semantics |
| src/Unlimotion.Test/TaskStatusMigrationTests.cs and related migration/storage contracts | parity/fatal/race/buffer tests | migration/read safeguards |
| relevant existing startup Headless UI tests | modern/legacy first load/edit/reload/external refresh | UI MUST |
| specs/2026-10-15-android-single-read-initialization.md and parent journal | results/decision | traceability |

## 17. Было→стало
| Area | Before | Candidate |
|---|---|---|
| modern | raw migration read + graph read | same graph buffer used for check/parse/hash |
| legacy | fresh status read/write + graph read | initial probe discarded; fresh status read/write; parse fresh or written bytes |
| default reader/nonraw | old path | retained |
| errors/hidden/report/watcher | old contracts | preserved via explicit boundaries/tests |

## 18. Альтернативы
Persistent certificate/runtime cache already rejected N5/N9; no replay. Reusing a migration-wide stale raw/model cache risks external overwrite and is excluded by earlier SPEC. Repeating directory-worker/lazy-command experiments has no new basis. Chosen per-file preparation shares only a modern read buffer; legacy writes use fresh reads, avoiding a new cache/provenance subsystem.

## 19. Quality gate / review
Root authoring check: scope/AS-IS/design/data invariants/AC/negative tests/profile/dependencies filled; independent review pending. No PASS asserted yet.

### Linter / rubric
Author recheck after three independent MEDIUM corrections; these are pre-EXEC plan assessments, not implementation evidence. Final independent stop verdict pending. User-visible completion is A4/A6, not compilation alone.

| # | Result | Concrete basis |
|---|---|---|
| 1 | PASS | §§1/11 same usable root and first action, relative experiment vs parent absolute criteria |
| 2 | PASS | §2 actual PID4336 trace and inspected three source files |
| 3 | PASS | §3 duplicate modern physical snapshots |
| 4 | PASS | §4 one buffer, fresh legacy, no persistent cache |
| 5 | PASS | §5 no search/lazy/schema/lifecycle expansion |
| 6 | PASS | §6.1 base storage/bridge/migration policy responsibilities |
| 7 | PASS | §§6.2/8 raw-watcher startup only; default readers retained |
| 8 | PASS | §§6.2/7 same-buffer parse/hash, null reread, distinct selector handling |
| 9 | PASS | §6.2 recognized read failures separated from fatal persistence/report |
| 10 | PASS | §11 fixed paired ratios/conservative root/MAD/no hard-fail extension |
| 11 | PASS | §9 ephemeral only, no new persisted state |
| 12 | PASS | §6 legacy encoding/report/backups and explicit unchanged non-CAS boundary |
| 13 | PASS | §10 revert failed experiment; own package and original data isolation |
| 14 | PASS | §11 A1–A6 measurable and no premature parent closure |
| 15 | PASS | AC matrix includes modern/legacy/fault/race/raw-watcher UI/native checks |
| 16 | PASS | §13 verified exact-class commands/full CI stage commands/provenance/capture stop rules |
| 17 | PASS | §13 review→code→tests→frozen native profile→ordinary→retention→parent |
| 18 | PASS | §6.5 decisions and §14 no human-owned questions; CAS excluded |
| 19 | PASS | §0 expanded for migration/multi-module/performance risk |
| 20 | PASS | §15 desktop/UI/performance independent review/test/evidence requirements |

Author rubric after fixes: цель/границы5 (§§1/5/11); AS-IS5 (§2/source/trace); дизайн5 (§§6–9); безопасность/совместимость/rollback5 (fresh legacy/no-CAS truthfulness/§10); проверяемость5 (§§11/13 commands and evidence); автономность5 (ledger/§14/standing autoapproval). 30/30 is author readiness only; independent findings and phase gates override the score.

### Role-Based Review
| Role | Applies | Question | Verdict | Required action |
|---|---|---|---|---|
| Domain | Yes | modern/legacy/report behavior retained? | SPEC PASS | actual contracts/fresh fallback reviewed |
| UX | Yes | same list/card/actions without delayed regression? | SPEC PASS | raw-watcher false/true and first-action plan reviewed |
| Tester | Yes | every AC/fault/race has evidence? | SPEC PASS | negative cases/exact commands/numeric gate reviewed |
| Architect | Yes | fresh fallback/no duplicateIO/no stale migration write? | SPEC PASS | callback/read-error/source boundary reviewed |
| Delivery | Yes | isolated sources/CI/rollback/no premature merge? | SPEC PASS | source pins/CI/retention/parent gates reviewed |

### Post-SPEC
PASS15Oct23:02 after independent /root/startup_profile_review and targeted re-review. Scope/evidence: actual SPEC, three storage files, migration/read/UI contracts and09-14 invariant, baseline31+12+7/UI2 and both rendered PNG inspected by root. Reviewer commands/tests/build/ADB not run; behavioral read-only in writable sandbox, no technical isolation.

Passes: scope/evidence bounded to initial raw-watcher read; contract preserves fresh legacy/nonraw/selection/errors/report and parent A6; adversarial tested stale-probe vs non-CAS boundary, callback exception swallowing, watcher:false false-positive UI coverage and ambiguous numerical gates. Domain/Architect, UX/Tester and Delivery role verdicts PASS for the plan after fixes. Independent linter1–20 PASS, rubric30/30. Depth includes unrelated scope, every AC/scenario/objection, actual vs planned evidence, hidden API/operations/behavior, comments/docs and manual challenge: could a claimed atomic status write still overwrite a concurrent legacy edit? Yes, existing fresh-read→replace race is explicit and no new CAS claim remains.

Findings: MEDIUM non-CAS overclaim fixed §2/6/11/12; MEDIUM ambiguous paired/MAD/root numerical gate fixed §11; MEDIUM missing exact commands/raw-watcher path fixed §6/13. All three independently re-reviewed closed. No new findings: source/plan boundaries and negative cases substantiate the verdict. Residual actual perf/native/Fold6/absolute acceptance unknown; they block later retention/completion, not this bounded implementation. Stop decision PASS: autoapprove SPEC and begin code; no human question.

### Post-EXEC
Partial implementation checkpoint15Oct23:09, no retention verdict. Product3files/related4testfiles reviewed independently /root/startup_profile_review: focused source PASS, no mandatory findings, writable sandbox/behavioral read-only. Same-buffer modern, fresh legacy/null, fatal callback/report, completion-before-publication, selection/casing/counts/generations/sourceSHA checked.

Actual validation: product Release build PASS; TaskStatusMigration40/40; ReadContract20/20; UnifiedMigrationRegression8/8; FileStorageTaskStatus47/47; SingleViewStartupUI9/9. New regression initial7/8 failed because test incorrectly demanded exact JSON after subsequent legitimate availability normalization; corrected to require external title+unknown field and exact original backup. A missing Newtonsoft.Linq test import then caused one build failure; fixed and8/8 rerun PASS. Failed logs retained, no false green claims. Root inspected baseline2PNG and candidate raw-watcher startup/edit-reload4PNG: same actual list/statuses; selected task shows saved/reloaded external title. Private DROOT/single-read-candidate-tests1, single-read-candidate-tests3/regression, single-read-candidate-ui1; baseline tests2/3/4 and ui-baseline1. Headless PNG fallback: this existing unit harness captures rendered PNG, not video; native paired Android videos still required.

Full current candidate CI/packaging/paired profile+ordinary gates are not yet executed; actual performance unknown. Parent A6 unmet; no merge/completion/task2/3. Final expanded PostEXEC contract/scenario/AC/role/depth review remains open until these gates.

## Approval
Автоматически подтверждено15Oct23:02 по прямому поручению пользователя после independent review/fixes PASS. EXEC разрешён в указанном scope; retention и parent completion требуют последующих evidence/gates.

## 20. Журнал
| Фаза | Решение | Evidence / остаток | Следующий шаг | Human | Артефакты |
|---|---|---|---|---|---|
| SPEC | share modern graph buffer; fresh legacy fallback; preserve errors/selection/report/watcher | actual17.07s status+27.50s graph; no implementation yet | independent expanded review | standing autoapproval | thisSPEC/core-deadline audit |

### Paired preparation15Oct23:40, before first app launch
Product363acfbc committed/pushed; full local Headless66/66 PASS in addition to40migration/20read/8regression/47watcher/9startupUI. Full current CI38799704681 Main step completed, Headless/report stages pending raw audit; AndroidPkg38799704721/CodeQL38799704691 PASS. No overall CI claim until raw1567/66/tree/invocation/stage audit.

Independent prebuild review corrected shared-shadow concurrency: all8 publisher/finalizer helpers hold one exclusive FileStream lease for source-copy/build or check/decode/freeze, with finally release. Actual lock contention/release controls PASS; each variant publish+finalize completed before switching source. Baseline product082 vs candidate363 differs only3product CS; each406 source snapshot verified against Git blobs. Profile APK baselineF1952E9AFB0C6592C343825D2AAA3C85EEA45B3A2F5755A3909D413B64A9E84A and candidateC5346065C752B5AFB62F8727A672863583575E8B6AD2CFA09E564FC5B2B8250B independently PASS: SDK401/AOT2ABI,10actualdecodedPE including changed FileStorage,12native unchanged, variant method/type presence, eager VM/bootstrap/observers/noProbe. Saved all10linked PE snapshots per variant before next source switch. Plain helpers prepared but plain APKs are not built; ordinary stage requires profile gates first.

Private actual helper set DROOT: measure-single-read-startup1.py, preinstall-single-read1.py, run-single-read-profile1.py, single_read_trace_validation.py, single_read_retention_gates.py, freeze-single-read-capture1.py. Core deadline180s/no heartbeat; accepted Core must be full/unexpired with exactly comparable initialgraph.begin→end (baseline status+graph, candidate prepared read). VM deadline120s unchanged. WholeVM guard explicitly connect.begin→loading.false; storage.init remains separate, never substituted. Dependency SHA checks include both imported validators before ADB/after collection.

Review findings fixed/re-reviewed: wholeVM boundary, transitive dependency pins, forbidden first3→five rescue, unconditional owned-child cleanup and complete warmup admission. Current controls: trace3=2positive/18negative; retention4=7actual-finally-AST; preflight-binding1=10actual-preADB-AST; numerical2=14; driver1=16; observer harness4actualnormal/deadline/race/queued PASS. Old proof versions and failed preparation invocations preserved as history; current collector/proofs bind actual files, not the initial generation SHA. Independent scoped source/helper/artifact verdicts PASS, writable reviewer sandbox, no technical read-only isolation.

OwnAVD25 booted API34/NexusLauncher/benchmark PID absent. No app launch/trial yet. Before warmup: full CI audit→actual single-read-capture-freeze1.json. Fixed p0B/C excluded; measured p1B/C,p2C/B,p3B/C. Each APK preinstalled before7checks≥60s launcher qualification, proof freshness≤30s/QEMU/APK bound. Warmup inspected PNGs/actual complete metadata gate measured starts. PNG-only contact composition is permitted between stopped trials with own app confirmed absent; each next trial repeats60s preflight. Video decoding remains after own QEMU shutdown; no host builds/tests/media while native capture runs. No failed measured trial replacement, no performance/retention/parent5/1/5 claim before actual gates.

### Native admission15Oct23:56
Full exact-head CI363acfbc now independently audited PASS: run38799704681 Main1567/1567 + Headless66/66, zero failed/error/skipped; raw root TRX, invocation SHA/run/project, restore/build/test exit0 and checkout tree8f0e4f37==head tree verified. DROOT/ci-363acfbc-root-audit.json allPassed:true. AndroidPkg38799704721/CodeQL38799704691 PASS.

Frozen protocol SHA80b315dce6286a3563742c10d7a5c970b4aabc5c0d6a10995f08a65ebb3f3a6b binds36actual inputs (34nonAPK +2profileAPK). Warmup p0-b/p0-c full flow/full trace/3094before-afterSHA/cleanup PASS, sameQEMU169692/different appPID4876/7882. Root inspected all32/28 PNG in original order plus full last Loading/root/card; correctUUID/24children/parents0/blocking0, no visible corruption. Videos retained/hashverified, not decoded while ownQEMU alive. Warmup review binds actual metadata/audit SHA; independent admission review PASS, no blocker. Warmups excluded from statistics.

Measured stage started continuous ownAVD25 with fixed p1-b,p1-c,p2-c,p2-b,p3-b,p3-c. First baseline trial complete; no paired/retention verdict yet. Parent5/1/5 remains open, Task1 InProgress/Active updated and read back through CLI; PR325 draft/no merge/task2/3. No physical Fold6 claim. During capture no host builds/tests/media or extra ADB.

### Profile cohort16Oct00:17 — numeric PASS, ordinary pending
Actual ownAVD25 completed all frozen measured trials p1-b,p1-c,p2-c,p2-b,p3-b,p3-c without substitution. Full unexpired Core and VM traces, owned current draw, exact3094SHA before/after, sameQEMU169692/sixfreshPID, complete app/recorder cleanup PASS. Warmups excluded. Root inspected all138PNG observations in original order and12full root/card frames: exactUUIDa5ef0164-8def-417b-9b7d-d235516dd8c7,24children,parents0/blocking0; no visible corruption. Actual manual receipts bind metadata/audit SHA.

| Fixed pair | InitialGraph B/C ms | WholeVM B/C ms |
|---|---|---|
| 1 B/C |21738.8578 /17239.1928|51946.091 /45381.94|
| 2 C/B |38635.3157 /23359.2334|78239.0975 /62003.9198|
| 3 B/C |34788.045 /18183.606|78381.2885 /49077.1944|

Frozen median paired C/B: initialGraphwall0.6046083221212037 (-39.54%), allocation0.8634043166191867 (-13.66%); wholeVMwall0.7924927789459739 (-20.75%), allocation0.9825731100130629 (-1.74%). All4profilegates PASS, no extension. Allocation counters are process-wide, not exclusive causal attribution. DROOT/single-read-profile-paired-audit1.json verifies exact frozen gate bytes/controls, complete ordered driver and manual/preflight/metadata bindings. Independent /root/startup_profile_review recomputed rawtrace metrics/invariants and gave PASS admission to ordinary stage. This is instrumented-profile performance only, not ordinary gain or retention.

Guarded ownshutdown: exact fullCIMidentity169692 matched then adb -s emulator-5580 emu kill; bounded wait proved absence, launcher/watchdog exit0. DROOT/single-read-ownavd25-exit1.json. No capture during later media work. Videohelper1 rejected nullmuxer duplicateDTS despite exit0; helper2 rejected FFmpeg9 incompatible -r/nonCFR options before decoding. Both failed analysis outputs and exact rawstderr preserved; no capture retried/replaced. Diagnostic2 on unchanged p0-bvideo with output-only setpts=N/TB, no -r, strictxerror/err_detectexplode passed exit0/empty stderr. Helper3 review/full8video integrity+lastframe manual review pending; raw original PTS retained and never used as latency.

Task1 CLI result updated/readback InProgress/Active. ParentA6/cold5s/resume1s/localfreshness5s remains open; ordinary observer-free final APK proofs/freeze/paired root/card gates remain mandatory before A5retention. PR325draft/no merge/task2/3. PhysicalFold6 untested.

### Profile media16Oct00:24 — PASS
Actual reviewed helper4 decoded all8unchanged videos with -xerror/err_detectexplode, normalized timestamps only on discarded null-output, originalsourcePTS retained. All streams/frame outputs540x1200 match frozen screenrecord setting; probe/decode/lastframe stderr empty, hashes match captured metadata. All8full native-lastframes/contact inspected by root: exactUUID/24children/parents0/blocking0, same settled card as PNGflows. All video frames decoded for integrity; only extractedlastframes and138PNG manually inspected, no claim of watching every video frame. DROOT/single-read-profile-video-review4/audit.json and single-read-profile-video-manual-review1.json bindactualSHA. Independent scoped artifact/receipt/rawlogs review PASS, ordinary admission retained.

Postprocessing1/2/3 failed artifacts remain: nullmuxer roundedDTS collision; incompatible FFmpeg9 -r/nonCFR flags; incorrect helper1080x2400 assumption (actual recorder540x1200). These are analysis-only preparation failures on unchanged sourcevideos, not discarded or rerun measured trials. Fullrawstderr/partialstep for2/3 retained; helper1genericassert plus diagnostic1 exactstderr. Diagnostic2 verified corrected decode command on samep0bSHA beforehelper4 fullrun.

Baselineplain SDK401/AOTbothABI publisher started after ownQEMUexit/media gate. No ordinary native trial yet. Plain driver/source/freeze plan now in independent review; actual22AST cleanup/warmup/profile+media-admission controls PASS. All ordinaryinputs still must be finalized/frozen before newownAVD26 captures. Task1/retention/parent status unchanged.

### Ordinary packaging16Oct00:34
Baselineplain publisher/finalizer/snapshot completed: APK3305FFCEFEB63C16BBA8911D3CC89AEB7DC43E32FA9E58805D0CB57D65C4A9D8, actual10PE/12native. Independent artifact PASS: decoded10PE==savedlinked, noobserver/noProbe/no candidatepreparedpath; eagerVM/Gitbootstrap preserved; source406==baselineGitblobs/snapshot,17prebuildinputs/stateproof/SDK401AOTbothABI/ownpackage verified. Snapshot completed before nextsourcecopy.

Candidateplain publisher/finalizer/snapshot completed: APK8C8452CBB4AE5781232458292D1CECE878CDCF305B4B58EF7E7EFF38CE3CF390, actual10PE/12native and saved10linked. Independent candidate artifact review pending. Existing build warnings retained (package/version/page-size/duplicate-native entries), not a claim of new Android16/physical compatibility; nativebytes comparison remains mandatory.

Ordinary protocol/source/readiness independently PASS: run-single-read-plain1.py,22actualAST controls (ownedchild cleanup6/warmupadmission10/profile-media admission6), review-single-read-plain-pngs1.py, freeze-single-read-plain-capture1.py. Scoped ordinary audit/media helpers PASS; LOW docstring corrected to ordinary. Fixedp0excluded/p1B-C,p2C-B,p3B-C/sameQEMU26/45m externalcontroller/nohostbuild-tests-media duringcapture/no replacement. New actualfreeze still pending, noordinary app launch yet. Root conservative C_upper/B_lower<=.85; firstcardmedian<=1.10 andmax<=1.25 unchanged; no profile metric substituted for ordinary behavior.

### Ordinary admission16Oct00:39
Candidateplain independently PASS actual10PE==savedlinked/12native unchanged/source406==Git363/snapshot/all17inputs/stateproof/noobserver&Probe/eagerVM/bootstrap/SDK401AOTbothABI/ownpackage. Actual ordinaryfreeze SHA73aab54b2f1eb9149ec134b52d9bf28cfcfa9100b8c8791b190f5da01d90140a binds47inputs including bothplainAPK/proofs, originalprofile/media/exit evidence,22drivercontrols and predeclared ordinaryanalysis/media helpers. Independent actual47hash/variant/admission audit PASS; no ordinaryresults yet.

OwnAVD26 launcher89918/watchdog39786 started; exactQEMU169544,parent158816,watchdog169388,ownAVD/port5580. Actualboot1/API34/3CPU/guestMemTotal2534336KiB/hardware-qemu2560MiB/1080x2400/hostGPU match profile. Globalmetadata ramMB2048/resourceVariant/recovery notes and olderconfig.ini are historical labels; saved actualhardware/guest are authoritative. Immutable DROOT/single-read-plain-controller-admission1 contains rawengine/watchdog/hardware and hash-bound audit, independently PASS. Watchdogdeadline22:20:41UTC; launcher's earlier45m bound starts≈21:35:22UTC, so effective controller stop≈22:20:22UTC. No extension of either bound.

Ordinary excludedwarmup driver55087 started p0-b/p0-c; no measured starts before bothcomplete/inspectionreceipt. No hostbuild/tests/media or extraADB during nativecapture. All original47inputs remain unchanged. CLI result5b readback InProgress/Active: profilePASS, all8videointegrity/all8native-last manuallychecked, plainpackaging/freeze/runtimeadmission and pendingordinary/retention/parent5/1/5. No merge/task2/3/physicalFold6claim.


### Ordinary final16Oct01:12 — NO-RETAIN; selective rollback in validation
Frozen3pairs completed without substitution: root conservative C_upper/B_lower ratios0.8921979456,1.0201452838,0.8915822223; median0.8921979456 exceeds0.85. Baseline relativeMAD0.00193376893 (0.1934%) is below15%, therefore extension4/5 forbidden. First-card median0.9164253979 and maximum1.0017142747 pass1.10/1.25. Insufficient required gain, not proof of a runtime regression. ProfilePASS does not override ordinaryFAIL. Independent reviewer recomputed raw125window brackets and exact ordered6PID/oneQEMU/freeze/3094/observerabsence/cleanup invariants: auditPASS, experimentNO-RETAIN.

Root inspected all143 measured PNG observations in original order plus12full root/card images and all8full video native-lastframes/contact; selectedUUIDa5ef0164-8def-417b-9b7d-d235516dd8c7/24children/parents0/blocking0. All8ordinary videos strictly decoded for integrity, probe/decode/lastframe stderr empty; original540x1200/PTS/source hashes retained. Not all video frames manually watched; PTS not latency. Artifacts DROOT/single-read-plain-paired-audit1.json, single-read-plain-measured-visual-review1.json, single-read-plain-video-review1/audit.json, single-read-plain-video-manual-review1.json. Later media receipt resolves the original immutable numeric audit videoDecodePending flag without rewriting measured evidence. Independent ordinary artifact/receipt reviewPASS.

OwnAVD26 exactQEMU169544 stopped with identity-guarded emukill; single-read-ownavd26-exit1.json proves absence, controller89918/watchdog39786 exit0 before media/tests. All captured data retained; no extra pair/retry/native substitution.

Per preapproved rollback policy restore only3productCS and FileTaskStorageReadContractTests exactly to08225864; remove3API-specific PreparedModern/PreparedLegacy cases and privateCreatePreparation helper. Preserve generic hidden/noID/malformed counts, fatalbackup/report, BOM/UTF16, externalafterpersistence regression and rawwatcher UI coverage. Prior retained EGL/nativeGit/disposed-save guard unaffected. Independent source-level rollback planPASS. Targeted/build/UI/full-CI rollback validation pending; no claim that candidate363CI proves restored source. Task1InProgress/Active, PR325draft, parentcold5s/resume1s/freshness5s open; no merge/task2/3/release/physicalFold6 claim.


### Post-EXEC rollback checkpoint —16Oct01:15
Scope/Evidence: actual approved SPEC, HEAD363->workingtree diff and082->workingtree diff,3productCS+reader tests exact Gitblob equality, retained generic migration/regression/startup UI tests, native profile/plain raw traces/gates/freeze/receipts/exit proofs, rollback TRX and4rendered PNG. Central performance/testing/review owners and local UI MUST apply. Independent /root/startup_profile_review behavior read-only in writable sandbox, no technical isolation; focused code/contract/media auditPASS, no findings.
Contract: S1–S6/A1–A4 correctness evidence survives but A5 performance retention FAIL; prescribed selective rollback performed. A6 parent5/1/5 remains open. No release/merge/task2/3. Rejected candidate and raw captures remain auditable. Product API/schema/runtime same as082; no unrelated product changes.
Adversarial: profilePASS cannot establish ordinary gain; stable baseline forbids extra pairs; metadata videos are540x1200 rather than PNG1080x2400; output-only PTS normalization is integrity processing, not measurement. No CAS/concurrent legacy write guarantee. Initial restore command had wrong read-test filename and failed before any mutation; inventory corrected to FileTaskStorageReadContractTests.cs, then exact restoration verified. These tool failures do not imply product failure. Hook name AfterPrepared misleading after rollback; renamed AfterEnumerated only, independent source reviewPASS, relevant regression rerun8/8PASS.
Role-based: domain workflow preserves modern/legacy/backup/report/unknown fields; UX inspected actual startup and saved/external-reload rows for1&130 with rawwatcher; tester checked raw37migration+12read+8regression+47watcher+9startupUI=113/113, zero fail/skip, rename8/8 rerun; architect checked no prepared API/context left, final reconcile/write guards retained; delivery checked parent open/draft/source rollback and no installed app replacement.
Depth checklist: scope/AC/scenarios/matrix/objections verified against NO-RETAIN policy; validation exact per-source bounded; unsupported perf/Fold6/fullrollbackCI claims absent; edgecases hidden/noID/malformed/BOM/UTF16/fatalbackup/report/externalafterpersist covered; docs historical records append-only, superseding disposition explicit; no hidden API/UI change remains. Manual challenge: current positive CI363 is historical and cannot validate rollback; require new exact-head raw Main+Headless/stage/tree audit. Next-best visual evidence for unit harness remains inspected PNG; actual paired Android flows have16 sourcevideos. Allframes integritydecoded, not all manuallywatched.
Findings: no blocking scoped rollback findings; misleading hook name fixed/re-reviewed. Residual: rollback fullHeadless and newhead fullCI pending, performance/physical/absoluteparent criteria unfulfilled. Stop decision: focused rollback reviewPASS for draft commit; overall parent incomplete; final validation closure pending CI. No human decision needed.


### Post-EXEC final16Oct01:43 — NO-RETAIN / rollback validation PASS
Exact rollback62ff9a9b pushed. Local targeted113/113+hookrename8/8+fullHeadless66/66 PASS; rawTRX audit single-read-rollback-local-audit1.json. New exact-head CI38815744332 Main1556/1556+Headless66/66 PASS, zero failures/errors/skips; rootTRX/stage restore/build/test exit0/invocationhead/run/project/clean tree verified. Checkout3e3e877c6b65c4a05220b1c4f0baef713dc91eaa tree5f82eb449bc117f157964336acc5398867884104 equals62headtree. DROOT/ci-62ff9a9b-root-audit.json allPassedtrue; independent actualraw/stage/GitHubGET auditPASS. AndroidPkg38815744314/CodeQL38815744307PASS. Prior CI363 is historical only.
Scope/Evidence final pass includes approvedSPEC, exact3CS/readcontract Gitblobrestore, retainedgeneric tests, newrollbackrawTRX/fullCI, all profile/plain freezes/gates/visual/media/exit evidence. Contract pass: A5ordinary retained-gain FAIL triggers prescribedrollback; A1–A4 correctness/provenance reviewed, A6parentopen. Adversarial pass: no reuse of profilePASS asordinaryproof, no stable-noise extension, no allvideo-manualwatch or physicalFold6claim; preparedAPI/context absent, genericregressions preserved. Role final: domain migration/unknownfields/backup/fatalreports unchanged; UX actualraw-watcherstartup/externalreload and nativecardflows inspected; Tester new1556+66/rawstage checks; Architect restoredcode/source guards preserved; Delivery draft/no merge/no nexttask. All prior checkpointfindings fixed/re-reviewed, no new required findings.
Depth/No-findings justification: scope/AC/scenarios/matrix/objections/validation/unsupportedclaims/edgecases/docs/hiddenAPI/lifetime/nativeprovenance challenged with actualevidence already listed; finalrawCI covers restoredcode rather than rejectedcandidate. Source equality+retainededgecase tests limit regression risk; parent5/1/5 remains unmet and explicitly separate. Stop PASS for completing this rejected experiment and verified rollback; performance outcome NO-RETAIN, Task1 remains InProgress/Active. No unresolved work from this experiment masquerades as parentcompletion.
