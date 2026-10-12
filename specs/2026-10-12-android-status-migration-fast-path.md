# Быстрая проверка миграции статусов при загрузке Android

## 0. Метаданные
- QUEST Expanded, performance-optimization/dotnet-desktop-client; риск high: путь старых данных и UI startup. Владелец Павел; task e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6.
- Ветка perf/android-e74c5d1a-task-loading, base origin/main fa1bfc03; первая задача согласованного последовательного scope. Следующие задачи не выполняются до acceptance/merge первой.
- Canonical template C:/Users/Kibnet/.codex/agents/templates/specs/_template.md. Central QUEST/review/linter/rubric/performance/UI/testing/commit/delivery stack и local UI-test MUST применяются.
- Codex/.NET10; немодельное изменение, model eval/runtime-tier сравнение не применимо. Samsung SM_F956B Android16, installed1.33.0/621 arm64. Тестовый disposable package org.unlimotion.loadingprobe использует текущую branch library, собственную копию3094 задач, EventLoop scheduler и native TextView; отличается от rendered release.
- User authorization: последовательное выполнение, CLI statuses/results, branch/worktree/commit/push/PR/merge, automatic SPEC approval после review/fixes. Установленный com.Kibnet.Unlimotion не заменяется; диагностические пакеты/fixtures отдельные. Релиз не публикуется.
- Evidence D:/CodexArtifacts/android-task-loading-20261012: fold6-cold-diagnostic.mp4, fold6-probe-stages.log, fold6-probe-init.log, fold6-dataset-validation.json, REPORT.txt. Полный diagnostic context: specs/2026-10-12-android-task-loading-execution.md.

## 1. Цель / Outcome contract
Уменьшить обязательную startup-проверку status migration для современных файлов, сохранив историческую миграцию и актуальное чтение каждого файла.
Исходная боль: долгое loading на Samsung Fold6; video показывает список примерно после25s. Cold Activity2–3s не заменяет ready measurement. Итог этого optimization item — измеренный code delta, migration/UI regressions и before/after evidence.
Цель всей e74: cold usable list<=5s, живой resume<=1s, локальное изменение видно<=5s. Эта optimization step не закрывает карточку автоматически: отсутствие полного AC блокирует completion/merge первой задачи; продолжаем исследование внутри той же ветки. При отсутствии значимого выигрыша эксперимент откатывается.

## 2. AS-IS
UnifiedTaskStorage.BuildInitialTaskViewsAsync запускает Task.Run, внутри вызывает private MigrateTaskStatusModel. Метод на каждом запуске File.ReadAllTextAsync + JObject.Parse каждого файла; современный файл с Status и непустой StatusHistory без IsCompleted/CompletedDateTime/ArchiveDateTime затем просто пропускается. Report не разрешает пропустить новые legacy files.
Actual native probe stages (один decomposition run, не paired series): migration7.141s, graph-worker4.134s, view-models2.688s, relations0.228s;3094/tasks,0 load errors. Init probe16.269s/CPU10.747s/~486.8MB. Scheduler/config/controls differ from release; это локализация стоимости, не candidate acceptance.
TryMigrateTaskStatusJson: property names OrdinalIgnoreCase; JObject exact duplicate property заменяет значение, различные casing остаются отдельными, FirstOrDefault выбирает первый. History — именно непустой JArray, включая элемент null. Modern Status presence достаточно; значение не валидируется этим fast-exit условием. Корrupt files catch/continue. Backups внеGit, owned atomic writes и report/flag при реальной migration сохраняются.

## 3. Проблема
Повторный DOM JObject и per-file async read scheduling оплачиваются для всех уже современных задач. Это отдельный измеренный startup cost; данные по phone не позволяют назвать его единственной причиной25s или background excessive CPU kill.

## 4. Цели дизайна
Не создавать постоянный cache/новый report; сохранить compatibility/freshness, case semantics и содержимое. Не переносить синхронный IO на UI: весь migration pass остается внутри существующего Task.Run. Reader — narrow private helper, не новый public API. Historical parser остается authoritative fallback.

## 5. Non-Goals
Изменения статусов/критериев/переходов, graph ordering/aliases/hashes/lock/generation guards, Git cadence, search, lifecycle cache, template/repeater UI, schema migration и reports format не входят в этот item. Скорость read-only probe не выдается за usable rendered release. Original phone data не правится экспериментом.

