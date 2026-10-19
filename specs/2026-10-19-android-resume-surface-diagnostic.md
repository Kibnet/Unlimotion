# Проверка чёрных кадров при возврате Android

## 0. Контекст и полномочия

Task1 e74c5d1a, та же ветка/worktree. Пользователь поручил последовательную работу, использовать эмулятор и автоматически подтверждать SPEC после review/исправлений. Эта диагностика не начинает Task2 и не изменяет приложение. Форма Expanded из-за native UI, нескольких clock domains и безопасного управления собственной AVD. Owners: central QUEST/review/performance/UI/tool-execution; локальное требование UI tests сохранено для будущих product changes. Поверхность Codex/Windows; назначенную модель/effort не меняем. Canonical template — центральный specs/_template.md; ниже заполнены применимые контракты. Original apps/tasks и релиз не затрагиваются.

## 1. Цель и ограниченный результат

Различить наблюдения physical-display PNG и виртуальной записи screenrecord при возврате с живым PID. Проверить, воспроизводятся ли чёрные кадры, и какие surface/system события совпадают с ними. Не объявлять причину, если evidence допускает несколько объяснений. Выход — фиксированная серия с полными артефактами либо честный incomplete/non-repro с условиями. Ни один исход этой диагностики не завершает родительские cold5s/resume-full-action1s/freshness5s.

## 2. AS-IS / evidence

Rollback2afd4ab2 product/tests exact43; локальные51+66 прошли, exact-head CI ещё выполняется. Source OnResume не вызывает Connect/Init; initialized repository уже сохраняется. Старые10returns PID4252 на ordinary EGL12.1.1 APK завершились с карточкой, но host ADB intervals не доказывают1s. Offline analysis2 сохранил280frames с чистым decode stderr и byte identity к первой extraction; inputSHA не менялись. В30s-run3 frames6/7 содержимое окна чёрное при сохранённой statusbar; frame8 снова список. До них уже виден список, поэтому earlyroot может быть cached/transition. Сообщения unknown buffer/freeAllBuffers и system Davey рядом — корреляция, не установленная причина.

Времена кадров доступны из двух routed mett tracks. API34 schema закреплена официальным AOSP android-14.0.0_r1, SHA288fe03f072e03a554d39609164069113633e874eb4061a9ac47d9da5ab64fb0. Serialized frame count uint32; sampled-at-finalization realtime offset не гарантирует отсутствие clock steps. Guest START исключает предшествующий transport; не заменять им прежний host full-flow gate.

## 3. Non-goals

Нет нового кеша, lifecycle/product/library/theme изменений, миграций, task writes, нового SDK/GPU/system image/network режима. Нет относительного candidate gain или абсолютной performance приёмки. Нет подтверждения physical Fold6, открытия карточки/полной интерактивности или state-preservation Task2. Не менять старые verdicts и не удалять неудачные попытки.

## 4. Решения и выбранный механизм

Использовать уже проверенный baseline profile APK43 (79DCFD97...), тот же package org.unlimotion.taskloadingbench и собственную копию3094taskfiles. Это instrumented диагностическая сборка, не ordinary performance acceptance. Профильные сессии должны нормально закрыться до HOME; сохраняем traces с init.end/loading.false/drop0/правильным PID и native rootPNG. В OnResume observer не добавлялся; отсутствие активной startup сессии проверяется по завершённым файлам, но нулевой observer overhead не заявляется.

Новая собственная AVD attempt30, API34/x64/3CPU/RAM2560/1080×2400/GPUhost/qt-hidden, тот же private engine37.2.12. Launcher/controller45min, C free≥2GiB, только exactPID/path/CreationDate/AVD/port ownership. До работы сверить фактическое hardware/runtime. После install/settings — прежняя60s qualification launcher. Git backup и update check выключены только в own settings; credentials отсутствуют.

