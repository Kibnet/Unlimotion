# Unlimotion CLI: полный контекст ночного агента, изменения и проверяемый preview

## 0. Метаданные

- Статус: core P0/P1 реализован после «Спеку подтверждаю» 2026-10-04. Финальные полные проверки завершены 2026-10-05: main 1217/1217, Headless 51/51, 0 failed/skipped; source hash неизменен (§11.4). Общий solution build остаётся отдельным upstream blocker: Debian Debug CS1061 воспроизведён на main, однострочный candidate проверен в изолированной копии. Внешняя миграция F и публикация не выполнялись.
- Тип: delivery-task / проектирование подсистемы. Масштаб large: публичный CLI, shared storage/domain, внешний workflow. Expanded SPEC по центральному `templates/specs/_template.md`.
- Владелец результата: пользователь; проектирование: Codex. Единственный writer этой SPEC — основной агент.
- Central stack: routing-matrix; creator-vibe-lens; model-behavior-baseline; tool-execution-baseline; collaboration-baseline; quest-governance/mode; spec-linter/rubric; review-loops. Профиль `product-system-design`, стек .NET desktop/shared domain, context `testing-dotnet`; testing-baseline задаёт будущие EXEC checks. Consumer `AGENTS.override.md` требует UI coverage при изменении UI-facing состояния.
- Поверхность: Codex desktop, Windows/PowerShell. Целевой behavior baseline каталога — GPT-6 Astra; точные model ID/effort клиента не проверялись и не влияют на контракт C#. Model eval не применим: меняется продуктовый API, не инструкции модели.
- Исходная база реализации: `49bc04e439c04d13958964e59f01cb864a9e33fa`, initial working tree чистый. База принятого delivery-кандидата от 2026-10-06: `68197565816295c7c766877c39cc40a72b3aeeb4`, ветка `feat/cli-night-context-main681` (§22).
- Output подтверждённого core EXEC: CLI/storage/domain, синтетические тесты, README и packaged skill, evidence этой SPEC. 2026-10-06 пользователь принял показанный core и разрешил Git delivery до merge (§22). Release пакета, установка CLI, живые задачи, vault и миграция реального ночного прохода остаются вне этого поручения.
- Связанные решения: [application contract](2026-09-17-night-agent-cli-application-contract.md), [discoverability](2026-09-27-cli-agent-discoverability-contract.md), [legacy dates](2026-10-01-cli-legacy-date-validation.md).
- Внешние источники прочитаны локально: протокол ночного прохода v3 с дополнениями 2026-10-04 и `New-NightSnapshot.ps1` / `Read-NightBatch.ps1`. Частные пути, карточки, ID и содержимое базы знаний в SPEC не копируются; ниже только синтетические примеры.

## 1. Overview / Цель

Агент должен тратить ночь на полезную подготовку и разрешённую работу по целям, используя уже накопленные результаты. CLI должен дать ему полный графовый контекст за ограниченное число чтений, показать изменения, требующие повторного осмысления, и точное воздействие согласуемого пакета.

**Outcome contract.** Исходная проблема — оставшиеся разрывы массового чтения, повторного использования контекста, поиска и preview, а не отсутствие базового apply. Успех продукта: после перезапуска агент восстанавливает все родительские ветви, продолжает подготовленную работу, не перечитывает неизменное окружение по одному ID и не выдаёт недосмотренный контекст за полный. Утром пользователь видит конкретный результат и необходимые решения; число карточек/экспортированных задач не является продвижением цели.

SPEC согласована 2026-10-04. Результат core EXEC — контракты §6 и доказательства §11 на синтетических пространствах. Переход реального ночного workflow на них — самостоятельный внешний этап F с собственным разрешением.

Stop rules: не расширять работу до нового proposal lifecycle, релиза или автоматического выполнения задач; неизвестные production timings не выдавать за измеренные. Остановить SPEC после закрытия её обязательных review gates. EXEC только после «Спеку подтверждаю»; это не разрешение менять vault/production или публиковать.

## 2. Текущее состояние (AS-IS)

### 2.1 Проверенные источники и границы доказательств

| Источник в этой базе | Подтверждённое поведение |
| --- | --- |
| `src/Unlimotion.Cli/Program.cs`: `RunReadCommand`, `RunUnlocked`, `RunSearch` | Каждый CLI read вызывает `ReadDirectoryAsync`; unlocked отбирает `CanStart`, roots — union roots/descendants. Search — ID/title, сортировка title ignore-case, title ordinal, ID; cursor привязан к path/filters/last key, но не к снимку. |
| Там же: `TaskSnapshotOutput`, `BuildRelations`, `TaskDetailsOutput` | `task --include` выдаёт ETag, details/criteria/status history/execution. Relations — только непосредственные соседи; repeater/importance/wanted уже читаются. |
| `TaskApplicationJson.cs`, `apply-request-v1.schema.json`, `TaskApplicationCommandService.cs` | Есть поля title/user description/duration/dates, criteria, createTask, contains/blocks, setStatus, preconditions, staged validation и recoverable apply. Дополнительные preconditions сейчас не являются общим guard окружения: проверяются затронутые операциями задачи. |
| `TaskApplicationCommandService.ExecuteAsync`, `NormalizeAvailability` | Preview не сохраняет staged tasks, но не возвращает before/after. Нормализация может изменить IsCanBeCompleted, UnlockedDateTime, UpdatedDateTime, status/history в том числе у задач вне явных operations. |
| `Program.RunApply/RunApplyInspect`, `TaskApplicationReceiptStore` | Сохраняются applicationId/requestHash/refs/changed IDs; receipt пишется после task transaction. Inspect различает receiptMatched, desiredStatePresent, readyForPreview, needsReconciliation. Receipt не доказывает отсутствие later drift. |
| `FileTaskStorage.ReadDirectoryCoreAsync`, `DeserializeTaskSnapshot`, `EnumerateTaskFiles` | Последовательное чтение верхнего уровня, известная политика имён, source byte hashes, duplicate/load diagnostics; read открывает файлы с ReadWrite/Delete sharing. `LiveGraphRevision` — счётчик экземпляра процесса, не общий durable change token. |
| `FileTaskStorage.WithDirectoryLockAsync`, `RecoverPendingTransactionsAsync`; `Unlimotion/FileStorage.cs` | CLI и текущий desktop используют общий directory lock по умолчанию. Вход под lock автоматически делает recovery journal, который может записать task files и удалить journal. Это относится и к текущим preview/inspect/read путям. |
| `TaskAvailabilityService` | Старт учитывает будущую PlannedBeginDateTime через текущее UtcNow; блокеры всех предков наследуются. Критерии блокируют completion, но не start. Missing references — validation issue, а не runtime blocker. |
| `UnlimotionCliIntegrationTests`, `FileTaskStorageReadContractTests`, `FileTaskStorageRecoverableMutationTests` | Уже есть процессные проверки apply/receipts/inspect/search/repeater, сохранения unknown fields, legacy dates и recovery при чтении. Наличие теста подтверждено чтением; текущий прогон не выполнялся. |

Подтверждение установленного пакета `1.32.1-local.20261001` и живого context было передано исходным чатом; здесь не перепроверялось и не используется как доказательство поведения базы. Для проектирования хватило source inspection. Живые задачи и их JSON не читались; helpers не запускались.

### 2.2 Матрица «требование → уже есть → разрыв → решение»

| Требование | Уже есть | Разрыв и статус evidence | Решение / приоритет |
| --- | --- | --- | --- |
| A: массовый контекст | Полный загрузчик каталога; single-task DTO и ETag | **Подтверждён:** N подробных task calls дают N полных загрузок; нет persistent snapshot API | Capture + immutable artifact + paged read, all-parent DAG; P0 |
| A: согласованность | Общий lock штатных writers, byte hashes | **Подтверждён:** нет snapshot identity; сторонние writers не обязаны соблюдать lock | Locked capture + второй manifest/hash pass; честная ограниченная consistency; P0 |
| B: изменения | ETag отдельной задачи; UpdatedDateTime | **Подтверждён:** нет delta API; собственный ETag не отражает контекст/время | Сравнение двух сохранённых снимков, membership/availability/context invalidation; P0 |
| C: найти существующее решение | Search ID/title, status/root/pages | **Подтверждён:** description/criteria не ищутся | Opt-in fields, literal NFC search, snapshot pagination; P1 |
| D: точное согласование | Dry-run validates request, changed/created IDs | **Подтверждён:** нет полного diff и guard показанного производного воздействия | `--diff full`, machine/text diff, optional `--expect-preview`; P1 |
| D: preview без записи | Staged domain objects не сохраняются | **Подтверждён кодом:** lock может запустить recovery до preview | Read-only observation boundary; pending recovery даёт отказ, без replay; P0 prerequisite |
| E: repeater/importance/wanted | Чтение всех трёх уже есть | **Подтверждён:** apply их не редактирует; необходимость для шести проверок не доказана | Все три write-расширения в последующей очереди, не в MVP |
| F: действующий workflow | Протокол, карточки, материалы, decisions, queues, helpers существуют | **Подтверждён:** helper после unlocked читает task JSON; batch показывает `ancestorPaths[0]` и урезает цепочку | Миграция helpers на официальный contract отдельным этапом, сохраняет обе очереди |
| Legacy date validation | Исправление уже в базе, тесты существуют | Закрытый разрыв; повторно не проектировать | Regression в наборе EXEC |

**Требуют эксперимента в EXEC:** latency/allocations/lock hold на синтетическом большом графе; races с внешним writer; UX читаемость больших diff; полный recover/restart cycle. **Необязательны:** persisted event feed, поисковый индекс, тонкий incremental invalidation, новые изменяемые поля. Они не являются дефектами MVP.

## 3. Проблема

CLI умеет безопасно выражать многие изменения задачи, но ещё не даёт воспроизводимую единицу чтения и проверки её контекста. Агент повторно собирает граф с потерями, а пользователь не может сопоставить предложенное изменение с полным воздействием того же пакета.

## 4. Цели дизайна

1. Один captured graph для selection, context, availability, ETag и страниц; никакой подмены страниц новым live read.
2. Явно отделять selection целей от context closure; удерживать все parents/ancestors, terminal states и запрошенную историю.
3. Delta — invalidation результатов анализа, а не команда повторно классифицировать весь backlog.
4. Переиспользовать domain rules, snapshot DTO, ETag, request v1 и existing apply/reconciliation; не создавать второй writer.
5. Никаких скрытых записей task/receipt/journal во время observation/preview/inspect; recovery остаётся в разрешённом write path.
6. Совместимость старых CLI forms и данных; ограниченный opt-in API с versioned артефактами.

## 5. Non-Goals

- Не хранить approvals, варианты, материалы, решения и queues в CLI; не вводить автоматическое согласование по receipt или наличию файла preview.
- Не запускать nightly scheduler, не выбирать за пользователя цели, не объявлять unlocked/Prepared полномочием.
- Не индексировать Obsidian/интернет, не делать semantic search, stemming, ранжирование моделью.
- Не менять семантику contains/blocks/status/criteria/repeater, не реализовывать writable importance/wanted/repeater в этом объёме.
- Не гарантировать filesystem-wide atomic snapshot, event history, отсутствие промежуточных изменений между снимками, distributed transaction с Obsidian.
- Не менять UI layout. UI regression для shared availability/storage planned; новый экран не нужен.
- Не рекурсивно импортировать физические подпапки `Archive`/backup как текущие задачи. Статусы Completed/Archived в штатном каталоге входят в снимок. Отличие от старого recursive helper отражается отдельно (§10), не маскируется.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Ответственность и этапы

**P0 / MVP:** безопасное observation-чтение, snapshot capture/read, all-parent контекст, delta, schemas/help и тесты. **P1 / core после P0:** content search и exact preview с opt-in guard. Обе части входят в согласуемое проектирование и будущий core EXEC; можно валидировать их последовательно. **F:** внешняя миграция helpers после core, отдельное разрешение на vault. **E:** необязательная очередь вне core.

| Компонент | Ответственность |
| --- | --- |
| FileStorage observation API | Lock без recovery, manifest/hash stability, полный diagnostic read без изменения task bytes |
| TaskTree snapshot/context services | Scope, adjacency/closure, analysis с единым временем, deterministic context fingerprints |
| CLI artifact codec | Versioned JSON, bounded output, atomic publication заданного файла, checksum, paging/delta cursors |
| Search service | Literal matching и безопасные snippets; общий движок над loaded graph или snapshot payload |
| Application planning service | Общий staged план для preview/apply, explicit/derived/system diff; guarded apply reuse |
| Agent workflow / Obsidian | Смысл шести проверок, useful work, evidence, proposal revisions/approval, external freshness, queues |

### 6.2 Snapshot API и артефакт

Новые forms (это проектируемые команды, сейчас их нет):

```text
unlimotion-cli snapshot capture --selection selection.json --output run.snapshot.json --tasks <path> --format json
unlimotion-cli snapshot read --snapshot run.snapshot.json --view targets|context|manifest [--page-size 100] [--cursor <token>] --format json|text
unlimotion-cli snapshot diff --before previous.snapshot.json --after run.snapshot.json [--page-size 100] [--cursor <token>] --format json|text
unlimotion-cli snapshot schema [--kind selection|artifact|page|delta] --format json
```

`read/diff/schema` не разрешают settings/tasksPath и не обращаются к live storage. Schema kind по умолчанию selection. `capture` требует явный `--tasks`; это исключает незаметное переключение пространства между запусками. Output — обязательный новый файл вне task root; отказ при существующем destination, отсутствии родителя, path traversal/reparse разрешении в task root либо пересечении с input. Создание temp sibling и rename без overwrite происходит только после полного успешного захвата. Отмена/ошибка не публикует final artifact; собственный temp удаляется. Это запись экспортного артефакта, не изменение задач. Успех capture: `{success:true,snapshotId,artifactHash,outputPath,counts,acquisition}`; каталог задач не создаётся при исчезновении root между resolver и observation-конструктором.

Схема selection v1, все остальные свойства запрещены:

