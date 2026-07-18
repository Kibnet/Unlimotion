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
  - dependency/freshness gate повторён 2026-07-18 на merged PR #274 / `origin/main` commit `8e34408a29894b9eaab2981b79ded86c83a634a5`;
  - фактические before/after MP4 и четыре after-снимка получены автоматизированным FlaUI flow; Headless используется для semantic/accessibility assertions без недостоверной fake-backend raster capture.
- Целевой релиз / ветка: `fix/status-availability-contract`; EXEC base = merged PR #274 / `origin/main` `8e34408a29894b9eaab2981b79ded86c83a634a5`; package доставлен merged PR #277, merge commit `75efc0497af0a1b4678372b67112a8f606ce28c9`
- Ограничения:
  - текущая фаза `EXEC`: пользователь 2026-07-17 сообщил точную фразу `Спеку подтверждаю` и попросил выполнить все этапы;
  - утверждены рекомендованные product choices: denied desktop targets видны disabled с inline reason/HelpText; previous `Completed`/missing/corrupt history восстанавливается в `NotReady`; Telegram входит в Stage 2;
  - master roadmap и stage-1 child spec доставлены merged PR #274; ancestry `8e34408 -> HEAD` и повторный baseline подтверждены до production edits;
  - локальный `AGENTS.override.md` требует UI tests для UI-facing поведения;
  - не менять enum статусов, JSON schema, DTO/molds, status-history schema или существующие данные;
  - не добавлять server wire method в Stage 2: server mode использует существующие GetAll/Load/Save endpoints; отсутствие cross-client compare-and-swap фиксируется честно и не называется atomic;
  - не переписывать прежнюю status spec как будто она всегда содержала новый контракт: добавить явную errata/supersession note;
  - не завершать EXEC без targeted, full domain, full headless и релевантного FlaUI gate;
  - UI video evidence `до`/`после` обязательно попытаться получить через автоматизированный FlaUI run; fallback допустим только с объективной причиной и next-best screenshots/logs.
- Связанные ссылки:
  - master roadmap: merged PR #274, `specs/2026-07-17-readme-reliability-roadmap.md`;
  - stage 1: merged PR #274, `specs/2026-07-17-readme-install-safety.md`;
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
  - future planned begin запрещает start, но не меняет graph availability и status-control opacity;
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

## 2. Текущее состояние (AS-IS, исторический pre-EXEC baseline)

Ниже зафиксирован исходный audit baseline до Stage-2 production edits; актуальное состояние и evidence после реализации находятся в Post-EXEC review.
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
- `git fetch origin --prune` повторён после merge PR #274.
- branch `fix/status-availability-contract` rebased/пересоздана поверх `origin/main` commit `8e34408a29894b9eaab2981b79ded86c83a634a5`; текущий spec commit `9f9a0f22d48eb71930e26a7aff797f4603fa862a`.
- latest release остаётся `1.27.0`; stage 2 не зависит от asset inventory.
- PR #274 merged как `8e34408a29894b9eaab2981b79ded86c83a634a5`; `git merge-base --is-ancestor 8e34408 HEAD` = PASS, поэтому prerequisite Stage-2 EXEC закрыт до production edits.

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
- `ITaskStorage` / `UnifiedTaskStorage` -> два storage-backed intent: `TrySetStatusAsync(taskId, target, author)` для явного target и `TryUnarchiveAsync(taskId, author)` для authoritative history normalization поверх `TaskGraphCommandService`; same-client calls сериализуются одним local gate, cache hydrate выполняется из authoritative command snapshot, не из предварительно мутированной ViewModel. Оба новых interface members имеют fail-closed default body, чтобы ранее скомпилированные implementers продолжали загружаться без silent write.
- `TaskItem` -> получает normalized restore helper на основе pure policy, без raw restore `InProgress`/terminal legacy states.
- `TaskItemViewModel` -> использует cached facts только для preview options и выбора archive/unarchive intent; picker/archive/cascade не вычисляют restore target и не присваивают `Status` до authoritative storage result.
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
- `ITaskStorage.TrySetStatusAsync(string taskId, TaskStatus requestedStatus, string? author = null)` обслуживает явный target из desktop picker/hotkey и Telegram, а `TryUnarchiveAsync(string taskId, string? author = null)` является отдельным storage-bound intent без client-computed target; оба возвращают `TaskOperationResult`;
- `UnifiedTaskStorage` направляет оба intent в `TaskGraphCommandService` под одним local gate. Для FileStorage read/resolve/re-evaluate/mutation/verification выполняются внутри `ITaskGraphWriteLock`; для `ServerStorage` — через diagnostic read без cross-client lock;
- `TaskOperationResult` additively содержит cloned `TaskItem? AuthoritativeTask`: текущий persisted snapshot для no-op/deny и post-write snapshot для verified success; `ChangedTasks` остаётся списком реально записанных tasks;
- при наличии `AuthoritativeTask` `UnifiedTaskStorage` hydrate соответствующий cached ViewModel даже при no-op/deny; это cache reconciliation без storage mutation. При success также применяются `ChangedTasks` и refresh relations;
- same-status success возвращает пустой `ChangedTasks`, но authoritative snapshot; history/version/file не меняются, а stale cache может и должен обновиться до persisted truth;
- business-rule deny возвращает stable policy reason, `Before` и authoritative snapshot; history/version/file не меняются, cache может reconcile stale display;
- unarchive вычисляет restore target только из authoritative history после входа в command/write boundary; если authoritative source уже не `Archived`, `StatusPreconditionFailed` возвращает snapshot, не пишет storage и останавливает child cascade;
- `StorageFailed` не маскируется success и использует честную retry copy без утверждения о refresh; `OutcomeUnknown` не обещает no-write и может не иметь authoritative snapshot. Тогда adapter делает explicit `Storage.Load(taskId)`; при success hydrate cache, при повторном failure оставляет cache как есть и показывает «итог неизвестен», а не success/rollback;
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
- wire DTO/JSON schema не меняются, но публичный .NET interface `ITaskStorage` расширяется двумя default fail-closed status methods; concrete production adapters и управляющие test doubles переопределяют их. Legacy CLR signatures фабрик result/reason и numeric values существующего `TaskOperationDeniedKind` сохранены; новые snapshot/reason данные имеют distinct factories без positional-null ambiguity.

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
- future date запрещает start, но не status-control dimming;
- status-control dimming `0.4` зависит только от graph unavailable;
- `Unlocked` означает graph-available и non-archived; projection не обещает, что task можно начать прямо сейчас;

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
  - `artifacts/ui-tests/status-contract/before-after-unarchive.png`;
  - `artifacts/ui-tests/status-contract/after-terminal-picker.png`;
  - `artifacts/ui-tests/status-contract/after-after-unarchive.png`;
  - `artifacts/ui-tests/status-contract/after-future-vs-blocked.png`;
  - `artifacts/ui-tests/status-contract/after-blocked.png`.
