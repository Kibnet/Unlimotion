# Подключение UI-проекций после начальной загрузки хранилища

## 0. Метаданные

Expanded SPEC по центральному `templates/specs/_template.md`. Профили: delivery-task/QUEST, performance-optimization, dotnet-desktop-client, testing-dotnet, UI automation/review-loops. Task1 `e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6`, worktree android-task-loading, branch `perf/android-e74c5d1a-task-loading`. Baseline HEAD `8156c4e06e079643511cf40f3efe206747ca77b5`: только docs относительно2afd, Git trees src/tests прежние; product43d95e1b. Масштаб medium, владелец Codex. Effective model/effort не раскрыт runtime; model eval неприменим к C#/UI изменению.

Пользователь разрешил весь последовательный scope, отдельные ветки/ворктри задач, merge принятой задачи перед следующей, CLI status/results и автоматическое подтверждение SPEC после review/fixes. Это гипотеза внутри незавершённой Task1; parent5/1/5 остаётся открытым. Релиз/установка оригинального приложения не входят. **CLOSED: qualified NO-RETAIN. Candidate UI6/6, regression88/88, fullMain1578/1578, Headless66/66 PASS; fixed7pairs whole-Connect median .83795922 выше порога .80. Allocations .88053940/wins7/gaps PASS не отменяют failure wall. Native не допущен, дополнительные пары запрещены. Reviewer подтвердил scoped archive/rollback; фактическое восстановление фиксируется отдельной receipt после проверки ZIP.** Последующие формулировки readiness в ранних разделах — исторический план, не текущий допуск.

## 1. Overview / исходный результат

Пользователь видит долгую загрузку/возврат Android и зависание поиска; Task1 должна приблизить обычную работу на релизных desktop/Android с Git и CLI-агентами к cold≤5s, resume с действием≤1s, locally available freshness≤5s. Телефон временно заменён разрешённым эмулятором.

Гипотеза: подключение тяжёлых списков/emoji-каталогов к уже загруженному SourceCache уменьшит повторную обработку49 начальных batches. Загрузка и миграции остаются прежними. Измерять весь MainWindowViewModel.Connect, а затем обычный запуск: перенос работы из storage.Init в следующую фазу не считается ускорением.

Output: минимальный MainWindowViewModel diff, реальные UI/regression tests, baseline/candidate whole-Connect allocation+wall и native ordinary evidence; оставить либо откатить; результат в CLI. Stop: потеря состояния/сохранений/обновлений, UI regression, отсутствующий либо недостаточный whole-flow gain → NO-RETAIN, без изменения порогов/добавления удачных попыток.

## 2. AS-IS / доказательства

`MainWindowViewModel.ConnectCore` сначала включает IsTasksLoading и Task.Yield, подключает storage, затем раннюю Tasks.Connect с AttachTaskContext/OnItemRemoved/OnItemUpdated, далее emoji/root/other-tab/card projections и только в конце вызывает taskStorage.Init. Init публикует64-item batches с yield. Lazy heavy tabs уже существуют; менять их не требуется.

Ранняя подписка задаёт NotificationManager/MainWindow/IsInitializedProvider и обслуживает текущую карточку. Она обязательна ДО Init. Начальные модели создаются с provider=false, но после привязки должны получить текущий provider. Сохранения/Reload и поздний Dispose guard остаются eager и неизменными.

Diagnostic32: Core models4.699s/248.63MB и cache publication7.388s/542.17MB, inclusive process counters; observer overhead/другие потоки включены. Это один размеченный запуск, не pure allocation attribution или paired gain. Последний native39 подтверждает примерно1GB process allocation внутри Core.Init; bounded-file-read дал combined median .675513>.65 и откатан. Повторять его, direct-INPC/standardRx или lazy Save/Reload не предлагается.

Baseline full evidence: exact2afd CI Main1572/Headless66, local51/66 PASS; src/tests8156 Git-identical. Новый docs-head CI39268993531/AndroidPkg39268993795/CodeQL39268993553 при подготовке SPEC ещё выполняются. Baseline benchmark снимается ДО product change на замороженном коде. Старый локальный test DLL содержит откатанный кандидат и для нового baseline без пересборки не используется.

## 3. Проблема

Тяжёлые UI-проекции наблюдают промежуточную гидратацию, хотя overlay закрывает обычное взаимодействие. Не установлено, сколько времени можно устранить: возможен только перенос той же работы на UI после Init. Это проверяется whole-Connect и ordinary gate.

## 4. Цели дизайна /5. Non-Goals

Один сдвиг await, без нового storage/API/config/schema/cache, глобальных scheduler/ThreadPool изменений, batch-size tuning, блокировки watcher, изменения JSON/миграций/CLI. Не включать в candidate отклонённые механизмы. Сохранить обработчики карточки/контекста, фильтры, выбранную задачу, действия после загрузки и отсутствие startup autosave. Parent Task2/3 не начинать.

## 6. Целевой дизайн

Первоначальная гипотеза: в ConnectCore перенести единственный `if (!storageAlreadyInitialized) await taskStorage.Init()` сразу ПОСЛЕ ранней контекстной Tasks.Connect подписки и ДО `#region Emoji`. Но source review выявил различие error surface при повторном Connect: baseline назначает новые публичные коллекции ДО Init, а простой перенос может оставить старые коллекции от успешной загрузки. **Этот перенос пока НЕ разрешён.**

