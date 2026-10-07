# Совместимость CLI preview и повторного apply с областями после удаления IsGoal

Expanded SPEC, версия v2. Фаза SPEC: код, fixtures на диске, builds/tests/native не выполнялись.

## 0. Метаданные

- Дата: 2026-10-07, Europe/Moscow. Тип: ограниченное изменение публичного CLI-контракта; expanded из-за версии projection и общего model/storage prerequisite. Масштаб medium.
- Owner собственного scope: автор CLI, этот чат. Один owner общего удаления IsGoal и AreaIds null/clone: автор workspace, «Дневник Unlimotion». Это разные prerequisite scope/approval.
- Собственный checkout: `C:/Users/Kibnet/.codex/scratch-worktrees/cli-workspace-spec-20261007/Unlimotion`, detached main `dce4e1961b3f29e33a2e439730cd09cac885a872`. Старые worktrees не восстановлены; eff3 не редактируется.
- Workspace HEAD при чтении: `5db0b05a5e4615c46de18be83cac7e4e80210959`, с незавершённым recovery/history diff. При первом чтении removal EXEC не был начат; в ходе review owner SPEC записал approval/переход EXEC, появились незавершённые model retirement edits. Они не accepted commit/evidence и не передают разрешение этому CLI scope.
- Продуктовое решение: «Давай выпилим IsGoal и всё что с ним связано, это кажется была лишняя вещь». Координация передала его для актуализации/review/show SPEC, не как approval CLI EXEC.
- P0: [2026-10-07-remove-is-goal.md](C:/Users/Kibnet/.codex/worktrees/eff3/Unlimotion/specs/2026-10-07-remove-is-goal.md). Initial SHA-256 `57D82C7913895DB741AF93D06B70754048721791C5F2FABB0FA8DB2E8DBF4C17`; повторное чтение после owner Approval/EXEC journal update: `9606B653803283A3F456D28A394215B1AF9254AD853DFFEAA55FB379A99B5F3A`. Оба evidence состояния записаны, источник продолжает меняться. Contract исключает AreaIds null/clone cleanup и CLI preview/schema. Owner record не является переносом approval на v2/P1 или доказательством finished P0.
- Историческая [CLI v1](2026-10-07-cli-workspace-classification-compatibility.md), SHA-256 `0272E803F13416A2108466E66CC909A29FED7FAE777DD400F81DA89BC7ABB136`, сохраняется без изменений. Её IsGoal projection/equivalence отменены; v1 больше не материал для approval/EXEC. v2 заменяет весь её активный CLI scope.
- Предложение `workspace-areaids-contract-proposal-20261006.md`, исторический SHA `DDF3CBB087104B89B1DB697967016AD7BCBD407843526D6DA68D1FE34D647EED`: из него остаётся только P1 AreaIds null/clone, не IsGoal-составляющая.
- Профиль .NET desktop consumer с собственным CLI diff; testing-dotnet/run-tunit-tests для будущего EXEC. Surface Codex desktop, текущая host-модель без переключения. Model eval неприменим к .NET protocol change. Parent sandbox danger-full-access / approval never; техническая read-only изоляция review не заявляется без actual child evidence.
- Будущая ветка `fix/cli-workspace-areaids` после approval и выбора accepted integration base. Release не назначен.
- Сейчас write только этого нового рабочего SPEC. Нет общего/native slot, Tasks, публикации, установки, push/PR/merge или сообщений в другие чаты.

## 1. Overview / Цель

После назначения областей задача сохраняет их при обычных разрешённых CLI изменениях; полный preview показывает активный AreaIds-контракт, а retry различает исторический receipt и текущие postconditions. Снятый IsGoal не возвращается через projection, unknown fields или equivalence.

Outcome contract:

- Исходное поручение: обновить прежний CLI план после отказа от IsGoal, сохранив области и receipt/version/raw observation границы.
- Success: typed AreaIds в полной projection; old top-level IsGoal не влияет на semantics/match; чтение не переписывает old task/journal JSON; authorized обычный task save удаляет только retired key согласно P0.
- Итог EXEC: own CLI diff/tests, обычный executable I/O на isolated fixtures и raw bytes/mtime evidence; общий implementation предоставляет workspace.
- Stop: exact approval v2 касается только своего CLI scope. Accepted P0 и P1 необходимы до integration readiness; обязательные AC/full gate нельзя заменить targeted pass. Старые CLI317/v1/removal/recovery approval не взаимозаменяемы.

## 2. Текущее состояние (AS-IS)

- Main Project перечисляет details явно, AreaIds отсутствует; unknown fields представлены opaque unknownFieldsHash. Envelope/projection/application/availability/guard сейчас 1.
- В committed workspace base ещё есть IsGoal и AreaIds; current dirty P0 начинает удалять goal. Исторический cd29 добавил `!task.IsGoal && task.AreaIds.Count == 0` в CreatedTaskMatches — postcondition конечного create с последующими operations, не delete/IsPristine guard. Эти source facts не описывают accepted future integration state.
- Общий Clone не изолирует AreaIds; explicit JSON null заменяет list initializer. Removal SPEC исключает изменение этой null-policy как попутный cleanup.
- RunApply: ValidateWitness → receipt lookup; matching shortcut без fresh graph/source/effect guard. Missing receipt: reconciliation → planning. Partial → reconciliationRequired; none → planning, occupied create ID → idempotencyConflict.
- Inspect strict observation сообщает receiptState/postconditionsMatch/assessment. Postcommit non-all/invalid graph → outcomeUnknown; receipt пишет CLI только при result.Success.
- Strict observation не replay journal; `.unlimotion.lock` transient. ReadGraph может вернуть cached liveGraph до InvalidateLiveGraph.
- Task FileStorage использует Newtonsoft.Json, Notes journal — System.Text.Json Web defaults. FileStorage mutation journal содержит raw образы в `BeforeBase64`/`AfterBase64` с `BeforeExists`/`AfterExists`; recovery/rollback восстанавливает байты. Это разные форматы.
- Schema loader отдаёт embedded `apply-preview-v1.schema.json`; envelope/resource 1 отделены от contracts.projection.
- Evidence сейчас только source/SPEC. Goal-containing producer projection2 в own checkout отсутствует: прежняя v1 была планом, не реализованным protocol release.

## 3. Проблема

Реализация старой v1 вернула бы ненужный goal в CLI и его влияние через hash/postconditions. Нужен актуальный AreaIds-only контракт поверх принятой retired-key совместимости общего владельца.

## 4. Цели дизайна

Один shared owner deserialize/clone/serialize/equivalence; явная typed projection/version; независимый replay oracle; сохранённые receipt shortcut и strict raw guard; воспроизводимые temporary fixtures и bounded cache/race claims.

## 5. Non-Goals

