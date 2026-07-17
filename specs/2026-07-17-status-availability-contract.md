# Единый контракт статусов, доступности и переходов

## 0. Метаданные
- Тип (stack + overlay): `.NET desktop client` + `ui-automation-testing`
- Владелец: Product Owner / активный пользователь
- Масштаб: medium
- Целевое семейство / behavior baseline: текущая пятистатусная модель Unlimotion `1.27.0`; без добавления новых статусов и без изменения persistence schema
- Поверхность: Codex в локальном Windows/PowerShell workspace; продуктовая поверхность — Avalonia desktop UI в file/server storage modes, shared status engine, Telegram status entry point и двуязычные root README
- Effective runtime: текущая Codex-сессия с включённым reasoning; model-specific behavior не является частью продуктового контракта
- Eval baseline / evidence:
  - source/test/docs audit от 2026-07-17 на `origin/main` commit `5aebebcb34eabe35fcdb7a47ff76ffdc2a7e16dd`;
  - `TaskTreeManagerSafetyTests`: 3/3 PASS;
  - `TaskStatusDomainTests`: 4/4 PASS, включая устаревшее ожидание `Archived(previous InProgress) -> InProgress`;
  - `TaskAvailabilityParityTests`: 2/2 PASS, но тест сравнивает service с его wrapper, а не UI consumer;
  - `MainControlTaskStatusIconUiTests`: 20/20 PASS, но текущие assertions используют ViewModel как собственный oracle;
  - before/after visual evidence создаётся в EXEC из одного автоматизированного FlaUI-сценария.
- Целевой релиз / ветка: `fix/status-availability-contract`; audit baseline `origin/main` = `5aebebc`, но EXEC разрешён только после merge PR #274 и rebase этой ветки на новый `origin/main`
- Ограничения:
  - текущая фаза `EXEC`: пользователь 2026-07-17 сообщил точную фразу `Спеку подтверждаю` и попросил выполнить все этапы;
  - утверждены рекомендованные product choices: denied desktop targets видны disabled с inline reason/HelpText; previous `Completed`/missing/corrupt history восстанавливается в `NotReady`; Telegram входит в Stage 2;
  - master roadmap и stage-1 child spec находятся в PR #274; до green checks, ready-for-review, merge PR #274, `git fetch` и rebase на содержащий его `origin/main` production edits запрещены, разрешены только spec/journal, dependency и branch-preparation действия;
  - локальный `AGENTS.override.md` требует UI tests для UI-facing поведения;
  - не менять enum статусов, JSON schema, DTO/molds, status-history schema или существующие данные;
  - не добавлять server wire method в Stage 2: server mode использует существующие GetAll/Load/Save endpoints; отсутствие cross-client compare-and-swap фиксируется честно и не называется atomic;
  - не переписывать прежнюю status spec как будто она всегда содержала новый контракт: добавить явную errata/supersession note;
  - не завершать EXEC без targeted, full domain, full headless и релевантного FlaUI gate;
  - UI video evidence `до`/`после` обязательно попытаться получить через автоматизированный FlaUI run; fallback допустим только с объективной причиной и next-best screenshots/logs.
- Связанные ссылки:
  - master roadmap: PR #274, `specs/2026-07-17-readme-reliability-roadmap.md`;
  - stage 1: PR #274, `specs/2026-07-17-readme-install-safety.md`;
  - прежняя status spec: `specs/2026-06-09-task-status-model.md`;
  - `src/Unlimotion.Domain/TaskStatus.cs`;
  - `src/Unlimotion.Domain/TaskItem.cs`;
  - `src/Unlimotion.TaskTreeManager/TaskAvailabilityService.cs`;
  - `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs`;
  - `src/Unlimotion.TaskTreeManager/TaskGraphCommandService.cs`;
  - `src/Unlimotion.ViewModel/TaskItemViewModel.cs`;
  - `src/Unlimotion/TaskStatusPicker.cs`;
  - `src/Unlimotion.TelegramBot/Bot.cs`;
  - `README.md` / `README.RU.md`.

Если секция не применима, это указано явно с причиной.

## 1. Overview / Цель
Устранить расхождение между каноническим движком статусов, desktop picker, разархивацией, Telegram entry point, тестами и README. Этап сохраняет off-diagonal правила `TaskAvailabilityService`, отдельно закрепляет effective command-level no-op на диагонали, делает pure policy единым источником решений и устраняет пользовательские команды, которые сейчас выглядят доступными, но затем молча откатываются.

Outcome contract:
- Success means:
  - один pure transition policy определяет матрицу 5x5 и reason codes для engine, ViewModel и bot adapter;
  - `Completed/Archived -> InProgress` не предлагается как разрешённый desktop/Telegram-переход и отклоняется без мутации status/history/timestamps/persisted file;
  - picker не показывает текущий статус, показывает остальные четыре target и делает denied targets disabled с локализованной inline-причиной/HelpText;
  - `InProgress -> Archived -> Unarchive` возвращает `Prepared`, а все legacy/missing-history cases следуют явной normalization matrix;
  - parent и подтверждённый child cascade используют одну normalization function;
  - lifecycle status, graph availability, start guard и completion guard больше не смешиваются в UI assertions и README;
  - future planned begin запрещает start, но не меняет graph availability и opacity;
  - `Completed` и `Archived` не являются активными blockers;
  - Telegram показывает только реально разрешённые target statuses и применяет тот же storage-backed guard без локальной мутации;
  - EN/RU README и errata прежней status spec описывают один контракт;
  - targeted и full UI/domain suites проходят, а before/after visual evidence доступно для review.
- Итоговый артефакт / output:
  - shared transition policy и reason-code mapping;
  - исправленные desktop picker/archive/unarchive и Telegram status flows;
  - regression/domain/headless/FlaUI coverage;
  - обновлённые README EN/RU и errata прежней status spec;
  - visual storyboard и actual before/after evidence;
  - отдельный PR stage 2.
- Stop rules:
  - не начинать EXEC без отдельного approval этой child spec;
  - не менять persistence/DTO contract ради удобства переходов;
  - не сохранять duplicate switch matrix в ViewModel или bot;
  - не объявлять denied transition успешным, даже если manager затем восстановил старый status;
  - не продолжать к full suite при падающем targeted test;
  - не завершать при расхождении Headless и FlaUI behavior;
  - не доставлять README diff поверх незакрытого конфликта с PR #274.

## 2. Текущее состояние (AS-IS)
- Пять статусов определены в `src/Unlimotion.Domain/TaskStatus.cs`: `NotReady`, `Prepared`, `InProgress`, `Completed`, `Archived`.
- Канонический engine contract живёт в `TaskAvailabilityService.EvaluateStatusTransition`:
  - `NotReady` и `Prepared` всегда доступны как target;
  - `Archived` доступен, кроме source `Completed`;
  - `InProgress` требует `CanStart`;
  - `Completed` требует source не `Archived` и `CanComplete`.
- `TaskGraphCommandService` обрабатывает same-status как no-op.
- `TaskAvailabilityService.Analyze` вычисляет разные оси:
  - graph availability / `IsCanBeCompleted`;
  - `CanStart` = graph available + planned begin не в будущем + source не terminal;
  - `CanComplete` = graph available + completion criteria satisfied + source не terminal.
- `TaskStatusExtensions.IsIncompleteForAvailability` считает незавершёнными только `NotReady`, `Prepared` и `InProgress`; поэтому `Completed` и `Archived` не должны оставаться активными blockers.
- `TaskTreeManager` автоматически переводит `InProgress -> Prepared`, если задача становится недоступной либо planned begin переносится в будущее.
- `TaskItemViewModel.CanTransitionToStatus` дублирует policy, но для `InProgress` не проверяет terminal source. Поэтому picker может разрешить `Completed/Archived -> InProgress`, а manager позже молча откатывает status.
- `AvailableStatusTransitionOptions` фильтрует disabled options. Хотя `TaskStatusPicker` уже умеет bind `IsEnabled` и tooltip, пользователь видит только enabled targets и не получает причину отсутствия команды.
- `ArchiveCommand` напрямую ставит `Model.GetRestoreStatusAfterArchive()`. Доменный helper возвращает сырой последний non-archived status, поэтому previous `InProgress` превращается в запрещённый `Archived -> InProgress`.
- Child unarchive cascade вызывает тот же сырой helper для каждого archived child.
- Current menu copy в `MainControl.axaml` всегда называет действие `Archive`, включая archived task.
- Telegram показывает все пять status buttons и сначала присваивает `task.Status`, только затем запускает save; это повторяет desktop optimistic-mutation problem.
- `TaskAvailabilityParityTests` сравнивает service с `TaskAvailabilityAnalyzer`, который является wrapper того же service; parity UI/picker не доказана.
- `MainControlTaskStatusIconUiTests` сравнивает flyout с `AvailableStatusTransitionOptions` той же ViewModel; duplicate policy может быть неверной и тест всё равно зелёный.
- `TaskStatusDomainTests` прямо закрепляет `Archived(previous InProgress) -> InProgress`.
- AppAutomation `MainWindowPage` / `MainWindowScenariosBase` не имеют row-scoped status picker, transition options и archive/unarchive сценария.
- `README.md` и `README.RU.md`:
  - показывают запрещённые `Completed/Archived -> InProgress`;
  - связывают future date с dimming, хотя opacity зависит только от graph availability;
  - называют `Unlocked` «доступными для выполнения», хотя future tasks и некоторые completed projections могут туда попасть;
- Прежняя spec обещает `Any -> InProgress`, `Archived -> Any`, disabled reasons и общий Telegram guard, поэтому больше не является точным current contract.

Freshness evidence:
- `git fetch origin --prune` выполнен 2026-07-17.
- branch `fix/status-availability-contract` создана непосредственно от `origin/main` commit `5aebebc`.
- latest release остаётся `1.27.0`; stage 2 не зависит от asset inventory.
- PR #274 содержит master/stage-1 specs и README install correction; текущая branch не включает его commit. Stage-2 EXEC запрещён до merge PR #274, после чего обязательны `git fetch origin --prune`, rebase на новый `origin/main`, проверка ancestry merge commit и повтор всего characterization baseline.

## 3. Проблема
У приложения есть канонический engine guard, но пользовательские entry points вычисляют или применяют переходы самостоятельно. Из-за этого UI и Telegram предлагают запрещённые действия, unarchive обходит собственную матрицу, тесты используют дублирующую реализацию как oracle, а README документирует несуществующее поведение.