Stage1 EXEC после planning review ограничен baseline characterization/tests и подготовкой baseline benchmark на неизменном MainWindowViewModel. Обязательный дополнительный сценарий: successful Connect A → reconnect с частичной публикацией и ошибкой Init → retry. Фиксируются root IDs/emoji source/catalog/current card/detached draft/IsInitializedProvider/IsTasksLoading/commands и отсутствие незапрошенных записей. Снимки и фактический baseline trace дают точный контракт, затем целевой дизайн и отдельный affected-surface review обновляются ДО product mutation. Объективный критерий — сохранение baseline поведения; reset, который может потерять черновик, не добавлять вслепую.

Инварианты будущего решения: Connect/storage.OnConnectionError/taskRepository assignment и ранняя подписка сохраняются; NotifyTaskStatusMigrationIfNeeded, AttachTaskContext loop, RegisterCommands и IsInitialized=true завершают успешную загрузку, Finally IsTasksLoading=false остаётся. Если один await move не обеспечивает error parity, выбрать минимальный механизм отложенного source binding с теми же public assignments после characterization; это отдельный design gate, а не разрешение менять subscriptions сейчас.

BindInitializedStorage не вызывает Init и сохраняет прежний путь. SourceCache.Connect даёт текущий snapshot и дальнейшие updates; ни один внешний edit во время Init не должен теряться при новом подключении. Обработчики удаления/замены карточки не откладываются. Поведение повторного Connect и сохранения detached draft должно остаться таким, как в baseline; новый контракт удержания выбора не вводится.

Ошибка Init: IsInitialized остаётся false, overlay снимается finally, provider не разрешает startup autosave. Empty defaults объясняют только первую неудачную загрузку; состояние после успешного предыдущего подключения обязательно характеризуется отдельно. Повторный Connect должен корректно освобождать старые subscriptions; не менять исключения, не терять detached draft и не добавлять скрытый recovery.

### User-Observable Scenarios / visual planning artifact

Экран без нового layout: существующие native39 Loading tasks → All Tasks → карточка Unlimotion и host SingleViewStartupUiTests служат reference. На устройстве overlay остаётся до конца загрузки и подключения проекций; после него тот же список/поиск/emoji/карточка. До окончания Init пользователь не обязан видеть промежуточные частичные списки за overlay. PNG после всех стадий должны быть просмотрены. Fallback timed-native: raw-window-bound PNG, screenrecord не запускать во время замера.

| Сценарий | Ожидаемый результат |
| --- | --- |
| Первый запуск, Init удержан после части задач | Overlay виден, dispatcher выполняет контрольные callbacks, startup autosave отсутствует; после release полный tree/catalog |
| Modern+legacy и file watcher | Миграция/современные bytes сохраняются; первая правка/Save и external update/Reload работают |
| Init падает после частичной публикации, затем повтор | Явная ошибка, overlay false/IsInitialized false; retry даёт один актуальный список/emoji-каталог |
| Сначала успешное A, затем partial-failed reconnect и retry | Exact baseline list/emoji/card/draft/provider/commands characterization, затем обязательная parity нового решения |
| Изменение задачи между частичной загрузкой и Init.end | Первый видимый список содержит последнее состояние; последующее обновление тоже видно |
| Уже инициализированное хранилище / повторное подключение | Init не повторяется для BindInitializedStorage, baseline card/draft/filter lifecycle сохранён |

### Decision Ledger

| Решение | Основание | Статус |
| --- | --- | --- |
| Ранний context/card observer остаётся | Provider/save и удаление/замена карточки не могут ждать Init.end | принято |
| Сдвинуть весь блок UI-проекций одним переносом Init | Меньше diff и меньше риска расхождения кода обычного/initialized paths | гипотеза, проверяется |
| Whole-Connect, не только storage.Init | Работа может лишь переместиться в следующую фазу | обязательно |
| Нет hidden-progress UX и новых persisted state | Исходный overlay/reference остаются | принято |
| Native только после host/test/source readiness | Не тратить device runs на неподтверждённый механизм | staged gate |

### Stage1: фактическая characterization и уточнение дизайна

19.10 baseline `targeted-baseline2`: оба fresh-child rendered случая PASS (ordinary/detached), source unchanged;8 PNG лично просмотрены. Первый baseline1 был harness failure (overlay находится в App.CreateSingleViewMainView, не в MainControl), не product regression. В baseline2 A64→B64 partial failure→C64 retry: root/catalog заменяются на B даже после ошибки; IsInitialized=false, loading/overlay=false, старый Create сохраняется. Обычная A-карточка очищается ранним OnItemRemoved. Missing A-карточка и её локальный Title сохраняются после B и C; provider=false при B, true после C; missing Save/status/reload остаются запрещены. Startup UpdateCount=0. Перед окончательным подтверждением zero-autosave добавлен scoped100ms default throttle ДО создания VM, с restore в fresh child и ожиданием700ms; это повторная проверка baseline3, не новая продуктовая логика. Teardown release+bounded observe pending Connect предшествует Dispose. Reviewer нашёл эти harness gaps, исправления проверяются повторно.

