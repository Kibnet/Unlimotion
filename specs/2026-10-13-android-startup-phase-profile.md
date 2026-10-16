# Локализовать стоимость actual Avalonia startup на Fold6

## 0. Метаданные
Expanded по canonical _template.md: два компонента и внешний diagnostic APK. Parent e74c5d1a; perf/android-e74c5d1a-task-loading от0b4c625a. Performance-optimization, dotnet-desktop-client, UI validation, QUEST/review. Runtime Codex desktop/PowerShell, unrestricted FS; reviewer только read-only по поведению, техническая sandbox isolation отсутствует. Пользователь разрешил последовательный EXEC и automatic SPEC approval после review/fixes. Model/API evaluation не применимы.

## 1. Цель / outcome
Получить timestamps, process CPU/allocations, thread identity и UI queue lag для настоящего startup. Success — определить доминирующие фазы и обоснованный следующий optimization item. Это диагностика; whole Android5/1/5 не закрывается профилем.

## 2. AS-IS
Actual own Avalonia baseline690d/candidatefc1 (product code совпадает с0b4) записаны по5 раз на folded968x2376/3094-task copy. Loader-visible lower bounds median16.261→16.509s, mean16.691→16.079s; readiness не измерена этим ROI. Frame gaps0.029..7.743s и wake/thermal/notification interference не дают устойчивого causal latency gain. Native decomposition не включает полноценно UI subscriptions/publication. CurrentCI0b4 rootMain1537/1537/Headless66/66 green; прежние failures сохранены.

## 3. Проблема
По aggregate/видео нельзя различить worker I/O/CPU, синхронные cache subscribers, queued UI work и финальную гидрацию. Оптимизация без такого различения рискует не ускорить исходный сценарий.

## 4. Дизайн
Граница — instrumented disposable APK org.unlimotion.taskloadingbench, собственная fixture/config, Git/updates off. Product checkout files не меняются. Точная копия двух исходников + helper вне Git под external MSBuild overlay. Strip-inserted-marker-lines должен вернуть исходные bytes/line endings; исходные SHA фиксируются. Timing points не изменяют awaits, Task.Run, migration/read/write/watcher order, batch64, UI subscriptions, state/guards/normalization.

## 5. Non-Goals
Не новый cache, не parser replacement, не оптимизация/ускорение, не original APK/config/task mutation, не релиз/merge/следующая задача. Не выдавать instrumented timings за честный uninstrumented before/after gain.

## 6. Решение и видимый сценарий
Core points: Init/worker start; status migration; live graph; migrations/pending sync; DTO snapshot; VM creation; relations; cache publication; reconciliation; Init end. Каждый cache batch: before/after synchronous Edit и before/after Task.Yield, с count. VM points: ConnectCore start; storage.Connect end; pipelines-ready; storage.Init awaited; AttachTaskContext loop end; RegisterCommands end; IsTasksLoading=false.

Helper internal, compiled отдельно в каждом из двух assemblies: process-monotonic Stopwatch ticks/frequency, UTC, PID, managedThreadId, optional processCpuMs/GC total allocation sample. Queue only numeric metadata/constant phase labels; никаких titles/descriptions/IDs/paths/credentials. CPU unavailable записать null, не0; process-wide allocations/CPU могут включать overlap и background work, nested phase durations не суммировать.

UI heartbeat только VM: Avalonia Dispatcher.UIThread.Post, period250ms, максимум один pending Post; callback подтверждает CheckAccess и фиксирует post/run ticks+queue lag. Captured SynchronizationContext type/null фиксируется отдельно; он не является доказательством UI thread. При недоступном dispatcher lag unavailable, не0. Timer ограничен120s/480 posts; очередь всех records максимум4096, metadata содержит dropped/expired. Session one-shot per assembly/process; accepting record и close/snapshot атомарны под одним lock. Timer callback и posted callback проверяют captured session identity/closed state; close отключает timer и не ждёт UI callbacks синхронно. Late callbacks игнорируются. Все helper entry points best-effort try/catch, ошибка trace не меняет exception/control flow приложения. Core и VM имеют разные output files, один monotonic clock. Core close/snapshot at Init end и VM close/snapshot в finally после снятия loader; сериализация/IO только background Task.Run, не на UI. Flush start/end/cost записываются, concurrent flush overhead может влиять на оставшийся startup и явно учитывается; cost не предполагается незначительным.