## 4. Цели дизайна
- Разделение ответственности: lifecycle state, graph availability, start guard и completion guard остаются отдельными понятиями.
- Повторное использование: одна pure policy и один набор reason codes используются всеми entry points.
- Тестируемость: матрица 5x5, guard facts, no-mutation и unarchive normalization проверяются data-driven tests.
- Консистентность: engine, desktop, Telegram, UI automation и docs говорят одно и то же.
- Обратная совместимость: persisted enum/history/JSON не меняются; legacy history нормализуется только при unarchive.
- Объяснимость UI: denied targets видны disabled и содержат локализованную inline-причину/HelpText; current target не дублируется.
- Минимальный diff: без redesign иконок, tabs, filters, storage или DTO.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем, не переименовываем и не удаляем статусы.
- Не меняем JSON schema, migrations, interface/server DTO, molds и history entry shape.
- Не меняем семантику relation graph, `IsCanBeCompleted`, completion criteria или planned dates.
- Не переделываем status icons, tabs, filters, card layout или graph layout.
- Не добавляем persistence выбранных filters между перезапусками.
- Не исправляем status-history author fallback (`DisplayName/UserId/Git/local`) в этом package.
- Не меняем Markdown import markers; только уточняем export-setting contract в README.
- Не вводим новые ограничения на добавление children/blockers к terminal tasks.
- Не переписываем всю старую status spec; добавляем явную errata/supersession note.
- Не меняем общую Telegram localization architecture; scope ограничен status buttons/denial response.
- Не исправляем весь root README: только status/availability/Unlocked/marker statements в обеих локалях.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `Unlimotion.Domain` -> pure `TaskStatusTransitionPolicy`, immutable input facts, stable denial reason codes и unarchive normalization; никаких UI strings и persistence side effects.
- `TaskAvailabilityService` -> вычисляет authoritative graph/start/complete facts и делегирует решение policy.
- `TaskGraphCommandService` -> execution boundary: заново читает graph/status/date/criteria, применяет policy, сохраняет clone и возвращает structured `TaskOperationResult`; для `ITaskGraphWriteLock` это один atomic critical section, для server storage — best-effort verified command без cross-client CAS.
- `ServerStorage` -> реализует `ITaskGraphDiagnosticStorage.ReadGraphAsync` через уже существующий GetAll REST contract с propagation read failure; wire DTO/hub methods не меняются. Он намеренно не объявляется `ITaskGraphWriteLock`.
- `TaskTreeManager.UpdateTask` -> сохраняет прежнюю generic mixed-update semantics; no-write guarantee к этому API целиком не применяется.
- `ITaskStorage` / `UnifiedTaskStorage` -> новый storage-backed `TrySetStatusAsync(taskId, target, author)` поверх `TaskGraphCommandService`; same-client calls сериализуются local gate, cache hydrate выполняется из authoritative command snapshot, не из предварительно мутированной ViewModel.
- `TaskItem` -> получает normalized restore helper на основе pure policy, без raw restore `InProgress`/terminal legacy states.
- `TaskItemViewModel` -> использует cached facts только для preview options и предоставляет async `TryTransitionToStatusAsync`; picker/archive/cascade не присваивают `Status` до успешного storage result.
- `MainWindowViewModel` -> `Ctrl+D` вызывает тот же async method, а не присваивает `Completed` перед generic `Update`.
- `TaskStatusPicker` -> current status не показывает; остальные четыре options видны, denied disabled с видимой краткой причиной, supplementary tooltip, `AutomationProperties.HelpText` и stable AutomationId.
- `MainControl.axaml` / ViewModel resources -> команда называется `Archive` или `Unarchive` по текущему status; automation identity остаётся стабильной.
- `Bot` -> status keyboard берёт только enabled non-current targets из shared preview; callback вызывает storage-backed command и не присваивает `Status` локально.
- Tests -> policy является oracle; UI больше не сравнивается с собственной duplicate collection без независимого expected contract.
- README EN/RU -> точная матрица и отдельные определения lifecycle/availability/start/complete.
- Old status spec -> короткий supersession/errata block со ссылкой на эту child spec; исторический journal не переписывается.

### 6.2 Детальный дизайн

Pure contract (точные имена могут быть скорректированы без изменения semantics):

```csharp
public readonly record struct TaskStatusTransitionFacts(
    TaskStatus CurrentStatus,
    bool IsGraphAvailable,
    bool PlannedBeginIsFuture,
    bool CompletionCriteriaSatisfied);

public enum TaskStatusTransitionDenialReason
{
    None,
    TerminalCannotStart,
    GraphUnavailableForStart,
    FutureDatePreventsStart,
    TerminalCannotComplete,
    GraphUnavailableForCompletion,
    CompletionCriteriaIncomplete,
    CompletedCannotArchive,
    InvalidTargetStatus
}

public readonly record struct TaskStatusTransitionEvaluation(
    bool IsAllowed,
    TaskStatusTransitionDenialReason Reason);
```

Policy invariants:
- pure evaluation сохраняет все текущие raw `TaskAvailabilityService` results для 25 defined source/target pairs, включая неоднородные diagonal results;
- dedicated `TaskGraphCommandService` после успешных storage read, graph write-safety validation и task lookup проверяет same source/target до raw evaluation и превращает все пять diagonal cases в effective no-op без history/storage change; storage/validation/not-found errors не маскируются no-op;
- для пяти defined statuses off-diagonal rules совпадают с текущим `TaskAvailabilityService` и являются единственным reusable matrix source;
- `NotReady`/`Prepared` target -> allowed;
- `Archived` target -> allowed except source `Completed`;
- `InProgress` target -> source не terminal, graph available, planned begin не future;
- `Completed` target -> source не `Archived`, source не terminal, graph available, criteria satisfied;
- command ordering: storage read -> graph write-safety validation -> task lookup -> `Enum.IsDefined(requestedTarget)` -> valid same-status no-op -> raw policy; поэтому persisted source и requested target с одинаковым undefined value всё равно denied как invalid target;
- undefined requested target -> `InvalidTargetStatus`, denied до equality/no-op;
- undefined persisted source не считается undefined target и сохраняет current recovery semantics: `NotReady`/`Prepared`/`Archived` доступны, а `InProgress`/`Completed` зависят от обычных graph/date/criteria facts; это покрывается отдельными tests с `(TaskStatus)int.MaxValue`;
- deterministic denial priority:
  - source/target terminal rule;
  - graph availability;
  - future date для start;
  - completion criteria для complete.

Preview и mutation boundary:
- preview options разрешено вычислять из текущего cache snapshot; они не являются разрешением на запись;
- `ITaskStorage.TrySetStatusAsync(string taskId, TaskStatus requestedStatus, string? author = null)` возвращает `TaskOperationResult` и является единственным новым status-write API для desktop и Telegram;
- `UnifiedTaskStorage` вызывает `new TaskGraphCommandService(TaskTreeManager.Storage).TrySetStatusAsync(...)`; для FileStorage command выполняет read/re-evaluate/mutation/verification внутри `ITaskGraphWriteLock`, для `ServerStorage` — через новый diagnostic read без cross-client lock;
- `TaskOperationResult` additively содержит cloned `TaskItem? AuthoritativeTask`: текущий persisted snapshot для no-op/deny и post-write snapshot для verified success; `ChangedTasks` остаётся списком реально записанных tasks;
- при наличии `AuthoritativeTask` `UnifiedTaskStorage` hydrate соответствующий cached ViewModel даже при no-op/deny; это cache reconciliation без storage mutation. При success также применяются `ChangedTasks` и refresh relations;
- same-status success возвращает пустой `ChangedTasks`, но authoritative snapshot; history/version/file не меняются, а stale cache может и должен обновиться до persisted truth;
- business-rule deny возвращает stable policy reason, `Before` и authoritative snapshot; history/version/file не меняются, cache может reconcile stale display;
- `StorageFailed` не маскируется success; `OutcomeUnknown` не обещает no-write и может не иметь authoritative snapshot. Тогда adapter делает explicit `Storage.Load(taskId)`; при success hydrate cache, при повторном failure оставляет cache как есть и показывает «итог неизвестен», а не success/rollback;
- stale graph, planned date, criteria или current status между открытием flyout и click учитываются повторной command-level проверкой;
- duplicate Telegram callback становится no-op либо deny по фактическому storage state и не создаёт повторную history entry.

Storage capability / concurrency contract:
- FileStorage и другие `ITaskGraphWriteLock` implementations дают strong same-storage atomicity для read/evaluate/write/verify;
- `ServerStorage.ReadGraphAsync` повторно использует существующий GetAll endpoint, но не скрывает network/read exception как пустой graph;
- `UnifiedTaskStorage` local `SemaphoreSlim` предотвращает гонку двух status commands из одного client instance;
- существующий server protocol не имеет compare-and-swap/server-authoritative status method, поэтому гонка разных клиентов остаётся неатомарной. Command обязан post-read verify: verified target -> success, расхождение/непроверяемый результат -> `OutcomeUnknown`, cache reconciliation attempt и user-visible warning;
- Stage 2 не обещает cross-client atomic no-write для server mode и не ухудшает его до unconditional `StorageFailed`; отдельный follow-up `server-authoritative-status-command` требуется, если roadmap позднее потребует эту гарантию;
- tests покрывают locked FileStorage, non-locking diagnostic fake и `ServerStorage.ReadGraphAsync` failure propagation; README не делает concurrency claims.

Generic mixed-update compatibility:
- существующие `ITaskStorage.Update` / `TaskTreeManager.UpdateTask` остаются API для title/description/date и других mixed edits;
- `same status + title change` продолжает сохранять title;
- `denied requested status + title change` сохраняет прежнее поведение manager: non-status payload может сохраниться, а status восстанавливается в прежний persisted source — даже если этот source сам undefined; этот partial legacy result явно характеризуется тестом;
- новые status entry points не используют generic mixed update, поэтому structured success/deny не выводится из этого partial behavior;
- правило `undefined target always denied` относится к dedicated status API/raw policy; generic `UpdateTask` остаётся compatibility path с прежним partial behavior;
- wire DTO/JSON schema не меняются, но публичный .NET interface `ITaskStorage` расширяется dedicated status method; все test doubles обновляются.

Diagnostic envelope:
- pure Domain reason остаётся coarse (`GraphUnavailableForStart` / `GraphUnavailableForCompletion`) и не зависит от `TaskAvailabilityReasonKind`;
- `TaskStatusTransitionDecision` содержит Domain `TaskStatusTransitionEvaluation` рядом с существующим `TaskAvailabilityAnalysis`; raw-service diagonal characterization сохраняется;
- `TaskOperationDeniedReason` additively получает nullable `TaskStatusTransitionDenialReason? StatusTransitionReason`; `TaskGraphCommandService` заполняет его для status deny, поэтому consumers не парсят English `Message`;
- `TaskOperationResult.Before.Reasons` сохраняет существующие structured diagnostics для incomplete contained task, direct blocker и inherited blocker;
- desktop/Telegram mapper сочетает policy reason с engine diagnostics для точной copy, не переносит TaskTreeManager types в Domain и имеет отдельные tests для трёх graph cases.

Localization contract:
- Domain reason codes не содержат customer-facing text.
- Desktop resources получают EN/RU строки для каждого denial reason.
- Picker tooltip и error toast используют один mapper.
- Telegram использует те же reason codes, но текущий Russian-only bot copy остаётся в его существующей localization boundary.

