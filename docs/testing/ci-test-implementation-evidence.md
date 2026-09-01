# Проверки реализации CI/test рекомендаций

Состояние на момент локальной приёмки 31.08.2026: корректность подтверждена тремя полными запусками каждого проекта — **2832 Passed**. Targeted ускорение измерено; full performance — **INCONCLUSIVE**, последняя ограниченная серия v5 остановлена из-за посторонней нагрузки. Итог задачи — PARTIAL по полному performance, без заявления об ускорении всего CI. Предыдущие неуспешные серии сохранены ниже. На этом этапе GitHub запуск, commit, push и PR ещё не выполнялись. Последующим запросом «Оформи mr» пользователь разрешил публикацию рабочей ветки и Draft PR; этот отчёт остаётся свидетельством локальных проверок, а не live CI.

## Исходная версия и условия

Исходный commit — `f39b32458aba0f7fe403b3bea26c14f9215d0507`; это не поздняя ветка Daily Feed из исторического аудита. SDK 10.0.400, .NET runtime 10.0.11, TUnit 1.44.0, MTP 2.2.2, Windows, Debug.

Исходный uninstrumented warmup остановлен локальным лимитом 25 минут (завершение через 25:32): 872 Passed, 0 Failed, отчёт неполный. Полный discovery baseline — 904. Это **не** успешный baseline и не используется при вычислении ускорения. Для дальнейших полных локальных запусков установлен лимит 45 минут после проверки прогресса/CPU и фактических 872 завершённых тестов. GitHub timeout 30 минут сохранён.

Для парных замеров создан отдельный архив исходников. В него добавлены только те же diagnostic hooks/phases и measurement metadata, без оптимизаций, исправлений production или удаления дублей. Изменённые файлы и SHA256 перечислены в локальном `baseline-instrumentation.json`. У вариантов одинаковые SDK/configuration/args; сборка выполняется до замеров. `paired-targeted-v1/samples.jsonl` содержит точные команды, порядок, warmup, outcomes, TRX durations и process wall-clock.

## Корректность

Настоящий `Invoke-TestStage -Stage test -Project headless` завершился с exit 0: **38/38 Passed**, 5m07.855s (`native-stage-smoke`). Reporter прочитал 38 результатов без telemetry errors. HTML содержит 38 test-body и 38 test-case spans, максимум одновременных body/case — 1. Это интеграционная проверка CI invocation и один полный Headless run, не часть парной performance серии.

Ожидаемый Main discovery на этой базе: 904 → 906 = 904 − 9 снятых duplicate entry points + 11 новых случаев (controlled emoji 4, runner probe 3, independent-subcase failure contracts 3, trace sink 1). Headless остаётся 38. Уменьшение исторических 1247 до 904 связано с другим checkout, а не удалением сотен тестов.

Пройденная targeted серия `targeted-v2-*`:

- Controlled emoji: include/exclude (2), добавление/удаление групп и пустой источник (2), emoji BDD (1).
- Облегчённый breadcrumb renderer (1), полный breadcrumb/navigation BDD (1).
- Search BDD (1), сохранённый второй Roadmap interactions BDD (1), server live BDD (1).
- Fault matrices (3): Catalog 40/40, Activation 36/36, FirstMigration 37/37, все persisted write cuts исполнены и прошли.
- Task-card padding/theme (по 1), полный task-card BDD (1).
- Runner probes (3), negative independent-subcase contracts (3), failing trace sink (1), fixture lifecycle (4).

До последнего teardown workaround семь перенесённых UI/search bodies совпали с исходниками после нормализации whitespace; это дополнительно проверено advisory reviewer. Затем в NoMatches helper изменён только factory raw → Safe session, описанный ниже; остальные шесть bodies и все assertions сохранены. Старые entry points и проверки перечислены в coverage map. Два live helper по-прежнему создают настоящий host/database и проверяют реальное взаимодействие клиентов.

Reporter проверен synthetic TRX/HTML fixtures: duration/identity, collision, escaping, malformed XML/XXE, missing HTML/manifest, build failure, cancellation с частичными outputs, runner crash, согласованность counters, повторный разбор без подмены source/runtime, stable fingerprints в отдельных процессах, dedup artifact copies и сопоставление phases через lifecycle identity. Orchestration harness проверяет пять native failure paths и отказ повторного test invocation без изменения старых metadata. Series harness отвергает skipped/failed даже при exit 0.

