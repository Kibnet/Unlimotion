# CLI-контракт обзора задач и применения одобренных предложений ночного агента

## 0. Метаданные
- Тип (профиль): delivery-task, expanded SPEC; `product-system-design` + `dotnet-desktop-client` + `ui-automation-testing` для проверки UI-facing состояния после CLI-записи.
- Владелец: пользователь — продуктовые решения и одобрение предложений; агент — технический контракт и реализация после approval.
- Масштаб: large — публичный CLI-контракт, несколько модулей, persisted state, optimistic concurrency, графовые инварианты и crash recovery.
- Целевое семейство / behavior baseline: GPT-6 Astra baseline применяется к работе Codex; контракт CLI не зависит от модели агента.
- Поверхность: Codex desktop / локальный `unlimotion-cli`; файловое task space Unlimotion.
- Effective runtime: текущая сессия Codex; точный model ID и reasoning mode не раскрыты средой и не влияют на формат CLI.
- Eval baseline / evidence: проверка исходников на commit `d4d90de7c310de9c195019e5b46101e5277f7c69`; существующие `UnlimotionCliIntegrationTests`, `TaskGraphCommandServiceTests`, `FileTaskStorageRecoverableMutationTests`, `CliLiveRefreshHeadlessTests`.
- Целевой релиз / ветка: отдельный worktree, detached HEAD; релиз и ветка до approval не назначаются.
- Ограничения: фаза QUEST/SPEC; до точной фразы `Спеку подтверждаю` меняется только этот файл. Не выполнять массовый обзор, не создавать расписание, не менять реальные задачи, не делать commit/push/PR/release/deploy.
- Связанные ссылки: `src/Unlimotion.Cli/README.md`, `src/Unlimotion.Cli/Program.cs`, `src/Unlimotion.TaskTreeManager/TaskGraphCommandService.cs`, `src/Unlimotion.TaskTreeManager/TaskAvailabilityService.cs`, `src/Unlimotion.FileStorage/FileTaskStorage.cs`, `src/Unlimotion.Domain/TaskItem.cs`, предыдущие SPEC `2026-09-13-agent-executor-cli*.md`.
- Instruction stack: central `AGENTS.md`; `creator-vibe-lens` (полный creative skill не требуется); `model-behavior-baseline`; `quest-governance`; `collaboration-baseline`; `testing-baseline`; `tool-execution-baseline`; `quest-mode`; `session-insights-context`; `dotnet-desktop-client`; `product-system-design`; `ui-automation-testing`; `spec-linter`; `spec-rubric`; `review-loops`; локальный `AGENTS.override.md`.

## 1. Overview / Цель

Дать ночному агенту полный read-only обзор всех разблокированных задач выбранного task space или подграфа, а дневному агенту — минимальный безопасный CLI-контракт для preview и применения только одобренных пользователем редакций предложений.

Outcome contract:
- Success means: агент способен без `claim` получить полный набор разблокированных задач; каждое предложение ссылается на наблюдённый `etag`; один одобренный JSON-манифест либо целиком применяется в одном task space, либо не оставляет частичного графа; повтор после сбоя не создаёт дубли; результат перечитывается и различает `applied`, `alreadyApplied`, `conflict`, `outcomeUnknown`.
- Итоговый артефакт / output: обратно совместимые расширения `unlocked`/`task`, новая команда `apply`, JSON Schema/DTO запроса и ответа, доменный batch command в `TaskGraphCommandService`, минимальные application receipts и тесты инвариантов.
- Stop rules: не применять манифест при несовпадении `etag`, конфликтующих операциях, недопустимом status transition, активной lease-bound попытке при смене статуса, сломанном графе, cycle, неизвестном исходе без authoritative read-back или попытке затронуть больше одного task space.

## 2. Текущее состояние (AS-IS)

### 2.1 Проверенные возможности

| Потребность ночного/дневного агента | Текущая возможность | Пробел | Предлагаемый контракт |
| --- | --- | --- | --- |
| Получить все разблокированные задачи task space | `unlocked` возвращает все `CanStart`, без лимита | Нет ограничения подграфом нескольких целей | Добавить повторяемый `--root`; без него контракт не меняется |
| Получить подробный контекст задачи | `task --include details,relations,criteria,history,execution` | Нет устойчивого optimistic concurrency token; relations только один hop | Добавить `etag` в expanded snapshot; ancestor traversal остаётся workflow агента |
| Не захватывать задачу ради обзора | `unlocked` и `task` read-only | Пробела нет | Явно запретить implicit `claim` в новых read/apply flows |
| Создать задачу с несколькими родителями | `create --parent` симметрично обновляет `ContainsTasks`/`ParentTasks` | Нет заданного task ID и batch-idempotency; мало полей | `apply/createTask` требует заранее выбранный `taskId`, поддерживает несколько parents и начальные поля |
| Изменить оценку активного времени | В модели есть `PlannedDuration`; CLI только читает | Нет записи | `apply/setField(plannedDuration)` с ISO 8601 duration |
| Добавить подготовленный контекст | CLI не редактирует пользовательскую часть Description | Нельзя безопасно сохранить marker `AgentExecution` | `setField(descriptionUserText)` заменяет только user-owned segment и сохраняет protected block |
| Изменить Title | CLI только читает | Нет записи | `apply/setField(title)` |
| Изменить плановые даты | CLI только читает | Нет записи/очистки | `setField`/`clearField` для `plannedBeginDateTime` и `plannedEndDateTime` |
| CRUD критериев | Есть только изменение `IsSatisfied` существующего критерия | Нет add/rename/remove; нет batch | `addCriterion`, `replaceCriterion`, `removeCriterion`, `setCriterionSatisfied` |
| Создать/удалить произвольную связь | `create` умеет только parents; движок умеет симметричные relation mutations | CLI не открывает их безопасно | `addRelation`/`removeRelation` только для canonical `contains` и `blocks` |
| Завершить/архивировать с проверкой | `set-status`, `complete` используют desktop status rules | Нет общего batch с контекстом/связями и `etag` | `apply/setStatus` после staged graph validation; justification/evidence обязательны для terminal status |
| Preview одобренного набора | Нет | Нельзя увидеть точный impact до записи | `apply --dry-run` строит staged graph и возвращает plan без записи/receipt |
| Повторить после сбоя | Есть transaction journal и `outcomeUnknown`; create без заданного ID может дублироваться | Нет request-level idempotency/reconciliation | `applicationId` + request hash + deterministic task IDs + postcondition reconciliation + receipt |
| Атомарно применить взаимозависимые предложения | Каждая CLI-команда отдельна; `create --parent` атомарна внутри себя | Несколько команд не атомарны | Один `apply` = одна directory lock + один recoverable mutation journal; вне одного request атомарность не обещается |
| Хранить решения по предложениям | CLI не хранит | Это не обязанность task engine | Оставить proposal lifecycle в Obsidian/agent workflow; CLI хранит только application receipt |

### 2.2 Модель и инварианты

- `TaskItem` содержит `Title`, `Description`, `Status`, `CompletionCriteria`, `UpdatedDateTime`, `PlannedBeginDateTime`, `PlannedEndDateTime`, `PlannedDuration`, четыре списка отношений, `Version` и `AgentExecution`.
- `TaskItem.Version` сейчас является версией схемы/миграции и обычно равен 1; он не увеличивается при каждой записи и не годится как record revision.
- `storageRevision` живёт в экземпляре `FileTaskStorage`; он полезен в ответе одной команды, но не является межпроцессным precondition token.
- `UpdatedDateTime` обновляется менеджером, но недостаточен как единственный guard для старых/null значений и неизвестных extension fields.
- Parent/child и blocker/blocked-by связи двусторонние. Незавершённая `ContainsTasks` блокирует содержащую задачу. Следующий шаг после текущей задачи поэтому нельзя автоматически делать её child.
- Валидация сейчас обнаруживает self/missing/duplicate/asymmetric relations и containment cycles. Dependency cycle в `BlocksTasks` отдельно не обнаруживается.
- `FileTaskStorage` использует directory lock, atomic replace и recoverable before/after journal. Это механизм, на котором должен строиться batch; прямые записи JSON запрещены.
- `AgentExecutionDescriptionRenderer` защищает служебный блок Description. Новый контракт не принимает raw full-description и не меняет `AgentExecution`.
- `claim` переводит Prepared в InProgress. Обзор и применение метаданных не должны делать implicit claim.
- Явный `--tasks` имеет приоритет над persisted desktop projection и должен сохранить приоритет.

