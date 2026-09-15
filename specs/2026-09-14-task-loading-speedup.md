# Ускорение загрузки задач Unlimotion: исследование и проект решения

## 0. Метаданные
- Фаза EXEC: пользователь подтвердил SPEC фразой «Спеку подтверждаю» 2026-09-14. Предыдущие разделы измерений описывают исследование baseline; результаты интеграции добавляются в журнал.
- Профили: performance-optimization, dotnet-desktop-client, product-system-design. Expanded, large: storage/migrations/lifecycle/UI.
- Stack: central routing-matrix, creator-vibe-lens (полный creative skill не нужен: критерии инженерные), model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance/mode, spec-linter/rubric, review-loops; локальный AGENTS.override.md. Для existing tests применён run-tunit-tests.
- Worktree: C:\Users\Kibnet\.codex\worktrees\task-loading-speedup\Unlimotion
- Исследовательская ветка research/task-loading-speedup; при подготовке PR переименована в perf/task-loading-speedup. Baseline измерений 23428aa4c358227c8730ca9028681056306ebe00. При delivery выполнен fetch: origin/main на 25 коммитов впереди baseline; результаты тестов и замеров относятся к проверенному source до интеграции этих коммитов.
- Основной checkout: три чужие untracked SPEC не затронуты. Commit/push отсутствуют.
- Codex desktop / PowerShell / SDK 10.0.400 / .NET 10.0.12 / Windows 10.0.22631 x64. Model-specific eval не применим.
- Замеры 2026-09-14: Ryzen 5 3500X, 6 cores/6 logical, RAM 63,9 GiB.
- Исходный snapshot SPEC определён из desktop Settings.json: локальное пространство, 2875 непустых task-файлов, 3,22 MiB, max 21,51 KiB. EXEC использует более позднюю копию 2879 задач, описанную в §20–21. Личные тексты/названия/содержимое настроек в отчёт не включены.

## 1. Overview / цель
Сократить время до актуальных задач, с которыми можно работать, при запуске/переключении минимум в 10 раз на одинаковом устройстве и snapshot. Android: различать обычный resume, пересоздание Activity и перезапуск процесса.

Результат исследования: обнаружена крупная лишняя работа при десериализации. Исходный план A: shared metadata resolver; затем по end-to-end замерам B: единый post-migration snapshot и C: bounded parallel reader. Итог интеграционных экспериментов EXEC: выбраны A + E2 (ленивые команды карточки); C/F/G не дали убедительного дополнительного выигрыша. B/D не включены. Подробности и границы доказательства — §21.

**Исследование: ×86,8 на чтении/десериализации. EXEC: полный Startup Ready ×7,20, переключения Ready ×12,52 / ×10,24.** Цель ×10 полного запуска пока не достигнута; Android не подтверждён. Коэффициенты микробенчмарков нельзя перемножать для заявления о скорости всей программы. Полная таблица пяти парных прогонов и отдельная метрика успешного действия — §21.5.

Outcome contract:
- Исследование: проверенный AS-IS, эксперименты четырёх подходов, выбор и критерии будущей реализации.
- Реализация: AC1–AC7 ниже, включая end-to-end ×10. Если не достигнуто, профилировать оставшийся bottleneck, не объявлять завершение по parser benchmark.
- Output SPEC-фазы: эта SPEC и сырые измерения; код тогда не менялся. После явного подтверждения пользователя EXEC выполняется в отдельном worktree.
- Stop: commit/push, установка в рабочую программу и публикация требуют отдельного поручения. Локальная реализация и проверки разрешены подтверждением SPEC.

## 2. AS-IS
Проверено в исходниках baseline:
1. UnifiedTaskStorage.Init очищает cache. BuildInitialTaskViewsAsync выполняет миграции, создаёт live graph, все TaskItemViewModel и индекс связей; cache публикуется пачками 64. Затем reconcile вновь вызывает HydrateCache для всего графа.
2. FileTaskStorage.DeserializeTask (423) вызывает CreateSerializerSettings (464) на каждый файл; каждый раз новый DefaultContractResolver либо IgnoreExtensionDataContractResolver. Кэш reflection-контрактов не переиспользуется. Это подтверждённый hotspot.
3. GetAll до live graph и ReadDirectoryAsync делают последовательный await Task.Run на каждый DeserializeTask. Это не параллельная загрузка файлов.
4. MigrateTaskStatusModel каждый раз читает/разбирает все JSON; status-model.migration.report не является fast-path.
5. ShouldForceReverseLinkRecheck возвращает true при историческом ForceRecheck=true. В актуальном migration.report: Version=1, ForceRecheck=true, Issues=0. Успешная принудительная проверка поддерживает повторный полный обход.
6. availability.migration.report: Version=2, TasksProcessed=2866, актуально 2875. Fast-path проверки количества на таком наборе не сработает.
7. EnableLiveGraphAsync снова читает каталог. Initial path содержит несколько full reads; точное число/время в пользовательском запуске ещё не измерены.
8. TaskRelationsIndex.Rebuild обновляет связи всех VM и computed fields. TaskItemViewModel.GetAllParents рекурсивно обходит/сортирует предков; VM создают много Rx-подписок/команд. Доля этой работы не измерена.
9. Тяжёлые проекции уже активируются лениво. StartupProjectionAndRelationsTests это подтверждает; общее «добавить ленивые вкладки» не новое решение.
10. TaskSourceManager.PrepareActivationCoreAsync создаёт runtime и вызывает Storage.Init; PublishActivationCoreAsync disconnect/dispose предыдущий. MainWindowViewModel имеет storageAlreadyInitialized guard — нет безусловного второго Init после bind.
11. MainActivity.OnResume вызывает base и завершает pending permission request; явного Init там нет. Причина Android-задержки не установлена: process death/recreation, sync burst или иной lifecycle path.
12. FileDbWatcher.SetEnable(false) не отключает регистрацию raw events. Это нужно для snapshot + replay.

