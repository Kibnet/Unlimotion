# Восстановить смену статуса через загруженный кеш

> Переработанная спецификация. Прежний вариант с ускорением полных чтений отозван после замечания пользователя и не входит в план реализации.

## 0. Метаданные
- Bugfix; medium, multi-module риск согласованности данных.
- Checkout: b577/Unlimotion, detached HEAD f39b3245. Production tasks и другой checkout daily-feed не изменять.
- Профили: dotnet-desktop-client, ui-automation-testing; performance-optimization для измерений.
- Stack: central AGENTS → routing-matrix/core baselines → QUEST → testing-dotnet/testing-baseline → profiles → spec-linter/spec-rubric/review-loops → локальный AGENTS.override.md.
- Canonical template: C:/Users/Kibnet/.codex/agents/templates/specs/_template.md.
- Runtime: Codex Desktop, Windows/PowerShell, SDK 10.0.400, Avalonia 12.0.3, TUnit 1.44.0. Reviewer sandbox не имеет технической read-only изоляции.
- Фаза EXEC. Пользователь подтвердил спецификацию точной фразой «Спеку подтверждаю» 2026-09-01. Разрешены изменения исходников и тестов в утверждённых границах; Git delivery/release по-прежнему не разрешены.

## 1. Overview / Цель
В загруженном локальном пространстве выбор доступного статуса сохраняет результат и обновляет UI без полного чтения задач с диска. Проверки используют уже загруженный доменный кеш, поддерживаемый подпиской на изменения файлов.

Outcome contract: сохранить правила, историю, обработку ошибок и подтверждение результата; убрать три полных чтения из обычной локальной операции; подтвердить результат тестами и измерением на копии рабочего объёма данных.

## 2. Текущее состояние (AS-IS)
- В 1.27 StatusOption немедленно менял VM. Commit b7166d6b подключил desktop к общему TaskGraphCommandService; UI ждёт подтверждённого результата.
- Путь: TaskStatusPicker → TaskItemViewModel → UnifiedTaskStorage → TaskGraphCommandService → TaskTreeManager → FileTaskStorage.
- Три полных чтения: ReadGraphAsync перед командой, GetAll в CanTransitionToStatus, ReadGraphAsync после записи. Все идут на диск, хотя обычный Load уже использует _tasks.
- FileStorage.OnUpdatingAsync принудительно читает изменённый файл перед событием UI; отсутствующий/нечитаемый файл не удаляется из _tasks.
- FileDbWatcher задерживает доставку, игнорирует имя до 60 секунд после собственной записи через глобальный MemoryCache, не обрабатывает Renamed; Error только уведомляет.
- Init включает watcher после загрузки; нет барьера snapshot → initial VM hydration → replay.
- TaskItem изменяем: нельзя использовать общие экземпляры сохранённого кеша для мутаций до подтверждения Save.
- Контракт specs/2026-07-17-status-availability-contract.md сохраняется для правил, stale/no-op/denied hydration и remote outcome verification. Здесь уточняется источник подтверждённых данных локального живого storage.

Подтверждённый baseline на временной копии:
- CLI, 2843 файла, NotReady → Prepared: success за 41.864 с.
- Простое ReadAllText 2844 файлов: 11.094 с; отдельный IO baseline, не время status command.
- Desktop на одной синтетической задаче: реальный выбор работает.
- Desktop на полной копии: после Prepared → InProgress значок ещё старый через 51.243 с, новый через 71.236 с. Это интервал наблюдения, не точная длительность.
- 6/6 TaskStatusPicker tests и 12/12 UnifiedTaskStorageStatusCommandTests прошли. Маленький InMemory dataset не ловит файловую регрессию.
- Рабочие файлы не менялись. Копия: C:/Users/Kibnet/AppData/Local/Temp/unlimotion-status-repro-4ff4f415fb844152b055a2c5ee4d66c9.
- UI logs/config/status-diagnostic.json/status-after-confirmed.png: C:/Users/Kibnet/AppData/Local/Temp/unlimotion-status-ui-0ab0da8e775545aa89197765b7ffa38d. Данные локальные, не публиковать.

## 3. Проблема
Desktop использует полный диагностический путь CLI вместо загруженного доменного кеша. Распараллеливание сохраняет причину. Bare _tasks.Values небезопасно без полноты, диагностики и согласования событий/мутаций.

## 4. Цели дизайна
- Ready status = 0 перечислений каталога и 0 полных чтений файлов задач.
- Один владелец подтверждённых доменных моделей; VM — проекция/редактор.
- Внешние изменения применяются точечно; потеря событий требует восстановления.
- Сохранить graph validation, историю, mapping файлов, unarchive, save failure и изоляцию пространств.
- Не менять внешний вид и формат файлов.