Main/Headless builds в `final-targeted-v3/build-*.log` прошли. Первый real-report smoke в `final-targeted-v3/fault-matrices` был invocation failure: SDK 10.0.400 отклонил отдельный аргумент уже созданного results directory до старта test host. Это не test assertion failure и не green. Helper и measurement invocation переведены на `--results-directory=<path>`; fingerprint поддерживает обе формы. Reporter/orchestration harness после правки прошли (`*-attached.log`). В `final-targeted-v4` все 3 fault matrices прошли; real reporter прочитал 3 результата без telemetry errors. Source-boundaries реально запустились, но дали 1 Passed / 1 Failed — это отдельный test failure, а не ошибка invocation.

`final-targeted-v4/source-boundaries` зафиксировал `KeyNotFoundException` DynamicData при возврате задач в cache. Новый тест менял Title с активным autosave throttle и одновременно очищал/восстанавливал upstream cache. Такой сценарий не обеспечивает единственного управляемого источника событий: autosave/watcher могут заново добавлять VM. Это подтверждённый дефект изоляции теста, но не доказательство причины конкретного исключения. В двух новых controlled тестах до любых изменений Title теперь закрываются и дожидаются producers через существующий `SealPendingSaves`; реальные PropertyChanged, emoji grouping, UI collections и keyboard input сохранены. Обычные BDD helpers сохраняют нормальный persistence lifecycle. При успешном restore второй batch в finally исключён; при отказе сначала отключаются UI projections, ошибки сценария и cleanup агрегируются. Последующая сборка и 10 процессов `source-boundaries-v5` прошли; границы этого доказательства и повторный RED/GREEN описаны ниже.

## Emoji: что доказано

`emoji-reentrant-red` воспроизвёл `InvalidOperationException: Collection was modified` в переборе `EmojiExcludeFilters` при синхронном upstream изменении title на границе collection notification. Snapshot `.ToArray()` устраняет изменение перечисляемой коллекции во время обхода.

`emoji-selection-red` воспроизвёл потерю выделения «Все» в обоих вариантах. `ApplySearchFilter` очищал и создавал displayed list, теряя выбранный объект. Теперь selection сохраняется только если тот же объект остаётся в списке; для удалённого элемента выделение не выдумывается. После исправлений `targeted-v2-emoji-controlled` — 2/2 Passed, без принудительного повторного выбора перед вторым Space.

Трасса показала различие порядка уведомлений: удалённая группа может исчезнуть ещё до входа в All handler. Поэтому проверяется состояние всех **оставшихся** filters и отсутствие удалённой группы, а не флаг на уже снятом объекте. UI выполняется на dispatcher thread.

На этапе controlled tests исторический `ArgumentOutOfRangeException` в Avalonia container insertion из run #221 не воспроизвёлся. Позднее baseline warmup v4 дал тот же класс ошибки в том же исходном test method (подробности ниже). Детерминированный trigger и причинная связь с двумя fixes пока не доказаны. Их собственные RED/GREEN и успешные повторы не объявляются доказанным устранением исторической причины.

Visual fallback: реальный Headless UI-тест пытался сохранить before/after через `CaptureRenderedFrame`, но runtime вернул `null` (`visual: frame-unavailable` в trace). Видео и screenshots не получены. Next-best evidence — реальные input events, visual-tree/selection/popup assertions, TRX и упорядоченная collection trace; это не ручной desktop walkthrough.

После изоляции producers новая Main build прошла (23.68s, 54 warnings, 0 errors). `source-boundaries-v5` — **10/10 отдельных процессов, 20/20 Passed**; все 10 traces complete, незакрытых lifecycle/lease и overlaps нет. Конкретный KeyNotFound больше не наблюдался в этой серии; это подтверждает работоспособность изолированного теста, но не доказывает исправление произвольного cache reload с активными saves.

`sealed-emoji-comparison-v1` повторяет RED/GREEN уже с новым seam: одинаковый final test assembly и зависимости, в изолированном RED runtime заменены только `Unlimotion.dll` и `Unlimotion.ViewModel.dll` на prebuilt baseline. SHA256 — `sealed-red-runtime.json`; оба запуска напрямую через MTP, не performance evidence. Baseline **0/2**: include теряет selection, exclude бросает `InvalidOperationException: Collection was modified`; candidate **2/2**. Следовательно, seal не убрал контролируемое UI воспроизведение.