## 3. Проблема

CLI умеет выполнять lease-bound содержание одной задачи и несколько отдельных мутаций, но не даёт безопасной границы для применения набора одобренных предложений: отсутствуют record-level preconditions, редактирование плановых полей/текста/критериев/связей, preview, deterministic create и batch recovery. Попытка склеить существующие команды в agent workflow создаёт окна гонок, частичный результат и дубли после сбоя.

## 4. Цели дизайна

- Полный read-only охват без `claim` и без лимита `candidates`.
- Узкий публичный API, связанный только с ночным обзором и дневным применением.
- Optimistic concurrency по opaque semantic `etag`, а не по schema `Version`.
- Один staged graph и одна recoverable mutation boundary на application request.
- Явная семантика preserve/replace/add/remove/clear без wildcard-разрушения коллекций.
- Идемпотентный create и безопасная ручная reconciliation после `outcomeUnknown`.
- Сохранение desktop status/availability semantics, symmetric relations, Description markers и active execution ownership.
- Обратная совместимость существующих команд/JSON без `--root` и без `--include`.
- Отделение approval/proposal state от факта применения.

## 5. Non-Goals (чего НЕ делаем)

- Не хранить в CLI полный lifecycle `proposed/accepted/rejected/deferred/superseded` и тексты всех предложений.
- Не интерпретировать естественный язык «прими P-042» внутри CLI; это делает дневной agent workflow.
- Не выполнять содержание созданной задачи и не считать её создание поручением на execution.
- Не добавлять relation type `alternative`; в граф попадает только выбранный вариант.
- Не планировать/запускать ночное расписание и не выполнять массовый обзор реальных задач.
- Не давать cross-task-space, Obsidian+Unlimotion или multi-process distributed transaction.
- Не поддерживать server storage в первой фазе; существующий CLI остаётся file-storage only.
- Не разрешать `apply` переводить задачу в `InProgress`; для этого остаётся `claim`.
- Не менять UI layout, тексты экранов или визуальную модель графа.
- Не обещать power-loss durability сверх существующей гарантии recoverable process crash.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент/файл | Ответственность |
| --- | --- |
| `Unlimotion.Cli/Program.cs` | parsing новых options, JSON request/response, stdin/file, exit codes, backward compatibility |
| Новые CLI contract DTO в `Unlimotion.Cli` | schema v1, validation, stable error/output names |
| `TaskGraphCommandService` | staged batch plan/apply, preconditions, business rules, read-back, outcome classification |
| `TaskAvailabilityService` | validation final staged graph, включая dependency cycles |
| `FileTaskStorage` | существующая lock/journal boundary; минимальная поддержка application receipt без обхода storage commands |
| `AgentExecutionDescriptionRenderer` | split/render user text с неизменённым protected block |
| Agent workflow / Obsidian | proposal ID/revision, decision state, approved variant, rejection suppression, daily coverage и materials |

### 6.2 Минимальный CLI API

#### 6.2.1 Scope-aware read

```powershell
unlimotion-cli unlocked [--root <task-id>]... [--tasks <task-dir>] [--format text|json]
```

- Без `--root` output и порядок остаются прежними.
- С одним/несколькими roots берётся union каждого root и его transitive `ContainsTasks` descendants; затем фильтр `CanStart`; ID дедуплицируются ordinal.
- Missing root, self/cycle/invalid graph дают exit 1 и structured error; read не чинит граф.
- Multiple parents не дублируют задачу.
- Parent/goal context не встраивается рекурсивно в этот ответ: агент читает `task --include relations` и рекурсивно поднимается по `ParentTasks`, сохраняя visited IDs.

```powershell
unlimotion-cli task --id <task-id> --include details,relations,criteria,history,execution [--tasks <task-dir>] --format json
```

- Expanded snapshot получает поля `etag`, `observedAt` и `details.descriptionUserText`; прежнее `details.description` сохраняется для обратной совместимости.
- Legacy `task --id ...` без `--include` сохраняет прежний analysis JSON.
- `etag` имеет формат `sha256:<lowercase-hex>` и является SHA-256 canonical semantic snapshot полного `TaskItem`, включая extension data и protected execution state. JSON object keys канонически сортируются ordinal; массивы сохраняют доменный порядок; timestamps нормализуются в round-trip UTC; hash не зависит от whitespace/порядка полей persisted JSON.
- `etag` — единственный межзапусковый optimistic token. `TaskItem.Version`, `UpdatedDateTime` и `storageRevision` не заменяют его.

#### 6.2.2 Единственная новая write boundary

```powershell
unlimotion-cli apply --request <path|-> [--dry-run] [--tasks <task-dir>] [--format text|json]
```

- `--request -` читает UTF-8 JSON из stdin.
- Один request относится ровно к одному resolved task space.
- `--dry-run` выполняет parsing, precondition checks, staging, business rules и final validation под directory lock, но не пишет task files/receipt.
- Обычный `apply` повторяет все проверки внутри write lock, применяет staged diff через один recoverable journal, перечитывает результат и только затем сообщает success.
- Никаких implicit `claim`, `release` или execution writes.

### 6.3 JSON request v1

```json
{
  "schemaVersion": 1,
  "applicationId": "A-2026-09-17-001",
  "proposalRefs": [
    { "id": "P-042", "revision": 3 }
  ],
  "author": "day-agent",
  "reason": "Одобрено пользователем 2026-09-17",
  "preconditions": [
    {
      "taskId": "task-1",
      "etag": "sha256:0123...",
      "status": "Prepared"
    }
  ],
  "operations": [
    {
      "operationId": "P-042-r3-duration",
      "kind": "setField",
      "taskId": "task-1",
      "field": "plannedDuration",
      "value": "PT45M"
    }
  ]
}
```

Общие правила:

- `schemaVersion` только `1`.
- `applicationId`: 1..128 ASCII `[A-Za-z0-9._:-]`, устойчив для одного одобренного batch.
- `proposalRefs`: не пусты; каждая пара `(id, revision)` уникальна. CLI не проверяет факт approval, а сохраняет ссылки в receipt/output.
- `operationId`: уникален в request; 1..128 ASCII `[A-Za-z0-9._:-]`.
- `author` и `reason` обязательны, trim, соответственно 1..128 и 1..4096 UTF-16 code units.
- Не более 256 operations, 512 preconditions и 4 MiB UTF-8 request.
- Каждый существующий task, который будет прямо или через inverse relation изменён, обязан иметь ровно одну precondition с `etag`. `status` опционален, кроме `setStatus`, где он обязателен.
- Новая задача обязана иметь caller-generated уникальный `taskId`. CLI не генерирует другой ID при retry.
- Preconditions сравниваются с initial authoritative graph до staging. Все операции затем применяются к staged clone в порядке массива.
- Несколько операций над одним task допустимы, но повторная запись одного scalar, одного criterion property, одного canonical relation edge или статуса в одном request отвергается как `conflictingOperations`.

### 6.4 Operation kinds и семантика частичных изменений

#### Скалярные поля