Удаление goal из модели/UI/Notes/server, собственный retired-key converter, общие AreaIds null/clone fixes, area assignment CLI commands, receipt/request schema change, delete/rollback redesign, night snapshot protocol redesign, recovery/history/emoji, backfill, installed tool/server rollout, публикация/Git delivery. Историческая v1 и другие SPEC/evidence не переписываются.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Owner | Результат | Gate |
| --- | --- | --- |
| Workspace P0 | Удаление active IsGoal, retired-key deserialize/serialize/clone, equivalence без goal, Notes/server/UI compatibility | Отдельная accepted removal SPEC, implementation SHA и evidence |
| Workspace P1 | AreaIds null-safe CreatedTaskMatches и независимый список в Clone | Отдельный accepted AreaIds prerequisite; removal approval его не разрешает |
| CLI P2–P4 | AreaIds-only preview/version/validation/schema/docs и свои consumer/receipt/raw fixtures | Exact approval v2 и base с accepted P0/P1 |
| Интеграция | Один общий implementation + own CLI diff | Fixed SHAs/drift check, затем validation |

CLI не меняет TaskItem/Snapshot/CommandService/TreeManager/shared serializers/Notes ради prerequisites. Недостаточный общий контракт остаётся dependency owner, не второй реализацией CLI. Approval v2 не разрешает чужой EXEC.

### 6.2 Детальный дизайн

**P0 — retired-key контракт removal SPEC.**

1. У модели нет active IsGoal. Top-level исторический task key IsGoal/isGoal/ISGOAL/смешанный регистр игнорируется OrdinalIgnoreCase на правильных границах Newtonsoft task deserialize и serialize/clone. Программный ExtensionData фильтруется общим owner в независимой копии; исходный объект не мутируется.
2. Retired key не возвращается как extension и не влияет на canonical task snapshot/ETag, unknownFieldsHash, preview changes/effect или CreatedTaskMatches. CLI не добавляет второй фильтр: producer получает P0-sanitized snapshots общего service/clone. Иначе P0 не принят для integration.
3. Вложенный same-name key пользовательской metadata не retired task property. Он, прочие unknown data, AreaIds, IDs/status/relations/history сохраняются. Retired semantic witness rejection не сканирует пользовательскую metadata рекурсивно.
4. Read/inspect/full dry-run, clone/serialize в памяти, matching-receipt retry и missing-receipt no-op recovery не удаляют key из task файла. Raw JSON/file mtime сохраняются. Нет scan/cleanup при запуске.
5. Persistent removal происходит только когда разрешённый обычный task save записывает новое представление. Title apply использует существующий transaction write/commit; preview показывает semantic title/date changes, не IsGoal-clear operation. New tasks и вновь serialized task after-image без retired key. Prepare-write не мутирует source object.
6. Failure до commit/rollback сохраняет/восстанавливает authoritative old bytes по storage контракту. После committed outcomeUnknown не обещать восстановление retired marker: read-back отражает authoritative state, success receipt не создаётся. Old raw rollback image с key — compatibility, не feature.
7. Старые Notes pending/task-created/completed records с goal читаются P0 owner через System.Text.Json, goal не участвует в intent/equivalence. Hashes/locators/ownership/parents/AreaIds остаются обязательными. Новые Notes records без goal; CLI их serializer не меняет.
8. FileStorage raw mutation journal `BeforeBase64`/`AfterBase64`, exists flags и guards не преобразуются ради key. Inspect/full dry-run pending journal → recoveryRequired, без replay/cleanup. Прежний write-path recovery/rollback может восстановить old raw bytes с key; следующий authorized обычный save удалит его из нового представления. Новый recovery trigger не добавляется.

**P1 — отдельно требуемый AreaIds контракт.**

- AreaIds:null/[] — нет областей для default-created match, nonempty — mismatch. Retired goal не условие независимо от true/false/absence.
- Check не присваивает TaskItem новый список и не пишет source/receipt.
- Clone получает отдельный список с теми же order/duplicates/строками; null source → clone [], source остаётся null. Изменения сторон независимы; остальные правила сохраняются.
- До integration нужны accepted P1 SHA и null/empty/nonempty/alias tests. Это не часть removal cleanup или разрешённый CLI fix.

**P2 — активная AreaIds-only projection.**

- `details.areaIds` — array строк; null → [] только в памяти. Не sort/dedup/trim/resolve unknown IDs.
- Created-root `/` содержит after.details.areaIds=[] по default; ни root details, ни scalar changes не содержат active isGoal. Unchanged areas не создают no-op entries, title edit сохраняет точный список.
- Controlled plan с отличием areas использует `/details/areaIds` и exact before/after arrays. Это fixture, не новая write-команда.
- Новые values в changes входят в effectHash. Unchanged areas защищены raw manifest, без обещания отдельного включения в effectHash. Retired key нет в sanitized unknownFieldsHash; nested metadata остаётся частью opaque hash.
- Independent replay oracle перечисляет активные поля/areaIds, не вызывает private Project как единственный oracle; IsGoal отсутствует.
- contracts.projection 1→2; previewVersion/application/availability/guard и receipt/request schema остаются 1. Embedded resource/URL `apply-preview-v1.schema.json` сохраняет envelope 1, const projection меняется на 2; help/docs их различают.
- Отменённая v1 только предлагала goal-containing projection2, executable/release не предъявлялся. До EXEC проверить actual integration producer; если такой protocol2 уже материализован, переоценить версию в SPEC до зависимого кода, не принять его молча.
- Consumer принимает AreaIds-only projection2. Реальный projection1 → previewInvalid до matching receipt. Prototype отменённой v1, даже projection2, с root details.isGoal или semantic path `/details/isGoal` (retired name case-insensitive) → previewInvalid. Rejection относится к known task details/path, не произвольной nested metadata.
- Created-root after.details требует array areaIds; missing/null/non-string item reject. `/details/areaIds` требует arrays строк с обеих сторон. Root before=null при creation; если details object предъявлен в root before/after, retired property там forbidden. Прочие duplicate/IDs/hash/placeholder checks сохраняются.
- Type/retired/missing-field negative fixtures пересчитывают test-only effectHash: отказ именно contract validation, не старый checksum. Valid-structure omission/tamper дополнительно ловится fresh effect comparison до write. Matching receipt остаётся no-op, не fresh approval source.
- Legacy task JSON с IsGoal допустим по P0; это не разрешает goal-containing semantic witness.

**P3 — receipt/retry.**

| Состояние C1 | Inspect | Retry | Запись |
| --- | --- | --- | --- |
| Matching receipt, AreaIds пусты, любой legacy goal | receiptMatched/all | alreadyApplied/didMutate=false/receiptWritten=false | Ничего |
| Matching receipt, nonempty areas | receiptMatched/partial | historical alreadyApplied | Ничего |
| Missing receipt, AreaIds null/[], любой retired goal | desiredStatePresent/all | alreadyApplied/didMutate=false/receiptWritten=true | Только restored receipt; task key не удаляется |
| Missing receipt, nonempty areas, create+rename | needsReconciliation/partial | reconciliationRequired | Ничего |
| Missing receipt, nonempty areas, create-only | needsReconciliation/none | idempotencyConflict | Existing task retained |
| Conflicting receipt | needsReconciliation | idempotencyConflict | Ничего |
| Corrupt/incomplete receipt | operationFailed | operationFailed | Ничего |