Storyboard: own launch → existing loader → existing roots → select/reveal existing card. Layout/copy/state сохраняются. Baseline inspected images/video используются как reference; profile run сохраняет own native video/PNG и markers. Inspector продолжает capture первого list frame и завершённого read-only action отдельно от loading.false.

| Решение | Owner / выбранное | Почему | Needs user |
| --- | --- | --- | --- |
| Mark actual two-component route | agent / sparse markers + bounded queue heartbeat | Worker-only probe недостаточен; без whole-system traces | Нет |
| Profiling overhead | agent / separate variant, process samples, max1 queued callback | Не claim causal improvement; помехи измерения явны | Нет |
| Phone available | user provides access; fresh guard checks | Не bypass lock/call, никаких security/timeout settings | Только actual availability при блокировке |

## 7. Инварианты
Граф/миграции/legacy/new incoming files/alias/case/hash/revision/watcher cutoff и защита writes остаются исходными. Log failures не скрывают product failures, нет recovery/fixture fixes ради trace. Guarded phone availability: exact Fold6/serial, unlocked/proximityfalse/no in-call, own package/display/files only.

## 8. Интеграция
External targets импортирует прежний own FileProvider overlay и заменяет Compile только для Unlimotion/Unlimotion.ViewModel при explicit diagnostic flag. VM-only внешний Reference Avalonia.Base использует уже resolved core Avalonia12.0.3 ref/net10.0 binary (asset path/hash в build metadata); direct Dispatcher API без reflection/trimming risk. Product csproj/package lists не меняются; ownAPK уже включает этот runtime assembly. Production/CI builds не импортируют этот файл. Build/publish sequential -m1/disable servers; original activity/package не запускаются экспериментом.

## 9. Данные и output
JSONL two modules: phase, monotonicTicks/frequency, utc, pid/thread, optionalCPU/allocation/count, heartbeat posted/run ticks. Phone output строго в /storage/emulated/0/Android/data/org.unlimotion.taskloadingbench/files/startup-profile-{core|vm}.jsonl; Environment.GetFolderPath(LocalApplicationData) fallback допускается только если path доступен own package, metadata фиксирует location. Release trace извлекается adb pull из own external files; если trace нет или он недоступен, AC3 не PASS, приложение продолжает обычную работу. Build metadata: source HEAD/file SHA, patch proof, SDK/build flags, own APK SHA. Collection metadata: display/thermal before+after, PID, task count, interference, video/markers. Всё private D:/CodexArtifacts/android-task-loading-20261012; в Git только SPEC/results без пользовательских raw records.

## 10. Rollback / cleanup
Удалить external overlay из future publish invocation и вернуть известный uninstrumented own APK. Source git diff должен показывать только SPEC. Helper wake bounded900s, stops on occupied; собственный stop marker завершает его. Сырые artifacts сохраняются; original release/data не трогаются.

## 11. Acceptance → checks
| AC | Evidence |
| --- | --- |
| AC1 source behavior preserved | Exact original bytes restored by stripping markers, checked insertion anchors/counts; diff no source/API changes; inspect helper exception/timer bounds |
| AC2 overlay isolated | Explicit project+flag conditions, no normal build import, correct own provider/package; build0errors + APK metadata/hash |
| AC3 trace actionable | Real own startup, samePID/core+VM clocks, phases/count3094, nullable supported metrics, batch Edit vs yield timing, queue lag; parse records/ordering without sums of nested durations |
| AC4 original visible behavior | Existing loader/list/card with expected copied task, actual native screenshots/video; profile UI error not ignored; scope relevant existing startup UI6/6+full CI0b4 and new rendered run (no product behavior change) |
| AC5 honest conclusion | Dominant phase hypothesis tied to observed intervals; uncertainty/overhead/interference preserved; no5/1/5 PASS/retention/merge from profiling |