Picker contract:
- список содержит все четыре non-current statuses;
- allowed item enabled;
- denied item disabled;
- denied row постоянно показывает краткую локализованную причину рядом со status text; причина не зависит от возможности сфокусировать disabled item;
- `ToolTip.ShowOnDisabled=true` и tooltip повторяют полную причину для pointer, но являются supplementary channel;
- `AutomationProperties.HelpText` содержит ту же причину; Headless проверяет HelpText, а FlaUI проверяет, что disabled row остаётся в accessibility tree;
- current status отображается иконкой/tooltip самого picker, но отсутствует в flyout;
- AutomationId остаётся `TaskStatusOption{Status}` для каждого target;
- keyboard navigation не фокусирует disabled item как actionable command, при этом видимый reason text доступен сразу после открытия flyout; если FlaUI докажет, что HelpText disabled row отсутствует из accessibility tree, обязательный fallback — отдельная focusable non-actionable reason summary в flyout;
- открытие/закрытие flyout не меняет state.

Unarchive normalization (утверждённый contract):

| Last valid non-archived history status | Restore target | Reason |
| --- | --- | --- |
| `NotReady` | `NotReady` | Сохранить явную неподготовленность |
| `Prepared` | `Prepared` | Сохранить готовность |
| `InProgress` | `Prepared` | Не обходить terminal -> `InProgress` guard |
| `Completed` | `NotReady` | Legacy/inconsistent terminal history; безопасный активный fallback |
| отсутствует / повреждён / неизвестен | `NotReady` | Консервативный fallback без выдумывания готовности |

Additional unarchive rules:
- normalizer получает explicit `now`/test clock; valid restore entry: non-null, defined enum, status не `Archived` и `ChangedAt <= now + 5 minutes` (bounded clock-skew tolerance);
- выбирается valid entry с максимальным `ChangedAt`; при одинаковом timestamp выигрывает запись с большим исходным list index;
- более новая null/undefined/`Archived`/far-future entry игнорируется, поэтому более старая valid non-archived entry всё ещё может определить restore target;
- raw null/invalid/future entries сохраняются семантически и в исходном list order при обычной serialization; normalizer их не удаляет и не переписывает;
- `CloneStatusHistoryEntry`, `LastChangedAt`, `LastNonArchivedStatus`/replacement, `SetStatusHistoryTimestamp` и `EnsureStatusHistory` становятся null-safe;
- после `SetStatus` `EnsureStatusHistory` сначала проверяет последний физический non-null entry: если он уже равен current status, повторная запись не добавляется даже при существующей future-dated legacy entry; accepted command добавляет ровно одну новую entry;
- normalization применяется отдельно к parent и каждому confirmed child;
- отказ пользователя от cascade не меняет children;
- parent unarchive не зависит от согласия на children cascade;
- каждый реально изменённый task получает ровно одну новую history entry через существующий save path;
- raw history не переписывается и не мигрируется;
- если save rejected, UI показывает ошибку и возвращает фактический persisted status, без success-toast.
- parent transition выполняется и awaits первым; только после его success показывается children confirmation;
- `INotificationManagerWrapper` additively получает generic `ConfirmAsync(header, message)`. `NotificationManagerWrapper` завершает его exactly once через `TaskCompletionSource<bool>(RunContinuationsAsynchronously)` и `DialogHost.Show` completion: yes -> `true`, no/click-away/dialog close -> `false`, infrastructure exception -> faulted task; status writes не запускаются fire-and-forget из sync `Action`;
- `ArchiveCommand` становится `ReactiveCommand.CreateFromTask`; при `NotificationManager == null` или false/dismiss parent success сохраняется, children не меняются. Confirmation exception ловится, показывает error, children не меняются; hang невозможен;
- при подтверждении children обрабатываются последовательно в стабильном visible traversal order; каждая задача атомарна отдельно, cross-task transaction не обещается;
- mixed failure не откатывает parent или уже сохранённых children: остальные confirmed children всё равно обрабатываются, failed children сохраняют фактический persisted status, затем показывается summary success/failure counts.

Telegram contract:
- current status остаётся в текстовой части карточки;
- keyboard не показывает current/denied status buttons, потому что Telegram не поддерживает disabled buttons с tooltip;
- callback со stale/подделанным denied target повторно проверяется policy;
- denied callback отвечает причиной и не мутирует task;
- allowed callback вызывает `ITaskStorage.TrySetStatusAsync`, awaits structured result и показывает фактический refreshed status.

README contract:
- диаграмма/таблица не показывает `Completed/Archived -> InProgress` и `Completed -> Archived`;
- отдельные определения:
  - lifecycle status = persisted `Status`;
  - graph availability = children/blockers, cached as `IsCanBeCompleted`;
  - start guard = graph + future date + non-terminal source;
  - completion guard = graph + criteria + non-terminal source;
- future date запрещает start, но не dimming;
- dimming `0.4` зависит только от graph unavailable;
- `Unlocked` означает graph-unblocked и не обещает, что task можно начать прямо сейчас;

Visual planning artifact — утверждённый disabled/`NotReady` storyboard:

| Frame | Task state | Picker / command | Visual/automation assertion |
| --- | --- | --- | --- |
| A | `Prepared`, graph available, no future date, criteria satisfied | Current omitted; `NotReady`, `InProgress`, `Completed`, `Archived` enabled | Opacity `1`; `TaskStatusOptionInProgress` enabled |
| B | `Completed` | `NotReady`, `Prepared` enabled; `InProgress`, `Archived` disabled with reasons | Current omitted; no enabled terminal bypass |
| C | `Archived`, previous `InProgress` | `NotReady`, `Prepared` enabled; `InProgress`, `Completed` disabled; menu label `Unarchive` | Click `Unarchive` -> visible `Prepared`, one history entry |
| D | `Prepared`, future planned begin | `InProgress` disabled with future-date reason; `Completed` follows only graph/criteria | Opacity stays `1` |
| E | `Prepared`, active blocker | `InProgress` and `Completed` disabled with graph reason; `Archived` enabled | Opacity `0.4` |
| F | `InProgress` loses graph availability | Automatic visible status becomes `Prepared` once | History author `System`; no repeated entries |

UI test video evidence:
- Before artifact: `artifacts/ui-tests/status-contract/before-terminal-unarchive.mp4`.
  - автоматизированный FlaUI flow показывает enabled `InProgress` у terminal task и неуспешный unarchive previous `InProgress`.
- After artifact: `artifacts/ui-tests/status-contract/after-terminal-unarchive.mp4`.
  - тот же seed/scenario показывает disabled terminal targets с reason и успешный normalize to `Prepared`.
- Supporting screenshots:
  - `artifacts/ui-tests/status-contract/before-terminal-picker.png`;
  - `artifacts/ui-tests/status-contract/after-terminal-picker.png`;
  - `artifacts/ui-tests/status-contract/after-future-vs-blocked.png`.
- Capture выполняется через `record-app-screen`/реальный синхронный `record_app_window.ps1` вокруг конкретного FlaUI test run. Repo wrapper принимает `-RecorderScriptPath`; если он не задан, разрешает только `$env:CODEX_HOME\skills\record-app-screen\scripts\record_app_window.ps1`, а при отсутствии пути останавливается с точной ошибкой.
- Test владеет `window-ready.json` и `scenario-complete.json`; wrapper владеет `scenario-go.signal` и `recording-finished.signal`. Test запускает видимое окно, устанавливает outer geometry `1280x800`, пишет exact title/process/rect в ready JSON и ждёт go.
- Test задаёт уникальный GUID-suffixed window title; ready JSON содержит PID/title/outer rect, а wrapper до запуска recorder проверяет, что этот PID владеет ровно одним visible top-level window с тем же title/rect, и передаёт recorder полный уникальный title. Совпадение только по общему process name не принимается.
- Wrapper запускает recorder отдельным `pwsh` process с `DurationSeconds` строго больше scenario timeout плюс 10 секунд, ждёт живой descendant `ffmpeg` и созданный nonempty output, затем пишет go; recorder снимает `30 fps` без audio/cursor scope change. `scenario-complete.json` обязан появиться, пока recorder и `ffmpeg` ещё живы; ранний exit делает capture failed. После полного flow wrapper дожидается штатного recorder exit, пишет результат/finished, и только после этого test выполняет assertions/закрывает окно.
- Один и тот же `StatusContract_TerminalPickerAndUnarchive` собирает observations и screenshots до assertions; все assertions выполняются после полного interaction flow, поэтому ожидаемый pre-fix failure не закрывает окно до записи unarchive шага.
- Before/after используют один seed, light theme, geometry и interaction order; отдельный dark-theme Headless render проверяет контраст без второго обязательного видео.
- MP4 проверяется `ffprobe` на readable video stream, geometry, frame rate и duration `> 0`; SHA-256, duration, resolution, command и локальный repo-relative path записываются в Post-EXEC/PR Validation.
- Видео и screenshots остаются ignored local-only artifacts по master policy и не коммитятся; reviewer evidence — проверенные локальные paths + hashes/metadata в PR. Если remote review потребует downloadable artifact, это stop/ASK-HUMAN для отдельного upload mechanism, а не silent commit крупного бинарника.
- Fallback разрешён только если recorder/window capture объективно не может привязаться к test-host window: сохранить точную ошибку, FlaUI/Headless screenshots, test log и причину в Post-EXEC review. Отсутствие времени не является причиной.

Границы сохранения поведения:
- status enum/order/icons/tabs/filter semantics остаются прежними;
- off-diagonal allowed engine transitions для пяти defined statuses не расширяются и не сужаются; diagonal определяется dedicated command no-op, а не raw service result;
- presentation denied targets меняется `hidden -> disabled + reason`; normalization previous `InProgress -> Prepared`, а previous `Completed`/missing/corrupt -> `NotReady` обязательны;
- `Ctrl+D` остаётся completion shortcut и использует тот же policy;
- archive modal/cascade UX сохраняется, кроме правильного restore target;
- preview performance ограничен pure evaluations; commit path сохраняет существующую graph-read nature и добавляет post-write verification/cache snapshot.

Обработка ошибок:
- invalid/stale desktop или Telegram target -> deny + localized reason, no mutation;
- save failure -> existing error surface без предварительной UI mutation; `OutcomeUnknown` отдельно инициирует re-read и не выдаётся за гарантированный rollback;
- missing/corrupt history -> утверждённый legacy fallback `NotReady`;
- unknown reason code -> generic localized blocked message и test failure для unmapped known values;
- duplicate history write -> blocking regression.