ValidateWitness раньше lookup/shortcut. Valid AreaIds-only witness + matching receipt не обещает fresh raw/effect guard после drift. Old/malformed/goal witness reject до shortcut. Missing receipt + partial composition прекращается на reconciliationRequired раньше guard, не previewStale.

**P4 — observation/raw drift/race.**

- Inspect/full dry-run pending committed/uncommitted FileStorage journal → recoveryRequired, no replay; authoritative tasks/journal/receipts/persistent sidecars unchanged.
- `.unlimotion.lock` разрешён transient, excluded bytes/file-mtime inventory; fresh fixture после completion без lock. Directory mtime/LastAccessTime не invariants.
- Raw manifest различает AreaIds:null↔[] и legacy IsGoal:true↔false/casing при semantic equality. Fresh guarded write без receipt → previewStale, no writes.
- Tested observed guard сохраняет обнаруженные external bytes. Controlled postcommit areas update + InvalidateLiveGraph → outcomeUnknown, authoritative areas retained, no success receipt. Cache без invalidation может скрыть bypass update; все non-cooperative writers атомарно не защищены.

Performance: projection/copy AreaIds O(n); own CLI не меняет observation passes/locks/limits. Benchmark/speedup claim отсутствует. GUI/video own scope неприменим; artifact-facing planning — JSON/text I/O ниже. P0 UI gates остаются у owner.

### 6.3 User-Observable Scenarios

| Scenario | Действие | Видимый результат | Evidence EXEC | AC |
| --- | --- | --- | --- | --- |
| S1 Области | Full preview create/controlled area change | areaIds typed, projection2, нет active goal | JSON/text/replay | AC1–2 |
| S2 Старые данные | Read/preview/inspect task с retired key | Та же task semantics, raw key сохранён | Process + inventory/mtime | AC3/11 |
| S3 Повтор | Retry с receipt/без него после areas | Historical no-op либо reconciliation | F0–F7 process I/O | AC4–7 |
| S4 Согласование | Old/malformed/goal witness или raw drift | previewInvalid/previewStale, no writes | F6/F9/F11 | AC8–9 |
| S5 Save | Title edit old task с areas/metadata | Areas/metadata сохранены, top-level retired key убран только на save | Preview/persisted JSON/read-back | AC12 |
| S6 Recovery/race | Pending journal/read-back external update | recoveryRequired без replay либо bounded outcomeUnknown без receipt | Journal/R1 trace/raw files | AC10/13 |

### 6.4 State / Interaction Matrix

Empty root допускает preview/create. Legacy true/false/absence не меняют semantics; null areas требует P1. Read-only и receipt-only no-op не мигрируют файлы. Normal title save меняет planned values плюс existing dates/serialization P0; rollback может вернуть old raw key. Retired witness fields запрещены, raw legacy data разрешены. Status/ETag/graph/marker conflicts сохраняются.

### 6.5 Decision Ledger

| Решение | Owner | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Удаление goal | User / P0 workspace | No active CLI field/equivalence | 1.0 | P0 ещё future gate | Отдельное approval P0 своему owner |
| Null/clone areas | P1 workspace | null→empty check/copy без source mutation | 0.95 | Не removal cleanup | Отдельное approval P1 своему owner |
| Projection | CLI | AreaIds-only2, envelope/resource1, reject goal witness | 0.95 | Actual producer сверить на integration base | Нет развилки сейчас |
| Receipt | CLI | Preserve historical shortcut; current state inspect | 1.0 | Не proof нынешних areas | Нет |
| Legacy key boundary | P0 + CLI consumer | Read untouched; authorized normal save strips | 0.95 | CLI revert не вернёт marker | Нет, §10 последствия |
| Own execution | User | Новое exact approval v2 | 1.0 | Не чужой EXEC/live delivery | Approval этой SPEC |

P0/P1 — внешние execution gates, не разрешение общего кода CLI. Для готовности CLI-плана дополнительных продуктовых развилок нет.

### 6.6 Runtime / Config / Data Contract Matrix

| Контракт | Source of truth | Граница | Compatibility / verification |
| --- | --- | --- | --- |
| Task semantics | accepted P0/P1 SHAs | Только areas; retired ignored | Old read untouched, normal save strips top-level only; owner+consumer tests |
| Preview | TaskApplicationPreview/schema | projection2 AreaIds-only | projection1/goal prototype reject; types/hash/replay |
| Receipt | ReceiptStore/RunApply | Формат/shortcut unchanged | v1 receipts; matching/missing/conflict/corrupt |
| Notes journal | P0 serializer/intent | Old goal ignored; new records no goal | Остальные guards retained; owner legacy states evidence |
| FileStorage journal | Raw images/guards | No read transform/replay | Raw recovery/rollback images; pending observation tests |
| Binary/source | own integration SHA/hashes | OS process F fixtures | Installed tool не используется; pre/post identity/I/O |

## 7. Бизнес-правила / Алгоритмы

Default-created match по areas iff null или Count==0, с прочими unchanged postconditions. Retired goal не критерий. Receipt historical; none не разрешает overwrite occupied ID. Semantic retirement/null equality не отменяет raw guard. No writes в этой SPEC означает task/journal/receipt/persistent sidecar files, кроме transient lock и явно restored receipt.

## 8. Точки интеграции и триггеры

P0 hydration/clone/serialize → shared service plan → CLI Project/validation/hash/render. ValidateWitness → receipt → reconciliation/planning → allowed write/commit → read-back → conditional success receipt. Notes предоставляет P0 owner; FileStorage observation strict. R1 test-owned wrapper без production hook.

## 9. Изменения модели данных / состояния

Own CLI не добавляет/удаляет model fields: P0 удаляет IsGoal, P1 чинит areas contract. Preview добавляет areaIds и forbids retired details/path. Receipt/request/envelope1 сохраняются. Nested metadata сохраняется. Existing night snapshot regression обязательна на shared model base, artifact redesign не включён.

## 10. Миграция / Rollout / Rollback

Первый read ничего не переписывает. Нет backfill/scan/journal normalization/receipt migration. Получить новый full AreaIds-only projection2 witness, converter отсутствует. Old key остаётся до authorized regular task save, включая no-op/receipt-only retry. Raw recovery/rollback старого образа не cleanup.

Own CLI revert возвращает projection1 и отвергает v2 witness; проверить оба направления. Он не отменяет accepted P0/P1 и не восстанавливает marker в уже saved файлах. P0 rollback принадлежит workspace; возврат потерянных values только из Git/history/backup по отдельному поручению. Release/install/live migration не входят.

## 11. Тестирование и критерии приёмки

Все I/O **ожидаемые, не выполненные**. Source main не содержит prerequisites, общий/native slot не назначен.

### Fixture/output contract

Каждый case fresh temporary root вне реальных Tasks; request/witness/output вне source root. Setup явно до measured command. Recursive authoritative file inventory/SHA-256/file LastWriteTimeUtc включают tasks/receipts/journal/persistent sidecars. Lock excluded с cleanup assertion; directory mtime/LastAccessTime не сравнивать. stdout/stderr/exit отдельно. Receipt удаляется только в owned setup.