## 6. TO-BE
### 6.1 Ответственность
UnifiedTaskStorage.cs: read bytes один раз на background worker, classify modern bytes без JObject, fallback decode/parse теми же Newtonsoft semantics. TaskStatusMigrationTests: parity/corruption/legacy/new files; startup UI coverage: loading/projection/interaction с современными и legacy task files. Existing harness baseline/candidate на изолированном наборе.

### 6.2 Детальный дизайн
1. ReadAllBytes синхронно в migration worker. Убрать per-file ReadAllTextAsync здесь, сохранив async owned saves/backup/report для редких legacy writes. Метод private, единственный caller внутри Task.Run; комментировать эту обязательную предпосылку.
2. Utf8JsonReader с CommentHandling.Skip, AllowTrailingCommas=true, стандартным MaxDepth64. UTF8 BOM допускается; иные encodings или unsupported Newtonsoft syntax возвращают false и идут в fallback. Вся modern JSON syntax проверяется до единственного завершенного root object и EOF, включая неизвестные nested values. Наличие поля modern-status не допускает пропуск сломанного хвоста JSON.
3. Только top-level props определяют migration predicate. Любое case-insensitive presence IsCompleted/CompletedDateTime/ArchiveDateTime => fallback. Наличие Status фиксируется независимо от значения, как нынешний TryMigrate fast exit. History выбирается по first case-insensitive matching property name; точное повторение этого имени заменяет выбранное значение, другое casing его не заменяет. Непустая array определяется peek-copy reader после StartArray; comments не считаются элементами, null считается. Все nested tokens проходят syntax validation и conservative numeric gate: Number token с raw length>380 => fallback, включая unknown fields на любой глубине. Это сохраняет Newtonsoft integer materialization limit; false negatives длинных допустимых floats/negative numbers разрешены.
4. Если классификация modern true: processed++ и continue. Если false/JsonException либо InvalidOperationException непосредственно при GetString проверенного PropertyName (invalid UTF8/unpaired surrogate): decode ТОГО ЖЕ byte[] через StreamReader Encoding.UTF8 с BOM detection, JObject.Parse и существующий TryMigrate. Ошибка decoding означает fallback, не общий skip файла. Не перечитывать файл между classification и fallback; original backup/report semantics сохраняются. Не ловить иные programmer/runtime errors как modern true.
5. File read/parse failures продолжают skip через существующий catch. Никаких writes для modern/corrupt files, migration flag true только при changed>0. Counts в reports совпадают для JSON, которое старый parser успешно принимал.
Visual plan — существующий `launch → loading overlay → list → navigate/open` без layout changes. Native baseline video inspected; after video из automated run отдельного candidate fixture либо объективный fallback screenshot+trace. До Android acceptance использовать actual config/data parity и serial build/test/methods.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Visible outcome | Evidence | AC |
| --- | --- | --- | --- | --- |
| современный Git dataset | cold launch Fold6 | тот же список/порядок/статусы, меньшая задержка | UI regression + comparable device before/after | AC1,2,5 |
| legacy/new legacy after first run | launch/повторный launch | прежний перенос status/history; новые старые файлы не скрыты report | migration contracts | AC3 |
| modern-looking damaged JSON | launch | ошибки читаются прежним способом; повреждение не переписывается/не теряется | bytes equality + storage diagnostics | AC4 |
| живой background/freshness | resume/external local write | сохранение исходных целей1/5; не заменять отдельной desktop проверкой | device acceptance первой задачи | AC6 |

### 6.4 State / Interaction Matrix
| State | Trigger | Result | Edge |
| --- | --- | --- | --- |
| loading + modern file | validated classification | skip only migration, normal graph load continues | no status/history semantics change |
| loading + legacy/empty history | fallback | exact existing migration/backups | modern report не отменяет проверку новых файлов |
| invalid UTF8/UTF16/comments/unsupported literal | classification cannot prove modern | old decode/parser fallback | invalid files remain diagnosed/unchanged |
| IO/access failure | read | skip as baseline, normal graph errors retained | не превращать unavailable в valid data |

### 6.5 Decision Ledger
| Decision | Owner | Option | Confidence | Risk | Needs user |
| --- | --- | --- | --- | --- | --- |
| один raw read и parser fast path | agent | worker byte[] + conservative classifier/fallback | 0.85 | parity errors mitigated tests/review | Нет |
| report-based global skip | agent | отклонено: new legacy files обязаны мигрировать | 0.99 | скрытые старые данные | Нет |
| lower5/1/5 to close item | user | не менять; incomplete e74 stays InProgress | 1.0 | ложная completion | Нет, поручение уже есть |
| independent review before execution | instructions | обязателен для migration-sensitive delta | 1.0 | read-only effective sandbox отдельно проверяется | Нет |