Фиксированный within-QEMU порядок пяти пар условий: **V/N, N/V, V/N, N/V, V/N**. V — screenrecord плюс нативные PNG; N — только нативные PNG. Основное V/N сопоставление использует общий20PNG канал с фактическими capture intervals/cadence/gaps; MP4 — дополнительный V-канал. Video-black(V) и PNG-clear(N) не доказывают recorder cause: короткий эпизод мог попасть между N снимками. Это observability control, не A/B приложения. Перед каждым возвратом HOME из подтверждённого собственного окна и30s подтверждённого фона при том же PID. После каждого случая exact recorder остановлен/отсутствует, затем одинаковый5s quiet interval и повторный guard/rootPNG. N после V означает отсутствие активного recorder, не доказанное отсутствие carryover; порядок и это ограничение сохраняются. Нет повторов плохого результата; PID change/transport/ownership loss останавливают серию и сохраняются как incomplete.

## 5. Сбор одного случая

1. Сохранить own focus/PID/rootPNG; HOME, проверить foreign launcher/non-keyguard, выдержать30s. Никакого ввода в посторонний app, кроме намеренного перехода HOME из собственного окна.
2. В V запустить собственный screenrecord540×1200/1Mbps/time-limit30s, подтвердить единственный recorderPID/starttime и exact cmdline/remote path. До amstart, перед/после каждого PNG в guest loop и после окна проверять ту же identity; host recorder child также должен оставаться активным. Полные контейнер/sample/count checks и encoder exit обязательны после stop. Earlyexit, превышенный30s, неполная identity/coverage или truncated MP4 делают V incomplete, не N/control. При VFR gaps не заявлять непрерывное содержание, даже если recorder жив. Чужую запись не прерывать. N проверяет отсутствие recorder.
3. Один bounded guest shell: сохранить uptime/date bounds; запустить явный am start только собственной activity; выполнить ровно20 sequential screencap PNG с checked samePID перед каждым. Для каждого файла сохранить гостевые date/uptime before/after. Общий guest budget≤10s; при timeout/ошибке остановить case и не начинать следующий. Нет UI tap/edit, поиска или повторной Init.
4. Сохранить live window/PID после окна, metadata и собственный logcat. Остановить только exact own recorder через SIGINT и доказать отсутствие; pull bounded files только собственного namespace. В V проверить наличие MP4; native PNG geometry/signature/число и clocks — обязательные evidence, не автоприёмка скорости.

Весь driver hard budget1200s (20min) от первого preflight внутри driver: capture/preparation cutoff1000s, cleanup reserve200s;240s initial warmup и manual root admission≤180s входят в эти1000s. Старт только с≥25min до watchdog deadline. Перед следующим случаем нужно≥75s capture остатка для background/recording/window/pull/quiet; иначе остановить incomplete и перейти к cleanup, не расширять deadline. Все subprocess/CIM/ADB/ожидания capped остатком stage deadline. Cleanup после cutoff использует только остаток1200s, independent attempts; watchdog остаётся внешним exact-own fallback. Отсутствие normal init.end/loading.false/root делает warmup incomplete. Пока capture идёт, не запускать builds/tests/media обработки; remote CI изолирован. Повторно используемый immutable APK43 уже прошёл свой exact CI; native диагностика этого APK не зависит от remote CI2afd, но новый head не считается проверенным старым CI. Перед следующей фазой свежий guard. Все input hashes заморожены до первого app launch.

## 6. Контракт данных / cleanup

До первого запуска и после stop приложения — exact manifest3094taskSHA, без изменений task JSON. Настройки/path/APK SHA проверяются; исходный пакет пользователя не затрагивается. На app PID replacement не force-stop replacement без отдельного свежего доказательства ownership. Cleanup stages независимы: own recorder, app, log/artifact pull, fixture; ошибки записываются, ничего не объявлять остановленным по пустому unchecked pidof. Проверенный STOPPED sentinel обязателен. После серии exact own QEMU exit, затем offline decode.