C1 — composed create+rename:

```json
{
  "schemaVersion":1,
  "applicationId":"areaids-spec",
  "proposalRefs":[{"id":"P-areaids","revision":1}],
  "author":"spec-agent",
  "reason":"Isolated area classification fixture",
  "preconditions":[],
  "operations":[
    {"operationId":"create","kind":"createTask","newTaskId":"created","title":"Before"},
    {"operationId":"rename","kind":"setField","taskId":"created","field":"title","value":"After"}
  ]
}
```

C2 = C1 без rename, applicationId=areaids-create-only, отдельный root; initial apply, затем setup AreaIds=["area-a"] и removal receipt. Retired key не классификация.

Planned обычные commands, не исполняемые сейчас:

```powershell
dotnet $cliDll apply --tasks $fixtureRoot --request $requestFile --dry-run --diff full --format json
dotnet $cliDll apply --tasks $fixtureRoot --request $requestFile --expect-preview $witnessFile --format json
dotnet $cliDll apply inspect --tasks $fixtureRoot --request $requestFile --format json
dotnet $cliDll apply --tasks $fixtureRoot --request $requestFile --format json
dotnet $cliDll task --tasks $fixtureRoot --id created --include details --format json
```

Witness — полный stdout successful JSON full preview, не inner preview. Dynamic hashes/clocks/ETags не выдумывать; actual top-level etag из task команды.

Fresh preview expected exit0/success=true/mode=preview/didMutate=false/receiptWritten=false, contracts=(application1,projection2,availability1). Created `/`: after.details.title="After", areaIds=[], без isGoal. Controlled sanitized plan []→["area-b","area-a","area-b"] выдаёт точный areaIds path, no goal.

После initial successful C1 apply setup меняет лишь указанные fields/receipt:

| Fixture | Setup | Inspect exit / fields | Retry exit / fields | File result |
| --- | --- | --- | --- | --- |
| F0 | Areas=[], no retired key, missing receipt | 0: missing/all/desiredStatePresent | 0: alreadyApplied/didMutate=false/receiptWritten=true | Only restored receipt |
| F1 | Areas=null, legacy IsGoal=true, missing receipt | Как F0 | Как F0, no null crash | Raw null/key/task bytes/mtime untouched |
| F2 | Areas=["area-a"], missing receipt | 0: missing/partial/needsReconciliation | 1: reconciliationRequired | No writes |
| F3 | F2 + legacy true/false/absence/casing variants | Как F2 | Как F2 | Legacy goal не меняет результат |
| F4 | Nonempty areas + matching receipt | 0: matching/partial/receiptMatched | 0: alreadyApplied/didMutate=false/receiptWritten=false | Task/receipt bytes/mtime untouched; legacy parameter variants |
| F5 | F2/F3 + valid AreaIds-only witness2 | missing/partial | 1: reconciliationRequired до fresh guard | No writes |
| F6 | F4 + projection1/malformed/goal-containing projection2 witness | Inspect как F4 | 1: previewInvalid до shortcut | No writes |
| F7 | C2 + nonempty areas, missing receipt | 0: missing/none/needsReconciliation | 1: idempotencyConflict | Existing task retained |
| F10 | C1/C2, Areas=[], legacy true/false/casing, matching/missing receipt | all; receiptMatched/desiredStatePresent | alreadyApplied; receiptWritten=false/true соответственно | Raw retired key retained; только missing receipt restored |

C3 для F8/F9/F11/F12 с actual ETag:

```json
{
  "schemaVersion":1,
  "applicationId":"areaids-title-edit",
  "proposalRefs":[{"id":"P-areaids","revision":1}],
  "author":"spec-agent",
  "reason":"Isolated area preservation and retired key fixture",
  "preconditions":[{"taskId":"created","etag":"ETAG_FROM_TASK_STDOUT"}],
  "operations":[{"operationId":"rename","kind":"setField","taskId":"created","field":"title","value":"Reviewed title"}]
}
```

ETAG_FROM_TASK_STDOUT — обозначение actual hash, не допустимый literal. До C3 persisted title=After; каждый case новый application/root без matching receipt.

- F8: Areas=["area-b","area-a","area-b"]. Full preview0/projection2, `/details/title` before After→after Reviewed title, нет no-op area change. Apply0/success=true/didMutate=true/receiptWritten=true, exact areas retained.
- F9: raw areas null. Fresh C3 witness, setup только null→[]. Guarded apply1/previewStale даже при equal normalized ETag, no writes.
- F11: Areas=[]/legacy top-level IsGoal=true. Fresh C3 witness, setup retired raw true→false (отдельно casing variation). Model/ETag равны по P0, raw manifest различен. Apply1/previewStale, no writes. Nested metadata change семантически значим и не игнорируется.
- F12: Areas nonempty/empty/null, retired casing variants, unknown `Custom:{"IsGoal":true,"label":"keep"}` и другой unknown top-level key. Read/inspect/preview0 сохраняют bytes/mtime, no retired field/unknownFieldsHash change только из-за него. Authorized guarded C3 save0: title Reviewed title, nonempty/empty arrays точно сохранены; input null после separately accepted P1 Clone сохраняется как []. Это не removal cleanup или дополнительная CLI нормализация. Nested/other unknown metadata сохранены, все top-level retired variants отсутствуют. Failure до commit/no-op не cleanup. Programmatic ExtensionData sanitation через shared Clone, original map intact; CLI второго фильтра не добавляет.

R1 deterministic postcommit выполняет **initial C1** на fresh empty root: нет created task, receipt или pre-existing application. Test-owned FileTaskStorage subclass reimplements ITaskGraphWriteScopeStorage, wraps настоящий IRecoverableTaskGraphWriteScope. CommitAsync: real C1 commit создаёт created с title After/default areas[] → raw edit именно created AreaIds=["external-area"] → public InvalidateLiveGraph на том же storage → read-back. Create postcondition теперь false, rename postcondition true: C1 partial, следовательно outcomeUnknown. Не использовать C3 title-only: areas тогда не входят в postcondition, мог бы получиться success+receipt. Refresh распаковывает inner scope, остальное/attempted writes делегируется. Test-only reflection вызывает actual private Program.RunApply с C1 request и parsed обычными unguarded apply args; expect-preview/full заменили бы storage, поэтому здесь не используются. Console/return capture serial. Expected return1/success=false/outcomeUnknown/receiptWritten=false, no receipt file/directory, persisted created title After/external areas retained. Direct service test supplemental, сам по себе receipt не доказывает. R1 actual RunApply+owned storage, не OS process; F fixtures — OS process. Private reflection локальный test maintenance risk, production hooks/API не добавлять.

Old journal cases: committed/uncommitted FileStorage raw image содержит retired true/false/casing и areas; inspect/full dry-run1/recoveryRequired, no replay, unchanged journal/task/receipt inventory. Old images сохраняются в shared rollback/recovery regression. Notes pending/task-created/completed compatibility — evidence P0 owner, не собственный CLI process test.

### Acceptance-to-Test Matrix

