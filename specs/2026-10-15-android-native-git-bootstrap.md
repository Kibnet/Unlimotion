# Android: загрузка упакованных OpenSSL до первого Git-вызова

## 0. Метаданные
- Delivery / Android native startup / UI; medium, владелец Codex, 15Oct2026.
- Codex desktop/PowerShell Windows, фактический model ID не доступен. Model eval не применим: исправление native loader, не model behavior.
- Ветка perf/android-e74c5d1a-task-loading, worktree android-task-loading; PR325 draft. Это зависимый checkpoint задачи e74c5d1a, не новая tracker task; следующая задача не начинается.
- База53fbda1d104b3c14ee764d59815e5304d22c8976; SDK401 Android build shadow, API34 x64 emulator, Release/default arm64+x64/AOT. Native package2.0.324-android.7; F-Droid conditionals сохраняются.
- Инструкции: central delivery/QUEST/testing/review; dotnet-desktop-client, UI automation, tool execution; local UI MUST. Parent performance5/1/5 сохраняется. User поручил autoapproval после review/fixes.
- Evidence private D:/CodexArtifacts/android-task-loading-20261012 (далее DROOT). Не публиковать task fixture/видео/сырые данные пользователя.

## 1. Цель
Устранить воспроизводимую ошибку LibGit2Sharp.Core.NativeMethods при запуске Android Release с имеющимися упакованными OpenSSL. Операции локального Git и обычный старт должны работать после первого запуска процесса.

Outcome: native Repository.Init → Stage → Commit → clean Status → read commit/blob действительно проходят в отдельном private test repo; обычный APK открывает список и карточку без наблюдавшейся Git loader ошибки. Это функциональное исправление; скорость и работа удалённого Git/TLS не выводятся из local smoke.
Stop: нет правдивого native proof — нет PASS; first attempt failed → preserve failure, diagnose same mechanism. Новая зависимость/обход TLS/необоснованный fallback требуют отдельного review. Parent задача не завершается этим checkpoint.

## 2. AS-IS
MainActivity.ConfigureCoreAppServices вызывается AndroidApp.OnCreate и Activity; AppServicesGate/_coreAppServicesConfigured обеспечивают once-per-process. Первая EnsureGitSafeDirectory вызывает GlobalSettings.SetConfigSearchPaths и может навсегда poison static NativeMethods initializer. Позже вызываются certificate bundle/App.Init/второй safe directory.

API34 own nondebuggable APK extractNativeLibs=true содержит libssl.so/.so.3 и libcrypto.so/.so.3; установлены только .so aliases. libgit2 DT_NEEDED libssl.so.3/libcrypto.so.3, libssl и libssh2 также требуют libcrypto.so.3. Aliases byte-identical с .so.3 и имеют SONAME .so.3. В B/C warmups/pairs одинаковые Git messages, transient red notification; финальные root/card clear. Это не missing APK entry.
Evidence: native-git-elf-dependencies-baseline-candidate.json (24ELF обе ABI), native-git-installed-resolution-proof.json, installed directory/SHA/dump, warmup known notifications. Официальный AOSP bionic source объясняет preAPI36 nondebuggable extraction только lib*.so: https://raw.githubusercontent.com/aosp-mirror/platform_bionic/main/android-changes-for-ndk-developers.md (section Native library extraction and .so suffix; SONAME lookup API23).

## 3. Проблема
Транзитивные DT_NEEDED .so.3 не разрешаются из установленного каталога, хотя правильные библиотеки уже установлены под aliases. Ошибка возникает до первого полезного Git-вызова и poison static initialization.

## 4. Цели дизайна
Один platform bootstrap, существующий lifecycle gate, trusted own-package paths. Не менять ELF/dependency package, portable Core/VM или task schema. Actual native operations и UI flow являются доказательством, source pattern checks только дополняют.

## 5. Non-goals
Не менять TLS verification/cert callbacks/cacert, версии native packages или producer scripts, настройки Git/credentials, remote push/clone, F-Droid distribution/release. Не чинить произвольный native package discovery. Не добавлять product diagnostics flags/probe. Не заявлять ускорение/Fold6 acceptance.

