# Единая фоновая операция чтения каталога задач

## 0. Метаданные
Expanded / small storage optimization в текущей e74c5d1a, branch perf/android-e74c5d1a-task-loading, source0b4c625a. Пользователь поручил реализацию/CLI/merge после acceptance; SPEC автоматически подтверждается только после review/fixes. Central QUEST, review-loops, performance-optimization, dotnet-desktop-client, local UI tests MUST. Поверхность Codex/PowerShell Windows; model eval не применим: немодельная .NET оптимизация. Related: specs/2026-10-12-android-task-loading-execution.md; diagnostic profile SPEC; draft PR325. Данные/профили только private D:/CodexArtifacts/android-task-loading-20261012.

## 1. Цель и outcome
Уменьшить scheduling overhead initial live graph без изменения результата чтения. Исходный сценарий: Android долго показывает Loading tasks. Успех этого эксперимента: одинаковый граф/ошибки/изоляция/locks, UI остаётся отзывчивым; в сопоставимом Android AVD значимый повторяемый выигрыш livegraph и отсутствие total/alloc regressions. Это промежуточный эксперимент; цели родительской задачи cold5/resume1/freshness5 остаются незакрытыми до отдельной проверки. Stop: нет выигрыша/регрессия/недостоверные метрики -> откат product delta; не ослаблять acceptance.

## 2. AS-IS
FileTaskStorage.ReadDirectoryCoreAsync на каждом файле await Task.Run(DeserializeTaskSnapshot), затем на caller context выполняет cloning/index/diagnostics. EnumerateTaskFiles и initial setup сейчас до первого await. Public ReadDirectory/ReadGraph и EnableLiveGraph вызывают этот private reader под прежней directory lock. Android actual profile attempt10 PID4834 source0b4, API34/x64/engine37.2.12/hostGLES/CPU3/RAM2560/1080x2400: livegraph12.712s, status18.982s, models9.585s, publication17.004s, Init61.617s. Оба traces complete/dropped0. Попытка9 first OS run152.394s, VMexpired: не сравнивать её как product delta. Scheduling overhead пока гипотеза, доля не измерена.

## 3. Проблема
O(N) дополнительных Task.Run/await/resume для заведомо последовательного directory snapshot. На UI caller также O(N) continuation work. Меняем только scheduling boundary.

## 4. Цели дизайна
Один reader worker, прежняя сортировка и последовательная обработка, независимые serializers/converters, прежние defensive clones и hashes, исключения/repair/диагностика, удержание directory lock на весь operation.

## 5. Non-goals
Нет parallel parsing, persisted cache, schema/API/options, миграционных правил, status fastpath, watcher/reconcile, VM/pipeline/search/lifecycle изменений. Не менять original phone/user AVD/task-space. Нет release/install original app.