- Capture выполняется через `record-app-screen`/реальный синхронный `record_app_window.ps1` вокруг конкретного FlaUI test run. Repo wrapper принимает `-RecorderScriptPath`; если он не задан, разрешает только `$env:CODEX_HOME\skills\record-app-screen\scripts\record_app_window.ps1`, а при отсутствии пути останавливается с точной ошибкой.
- Test владеет `window-ready.json` и `scenario-complete.json`; wrapper владеет `scenario-go.signal` и `recording-finished.signal`. Test запускает видимое окно, устанавливает outer geometry `1280x800`, пишет exact title/process/rect в ready JSON и ждёт go.
- Test задаёт уникальный GUID-suffixed window title; ready JSON содержит PID/title/outer rect, а wrapper до запуска recorder проверяет, что этот PID владеет ровно одним visible top-level window с тем же title/rect, и передаёт recorder полный уникальный title. Совпадение только по общему process name не принимается.
- Wrapper запускает recorder отдельным `pwsh` process с `DurationSeconds` строго больше scenario timeout плюс 10 секунд, ждёт живой descendant `ffmpeg` и созданный nonempty output, затем пишет go; recorder снимает `30 fps` без audio/cursor scope change. `scenario-complete.json` обязан появиться, пока recorder и `ffmpeg` ещё живы; ранний exit делает capture failed. После полного flow wrapper дожидается штатного recorder exit, пишет результат/finished, и только после этого test выполняет assertions/закрывает окно.
- Один и тот же `StatusContract_TerminalPickerAndUnarchive` собирает observations и screenshots до assertions; все assertions выполняются после полного interaction flow, поэтому ожидаемый pre-fix failure не закрывает окно до записи unarchive шага.
- Before/after используют один seed, light theme, geometry и interaction order; отдельные RU/dark FlaUI scenarios дают real-pixel evidence future и blocked states, а Headless проверяет semantic theme/accessibility contract без raster claim.
- MP4 проверяется `ffprobe` на readable video stream, geometry, frame rate и duration `> 0`; SHA-256, duration, resolution, command и локальный repo-relative path записываются в Post-EXEC/PR Validation.
- Видео и screenshots остаются ignored local-only artifacts по master policy и не коммитятся; reviewer evidence — проверенные локальные paths + hashes/metadata в PR. Если remote review потребует downloadable artifact, это stop/ASK-HUMAN для отдельного upload mechanism, а не silent commit крупного бинарника.
- Fallback разрешён только если recorder/window capture объективно не может привязаться к test-host window: сохранить точную ошибку, Headless semantic assertions, FlaUI screenshots/logs и причину в Post-EXEC review. Отсутствие времени не является причиной.

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
| Archived picker | Открыть picker у `Archived` | `InProgress`/`Completed` disabled; активные targets доступны | Policy + Headless semantic assertions + FlaUI screenshot | S2-AC-02..04 |
| Unarchive after work | `InProgress -> Archived`, затем `Unarchive` | Status становится `Prepared`, history содержит одну новую запись | Domain/ViewModel/FlaUI/video | S2-AC-05, S2-AC-06 |
| Legacy unarchive | Разархивировать task с previous `Completed` или без history | Status = `NotReady`, приложение не падает | Data-driven domain test | S2-AC-05 |
| Child cascade | Подтвердить/отклонить unarchive children | Подтверждённые children нормализованы каждый отдельно; отказ оставляет их archived | ViewModel/UI tests | S2-AC-06 |
| Future start | Открыть future `Prepared` task | `InProgress` disabled; status-control opacity остаётся `1` | Availability + Headless semantic assertions + FlaUI screenshot | S2-AC-07 |
| Blocked task | Открыть graph-blocked task | `InProgress`/`Completed` disabled; status-control opacity `0.4` | Direct/inherited blocker tests + UI | S2-AC-08 |
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
| Unarchive | raw last non-archived history в cached ViewModel | authoritative normalization внутри storage command boundary | No bulk rewrite; action-time normalization | domain + stale-cache + locked concurrency + parent/child UI tests |
| Desktop picker | hidden denied options | четыре non-current rows; denied disabled с inline reason/HelpText | AutomationIds сохраняются | Headless + FlaUI |
| Status mutation | generic optimistic ViewModel update | `ITaskStorage.TrySetStatusAsync` + `TryUnarchiveAsync` + structured result/cache refresh | Default-interface/CLR-compatible additive API; wire schema unchanged | stale/save/precondition/no-mutation tests |
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
10. Future begin не меняет `IsCanBeCompleted`, `Unlocked` membership и status-control opacity.
11. Graph unavailable даёт status-control opacity `0.4`; future-only task остаётся `1`.
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
- `TaskGraphCommandService.TrySetStatusAsync` -> под write lock re-evaluate и применяет explicit target; `TryUnarchiveAsync` внутри той же boundary разрешает target из authoritative history и проверяет archived precondition.
- `UnifiedTaskStorage.TrySetStatusAsync` / `TryUnarchiveAsync` -> общий command gate + cache refresh.
- `TaskTreeManager.UpdateTask` -> legacy generic mixed update, не используется новыми status entry points.
- `TaskItemViewModel.RefreshStatusOptions` -> preview обновляется при status, graph availability, planned begin и completion criteria changes.
- `TaskItemViewModel.StatusOption` и `MainWindowViewModel.Ctrl+D` -> explicit-target command; `ArchiveCommand`/async child cascade -> archive target либо dedicated unarchive intent без cached restore target.
- `TaskStatusPicker.BuildStatusFlyout` -> строит четыре non-current items, включая disabled denied targets с reason.
- `MainControl` context menu -> reactive Archive/Unarchive label.
- Telegram `ShowTask` -> enabled targets only; callback -> validated method.
- README/status spec -> обновляются в одном PR после production/tests.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- `TaskStatus`, `StatusHistory`, `CompletionCriteria`, dates и relation lists не меняются.
- Добавляются только runtime value types/policy/reason codes.
- `ITaskStorage` получает публичные default fail-closed `.NET` methods `TrySetStatusAsync` и `TryUnarchiveAsync`; wire DTO/JSON schema не меняются.
- Legacy history не переписывается массово.
- Accepted unarchive записывает существующую обычную status history entry; stale-source `StatusPreconditionFailed`, другой deny/no-op не пишет запись.
- Wire DTO/JSON compatibility: без изменений; public in-process `.NET` API расширяется additively с default implementations, старые CLR factory signatures и enum numerics остаются доступны; production adapters/управляющие doubles переопределяют новые intents.

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
- **S2-AC-03:** Все входящие в Stage 2 user status writes идут через storage-backed `ITaskStorage.TrySetStatusAsync` либо intent-specific `TryUnarchiveAsync`; preview не мутирует ViewModel и не вычисляет authoritative restore target. Stable deny/no-op/precondition failure не меняет status/history/timestamps/version/file и возвращает `AuthoritativeTask`, когда persisted task известна; stale graph/date/criteria/status/history, save failure, outcome-unknown, invalid-equals-invalid и duplicate Telegram callback имеют structured result tests. Generic `UpdateTask` отдельно сохраняет текущие `same status + title` и denied-status mixed-update semantics.
- **S2-AC-04:** Desktop picker скрывает current status, показывает остальные четыре, allowed enabled, denied presentation соответствует решению пользователя; для disabled-варианта reason постоянно видим, RU/EN локализован, имеет `ShowOnDisabled`, HelpText и stable AutomationIds. Runtime EN↔RU switch обновляет уже созданные task/options, cached reason и открытый flyout без status/history mutation. Headless проверяет semantic/accessibility contract, а FlaUI — реальный Windows render, pointer tooltip и end-user interaction.
- **S2-AC-05:** Unarchive normalization соответствует утверждённой таблице для `NotReady`, `Prepared`, `InProgress`, `Completed`, missing/corrupt history; undefined target/source/history разделены и покрыты `(TaskStatus)int.MaxValue`, null, equal-timestamp, far-future и newer-invalid/older-valid cases. End-to-end FileStorage command с null/future legacy history не падает, сохраняет raw entries и добавляет ровно одну новую entry.
- **S2-AC-06:** Parent/child unarchive cascade вызывает dedicated storage-bound normalization отдельно для parent и каждого child; stale cached history не влияет на target, а authoritative non-archived parent даёт `StatusPreconditionFailed`, hydrate cache и останавливает confirmation/cascade. `0 children`, null manager, no/click-away/exception не меняют children и не зависают; confirmed tasks обрабатываются awaited sequentially, accepted task получает одну history entry, mixed save failure даёт точный partial summary без ложного rollback.
- **S2-AC-07:** Future planned begin запрещает только `InProgress`; graph availability/Unlocked/status-control opacity не меняются.
- **S2-AC-08:** Active direct/inherited blockers запрещают start/complete и дают status-control opacity `0.4`; archived blockers не блокируют.
- **S2-AC-09:** Completion criteria блокируют только `Completed`, но не `InProgress`, graph availability или status-control opacity.
- **S2-AC-10:** Потерявшая availability `InProgress` один раз становится `Prepared` с system history entry и не зацикливается.
- **S2-AC-11:** Telegram keyboard показывает только enabled non-current targets; denied/stale/duplicate handler callback возвращает reason без мутации, а allowed callback awaits storage-backed command и показывает persisted refreshed status.
- **S2-AC-12:** README EN/RU содержат одинаковую canonical matrix и отдельно объясняют lifecycle/graph/start/complete.
- **S2-AC-13:** README исправляет future dimming и `Unlocked`; Markdown marker/export copy остаётся в Stage 7 и не меняется здесь.
- **S2-AC-14:** Old status spec содержит заметную errata/supersession note перед разделами transition rules, availability и Telegram status behavior со ссылкой на эту spec, без переписывания исторического журнала.
- **S2-AC-15:** Domain status enum, persisted schema, server wire DTO/hub methods и existing history entries не изменены; additive in-process `.NET` API (`ITaskStorage.TrySetStatusAsync`/`TryUnarchiveAsync` и `ConfirmAsync` с default bodies, structured result snapshot/reason с legacy CLR overloads, `ServerStorage.ReadGraphAsync`) и обновлённые adapters/doubles перечислены в diff/PR. Numeric values существующих denial kinds сохранены; новые reason enum values зафиксированы snapshot tests.
- **S2-AC-16:** Все перечисленные targeted filters, full `Unlimotion.Test`, full Headless и релевантный FlaUI suite PASS serially; solution и Telegram build PASS; FileStorage locked и ServerStorage non-locking/failure tests PASS; required GitHub PR checks green.
- **S2-AC-17:** Один и тот же automated flow имеет verified before/after MP4 либо объективный recorder failure с screenshots/logs; PR содержит paths, SHA-256, duration/resolution/FPS и local-only retention disclosure.
- **S2-AC-18:** PR #274 green/ready/merged до EXEC; после merge выполнены fetch/rebase, ancestry check и повтор baseline, а перед delivery — clean scope, `git diff --check`, Post-EXEC review и PR/release-note handoff.