`emoji-100-v1` завершён 06:43 MSK: **100/100 отдельных процессов, 200/200 Passed**, native exit 0 во всех, без skipped/retry/count mismatch. Все 100 traces complete, 200 lifecycle и 200 test-body spans сопоставлены, resource overlaps отсутствуют; максимум simultaneous body внутри каждого process — 1. Test assembly SHA256 совпадает с RED/GREEN manifest; это сборка до последующей отдельной правки readiness CompletedTree. Краткая сводка — `emoji-100-v1-summary.json`. Это bounded controlled regression evidence, не статистическая гарантия отсутствия произвольного flaky behavior.

После 100 процессов: `affected-emoji-class-v1` **16/16 Passed**, `affected-emoji-bdd-v1` **1/1 Passed**; оба настоящих invocation разобраны reporter без telemetry errors. UI assertions включают узкий/широкий viewport, popup, typography, summary, включение/исключение, клавиатуру и controlled source changes.

## Зависание при полном прогреве и готовность CompletedTree

`paired-full-v1`: baseline Main **904/904**, Headless **38/38** прошли. Candidate warmup остановился после 603 завершённых lifecycle в `TreeCommandUi_NonAllTasksTabs_CurrentAndAllCommands_Work(5, CompletedTree, ...)`. Этот же baseline body занимал 3.632 с. После нескольких минут без прогресса и роста CPU сняты два managed stack и локальный triage dump; затем завершён только проверенный собственный test host. Native exit −1, TRX не создан: это **незавершённый failed validation run**, не Passed и не performance sample. Измеряемые full samples ещё не начинались.

`candidate-warmup-hang-clrstack.txt` показывает UI `SelectTab → CompletedMode → ActivateCompletedProjection` и другой поток `UnifiedTaskStorage.Update → TaskItemViewModel.Update → CompletedDateTime`, оба ожидают блокировки DynamicData. Подтверждены пересечение операций и lock waits; полный цикл взаимной блокировки и новый production defect отдельно не доказаны. Тело данного теста, его preparation и Completed projection до находки не менялись. Связь с production emoji diff не установлена.

Подготовка CompletedTree создавала дочернюю задачу, меняла Title/IsCompleted и ждала только `WaitThrottleTime`. После этой паузы теперь ожидается snapshot начатых saves через существующий `TestHelpers.WaitForPendingSavesAsync`, затем проверяется реальный файл: задача существует, Status=Completed, CompletedDateTime задано. Persistence, UI subscriptions и исходные assertions сохранены; seal, новый scheduler и увеличение sleep не применяются. Это готовность fixture перед tree commands, не исправление произвольной конкуренции приложения. Pending-saves helper не является barrier для всех будущих watcher callbacks.

Advisory reviewer подтвердил finding HIGH и допустимость узкой правки; его статус остаётся NEEDS-FIX до targeted/full evidence. Для новой полной performance серии создан отдельный revised baseline `unlimotion-perf-base-20260831-v2` с **точно той же** fixture preparation и проверками файла. Старый baseline и `paired-full-v1` не переименовываются; 12 DLL из обоих прежних вариантов сохранены и сверены с исходными SHA256 в `before-completed-fixture-fix/manifest.json`. Время стабилизации не будет приписано оптимизациям. Dump остаётся только локально и не входит в CI upload allowlist.

После правки candidate build прошла (21.42s, 0 errors). `completed-fixture-candidate-v1`: **10/10 процессов, 40/40 Passed**, все четыре варианта в каждом процессе; 10 complete traces, 40 сопоставленных body spans, без overlaps/open leases. На новой test assembly дополнительно `completed-fixture-emoji-class-v1` **16/16** и `completed-fixture-emoji-bdd-v1` **1/1**, native/report exits 0, telemetry errors 0. Static re-review preparation — PASS; runtime full gate ещё открыт. Reviewer отдельно подтвердил допустимость переноса старого 100-process emoji evidence по анализу влияния: обе production DLL прежние, emoji tests/hooks не менялись, preparation не вызывается emoji-фильтром. Это не утверждение о 100 повторах нового бинарника.