```jsonl
{ "operationId": "...", "kind": "setField", "taskId": "...", "field": "title", "value": "Новый заголовок" }
{ "operationId": "...", "kind": "setField", "taskId": "...", "field": "descriptionUserText", "value": "Полный новый пользовательский текст" }
{ "operationId": "...", "kind": "setField", "taskId": "...", "field": "plannedDuration", "value": "PT1H30M" }
{ "operationId": "...", "kind": "clearField", "taskId": "...", "field": "plannedEndDateTime" }
```

- Preserve: поле не упоминается.
- Replace: `setField` заменяет целиком ровно одно allowlisted поле.
- Clear: `clearField`; разрешён для `descriptionUserText`, `plannedDuration`, `plannedBeginDateTime`, `plannedEndDateTime`. `title` очищать нельзя.
- Add/append текста намеренно отсутствует: agent читает user text, строит полный desired text и делает guarded replace. Это делает retry идемпотентным и не дублирует блок после сбоя.
- `descriptionUserText` меняет только пользовательский сегмент, который caller получает отдельным полем expanded snapshot. Служебный AgentExecution block сохраняется семантически и не может быть передан во входе. Любой reserved marker во входе или invalid existing marker => `descriptionMarkerConflict` без записи.
- `plannedDuration`: ISO 8601 duration, только `PT...`/`P...`, значение `> PT0S`, максимум `P3650D`; output в нормализованной ISO 8601 форме. Ожидание и machine time в это поле не записываются — это оставшийся активный труд исполнителя.
- Dates: RFC 3339 с обязательным offset или `Z`; output UTC `O`. Naive local time запрещён. `plannedEndDateTime < plannedBeginDateTime` в final staged state запрещено.

#### Criteria

```jsonl
{ "operationId": "...", "kind": "addCriterion", "taskId": "...", "criterionId": "C-01", "text": "...", "isSatisfied": false }
{ "operationId": "...", "kind": "replaceCriterion", "taskId": "...", "criterionId": "C-01", "text": "..." }
{ "operationId": "...", "kind": "setCriterionSatisfied", "taskId": "...", "criterionId": "C-01", "isSatisfied": true }
{ "operationId": "...", "kind": "removeCriterion", "taskId": "...", "criterionId": "C-01" }
```

- Add требует caller-generated stable criterion ID и отсутствия ID.
- Replace меняет только Text и сохраняет `IsSatisfied`/extension data.
- Remove удаляет ровно указанный ID; wildcard clear не поддерживается. Очистка списка — явный набор removes, видимый в preview.
- Completed/Archived criteria immutable; для terminal task любая criteria operation — `businessRuleDenied`.
- Existing `set-criterion`/`satisfy-criterion` остаются совместимыми.

#### Canonical relations

```jsonl
{ "operationId": "...", "kind": "addRelation", "relation": "contains", "fromTaskId": "parent", "toTaskId": "child" }
{ "operationId": "...", "kind": "addRelation", "relation": "blocks", "fromTaskId": "current", "toTaskId": "next" }
{ "operationId": "...", "kind": "removeRelation", "relation": "blocks", "fromTaskId": "a", "toTaskId": "b" }
```

- Только два canonical direction: `contains` = parent→child; `blocks` = blocker→blocked. Inverse arrays нельзя адресовать напрямую.
- Add/remove всегда одновременно меняют обе стороны.
- Existing identical add и absent remove являются satisfied no-op, а не дублем/ошибкой.
- Replace/clear relation collection намеренно не поддерживаются: замена выражается точным набором add/remove, чтобы preview показывал каждое удаляемое ребро.
- Final staged graph запрещает self relation, missing inverse/reference, duplicate edge, containment cycle и dependency cycle по `blocks`.
- Не создаётся `alternative` edge. До application выбран один вариант; остальные rejected/deferred остаются во внешнем proposal store.

#### Create

```json
{
  "operationId": "P-043-r2-create-b",
  "kind": "createTask",
  "taskId": "f89d8f57-1b45-4c28-b785-79ac56b38ea8",
  "title": "Проверить вариант Б",
  "descriptionUserText": "Подготовленный контекст...",
  "plannedDuration": "PT25M",
  "plannedBeginDateTime": null,
  "plannedEndDateTime": null,
  "parentIds": ["goal-1", "goal-2"],
  "criteria": [
    { "criterionId": "C-01", "text": "Результат проверен", "isSatisfied": false }
  ]
}
```

- Status всегда `Prepared`; `InProgress` создаётся только последующим explicit claim.
- `taskId` уникален и валиден для FileTaskStorage. Если task уже существует и все заданные create-поля, criteria и canonical relations совпадают, операция считается already satisfied; generated timestamps и другие не задаваемые request поля при этом не сравниваются. Иначе возвращается `idempotencyConflict`.
- Все existing parents перечислены в request preconditions, потому что их `ContainsTasks` изменится.
- Для prerequisite/current-part/next-step остальные relations добавляются отдельными явными operations.

#### Status

```json
{
  "operationId": "P-044-r1-archive",
  "kind": "setStatus",
  "taskId": "task-old",
  "status": "Archived",
  "justification": "Подтверждено владельцем процесса: результат более не нужен",
  "evidenceLinks": ["obsidian://.../P-044"]
}
```

- `status`: `NotReady`, `Prepared`, `Completed`, `Archived`. `InProgress` запрещён; используется `claim`.
- Precondition `status` обязательна и должна совпасть с initial state.
- `Completed`/`Archived` требуют непустые justification и хотя бы одну syntactically valid absolute URI evidence link. CLI не открывает ссылки и не утверждает истинность доказательства.
- Transition проверяется существующим desktop policy на final staged graph. Старость и отсутствие обновлений не являются доказательством.
- Active `AgentExecution` нельзя обойти: terminal/status transition, запрещённый текущим lease contract, возвращает `businessRuleDenied`; apply не release/complete execution.

### 6.5 JSON output

Успех/preview:

```json
{
  "success": true,
  "mode": "preview",
  "applicationId": "A-2026-09-17-001",
  "requestHash": "sha256:...",
  "didMutate": false,
  "changedTaskIds": ["task-1"],
  "createdTaskIds": [],
  "operationResults": [
    {
      "operationId": "P-042-r3-duration",
      "taskId": "task-1",
      "outcome": "wouldApply"
    }
  ],
  "validation": { "isValid": true, "issues": [] }
}
```

Apply success uses `mode: "applied"`, `didMutate: true`, includes `beforeEtags`, `afterEtags`, `authoritativeTasks` expanded snapshots and `receiptWritten: true`.

Повтор того же request:

```json
{
  "success": true,
  "mode": "alreadyApplied",
  "applicationId": "A-2026-09-17-001",
  "requestHash": "sha256:...",
  "didMutate": false,
  "changedTaskIds": ["task-1"],
  "createdTaskIds": [],
  "stateDriftedAfterApply": false,
  "authoritativeTasks": []
}
```

Ошибка:

```json
{
  "success": false,
  "applicationId": "A-2026-09-17-001",
  "requestHash": "sha256:...",
  "error": {
    "kind": "preconditionFailed",
    "message": "Task 'task-1' no longer matches the approved snapshot.",
    "operationId": "P-042-r3-duration",
    "taskId": "task-1",
    "expectedEtag": "sha256:...",
    "actualEtag": "sha256:..."
  },
  "authoritativeTasks": []
}
```

Stable error kinds:

| Kind | Exit | Значение |
| --- | ---: | --- |
| `invalidArguments` | 2 | JSON/schema/value/limit/unknown field error |
| `notFound` | 1 | task/root/criterion отсутствует |
| `preconditionFailed` | 1 | etag/status не совпал |
| `conflictingOperations` | 1 | две одобренные операции задают несовместимый final intent |
| `descriptionMarkerConflict` | 1 | существующий/входной protected marker небезопасен |
| `businessRuleDenied` | 1 | status/terminal/lease/date/domain rule denied |
| `validationFailed` | 1 | initial или staged graph invalid, включая cycle |
| `idempotencyConflict` | 1 | тот же application/task/criterion ID с другим payload |
| `reconciliationRequired` | 1 | после сбоя authoritative state не равен ни complete before, ни complete desired state |
| `outcomeUnknown` | 1 | запись могла состояться, но recovery/read-back не подтвердили outcome |
| `operationFailed` | 1 | storage/permission failure до возможной записи |