### 6.6 Runtime / Config / Data Contract
| Area | Source of truth | Change | Compatibility | Validation |
| --- | --- | --- | --- | --- |
| task JSON | freshly read bytes + historical JObject | allocation/scheduling only | BOM/UTF16/casing/duplicates/legacy retained | contracts + byte equality |
| reports/backups | existing migration method | none | flag/count/rollback preserved | report assertions |
| Android | same device/fixture/package/build/scheduler | library delta only | original APK untouched | before/after repeated modes, rendered UI separately |

## 7. Алгоритмы / инварианты
Modern classifier must only return true where TryMigrateTaskStatusJson(JObject.Parse(decoded bytes)) returns false. False negatives допустимы (fallback), false positives запрещены. Чтение всех файлов сохраняется, mtime/report не является proof-of-content. Source file alias/id ordering, SHA observed writes, pending watcher cutoff and source graph publication remain unchanged. Никакого parser replacement для normal task storage.

## 8. Интеграция
Только существующий migration вызов в BuildInitialTaskViewsAsync. Helper private/static; обычный startup/source switch получают оптимизацию при тех же Task.Run boundaries. Test direct method reflection in diagnostic harness всегда с fixture guard; нельзя экспортировать helper как public feature.

## 9. Данные / output
Production file schema без изменений. Bench records taskCount/time/CPU/allocated, configuration identity и limitations; no titles/secrets публикуются. Native helper/source project и raw user dataset local-only outside Git. SPEC и необходимый код/tests коммитятся.

## 10. Совместимость / rollback / cleanup
Rollback conventional revert optimization commit. Отдельный диагностический APK/fixture можно удалить после приемки с exact package/path guard; оригинальный installed package/data не удаляется. Не архивировать текущий worktree до task result/merge.

## 11. AC / Test matrix
| AC | Acceptance | Planned evidence |
| --- | --- | --- |
| AC1 | modern JSON bytes unchanged; flag false and no backup/report added | parameterized migration tests |
| AC2 | classifier parity for mixed casing, exact duplicates including escaped/unescaped exact names, nested fake props, null history item, comments/trailing comma, BOM/UTF16 and invalid property name encoding/surrogates | direct private-helper delegate oracle: true implies historical decode/JObject.Parse success and historical migration predicate false; Init content/flag/reports |
| AC3 | old statuses/history/backup preserved; newly added legacy migrates on second Init | existing TaskStatusMigrationTests + new regression |
| AC4 | malformed tail/non-object/invalid parser input never skipped as valid modern; bytes retained and diagnostics | direct classifier false; mixed modern-corrupt + legacy fixture TasksProcessed count; same-byte fallback legacy regressions for invalid UTF8/property surrogate |
| AC5 | targeted→full green; relevant UI loading/list/status flow added and run; device migration latency/allocations significant gain in repeated comparable runs | TUnit main/UI + Android probe/native evidence |
| AC6 | whole e74 cold/resume/freshness5/1/5 remains mandatory before completion/merge | separate actual-ready/action/native series; unmet means continue first task |

Tests serial Avalonia resources with --maximum-parallel-tests1, TUnit --treenode-filter and --minimum-expected-tests1; no VSTest syntax. Builds serialized -m:1 --disable-build-servers -p:UseSharedCompilation=false. Original baseline targeted MainScreenLoadingUiTests4/4 and build0errors retained, not broad migration proof. Run baseline migration/UI before change; full after.

## 12. Expected User Review Objections
| Objection | Answer/action | State |
| --- | --- | --- |
| опять «оптимизация» без результата на Fold | before/after native migration + actual e74 ready gate | validation pending |
| старые задачи будут сломаны | exact fallback/decode and adversarial migration tests | planned |
| отчёт закроет25s loading | first task stays InProgress until full accepted outcome | enforced |

## 13. План
SPEC independent review/fixes → auto approval → baseline migration/UI tests → single optimization delta/tests → targeted/full → same-package device before/after → retain or revert → continue first-task bottleneck/acceptance work → reviewed PR/merge/CLI completion only when whole e74 done.

## 14. Открытые вопросы
Продуктовых user decisions нет. Effective read-only reviewer sandbox должен быть подтвержден; иначе technical independence не заявляется и отдельный adversarial fallback записывается. Точный эффект неизвестен до paired measurement.