Characterization baseline до EXEC:
- `TaskTreeManagerSafetyTests`: 3 PASS — engine запрещает terminal -> `InProgress`.
- `TaskStatusDomainTests`: 4 PASS — один тест закрепляет неправильный raw restore `InProgress` и должен быть заменён data-driven normalization matrix.
- `TaskAvailabilityParityTests`: 2 PASS — недостаточный oracle, должен быть расширен policy/consumer parity.
- `MainControlTaskStatusIconUiTests`: 20 PASS — недостаточный oracle, должен проверять независимые expected options/reasons.

Targeted tests to add/update:
- `StatusAvailabilityContractCharacterizationTests` — observation-first terminal picker/unarchive drift и post-fix contract.
- `TaskStatusTransitionPolicyTests` — raw 5x5 service parity, reason priority и invalid enum; не подменяет command-level no-op.
- `TaskAvailabilityCalculationTests` — contained/direct/inherited diagnostics, future и criteria facts.
- `TaskAvailabilityParityTests` — service facts/policy parity.
- `TaskTreeManagerSafetyTests` — legacy mixed-update compatibility и automatic rollback idempotency.
- `TaskGraphCommandServiceTests` — пять valid diagonal no-op cases, invalid-equals-invalid deny, legacy API/numeric snapshots, authoritative unarchive history/precondition, locked concurrent unarchive, non-locking diagnostic storage, concurrent/stale/save-failure/undefined-source behavior.
- `TaskStatusDomainTests` — valid-entry predicate, deterministic order, null/future idempotency и unarchive normalization table.
- `ServerStorageStatusCommandTests` (new) — diagnostic GetAll mapping, no unconditional `StorageFailed`, propagated read failure, explicit-target и unarchive authoritative/precondition/post-verify results; internal injected fetch delegate/client, без real network.
- `FileStorageTaskStatusTests` — end-to-end null/future history clone/save и ровно одна accepted entry.
- `TaskStatusTransitionTests` — ViewModel uses preview policy + storage-backed command, no optimistic assignment/duplicate switch.
- `TaskItemViewModelStatusCommandTests` — public setter compatibility, dedicated parent/child unarchive, stale cached-history inversion, precondition stop, honest no-snapshot failure copy, preview/authoritative diagnostics and reason priority.
- `UnifiedTaskStorageStatusCommandTests` — authoritative hydration, stale cached vs persisted unarchive history, deny/no-op/save failure и disposed behavior на desktop storage boundary.
- `MainControlTaskStatusIconUiTests` — four options, disabled state, tooltip, AutomationId.
- `MainControlAvailabilityUiTests` — future status-control opacity `1` vs blocker status-control opacity `0.4`.
- `MainWindowViewModelTests` — hotkey, 0-child/null-manager/yes/no/click-away/exception/mixed-history/mixed-save parent/child cascade и single history entry.
- `LocalizationDisplayDefinitionTests` — уже созданная archived task, её ViewModel/status options и cached denial copy обновляются EN↔RU без status/history/collection/option-instance mutation.
- `NotificationManagerWrapperTests` (new) — реальный `NotificationManagerWrapper` с смонтированным `MainScreen`/`DialogHost`: yes/no/click-away/programmatic close/host exception завершают `ConfirmAsync` exactly once без hang; ViewModel mock не считается заменой этого gate.
- `TelegramStatusContractTests` — full handler-level keyboard/callback test без real Telegram network.
- `StatusContractScenariosBase` inherited Headless/FlaUI `StatusContract_TerminalPickerAndUnarchive`; Headless владеет dynamic menu rows/HelpText и semantic theme assertions, FlaUI — end-user click/keyboard flow, visible result и pixel screenshots.
- `MainWindowHeadlessTests.StatusContract_RussianDarkFutureAndBlocker` — уже открытый picker и archive command синхронно обновляют visible title/reason EN↔RU; сценарий остаётся semantic/accessibility oracle без raster claim.
- Изолированные FlaUI `StatusContract_RussianDarkFuture` и `StatusContract_RussianDarkBlocked` проверяют фактические RU/dark rows, opacity и pointer tooltip без разделяемого process state.