Реальный reporter для оборванного candidate run выдал `runner-failure`, `telemetryComplete=false`, `historyComparable=false`, observed=0; 603 завершённых lifecycle не подменены TRX Passed. Оставшаяся после принудительного завершения fixture идентифицирована по пути и времени создания, её 31 файл перенесён в локальный `paired-full-v1/hung-fixture` с проверкой всех SHA256. Это сохранённая диагностика failed run, не успешный автоматический teardown; в активном test output эта директория больше не находится.

Revised baseline Main/Headless builds прошли (26.32s / 7.05s, 0 errors). `completed-fixture-baseline-v1`: **10/10 процессов, 40/40 Passed**, 10 complete traces и 40 сопоставленных bodies, overlaps 0. `full-v2-preparation/prepared.json` подтверждает неизменность пяти candidate DLL, кроме ожидаемо пересобранной `Unlimotion.Test.dll`: новая SHA256 `8EEC264CE83B8262D9013F0C6E9A857BEE66AA6BFCF41DB7990B1245A7D4FA12`. `completed-fixture-readiness.json` содержит одинаковые хеши preparation method обоих вариантов. Полная серия `paired-full-v2` начата отдельно: 4 warmup и 12 измеряемых процессов (три пары каждого проекта); результаты ещё ожидаются.

В новой серии baseline Main warmup прошёл **904/904**, 1158.267 с. Все 904 lifecycle/body сопоставлены, trace complete, resource overlaps 0, fault sets 40/36/37 полные; CompletedTree body — 3.620 с. Reporter прочитал 904 результата без telemetry errors. Его отдельная диагностическая длительность сохранена в `paired-full-v2/warmup-reporter-duration.json`; postprocessing, restore/build и upload не входят в paired `dotnet test` wall-clock. Поэтому даже итоговый full-suite gain нельзя автоматически назвать ускорением всего GitHub job.

## Отклонённые гипотезы

Catalog immutable seed откатан после пяти парных измерений каждого варианта: median process wall-clock 29.187 → 32.720 с (+12.1%), mean 32.083 → 32.151 с. Значимого выигрыша нет; различие median/mean указывает на шум и не обосновывает сохранение усложнения. Вернулась исходная подготовка каждого case через `CreateRemovalBeforeState`; все 40 persisted write cuts и новая phase/coverage instrumentation сохранены. Сырые результаты отклонённого варианта остаются в `paired-targeted-v1`, финальные full runs должны использовать сборку после отката.

Task-card padding и dark-theme оставлены на полноценном MainControl. Однопроходная диагностика: setup около 5.5/5.4 с, body 12/98 мс, cleanup 170/192 мс; это не paired performance result. Проверяемые controls/styles объявлены внутри `MainControl.axaml`, включая внешний details frame и global create button. Отдельный синтетический card control копировал бы XAML и ослаблял integration contract; выделение production component выходит за согласованный test-only pilot. Добавлена только phase instrumentation, обе проверки и полный BDD прошли.

## Обновление CI/README контракта и полная серия v3

Candidate Main warmup `paired-full-v2` завершился **905/906 Passed**, native exit 2, 1102.943 с. Единственный failure — `CiReadmeMediaScenario_ExecutesFeatureSteps`: статическая проверка искала путь Headless project и CLI parallelism прямо в YAML, хотя новый workflow делегирует их `Invoke-TestStage.ps1`. Это stale test contract, не flaky UI failure. Все 906 lifecycle/body сопоставлены, resource overlaps 0, fault sets 40/36/37 полны; CompletedTree прошёл. Неуспешный прогрев исключён из performance evidence.

`CiReadmeMediaContract` теперь проверяет делегирование в YAML, а project path и неизменный parallelism — в вызываемом helper. Media/README assertions и реальный loading UI smoke сохранены. Build: 0 errors; целевой `ci-readme-contract-v1`: **1/1 Passed**, native/report exits 0, telemetry errors 0. Advisory static re-review — PASS по этой правке. После неё изменилась только Main test DLL: SHA256 `1227BFC1C3C02F8BE5DE714BBC7227E7C4C912B28FBA806A49F04FCD928D5264`; остальные пять candidate DLL совпадают с предыдущим manifest. Старые 12 DLL сохранены в `before-ci-contract-fix`.