## 6. TO-BE
### 6.1 Ответственность
MainActivity platform bootstrap: в ConfigureCoreAppServices перед EnsureGitSafeDirectory(dataDir,dataDir) загрузить существующие aliases. Test-only source/runner в scripts/android-native-git-smoke: отдельный private package, фактический Git/обычный UI проверяются отдельно.

### 6.2 Детальный дизайн
Под существующим AppServicesGate загрузить System.Runtime.InteropServices.NativeLibrary.Load по Path.Combine(context.ApplicationInfo.NativeLibraryDir, "libcrypto.so"), затем "libssl.so". NativeLibraryDir обязателен, абсолютен; не искать в cwd/пользовательских папках/системном OpenSSL. Проверить aliases до первой загрузки. Хранить IntPtr handles статически весь процесс; не Free при Activity destroy/background. Каждую успешно загруженную библиотеку не загружать снова при повторном вызове или частичной ошибке.

Если bootstrap не удался: WriteStartupError + Android Log.Error с конкретным исключением; не throw из bootstrap, чтобы сохранённый локальный список мог открыться по существующему поведению. Не скрывать последующие Git failures/не заявлять success. `_coreAppServicesConfigured` сохраняет исходное once behavior; частичная успешная загрузка не освобождается и не дублируется. Ошибка возврата/нет libdir не заменяется ненадёжным fallback.

Ожидаемый namespace/SONAME effect пока гипотеза: actual native smoke обязан подтвердить её. При неработающем namespace не переключать бездумно на JavaSystem.Load, сначала evidence/review correction.

Visual planning: existing before notification PNG/native video → after same loading/root/card без loader red error; layout/copy unchanged. Existing actual B captures подходят как before shared error. After: обычный own-package APK без Probe, fixed3094fixture, ранние PNG/native video и ownedPID logs с полным startup window; открытая root и та же task card/24children. Video PTS не использовать как host latency.

Test-only harness: committed Probe C# + Python runner для подготовки точной временной overlay копии MainActivity и MSBuild targets вне source tree. Overlay заменяет единственный AndroidApp.OnCreate ConfigureCoreAppServices(this) на тот же вызов + Probe.Run(this); targets Compile Remove original/Include overlay+Probe только с явным own smoke ApplicationId org.unlimotion.nativegitsmoke. Для negative control тот же overlay дополнительно оборачивает только выражение nativeLibraryDir в Probe.GetNativeDirectory(context, actual): при test-only sentinel в own externalFilesDir возвращает заведомо отсутствующий абсолютный подкаталог; иначе точное исходное значение. Все overlay substitutions требуют unique signature и manifest SHA, product source неизменён; sentinel/control отсутствуют в ordinary APK. Runner не меняет product source. Запись результата atomically в own externalFilesDir/native-git-smoke.json; test repo новый уникальный внутри own internalFilesDir. No task-space/network/recursive user cleanup. Probe выполняется перед base.OnCreate после настоящего product bootstrap. Сохраняет actual operation outcomes/errors, PID/ABI, loaded alias paths, commit/blob/clean status. Twice ConfigureCoreAppServices sameprocess затем второй Git operation доказывают idempotent startup; снимки retainedhandles до/после допустимы test-only reflection как дополнение реальным операциям. Повторный force-stop/start — новый процесс, новый unique repo. Каждый control выполняется с coldnewPID, stale output до старта удаляется только внутри own package. JSON создаётся через JsonObject без AOT reflection serializer. Baseline probe build если потребуется: тот же overlay без product fix должен показать реальную прежнюю ошибку; существующие before logs уже сохраняются. Runner failure при отсутствующем/неполном/stale JSON/PID/package mismatch, а не ложный PASS.

### 6.3 Видимые сценарии
| Scenario | Trigger | Result | Evidence | AC |
|---|---|---|---|---|
| Release startup with local Git | Start newprocess | Native Git works, no observed loader notification, root/card usable | Native smoke + ordinary video/logs | A1,A2,A3 |
| Repeated startup calls/process restart | Configure twice; restart | No duplicate load/release; newprocess also works | Probe twice/newPID | A2 |
| Missing dependency | Negative smoke variant or controlled test path | Explicit diagnostic, no success, no arbitrary fallback | Failed native JSON/control | A4 |