Уточнённый минимальный дизайн для affected-surface review (ПОКА НЕ ДОПУЩЕН): ранний context/card observer остаётся; Init выполняется перед Emoji. Если Init выбрасывает Exception, сохранить ExceptionDispatchInfo, затем подключить прежний блок проекций к фактическому частичному snapshot и повторно выбросить исходную ошибку ДО Notify/Attach-loop/RegisterCommands/IsInitialized=true. Finally снимает loading как раньше. Успех также подключает тот же блок единожды после Init; BindInitializedStorage пропускает Init. Не добавлять reset, не подменять исключение/stack, не регистрировать новые команды на failed reconnect. Таким образом частичный UI виден после ошибки, текущая карточка обслуживается ранним observer всё время. Промежуточные скрытые списки за overlay могут ещё оставаться прежними во время pending Init; это уже разрешённый сценарий, и не должно становиться assertion о performance-кандидате. Test ждёт B root только после failure; baseline partial snapshot сохраняется как characterization, не контракт скрытой перерисовки. Проверить failed first Init, partial external update, initialized-bind отдельно до принятия.

Host helper preparation `DROOT/projection-binding-host1/Program.cs`: самостоятельный Release executable с real Avalonia.Headless/Skia/App, реальными FileStorage+Unified, normal vm.Connect,3094 tasks/49 add batches owner-thread assertions. Initiated отмечает конец Init; DispatcherTimer25ms покрывает Connect и500ms после; whole allocation/wall до Connect return, post-init UI-gap отдельно. Фиксированный AppName build property одинаков B/C. No-owner rejection и600ms stall после Initiated — отдельные helper controls на собственных64 современных задачах, исключены из3094 cohorts. Это не7B подряд. Build/helper execution требует отдельной source/readiness проверки; product пока неизменен.

Affected fixes до build: host использует существующее friend assembly name `Unlimotion.Test` без изменения production API и без ссылки на настоящий Test executable; только два UI/mock исходника linked. FileStorage watcher=true и обязательный IRawDatabaseWatcher assert. Минимальный test-only TimedTaskStorage наследует настоящий Unified и reimplements только ITaskStorage.Init: await base.Init → InitReturned callback. Это конец реального base.Init, а не Initiated внутри него; overhead одинаков B/C. DispatcherTimer maximum считается по ВСЕМ gaps до Connect+500ms, включая gaps, начавшиеся после overlay off. Четыре helper controls: provenance stale-source/stale-control rejection, no-owner rejection,600ms после base.Init и600ms после Connect return. Runner `run-projection-binding-host1.py` связывает immutable build-input proof→payload manifest→build receipt, требует equality execution/build inputs и неизменные payload SHA, фиксирует link в каждой stage receipt; копия fixture и её task SHA проверяются до/после. Baseline diagnostic допускается только после4 PASS controls; все controls исключены из парных timing cohorts. Known headless Dispose NRE ограничен точным top stackframe и записывается отдельно; другие exceptions не игнорируются, dispatch failure явно rethrow через EDI. These fixes supersede прежнее описание watcher/Initiated. Affected review PASS на baseline build/4controls/one excluded B diagnostic; автоматически подтверждено. Reviewer только читал writable runtime; author fallback сверил4controls и source/payload binding. Product/paired/native пока held.

Dual-failure policy будущего product design: исходное Init exception/stack остаётся primary, даже если subsequent snapshot projection setup бросит; secondary projection exception фиксируется отдельно диагностикой без нового user-flow/reset. Overlay снимается finally. Обязательный fault-control проверит identity исходного Init exception и запрет RegisterCommands/provider=true в этом случае. До разрешения product move сначала дописать и прогнать на baseline уже запланированные first-failure/retry, update-during-init и BindInitializedStorage сценарии; dual-failure candidate control требует конкретного test seam review, production-only injection не вводить.

19.10 actual baseline4: Main Debug build0errors; targeted root5/5+five fresh child1/1, no skips, sources unchanged. First-failure сохраняет тот же Init exception instance/stack, partial64root и retry64B; update-during-init replacement и последующий replacement видны в root VM/catalog; initialized-bind вызывает Init только вручную1раз, bind не повторяет и early providers становятся true. Четыре новых actual PNG (first-failure/final retry/update/initialized-bind) просмотрены; snapshot update содержит UI root, обновлённая строка вне текущего viewport, content-level проверка automated по bound VM, отдельная видимая карточка будет добавлена в candidate validation. Нулевые незапрошенные записи проверены с100ms throttle.

Host1 build-baseline1 failed CS1061 UseSkia: standalone csproj недоставало direct Avalonia.Skia12.1.1, product и inputs неизменны, exit1 без timeout, controls/timing не запускались. Исправление `prepare-projection-binding-host2.py` создало fresh `projection-binding-host2` и `run-projection-binding-host2.py`, добавлена только прямая dependency;3host sources/newrunner receipt записаны. Oldfailed evidence не перезаписывается. Аffected review build/controls/oneexcludedbaseline для нового root требуется до исполнения.

