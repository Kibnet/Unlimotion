# Оставшиеся эксперименты по загрузке Unlimotion

## 0. Метаданные
- Фаза: EXEC завершён; выбран N4+N6a, отклонённые прототипы удалены, post-EXEC review и финальные validation gates пройдены.
- Владелец: пользователь; исполнитель: Codex. Масштаб large, expanded canonical template центрального каталога.
- Профили: dotnet-desktop-client, ui-automation-testing; контексты performance-optimization и testing-dotnet; локальный UI testing override; QUEST/review-loops.
- Worktree: `C:\Users\Kibnet\.codex\worktrees\task-loading-next-pass\Unlimotion`; ветка `perf/task-loading-next-pass`; база `6c58a31c500af4a07d6a0429f84782c75ad490d2` (main после merge #297).
- Runtime исследования: Windows/PowerShell, .NET; точные SDK/runtime/железо/параметры будут записаны перед серией. Effective model ID/reasoning не сообщены runtime; model eval не применим к оптимизации C# приложения.
- Источник запроса: история задачи `01a09d12-adbe-79c0-b1e5-35d1a2859275`, просьба испытать все ранее предложенные, но ещё не испытанные способы.
- Источники результатов: `specs/2026-09-14-task-loading-speedup.md` в текущем дереве; в старом `C:\Users\Kibnet\.codex\worktrees\task-loading-speedup\Unlimotion` — `specs/2026-09-14-task-loading-speedup-next-research.md`, `specs/2026-09-15-task-loading-shared-observations.md`, `TestResults/loading-profile-current/REPORT.md`. Последние три локальные, не часть main.
- Исторические утверждения спецификаций не переносятся на эту расширенную кампанию автоматически. Предусмотрено одно подтверждение всей программы, без выбора пользователем внутренних алгоритмов.

## 1. Overview / Цель
Проверить каждый оставшийся способ из истории, сравнить безопасные прототипы с актуальным main и выбрать подтверждённую комбинацию. Ускорением считать полное время до доступной актуальной задачи и первого действия, а не только сокращение Init или allocations.

Outcome contract:
- Success: у каждого N1–N11 есть реализация-проба и результат либо конкретный воспроизводимый блокер; у безопасных интегрированных кандидатов есть парный E2E. Непроверенные/заблокированные варианты не объявляются испытанными. Выбранная комбинация проходит проверки поведения и отдельную итоговую серию.
- Артефакты: эта SPEC с итоговым решением; локальные raw JSONL/TRX/traces, manifest источников и данных, воспроизводимые скрипты и патчи каждого кандидата; локальные изменения только выбранных оптимизаций и нужных тестов.
- Stop rules: ошибка корректности останавливает измерение кандидата до исправления; доказанный проигрыш исключает его; после предельной серии неопределённость так и называется. Отсутствие выигравшего варианта — допустимый отрицательный результат, не повод обещать ускорение.

## 2. Текущее состояние (AS-IS)
Merged A (общий неизменяемый Json.NET resolver) и E2 (ленивые команды) остаются контролем. UnifiedTaskStorage последовательно выполняет миграции, live graph/reconcile, создание VM и индекса отношений, публикацию порциями с UI yield, включение watcher и финальный reconcile. FileTaskStorage.GetAll без live graph запускает отдельный Task.Run для каждого DeserializeTask. TaskItemViewModel и MainWindowViewModel создают повторяющиеся WhenAnyValue expressions/подписки.

Предыдущий профиль относится к source до merge: Ready 10,636 / 6,704 / 5,276 s для старта/A→B/B→A. Отдельная instrumented серия: миграции+graph 2,81 s/256 MiB, VM 1,77 s/306 MiB, первая публикация 1,17 s/148 MiB; до Init около 3,58 s. Эти фазы из разных измерений не складываются в полное время. Нужен новый baseline main.

### Инвентаризация уже испытанного
| ID | Что испытано | Evidence / решение | Что этим НЕ проверено |
| --- | --- | --- | --- |
| A, E2 | Resolver, ленивые duration/card/relation commands | Приняты, merged #297 | Ленивый полный редактор и компактные проекции |
| B | Повторный GetAll после миграций | Малый остаточный бюджет, не выбран | Общий снимок для самих миграций |
| C | Parallel file reader degree≤4 | Интегрирован и снят, существенного выигрыша нет | Один последовательный worker вместо тысяч Task.Run |
| D | Resident parsed-model cache с полным hash | Микробенч 265,80 против 232,69 ms, проигрыш | Повторное использование VM/runtime пространства |
| F | Единая публикация cache | UI regression tests прошли; только exploratory warmup E2E, убедительного выигрыша нет, снят | Сокращение построения самих UI-проекций |
| G | Parallel VM degree≤4 | Exploratory warmup E2E, переключения почти без изменения, снят | Ленивые VM/редакторы |
| H | ReadyToRun | 5 пар: startup около −10%, A→B +10,4%, размер +21,1%; не выбран | Отключение tiered compilation |
| K1 | Общий поток трёх Status observers | 5 пар VM: медиана +6,31%, быстрее 2/5; снят | Замена затратного транспорта INPC на остальных путях |
| J/K micro | Hash/parse; прямые handlers/CurrentThread, Publish.RefCount на синтетике | Только механизмы, не доказательство полной эквивалентности и E2E | Интеграция N4–N6 |

F/G не называются статистически доказанно бесполезными: это завершённые предварительные пробы. Не повторять их под другими именами. Новое основание для повторения фиксировать отдельно.

## 3. Проблема
После первого ускорения остались повторное чтение/проверка одного содержимого, дорогое построение VM/подписок и работа UI до готовности. Потенциальная экономия известна по профилю, но вклад отдельных решений и их совместимость не измерены.

## 4. Цели дизайна
Сохранить семантику JSON/миграций, полный набор задач, watcher/recovery, UI и lifecycle; изолировать вклад каждого изменения; исключить скрытый перенос задержки; ограничить удержание памяти и обеспечить простой откат.

## 5. Non-Goals
Не менять формат задач, serializer, бизнес-правила, внешний вид и набор доступных задач; не отключать миграции/проверки целостности, не менять глобальные schedulers/GC mode. Не повторять A–H/K1 без новых оснований. Не писать в установленную программу, личные настройки и исходный набор задач. Не делать push/PR/merge/release по исторической авторизации старой ветки. Не заявлять Android или холодный OS cache по desktop измерениям.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- FileTaskStorage/JsonRepairingReader: N2/N3, диагностический снимок чтения для N4/N5.
- UnifiedTaskStorage и миграторы: проверка снимка, его поколения и postconditions; никаких решений о UI из storage.
- TaskItemViewModel/наблюдения: N1/N6/N8; MainWindowViewModel/ConnectCore: N1/N7/N10.
- `Services/TaskSpaceCoordinator.cs`, `TaskSourceManager.cs`, `TaskSourceRuntime.cs`: N9; `App.axaml.cs` bootstrap и конфигурация отдельного процесса: N10/N11. `Unlimotion.Android/MainActivity.cs` — точка наблюдения Android lifecycle, а не доказанный источник reload: текущий OnResume сам по себе только завершает запрос доступа к хранилищу.
- Тесты/benchmark harness: измерение готовности и поведения, injection гонок/ошибок; не включать profiling overhead в основную серию.

### 6.2 Детальный дизайн экспериментов
Все варианты испытываются отдельно от неизменяемой базы. Зависимые варианты дополнительно сравниваются с непосредственным предшественником; комбинация измеряется заново. Назначение основных сценариев ниже фиксируется до результатов. Статусы: winner (подтверждён E2E), component-candidate (корректен, воспроизводимо сократил работу/allocations, E2E нейтрален), rejected, inconclusive, blocked. Component-candidate не называется ускорением загрузки, но участвует в обоснованной совместной пробе с другими совместимыми кандидатами. Известная E2E/UX-регрессия не маскируется этим статусом.

| ID | Конкретная проба | Основной сценарий / проверка механизма | Контракт и отрицательный контроль |
| --- | --- | --- | --- |
| N1 | Static readonly типизированные expressions прямых свойств; сначала TaskItemViewModel, затем повторяемые UI selectors отдельной дельтой | Startup; VM time/alloc, reflection arrays | Не захватывать VM/closures, не кэшировать значения; initial/repeated/broad INPC, reentrancy, Dispose |
| N2 | Вынести последовательный проход файлов на один worker вместо Task.Run на каждый файл | Startup; reader time/alloc/task scheduling | Порядок, clone isolation, ошибки чтения/repair/duplicate ID, отмена; UI поток не блокируется |
| N3 | Пул буферов Json.NET reader/repair path без смены serializer | Startup; parser alloc/GC и файлы разных размеров | Буферы возвращаются в finally, не разделяются между читателями; повреждённый JSON, исключения и конкурентные readers; никакого double-return |
| N4 | Один read snapshot с bytes/models/diagnostics для проверок неизменённого содержимого и live graph | Startup и A→B; число чтений/parse по фазам | Порядок миграций прежний; после записи свежий snapshot; ошибки/duplicates/hidden/empty не теряются |
| N5 | Сертификат проверенного post-migration содержимого, hit по полному inventory+SHA, использование N4 | Повторный fresh-process запуск того же пути; отдельно cold miss, unchanged hit, changed miss | Только full-content validation; алгоритм/источник/path/version bound; сбой certificate → slow path; same-size/mtime mutation обязана дать miss |
| N6a | Прямой адаптер INPC с минимальным transport на подтверждённых горячих путях, сохраняя подписочный контракт | Startup и B→A; queues/Register/CollectHandlers, attach/dispose | Initial delivery, broad property name, reentrancy, изменение при подписке, attach/detach thread, errors, Dispose; не повтор K1 |
| N6b | Локальный CurrentThread transport на тех же путях, отдельная альтернатива N6a | Те же сценарии и отдельный патч | Никаких глобальных scheduler overrides; проверка последовательности callbacks и UI thread affinity |
| N6c | Общие наблюдения повторяемых PlannedBeginDateTime/IsCanBeCompleted и lazy consumers; отдельные пробы sharing этих свойств, затем совместимость с Status | Startup; реальные attach/detach/alloc и VM/E2E | Это отложенная часть K, не повтор K1. Сначала characterization порядка относительно raw autosave handler; initial/replay, reconnect после нуля subscribers, поздний getter команд, таймер и flush. При невозможности сохранить порядок — отклонить, не менять бизнес-семантику |
| N7 | Строить начальные фильтры/сортировки/счётчики из согласованного snapshot; подключать дорогие наблюдения неактивных проекций при необходимости | Startup/A→B, первая активация каждой вкладки | Непрерывность изменений во время подключения, текущие фильтры/поиск/emoji/сортировка; не просто F single Edit |
| N8 | Отделить минимальные данные карточки/поиска/отношений от лениво создаваемого редактора и его подписок | Startup; первое редактирование обычной и Completed/Archived задачи | Все задачи остаются в поиске/графе; изменения сохраняются при switch/close; однократная инициализация и disposal |
| N9 | Ограниченный cache runtime/VM пространств: не более двух, отключение UI и активной работы при уходе, reconcile перед возвратом | B→A unchanged и external-change; 20 циклов переключений | Eviction → полный Dispose; dirty state drained перед detach, failure → действующая rollback ветка switch; revision/generation; fallback полный reload |
| N10 | Разметить startup до Init и вынести подтверждённую необязательную работу из критического пути; каждый найденный перенос отдельной дельтой | Startup + обращение к перенесённой функции | Не снимать loading раньше готовности; все переносы сравниваются также по latency первого использования; отсутствие переносимой работы — документированный отрицательный профиль |
| N11 | Отдельный процесс с DOTNET_TieredCompilation=0 против штатной конфигурации, тот же binary | Startup/A→B/B→A плюс 10 минут одинаковых действий после прогрева | Override передаётся только запускаемому приложению через `DesktopAppLaunchOptions.EnvironmentVariables`, тестовый runner работает в штатной конфигурации; не менять установленный runtime; throughput/паузы/CPU и RSS |

N10 имеет ограниченную процедуру: один полный trace от старта процесса, определить стеки/владельцев интервала до Init, проверить каждую ранее отмеченную обязательность; прототипировать независимые задерживаемые конструкторы только если их результат не нужен для Ready. Остальные кандидаты не пропускаются из-за слабого результата N1.

Исторический отдельный N11 off-tiering испытывается даже при слабом ожидании эффекта. TieredPGO и другие новые runtime комбинации не входят: их не предлагали как отдельное подтверждённое направление; это не бесконечный перебор настроек.

Visual planning: интерфейс и расположение элементов прежние. Storyboard: loading → актуальная выбранная задача → доступный редактор → успешное действие/отмена → переключение → актуальная задача. До/после UI video записать на синтетическом наборе без личных данных; на реальном snapshot — только timings/агрегаты и автоматизированные assertions. Если recorder недоступен: записать причину и fallback screenshots+UI automation log; это не video evidence.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Expected visible result | Evidence | AC |
| --- | --- | --- | --- | --- |
| S1 Старт | Fresh process, пространство A | Loading заканчивается с актуальным списком и рабочей карточкой | FlaUI Ready/Action + UI tests | AC2/3 |
| S2 Переключение | A→B→A, быстрый повтор/отмена | Правильное пространство; нет потерь, двойных подписок и зависания | FlaUI + transaction tests | AC3/4 |
| S3 Отложенная работа | Впервые открыть каждую затронутую вкладку, найти и изменить archived/completed задачу | Полнота данных и прежние операции | Headless/FlaUI, first-use timings | AC3/5 |
| S4 Внешнее изменение | Изменить/удалить/добавить JSON в неактивном пространстве, затем вернуться | Новые данные видны; устаревший cache не используется | Storage/watchers + UI | AC4 |
| S5 Ошибка | Read/save/cache-write failure, отмена, corrupt JSON | Прежняя диагностика/recovery, доступность повторной загрузки | Fault injection | AC4 |
| S6 Android | Same-PID resume, recreation, cold process | Актуальные задачи, без лишней полной загрузки/потери правок | Отдельный APK и сценарий на disposable AVD/device | AC6 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition | Empty/error/concurrent |
| --- | --- | --- | --- |
| Uninitialized | Init | Loading → Ready | Empty: готовый пустой список; failure: recoverable error |
| Loading generation g | Switch/cancel | Отмена g, публикация только нового поколения | Старые callbacks не публикуют stale state |
| Snapshot g | File event g+1 | Replay/reconcile, при необходимости restart | Более новые pending events не очищаются |
| Ready active | Deactivate | Drain save, detach UI; cached или disposed | Save failure сохраняет действующий rollback switch |
| Cached | Activate | Проверка изменений → reconcile → attach | Miss/error/evicted: прежний полный load |
| Cached | Third space / pressure / close | Evict → Dispose | Нет timers/autosave/подписок после Dispose |
| Suspended | Resume/recreation | Актуализация по фактическому lifecycle | Same-PID и новый процесс не смешиваются |

### 6.5 Decision Ledger
| Decision | Owner | Chosen | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Объём | agent по запросу | Все N1–N11, включая микропроверенные, но не интегрированные | 0.95 | Пропуск гипотезы | Нет |
| Сохранение алгоритма | agent | Только по correctness и paired evidence | 0.95 | Шум/перенос задержки | Нет |
| Runtime cache | agent | До двух пространств, eviction и memory gate | 0.8 | Удержание памяти | Нет |
| UX/формат | agent | Прежние контракты | 0.99 | Незаметная регрессия | Нет |
| Новая кампания | user | Единое SPEC approval перед EXEC | 1.0 | QUEST gate | Нет: approval этой SPEC, не отдельный design choice |

### 6.6 Runtime / Config / Data Contract Matrix
| Area | Source of truth | Change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Tasks | JSON в Git/task source | Только disposable copies на опытах | Формат/reader tolerance прежние | Inventory/hash + read contract tests |
| Certificate | Не источник истины | Private local cache вне synced tasks | Отсутствует/unknown/corrupt → slow path | Cold/hit/miss/error controls |
| VM state | Storage и текущие правки | Lazy/runtime residency | Save/rollback preserved | Transaction/disposal tests |
| Runtime options | Штатный publish | Только N11 process env | Не сохраняются в user config | Captured env/runtime manifest |
| Android | Текущий lifecycle entrypoint | Только переносимые выигравшие изменения | Desktop claim отдельно | Device/API/ABI/build recorded |

## 7. Бизнес-правила / Алгоритмы
Certificate разрешён только для доказанного валидного post-migration состояния. Completed report мигратора недостаточен: существующие save errors могут поглощаться, UpdatedItems считаться до успешной записи, parse errors пропускаться. Нужен явный успешный outcome либо свежая проверка постусловий. Hash и модели относятся к тем же bytes. Полный inventory покрывает различия status/file readers, hidden/empty/duplicate/repair. Miss выполняет прежний порядок миграций со свежими данными; старые report gates не дают права пропуска. ForceRecheck не отключается ради скорости.

Snapshot владеет неизменяемым содержимым и не отдаёт изменяемые TaskItem/коллекции наружу: mutable consumers получают defensive clones с прежней глубиной изоляции. Live graph не выдаёт внутренние модели. Hash/validity относятся к сохранённым bytes, а не к изменённому объекту потребителя. Обязательный negative test: изменить модель/вложенную коллекцию у одного consumer, затем выполнить failed save; snapshot, второй consumer и validity/hash остаются прежними. Сокращение parse не разрешает устранение обязательных defensive clones.

Сертификат привязан к идентичности source/path и версиям reader/всех migration algorithms; запись атомарная, ошибки cache не блокируют slow path. Watcher events буферизуются даже при отключённой публикации, обрабатываются по поколениям, финальный reconcile остаётся. Cached fast path не пишет task JSON. Arbitrary external editor не разделяет lock приложения: hash-before-save не устраняет TOCTOU; новый код не расширяет существующее окно migration writes и не обещает атомарную внешнюю запись.

## 8. Точки интеграции и триггеры
Init/BuildInitialTaskViewsAsync, GetAll/DeserializeTask, migration completion, live graph activation, watcher raw events, MainWindowViewModel.ConnectCore/disconnect, task-space switch transaction, VM property events/Dispose, editor opening и Android lifecycle. Для каждого прототипа фиксируется diff затронутых entrypoints и порядок вызовов до/после.

## 9. Изменения модели данных / состояния
Task JSON без изменений. Возможные новые внутренние типы: ReadSnapshot с inventory/models/diagnostics/generation; versioned certificate; immutable expression constants; lazy editor state; bounded runtime cache с ownership. Не публиковать внутренние типы как новый внешний API без необходимости. Не удерживать VM статическими selectors.

## 10. Миграция / Rollout / Rollback
Первый запуск N5 без cache всегда slow path. Экспериментальные binaries не заменяют установленную программу. Каждый вариант: manifest базового source, собственный patch, отдельный publish/output. Отрицательный вариант снимается восстановлением только собственных файлов с проверкой hash; не использовать blanket reset/clean и не трогать старый worktree. Откат выбранной комбинации — возврат изменённых алгоритмов к base; certificate игнорируется, task JSON читается прежним способом. На Git rollback/import — miss/reconcile. Продуктовая доставка не входит.

## 11. Тестирование и критерии приёмки
AC1: полный реестр N1–N11 с гипотезой, diff/source/runtime hashes, уровнем проверки, результатом и решением. Блокер/статический вывод не равен успешному испытанию.
AC2: новый baseline на main и пары каждого безопасного интегрированного кандидата; raw rows, статистика и ограничения воспроизводимы.
AC3: сохранены S1–S3; targeted characterization до/после, релевантные UI tests, итоговые полные main+headless без пропущенных классов.
AC4: snapshot/cache/reactivity выдерживают негативную матрицу; исходный набор и старый worktree не изменились; нет callbacks после Dispose.
AC5: комбинация удовлетворяет latency/memory gates ниже; allocations-only выигрыш так и назван. 10× относительно исторического baseline не заявляется без новой сопоставимой серии original→combined.
AC6: Android build и отдельная проверка S6; если нет рабочего disposable device/AVD, фиксируется точная причина и Android остаётся непроверенным. Это не мешает закончить desktop эксперименты, но не разрешает claim ускорения Android.

### Протокол и численные gates
1. Замороженный прежний snapshot 2879 задач/2882 файла из старого `TestResults/loading-e2e/dataset` копируется в новый ignored каталог после hash/inventory проверки. Live personal tasks не используются. Перед каждым опытом одинаковые данные; различия миграций измеряются на отдельных fixtures. Синтетический набор — для adversarial/UI video.
2. Baseline и candidate одной конфигурации Release/runtime/target. Каждая версия публикуется после явного rebuild изменённых проектов в собственный output; source/DLL hashes сохраняются, восстановление timestamp не должно оставить stale incremental binary (такой сбой уже был в K1). Перед E2E один warmup каждого, затем 5 свежих пар с чередованием порядка. Новые процессы, прогретый OS file cache; очистка OS cache не заявляется. N5 hit требует стабильного disposable пути между fresh-process runs и отдельного cache namespace каждой версии; текущий harness с новыми A/B путями на каждый запуск для этого расширяется. Подготовка hit и копирование не входят в таймер; validation содержимого входит.
3. Основная метрика — Ready из таблицы N. Также Ready+Action, первый редактор/вкладка, CPU, peak private/working set; traces/alloc/GC/heartbeat в отдельных instrumented runs. В trace контролировать lost events и включение старта процесса. UIA observer stacks не оптимизировать как product hotspots.
4. Положительный E2E результат: не менее 5% выигрыша и по среднему, и по медиане основной метрики; быстрее минимум 4/5 пар. Если результат неустойчив или пересекает порог, заранее предусмотрено расширение до 10, затем 20 пар; нужны ≥80% положительных пар. При 20 неопределённость закрывается как inconclusive. Показывать paired distribution/range, не выдавать правило за статистическую значимость. Ошибочные/потерянные runs не исключать молча: причина, повтор всей пары.
5. Guardrails: прочие Ready/Ready+Action и first-use не хуже более чем на 5% по среднему и по медиане; превышение хотя бы одного показателя требует расширения серии, затем отказа при сохранении регрессии. Для короткого first-use допуск max(5%,100 ms) учитывает разрешение UI polling; относительные и абсолютные дельты публикуются. Не снимать loading раньше текущего Ready contract. Heartbeat max/p95 публиковать отдельно; заметный рост пауз >100 ms требует проверки и отказа от UX-регрессии.
6. Память: для N1–N8/N10/N11 peak private не выше контроля >10%; для N9 максимум +25% при значимом ускорении возврата, не более двух runtime. После 20 A/B/C циклов live VM/handlers ограничены cache capacity и стабилизируются; weak-reference/disposal checks на evicted objects. GC в диагностике после settle для retained heap допускается, в latency runs принудительный GC запрещён. Не считать один RSS slope доказательством утечки.
7. N11: после warmup одинаковый 10-минутный сценарий поиска/вкладок/правок/переключений, сравнить operations latency/CPU и память. Не принимать startup выигрыш ценой >5% устойчивого проигрыша latency.
8. Все тесты, builds, traces и бенчмарки на машине последовательно. Для каждого N сначала characterization и механизм, затем correctness и E2E. Если безопасный прототип не даёт E2E gain, сохранить evidence и идти дальше, не пропускать остальные N. Воспроизводимое снижение allocations/работы при нейтральном E2E даёт component-candidate; после отдельных проб проверить комбинацию таких кандидатов и winners, указав до запуска ожидаемый совместный механизм. При нескольких overlapping решениях сравнить взаимоисключающие комплекты, не суммировать проценты; полный перебор 2^N не требуется. После финального combine — повтор пар и полные suites; после не связанных с production правок документов suites не перезапускать.

### Acceptance-to-Test Matrix
| AC | Automated tests | Log/visual check | Evidence | Ограничение |
| --- | --- | --- | --- | --- |
| AC1/2 | TaskLoadingPerformanceFlaUiTests + series audit | Baseline/source/data manifest, per-N decision | `TestResults/loading-remaining/<N>/` | PASS: N1–N11 проверены; raw evidence сохранён |
| AC3 S1/S2 | MainScreenLoadingUiTests, SingleViewStartupUiTests, TaskSpaceTransactionTests | Ready/Action, synthetic video | TRX/JSONL/video | Новые UI cases для изменённого поведения |
| AC3 S3 | StartupProjectionAndRelationsTests + Headless/FlaUI first-use cases | Все затронутые вкладки/статусы | TRX/first-use JSONL | Тесты расширяются в EXEC |
| AC4 storage | FileTaskStorageReadContractTests, RecoverableMutation, UnifiedTaskStorageMigrationRegressionTests + injection cases | same-size/mtime, delete/create/rename/Id/duplicate, hidden/empty/corrupt/repaired, Git rollback, save/read/certificate failures | TRX/input hashes | Полные классы, не только happy path |
| AC4 INPC | Новые characterization по K1 контрактам с адаптацией main | Initial, payload, broad INPC, reentrancy, threads, errors, detach/dispose | TRX/handler counters | Старые untracked tests не копировать вслепую |
| AC4/5 runtime | TaskSpaceTransactionTests + cache/lazy tests | Pending writes, invalidation во всех snapshot→publish→OnInited границах; 20 switches/eviction | TRX/retained counters | Профиль отдельный от latency |
| AC5 combine | Full main+headless, итоговый FlaUI | Paired summary + traces/heap/first-use | `combined/`, `validation/` | Нет performance claim до результатов |
| AC6 | Android build + lifecycle scenario | PID/activity/content assertions | APK manifest/log/timings | Отдельная платформа; blocker описать явно |

### Команды
Из корня нового worktree, после подготовки publish и harness; переменные путей устанавливаются на артефакты этой кампании:
```powershell
dotnet build tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Release
pwsh -File scripts/measure-task-loading.ps1 -Dataset $dataset -BaselineExe $baselineExe -CandidateExe $candidateExe -OutputDirectory $freshOutput -Runs 5
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage restore -Project main -ResultsRoot $validation
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage build -Project main -ResultsRoot $validation
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project main -ResultsRoot $validation
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage restore -Project headless -ResultsRoot $validation
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage build -Project headless -ResultsRoot $validation
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project headless -ResultsRoot $validation
```
Отдельные TUnit классы запускать отдельными `--treenode-filter '/*/*/ClassName/*'` и сверять реальные test counts в TRX: OR фильтры ранее теряли классы. Новые harness команды N5/first-use сохраняются вместе с реализацией; нельзя использовать старый fresh-path harness как evidence certificate hit.

## 12. Риски и edge cases
Главные риски: stale snapshot, скрытые migration failures, потеря watcher event, двойные subscriptions, удержание VM, неправильный scheduler/initial delivery, задержка первого editor, сохранение в неправильное пространство, desktop-only оптимизация. Они связаны с AC4/5 и негативной матрицей, не принимаются как разрешение ухудшить корректность.

### Expected User Review Objections
| Likely objection | Why | Mitigation | Status |
| --- | --- | --- | --- |
| Опять проверена только одна идея | Раньше K1 был узким этапом | Реестр N1–N11 и запрет пропускать остальные после первой неудачи | mitigated |
| Быстрее только Init/открытие тормозит | Lazy переносит работу | Ready+Action, первые вкладки/editor и guardrails | mitigated |
| Кэш скрывает внешние изменения | JSON/Git редактируются вне приложения | Full-content inventory, miss/replay/negative controls | mitigated |
| Коэффициенты от старой ветки | Main изменился | Новые base/candidate publish и raw evidence | mitigated |
| Исследование затронет мои задачи | Реальный набор нужен для timing | Frozen copies, private reports, SHA до/после | mitigated |
| Android снова не измерен | Desktop стенд не доказывает resume | AC6 и явный платформенный blocker, без переноса коэффициентов | mitigated |

Rework Prevention Checklist: видимые S1–S6 заданы; AC→evidence задано; решения агента перечислены; возражения покрыты; независимый review выполнен и замечания закрыты; AC описывают выполненный результат; EXEC имеет воспроизводимый путь проверки.

## 13. План выполнения
1. Зафиксировать main/data/runtime; baseline full correctness и E2E; сохранить локальные прошлые research evidence в manifest без правок старого worktree.
2. N1; N4→N5; N2/N3 отдельно и как дельта к выигравшему reader; N6a/N6b/N6c; N7; N8; N9; N10; N11. Зависимости позволяют менять порядок при доказанной технической необходимости, не удалять варианты.
3. Для каждого: characterization → isolated prototype → targeted/UI → механизм/paired E2E → winner/component-candidate/rejected/inconclusive/blocked. Обновлять журнал сразу.
4. Совместимые winners и обоснованные component-candidates → комбинации → повтор полной серии, first-use/retained memory и final suites; отдельные Android проверки переносимых winners. Итоговая выбранная комбинация обязана пройти E2E gates независимо от статуса отдельных частей.
5. Независимый post-EXEC review, исправления и повтор затронутых проверок. Итоговая матрица с цифрами и границами; локальное решение для последующего внедрения/публикации.

## 14. Открытые вопросы
Блокирующих пользовательских design choices нет. Наличие рабочего Android стенда и фактическая стоимость N10 устанавливаются исполнением, не угадываются. Требуется предусмотренное QUEST подтверждение этой программы перед изменением product/test/harness source.

## 15. Соответствие профилю
dotnet-desktop-client: UI thread affinity, commands/disposal, storage/recovery и UI тесты обязательны. Performance: baseline/candidate и сырой evidence, throughput/latency/память отдельно, никакого суммирования процентов. testing-dotnet: TUnit фактическое обнаружение, targeted и финальные полные suites. QUEST: expanded SPEC, approval, independent review large multi-module. Publication не входит; runtime overrides локальные процессу.

## 16. Таблица изменений файлов
| Файл/группа | Изменения | Причина |
| --- | --- | --- |
| Эта SPEC | Программа, review, далее результаты | Единственная текущая правка |
| src/Unlimotion.FileStorage/* | Snapshot/reader/buffers/certificate прототипы | N2–N5 |
| src/Unlimotion/UnifiedTaskStorage.cs и существующие миграторы | Orchestration и validity outcomes | N4/5 |
| src/Unlimotion.ViewModel/TaskItemViewModel.cs, MainWindowViewModel.cs | Expressions/INPC/projections/lazy state | N1/6/7/8 |
| src/Unlimotion/Services/TaskSpaceCoordinator.cs, TaskSourceManager.cs, TaskSourceRuntime.cs; App.axaml.cs | Bounded cache и подтверждённый preInit hotspot | N9/10; дополнительные владельцы только по trace |
| src/Unlimotion.Test/*, tests/Unlimotion.UiTests.*/* | Characterization, first-use, races/eviction | Проверка контрактов |
| scripts/measure-task-loading.ps1 и новый campaign harness | Stable-path/hit, per-N runs, audit | Воспроизводимость |
| TestResults/loading-remaining/* | Ignored binary/patch/raw/trace/video artifacts | Локальное evidence без личных task payload |

## 17. Таблица соответствий (было → стало)
| Область | Было | Стало |
| --- | --- | --- |
| Выбор оптимизаций | Профиль и частичные пробы | Каждая оставшаяся гипотеза имеет проверенный статус |
| Производительность | Исторический A+E2 результат | Current-main paired evidence, отдельно комбинация |
| Поведение | JSON/внешние изменения/UI/lifecycle | Те же контракты, улучшение только при доказательстве |

## 18. Альтернативы и компромиссы
Ограничиться N1 проще и безопаснее, но не выполняет запрос «все». Внедрить всё одновременно быстрее по объёму кодирования, но невозможно отделить эффект/регрессии; отклонено. Массовый параллелизм, смена serializer, GC mode и исключение archived tasks не обоснованы профилем или нарушают контракт; это исключённые направления, а не выдуманные отрицательные бенчмарки. Выбрана последовательная кампания с сохранением отрицательных результатов и отдельным combine.

## 19. Результат quality gate и review
### SPEC Linter Result
Self-check всех 20 пунктов и повтор затронутых пунктов после независимого review. Итог: ГОТОВО к подтверждению SPEC; это не завершение EXEC.
| № | Статус | Проверяемое основание |
| --- | --- | --- |
| 1 | PASS | §1, S1–S6 |
| 2 | PASS | §2, исходники и локальные research artifacts |
| 3 | PASS | §3 |
| 4 | PASS | §4 |
| 5 | PASS | §5 |
| 6 | PASS | §6.1,16 |
| 7 | PASS | §8 |
| 8 | PASS | §6.2,7 |
| 9 | PASS | §6.4,7,12 |
| 10 | PASS | §11 paired protocol/guardrails |
| 11 | PASS | §6.6,9 |
| 12 | PASS | §7,10 |
| 13 | PASS | §10 owned patches |
| 14 | PASS | AC1–AC6 |
| 15 | PASS | Acceptance-to-Test Matrix |
| 16 | PASS | §11 commands/stop rules |
| 17 | PASS | §13 |
| 18 | PASS | §6.5,14 |
| 19 | PASS | Large expanded, §0 |
| 20 | PASS | §15, UI/full tests и evidence |

### SPEC Rubric Result
| Критерий | Балл 0/2/5 | Основание |
| --- | ---: | --- |
| Ясность цели и границ | 5 | Конечный реестр; publication вне scope |
| Понимание AS-IS | 5 | Merge base, код, уровни прежнего evidence |
| Конкретность дизайна | 2 | N10 требует нового trace; scope эксперимента задан, конечный алгоритм неизвестен |
| Безопасность | 5 | Snapshot/replay/rollback, copies и отказ от unsafe wins |
| Тестируемость | 5 | AC mapping, сценарии, протокол и пороги |
| Автономность | 5 | Решения по каждому N делаются по общим gates |
Итого 27/30; это исследовательская SPEC, а не утверждение готовности алгоритмов.

### Role-Based Review Result
| Role | Applicability | Verdict | Обоснование |
| --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | PASS self-review | Все направления, задача не урезана; S1–S6 |
| UX / designer | applicable | PASS self-review | Прежний UI, first-use guardrails, synthetic video |
| Tester / validation | applicable | PASS self-review | AC и negative controls, фактические counts |
| Developer / architect | applicable | PASS self-review | Раздельные варианты, snapshot ownership и cache bounds |
| Delivery / operations / security | applicable | PASS self-review | Изолированный runtime/data, нет публикации |

### Post-SPEC Review
- Scope/Evidence: canonical template; quest-mode, linter/rubric/review-loops и профиль; история final responses; три предыдущие SPEC и REPORT; текущие UnifiedTaskStorage/FileTaskStorage/VM paths, benchmark/CI scripts; clean new worktree.
- Contract pass: все предложенные неинтегрированные идеи сопоставлены N1–N11; K1 и F/G не выданы за новые пробы; новая SPEC не выдана за EXEC.
- Adversarial pass: path-changing harness не проверяет N5 hit — предусмотрено расширение; migration completed не доказывает valid data — нужны postconditions; lazy latency учтена; external writer TOCTOU явно ограничен.
- Findings/fix and re-review: предварительно устранены пропуск off-tiering, смешение parsed-model/VM cache и преувеличение evidence F/G.
- Independent review: `/root/remaining_spec_review`, роль independent-reviewer; полный scope/contract/adversarial/role-based pass и read-back исправлений. Прочитаны SPEC, next-research/REPORT, TaskItemViewModel/FileTaskStorage, FlaUI performance test и measurement script, central owners. Effective child sandbox `danger-full-access`, approval `never`: технической read-only изоляции нет; reviewer выполнял только чтение и status, без тестов/сборок/изменений.
- Fix and re-review: MEDIUM M1 — добавлен пропущенный sharing PlannedBeginDateTime/IsCanBeCompleted (N6c); MEDIUM M2 — component-candidates и обязательный combine позволяют проверить совместную экономию allocations; MEDIUM M3 — закреплены snapshot ownership/defensive clones и failed-save negative test; LOW — однозначный OR порог регрессии. Все четыре исправления независимо проверены повторным чтением, статус fixed. Незакрытых BLOCKER/HIGH/MEDIUM/LOW нет. Повторены linter 1/6/8/10/11/14/15/17, rubric и проверки contract/adversarial; новые находки отсутствуют, потому что каждый выявленный counterexample теперь имеет правило и тест/evidence.
- Role-Based: независимый reviewer подтвердил domain/UX/validation/architect/operations passes; результаты self-review выше поддержаны независимым pass.
- Stop decision: PASS для SPEC. Manual challenge: непроверенные прототипы не объявлены работающими; Android и combined gain требуют исполнения. Рабочее дерево содержит только эту новую SPEC; product/test/harness не изменены.

### Post-EXEC Review
- Независимый reviewer `/root/remaining_spec_review` проверил замороженный product/test/harness diff, SPEC и raw JSONL финальной серии. Выполнены scope/contract/adversarial/role passes; BLOCKER/HIGH/MEDIUM/LOW findings нет, verdict PASS.
- N4: live graph включается только после raw status migration; reverse/availability работают с defensive clones, успешные saves обновляют graph, заключительный reconcile сохранён.
- N6a: сохранены initial delivery, broad-property handling, equal-value suppression, cleanup при initial subscriber exception и синхронизированный Dispose; прежние `ObserveOn` на UI-путях не удалены.
- N5/N9 hooks отсутствуют в product diff. Reviewer независимо пересчитал пять финальных пар и подтвердил медианы Ready; peak-private guard не нарушен.
- Остаточные ограничения: Android wake/resume не измерен без устройства/AVD; тесты не являются доказательством всех возможных межпоточных interleavings INPC; отрицательный N5 не опровергает любую будущую архитектуру сертификата.
- Follow-up GitHub review после открытия PR выявил stale duplicate-bearing graph между миграциями и загрязнение test runner в N11 harness; исправления добавлены в `26d09e93` и `f1884c25`, обе дискуссии закрыты после targeted checks.
- Повторный GitHub review выявил, что raw watcher change мог оставить live snapshot читаемым до первой миграции. Raw task events теперь сразу инвалидируют snapshot и legacy cache, invalidated graph не выдаётся обычным `Load`/`GetAll`, pending changes дренируются до reverse-link migration и между миграциями, а финальный reconcile отдельно сохраняет последнюю опубликованную проекцию для unreadable files.
- Следующий review уточнил alias-file контракт: при raw invalidation сохраняется source-file mapping, необходимый delayed delete callback для публикации domain task ID; object cache при этом очищается. Полное очищение mapping остаётся для transaction recovery.
- Финальный alias follow-up закрыл смену domain ID внутри поддерживаемого aliased source: delayed callback повторно разрешает source mapping после precise refresh, публикует удаление старого ID перед сохранением нового и не создаёт файл по старому ID. Для delete старый domain ID сохраняется отдельно до delayed publication, поэтому промежуточный command refresh не превращает его в имя source-файла. Детерминированные storage-тесты и headless UI-сценарий подтверждают обе границы.
- Перед финальным merge gate ветка rebased без конфликтов на `f58bb6fc`. После watcher interleaving и UI projection reentrancy fixes авторитетный benchmark актуального main против точного candidate `89ff952c` расширен до 10 пар: startup Ready median 12,376→9,856 s, median paired delta −19,99% (10/10); Space B 6,790→5,330 s, −22,35% (10/10); возврат Space A 6,060→4,318 s, −30,79% (10/10). Полный маршрут быстрее 10/10, median paired delta −21,95%; Ready+Action до фактического появления relation editor: −19,35% (10/10) / −13,87% (10/10) / −16,74% (9/10). Чистое Action проходит guardrail: startup median +35 ms при short-action допуске 100 ms; Space B −44 ms; Space A +204 ms и +4,56% при 5% пороге. Peak private memory ниже на 6,49–15,18% median paired. Dataset fingerprint `49906522...567135A`; exe SHA-256 main/candidate `D80E10E3...17FE1D6`/`B832003B...6832DF4`. Артефакты: `final-head-89ff952c-vs-main*/measurements.jsonl`.
- Raw watcher follow-up сохраняет немедленную invalidation, но precise per-file events применяет к последнему live graph под directory lock без полного обхода. Global watcher invalidation, recovery, поколение, изменившееся во время синхронизации, и duplicate-ID остаются fail-safe причинами полного rescan. Изолированная серия rebased pre-change→optimized завершила 5/5 пар: полный маршрут −8,36% median paired (4/5); startup −7,84%, Space B +0,86% внутри guardrail, Space A −12,58%; peak private memory не вырос.
- Исправлен race FlaUI action: harness больше не закрывает уже открытую details pane при медленной доставке title binding. После failed diagnostic warm-up выполнены отдельные успешные серии `final-harness-smoke2` 4/4 процессов, final main comparison 12/12 и raw-watcher contribution 12/12; failed серия не смешивалась с измерениями.
- Фазовая диагностика выявила дефект старой метрики: `Ready+Action` включала закрытие relation editor, хотя первая полезная операция завершалась при его открытии. Harness теперь записывает `cardOpenedMs`, `relationEditorOpenedMs` и `actionCleanupCompleteMs`, продолжает проверять cleanup, но merge guard оценивает фактическую first-use готовность. Старая расширенная 10-парная серия сохранена как диагностика и не смешивается с исправленной финальной серией.
- Повторный review текущего head выявил remove-before-lock окно: delayed `OnUpdated` мог удалить pending entry, пока более ранняя команда ожидала directory lock, после чего команда помечала stale graph актуальным. В `e57a85e5` entry остаётся pending до locked refresh; детерминированный queued-command regression test фиксирует расписание. Targeted validation: FileStorageTaskStatus 27/27, migration 5/5, read contract 9/9, recoverable mutation 14/14.
- Две полные CI попытки `411fbd7f` упали в разных emoji-filter UI tests с одной причиной: nested task projection update модифицировал Avalonia bound collection внутри незавершённого `CollectionChanged`. `89ff952c` ставит all-tasks projection delivery в очередь UI scheduler перед `Bind`. Весь класс 18/18 и детерминированный reentrant сценарий 20/20; post-fix 10-парный benchmark проходит latency/memory gates.
- Финальная локальная проверка после interleaving self-review: `FileStorageTaskStatusTests` 27/27, `UnifiedTaskStorageMigrationRegressionTests` 5/5, `FileTaskStorageReadContractTests` 9/9, full main 1037/1037 и headless UI 40/40, без failures/skip. Android arm64 Debug build успешен с существующими package/native warnings; wake/resume по-прежнему не измерен на устройстве/AVD.

## Approval
Получено «Спеку подтверждаю» на программу N1–N11. Это не разрешение commit/push/PR/публикации.

## 20. Журнал действий агента
| Фаза | Тип намерения/сценария | Уверенность | Каких данных не хватает | Следующее действие | Нужна передача человеку | Фактическое обращение / решение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Восстановление истории | 0.95 | Новых paired результатов main | Сопоставить варианты | Нет | Пользователь запросил все оставшиеся | Micro и интеграция разделены | История, старые SPEC/REPORT |
| SPEC | Изоляция | 1.0 | Нет | Работа в новом дереве | Нет | Запрос новой ветки/worktree ранее | Base после merge #297, старое дерево сохранено | perf/task-loading-next-pass |
| SPEC | Дизайн всей кампании | 0.9 | Независимый review | Review и устранение находок | После review: единое approval | Ещё не запрошено | Product/test source до approval не меняется | Эта SPEC |
| SPEC | Независимый review и rework gate | 0.95 | Результатов EXEC | Единое подтверждение программы | Да, предусмотренный QUEST переход | В финальном ответе запрашивается «Спеку подтверждаю»; ответ ещё не получен | Закрыты 3 MEDIUM и 1 LOW, final reviewer PASS; только SPEC изменена | Эта SPEC, reviewer remaining_spec_review |
| EXEC | Approval и current-main baseline | 0.95 | Вклад вариантов | N1 selectors | Нет | Пользователь: «Спеку подтверждаю» | Baseline publish зафиксирован; 5 self-pairs прошли. Ready общий диапазон: startup 10,767–11,627 s, B 6,616–7,771 s, A 5,740–8,597 s; возврат A содержит большой выброс | `TestResults/loading-remaining/baseline-app`, `baseline-noise` |
| EXEC | N1 typed selector expressions | 0.9 | Валидная полная E2E серия | Повторить серию целиком | Нет | Не применимо | Rebuild успешен; targeted 24+12+9+1 tests прошли. Первая E2E серия инвалидна: baseline-2 дошёл до startup Ready 10,783 s, затем UI automation timeout открытия карточки; сохранён, не исключён молча | TaskItemViewModel.cs; `n1-app`, `n1-e2e` |
| EXEC | Стабилизация E2E harness | 0.98 | Нет | Использовать для следующих серий | Нет | Не применимо | Найдены две причины: закрытие карточки после клика и зависимость ComboBox item от clickable point. Порядок исправлен, строка повторно находится до подтверждения, пространство выбирается UIA SelectionItem. Финальный self-control 12/12 процессов, 36/36 загрузок+Action | TaskLoadingPerformanceFlaUiTests.cs; `harness-fix3-baseline` |
| EXEC | N1 итог | 0.95 | Allocation trace | Сохранить как startup winner, проверить в combine | Нет | Не применимо | 5 пар: startup Ready 10,654→10,043 s mean (−5,74%), 10,666→10,131 s median (−5,02%), быстрее 5/5. B mean −1,79%, median −3,25%; A mean +2,84%, median +3,18% (guardrail не нарушен). Action не регрессировал критично | `n1-e2e-final` |
| EXEC | N2 single reader worker | 0.95 | Нет | Отклонить и вернуть только N2 diff | Нет | Не применимо | 30 storage tests прошли. N1→N1+N2: startup mean −0,42%, median −0,37%, быстрее 2/5; B mixed 3/5; A mean −5,48%, median −3,33%, 5/5. Нет полного порога; вариант буферизовал выдачу до конца чтения. Product diff N2 удалён | FileTaskStorage.cs restored; `n2-app`, `n2-e2e-final` |
| EXEC | N11 tiered compilation off | 0.99 | Нет | Отклонить | Нет | Не применимо | Первичная серия признана недействительной: `DOTNET_TieredCompilation=0` наследовал и тестовый runner. Исправленный harness передал override только дочернему приложению. На том же selected binary завершены 2 пары; candidate startup был 18,316/17,238 s против baseline 13,499/13,627 s. В candidate-3 startup завершился за 17,464 s, затем UI action в Space B упал по timeout открытия карточки. По correctness stop rule N11 отклонён, 10-минутный throughput и добор пяти пар не выполнялись. | measure-task-loading.ps1; TaskLoadingPerformanceFlaUiTests.cs; `n11-tiered-off-isolated-e2e` |
| EXEC | N4 единый live graph snapshot | 0.95 | Расширенная adversarial/full validation | Сохранить winner, затем combine | Нет | Не применимо | Live graph включён после raw status migration; reverse/availability читают clones, saves обновляют graph, final reconcile сохранён. 25 targeted tests green. N1→N1+N4: startup −5,92% mean/−7,49% median, 4/5; B −9,24%/−9,34%, 4/5; A −9,05%/−6,86%, 5/5; Action −5,5–7,53% | UnifiedTaskStorage.cs; `n4-app`, `n4-e2e-final` |
| EXEC | N3 pooled JSON buffers | 0.95 | Полный contract/adversarial pass | Оставить только как component-candidate | Нет | Не применимо | 21 reader/storage test green. N4→N4+N3: startup mean +1,37%, median +0,19%; B +0,32%/+0,02%; A +4,21%/+1,16%, значимого latency win нет. Отдельный probe на 2 882 файлах: allocations 36 571 672→27 178 664 bytes (−25,69%), median parse 361,4→368,7 ms (≈+2%). Буферы полезны по allocation, но сами по себе загрузку не ускорили | JsonRepairingReader.cs; `n3-app`, `n3-e2e-final`, `n3-probe` |
| EXEC | N6a direct INPC adapter | 0.97 | Characterization reentrancy/broad/dispose | Сохранить winner | Нет | Не применимо | 46 targeted tests green. N4+N3+N1→N4+N3+N6a: startup −9,43% mean/−6,72% median, 5/5; B −22,55%/−23,64%, 5/5; A −26,80%/−25,98%, 5/5. Action также быстрее на 8,52–17,13% mean. Прямой адаптер заменил дорогие typed expression chains на подтверждённых путях | TaskItemViewModel.cs; `n6a-app`, `n6a-e2e-final` |
| EXEC | N6b CurrentThread transport | 0.98 | Нет | Отклонить и удалить N6b diff | Нет | Не применимо | 46 targeted tests green. После пограничных первых 5 пар выполнены 10 свежих пар: startup хуже 7,29% mean/11,92% median, быстрее 2/10; B хуже 7,39%/6,31%, 2/10; A хуже 2,74%/3,17%, 3/10. `ObserveOn(CurrentThreadScheduler)` удалён | `n6b-e2e-final`, `n6b-e2e-10pairs` |
| EXEC | N6c shared Replay observations | 0.98 | Нет | Отклонить и удалить N6c diff | Нет | Не применимо | 46 targeted tests green. После отдельного исправления harness для transient `NoClickablePointException` чистая серия 5 пар: startup −1,23% mean/−0,24% median, 4/5; B +0,73%/+0,86%, 2/5; A +0,03%/+2,82%, 3/5. Порог не пройден; shared connections удалены, N6a сохранён | TaskItemViewModel.cs; TaskLoadingPerformanceFlaUiTests.cs; `n6c-e2e-retry1` |
| EXEC | N9 bounded local runtime cache | 0.99 | Нет | Отклонить и удалить N9 diff | Нет | Не применимо | Первичный прототип ускорял возврат A примерно в 2,84×, но независимый review выявил недостаточную provenance cached runtime. После обязательного full-content подтверждения safe hit требовал второго полного прохода; исправленная candidate-сборка несколько минут держала высокий CPU и воспроизвела исходную проблему. Серия `final-provenance-e2e` показала отсутствие устойчивого выигрыша (startup −5,37%, B −1,34%, A +9,66%), а `final-verified5-e2e` остановлена на многоминутной верификации. Небезопасный быстрый cache не принимается; весь N9 product/test diff удалён | `n9-e2e-final` (отозванный unsafe result), `final-provenance-e2e`, `final-verified5-e2e` |
| EXEC | N7 SortAndBind initial projection | 0.98 | Нет | Отклонить и удалить N7 diff | Нет | Не применимо | Существующее lazy подключение неактивных вкладок подтверждено как уже имеющееся в main. Для обязательной AllTasks projection заменён устаревший Sort+Bind на SortAndBind; 7 projection tests green. Чистые 5 пар: startup хуже 1,99% mean (median лучше 1,51%), 2/5; B лучше 2,23%/2,85%, 4/5; A хуже 0,35%/0,86%, 2/5. Порог не пройден, product diff удалён. Две прежние частичные серии сохранены как invalid из-за harness action; исправлена инвертированная семантика Details toggle, retry2 12/12 green | MainWindowViewModel.cs restored; TaskLoadingPerformanceFlaUiTests.cs; `n7-e2e-retry2` |
| EXEC | N8 lazy editor subscriptions | 0.99 | Нет | Отклонить на correctness gate и удалить N8 diff | Нет | Не применимо | Completion criteria, planning и repeater subscriptions подключались при первом открытии карточки; first-use UI test green. До latency run 3/9 start-date cases перестали удалять repeater при очистке начала и 1/4 marker cases потерял уведомления. Это публичная VM-семантика вне конкретной карточки, поэтому тесты не переписаны, вариант полностью удалён | TaskItemViewModel.cs/MainWindowViewModel.cs restored; `n8-start-date`, `n8-repeater-marker`, `n8-editor-first-use` |
| EXEC | N5 full-content validation certificate | 0.99 | Нет | Отклонить и удалить N5 diff | Нет | Не применимо | После feasibility probe реализован path/version/full-inventory-SHA-bound сертификат вне task directory и fresh-process hit, пропускающий миграционные проверки с обязательным финальным fingerprint fallback. На трёх завершённых независимых парах второй процесс не ускорил startup: +16,50%, +1,75%, +0,02%; Space A колебался около нуля (−4,01%, +1,39%, −0,82%), Space B был нестабилен. Одна третья baseline-серия упала на UI action после записанного startup и не подменяет завершённые пары. Сложность и TOCTOU-риск не оправданы измеримым эффектом; product/harness N5 diff удалён | `n5-probe/results-reused-buffer.txt`, `n5-integrated/pilot`, `n5-integrated/series3` |
| EXEC | N10 pre-Init trace | 0.98 | Нет | Отклонить: переносимой работы нет | Нет | Не применимо | 18,8 MB sampled-thread-time trace снят на реальном запуске (startup Ready 8,071 s). `App.InitializeRuntime` ≈0,52% inclusive samples; горячие product owners — file read/JSON, TaskItemViewModel.Init, Reactive/DynamicData и XAML. Scheduler уже lazy. Независимой работы до Ready, способной пройти 5%, не найдено; Ready не сдвигался | `n10-trace-sampled/startup.nettrace`, measurement.jsonl |
| EXEC | N6a contract hardening | 0.99 | Нет | Сохранить | Нет | Не применимо | Добавлен per-subscription DistinctUntilChanged, чтобы broad/equal INPC не запускал повторный autosave. Characterization фиксирует broad/equal, initial subscriber exception и полное снятие handlers при Dispose; свежий класс 15/15 green | TaskItemViewModel.cs; TaskItemViewModelStorageUpdateTests.cs; `validation-final6-20260916/targeted-inpc` |
| EXEC | Финальная комбинация N4+N6a | 0.99 | Android lifecycle на устройстве | Выбрать для внедрения | Нет | Не применимо | Чистая финальная серия baseline main→candidate, 5 пар/10 успешных процессов/15 сценариев: startup median 11,485→9,268 s, median paired delta −23,34%, 5/5; B 6,250→4,644 s, −24,86%, 5/5; возврат A 5,613→3,731 s, −32,55%, 5/5. Ready+Action median paired delta: −19,42%, −14,00%, −20,71%. Dataset и DLL hashes записаны; N3/N5/N9 и остальные отклонённые product diffs удалены. Ускорение на порядок не достигнуто и не заявляется | `selected-n4-n6a-app`, `selected-n4-n6a-e2e/measurements.jsonl` |
| EXEC | Итоговая валидация выбранного решения | 0.99 | Только Android lifecycle на реальном устройстве/AVD | Завершить EXEC; оставить изменения незакоммиченными | Нет | Не применимо | После заморозки diff: targeted INPC 15/15, status migration 5/5, unified migration regression 4/4; full main 1028/1028 и headless 40/40, без failures/skip; Android `net10.0-android` Debug arm64 build успешен с существующими package/native warnings. `adb devices` пуст, поэтому wake/resume timing не проверен. Независимый post-EXEC review PASS без findings | `validation-final6-20260916`, Android build output, `selected-n4-n6a-e2e` |
| PR REVIEW | Raw watcher consistency | 0.99 | Новый full CI | Закрыть P1 и повторить review | Нет | Codex review P1 | Добавлена invalidation семантика для `Load`/`GetAll`, refresh до первой и между миграциями, сохранение last-published projection для corrupt-file reconcile и alias mapping для delayed delete. Targeted: FileStorageTaskStatus 24/24, migration 5/5, read contract 9/9 | `review-fixes-alias`, `review-fixes-migration-p1`, `review-fixes-read-contract` |
| PR FINAL | Rebase и точный final-head benchmark | 0.99 | Только CI нового remote head | Commit/push и проверить CI | Нет | Пользователь запросил rebase и три merge gate | Rebase на `f58bb6fc`; exact `89ff952c` main→candidate, 10 пар: Ready −19,99%/−22,35%/−30,79%, полный маршрут −21,95%, 10/10. После финального projection follow-up exact `7f8fd29b`, 5 свежих пар на правом экране: Ready −16,42% (4/5) / −24,04% (5/5) / −32,85% (5/5), полный маршрут −21,74% (5/5), mean −21,43%. Startup Action +42 ms median/+47 ms mean проходит 100 ms guardrail; остальные Action нейтральны/быстрее. Peak private median ниже на 9,86–15,10% | `final-head-89ff952c-vs-main*/measurements.jsonl`, `final-head-7f8fd29b-vs-main-right2/measurements.jsonl` |
| PR FINAL | Precise raw watcher refresh | 0.99 | Нет | Сохранить | Нет | Пользователь явно запросил оптимизацию raw watcher | Precise события применяются инкрементально; unknown/global/duplicate остаются full-rescan. Изолированный вклад: полный маршрут −8,36%, startup −7,84%, B +0,86%, A −12,58%; 12/12 процессов | `final-raw-watcher-contribution/measurements.jsonl`, FileStorageTaskStatusTests 28/28 |
| PR REVIEW | Pending entry ownership | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P1 на `78d5dcec` | Устранено remove-before-lock окно между delayed callback и queued command; pending batch теперь дренируется только под directory lock. Детерминированный interleaving test + exact post-fix benchmark green | `e57a85e5`, `final-head-e57a85e5-vs-main/measurements.jsonl` |
| PR REVIEW | Atomic pending publication | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P1 на `5748e9cc` | Pending entry и live-graph invalidation generation публикуются под одним `_liveGraphSync`, поэтому command видит либо precise work, либо новую generation. Новый детерминированный lock-interleaving test подтверждает, что stale acceptance блокируется до invalidation и затем отклоняется. Targeted: 28/28 + 5/5 + 9/9 + 14/14 | Final follow-up после `5748e9cc` |
| PR REVIEW | Aliased source identity lifecycle | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex P1 на `d317b0e3` и `55a0cf68` | Raw event сохраняет старый domain ID до delayed publication, включая intervening command refresh/delete; после precise refresh callback повторно разрешает актуальный mapping, загружает фактический alias source и при смене ID публикует ordered removal old ID + save new ID. Storage regression 29/29, новый headless UI-сценарий green, полный headless 41/41 | Final follow-up после `55a0cf68` |
| CI FIX | Reentrant UI projection | 0.99 | Финальный remote CI | Сохранить fix | Нет | Две CI попытки упали на одном Avalonia/DynamicData nested-update механизме | All-tasks projection delivery проходит через current-thread trampoline: initial projection остаётся синхронной, а nested `CollectionChanged` ставится в очередь. Поиск держит восстановление выбора pending до появления replacement wrapper. Toolbar UI 18/18, tree commands 45/45, reentrant cases 20/20, exact 10-pair benchmark предыдущего эквивалентного product head green | `89ff952c` + final follow-up, `final-head-89ff952c-vs-main*` |
| PR FINAL | Harness и regression gates | 0.99 | Новый remote CI | Commit/push | Нет | Обязательный pre-merge пункт | Устранено закрытие открывшейся pane при задержке binding; smoke 4/4, две финальные серии по 12/12. После trampoline/search-selection follow-up локально: main 1038/1038 за 25m54s, headless 40/40, toolbar UI 18/18, tree commands 45/45 | `final-harness-smoke2`, `TestResults/pr300-final-local/main`, локальные test reports |
| PR REVIEW | Stable migration source generation | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P1 на `e9769b55` | Reverse-link и availability migrations выполняются под generation guard. Raw edit до записи прерывает попытку, pending burst требует 200 мс непрерывной тишины, следующая из максимум пяти попыток перечитывает граф под directory lock; отчёт не остаётся опубликованным при поздней invalidation. Задержка применяется только после invalidation. Собственные watcher echoes принимаются только при точном SHA-256 совпадении подготовленного содержимого. Детерминированная external-edit regression и реальные headless watcher-сценарии green | Финальный follow-up после `e9769b55`; UnifiedTaskStorageMigrationRegressionTests 6/6; FileStorageTaskStatusTests 32/32; Debug headless 41/41 |
| PR REVIEW | Startup watcher identity retirement | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P2 на `e9769b55` | После startup reconcile удаляются только delayed identity entries, чьё поколение было потреблено отключённым watcher. Более новые события сохраняются. Регрессия old→new startup edit, затем new→newer edit подтверждает ordered removal/save без повторного использования старого ID | Финальный follow-up после `e9769b55`; `ConsumedStartupAliasIdentity_IsNotReusedByLaterUpdate` |
| PR REVIEW | Confirmed own-write lifetime | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P2 на `115a4c17` | SHA-256 confirmation теперь ограничен 5-секундным echo window и немедленно удаляется при первом raw-событии с отличающимся содержимым. Поэтому external B после own A завершает старое подтверждение, а последующий checkout к точным байтам A снова инвалидирует graph. Детерминированная регрессия проверяет всю последовательность | Финальный follow-up после `115a4c17`; `ExternalChange_ExpiresConfirmedWriteBeforeContentReturnsToSameBytes`; FileStorageTaskStatusTests 32/32 |
| PR REVIEW | Explicit own-publication generation | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P1 на `ca7c4615` | `PublishLiveFileChange` возвращает generation только когда сама публикация выполнила invalidation. Guard больше не выводит ownership из разницы счётчика `+1`, поэтому внешнее raw-событие между capture и публикацией прерывает миграцию и не позволяет принять stale materialized graph. Детерминированная регрессия вводит внешний edit точно в это окно и подтверждает сохранность внешнего содержимого | `GuardedSave_RejectsExternalInvalidationDuringLiveGraphPublication`; FileStorageTaskStatusTests 34/34; UnifiedTaskStorageMigrationRegressionTests 6/6; Debug headless 41/41 |
| PR REVIEW | Disabled-watcher hydration cutoff | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P2 на `ca7c4615` | Cutoff поколений отключённого watcher снимается после пакетного заполнения стартового cache и непосредственно перед re-enable. Поэтому raw alias events, пришедшие во время UI `Task.Yield`, после reconcile удаляются из identity queue и не переиспользуют старый domain ID при следующем edit. Регрессия останавливает `Init` после первого batch из 64 задач, вводит `old→new`, затем проверяет корректный `new→newer` | `DisabledWatcherIdentityRaisedDuringCacheHydration_IsDiscarded`; FileStorageTaskStatusTests 34/34; Debug headless 41/41 |
| PR REVIEW | Atomic watcher enable cutoff | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P2 на `55a7815e` | `FileDbWatcher` регистрирует raw event, проверяет enabled-state и выполняет cutoff callback со сменой state под одним lock. Событие на границе теперь либо попадает в disabled cutoff без delayed callback, либо получает generation после cutoff и обязательный delayed callback. Детерминированный тест удерживает enable barrier, запускает конкурирующий alias event и подтверждает, что он блокируется до включения, затем очищает identity через callback | `WatcherEnableBarrier_ClassifiesConcurrentAliasEventAsEnabled`; FileStorageTaskStatusTests 35/35; UnifiedTaskStorageMigrationRegressionTests 6/6; Debug headless 41/41 |
| PR REVIEW | Guarded atomic-write rollback | 0.99 | Финальный remote CI/re-review | Сохранить fix | Нет | Codex review P1 на `df415dbc` | Guarded migration write удерживает displaced backup до всех generation checks. При invalidation текущий target атомарно переносится в quarantine: если SHA-256 совпадает с миграционными байтами, восстанавливаются точные displaced bytes; если target уже содержит более поздний внешний edit, он возвращается без перезаписи. Конкурентно воссозданный target всегда выигрывает, а невозможность безопасного rollback останавливает миграцию вместо retry по потерянным данным | `GuardedSave_RestoresExternalEditDisplacedDuringAtomicReplace` + `GuardedSave_RejectsExternalInvalidationDuringLiveGraphPublication`; FileStorageTaskStatusTests 36/36; UnifiedTaskStorageMigrationRegressionTests 6/6; Debug headless 41/41 |