```json
{
  "schemaVersion": 1,
  "select": { "mode": "unlocked", "rootIds": ["goal-a", "goal-b"], "statuses": [] },
  "context": "night-v1",
  "missingSelection": "report",
  "include": ["details", "criteria", "history", "execution"]
}
```

- `mode`: `unlocked` (CanStart), `all` (без availability filter), `ids` (обязательный непустой `taskIds`). В ids roots запрещены; statuses запрещены, чтобы явно названные IDs не исчезали молча. В all/unlocked отсутствующие roots означают всё пространство; [] эквивалентно отсутствию. Root union включает сам root и всех descendants через ContainsTasks. `statuses` — дополнительный AND фильтр только targets; [] = все статусы.
- `missingSelection`: `error` (default) или `report` (для повторяемого ночного scope). В error missing root/explicit ID → `notFound`, без артефакта. В report сохраняются `missingRootIds`/`missingTaskIds`, выборка строится только от существующих корней/IDs; отсутствующий root даёт пустую ветвь, а не fallback на всё пространство. Это полное наблюдение отсутствия заданного ID, а не неполный read. Policy входит в scopeHash; ночной consumer выбирает report с первого baseline, чтобы корректно увидеть удаление root/явно выбранной задачи в delta. Missing **graph reference** всё равно фатальна.
- Поля details/criteria включены по умолчанию и обязательны для `night-v1`; history/execution opt-in. История здесь — имеющаяся StatusHistory, execution — текущий record/audit, включая его признак truncation. CLI не восстанавливает несуществующую полную историю правок. Excluded секция помечена `notRequested`, не пустым массивом. Запрошенные секции одинаковы у targets и context nodes; terminal ancestors не урезаются.
- `context`: `none` для инвентаризации или `night-v1`. Полный ночной контекст требует night-v1. Новая политика closure в будущем — новая версия, а не тихое изменение смысла.

**Формальный context closure для target t.** `Anc(X)` — все предки через ParentTasks до корней, без фильтра статуса. `S={t}∪Anc({t})`. `D` — все непосредственные ContainsTasks, BlocksTasks, BlockedByTasks узлов S. `C(t)` — наименьшее множество, содержащее S∪D и замкнутое по ParentTasks и BlockedByTasks. Так включаются все родительские ветви, соседи исходной работы/её целей, контекст и upstream blockers включённых соседей. Не обходить ContainsTasks/BlocksTasks рекурсивно у каждого нового контекстного узла: это раздувает выборку до connected component. Доступность всех узлов вычисляется по полному captured graph, а не по усечённому C(t).

`payloadIds = union C(t)` (для context=none — targets). Каждый node сериализуется один раз по ID, но все edges остаются. Для каждого t сохраняются `contextIds` и `contextHash`; общие ID не означают выбор одного пути. Canonical edges `{kind:contains|blocks,from,to}`; полные четыре relation ID arrays доступны для каждого payload node. Edges к существующим узлам вне payload обозначаются `outsidePayload`, отсутствующая цель — `missingReference` (фатальная ошибка). `outsidePayload` не означает «прочитан полный контекст этого соседа»; при необходимости захватывается более широкая selection.

Сериализуется DAG, а не список всех root-to-leaf paths: число путей экспоненциально. Human view показывает каждое родительское ребро, shared-node ссылки по ID и terminal badges; может сворачивать повторный показ узла, но не скрывать ребро. Произвольный cutoff глубины/первый parent запрещён. Лимит вывода даёт явное продолжение, а не утверждение о полноте показа.

**Артефакт `snapshotFormatVersion=1`:**

| Поле | Точный смысл |
| --- | --- |
| snapshotId | Новый случайный UUID, не task ID и не разрешение |
| artifactHash | SHA-256 канонического JSON всех полей, кроме самого artifactHash; используется для integrity/cursors, не для аутентификации |
| source | `storageKind=file`, opaque `sourceKey`, `sourceNamespace=topLevelTasks-v1`; без абсолютных paths/имен файлов |
| contract | schema/closure/ETag/availability/search-normalization versions, timezone ID/offset загрузчика; часть compatibility |
| selection, scopeHash | Каноническая selection: sorted unique IDs/statuses/includes; hash включает все параметры и contract versions |
| acquisition | startedAt/completedAt, evaluatedAt, consistency=`cooperativeLockWithVerifiedManifest`, atomic=false, attempts |
| completeness | `complete=true`, payloadCount/targetCount/catalogCount, sections, missingRootIds/missingTaskIds; validation warnings; successful artifact всегда полный для заявленной selection/policy |
| catalog | Для **всего штатного каталога**: ID, ETag, field-section hashes, canonical edges, availability value/hash. Без title/description/history bodies вне payload. Нужен, чтобы отличать новое/удалённое от вошедшего/вышедшего из selection |
| targets | Sorted entries `{id,contextIds,contextHash}` |
| nodes | Sorted payload records: ID, ETag, task summary, details/criteria, requested history/execution, all relation IDs, canonical availability with reason IDs |
| nextEvaluationAt | Минимальная будущая PlannedBeginDateTime полного графа относительно evaluatedAt, либо null; подсказка повторного захвата, не расписание |

`sourceKey` = hash нормализованного абсолютного resolved physical root (Windows без различения регистра) + namespace + platform identifier. Path aliases через reparse/symlink должны разрешаться к одному root; невозможность надёжно разрешить путь — явный отказ capture. Этот ключ различает каталоги, но не доказывает непрерывность пространства после полного замещения по тому же пути. В таком случае delta даёт state difference, не историю. Перенос пространства в другой path требует нового baseline. Никаких sidecar source UUID в task root.

ETag идентичен `TaskEtag.Create`, не заменяется raw hash/mtime/Version. В него входят неизвестные domain fields; непоказанные изменения отмечаются `unprojectedDataChanged`. Raw bytes hash используется для проверки acquisition и отдельно для application source-manifest guard (§6.6), не как task ETag. ContextHash включает sorted `(ID,ETag,availabilityHash)` всех C(t), induced/boundary edges и membership, версии policy и requested sections; изменение parent/блокера инвалидирует анализ потомка даже без изменения его ETag. При context=none `C(t)={t}`. ContextHash не включает snapshotId/acquisition timestamps: новый захват неизменного fully specified model не инвалидирует анализ только из-за времени создания артефакта.

### 6.3 Согласованность, lifetime, limits, paging

Capture внутри общего directory lock выполняет: проверку pending journals без recovery; enumeration A; чтение/десериализацию каждой задачи ровно один раз с byte hash; enumeration B и повторный byte hash всех файлов; сравнение множеств relative paths/hash; graph validation; единый analysis на `evaluatedAt` после стабильного чтения. Политика имён совпадает со storage; zero-length task-shaped files теперь диагностируются как incomplete capture, а не исчезают. Исключаются hidden service directories. Успешная десериализация по **существующей** domain read policy, ID mapping, duplicate IDs, reciprocal links и cycles проверяются до публикации. Совместимость `JsonRepairingReader` сохраняется: допустимый in-memory repair без sidecar не превращается в новый strict-JSON отказ; raw hashes относятся к исходным bytes. Невосстановимый parse error фатален. Stored/computed availability mismatch сообщается как warning: снимается вычисленное состояние без записи/исправления. Legacy load-time defaults могут менять ETag при неизменных bytes; такие консервативные invalidations не скрываются. No-op reuse AC проверяется на fully specified fixture, не обещает стабильности неизвестных legacy default values.

Одинаковый manifest подтверждает наблюдённую стабильность двух обходов. Штатные writers с тем же lock не вклинятся; внешние программы, не соблюдающие lock, могут сделать ABA или взаимно несогласованный переход между проверками. Поэтому **не обещается линейризуемость/атомарность относительно произвольных writers**. В артефакте всегда `atomic=false`. Для критичного application всё равно нужен свежий preview/ETag guard. Если внешний writer непрерывно меняет файлы, возвращается `snapshotUnstable`, без частичного valid artifact.

Одна попытка capture использует два bounded raw-read прохода и один domain parse. При явном drift допускается ещё две попытки (всего 3) с освобождением lock между ними. Lock timeout остаётся storage default 10 s на acquisition lock; общий capture deadline 60 s, cancellation проверяется между файлами/блоками hash и traversal. Timeout/permissions/corruption не ретраятся без устранения причины. Большой legit каталог требует явного изменения лимитов после measurement, не бесконечного lock.

MVP limits: 50 000 tasks, 500 000 canonical edges, 8 MiB на исходный task file, 256 MiB total source bytes, 128 MiB artifact UTF-8, 5 000 000 суммарных entries в per-target contextIds. Превышение — `snapshotTooLarge` с названием лимита и измеренным/минимально известным значением. Никакого silent truncation, автоматического отбрасывания history/terminal ancestors или «успешной» частичной выборки. Уменьшение roots сокращает payload, но не полный source scan; сообщение различает source/payload limits.

Артефакт хранит вызывающий workflow в своём output location. Автоматического TTL/GC/внутреннего cache нет. Он читается после перезапуска, пока существует и поддерживается версия; давность не мешает historical read/diff. Он **никогда не означает текущие ETags**. ScopeHash/contract incompatibility, повреждение или пропажа baseline требуют full fallback. Обновление CLI с сохранёнными contract versions не инвалидирует артефакт только из-за package version.

`read` views: targets — selected nodes, context — только дополнительные payload nodes, manifest — catalog metadata. Page size 1..500, default 100, порядок ID Ordinal. Страница максимум 16 MiB; если один record не помещается — `recordTooLarge`, предлагается явно более высокий разрешённый output budget в последующей версии, но v1 не обрезает. При byte boundary возвращается меньше page-size, nextCursor указывает следующий ID. JSON envelope: `{schemaVersion,snapshotId,artifactHash,view,totalCount,items,nextCursor,complete:true}`; complete означает корректную страницу, `nextCursor=null` — конец view. Все страницы используют исходные evaluatedAt/availability.

Cursor — opaque base64url versioned structure: artifactHash/snapshotId/scopeHash/view/filterHash/page-size/last ID. Не доверенный file path и не permission. Malformed/wrong artifact/filter cursor — `cursorMismatch`, exit 2; структура сверяется с данными артефакта, но курсор не подписан: потребитель обязан передавать полученный токен без изменения, самостоятельно пропущенный last ID не считается доказательством полноты обхода. Отсутствующий artifact — `snapshotNotFound`, exit 1; повреждённый digest/schema — `snapshotInvalid`, exit 1. Обновлять содержимое файла под прежним ID запрещено; checksum mismatch не запускает live fallback. До десериализации проверяется лимит bytes; неизвестные schema properties/versions отклоняются. stdout содержит только response выбранного формата, diagnostics идут в stderr. Invalid arguments/cursor — exit 2, storage/limit/integrity/recovery errors — exit 1, только полный успех — exit 0.

### 6.4 Delta вместо persisted change feed

Выбран **state diff двух полных, совместимых snapshot artifacts**. Захват всё ещё O(N bytes), но один вместо N загрузок. Separate event feed потребовал бы всех writers, delete tombstones, durable generations, compaction и recovery; Git/sync всё равно могут обойти его. Для текущего сценария state delta достаточна и проверяема.

Сравнение допускается при одинаковых sourceKey/namespace/scopeHash/contract versions, успешной completeness и `after.evaluatedAt >= before.evaluatedAt`. Новый capture с тем же evaluatedAt допустим для теста. Изменение roots, mode, statuses, include/history policy, normalization/availability version или timezone → `deltaIncompatible`; отсутствие/порча before → `baselineUnavailable`. Нет ответа «изменений нет» при ошибке. Нет silent partial delta.

`diff` envelope: `{schemaVersion,beforeSnapshotId,afterSnapshotId,scopeHash,coverage:"stateDifference",items,totalCount,nextCursor,baselineUsable:true}`. Records сортируются `(kind Ordinal, taskId Ordinal, relation key Ordinal)`, paging/byte limit как read; cursor связан с обоими artifact hashes. Типы:

| kind | Условие / поля |
| --- | --- |
| created | ID отсутствовал в before.catalog, есть в after.catalog; afterEtag, afterRoles |
| deleted | ID был в catalog, теперь отсутствует; beforeEtag, beforeRoles, evidence=`absentFromObservedNamespace` |
| updated | ETag отличается; before/after ETag, changedSections, unprojectedDataChanged |
| relationAdded / relationRemoved | Canonical edge difference, from/to/kind; изменение одной и той же reciprocal связи выводится один раз |
| membershipChanged | beforeRoles/afterRoles (`target`, `context`, пусто); entering/leaving targets/context отдельно от created/deleted |
| availabilityChanged | before/after CanStart/CanComplete/IsCanBeCompleted/reason identities; причины taskData/context/clock (набор, не ложная точная причинность) |
| targetInvalidated | Target присутствует хотя бы в одном снимке, изменились собственные данные, C(t), contextHash, availability или membership; причины и changedContextIds |

`deleted` означает отсутствие в штатном namespace; CLI не утверждает, что файл физически уничтожен, а не перемещён в backup. Terminal status не равен deleted. Вышедшая из roots/status/unlocked выборки задача остаётся в catalog → membershipChanged. Если нет target в after, consumer удаляет его из активной выборки, но сохраняет материалы/decision history. Context-only узел остаётся, пока нужен хотя бы одному target.

Catalog differences полны по всему observed namespace; `targetInvalidated` определяет работу именно заявленной selection. Чужая независимая ветвь может дать updated без invalidated target. For each t из union targets вычислить C_before(t)/C_after(t), сравнить contextHash и semantic availability; изменение общего предка/блокера инвалидирует все затронутые t. Default консервативен: ETag history/extension change может дать лишнюю invalidation, но не пропускается. Оптимизировать до field-specific dependencies — follow-up с доказательством отсутствия пропусков.

Clock-only change обязательно обнаруживается: новый capture пересчитывает **весь граф** с новым evaluatedAt. Наступивший planned begin может изменить unlocked membership при идентичных ETags. Старый snapshot при read не пересчитывается. `nextEvaluationAt` помогает consumer решить, когда захватить новый state.