| AC | Критерий | Planned test/process | Evidence EXEC |
| --- | --- | --- | --- |
| AC1 | Typed areas root/path/order/duplicates; no active goal | PreviewTests created/control + schema/text | JSON/text/TRX/schema |
| AC2 | Full active replay, independent oracle | ReplayTests/negative omitted area | Replay/TRX |
| AC3 | Accepted P0/P1, retired semantics/null/copy no mutation | Owner tests/SHA + Clone→consumer | Owner evidence + consumer TRX |
| AC4 | Matching receipt не пишет task/receipt | F4/F10 legacy variants | I/O/hashes/mtime |
| AC5 | Missing defaults/null/retired goal restores only receipt | F0/F1/F10 composed/create-only | Same + receipt body |
| AC6 | Nonempty composed partial/reconciliation | F2/F3/F5 | Same/no-write |
| AC7 | Nonempty create-only none/idempotencyConflict | F7 | Same/no recreation |
| AC8 | Old/goal/malformed reject до shortcut; valid2 accepts | F6/rehashed type cases/both version directions | Error/no-write/TRX |
| AC9 | Area null/empty и retired-only raw drift stale | F9/F11/source-pin guards | previewStale/raw hashes |
| AC10 | Pending journal read не replay/normalize | committed/uncommitted inspect/full preview | recoveryRequired/bytes/mtime/lock cleanup |
| AC11 | Retired task key ignored/read untouched; nested data intact | F1/F10/F12/casing/programmatic Clone | Process/raw before-after |
| AC12 | Title save сохраняет areas/metadata, strips только top-level retired | F8/F12/shared failed-save regression | Preview/persisted JSON/read-back |
| AC13 | Detected postcommit area race outcomeUnknown/no success receipt | R1 actual RunApply + service | Capture/trace/inventory |
| AC14 | Receipt errors и docs/contracts AreaIds-only | conflict/corrupt/incomplete + README/help/schema | Error/exit/no-write/source/TRX |

### Будущие commands и validation stop

После approval, accepted P0/P1 и fixed own integration SHA, sequential builds; не запускать в SPEC:

```powershell
dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -m:1 -p:UseSharedCompilation=false
dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj -c Debug -m:1 -p:UseSharedCompilation=false
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --treenode-filter '/*/*/TaskApplicationPreviewTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --treenode-filter '/*/*/TaskApplicationPreviewReplayTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --treenode-filter '/*/*/UnlimotionCliIntegrationTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --treenode-filter '/*/*/CliClassificationCompatibilityTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --treenode-filter '/*/*/FileTaskStorageObservationTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx
```

R1 выделен в CliClassificationCompatibilityTests и имеет собственный exact class filter. UnlimotionCliNightIntegrationTests.cs объявляет partial UnlimotionCliIntegrationTests: отдельного класса Night нет. Whole-class filter включает новые AreaIds_* и прежние Apply*/night regressions. Проверить discovery/count и все variants; minimum1 не coverage. Result dirs fresh вне Tasks. Source/CLI DLL/test DLL pre/post hashes фиксируют compiled SHA; старый DLL не evidence. Snapshot/source-pin/receipt/rollback regression по touched integration diff.

Before completion полный Main+Headless integration gate: локально только allocated shared slot, либо CI после самостоятельного Git delivery разрешения. Targeted pass не full pass. Native own CLI неприменим; P0 UI owner выполняет свои gates. Показать fresh preview и F1/F3/F4/F6/F11/F12, source/binary identity, I/O/raw bytes/mtime; затем отдельная приёмка EXEC. Артефакты `artifacts/cli-workspace-areaids/<run-id>/` автоматически не коммитить.

## 12. Риски и edge cases

- P0 owner записал approval/EXEC, но accepted implementation/evidence ещё не получены; P1 отдельно не утверждён. Main/dirty eff3 нельзя объявлять accepted base без SHAs/evidence.
- DTO-only retirement без ExtensionData/clone вернёт key в hash/save. Consumer выявляет недостаточный P0, CLI не чинит его сам.
- Legacy task допустим, goal-containing witness запрещён. Nested same-name metadata не retired task property.
- Null semantics не разрешают read rewrite; ordinary save null→[] только вследствие separately accepted P1 Clone, не removal cleanup и не новый CLI normalize.
- Historical receipt не current proof; missing receipt не разрешает overwrite ID.
- Raw mutation images и Notes intent records — разные serializer/ownership boundaries; global converter не покрывает оба.
- Save убирает marker; CLI revert не восстановит его. OutcomeUnknown не rollback committed bytes и не detection всех bypass writers.

### Expected User Review Objections

| Возражение | Причина | Мера / disposition |
| --- | --- | --- |
| «Вы опять добавили цель» | Старый v1 требовал isGoal | v1 historical, active areas-only, goal witness reject; mitigated |
| «Почему цель осталась в файле после чтения?» | Не было разрешённой записи | Read/no-op untouched, только normal save strips; disclosed |
| «Пропали metadata/области» | Broad key cleanup/list alias | Top-level boundary/nested preservation/P1 copy/F12; mitigated |
| «Retry alreadyApplied, но области другие» | Historical shortcut | Inspect partial отдельно/no-op evidence; disclosed |
| «Удаление goal разрешило весь cleanup?» | Prerequisites смешаны | P0/P1 разные approval, один owner; mitigated |
| «Старое согласование годится?» | Envelope1/projection2 confusion | Full new witness, old/goal reject; mitigated |

Rework prevention: S1–S6, Decision Ledger, AC1–14, objections и output/stop contract заполнены. GUI own scope неприменим; process plan не runtime evidence.

## 13. План выполнения

1. Review/show v2, новое exact approval own CLI scope.
2. Получить accepted P0 SHA/evidence и отдельно P1 SHA/evidence workspace; сверить actual producer/version/base. Чужие files не менять.
3. Own AreaIds projection/validation/version/schema/docs и consumer fixtures; characterize receipt/default/legacy branches. Независимую подготовку этого diff можно вести, пока ожидаются accepted P0/P1; она не является integration-ready результатом. Перенос на общий base, сборки и runtime validation — только после accepted prerequisites.
4. Targeted build/tests/process show, full integration gate в allocated месте; проверить raw read/no-op/save boundary и ownership diff.
5. Full post-EXEC review/приёмка. Delivery/install/live отдельно.

## 14. Открытые вопросы

Дополнительных продуктовых развилок нет: goal убран, areas сохранены, version/compatibility выбраны. P0/P1 acceptance и slot — future execution gates. При изменении accepted shared retired-key/journal semantics либо обнаружении real goal-containing producer2 — update/review до dependent code. V1 не current approval material.

## 15. Соответствие профилю

Central AGENTS/routing, model/tool/collaboration/testing baseline, quest-governance/mode/prompt-spec, canonical expanded template, linter/rubric/review-loops; .NET desktop consumer/testing-dotnet и run-tunit-tests для будущего EXEC. Локальный UI-test MUST остаётся у P0 UI consumers. Own diff CLI/tests/docs без desktop layout, visual fallback — exact artifact JSON/text/process I/O, не native proof. Effective-model eval неприменим.