Производительность:
- policy O(1), allocation-free либо с минимальными immutable values;
- picker preview не делает storage/network call;
- dedicated status command остаётся O(N) по graph, как текущий manager guard, и запускается только по явному action; verified result может потребовать post-read;
- server mode использует существующие GetAll/Load/Save endpoints и поэтому добавляет network round trips относительно optimistic UI path; command disabled/busy state блокирует duplicate click, а latency фиксируется в test log;
- для simple non-repeater server command budget: stable deny/no-op не более одного top-level GetAll; verified allowed transition не более трёх (preflight, manager re-evaluation, post-verify). Relation-specific loads и repeater side effects считаются отдельно в evidence;
- background scans, migrations и новые endpoints не добавляются; call-count tests защищают от случайного сверх оговорённого дублирования.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Terminal picker | Открыть picker у `Completed` | `InProgress`/`Archived` видны disabled с объяснением; `NotReady`/`Prepared` доступны | Domain + Headless + FlaUI + video | S2-AC-01..04 |
| Archived picker | Открыть picker у `Archived` | `InProgress`/`Completed` disabled; активные targets доступны | Policy + Headless screenshot | S2-AC-02..04 |
| Unarchive after work | `InProgress -> Archived`, затем `Unarchive` | Status становится `Prepared`, history содержит одну новую запись | Domain/ViewModel/FlaUI/video | S2-AC-05, S2-AC-06 |
| Legacy unarchive | Разархивировать task с previous `Completed` или без history | Status = `NotReady`, приложение не падает | Data-driven domain test | S2-AC-05 |
| Child cascade | Подтвердить/отклонить unarchive children | Подтверждённые children нормализованы каждый отдельно; отказ оставляет их archived | ViewModel/UI tests | S2-AC-06 |
| Future start | Открыть future `Prepared` task | `InProgress` disabled; opacity остаётся `1` | Availability + Headless + screenshot | S2-AC-07 |
| Blocked task | Открыть graph-blocked task | `InProgress`/`Completed` disabled; opacity `0.4` | Direct/inherited blocker tests + UI | S2-AC-08 |
| Criteria incomplete | Открыть available task с unchecked criteria | `Completed` disabled, `InProgress` не блокируется criteria | Policy/UI tests | S2-AC-09 |
| Lost availability | Добавить active blocker к `InProgress` | Автоматический `Prepared` ровно один раз | Manager tests | S2-AC-10 |
| Telegram status | Открыть task и выбрать status / отправить stale callback | Только allowed buttons; denied callback не меняет task и возвращает reason | Handler test + bot build/log | S2-AC-11 |
| Read task model | Открыть README EN/RU | Различимы lifecycle/availability/start/complete и точная matrix | Paired docs review/GFM render | S2-AC-12, S2-AC-13 |

### 6.4 State / Interaction Matrix

- `Start` = graph available + planned begin не future + source не terminal.
- `Complete` = graph available + criteria satisfied + source не terminal.

Таблица ниже — **effective dedicated status-command matrix для существующей задачи в write-safe graph после успешного read**. Off-diagonal cells отражают raw `TaskAvailabilityService` rules; diagonal cells являются command-level short-circuit в `TaskGraphCommandService` и отдельно характеризуются для всех пяти statuses. Raw service не обязан возвращать allow на каждой diagonal cell; storage/validation/not-found errors имеют приоритет над таблицей.

| Current \ Target | `NotReady` | `Prepared` | `InProgress` | `Completed` | `Archived` |
| --- | --- | --- | --- | --- | --- |
| `NotReady` | no-op | allow | `Start` | `Complete` | allow |
| `Prepared` | allow | no-op | `Start` | `Complete` | allow |
| `InProgress` | allow | allow | no-op | `Complete` | allow |
| `Completed` | allow | allow | deny terminal | no-op | deny terminal |
| `Archived` | allow | allow | deny terminal | deny terminal | no-op |

Interaction notes:
- same-status target не показывается в UI и не пишет history;
- denied desktop target виден disabled с reason; denied Telegram target отсутствует в keyboard и повторно проверяется при callback;
- concurrent/stale action всегда re-evaluates facts непосредственно перед mutation;
- failure не маскируется последующим rollback как success;
- `Archived` и `Completed` остаются terminal/complete для dependency graph.

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Каноническая matrix | user + roadmap | Сохранить current off-diagonal `TaskAvailabilityService` rules + current command-level diagonal no-op | 0.99 | Изменение привычных allowed flows | Нет; задано roadmap |
| Denied desktop targets | user | Выбрано: показывать disabled + всегда видимый localized reason/HelpText; current status скрывать | 1.00 | Более длинный flyout, отличие от current hidden UX | Нет; утверждено 2026-07-17 |
| Unarchive previous `InProgress` | user + roadmap | Normalize to `Prepared` | 0.99 | Пользователь ожидал resume | Нет |
| Legacy previous `Completed` | user | Выбрано: normalize to `NotReady` | 1.00 | Потеря inferred readiness | Нет; утверждено 2026-07-17 |
| Missing/corrupt history | user | Выбрано: `NotReady` (совпадает с current missing-history fallback) | 1.00 | Более осторожный status | Нет; утверждено 2026-07-17 |
| Child cascade | agent | Та же normalization per child; отказ оставляет children archived | 0.95 | Существующие данные могут иметь mixed history | Нет |
| Telegram | user | Выбрано: включить в Stage 2 как реальный обход shared policy | 1.00 | Scope шире минимального desktop package | Нет; утверждено 2026-07-17 |
| Old spec | agent | Errata/supersession note, не historical rewrite | 0.98 | Старый текст всё ещё длинный | Нет |
| Persistence | user + roadmap | Без schema/data migration | 1.00 | Legacy history остаётся в raw виде | Нет |
| Generic mixed update | agent | Сохранить legacy partial semantics; status commands используют dedicated API | 0.96 | Публичный API остаётся неоднородным | Нет; это compatibility boundary |
| Undefined persisted source | agent | Сохранить текущие target-based recovery rules | 0.97 | Corrupt data остаются recoverable, но требуют diagnostics | Нет; behavior-preserving |
| Future history validity | agent | Игнорировать entry дальше `now + 5 min`, raw entry не удалять | 0.91 | Сильно рассинхронизированные часы могут изменить выбранный fallback | Нет; conservative corrupt-data boundary |
| Server concurrency | agent | Existing endpoints + diagnostic/verified fallback; не заявлять cross-client atomicity | 0.95 | Межклиентская гонка остаётся `OutcomeUnknown` | Нет; wire expansion вынесен follow-up |
| Confirmation API | agent | Additive exactly-once `ConfirmAsync` вместо TCS поверх `Ask` | 0.98 | Дополнительные implementers/test doubles | Нет; устраняет hang/fire-and-forget |
| PR #274 dependency | governance + roadmap | Green/ready/merged, затем rebase до production edits | 1.00 | Merge conflict в status paragraph | Нет |
| Video evidence | governance | Attempt before/after recording; documented objective fallback only | 1.00 | Recorder может не привязаться к окну | Нет |

Три product choices закрыты пользователем 2026-07-17 вместе с точной approval-фразой: disabled-with-reason, legacy fallback `NotReady`, Telegram included. Материально отличающихся user-owned решений перед EXEC больше нет.

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Lifecycle enum | `TaskStatus.cs` | Без изменений | Полная | Enum snapshot/test |
| Transition matrix | switch в `TaskAvailabilityService`; command diagonal short-circuit; duplicate в VM/bot | Pure Domain off-diagonal policy + dedicated command no-op + adapters | Effective command behavior сохраняется | raw-policy + 5 diagonal data-driven tests |
| Graph availability | `TaskAvailabilityService.Analyze`, cached `IsCanBeCompleted` | Без semantics change | Полная | direct/inherited blocker tests |
| Start guard | service + duplicate VM | Shared facts/policy | Behavior engine сохраняется, UI исправляется | future/terminal/UI tests |
| Completion guard | service + duplicate VM | Shared facts/policy | Behavior engine сохраняется, UI исправляется | criteria/terminal tests |
| Unarchive | raw last non-archived history | normalization matrix | No bulk rewrite; action-time normalization | domain + parent/child UI tests |
| Desktop picker | hidden denied options | четыре non-current rows; denied disabled с inline reason/HelpText | AutomationIds сохраняются | Headless + FlaUI |
| Status mutation | generic optimistic ViewModel update | `ITaskStorage.TrySetStatusAsync` + structured result/cache refresh | Public .NET API additive; wire schema unchanged | stale/save/no-mutation tests |
| FileStorage command | Existing diagnostic read + directory lock available | Atomic read/evaluate/write/verify | No data migration | locked integration tests |
| ServerStorage command | Existing GetAll/Load/Save, no diagnostic interface/CAS | Diagnostic read via existing endpoint + verified best-effort command | No wire change; cross-client atomicity not claimed | injected fetch + non-locking/failure tests |
| Cascade confirmation | Sync callback-only `Ask` | exactly-once `ConfirmAsync` | Public in-process API additive | yes/no/dismiss/exception/null tests |
| Telegram | all buttons + optimistic mutation | enabled targets only + storage-backed command | Callback payload format сохраняется | handler test + bot build |
| Persistence/history | existing JSON/history | Без schema change; одна запись на accepted transition | No migration | serialization/no-mutation assertions |
| README | смешанные claims | paired exact contract | Docs-only | structural/source review |

## 7. Бизнес-правила / Алгоритмы
1. Lifecycle status, graph availability, start guard и completion guard — четыре разные оси.
2. `Completed` и `Archived` являются terminal и complete для dependency graph.
3. `Completed/Archived -> InProgress` запрещён.
4. `Completed -> Archived` и `Archived -> Completed` запрещены.
5. `NotReady` и `Prepared` являются безопасными active targets из любого status.
6. Start требует graph available, non-terminal source и planned begin не future.
7. Complete требует graph available, non-terminal source и все criteria satisfied; future begin не блокирует complete сам по себе.
8. Для locked storage либо stable server snapshot dedicated business-rule deny/no-op не меняет status, history, timestamps, version и persisted file; cross-client server race классифицируется `OutcomeUnknown`, а generic mixed `UpdateTask` сохраняет прежнюю partial semantics.
9. Все status entry points не делают optimistic assignment и проходят authoritative storage-backed re-evaluation при stale/concurrent action.
10. Future begin не меняет `IsCanBeCompleted`, `Unlocked` membership и opacity.
11. Graph unavailable даёт opacity `0.4`; future-only task остаётся `1`.
12. `InProgress`, потерявшая start availability, автоматически становится `Prepared` ровно один раз с `Author=System`.
13. Archived direct и inherited blocker не блокирует dependants.
14. Unarchive previous `InProgress` -> `Prepared`; previous `Completed`/missing/corrupt -> `NotReady`.
15. Current status отсутствует в picker; denied desktop target виден disabled с reason; denied Telegram target отсутствует в keyboard.
16. Child cascade применяет normalization только после явного confirmation.
17. README EN/RU customer-facing contract меняется синхронно.
18. Undefined target всегда denied; undefined persisted source сохраняет текущие target-based recovery rules, а undefined history entry игнорируется normalizer.
19. Invalid-target validation выполняется до same-value no-op; no-op разрешён только для пяти defined statuses после успешного read/validation/task lookup.
20. Status result hydrate stale cache из authoritative snapshot без записи в storage; отсутствие snapshot при `OutcomeUnknown` требует explicit reload attempt и warning.