**No-miss boundary:** любой отличающийся endpoint state/edge/membership/включённый контекст сравнивается; созданная и удалённая между двумя capture задача, изменение с возвратом к прежнему состоянию и внешние источники не обнаруживаются. Это явно state comparison, не audit/event stream. Внешние факты и Obsidian свежесть проверяет workflow независимо.

**Full fallback:** сохранить очередь/decisions/materials; сделать новый capture с желаемой selection; отметить `baselineReset` во внешнем run record; пройти весь набор targets на необходимость revalidation, сохраняя ранее подготовленные результаты как candidates reuse, а не как проверенные текущие. Baseline становится текущим только после атомарной записи queue checkpoint, отсылающего к after artifact hash. При пропуске запуска разрешено сравнить последний обработанный baseline с новым снимком; промежуточные captures не обязательны.

### 6.5 Content search

```text
unlimotion-cli search --snapshot all.snapshot.json --query "проверка решения" --fields title,description,criteria --limit 20 --format json
unlimotion-cli search --tasks <path> --query "проверка решения" --fields title,description,criteria --limit 20 --format json
```

Старый вызов без новых флагов остаётся побайтно совместимым по JSON shape и логике ID/title/OrdinalIgnoreCase/live cursor v1. Новые формы имеют `searchVersion=2`, matches и source metadata. `--snapshot` и `--tasks` взаимоисключающие; ни settings, ни live refresh не используются для snapshot search. Поиск идёт только по payload, `searchedNodeCount`, `catalogCount`, `coverage=payload` это явно показывают. Для поиска по всем задачам consumer делает capture mode=all/context=none/include details,criteria; terminal статусы включены по умолчанию.

`--fields`: непустой unique subset `id,title,description,criteria`; default id,title. description — только `DescriptionUserText` без generated execution markers; criteria — Text всех критериев, включая satisfied/terminal. Execution logs/history, unknown extension values, raw protected description не ищутся. Повреждённые маркеры описания дают warning `descriptionUnavailable` для ID и `searchComplete=false`; такой ответ не доказывает отсутствие решения. Unknown field — invalidArguments.

Query — одна буквальная подстрока, whole query OR по выбранным полям, без tokenization/regex/wildcards/operators. Trim не делается; пустая строка перечисляет всё, совпадений/snippets для неё нет. Unicode NFC для query и каждого поля, затем OrdinalIgnoreCase; ё/е различны, Latin/Cyrillic confusables различны. Не применять текущую culture/морфологию. PowerShell quoting — обычное shell quoting, JSON escaping — только JSON serializer. HTML/Markdown не интерпретируются. Text output экранирует control/ANSI chars; исходные JSON строки сохраняют точное содержание.

Match `{field,criterionId?,snippet,normalization:"NFC",ranges:[{start,length}],moreOccurrences}`: индексы — UTF-16 code units нормализованного snippet; boundaries не разрезают surrogate pair. До 3 совпадений на поле (для criteria на criterion), 160 Unicode scalar values на snippet; `matchesTruncated=true` при отсечении. Это только отображение, не отсечение самих matched tasks. Порядок задач прежний title ignore-case/title ordinal/ID; matches — field ordinal, criterion ID ordinal, offset. Relevance scoring нет.

Snapshot cursor v2 связан с artifactHash, query, fields, status/roots, normalization version, limit и последним sort key. Для --root весь union соответствующих descendants по catalog должен присутствовать в payload, иначе `scopeNotCaptured`: наличие одного root record не доказывает полноту его ветви. Запрос description/criteria при исключённой соответствующей секции → `sectionNotCaptured`, а не пустая успешная выдача. Live v2 cursor фиксирует фильтры/path, но честно `consistency=liveUnpinned`; stable pages гарантирует только --snapshot. Индекс не нужен: in-memory linear scan O(text bytes), без persistent индекс-файлов и freshness debt. Индекс — отдельный measurement-driven этап.

### 6.6 Exact preview и guard применения

```text
unlimotion-cli apply --request approved.json --dry-run --diff full --tasks <path> --format json
unlimotion-cli apply --request approved.json --expect-preview preview.json --tasks <path> --format json
```

`--diff full` допустим только с --dry-run. Старый dry-run без флага сохраняет shape. Новый response сохраняет прежние поля плюс `preview` ниже. JSON stdout можно сохранить в preview.json; file должен содержать полный успешный response, не human text. `--expect-preview` opt-in, обязательный для нового ночного workflow; request v1 не меняется. Старые writers не обязаны использовать guard и сохраняют прежнее поведение. Help явно различает unsupported flag на старом binary и plain preview; workflow не молча откатывается к более слабому режиму.

`apply schema --kind preview --format json` выдаёт schema полного witness response; default `--kind request` и прежний `apply schema --format json` сохраняют request v1. Schema kind никак не выбирает режим выполнения request.

```json
{
  "previewVersion": 1,
  "requestHash": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "sourceKey": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
  "evaluatedAt": "2026-10-04T02:00:00Z",
  "complete": true,
  "changes": [
    {"taskId":"work-1","path":"/details/plannedDuration","before":null,"after":"PT45M","origin":"explicit","operationIds":["duration-1"]},
    {"taskId":"goal-a","path":"/relations/contains/work-1","before":false,"after":true,"origin":"explicit","operationIds":["contain-1"]},
    {"taskId":"goal-a","path":"/availability/canStart","before":true,"after":false,"origin":"derived","operationIds":["contain-1"],"reasonTaskIds":["work-1"]}
  ],
  "protectedExecution": {"changed":false},
  "guard": {"version":1,"sourceManifestHash":"sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","effectHash":"sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"}
}
```

Это сокращённый пример формы `preview`, не готовый guard-файл: синтетический request содержит `setField(plannedDuration)` для work-1 и `addRelation(contains, goal-a, work-1)`. Goal-a до пакета Prepared и доступен; work-1 не завершена. Поэтому contains блокирует goal-a, а duration сама по себе доступность не меняет. Полный response также содержит reciprocal/system изменения, beforeEtags и сформированные hashes.

Planner отдаёт original→final staged, не промежуточные значения отдельных operations. Общая модель используется и реальным apply. Diff содержит:

- Все явные field/criterion/status изменения; полные строки before/after, null отличается от пустого текста. Created node: before=null, after полная доступная проекция, включая default status/criteria/parents.
- Canonical relations added/removed с обеими сторонами, плюс reciprocal stored field changes со ссылкой на canonical edge. Удаление criterion передаёт полное before. Удаление задачи не добавляется как новая операция.
- Все производные stored изменения **по всему графу**: availability normalization, UnlockedDateTime, status/history и UpdatedDateTime; все изменения вычисляемой availability (CanStart/CanComplete/reasons), даже при неизменном task ETag. Stored и calculated paths различны.
- `origin`: explicit / derived / system. `operationIds` только когда causality известна; normalization с несколькими причинами указывает reasonTaskIds и `cause=graphNormalization`, не выдуманную единственную operation.
- beforeEtag для каждой существующей затронутой задачи; afterEtag preview обозначен predicted и не пригоден для future precondition. AgentExecution и execution-сегмент description защищены; raw diff их не раскрывает, показывает invariant `protectedExecution.changed=false`. Изменение user text не перезаписывает marker. Нарушение invariant — отказ preview, не скрытие diff.

Генерируемые apply timestamps (`CreatedDateTime` у новой, UpdatedDateTime, новые history timestamps, новый UnlockedDateTime) представлены точным before и typed after `{generatedAtApply:true,rule:"applicationClock"}`; для UpdatedDateTime rule=`nextUpdated` сохраняет existing `previous >= now ? previous + 1 second : now`; для UnlockedDateTime — сохранение прежнего или now при переходе. Прогноз previewTime можно показывать отдельно. Они не обещаются exact до commit. Business values, статусы, наличие history entry/author и все связи exact. EffectHash исключает только перечисленные generated timestamp values, но включает их наличие/тип/rule/author/status; остальные даты, execution state и unknown field hashes не игнорируются. Create, SetStatus и NormalizeAvailability используют единый clock одного plan, без дополнительных UtcNow внутри прохода.

Guard привязан к exact decoded request text hash (существующая BOM policy), sourceKey, contract versions, **sourceManifestHash** всего штатного namespace и semantic effectHash. Manifest hash — sorted `(relative filename, SHA256 original bytes)` по стабильной enumeration/verification; он ловит create/delete/rename и byte-only changes. Имена в public preview не выдаются, только digest. Нельзя использовать ETag всех задач как graph guard: TaskItem.CreatedDateTime default=UtcNow у старого JSON без этого поля даёт новый ETag при неизменных bytes. Existing ETag preconditions для явно затронутых operations остаются неизменными. Канонизация object keys/primitive JSON использует неизменную семантику текущего TaskEtag canonicalizer; массивы новых контрактов заранее сортируются в явно указанном порядке. Unicode для hash не нормализуется; NFC применяется только к поиску. Содержимое preview не является подписью или approval.

Перед receipt shortcut проверяется сам witness: ограничение 32 MiB, strict schema/versions, `success=true`, `mode=preview`, `didMutate=false`, `complete=true`, совпадающие applicationId/requestHash/sourceKey, отсутствие дубликатов change keys и недопустимых generated placeholders. Changes/affected IDs/protected invariants из witness нормализуются, effectHash **пересчитывается из тела** и сравнивается с сохранённым. Observational beforeEtags/predicted afterEtags не входят в effectHash: actual before bytes защищены manifest, а explicit ETags независимо проверяются against request. Полные semantic before/after values не исключаются. Удалённая derived change при прежнем guard даёт `previewInvalid`, без новых request writes. Только scalar hashes из файла недостаточны. После этой проверки и receipt/reconciliation, если нужна запись, под application lock сравниваются свежий sourceManifestHash, explicit ETags и нормализованный saved effect с re-staged plan/effectHash. Любое несовпадение даёт `previewStale` без новых writes текущего request. В том числе изменение bytes независимой задачи консервативно инвалидирует guard; это осознанная цена простоты. Clock-only изменение, влияющее на эффект, тоже отклоняется. Изменение clock без изменения semantic effect разрешено.

Observation отмечает known nondeterministic materialization: отсутствие CreatedDateTime в raw JSON и производную историю, использовавшую этот load-time default. Незатронутая такая задача не мешает guarded apply: её raw manifest стабилен, её observational ETag не сравнивается глобально. Если задача с таким default входит в existing explicit preconditions, fresh full preview возвращает `unstablePrecondition`; если planner собирается сохранить её лишь вследствие global normalization — `unstableSource`. В обоих случаях указываются taskId/field, full executable guard не выдаётся, автоматического исправления legacy нет. Ограничение относится к записи нестабильно материализованной задачи, а не к её присутствию в пространстве. Решение о явном ремонте этой задачи — отдельный scope; core не обещает его. Tests различают untouched legacy, explicit target и derived write. Эта политика не меняет TaskEtag algorithm и plain legacy apply.

Guard закрывает drift, наблюдаемый до первой записи, относительно writers, соблюдающих lock. Bypass writer после проверки может столкнуться с existing storage source guards/recovery и привести к частичным попыткам записи, rollback или `outcomeUnknown`; обещание «stale никогда не пишет» к этой гонке не применяется. Required evidence — truthful outcome и полный inspect/read-back, без заявления exactly-once истории. Ужесточение до межпроцессного CAS всех сторонних writers вне core.

При matching receipt guard не сравнивает старый graph с уже применённым: возвращает существующий alreadyApplied, после чего обязателен inspect/read-back. При отсутствии receipt и полном desired state существующая reconciliation допускает alreadyApplied, не повторяя task writes; guard не превращает это в новый apply. Mixed/drift state остаётся needsReconciliation. Поведение receipt и его отдельная запись сохраняются; cross-store atomicity не добавляется.

Чтение для dry-run и inspect использует новый observation mode и ту же verified acquisition политику, что capture: pending journal → `recoveryRequired`, никакого replay. Разрешённый write-path по-прежнему выполняет штатный recovery; user/workflow не удаляет lock/journal руками. Recovery может изменить ранее записанный незавершённый пакет до проверки нового guard: обещание `previewStale` — **без новых writes текущего request**, не отрицание existing recovery. После recovery повторяются inspect/preview в зависимости от результата. Lock-file handle/создание `.unlimotion.lock` — разрешённая coordination metadata; «без записи preview» означает отсутствие изменений task bytes, receipt, journal и application audit, а не отсутствие системных filesystem metadata.

Full diff максимум 32 MiB; если не помещается — `previewTooLarge`, `complete=false`, **без guard**. Нельзя согласовывать урезанный diff; пакет делится только по самостоятельным атомарным намерениям и с новыми applications/preview, иначе требуется новый лимит в отдельном изменении. Text view по умолчанию показывает полный human diff, экранирует control/ANSI, группирует explicit/derived/system и в конце перечисляет affected IDs. Длинные тексты показываются unified lines с явной ссылкой на полную JSON-форму; human excerpt не заменяет complete machine artifact.

Human preview storyboard (синтетический пакет содержит duration и contains):

```text
Предложение P-012 / r2; application A-012-r2; request sha256:…
Явно: work-1 «Подготовить проверку»
  plannedDuration: не задано → 45 минут
  contains: goal-a → work-1 [добавить]
Производно: goal-a
  canStart: да → нет; причина: незавершённая work-1
Системно: work-1, goal-a
  UpdatedDateTime: прежнее значение → время применения
Execution state: сохранён. Полный diff: да. Это preview, задачи не записаны.
```

### 6.7 Дополнительные поля E

Для шести проверок нужны чтение и предложения, а не все возможные setters. Duration/dates/description/criteria/graph/status уже позволяют согласовать полезный следующий шаг. `importance`, `wanted`, `repeater` **не включены в core write scope**; значения продолжают читаться и сохраняться неизменными existing apply.

| Поле | Почему отложено | Условие будущей SPEC |
| --- | --- | --- |
| importance | Ранжирование workflow не должно автоматически менять приоритет владельца; доказанного блокирующего сценария нет | Согласовать диапазон/значение, не выводить доменный диапазон из UI control; same ETag/preview/unknown-field tests |
| wanted | Намерение пользователя не выводится из unlocked/давности | Явное owner decision, boolean semantics и отсутствие автоматической сортировки/архивации |
| repeater | Rule и occurrence различны; это не простой перенос срока | Validation Type/Period/pattern/AfterComplete, timezone/calendar, invalid legacy preservation; отдельно preview будущего occurrence и parity desktop |