## 12. Риски / counterexamples
Sampler can perturb timing: profile diagnostic only, do not compare instrumented vs uninstrumented as gain. UI blocked implies one pending callback, not unbounded flooding. Late heartbeat/flush races prevented with atomic session close/snapshot, bounded duration/record count and identity check; traceFile unsupported/CPU API throws → null/missing trace, no app change и AC3 incomplete. Compiler default item duplicate → build fails and repair own overlay. Profile currently unstable phone/thermal → label incomplete, preserve evidence, no bypass. Source-marker removal cannot prove helper scheduling is zero-cost; reviewer explicitly checks it as observer overhead.

## 13. План
Review/fixes → automatic approval → external generator/helper/overlay → static fidelity/guard review → build → safe own phone run → trace/visual inspection → independent evidence review → cause-specific optimization SPEC. Product code changes начинают только после следующего reviewed design.

## 14. Открытые вопросы
User-owned product decisions нет. Actual phone availability может измениться; проверять перед запуском. No marker output/unsupported metrics являются diagnostic limitations, не0/не PASS.

## 15. Profile compliance
Hypothesis здесь localization, не optimization gain. Existing uninstrumented5pair baseline сохранён. Future optimization targeted→full/paired checks обязательны. UI flow preserved and inspected; scopes/device/installed evidence различены.

## 16. Files
SPEC local specs; external copied UnifiedTaskStorage.cs/MainWindowViewModel.cs, internal helper(s), generator/overlay, APK/JSONL/video вне Git. Product source/test/public API untouched.

## 17. Mapping
Opaque aggregate loading → separate phase/CPU/queue observations. Original runtime behavior → same calls/order plus best-effort observer overhead. Missing metric → explicit unavailable/null.

## 18. Альтернативы
Ещё native worker probe не различает UI. Blind persisted-cache/batch change без cause не выбрано. Whole-system trace может захватить unrelated private activity, не нужен. Sparse points alone пропустят UI queue backlog; bounded heartbeat выбран вместе с ними.

## 19. Quality gate
Post-SPEC PASS: independent initial review5MEDIUM и targeted re-review PASS после исправлений; root adversarial fallback выполнен. Stop/flush/UI identity/output и expanded record закрыты в дизайне. Automatic approval13Oct13:53. Post-EXEC pending build/runtime/trace; wholee74 NEEDS-FIX.

## Approval
После post-SPEC PASS автоматически подтвердить по прямому user instruction; повторное human approval не запрашивается.

### Expected User Review Objections / disposition
- «Профиль сам тормозит UI»: writer вынесен в background; flush interval и sampling limits фиксируются. Overhead residual risk; вывод только о localization, не о gain.
- «Это работает на моих данных/приложении»: только own package/копия, external files extraction, no secrets in trace; original не запускается экспериментом.
- «Почему задача не завершена после green CI»: real cold lower bounds превышают5s; profile не закрывает5/1/5, next tasks не начинаются.
- «Loading false ещё не usable»: требуется отдельное inspected first-list/card action; без него AC4 incomplete.

### Full post-SPEC review record
Scope reviewed: этот SPEC; central review-loops/performance/QUEST; App single-view startup; UnifiedTaskStorage.Init/worker/batch64/reconcile; MainWindowViewModel.ConnectCore; actual5pair/private boundaries/current0b4 CI metadata. Planned changed files — external two shadow sources/helper/overlay only; source code generation ещё не начиналась.