## 8. Точки интеграции и триггеры
- `TaskAvailabilityService.Analyze` -> формирует shared facts.
- `TaskAvailabilityService.EvaluateStatusTransition` -> делегирует pure policy.
- `TaskGraphCommandService.TrySetStatusAsync` -> под write lock re-evaluate и применяет только allowed decision.
- `UnifiedTaskStorage.TrySetStatusAsync` -> command adapter + cache refresh.
- `TaskTreeManager.UpdateTask` -> legacy generic mixed update, не используется новыми status entry points.
- `TaskItemViewModel.RefreshStatusOptions` -> preview обновляется при status, graph availability, planned begin и completion criteria changes.
- `TaskItemViewModel.StatusOption`, `MainWindowViewModel.Ctrl+D`, `ArchiveCommand`, async child cascade -> один storage-backed async transition method.
- `TaskStatusPicker.BuildStatusFlyout` -> строит четыре non-current items, включая disabled denied targets с reason.
- `MainControl` context menu -> reactive Archive/Unarchive label.
- Telegram `ShowTask` -> enabled targets only; callback -> validated method.
- README/status spec -> обновляются в одном PR после production/tests.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- `TaskStatus`, `StatusHistory`, `CompletionCriteria`, dates и relation lists не меняются.
- Добавляются только runtime value types/policy/reason codes.
- `ITaskStorage` получает публичный .NET method `TrySetStatusAsync`; wire DTO/JSON schema не меняются.
- Legacy history не переписывается массово.
- Accepted unarchive записывает существующую обычную status history entry; denied/no-op не пишет запись.
- Wire DTO/JSON compatibility: без изменений; public in-process .NET `ITaskStorage` API расширяется additively и требует обновить implementers/test doubles.

## 10. Миграция / Rollout / Rollback
- Миграция данных: Не применимо; schema не меняется.
- Rollout:
  1. Дождаться merge PR #274, fetch/rebase и повторить baseline до production edits.
  2. Зафиксировать failing regression tests/current visual baseline.
  3. Добавить pure policy, dedicated storage command и engine parity.
  4. Перевести desktop entry points/unarchive.
  5. Перевести Telegram keyboard/callback на shared storage-backed contract.
  6. Обновить UI automation и visual evidence.
  7. Обновить README/old-spec errata.
- Existing tasks:
  - raw history сохраняется;
  - normalization выполняется при следующем unarchive;
  - previous `Completed`/invalid/missing history нормализуется в `NotReady`.
- Rollback:
  - один revert stage-2 PR возвращает прежнюю policy/presentation и является только code rollback;
  - accepted transitions, выполненные до rollback, уже записали пользовательские history entries; revert их не удаляет и автоматический data rollback не выполняется;
  - schema не меняется, поэтому эти history entries остаются читаемыми предыдущей версией, но пользовательское состояние после rollback не обещает автоматически вернуться к pre-release snapshot.
- Delivery contract:
  - branch: `fix/status-availability-contract`;
  - PR title: `fix(status): align transition and availability contract`;
  - PR body содержит `Summary`, `Changes`, `Validation`, `UI automation evidence`, `Risks / Rollback`, `Release-note handoff`, `Links`;
  - `Validation` содержит exact targeted/full commands, required GitHub checks и executed counts;
  - `UI automation evidence` содержит before/after local-only paths, SHA-256, `ffprobe` metadata и visual-review verdict;
  - release-note handoff text: «Исправлена согласованность переходов статусов: запрещённые terminal-переходы больше не предлагаются как успешные, разархивация после `InProgress` возвращает задачу в `Prepared`, повреждённая/отсутствующая legacy history — в `NotReady`, а Telegram использует тот же проверяемый контракт.»;
  - отдельный changelog/release файл в этом PR не создаётся: release-note artifact — явный PR body handoff для следующего SemVer release.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria
- **S2-AC-01:** Pure policy data-driven воспроизводит текущие 25 raw-service results и является единственным reusable off-diagonal source; dedicated command отдельно short-circuits все пять same-status cases как no-op; raw-service и effective-command semantics не смешиваются.
- **S2-AC-02:** `Completed/Archived -> InProgress`, `Completed -> Archived`, `Archived -> Completed` denied через service, command manager, desktop и stale Telegram callback.
- **S2-AC-03:** Все входящие в Stage 2 user status writes идут через storage-backed `ITaskStorage.TrySetStatusAsync`; preview не мутирует ViewModel. Dedicated stable deny/no-op не меняет status/history/timestamps/version/file и возвращает `AuthoritativeTask` для stale-cache hydration; stale graph/date/criteria/status, save failure, outcome-unknown, invalid-equals-invalid и duplicate Telegram callback имеют structured result tests. Generic `UpdateTask` отдельно сохраняет текущие `same status + title` и denied-status mixed-update semantics.
- **S2-AC-04:** Desktop picker скрывает current status, показывает остальные четыре, allowed enabled, denied presentation соответствует решению пользователя; для disabled-варианта reason постоянно видим, RU/EN локализован, имеет `ShowOnDisabled`, HelpText и stable AutomationIds, проверенные Headless/FlaUI accessibility assertions.
- **S2-AC-05:** Unarchive normalization соответствует утверждённой таблице для `NotReady`, `Prepared`, `InProgress`, `Completed`, missing/corrupt history; undefined target/source/history разделены и покрыты `(TaskStatus)int.MaxValue`, null, equal-timestamp, far-future и newer-invalid/older-valid cases. End-to-end FileStorage command с null/future legacy history не падает, сохраняет raw entries и добавляет ровно одну новую entry.
- **S2-AC-06:** Parent/child unarchive cascade использует ту же normalization; `0 children`, null manager, no/click-away/exception не меняют children и не зависают; confirmed tasks обрабатываются awaited sequentially, accepted task получает одну history entry, mixed save failure даёт точный partial summary без ложного rollback.
- **S2-AC-07:** Future planned begin запрещает только `InProgress`; graph availability/Unlocked/opacity не меняются.
- **S2-AC-08:** Active direct/inherited blockers запрещают start/complete и дают opacity `0.4`; archived blockers не блокируют.
- **S2-AC-09:** Completion criteria блокируют только `Completed`, но не `InProgress`, graph availability или opacity.
- **S2-AC-10:** Потерявшая availability `InProgress` один раз становится `Prepared` с system history entry и не зацикливается.
- **S2-AC-11:** Telegram keyboard показывает только enabled non-current targets; denied/stale/duplicate handler callback возвращает reason без мутации, а allowed callback awaits storage-backed command и показывает persisted refreshed status.
- **S2-AC-12:** README EN/RU содержат одинаковую canonical matrix и отдельно объясняют lifecycle/graph/start/complete.
- **S2-AC-13:** README исправляет future dimming и `Unlocked`; Markdown marker/export copy остаётся в Stage 7 и не меняется здесь.
- **S2-AC-14:** Old status spec содержит заметную errata/supersession note перед разделами transition rules, availability и Telegram status behavior со ссылкой на эту spec, без переписывания исторического журнала.
- **S2-AC-15:** Domain enum, persisted schema, server wire DTO/hub methods и existing history entries не изменены; additive in-process `.NET` API (`ITaskStorage.TrySetStatusAsync`, `ConfirmAsync`, structured result snapshot/reason, `ServerStorage.ReadGraphAsync`) и обновлённые doubles перечислены в diff/PR.
- **S2-AC-16:** Все перечисленные targeted filters, full `Unlimotion.Test`, full Headless и релевантный FlaUI suite PASS serially; solution и Telegram build PASS; FileStorage locked и ServerStorage non-locking/failure tests PASS; required GitHub PR checks green.
- **S2-AC-17:** Один и тот же automated flow имеет verified before/after MP4 либо объективный recorder failure с screenshots/logs; PR содержит paths, SHA-256, duration/resolution/FPS и local-only retention disclosure.
- **S2-AC-18:** PR #274 green/ready/merged до EXEC; после merge выполнены fetch/rebase, ancestry check и повтор baseline, а перед delivery — clean scope, `git diff --check`, Post-EXEC review и PR/release-note handoff.

Characterization baseline до EXEC:
- `TaskTreeManagerSafetyTests`: 3 PASS — engine запрещает terminal -> `InProgress`.
- `TaskStatusDomainTests`: 4 PASS — один тест закрепляет неправильный raw restore `InProgress` и должен быть заменён data-driven normalization matrix.
- `TaskAvailabilityParityTests`: 2 PASS — недостаточный oracle, должен быть расширен policy/consumer parity.
- `MainControlTaskStatusIconUiTests`: 20 PASS — недостаточный oracle, должен проверять независимые expected options/reasons.

Targeted tests to add/update:
- `TaskStatusTransitionPolicyTests` — raw 5x5 service parity, reason priority и invalid enum; не подменяет command-level no-op.
- `TaskAvailabilityCalculationTests` — contained/direct/inherited diagnostics, future и criteria facts.
- `TaskAvailabilityParityTests` — service facts/policy parity.
- `TaskTreeManagerSafetyTests` — legacy mixed-update compatibility и automatic rollback idempotency.
- `TaskGraphCommandServiceTests` — пять valid diagonal no-op cases, invalid-equals-invalid deny, authoritative snapshot/cache inputs, locked/non-locking diagnostic storage, concurrent/stale/save-failure/undefined-source behavior.
- `TaskStatusDomainTests` — valid-entry predicate, deterministic order, null/future idempotency и unarchive normalization table.
- `ServerStorageStatusCommandTests` (new) — diagnostic GetAll mapping, no unconditional `StorageFailed`, propagated read failure и non-locking verified/unknown result; internal injected fetch delegate/client, без real network.
- `FileStorageTaskStatusTests` — end-to-end null/future history clone/save и ровно одна accepted entry.
- `TaskStatusTransitionTests` — ViewModel uses preview policy + storage-backed command, no optimistic assignment/duplicate switch.
- `MainControlTaskStatusIconUiTests` — four options, disabled state, tooltip, AutomationId.
- `MainControlAvailabilityUiTests` — future opacity `1` vs blocker opacity `0.4`.
- `MainWindowViewModelTests` — hotkey, 0-child/null-manager/yes/no/click-away/exception/mixed-history/mixed-save parent/child cascade и single history entry.
- `NotificationManagerWrapperTests` (new) — реальный `NotificationManagerWrapper` с смонтированным `MainScreen`/`DialogHost`: yes/no/click-away/programmatic close/host exception завершают `ConfirmAsync` exactly once без hang; ViewModel mock не считается заменой этого gate.
- `TelegramStatusContractTests` — full handler-level keyboard/callback test без real Telegram network.
- `MainWindowScenariosBase` inherited Headless/FlaUI `StatusContract_TerminalPickerAndUnarchive`; Headless владеет dynamic menu rows/HelpText/theme assertions, FlaUI — end-user click/keyboard flow и visible result.