Host2 affected review PASS, autoapproved; actual build начат отдельно, без разрешения product/timing pairs/native. Для product affected review уточнён dual-failure test seam без production hooks: только ControlledStorage в тесте получает SourceCache backing-field и one-shot Tasks getter fault, который активируется ПРИ выбрасывании InitFailure. Baseline успевает подключить projections до Init и повторно Tasks getter не читает; candidate после InitFailure пытается подключить partial projections и получает отдельный ProjectionFailure. В обоих случаях test требует identity исходного InitFailure, его Init stack, overlay=false/IsInitialized=false/Create не зарегистрирован. Если вторичный fault был consumed, scoped TraceListener должен получить именно ProjectionFailure; baseline этого дополнительного события не требует. Обычная parity partial-failure проверяется другими тестами, а fault-control не подменяет её. После source fault обязательно выключить one-shot guard до teardown/owned VM Dispose.

Host2 compilation фактически exit0, но receipt finalizer не прошёл assertion из-за Windows `str(relative_path)` с обратными slash; эта попытка не считается validated build. Host3 создал новый namespace и `.as_posix()` manifest keys, отдельный конкретный WindowsPath conversion control PASS; author affected review mechanical PASS без смены gates. Host3 build/provenance/no-owner PASS, затем control-stall1 упал ДО vm.Connect: standalone host не выполнил ReactiveUI bootstrap, обычно вызываемый TUnit session hook. Qualified3094/paired timings отсутствуют. Host4 preparation добавляет тот же core/UI scheduler/exception handler bootstrap, оставляет production throttle10s. Старые попытки сохранены. Source/failure paths проверены перед исправлением; blind reruns не выполнялись.

Уточнение истории throttle: `ReactiveUiSessionHooks.InitializeReactiveUi` уже выставляет10ms в настоящем TUnit процессе. Поэтому baseline2 с700ms ожиданием тоже охватывал effective throttle; первоначальное замечание «default10s делает этот запуск недостаточным» не учло hook. Scoped100ms в baseline3/4 оставлен как явное локальное условие и восстанавливает предыдущее10ms значение. Host performance не наследует этот test-only throttle, его10s остаются как в продукте.

Минимальная product реализация после допуска: `ExceptionDispatchInfo? initialLoadFailure=null` до внешнего try; await Init сразу после раннего observer с catch, сохраняющим EDI; тот же существующий блок Emoji→Roots→lazy/card после него; перед Notify вызвать captured EDI.Throw. Внешний catch с filter `initialLoadFailure!=null` повторно выбрасывает исходный EDI; только при другом exception instance сначала `System.Diagnostics.Trace.TraceError` фиксирует secondary snapshot binding error. Existing finally всегда снимает loading. Trace failure не должен заменить первичную ошибку: best-effort TraceError с узким exception guard вокруг диагностического вызова; это ограниченный exception handling только secondary diagnostic, не swallow Init. Никаких production test injection API, новых persisted fields/config/cache/schedulers.

Affected review correction: dual-failure candidate test ОБЯЗАН требовать FaultConsumed==1 и Trace с ProjectionFailure, без условного PASS при0. Baseline отдельная characterization ожидает0. Это source-bound test expectation: baseline test constant false, после product mutation true, оба исходника/результата сохраняются; runtime env/skip/production hook не требуется. Остальные identity/Initstack/overlayfalse/IsInitializedfalse/Create-null проверки одинаковы в обеих версиях. Исправляет допускавшее vacuous PASS прежнее «если consumed»; новый текст имеет приоритет.

Stage2 affected design PASS / автоматически подтверждено после исправления FaultConsumed condition. Admission conditions выполнены: actual baseline5 root6/6+six child1/1/sourceunchanged, snapshot теста сохранён `projection-binding-ui-baseline5.cs`; exact-head8156 CI roots1572/66 и AndroidPkg/CodeQL PASS проверены `audit-projection-baseline-and-ci1.py`, без skips. Host4 build/4controls/excludedBdiag PASS с owner/fixture/provenance/PNG и childexit0. Допущен только описанный минимальный product EDI diff и candidate correctness validation; парные/native/retention gates ещё закрыты. Reviewer advisory writable/read-only conduct; author adversarial fallback проверил control identity/stack, отсутствие новых public hooks, сохранение load/finally/BindInitialized paths и no-autosave condition.

## 7. Бизнес-правила /8. Интеграции /9. Данные

File/server/CLI контракты и SourceCache source of truth прежние. Root, current card, child relations, status/emoji filters и первая запись должны соответствовать baseline. Ни один task JSON не переписывается для benchmark; native используется копия frozen3094, Git backup/update check выключены одинаково. Host fake partial-init проверяет UI scheduling; отдельно real FileStorage+watcher проверяет persistence. Это разные доказательства.

## 10. Rollout / rollback

До значимого whole-flow gain — незакоммиченный candidate в Task1 worktree. При NO-RETAIN сохранить diff/source/tests/evidence в проверенном приватном архиве, восстановить только свои product changes; полезные общие тесты оставить только если проверяют baseline контракт. Retained optimization — отдельный Conventional Commit/CI/draft PR, Task1 merge только после parent5/1/5. Original app/phone/release не заменять.

## 11. Acceptance-to-Test Matrix / validation