Review passes: independent reviewer нашёл5 MEDIUM (lifecycle, UI identity, observer IO, phone output, expanded record). Root исправил plan: atomic accepting/close snapshot one-shot session;120s/480posts/4096records; explicit Dispatcher.CheckAccess + context metadata + posted/executed/suppressed counters; background measured IO; explicit own Android path/extraction; настоящий expanded record. Targeted re-review pending. Contract/adversarial проверили callback-after-close, writer failure, unavailable dispatcher/nullcontext, observer interference и missing trace. Проверки реализации: standalone helper lifecycle harness со stop/delayed callback/overflow/expired и writer-failure; отдельное APK runtime dispatcher proof. Harness не заменяет phone flow.

Role-Based Review Result: domain — original data/legacy/5-1-5 сохранены; UX — existing storyboard/private reference достаточен, loader false не first-frame/action; tester — lifecycle/error checks и completeness обязательны; architect — source fidelity не zero overhead, metrics process-wide; delivery/security — explicit flag/own package/paths, no public raw data.

Depth checklist: scope/API/cache drift отсутствует; AC1..5 доступны проверке; user scenarios/decision ledger/objections заполнены; CI отдельно от profile acceptance; unsupported gain/null-as-zero исключены; callback/flush/context edges уточнены; docs-only Git diff; original installed state не меняется. Manual-review challenge: не выдать starvation/observer IO/не-UI context за bottleneck. Root adversarial fallback выполнен из-за writable child sandbox: lock state snapshot нельзя изменить после close, UI не ждёт callback; max1pending с suppressed counts; incomplete trace блокирует AC3; package/fixture guards сохраняются.

Findings disposition: все5 MEDIUM исправлены в SPEC; root targeted Fix-and-re-review проверил lifecycle bounds/atomic close, direct UI identity, measured background IO, own output/extraction/incomplete, заполненные objections/roles/depth. Avalonia12.0.3 ref binary существует и public Dispatcher Post/CheckAccess проверены по локальной XML API documentation; checksum46EFFF1EA19689750944CD59510846631B18DDEA40BDF146239222FCE6195B41. Independent initial review и writable-sandbox fallback сохранены, дополнительное targeted independent re-review продолжается. No-findings justification не применимо: findings найдены и закрыты plan corrections. Stop decision post-SPEC PASS для ограниченного diagnostic EXEC, automatic approval13Oct13:53 по user instruction. Post-EXEC pending; parent e74 NEEDS-FIX.

13Oct: targeted independent post-SPEC PASS подтвердил все5 fixes и допустимость external direct Reference. Static EXEC reviewer обнаружил MEDIUM: heartbeat timestamps включали observer sampling. Исправлено: actual dispatch ticks непосредственно перед delegate/Post; callback entry ticks до Gate/check/sampling; ordinary sample ticks отдельно. Root проверяет numeric ordering в standalone harness, dispatcher proof всё ещё требует own phone run. External ApplicationId+flag+project guards проверены evaluated Compile items: enabled точно2 shadow/helper, originals removed; wrong package и disabled zero diagnostic items. Старый harness5/5 PASS; timestamp-case/review/build pending. Byte restoration двух copies независимо подтверждена.

## 20. Журнал
13.10: actual5pair evidence/reviewer показали отсутствие robust startup gain и missing actual UI phase localization. Этот design создан до instrumented code generation/build; source unchanged. Nativecold5 targetfailed, parentfirst task InProgress/Active, draftPR325/notmerged, next tasks unclaimed.

### Amendment: Android emulator по прямому user instruction
13Oct пользователь: «Используй эмулятор, телефон не могу пока подключить». Phone reconnect больше не блокирует дальнейшую работу. Диагностика переносится на новый own AVD codex_loading_profile_20261013, private path D:/CodexArtifacts/android-task-loading-20261012/avd-profile-20261013, reserved emulator-5580, available API34 google_apis_playstore x86_64,968x2376/420dpi/4cores/4096MB. Cold boot/no snapshots, no audio/camera, software SwiftShader и Vulkan off; WHPX available. Старые user AVD/data и host hypervisor/security settings не меняются. Own headless AVD подготовка авторизована user instruction, код приложения не меняется.