Официальное объяснение механизма: [Json.NET Performance Tips](https://www.newtonsoft.com/json/help/html/Performance.htm), [ContractResolver](https://www.newtonsoft.com/json/help/html/ContractResolver.htm). Документация подтверждает механизм; числа получены локально.

## 3. Проблема
Дорогая работа повторяется на каждом файле и между стадиями загрузки: metadata типов, read/parse, hydration. Это подтверждённые механизмы лишней работы, но не доказанная единственная причина всей пользовательской задержки.

## 4. Цели дизайна
- Удалить повторную работу, сохранить JSON/Git и доменные правила.
- Один authoritative owner графа, snapshot isolation, диагностика ошибок.
- Сначала минимальный доказанный выигрыш, затем усложнение по измерениям.
- Отдельно измерять first paint, Ready, успешное действие и фоновые sync.
- Не блокировать UI, не выдавать устаревшее состояние за актуальное.

## 5. Non-Goals
- В исследовании не меняются source программы/тестов, настройки и рабочие task-файлы. Диагностический C# компилируется в память из SPEC и не подключается к продукту.
- Нет перехода на SQLite/бинарный формат/новое облако: данные не оправдывают миграцию.
- Не отключать validation, migration, history, repair, watcher, locks ради скорости.
- Не вводить zero-check cache и LRU множества runtime/VM.
- Layout, automation selectors, публичный ITaskStorage/command API, формат сохранения неизменны.

## 6. TO-BE
### 6.1 Ответственности
| Компонент | Ответственность |
|---|---|
| FileTaskStorage | Immutable resolver по policy, serializer/reader на операцию, inventory/parse/diagnostics/authoritative graph |
| UnifiedTaskStorage | Миграции прежним безопасным путём; post-migration snapshot consumers, VM publication и replay |
| FileTaskMigrator | Версионированная миграция/проверка; historical ForceRecheck не вечный demand |
| TaskSourceManager/Coordinator | Прежняя транзакционная смена пространства, pending saves, rollback |
| VM/RelationsIndex | Сохранять поведение; оптимизировать только после phase profiling |

### 6.2 Детальный дизайн
**A — первое обязательное изменение.** Reuse DefaultContractResolver и IgnoreExtensionDataContractResolver раздельно; immutable после конфигурации. Не делить mutable JsonSerializer между потоками. Сохранить converters, DateTimeOffset, enums, unknown fields и repair. Сначала замерить весь startup после A: возможно, A уже достаточно для ×10. Любое сопутствующее изменение Save требует отдельных roundtrip/no-data-loss проверок; не расширять read fix автоматически.

**B — согласованный snapshot ПОСЛЕ миграций.** Существующие миграции завершаются до acquisition общего snapshot. Не передавать retained snapshot в migration writes и не убирать их существующие повторные чтения/проверки: иначе external edit/delete между snapshot и Save может быть перезаписан, и последующий watcher replay этого не исправит. Оптимизация миграционных записей через stale snapshot исключена из этой SPEC; для неё нужен отдельный transactional/freshness/conflict дизайн с тестами competing external writers.

После миграций FileTaskStorage остаётся единым owner: LoadedTaskSnapshot содержит source identity, поколение, models, mapping task↔file, duplicate IDs, load errors. Не читать повторно неизменённые данные только для graph/VM/projection/validation consumers. Сценарий «1 read → 3 consumers» в микробенчмарке — модельная оценка потенциала, не доказательство трёх устранимых реальных migration reads. B выполнять только если phase profiling найдёт реальные повторные reads после миграций. Текущие GetAll/ReadGraph в live-state уже используют память; исходники сами по себе не доказывают наличие трёх повторных disk reads после миграций. Если счётчики показывают ноль, B не реализовывать.

Raw watcher открыт до initial enumeration; buffer/revisions действуют через load/publication. Initial VM применяются один раз; final replay содержит только изменённые модели/удаления с revision/tombstone guards. При overflow/неполной истории conservative full reload. Просто удалить reconcile нельзя. Защита VM от stale revision не является защитой migration writes на диске.

Исторический ForceRecheck рассмотреть отдельным локальным regression fix в прежнем migration flow: успешное завершение проверки не должно само создавать вечный demand. Версия/диагностика/изменившееся содержимое по-прежнему требуют re-evaluation; новый legacy файл из Git должен обнаруживаться. Не добавлять fast-path «report существует» или «size/mtime те же». Простая замена ForceRecheck=true на false недостаточна: IsCurrentReport может пропустить новые/изменённые legacy/links; добавить content-aware валидацию изменённых входов либо оставить проверку всех моделей быстрым reader-ом. Inventory/content/relation validation должна происходить ДО существующего IsCurrentReport gate, который сейчас проверяет лишь Version. Пока это не реализовано/не проверено, не очищать ForceRecheck ради пропуска. Regression: успешный forced report → внешняя правка links/import legacy → следующий Init обязан обнаружить изменение. Сохранять backup/errors/rollback и исходную JSON-информацию legacy (IsCompleted и unknown поля нельзя потерять ранним переводом в TaskItem).
**C — дополнительный bounded parallel reader.** На desktop кандидат degree=4; degree=8 в итоговом прогоне не дал устойчивого выигрыша над degree=4. Выбор после end-to-end latency, UI heartbeat, CPU/memory; Android сначала degree=1, сравнить 1/2/4 на устройстве. Отдельные serializer/converter instances для каждого worker/операции, shared только immutable resolver. Не распараллеливать migrations/Save/VM mutation. Результаты по исходному ordinal index, не completion order; deterministic diagnostics/duplicates.

**D — не выбран.** Resident graph cache с полной SHA-проверкой не быстрее дешёвого parse. Zero-check reuse не удовлетворяет external sync. Cache VM мог бы дать другой эффект, но он не измерен, lifecycle/memory pressure сложнее.

Внутренние API могут добавиться (ReadPostMigrationSnapshotAsync/graph-VM consumers), публичный Init/command contracts сохраняются. Cancellation/source generation проверяются перед публикацией; cancelled candidate освобождается; предыдущая сессия восстанавливается штатно.

Visual planning artifact: layout не меняется, storyboard состояний:
~~~text
Старт / смена -> существующее Loading
 -> прежние migrations (фон), затем post-migration snapshot
 -> VM + активная проекция + replay
 -> Ready: актуальные данные и успешные действия
Ошибка candidate -> прежнее пространство + понятная ошибка
Android живой процесс -> установленный lifecycle; сначала trace
Android новый процесс -> оптимизированный startup
~~~
Видео/скриншоты до/после не применимы к текущему исследованию без UI-изменений. При интеграции — UI scenario capture; недоступность native capture документировать с причиной и headless/log fallback.

### 6.3 User-Observable Scenarios
| ID | Триггер | Ожидание | Evidence |
|---|---|---|---|
| S1 | Launch большого пространства | Ready и действие минимум ×10 быстрее | AC1 |
| S2 | A→B→A | Нет чужих задач/событий/потери правок, быстрая загрузка | AC1/3 |
| S3 | Android foreground/recreation/restart | Нет лишнего load; быстрый реально необходимый load | AC1/5 |
| S4 | External edit/delete во время load | Новая модель не перезаписана, удалённая задача не воскресает | AC3 |
| S5 | Legacy/corrupt/duplicate | Прежняя миграция/диагностика, unsafe writes blocked | AC2/4 |

### 6.4 State / Interaction Matrix
| State | Trigger | Result | Failure/concurrency |
|---|---|---|---|
| Inactive | Activate | Loading candidate | Прежний runtime до штатного commit |
| Loading | Newer event | Buffer/replay | Revision/tombstone guard |
| Loading | Cancel/ещё switch | Discard candidate | No late publish, dispose |
| Loading | Invalid source | Diagnostics/recovery | Unsafe commands не разрешены |
| Ready | External edit | Delta | Full reload при invalidation |
| Background | Resume | Живой или новый process | Не предполагать OnResume→Init |

### 6.5 Decision Ledger
| Решение | Owner | Выбор | Confidence | Риск | Нужно решение до EXEC |
|---|---|---|---:|---|---|
| A | agent | Shared resolver first | 0.99 | Compatibility | Нет |
| B | agent | Следующий шаг по phase measurements | 0.9 | Legacy/replay atomicity | Нет |
| C | agent | Platform-specific по измерению | 0.85 | IO variance | Нет |
| D | agent | Не включать сейчас | 0.9 | Неизмеренный VM выигрыш | Нет |
| Android | evidence | Не менять lifecycle без trace | 1.0 | Нет устройства/log | Не блокирует A |
| Интеграция | user | Отдельный обычный SPEC approval | 1.0 | Сейчас дизайн | Approval |

### 6.6 Runtime / Config / Data Matrix
| Область | Source of truth | Изменение | Совместимость / проверка |
|---|---|---|---|
| Task data | JSON/Git | Формат прежний | Все поля, unknown, dates/status/repair, roundtrip |
| Graph | FileTaskStorage | Post-migration acquisition + delta | Clones/diagnostics/locks/race tests |
| Space | Descriptor/coordinator | Контракт прежний | A→B→A/cancel/failure |
| Resolver | Immutable metadata policy | Lifetime увеличен | true/false policy без bleed |

## 7. Инварианты
Все IDs, поля, история, criteria, repeater, ExtensionData, связи и значимый порядок эквивалентны. Snapshot сохраняет duplicate/errors/filenames, не сворачивает дубликаты простым ToDictionary. Mutable модели не разделяются между consumers без copy/isolation. Hydration не вызывает autosave. No-change startup не пишет tasks; legacy migration делает только нужные изменения с backup. Параллельный completion order не меняет результат.

## 8. Точки интеграции
FileTaskStorage.DeserializeTask/CreateSerializerSettings/GetAll/ReadDirectoryAsync/EnableLiveGraphAsync; UnifiedTaskStorage.BuildInitialTaskViewsAsync/Init/Reconcile/Migrate*; FileTaskMigrator; source activation. Android сначала instrumentation, functional patch только после воспроизведения.

## 9. Модель данных/состояния
Persisted tasks без изменений. Внутренний post-migration generation snapshot; legacy migration information остаётся в прежнем migration flow. Старые reports безопасно re-evaluate. Не сохранять реальные task snapshots в Git/research artifacts.

## 10. Rollout / rollback
A отдельным локальным изменением + fidelity/E2E tests. B/C отдельно при доказанной пользе. Не перемножать коэффициенты: измерять combined. A/C rollback не мигрирует данные; B совместим со старыми reports. Не откатывать новые пользовательские задачи. Integrated startup испытывать только на независимых копиях одинакового snapshot с migration markers: Init способен писать. Исходный набор в Init не передавать.

## 11. Тестирование / критерии приёмки
| AC | Критерий реализации | Test/evidence | На этапе SPEC; итог EXEC — §21 |
|---|---|---|---|
| AC1 | Median baseline/candidate Ready latency >=10 для launch и A→B→A, одинаковое устройство/snapshot | Warmup + >=5 paired runs; first paint, Ready+успешное действие, phase spans; warm/cold OS cache отдельно | Только component timings |
| AC2 | 0 отличий данных/связей/диагностики, snapshot isolation | FileTaskStorageTests, new resolver fidelity both policies, unknown/date/enum/repair/corrupt cases | 2875 real models равны; негативные cases будущие |
| AC3 | Switch/cancel/replay/pending edit без потерь | Unified race tests, TaskSpaceTransactionTests, Headless/FlaUI A→B→A | План |
| AC4 | Forced recheck не вечный; imported legacy обнаруживается; ошибка migration безопасна | Second init report=true, clean report → import Version=1 JSON с непарной reverse link, same-count/same-size/mtime edits, rollback | Причина по source/report установлена |
| AC5 | Android resume/activity recreation/process restart различены; каждый воспроизведённый медленный load минимум ×10 быстрее. Живой быстрый resume: нет дополнительного Init, median не хуже baseline более чем на 10%, UI usable ≤1s | PID/activity/source generation, >=5 matched device runs | adb в PATH не найден, устройства/trace нет |
| AC6 | UI не блокируется, память не регрессирует | Dispatcher heartbeat p95/max, peak RSS, allocations, GC; не добавлять паузы >200ms reader-ом | Только process allocations |
| AC7 | Relevant UI coverage и required suites пройдены, нет unrelated source diff | Targeted→required full Main/Headless, Desktop build, Android по применимости | Baseline 7/7 targeted; Release build passed with existing warnings |

Уже выполнено:
~~~powershell
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -- --treenode-filter '/*/*/StartupProjectionAndRelationsTests/*' --maximum-parallel-tests 1 --output Normal
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter '/*/*/SingleViewStartupUiTests/*' --maximum-parallel-tests 1 --output Normal --results-directory 'TestResults\loading-research-single-view'
~~~
2/2 (9,067s), 5/5 (4,734s). Это existing baseline tests, не tests интегрированного candidate. Отчёты: src/Unlimotion.Test/bin/Release/net10.0/TestResults и TestResults/loading-research-single-view. Полный suite/mobile build не запускался: продукт не менялся. При реализации добавить relevant UI coverage и выполнить required full-run по testing-baseline; tests с общим UI state/bin запускать последовательно.

### Методика итогового контролируемого сравнения
- Actual ReadDirectoryAsync отдельный sanity: 19335,04ms, 2875 tasks, 0 errors/duplicates; один run, не используется вместо медианы.
- Итоговое приложение A: штатный private DeserializeTask (fresh resolver на файл) против того же JsonRepairingReader с отдельными settings/serializer/converters для каждой операции. Переиспользуется только immutable resolver. Во всех read-вариантах одинаковые две TaskItemSnapshot.Clone; baseline Invoke имеет небольшой reflection-call overhead, это учитывается как ограничение стенда.
- 8 режимов × (1 warmup + 5 measured) = 48 runs. В нечётных итерациях порядок обратный; GC перед timer; canonical serialization всех 2875 моделей после timer. Все 48 runs: mismatch=0.
- SHA-256 и inventory до/после каждого run: source unchanged. Resident включает полную SHA-проверку в timed region; остальные проверки источника вне timer.
- Один процесс/одна Release assembly baseline, одинаковые данные, Windows/.NET. OS cache прогрет; не cold-disk/process-launch benchmark.
- Allocated = GC.GetTotalAllocatedBytes process-wide, не RSS; включает фоновые расходы PowerShell/runtime. Нет заявления о statistical p95 по пяти измерениям. VM/UI/реальные миграторы/фоновая синхронизация в эти timings не входят.
- PreserveUnknownJson=true на актуальных данных. false policy, invalid/legacy/parallel writes проверяются интеграционными tests при EXEC; здесь не объявлены пройденными.
- Первые PS и C# exploratory замеры помогли найти hotspot; итоговые claims и таблица ниже относятся только к исправленному, полностью воспроизводимому приложению A. Его serializers и converters не разделяются между workers.

### Итоговые результаты, 5 measured runs на режим
| Режим | Mean ms | Median ms | Min–max ms | Allocated MiB |
|---|---:|---:|---|---:|
| Текущий fresh resolver | 20378.62 | 20190.82 | 18813.53–22826.81 | 756.65 |
| A: общий resolver, отдельные settings/serializer/converters | 235.33 | 232.69 | 230.67–248.17 | 42.60 |
| A+C: degree 2 | 144.57 | 141.78 | 136.36–161.92 | 42.61 |
| A+C: degree 4 | 89.80 | 88.20 | 84.71–96.46 | 42.61 |
| A+C: degree 8 | 92.06 | 88.30 | 76.95–121.59 | 42.61 |
| D: resident + полная SHA-проверка | 265.95 | 265.80 | 259.65–270.69 | 9.94 |
| Контроль: 3 reads / 3 consumers | 741.95 | 734.48 | 700.30–801.46 | 127.80 |
| B модель: 1 read / 3 consumers | 253.03 | 253.83 | 237.72–265.73 | 51.96 |

Относительно собственного baseline в одном стенде:
- A: 20190,82 → 232,69ms, **×86,8**; allocations 756,65 → 42,60MiB, **×17,8** меньше.
- A+C degree4: 20190,82 → 88,20ms, **×228,9 этап чтения**, ещё ×2,64 относительно A. Это прямое измерение комбинации, а не перемножение несопоставимых тестов.
- Degree8: median 88,30ms и более широкий диапазон; устойчивого выигрыша над degree4 нет. Degree4 предпочтительнее для следующего desktop integrated test; Android не проверен.
- D: 265,80ms против A 232,69ms. Уменьшает transient allocations, но не latency; кэш VM не проверялся.
- B модель: 734,48 → 253,83ms, ×2,89. Consumers эмулируются deep clone; не реальные миграторы. Post-migration graph уже кешируется, поэтому B не реализуется без доказательства реально лишних операций по phase profiling.
- Полный запуск/переключение/Android ×10 остаётся целью AC1/5, не установленным результатом.

## 12. Риски / Expected User Review Objections
| Возражение/риск | Mitigation | Status |
|---|---|---|
| Parser быстрее, приложение всё ещё висит | AC1: E2E обязательный; phase profiling VM/Rx/render | Evidence gap до интеграции |
| Android всё ещё грузится после фона | PID/activity lifecycle trace, без guessed OnResume patch | Evidence gap |
| Cache теряет внешние edits | D не выбран, retain raw replay/revisions/diagnostics | Mitigated in design |
| Результаты на игрушечных данных | 2875 real files, equality + SHA; consumer модель явно обозначена | Mitigated |
| Исследование изменило live data | Только read APIs, sidecar=false, SHA unchanged | Checked |
| Миграции пропустят legacy после Git | Не доверять только report/mtime/size | Negative tests required |
| Shared serializer/unknown field bleed | Immutable distinct resolver policies, mutable serializer per operation | Tests required |

Rework checklist: scenarios/decisions/AC заполнены, доказательства ограничены фактическим scope, E2E gaps не выданы за completion, API/layout contract сохранён.

## 13. План
A: минимальный resolver change + fidelity tests + phase spans + full startup paired on copies.
Если A не закрывает цель, B: post-migration graph/VM/replay с race coverage; report ForceRecheck — отдельно с legacy regression coverage. Если B приносит объективную самостоятельную пользу, оценить после измерения.
C: compare 1/2/4 на target, оставить только meaningful E2E gain без UI/memory regression.
Combined E2E, required targeted/full/UI suites, device Android, post-EXEC review. Если Android недоступен, desktop claim отдельный, Android AC не закрывать. Commit/push не разрешены.

## 14. Открытые вопросы
Android device и тип resume: async вопрос задан, ответа на момент составления нет; не блокирует A, блокирует Android acceptance. E2E baseline/candidate отсутствует и блокирует заявление о достигнутом ×10 программы. Пользователю не нужно выбирать внутренний алгоритм; варианты выбраны по evidence.

## 15. Соответствие профилю
Performance: четыре hypotheses/method/risk/result; latency+allocations, paired within harness. Individual optimization commits не применимы к SPEC-only scope. Desktop: source/UI не изменены; existing UI tests passed, интеграция требует new UI coverage/run. Product design: APIs, source-of-truth, errors/rollout/rollback описаны.

## 16. Таблица файлов
| Файл | Сейчас | Future |
|---|---|---|
| specs/2026-09-14-task-loading-speedup.md | Единственный новый reviewable artifact | EXEC evidence |
| FileTaskStorage.cs | Read only | A/C resolver/reader |
| UnifiedTaskStorage.cs, FileTaskMigrator.cs | Read only | B post-migration graph/VM/replay; report fix отдельно |
| MainActivity.cs | Read only | Сначала trace |
| src/Unlimotion.Test, tests/Unlimotion.UiTests.Headless | Source unchanged | Resolver/negative/migration/UI regression |

## 17. Было → стало (предложение)
| Область | Было | Цель |
|---|---|---|
| Contracts | Reflection заново на каждый файл | Metadata один раз/policy |
| Initial source | Несколько read/parse циклов | Snapshot + delta |
| Recheck | Historical force повторяет работу | Реальная актуальность/diagnostics |
| IO | await Task.Run последовательно | Bounded workers если оправданы |
| Lifecycle | Новый runtime на switch | Контракт сохраняется, Init ускоряется |

## 18. Альтернативы и компромиссы
| Подход | Инженер/архитектор/ТРИЗ | Experiment | Решение |
|---|---|---|---|
| A metadata reuse | Инженер hotspot; ТРИЗ предварительное действие | 5 paired real-model runs ×86,8 | Выбран first |
| C parallel reader | Независимые операции одновременно с лимитом | degree1/2/4/8, по 5 | Дополнение target-specific |
| B single snapshot | Архитектор единый owner; ТРИЗ объединение повторных операций | 1 vs 3 reads + clones ×2,89 | Условный кандидат только для post-migration consumers; migration writes исключены |
| D resident models | ТРИЗ исключение повторного parse | SHA+copy 266 vs read233ms | Не выбран |

Противоречие: данные обязаны быть актуальны, но полное восстановление дорого. Решение: сохранять обязательную проверку изменений, переиспользовать metadata и результат одного согласованного read. «Вообще не проверять» не допустимо при external sync.

## 19. Quality gate
### SPEC Linter
| № | Критерий | Статус / основание |
|---|---|---|
| 1 | Outcome | PASS §1/AC1 |
| 2 | AS-IS | PASS source/reports/measurements |
| 3 | Root mechanism | PASS resolver experiment; total latency cause ограничена |
| 4 | Goals | PASS §4 |
| 5 | Scope | PASS SPEC-only §5 |
| 6 | Responsibilities | PASS §6.1 |
| 7 | Integration | PASS §8 |
| 8 | Algorithms | PASS §6/7 |
| 9 | Errors | PASS state/recovery |
| 10 | Performance | PASS §11; E2E будущий AC |
| 11 | State/data | PASS matrix/§9 |
| 12 | Compatibility | PASS legacy/unknown/locks |
| 13 | Rollback | PASS §10 |
| 14 | Measurable AC | PASS §11 |
| 15 | AC→test | PASS device limitation явно |
| 16 | Commands/stop | PASS §1/11 + приложение |
| 17 | Plan | PASS A→measure→B/C |
| 18 | Decisions | PASS §6.5/14 |
| 19 | Form/risk | PASS expanded multi-module |
| 20 | Profiles | PASS §15 |

### SPEC Rubric
Цель/границы 5; AS-IS 2 (нет end-to-end phase profile); design 5; безопасность/rollback 5; проверяемость 5; автономность 5. 27/30 относится к плану, не подтверждает AC1. Обоснования: scope отдельно, controlled measurements, A-first, legacy/replay safeguards, явная acceptance matrix, нет user-owned algorithm choices.

### Role-Based Review Result
| Роль | Проверка | Результат |
|---|---|---|
| Domain analyst | Ready означает работу с актуальными данными? | PASS в плане, no spinner-hide proxy |
| UX | Состояние/ошибки/предыдущее пространство сохраняются? | PASS в плане, candidate UI не проверен |
| Tester | Нет смешения harness/E2E? | PASS: раздельные scopes, equality/SHA |
| Architect | Не rewrite ради resolver? | PASS: A-first, B/C evidence-based |
| Operations/security | Live data/main не изменены? | PASS: separate worktree, hash, only spec |

### Post-SPEC Review
- Scope/Evidence: эта SPEC, central stack/profile, affected source methods, actual metadata/reports, два harness, 7 baseline tests.
- Contract: дизайн/эксперименты четырёх подходов выполнены на указанном уровне; интеграционный ×10 и Android будущие AC.
- Adversarial: stale cache, same size/mtime, unknown/legacy fields, shared mutable serializer, duplicate identity, deleted-task resurrection, Amdahl limit.
- Depth: no unrelated changes; scenarios/decisions/objections/AC связаны; unsupported claims отделены; diagnostics/compatibility covered; docs/changelog не затронуты; hidden API/UX changes не вводятся.
- Manual-review challenge: перенос parser gain на весь UI/Android. Такое заявление запрещено до AC1/5.
- Advisory reviewer /root/loading_spec_review выполнил отдельный source/evidence review и независимо пересчитал JSONL: 48 runs, 40 measured, пять на режим, mismatch=0, медианы совпадают. Verdict PASS для ограниченного advisory post-SPEC.
- Effective child runtime: danger-full-access, unrestricted filesystem, approval=never. Технической read-only изоляции нет; reviewer фактически выполнял только чтение/rg/git status/расчёт агрегатов, ничего не менял и не запускал benchmark/build/tests. Это не technically isolated independent review.
- Fix and re-review: закрыты все пять findings ниже. После правок повторены C# benchmark целиком (48 runs), проверка raw counts/medians/equality/SHA, соответствие implementation snippet resolver-only design, scope/status/whitespace/sections. Требования AC1/5 не помечены выполненными.
- Main adversarial fallback: отдельно проверены два контрпримера — внешний edit перед migration write (B теперь исключает такие writes) и clean report перед imported legacy (validation до существующего gate, иначе ForceRecheck сохраняется); проверены отсутствующий extra Init, mutable converter isolation, ложный E2E claim и сравнение разных harness. Source/profile/test evidence сверено с AC и Non-Goals.
- Stop decision: PASS для проекта и ограниченного read-only исследования. Открытых BLOCKER/HIGH/MEDIUM нет. E2E, Android, negative/candidate UI tests — явно будущие acceptance, не «пройдено». No-findings justification: actionable риски исправлены/сужены и перепроверены; допущений о законченной программе нет.

| Severity | Area | Finding | Required action / status |
|---|---|---|---|
| HIGH | Migration concurrency | Retained snapshot мог перезаписать external edit до replay | Fixed: B только post-migration; stale migration writes исключены |
| MEDIUM | Parallel prototype | Settings делили converter objects между workers | Fixed: отдельные settings/serializer/converters; 48 runs повторены |
| MEDIUM | Evidence reproducibility | Первый PS коэффициент не воспроизводился C# приложением | Fixed: основной result fresh/shared в едином стенде, 48 raw JSONL |
| MEDIUM | Android AC | Измерение не обеспечивало ×10 slow path | Fixed: AC5 ×10 для медленного пути и no-extra-init/no-regression быстрого |
| MEDIUM | ForceRecheck | Простая очистка активирует Version-only skip | Fixed: validation до gate, иначе не очищать flag; расширен AC4 |
- Остаточный риск review: нет технической изоляции child; advisory pass и отдельный main fallback не заменяют будущий post-EXEC review.

### Post-EXEC Review
На момент исходной SPEC EXEC ещё не начинался. Актуальные результаты реализации, validation и post-EXEC review находятся в §21; исходные исследовательские прототипы не используются приложением.

## Approval
Для дальнейшей интеграции в продукт действует обычное «Спеку подтверждаю». Сейчас пользователь запросил проектирование/эксперименты. Исследование не разрешает commit/push.


## Приложение A. Воспроизводимый read-only микробенчмарк

Код ниже является исполняемым исследовательским приложением к SPEC. Он компилируется Add-Type в память, не заменяет код программы, не вызывает Init/миграции/Save, не записывает задачи. Snapshot содержит личные данные только в памяти; в отчет выводятся агрегаты. Измерение включает чтение/parse/две копии модели, не включает VM/UI. OS cache прогрет. SHA-256 сверка source выполняется вне времени вариантов (для resident входит в время).

```csharp
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;
public sealed class TaskLoadingExperiment {
 readonly string path;
 readonly string[] files;
 readonly string[] hashes;
 readonly FileTaskStorage storage;
 readonly JsonSerializerSettings settings;
 readonly MethodInfo deserialize;
 readonly TaskItem[] snapshot;
 readonly string[] canonical;
 public TaskLoadingExperiment(string directory) {
  path=directory;
  if(!Directory.Exists(path)) throw new DirectoryNotFoundException();
  files=Inventory();
  hashes=files.Select(Hash).ToArray();
  storage=new FileTaskStorage(new FileTaskStorageOptions {Path=path, UseDirectoryLock=false});
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  settings=(JsonSerializerSettings)typeof(FileTaskStorage).GetMethod("CreateSerializerSettings",flags).Invoke(storage,null);
  deserialize=typeof(FileTaskStorage).GetMethod("DeserializeTask",flags);
  snapshot=Read("shared");
  canonical=snapshot.Select(x=>JsonConvert.SerializeObject(x)).ToArray();
 }
 string[] Inventory()=>Directory.GetFiles(path).Where(f=>!Path.GetFileName(f).StartsWith(".") && new FileInfo(f).Length>0 && (Path.GetExtension(f)=="" || Path.GetExtension(f).Equals(".json",StringComparison.OrdinalIgnoreCase))).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
 static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
 public void VerifySource() {
  if(!Inventory().SequenceEqual(files)) throw new InvalidOperationException("Source inventory changed");
  for(int i=0;i<files.Length;i++) if(Hash(files[i])!=hashes[i]) throw new InvalidOperationException("Source content changed");
 }
 JsonSerializer CreateReadSerializer()=>JsonSerializer.Create(new JsonSerializerSettings {
  ContractResolver=settings.ContractResolver,
  Converters=new JsonConverter[] {
   new Newtonsoft.Json.Converters.IsoDateTimeConverter {
    DateTimeFormat="yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz",
    Culture=System.Globalization.CultureInfo.InvariantCulture,
    DateTimeStyles=System.Globalization.DateTimeStyles.None
   },
   new Newtonsoft.Json.Converters.StringEnumConverter()
  }
 });
 TaskItem[] Read(string mode) {
  var output=new TaskItem[files.Length];
  Action<int> load=i=> {
   var value=mode=="fresh" ? (TaskItem)deserialize.Invoke(storage,new object[]{files[i]}) : JsonRepairingReader.DeserializeWithRepair<TaskItem>(files[i],CreateReadSerializer(),false);
   output[i]=TaskItemSnapshot.Clone(TaskItemSnapshot.Clone(value));
  };
  if(mode.StartsWith("parallel")) Parallel.For(0,files.Length,new ParallelOptions {MaxDegreeOfParallelism=int.Parse(mode.Substring(8))},load);
  else for(int i=0;i<files.Length;i++) load(i);
  return output;
 }
 public string Run(string mode,int iteration) {
  VerifySource();
  GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
  long allocated=GC.GetTotalAllocatedBytes(true);
  var watch=Stopwatch.StartNew();
  TaskItem[] output;
  int consumers=1;
  if(mode=="resident-verified") {
   VerifySource();
   output=snapshot.Select(x=>TaskItemSnapshot.Clone(TaskItemSnapshot.Clone(x))).ToArray();
  } else if(mode=="onepass-three-consumers" || mode=="threepass-three-consumers") {
   consumers=3;
   output=Read("shared");
   for(int c=1;c<3;c++) {
    output=mode=="threepass-three-consumers" ? Read("shared") : output.Select(x=>TaskItemSnapshot.Clone(TaskItemSnapshot.Clone(x))).ToArray();
   }
  } else output=Read(mode);
  watch.Stop(); allocated=GC.GetTotalAllocatedBytes(true)-allocated;
  int mismatches=0;
  for(int i=0;i<output.Length;i++) if(JsonConvert.SerializeObject(output[i])!=canonical[i]) mismatches++;
  VerifySource();
  return JsonConvert.SerializeObject(new {Mode=mode,Iteration=iteration,Ms=watch.Elapsed.TotalMilliseconds,AllocatedMiB=allocated/1048576.0,Tasks=output.Length,Consumers=consumers,Mismatches=mismatches});
 }
}
```



### Команда воспроизведения приложения A
Запуск из worktree, после указанной Release build; shell PowerShell 7 с .NET 10.
~~~powershell
$ErrorActionPreference='Stop'
$bins=(Resolve-Path 'src\Unlimotion.Test\bin\Release\net10.0').Path
foreach($name in @('Unlimotion.Domain.dll','Unlimotion.TaskTree.dll','Unlimotion.FileStorage.dll')){
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $bins $name))
}
$refs=@(
    [Newtonsoft.Json.JsonConvert].Assembly.Location,
    (Join-Path $bins 'Unlimotion.Domain.dll'),
    (Join-Path $bins 'Unlimotion.FileStorage.dll'),
    (Join-Path $bins 'Unlimotion.TaskTree.dll')
) + @(Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
$spec=Get-Content -Raw 'specs\2026-09-14-task-loading-speedup.md'
$source=[regex]::Match($spec,'(?s)\x60\x60\x60csharp\r?\n(.*?)\r?\n\x60\x60\x60').Groups[1].Value
Add-Type -TypeDefinition $source -ReferencedAssemblies $refs -CompilerOptions '/nowarn:1701'
$probe=[TaskLoadingExperiment]::new('C:\Projects\Education\Unlimotion Space\Tasks')
foreach($iteration in 0..5){
    $modes=@('fresh','shared','parallel2','parallel4','parallel8','resident-verified','onepass-three-consumers','threepass-three-consumers')
    if($iteration % 2 -eq 1){[array]::Reverse($modes)}
    foreach($mode in $modes){$probe.Run($mode,$iteration)}
}

$probe.VerifySource()
~~~
Iteration=0 исключается из статистики. /nowarn:1701 относится к reference-assembly version compatibility Newtonsoft (.NET6 reference) с .NET10 в Add-Type; продуктовые warnings не подавлялись. Если данных нет/изменились hashes, эксперимент падает, а не продолжает сравнивать разные snapshots.

### Сырые итоговые результаты (JSONL)
Iteration=0 — warmup; остальные пять участвуют в таблице. Это результаты исправленного приложения A; все mismatch=0, итоговый marker SOURCE_HASH_VERIFIED_UNCHANGED.
~~~json
{"Mode":"fresh","Iteration":0,"Ms":19549.6883,"AllocatedMiB":756.9184417724609,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"shared","Iteration":0,"Ms":233.7666,"AllocatedMiB":42.60399627685547,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel2","Iteration":0,"Ms":144.7291,"AllocatedMiB":42.60590362548828,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel4","Iteration":0,"Ms":90.6941,"AllocatedMiB":42.60893249511719,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel8","Iteration":0,"Ms":86.8317,"AllocatedMiB":42.61119079589844,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"resident-verified","Iteration":0,"Ms":264.851,"AllocatedMiB":9.940185546875,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"onepass-three-consumers","Iteration":0,"Ms":246.4492,"AllocatedMiB":51.960655212402344,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"threepass-three-consumers","Iteration":0,"Ms":811.8497,"AllocatedMiB":127.80400848388672,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"threepass-three-consumers","Iteration":1,"Ms":801.4604,"AllocatedMiB":127.80400848388672,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"onepass-three-consumers","Iteration":1,"Ms":253.8275,"AllocatedMiB":51.960594177246094,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"resident-verified","Iteration":1,"Ms":270.6929,"AllocatedMiB":9.94012451171875,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel8","Iteration":1,"Ms":121.5893,"AllocatedMiB":42.60784149169922,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel4","Iteration":1,"Ms":84.71,"AllocatedMiB":42.60777282714844,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel2","Iteration":1,"Ms":144.8805,"AllocatedMiB":42.605751037597656,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"shared","Iteration":1,"Ms":233.91,"AllocatedMiB":42.60399627685547,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"fresh","Iteration":1,"Ms":21130.1238,"AllocatedMiB":756.5882873535156,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"fresh","Iteration":2,"Ms":22826.8092,"AllocatedMiB":756.6307144165039,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"shared","Iteration":2,"Ms":231.2346,"AllocatedMiB":42.60399627685547,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel2","Iteration":2,"Ms":161.9208,"AllocatedMiB":42.60797882080078,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel4","Iteration":2,"Ms":96.4592,"AllocatedMiB":42.60942840576172,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel8","Iteration":2,"Ms":95.7682,"AllocatedMiB":42.61145782470703,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"resident-verified","Iteration":2,"Ms":265.7999,"AllocatedMiB":9.939849853515625,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"onepass-three-consumers","Iteration":2,"Ms":259.1204,"AllocatedMiB":51.960594177246094,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"threepass-three-consumers","Iteration":2,"Ms":772.2233,"AllocatedMiB":127.80400848388672,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"threepass-three-consumers","Iteration":3,"Ms":734.4787,"AllocatedMiB":127.80400848388672,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"onepass-three-consumers","Iteration":3,"Ms":248.76,"AllocatedMiB":51.960594177246094,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"resident-verified","Iteration":3,"Ms":268.3833,"AllocatedMiB":9.94012451171875,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel8","Iteration":3,"Ms":88.2983,"AllocatedMiB":42.60786437988281,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel4","Iteration":3,"Ms":87.3878,"AllocatedMiB":42.606422424316406,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel2","Iteration":3,"Ms":137.8972,"AllocatedMiB":42.605804443359375,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"shared","Iteration":3,"Ms":232.694,"AllocatedMiB":42.60399627685547,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"fresh","Iteration":3,"Ms":20190.8233,"AllocatedMiB":756.6337814331055,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"fresh","Iteration":4,"Ms":18931.7988,"AllocatedMiB":756.6993560791016,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"shared","Iteration":4,"Ms":230.6675,"AllocatedMiB":42.60399627685547,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel2","Iteration":4,"Ms":141.7817,"AllocatedMiB":42.60911560058594,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel4","Iteration":4,"Ms":92.2478,"AllocatedMiB":42.60841369628906,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel8","Iteration":4,"Ms":77.6949,"AllocatedMiB":42.612754821777344,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"resident-verified","Iteration":4,"Ms":265.212,"AllocatedMiB":9.94012451171875,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"onepass-three-consumers","Iteration":4,"Ms":265.7304,"AllocatedMiB":51.960594177246094,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"threepass-three-consumers","Iteration":4,"Ms":700.2965,"AllocatedMiB":127.80400848388672,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"threepass-three-consumers","Iteration":5,"Ms":701.2993,"AllocatedMiB":127.80400848388672,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"onepass-three-consumers","Iteration":5,"Ms":237.7159,"AllocatedMiB":51.960594177246094,"Tasks":2875,"Consumers":3,"Mismatches":0}
{"Mode":"resident-verified","Iteration":5,"Ms":259.6543,"AllocatedMiB":9.94012451171875,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel8","Iteration":5,"Ms":76.9518,"AllocatedMiB":42.608314514160156,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel4","Iteration":5,"Ms":88.2017,"AllocatedMiB":42.60723114013672,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"parallel2","Iteration":5,"Ms":136.3639,"AllocatedMiB":42.60607147216797,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"shared","Iteration":5,"Ms":248.1666,"AllocatedMiB":42.60399627685547,"Tasks":2875,"Consumers":1,"Mismatches":0}
{"Mode":"fresh","Iteration":5,"Ms":18813.5301,"AllocatedMiB":756.6993560791016,"Tasks":2875,"Consumers":1,"Mismatches":0}
~~~

## 20. EXEC: хронология интеграционных экспериментов
- Явное подтверждение пользователя: «Спеку подтверждаю», 2026-09-14. Работа только в отдельном worktree; commit/push/install не выполнялись.
- Актуальная immutable fixture: 2879 task files + 3 reports, 3382322 bytes. Создана из рабочего пространства read-only; per-file SHA256 сохранены в ignored `TestResults/loading-e2e/input-manifest.json`. Каждое приложение получает отдельные временные A/B копии + один безопасный sentinel. Рабочие данные никогда не передаются Init/migrations.
- Baseline executable сохранен до product fix: `TestResults/loading-e2e/baseline/Unlimotion.Desktop.exe`; FileStorage SHA256 F3E4DD91A9CFA10011810ACFE2038FBE53A84805A7EBB87406D5031CB6F44B8D.
- A реализован только в чтении: два static resolver по PreserveUnknownJson; serializers/converters независимы; Save неизменен. Новый allocation regression до исправления: 38392280 bytes на 128 JSON против 16MiB; 4 остальных serialization tests passed. После A: 5/5 passed.
- FlaUI harness запускает реальный процесс, затем A→B→A. Timer до Launch; Ready: sentinel видим, selector enabled, switch progress скрыт, нет ошибок; Action: выбрать правильную карточку, открыть и закрыть relation editor. Отдельные readyMs/readyAndActionMs; windowAvailableMs — доступность окна через UIA, НЕ измеренный first paint. Fixture hash/copy выполняются до timer; результирующие заголовки/тексты задач в telemetry отсутствуют.
- Исправления harness: возврат в пространство может закрывать панель деталей; щелкать нужно по InlineTaskTitleTextBlock, а не произвольному совпадающему заголовку. Grid loading overlay не всегда UIA control, поэтому дополнительно проверяется TaskSpaceSwitchProgress и enabled selector. Эти невалидные прогоны не включаются в итоговую статистику.
- Предварительный advisory review: product A PASS; P2 про fingerprint, отдельный Ready и завершение Cancel исправлены. Reviewer sandbox фактически writable; техническая read-only изоляция не заявляется.

Предварительные одиночные замеры (НЕ финальная paired статистика; изменялась подготовка fixture/cache):
| Build / run | Startup Ready / Action, s | A→B Ready / Action, s | B→A Ready / Action, s |
| --- | --- | --- | --- |
| baseline warmup v2 | 96.63 / 97.53 | 99.02 / 100.35 | 65.98 / 67.32 |
| A warmup v2 | 26.93 / 27.84 | 21.47 / 22.94 | 9.88 / 11.23 |
| A profile, с SHA-verified копиями | 14.43 / 15.37 | 9.41 / 10.82 | 9.43 / 10.82 |
| A + lazy duration commands | 14.63 / 15.64 | 7.25 / 8.62 | 6.79 / 8.14 |
| A + lazy duration + C parallel4 | 14.29 / 15.31 | 7.19 / 8.63 | 6.99 / 8.46 |

- Полный baseline warmup: 1/1 UI test, 4m32s. В A profile была диагностическая трассировка, этот run не используется для окончательного коэффициента.
- C реализован экспериментально в ReadDirectory/GetAll с Parallel.For degree≤4 и восстановлением исходного порядка, прошел 12/12 storage tests, но не дал существенного выигрыша полного сценария относительно A+duration. C полностью удален из product diff, сборка для воспроизведения сохранена ignored.
- Профиль `candidate-a-full.nettrace` / `.speedscope.json`: за окно двух переключений TaskItemViewModel.Init ~14.4s inclusive sampled thread time; SetDurationCommands ctor ~6.46s; заметен SystemClock.Register/CollectHandlers. Это sampled wall time потоков, не CPU percentage и не аддитивные независимые этапы.
- Дополнительный вариант E: создавать команды карточки/контекстного меню при первом использовании. Duration-only уменьшил VM allocations: исходный тест 39157128 bytes /128 VM, после lazy duration прошел бюджет24MiB. Проверены повторное использование команд и первое изменение/очистка длительности; реальный Avalonia menu UI test 1/1 passed. Сейчас проверяется расширение E на Archive, Add/RemoveCompletionCriterion, Unblock, DeleteParentChildRelation. SaveItemCommand и подписки, обеспечивающие model/state, остаются при Init.
- Ошибки runner/filters (не TDD red): unsupported property-filter combination и OR-синтаксис из skill дали error/zero tests; исправлено последовательным запуском отдельных классов. Не включать эти попытки в passed evidence.
- Android: SDK/workload присутствует, `adb devices -l` не показал устройств. Пользователю задан асинхронный вопрос о подключении телефона. Реальное Android resume/process recreation ускорение пока НЕ доказано.
- UI video для реального набора: privacy fallback, поскольку окно показывает личные задачи. Next-best evidence — процессный FlaUI сценарий, SHA manifests, JSONL timings и .nettrace. Безопасный небольшой UI smoke подтверждает взаимодействия отдельно от performance.

## 21. Итоговый состав EXEC и проверка

Продуктовый diff: `FileTaskStorage.cs`, `TaskItemViewModel.cs`, `SetDurationCommands.cs`. Добавлены/обновлены `FileTaskStorageReadContractTests`, `StartupProjectionAndRelationsTests`, `TaskDurationMenuStartupUiTests`, `TaskLoadingPerformanceFlaUiTests` и скрипт повторяемого измерения. `UnifiedTaskStorage.cs` в итоговом diff отсутствует. Все диагностические данные/сборки находятся в ignored `TestResults`; тексты реальных задач в Git не добавлены.

### 21.1 Решения по экспериментам

| Вариант | Гипотеза / проверка | Результат / решение |
| --- | --- | --- |
| A: shared read resolver | Не строить reflection-контракты заново для каждого JSON; fidelity/allocations + реальный процесс | Оставлен. Две политики resolver раздельны; settings, serializer и converters локальны операции. Save без изменения. |
| B: один post-migration snapshot | Удалить повторное чтение уже проверенного состояния | Не включён. В интегрированном live graph GetAll уже занимает 3–13 ms, reconcile 118–192 ms; экономия мала. Миграции сохраняют собственные чтения и проверки. |
| C: parallel reader, degree≤4 | Параллельно читать/разбирать файлы, сохранить порядок применения | Испытан в продукте, 12/12 storage tests. A+duration: A→B 7,249 s / B→A 6,794 s; с C 7,192 / 6,990 s. Существенного E2E выигрыша нет; откатан. |
| D: resident cache с проверкой | Сохранить snapshot между активациями | Исследовательский verified-cache prototype проверен, production не включён: проверка источника всё равно нужна, добавляется сложность invalidation/владения. |
| E2: команды карточки по первому обращению | Не создавать 13 duration и 5 card/relation commands для тысяч неоткрытых карточек | Оставлен. Общие состояния, autosave и SaveItemCommand инициализируются как раньше. Проверены первое использование, текущий статус, override ownership, concurrent initialization, dispose. |
| F: одна публикация cache | Убрать повторную сортировку/биндинг промежуточных порций | Испытан: regression 3→1 notification, 8/8 projection + 2/2 loading + 5/5 single-view tests. Warmup Ready 14,722 / 6,885 / 5,706 s. Убедительного улучшения нет; убран, сохранены batch64/Task.Yield. Не заявляется доказательство отзывчивости единой публикации. |
| G: parallel VM creation, degree≤4 | Сократить около 1,9 s создания VM; join до связей/публикации | Ограниченный прототип с ordered array и cleanup созданных VM при failure/cancel. FlaUI 1/1; warmup Ready 13,554 / 6,356 / 5,704 s. Переключения практически не ускорились; прототип убран, в production и регрессионные гарантии не включён. |

В F/G приведён порядок startup / A→B / B→A. Это диагностические одиночные runs, не окончательные коэффициенты. Они не доказывают полную эквивалентность отклонённых вариантов. `UnifiedTaskStorage.cs` полностью возвращён к baseline.

### 21.2 Безопасность ленивых команд

Первый вариант E2 имел два обнаруженных review дефекта: гонку `??=` и создание активной команды после Dispose. Добавленные регрессии подтвердили оба дефекта (разные экземпляры при 32 concurrent accesses; 2 CanExecuteChanged после teardown). Исправление: единственный lock и принадлежащий VM CompositeDisposable, зарегистрированный при Init. Позднее добавление в disposed owner немедленно освобождает подписки. Публичные setters сохранены, внешние overrides не становятся собственностью VM. Для SetDurationCommands освобождаются исходные 13 команд, даже если их свойства позже заменены.

После исправления 7/7 StartupProjectionAndRelationsTests passed. TaskItemViewModelStatusCommandTests 24/24 и настоящий duration-menu UI test 1/1 passed. Финальные полные suites фиксируются ниже отдельно.

### 21.3 Фазы A+E2 (диагностическая сборка)

| Этап, ms | Startup | A→B | B→A |
| --- | ---: | ---: | ---: |
| Status migration | 872 | 738 | 615 |
| Reverse-links migration | 782 | 528 | 397 |
| Availability migration | 778 | 622 | 122 |
| Enable live graph | 588 | 443 | 371 |
| Pending events | 28 | 19 | 3 |
| Read models из graph | 3 | 12 | 13 |
| Create VM | 1870 | 1852 | 1922 |
| Relations | 146 | 116 | 132 |
| Build total | 5075 | 4332 | 3582 |
| Publish cache | 1134 | 2 | 1 |
| Reconcile | 192 | 138 | 118 |

Источник: ignored `TestResults/loading-e2e/ae2-phases.txt`. Phase instrumentation удалён из source после измерения, сборка хранится отдельно. Фазы не равны времени от старта процесса и не используются вместо полного E2E.

### 21.4 Воспроизводимость финальной серии

Скрипт `scripts/measure-task-loading.ps1` получает Dataset/BaselineExe/CandidateExe/OutputDirectory. Harness предварительно собирается в Release. Серия последовательная: отдельный warmup для обоих вариантов, затем 5 повторений с чередованием порядка. Каждое повторение выполняет запуск, A→B, B→A и действие в карточке после каждой загрузки. Файловый кэш прогрет copying/SHA verification у обеих версий; очистка OS cache не производится. Новые процессы запускаются каждый раз, но это не измерение запуска после перезагрузки устройства.

Команда выполненной серии:

```powershell
./scripts/measure-task-loading.ps1 -Dataset TestResults/loading-e2e/dataset -BaselineExe TestResults/loading-e2e/baseline/Unlimotion.Desktop.exe -CandidateExe TestResults/loading-e2e/candidate-selected/Unlimotion.Desktop.exe -OutputDirectory TestResults/loading-e2e/paired-selected -Runs 5
```

Каждый HTML TUnit report содержит captured stdout с inputFingerprint и четырьмя assembly SHA256: FileStorage, ViewModel, Unlimotion, Desktop. JSONL содержит только агрегатные времена и число файлов. Warmup хранится отдельно. После серии обязательны проверка 30 записей/10 успешных UI tests, совпадения входного hash, постоянства hashes каждого варианта и различия изменённых assemblies. Для текущего fixture проверка реализована в ignored `TestResults/loading-e2e/audit-paired.py`; результат `audit-summary.json`.

Audit завершён: 30/30 measured records, 10/10 measured UI tests, отдельно 2/2 warmup. Один inputFingerprint `49906522CB71F4F1AFE2F4EEB68ECE04DD9F3286BA12E1354EAE6D21B567135A`. Все четыре assembly SHA256 постоянны внутри каждого варианта; FileStorage и ViewModel различаются между baseline/candidate. Финальная повторная проверка fixture: 2882/2882 файла, 3382322 bytes, все SHA256/длины совпали с манифестом.

### 21.5 Финальные desktop E2E результаты

Пять measured runs, warmup исключён. `Ready` отсчитывается от Launch процесса или щелчка по выбранному пространству. Это полная измеренная задержка, без вычитания времени создания окна.

| Сценарий | Baseline median, s | Candidate median, s | Ускорение median | Baseline mean / min–max, s | Candidate mean / min–max, s |
| --- | ---: | ---: | ---: | --- | --- |
| Startup Ready | 80,988 | 11,249 | ×7,20 | 85,215 / 79,695–101,485 | 11,770 / 10,939–14,058 |
| A→B Ready | 85,888 | 6,861 | ×12,52 | 87,711 / 84,987–95,483 | 6,753 / 6,404–6,893 |
| B→A Ready | 62,715 | 6,122 | ×10,24 | 62,465 / 60,217–64,061 | 6,017 / 5,689–6,446 |

Отдельная метрика: тот же старт timer → завершение успешного действия в карточке (выбор задачи, открытие редактора связи, отмена с подтверждённым закрытием):

| Сценарий | Baseline median, s | Candidate median, s | Ускорение median |
| --- | ---: | ---: | ---: |
| Startup + Action | 81,846 | 12,327 | ×6,64 |
| A→B + Action | 87,214 | 8,319 | ×10,48 |
| B→A + Action | 64,199 | 7,538 | ×8,52 |

**AC1 закрыт частично:** Ready двух переключений ≥×10; полный запуск ×7,20. Успешное действие подтверждено во всех runs, но коэффициент полного сценария возврата с действием ×8,52. Нельзя объявлять исходную цель ≥×10 для всех сценариев выполненной. First paint и холодный OS cache не измерены; доступность окна через UIA записана отдельно и не вычиталась из Startup Ready. Android в эту таблицу не входит.

### 21.6 Диагностика памяти и очереди UI

Отдельные instrumented baseline/candidate: по одному запуску с A→B→A, 2/2 FlaUI tests passed, 6/6 heartbeat records прошли структурный audit, в каждой записи 2880 projected tasks и нет ошибок пробника. Эти runs не входят в пять пар §21.5. Артефакты: ignored `heartbeat-baseline.jsonl`, `heartbeat-candidate.jsonl`, `heartbeat-audit.json` в `TestResults/loading-e2e`.

| Метрика baseline → candidate | Startup | A→B | B→A |
| --- | ---: | ---: | ---: |
| Init, ms | 75974 → 6740 | 84220 → 4395 | 61168 → 3902 |
| Allocated during Init, MiB | 4132 → 743 | 4362 → 609 | 3302 → 551 |
| Process lifetime peak working set at Init end, MiB | 846 → 432 | 1501 → 686 | 2161 → 894 |
| Completed dispatcher samples | 3680 → 236 | 4160 → 195 | 2963 → 181 |
| Completed dispatcher delay p95, ms | 0,686 → 2,510 | 0,713 → 0,614 | 0,636 → 0,555 |
| Completed dispatcher delay max, ms | 315,654 → 316,889 | 62,133 → 67,171 | 14,859 → 33,791 |
| Outstanding callback lower bound at Init end, ms | 1723 → 1495 | 136 → 202 | 161 → 97 |

Метод: Threading.Timer с периодом 20 ms публикует не более одного незавершённого Dispatcher callback с Input priority; gate защищает завершение и запись. Ноль samples означает недостаточность измерения, ошибки публикуются отдельно. Также записаны максимальный завершённый timer gap и нижняя граница хвоста, чтобы не скрыть starvation. GC/allocated bytes считаются для всего процесса за интервал Init; peak working set — максимум с рождения процесса, поэтому переключения включают прошлые этапы.

Scope ограничен `UnifiedTaskStorage.Init`: post-Init layout/render и первое действие сюда не входят. p95 завершённых callbacks не является долей wall time или frame latency; незавершённый callback учитывается отдельно как цензурированная нижняя граница. При startup candidate сохраняется задержка минимум 1495 ms. **Полная отзывчивость UI / AC6 не доказана.** Измеренное уменьшение allocations и peak memory не отменяет эту паузу.

Диагностический builder сохранял исходные bytes четырёх файлов, временно включал probe, затем в finally восстановил и проверил точное совпадение. `LoadingHeartbeatProbe.cs` удалён; `UnifiedTaskStorage.cs` не имеет diff. Инструментация отсутствует в выбранном product source и основной серии §21.5. Review методики — PASS; reviewer работал в advisory режиме, без технического read-only ограничения.

### 21.7 Оставшиеся проверки / границы

- Полные suites завершены последовательно на итоговом product source: **Main 964/964**, 0 failed/0 skipped, 17m40,418s; **Headless 38/38**, 0 failed/0 skipped, 1m42,958s. Для каждого проекта выполнены restore → Debug build → test через штатный `scripts/ci/Invoke-TestStage.ps1`, без фильтра, `--maximum-parallel-tests 1`. TRX/HTML, invocation и stage metadata: ignored `TestResults/loading-final/main` и `headless`. Это локальная проверка, не GitHub CI.
- AC4 (устранение вечного ForceRecheck): не реализован. Это отдельный безопасный fast-path с обнаружением внешнего legacy import, а не простое сбрасывание флага. Вариант A+E2 сохраняет прежние миграционные проверки и формат reports.
- AC5: физический Android не подключён; ≥×10 Android и Activity/process recreation пока не подтверждены. Ограниченный эмуляторный smoke описан ниже.
- AC6: allocation regression и отдельные dispatcher/peak memory measurements завершены (§21.6); остаётся startup UI pause и отсутствует измерение всей post-Init фазы. Полный набор критериев не объявлен закрытым.
- Независимый post-EXEC review A+E2 — **PASS в ограниченном scope**, оставшихся P1/P2 нет. Reviewer прочитал product diff/tests/harness, независимо пересчитал 30 measurements и сверил TRX 964/38. Сборки/тесты повторно не запускал; review advisory, технического read-only sandbox нет. Verdict не закрывает исходные AC1/4/5/6. Публикации/установки в рабочую программу нет.

Следующий участок работы для исходного целевого SLA: дополнить профиль интервала от завершения Init до Ready и первого действия, включая создание окна/первый layout; отдельно проверить startup публикацию и подписки сортировок. Преждевременная единая публикация F и parallel VM G уже проверены и не выбраны. Для migration fast-path сначала нужен контракт обнаружения внешних legacy изменений и negative tests AC4, затем сравнение полного процесса. Для Android нужны отдельные измерения same-PID resume, Activity recreation и cold process на устройстве; desktop коэффициенты переносить на него нельзя. Эти пункты остаются работой, а не выполненными проверками.

### 21.8 Android build и ограниченный smoke

`dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug -r android-x64 -o TestResults/loading-android/candidate` завершён: 0 errors, 37 warnings, 2m55,82s. Предупреждения включают LibGit2Sharp dependency version и Android native libraries/page-size/duplicate entries; эта проверка не подтверждает Release/Android 16 совместимость. APK и build.log находятся в ignored `TestResults/loading-android`.

Создан отдельный пустой AVD `unlimotion_loading_20260914`, API34 x86_64, Pixel6 profile, 1536MiB RAM/3cores/WHPX. Его данные находятся в worktree/TestResults. Личные существующие AVD и физические устройства не использованы; все команды адресованы `emulator-5580`. Debug APK установлен только туда. Начальный `am start -W` вернул timeout, но позднейший screenshot и UI hierarchy подтвердили отрисованное окно Local tasks с пустым списком. Это не успешный latency benchmark: Android ActivityManager timing не равен Ready.

HOME → повторный start вернул Status=ok; PID сохранился 5302→5302, UI hierarchy после возврата содержит Filters/Search. Сохранены `loading-smoke.png` (визуально проверен), XML до/после и `resume-smoke.json`. **Подтверждён только запуск и same-PID resume на пустом fixture.** Реальный набор 2879 задач, baseline-сравнение, полноценное действие в карточке, Activity recreation/process death, физический телефон и ×10 performance в этом smoke не проверены. Видеозапись Android smoke не выполнялась; fallback — screenshot + XML/PID evidence. После проверки тестовый эмулятор остановлен.

## 22. Журнал действий агента
| Фаза | Намерение | Confidence | Не хватает | Следующее действие | Передача человеку | Фактическое обращение/решение | Объяснение | Артефакты |
|---|---|---:|---|---|---|---|---|---|
| SPEC | Preflight, memory/source routing, separate worktree | 0.99 | Phase timings | Inspect loader | Нет | Пользователь попросил дизайн/эксперименты | Изолировать исследование от main и чужих SPEC | Worktree; эта SPEC |
| SPEC | Hotspot / migration / lifecycle analysis | 0.99 | Android trace | Read-only measurements | Да для Android evidence | Async вопрос о телефоне/resume задан, ответа пока нет | OnResume не содержит Init; не угадывать причину | Source/read reports |
| SPEC | Real data baseline / shared resolver | 0.99 | Full UI timings | Compare variants | Нет | Не запрашивалось разрешение на чтение | 2875 models equal, source SHA unchanged | SPEC measurement table |
| SPEC | Four approach prototypes + baseline tests | 0.98 | Integrated candidate + device | Design/review | Нет | Продуктовая реализация не выполнялась | Add-Type in-memory from SPEC; source/tests unchanged | Appendix A; 7/7 tests |
| SPEC | Reproducibility and spec assembly | 0.98 | Final review | Reviewer + sanity | Нет | Нет | Add-Type references исправлены; длинная команда документа заменена bounded writes, не product fix | Только эта SPEC |







| SPEC | Итоговый controlled rerun и review | 0.99 | E2E/Android/candidate negative tests | Передать готовый проект; интеграция отдельным этапом | Да для следующего EXEC | Продуктовый approval не запрашивался: текущий запрос про проект | 48 runs, 0 mismatches, SHA unchanged; пять findings исправлены, advisory + main fallback PASS | Эта SPEC |


| EXEC | Подтверждение и preflight | 1.0 | E2E benchmark и fidelity regression | Добавить failing allocation check, затем минимальный read-only resolver fix | Нет | Пользователь: «Спеку подтверждаю» | Рамки A-first подтверждены; commit/push не разрешены | SPEC; отдельный worktree |
| EXEC | Интеграция A/E2, эксперименты C/F/G и review | 0.99 для локального diff | Полные suites, paired audit, Android | Завершить последовательную validation | Да только для физического телефона и будущей публикации | Телефон запрошен асинхронно; разрешение на product EXEC уже есть | Оставлены A/E2; C/F/G откатили по E2E; public API и lifecycle сохранены | §20–21; ignored TestResults/loading-e2e |
| EXEC | Финальная локальная validation | 0.99 для измеренных desktop результатов | Полный startup ×10, AC4, физический Android, post-Init UI latency | Профиль оставшейся startup фазы и отдельная Android acceptance | Да для физического устройства; публикация отдельно | Дополнительное разрешение на уже одобренный EXEC не запрашивалось | 5 пар/30 records audited; Main964/964, Headless38/38; diagnostic6/6; advisory post-EXEC PASS; Android build и empty same-PID smoke | §21; source-manifest.json; ignored raw evidence |
| Delivery | Оформить draft PR с A+E2 | 0.99 для проверенного source | Интеграция с актуальным main и оставшиеся AC | Коммиты, push, draft PR | Нет | Пользователь: «Оформи pr» — разрешены необходимые commit/push/PR, merge/release не запрошены | Source manifest повторно совпал; результаты опубликованы с границами доказательства | Эта SPEC; PR validation; локальные raw evidence не публикуются |

## 22. Исправление проверок PR #297 после rebase — 2026-09-15

Продолжение утверждённого EXEC по прямому поручению пользователя «Посмотри PR ... давай исправим это». Scope: исправить CI-совместимость теста, добавленного этим PR, и настройку bootstrap Android SDK. Продуктовый контракт чтения/записи дат и оптимизации A+E2 не изменяются; прошлые незакоммиченные исследования и UI/characterization tests не включаются в исправление.

Evidence: PR head f907719225f8ec921e05ed0680fa6e9aa616a429 совпадает с локальным HEAD. Run34959061037: Main1019/1021, две ошибки в FileTaskStorageReadContractTests — ожидается offset+03, на UTC runner получен0. Run34959061060: setup-android падает до компиляции с Failed to find package tools. CodeQL успешен.

Решения и наблюдаемые контракты:
- Десериализация до и после shared resolver использует тот же IsoDateTimeConverter и настройки JsonTextReader. Проверить локальную нормализацию offset при сохранении момента времени, а не навязывать новое production-поведение.
- Расширить fixture разными исходными смещениями и проверять UTC instant, ожидаемое локальное представление и roundtrip. Не менять timezone ОС и не подгонять CI к Москве.
- У setup-android@v3 явно задать packages: platform-tools; последующий шаг продолжает устанавливать pinned build-tools/platform/NDK. Источник: официальный action.yml v3, default tools platform-tools; эта настройка не нужна компилятору приложения.
- AC: воспроизведён неверный offset-contract вне исходной зоны; targeted tests и полный Main green; Android workflow YAML валиден, package argument проверен. Результаты локальной сборки, проверки sdkmanager и CI не смешивать.
- Риски: случайное ослабление date assertions; покрыть точный момент времени и offset отдельно. Bootstrap Android локально не воспроизводит Ubuntu runner; окончательная проверка — CI. UI/API/storage не меняются, visual artifact не применим.
- Post-SPEC self-review: Scope/Evidence и Contract проверены по diff resolver и исходникам конвертера/action; adversarial — разные offsets, roundtrip и сохранность unknown JSON; tester/dev/operations роли покрыты, UX не применим. Решение: продолжить локальное исправление существующего PR; повторное продуктовое согласование не требуется.

### Журнал действий агента — CI follow-up
| Фаза | Намерение | Evidence / решение | Следующий шаг | Передача человеку |
|---|---|---|---|---|
| EXEC | Выяснить обе ошибки CI | Два неверных timezone assertion и отсутствующий Android tools; не ошибки компиляции приложения | Воспроизвести и исправить в узком scope | Не требуется: исправление запрошено |
| EXEC | Воспроизвести зависимость теста от зоны | Fixture с +03:00/-05:30/+00:00 и обеими JSON-политиками: старое ожидание дало4 failures, включая +00 на Moscow host | Проверять сохранение UTC instant и legacy local offset | Не требуется |
| EXEC | Исправить и проверить targeted scope | Read-contract9/9 green; workflow разобран PyYAML, sdkmanager --list exit0 подтверждает platform-tools; source приложения не менялся | Полный Main по CI script и новые удалённые checks | Не требуется |
| EXEC | Post-EXEC review перед обновлением PR | Advisory reviewer + self Scope/Contract/Adversarial/Role passes: находок нет; full Main запущен, его итог ещё не заявляется | Передать проверенные узкие исправления в тот же PR | Поручение пользователя исправить PR; прежнее поручение оформить PR сохраняется |

Review evidence: `TestResults/pr297-ci-fix/red.log`, `green.log`, `sdk-packages.log`, исходные CI logs; post-EXEC проверены production diff, offset counterexamples, политики JSON, SDK bootstrap и сохранение pinned Android dependencies. Reviewer работал без изменений файлов в danger-full-access; технически изолированным read-only audit это не называется. Общий полный прогон и удалённый CI отражаются отдельными результатами PR, не подменяются targeted green. UI-поведение не изменено; прежний Headless CI step уже был success.

### 22.1 Дополнительная гонка UI-состояния в повторном CI

Повторный Tests run `34962256177` на `2756948a`: Main 1024/1025, Headless 40/40; date-contract полностью green. Новый failure — `MainTabs_LanguageChangeWhileOverflowActive_RecalculatesHiddenTabWidths`: русский заголовок остался английским. Android и CodeQL green.

Трасса `ci-main/test-results/34962256177-1/main/diagnostics-8340.jsonl` показывает одновременное выполнение language UI test (11:19:46.819–11:20:03.315 UTC) и двух `TaskSpaceTransactionTests.BindInitializedStorage_*` (11:19:49.856–11:20:03.402 UTC). Оба создают MainWindowViewModel без settings; его SettingsViewModel вызывает SetLanguage у глобального LocalizationService.Current. У этих методов нет SharedUiStateParallelLimit. Параметр MTP maximum-parallel-tests=1 фактически не исключил пересечение с очередью NotInParallel-тестов. Это конкретный незакрытый доступ к общему UI-состоянию.

Узкое исправление: пометить только два создающих MainWindowViewModel метода существующим `ParallelLimiter<SharedUiStateParallelLimit>`. Остальные transaction tests сохраняют текущую планировку; production, локализация и assertions не меняются. Проверка: совместный запуск transaction/UI сценариев с диагностикой и проверкой отсутствия пересечения, затем новый CI. Self post-SPEC Scope/Contract/Adversarial/Role pass: однозначный дефект тестовой изоляции, исправление в разрешённом scope CI follow-up.

Дополнительное локальное наблюдение: полный Main 1035/1036 (включает 11 неопубликованных исследовательских тестов), один failure emoji trail на ширине390. Отдельно карточка3/3, tabs8/8. В CI этот emoji-тест прошёл. Причину локального сбоя пока не считать доказанной и не скрывать повторным green.

Промежуточная проверка22.1: сборка успешна, TaskSpaceTransactionTests42/42. Проверка TRX выявила, что union полного пути фактически выбрал только первый класс, а синтаксис OR из актуальной документации дал zero tests на установленном TUnit1.44/MTP2.2.2. Эти попытки не засчитываются совместным UI-прогоном. Для итоговой проверки используется полный Main без фильтра, с анализом trace на пересечения. Advisory post-EXEC static review: находок нет; точный момент SetLanguage в исходном CI не протрассирован, доказан дефект изоляции и согласующийся с failure механизм.