## 5. Non-Goals (чего НЕ делаем)
Не вводить optimistic success/spinner, новые статусы, второй граф из VM.Model, глобальный кеш или периодический полный polling. Не ускорять cold CLI массовым чтением, не переделывать server, миграции/JSON/settings/CI. Не обещать CAS с внешним редактором. Не оптимизировать все O(N) вычисления в памяти. Не менять daily-feed checkout, рабочие задачи, не выполнять commit/push/release.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- FileTaskStorage — единственный domain cache owner: модели, исходные файлы, errors/duplicates; cold disk mode по умолчанию.
- FileStorage — live lifecycle и связь существующего watcher с кешем.
- FileDbWatcher — немедленная регистрация dirty paths/потери событий; debounce не является барьером актуальности графа.
- UnifiedTaskStorage — lifecycle/VM hydration и существующий statusCommandGate, удерживаемый до hydration.
- TaskGraphCommandService/TaskTreeManager — прежние правила/операции, получают snapshot через storage; remote/cold поведение сохраняется.

### 6.2 Детальный дизайн
**Один кеш и чтения.**
1. Продвинуть существующий кеш FileTaskStorage в полный кеш графа; допустим внутренний helper этого storage. Не оставлять два независимо обновляемых набора задач.
2. Хранить состояние по source file: TaskItem либо load error. Индекс Id → file и duplicate diagnostics выводить из того же состояния. Дубликаты нельзя потерять при свёртке в Dictionary<Id,TaskItem>. Full load, raw targeted refresh и forced read используют один file-admission predicate: прямые дочерние непустые файлы без расширения либо .json, без скрытых/служебных артефактов. Сохранить прежнее исключение zero-length из графа; прежний TaskItem такого пути больше не актуален. Посторонние README.md/tmp/report и файлы вне корня не превращаются в load errors. Rename из допустимого имени в исключённое удаляет прежний путь из графа; обратный rename добавляет новый.
3. Full load строит новый набор и атомарно заменяет старый; отсутствующие задачи удаляются. Сохранить детерминированный порядок source mapping/diagnostics текущего ReadDirectoryAsync. Ready означает полноту учёта файлов, а не write safety: известные corrupt/duplicate записи остаются диагностикой Ready-графа и блокируют write без нового full scan. Ошибка самого перечисления/неполный snapshot оставляет NeedsReload.
4. В Ready ReadGraphAsync возвращает snapshot с diagnostics, GetAll — тот же логический граф без файлового перечисления, Load — данные того же владельца. Явный ReadDirectoryAsync остаётся дисковым diagnostic API; публикация его результата в live cache возможна только как полный согласованный refresh.
5. На границах кеша изолировать изменяемые TaskItem и вложенные history/criteria/lists/repeater/ExtensionData. Мутация полученного объекта или аргумента Save после возврата не меняет сохранённый snapshot. Null/legacy history обрабатывать по текущему контракту.
6. Полный scan допустим для startup, explicit refresh/reinit, recovery после потери событий и cold mode без живой подписки. У обычной команды нет TTL-rescan.

**Барьер команд, изменения файлов, подтверждение.**
7. Использовать существующий per-directory mutation lock для публикации snapshot, применения внешних изменений и локальных записей. Raw watcher callback только регистрирует путь/поколение, не ждёт IO, UI или этот lock. Не создавать обратный порядок lock относительно statusCommandGate.
8. Синхронно регистрировать dirty path/generation до debounce. Rename регистрирует old/new пути. Очередь принадлежит конкретному source. Повторные события пути объединяются; позднее поколение не стирается снятием раннего.
9. Перед первой validation команда под directory lock применяет все уже зарегистрированные пути. Читать фактическое содержимое/отсутствие файла, не доверять последнему типу события. При нормальной доставке это точечная операция.
10. Входящие во время мутации события накапливаются. Перед финальной validation применить зарегистрированные изменения. Checkpoint ограничен тремя попытками; при непрерывных изменениях — контролируемая ошибка/OutcomeUnknown, не зависание и не заведомо stale success.
11. Save/Remove публикуют подтверждённое состояние в кеш только после дисковой операции под тем же lock. До IO регистрировать attempted path в области текущей команды, включая newly-generated task Id; при failed/partial cascade перечитать все попытанные пути и вернуть существующую ошибку. Нельзя брать этот список только из result.ChangedTasks: при исключении результата ещё нет. Не изображать all-or-nothing и не менять mutable cache до Save.
12. Финальная локальная проверка перечитывает только попытанные/изменённые файлы, затем анализирует обновлённый граф в памяти. Warm no-op/denied без событий не пишет и не делает readback. Объём IO зависит от changed/dirty paths, не общего числа задач. Remote verification не менять.
13. Убрать подавление всех событий имени на 60 секунд: внешний edit сразу после Save обязан учитываться. Собственные echoes объединяются/читаются точечно; подтверждённый fingerprint содержимого файла позволяет не публиковать повторное UI hydration при совпадении. Fingerprint привязан к конкретному source/path, не только имени или timestamp. Иначе собственное эхо могло бы перетереть ещё несохранённую правку VM. Отличающееся внешнее содержимое не подавляется.
14. Delete очищает domain entry/mapping. Rename/Id change обновляет оба направления, удаляя старый Id лишь если его больше нет в другом файле. Corrupt файл хранится как load error и запрещает запись; старый TaskItem не считается актуальным. Исправление/удаление снимает diagnostics точечно. Forced Load обновляет того же владельца.
15. Error/overflow/реальная остановка raw наблюдения → NeedsReload. Reload выполняет первый запрос, который вошёл в directory lock и обнаружил NeedsReload; следующие после захвата lock повторно проверяют state и используют уже опубликованный результат. Запрещено под этим lock ждать shared refresh Task, который сам ожидает тот же lock; отдельный фоновый lock-waiting owner не нужен. Failed reload запрещает статусную запись, не объявляет старый snapshot Ready. Обычный SetEnable(false) из Git backup при активном live-cache приостанавливает публикацию UI, но не raw сбор путей. Live-cache держит отдельную подписку/lease на тот же FileSystemWatcher; второй watcher не создавать. При SetEnable(true) накопленные пути обрабатываются точечно и replay выполняется без full reload. ForceUpdateFile проходит через ту же регистрацию пути. Реальное освобождение raw lease при завершении storage lifetime прекращает наблюдение; повторный запуск требует reload.