MVP не создаёт setters этих полей и не меняет повторение при переносе planned dates. Перенос конкретного шага ≠ завершение; completion ≠ «пропустить один повтор»; намерение отредактировать правило не реализуется заменой конкретного экземпляра. Repeating-task mutation допускается лишь при уже понятной existing operation semantics; неоднозначное намерение остаётся внешним предложением, не новый автоматический write.

### 6.8 User-Observable Scenarios

| Scenario | Trigger | Наблюдаемый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Все ветви одной задачи | Два parents, общий ancestor, terminal ancestor | Один node, все связи и цепочки достижимы; descriptions/criteria/history по include сохранены | Synthetic DAG pages и rendered text | AC1–3 |
| Продолжение следующей ночью | Новый capture и diff | Неизменные материалы reused, затронутые ancestors/blockers revalidated, обе queues продолжаются | 3-cycle replay ledger | AC4, AC11 |
| Решение уже записано | Query совпадает только с description/criterion | Найдена задача со snippet и ETag, terminal также найдены | Search fixture | AC5 |
| Согласовать пакет | Создание части + изменение оценки | Видны exact field/edge/derived impact и защищённое execution state | Machine/text golden artifacts | AC6–7 |
| Desktop меняет данные | После preview, до apply | previewStale; обновлённый смысл требует revision, старое согласие не переносится | Concurrency fixture | AC8 |
| Сбой после tasks commit | До receipt или до Obsidian checkpoint | Inspect/read-back; нет duplicate create; result journal восстановлен | Crash-point matrix | AC9, AC11 |
| Отказ/отложено | Decision на конкретную revision | Нет apply; повторный показ только по новым основаниям/условию | External synthetic workflow replay | AC11 |

### 6.9 State / Interaction Matrix

| State | Trigger | Result | Error/concurrent handling |
| --- | --- | --- | --- |
| Нет baseline | Первый запуск | Complete capture, полная initial revalidation queue | Ошибка capture не создаёт empty baseline |
| Complete artifact | Read/page after restart | Те же nodes/order/evaluatedAt | Missing/corrupt → explicit error, не live data |
| Baseline + current | Compatible diff | End-state changes + invalidations | Incompatible → explicit full fallback |
| Capture in progress | External writer | Retry только drift, до 3 attempts | Нет final artifact при failure |
| Proposal r1 pending | Accept exact r1 | Prepare exact request/preview, then guarded apply в разрешённом workflow | Неоднозначный ответ сохраняет pending |
| Proposal r1 rejected/deferred | Следующий запуск | Сохранить решение, не спрашивать автоматически снова | Новое основание → r2, новое решение |
| Apply outcome unknown | Restart | Inspect + read-back всех affected IDs | Pending recovery блокирует observation; mixed → reconciliation |
| Tasks committed, vault checkpoint absent | Resume | Записать application result ровно один раз, восстановить queues | Не повторять task mutation ради записи результата |

### 6.10 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| State snapshot vs event feed | agent | Explicit artifacts + state diff | 0.95 | Не видны transient events; граница заявлена | Нет |
| Context encoding | agent | DAG + формальный night-v1 closure | 0.90 | Может быть широкий payload; limits и tests | Нет |
| Partial data | agent | Fail closed publication, no truncation | 0.95 | Один corrupt task остановит capture | Нет |
| Extra fields | agent | Read-only в core, setters later | 0.90 | Часть будущих предложений останется внешней | Нет |
| Preview guard | agent | Opt-in full source-manifest guard + explicit ETags + semantic effect | 0.90 | Ложная invalidation на unrelated byte edit; volatile target требует отдельного ремонта | Нет |
| Approval/queues | user context + agent | Существующий внешний workflow | 0.99 | Нет cross-store transaction; recovery planned | Нет |
| EXEC scope gate | user | Core P0/P1 только после exact approval | 1.00 | Изменение поведения без разрешения запрещено | Отдельное подтверждение SPEC, не открытый выбор |
| Vault rollout | user | Самостоятельный этап F после core | 1.00 | Без него live helpers останутся прежними | Вне core EXEC; отдельное поручение |

### 6.11 Runtime / Config / Data Contract Matrix

| Area | Current truth | Change | Compatibility/migration | Verification |
| --- | --- | --- | --- | --- |
| Task source | resolver + FileTaskStorage | capture explicit path, opaque sourceKey | Resolver precedence не меняется | Alias/path/source mismatch tests |
| Time | UtcNow внутри analyzer | Explicit evaluatedAt/TimeProvider overload для capture/preview | Default current-time behavior сохраняется | Boundary clock test + UI regression |
| Read lock/recovery | Recovery on entry | Observation mode no replay для новых reads, dry-run, inspect | Default existing write recovery остаётся; new recoveryRequired error documented | Pending committed/uncommitted journal byte checks |
| Task ETag | TaskEtag.Create | Reuse exact algorithm | Version included in artifacts | Same-node ETag parity |
| Persisted task format | TaskItem + extension data | Нет новых domain fields | Нет миграции task files | Unknown-field/legacy regression |
| Snapshot files | Отсутствуют | Caller-owned immutable v1 JSON | Не в task root, нет auto cleanup | Restart/integrity/limit tests |
| Apply request/receipt | v1 + request text hash | Request v1 unchanged, optional preview witness | Existing schema and receipt preserved | Legacy process tests |
| Private workflow | Vault protocol v3/helpers | Только отдельная миграция F | Existing decisions/materials/queues preserved | Synthetic replay, later authorized live read-back |

## 7. Бизнес-правила и сквозной цикл

Шесть исходных проверок сохраняются у workflow: (1) контекст дерева/базы и точное место сохранения недостающего ответа; (2) полезная автономная подготовка/эксперимент; (3) evidence завершения/архива; (4) активная длительность и допущения; (5) полезная декомпозиция, обычно свыше 30 минут, варианты; (6) известный следующий шаг и основание. CLI поставляет факты, не заменяет смысл этих проверок.

`contains(current,part)` — часть результата, незавершённая part блокирует current. `blocks(prerequisite,current)` — prerequisite. `blocks(current,next)` с осознанно выбранными goal parents next — последующий шаг; не добавлять contains(current,next). Варианты остаются вне графа, до выбора не создавать все альтернативы как обязательные children. Существующие multiparent связи не удалять при выборе новой области.

Синтетический end-to-end acceptance trace:

1. Night N1 сохраняет capture S1 и baseline ID/hash; все goals учитываются, готовые действия имеют приоритет перед повторной инвентаризацией. Target `work-1` имеет родителей goal-a/goal-b, оба видны.
2. Workflow выполняет разрешённую проверку, сохраняет материал M1 и предложение `P-012/r2`: source S1, contextHash, exact fields/edges before→after, rationale, evidence, selected option, observed ETags, dependencies on proposal revisions. Формирует exact request A-012-r2 с existing operations. Ночью tasks не изменяет.
3. Пользователь видит готовый материал, diff и одно смысловое решение. Однозначное accept P-012/r2 записывается вне CLI. Rejection/defer хранит причину/условие; изменение содержания → r3 без переноса approval. Approval не означает запуск произвольного кода, публикацию или completion фактической работы.
4. До записи workflow сохраняет durable application intent: applicationId, proposalRefs, **исходные request bytes**, requestHash, expected affected IDs, decision reference. Затем делает fresh full preview теми же bytes, сравнивает с одобренным intent включая derived effects. Новый эффект требует новой revision; если intent/эффект прежний и разрешение действует — повторный вопрос не нужен.
5. Guarded apply с pinned executable/tasksPath и тем же request. На precondition/previewStale не редактирует автоматически ETag; делает current read/новый preview, оценивает изменение смысла. Уже данное разрешение не расширяется на иной before/after.
6. Inspect при неизвестном исходе; read-back всех explicit **и derived** affected IDs, новых задач и endpoints изменённых edges. Если current отличается от receipt — фиксировать drift, не объявлять успех. Сравнить business fields/relations/availability с intent; реальные generated timestamps сохранить как результат, не сравнивать с прогнозом.
7. Внешний application-result record записывается идемпотентно по applicationId+requestHash, хранит receipt assessment/read-back time/IDs/evidence и confirmed outcome. Только после этого queue checkpoint ссылается на application result и snapshot. Прерванная запись checkpoint после result восстанавливается по ID; tasks повторно не создаются.
8. Night N2 наследует обе очереди/решения и S1, делает S2+delta. Материалы M1 используются повторно при валидности их context/external evidence; подготавливаются шаги, открытые решением. Clock/ancestor/blocker invalidations не пропускаются. Unchanged CLI delta не доказывает отсутствие новых внешних фактов.
9. При сбое между task commit и receipt intent позволяет найти исходный request. `desiredStatePresent` подтверждает состояние, не авторство записи; фиксируется reconciled state, не вымышленный receipt. При missing intent нельзя конструировать новый application «по памяти»: read-back/reconciliation. При pending storage journal observation возвращает recoveryRequired; только уже разрешённый write recovery по существующим правилам, затем новая сверка. Cross-store transaction нет.

В протоколе уже есть обе очереди и лимит не более пяти самостоятельных утренних решений за весь обычный разбор. Migration сохраняет их, не создаёт дополнительную анкету. Delta invalidation — повод проверить актуальность, а не автоматически переписать карточку, повторно спросить пользователя или повторить завершённый эксперимент.

## 8. Точки интеграции и триггеры

- `Program` dispatch/`CliOptions`/`CliIntrospection`: новые forms, help/schema, errors и resource versions; source resolution до capture, отсутствие source resolution для offline read/diff/search.
- `FileTaskStorage`: отдельный observation scope, чтобы PreviewAsync/InspectAsync не заходили в default recovering lock; счётчики bytes/parse/attempts для tests. Обычные writes сохраняют recoverable boundary.
- `TaskAvailabilityService`/Analyzer: единый clock на план/снимок; не менять domain logic/status policy. Replay tests проверяют отсутствие UtcNow drift в одном результате.
- `TaskApplicationCommandService`: выделить общий in-memory plan, original/final snapshots/analysis, diff и effectHash; проверять guard до первой Save под lock. Не считать дополнительные request preconditions заменой graph guard.
- Application receipt store не превращается в proposal store. Preview не пишет receipt; failure receipt write после task commit остаётся unknown/reconciliation case.

## 9. Модель данных / storage

TaskItem format, statuses, edge types, receipt schema и task ETag неизменны. Новый durable state — только пользовательские snapshot/preview artifacts вне каталога задач. Catalog metadata в artifact покрывает весь namespace; его scope шире human payload и это раскрыто в help, без приватных bodies вне payload. Нельзя коммитить generated live artifacts в product repo по умолчанию.

Observation mode меняет поведение чтения pending journal, но не формат/recovery algorithm. Legacy read commands за пределами новых forms могут сохранить recovering behavior; **dry-run и inspect переводятся обязательно**, их help явно сообщает новый отказ. Snapshot artifacts и preview не являются источниками данных для blind overwrite task files. Unknown extensions сохраняются existing writer; diff их непредвиденное изменение считает ошибкой planner.

## 10. Миграция / Rollout / Rollback

Core можно доставить без миграции задач. Новые consumers capability-check `help snapshot`, `help search`, `help apply`; имена контракта/versions важнее package guess. Первое использование — explicit capture и full baseline. При rollback binary старые команды/requests продолжают работать; snapshot v1 остаётся evidence, unsupported version даёт отказ. Удаление caller-owned артефактов не меняет задачи, но требует full fallback. Применённые task changes не откатывать из snapshot; обратное изменение — отдельный согласованный request с текущими ETags.

**F — план отдельно разрешаемой миграции внешних helpers:**

1. Зафиксировать их актуальные versions/inputs и backup в разрешённом vault workflow; текущая работа этого не делает.
2. `New-NightSnapshot.ps1`: pinned binary+context, capture вместо unlocked + raw task JSON. Сохранить RunId, metadata, очереди/карточки; raw stateHash больше не сравнивать с новым ETag. Новый формат записывать рядом с прежними историческими runs, не переписывать их.
3. Проверить semantic coverage старого recursive loader и нового top-level namespace на синтетических duplicates/archive/virtual IDs. Старый helper выбирает по UUID filename и приоритету archive; новый contract принимает реальные storage IDs и не подбирает backup как задачу. Любые исключённые historical archive-only данные сообщаются как external history source, не как исчезнувшая текущая задача. Если действующему workflow они нужны — отдельный официальный historical-source contract, а до него migration не объявляется полной.
4. `Read-NightBatch.ps1`: официальный snapshot read/context; вместо `ancestorPaths[0]` показать DAG со всеми parent edges/корнями и shared references, terminal status, missing/continuation diagnostics. Номер страницы — display navigation, не stable task identity.
5. Consumers сохраняют baseline hash, contextHash и applied application IDs; изменение внешнего evidence продолжает инвалидировать материал независимо от CLI delta. Baseline advancement только с queue checkpoint.
6. Synthetic N1→morning→N2 replay и no-op N3 доказывают отсутствие потерь/дублей/повторных вопросов. Реальный read-only shadow-run только по отдельному поручению; запуск helpers не подразумевается чтением их исходников. После проверки — controlled switch; rollback возвращает старые helpers и сохранённые runs/decisions, не откатывает task data.

Без этапа F core CLI может быть готов, но устранение обходного чтения в действующем ночном процессе ещё не выполнено. Это отдельные уровни готовности в отчёте.

## 11. Тестирование и критерии приёмки

### 11.1 Acceptance-to-Test Matrix

Матрица согласована в SPEC-фазе; результаты реализации и фактических запусков приведены в §11.3. Все новые fixtures синтетические, temporary directories, без user task root.