### 6.6 Application receipt и граница proposal storage

- CLI хранит только receipt в `<resolved-task-root>/.unlimotion.applies/v1/<sha256-of-applicationId>.json`; path строится без пользовательских path fragments и после `GetFullPath` обязан оставаться внутри этой директории. Task enumeration вложенную директорию не сканирует.
- Receipt содержит `applicationId`, `requestHash`, proposal refs, applied timestamp, operation IDs, changed/created task IDs, before/after etags. Он не хранит rationale/materials/decision discussion.
- Если receipt с тем же ID и hash найден, CLI не повторяет запись. Если hash другой — `idempotencyConflict`.
- Receipt пишется после подтверждённого task transaction. Сбой между task commit и receipt даёт `outcomeUnknown`; повтор после authoritative read-back сравнивает complete desired postconditions и восстанавливает receipt без повторной мутации.
- Если receipt есть, но task позже изменён вручную, CLI возвращает `alreadyApplied` и `stateDriftedAfterApply: true`; старое решение не переигрывается поверх новых изменений.
- Proposal decision/application state остаётся во внешнем workflow: `proposed/accepted/rejected/deferred/superseded` отдельно от `notApplied/applied/alreadyApplied/conflict/outcomeUnknown`.

### 6.7 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Полный ночной обзор | Агент запускает `unlocked` для task space/roots | Все startable IDs без claim и дублей; бюджетный остаток учитывается workflow | CLI JSON + unchanged statuses/execution | AC-1, AC-2 |
| Preview одобренного batch | Дневной агент строит request из точных accepted revisions | `wouldApply`, impacted IDs, validation; task files unchanged | before/after hashes, dry-run test | AC-3 |
| Применение оценки/контекста | Пользователь одобрил proposal revision | Только указанные поля меняются, protected text сохранён | read-back snapshot + desktop headless view | AC-4, AC-5 |
| Декомпозиция выбранного варианта | Пользователь выбрал Б | Создана только task Б, parents symmetric; варианты А/В не создаются | graph read-back | AC-6 |
| Следующий шаг | Одобрен later step | New task shares goal parents and is blocked by current; не child current | relation output + availability assertion | AC-7 |
| Завершение/архив | Есть factual evidence | Transition только если policy разрешает; justification/links в receipt | status/history/read-back | AC-8 |
| Retry после crash | Первый apply вернул `outcomeUnknown` | После read-back повтор даёт applied/alreadyApplied/conflict, без duplicate task/text/edges | injected fault tests | AC-9 |
| Утренний конфликт | Task изменена после proposal | `preconditionFailed`, ни одна операция batch не применена | authoritative etag + unchanged graph | AC-10 |

### 6.8 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Valid graph, etags match | dry-run | staged valid plan, no files changed | invalid final graph => validationFailed | receipt absent |
| Valid graph, etags match | apply | all after-images committed + read-back | write fault => rollback/roll-forward then outcome classification | one task space |
| Same application receipt/hash | apply retry | alreadyApplied, no mutation | later drift flagged | never replay old intent |
| No receipt, desired postconditions all match | retry after crash | alreadyApplied + receipt recovery | mixed state => reconciliationRequired | no blind mutation |
| Initial preconditions all match after rollback | explicit retry after read-back | apply normally | concurrent write => preconditionFailed | no auto-retry |
| Active execution | metadata/relation operation | allowed only if AgentExecution itself/status ownership unchanged | terminal/status conflict denied | marker preserved |
| Active execution | setStatus Completed/Archived | denied by lease-bound rules | user must complete/release through existing commands | apply cannot steal lease |
| Terminal task | criteria mutation | denied | no mutation | preserves current immutability |

### 6.9 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Proposal storage location/lifecycle | agent | outside CLI; CLI only receipt | 0.95 | duplicated source of truth if CLI stores more | Нет |
| Public write surface | agent | one `apply` manifest, not many field commands | 0.90 | larger implementation but safer batch semantics | Нет |
| Concurrency token | agent | canonical semantic `etag`; not `Version`/storageRevision | 0.98 | canonicalization bugs | Нет |
| Batch atomicity | agent | one request/one task space/one journal | 0.90 | multi-space approvals need separate results | Нет |
| Alternative modeling | user intent + agent | create only selected alternative; no graph edge | 0.98 | external proposal store required | Нет |
| Dependency cycles | agent | reject cycles in `blocks` as invalid final graph | 0.90 | existing dirty cycles need repair before writes | Нет |
| Relation bulk replace/clear | agent | exclude; explicit add/remove only | 0.92 | more verbose manifests | Нет |
| Description append | agent | exclude; guarded full user-text replace | 0.95 | caller must construct desired text | Нет |
| Server mode | agent | non-goal for v1 | 0.95 | later separate design needed | Нет |

### 6.10 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Task directory | explicit `--tasks` then desktop persisted projection | unchanged | exact precedence retained | resolver tests |
| Task data | top-level task files through FileTaskStorage | staged writes only via storage/service | no task schema migration | round-trip/unknown JSON tests |
| Etag | absent | computed, not persisted in task | additive expanded JSON | canonicalization tests |
| Receipt | absent | nested `.unlimotion.applies/v1` | ignored by task enumeration; removable for rollback | load/receipt tests |
| Transaction | per command journal | batch reuses one recoverable scope | existing journals remain readable | fault matrix |
| Description | user text + protected rendered block | user segment patch only | marker v1 retained | marker tests |
| UI reflection | file watcher/live refresh | changed fields/relations appear after CLI apply | no layout change | Headless live refresh test |

### 6.11 Внешний handoff предложения и сквозные примеры

CLI не читает Obsidian-карточку, но дневной workflow обязан строить request только из proposal record со следующими полями: stable `proposalId`, integer `revision`, target task IDs, observed etags, точное `before → proposed`, rationale/evidence links, dependencies on other proposal revisions, выбранный option (если есть), user decision и отдельный application result. Изменение exact change создаёт новую revision и не наследует approval.

Рекомендуемое внешнее размещение остаётся за workflow: постоянная карточка по task ID, материалы по task ID и daily run summary. Context gaps формулируются как вопрос + источник/человек + место сохранения ответа; read-only обзор не использует lease-bound `execution question`.

Во всех примерах request сначала проходит:

```powershell
unlimotion-cli apply --request .\approved-application.json --dry-run --tasks <task-dir> --format json
unlimotion-cli apply --request .\approved-application.json --tasks <task-dir> --format json
```

#### Пример 1. Предложить и применить оценку

- Ночью P-042/r3 фиксирует before `plannedDuration = null`, proposed `PT45M`, active work assumptions и observed etag.
- После approval дневной request содержит:

```json
{
  "operationId": "P-042-r3-duration",
  "kind": "setField",
  "taskId": "task-1",
  "field": "plannedDuration",
  "value": "PT45M"
}
```

- Success: read-back показывает `PT45M`; ожидание ответа и machine time остаются в proposal rationale, не в `PlannedDuration`.

#### Пример 2. Добавить подготовленный контекст

- Агент читает `details.descriptionUserText`, добавляет подготовленный блок локально и сохраняет полный desired user text в P-043/r1.
- Apply использует `setField(descriptionUserText)` с точным etag, не append.
- Success: user text равен approved desired text, а structured execution marker и `AgentExecution` byte/semantic state не изменились.

#### Пример 3. Принять один вариант декомпозиции

- P-044/r2 содержит варианты А/Б/В; пользователь выбирает Б.
- Request содержит только `createTask` для stable ID варианта Б с `parentIds: ["current-task"]`, потому что это реальная часть текущей работы.
- Tasks вариантов А/В не создаются. New child остаётся Prepared и блокирует completion parent согласно существующей семантике; это ожидаемо именно для decomposition.