**Lifecycle и UI.**
16. После существующих миграций включить наблюдение до полного live snapshot, буферизовать UI updates до окончания initial VM hydration. После подписки UnifiedTaskStorage проиграть события между snapshot и публикацией VM: не терять их и не перетирать более новую модель initial batch.
17. Dispose/source switch прекращает применение и доставку старых событий; raw lease освобождается после существующего seal/drain pending saves/status commands. Возврат к source после реальной остановки raw наблюдения требует reload до Ready. Пауза только UI-доставки при сохранённом raw наблюдении требует drain/replay, а не reload.
18. Доставлять UI updates после обновления доменного кеша и вне directory lock. Каждая публикация имеет монотонное поколение в рамках storage lifetime; command result и watcher update проходят единую generation-aware доставку. Пакет более старого поколения не может перезаписать уже применённый новый, включая второй Update из TaskItemViewModel после возврата команды. ChangedTasks каскада брать из финального подтверждённого snapshot, а не из объектов, изменённых до последнего readback. Reconciliation/reload применяет diff к VM без доменного Delete/Save в ответ на наблюдение файлов: recovery не должен запускать каскадную запись. Соответствующее изменение внешнего delete описано в §7 явно.
19. При corrupt/temporarily unreadable файле не запускать удаление/успешный status; допустимо оставить последнюю отображённую VM до исправления, но она не источник write validation. Ошибка — через существующий result/notification.
20. Меню, пиктограмма, клавиатура/мышь, фильтры и no-op/denied hydration не меняются. Старый план spinner/flyout/attach redesign исключён.

Граница согласованности: учитываются доставленные watcher события и результаты собственных записей. События внешнего редактора, доставленные после checkpoint, применяются следующим обновлением; FileSystemWatcher не даёт атомарный snapshot/CAS с произвольным процессом.

Storyboard: старый статус → выбор → проверки в памяти → запись изменённых файлов → новый подтверждённый статус. Геометрия UI не меняется; отдельный макет не нужен.

### 6.3 User-Observable Scenarios
| Сценарий | Результат | Evidence |
|---|---|---|
| Выбор в большом загруженном source | значок/история обновлены без массового IO | file-backed UI + counters + desktop timing |
| External blocker до команды | новый запрет учитывается до debounce | raw event + command regression |
| Save, затем внешний edit | внешнее состояние видно/учтено | watcher integration |
| Delete/rename/Id change | нет фиктивного старого Id и каскадной записи от наблюдения | storage/UI tests |
| Corrupt/duplicate | write denied с diagnostics; после repair работает | safety tests |
| Save/partial failure | нет неподтверждённого UI успеха; кеш согласован readback | fault injection/UI |
| Overflow/reopen/source switch | reload либо ошибка; старые события не меняют новый source | lifecycle/race tests |
| Git commit/push/pull pause | raw изменения не теряются; resume не вызывает full scan | SetEnable/ForceUpdateFile integration |