Visual acceptance:
- Storyboard frames A-F соблюдены в desktop app.
- Disabled item визуально отличим, текст/иконка читаемы в light/dark theme.
- Reason постоянно видим в disabled row, tooltip открывается pointer при `ShowOnDisabled=true`, HelpText/automation name доступен screen reader; keyboard не обязан фокусировать disabled action.
- EN и RU reason mapping проверены отдельно; RU UI screenshot содержит фактическую русскую причину.
- Archive/Unarchive copy соответствует status.
- Future и blocker различаются opacity.
- Automation test использует row-scoped selectors, не случайный первый picker.
- Уже открытый flyout не исполняет stale preview после конкурентного blocker/date/status change: click/callback получает command-level deny и UI refresh.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| S2-AC-01 | `TaskStatusTransitionPolicyTests`, parity tests | Diff confirms no duplicate switch | test log | — |
| S2-AC-02,03 | policy/manager/command/VM/Telegram adapter tests | Inspect denied UI/callback | test log + before/after video | — |
| S2-AC-04 | Headless picker assertions + inherited FlaUI scenario | Light/dark screenshot, tooltip | screenshots/video | — |
| S2-AC-05,06 | domain normalization + ViewModel cascade + real `NotificationManagerWrapperTests` | Unarchive visible result and DialogHost dismissal | after video/log | — |
| S2-AC-07 | availability/domain/UI tests | Future opacity screenshot | `after-future-vs-blocked.png` | — |
| S2-AC-08 | direct/inherited/archived blocker tests + UI | Blocked opacity screenshot | test log/screenshot | — |
| S2-AC-09 | policy/criteria/UI tests | Disabled Complete reason | test log | — |
| S2-AC-10 | manager regression | Inspect single history entry | test log | — |
| S2-AC-11 | Telegram adapter test + bot build | Sanitized callback log | test/build log | Telegram client network call не требуется |
| S2-AC-12,13 | paired structural/token checks | Source-to-doc review + GFM render | docs review record | Prose semantics require manual review |
| S2-AC-14 | exact supersession link check | Read old spec header | diff | — |
| S2-AC-15 | schema snapshot/diff check | Inspect production diff | git diff | — |
| S2-AC-16 | full builds/suites | CI/run logs | TRX/HTML/log | — |
| S2-AC-17 | automated FlaUI recording | Video/screenshot inspection | named artifacts | Fallback only after objective recorder failure |
| S2-AC-18 | git/GitHub checks | Base/dependency/PR review | Post-EXEC journal | — |

Repo-proven commands (TUnit использует `--treenode-filter`, не VSTest `--filter`):

```powershell
git fetch origin --prune
$stage1 = gh pr view 274 --repo Kibnet/Unlimotion --json state,isDraft,mergeCommit | ConvertFrom-Json
if ($stage1.state -ne "MERGED" -or [string]::IsNullOrWhiteSpace($stage1.mergeCommit.oid)) { throw "PR #274 must be merged before Stage 2 EXEC." }
git rebase origin/main
if ($LASTEXITCODE -ne 0) { throw "Stage 2 rebase failed." }
git merge-base --is-ancestor $stage1.mergeCommit.oid HEAD
if ($LASTEXITCODE -ne 0) { throw "HEAD does not contain PR #274 merge commit $($stage1.mergeCommit.oid)." }
git status --short

dotnet restore src/Unlimotion.sln
dotnet restore tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj
dotnet restore tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj

dotnet build src/Unlimotion.sln -c Debug --no-restore -p:UseSharedCompilation=false
dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-restore -p:UseSharedCompilation=false
dotnet build tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug --no-restore -p:UseSharedCompilation=false
dotnet build src/Unlimotion.TelegramBot/Unlimotion.TelegramBot.csproj -c Debug --no-restore -p:UseSharedCompilation=false
```

Targeted pattern, повторить для перечисленных классов:

```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskStatusTransitionPolicyTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskAvailabilityCalculationTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskAvailabilityParityTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskTreeManagerSafetyTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskGraphCommandServiceTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskStatusDomainTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/ServerStorageStatusCommandTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/FileStorageTaskStatusTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskStatusTransitionTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainControlAvailabilityUiTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowViewModelTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/NotificationManagerWrapperTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TelegramStatusContractTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowHeadlessTests/StatusContract_TerminalPickerAndUnarchive" --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowFlaUiTests/StatusContract_TerminalPickerAndUnarchive" --maximum-parallel-tests 1 --output Detailed
```

Если `dotnet test ... -- --list-tests` для UI project сообщает 0, discovery выполняется repo-proven fallback-командой `dotnet run --project <UiTests.csproj> -c Debug --no-build -- --list-tests`; известный FlaUI baseline должен содержать не менее 9 inherited nodes до добавления нового scenario. Любая targeted команда обязана показать ровно ненулевое число tests; exit code 0 без executed node не считается PASS.

Full gate:

```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build -- --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowFlaUiTests/StatusContract_TerminalPickerAndUnarchive" --maximum-parallel-tests 1 --output Detailed

git diff --check

# После создания stage-2 PR; $stage2PrNumber берётся из результата PR creation:
gh pr checks $stage2PrNumber --repo Kibnet/Unlimotion --required --watch
```

UI evidence capture:
- orchestration script запускает targeted test как отдельный process с уникальным temp handshake directory и уникальным GUID-suffixed window title; scenario пишет PID/title/outer rect в `window-ready.json`, ждёт принадлежащий wrapper `scenario-go.signal`, выполняет полный flow, пишет `scenario-complete.json`, ждёт `recording-finished.signal` и только затем запускает assertions;
- до production edits выполнить `pwsh -File scripts/record-status-contract-evidence.ps1 -Phase Before -RecorderScriptPath C:\Users\Kibnet\.codex\skills\record-app-screen\scripts\record_app_window.ps1 -OutputPath artifacts/ui-tests/status-contract/before-terminal-unarchive.mp4`;
- `Before` считается capture PASS только когда recorder/ffprobe успешны, `scenario-complete.json` имеет `FlowCompleted=true` и exact known failure ids (`TerminalInProgressWasEnabled`, `UnarchiveDidNotRestorePrepared`), а test завершился nonzero именно на aggregated contract assertions; harness/launch/timeout/unknown assertion failure не принимаются как baseline evidence;
- после fixes выполнить тот же wrapper с `-Phase After ... -OutputPath artifacts/ui-tests/status-contract/after-terminal-unarchive.mp4`; `After` требует recorder PASS, `FlowCompleted=true`, empty failure ids и test exit code 0;
- wrapper сверяет ready PID/title/rect с единственным visible window, передаёт recorder полный уникальный title и использует одинаковые seed, geometry `1280x800`, capture `30 fps` и timeout. Recorder duration задаётся больше scenario timeout плюс 10 секунд; recorder/ffmpeg должны оставаться живы до появления `scenario-complete.json`, иначе phase завершается harness failure;
- `finally` всегда пишет finish signal, затем завершает только отслеженные test/recorder/ffmpeg descendant PIDs и удаляет temp handshake directory; unrelated processes по имени не завершаются;
- выполнить `ffprobe -v error -show_entries stream=codec_name,width,height,avg_frame_rate -show_entries format=duration -of json <mp4>` и `Get-FileHash -Algorithm SHA256 <mp4>` для обоих файлов;
- проверить MP4 и screenshots визуально; paths, commands, hash/metadata и local-only retention записать в Post-EXEC review/PR validation.

Stop rules для validation:
- zero tests ran -> проверить filter/argument separator, не считать PASS;
- targeted failure -> исправить до full suite;
- full-suite failure -> isolated rerun + evidence, не маркировать flake без воспроизведения;
- Headless/FlaUI disagreement -> stage incomplete;
- recorder failure -> сохранить exact error и next-best evidence, затем независимый review решает достаточность;
- PR #274 не merged -> production edits не начинать; merge/rebase conflict -> остановить EXEC и разрешить status prose вручную;
- required GitHub PR check не green -> PR остаётся draft; unrelated suspected flake требует isolated rerun/evidence, а не бездоказательную маркировку;
- runtime/schema diff вне table 16 -> scope stop.

## 12. Риски и edge cases
- Disabled options могут сделать flyout визуально плотнее: проверить narrow/window and keyboard behavior.
- Disabled `MenuItem` пропускается keyboard focus: inline reason является основным visual channel; `ShowOnDisabled`/HelpText проверяются отдельно, fallback — focusable non-actionable reason summary.
- ViewModel preview строит facts из cached `IsCanBeCompleted`, но не авторизует запись; storage command всегда перечитывает graph.
- Любой оставшийся direct `Status = ...` в picker/hotkey/archive/Telegram является blocking diff finding. Model-to-cache hydration под `_isUpdatingFromModel` остаётся разрешённой.
- Stale Telegram callback после изменения blockers/status обязан re-evaluate непосредственно перед mutation.
- Legacy history может иметь null/terminal/unknown/unsorted/equal/future-time entries: valid predicate, skew tolerance, list-index tie-break и end-to-end FileStorage tests обязательны.
- Child cascade не является cross-task transaction: parent first, затем все confirmed children sequentially; mixed result отображается summary и не объявляется полным success.
- DialogHost click-away/close может не вызывать старый `noAction`: новый `ConfirmAsync` обязан завершаться exactly once на любом dismiss; null manager = false.
- ServerStorage не имеет cross-client CAS: strong atomicity не заявляется, post-verify mismatch = `OutcomeUnknown`; server-authoritative wire command остаётся named follow-up.
- Stale cache при no-op/deny нельзя оставлять как есть: `AuthoritativeTask` hydrate обязателен и отдельно не считается persisted mutation.
- Auto `InProgress -> Prepared` может записывать повторные system entries: idempotency test.
- PR #274 меняет соседний historical paragraph в status section: final rebase may conflict.
- Full FlaUI может быть длинным/нестабильным: targeted scenario обязателен, failure evidence-first.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Почему не просто скрыть запрещённые пункты?» | Current UI так делает | Утверждённый disabled + inline reason объясняет контракт и остаётся доступным для accessibility review | mitigated by approved choice |
| «Почему legacy Completed восстанавливается в NotReady?» | Возможны ожидания Prepared | Утверждённый conservative fallback не выдумывает readiness и не переписывает raw history | mitigated by approved choice |
| «Зачем включать Telegram?» | Stage кажется desktop-only | Утверждённый scope закрывает существующий обход shared policy и проверяется handler test | mitigated by approved choice |
| «Не хочу миграцию данных» | Status history чувствительна | Schema и raw history не меняются; normalization только при user unarchive | mitigated |
| «UI tests снова будут flaky» | Stateful Avalonia/FlaUI history | Serial execution, targeted first, isolated rerun and evidence rules | mitigated |
| «Не переписывайте старую spec» | Она является аудит-журналом | Только заметная errata/link, исторический текст и journal сохраняются | mitigated |

### Rework Prevention Checklist
- [x] Spec называет видимые пользователю picker/unarchive/future/blocked/Telegram/docs scenarios.
- [x] Каждый scenario связан с AC и evidence.
- [x] Assumptions по disabled UX, legacy normalization и Telegram вынесены в Decision Ledger.
- [x] Три product choices явно подтверждены пользователем и отражены в финальном contract.
- [x] Likely objections закрыты утверждёнными решениями и verification contract.
- [x] Domain, UX, tester, architecture и delivery roles обязательны для review.
- [x] AC проверяют итоговое поведение, а не подготовительные шаги.
- [x] EXEC имеет deterministic, UI и visual evidence path.
- [x] Data/schema non-goals и rollback определены.