Output namespace `resume-surface-diagnostic1`, fresh/no overwrite, per-case logs/PNGs/video/metadata SHA; фиксированные run IDs. Гостевые каталоги только под own package files с уникальным prefix. Не удалять task locks/journal или пользовательские файлы. Возможный shell timeout оставляет cleanupUncertain и включает exact own emulator controller fallback.

## 7. Clock и анализ

Host request сохраняется отдельно. Native date/uptime brackets и MP4 elapsed frame clocks не смешивать без доказанного bounded mapping. До/после окна сохранять несколько host↔guest handshakes; пересечение offset intervals/разрешение uptime/clock drift явно учитывать. Если общий clock не подтверждён, сравнивать только порядок/состояния и сохранять unavailable timing. No hard1s verdict даже при узком guest интервале: здесь нет обязательного card action.

MP4: unique video+legacy+v2 tracks, sample-table bounds, counts/order/relative PTS, исходные SHA; output-only setpts=N/TB для offline extraction, без изменения sourcePTS/rate. Decoder stderr проверяется, ambiguous/malformed — unavailable. PNG: expected1080×2400, не считать чёрную область по одному pixel threshold; просмотреть full frames и окна. В V сопоставить capture intervals с соседними videoframes; отсутствие точного overlap не доказывает различие дисплеев. Даже physicalPNG/video различие не локализует виновный компонент само по себе. В N не наблюдённый short black означает только отсутствие его в сохранённых20samples; фактические gaps и sampling uncertainty обязательны. Разная cadence V/N или carryover не скрываются за бинарным verdict.

## 8. Сценарии, матрица приёмки и objections

Storyboard/visual planning artifact: old30s-run3 fullframes5→6/7→8 (root→black→root), затем `ownroot→HOME30s→samePID return→nativePNG window`, V и N. Layout/design не меняется; существующие native кадры доступны reviewer.

| Сценарий/AC | Проверка | Результат при отсутствии |
|---|---|---|
| A1 исходники/данные/AVD однозначны | actualAPK/coretrace/PID/hardware/frozeninputs/3094SHA | no series admission |
| A2 все10случаев в фиксированном порядке | fresh paths/samePID/QEMU/30s/checkedshell/clocks/raw | incomplete, не замена |
| A3 black/nonblack имеет actual evidence | полный просмотр отмеченныхnativePNG/соседнихvideoframes/ownlogs | unavailable/non-repro по условиям |
| A4 clocks и encoding не создают ложный endpoint | exact counts/routing/PTS/stderr/offset bounds | no timing claim |
| A5 cleanup доказан | ownrecorder/app/QEMU exit + manifest after | cleanupUncertain/no acceptance |

Expected objections: «ранний список — snapshot», «чёрныйvideoframe — capture artifact», «marker позже hostrequest», «наблюдатель сам влияет на jank», «один успешныйrun заменил плохой». Все сохранены в протоколе/control/ограничениях; не обещаем заранее единственную cause. Decision ledger: agent выбирает smallest discriminating diagnostic вместо нового кеша без reload; user decision не требуется. Альтернатива: сразу менять Avalonia/theme/cache отвергнута без cause. Больше Android30/300s performance серий здесь не запускать.

## 9. Review / выполнение

Post-SPEC author Scope/Contract/Adversarial/Role passes: parent/source/analysis2/rawblack/log/API34 schema inspected; no product delta; alternative frame clocks и observer effects не скрыты. Domain — task данные/команды не меняются; UX — проверяется реальный return/root без readiness shortcut; Tester — fixedpairs/negative ownership/clock/container controls; Architect — no cache/API change; Operations — bounded own runtime/independent cleanup. No-findings justification pending actual helper review, не actual EXEC PASS. Planning advisory review/fixes pending; автоматически подтвердить после PASS. Перед native EXEC actual helpers/admission и guards требуют focused review. Post-EXEC — raw/clock/visual/cleanup и честный disposition, затем определить следующий cause-specific шаг. Отдельный writable advisory плюс author fallback; техническая изоляция не заявляется.

### Исправления planning review19Oct