#### Пример 4. Добавить последующий шаг без ложной подзадачи

- Текущая task имеет goal parents `goal-1`, `goal-2`. P-045/r1 описывает работу после неё.
- Request создаёт `next-task` с `parentIds: ["goal-1", "goal-2"]` и добавляет:

```json
{
  "operationId": "P-045-r1-order",
  "kind": "addRelation",
  "relation": "blocks",
  "fromTaskId": "current-task",
  "toTaskId": "next-task"
}
```

- Request не добавляет `contains(current-task, next-task)`. Пока current не завершена, next заблокирована; parent completion current не зависит от next.

#### Пример 5. Завершить или архивировать с проверкой

- Для completion P-046/r1 ссылается на factual result и уже satisfied criteria; request делает `setStatus(Completed)` с expected initial status/etag. Final staged graph повторно проверяет `CanComplete`.
- Для archive P-047/r1 содержит подтверждение утраты актуальности и evidence URI; request делает `setStatus(Archived)`. Возраст/тишина сами по себе не используются.
- Active lease, incomplete criteria или graph blocker дают `businessRuleDenied`; ни description/relations из того же batch не записываются.

#### Пример 6. Повторить применение после сбоя без дублей

1. Apply `A-001` с create + relations получает `outcomeUnknown` после возможного task commit и до receipt.
2. Дневной agent перечитывает все attempted task IDs и не меняет applicationId/request bytes.
3. Повтор того же request под lock сначала ищет receipt, затем сравнивает complete desired postconditions.
4. Если transaction rolled forward — `alreadyApplied`, receipt восстанавливается, task/edge не дублируются. Если rolled back и initial etags ещё совпадают — request применяется один раз. Если состояние смешано/изменено — `reconciliationRequired`/`preconditionFailed`, без записи.

## 7. Бизнес-правила / Алгоритмы

### 7.1 Ночной coverage workflow (вне CLI, но определяет контракт)

1. Получить `unlocked` без `candidates` limit для всего task space или union roots.
2. Зафиксировать ordered unique IDs как coverage set; при окончании бюджета сохранить точный unreviewed suffix и не заявлять full coverage.
3. Для каждого ID перечитать expanded task, recursively собрать parent goals с visited set и сохранить observed `etag`.
4. До дорогой подготовки проверить factual completion/obsolescence evidence; после эксперимента проверить повторно.
5. Отдельно оценить context gap, permitted preparation, completion/archive evidence, active-time estimate, decomposition и next step.
6. Не менять canonical task. Создать/обновить proposal revision во внешнем store. Rejected revision не повторять без new evidence.

### 7.2 Дневное применение

1. Разобрать только явно approved proposal IDs/revisions и выбранные варианты.
2. Перечитать current tasks и сравнить etags. Изменившееся предложение требует новой revision/approval; approval старой revision не переносится.
3. Сконсолидировать operations; конфликтующие approvals не отправлять в CLI.
4. Выполнить `apply --dry-run`; показать/проверить exact impact.
5. Выполнить `apply` тем же request bytes/applicationId.
6. Перечитать authoritative tasks; записать created IDs и application state во внешнем store.
7. На `outcomeUnknown` не делать automatic retry: сначала read-back, затем явно повторить тот же request. CLI reconcile по receipt/postconditions.

### 7.3 Staging и commit

1. Parse/schema/limits и вычислить canonical `requestHash`.
2. Под directory lock прочитать authoritative graph, восстановив pending journal, и проверить initial graph write safety.
3. Если receipt найден: тот же hash возвращает `alreadyApplied` с проверкой later drift; другой hash возвращает `idempotencyConflict`. Etags старого request в этой ветке не переигрываются.
4. Если receipt отсутствует, проверить complete desired postconditions. Все satisfied означают recovery `alreadyApplied` и восстановление receipt; смешанный before/desired state означает `reconciliationRequired`.
5. Если desired state ещё не применён, проверить все etag/status preconditions. Ни одной мутации до полного успеха этого шага нет.
6. Применить operations к deep-cloned staged graph; собрать exact changed IDs.
7. Проверить operation conflicts, marker rules, dates, status policy и final graph включая both cycle types.
8. Для dry-run вернуть plan без receipt.
9. Для apply записать все changed tasks в одной recoverable scope, commit journal, перечитать graph и проверить desired postconditions/etags.
10. Записать receipt; вернуть authoritative snapshots. Сбой записи receipt после подтверждённого task commit классифицируется `outcomeUnknown`, а не success.

## 8. Точки интеграции и триггеры

- `CliOptions.Parse`/command dispatch: `apply`, `--request`, `--dry-run`, repeatable `--root`.
- `RunUnlocked`: optional descendant scope.
- `TaskSnapshotOutput`: additive `etag`, `observedAt` only in expanded output.
- `TaskGraphCommandService`: `PreviewApplicationAsync`/`TryApplyApplicationAsync` sharing one planner; concrete names may vary, behavior may not.
- `TaskAvailabilityService.Validate`: dependency cycle issue kind.
- `FileTaskStorage`: application receipt repository and existing recoverable scope; no direct task JSON patching.
- Desktop watcher/headless: verify changed title/description/duration/dates/criteria/relations refresh without restart.

## 9. Изменения модели данных / состояния

- `TaskItem` schema не получает proposal/application fields.
- New persisted auxiliary receipt schema v1 под `.unlimotion.applies/v1`.
- New CLI DTO/schema v1 is public contract.
- `TaskGraphReferenceIssueKind` получает dependency-cycle kind, если реализация использует общий validator.
- `etag` calculated only, not stored.
- Receipt cleanup policy в v1: не удалять автоматически; bounded size small. Отдельная retention feature — non-goal.

## 10. Миграция / Rollout / Rollback

- Existing task files не мигрируются.
- Existing commands/output сохраняются; `etag` только additive в already-expanded snapshot; `unlocked` без roots не меняет JSON shape/order.
- First apply создаёт auxiliary directory lazily.
- Rollback code: удалить новый command/DTO/service paths; receipts можно оставить ignored или удалить после проверки path. Task rollback — восстановить repository/worktree or user backup, не через автоматический reverse apply.
- Если новая dependency-cycle validation обнаружит существующий cycle, writes fail closed; read remains available. Нужна отдельная явная repair operation/spec, не auto-fix.
- Server mode остаётся fail-closed как сейчас.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- AC-1: `unlocked` без roots байт-семантически сохраняет текущий JSON shape/order; roots дают полный deduplicated descendant union и не claim/mutate tasks.
- AC-2: expanded `task` выдаёт стабильный semantic etag; изменение любого domain/extension/execution поля меняет etag, whitespace/property-order persisted JSON — нет.
- AC-3: dry-run строит тот же staged diff/validation, что apply, и не меняет task/receipt files.
- AC-4: set/clear fields соблюдают ISO/RFC formats, guarded replace и field preservation.
- AC-5: descriptionUserText никогда не повреждает/дублирует protected AgentExecution block; reserved marker rejected.
- AC-6: selected decomposition creates exactly one deterministic task and symmetric multi-parent edges; retry has no duplicate.
- AC-7: next-step example uses current→next `blocks`, shares goal parents and does not add current→next `contains`; availability follows desktop rules.
- AC-8: criteria CRUD and terminal status enforce existing policy, justification/evidence contract and active lease boundary.
- AC-9: injected faults before/after journal commit and before receipt produce rollback/roll-forward plus safe reconciliation, never partial graph/duplicate text/task/edge.
- AC-10: any mismatched etag/status aborts whole batch before writes and returns authoritative evidence.
- AC-11: containment and blocks cycles/self/asymmetry/missing references are rejected on final staged graph.
- AC-12: one request is atomic within one file task space; docs explicitly deny atomicity across requests/spaces/Obsidian.
- AC-13: desktop/headless live session observes applied fields and relations without restart; no UI layout/interaction regression.
- AC-14: README documents proposal/application boundary, examples, errors, recovery and `TaskItem.Version` caveat.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 | CLI integration parser/scope tests | compare legacy fixture | test output | — |
| AC-2 | etag canonicalization/unit + CLI integration | inspect sample | JSON fixture | — |
| AC-3 | command service preview/apply parity | file hash diff | test output | — |
| AC-4 | table-driven field/date/duration tests | inspect normalized JSON | test output | — |
| AC-5 | marker conflict/preservation tests | read-back Description | test output | — |
| AC-6 | integration create/multi-parent/retry | graph snapshot | test output | — |
| AC-7 | availability + relation integration | task JSON | test output | — |
| AC-8 | criteria/status/lease negative matrix | receipt/read-back | test output | — |
| AC-9 | FileTaskStorage fault injection at all boundaries | journal/receipt directory clean | fault trace | — |
| AC-10 | concurrent change/precondition tests | authoritative error JSON | fixture | — |
| AC-11 | staged cycle/invalid graph tests | validation issues | test output | — |
| AC-12 | multi-op atomicity test | README wording check | test + diff | cross-space transaction intentionally absent |
| AC-13 | extend `CliLiveRefreshHeadlessTests` | screenshot/log optional | headless test output | no layout change, deterministic assertions are primary |
| AC-14 | README contract test/manual review | rendered Markdown | diff | — |