### 6.4 State / Interaction Matrix
| State | Read/command | Events/UI |
|---|---|---|
| Cold | существующий disk path | кеш не считается полным |
| Loading | нет Ready до полного snapshot | buffer + initial hydration + replay |
| Ready | snapshot; dirty drain до write | точечное применение |
| NeedsReload | coalesced reload; failure запрещает write | продолжить регистрацию событий |
| Disposed | новые операции отклонены | не обновлять другой source |

### 6.5 Decision Ledger
| Решение | Владелец | Статус |
|---|---|---|
| Использовать существующий кеш | пользователь: замечание + «Делай» | направление принято |
| Один domain owner, не VM graph | реализация | зафиксировано |
| Cold CLI/server прежние; targeted local readback | реализация | зафиксировано |
| Формальный переход в EXEC | central QUEST/пользователь | подтверждено 2026-09-01 |

### 6.6 Runtime / Config / Data Contract Matrix
| Контракт | Изменение |
|---|---|
| JSON/settings/migration version | нет |
| Local с активным watcher | live cache после init |
| Local без watcher | cold path, stale cache не Ready |
| CLI diagnostics/server | прежний disk/remote verification |
| Lifetime | кеш/очередь одного storage |
| Directory lock | сохранён, callback его не захватывает |

## 7. Бизнес-правила / Алгоритмы
TaskStatusTransitionPolicy/TaskAvailabilityService не менять. Сохранить запреты, completion criteria, unarchive normalization, один history entry на реальный переход, no-op без записи и authoritative hydration. Load errors, duplicates и broken references по-прежнему запрещают graph write. Наблюдение внешнего удаления меняет проекцию, но больше не вызывает TaskTreeManager.DeleteTask(..., false), который сейчас записывает связанные задачи. Это необходимое уточнение sync-контракта: внешняя неполная правка связей остаётся диагностикой, а не запускает скрытый ремонт/перезапись файлов. Явная пользовательская команда удаления по-прежнему выполняет прежний доменный алгоритм.

## 8. Точки интеграции и триггеры
Init/reinit активирует cache; raw create/change/delete/rename делает paths dirty; error/disable инвалидирует полноту; status/unarchive/criterion получает snapshot через storage. Любой Save/Remove, включая обычное редактирование, обновляет тот же кеш. UI delivery не выполняется под mutation lock.

## 9. Изменения модели данных / состояния
Только внутренние lifecycle states/dirty generations/diagnostics. Никаких сериализуемых флагов TaskItem/Settings. При выделении helper заменить прежнего owner, не хранить вторую независимо обновляемую коллекцию.

## 10. Миграция / Rollout / Rollback
Миграция не нужна; после запуска кеш строится из файлов. Проверять на временной копии без Git backup. Откат сборки сохраняет читаемый JSON. Commit/push/release не разрешены этим task.

## 11. Тестирование и критерии приёмки
- AC1: warm status/unarchive через настоящий FileStorage сохраняет файл/историю и UI; счётчики полного enumeration/read после warm-up равны 0.
- AC2: raw dirty до debounce, create/change/delete/rename/Id change/own-write→external-edit корректны и изолированы по source.
- AC3: corruption/duplicate/reference safety и repair сохранены; mutable alias/failed Save не портят подтверждённый snapshot. Full/targeted admissions совпадают для ignored/zero-length файлов и rename через границу фильтра.
- AC4: partial failure/readback/no-op/denied/OutcomeUnknown сохраняют результатный контракт; readback лишь attempted/changed files.
- AC5: init race/pause/resume/overflow/concurrent refresh/dispose проходят без stale success/deadlock и без обратных записей от UI reconciliation. Обычная Git backup pause/resume не увеличивает full-scan counter; raw события stash/rename, включая после ForceUpdateFile, учитываются. Собственный echo не перетирает pending editor data. При отложенной доставке command result новое watcher поколение не откатывается старым result ни в UnifiedTaskStorage, ни в TaskItemViewModel.
- AC6: cold CLI/server/unknown JSON fields совместимы.
- AC7: warm median из 5 одиночных переходов на той же большой копии <2 с, full IO counters = 0. Измерять selection → confirmed VM, отдельно запись/hydration. Это локальная цель, не SLA всех устройств. Если не достигнута, измерить и устранить относящийся к пути bottleneck.
- AC8: relevant UI tests добавлены/обновлены и запущены; полный Main/Headless перед завершением либо конкретный environment blocker и next-best evidence. Baseline green не доказывает fix.