| AC | Проверяемый результат | Automated test/check | Evidence artifact / manual check |
| --- | --- | --- | --- |
| AC1 | Targets равны domain selection при одном evaluatedAt; root union/ids/statuses и empty корректны | NightAgentSnapshotTests: compare oracle graph; missing root/ID в error/report, root deletion между snapshots; Completed/Archived context | Selection JSON + expected IDs |
| AC2 | Все непосредственные parents/ancestor edges и нужные dependencies сохранены без выбора primary parent | Diamond DAG, общие ancestors, >2 parents, terminal ancestor, blocker второй ветви; C(t) oracle | Paged JSON reconstructed graph и text DAG inspection |
| AC3 | Одна immutable выгрузка: ETag parity, секции, restart/pages без drift | CLI process snapshot read after task mutations; roundtrip hash; pages union no missing/duplicates; limits/cancel/corrupt/zero-byte/duplicate/cycle | Hashes, page IDs, explicit errors; task bytes unchanged |
| AC4 | Delta не пропускает endpoint state/membership/context/time impacts | Create/update/absence; leave scope/unlocked; relation-only; parent description; inherited blocker; clock-only; changed include/source/version; missing baseline | Before/after fixtures + oracle invalidated targets, replay output |
| AC5 | Поиск ID/title/description/criteria, русский Unicode и literal escaping; old search сохранён | Snapshot+live search v2, Ё/Е, composed/decomposed Unicode, quotes/brackets/newline/ANSI, terminal criterion, malformed marker, matching across pages | Expected snippets/ranges + legacy JSON golden |
| AC6 | Full diff отражает все explicit/derived stored/calculated изменения | TaskApplicationPreviewTests и TaskApplicationPreviewReplayTests: create+relations+criteria/status+duration; normalization unrelated ID; long strings/null; after no-op | Machine diff applied to public before projection equals final (generated fields по правилам); human storyboard inspection |
| AC7 | Preview/inspect/new reads не меняют задачи/receipt/journal, protected state сохранён | Pending committed/uncommitted journals, crash residue, unknown fields/markers; bytes/mtime and write-spy | recoveryRequired, identical persisted bytes; lock metadata отдельно |
| AC8 | Guard связывает request/body/source bytes/effect; drift до guard не пишет текущий request | Changed request whitespace, appId/source, target/context/unrelated bytes; clock; удалить derived change при прежнем hash; incomplete witness; missing CreatedDateTime untouched vs explicit/derived target; bypass writer до/после guard и recovery отдельно | До guard: zero new-request writes; untouched legacy не мешает; volatile written target — typed refusal; recovery: только старый journal; после guard: truthful failure/rollback/outcomeUnknown + read-back; stable-input guarded preview succeeds |
| AC9 | Retry/restart не дублируют task/edge; receipt ≠ current state | Existing apply tests + crash before/after journal commit/receipt; matching receipt with later drift; no-receipt desired/mixed state | inspect/read-back trace, task/history counts |
| AC10 | Existing public behavior и desktop state не регрессируют | Full Unlimotion.Test suite; MainControlAvailabilityUiTests/MainControlTaskStatusIconUiTests; new UI regression для clock/normalization path; existing repairing-read fixture | Build/full-suite result; synthetic desktop scenario |
| AC11 | Полный внешний цикл воспроизводим без действующего vault | Synthetic harness N1/r2 accept/apply/crash/result/N2 + reject/defer/r3; goal-a/goal-b queues и next-step semantics | Decision/application/queue ledger, no duplicate experiment and unchanged material hashes |
| AC12 | Повторная работа измеримо сокращена при полном контексте | 3 000 nodes/600 targets benchmark, 10% multiparent, one shared blocker; compare existing repeated task reads vs capture/read | Raw wall time/allocations/source read/parse counts; 100% expected context edges |

AC12 обязателен по структурному показателю: N parses для успешного capture плюс N verification byte reads (без retry), **0 source reads** для дальнейших страниц/diff/search по артефакту; baseline single-task route — N×K source parses для K подробных reads. На no-op N3: 0 invalidated targets, 0 заново выполненных одинаковых экспериментов и 0 переписанных неизменных материалов; queue continuation при этом не остановлена. На changed parent/blocker fixture ожидаемые affected targets строго совпадают с oracle, потерянных контекстных edges — 0. Wall-time speedup не предсказывается; сохраняется измерение и объяснение overhead, regression >20% против capture budget на одинаковом fixture расследуется, но не подменяет completeness.

Полный suite обязателен в core EXEC из-за public contract/shared storage. При изменении UI-facing availability/normalization добавить/обновить релевантный существующий Avalonia.Headless/AppAutomation test согласно local override, запустить его и проверить наблюдаемое состояние. UI layout/video planning для нового экрана не применим: экран не создаётся; для shared-state UI scenario fallback — existing headless screenshot/state evidence с явной причиной отсутствия physical video. Если выбран FlaUI/AppAutomation workflow, применить его отдельные video gates.

### 11.2 Команды и stop rules EXEC

Перед тестами проверить установленный SDK/runner, рабочую branch/base и `--list-tests`. TUnit/Microsoft.Testing.Platform, не VSTest `--filter`. План обычных команд:

```powershell
dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --list-tests
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter '/*/*/UnlimotionCliIntegrationTests/*'
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter '/*/*/NightAgentSnapshotTests/*'
dotnet build src/Unlimotion.sln
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj
git diff --check
```

Имена solution/new tests подтвердить inventory перед EXEC. Existing CLI characterization → synthetic failing check → implementation → targeted → affected build → full required suite. Длительность полной suite в этом worktree не измерялась; объявить команду, log path и repo-specific timeout перед запуском, не изобретать SLA. После failure сначала diagnosis, не одинаковые blind retries. После green обязательного набора не расширять проверки без нового риска.

Текущий design validation: source/contract walkthrough, synthetic decision tables, JSON examples parseability, local links, scope/privacy/diff checks, full post-SPEC review. Это не implementation/benchmark/UI test evidence. Установленный CLI, NuGet package и real night effectiveness здесь не проверяются.

### 11.3 Evidence EXEC

Среда: Windows, .NET SDK 10.0.401, runtime 10.0.12, TUnit 1.44.0 / Microsoft.Testing.Platform 2.2.2. Проверки используют синтетические task directories. Реальный vault и установленный CLI не участвуют.

**Сборки.** Изолированный CLI: `dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj --no-restore --output artifacts/cli-night-context/cli-final -v minimal` — 0 ошибок, 0 предупреждений, 4,24 с (`artifacts-night-cli-final-build.log`). Финальная сборка `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore -v minimal` — 0 ошибок, 51 предупреждение, 39,46 с (`artifacts-night-build-final5.log`); включены CLI, storage/domain, desktop и server dependencies. Предупреждения не объявлены устранёнными.

**Общая suite.** `dotnet run --no-build --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --maximum-parallel-tests 4 --output Detailed --report-trx --results-directory TestResults/night-full-final` завершилась за 31 мин 48 с: **1216 выполнено, 1212 passed, 4 failed, 0 skipped**. Лог `artifacts-night-full-final.log`; TRX `TestResults/night-full-final/*_2026-10-04_15_06_07.9916623.trx`.

Все четыре падения исправлены, пересобраны и повторно проверены:

| Найденный дефект | Исправление | Фактический повтор |
| --- | --- | --- |
| Snapshot schema cold start и CLI schema process (2 теста) | Явный DefaultJsonTypeInfoResolver в snapshot JsonOptions; schema доступна до первого serialize | NightAgentSnapshotTests 17/17; UnlimotionCli* 71/71; fresh CLI process для всех четырёх schemas |
| Pending-journal refusal CLI показывал operationFailed вместо recoveryRequired | Явное отображение всех восьми новых application error kinds | UnlimotionCli* 71/71; fresh plain/full/inspect процессы возвращают recoveryRequired и сохраняют bytes/mtime |
| Settings SSH fixture наблюдала старое Connected до завершения команды | Тест ожидает завершения ReactiveCommand, сохраняя исходные конечные assertions; production settings не менялись | SettingsViewModelTests 74/74 |

Группы повторной проверки 2026-10-04: snapshot **17/17**, CLI **71/71**, settings **74/74**, preview и machine-diff replay **13/13** — всего **175/175**. Logs: `artifacts-night-{snapshot,cli,settings}-recheck.log`, `artifacts-night-preview-final.log`; TRX в `TestResults/night-{snapshot,cli,settings}-recheck/`, `TestResults/night-preview-final/`. AC6 replay применяет каждый machine change к полной публичной before-проекции и сравнивает с final, включая canonical edges, escaped criterion IDs, generated placeholders и unrelated normalization; удаление derived changes служит отрицательным контролем. Исправлена только сериализация nullable значений в oracle, полнота сравнения сохранена. Отдельный процесс также проверяет byte-only drift → previewStale без записи и успешное применение исходного witness после восстановления fixture. На этом этапе полная suite после исправлений ещё не повторялась; окончательные полные main/headless результаты, закрывающие этот пробел, приведены в §11.4.

В завершённой общей suite прошли FileTaskStorageObservationTests **21/21**, NightAgentSearchTests **6/6**, TaskApplicationPreviewTests **12/12**, UnlimotionCliSourcePinTests **2/2**, TaskAvailabilityAnalyzerTests **7/7**, MainControlAvailabilityUiTests **4/4**, оба NightAgentWorkflowReplayTests. Новая Headless UI regression `HistoricalAvailabilityObservation_DoesNotChangeCurrentStatusPicker` подтверждает неизменность текущего status picker после historical-clock анализа. Физическое pointer/video evidence не создавалось: экран и layout не менялись; next-best evidence — автоматизированное Headless состояние UI, CLI text storyboard и JSON. AC7 сравнивает также LastWriteTimeUtc task/receipt/journal. Junction-тесты проверяют закрепление physical source перед reconciliation и receipt shortcut.

**Три ночи.** `ThreeNightsAndMorningReplay_GuardedCommitLostReceiptReconciliationRevisionAndCheckpoint` прошёл отдельно и в общей suite: domain guarded commit → отсутствующий receipt → отдельный CLI inspect → alreadyApplied без повторных task writes → единственный result → восстановленный checkpoint. N3: 0 invalidations, прежний material hash, сохранены rejected/deferred decisions и execution queue, experiment count=1; r3 не принимает witness r2. Missing baseline вызывает полный reset/revalidation с сохранением внешних ledgers. Evidence: `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/night-agent-replay-20261004-150604/` (snapshots, exact request/preview, intent/result/ledger/checkpoint и конечные synthetic tasks). Это synthetic consumer, не миграция установленного workflow.

**Benchmark AC12.** Raw evidence: `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/night-agent-benchmark-20261004-150601.json`, копия `artifacts/cli-night-context/benchmark.json`. Fixture: 3000 узлов, 600 targets, 60 multiparent targets, общий blocker.

| Измерение | Старые повторные чтения | Capture + offline |
| --- | ---: | ---: |
| Wall time | 347,091 с | 11,131 + 6,905 = 18,036 с |
| Domain task parses | 1 800 000 | 3000 при capture; ещё 3000 raw files проверены вторым manifest pass |
| Чтения источника на offline этапе | Не применимо | 0; исходный каталог физически перемещён |
| Process-wide cumulative GC allocations | 22 406 087 136 bytes | 1 926 514 600 + 1 676 658 200 bytes |

В этом fixture wall time сокращён примерно **в 19,2 раза**. Allocations — суммарные выделения GC, не peak memory; test изолирован внутри TUnit, прочая нагрузка host не контролировалась, OS cache включён, порядок capture → legacy → second capture → offline. Legacy исполняет 600 настоящих ReadDirectoryAsync+Analyze+TaskSnapshotOutput без process startup. Результат не переносится автоматически на установленный ночной workflow.

Независимый fixture oracle проверил все **1260 canonical edges**, **600 contexts**, включая все **60 multiparent targets**, **2668 payload relation arrays**, **7260 aggregate context IDs** и **366600 aggregate incident context edges**: missing/extra = 0. No-op: 0 target invalidations, 0 task writes, bytes неизменны; повторный capture снова читает 3000 и проверяет 3000 файлов.

**Читаемость.** Проверены `artifacts/cli-night-context/storyboard/preview.txt` и `context-dag.txt`: явные/производные/системные изменения различимы, показаны обе parent-ветви shared-work; task bytes после preview идентичны. Help/schema/README и packaged skill описывают discovery, ограничения и guard/recovery boundaries.

**Ограничения общего gate.** Предварительный serial suite остановлен после server-fixture index race и ShiftDelete UI assertion; он не является PASS. Server-fixture получила существующее в coordinator bounded ожидание Raven index, без изменения сервера; в завершённой suite прошла. ShiftDelete также прошёл в завершённой suite без изменения его production-кода. Общий `dotnet build src/Unlimotion.sln` выявил CS1061 `WithDeveloperTools` в Desktop/ForMacBuild/ForDebianBuild и остановлен до завершения остальных платформ (`artifacts-night-solution-build.log`). Desktop source/project files этим core не менялись, но отдельного чистого baseline reproduction нет; причина не объявляется независимо доказанным baseline-дефектом. Это непройденный solution gate, при успешной отдельной сборке test project после restore. CI, packaging, установка и live rollout не проверялись.

### 11.4 Закрытие remaining validation gate после координации

2026-10-04 проверка возобновлена: успешные affected rechecks не заменяют green обязательной полной suite. Выполнены свежие сборки, полные main/headless прогоны на одном зафиксированном наборе исходников, проверка его hash после запуска и сверка shared storage с #314. Общий solution build учитывается отдельно, пока подтверждённый upstream blocker не устранён. На общей машине соблюдена очередь Importance → status → CLI; full CLI начат только после явного освобождения слота status. Результат получен 2026-10-05 по Europe/Moscow; имена TRX/raw artifacts используют UTC 2026-10-04.

Сверено `main@5a780b2e9c90c68eca421f1477c69c543a90b92d`. Ни одно из четырёх падений §11.3 не исправлено этим main: два schema-теста и pending-journal test относятся к новым CLI changes, settings-test всё ещё использует раннее наблюдение состояния вместо завершения команды. Наши исправления остаются нужны. Shared fixes взяты точно из main в четырёх файлах: ServerStorageCrudRealtimeContract (штатный timeout первого запроса, bounded visibility poll и failure diagnostics), FileStorageTaskStatusTests (current-format hydration fixture), TestHelpers (ожидание реального изменения task count), MainControlTreeCommandsUiTests (async waits, факт удаления из cache и файла). Production emoji changes не переносились; перед заменой сохранены локальные копии.