| AC | Автоматическая проверка | Визуальное/native evidence |
| --- | --- | --- |
| A1 Init exactly once / initialized zero / ранний provider/card lifecycle | Новые gated UI cases + SingleViewStartupUiTests/StartupProjectionAndRelationsTests/TaskSpaceTransactionTests | Loading/final root/card PNG, rendered reference |
| A2 Без потери данных и обновлений / первая запись | Real file/watcher UI case, modern+legacy suite, existing reload/source-switch/emoji/UI contracts | Exact persisted content и отображённый external title |
| A3 Ошибка, retry, partial update, UI dispatcher | Controlled partial-init/failure/retry/UI callbacks, latest snapshot + late update; success→partial-failed reconnect→retry exact characterization/parity; full Main/Headless | Реальный показанный Window и capture, не одни bounds |
| A4 Значимый end-to-end gain | Ниже fixed whole-Connect host/native gates | Ordinary без observers, точный own runtime/current draw/fixture SHA |

План новых UI coverage: класс StartupProjectionBindingUiTests, пять исходных сценариев плюс шестой success→partial-failed reconnect→retry на реальном MainView в Window.Show; SafeHeadlessUnitTestSession+EmojiTitleTestAppBuilder (реальные Skia/fonts), fresh rendered process по существующему TaskTabEmojiFilterRenderedProcess pattern. Partial init — контролируемый ITaskStorage proxy с ранними64 задачами/TaskCompletionSource; real-file case — отдельный временный каталог/настоящий FileStorage/watcher. Stage1 сначала добавляет и исполняет characterization шестого сценария на baseline, остальные cases по мере конкретизации без product change. Не менять production API ради теста. До прогона зафиксировать фактическую discovery/count, без скрытых skips/exclusions.

Команды стартового EXEC (из P, DN=`C:/Program Files/dotnet/dotnet.exe`, PS7):

    dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --disable-build-servers -p:UseSharedCompilation=false
    dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --disable-build-servers -p:UseSharedCompilation=false
    dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter '/*/*/StartupProjectionBindingUiTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx --results-directory D:/CodexArtifacts/android-task-loading-20261012/projection-binding-validation1/targeted-ui
    pwsh -NoProfile -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project main -ResultsRoot D:/CodexArtifacts/android-task-loading-20261012/projection-binding-validation1/full -DotnetCommand 'C:/Program Files/dotnet/dotnet.exe'
    pwsh -NoProfile -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project headless -ResultsRoot D:/CodexArtifacts/android-task-loading-20261012/projection-binding-validation1/full -DotnetCommand 'C:/Program Files/dotnet/dotnet.exe'

Targeted existing filters тем же MTP: SingleViewStartupUiTests, StartupProjectionAndRelationsTests, TaskProjectionObservationContractTests, EmojiProjectionUiAffinityTests, TaskSpaceTransactionTests и релевантные existing reload/emoji/source-switch contracts; exact class/run manifest до исполнения. Fresh outputs, serial tests; build≤600s, targeted class≤300s (existing rendered helper child≤180s each), fullMain≤2100s, fullHeadless≤900s. Без параллельных builds/tests/media во время fullMain; timeout/failure сохраняется с exact child cleanup и диагностируется до нового запуска. Product/test inputs SHA заморожены. Full count определяется discovery исходного1572 плюс новые cases, no skips. FullHeadless baseline66 без нового класса в той assembly.

### Prospective performance stages

Host: normal Connect на настоящих UnifiedTaskStorage+FileStorage и реальном owner UI SynchronizationContext. Console/no-context fixture не подходит: Unified.Init выбирает yielding64-item batches только при non-null SynchronizationContext. Helper обязан доказать3094 задач/49 начальных Add batches, captured owner context/thread и неизменный fixture. Fake partial-init нужен только функциональным UI tests и не заменяет real-storage benchmark.

Baseline ДО product изменения: один отдельный B-only diagnostic/excluded run на frozen B executable. Он не входит в статистику будущих пар. После готовности C — по1 excluded warmup B/C и7 фиксированных чередующихся пар из тех же замороженных B/C executable; нельзя сравнивать семь прежних B подряд с семью последующими C. Итого один preimplementation diagnostic плюс16 fixed cohort runs, без extensions/replacements. Одинаковые frozen3094/settings/runtime, свежий процесс для каждого run.

Измерять весь Connect begin→успешный конец включая создание проекций/IsInitialized/снятие overlay; process allocation inclusive. Gate: median paired wall C/B≤0.80, allocated≤0.90, wins≥5/7 для wall. UI dispatcher sampler одинаковый B/C на всём Connect и500ms после overlay-off; охватывает также Init.end→подключение snapshot→overlay-off. До timing helper controls должны доказать, что намеренная задержка UI именно после Init.end обнаруживается, а sampler без owner context отвергается. Sampler interval25ms; в каждой из7пар maximum observed dispatcher gap C≤max(1.05×B,250ms), чтобы не скрыть starvation за улучшившимся total. Это host no-regression gate, не доказательство native resume≤1s. Конкретный capped sampler/helper/source overlay/команды/бюджеты проходят отдельный staged review ДО запуска; readiness не объявляется планом. Host позволяет отклонить гипотезу, не доказывает Android gain.