Planned commands after approval (exact treenode names finalized with test discovery):

```powershell
dotnet build src\Unlimotion.sln -c Release -p:UseSharedCompilation=false
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter "/*/*/UnlimotionCliIntegrationTests/*|/*/*/TaskGraphCommandServiceTests/*|/*/*/FileTaskStorageRecoverableMutationTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet run --project tests\Unlimotion.UiTests.Headless\Unlimotion.UiTests.Headless.csproj -c Release --no-build -- --treenode-filter "/*/*/CliLiveRefreshHeadlessTests/*" --maximum-parallel-tests 1 --output Detailed
git diff --check
```

UI planning/evidence:

- Visual planning artifact: state storyboard `CLI preview JSON → atomic storage change → existing desktop task card/relations refresh`; UI geometry/copy do not change, so wireframe/render is not applicable.
- Video baseline/after: no user interaction or layout flow is added; current relevant suite is Avalonia Headless and has no established safe window-video artifact for this scenario. Fallback is deterministic Headless assertion plus failure screenshot/log if harness provides it. During EXEC, if the repository runner exposes supported video capture, save after-run video outside committed sources and report its path.
- Test failure, build failure, graph validation regression or inability to demonstrate crash recovery blocks completion.

Stop rules:

- После первого детерминированного failure исправить root cause; не повторять тест вслепую более одного раза.
- Если existing unrelated tests fail, изолировать targeted tests и доказать baseline; не менять unrelated code.
- Если реализация receipt потребует ослабления journal path safety или сканирования auxiliary JSON как task files, остановить EXEC и обновить SPEC вместо workaround.

## 12. Риски и edge cases

- Etag canonicalization может пропустить extension-data drift. Мера: hash canonical full TaskItem including extension data, dedicated mutation tests.
- Receipt после task commit — отдельное окно сбоя. Мера: postcondition reconciliation; receipt никогда не является единственным доказательством состояния.
- Dependency cycles уже могут существовать. Мера: read works, writes fail closed, no auto-repair.
- Large batch увеличивает lock time. Мера: limits, all planning on in-memory clone, lock only for authoritative check+stage+commit; benchmark not required unless tests show regression.
- Manual edit after apply. Мера: receipt prevents replay; drift flag and current snapshot.
- Multiple approved proposals target same field. Мера: conflictingOperations before write; human chooses new consolidated revision.
- Removal of criteria/relations can change completion/availability. Мера: exact preview, final analyzer, explicit operation list.
- Active lease and metadata change race. Мера: etag includes AgentExecution; concurrent execution change invalidates approved snapshot.
- Unknown outcome without read-back. Мера: stop and report attempted IDs; no automatic retry.
- Absolute evidence URI may contain sensitive data. Мера: CLI does not open it; external workflow should prefer local vault links without secrets.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Почему не отдельные простые команды?» | Они кажутся легче | Один manifest нужен для atomic preview, interdependent proposals и crash-safe batch; existing simple commands retained | mitigated |
| «CLI снова становится хранилищем предложений» | Есть receipt | Receipt хранит только application fingerprint/result; decisions/materials остаются снаружи | mitigated |
| «Почему Version нельзя использовать?» | Поле уже есть | AS-IS подтверждает schema version без per-write increment; введён semantic etag | mitigated |
| «Не потеряется мой Description?» | Есть protected marker | Только full replace user segment, marker preservation tests, no raw description write | mitigated |
| «Все альтернативы станут обязательными?» | Графовая декомпозиция опасна | Создаётся только выбранный вариант; relation alternative отсутствует | mitigated |
| «Следующий шаг снова заблокирует текущую задачу?» | Child semantics | Сквозной пример и AC требуют shared goal parents + current blocks next, без child edge | mitigated |
| «Batch точно атомарен?» | Несколько файлов | Только один request/task space на существующем recoverable journal; иные границы явно non-atomic | mitigated |
| «После сбоя появятся дубли?» | Create/text/edges | caller IDs, guarded replace, set-like edges, receipt+postcondition reconciliation | mitigated |

### Rework Prevention Checklist
- [x] Spec называет команды и видимые JSON outputs.
- [x] Каждый user-visible scenario имеет evidence и AC.
- [x] Decision Ledger фиксирует assumptions и owners.
- [x] Likely objections закрыты.
- [x] Role-based review запланирован и заполнен ниже.
- [x] AC являются verifier outcomes.
- [x] EXEC имеет путь доказать scenarios до final.

## 13. План выполнения после approval

1. Characterization tests для legacy CLI output/parser и etag canonicalization.
2. Read additions: roots filtering и expanded snapshot etag.
3. Request DTO/parser/schema validation и shared in-memory planner без writes.
4. Field/criteria/relation/create/status staged operations; final cycle/status validation.
5. One-scope atomic apply через `TaskGraphCommandService`/FileTaskStorage journal.
6. Receipt repository и retry reconciliation; fault injection.
7. CLI JSON/text render, stable error mapping, README.
8. Headless desktop live-refresh coverage.
9. Targeted tests → affected full suites → Release build → post-EXEC review.

Этапы 3–6 не разделяются на independently shippable public commands: до recovery/idempotency новый `apply` не документируется как готовый.

## 14. Открытые вопросы

Блокирующих вопросов перед approval нет. Конкретные defaults выбраны в Decision Ledger. Любое расширение до server storage, distributed batch, retention receipts или natural-language proposal store требует отдельной SPEC.

## 15. Соответствие профилю

- Профиль: `product-system-design`.
- Выполнено: цели/non-goals, subsystem boundaries, public API/schema/errors, compatibility, security/limits, persistence/recovery, alternatives.
- `.NET desktop`: reused domain/storage manager instead of direct JSON; desktop status/availability semantics and UI refresh covered.
- `ui-automation-testing` + local override: planned Headless coverage for UI-facing state, explicit video fallback, no layout change.
- QUEST expanded: public API, persisted auxiliary state, multi-module/recovery risk require expanded template.