Переносимый settings fix: `artifacts/cli-night-context/validation-20261004/settings-ssh-command-wait.patch`, SHA-256 `dcadf0df86dd74d720b9d266e12e3b975ec24ddc31407813270ef916f6c3f51e`. Он ожидает завершения ReactiveCommand и сохраняет конечные assertions. Новые CLI fixes специфичны этой feature: DefaultJsonTypeInfoResolver и явное отображение новых error kinds в ApplicationCommandOutput.

**Общий build blocker теперь воспроизведён на main.** Из `git archive` точного main собрана отдельная копия в `artifacts/cli-night-context/validation-20261004/main-build-probe/`. Команда `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj -m:1 -p:UseSharedCompilation=false -v minimal` (Debug по умолчанию) завершилась exit 1, CS1061 WithDeveloperTools, Program.cs:81; лог `main-debian-build.log`. Контроль `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false -v minimal` на той же копии завершился exit 0, 0 warnings/errors (`main-desktop-build.log`). Общие Program.cs и project files совпадают между базой core и main. Debian не содержит DiagnosticsSupport PackageReference при общем DEBUG-вызове; Desktop и ForMacBuild содержат ссылку. Сообщённый diary-кандидат с одной Debug-only PackageReference проверяется только в отдельной копии main, без переноса сторонних UI/packages и без изменения desktop-файлов основной feature-ветки.

**Storage contract #314, чтение `41bd9a78`:** partial class/interface lists объединяются аддитивно; public WithDirectoryLockAsync сохраняет recovering default для Reload, observation вызывает отдельный core с recoverPendingTransactions=false. Reload generation/revision/hash checks и историческая `_reloadTaskFilePaths` mapping сохраняются рядом с CLI observation raw manifest. При объединении успешное observation alias mapping нужно также согласовать с reload mapping, чтобы subsequent corrupt/renamed alias не превращался в ложное Missing. Нельзя подменять source manifest local revision или добавлять безусловный flush перед Reload. После реального объединения обязательны combined alias/corrupt/watcher/reload и preview pending-journal tests; read-only сверка не объявляет merge проверенным.

Однострочный diary-кандидат в отдельной main-копии проверен: `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj -m:1 -p:UseSharedCompilation=false -p:BuildProjectReferences=false -v minimal` завершился exit 0, 0 errors, 1 NETSDK1206 warning, 18,97 с (`main-debian-candidate-debug.log`). MSBuild item evaluation подтверждает presence DiagnosticsSupport в Debug и отсутствие в Release (`main-debian-candidate-{debug,release}-items.json`); Release build здесь не выполнялся. Source/code feature-ветки не менялся этим экспериментом. Нельзя объявлять этим единственным project build успешным общий solution build.

Новый validation evidence сохранён в `artifacts/cli-night-context/validation-20261004/`; source fingerprint включает 687 non-ignored build/test inputs, hash **`8155caf36a79d80070ce1e2d6745f44bf5e693c53b4c4d905f1adc96b68545c5`**. Он совпал в подготовительном снимке, перед full и после обоих full запусков. Обе свежие сборки успешны: main 0 errors/55 warnings/56,49 с, Headless 0 errors/4 warnings/69,20 с (`build-main.log`, `build-headless.log`). Перенесённые fixtures проверены адресно: hydration 1/1, ShiftDelete 3/3, settings 1/1, server CRUD/realtime 1/1. Discovery подтвердил 1217 main и 51 Headless тест.

**Финальный полный результат:** `pwsh -NoProfile -File artifacts/cli-night-context/validation-20261004/Invoke-FullGate.ps1` последовательно вызвал штатный `scripts/ci/Invoke-TestStage.ps1 -Stage test -Project main` и `-Project headless` с общим ResultsRoot и maximum-parallel-tests=1. Итог helper exit 0, обе стадии exit 0. Локальное время: main начат 2026-10-05 00:46:50, Headless 01:18:56; проверка завершена около 01:21:42.

| Полный project | Total / Passed / Failed / Skipped | Время тестового отчёта | Evidence относительно validation directory |
| --- | --- | --- | --- |
| main | **1217 / 1217 / 0 / 0** | 31 мин 59,821 с | `full-main.log`, `full-gate/main/*_2026-10-04_22_18_55.0439325.trx` |
| Headless | **51 / 51 / 0 / 0** | 2 мин 41,166 с | `full-headless.log`, `full-gate/headless/*_2026-10-04_22_21_41.6672974.trx` |

TRX проверены на точное совпадение с discovery, без пропусков. В полном main отдельно подтверждены Passed всех четырёх прежних failures, `MachineDiff_ReplaysWholeFinalProjection_IncludingCreationAndUnrelatedNormalization` и `TaskGraphWorkspaceCommandScenario_ExecutesFeatureSteps` (40,271 с). Последнее относится к этому source hash и не отменяет finding другой ветки. `source-full-before.json`, `source-full-after.json`, `test-binaries.json`, `full-result.json`, `completion.json` сохраняют источник, бинарные hashes и результат; исходники во время проверки не изменялись. Слот полных проверок освобождён после завершения обоих процессов.

Повторный AC12 benchmark также Passed: **347,598 с legacy против 17,003 с capture+offline (11,078 + 5,924 с), примерно 20,4×** в том же synthetic fixture. 3000 parses + 3000 raw verification files против 1 800 000 domain parses; offline sourceReads=0, canonical/context coverage missing/extra=0, no-op writes/invalidations=0. Raw evidence `benchmark.json` в validation directory, оригинал `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/night-agent-benchmark-20261004-221852.json`. GC allocations: legacy 22 406 170 168 bytes, capture 1 926 496 832, offline 1 638 481 608; это cumulative process allocation, не peak memory. Границы измерения из §11.3 сохраняются. Повторный three-night replay сохранён в `src/Unlimotion.Test/bin/Debug/net10.0/TestResults/night-agent-replay-20261004-221228/`.

При финальной read-only сверке рабочий `FileTaskStorage.cs` status-ветки не имеет diff к `41bd9a78`, SHA-256 `0580fe287b3559933cc1f1c2b4b96187f0675423536a454e9e65febee21ee81c`; storage merge rules выше актуальны для проверенного снимка. Реального merge/runtime integration здесь нет. Остаточный gate — общий solution build; narrow Debian candidate доступен отдельно, main/Headless PASS не объявляется успешным full solution/CI/release.

## 12. Риски и edge cases

| Риск | Смягчение / остаточная граница |
| --- | --- |
| External ABA writer | atomic=false; bounded double-manifest verification; свежий guarded apply; абсолютная гарантия без всех writers невозможна |
| Wide parent closure / path explosion | DAG, explicit payload/byte/context-index limits; no silent cutoff |
| One corrupt task / pending journal | Complete capture отказ; observation не чинит store; recovery отдельно в write path |
| Full graph guard часто устаревает | Conservative MVP; повторить preview; targeted read-set guard только после measurement |
| Timezone/default normalization | Версия и timezone в compatibility; ETag reuse same code, fixed evaluatedAt; не объявлять raw offset preserved |
| Lost baseline / artifact tampering | Integrity/schema validation и full fallback; hash не security signature |
| Historical Archive differs from active namespace | Явная migration inventory; не включать backups как authoritative автоматически |
| Receipt/task/Obsidian windows | Durable intent + inspect/read-back + idempotent result checkpoint; никакой cross-store transaction |
| Protected execution / unknown fields | Field-specific projection + full ETag, invariant tests; не search generated marker |

### Expected User Review Objections

| Likely objection | Why likely | Mitigation | Status |
| --- | --- | --- | --- |
| «Опять инвентаризация вместо работы» | Snapshot сам ничего не продвигает | Outcome/AC11–12 требуют reuse материалов и продолжение обеих queues; метрики прогресса отдельно | mitigated |
| «Ты потерял вторую цель/родителя» | Текущий batch показывает первый path | Формальный all-parent closure/DAG, diamond/terminal fixtures, edge coverage 100% | mitigated |
| «Я соглашался на другой эффект» | Derived normalization не видна в dry-run | Full explicit/derived diff, same request hash, guarded apply, revision boundary | mitigated |
| «Новая команда не помогла живому helper» | Repo и vault разные области | F отдельно отмечен как необходимый rollout, core не объявляется live migration | mitigated |
| «Почему не менять повторение сразу?» | Поле уже читается | Ни одна из шести проверок не требует setter; rule/instance semantics выделены в E | accepted-risk вне core |

Rework Prevention: наблюдаемый результат/CLI forms названы; сценарии связаны с AC; choices и unknowns отмечены; роли review применены в §19; критерии проверяют результат, а не наличие активности. EXEC имеет synthetic route до public delivery. Новых private artifacts в repo нет.

## 13. План выполнения после approval

1. **Observation + characterization:** byte-level no-write tests, pending journal behavior, фиксированный clock, без изменения recovery write algorithm.
2. **Capture/read:** format/schema, bounds, closure/ETag, paths/parents/text view, restart/page tests. Stop при неопределённой source identity/неполноте, а не fallback на direct JSON helper.
3. **Delta:** compatible endpoint comparison, all-parent/context/time invalidation, full fallback contracts и benchmark counters.
4. **Search:** opt-in fields/NFC/snippets, legacy compatibility, snapshot pages.
5. **Preview/guard:** общий planner, full diff, protected fields, stale/reconciliation/crash tests; total impact сохраняется.
6. **Integration:** schemas/help/README/operational skill examples; synthetic workflow replay; targeted + full tests, shared-state UI regression, post-EXEC review. Только после этого отдельные delivery/install/real workflow утверждения.
7. **F вне core:** получить отдельное поручение на helpers, выполнить §10 и проверить live migration отдельно.

## 14. Открытые вопросы

Блокирующих продуктовых решений для согласования core нет: defaults и консервативные границы выбраны. Latency/размеры/частота guard conflicts требуют измерений в EXEC, с определёнными тестами и failure policy; это не основание придумывать atomic guarantees или расширять scope. Потребность в archive-only historical files оценивается при F; её отсутствие не предполагается.

## 15. Соответствие профилю

Product-system-design: цели/non-goals, API и совместимость, границы ownership/config/storage, error/recovery и alternative analysis заданы. .NET/shared domain: существующие services переиспользуются; UI thread/layout не затрагивается; defaults clock и state должны пройти regression. Quest: изменяется только рабочая SPEC, canonical expanded structure сохранена, review и exact approval отдельны. Личный контекст использован для бережного отношения к времени и результата, личные сведения в артефакт не перенесены.

## 16. Таблица изменений файлов

**Фактические области core EXEC:**

| Файл / область | Изменение | Причина |
| --- | --- | --- |
| `src/Unlimotion.Cli/Program.cs`, options/introspection | New forms/flags/outputs/errors | Официальная обнаружимость |
| Новые `src/Unlimotion.Cli/NightAgentSnapshot*.cs`, search/preview DTO и embedded schemas | Artifact codec/cursors/diff/search | Versioned bounded contracts |
| `src/Unlimotion.FileStorage/FileTaskStorage.cs`, options | Observation scope и verified manifest | Read-only guarantee без recovery |
| `src/Unlimotion.TaskTreeManager/TaskAvailabilityService.cs`, analyzer | Explicit clock overload | Stable analysis и clock-only delta |
| Новые `TaskGraphObservation.cs`, `TaskApplicationPlan.cs` в TaskTreeManager | Observation/guard interfaces и before/explicit/final plan | Общий контракт domain/storage; closure/hash/membership реализованы в CLI snapshot codec |
| `src/Unlimotion.TaskTreeManager/TaskApplicationCommandService.cs` | Common planner, diff, optional guard | Preview/apply consistency |
| Existing TaskEtag / TaskApplicationJson | Переиспользованы без изменения алгоритма и файла | Compatibility |
| CLI README, `.agents/skills/unlimotion-cli/SKILL.md` | Usage/capability/error/limits docs | Убрать необходимость обхода файлов |
| CLI integration + storage read/recovery + новые snapshot/search/preview tests | AC1–12 | Постоянные регрессии |
| Existing Headless availability/status test area | Shared-state regression as applicable | Local UI gate |
| `ServerStorageCrudRealtimeContract.cs`, `FileStorageTaskStatusTests.cs`, `TestHelpers.cs`, `MainControlTreeCommandsUiTests.cs`, `SettingsViewModelTests.cs` | Четыре shared fixture fixes из main и отдельное ожидание завершения ReactiveCommand | Устранение гонок тестов; production server/settings не менялись |
| Внешние helpers/vault | Нет изменения core; план F | Отдельная область разрешения |

## 17. Было → стало

| Область | Было | После core |
| --- | --- | --- |
| Детали K задач | K загрузок полного каталога | Один capture, offline pages |
| Родители | Immediate summaries, client recursion | All-parent DAG + context records |
| Изменения | Повторный сбор/сравнение скриптом | Compatible state diff + invalidation |
| Поиск | ID/title | Opt-in description/criteria + stable snapshot pages |
| Preview | Changed IDs | Full before/after + derived impacts + optional guard |
| Pending recovery на preview | Может выполнить replay | recoveryRequired без task/journal writes |
| Decisions/queues | Внешний работающий workflow | Сохраняется; F переводит транспорт данных |

## 18. Альтернативы и компромиссы