## 6. TO-BE
### 6.1 Владельцы
FileTaskStorage: private ReadDirectoryCoreAsync -> Task.Run(ReadDirectoryCore); новый private sync core сохраняет весь прежний loop/result. Public callers и lock owner без изменений.
### 6.2 Дизайн
Перенести всю enumeration/parse/hash/clone/index/result aggregation в один Task.Run, убрать внутренний per-file Task.Run. Сохраняется ExecutionContext/AsyncLocal flow. Lock callback ждёт worker до освобождения lock; reader не вводит mutation или callback UI. Порядок и per-file catch остаются прежними, внешние exceptions доставляются через faulted Task. Каждый файл продолжает создавать локальный serializer, immutable resolver общий как раньше. Worker sequential, без новых очередей/кеша. Visual storyboard прежний: Loading -> актуальный root list -> selected card. Before/after native AVD video или явный fallback с причинами; Headless screenshot реально просматривается.
### 6.3 User observable scenarios
| Сценарий | Ожидаемый результат | Evidence / AC |
|---|---|---|
| Startup смешанной modern/legacy коллекции >2 initial batches | Все задачи и статусы, overlay скрыт, карта открывается, modern bytes сохранены | Parametrized SingleViewStartup UI + native AVD / AC1,2 |
| ReadDirectory caller с UI context | IO и traversal не блокируют dispatcher, результат без потерь | UI flow + focused reader context/ordering checks / AC1,2 |
| Ошибка JSON/duplicates/unknown fields/external writer | Прежняя диагностика, политика unknown, lock isolation | existing read/transaction/livegraph tests / AC1 |
### 6.4 State matrix
Empty -> empty result. Corrupt -> прежний per-file LoadErrors. Duplicates -> прежний last sorted source winner + issue. Lock held by writer -> прежнее ожидание/timeout. Reader running -> фон, lock удерживается. Completion -> прежние public await/context и publication. Cancellation не добавляется (прежний API без token).
### 6.5 Decision ledger
| Решение | Owner | Выбор | Confidence/risk | Нужно user |
|---|---|---|---|---|
| Scheduling | agent | Один sequential worker |0.8: гипотеза overhead, только по замеру retain|Нет|
| Platform | user | Emulator пока нет телефона |1: нельзя выдавать за Fold6|Нет|
| Acceptance | user/task | Родительский5/1/5 сохраняется |1: эксперимент его не закрывает|Нет|
### 6.6 Контракты
Storage truth прежние task files, result hashes/diagnostics/sorted ordering. Locks/AsyncLocal прежний owner; отдельная regression проверка writer exclusion. JSON/date/enums/unknown/repair unchanged. Configuration/publicAPI/no new persistence. Effective runtime фиксируется в каждом APK/AVD metadata.

## 7. Инварианты
Последовательный порядок файлов с прежним StringComparer.OrdinalIgnoreCase; per-file catch только прежние JsonException/IOException/UnauthorizedAccessException; SHA того же byte snapshot; clone isolation; не делить mutable serializer. Directory lock освобождается после полного результата. Не выполнять IO/enum на UI.

## 8. Интеграции
Все прежние callers private ReadDirectoryCoreAsync получают новый scheduling boundary; GetAll fallback и отдельный Load не меняются. Review учитывает protected EnumerateTaskFiles overrides: это protected extensibility surface; обещания caller/UI affinity в просмотренных overrides нет, перенос off-UI затрагивает только directory reader, тестовые coordination gates должны оставаться корректными.

## 9. Модель данных
Не меняется; runtime worker на один snapshot вместо worker на файл.

## 10. Rollout/rollback
Отдельный commit только после доказанного выигрыша; draft текущей задачи до всей acceptance. Откат двух reader methods + tests-specific changes отдельным revert; task JSON не переписывается. Никаких новых versioned artifacts/migration.

## 11. Acceptance и validation
| AC | Проверка | Evidence |
|---|---|---|
|1 Результаты/locks/repair/duplicates/isolation совместимы|FileTaskStorageReadContractTests, FileTaskStorageTests, FileTaskStorageObservationTests, recoverable mutation/livegraph suites; fullMain|TRX exactsource|
|2 UI large mixed startup и успешное открытие работают|обновить SingleViewStartup_RendersModernAndLegacyTasks... параметрами1/130modern, проверить полную выдачу/статусы/байты/overlay; полный Headless + native ownAVD list/card|PNG/native video просмотр|
|3 Выигрыш значим и не ухудшает whole loading/alloc|до/после одной effectiveAVD geometry/engine/GPU/config, same fixture/hashes, обязательно минимум3 alternating pairs; фазовый instrumentation одинаковый, uninstrumented whole-flow также|private traces/metadata/summary; no physical claim|
|4 scope/review/delivery|independent review, clean related diff, каждый optimization отдельный commit; родительский5/1/5 остаётся отдельным gate|SPEC/review/PR/CLI|
Baseline existing exact0b4 CI Main1537/1537+Headless66/66 PASS и свежие actual profiles. Перед product edit baseline focused reader/UI запустить нормальной сборкой (текущие Release binaries ранее instrumented). После targeted -> fullMain/Headless. Не расширять тесты после passing без новых причин. Test commands по run-tunit-tests и существующему CI invocation; logs+timeouts сохранить. APK до/после разные подписанные ownpackage versions/sha metadata. При native/QEMU interruption run incomplete, не нулевое время.