AC2/3/4 diagnostic runtime теперь могут быть выполнены реальным own APK в этом Android AVD: тот же dataset3094, package/provider/config/trace contracts, actual Android-rendered loader/list/card и Dispatcher callback proof. Guards exactserial emulator-5580 + ro.kernel.qemu/ro.boot.qemu + own AVD process/metadata + actualmodel/display; no Fold6 assumption. Measurement script/output names имеют emulator prefix, metadata API/ABI/config/boot/GPU; отдельный validator читает соответствующие filenames. Late lock/occupied/transport guard прекращает дальнейшие actions, marker/trace incompleteness не становится PASS.

Это смена diagnostic platform по user instruction, не доказательство physical Fold6 latency improvement. Existing Fold6 failed cold bounds/thermal/confounders сохраняются; будущие before/after сравниваются только на одинаковом AVD/APK/config/dataset. Product optimization scope/5-1-5 не ослаблены. Emulator host workload/virtual filesystem/GPU — residual limitations. Spec amendment targeted review pending; no original package/phone mutation, merge/completion до обязательных implementation/validation gates. При повторном QEMU0xc0000005 проверять own fresh config/renderer/version, не уничтожать user AVD/переключать Windows hypervisor и не возвращать зависимость от отсутствующего телефона.

Amendment targeted independent review PASS13Oct; automatically confirmed по user instruction. Own AVD boot_completed=1, exact name/QEMU1/API34/sdk_gphone64_x86_64/x64/display968x2376 verified. Initial offline transport recovered by adb reconnect offline при inventory containing onlyownemulator, no SDK/host hypervisor modifications; fresh AVD did not exhibit previous QEMU crash. Main profile APK publish exit0, own package/provider/ABI badging verified; source0b4c625a SHA B0C29B376D0FFE07AC067C19C7E4C262390F6385202BF209F670758C2AAF9D01,115994265bytes. Helper6/6 PASS includes corrected timestamp case. Static independent EXEC re-review PASS (sampling and ApplicationId guards closed). Full diagnostic EXEC still pending actual trace/visible flow; new own dataset transfer/run started.

15Oct resource correction: prior orphan trial1 am-start Status timeout/UNKNOWN, no completed host trace/video or metadata: not PASS. Source AVD D: maps to SATA HDD;4252-file5.3MB fixture push took312.474s. RAM backing snapshots/default_boot/ram.img4GB existed despite no-snapshot. Own closed AVD copies to C:/Users/Kibnet/.codex/android-avd/codex-loading-profile-20261015 on NVMe; source retained, snapshots/lock files excluded, copied boot/userdata <=2GB, require >=3GB free after copy. Only own .android/avd/codex_loading_profile_20261013.ini path changes. QuickbootFileBacked disabled per own invocation flag, no global feature/host hypervisor changes; stable before/after only on same new config. Boot host stays alive to drain redirected output; ownership verified before stopping own QEMU. Source storage/pipe-overhead hypotheses are test-environment issues, not proven application bottleneck. C free space below2GB stops further test work; do not delete unrelated files. Old D AVD and raw records retained; new output filenames/metadata distinguish attempts, no user AVD mutations.

### 15Oct emulator runtime evidence
15Oct13:35: attempt9 engine37.2.12 Qt-hidden -gpuauto actualGLES host/NVIDIA/CPU3/RAMactual2560/1080x2400 stable, same ownAPK0b4. PID2925 core216records/3094 complete152.394s: status38.906s/livegraph67.161/models15.432/cachepublication25.342. VM845records419actualUIcallbacks dropped0 expired120s; VMclosed17.913s BEFORE publication, no UIlag claim for publication/contexts/loadingfalse. Bothrawtraces pulled, rootlist attempt9-ready.png and selected Unlimotioncard attempt9-details.png inspected after actual taps. No Android fatal/deniedlock in capturedlog;188missingSSL still present. These are phase localization data, no productgain/physicalacceptance. Independentreview confirms limits; sameconfig freshPID/warmedOSFS attempt10 underway; Task1notcomplete/no merge/no nexttask.