### 6.4 Состояния
| State | Trigger | Result | Error/concurrency |
|---|---|---|---|
| No handles | First bootstrap | crypto then ssl retained | gate serializes; failure diagnosed |
| crypto only | Retry before configured | skip crypto, try ssl | retain successful handle |
| Both/configured | Activity repeated | existing early return | no unload onDestroy |
| New process | OS restart | load both anew | no persistent handle data |
| Missing native libdir/file | Startup | explicit error; existing local startup allowed | later Git failure remains visible |

### 6.5 Решения
| Decision | Owner | Chosen | Confidence | Risk | User before EXEC |
|---|---|---|---:|---|---|
| App preload vs rebuild all ELF | agent | Preload installed same-byte aliases | .8 | namespace must be tested | Нет |
| Release own package | user scope/agent detail | No original app replacement | 1 | boundary/provenance checks | Нет |
| No phone now | user | API34 x64 actual; arm64 static only | 1 | physical boundary explicit | Нет |
| No latency claim | agent | Functional checkpoint only | 1 | parent remains open | Нет |

### 6.6 Контракты
| Area | Source | Change | Compatibility | Verify |
|---|---|---|---|---|
| Native aliases | Current csproj/package | None | bothABI/Fdroid retained | ELF/installSHAs |
| FirstGit call | ConfigureCoreAppServices | preload before safeDir | existing gate preserved | source order/native probe |
| Handles | process static | crypto/ssl pointers | no persistence/migration | samePID repeat/newPID |
| Certificates/config | existing code | None | no trust bypass | diff/full tests |
| Probe | scripts only/custom targets | test APK only | absent ordinary assembly | finalPE/provenance |

## 7. Правила
crypto → ssl → firstGit; loader handles live until process exit. Load only own installed aliases. No success from file existence/green CI alone. No timings inferred from smoke. Never write user taskspace. Error evidence kept.

## 8. Интеграция
One invocation inside ConfigureCoreAppServices before first safe directory. AndroidApp/Activity use same path. OnDestroy unchanged. Existing certificate setup follows preload.

## 9. Данные/состояние
Two process-only handles, no task/config/model schema. Test JSON/repo confined own smoke package; each invocation unique ID/newrepo.

## 10. Rollout / rollback
Build own Release packages only, no original phone. Same native binaries and trust config. Revert bootstrap/test delta for rollback; no data migration. Preserve failed/successful diagnostic artifacts. F-Droid compile/static contracts remain; runtime on physical ARM64 not claimed.

## 11. Приёмка/тесты
| AC | Test/check | Artifact | Untested boundary |
|---|---|---|---|
| A1 Actual native local Git works | Init/write/Stage/Commit/StatusClean/read exact blob in nondebuggable API34 x64 own APK | JSON/native log/APK hash/payload proof | no remote TLS claim |
| A2 Idempotent/restart | twiceConfigure then actual secondrepo operation; force-stop/start newPID uniqueproof | two complete JSONs/handle snapshots | no physicalARM64 |
| A3 Ordinary UI startup | existing actual B loadererror → ordinary newAPK3094root/card24, earlyscreens/video/ownedPIDlogs no corresponding loadererror | rawwindows/nativevideo/PNG review/fixture3094SHA | emulatoronly; no5/1/5 claim |
| A4 Negative control | test-only omit required alias/loading path before firstGit in isolated smoke package; explicit non-success and preserved error, local startup check if bootstrap error injected | failed JSON/log and ordinary no-crash/root flow for error path | test overlay never shipped |
| A5 Compatibility/build/tests | Android Release bothABI payload same SHA; F-Droid static contracts; relevant startupUI tests; full current-head Main+Headless+AndroidPkgCI | tests/TRX/CI/sourceaudit | Fdroidruntime unavailable |
| A6 Probe isolation | source and linked/finalPE ordinary has noProbe, smoke package unique; targets fail closed for other ID; stale/missing JSON controls | SHA/PE/negativecontrols | none |

Obligatory native UI runner/coverage added in scripts, not a source-regex-only test. Headless existing SingleView startup UI complements Android proof. Native operations persist/read exact content after a commit; no mere constructor smoke. Visual artifacts inspected beforePASS. Normal product launch uses plain candidate without test overlay. Before shared error clips retained; do not re-time old captures. Native smoke/debug observations are not ordinary performance captures. No host build/test during future performance capture. Repeated validation only after fixes/failure/newrisk; bounded ADB/child timeouts and fail on cleanup uncertainty. Test scripts expose explicit evidence paths and fail on unexpected ownpackage/PID or missing assertions.