## 12. Риски
Worker duration/ThreadPool fairness: один worker вместо множества; это не parallel increase, проверить UI. AsyncLocal lock scope должен течь в Task.Run; original caller lock удерживается доawait. Enumeration overrides могут предполагать caller thread в тестах: inspect до change, не ломать coordination. Эмулятор noisy/warmFS: alternating pairs/платформенные ограничения. Fixed startup до storage не ускоряется этой правкой.
Expected objections: «почему не кеш» -> профиль выделил затраты, этот узкий опыт обратим; «почему готово без5s» -> не готово, parent unmet; «тесты вместоUI» -> actual native list/card и Headless; «ускорение от прогрева» -> matching repeated alternating controls.
Rework checklist: observable flow/AC, решение/разрешение, regression/rollback/evidence и review определены; full gate не заменён подготовкой.

## 13. План
Review/fix/autoapprove -> baseline focused -> code+UI coverage -> targeted/full -> Android controlled before/after -> retain/revert -> review/CLIresult. Parent merge только после его обязательных gates.

## 14. Вопросы
User-owned решений нет; reviewer проверяет AsyncLocal/thread assumptions и достаточность измерения.

## 15. Профиль
Performance: hypothesis/baseline/latency+alloc/retain-or-revert/separatecommit. UI MUST coverage/run/inspection. QUEST Expanded. Security/platform: собственная копия/пакет/AVD, без credentials/original app.

## 16. Файлы
src/Unlimotion.FileStorage/FileTaskStorage.cs — две private функции scheduling; src/Unlimotion.Test/FileTaskStorageReadContractTests.cs — обязательные reader regression tests для новой scheduling boundary; src/Unlimotion.Test/SingleViewStartupUiTests.cs — large mixed dataset variant; эта SPEC. Diagnostic scripts/APK private внеGit.

## 17. Mapping
Per-file Task.Run(N) -> one directory Task.Run(1), та же sequential semantics. Small mixed UI -> old+130 modern variation. Persisted files/API -> без изменений.

## 18. Альтернативы
Boundedparallel4 меняет pressure/order aggregation и требует нового дизайна; persisted cache большой freshness/write safety scope; убрать Task.Run без внешней boundary блокирует UI. Один worker — наименьший обратимый experiment; retain исключительно послеAC3.

## 19. Quality gate
Independent initial review NEEDS-FIX:2MEDIUM (numeric retain rule, mandatory scheduling/lock checks),1LOW точный comparer/protected hook. Root исправил все3 ниже. Linter1-20 initial:14FAIL/7,8,10,15,20PARTIAL, остальныеPASS; rubric24/30 (5/5/5/5/2/2), не является approval. Domain/UX/architecture/operations прошли initial pass, tester требует исправлений ниже. PostEXEC не выполнен. Обязательные условия выше не заменяются этой оценкой. Root initial adversarial: signatures/schema/locks unchanged; caller enumerator thread change явный, ожидаетсяoffUI; сравнение first/warm невалидно как gain. Нужен independent pass передEXEC.

## Approval
Автоматически после post-SPEC PASS и исправлений согласно user instruction этой сессии; повторное подтверждение не требуется.

## 20. Журнал
15Oct SPEC draft: actual full profile attempt10 получен, private source0b4, productcode не изменён; отправляется наreview. Следующее — устранить findings и начать baseline/EXEC.