### Acceptance-to-Test Matrix
| AC | Coverage | Контрпример |
|---|---|---|
| 1 | MainControlTaskStatusIconUiTests с FileStorage + IO counters | InMemory green скрывает три scan |
| 2 | FileStorageTaskStatusTests/FileDbWatcher integration | debounce/TTL теряет внешний edit |
| 3 | FileTaskStorageTests/TaskGraphCommandServiceTests | stale delete/duplicate/alias, empty/ignored/rename admission |
| 4 | UnifiedTaskStorageStatusCommandTests + faulting file writer | failed write выглядит успешным |
| 5 | Init/source lifecycle и UI projection tests; Git watcher pause integration | event во время batch, поздний callback, reload cascade, backup-triggered scan, own echo; deterministic overflow/reload/command lock race |
| 6 | CLI status/validate и remote command regression | live cache без подписки |
| 7 | реальный desktop на полной копии + timers/counters | timing одной задачи выдаётся за результат |
| 8 | full Main/Headless, pointer/keyboard flow | прямой MenuItem.Click заменяет пользовательский ввод |

Команды, после добавления тестов:
~~~powershell
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter '/*/*/FileStorageTaskStatusTests/*' --maximum-parallel-tests 1 --output Detailed
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter '/*/*/MainControlTaskStatusIconUiTests/TaskStatusPicker*' --maximum-parallel-tests 1 --output Detailed
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter '/*/*/UnifiedTaskStorageStatusCommandTests/*' --maximum-parallel-tests 1 --output Detailed
git diff --check
~~~
Остальные затронутые классы и full Main/Headless — по актуальному repo workflow. Regression сначала падает на baseline по IO/sync assertion. Desktop screenshots и before/after recording — через существующий UI harness; при невозможности записи фиксировать конкретный технический blocker и screenshots/logs. Не публиковать названия реальных задач.

## 12. Риски и edge cases
Raw pending event не равен applied event; нужен generation checkpoint. Mutable snapshots, duplicate files, rename/atomic replace, Init replay, UI writeback, lock inversion и continuous events покрываются §6.2 и тестами. Полный reload после overflow допустим, но не обычная ветка статуса. Граница late external events описана явно, CAS не обещан.

### Expected User Review Objections
| Возражение | Ответ и проверка |
|---|---|
| «Почему опять все файлы?» | zero full IO counters; big-copy timing |
| «Завели ещё один кеш?» | один storage owner; diff/state-owner review |
| «Внешние изменения?» | raw events/checkpoints/recovery; race tests |
| «Кеш покажет failed Save как успех?» | isolation/readback/fault injection |
| «Трогаете меню или чужой checkout?» | scope и diff review |
| «Проверяли одну задачу?» | file-backed UI + big-copy desktop |

### Rework Prevention Checklist
Scenarios/decisions/acceptance/objections заполнены. Продуктовых блокеров нет. Старый parallel-reader/spinner план исключён. Требуется formal phase gate и review обновлённого плана.

## 13. План выполнения
1. Утвердить spec по central QUEST. Выполнено 2026-09-01.
2. Добавить падающий file-backed UI/storage regression.
3. Реализовать одного domain owner, diagnostics/snapshot isolation.
4. Подключить raw events/checkpoints/lifecycle/recovery/targeted readback.
5. Запустить safety/races/CLI/server/UI tests.
6. Выполнить desktop measurement/full tests/post-EXEC review.
7. Отчитаться о локальном результате без claims о Git delivery/release.

## 14. Открытые вопросы
Продуктовых blockers нет. After-performance/tests/recording — будущие проверки EXEC, не подтверждённые результаты.

## 15. Соответствие профилю
UI-facing bugfix требует UI coverage/run, real desktop walkthrough и inspected screenshots. Проверять status control в списке/текущей задаче, pointer/keyboard, фильтры/source switching; layout не меняется. TUnit использует --treenode-filter. Performance измеряется после warm-up отдельно от startup/migrations/full-suite duration.

## 16. Таблица изменений файлов
| Область | План |
|---|---|
| FileTaskStorage.cs / internal helper | один кеш, snapshots/diagnostics/targeted refresh |
| FileStorage.cs | live mode, watcher queue/replay |
| FileDbWatcher.cs / watcher interface | один native watcher, raw lease отдельно от UI pause; paths/rename/invalidation без global own-write TTL |
| UnifiedTaskStorage.cs / TaskStorageBuilder.cs / TaskItemViewModel.cs при необходимости | lifecycle, generation-aware hydration и projection без writeback; меню/layout не менять |
| TaskGraphDiagnostics.cs / command service при необходимости | local targeted verification capability; remote не менять |
| Clone helper при необходимости | глубокая копия без data format changes |
| File/storage/command/UI/lifecycle tests | zero scans, safety, races |
| Эта spec | журнал/evidence/review |
GetAll в TaskTreeManager при live режиме не должен идти на диск; алгоритм по возможности остаётся прежним.