Concrete commands planned (all paths/output unique, no original package install):
- `python scripts/android-native-git-smoke/run.py prepare --source <shadow> --output <private-overlay>` emits targets/source manifest, fails if source signatures not unique or package not own.
- `dotnet publish src/Unlimotion.Android/Unlimotion.Android.csproj -c Release -o <unique-smoke-output> -p:ApplicationId=org.unlimotion.nativegitsmoke -p:CustomAfterMicrosoftCommonTargets=<overlay.targets> -m:1 --disable-build-servers -p:UseSharedCompilation=false` under pinned shadow SDK401; bounded40min childtree cleanup.
- `python scripts/android-native-git-smoke/run.py run --adb <adb> --serial emulator-5580 --apk <Signed.apk> --output <new-evidence>` validates positive/newPID repeat/negative from same smoke APK, collects UI screenshots/video/logs. Managed probe JSON is supplemental to ordinary UI run; not a latency gate.
- `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/SingleViewStartupUiTests/*" --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx --results-directory <unique>`; existing test build unchanged if no managed desktop source changes.
- `pwsh -File scripts/test-android-build-scripts.ps1`; new-head CI Main/Headless/AndroidPkg and finalAPK source/payload/ELFSHAs.

Smoke runner additionally verifies actual APK ApplicationId with aapt before install, explicit serial/API34/x64/ownAVD identity, own provider authority, nondebuggable and extractNativeLibs manifest; never installs arbitrary original package. Each trial captures own PID/focus/window dumps/early screenshots and bounded screenrecord. No automated overall UI PASS from Git JSON alone: visual qualification/manual review of actual root remains required. Output directory is fresh/nonoverwriting, ADB commands and screenrecord child cleanup bounded; incomplete output or cleanup uncertainty fails collection. No `pm clear`, arbitrary uninstall, user task edits, or broad remote rm. A4 bootstrap diagnostics checked in ownedPID log, not merely expected Git failure; positive logs checked for corresponding loader failures. Ordinary root/card proof uses original manifest3094 and separate plain APK, not this small smoke task space.

## 12. Риски/edge cases
NativeLibrary managed loader namespace vs LibGit2Sharp: actual operations must decide. Future no-extractionAPK mode needs separate compatibility change; currentmanifest extractNativeLibs=true. Fdroid currentarm64aliases same dependencyshape; do not silently addx64. Logs may contain apppath, keep private. Disk/ADB errors distinguish infrastructure from product; preserveattempts and verifyownresources.

| Likely objection | Mitigation | State |
|---|---|---|
| Это ускорит старт? | No claim; parent measured5/1/5open | mitigated |
| Проверили настоящий Git? | Stage/Commit/readblob, nofixture-only proof | planned mandatory |
| Сломает сертификаты/телефон? | samebytes/trust/config, ownpackage, no phone | mitigated |
Rework checklist: actual startup/taskcard named; allAC evidence mapped; technical decisions agentowned; relevant UX/tester/architect/delivery independent review mandatory; EXECproof path available.

## 13. План
Independent SPEC review/fixes → standing autoapproval → bootstrap/testharness → local static/build/Headlessstartup → own Release smoke/plain exactsourceproof → native operations/negativecontrols/restart+ordinaryUI → postEXEC/fullcurrentCI → continue parent performance diagnosis. No merge/complete/nexttask yet.

## 14. Вопросы
User-owned blocking questions отсутствуют. Technical namespace hypothesis решается экспериментом; если дизайн не работает, prospective correction reviewed до нового product mechanism.

## 15. Профиль
Delivery/.NETAndroid/UI; central QUEST expanded из-за platform integration/lifecycle. Actualnative/ordinaryUI/newtestcoverage, privateartifacts, rollback и nooverclaim обязательны.