После host gate и корректности — отдельный native source/package readiness. Минимальный VM observer измеряет **весь Connect**, одинаковый B/C, без per-item trace. Native: по1 warmup B/C и3пары B,C/C,B/B,C; wholeConnect median wall≤0.80 с wins≥2/3, allocated≤0.90. Затем ordinary такой же fixed2+6 протокол: conservative root CUpper/BLower median≤0.85, max≤1.05, та же usable карточка/все3094SHA. Не заменять wholeConnect уменьшением одного Core.Init. Любой valid fail/noisy inconclusive → NO-RETAIN, no extension/N-tuning. Один только cause-specific invalid-helper correction допустим prospectively после сохранения raw/review; valid runs не заменяются.

Own AVD API34/x64/3CPU/2560MiB/hostGPU/1080×2400, SDK401/AOT, отдельный package org.unlimotion.taskloadingbench; original не трогать. Fresh runtime IDs/namespace/absolute bounds/exact shutdown; concrete source→actual PE/native/AOT bindings, controls/receipt gates и команды обязательны ДО native. Проверенный observer-free Bplain2 может использоваться только при новом explicit proof полной product-source/config/runtime/payload equivalence с8156; иначе fresh baseline rebuild. Старые profile observers другого состава не переиспользовать.

Все A1-A4 и оба native gates нужны для retention, parent5/1/5 отдельно для Task1.

Prospective helper `DROOT/prepare-projection-binding-native1.py` подготовлен как PREPARATION ONLY и ещё не исполнен. Предусловие: sourcebound host gate `paired2/cohort1/gate.json` с evidenceValid/PASS. Замораживает406 product inputs B/C из проверенного старого baseline manifest с normalized equality GitHEAD8156 и current worktree (единственное product отличие — MainWindowVM). Создаёт byte-reversible external overlay в `Unlimotion.ViewModel`: `connect.begin` перед IsTasksLoading=true, `connect.end` после false в finally, затем async flush; ровно2 marks/1complete, тот же минимальный observer B/C, никаких per-item listeners. Уменьшенный cap2 и отдельные имя файла `startup-profile-connect.jsonl`/module `viewmodel-connect` отделяют новую поверхность от прежнего Core6. Compile target только diagnostic package/profile/конкретный project, ordinary product inputs прежние. Подготовка сама не создаёт shadow, не собирает/устанавливает APK и не запускает AVD. Проверка синтаксиса выполнена, targeted review запрошен; build/cohort admission и package PE/native/AOT proof ещё отсутствуют.

## 12. Риски / Expected User Review Objections

| Возражение/риск | Мера | Статус |
| --- | --- | --- |
| Просто спрятали ту же работу после Init | WholeConnect+ordinary, не Core.Init-only | mitigated |
| Ранние save/card callbacks исчезнут | Оставить минимальную подписку до Init, gated/reconnect cases | mitigated |
| Теряется update между Init и subscribe | Full SourceCache snapshot+late update UI case | проверяется |
| Один большой snapshot заморозит UI | Gated dispatcher case не заменяет native; ordinary/current-window/ANR/log/UI evidence, отказ при regression | проверяется |
| Ошибка/повтор даст null или дубли catalog | Existing Empty defaults+failure/retry test, finally прежний | проверяется |
| Снова следующий пункт без принятия Task1 | Task2/3 и merge заблокированы parent5/1/5 | обязательно |

Rework checklist: обычный UI flow назван, AC связаны с evidence, решения явны, все релевантные роли ниже. Manual challenge: один крупный snapshot может стоить столько же или больше49 batches; безопасный revert предпочтительнее подбора порогов.

## 13. План /14. Открытые вопросы

Review/fixes/autoapproval Stage1 → baseline success/failure/retry characterization/tests → уточнить product design и affected review по точному parity контракту → конкретный baseline host helper/source freeze/readiness → baseline замер → отдельно допущенный маленький product change+UI tests → targeted/full/source review → host post/cohort → приPASS native helper/admission+fixed gates → postEXEC/retain-or-rollback/CLI. Нерешённых user-owned решений нет. Сейчас baseline/product-design gates пройдены, candidate UI6/6 PASS; existing regressions/full/source review до host cohort ещё в работе. Host pair driver подготовлен отдельно, execution требует readiness и прохождения предусмотренных tests.

## 15. Соответствие профилю

Performance: гипотеза, pre/post identical baseline, latency+allocations, tests/full, separate retain/revert. UI MUST: реальные rendered loading/root/card/persistence/error/retry flows; existing suites. QUEST: текущая только SPEC, code/test mutations после review/исправлений и пользовательского autoapproval. Native/publication/release границы отдельные.

## 16. Файлы /17. Было → стало

| Файл | Изменение |
| --- | --- |
| src/Unlimotion.ViewModel/MainWindowViewModel.cs | Перенос единственного Init await перед тяжёлыми subscriptions, ранний observer сохранён |
| src/Unlimotion.Test/StartupProjectionBindingUiTests.cs | Gated/failure/retry/latest snapshot/initialized/real-file UI tests |
| Частный DROOT + текущая SPEC | Source-frozen baseline/candidate helpers, измерения, фактический результат |

Было: UI projections → Init batches → готовность. Стало: ранний обязательный observer → Init batches → UI snapshot/projections → готовность. Данные, команды, интерфейс после готовности прежние.

## 18. Альтернативы

SuspendNotifications в storage меняет контракт всех клиентов и обработку raw watcher; неоправданно широкий вариант. Отдельно отключать каждую emoji/root chain дублирует жизненный цикл и повышает риск забыть observer. Переносить весь ранний observer нельзя из-за save/card context. Baseline остаётся rollback, если перенос не ускорит whole flow.