## 13. План выполнения
1. Завершено 2026-07-17: пользователь утвердил disabled-with-reason, legacy fallback `NotReady`, Telegram included и сообщил отдельное `Спеку подтверждаю`; product-specific re-review выполняется перед production edits.
2. Дождаться green/ready/merge PR #274. До его merge production edits запрещены; CI lifecycle blocker оформляется отдельной child spec/PR, а не смешивается со Stage 2.
3. Выполнить `git fetch origin --prune`, rebase `fix/status-availability-contract` на новый `origin/main`, доказать ancestry merge commit PR #274, clean scope и повторить 29-test characterization baseline.
4. До production edits добавить observation-first failing characterization/UI scenario и записать before FlaUI evidence через orchestration script.
5. Добавить pure Domain off-diagonal transition policy/facts/reason codes, command diagonal contract и invalid source/target/history tests.
6. Реализовать `ITaskStorage.TrySetStatusAsync` через `TaskGraphCommandService`, cache refresh и structured stale/save/no-op behavior; сохранить generic mixed-update compatibility tests.
7. Добавить deterministic unarchive normalizer и awaited parent/child cascade с mixed-failure tests.
8. Перевести ViewModel/picker/`Ctrl+D` на storage-backed path; реализовать утверждённый denied UX, localized accessibility contract и reactive Archive/Unarchive label.
9. Обновить Headless/FlaUI flow, RU/EN/light/dark assertions и записать/проверить after evidence.
10. Перевести Telegram keyboard/callback на storage-backed path и добавить handler test/build.
11. Обновить README EN/RU и точечную errata старой spec после behavior/tests; не менять Stage-7 marker-export copy.
12. Выполнить все targeted filters -> full domain -> full Headless -> targeted FlaUI -> build/diff -> required GitHub checks; затем независимый Post-EXEC review, commit/push, draft PR и ready transition.

## 14. Открытые вопросы
Блокирующих product-вопросов нет. Пользователь 2026-07-17 утвердил рекомендованный набор: disabled-with-reason, legacy fallback `NotReady`, Telegram included, и сообщил точную фразу `Спеку подтверждаю`.

Внешняя dependency не является открытым product-вопросом: production EXEC ждёт green/ready/merge PR #274 и rebase/ancestry gate. CI lifecycle race из full suite изолируется отдельной child spec/PR.