## 16. Таблица изменений файлов

| Файл | Будущее изменение | Owner |
| --- | --- | --- |
| src/Unlimotion.Cli/TaskApplicationPreview.cs | AreaIds projection2/types/retired witness rejection/hash/render | CLI |
| src/Unlimotion.Cli/apply-preview-v1.schema.json | Envelope/resource1, projection const2, areas/retired details/path constraints | CLI |
| src/Unlimotion.Cli/CliIntrospection.cs | Areas-only help; resource v1 lookup сохранён | CLI |
| src/Unlimotion.Cli/README.md | New full witness/historical receipt/legacy read/save boundary | CLI |
| src/Unlimotion.Test/TaskApplicationPreviewTests.cs | AreaIds/type/retired/hash/null consumers | CLI |
| src/Unlimotion.Test/TaskApplicationPreviewReplayTests.cs | Independent active oracle, no goal | CLI |
| src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs | F0–F12 consumer/receipt/save process fixtures | CLI |
| src/Unlimotion.Test/UnlimotionCliNightIntegrationTests.cs | Version/source/raw/journal observation | CLI |
| src/Unlimotion.Test/CliClassificationCompatibilityTests.cs | Если отдельный файл: R1 RunApply/owned wrapper/reflection | CLI tests only |
| Shared model/Snapshot/Service/TreeManager/serializers/Notes/UI/DTO/tests | P0 removal и P1 null/clone, не own diff | Workspace |
| Этот v2 SPEC | Единственный current authoring файл | CLI |
| Historical v1 SHA0272 | Без изменений, не active approval | История |

## 17. Было → стало

| Область | Historical v1 / current main | Active v2 |
| --- | --- | --- |
| Projection | v1 предлагала goal+areas/2; actual main1 | Только areas/2, retired details forbidden |
| Equivalence | cd29 goal false и areas.Count0 | P0 no goal, P1 null/empty areas |
| Old task JSON | Может содержать goal | Semantic ignore/read untouched/normal save strips |
| Receipt | Historical shortcut | Preserved, retired goal не мешает default retry |
| Journal | Legacy records/raw images | Разные owner boundaries, no read migration |
| Approval | CLI317/v1/removal разные | Новое v2 + separately accepted P0/P1 |

## 18. Альтернативы и компромиссы

IsGoal в projection «ради совместимости» возвращает feature — отклонено. Projection1 с areas не согласована old witness — отклонено. Projection3 только из-за отменённого плана v1 не требуется без реального producer2; actual base check обязателен. Broad ExtensionData cleanup/CLI converter повреждает metadata/дублирует owner — отклонено. Inspect/no-op cleanup нарушает no-write — отклонено. Revalidate current graph в matching receipt меняет контракт вне scope; inspect показывает drift отдельно.

## 19. Quality gate / Review

### SPEC Linter Result

Итог ГОТОВО по содержанию плана после полного авторского pass и отдельного adversarial fallback; это не runtime validation.

| № / блок | Оценка | Evidence |
| --- | --- | --- |
| 1 / A | PASS | §1 исходный outcome после нового product decision, S1–S6 |
| 2 / A | PASS | §0/2 pinned main/actual workspace, source/schema/journal/cache |
| 3 / A | PASS | §3 устранён obsolete goal projection/hash/equivalence |
| 4 / A | PASS | §4 one owner, typed witness/replay и raw observation |
| 5 / A | PASS | §5 excludes shared fixes/live delivery/history rewrite |
| 6 / B | PASS | §6.1 разные P0/P1 и собственный P2–P4 |
| 7 / B | PASS | §8 actual RunApply ordering и shared producer boundaries |
| 8 / B | PASS | P0/P1/P3 и §7 retirement/null/receipt/raw invariants |
| 9 / B | PASS | Error kinds/receipt restoration/R1/recovery outcomes |
| 10 / B | PASS | Bounded O(n) areas; no new observation passes/performance claims |
| 11 / C | PASS | §9 active AreaIds-only, shared retirement, nested metadata |
| 12 / C | PASS | Version2 vs envelope1; old task allowed/old semantic witness rejected |
| 13 / C | PASS | §10 revert witnesses/shared owner/data marker loss explicit |
| 14 / D | PASS | AC1–14 observable values/exit/hash/file mtime/inventory |
| 15 / D | PASS | C1/C2/C3/F0–F12/R1, typed rehashed negatives, journal states |
| 16 / D | PASS | Planned ordinary commands, exact filters/counts, full slot gate |
| 17 / E | PASS | §13 accepted prerequisites/integration/show/acceptance sequence |
| 18 / E | PASS | Ledger выбран, §14 без дополнительных product choices |
| 19 / E | PASS | Expanded medium public model/storage/protocol compatibility |
| 20 / F | PASS | §15 central/.NET testing/UI owner/own artifact fallback |

### SPEC Rubric Result

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель/границы | 5 | Новый areas-only outcome, v1 historical, one owner |
| AS-IS | 5 | Main/eff3/removal pins, real schema/cache/raw journal read |
| Дизайн | 5 | Retired vs active fields/version/receipt/null/data boundaries |
| Безопасность/миграция/rollback | 5 | No read rewrite, authorized save, old images, explicit marker loss |
| Проверяемость | 5 | AC matrix/typed negatives/raw drift/process/R1 |
| Автономность решений | 5 | Решения заданы; P0/P1 не скрыты и не присвоены CLI |

30/30 оценивает достаточность плана после approval, не accepted P0/P1 или выполненные tests.

### Role-Based Review Result

| Роль | Авторский verdict | Проверка |
| --- | --- | --- |
| Domain workflow | PASS по плану | Retired goal не mismatch; areas/receipt исторические границы |
| UX / artifact | PASS по плану | No active goal, полный JSON/text, old witness reject; GUI неприменим |
| Tester | PASS по плану | No-op/read/save разные evidence, casing/null/journal/R1 mapped |
| Architect | PASS по плану | P0/P1 one owner, different serializer boundaries, protocol tuple |
| Operations | PASS по плану | Historical v1 SHA intact, no live/slots claimed, bounded rollback |

### Full post-SPEC review