## 19. Quality / review

Preliminary linter: A1–5/B6–10/C11–13/E17–19/F20 описаны; D14–16 требует reviewer проверки coverage/staged command readiness. Preliminary rubric: goals5/ASIS5/design5/safety5/testability2/autonomy2 =24/30; это draft, не PASS. Native/host helper execution отдельно gated, отсутствующие helper proofs не заменяются планом.

Role-Based: Domain — parent5/1/5 и первая запись/обновления; UX — overlay/error/retry/root/card; Tester — partial/failure/latest/reconnect/BindInitialized/full+source guards; Architect — minimal await order/early context/source snapshot semantics; Ops — frozen code/config/package/owned-AVD/rollback/draft PR. Все применимы; verdict pending review.

Post-SPEC: NEEDS-REVIEW. Scope inspected: central canonical template/performance/QUEST/review/UI/TUnit, ConnectCore/AttachTaskContext/SourceCache contracts, SingleViewStartupUiTests/EmojiTitleTestAppBuilder/StartupProjectionAndRelationsTests, closed32/39 artifacts и actual rollback/docs8156 source equality. Проверить hidden timing semantics, error/retry/UI-thread/initialization/save и staged gate boundary. Post-EXEC не выполнен. Approval: по текущему указанию пользователя автоматически только после reviewPASS и исправлений; до того EXEC не начат.

Targeted post-SPEC Stage1 re-review PASS: три MEDIUM исправлены сужением scope до baseline characterization, owner UI yielding/sampler и explicit paired protocol. Авторский отдельный adversarial fallback согласен: list/emoji/card/draft/provider/commands после failed reconnect не предполагаются очищенными; реальные SourceCache49 batches остаются и без контекста, меняется yield; B-only diagnostic не смешивается с accepted pairs. Роли Domain/UX/Tester/Architect/Ops PASS для baseline characterization/helper preparation. Separate reviewer работал только на чтение в writable runtime, технической изоляции нет. LOW factual journal уточнён, existing child timeout приведён к фактическим180s. Linter20 PASS/rubric30/30 относятся только к Stage1 readiness (цель/границы5,ASIS5,ограниченныйdesign5,baseline-safety5,targetedUIcommands5,autonomous-prep5); product implementation design и actual timing/native helper readiness по-прежнему не приняты. На этом основании действует пользовательское autoapproval Stage1. Post-EXEC не выполнен.

## 20. Журнал действий агента

### Post-EXEC: disposition эксперимента, 19 октября

Scope/Evidence: фактический diff MainVM/new500-line UI test, baseline6 и candidate6 с child1/1, regression88, full roots1578/66, frozen B/C receipts/payloads, fixed16 schedule/results/process receipts/source proofs и все16 root PNG. `DROOT/projection-binding-host4/paired2/cohort1/gate.json` и final подтверждают корректный сбор/sourceunchanged. Все43 fullHeadless и12 candidate/6realFile PNG также просмотрены. Видео fallback: Skia Headless не создаёт desktop окно для OS recording; actual rendered PNG из автоматизированных тестов и host, SHA/пути сохранены; Android доказательств нет.

Contract: A1–A3 проверенные correctness surfaces PASS, A4 wall FAIL (.83795922>.80); allocations .88053940, wins7/7 и каждая responsiveness pair PASS. Поэтому retention запрещён. Task1 parent cold5/resume1/freshness5 не подтверждён. Native source-preparer/PE helper подготовлены лишь как текст и не исполнялись; device/build/cohort не допущены.

Adversarial/roles: Domain — fixture3094/relations/firstSave/latest/errorRetry сохранены; UX — hidden oldA overlay допустим по baseline, failedB/retryC/card parity и корневые15 видны; Tester — actual TRX без skips/errors, controlled dualfault consumed1+original EDI, no-autosave и child teardown; Architect — один product file/order move, trace listener не маскирует original failure; Ops — unique16 PID/exit0/frozen source/payload/3094 SHA, no native/publish/merge. Ручной challenge: ускорение16.2% стабильно, но выборочно считать wins/allocations вместо заранее установленного wall gate нельзя. Повторные пары/новые пороги не рассматриваются.

Finding: HIGH / acceptance / обязательный wall threshold не выполнен / NO-RETAIN и scoped rollback / rollback pending verified archive. Других in-scope correctness находок после fixes не обнаружено: exact full/targeted/visual и raw provenance перечислены выше; отсутствующий Android не закрывается host evidence. Reviewer PASS относится к qualified rejection/rollback plan, не к реализации или завершению Task1. Чтение в writable runtime, технической read-only изоляции нет; отдельный author fallback пересчитывает gate/process/payload/source proofs и ZIP entries. Linter/rubric для retention не PASS: A4 failure — явный stop. Stop decision: сохранить evidence, восстановить только MainVM HEAD8156 и удалить собственный untracked UI test после ZIP SHA; SPEC оставить CLOSED NO-RETAIN, CLI InProgress, далее другая гипотеза Task1.