| Вариант | Плюсы | Минусы / решение |
| --- | --- | --- |
| Batch `task --ids` без snapshot | Малое изменение, одна загрузка | Нет durable pages/delta/restart; недостаточно исходному сценарию |
| Snapshot artifact + state diff | Простая проверяемая model, охватывает внешние endpoint changes | Full scan на capture, storage artifacts; **выбрано** |
| Persistent event feed | Дешёвое чтение incremental changes | Нужны все writers/tombstones/retention; не покрывает bypass автоматически; отложено |
| Глобальный atomic storage/MVCC | Более сильная consistency | Меняет desktop/sync/storage architecture; непропорционально core |
| Export всех root-to-leaf strings | Удобно для короткого дерева | Exponential duplication, легко потерять branch; DAG выбран |
| Search index | Быстрее большие повторные запросы | Freshness/миграция/дублирование данных; linear scan сначала |
| Проверять только explicit ETags | Совместимо с текущим apply | Не охватывает context/derived impact; optional full graph guard выбран |
| Approval lifecycle внутри CLI | Можно соединить proposal и receipt | Дублирует рабочий vault и границу ответственности; не нужен |

## 19. Quality gate и review

### SPEC Linter Result

| № | Блок / пункт | Статус | Проверяемое основание |
| --- | --- | --- | --- |
| 1 | A / Цель и outcome | PASS | §1: полезная работа/reuse вместо подсчёта карточек; текущий output — SPEC |
| 2 | A / AS-IS | PASS | §2: actual HEAD и исходники, возможности apply не объявлены отсутствующими |
| 3 | A / Корневая проблема | PASS | §3: воспроизводимая единица контекста и проверка воздействия |
| 4 | A / Цели дизайна | PASS | §4: responsibility, consistency, compatibility |
| 5 | A / Границы | PASS | §5, P0/P1/E/F; никаких live changes/соседнего релиза |
| 6 | B / Ответственности | PASS | §6.1: storage/domain/codec/planner/workflow |
| 7 | B / Интеграции | PASS | §8, §16: dispatch, read scope, clock, planner, docs/tests |
| 8 | B / Алгоритмы | PASS | Closure, membership, hash/cursor, query semantics, guarded effect |
| 9 | B / Errors/recovery | PASS | Pending journals, partial capture, stale/invalid preview, reconciliation |
| 10 | B / Performance | PASS | Явные size/time bounds, parse/read counters, AC12; скорости не выдуманы |
| 11 | C / Данные | PASS | §9: нет task migration, artifacts вне root, catalog scope раскрыт |
| 12 | C / Совместимость | PASS | Legacy CLI forms/ETag/request сохранены; opt-in forms и new error documented |
| 13 | C / Rollback | PASS | §10: binary/helper rollback без blind task restore |
| 14 | D / Измеримые AC | PASS | AC1–12, no-missing context edges, no-op reuse и crash points |
| 15 | D / Evidence mapping | PASS | §11 matrix покрывает multiparent/concurrent/invalid baseline/reject/revision/restart |
| 16 | D / Commands/stop | PASS | TUnit commands, existing solution path verified, full suite planned, no blind retry |
| 17 | E / План | PASS | §13 staged dependencies и F вне core |
| 18 | E / Decisions | PASS | §6.10 и §14: choices выбраны, нет незакрытого user-owned выбора core |
| 19 | E / Форма/масштаб | PASS | Expanded, large public behavior/shared storage, полный review |
| 20 | F / Profile | PASS | §15 и роли ниже; ограничения actual sandbox раскрыты |

Итог: **ГОТОВО к согласованию SPEC**. PASS относится к проектированию и плану проверок, не к реализованному CLI.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Цель и границы | 5 | Исходный сценарий и шесть проверок сохранены; core/F/E отделены |
| 2. AS-IS | 5 | Проверены фактические source contracts, recovery и volatile legacy ETag |
| 3. Конкретность | 5 | CLI forms, JSON fields, closure, hashes, paging, compatibility/errors заданы |
| 4. Безопасность/откат | 5 | Observation без replay, no partial publication, conservative guard, recovery boundaries |
| 5. Проверяемость | 5 | AC1–12 с отрицательными/сбойными сценариями и performance structural evidence |
| 6. Автономность после approval | 5 | Выбраны defaults, этапы и stop rules; неизвестные измерения имеют проверочный план |

**30/30, готово к автономной реализации согласованного core после approval.** Балл оценивает документ; не подтверждает implementation tests, installation, live helper migration или фактическую экономию времени в реальном ночном проходе.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes / evidence |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Сохранены все цели, шесть проверок и внешний lifecycle? | PASS | §7/AC11, existing protocol v3 сверён; inventory не success metric |
| UX / designer | applicable: artifact/CLI output | Видны все ветви и точный согласуемый эффект? | PASS | DAG/shared refs, text storyboard, no silent truncation, complete witness |
| Tester / validation | applicable | Есть проверка каждого AC, negative/restart cases? | PASS | §11, добавлены missing-root/volatile-source/tampered-witness cases |
| Developer / architect | applicable | Storage/clock/identity/delta/preview совместимы? | PASS | Manifest guard вместо нестабильного общего ETag; state diff boundary |
| Delivery / operations / security | applicable: runtime/storage | Есть no-write observation, безопасный fallback/rollback и scope? | PASS | Pending journal refusal, no-create source, private data absent, F отдельно |

### Post-SPEC Review

- **Статус / stop decision: PASS — можно показывать SPEC для отдельного согласования; EXEC не начат.**
- **Scope reviewed:** этот документ; central owners из §0, canonical expanded template, product-system-design/.NET/testing profiles и local override; источники §2, planned files §16, open questions §14. Git status показывает только новую текущую SPEC.
- **Scope/Evidence pass:** сверены фактические cwd/worktree/HEAD, CLI dispatch/read/search/task/apply/inspect, schema/parser/ETag/receipt, storage load/lock/recovery, availability и repeating model; три predecessor SPEC, relevant integration/read/recovery tests; внешний протокол/helpers прочитаны без запуска. Свежего live CLI/version/source claim не сделано.
- **Contract pass:** A–F сопоставлены с §2.2, APIs и AC1–12. All-parent closure отделён от selection, removed root имеет report policy, clock не включается в contextHash как шум, внешние данные не обещаны delta. Existing apply/создание next step/legacy date fix переиспользуются. Неизменные business contracts отделены от новых opt-in contracts и recoveryRequired.
- **Adversarial risk pass:** рассмотрены diamond graph, terminal ancestor, blocker второй ветви, отсутствующий root, missing baseline, clock-only transition, malformed/shortened witness, changing request text, receipt без актуального состояния, writer после guard, missing CreatedDateTime и прерывание перед Obsidian checkpoint. Существенные контрпримеры и исправления приведены ниже.
- **Role-Based pass:** пять применимых ролей в таблице выше, не заменены одним общим «проверено».
- **Reviewer facility:** использован custom `independent-reviewer`; фактический child sandbox сообщил `danger-full-access`, network enabled, approval=never. Поэтому результат называется **writable-runtime reviewer pass**, а не технически независимым read-only review. Reviewer выполнял только чтение, изменений не делал. Причина fallback — parent runtime override не обеспечил read-only sandbox.
- **Отдельный adversarial fallback основного агента:** независимо от названия reviewer проверены source-default→ETag→guard цепочка, удаление root→delta, saved-diff truncation→effectHash, recovery→read-only обещание и shadow external-writer→outcomeUnknown. Это self-review, не заявленная изоляция. Остаточный риск sandbox isolation раскрыт; implementation correctness ещё проверяется EXEC tests.
- **Fix and re-review:** reviewer перечитал исправленные §6.6/AC8 и вернул targeted PASS без незакрытых BLOCKER/HIGH/MEDIUM. Основной агент повторно проверил связанные §6.3–6.5/§8/§11, удалил прежний общий ETag guard и уточнил schema discovery/context=none. Static checks повторяются на финальной записи документа, не имитируют product tests.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | Scope/delta | Удаление заданного root/ID делало следующий capture невозможным | missingSelection=error/report, отсутствие root не расширяет scope, AC1 | fixed |
| HIGH | Preview integrity | Scalar effectHash не связывал сохранённый сокращённый diff с показанным эффектом | Strict witness + recompute body effectHash, missing derived change test | fixed, reviewer re-read |
| HIGH | Legacy/guard | Общий hash ID/ETag устаревал при untouched JSON без CreatedDateTime | Raw source-manifest guard, explicit ETags, separate volatile-write refusals/AC8 | fixed, reviewer re-read |
| MEDIUM | Recovery/concurrency | Безусловное no-write stale обещание скрывало recovery и bypass race | Разделить old journal/new request/after-guard writer, AC7–9 | fixed, reviewer re-read |
| MEDIUM | Compatibility/search | Strict JSON формулировка и неполный payload могли дать regression/ложный no-match | Сохранить in-memory repair; descendants completeness и sectionNotCaptured | fixed |
| LOW | Docs/commands | Пример preview не показывал причинное contains; solution path был неполным | Исправлены synthetic example и `src/Unlimotion.sln` | fixed |

**Evidence inspected:** две JSON-вставки разобраны `ConvertFrom-Json`; три relative Markdown links существуют; sections 0..20 присутствуют, code fences сбалансированы. Приватных path/user/session markers в документе не найдено. In-memory synthetic walkthrough, без файлов/live tasks: 8/8 assertions для двух parents, shared/terminal ancestors, upstream blocker, parent next step, bounded downstream traversal, ID dedup, affected-context и unrelated edit. Это проверка формальной модели дизайна, не запуск продукта. `git diff --no-index --check -- NUL <spec>` не нашёл whitespace errors; стандартное предупреждение LF→CRLF не является finding.

**Depth checklist:**

- Scope drift/unrelated changes: только эта SPEC; helpers, runtime, task files не изменены.
- Acceptance/scenarios/decisions/objections: матрицы заполнены, six-check intent и external queue preserved.
- Validation: текущие static/design checks отделены от будущих full tests/UI/benchmark.
- Unsupported claims: installed package, atomicity, event completeness, cross-store transaction и live migration не заявлены.
- Regression/edge risks: legacy parser/ETag/dates, all-parent, clock, old CLI forms, retries covered планом.
- Docs/comments/changelog: product docs только в future plan; нынешняя работа не требует changelog нового поведения, которого ещё нет.
- Hidden API/operations changes: observation recoveryRequired и opt-in guard limitations явно записаны.
- Manual-review challenge: наиболее вероятное замечание — «посторонняя legacy задача блокирует каждое согласование»; исправлено source-manifest witness и отдельной проверкой untouched legacy. Вопрос «когда helper реально перестанет читать JSON» отвечает этап F, не core completion.

**No-findings justification:** незакрытых findings текущей SPEC-фазы нет после указанных исправлений/re-review; A–F имеют выбранный контракт, negative tests и границы evidence. Размеры/скорость и UI/runtime корректность остаются обязанностью EXEC, для них не выдан PASS реализации. Needs human: только отдельное подтверждение core SPEC; отдельное поручение для F/delivery. Неразрешённых продуктовых выборов, маскируемых approval, нет.

### Post-EXEC Review

EXEC разрешён 2026-10-04. Два reviewer-прохода проверили части реализации, которые не были написаны соответствующим reviewer: snapshot/search/storage и preview/receipt/guard. Техническая среда допускает запись; проверяющие не изменяли файлы и не запускали тесты, это не изолированный read-only runtime. Старый reviewer SPEC недоступен для продолжения из-за ограничения числа agent threads; использованы disjoint-author проходы и отдельный adversarial self-review основного агента.

Закрыты HIGH legacy-null Description и MED внутренняя полнота closure/contextHash, раскрытие типов page schema, output при источнике в корне диска. Добавлен opt-in observed source guard: сравнение displaced bytes, проверка полного expected manifest перед commit, условный rollback с сохранением внешних изменений. Persisted recovery contract не меняется; crash вместе с bypass writer остаётся границей, требующей read-back.

Поздний adversarial review закрыл source-alias race: physical root закрепляется до чтения request, поэтому reconciliation и receipt shortcut используют тот же источник, что и witness. Два детерминированных теста меняют junction через stdin boundary; оба прошли. AC7 усилен сравнением bytes и LastWriteTimeUtc task/receipt/journal; AC12 — независимым oracle всех 1260 canonical edges, каждого contextId и relation array. Общий suite обнаружил cold-start schema initialization и пропущенное отображение новых error kinds в CLI; эти находки учитываются отдельно от раннего static PASS.

Совместимость с параллельными ветками проверена чтением, без merge. При последующей интеграции status-recovery #314 необходимо сохранить его reload-path mapping и generation/revision checks рядом с отдельным observation-path; текущий recovering `ReloadTaskAsync` нельзя объявлять no-recovery read. Local revision не заменяет durable snapshot/manifest, flush-before-reload не добавляется. В будущей diary #285 create-equivalence должна учитывать default `IsGoal=false` и пустой `AreaIds`, когда эти поля появятся в domain; в данной базе этих полей нет. Git HEAD history не заменяет guard рабочих bytes.

**Итог EXEC:** core P0/P1 реализован; self-review и disjoint-author re-review не оставили открытых BLOCKER/HIGH/MEDIUM в проверенном product scope. Runtime findings холодных schemas и error mapping исправлены; четыре прежних failures, полный machine-diff replay и workspace-сценарий прошли в свежей полной suite. После shared fixture fixes из main окончательно подтверждены **main 1217/1217 и Headless 51/51**, без пропусков и без изменения source hash во время проверки (§11.4). Ранний результат 1212/1216 сохранён как история найденных дефектов, а не текущий test gate. Общий solution build остаётся непройденным, его Debian Debug defect воспроизведён на main, узкий upstream candidate проверен отдельно. Полный solution/CI/release PASS и live migration не заявлены. Два synthetic benchmark прохода дали 19,2× и 20,4× при полном graph coverage; это не live эффективность.

**Остаточные риски:** writable-runtime review вместо технически изолированного reviewer; обход cooperative lock и crash-recovery не дают универсальной атомарности; full graph guard консервативно отказывает при постороннем byte drift; большие closures ограничены явными лимитами. Эти границы отражены в README/skill и failure tests. Изменений пользовательских задач, внешних helpers, установленного CLI и публикации нет.

## Approval