- Статус PASS для SPEC v2 после fixes/re-review; open phase findings и дополнительные user product choices отсутствуют. Это не EXEC approval или integration/runtime PASS.
- Scope reviewed: current v2, unchanged historical v1, working removal SPEC SHA57D82, central stack/template/linter/rubric/review-loop/.NET/testing profile, local UI requirement, §16 planned files и §14 dependencies.
- Scope/Evidence pass: main Project/ComputeEffectHash/ValidatePayload/CheckGuard, RunApply/Inspect/ReceiptStore и preview/request schema/resource loader; existing preview/replay/CLI/observation tests из v1 source pass; дополнительно FileStorage ApplyJournalImages/RecoverableMutationEntry, Clone/ExtensionData и cache invalidation source. Removal SPEC прочитана целиком; eff3 active source/dirty status только read. Это не execution evidence.
- Contract pass: active projection/equivalence без goal; narrow top-level P0 retirement/nested preserve; P1 отдельный gate; read/no-op/save/raw journals не смешаны; scenarios/AC/objections/Non-Goals и instruction phase соблюдены.
- Adversarial author pass: challenged obsolete v1 with proposed projection2, same-name nested metadata, raw retired-only drift при semantic match, retry legacy true без receipt, null save policy, pending raw journal normalization, receipt success condition и cached postcommit update.
- Отдельный custom acceptance_review выполнил full content pass и targeted R1/null/provenance re-review, verdict PASS. Reviewer snapshot SHA-256 `E2C4A5911A4C7FE3D20A0DED9E7EAB381559F42E9D60B93E89074AFF947842F4`, сверён автором до внесения этого audit. Actual child runtime danger-full-access / filesystem unrestricted / approval never / network enabled: это writable adversarial fallback, не технически изолированное independent review. Reviewer только читал, без файловых правок/runs/network; root дополнительно выполнил отдельный adversarial/re-review выше. Residual risk: sandbox не предотвращал запись технически.
- Fix and re-review: actual journal fields сверены source; F12 null→[] только accepted P1, read/no-op raw null intact. R1 теперь initial C1 на fresh empty root, create false/rename true после external areas edit → partial → outcomeUnknown; контрпример C3 title-only исключён. Removal provenance drift (approval/dirty EXEC) записан без присвоения P0 authorization или readiness. Reviewer подтвердил закрытие замечаний; новые business решения после его hash не менялись, дополняется только audit/journal.
- Stop decision: можно показать v2 и запросить её exact approval. P0/P1 accepted implementation/evidence, own EXEC/runtime/full validation и приёмка остаются самостоятельными gates.

### Depth checklist / Evidence inspected

| Area | Evidence / challenge |
| --- | --- |
| Scope drift / unrelated files | Own checkout только two untracked SPECs: old v1 intact, new v2 current; eff3 untouched |
| Acceptance | AC1–14 mapped; C2 retired key не area classification, F1/F10 default no-op retains key |
| Scenarios / ledger / objections | S1–S6 и шесть objections; selected compatibility и explicit shared gates |
| Validation evidence | Static JSON parse/whitespace/source checks only, tests/build/native future |
| Unsupported claims | Removal не completed, no producer2 release claimed, no installed/live/full PASS |
| Regression / edge cases | Casing/programmatic ExtensionData/nested metadata/null/copy; old/raw journal; cache/receipt gate |
| Docs/schema | Embedded resource1 vs projection2, legacy data allowed vs goal witness invalid |
| Hidden behavior | Authorized save strips marker; no-op не migration; null write follows P1 not P0 |
| Manual challenge | Проверять actual receipt gate и cache invalidate; old images byte restore не goal feature |

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | Journal names | Draft назвал conceptual BeforeImage/AfterImage как поле | Actual BeforeBase64/AfterBase64 и exists flags | fixed/root source re-reviewed |
| MEDIUM | Null-save evidence | F12 «areas policy» недостаточно явно описывала persisted null | Expected [] только после separately accepted P1, raw read/no-op untouched | fixed/root contract re-reviewed |
| MEDIUM | R1 setup | Title-only request не сделал бы external areas postcondition mismatch | Initial C1 на fresh empty root, commit created затем raw areas edit: partial | fixed/targeted re-review PASS |
| LOW | Provenance | Removal SPEC обновилась approval/EXEC record в ходе review | Initial/latest hashes, committed vs dirty source, no transferred authorization/readiness | fixed/targeted re-review PASS |

No-findings justification: открытых BLOCKER/HIGH/MEDIUM/LOW нет после full pass и targeted re-review. Counterexamples проверены по реальному ordering/producer/receipt/storage source: retired witness reject до shortcut с rehashed negatives; old task/nested metadata отличается от active details; retired-only raw drift stale при semantic equality; composed partial отличается от create-only none; no-op только restores receipt и не strips key; raw journal byte images не Notes intent serializer; actual R1 проходит receipt gate, явно инвалидирует кэш и нарушает create postcondition. Для каждого AC есть fixture/test/output evidence plan, выполненным он не называется. Future residual gates accepted P0/P1 SHAs/evidence, fresh integration base/actual producer, full slot и executable show сохраняются.

Post-EXEC не выполнялся; новые builds/tests/PNG/CI не запускались. Общие prerequisite tests не свой CLI runtime PASS.

### EXEC preparation checkpoint — 07.10.2026

EXEC начат, но не завершён. Подготовлен reviewable own diff на `fix/cli-workspace-areaids` от `dce4e1961b3f29e33a2e439730cd09cac885a872`; перенос на shared base и runtime gates ещё не выполнялись. Промежуточный source review не является final post-EXEC PASS.