## 15. Profile compliance
Performance hypotheses/methods/risks/retain-or-revert явны; data compatibility и UI testing обязательны. Independent optimization commit; broad/full validation and exact-head CI before merge. No claim release/install original production.

## 16. Files
src/Unlimotion/UnifiedTaskStorage.cs; src/Unlimotion.Test/TaskStatusMigrationTests.cs; relevant startup UI test file selected by existing patterns; this SPEC. Иные product changes потребуют review scope supplement.

## 17. Quality gate / review
### Post-SPEC Review — fixes pending re-review
- Scope reviewed: this SPEC; source migration method and historical predicate; TaskStatusMigrationTests/SingleViewStartupUiTests; native stage log; central review/performance/testing and local UI MUST. Product diff absent.
- Scope/Evidence: installed video and aggregate stage timings inspected; native tool accurately labeled non-rendered, not release before/after. External APK unchanged.
- Contract: modern true is a one-sided proof of historical skip; all original files still read; counts/backups/report preserved. Whole-task5/1/5 remains completion/merge gate.
- Adversarial pass: BOM/UTF16, comments, duplicate casing/exact escaped names, null-history-element, invalid UTF8/property surrogate, malformed tail, new legacy after report and private helper caller context checked. Explicit GetString decoding fallback fixed; helper true oracle and mixed fixture count added.
- Role-Based Review Result: domain — legacy status/history and new incoming legacy preserve workflow; UX — loading/list/status interaction retained, actual-ready evidence remains mandatory; tester — direct oracle distinguishes fast false-positive from identical downstream diagnostics, UI and full suites planned; architect — existing worker boundary, no cache/report/global skip; delivery/security — private fixtures/separate APK/CLI lease, no release/original data writes.
- Review facility: separate independent-reviewer role inspected read-only by behavior, but effective child FS is danger-full-access. Technical read-only sandbox unavailable. Do not claim sandboxed independence; root performs separate adversarial fallback above, residual risk is reviewer was instructed not to mutate rather than enforced by FS. Actual reviewer reported no writes.
- Findings: HIGH decoding exception fallback fixed in design; MEDIUM test oracle/count/escaped names fixed; MEDIUM missing expanded roles/passes/depth fixed. Required re-review targeted to these areas before automatic approval.
- Depth checklist: scope/product diff unchanged; AC/test links explicit; original scenario/decision ledger/objections nonempty; baseline tests5/5 and UI4/4 retained, future tests not claimed PASS; no unsupported single-cause/performance claim; encoding/duplicates/tail/new-legacy negatives covered; no docs/changelog/public API hidden changes; manual challenge «fast classifier skipped accepted legacy» addressed with decoder fallback and report count oracle.
- No-findings justification: not yet applicable until targeted re-review; no open user decision. Stop decision NEEDS-FIX pending re-review, code EXEC not yet started.
- Rubric: goal5/evidence4/design4/risks4/testability5/autonomy5=27/30. Linter completeness reassessed after review fixes above.

## 18. Journal
12.10: Native stage evidence inspected;7.141s repeat migration despite modern dataset. Narrow allocation/scheduling hypothesis chosen; no source code changed before review. User automatic approval applies only after review/fixes; no new permission request.

12.10 post-SPEC targeted re-review PASS: separate reviewer подтвердил закрытие HIGH/MEDIUM designs, root adversarial fallback checked decoder exceptions/escaped names/report counts/Task.Run caller. Effective FS read-only отсутствует и не заявляется. Automatic approval по действующему user instruction: EXEC разрешён для этого delta. Baseline migration5/5, UI4/4;5 native decomposition runs retained thermal1/2, migration3.61–5.15s. Это baseline optimization item, не whole-task acceptance. Product changes начинаются после этой записи.

Preliminary post-EXEC review: migration26/26, startup UI6/6 и inspected Skia PNG; найден MEDIUM materialization counterexample —381-digit nested unknown integer принимается Utf8 syntax, но отвергается JObject. Это нарушает уже согласованный one-sided invariant; обязательное in-scope исправление — token traversal/numeric gate на всех depths +380/381/negative/report-count regressions, затем targeted re-review. Начатый full-run до этой коррекции маркируется pre-correction и не заменяет финальный full-run. Пользовательского решения не требуется.