Закрыты в плане4MEDIUM: общийPNGканал/cadence/carryover; exact recorder coverage с30s cap и invalidV disposition;1000+200=1200s budgets включаяwarmup/admission; Expanded quality record/commands. Это не actualhelper closure. Depth: scope/data/AC/scenarios/ledger/expectedobjections/unsupportedcausality/clock/renderinput/cleanup reviewed. Manual challenge:31ms episode междуPNG и release/reuse recorderPID; первый закрыт unavailable evidence, второй exactstarttime+checks. Нет user-owned выбора, humanquestion не нужен. Targeted advisory rereview pending, autoapproval пока не заявляется.

Author linter statuses:1PASS§1outcome;2PASS§2ASIS;3PASS§2causeunknown;4PASS§4commonchannel;5PASS§3boundaries;6PASS§0owners;7PASS§4/5integrations;8PASS§5recordercoverage;9PASS§5/6numericcleanup;10PASS§7noperformanceclaim;11PASS§6data;12PASS§3/4compatibility;13PASS§6teardown;14PASS§8AC;15PASS§7/commandsnegativecontrols;16PASSplannedcommandsbelow;17PASS§5admission/cutoffs;18PASS§8ledger;19PASS§0Expanded;20PASSthisqualityrecord. Author rubric goal/ASIS/design/safety/testability/autonomy:5/5/5/5/5/5=30/30 planning only; actualhelper/execution notpassed. Advisory re-review governs openfindings.

Planned commands, не execution evidence (paths relative to private DROOT; helpers ещё надо написать/проверить):

```powershell
pwsh -NoProfile -File ./launch-own-private-emulator-no-reports.ps1 -Attempt 30 -Gpu host -WindowMode qt-hidden
pwsh -NoProfile -File ./watch-own-emulator-space.ps1 -MaxMinutes 45
python ./resume-surface-ownavd30.py runtime
python ./test-resume-surface-guards1.py
python ./run-resume-surface-diagnostic1.py
python ./resume-surface-ownavd30.py exit
python ./audit-resume-surface-diagnostic1.py
```

Перед каждым запуском использовать закреплённые absolute pwsh7/Python314/ADB пути; actual launcher/watchdog parameters сверить с исходником. Planned guard controls: wrongPID/path/CreationDate/AVD/port, stalewatchdog/reserve, failedADB не STOPPED, deadline cutoff, mismatchedrecorderstarttime/earlyexit, Nwithrecorder, missing/invalidsession/rootadmission, malformed/ambiguousMP4 clocks. Controls synthetic/actualhelper-bound, без чужих native actions. Сообщить точное actual число случаев после исполнения, не прогнозировать PASS.

### Planning closure / implementation19Oct11:04

Targeted advisory re-review PASS: четыреMEDIUM закрыты,20/20linter иrubric30/30 planningonly, Scope/Contract/Adversarial/Role/depth без новыхfindings; sample uncertainty/carryover сохранены. Автоматически подтверждено по прямому поручению пользователя. Начаты private helper drafts, nativeEXEC не начат. Actual helper/admission review и controls обязательны.

Implementation clarification: collectionComplete означает только сбор/cleanup, validationComplete и conditionQualified остаются false до offline container/sample/decode/PNG review после ownQEMU exit. Known earlyexit/coverage loss немедленно прерывает driver. Latent truncated/invalid MP4, найденный offline, делает V и qualifiedseries incomplete, даже если последующие raw cases уже сохранены. Это не ослабляет ни condition qualification, ни parent gates. Driver сам вызывает exact-owned exit helper в cleanup budget; отдельная exit команда выше — fallback, если выход не доказан, а не повтор уже выполненного kill.

### Actual helper fixes / pre-native19Oct