`paired-full-v3` начат 08:36 MSK: два новых candidate warmup и все **12 измеряемых процессов** (три пары каждого проекта). Два успешных baseline warmup берутся по ссылке из `paired-full-v2` (Main904/904, Headless38/38); raw rows/logs остаются на прежнем месте. Guard проверил значения среды, совпадение строк-ссылок на instrumentation/preparation manifests, шесть baseline DLL относительно обоих manifests и диска, native results/counts, argv и отсутствие reboot. Содержимое referenced instrumentation/preparation manifests этот guard не проверяет; одинаковая preparation проверена отдельно в `completed-fixture-readiness.json`. Пять guard contracts прошли: valid и отказ при изменении binary, SDK, argv, исходном failed warmup. Failed candidate не переносится, warmup durations не входят в statistics. Это 14 новых процессов и 2 существующих baseline warmup, а не 16 новых запусков.

## Teardown Avalonia: воспроизведение и существующий workaround

`paired-full-v3` дал успешный candidate warmup Main **906/906**, Headless **38/38**, затем две полные успешные пары: baseline 1263.537 / 1268.262 с, candidate 1188.107 / 1187.898 с. Третий candidate Main — **905/906**, native exit 2; серия остановлена, Headless/третий baseline не запускались. Единственный failure: `MainControlFilterToolbar_NarrowWindow_KeepsEmojiSummariesInsideWindow`, `NullReferenceException` с первым frame `Avalonia.Headless.HeadlessUnitTestSession.DisposeAsync`. Это failed sample, его нельзя молча удалить или смешать с новым бинарником для заявления о трёх однородных парах. Все шесть имеющихся Main traces complete, overlaps 0; это само по себе не превращает teardown failure в Passed.