## 16. Файлы
| File | Change | Reason |
|---|---|---|
| src/Unlimotion.Android/MainActivity.cs | preload aliases/handles | resolve requiredSONAME first |
| scripts/android-native-git-smoke/* | Probe/overlay+runner/nativeUI checks | meaningful Android regressioncoverage |
| specs/this,parent | decisions/evidence | audit |

## 17. Было → стало
FirstGit immediately fails NativeMethods → own OpenSSL aliases preloaded, actual Git operations pass. APKbinary/TLS bytes unchanged. Parentloadperformance pending → remains pending.

## 18. Альтернативы
Rebuild DT_NEEDED/SONAME aliases in all producers requires new native version/Fdroiddelivery and bigger rollout. extractNativeLibs=false requires uncompressed/aligned compatible loader changes with wider packagingimpact. Platform OpenSSL/system lookup unsupported and unsafe. Preload same-byte installed aliases is bounded/reversible; if native proof fails, reconsider technicalapproach throughreview.

## 19. Quality gate / review
Post-SPEC advisory review PASS 15Oct, reviewer /root/startup_profile_review. Behavioral read-only in writable sandbox; технической изоляции нет. Scope/evidence: actualSPEC/MainActivity/csproj/installedELF proof/AOSP/centralreview-linter-rubric. Contract pass: bounded platform bootstrap, noAPI/schema/TLS change, all A1–A6 mapped. Adversarial pass: namespace mismatch/poisonedcctor/partialload/repeatedConfigure/restart/staleJSON/AOTserialization/ordinaryProbeleak/falsefileexistencePASS checked. Negativehook acts before preload, ownsentinel/coldPID, positiveactualdir; existing WriteStartupError includes Log.Error so no duplicate call. No findings after clarification/re-review; basis: actualnative commit/read mandatory and ordinaryUI separate.

Linter1–20 individually PASS: 1outcome,2ASIS,3problem,4goals,5non-goals,6responsibilities,7integration,8invariants,9errors,10noperfclaim,11processstate,12compatibility,13rollback,14AC,15positive/negative,16commands/stops,17plan,18decisions,19expandedform,20profile. Rubric30/30: goal5/ASIS5/design5/safety5/testing5/autonomy5.

| Role | Verdict | Evidence/reason |
|---|---|---|
| Domain | PASS | ActualInit/Stage/Commit/cleanStatus/exactblob |
| UX | PASS | notifications/ordinaryloading/root/card separate |
| Tester | PASS | positive/repeat/restart/negative/stalecontrols |
| Architect | PASS | firstGitordering/gate/processhandles/failurepolicy |
| Delivery/security | PASS | ownpackage/private/TLSbytes/Probeabsence/newCI |

Depth/manualchallenge: scope/hiddeninitialization/contracts/edgecases/rollback/docs/objections checked; "Did Git really commit/read?" answered by mandatoryactualnativeproof. Stop decision: SPEC ready, autoapprove; namespace runtime hypothesis stillunproved, A1–A6/currentheadCI/PostEXEC mandatory. ARM64/Fdroidruntime/remoteTLS/parent5/1/5open. Post-EXEC не выполнен.

### Post-EXEC functional checkpoint15Oct21:32
Advisory functional PASS, reviewer /root/startup_profile_review; behavioral readonly in writablesandbox, no technicalisolation. Actualscope approvedSPEC/productdiff/currentPython/Probe/README/protocolcontrols/source/8PEpackaging/native4ops/negative/ordinaryrawwindows3094/media/CIrootTRX+stages. Independently recomputed23windowbrackets/3094beforeafter, stablefraction1.0 withspacing1939.71ms, decoded8actualplainPE/native12, inspected4contactsheets/7fullPNGs. A1/A2/A3/A4/A6PASS; A5productdd1547+66PASS, nextPythoncommit/currentheadCI pending. Domain/UX/tester/architect/deliveryPASS forfunctionaloutcome. Adversarial/depth: earlysplash/cachedframesnotrootproof, transientcard00notsettled, negativeJSONnotsuccess, scope/contracts/edgecases/docs/unsupportedclaims checked. No findings. Manualchallenge actualcommit/reopenedblob and actualdrawnroot/card satisfied.

Full delivery stop NEEDS-FIX only for commit/currentheadCI of already tested Pythonbytes and finalaudit; no native recapture required for unchanged bytes. Root91.785–101.731s/card14.839s are diagnostics, no performanceacceptance/pairedclaim. Parent5/1/5/ARM64runtime/remoteTLS remainopen. Update currentheadCI before final deliveryPASS; parenttaskcompletion remainsseparate.

## Approval
Автоматически подтверждено15Oct после advisory review/fixes PASS по прямому поручению пользователя. EXEC разрешён, без повторного вопроса. Parenttask/PR остаются InProgress/draft.

## 20. Журнал
| Phase | Decision/evidence | Next | Human | Artifacts |
|---|---|---|---|---|
| SPEC15Oct | installed/ELF causeconfirmed; small preloading design proposed | independent review | Standingautoapproval/emulator | thisSPEC,DROOTnativegitproof |
| Review/autoapproval | advisoryPASS/nofindings; negativecontrol/isolation clarified | implement/bootstrap+harness | Standingautoapproval | thisSPEC |
| EXEC prebuild review | bootstrap/harness implemented, focused advisoryPASS after3MEDIUM fixes: bounded aapt/preflight, independent cleanup preservingrecorderuncertainty, checkedliveexit sentinel. Reviewer behaviorreadonly/writablesandbox. StaticAndroidcontracts/7protocolcontrols/diffcheckPASS; nativeunproved | package/provenance/nativeproof | Standingauthorization | MainActivity,scripts,DROOT |
| Packaging15Oct21:12 | dd1f61c1 pushed; staticcontracts/7protocolcontrols/SingleViewUI7 rawTRXPASS. SmokeAPK4A87CB13C5D2C73F9ACBE6F1A19D41793D226E271D368FA6B504AA9A95371ADB verified8decodedPE/12unchangednative/default2ABI/AOT/SDK401; Probe onlyAndroid, bootstrapbothABI/noobserver. Linked8snapshotsretained. Actual prebuild toolreceipt17:55:57Z precedespublish17:56:12Z; 4inputSHAs verified via supplementalproof created18:07Z, notbackdated. Plain1 intentionallystopped for incompleteexternalinputpin, failedstate/logsretained/noacceptance; newplain2 captures4inputs beforeclean/build. Helperreviewfixed boundedtools, completeinputpin, nolegacy10sfunctional-latencygate, recorderunknownexit/commonuncertainty. LocalPython-onlyfixreviewed/frozenSHA40C9C89A... awaitingcommit, APKCSharpunchanged. Focusedpre-nativeadvisoryPASS; native/ordinaryUI/currentCI stillpending. Previous53CIcancelledconcurrency; ddCI38781336726running | finalizeplain2, actualnative/ordinaryUI | Standingemulator/autoapproval | DROOTnative-git1/2 proofs, source-lock, collector-lock |
| Native/ordinary21:26 | Plain2APK64787D0E232167E5D899E7B59FC1F31E08B7B344BDC9C28B60188E5DFFBB7AD8 verified8finalPE/noProbe/noobserver/12nativeunchanged/SDK401AOT2ABI. OwnAVD20: PID4100/4486 each2Init/Stage/Commit/clean/close/reopenexactblob PASS, sameprocesshandlesstable; negative4807missingdir explicitbootstrap/NativeMethodsfailure/ops0 and finalemptylocalroot. FrozenactualcollectorSHA913300C4... includes observedCRCRLF exactidentitynormalization,8protocolcontrolsPASS. OrdinaryPID5352 loading21→root22 rawcurrentdrawbrackets recomputedPASS, actualcarda5ef...24children, before/after3094exact/noKnownGitLoader/fatal/ANR/surfaceerrors. All9×3native+23ordinary/3cardobservations inoriginalorder and full3nativeLast/loading/root/card/ordinaryLast inspected; videoPTS nothostclock. Diagnosticrootinterval[91784.66,101731.06]ms/firstcard14.839s: functionalonly, no comparativeperformanceacceptance; parent5/1/5unmet. OwnAVD20shutdownfirst10swaitexpired, laterCIMconfirmedQEMU/launcherexited; noforcedkill. ExactddCI38781336726 Main1547/1547+Headless66/66 zero fail/skips, rootTRX/stageexit/invocation/checkoutequalheadtree audited; AndroidPkg38781336738/CodeQL38781336768PASS. NewPython-onlycommit/currentCI/PostEXECreview stillpending | advisoryreview/commitcollectorfix/results, newheadCI; profileearlystartup | Standingauthorization | native-git1-acceptance-audit.json,mediareview,ci-dd1f61c1-root-audit.json |