## 17. Таблица соответствий (было -> стало)
| Было | Стало |
|---|---|
| ReadGraph/GetAll обходят кеш | согласованный live snapshot |
| Полный post-read | readback затронутых + анализ памяти |
| Forced null оставляет stale entry | eviction/diagnostic в общем owner |
| Debounce/TTL определяют свежесть | raw queue задаёт dirty state |
| Init пропускает события | snapshot + replay |
| Overflow только toast | invalidate + coalesced reload |

## 18. Альтернативы и компромиссы
Parallel scans отклонены пользователем: O(N) IO остаётся. VM graph содержит unsaved edits. Bare _tasks.Values теряет полноту/diagnostics. Новый cache service рядом со старым дублирует owner. Full scan не даёт CAS с внешним редактором. Выбран существующий domain cache с live lifecycle; CPU validation всего графа и память для snapshots пока сохраняются и измеряются.

## 19. Результат quality gate и review

### SPEC Linter Result
| Пункт | Статус | Основание |
|---|---|---|
| A1 | PASS | цель §1 |
| A2 | PASS | AS-IS + runtime baseline |
| A3 | PASS | причина трёх чтений |
| A4 | PASS | zero full IO + safety |
| A5 | PASS | Non-Goals |
| B6 | PASS | один owner |
| B7 | PASS | lifecycle/integration |
| B8 | PASS | policy неизменна |
| B9 | PASS | failure/recovery |
| B10 | PASS | counters/warm timings |
| C11 | PASS | без data change |
| C12 | PASS | без migration, restart rebuild |
| C13 | PASS | CLI/server/rollback |
| D14 | PASS | AC1–AC8 |
| D15 | PASS | acceptance mapping |
| D16 | PASS | commands/full tests |
| E17 | PASS | этапы §13 |
| E18 | PASS | product blockers нет |
| E19 | PASS | multi-module scope указан |
| F20 | PASS | UI/desktop/perf |
Итог: ГОТОВО. Повторно проверены пункты 1–20, секции 0–20, acceptance mapping и отсутствие trailing whitespace; post-SPEC review завершён ниже.

### SPEC Rubric Result
| Критерий | Балл | Основание |
|---|---:|---|
| Цель/границы | 5 | local warm status |
| Текущее состояние | 5 | source/tests/timings |
| Целевой дизайн | 5 | owner/queue/lock/readback |
| Безопасность | 5 | isolation/recovery/JSON |
| Тестируемость | 5 | counters/races/UI |
| Автономность | 5 | decisions/planned steps |
Итого 30/30 по полноте плана, не оценка реализации.

### Role-Based Review Result
- Domain: rules/history/no-op сохраняются.
- UX: прежнее меню, confirmed update; spinner не нужен для этого fix.
- Tester: file-backed UI/counters/races/failure и full dataset.
- Architect: один owner/generation barrier/targeted readback/cold compatibility.
- Delivery/security: local copies, без production/Git delivery.

### Post-SPEC Review
- Статус PASS для спецификации; реализация не начата.
- Scope/Evidence: FileTaskStorage/FileStorage/FileDbWatcher/IDatabaseWatcher, UnifiedTaskStorage lifecycle/updating, TaskStorageBuilder, diagnostics/command cloning, status tests, central owner-documents.
- Contract pass: старые parallel scans/pending UI исключены, целостность live cache включена.
- Adversarial pass: отдельный reviewer /root/review_status_spec прочитал новую spec и исходники. Выявлены UI ordering, reload lock ownership, file-admission parity; исправления внесены. Его runtime danger-full-access/approval never: это отдельный adversarial fallback, не технически изолированное независимое review.
- Role-Based pass: domain — unchanged status rules и явное уточнение projection-only external delete; UX — прежний control без stale rollback; tester — IO/race/failure/UI coverage; architect — один owner и lock protocol; delivery/security — только local copies и spec, без публикации.
- Fix and re-review: reviewer повторно прочитал §6.2 п2/3/11/15/18, §7, AC3/AC5 и таблицу файлов; вернул PASS/«Нет находок». Проверены также raw lease/Git pause/ForceUpdateFile, attempted paths и Ready vs write safety. Основной агент повторил mapping, all 20 linter items и структурную/whitespace проверку.
- Depth checklist: scope drift — parallel IO/spinner/чужой checkout исключены; acceptance — AC1–AC8 измеримы; scenarios/decisions/objections сопоставлены; validation — есть baseline, новые тесты/after timing ещё не заявляются; regression — generation ordering, mutable cache, corruption, duplicates, early/late events, reload deadlock перечислены; comments/docs — меняется только текущая spec; hidden contract — external delete projection-only назван явно в §7; manual challenge — старый result после нового watcher update теперь покрыт обязательной проверкой.
- Evidence inspected: перечисленные исходники плюс BackupViaGitService SetEnable/ForceUpdateFile/stash apply, TaskItemViewModel.ExecuteStatusOperationAsync, текущая spec и неизменённое состояние tracked files. Baseline commands/results и local-only artifacts сохранены в §2/§11.
- No-findings justification: actionable замечания имеют конкретные изменения и regression checks; повторный проход не нашёл открытых design gaps в рассмотренной области. Это не подтверждает runtime performance или корректность ещё не написанного кода.
- Stop decision: PASS — можно запрашивать точный переход central QUEST; продуктовых вопросов нет. Исходники/тесты не менять до формального approval.
- Residual risks: reviewer runtime без read-only sandbox, реализация/after-evidence отсутствуют.