В [исходнике Avalonia 12.0.3](https://github.com/AvaloniaUI/Avalonia/blob/12.0.3/src/Headless/Avalonia.Headless/HeadlessUnitTestSession.cs) `StartNew` передаёт captured `task` в constructor из самого `Task.Run`; worker может прочитать поле до присваивания результата `Task.Run`. `DisposeAsync` отменяет token, завершает очередь, затем ожидает `_dispatchTask`. Декомпиляция установленной DLL подтвердила этот код (`avalonia-headless-session-decompiled.cs`).

Отдельный probe без Unlimotion и UI dispatch воспроизвёл механизм: из 2033 созданных sessions три имели `_dispatchTask == null` (read-only reflection, поле не изменялось), и каждая дала именно NRE в DisposeAsync. Probe повышает минимум ThreadPool до 32 для нагрузки на startup; отношение 3/2033 нельзя трактовать как частоту flaky behavior в CI. Артефакты: `prepare-headless-race-probe.ps1`, `headless-start-race-probe.log`; использована та же Avalonia.Headless 12.0.3. Это доказательство дефекта dependency, а не production emoji regression и не повтор #221.

Три оставшихся raw sessions в `MainControlFilterToolbarResponsiveUiTests` — NarrowWindow, DetailsPaneMediumNarrow и NoMatches helper — переведены на существующий `SafeHeadlessUnitTestSession`. Такое же изменение внесено в revised baseline v3. UI bodies, assertions, Close и awaited CleanTasksAsync сохранены; catch policy существовала в HEAD и не расширена. Это применение известного upstream workaround, **не исправление внутреннего lifecycle Avalonia и не утверждение об отсутствии подавленных teardown NRE**. При null task исходный Dispose не достигает ожидания worker и CTS.Dispose; закрытый trace lease не доказывает завершение worker.

12 прежних DLL сохранены с проверкой SHA256 в `before-headless-workaround`; подготовка новой пары описана в `headless-workaround-preparation.json`. Новая строгая full performance серия требует собственных warmups и трёх измеряемых пар; предыдущие две пары остаются отдельным evidence прежней сборки. Targeted выигрыши и 100-process controlled evidence сохраняют свои явно обозначенные исходные бинарники.

После workaround все четыре builds прошли. Candidate Main test SHA256 — `BA703A2C085DE1D8F6F28904EBD887F87D8167EBF885C26C1C4F4BD2FC8C8BFF`; остальные пять candidate DLL неизменны (`full-v4-preparation/candidate-binary-impact.json`). Layout: 10 процессов, **20/20**; canonical emoji BDD: 5 процессов, **5/5**; весь affected class **16/16**, Safe session class **2/2**. Все 17 traces complete, overlaps 0; два реальных reporter runs без telemetry errors. Linked-helper probe сохранил тот же объект body NRE и cleanup InvalidOperationException, выполнил finally и успешно создал следующую session; trace sink там заменён локальным счётчиком, worker drain после подавленного upstream NRE этим не проверялся.

Relevant advisory re-review — PASS: ровно три factory replacements, тот же diff в baseline, прежние catch/assertions/cleanup сохранены, 12 preserved hashes и текущие hashes сверены. HIGH исправлен в исходниках и targeted; полная приёмка ещё ожидается. `paired-full-v4` начат 10:47 MSK: отдельные четыре warmup и все 12 measured processes, без переноса samples v3. Reporter для failed third v3 честно выдал `test-failure`, telemetryComplete=true, 906 records, telemetryErrors=0.

## Исторический index failure повторился на baseline

`paired-full-v4` остановлен после единственного baseline Main warmup: **903/904 Passed**, native 2, 1167.356 с; измеряемых samples нет. Упал исходный standalone `Toolbar_EmojiFilters_AllItemTogglesEveryEmojiFilter`: `ArgumentOutOfRangeException`, `List.Insert → Avalonia.PanelContainerGenerator → DynamicData.SortedObservableCollectionAdaptor → EmojiFilter.ShowTasks → All handler → keyboard Space`. Это соответствует методу и классу ошибки исторического [run #221](https://github.com/Kibnet/Unlimotion/actions/runs/32729322636). Production baseline остаётся исходным; candidate ещё не запускался в v4. Это наблюдаемая нестабильность исходной версии, а не ожидаемый RED отдельного controlled теста и не Passed.

Трасса v4 complete, 904 body/lifecycle сопоставлены, resource overlaps 0; посторонняя build/test нагрузка не наблюдалась. Конкретный interleaving collection updates не локализован; один полный baseline run не доказывает, что snapshot/selection fixes устранили этот index failure. Сведения о прежней невоспроизводимости обновлены только в пределах нового факта.

По bounded advisory review заранее установлен **один последний performance cohort v5**: те же бинарники, среда и аргументы, новые четыре warmup и все три пары без переноса samples. Любой следующий failure, включая warmup, прекращает performance validation с результатом **INCONCLUSIVE**; v6/повторов до green не будет. Тогда три candidate full correctness runs выполняются отдельно и не подменяют paired evidence. Даже успешный v5 характеризует latency успешных full runs; failed baseline v4 остаётся в reliability evidence и финальном отчёте. Это ограничение предотвращает выбор только удобных повторов после обнаружения baseline flake.

## Итог performance validation и границы результата

`paired-full-v5` использует одинаковые бинарники с v4, без новой сборки или переноса warmup. Выполнены четыре warmup и семь measured processes. Все завершённые test processes имеют native exit 0 и ожидаемые Passed/counts. Последний sample, candidate Main iteration 2, прошёл **906/906**, но получил `environmentEligible=false`: 48 наблюдений монитора содержат восемь посторонних процессов, первое — 12:56:23 MSK. В raw evidence сохранены только безопасные PID/имена/время, без command lines. Чужие процессы не останавливались.

| Measured iteration | Main baseline, с | Headless baseline, с | Main candidate, с | Headless candidate, с |
| --- | ---: | ---: | ---: | ---: |
| 1 | 1153.854 | 115.946 | 1086.584 | 110.414 |
| 2 | 1173.938 | 120.977 | 1452.898, timing invalid | не запускался в paired series |
| 3 | не запускался | не запускался | не запускался в paired series | не запускался в paired series |

Driver записал результат и остановился; performance v6 и новые попытки до green не выполняются. **Трёх пригодных полных пар нет, full-suite median gain и regression guard не подтверждены.** Одна первая пара не заменяет требуемую выборку. Результат performance-части — PARTIAL: targeted gains ниже доказаны, общий эффект INCONCLUSIVE. Корректность кандидата завершается отдельно с сохранением ссылок на исходные samples; она не восполняет paired performance.

Read-only разбор HTML показал увеличение времени в разных участках: Catalog fault matrix 32.149 → 48.767 с; compact Roadmap toolbar 2.687 → 19.246 с; workspace command BDD 28.887 → 43.296 с; Activation fault matrix 31.322 → 43.406 с. Это диагностическое сравнение с непригодным timing sample, а не доказанная регрессия этих методов. Наблюдение чужой нагрузки достаточно для исключения замера, но не доказывает единственную причину всего замедления. `boundary-summary.json`, `environment-incident.json`, `comparison.json`, `phases.json`, `traces.json`, `bodies.json` сохраняют детали.

Все шесть Main traces v5 complete: по 904 baseline / 906 candidate lifecycle, без open leases/lifecycles и overlap одинакового ресурса. Во всех шести совпадают полные последовательности persisted write paths и executed/passed fault indices 40/36/37. Одиннадцать уникальных HTML содержат ожидаемое число body/case spans; максимальная независимая concurrency — 4, shared-resource concurrency — 1. Headless имеет 38 spans и concurrency 1; Main hooks в нём не компилируются, поэтому diagnostic joins для этого проекта не требуются.

Process wall-clock исключает restore/build, reporter и upload. Поэтому даже пригодная полная локальная серия не была бы измерением всей длительности GitHub job. CPU/allocations и неинструментированные lock waits — N/A. Доступность CI artifacts после retention 14 дней и воспроизводимость исторического failure не обещаются.

## Подтверждённые targeted performance results

Breadcrumb: после отдельного warmup пять парных измерений дали median 8.177 → 2.389 с (−70.8%, −5.788 с); mean 9.194 → 2.598 с, max 13.823 → 3.666 с. Все samples Passed. Облегчённый renderer проходит оба порога сохранения; это выигрыш одного пакета, а не доказанный процент ускорения всего suite.

Во время server-пакета обнаружены чужие Arm.Srv build/test/RavenDB процессы, стартовавшие около 05:31–05:33 MSK. Поэтому server/emoji/search в `paired-targeted-v1` не принимаются как performance evidence без конкурирующей нагрузки; Passed остаётся фактом correctness. `environment-incident.json` фиксирует PID/время/категорию без command-line с потенциальными секретами. Breadcrumb и Catalog закончились до этих процессов. Чужая задача не прерывалась; оставшиеся performance samples требуют отдельной чистой серии.

Чистая серия `paired-targeted-v2` завершена 07:06 MSK: **36/36 процессов**, отдельные warmups исключены, по пять измерений каждого варианта в трёх пакетах. Все native exits/counts/outcomes ожидаемые; все 36 traces complete, overlaps 0. Отдельный монитор подтвердил готовность до измерения и сохранял успешные опросы/итоговый snapshot; чужие build/test процессы не наблюдались. Опрос раз в 15 секунд не доказывает отсутствие любой кратковременной фоновой нагрузки.

| Пакет | Baseline mean / median / max, с | Candidate mean / median / max, с | Снижение median |
| --- | ---: | ---: | ---: |
| Server | 53.381 / 52.931 / 55.240 | 29.830 / 29.745 / 30.178 | 43.8% |
| Emoji | 35.325 / 35.281 / 35.606 | 22.024 / 22.012 / 22.285 | 37.6% |
| Search | 37.027 / 37.013 / 37.124 | 22.713 / 22.680 / 23.094 | 38.7% |

Все три пакета проходят порог median ≥15% и ≥0.5s. Это сравнение исполнения одинаковых сценариев до/после устранения дублирующих entry points, не процент ускорения всего CI. Raw samples, binaries, environment observations и phase statistics сохранены рядом с `comparison.json`. Измерения относятся к сохранённым сборкам до поздних test-only readiness/CI-contract/Safe-session правок; для финальной сборки успешная полная performance-приёмка не заявляется. Production emoji DLL после controlled RED/GREEN не менялись. Полная серия v1 не дала measured samples, итог последней v5 — INCONCLUSIVE, как описано выше.

Первый полный baseline Main warmup завершён: **904/904 Passed**, 1145.864 с, environment eligible. Он исключается из performance statistics. Реальный reporter прочитал 904 результата без telemetry errors (`warmup-report`). Трасса полная: 904 lifecycle, 904 сопоставленных HTML body/case spans, без незакрытых leases/lifecycles и resource overlaps. Полные наборы fault indices равны записанным диапазонам 40/36/37. Максимум concurrent independent bodies — 3, по каждому declared shared resource — 1; это подтверждает необходимость анализа ресурсов вместо предположения о глобальной последовательности.

## Изоляция

Probe зафиксировал реальное пересечение независимого ресурса и общего ресурса при `--maximum-parallel-tests 1`; два случая с одинаковым constraint не пересекались. Сравнение `ProbeSharedResource` lease дало максимум 1, все пары entered/left закрыты. Это опровергает предположение об общей последовательности по одному CLI-флагу, но не заменяет проверку трёх полных candidate runs. Полный список владельцев и границы evidence — в `ci-test-resources.md`.

## Финальная локальная приёмка

Три отдельных полных candidate executions каждого проекта на одной неизменной сборке завершились без native failure, Failed или Skipped: Main **3 × 906**, Headless **3 × 38**, итого **2832 Passed**. Первые Main1+2 и Headless1 учитываются по исходным v5 paths; поздние процессы имеют отдельный correctness-only manifest. Warmup не засчитывается как один из трёх запусков.

| Execution | Main Passed / process wall, с | Headless Passed / process wall, с | Источник |
| --- | --- | --- | --- |
| 1 | 906 / 1086.584 | 38 / 110.414 | v5 measured iteration1, raw без изменений |
| 2 | 906 / 1452.898 | 38 / 169.640 | Main v5 с invalid timing; Headless correctness-only |
| 3 | 906 / 1914.876 | 38 / 197.468 | correctness-only, без performance claims |

Последние три процесса — `candidate-full-correctness-v1`. Main3 завершился 13:57 MSK; диагностический снимок во время него показал 100% system CPU при наличии свободной памяти. Это не доказательство единственной причины замедления. Его 31m54s превышают неизменный GitHub job timeout 30m: локальный лимит 45m позволяет проверить assertions, но **не подтверждает, что весь CI job с build/reporter/upload уложится в 30m**. Это требует отдельного live GitHub validation, а не скрытого увеличения timeout.

Все три Main traces complete, по 906 lifecycle и HTML body/case spans, без пропущенных joins, open leases/lifecycles или overlap одинаковых ресурсов. Максимум независимых body — 4, shared-resource body — 1. В каждом совпадают recorded write sequences и полные executed/passed40/36/37. Три Headless HTML дают по38 body/case spans и максимум1; отсутствие Main hooks в этом проекте оговорено выше.

После последнего процесса в Main output directory нет `MainWindowViewModelFixture_*`. В двух именованных fixture roots после контрольного снимка нет новых оставшихся каталогов; AppAutomation root содержит40 прежних каталогов против41 во время активного Headless. Прежние каталоги не удалялись. Это ограниченная проверка конкретных roots, не глобальная гарантия отсутствия утечек. Собственные Main/Headless test processes завершились. Known Safe-session suppression по-прежнему не доказывает внутренний worker drain Avalonia.

Финальные exact counts, invocation IDs, native exits, report completeness, fault coverage, binary hashes и границы cleanup объединены в `candidate-full-correctness-v1/validation-summary.json`. Native reporter для двух Main/Headless1 — `paired-full-v5/candidate-reports`; для последних Headless — `candidate-full-correctness-v1/headless-reports`, для Main3 — `main-report`. Local dirty worktree корректно имеет `historyComparable=false`; эти executions не выдаются за сопоставимые GitHub runs.

Final advisory readback и отдельный author adversarial fallback завершены; post-EXEC review — PASS для локального результата с перечисленными ограничениями. Новых находок нет, runtime gate закрыт. Review не меняет INCONCLUSIVE full performance на успешный результат; подробные Scope/Contract/Adversarial/Role/Depth passes и AC-таблица сохранены в spec.

## Остаточные ограничения

- Full performance — INCONCLUSIVE; новых performance-попыток в этой задаче нет. Targeted timing и 100-process emoji series сохраняют указанные выше границы сборок.
- Исторический #221 наблюдён на baseline, но его причинное устранение не доказано. Доказаны отдельные controlled enumeration/selection defects и их RED/GREEN.
- Screenshot/video не получены: `CaptureRenderedFrame` возвращал null; fallback — реальные UI input/assertions, TRX и trace, без утверждения о ручном desktop walkthrough.
- Read-only действия advisory reviewer не равны технической изоляции: effective sandbox `danger-full-access`; отдельный author adversarial fallback обязателен и фиксируется в spec.
- Live GitHub execution/upload/30m budget этим локальным отчётом не подтверждаются. После отдельно разрешённого оформления PR актуальный статус доступен в его checks. Merge, deploy и release не входят в запрос на PR.