Visual acceptance:
- Storyboard frames A-F соблюдены в desktop app.
- Disabled item визуально отличим, текст/иконка читаемы в light/dark theme.
- Reason постоянно видим в disabled row, tooltip открывается pointer при `ShowOnDisabled=true`, HelpText/automation name доступен screen reader; keyboard не обязан фокусировать disabled action.
- EN и RU reason mapping проверены отдельно; RU UI screenshot содержит фактическую русскую причину.
- Уже открытый picker и Archive/Unarchive copy обновляются при runtime EN↔RU switch.
- Archive/Unarchive copy соответствует status.
- Future и blocker различаются status-control opacity.
- Automation test использует row-scoped selectors, не случайный первый picker.
- Уже открытый flyout не исполняет stale preview после конкурентного blocker/date/status change: click/callback получает command-level deny и UI refresh.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| S2-AC-01 | `TaskStatusTransitionPolicyTests`, parity tests | Diff confirms no duplicate switch | test log | — |
| S2-AC-02,03 | policy/manager/command/VM/Telegram adapter tests | Inspect denied UI/callback | test log + before/after video | — |
| S2-AC-04 | Headless semantic/accessibility assertions + inherited и RU/dark FlaUI scenarios | Light/dark screenshots, реальный pointer tooltip | screenshots/video | Headless fake drawing backend не используется как pixel oracle |
| S2-AC-05,06 | domain normalization + ViewModel cascade + real `NotificationManagerWrapperTests` | Unarchive visible result and DialogHost dismissal | after video/log | — |
| S2-AC-07 | availability/domain/UI + `StatusContract_RussianDarkFuture` | Future opacity screenshot | `after-future-vs-blocked.png` | — |
| S2-AC-08 | direct/inherited/archived blocker tests + `StatusContract_RussianDarkBlocked` | Blocked opacity и pointer tooltip screenshot | `after-blocked.png` | — |
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
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/StatusAvailabilityContractCharacterizationTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskAvailabilityCalculationTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskAvailabilityParityTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskTreeManagerSafetyTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskGraphCommandServiceTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskStatusDomainTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/ServerStorageStatusCommandTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/FileStorageTaskStatusTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskStatusTransitionTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskItemViewModelStatusCommandTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/UnifiedTaskStorageStatusCommandTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainControlTaskStatusIconUiTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainControlAvailabilityUiTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowViewModelTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/LocalizationDisplayDefinitionTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/NotificationManagerWrapperTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TelegramStatusContractTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowHeadlessTests/StatusContract_TerminalPickerAndUnarchive" --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowHeadlessTests/StatusContract_RussianDarkFutureAndBlocker" --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowFlaUiTests/StatusContract*" --maximum-parallel-tests 1 --output Detailed
```

Если `dotnet test ... -- --list-tests` для UI project сообщает 0, discovery выполняется repo-proven fallback-командой `dotnet run --project <UiTests.csproj> -c Debug --no-build -- --list-tests`; известный FlaUI baseline должен содержать не менее 9 inherited nodes до добавления нового scenario. Любая targeted команда обязана показать ровно ненулевое число tests; exit code 0 без executed node не считается PASS.

Full gate:

```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-build -- --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainWindowFlaUiTests/StatusContract*" --maximum-parallel-tests 1 --output Detailed

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
2. Завершено 2026-07-18: PR #274 green/ready/merged как `8e34408`; lifecycle blocker был вынесен в отдельный merged PR #275.
3. Завершено 2026-07-18: fetch/rebase/ancestry и повторный characterization baseline PASS.
4. Завершено 2026-07-18: observation-first characterization/UI scenario и before FlaUI MP4 записаны до fixes.
5. Завершено: pure Domain off-diagonal transition policy/facts/reason codes, command diagonal contract и invalid source/target/history tests добавлены.
6. Завершено: default-compatible `ITaskStorage.TrySetStatusAsync`/`TryUnarchiveAsync`, command/storage adapters, cache hydration и structured stale/save/no-op/precondition behavior реализованы с CLR/source/numeric compatibility tests.
7. Завершено: deterministic authoritative unarchive normalizer внутри storage boundary и awaited parent/child cascade реализованы с stale-history, locked concurrency, server verification и mixed-failure tests.
8. Завершено: ViewModel/picker/`Ctrl+D` переведены на storage-backed path; denied UX, localized accessibility contract и reactive Archive/Unarchive label реализованы.
9. Завершено локально: Headless semantic contract, три focused FlaUI flows, RU/EN/light/dark assertions, after MP4 и четыре screenshots PASS.
10. Завершено: Telegram keyboard/callback переведены на storage-backed path и покрыты handler tests/build.
11. Завершено: README EN/RU и точечная errata старой spec обновлены; Stage-7 marker-export copy не менялась.
12. Локальный gate завершён 2026-07-18: targeted Unit, full Unit 755/755, full Headless 33/33, focused FlaUI 3/3, solution/Telegram builds, diff/schema/API checks, visual evidence и независимый Post-EXEC review PASS. Delivery завершён: commit `b7166d6`, PR #277, required GitHub checks PASS и merge commit `75efc0497af0a1b4678372b67112a8f606ce28c9`.