Получена отдельная фраза пользователя **«Спеку подтверждаю»** 2026-10-04. Подтверждение относится к core P0/P1; vault migration, живые записи, push/PR/release требуют соответствующего отдельного поручения.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC / preflight | Отдельный clean detached worktree подтверждён; только проектирование | HEAD 49bc04e, все source specs доступны | Source inspection | Делегированное поручение на SPEC | Эта SPEC |
| SPEC / AS-IS | Existing apply не проектировать заново; выделены A–F, recovery side effect и clock delta | Код CLI/storage/domain, протокол v3/helpers прочитаны; live tasks не читались | Concrete contracts | EXEC не разрешён | Эта SPEC |
| SPEC / draft | Explicit snapshots/state diff, DAG, opt-in preview guard, E deferred, F separate | §6–18, synthetic AC plan | Review и исправления | Нет нового решения | Эта SPEC |
| SPEC / review corrections | Закрыты removed-root, shortened-preview, volatile-ETag guard, recovery/bypass и search completeness | Reviewer targeted PASS, design walkthrough 8/8, JSON/links/scope checks; sandbox read-only не обеспечен, выполнен fallback | Показать review-ready SPEC | Approval ещё нет | Эта SPEC |
| SPEC / complete | Документ готов к согласованию, current-phase quality gates PASS | Реализация, product tests и external migration не выполнены и не заявлены | Ждать отдельное «Спеку подтверждаю» для core | Нет | Эта SPEC |
| EXEC / start | Exact approval получен; core P0/P1 реализуется в отдельной ветке feat/cli-night-context от 49bc04e4 | Upstream status recovery ещё не в локальном main; его reload/generation контракты сохраняются при дальнейшей интеграции | Observation, snapshot/delta, preview/search, tests | «Спеку подтверждаю» | Core files §16; эта SPEC |
| EXEC / local result | Core P0/P1 реализован; no-recovery observation, snapshot/delta/search, exact preview/guard, docs и synthetic workflow проверены | CLI/test builds успешны; full suite 1212/1216, четыре исправления подтверждены affected rechecks 175/175; benchmark 19,2×, graph coverage без пропусков; solution build CS1061 остаётся ограничением | Передать локальный результат и evidence; delivery/F требуют отдельного поручения | Исходное approval core | §11.3, README/skill, logs/TRX, replay и benchmark artifacts |
| EXEC / final full validation, 2026-10-05 | Сохранён обязательный полный gate; four shared fixtures сверены с main5a780b2e, narrow settings fix передан для повторного использования | Main 1217/1217, Headless 51/51, source SHA-256 8155caf3… неизменен; four prior failures и workspace Passed; common Debian Debug blocker и narrow candidate воспроизведены отдельно | Полный test-слот освобождён; product delivery/F и реальная integration с #314 вне текущего шага; solution gate остаётся общим blocker | Прежнее approval core, согласованный порядок validation | §11.4, validation-20261004/completion.json, TRX, raw benchmark/replay |

## 21. CLI/status compatibility acceptance — 2026-10-06

Локальная интеграция выполняется в отдельном `cli-status-acceptance` от `635c941eaae8f02f8d8c3ff21b88f965d0fdef58`; исходный `6102` не изменяется. Основание — прежнее exact approval core P0/P1 и отдельное поручение координатора на исполнение совместимости в том же scope. Источник CLI delta: архив `cli-night-context-49bc04e4-8155caf3.zip` (preimages `49bc04e4`), не dirty workspace #2. Предыдущие full-suite результаты относятся к прежнему source hash и не доказывают совместимость новой интеграции.

До кода фиксируется контракт: после verified observation ID→physical alias публикуется в обе карты `_taskFilePaths` и `_reloadTaskFilePaths`, включая замену старого alias после rename. Это не меняет raw manifest на local revision/epoch и не добавляет flush перед Reload. Default Reload сохраняет штатное recovery; strict observation pending committed/uncommitted journal отказывает без изменения task/receipt/journal bytes и mtime. Реальная команда UI Reload должна затем прочитать соответствующий восстановленный task и обновить именно его карточку.

Acceptance: RED→GREEN `cold capture → corrupt/empty → forced Load → Reload` без Save, тот же сценарий после `known old alias → rename → capture`; strict journal→UI Reload для committed/uncommitted. Shared fixtures из HEAD (FileStorageTaskStatusTests, MainControlTreeCommandsUiTests, ServerStorageCrudRealtimeContract, TestHelpers, SettingsViewModelTests) сохраняются, перенос архивных целых файлов поверх них запрещён. Новые combined tests отделены от этих fixtures.

Workspace own-card acceptance: предварительно анализируется commit `079263737c1034850544c14172ae7495eb0ead3e` (parent `2b0cfeec`). Перед широкими изменениями координатору сообщаются пересечения и план disposable union, затем выполняется синтетический combined check `apply/rollback → own card A при active B`. Непроверенный overlay 91 файла и перенос двух исключённых dirty CLI изменений недопустимы.

Validation этого этапа ограничена targeted unit/headless, serial builds с `-m:1 -p:UseSharedCompilation=false`. Full/native отложены до отдельного резервирования; no push/commit/install/live tasks. Итоговый handoff содержит точные команды, результаты RED/GREEN, source manifest/hash, HEAD, patch и ограничения.
### 21.1 Status candidate evidence and union merge decisions

В `cli-status-acceptance` архивный CLI delta применён трёхсторонним переносом. Единственный source conflict этого этапа — список интерфейсов FileTaskStorage: сохранён ITaskReloadReader и добавлены partial/observation interfaces. Пять общих fixtures имеют нулевой diff относительно HEAD635. Regression RED: четыре cold/rename × corrupt/empty варианта возвращали Missing вместо Failed. После публикации проверенных aliases в обе карты под `_liveGraphSync`: GREEN 6/6 (включая committed/uncommitted strict observation → реальный menu click Reload). Дополнительно77/77: Observation21, Reload9, Preview12, preview replay1, facade18, recoverable mutation16. Full/native не выполнялись. Первый составной filter не выбрал тестов; итоговые regression запуски выполнены шестью точными class filters. Первая версия UI fixture использовала неподдерживаемый IInvokeProvider; заменена штатным headless mouse click, product code из-за этого не менялся.

Status product source fingerprint (34 изменённых `src/` paths): `d895fd2748d60fc6de1a30c22c0a0bafc8a698b791fc4511e54f55d699b0267e`; exact raw per-path hashes и base HEAD в `artifacts/cli-status-acceptance/status-handoff-manifest.json`. Логи RED/GREEN/regression/TRX находятся рядом. Alias mapping не публикует live graph и не выдаёт process-local revision за durable manifest.

Disposable workspace union основан на git-archive0792637, parent2b0cfeec. CLI production paths переносятся отдельно; MainControl, TaskPresentationControl, TaskCardView и workspace routing не импортируются из635. Production status support: FileTaskStorage/TaskReloadResult, ITaskStorage, TaskItemViewModel, FileStorage, UnifiedTaskStorage, ServerStorage, ровно8 status resource keys на язык. Test support: factory override в MainWindowViewModelFixture (прежний ctor делегирует новому; override задаёт реальный facade/storage), отдельный CliWorkspaceOwnCardIntegrationTests.

Конфликты решены явно: (1) TaskItemViewModel autosave сохраняет workspace `_editorPersistenceGate`, внутри добавлены missing/reloading/unknown guards; (2) seal/drain сохраняет status pending write producers и проверку unresolved editor revisions; (3) editor flush сохраняет workspace цикл сброса поздних полей и raw TaskItem Update, в каждом проходе проверяется missing. (4) UnifiedTaskStorage raw Update сохраняет lookup/create и UI-context publication из workspace, epoch advance и cache update выполняются под reloadEpochSync. (5) ServerStorage реализует одновременно classification capability и Reload reader. Resx переносит только8 status keys. Отдельный CLI service конфликт planned dates сохранён как guarded PreviewStale с точной диагностикой и legacy unchanged-date validation. Текстовое auto-merge без этих проверок не считалось корректной интеграцией.

Union acceptance ограничен rendered own-card A/scoped route при active B, через реальную A.ReloadTaskCommand/facade после guarded apply и rollback. Это не полная совместимость всех workspace/status UI menu/error flows и не full union-ready gate. Текущий этап не переносит два исключённых dirty CLI изменения workspace #2.
### 21.2 Superseding workspace scope and SPEC-delta proposal — diagnostic only

По уточнённому поручению координатора workspace Reload/error и Git history принадлежат отдельному SPEC-delta, ожидающему «Спеку подтверждаю». Disposable union079 — только diagnostic probe, не утверждённый implementation candidate и не acceptance нового UI-контракта. Production union после обнаружения HIGH не менялся; исходный status635 source сохранён. Нового External Missing поведения не добавлено.

Уточнённый ownA/activeB probe: настоящие две TaskCardView создаются workspace navigation; A выбирается по RouteTaskItem.Id, действия/title ограничены её subtree. В существующий actions flyout A добавлен **test-only** MenuItem с Command=A.CardContext.Task.ReloadTaskCommand; реальный headless pointer нажимает пункт в popup TopLevel. Это adapter для будущего стыка: shipped079 не содержит Reload menu и его готовая UI-интеграция не доказана. После guarded apply либо rollback проверены A title и неизменные B text/description/status, selection/caret, scroll, editable revision/dirty flag, selected pane/tab и history cursor/entries, bytes/mtime. Уточнённый probe PASS2/2 (`union-own-card-pointer.log`), старый direct-command PASS2/2 не засчитывается за уточнённый контракт.

Подтверждённые HIGH переданы владельцу workspace:

- Missing + dirty draft + Flush: Drain возвращается без записи, но Flush требует отсутствия dirty fields и повторяет цикл. Bounded RED: prompt rejection не получен за1s при сохранённом draft и0 Update. Probe выполнялся в отдельном dotnet process с hard timeout30s; test-only reflection release после замера позволил завершить loop без зависшего host. Это не production fix.
- OutcomeUnknown + последующая dirty edit + Flush: запрещённый SaveItemCommand путь обходится через Flush→Drain; bounded RED получил UpdateCount=1 вместо0. Draft/readback guard нужен в owner SPEC, в текущей production-копии не исправлен.
- Disposal после ожидания `_editorPersistenceGate`: SaveItemCommand проверяет missing/reloading/readback, но не recheck disposed/accepting после await. Это STATIC finding без отдельного RED; гарантия недопустимости поздней записи должна быть уточнена и проверена владельцем.

**Предложение для owner SPEC (не реализовано и не утверждено):** определить единый отказ всех editor persistence entrypoints при Missing/OutcomeUnknown/disposed после await-boundary, сохранить копируемый dirty draft, не очищать revisions фиктивным успехом и не блокировать UI бесконечным flush; навигация должна получить явный отказ до смены scope. Reload по scoped CardContext, error presentation и Git-history binding рассматривать в том же owner delta; запретить global-current-task oracle для владения карточкой. Acceptance: bounded Missing/Unknown flush zero-write + retained text, disposal after gate, rendered ownA/activeB menu actions и lifecycle cleanup. Нужны separate exact approval и затем реальные implementation gates.

Дополнительные диагностические результаты: TaskItemViewModelStatusCommandTests35/35 PASS; WorkspaceTaskSaveGuardUiTests5/7 PASS, два OrdinaryTransition_RechecksLateEditAndRejectsItsFailedSave(False/True) упали на transition.IsCompleted==True в строке379 — причина ещё не установлена, не объявляется baseline/unrelated. WorkspaceTaskProjectionUiTests1/1 PASS. Missing/Unknown probes RED2/2 ожидаемо фиксируют незакрытые HIGH. Общий union PASS не заявлен.

Последний Projection targeted запуск использует обновлённый Invoke-TargetedAcceptance.ps1: production/test fingerprints и DLL SHA-256 записаны before-build, before-tests, after-tests; production `755afff588799df625b8806e6ccf2a0e11e3c6558e0d1c9b2b4b99d036a39e61` и tests `70ca41936bd35d2531ad2597401141c9a8a76525b9e5c1bcb50a92d9b6601fd5` до/после совпадают. Ранние DLL hashes — только after-runs, без ретроактивных pre-run claims. Исторические logs/TRX сохранены; full/native pending, новых runs после этого targeted этапа нет.

## 22. Приёмка core и доставка в main, 2026-10-06

Прямое решение пользователя после просмотра воспроизводимого демо: **«Хорошо, мне нравится, вливай»**. Это приёмка core P0/P1 с проверенным CLI/status стыком и разрешение создать ветку/коммит, push/PR и merge после обязательных проверок. Установка, package release, live task-space и workspace VM/UI SPEC-delta не входят в это решение. Прежние запреты Git delivery в журнале описывают границы соответствующих прошлых этапов.

Кандидат создан в отдельном managed worktree на `main@68197565816295c7c766877c39cc40a72b3aeeb4` (tree `cb4a9a95f2a0ba70f3ae4e41283c8c58b9e1c813`). Перенесён status-final.patch SHA-256 `858d6ddb254ef85a1375d833d26e32b3ef3e71090ae8a579bf82ab52ec40f47a`, manifest 37 paths `75256d77ebf7896686f1a1f5f0cf3c3452898924f8c16c9d993b0806e06f23bf`. Изменение этой SPEC поверх патча фиксирует только новое решение и delivery evidence; имена локальной машины в трёх исторических TRX заменены маской.

Деревья src/.github/scripts/.agents базы681 равны проверенной635. Все37 переносимых файлов совпали с исходным tested worktree после нормализации LF; raw SHA отличаются из-за Git line-ending conversion. Это source-equivalence evidence, а не новый test run. 83 прежних targeted PASS сохраняют свою историческую привязку; код и пять shared fixtures сохранены. Workspace диагностические RED и production VM/UI probe отсутствуют в delivery diff.

Перед merge требуется полный Main/Headless CI на опубликованном CLI candidate SHA, self-review и закрытие обязательных findings. Этот gate выполняется в GitHub CI; локальный full/native slot данным этапом не занимается. UI-facing alias/recovery контракт имеет Headless menu-click coverage, включённое в Main suite. Новый UI layout не создаётся. Физическое видео не записывается в этом CLI delivery; next-best evidence — изолированное пользовательское CLI demo, deterministic RED→GREEN и Headless pointer/menu tests. Результаты CI, точный head и merge подтверждаются в PR и delivery-handoff; прежние отдельные core/status suites не объявляются полным тестированием нового объединения.