- Scope/Evidence: `git status --short`, `git diff --stat`, relevant CLI/schema/help/README/test diff, новый CliClassificationCompatibilityTests, current v2 и unchanged historical v1; source RunApply/receipt store/write scope/read-back и shared prerequisite source просмотрены. Production changes только в четырёх CLI files; tests только в пяти own files из §16. Shared files не менялись и не копировались. Historical v1 SHA-256 `0272E803F13416A2108466E66CC909A29FED7FAE777DD400F81DA89BC7ABB136` повторно сверён.
- Contract: projection2/envelope1, areas typed root/path/null/order/duplicates; retired goal запрещён в witness до receipt shortcut. CLI не добавляет area mutation operation или второй ExtensionData filter. Independent field-list oracle заменил reflection к Project/Availability; negative omission созданного areaIds проверяется против этого oracle.
- Prepared scenarios: Unit typed/rehashed root и both path-side negatives/version/retired casing/hash/text/no source mutation; OS-process F0–F12 для receipt/default/nonempty/legacy/null/read/save/raw drift/nested metadata; old committed/uncommitted journal images; R1 actual private RunApply на owned storage с initial C1/real commit/raw external areas/invalidation/no receipt; consumer shared Clone original-map/list-copy/null checks. Это source preparation, не executed test evidence.
- Static validation: `git diff --check` без ошибок. PowerShell `Test-Json -SchemaFile src/Unlimotion.Cli/apply-preview-v1.schema.json` — 15/15 synthetic schema-only cases дали ожидаемый результат: empty root, duplicate/order array, nested metadata и valid area path accepted; missing/null/string/mixed areas, projection1, retired root casing/path variants и null area path rejected. Эти cases не executable CLI witness, не process show и не .NET test PASS.
- Adversarial/roles: challenged omission shared by production formatter, rehashed malformed witness bypassing receipt, broad retired-key cleanup, null read rewrite, composed versus create-only reconciliation, postcommit title-only counterexample и unrelated receipt/lock writes. Domain workflow/architect/source interfaces и tester fixture contracts сверены; artifact role — typed JSON/text source, rendered/runtime show pending; operations — no delivery/install/live/slot ownership claimed.
- Separate acceptance_review inspected diff and returned no open source findings after targeted fixes. Actual child sandbox `danger-full-access`, filesystem unrestricted, approval never, network enabled: writable adversarial fallback, не технически read-only independent review. Он выполнял только чтение; no edits/builds/tests/runtime/network. Проверенный production TaskApplicationPreview.cs SHA-256 `B8D2A139DD9FDD11738252DCF9F9699DBC51D8CE4F83F174569DE82E1523150B` сверён root.
- Fix and re-review: (MEDIUM) команды теперь выбирают actual whole partial Integration class и отдельный R1/Clone class; (MEDIUM) missing-default receipt comparison разрешает ровно computed SHA256(applicationId) receipt path, проверяет его body/requestHash/OperationIds и full other-file bytes/mtime, включая unrelated receipt; (LOW) общий inventory helper отклоняет leftover root lock. Relevant source re-review закрывает эти findings. Добавленные schema/help/text assertions сверены с producer/resource source отдельно.
- Prerequisite read-back: eff3 HEAD всё ещё `5db0b05a5e4615c46de18be83cac7e4e80210959`; P0 removal dirty, owner §19 сообщает incomplete Main/Headless/native acceptance. P1 source TaskApplicationCommandService:844 всё ещё dereferences `task.AreaIds.Count`, TaskItemSnapshot.Clone не копирует/нормализует AreaIds. Accepted P0/P1 SHAs/evidence отсутствуют. Own base не имеет AreaIds; compilation readiness не заявляется.
- Stop decision: независимая подготовка завершена, integration/runtime продолжение ждёт обязательные P0/P1 (§13: «Перенос на общий base, сборки и runtime validation — только после accepted prerequisites»). Discovery/count, targeted/process show, builds, full Main+Headless gate и full post-EXEC review остаются невыполненными. No .NET builds/tests/CI/native/live mutation/commit/push/PR/merge/install выполнялись в этом EXEC checkpoint. User acceptance конечного результата пока не запрашивается.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Test invocation | Planned filter пропускал AreaIds_* и ссылался на несуществующий Night class | Whole actual Integration и separate R1/Clone filter, discovery/count позже | fixed/source re-reviewed |
| MEDIUM | Receipt-only proof | Исключение всего receipt subtree скрывало дополнительные записи | Exact allowed receipt path + body + unrelated receipt/raw inventory | fixed/source re-reviewed |
| LOW | Lock cleanup | Inventory exclusion скрывал persistent leftover lock | Capture только после operation и explicit leftover rejection | fixed/source re-reviewed |
| BLOCKER | Integration prerequisite | Accepted P0/P1 implementation/evidence отсутствуют | Получить их от owner, подтвердить actual base/contracts | pending/shared owner |
| BLOCKER | Mandatory validation | Build/discovery/targeted/full/process show ещё не выполнены | Выполнить после accepted base и allocated validation location | pending/dependency gate |

## Approval

Получено прямое «Спеку подтверждаю» 07.10.2026 для v2 (предъявленный SHA-256 E392B91F26BDAD089ADC6657BD9A4E8A57C751ACF4A46F3B6E80A3AC0BBB47F9). Разрешён только own CLI EXEC; P0/P1 cleanup, Git delivery/install/live не разрешены этим подтверждением. Accepted shared prerequisites остаются обязательными для integration/runtime validation.

Отдельное прямое поручение 07.10.2026 «Оформи PR и влей в мейн» разрешает commit/push/PR/merge own CLI diff. Accepted P0/P1 и обязательный green validation не отменены. На проверенной текущей main dce4e196 TaskItem не имеет AreaIds; eff3 P0 dirty/incomplete, P1 null/Clone ещё не реализован. Поэтому Git delivery выполняется как draft PR с явными blockers; ready/merge остаются до выполнения этих gates. Исправление найденного при установке parentIds-create outcomeUnknown — отдельный product scope, не добавляется скрыто в этот PR.

## 20. Журнал действий агента

| Фаза | Решение | Evidence / остаток | Следующее действие | Человек | Артефакт |
| --- | --- | --- | --- | --- | --- |
| SPEC v2 draft | Goal retired, v1 исторический | Removal SHA57D82/read source; v1 SHA0272 unchanged; P0/P1 future gates | Full review/show/approval | «Давай выпилим IsGoal…», передано координацией | Только этот новый SPEC |
| Post-SPEC v2 | Full root pass + writable adversarial fallback; R1/null/save/provenance уточнены | Reviewer PASS, linter20 PASS/rubric30, two JSON blocks/21 sections static check; removal owner record SHA9606, accepted P0/P1 evidence ещё нет; v1 unchanged | Показать current v2 и запросить её exact approval | CLI v2 пока не подтверждена | Этот SPEC, audit §19 |
| EXEC approval | Прямое подтверждение v2 принято; branch fix/cli-workspace-areaids от main dce4e196 | P0 в dirty eff3, его полные gates незавершены; P1 Count/null и Clone ещё не исправлены/accepted. Общие файлы не копируются | Подготовить независимый CLI diff/tests; runtime после accepted P0/P1 | «Спеку подтверждаю» | Эта SPEC и own branch |
| EXEC preparation | Own CLI projection2/types/schema/docs и F0–F12/R1/oracle prepared; source findings исправлены | git diff --check; schema-only 15/15; writable reviewer fallback; ни build, ни .NET test, ни process show не выполнены | Accepted P0/P1 → integration base → discovery/build/tests/full gates → final review | Нового решения внутри own scope не требуется; shared gates остаются | Own diff, checkpoint §19 |
| Отдельная local install | По прямому поручению установлен existing merged CLI main dce4e196, не AreaIds candidate | Version 1.32.1-local.20261007.dce4e196; Release pack и 24 smoke checks до/после global update, DLL hashes равны, persistent PATH/default context verified; first combined parentIds-create обнаружил outcomeUnknown и сохранён как known limitation | Агенты могут использовать установленный main; own projection2 всё ещё ждёт P0/P1 и integration gates | «Установи текущую версию CLI локально, чтобы агенты могли уже пользоваться» | C:/Users/Kibnet/.codex/cli-installs/20261007-dce4e196-0ea8c2c7/installation.md; предыдущий nupkg сохранён |
| Git delivery preflight | Создать draft PR; merge разрешён пользователем, readiness пока нет | Remote main=dce4e196; eff3 HEAD5db0b05a и dirty P0; Count/null844 и Clone P1 прежние; source review/schema-only не runtime PASS. Auth verified, existing branch PR отсутствует | Commit только own §16 files/current SPEC → push → draft PR; дождаться prerequisites и exact-head mandatory green до merge | «Оформи PR и влей в мейн» | Этот SPEC и draft PR |
| Draft PR delivery | Own candidate закоммичен и опубликован; PR [#318](https://github.com/Kibnet/Unlimotion/pull/318) открыт draft и attached к чату | Code commit 8d3121c946e61efabcc069d89d5b7c51682fbc12, base main dce4e196, GitHub OPEN/isDraft=true/mergeStateStatus=BLOCKED; CI triggered, green не заявлен. Historical v1 остался untracked/unchanged; shared files в diff отсутствуют | Accepted P0/P1 → integrate → mandatory exact-head green/full post-EXEC review → ready/merge. Merge ещё не выполнен | Разрешение merge получено, readiness gates не сняты | PR #318; source/static checkpoint §19 |