| Severity | Area | Finding | Required action | Status |
|---|---|---|---|---|
| HIGH | UI ordering | watcher N+1 мог откатиться отложенным command N, включая второй VM.Update | общий generation guard, final cascade snapshot, race test | fixed in spec, re-reviewed |
| MEDIUM | reload lock | shared refresh мог ждать удерживаемый командой lock | reload owner внутри critical section, deterministic race | fixed in spec, re-reviewed |
| MEDIUM | file admission | targeted/full расходились на ignored/empty/rename | единый predicate и тесты границы | fixed in spec, re-reviewed |
| — | итог | Нет находок после исправлений в проверенной области | EXEC после phase gate | PASS |

### Post-EXEC Review
- Статус PASS. Отдельный reviewer повторно проверил текущий diff после исправления всех найденных гонок; открытых BLOCKER/HIGH/MEDIUM/LOW замечаний нет. Изоляция reviewer была adversarial fallback: runtime unrestricted, файлов он не менял.
- Обычная локальная статусная команда читает согласованный live graph из `FileTaskStorage`; `GetAll`/`ReadGraphAsync` не перечисляют каталог в Ready-состоянии. Источники без raw watcher остаются в прежнем cold-режиме.
- Raw watcher регистрирует путь и поколение до debounce. Delete/zero-length/corrupt/rename, invalidation/reload, pause/resume и dispose обновляют либо инвалидируют того же владельца графа. Инициализация имеет checkpoint до и после VM hydration.
- Запись публикуется только после disk IO. При частичном/неопределённом результате перечитываются все attempted IDs, а итоговый command result содержит подтверждённый snapshot и revision. Mutable `TaskItem` изолированы глубокой копией.
- Unified хранит source-lifetime revision/tombstone по ID: watcher remove `N+1` блокирует поздний command hydrate `N`, а watcher create/update `N+1` блокирует старые remove/reconcile `N`.
- Дополнительный пользовательский сценарий выявил потерю несохранённых editor-полей: немедленный Complete мог обогнать throttled save, а последующая Unified hydration перезаписывала `Model` подтверждённым состоянием с диска. Теперь статусная операция сначала сохраняет immutable editor snapshot, после ответа объединяет только editor-owned поля с авторитетным статусом/историей и при закрытии ожидает все допущенные producers и выполняет финальный drain. Ошибка финального сохранения остаётся видимой lifecycle seal, но не превращает уже подтверждённую смену статуса в ложную ошибку.
- Точный file-backed regression создаёт задачу, сразу задаёт название, дату начала и ежедневное повторение, затем Complete: исходная задача сохраняет поля, а следующая Prepared-задача создаётся. Тот же поток покрыт через Headless status picker; отдельно проверены criterion edits, edit во время реальной `FileStorage → Unified → ViewModel` hydration, retry и lifecycle seal.
- Сборка `Unlimotion.Test` прошла. Targeted: Unified 14/14, FileStorage status 22/22, TaskItemViewModel status 24/24, status picker UI 7/7, TaskGraph command 38/38, FileTaskStorage 7/7, BackupViaGit 53/53, TaskSourceManager 36/36, card layout UI 20/20, completion-criterion throttle 1/1. Полный Headless: 38/38 за 2:10.083.
- Актуальный полный Main после исправления найденных cleanup/seal и test-fake проблем: 932/932, 0 failed, 0 skipped за 22:18.500. Более ранний проход 919/920 с несвязанным localization-flake и его чистый повтор 1/1 сохранены как историческое evidence, но не описывают итоговый код.
- Реальный desktop на копии 2844 задач: pointer Prepared→InProgress записан на диск за 289 ms, новый icon наблюдался через 1258 ms вместе с screenshot capture. Это один подтверждённый проход, не требуемая AC7 median из пяти; performance evidence остаётся ограниченным.
- `git diff --check` чист по содержимому, присутствуют только предупреждения о будущей LF→CRLF нормализации. Commit/push/release не выполнялись.

## Approval
Получено: «Спеку подтверждаю» — 2026-09-01. EXEC разрешён в утверждённых границах.