### Уточнение после independent post-SPEC review
Scope reviewed: эта SPEC, FileTaskStorage private reader/public callers/lock AsyncLocal/protected overrides; FileTaskStorageReadContractTests, SingleViewStartupUiTests; actual attempt10 core/VM/metadata.
Performance AC3 protocol до product edit: B=source0b4 APK, C=единственная scheduling delta+те же observer settings. API34/x64/engine37.2.12/GLES host/CPU3/RAM2560/display1080x2400, одинаковый сохранённый fixture с byte-hash manifest; Git/updatesoff. Перед каждой попыткой ownforce-stop, exactAPKhash, reset одинаковыхSettings, fixture восстановления если bytesdrift, freshPID. Для обоихvariants сначала один исключённый warm-up, затем3пары B-C/C-B/B-C безперезапускаAVD; OS/FS warm, неflushкешей. Metadataфиксируетengine/QEMU/GPU/display/source/hash/PID/fixture; приQEMU/focus/interruption boundedtrace invalid сохраняется, неучитываетсякак0. Максимум5валидныхпар; при повторныхnativefailures прекращаетсяcapture, необъявляетсяgain.
Primary interval: core livegraph.begin->livegraph.end. Whole storage: core init.begin->init.end; wholeVM: connect.begin->loading.false; allocations сравниваются отдельно для wholeVM иlivegraph по process-wide deltas, не суммой вложенныхфаз. Дополнительно uninstrumented launch->inspected usablelist+card (native videoPTS с boundaryuncertainty, неam-W) на одинаковом3pairprotocol; observer-effect невыдаётсязаproductgain.
Заранее retain rule: median paired reduction primary >=10%, все3primaryratios<1; median paired reduction должна превышать относительный baselineMAD (MAD(B)/median(B)); medianwholeVM/storage/uninstrumentedusableflow regression <=5%; median wholeVM/livegraph allocatedbytes regression <=5%; no pair wholeVM/storage/usableflow regression>15%. Если3pairs inconclusiveтолькоиззашума, добратьдо5по томужеprotocol; на5 требовать≥4primaryratios<1, остальныеmedian/thresholdgatesпрежние. Еслиeffectthresholdнепройденилиunrelateddriftнельзяустранить, откатитьproductdelta; еслиданныхнедостаточно, retainНЕпринят/draft, отдельногоcommitоretentionнет. Absoluteparent5/1/5 остаётсяне выполненнымдоегоnativeacceptance.
Mandatory behavioral regression tests: (a) FileTaskStorage reader вызывается с AvaloniaUIcontext, protectedenumerationgated worker; доreleaseпроверить actualDispatcher.CheckAccess=false вenumerator и выполнениеUIcallbackприpausedworker, послеreleaseполныйresult; тест не считаетTask.Run. (b) readerpaused подUseDirectoryLock=true, competingstorage.WithDirectoryLockAsync operation с cancellationtoken не входит доrelease, lockвсёещёexclusive; послеsnapshotwriterвходит. (c) outerWithDirectoryLockAsync callback делает Task.Run->nestedWithDirectoryLockAsync->ReadDirectoryAsync, completesboundedбезdeadlock/timeout, graphтотже. Fixture deadlines/finallygate release исключаютвисящийtest. Existing knownJSON/date/repair/duplicates/hash/clone tests дополняютновые, а не заменяютсяими. UIlarge130scenario остаётсядополнительнымобычнымstartupflow.
Review passes: rootScope/Evidence/Contract/Adversarial/roles;2MEDIUM fixedпланомзаранеезаданныхnumericrules+3mandatorybehavioraltests; LOW fixed точнымcomparer/extensibilitywording. Rootmanualchallenge: warmFS/fixturemutation/observercost/queuedcallbackиnestedlockне должны выдаватьложныйgain/PASS. Независимыйtargetedre-review pending, EXECне начат. User-ownedнеясностейнет. Findings/fixesизэтогоreviewне ослабляютобязательныеgates. Следующийstopdecisionпослеtargetedre-review; automaticapprovalтолькопослеPASS.

15Oct13:45 targeted independent post-SPEC re-review PASS:2MEDIUM+LOW fixed; linter1-20PASS, rubric30/30designreadiness; role domain/UX/test/architecture/operationsPASS for plan. Root adversarial fallback confirms no source mutation yet/previouslyvalidated source0b4; no newfindings. Automatic SPEC approval granted under explicituserinstruction; EXECauthorized. Retention/parentacceptancepending.