## 15. Соответствие профилю
- Stack + overlay: `.NET desktop client`, `ui-automation-testing`.
- Выполненные требования профиля:
  - authoritative domain policy и compatibility boundaries;
  - state/interaction matrix и decision ledger;
  - user-visible storyboard;
  - mandatory domain/headless/FlaUI coverage;
  - before/after video paths и fallback rule;
  - TUnit `--treenode-filter` commands;
  - full-suite, rollback, rebase и post-EXEC gates;
  - local UI-test override соблюдён.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Domain/TaskStatusTransitionPolicy.cs` (new) | Pure facts/evaluation/reason/restore normalization | Единый contract без UI dependency |
| `src/Unlimotion.Domain/TaskItem.cs` | Normalized restore + null/future-safe idempotent history helpers | Исправить unarchive bypass/duplicate entry |
| `src/Unlimotion.Domain/TaskStatusExtensions.cs` | Null-safe history queries либо delegation в new normalizer | Не падать на corrupt legacy list |
| `src/Unlimotion.TaskTreeManager/TaskAvailabilityService.cs` | Делегировать matrix pure policy | Удалить отдельный switch source |
| `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs` | Сохранить generic mixed-update semantics; использовать shared policy для internal automatic status correction | Engine consistency без скрытого API break |
| `src/Unlimotion.TaskTreeManager/TaskGraphCommandService.cs` | Dedicated same/stale/no-op/invalid-target alignment, structured engine diagnostics | Authoritative storage-write boundary |
| `src/Unlimotion.TaskTreeManager/TaskOperationResult.cs` | Additive `AuthoritativeTask` snapshot + nullable Domain status-reason property | Stale-cache hydration и stable mapping без parsing English message |
| `src/Unlimotion.ViewModel/ITaskStorage.cs` | Добавить `TrySetStatusAsync` contract | Запрет optimistic mutation у consumers |
| `src/Unlimotion/UnifiedTaskStorage.cs` | Реализовать local command gate, adapter и cache hydration из `AuthoritativeTask`/`ChangedTasks` | Storage-backed desktop/Telegram result |
| `src/Unlimotion/ServerStorage.cs` | Реализовать diagnostic graph read через existing endpoint с propagated errors/internal test seam; без wire/CAS claim | Не сломать server-backed status commands |
| Четыре `ITaskStorage` doubles: `tests/Unlimotion.Performance/Program.cs`, `src/Unlimotion.Test/TaskItemRepeaterListMarkerTests.cs`, `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs` | Реализовать новый method или shared fake adapter | Сохранить compile и controllable results |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Удалить duplicate switch, async validated method, awaited normalized parent/child unarchive, reactive label | Desktop contract |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Перевести `Ctrl+D` на async status command | Закрыть optimistic hotkey path |
| `src/Unlimotion.ViewModel/INotificationManagerWrapper.cs` | Additive generic `ConfirmAsync` | Awaitable confirmation contract |
| `src/Unlimotion/NotificationManagerWrapper.cs` | Exactly-once yes/no/dismiss/exception completion | Не зависать и не запускать fire-and-forget writes |
| `src/Unlimotion.Test/NotificationManagerWrapperMock.cs`, `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAppLaunchHost.cs` | Реализовать deterministic confirmation result/dismiss behavior | Compile + unit/UI cascade tests |
| `src/Unlimotion.Test/NotificationManagerWrapperTests.cs` (new) | Смонтировать реальный `MainScreen`/`DialogHost` и проверить yes/no/click-away/close/exception exactly once | Исполнимый gate production `ConfirmAsync`, не только mock ViewModel |
| `src/Unlimotion.ViewModel/TaskStatusOption.cs` | Reason mapping/state при необходимости | Disabled picker copy |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` / `Strings.ru.resx` | Localized denial и Archive/Unarchive text | UX/accessibility |
| `src/Unlimotion/TaskStatusPicker.cs` | Disabled non-current options + inline reason/`ShowOnDisabled`/HelpText | Утверждённый denied UX |
| `src/Unlimotion/Views/MainControl.axaml` | Reactive command header/AutomationId при необходимости | Correct Unarchive copy |
| `src/Unlimotion.TelegramBot/Bot.cs` | Enabled targets only, storage-backed callback | Закрыть policy bypass |
| `src/Unlimotion.TelegramBot/AssemblyInfo.cs` (optional new) | Test internals only если нужен handler test | Direct bot coverage без public API |
| `src/Unlimotion.Test/Unlimotion.Test.csproj` | Bot project reference для handler test | Test Telegram adapter |
| `src/Unlimotion.Test/TaskStatusTransitionPolicyTests.cs` (new) | Raw 5x5/reasons/invalid parity | Canonical raw-policy tests |
| `src/Unlimotion.Test/TaskAvailabilityCalculationTests.cs`, `TaskAvailabilityParityTests.cs`, `TaskTreeManagerSafetyTests.cs`, `TaskGraphCommandServiceTests.cs`, `TaskStatusDomainTests.cs`, `FileStorageTaskStatusTests.cs`, `TaskStatusTransitionTests.cs`, `MainControlTaskStatusIconUiTests.cs`, `MainControlAvailabilityUiTests.cs`, `MainWindowViewModelTests.cs` | Расширить exact cases из section 11 | Regression coverage |
| `src/Unlimotion.Test/ServerStorageStatusCommandTests.cs` (new) | Existing-endpoint diagnostic/failure/non-locking contract | Server-mode regression |
| `src/Unlimotion.Test/TelegramStatusContractTests.cs` (new) | Handler-level keyboard/callback contract без network | Проверить Telegram shared-policy integration |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Row-scoped status/archive controls и generic accessibility reads только для supported adapter primitives | Shared user-flow page object |
| `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs` | Inherited terminal/unarchive observation-first scenario | Один flow в двух harnesses |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs` | Deterministic status/future/blocked seed | Stable automation evidence |
| `tests/Unlimotion.UiTests.Headless/Tests/MainWindowHeadlessTests.cs` / headless-specific helper | Dynamic menu row, tooltip/HelpText, RU/EN и light/dark assertions | Покрыть adapter capabilities, которых нет в shared abstraction |
| `tests/Unlimotion.UiTests.FlaUI/Tests/MainWindowFlaUiTests.cs` | End-user click/keyboard flow и accessibility-tree assertions | Реальный Windows UI evidence |
| `scripts/record-status-contract-evidence.ps1` (new) | Test/window handshake, `record_app_window.ps1` orchestration, 1280x800/30fps и `ffprobe`/SHA report | Воспроизводимое before/after video evidence |
| `README.md` / `README.RU.md` | Canonical matrix и semantic corrections | Public truthfulness |
| `specs/2026-06-09-task-status-model.md` | Supersession/errata перед 6.2, 7.2, status-control и Telegram claims | Не выдавать stale contract за current |
| `specs/2026-07-17-status-availability-contract.md` | Approval/Post-EXEC journal | Audit trail |

Optional rows разрешены только если direct Telegram adapter test нельзя сделать без них; Post-EXEC обязан указать фактический file set и отсутствие unrelated changes.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Matrix ownership | Service + duplicate VM + bot behavior | One pure policy + adapters |
| Terminal picker | `InProgress` может выглядеть allowed и откатиться | Disabled с reason, mutation не начинается |
| Denied options | Hidden | Visible disabled + inline reason/HelpText |
| Unarchive previous `InProgress` | Raw `InProgress`, затем reject/rollback | `Prepared` |
| Legacy previous `Completed`/missing/corrupt | Raw/implicit fallback | `NotReady` |
| Child cascade | Raw restore per child | Same normalization per confirmed child |
| Future task | README обещает dimming | Start disabled, opacity remains `1` |
| Archived blocker | Coverage неполна | Direct/inherited terminal blocker regressions |
| Telegram | All buttons + optimistic mutation | Enabled only + storage-backed command |
| README diagram | Forbidden terminal arrows | Canonical 5x5 contract |
| Old spec | Stale claims выглядят authoritative | Visible supersession/errata note |

## 18. Альтернативы и компромиссы
- Вариант: исправить только два `if (Status.IsTerminal())` в ViewModel.
  - Плюсы: минимальный diff.
  - Минусы: duplicate matrices и Telegram/unarchive bypass остаются; drift повторится.
- Вариант: оставить denied options hidden.
  - Плюсы: current visual behavior, компактный flyout.
  - Минусы: пользователь не понимает, почему target исчез; прежняя spec и existing tooltip infrastructure не используются.
- Вариант: восстановить previous `InProgress` напрямую.
  - Плюсы: формально resume.
  - Минусы: нарушает утверждённый terminal guard и уже отвергается engine.
- Вариант: мигрировать всю history.
  - Плюсы: raw data становится normalized.
  - Минусы: ненужный destructive scope и rollback risk.
- Зафиксированная общая часть: pure policy + storage-backed consumer alignment + action-time normalization, без schema migration.
- Утверждённый product contract: visible disabled reasons, conservative `NotReady` fallback и Telegram parity. Альтернативы выше сохраняются только как audit trace и не входят в EXEC.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, root cause, goals и non-goals определены |
| B. Качество дизайна | 6-10 | PASS | Technical boundary и утверждённые disabled/`NotReady`/Telegram product choices заданы однозначно |
| C. Безопасность изменений | 11-13 | PASS | Dedicated mutation boundary, generic compatibility, no schema migration и code-only rollback заданы |
| D. Проверяемость | 14-16 | PASS | 18 AC связаны с domain/headless/FlaUI/docs/git evidence |
| E. Готовность к автономной реализации | 17-19 | PASS | Sequence/files/commands заданы; explicit dependency gate запрещает production edits до merge PR #274 |
| F. Соответствие профилю | 20 | PASS | .NET/TUnit/UI automation/local override отражены |

Итог: `ГОТОВО`; spec утверждена, но production EXEC ожидает prerequisite PR #274.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Desktop, Telegram, docs, data/API non-goals и delivery boundary определены |
| 2. Понимание текущего состояния | 5 | Code/tests/docs drift прослежен по entry points |
| 3. Конкретность целевого дизайна | 5 | Mutation/API/history, disabled UX, `NotReady` fallback и Telegram flow конкретны |
| 4. Безопасность (миграция, откат) | 5 | No schema change, storage command, code-only rollback и history residue заданы |
| 5. Тестируемость | 5 | Targeted/full/UI/video matrix |
| 6. Готовность к автономной реализации | 5 | Sequence/files/commands/evidence заданы; external dependency имеет проверяемый stop/rebase gate |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению после prerequisite gate.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Matrix, unarchive, blockers и Telegram соответствуют user workflow? | PASS | Утверждены terminal matrix, `Prepared`/`NotReady` normalization и Telegram parity |
| UX / designer | applicable | Disabled reasons, copy, future/blocker distinction и storyboard ясны? | PASS | Visible disabled reasons/HelpText, RU/EN/light/dark и fallback accessibility state заданы |
| Tester / validation | applicable | Каждый AC имеет negative/edge/UI evidence? | PASS | Exact filters, unconditional Telegram gates, real ConfirmAsync gate и video handshake повторно проверены |
| Developer / architect | applicable | Pure policy layering, no-mutation и no-schema boundaries coherent? | PASS | Storage fallback/concurrency, command ordering, authoritative snapshot, history и generic compatibility повторно проверены |
| Delivery / operations / security | applicable | Dependency/rebase, artifacts, rollback и CI gates безопасны? | PASS | PR #274 dependency, separate CI-fix scope, rebase/ancestry, evidence и required-check stop rules явные |

### Post-SPEC Review
- Статус: PASS после technical и product-specific fix/re-review cycles; PR #274 остаётся обязательным sequencing prerequisite, но не недоопределённостью spec
- Scope reviewed: эта spec, source/test/docs evidence, central routing, branch/dependency metadata, PR #274 CI, recorder prerequisites и executable TUnit/UI filters
- Decision: approval принят; Stage 2 можно исполнять только после green/ready/merge PR #274 и rebase/ancestry/baseline gate
- Review passes:
  - Scope/Evidence pass: PASS — Stage-7 marker claim исключён, Telegram включён явно, stage-1 merge закреплён prerequisite.
  - Contract pass: PASS after re-review — storage-backed command, raw/effective matrix split, generic compatibility, invalid cases и ServerStorage boundary согласованы.
  - Adversarial risk pass: PASS after re-review — accessibility, deterministic history, mixed cascade, real ConfirmAsync gate, code-only rollback и full-flow video orchestration согласованы.
  - Role-Based pass: PASS — Business/UX/Tester/Architecture/Delivery contracts согласованы с утверждёнными choices.
  - Re-review after fixes / Fix and re-review: PASS — невыбранные hidden/Prepared/exclude-Telegram ветви удалены из executable contract; conditional commands/file inventory и два последних Telegram test/callback упоминания сделаны обязательными.
  - Stop decision: PASS для spec/approval; STOP production до prerequisite PR #274.
- Evidence inspected: source audit; 29 baseline targeted tests PASS; три initial independent NEEDS-FIX verdicts, два focused technical PASS verdicts и заключительный independent product-specific audit; exact engine/command/storage/ViewModel/Telegram source; old spec headings; PR #274 checks; actual recorder script/ffmpeg/ffprobe preflight; TUnit/FlaUI discovery behavior
- Depth checklist:
  - Scope drift / unrelated changes: только эта spec изменена
  - Acceptance criteria: mapping расширен storage/concurrency/accessibility/history/CI cases
  - User-observable scenarios / Decision ledger / Expected objections: заполнены; user-owned decisions имеют chosen values и `Needs user = Нет`
  - Validation evidence: baseline есть; exact targeted/full/video contract задан; EXEC evidence pending
  - Unsupported claims: преждевременные 30/30 и source claim исправлены
  - Regression / edge case: undefined source/target/history, generic mixed update, stale flyout, 0/refusal/mixed cascade добавлены
  - Comments/docs/changelog: exact old-spec sections и PR release-note handoff заданы
  - Hidden contract change: disabled UX, legacy fallback и Telegram scope явно утверждены и связаны с AC/evidence
  - Manual-review challenge: remote availability local-only videos и cross-task atomicity явно ограничены
- No-findings justification: заключительный independent product-specific audit нашёл два условных Telegram test/callback остатка в S2-AC-03 и acceptance matrix; оба исправлены, после чего executable contract не содержит открытых product/API/UX вариантов; remaining PR #274 condition является явным sequencing gate, а не скрытым finding.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | product decisions | Denied presentation, legacy fallback и Telegram scope не были утверждены | Зафиксировать disabled-with-reason, `NotReady`, Telegram included | fixed by user approval |
| HIGH | mutation/concurrency | Preview и optimistic save не давали authoritative no-mutation contract | Dedicated storage-backed command + cache refresh + stale tests | fixed in spec, technical re-review PASS |
| HIGH | compatibility/invalid | Generic mixed update и invalid target/source/history были смешаны | Развести API semantics/recovery и добавить tests | fixed in spec, technical re-review PASS |
| HIGH | UX/accessibility | Disabled tooltip был недоступен keyboard/screen reader | Inline reason + ShowOnDisabled + HelpText + fallback/test split | fixed in spec, product choice approved |
| HIGH | validation/evidence | Не хватало exact filters, real ConfirmAsync gate, video orchestration/retention и CI gate | Добавить commands, handshake, ffprobe/hash/local-only disclosure | fixed in spec, technical re-review PASS |
| MEDIUM | delivery prerequisite | PR #274 ещё не merged | Green/ready/merge, fetch/rebase/ancestry/baseline до production edits | enforced stop rule; separate CI child spec required |

- Fixed before continuing: оба technical fix set и product-choice fix set внесены; hidden/Prepared/exclude-Telegram executable branches и два последних conditional Telegram test/callback упоминания удалены
- Checks rerun: baseline targeted tests; structural spec checks PASS (22 H2, even fences, no unresolved decision rows); independent architecture/test re-review и заключительный product-specific audit PASS после fixes
- Needs human: Stage 2 approval/choices закрыты; отдельная новая CI-lifecycle child spec потребует собственную точную approval-фразу до её code EXEC
- Residual risks / follow-ups: PR #274 merge, recorder handshake implementation, actual full-suite duration и local-only video availability

### Post-EXEC Review
- Статус: Не выполнен; EXEC утверждён, production phase не начата из-за prerequisite PR #274
- Scope reviewed: Не применимо до EXEC
- Decision: Не применимо до EXEC
- Review passes:
  - Scope/Evidence pass: Не применимо
  - Contract pass: Не применимо
  - Adversarial risk pass: Не применимо
  - Role-Based pass: Не применимо
  - Re-review after fixes / Fix and re-review: Не применимо
  - Stop decision: Не применимо
- Evidence inspected: Не применимо
- Depth checklist: Не применимо
- No-findings justification: Не применимо

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | phase | EXEC не начат | Выполнить полный Post-EXEC review после implementation/validation | follow-up |

- Fixed before final report: Не применимо
- Checks rerun: Не применимо
- Validation evidence: Не применимо
- Unrelated changes: Не применимо
- Needs human: не для Stage 2; отдельный approval новой CI-lifecycle child spec
- Residual risks / follow-ups: перечислены выше

## Approval
Пользователь 2026-07-17 сообщил точную фразу `Спеку подтверждаю` и попросил выполнить все этапы. Approval трактуется вместе с ранее предложенным рекомендованным набором: disabled-with-reason, legacy fallback `NotReady`, Telegram included.

Stage-2 EXEC разрешён, но production edits остаются заблокированы explicit dependency gate до green/ready/merge PR #274, fetch/rebase, ancestry check и повторного characterization baseline.

Approval master roadmap и stage 1 не распространяется автоматически на этот stage-2 child EXEC.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Выполнить stage-2 freshness gate | 1.00 | PR #274 ещё не merged | Создать отдельную child branch/spec от `origin/main` | Нет | Не применимо | Base и dependency зафиксированы до design | `specs/2026-07-17-status-availability-contract.md` |
| SPEC | Провести source/test/docs audit | 0.99 | Нужен independent synthesis | Сформировать AS-IS matrix и gaps | Нет | Не применимо | Два независимых audit подтвердили duplicate policy, unarchive bypass, UI/docs/test gaps | Source/tests/README/old spec inspected |
| SPEC | Запустить characterization baseline | 1.00 | Нет | Записать test evidence в spec | Нет | Не применимо | 29 targeted tests PASS, при этом два test-oracle gaps и stale unarchive expectation подтверждены | `Unlimotion.Test.csproj` test outputs |
| SPEC | Предложить UX, legacy normalization и Telegram scope | 0.90 | Нужны три product choices | Запросить явные ответы | Да | Independent review потребовал ASK-HUMAN | Recommended defaults не выдаются за утверждённые решения | Эта spec |
| SPEC | Провести первый multi-role Post-SPEC review | 1.00 | Найдены contract/UX/evidence gaps | Внести draft fix set | Нет | Три независимых reviewer verdicts = NEEDS-FIX | Review проверил source/API/UI/test/delivery claims, а не только полноту template | Эта spec, source/tests/PR #274 |
| SPEC | Исправить technical review findings | 0.98 | Product choices и re-review pending | Получить ответы пользователя, затем повторить review | Да | Ещё не обращались за тремя choices | Добавлены storage command, generic compatibility, invalid/history rules, accessibility, exact tests/video/rollback/dependency gates | Эта spec |
| SPEC | Провести focused technical re-review после fixes | 1.00 | Остаются product choices и PR #274 dependency | Запросить три явных ответа, удалить невыбранные ветви и провести product-specific review | Да | Два independent reviewers дали PASS по architecture и test/evidence contracts | Условный Telegram scope, full-flow recorder handshake и real ConfirmAsync gate проверены повторно | Эта spec |
| EXEC | Принять approval и product choices Stage 2 | 1.00 | PR #274 ещё не green/merged | Зафиксировать choices, выполнить product-specific re-review и оформить отдельную CI-lifecycle child spec | Нет для Stage 2; отдельный approval нужен новой CI spec | Пользователь дословно сообщил `Спеку подтверждаю` и «Выполни все этапы» | Рекомендованные UX/legacy/Telegram варианты приняты; production edits не обходят dependency gate | Эта spec, PR #274 |
| SPEC | Закрыть заключительный product-specific audit | 1.00 | Нет | Зафиксировать spec-only commit и перейти к отдельной CI-lifecycle child spec | Нет | Independent reviewer нашёл два conditional Telegram test/callback остатка; оба исправлены | S2-AC-03 и acceptance matrix теперь безусловно включают Telegram; executable scope согласован с утверждённым решением | Эта spec |