## 20. Журнал действий агента
| Фаза | Блок | Уверенность | Недостающие данные | Следующее действие | Передача человеку | Фактическое решение | Обоснование | Артефакты |
|---|---|---:|---|---|---|---|---|---|
| SPEC | Диагноз | 0.99 | after evidence | спроектировать fix | после review | main/daily-feed; 1.27 OK | tiny tests скрывают latency | source diff, 6 UI tests |
| SPEC | CLI/desktop baseline | 0.99 | точное confirmed время | spec | нет | нет | CLI 41.864 с; UI 51.243–71.236 с | temp copies/screenshots |
| SPEC | Первый дизайн | 0.8 | замечание пользователя | пересмотреть | approval | full scans отвергнуты | parallel IO сохраняет root cause | старый draft отозван |
| SPEC | Коррекция | 0.99 | coherence contract | source inspection | пока нет | кеш+подписка, затем «Делай» | направление принято; exact phase gate остаётся | storage/watcher sources |
| SPEC | Новый дизайн | 0.96 | adversarial review | reviewer/recheck | после review | повторно approval ещё не запрошено | один owner, generations, targeted readback | эта spec |
| SPEC | Git pause и own echo | 0.98 | review нового контракта | adversarial check | нет | нет | BackupViaGitService временно выключает watcher; raw lease предотвращает повторные scans, fingerprint защищает от собственного echo | BackupViaGitService 692–886, spec §6.2 |
| SPEC | Review: UI ordering | 0.98 | повторный reviewer pass | re-review | нет | нет | старый command result не должен откатывать новое watcher состояние; confirmed cascade берётся из final snapshot | spec §6.2/AC5, reviewer finding |
| SPEC | Review: reload lock и file admission | 0.98 | итоговый re-review | повторить gates | нет | нет | reload owner выбирается внутри lock; full/targeted фильтрация едина | spec §6.2/AC3/AC5, reviewer findings |
| SPEC | Полный post-SPEC loop завершён | 0.98 | formal phase approval | EXEC после фразы | да, central QUEST | финальный ответ запрашивает «Спеку подтверждаю» | reviewer re-review PASS; 21 секция/20 linter items/0 trailing whitespace; tracked source без изменений | spec, reviewer results, git status |
| EXEC | Переход в реализацию | 0.99 | падающий regression и implementation evidence | создать ветку и тест | нет | пользователь: «Спеку подтверждаю» | exact QUEST gate выполнен; scope cache/watcher/UI ordering | branch fix/status-transition-cache, эта spec |
| EXEC | Live graph и warm regression | 0.97 | full-suite/review | watcher/lifecycle tests | нет | нет | Unified.Init публикует storage-owned snapshot; warm status имеет 0 новых directory enumerations | FileTaskStorage, UnifiedTaskStorage, FileStorageTaskStatusTests: 9/9 |
| EXEC | Raw watcher и ordering | 0.95 | reviewer/race coverage | full tests/review | нет | нет | raw path регистрируется до debounce; invalidation даёт один reload; revisions защищают Unified и второй VM.Update | watcher/storage/result/VM files; targeted 89 tests green |
| EXEC | Большая копия desktop | 0.97 | 5-run median без capture overhead | full tests/review | нет | нет | 2844 задачи, pointer click Prepared→InProgress: disk 289 ms, новый icon наблюдался через 1258 ms; control screenshot capture adds variable latency | temp copy, computer-use screenshots, own PID 49608 |
| EXEC | Generation tombstones | 0.99 | нет | targeted/full tests | нет | нет | source-lifetime CAS не позволяет старому command/reconcile перезаписать более новое watcher событие | UnifiedTaskStorage, 2 deterministic race tests |
| EXEC | Немедленный Complete после редактирования | 0.99 | нет | file-backed/UI regression | нет | пользовательский сценарий воспроизведён | статус обгонял throttled save, а hydration стирала editor-поля; immutable snapshot и pre/post-command drain сохраняют поля и создают повтор | TaskItemViewModel, FileStorageTaskStatusTests, status picker UI |
| EXEC | Lifecycle seal | 0.99 | нет | full tests/review | нет | нет | seal закрывает admission, ждёт producers, сохраняет стабильную throttled revision и сообщает persistent flush failure; подтверждённый status result остаётся независимым | TaskItemViewModel status tests, layout UI tests |
| EXEC | Полные проверки | 0.99 | AC7 median 5 не измерена | post-EXEC review | нет | нет | affected suites, Headless 38/38 и актуальный Main 932/932 зелёные | TUnit reports, 2026-09-01 |
| EXEC | Post-EXEC review | 0.99 | AC7 median 5 не измерена | финальный локальный отчёт | нет | нет | reviewer PASS без открытых findings; runtime adversarial fallback, файлов не менял | review_status_spec, текущий diff |