| Фаза | Решение / основание | Evidence / остаток | Следующий шаг |
| --- | --- | --- | --- |
| SPEC | После NO-RETAIN bounded read выбран другой механизм повторной UI-публикации | Product/tests baseline8156/43, existing native models/publication profile; новых измерений/кода нет | Planning review, исправления, staged baseline readiness |
| Planning MEDIUM | Простой перенос Init может оставить stale collections после предыдущей успешной загрузки и failed reconnect | Stage1 сужен до exact baseline characterization/test и helper preparation; product mutation запрещён до уточнённого design review | Зафиксировать фактические list/emoji/card/draft/provider/commands, затем сохранить parity |
| Planning MEDIUM ×2 | Console/no-context benchmark не воспроизводит UI-yielding execution; baseline-before-code нельзя превращать в две временно разнесённые группы B/C | Real owner context/3094/49 обязательны; sampler включает post-Init snapshot; отдельный preimplementation B diagnostic и затем fixed alternating7pairs из frozen executables | Проверить actual helper controls/source перед benchmark, timing gates заданы до данных |
| Stage1 EXEC | Targeted planning re-review/author fallback PASS, все3MEDIUM исправлены; autoapproval пользователя | Допущены только baseline characterization/tests и helper preparation, product move/timings/native закрыты | Добавить success→partial-failed reconnect→retry characterization на неизменном MainVM |
| Stage2 EXEC, 19 октября | Affected design review/fixes/autoapproval; baseline6/6+six children, candidate6/6+six children; existing regressions88/88 PASS | Ранний context/card observer сохранён, heavy bindings после Init, original failure EDI сохраняется при secondary projection fault; все12 candidate PNG и6 real-file regression PNG просмотрены | Полные suites и затем frozen host cohort |
| Full UI | 66/66 PASS без skips/other outcomes, sourcebound TRX проверен | `projection-binding-full1/full-ui1-final.json`; все43 actual PNG просмотрены, `projection-binding-full-ui-visual1/review1.json`; PNG fallback не Android/video acceptance | Main1578 выполняется SOLO, финальный результат ещё неизвестен |
| Frozen host candidate | Host4 default Compile glob захватил вложенный baseline obj: build1 failed CS0579 до измерений; raw сохранён | Свежий host5 с byteidentical тремя source files; candidate build2 PASS, frozen B/C payload/provenance готовы; timing не запускался | Только после Main1578 и UI66 PASS — fixed16 cohort, без concurrent builds/tests/media |
| Tracker | CLI execution result обновлён и точно перечитан | `projection-binding-progress10-cli-readback1.json`: Task1 InProgress/Active, текст/критерии/связи сохранены, Task2/3 не начаты | Продолжать Task1 до acceptance; скорость/parent5/1/5 не заявлены |
| Prospective native preparation review | MEDIUM: gate.json записывается до final input check, одного PASS gate недостаточно; whitelist MainVM сам по себе допускает последующий source drift | До исполнения исправлено: require cohort-final success/sourceUnchanged/inputProof SHA, actual SHA всех measured inputs, equality C-build/cohort input dictionaries, C receipt/gate SHA links; bindings+measured MainVM SHA сохраняются в manifest | Affected re-review подготовки; helper ещё не исполнен, build/device не допущены |
| Preparation re-review | PASS PREPARATION ONLY: MEDIUM закрыт цепочкой cohort final→inputs SHA→C receipt→measured MainVM | Reviewer читал в writable runtime, технической read-only изоляции нет; author fallback проверил same bytes/2endpoints/profile-only target и closure inputs. Autoapproval пользователя действует на эту подготовку | Исполнение только после host PASS; build/cohort/retention не допущены |
| Full Main | 1578/1578 PASS, без skips/errors/other outcomes; actual rootTRX и final input SHA проверены | `projection-binding-full1/full-main1-final.json`, exit0/inputs unchanged; CLI `projection-binding-progress11-cli-readback1.json` точно прочитан, Task1 InProgress/Active | Допущен reviewed fixed16 host cohort из frozen B/C; источник, настройки и thresholds не меняются |
| Fixed host cohort | Qualified NO-RETAIN: paired wall median .83795922>.80, allocation .88053940, wins7/7, allgap PASS | 16 свежих PID/exit0, все3094SHA×16 проверены; input/payload/runtime/49ownerbatches/15roots,16PNG просмотрены; reviewer+author пересчитали gate | Native helper/preparer/build не запускать, пороги/выборку не расширять |
| Archive / rollback | Проверены171entries ZIP и SHA `e202bc3eb5b7fd97ef7d6422264f73ce36eb43501ff442910f5316245f7c3b39` | `projection-binding-rejected-candidate1.zip`, `projection-binding-rejected-candidate1-audit.json`; receipt `projection-binding-candidate-rollback1.json`: restored=true, baselineExactBytes=true, srcTestsGitDiffEmpty=true; восстановлен толькоMainVM, удалён толькоownuntrackedtest | Все source/test snapshots и доказательства относятся к архивному кандидату; текущий baseline не выдаётся за1578 |
| CLI closure | `projection-binding-progress12-cli-readback1.json` — exactSummaryReadback, InProgress/Active, user text/criteria/relations preserved | Письменно зафиксированы NO-RETAIN/rollback, parent5/1/5 открыты; Task2/3/merge/release не начаты | Дальше другая гипотеза Task1 на восстановленном baseline |