## 16. Таблица изменений файлов (план EXEC)

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Cli/Program.cs` | commands/options/rendering | public surface |
| `src/Unlimotion.Cli/*Application*.cs` (новые) | DTO/parser/hash/output | isolate contract |
| `README.md` | docs/examples/recovery | user contract |
| `src/Unlimotion.TaskTreeManager/TaskApplicationCommandService.cs` (новый) | preview/apply batch | shared domain boundary |
| `src/Unlimotion.TaskTreeManager/TaskAvailabilityService.cs` | blocks-cycle validation | prevent dead graph |
| `src/Unlimotion.TaskTreeManager/TaskGraphDiagnostics.cs` / operation result | etag/batch evidence as needed | structured result |
| `src/Unlimotion.Cli/TaskApplicationReceiptStore.cs` (новый) | auxiliary receipt/recovery integration | idempotency without task-file scanning |
| `src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs` | end-to-end contract | CLI coverage |
| `src/Unlimotion.Test/TaskGraphCommandServiceTests.cs` | planner/rules/retry | domain coverage |
| `src/Unlimotion.Test/FileTaskStorageRecoverableMutationTests.cs` | crash windows | recovery proof |
| `tests/Unlimotion.UiTests.Headless/Tests/CliLiveRefreshHeadlessTests.cs` | desktop reflection | local MUST UI test |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Обзор | whole-space unlocked only | whole-space или root-union, claimless |
| Актуальность | timestamps/schema version | opaque semantic etag |
| Изменения | отдельные narrow commands | one approved atomic manifest |
| Description | execution renderer only | guarded user-text replace + marker preservation |
| Criteria | satisfied toggle | explicit CRUD |
| Relations | create parents only | explicit canonical add/remove |
| Create retry | generated ID, possible duplicate | caller taskId + receipt/postcondition reconcile |
| Batch failure | multiple independent outcomes | one journal/task space + read-back |
| Proposal state | absent | remains external by design |

## 18. Альтернативы и компромиссы

### A. Добавить отдельные команды `set-duration`, `set-description`, `add-relation`, ...
- Плюсы: простой parser, удобный ручной shell.
- Минусы: нет atomicity взаимозависимых approvals, множество race windows, сложнее retry/orchestration.
- Решение: не выбирать; существующие простые команды остаются, новые gaps идут через `apply`.

### B. Хранить предложения внутри TaskItem
- Плюсы: один store.
- Минусы: загрязняет domain, смешивает decision и task truth, требует UI/migration, rejected history разрастается.
- Решение: не выбирать; только receipts в auxiliary store.

### C. Использовать `UpdatedDateTime` или `Version`
- Плюсы: нет нового hash.
- Минусы: Version не record revision; timestamp может быть null/не покрывает exact state.
- Решение: semantic etag.

### D. Последовательно выполнять operations без атомарного batch
- Плюсы: меньше engine work.
- Минусы: partial graph и неопределённые approvals после crash.
- Решение: не выбирать; one request/one journal.

### E. Добавить relation `alternative`
- Плюсы: варианты видны в графе.
- Минусы: текущая availability model не знает XOR; все варианты могут стать обязательными/блокирующими.
- Решение: варианты живут в proposal store, selected option materializes.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Блок | Статус | Проверяемое основание |
|---:|---|---|---|
| 1 | A | PASS | Success means и observable JSON/task outcomes заданы. |
| 2 | A | PASS | AS-IS проверен по Program/README/domain/service/storage/tests на указанном commit. |
| 3 | A | PASS | Корневая проблема — отсутствие одной безопасной approved-application boundary. |
| 4 | A | PASS | Девять design goals заданы отдельно от реализации. |
| 5 | A | PASS | Non-goals исключают schedule, real tasks, proposal DB, server и distributed transaction. |
| 6 | B | PASS | Ответственности CLI/domain/storage/workflow разделены таблицей. |
| 7 | B | PASS | Integration points перечислены по модулям и trigger. |
| 8 | B | PASS | Coverage, daytime flow и staged commit алгоритмы определены. |
| 9 | B | PASS | outcomeUnknown, journal recovery, receipt gap и reconciliation заданы. |
| 10 | B | PASS | Performance ограничен 256 operations/4 MiB; staging in-memory, lock bounded. |
| 11 | C | PASS | Task schema неизменна; auxiliary receipt и calculated etag описаны. |
| 12 | C | PASS | Legacy shapes/commands и file-storage compatibility сохранены. |
| 13 | C | PASS | Code/data rollback и fail-closed existing-cycle path определены. |
| 14 | D | PASS | AC-1..AC-14 измеримы. |
| 15 | D | PASS | Каждый AC связан с automated/manual evidence, включая negative/fault/UI. |
| 16 | D | PASS | Planned commands и failure/loop stop rules указаны. |
| 17 | E | PASS | Девять outcome stages и зависимости реализации определены. |
| 18 | E | PASS | Decision Ledger заполнен; блокирующих open questions нет. |
| 19 | E | PASS | Large/expanded выбран из-за public/persisted/multi-module/recovery risk. |
| 20 | F | PASS | Product-system, desktop/storage и UI-state requirements отражены. |

Итог: ГОТОВО после полного post-SPEC review ниже.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один связный closed-loop outcome, explicit non-goals. |
| 2. Понимание текущего состояния | 5 | Проверены commands, model, version semantics, graph/recovery. |
| 3. Конкретность целевого дизайна | 5 | Exact syntax, JSON, operations, errors и algorithms. |
| 4. Безопасность | 5 | Etag, atomic boundary, receipts, reconciliation, rollback. |
| 5. Тестируемость | 5 | AC→tests включая faults/concurrency/UI. |
| 6. Готовность к автономной реализации | 5 | No blocking decisions; phased file-scoped plan. |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению после exact approval.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Все цели ежедневно, proposal≠application, корректны ли decomposition/next step? | PASS | Нет |
| UX / designer | limited | Понятны ли preview/error/result и desktop-visible state? | PASS | Layout не меняется; state storyboard/fallback добавлены |
| Tester / validation | applicable | Есть ли AC для conflicts, cycles, crash, no duplicates и UI refresh? | PASS | Нет |
| Developer / architect | applicable | Coherent ли API, staged graph, etag, receipt и boundaries? | PASS | Нет |
| Delivery / operations / security | applicable | Ограничены ли paths/input/side effects; recovery/rollback ясны? | PASS | Server/cross-space excluded, limits/URI behavior заданы |

### Post-SPEC Review
- Статус / stop decision: `PASS`; можно запрашивать exact approval.
- Scope/Evidence pass: перечитаны эта SPEC, central routing/QUEST/linter/rubric/review/profile owners, local override, текущие `Program.cs`, CLI README, `TaskItem`, `TaskGraphCommandService`, `TaskAvailabilityService`, `FileTaskStorage`, `TaskGraphDiagnostics` и relevant CLI/service/storage/UI test inventory. `git status --short`, `git diff --check` и heading/contract scan выполнены; вне этой SPEC изменений нет.
- Contract pass: сверены all-goals coverage, no claim, proposal revision boundary, correct relation semantics, approved-only application, read-back и recovery.
- Adversarial risk pass: проверены stale etag, same ID/different payload, crash before receipt, partial/mixed state, active lease, multiple parents, two cycle types, protected markers, later manual drift и multi-space non-atomicity.
- Role-Based pass: таблица выше.
- Independent reviewer: не запускался; текущая delegation policy запрещает создавать subagents без явной просьбы пользователя. Выполнен отдельный self-adversarial fallback; residual risk — независимое ревью может найти более дешёвую реализацию receipt integration.
- Findings:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | implementation cost | Atomic receipt внутри существующего journal потребовал бы ослабить top-level task-path invariant | Оставить receipt post-commit и доказать postcondition reconciliation | fixed in design |
| LOW | UX evidence | UI layout не меняется, video evidence непропорционально | Headless state assertion + objective video fallback | fixed in design |

- Fix and re-review: receipt contract отделён от task transaction; no-duplicate correctness основана на deterministic operations/postconditions, а не на receipt alone. Receipt/reconciliation order исправлен до etag gate, expanded output дополнен `descriptionUserText`, create replay сравнивает только request-defined postconditions, linter развёрнут на все 20 критериев. Повторены `git diff --check` и contract scan.
- Manual-review challenge / остаточные риски: canonicalization и receipt crash window требуют особенно тщательных fault tests; это отражено в AC-2/AC-9.
- Needs human: только exact QUEST approval; продуктовых блокирующих вопросов нет.

### Post-EXEC Review
- Статус / stop decision: `PASS с остаточным test-debt`; безопасное изменение существующих задач, создание следующей задачи и её атомарная связь с текущей задачей подтверждены. Commit/push/PR/release/deploy не выполнялись.
- Scope/Evidence pass: добавлены `unlocked --root`, additive expanded-task `etag`/`descriptionUserText`, `apply --request ... [--dry-run]`, in-memory staged graph, один recoverable write scope, final containment/dependency-cycle validation, receipt в `.unlimotion.applies/v1` и postcondition reconciliation. Proposal lifecycle и task schema не изменялись.
- Contract pass: request ограничен 4 MiB и 256 operations; применяются etag/status preconditions, conflict guard, protected description segment, criteria/relation/create/status operations, terminal justification/evidence и authoritative post-read. Output сообщает `preview`/`applied`/`alreadyApplied`, validation, snapshots и `receiptWritten`.
- Validation pass (2026-09-18):
  - `dotnet build src\\Unlimotion.Test\\Unlimotion.Test.csproj -c Release -p:UseSharedCompilation=false` — PASS (existing warnings only).
  - `dotnet test src\\Unlimotion.Test\\Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter "/*/*/UnlimotionCliIntegrationTests/*" --maximum-parallel-tests 1 --output Normal` — PASS, 33/33. Дополнительно покрыты изменение title/user description/estimate/criterion/relation между существующими задачами, `createTask` с parent/description/estimate/criterion и `createTask` + `addRelation` к новой task в одном ordered manifest.
  - `dotnet test src\\Unlimotion.Test\\Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter "/*/*/TaskGraphCommandServiceTests/*" --maximum-parallel-tests 1 --output Normal` — PASS, 39/39.
  - `dotnet test tests\\Unlimotion.UiTests.Headless\\Unlimotion.UiTests.Headless.csproj -c Release -- --treenode-filter "/*/*/CliLiveRefreshHeadlessTests/Apply_RefreshesOpenDesktopProjection" --maximum-parallel-tests 1 --output Detailed` — PASS, 1/1.
  - `dotnet test src\\Unlimotion.Test\\Unlimotion.Test.csproj -c Release --no-build -- --maximum-parallel-tests 1 --output Normal` — PASS, 1062/1062, 19m 34s.
  - `git diff --check` — PASS.