15Oct13:50 focused implementation independent review PASS/no findings: product schedulingonly, exact loop/order/catches/hash/clone; mandatory testsrealUI/offUI/exclusivefilelock/waiter/nestedtimeout and gatecleanup inspected. Normal candidatebuild PASS0errors74warnings; read12/12/UI8/8PASS, PNG1/130 inspected. Final test-only cleanup boundednestedDirectoryLockTimeout2s and explicitlockexists rerun passed12/12; fullMain underway. FocusedPASS isnotretention/finalpostEXEC; nativepairs+fullHeadless+wholeparentgatespending.


### Уточнение measurement contract до A/B (15Oct)
Во внешнем measure-worker-variant.py cold-run videoPTS не имеет точного общего начала с host launchclock. Для uninstrumentedwhole-flow заранее уточняется измеряемый trigger: отправка host-команды amstart -> native presentation карточки после scripted выбора/раскрытия. Это обычный UI flow, inclusivepoll/ADB overhead, не nativeActivitystart и не physical5sacceptance. Каждый trial хранит interval [hostbeginпоследнегоnativeROOTcaptureбезкарточки,hostendпервогоnativeCARDcapture] относительно отправки launch. Actual rendered card presentation находится в этом консервативном интервале при проверенномroot->selection->cardflow; classifierguard не заменяет самостоятельный просмотрPNG/video. Для5%median/15%individual regression gate брать консервативное отношение candidateUpper/baselineLower, не толькоupper/upper; еслиintervaluncertainty не позволяет выполнитьgate — результатinconclusive/no-retain, не ослаблятьgate. ВидеоPTS используется отдельно дляпроверкипорядка/границкадров иVFRgaps, невыдумыватьточнуюсвязьlaunchclock. Primarycore/VM intervals и allocation rulesбезизменений.
Helperinitialreview3MEDIUM: exactfixturekeyset (не толькоожидаемыехеши), actualCPU/RAM/engine/currentQEMU binding, failureownappcleanup. Всеисправленыпередзапуском: taskfileconventionexact3094keyset; API34/x64/CPU0-2/MemTotal2530000..2540000KiB+resolvedhardwareRAM2560/currentQEMUpath+parent+AVD+engine37.2.12; exactownAVD8stimeoutforceStopfinally. CollectionCompleteпереименованвоизбежаниеacceptanceclaim; visualReviewpending. Targetedrereviewmeasurementamendment+helperpending. Nativecomparisonsещёне начинались.

15Oct14:07 targeted independent measurement-helper/amendment re-review PASS/no newfindings; all3MEDIUMfixed. Root adversarial: exacttask-keyset excludesunexpected extraJSON; actualCPU/RAM/currentQEMU not requestedRAM; boundedcleanup and conservativeinterval/no-retainifuncertain. Amendment automatically approved underuserinstruction. RawcaptureallowedonlyafterAPKs verified; no runtimecomparison/retentionclaimed.


15Oct14:16 packaging correction beforeA/B: shadowbaselinepublish with global RuntimeIdentifiers=android-x64 failedMSB3030 missingDomain.dll underRIDobj; failure retained, no APK/run/PASS. RemovedglobalRIDoverride and returned previouslysuccessful repo-default twoABI arm64+x64 publish, sameSDK401/flagsforB/C and gueststillAPI34/x64. This is disposable packagingconfiguration, trackedprojectunchanged; comparisonguardnowpinsbothABIs symmetrically. Extra fail-closed guards added qemuflag/onlineCPU/settingsJSONpath+SHA/expectedproductSHA and bounded5s trace-footer wait; thermalbefore/after captured. StaticcompilePASS, no changednumericgates.


15Oct14:37 full local validation: noncanonicalparallel run interrupted after5GitIndicatorerrors; rerun maxparallel1 at14:33 ended exit1 by40m timeout,1525reportedpassed/0reportedfailed, during existing NightAgentWorkflowReplayTests.MeasuredBenchmark_3000Tasks600ReadsVsCaptureAndOfflinePages (100/600reads,300000parses198.113s at lastprogress). No fullPASS/rootTRXclaimed; remainingtests notverified. Same-source canonicalCI willrun from provisionaldraftcommit. Targeted12/8andfocusedreviewPASS remainvalid; retain/Androidcomparisons/fullHeadlesspending.