12.10 после исправления numeric gate: отдельный reviewer подтвердил targeted code re-review PASS; новых находок по изменённой поверхности нет. Финальная сборка и TaskStatusMigrationTests31/31, SingleViewStartupUiTests6/6 выполнены. Визуальное доказательство — inspected Skia PNG mixed-status-startup.png, не Android latency. Полный pre-correction прогон остановлен как superseded и записан canceled-incomplete. Полный baseline main от0f6c0f7a (product basefa1bfc03) выполняется в отдельном managed worktree; это ретроспективная проверка неизменённого исходного кода, а не полный прогон, выполненный до EXEC. Финальные full main/Headless и paired Android latency/allocations остаются pending. Stop decision whole post-EXEC: NEEDS-FIX до этих обязательных проверок.

12.10 Android packaging: native decomposition baseline/candidate APK сохранены отдельно вне Git. Actual Avalonia candidate Release arm64 package org.unlimotion.taskloadingbench собран без ошибок, baseline того же package/build options собирается от0f6c0f7a. Секретов и original config не копируем: собственный Settings.json указывает только на diagnostic fixture, Git backup и Updates:AutoCheckEnabled выключены. Этот package не заменяет com.Kibnet.Unlimotion; его сборка не доказывает UI acceptance. Wireless transport потерян до paired run; прежний192.168.0.36:34311 timeout, после restart devices/mDNS пусты и Android USB не обнаружен Windows. Пользователь сообщил сеть/USB подключены; запрошен точный текущий IP/port. CLI result/read-back обновлён, task InProgress/execution AwaitingInput, вопрос7331a9a2-6b61-4605-be8b-678a4525839d; независимые проверки продолжаются.

12.10 baseline main завершён:1472/1473, один failure ClearingStart_HidesSectionAndPersistsReset_WithoutRestoringRepeater(390, False), ожидание повторного сохранения даты после disk read-back (MainControlRepeaterStartDateUiTests:336/369). Это исходный product code без оптимизации; полный baseline НЕ PASS. Отдельный повтор всего класса на baseline9/9 прошёл. Exact-base fa1bfc03 GitHub Tests run38173809477 success, Main20m21s/Headless3m33s; это отдельное CI evidence Debug, не превращает локальный Release failure в успех. Сбой сохранён как baseline finding; unrelated repeater product/test changes не добавлены. Baseline Headless и финальные candidate main/Headless запускаются последовательно с полной видимостью failures. После fetch origin/main=18e820f1 (новые независимые emoji filters/Headless dispatcher fixes); перед delivery проверить интеграцию с актуальным main и exact-head CI, не выдавать старый base run за проверку нового head.

12.10 candidate full до интеграции main: Main1499/1500 (NewTask_TitleNotResetAfterFileSave:436, двухсекундное составное ожидание VM/disk title), Headless62/63. Оба полных прогона FAILED. Title isolated retry candidate1/1 и baseline1/1, затем весь candidate MainWindowViewModelTests97/97; это не PASS полного набора и не доказательство baseline-flake нового failure. Отдельный reviewer не нашёл доказанного дефекта reader: migration вызывается только Init, Connect/create-command/pending saves ожидаются до Title-edit; residual watcher/scheduler timing не исключён. MEDIUM требует paired class-context/evidence при воспроизведении; assertion/timeout не ослабляются. Root fallback сверил compound assertion, Init boundary и trace/TRX; обязательные Android AC остаются blocker завершения, overall stop NEEDS-FIX.

Headless finding локализован: rendered child GitIndicator_SaveSyncFailureSwitchKeyboardAndRenderedMatrix упал в fixture Commit:547 при Stage(*) с NotFoundException на временном .unlimotion.lock. Исправление9f6a6bec уже входит в current main. Рабочая ветка синхронизирована с origin/main18e820f1 conventional merge commit bb12868c; owned uncommitted source/tests/SPEC сохранились, предварительный patch/SPEC backup вне Git. Это integration main в task branch, а не delivery/merge задачи в main. Baseline/candidate APK и прежние проверки помечаются pre-integration; актуальная сборка и затронутые UI/storage проверки повторяются. Первая задача InProgress/AwaitingInput, следующие не начаты. CLI result refresh в AwaitingInput запрещён executionStateDenied/no mutation; новый evidence хранится в уже связанном REPORT, pending question не закрывается фиктивным ответом.