## 14. Открытые вопросы
Блокирующих product-вопросов нет. Пользователь 2026-07-17 утвердил рекомендованный набор: disabled-with-reason, legacy fallback `NotReady`, Telegram included, и сообщил точную фразу `Спеку подтверждаю`.

Внешняя dependency закрыта: PR #274 merged, rebase/ancestry/baseline gate пройден. Оставшиеся lifecycle/CAS/Headless-capture вопросы изолированы как follow-up и не расширяют утверждённый Stage-2 scope.

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

Таблица актуализирована по фактическому EXEC diff; строки могут группировать однотипные test doubles и regression suites.

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Domain/TaskStatusTransitionPolicy.cs` (new) | Pure facts/evaluation/reason/restore normalization | Единый contract без UI dependency |
| `src/Unlimotion.Domain/TaskItem.cs` | Normalized restore + null/future-safe idempotent history helpers | Исправить unarchive bypass/duplicate entry |
| `src/Unlimotion.Domain/TaskStatusExtensions.cs` | Null-safe history queries либо delegation в new normalizer | Не падать на corrupt legacy list |
| `src/Unlimotion.TaskTreeManager/TaskAvailabilityService.cs` | Делегировать matrix pure policy | Удалить отдельный switch source |
| `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs` | Сохранить generic mixed-update semantics и использовать existing mutation lock как bridge для dedicated command path | Command consistency без скрытого API break |
| `src/Unlimotion.TaskTreeManager/TaskGraphCommandService.cs` | Dedicated explicit-target и authoritative-history unarchive intents, precondition/no-op/invalid-target alignment, structured diagnostics | Authoritative storage-write boundary |
| `src/Unlimotion.TaskTreeManager/TaskOperationResult.cs` | Additive `AuthoritativeTask`/reason data через distinct factories; legacy overloads и existing enum numerics сохранены | Stale-cache hydration без binary/source ambiguity |
| `src/Unlimotion.ViewModel/ITaskStorage.cs` | Добавить default fail-closed `TrySetStatusAsync`/`TryUnarchiveAsync` contracts | Запрет optimistic/cached-history mutation без break старых implementers |
| `src/Unlimotion/UnifiedTaskStorage.cs` | Реализовать общий local command gate, два adapters и cache hydration из `AuthoritativeTask`/`ChangedTasks` | Storage-backed desktop/Telegram result |
| `src/Unlimotion/ServerStorage.cs` | Реализовать diagnostic graph read через existing endpoint с propagated errors/internal test seam; без wire/CAS claim | Не сломать server-backed status commands |
| Четыре `ITaskStorage` doubles: `tests/Unlimotion.Performance/Program.cs`, `src/Unlimotion.Test/TaskItemRepeaterListMarkerTests.cs`, `src/Unlimotion.Test/RoadmapGraphUiTests.cs`, `src/Unlimotion.Test/MainControlTaskStatusIconUiTests.cs` | Реализовать новый method или shared fake adapter | Сохранить compile и controllable results |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Удалить duplicate switch/cached restore target, async validated methods, awaited dedicated parent/child unarchive, reactive label | Desktop contract и authoritative stale-history safety |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Перевести `Ctrl+D` на async status command; централизованно refresh existing task status copy при culture switch | Закрыть optimistic hotkey path и stale localization без per-task subscriptions |
| `src/Unlimotion.ViewModel/INotificationManagerWrapper.cs` | Additive default fail-closed generic `ConfirmAsync` | Awaitable confirmation contract без break старых implementers |
| `src/Unlimotion/NotificationManagerWrapper.cs` | Exactly-once yes/no/dismiss/exception completion | Не зависать и не запускать fire-and-forget writes |
| `src/Unlimotion.Test/NotificationManagerWrapperMock.cs`, `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAppLaunchHost.cs` | Реализовать deterministic confirmation result/dismiss behavior | Compile + unit/UI cascade tests |
| `src/Unlimotion.Test/NotificationManagerWrapperTests.cs` (new) | Смонтировать реальный `MainScreen`/`DialogHost` и проверить yes/no/click-away/close/exception exactly once | Исполнимый gate production `ConfirmAsync`, не только mock ViewModel |
| `src/Unlimotion.ViewModel/TaskStatusOption.cs` | Reason mapping/state и localization notifications | Disabled picker copy, включая уже открытый flyout |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` / `Strings.ru.resx` | Localized denial, honest storage-failure retry, stale-source и Archive/Unarchive text | UX/accessibility без ложного refresh claim |
| `src/Unlimotion/TaskStatusPicker.cs` | Disabled non-current options + inline reason/`ShowOnDisabled`/HelpText + reactive localized title binding | Утверждённый denied UX и runtime language parity |
| `src/Unlimotion/Views/MainControl.axaml` | Reactive command header/AutomationId при необходимости | Correct Unarchive copy |
| `src/Unlimotion.TelegramBot/Bot.cs` | Enabled targets only, storage-backed callback | Закрыть policy bypass |
| `src/Unlimotion.TelegramBot/TelegramStatusContract.cs` (new), `AssemblyInfo.cs` (new) | Чистый keyboard/callback adapter и test internals | Direct bot coverage без network/public API expansion |
| `src/Unlimotion.Test/Unlimotion.Test.csproj` | Bot project reference для handler test | Test Telegram adapter |
| `src/Unlimotion.Test/InMemoryStorage.cs` | Реализовать diagnostic graph read и deep clone | Executable command/storage tests без filesystem dependency |
| `src/Unlimotion.Test/StatusAvailabilityContractCharacterizationTests.cs` (new) | Observation-first baseline/contract characterization | Зафиксировать исходный drift и итоговый contract |
| `src/Unlimotion.Test/TaskItemViewModelStatusCommandTests.cs`, `UnifiedTaskStorageStatusCommandTests.cs` (new) | Setter/preview/no-optimistic-write, dedicated unarchive stale-history/precondition и storage hydration/failure coverage | Проверить additive API compatibility и mutation boundary |
| `src/Unlimotion.Test/TaskStatusTransitionPolicyTests.cs` (new) | Raw 5x5/reasons/invalid parity и reason numeric snapshot | Canonical stable raw-policy tests |
| `src/Unlimotion.Test/TaskAvailabilityCalculationTests.cs`, `TaskAvailabilityParityTests.cs`, `TaskTreeManagerSafetyTests.cs`, `TaskGraphCommandServiceTests.cs`, `TaskStatusDomainTests.cs`, `FileStorageTaskStatusTests.cs`, `TaskStatusTransitionTests.cs`, `MainControlTaskStatusIconUiTests.cs`, `MainControlAvailabilityUiTests.cs`, `MainWindowViewModelTests.cs` | Расширить exact cases из section 11 | Regression coverage |
| `src/Unlimotion.Test/LocalizationDisplayDefinitionTests.cs` | Existing task/status option PropertyChanged и no-mutation assertions при EN↔RU | Закрыть runtime localization regression |
| `src/Unlimotion.Test/ServerStorageStatusCommandTests.cs` (new) | Existing-endpoint diagnostic/failure/non-locking explicit-target и unarchive contract | Server-mode regression |
| `src/Unlimotion.Test/TelegramStatusContractTests.cs` (new) | Handler-level keyboard/callback contract без network | Проверить Telegram shared-policy integration |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Row-scoped status/archive controls и generic accessibility reads только для supported adapter primitives | Shared user-flow page object |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenario.cs`, `UnlimotionAutomationScenarioData.cs`, `UnlimotionAppLaunchHost.cs` | StatusContract scenario, deterministic status/future/blocked seed и recorder handshake | Stable automation evidence |
| `tests/Unlimotion.UiTests.Authoring/Tests/StatusContractScenariosBase.cs` (new) | Shared terminal/unarchive flow с явной screenshot capability | Один observation-first contract для Headless/FlaUI без ложной pixel parity |
| `tests/Unlimotion.UiTests.Headless/Tests/MainWindowHeadlessTests.cs` / headless-specific helper | Dynamic menu row, tooltip/HelpText, RU/EN и light/dark assertions | Покрыть adapter capabilities, которых нет в shared abstraction |
| `tests/Unlimotion.UiTests.FlaUI/Tests/MainWindowFlaUiTests.cs` | End-user click/keyboard flow и accessibility-tree assertions | Реальный Windows UI evidence |
| `scripts/record-status-contract-evidence.ps1` (new) | Test/window handshake, `record_app_window.ps1` orchestration, 1280x800/30fps и `ffprobe`/SHA report | Воспроизводимое before/after video evidence |
| `README.md` / `README.RU.md` | Canonical matrix и semantic corrections | Public truthfulness |
| `specs/2026-06-09-task-status-model.md` | Supersession/errata перед 6.2, 7.2, status-control и Telegram claims | Не выдавать stale contract за current |
| `specs/2026-07-17-status-availability-contract.md` | Approval/Post-EXEC journal | Audit trail |

Фактический scope перед delivery: 45 tracked production/test/docs файлов изменены и 12 новых файлов добавлены; после journal updates также изменены эта child spec и master roadmap (итого 47 tracked content diffs). `artifacts/ui-tests/status-contract/*` намеренно ignored/local-only и в commit не входят. Unrelated changes не обнаружены.

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

Итог SPEC gate: `ГОТОВО`; prerequisite PR #274 закрыт 2026-07-18, EXEC и локальный validation gate завершены, package находится на delivery gate.

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
Зона: автономный EXEC разрешён и выполнен после закрытия prerequisite gate; итоговый verdict фиксируется ниже в Post-EXEC.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Matrix, unarchive, blockers и Telegram соответствуют user workflow? | PASS | Утверждены terminal matrix, `Prepared`/`NotReady` normalization и Telegram parity |
| UX / designer | applicable | Disabled reasons, copy, future/blocker distinction и storyboard ясны? | PASS | Visible disabled reasons/HelpText, RU/EN/light/dark и fallback accessibility state заданы |
| Tester / validation | applicable | Каждый AC имеет negative/edge/UI evidence? | PASS | Exact filters, unconditional Telegram gates, real ConfirmAsync gate и video handshake повторно проверены |
| Developer / architect | applicable | Pure policy layering, no-mutation и no-schema boundaries coherent? | PASS | Storage fallback/concurrency, command ordering, authoritative snapshot, history и generic compatibility повторно проверены |
| Delivery / operations / security | applicable | Dependency/rebase, artifacts, rollback и CI gates безопасны? | PASS | PR #274 dependency, separate CI-fix scope, rebase/ancestry, evidence и required-check stop rules явные |

### Post-SPEC Review (исторический gate перед EXEC)
- Статус: PASS после technical и product-specific fix/re-review cycles; на момент review PR #274 оставался обязательным sequencing prerequisite, который закрыт 2026-07-18
- Scope reviewed: эта spec, source/test/docs evidence, central routing, branch/dependency metadata, PR #274 CI, recorder prerequisites и executable TUnit/UI filters
- Decision: approval принят; Stage 2 можно исполнять только после green/ready/merge PR #274 и rebase/ancestry/baseline gate
- Review passes:
  - Scope/Evidence pass: PASS — Stage-7 marker claim исключён, Telegram включён явно, stage-1 merge закреплён prerequisite.
  - Contract pass: PASS after re-review — storage-backed command, raw/effective matrix split, generic compatibility, invalid cases и ServerStorage boundary согласованы.
  - Adversarial risk pass: PASS after re-review — accessibility, deterministic history, mixed cascade, real ConfirmAsync gate, code-only rollback и full-flow video orchestration согласованы.
  - Role-Based pass: PASS — Business/UX/Tester/Architecture/Delivery contracts согласованы с утверждёнными choices.
  - Re-review after fixes / Fix and re-review: PASS — невыбранные hidden/Prepared/exclude-Telegram ветви удалены из executable contract; conditional commands/file inventory и два последних Telegram test/callback упоминания сделаны обязательными.
  - Stop decision: PASS для spec/approval; исторический STOP production до prerequisite PR #274 был соблюдён.
- Evidence inspected: source audit; 29 baseline targeted tests PASS; три initial independent NEEDS-FIX verdicts, два focused technical PASS verdicts и заключительный independent product-specific audit; exact engine/command/storage/ViewModel/Telegram source; old spec headings; PR #274 checks; actual recorder script/ffmpeg/ffprobe preflight; TUnit/FlaUI discovery behavior
- Depth checklist:
  - Scope drift / unrelated changes: только эта spec изменена
  - Acceptance criteria: mapping расширен storage/concurrency/accessibility/history/CI cases
  - User-observable scenarios / Decision ledger / Expected objections: заполнены; user-owned decisions имеют chosen values и `Needs user = Нет`
  - Validation evidence: baseline и exact targeted/full/video contract были заданы до EXEC; фактическое evidence отражено в Post-EXEC ниже
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
| MEDIUM | delivery prerequisite | На момент Post-SPEC PR #274 ещё не был merged | Green/ready/merge, fetch/rebase/ancestry/baseline до production edits | fixed 2026-07-18; gate observed |

- Fixed before continuing: оба technical fix set и product-choice fix set внесены; hidden/Prepared/exclude-Telegram executable branches и два последних conditional Telegram test/callback упоминания удалены
- Checks rerun: baseline targeted tests; structural spec checks PASS (22 H2, even fences, no unresolved decision rows); independent architecture/test re-review и заключительный product-specific audit PASS после fixes
- Needs human: Stage 2 approval/choices закрыты; отдельная новая CI-lifecycle child spec потребует собственную точную approval-фразу до её code EXEC
- Residual risks / follow-ups: исторические prerequisite/recorder implementation risks закрыты; актуальные residuals перечислены в Post-EXEC

### Post-EXEC Review
- Статус: `PASS / DELIVERED`; утверждённый EXEC, локальные implementation/validation/UI-evidence gates и внешний GitHub delivery завершены 2026-07-18; PR #277 merged как `75efc0497af0a1b4678372b67112a8f606ce28c9`
- Scope reviewed: 47 tracked content diffs и 12 новых файлов в утверждённых Domain, TaskTreeManager, ViewModel, desktop UI, storage adapters, Telegram, tests, paired README и spec/journal surfaces; `artifacts/ui-tests/status-contract/*` ignored/local-only и не входит в commit
- Decision: S2-AC-01..18 выполнены; package доставлен в `main`. Stage 3 не начинается до отдельной child SPEC, Post-SPEC PASS и явного approval
- Review passes:
  - Scope/Evidence pass: PASS — фактический diff сверен с section 16, unrelated files и schema/wire surfaces не затронуты; до/после video, шесть screenshots, TRX/HTML и build/diff evidence проверены.
  - Contract pass: PASS — одна pure 5x5 policy управляет desktop/Telegram adapters; mutations идут через storage-backed commands; dedicated unarchive вычисляет target по freshly-read authoritative history внутри local write boundary.
  - Adversarial risk pass: PASS — проверены invalid/undefined values, diagonal no-op ordering, corrupt/future/null history, stale cache, parent/child partial failure, concurrent unarchive, storage failure, server post-verification и runtime localization.
  - Role-Based pass: PASS — Business/UX/Tester/Architecture/Delivery reviews не оставили BLOCKER/HIGH/MEDIUM findings после fixes.
  - Re-review after fixes / Fix and re-review: PASS — повторный API/compatibility, code, docs parity и UI-evidence reviews подтвердили fixes и честные residuals.
  - Stop decision: delivery gate закрыт после green checks и merge PR #277; stage 3 остаётся закрыт отдельным approval gate.
- Evidence inspected:
  - full Unit: 755/755 PASS, `C:\tmp\unlimotion-stage2-unit-20260718-final6\Unlimotion.Test-windows-net10.0-report.html` и `Kibnet_DESKTOP-AUDO1TJ_2026-07-18_19_04_20.6806803.trx`;
  - full Headless: 33/33 PASS, `C:\tmp\unlimotion-stage2-headless-20260718-final6\Unlimotion.UiTests.Headless-windows-net10.0-report.html` и `Kibnet_DESKTOP-AUDO1TJ_2026-07-18_19_06_13.4238520.trx`;
  - focused FlaUI: 3/3 PASS, `C:\tmp\unlimotion-stage2-flaui-20260718-final7\Unlimotion.UiTests.FlaUI-windows-net10.0-report.html` и `Kibnet_DESKTOP-AUDO1TJ_2026-07-18_19_14_56.8637630.trx`;
  - final targeted reruns: `TaskGraphCommandServiceTests` 38/38, `TaskItemViewModelStatusCommandTests` 15/15, `UnifiedTaskStorageStatusCommandTests` 12/12, `FileStorageTaskStatusTests` 6/6, `ServerStorageStatusCommandTests` 10/10 и `TaskStatusTransitionPolicyTests` 42/42 PASS;
  - `dotnet build src/Unlimotion.sln -c Debug --no-restore -p:UseSharedCompilation=false`: PASS, 0 errors, 118 known baseline/platform/line-ending warnings; отдельный Telegram build: PASS, 0 errors, 47 warnings; exact warning counts наблюдались в финальном session output, отдельный build log не сохранялся;
  - `git diff --check`, protected enum/schema/server-interface/service-model audit и проверка отсутствия cached-history resolver в `TaskItemViewModel`: PASS.
  - GitHub delivery: PR #277; `All tests`, `android-build` и все CodeQL jobs PASS; commit `b7166d6` merged в `main` как `75efc0497af0a1b4678372b67112a8f606ce28c9`; remote feature branch удалена.
- UI automation evidence:
  - before video: `artifacts/ui-tests/status-contract/before-terminal-unarchive.mp4`, H.264, 1280x800, 105 s, 3141 frames, average 29.914 fps, SHA-256 `15D509B1C3A1F1EC22951B87118DF0D225B4B5B9080949565941D9EF793F7910`; recorder flow завершён с двумя ожидаемыми baseline failure ids;
  - after video: `artifacts/ui-tests/status-contract/after-terminal-unarchive.mp4`, H.264, 1280x800, 105 s, 3145 frames, nominal 30 fps / average 29.952 fps, SHA-256 `3B175D5280519FE297C98289A32643BBD04480CA2E6096AC3BE1D8FFC9525281`; emitted wrapper session output reported run id `c611cf0ddf644f24af3f28be5a8b5d08`, test exit 0 и empty failure ids, но transient handshake JSON удалён wrapper cleanup;
  - screenshots: `before-terminal-picker.png`, `before-after-unarchive.png`, `after-terminal-picker.png`, `after-after-unarchive.png`, `after-future-vs-blocked.png`, `after-blocked.png`; внешний test window/video настроен на 1280x800, все PNG client-area captures имеют 1252x721; финальные четыре кадра просмотрены и подтверждают terminal reasons, restore to `Prepared`, RU/dark future reason и реальный pointer tooltip для blocker reason.
- Depth checklist: source-to-AC trace, public CLR/source/numeric compatibility, default-interface fallback, schema/wire stability, authoritative write boundary, localization/accessibility, executable UI semantics, video metadata/hash, rollback и delivery scope проверены.
- No-findings justification: после последнего fix cycle независимые code/API и docs-parity reviewers вернули PASS; новые BLOCKER/HIGH/MEDIUM findings отсутствуют, а перечисленные ниже ограничения не маскируются как закрытые.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | storage lifecycle | Прямой `UnifiedTaskStorage.Dispose()` не атомарно блокирует уже начавшийся confirmation producer | Вести отдельной production-storage-lifecycle child spec; не расширять Stage 2 | follow-up |
| LOW | server concurrency | Existing transport не даёт cross-client compare-and-swap | Сохранять честный `OutcomeUnknown`; проектировать server-authoritative command отдельно | follow-up |
| LOW | UI infrastructure | Fake Headless backend не является pixel oracle; real-Skia capture и process-global DPI awareness требуют отдельного hardening | Оставить semantics в Headless, реальное rendering evidence во FlaUI/video | follow-up |
| LOW | public records | Новые additive record properties участвуют в equality/`ToString` и могут появиться у стороннего generic JSON serializer | Зафиксировать compatibility caveat; официальный persistence/server wire не затронут | accepted residual |
| INFO | delivery evidence | UI artifacts intentionally ignored/local-only; exact build warning counts и wrapper RunId/TestExitCode/FailureIds сохранены только в session output/spec, без отдельного build/handshake log | Указать durable TRX/video/hash/paths и provenance полей в PR; не коммитить evidence и не выдавать session-derived поля за отдельный retained log | accepted residual |

- Fixed before final report: public setter/API/numeric compatibility, blocker diagnostic priority, real tooltip and exact row selection, flyout close, deterministic confirmation TCS, recorder FPS gate, runtime localization including an open picker, README opacity truthfulness, authoritative stale-history unarchive/precondition и честная storage-failure copy.
- Checks rerun: полный Unit/Headless/FlaUI gate, six final targeted classes, solution/Telegram builds, `git diff --check`, schema/wire audit, media `ffprobe`/SHA и independent code/docs re-reviews.
- Validation evidence: `PASS / DELIVERED`; локальный gate дополнен green GitHub checks и merged PR #277.
- Unrelated changes: не обнаружены; scope = 47 tracked content diffs + 12 new, ignored local UI evidence исключено.
- Needs human: для Stage-2 delivery — нет; Stage-3 child spec требует отдельного explicit approval до EXEC.
- Residual risks / follow-ups: storage lifecycle, server CAS, Headless/real-Skia and DPI capture hardening, generic external record serialization caveat и known build warnings, как перечислено в таблице.

## Approval
Пользователь 2026-07-17 сообщил точную фразу `Спеку подтверждаю` и попросил выполнить все этапы. Approval трактуется вместе с ранее предложенным рекомендованным набором: disabled-with-reason, legacy fallback `NotReady`, Telegram included.

Stage-2 EXEC разрешён, но production edits остаются заблокированы explicit dependency gate до green/ready/merge PR #274, fetch/rebase, ancestry check и повторного characterization baseline.

Dependency update 2026-07-18: PR #274 merged как `8e34408`; fetch/rebase, ancestry и characterization gates выполнены до production edits. Указанная выше блокировка была соблюдена и больше не активна; локальный validation и delivery gates закрыты merged PR #277 (`75efc049`).

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
| EXEC | Реализовать и стабилизировать Stage-2 contract | 1.00 | Нет | Выполнить финальный full gate | Нет | Не применимо | Storage-backed status/unarchive, disabled reasons, Telegram parity, localization, README/errata и compatibility guards реализованы; поздние review findings исправлены | Production/test/docs diff, UI automation harness |
| EXEC | Завершить локальный Stage-2 validation и Post-EXEC review | 1.00 | Только внешний GitHub delivery gate | Commit/push, draft PR, required checks и merge | Нет для Stage 2 | Не применимо | Unit 755/755, Headless 33/33, FlaUI 3/3, builds/diff/schema/media gates и independent re-reviews PASS; residuals честно маршрутизированы | TRX/HTML, `artifacts/ui-tests/status-contract/*` local-only, эта spec, master roadmap |
| EXEC | Завершить Stage-2 GitHub delivery | 1.00 | Нет | Зафиксировать merge record и открыть Stage-3 SPEC gate | Нет | Не применимо | Commit `b7166d6` прошёл `All tests`, Android и CodeQL checks; PR #277 merged в `main` как `75efc049`, remote branch удалена | GitHub PR #277, `origin/main@75efc049`, эта spec, master roadmap |