Первое actual helper review NEEDS-FIX:3MEDIUM. Исправлены raw stdout/stderr retention до throw при guest timeout/nonzero и независимый bounded pull partial own namespace даже при appPID replacement; preflight taskkill и wait теперь независимы, ошибки закрытия потоков не пропускают QEMU exit/final metadata; preflight проверяет full CIM equality с pinned runtime30, exact AVD/port tokens и stderr через absolute PS7 до force-stop/install. Свежие76 actual-source synthetic controls PASS (`resume-surface-guard-controls2.json`, output `guard-controls3`); предыдущие62 и их source hashes сохранены как история. Контроли не запускают ADB/AVD. Author Scope/Contract/Adversarial/Role fallback PASS, отдельный advisory rereview ещё ожидается. NativeEXEC не начат.

CI update: exact2afd4ab2 Tests39217690308 Main1572/1572+Headless66/66 PASS без ошибок/пропусков, AndroidPkg39217690358/CodeQL39217690213 SUCCESS. Root raw TRX/hash/invocation/stage/tree audit завершён, separate advisory closure запрошен. PR325 body5 прочитан live: head2afd/draft/open, gates5/1/5 остаются открыты. Это не заменяет native диагностику.

### Admission closure / native start19Oct11:21

Отдельный advisory focused re-review PASS:3MEDIUM закрыты,76/76 и6sourceSHA совпали. Exact2afd CI closure также PASS: raw1572+66, stages/invocations/clean tree, fresh GitHub GET трёх workflows. Writable runtime/read-only conduct, техническая изоляция не заявляется. Launcher LOW fresh outputs исправлен до первого запуска. Actual source freeze и обе review receipts сохранены в private DROOT.

Запущена ownAVD30: exactQEMU174968, actualAPI34/x64/3CPU/2560MiB/1080×2400/hostGPU,45min watchdog. Runtime proof PASS, driver начал preflight; native outcome пока pending, ничего не принято. Отсутствие новых product изменений означает, что повторять CI2afd сейчас не требуется. Manual warmup root admission ещё не выполнен.

### EXEC disposition19Oct11:29 — INCOMPLETE / insufficient

Manual actual fullroot PNG admission выполнен, normal Core/VM/drop0 завершены, appPID4302 сохранён. Первый p1-v после HOME30s выполнил20native PNG; driver остановился до parse/следующего случая на штатном stderr Android `Warning: Activity not started, its current task has been brought to the front`. Это ошибка классификации диагностического сценария, не доказанная app regression. Остальные9случаев/N не запускались, повторов нет; исходные metadata/логи/видео сохранены.

Независимый retention сохранил20native PNG и MP4/26decoded frames в `resume-surface-diagnostic1/p1-v/recovered-native`; app/QEMU exit доказаны, все3094 task SHA до/после совпали, cleanupErrors пуст. Вне task manifest обычный startup изменил `migration.report`; неизменность всей директории не заявляется. Весь driver234.379s. Offline source/container/legacy-v2/count/relativePTS/decode проверены, stderr пуст,11frozen+49offline inputs SHA совпадают; отдельные4coverage controls PASS. Root и advisory просмотрели contacts и fullnative0/video12: полного black app content в сохранённых samples не видно. Это не непрерывное наблюдение и не причина отсутствия прежнего эпизода. N отсутствует, coverage unavailable, rawCollectionComplete/qualifiedSeriesComplete/parentAcceptance false.

LOW audit erratum: partial branch поставил `clockOrderingValid=False` как placeholder без вычисления. Это означает not-evaluated, не доказанный clock failure; исходный audit воспроизводим и не переписывается. В итоговом disposition clocks/coverage unavailable; любой дальнейший расчёт оформляется отдельным receipt.

PostEXEC separate advisory+author adversarial fallback: PASS только для честного INCOMPLETE disposition/retention/cleanup, не functional/performance PASS. Runtime writable, техническая изоляция не заявляется. Surface diagnostic закрыт как insufficient; новую серию ради одного warning не запускать, вернуться к primary cold-loading bottleneck. Black-surface cause остаётся открытой; повторная диагностика оправдана при воспроизведении в основном сценарии. Родительская Task1 остаётся InProgress/Active, цели5/1/5 открыты, merge/Task2/Task3/release не выполняются.