12.10 integration validation bb12868c + owned uncommitted delta: Main/Headless Release builds0errors; migration31/31, SingleViewStartup6/6. Paired class-context до integration: MainWindowViewModelTests candidate97/97 и baseline97/97. Title failure в этом контексте не воспроизвёлся; причина полного failure не установлена, исходный full1499/1500 остаётся FAILED. Integration full Headless65/66: прежнего lock NotFoundException нет, новый failure того же rendered Git-test — TimeoutException Text did not become Idle после retry/fixture Commit (строки402/424). Failure-time output показывает DirtyFileCount1 и прежнее failed-operation состояние; имени файла или причины поздней записи нет. Isolated current-head retry1/1 прошёл, это НЕ заменяет full gate. Таймаут/ассерты и Git product behavior не изменены. Полный Main на интегрированном head и exact-head CI ещё не выполнялись. Independent partial review + root fallback stop NEEDS-FIX: доказанного дефекта reader нет, full failures и Android paired/5/1/5 остаются незакрытыми условиями. Продолжение performance выбора/retention требует реального transport; точный IP/port от пользователя ещё не получен. Весь scope не завершён, первая задача не delivered, последующие не начаты.
15:23 device transport restored by user: 192.168.0.36:40013, getprop SM-F956B (ADB inventory SM_F956B); diagnostic guards normalize hyphen/underscore. Pending CLI question7331a9a2 answered using actual connected transport; read-back InProgress/Active, same owner/lease. Latest result15:25:41 written/read back. Current integrated full Main started, output integration-full-main.log; no source delta beyond previous uncommitted migration.
Five alternating pre-integration native stage pairs completed: migration mean5654.70 ->4977.41ms (-12.0%), overall CPU9721.85 ->8433.01ms (-13.3%), allocated443431261 ->345896955 bytes (-22.0%). Overall decomposition mean14441.15 ->14690.87ms (+1.7%), not latency improvement. Thermal first baseline0, first candidate1, all later2; pair4 candidate regression preserved. Graph/VM portions unchanged by this delta; no claimed whole5/1/5 or current-head acceptance. Summary paired-summary.csv, ten JSON/log records retained.
ActualAvalonia disposable baseline installation rejected INSTALL_FAILED_CONFLICTING_PROVIDER: com.Kibnet.Unlimotion.fileprovider owned by installed original. Own packaging error, not product loading failure. Original release/config/tasks untouched. Prepared identical external diagnostic-package.targets/AndroidManifest.DiagnosticUpdater.xml using org.unlimotion.taskloadingbench.fileprovider only for ownpackage; updates disabled. Baseline rebuild in progress; future current-main candidate artifact must be labeled separately from pre-integration C1. No optimization retention/delivery/next task yet.
12.10 continuation: Five alternating native baseline/C1 pairs on the same3094-task fixture completed. Mean migration5654.70->4977.41ms (-12.0%), CPU9721.85->8433.01ms (-13.3%), allocated443431261->345896955bytes (-22.0%). Mean complete decomposition14441.15->14690.87ms (+1.7%); thermal0/1/2 and pair4 regression retained. Stable allocation gain is evidence for a provisional code checkpoint, not whole-task latency/retention acceptance. Actual Avalonia readiness/action/resume/freshness remains unverified because the physical display is unavailable. Original installed APK is not replaced; diagnostic own-provider packaging is external-only.
Integrated Main on18e820f1+reader completed1527/1528 FAILED49m12; sole Raven default-revisions LicenseLimitException. NewTask_TitleNotResetAfterFileSave PASS. Reviewer verified server fixture does not invoke this file migration; no reader causal link. Original baseline isolated same server scenario1/1PASS with sanitized loopback license metadata, not a new full PASS or proof of the failing run effective license. No licensing/settings/product revisions changed. Full Main green remains mandatory.
Reviewed autosave test-only correction committed separately fcb36b3a: ordinary command completion and exact diskTitle instead of duplicate manual save, preserved Idle5s/assertions, synthetic diagnostics. Targeted1/1 and full Headless66/66 PASS; rendered PNG and beforeCommitdirty1/afterCommitclean/freshIdle0 inspected. Independent short postEXEC PASS covers only that correction. Whole migration/e74 review still NEEDS-FIX.
Remote main advanced690d4184 (independent SSH reset). Task branch integrated it as8ff81b65, owned delta preserved. Fresh Release Main build0errors, migration31/31, SingleViewStartup6/6; latest Headless is running. Prior APKs/results retain their actual revisions. A provisional implementation checkpoint may be pushed in a draft PR to obtain exact-head CI; it is not accepted retention, completion or merge. Final retain-or-revert and next performance item require comparable rendered device evidence and full mandatory gates; first task staysInProgress, next tasks unclaimed.