- Full solution note: `dotnet build src\\Unlimotion.sln -c Release -p:UseSharedCompilation=false` reached Android AOT for multiple architectures, then its outer `dotnet` process remained without child processes or an exit code. It was stopped after the targeted builds/tests had passed; therefore this is **not** recorded as a full-solution PASS. Existing Android NU1608/CA1416 warnings were emitted before that point.
- Recovery evidence: CLI integration performs preview without files, applies duration, removes the written receipt to model the commit→receipt gap, then repeats the identical request and gets `alreadyApplied` without a second mutation. This proves the deterministic postcondition path; it is not a process-kill fault injector at every journal boundary.
- UI evidence: new Headless test launches the desktop, obtains an etag through the real CLI, writes an approved `apply` request through the real CLI process, and asserts the current task card title refreshes without restart. No layout change exists; the Headless harness cannot record a window video, so the deterministic state assertion and HTML test report are the video fallback.
- Findings:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | receipt fault matrix | No new injected process-kill test at every existing FileTaskStorage journal boundary | Keep existing journal suite in release gate; add dedicated fault-injection matrix before claiming exhaustive crash-boundary coverage | open test-debt |
| LOW | API output | `observedAt` was removed because it broke byte-stable legacy snapshot comparison | Etag remains the stable observation token; do not re-add a volatile field without versioning the response | fixed |
| HIGH | atomic next-step flow | `createTask next`, followed by `addRelation` to `next` in the same manifest, was rejected with `preconditionFailed`: the new ID has no initial etag | `RequireStaged` now requires an ETag only for tasks present in the original persisted graph; the red-to-green regression test covers the ordered flow | fixed |

- Re-review: inspected changed CLI/domain/UI test paths, output schemas, receipt location, `RequireStaged` ETag boundary and `git diff --check` after the fix. The regression test first failed with `preconditionFailed`, then passed after the fix; targeted CLI/graph/recovery/UI and full TUnit suites are green. No unrelated source changes detected. Independent reviewer was not spawned because current delegation policy prohibits it without an explicit user request.

## Approval

Ожидается точная фраза: `Спеку подтверждаю`.

Подтверждение разрешает только реализацию/тестирование в границах этой SPEC. Оно не разрешает commit, push, PR, merge, release, deploy, расписание или изменение реальных пользовательских задач.

## 20. Журнал действий агента

| Фаза | Тип намерения/сценария | Уверенность | Каких данных не хватает | Следующее действие | Нужна ли передача решения человеку | Фактическое обращение/решение | Короткое объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction routing и scope gate | 0.99 | Нет | Read-only repo audit | Нет | Пользователь уже задал отдельный worktree и SPEC-only | Expanded из-за public/persisted/recovery risk | central stack, local override |
| SPEC | AS-IS CLI/model/storage audit | 0.96 | Нет | Спроектировать minimal contract | Нет | Не требовалось | Проверены реальные команды и отсутствие record revision | Program/README/domain/service/storage/tests |
| SPEC | API/atomicity/idempotency design | 0.90 | Независимое ревью недоступно по delegation policy | Создать SPEC и self-review | Нет | Не требовалось | One apply boundary минимизирует race/partial state | эта SPEC |
| SPEC | Pre-approval quality gate | 0.94 | Только exact approval | Перечитать, lint, adversarial review, затем запросить approval | Да | Ожидается `Спеку подтверждаю` | Блокирующих product decisions нет | эта SPEC |
| SPEC | Fix and re-review | 0.96 | Только exact approval | Передать SPEC пользователю | Да | Фактический запрос approval — в итоговом сообщении | Исправлены replay ordering, extracted user text, JSON examples, 20-point lint и end-to-end scenarios; structural checks PASS | эта SPEC |
| EXEC | Approved CLI application boundary | 0.94 | Exhaustive process-kill matrix не добавлена | Реализовать staged apply, receipt и tests | Нет | Реализованы новые CLI/domain/receipt paths без изменения TaskItem schema | Один manifest использует существующий recoverable journal; proposals остались outside task store | Program.cs, TaskApplication*.cs |
| EXEC | Regression and recovery proof | 0.92 | Process-kill fault injection at every journal boundary | Run targeted TUnit and Headless suites | Нет | CLI 30/30, graph service 39/39, Headless 1/1 PASS | Receipt deletion after successful transaction exercises no-receipt reconciliation; full fault matrix остаётся test-debt | test reports, README |
| TEST | New CLI task-change workflow | 0.98 | Atomic reference to a newly-created task is unsupported | Verify successful edit/create flows and probe next-step relation | Да | Existing-task mutation and standalone creation PASS; `createTask` then `addRelation` to that new ID fails deterministically with `preconditionFailed` | New functions help safely change existing task data and create a fully described next task, but the common one-manifest linked-next-step flow is not ready | `UnlimotionCliIntegrationTests`, `TaskApplicationCommandService` |
| EXEC | Fix atomic next-step relation | 0.99 | No ETag exists for a task created earlier in the same ordered manifest | Add red regression test, distinguish original and staged tasks, then rerun validation | Нет | Red test reproduced `preconditionFailed`; `RequireStaged` now skips snapshot ETag only for IDs absent from the original graph; green test verifies create + link | Existing persisted tasks still require their approved ETag, while a new staged task can be used by later operations in the same atomic request | `TaskApplicationCommandService`, `UnlimotionCliIntegrationTests`, 1062/1062 TUnit |
