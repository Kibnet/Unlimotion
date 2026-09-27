# Самодокументируемый Unlimotion CLI для работы агента с задачами

## 0. Метаданные

- Тип: `delivery-task`; профиль `product-system-design`, контекст проверки `testing-dotnet`, правила `testing-baseline`; для обновления личного скилла — `skill-creator`.
- Форма: expanded. Меняется публичный CLI-контракт в нескольких командах и инструкция установленного скилла; short SPEC неприменима.
- Владелец: `Unlimotion.Cli`; существующие бизнес-правила задач остаются у `Unlimotion.TaskTreeManager`.
- Масштаб: large из-за нового read API, справки, контракта `apply` и совместимости ответов.
- Целевое семейство / behavior baseline: GPT-6 Astra из central stack для оценки агентского workflow; продуктовый CLI не зависит от модели.
- Поверхность: Work / Codex с установленным .NET tool и локальным файловым task space.
- Effective runtime: фактическая модель/effort текущего сеанса не влияют на CLI-контракт; при будущем behavioral smoke скилла зафиксировать одинаковые модель, effort, поверхность и sandbox до/после.
- Eval baseline / evidence: установленный `unlimotion.cli` 1.31.1; checkout `ccd25fbe` (`v1.31.1-27-gccd25fbe` на момент аудита); `unlimotion-cli --help`, `apply --help`, `--version`, `status --format json`, расширенный `task`; исходники и `UnlimotionCliIntegrationTests`. Живые задачи читались только для проверки формы ответа.
- Целевой релиз / ветка: не задан; публикация, обновление глобального инструмента и PR не входят в эту SPEC.
- Ограничения: до точной фразы `Спеку подтверждаю` менять только этот файл. Не затрагивать уже изменённые UI/Feed файлы рабочего дерева.
- Связанные материалы: `src/Unlimotion.Cli/README.md`, `specs/2026-09-17-night-agent-cli-application-contract.md`, `specs/2026-09-18-cli-release-version-source.md`, `C:\Users\Kibnet\.codex\skills\unlimotion-cli\SKILL.md` и `references/apply-v1.31.1.md`.

## 1. Overview / Цель

Пользователь попросил проверить, достаточно ли `unlimotion-cli` сообщает агенту о себе и командах, и улучшить CLI так, чтобы скилл был полноценным и очевидным. Последующий аудит выявил недостаточную обнаруживаемость уже существующих операций и пробелы в чтении. Эта SPEC фиксирует связанный результат: агент на установленном бинарнике узнаёт точный контракт, находит задачу, проверяет выбранное пространство, готовит изменение, устанавливает исход записи и сверяет сохранённое состояние без чтения исходников или прямого чтения task JSON.

Success means:

1. Версия, команды, параметры, семантика `apply` и источник task space доступны через сам CLI в text/JSON там, где это полезно агенту.
2. Любая локальная задача, включая недоступную для старта и завершённую, находится по названию/ID через ограниченный постраничный read API.
3. Расширенный `task` показывает повторитель и действительно отображает запрошенные секции в текстовом режиме; legacy JSON без `--include` сохраняется.
4. После потерянного ответа `apply` есть read-only способ получить проверяемые признаки исхода по исходному request без повторной записи.
5. Скилл использует возможности установленной версии и сохраняет безопасный путь для 1.31.1.

Итог: изменения CLI, интеграционные/контрактные тесты, упакованная справка/схема, README и условно обновлённый личный скилл. Остановка: не делать запись в живое пространство задач, не обновлять глобальную установку и не объявлять новую версию доступной до отдельной установки.

## 2. Текущее состояние (AS-IS)

- Глобально установлен `unlimotion.cli` 1.31.1. `--version` возвращает `invalidArguments`; `apply --help` и `task --help` печатают общую `Usage`. В `CliOptions.Parse` любое `--help` обрабатывается до выбора команды.
- `PrintUsage` перечисляет синтаксис, но не объясняет значения, defaults, ошибки или request JSON. В строках почти везде указан `--tasks <path>` без скобок, хотя параметр необязателен. Общий список в `src/Unlimotion.Cli/README.md` пропускает `apply` и `unlocked --root`.
- `apply` поддерживает `setField`, `clearField`, операции с критериями и связями, `createTask`, `setStatus`; применяет ETag, dry-run, receipt и postcondition reconciliation. Скилл отсылает к `references/apply-v1.31.1.md`, но эта справка раскрывает лишь пример и поля `setField`, хотя сам скилл направляет к `apply` для связей.
- `candidates` по умолчанию фильтрует `Prepared`/`CanStart`, требует `--limit 1..100` и возвращает массив без признака следующей страницы. `unlocked` возвращает все `CanStart` задачи. Поиск произвольной задачи по названию отсутствует.
- На момент утверждения SPEC `status --format json` возвращал агрегаты без пути/способа выбора пространства. В исходном checkout `TaskDirectoryResolver` отдавал приоритет явному `--tasks`, иначе использовал persisted desktop settings; поддержка `UNLIMOTION_TASKS` ещё не была в этой ветке. При переносе на свежий `origin/main` она уже была включена и должна сохраняться.
- `task --include ... --format json` отдаёт `etag`, `task`, запрошенные детали/связи/критерии/историю/execution. Установленный 1.31.1 не показывает `Repeater`; модель `TaskItem` его хранит. `task --include ... --format text` молча показывает только анализ доступности.
- `apply` хранит receipt по `applicationId`, но отдельного read-only вызова для inspection нет. `apply createTask` уже поддерживает задаваемый caller ID и восстановление; обычный `create` генерирует ID и при потерянном ответе не даёт того же уровня проверки. Текущая reconciliation сравнивает операции по отдельности: допустимый request `createTask(title=A)` + `setField(title=B)` для той же новой задачи после успешного commit без receipt ошибочно не распознаётся как полностью применённый, потому что проверка `createTask` ожидает промежуточный title A.
- JSON-ошибки уже содержат `success=false` и `error.kind/message`; их контракт нужно сохранить.

## 3. Проблема

CLI является рабочим интерфейсом записи, но для нового агента не является достаточным источником сведений о собственном контракте и текущем состоянии пространства. Это вынуждает копировать версионные правила в скилл, читать код или делать неполные выводы после ограниченной выборки и потерянного ответа.

## 4. Цели дизайна

- Один публичный источник точного контракта установленного бинарника; скилл хранит порядок принятия решений и полномочия, а не копию полного API.
- Ограниченные, детерминированные read-команды без `claim` и без прямого доступа к файлам задач.
- Явное различение факта применения, наличия желаемого состояния и отсутствия достаточного доказательства.
- Неизменность бизнес-правил `claim`, `apply`, статусов, связей, ETag и защищённого блока `AgentExecution`.
- Обратная совместимость существующих команд и legacy JSON; новые поля только в расширенном снимке, новые ответы — у новых команд.
- Проверяемость по реальному CLI entry point, пакету инструмента и агентским сценариям.

## 5. Non-Goals

- Серверный task space, синхронизация, изменение desktop UI, task JSON schema или бизнес-правил доступности.
- Общий полнотекстовый индекс описаний, fuzzy/semantic search, команды natural language и управление предложениями/approval внутри CLI.
- Автоматический повтор `apply` после неизвестного исхода. Inspection не выполняет применение.
- Автоматическое обновление/публикация NuGet, релиз, PR и подмена установленной 1.31.1 локальной сборкой.
- Переписывание всего парсера ради одной справки; допустим узкий слой метаданных с контрактными тестами против фактического парсера.
- Обещание snapshot-consistent пагинации между отдельными вызовами при параллельной записи.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент | Ответственность |
| --- | --- |
| `Unlimotion.Cli/Program.cs` и при необходимости новые CLI-классы | dispatch, справка, новые read-команды, версии, ответы и exit codes |
| Упакованный контракт `apply` в `Unlimotion.Cli` | JSON Schema v1, примеры и описания семантических правил установленной сборки |
| `TaskDirectoryResolver` | единый результат разрешения пути плюс происхождение, без смены приоритета `--tasks` |
| `TaskSnapshotOutput` / DTO повторителя | полная читаемая проекция повторителя и текстовые секции |
| `TaskApplicationCommandService` / receipt store | переиспользуемая read-only оценка receipt, ETag и postconditions для inspection; правила записи неизменны |
| `UnlimotionCliIntegrationTests` и package smoke | поведение через процесс CLI, негативные случаи и соответствие пакетной версии справке |
| README и личный `unlimotion-cli` skill | сценарии использования; старый контракт остаётся доступен только для старой версии |

### 6.2 Публичный CLI-контракт

**Версия и помощь.** `unlimotion-cli --version` выдаёт человекочитаемую версию без чтения task settings. `unlimotion-cli version --format json` возвращает `packageVersion`, `applicationVersion` и признак локальной/пакетной сборки; для установленного пакета `packageVersion` совпадает с NuGet metadata. Локальная сборка без явного `PackageVersion` не выдаётся за опубликованный релиз. `--help` без команды сохраняет общий обзор. `help <command> [--format text|json]` и `<command> --help` показывают именно эту команду; для `execution` поддерживается подкоманда. Справка содержит цель, required/optional параметры, defaults, допустимые значения, повторяемость, read/write effect, exit/error kinds и короткий рабочий пример. JSON-форма предназначена для интроспекции, а не для выполнения команды. `--tasks` в примерах обозначен как optional.

**Контракт `apply`.** `unlimotion-cli apply schema --format json` выдаёт упакованную JSON Schema request v1 с `$schema`, `schemaVersion`, ограничениями структуры и `oneOf` для каждого `kind`. Схема обязана покрывать `setField`, `clearField`, `add/replace/remove/setCriterionSatisfied`, `add/removeRelation`, `createTask`, `setStatus`; отображать допустимые имена полей и направленные `contains`/`blocks`. `unlimotion-cli apply example <set-field|add-relation|create-task> --format json` выдаёт синтаксически валидный шаблон запроса с явно помеченными **примерными** ID/ETag в допустимом формате, которые нужно заменить перед dry-run. Семантические ограничения, которые JSON Schema не выражает (статусные правила, граф, защищённые маркеры, ETag, receipt, необходимость preview), раскрыты в `help apply` и README. Ресурс поставляется внутри tool package; тесты сравнивают схему/примеры с фактическим parser/service contract, исключая тихий drift. Синтаксис и JSON shape существующего `apply --request` сохраняются; исправляется его ложный отказ при составной/no-op reconciliation без receipt.

**Контекст пространства.** `unlimotion-cli context [--tasks <path>] --format json` возвращает абсолютный разрешённый `tasksPath`, `sourceKind` (`explicitTasks`, `environmentTasks` или `desktopSettings`), `storageKind=file` и версию CLI. На актуальном мейне порядок выбора: `--tasks`, затем непустой `UNLIMOTION_TASKS`, затем desktop settings. Команда использует тот же resolver, что task-команды, и не загружает задачи, но проверяет существование выбранного каталога так же, как обычные команды. Отсутствующее/повреждённое/server-mode settings при выборе settings, а также несуществующий явный или environment-каталог дают структурированный отказ без fallback; успех `context` подтверждает выбор и существование пути, но не валидность графа или права на каждую будущую запись. После `context` агент **передаёт полученный `tasksPath` как явный `--tasks` во всех связанных search/task/dry-run/write/inspect/read-back вызовах**: настройки desktop и environment могут смениться между процессами. Существующие JSON-ответы `status` и мутаций остаются неизменными.

**Поиск/список.** Новая read-only команда:

```text
unlimotion-cli search [--query <text>] [--status <status>] [--root <task-id>]... [--limit <1..100>] [--cursor <token>] [--tasks <path>] [--format text|json]
```

Без фильтров включает все статусы; `--query` ищет подстроку в title или ID через `OrdinalIgnoreCase`, без поиска в description. `--root` ограничивает union корней и их descendants по `ContainsTasks`, включая корни; missing root даёт `notFound`. Default limit — 20. JSON: `{items:[{id,title,status,importance,canStart,canComplete}], totalCount, nextCursor}`. Порядок `(title OrdinalIgnoreCase, title Ordinal, id Ordinal)`; курсор содержит версию формата, fingerprint фильтров **и нормализованного абсолютного пути task space**, а также последний ключ; это не полномочие и не task ID. Курсор от пространства A с `--tasks B`, неверный или несовпадающий по фильтрам даёт `invalidArguments`, пустая страница — `items=[]`, `nextCursor=null`. Для одного вызова `totalCount` точен; при изменении task space между страницами CLI не обещает snapshot, а потребитель дедуплицирует IDs и при необходимости повторяет поиск. Вывод остаётся ограниченным 100 элементами, даже если совпадений больше.

**Снимок задачи.** Расширенный `task --include details --format json` добавляет `details.repeater`: `null` или объект с `type`, `period`, `afterComplete`, `pattern` и типизированными extension fields, если они есть. DTO не меняет хранение и не теряет неизвестные поля при read-back. Текстовый `task --include ...` показывает запрошенные секции и ETag, сохраняя прежний текст без `--include`. Legacy JSON без `--include` остаётся прежним. `observedAt` не добавляется: ранее его убрали ради стабильного snapshot; ETag остаётся токеном наблюдения.

**Проверка исхода `apply`.** `unlimotion-cli apply inspect --request <path|-> [--tasks <path>] --format json` принимает тот же request text и возвращает `didMutate=false`, `applicationId`, `requestHash`, `receiptState` (`matching|conflicting|missing`), `postconditionsMatch` (`all|partial|none|unknown`), `preconditionsMatch`, известные `changedTaskIds`/`createdTaskIds` и `assessment`. Чтение/декодирование file/stdin и SHA-256 от UTF-8 декодированной строки должны использовать **тот же `RequestHash` путь**, что существующий `RunApply`; raw file-byte hash не вводится. Пробелы/переносы остаются значимыми для hash, BOM обрабатывается одинаково в apply/inspect. Inspection и существующая retry-reconciliation используют один evaluator **итоговых postconditions всего упорядоченного request**, а не подсчёт совпавших промежуточных операций. Для `createTask` evaluator учитывает последующие допустимые операции с созданной задачей: изменение поля/статуса, отношения и другие разрешённые target-комбинации; нецелевые поля и последующие внешние изменения не принимаются за результат request. Если итог нельзя доказать, вернуть `needsReconciliation`/`unknown`, но не ложный `receiptMatched` или `desiredStatePresent`. Исправление существующей ветки retry без receipt входит в scope и защищается отдельной regression fixture.

| Assessment | Доказательство / допустимый вывод |
| --- | --- |
| `receiptMatched` | Есть receipt с тем же hash: прежняя application подтверждена; текущее состояние показывается отдельно и может дрейфовать. |
| `desiredStatePresent` | Receipt отсутствует, все postconditions выполнены: желаемое состояние наблюдается; история применения не доказана. |
| `readyForPreview` | Receipt отсутствует, preconditions совпадают и планирование возможно, даже если отдельная операция уже no-op; желаемое состояние ещё не выполнено. Можно выполнить dry-run, но история прежней попытки не доказана. |
| `needsReconciliation` | Hash конфликтует, состояние смешано, ETag изменён или доказательств недостаточно; запись без ручной сверки запрещена. |

Если receipt отсутствует, а все ETag/status preconditions исходного request совпадают, ранее совпадавшая отдельная операция может быть законным no-op. В этом случае `readyForPreview` определяется возможностью пройти обычное планирование, а не ненулевым счётчиком совпавших операций; существующий `apply` после успешного preview не должен отказывать только из-за такого no-op. Если preconditions уже не совпадают, общий evaluator итогового состояния определяет возможность reconciliation. Inspection не создаёт receipt и не выполняет новое application. Обычное восстановление незавершённого storage journal при открытии каталога остаётся обязанностью `FileTaskStorage` и не считается новой командной мутацией. Неправильный request/пространство даёт структурированную ошибку; `outcomeUnknown` не превращается в ложный success. Для `create` с риском потери ID справка рекомендует `apply createTask` с заранее выбранным ID и `dry-run`; простая команда `create` сохраняется.

**Скилл.** После локальной CLI/package проверки обновить личный skill через отдельный candidate и `quick_validate.py`. В entrypoint оставить выбор установленной версии/пространства, границы полномочий, preview/read-back и recovery. Для новой версии направлять к `version`, `context`, `help`, `schema`, `search`, `inspect`; **всю цепочку одной операции закреплять явным `--tasks` из `context`**, включая повторное чтение. Для 1.31.1 сохранять `references/apply-v1.31.1.md` и прямое правило неизвестного исхода. Дополнить старую reference форматом relation operation или явно запретить её без matching source; предпочтительно дополнить, чтобы она соответствовала существующей инструкции скилла. Не заявлять поддержку `UNLIMOTION_TASKS` по дате: только по подтверждённому контракту конкретного бинарника. До активации сверить drift глобального skill path и обеспечить откат. Без глобальной установки новой версии skill не должен переключаться на её контракт.

Visual planning artifact и UI video evidence: не применимо — меняются CLI stdout/JSON и skill, desktop UI не меняется.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | AC |
| --- | --- | --- | --- | --- |
| Первый контакт | Агент запускает `--version`, `help apply`, `apply schema/example` на установленном пакете | Версия и полный применимый контракт получены без исходников | package tool smoke + contract tests | AC1, AC2 |
| Выбор пространства | Агент запускает `context`, затем связанные команды с явным `--tasks` | Видит точный путь и источник; смена desktop settings не перенаправляет запись | integration tests | AC3, AC7 |
| Поиск закрытой/недоступной задачи | Агент ищет фразу названия, затем переходит по страницам | Находит ID вне `candidates`, знает, что страницы закончились | integration tests >100 tasks | AC4 |
| Проверка изменения | Агент читает `task --include details,relations,criteria --format json/text` | Видит повторитель и запрошенные секции; старый JSON не сломан | integration/compat tests | AC5 |
| Потерян ответ `apply` | Агент передаёт исходный request в `apply inspect` | Получает evidence class без записи и не дублирует создание | receipt-gap/mixed-state tests | AC6 |
| Агент по скиллу | Агенту поручено найти задачу и изменить описание/связь | Он выбирает контракт установленного CLI, preview, запись один раз и read-back; 1.31.1 работает по прежнему пути | before/after behavioral smoke | AC7 |

### 6.4 State / Interaction Matrix

| Состояние | Триггер | Результат | Негативный/конкурентный случай |
| --- | --- | --- | --- |
| Settings / environment / explicit path | `context`, затем явный `--tasks` | выбранный путь и sourceKind закреплены для операции | смена settings или environment между процессами не меняет выбранный каталог; отсутствующий выбранный path: отказ без fallback |
| Любой валидный граф | `search` | ограниченная страница всех совпадений | пусто: empty items; missing root: notFound; malformed cursor: invalidArguments |
| Граф изменился между страницами | `search --cursor` | новая страница по keyset текущего снимка | snapshot consistency не обещается; дедупликация по ID у caller |
| Task с/без repeater | `task --include details` | полный repeater/null | неизвестные extension fields видны и не теряются |
| Receipt совпал, состояние позже изменилось | `apply inspect` | `receiptMatched`, отдельный postconditions/drift сигнал | старый intent не переигрывается |
| Receipt отсутствует, desired state уже есть | `apply inspect`, включая `createTask` + последующие операции | `desiredStatePresent` по итоговому составному состоянию без исторического claim | смешанное состояние: `needsReconciliation` |
| Receipt отсутствует, ETag начальный | `apply inspect` | `readyForPreview` | не запускает `apply` автоматически |

### 6.5 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Объём | user + agent | один связный результат по пяти обнаруженным пробелам CLI и условному обновлению skill | 0.9 | большой diff | Нет: запрос «Оформи спеку» следует за согласованным перечнем |
| Версионирование помощи | agent | версия из package build metadata, JSON/help/schema из установленного бинарника | 0.9 | локальный build выдаст ложный релиз | Нет |
| Совместимость | agent | новые команды; legacy `status`, `apply`, `task` без include без изменения JSON shape | 0.95 | строгие потребители JSON | Нет |
| Поиск | agent | bounded `search`, title/ID substring, keyset cursor без snapshot guarantee | 0.85 | изменения между страницами | Нет |
| Source disclosure | agent | отдельный `context` плюс явный `--tasks` на всю цепочку; path не добавлять во все ответы | 0.9 | settings drift / лишнее раскрытие пути | Нет |
| Recovery | agent | read-only `apply inspect` с evidence states, не автоматический retry | 0.85 | ложная уверенность при absent receipt | Нет |
| Skill activation | agent | новый workflow только после runtime capability check; fallback 1.31.1 | 0.95 | skill обгонит установленный CLI | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| CLI version | release tag → `PackageVersion` в build/pack | вывести package version из метаданных сборки | нет task migration | pack/install smoke, nuspec/version equality |
| Task source | `TaskDirectoryResolver` | добавить provenance DTO для `context` | тот же приоритет explicit `--tasks`; no fallback; agent pinning пути | resolver/CLI/settings-switch tests |
| Task graph | `FileTaskStorage`, `TaskAvailabilityAnalyzer` | только новый поиск и read projection | task files не менять | fixture tests, unchanged file hashes |
| `apply` request | `TaskApplicationJson`, service | упакованная schema/help/examples | request v1 и старый apply output неизменны | schema/example/service contract tests |
| Receipt/reconciliation | `.unlimotion.applies/v1`, `TaskApplicationCommandService` | read-only inspection и корректная сверка составного итогового состояния при retry | receipt format/запись не менять; исправить ложный отказ без receipt | receipt-gap/composed/mixed-state tests |
| Skill | глобальный `unlimotion-cli/SKILL.md` | conditional routing по реальному CLI | 1.31.1 fallback сохраняется; активный путь меняется только после candidate review | validator + same-runtime behavioral smoke |

## 7. Бизнес-правила / инварианты

- Help/version/schema/example не требуют существующего task space и не читают настройки пользователя.
- `context`, `search`, `task`, `apply inspect` не вызывают `claim` и не выполняют новую business mutation; `FileTaskStorage` может восстановить незавершённую прежнюю journal-транзакцию.
- `search` не принимает результат усечения за полный список: `nextCursor` обязателен при остатке; никакой `limit >100`.
- У `apply inspect` receipt подтверждает только конкретный `applicationId + requestHash`; совпадение желаемого состояния без receipt не доказывает историческое применение. `didMutate=false` означает отсутствие новой application mutation. Составной request оценивается по конечному эффекту в порядке операций, а не по промежуточным значениям `createTask`.
- Совпавшая отдельная операция при исходном ETag не считается доказательством частичного применения: она может быть no-op. Только итоговая проверка request и preconditions определяет assessment и допустимость обычного apply.
- Ошибки остаются JSON с `success=false` и `error.kind/message` при `--format json`; exit codes 0/1/2 сохраняют значение.
- `--tasks` всегда имеет приоритет перед `UNLIMOTION_TASKS`, а непустая переменная — перед desktop settings; server-mode settings не превращается в локальный fallback. `context` сам по себе не закрепляет будущий процесс: caller передаёт разрешённый path явным `--tasks` до завершения read-back.
- Скилл не считает выполнение task mutation выполнением реальной работы по задаче.

## 8. Точки интеграции и триггеры

- `CliOptions.Parse`/dispatch: общая и командная help, version, context, search, `apply schema/example/inspect`.
- `TaskDirectoryResolver`: вернуть путь и происхождение одним проходом; старый `Resolve` сохранить или адаптировать без изменения поведения.
- `RunReadCommand`/analyzer: поиск из одного загруженного снимка, без N отдельных чтений.
- `TaskSnapshotOutput` и text renderer: repeater и `--include` секции.
- `RunApply`/receipt store/service: вынести общий read-only assessment, сохранить текущую атомарность записи.
- Package build: встроить help/schema/examples и проверить их доступность после `dotnet tool install --tool-path`.
- Skill: обновить только после локальной проверки нового бинарника; старый установленный путь остаётся валидным.

## 9. Изменения модели данных / состояния

- Новых полей `TaskItem`, миграции task files и нового persistent index нет.
- Новые DTO команд `context`, `search`, `apply inspect`; `details.repeater` добавляется только в expanded snapshot.
- Schema/help/examples — versioned package resources, не пользовательские данные.
- Receipt v1 остаётся прежним. Cursor эфемерен и не сохраняется в task space.

## 10. Миграция / Rollout / Rollback

- Реализация и тесты выполняются в checkout, тестовые записи только во временных task directories. Установленную 1.31.1 не заменять в рамках EXEC.
- Новые команды/DTO additive; legacy команды и JSON contract фиксируются characterization tests. Если strict compatibility test не проходит, исправить в рамках SPEC, не объявлять breaking change как мелкую правку.
- Личный skill обновляется через candidate copy, проверку drift активного пути и backup; новая ветка инструкции включается только при подтверждённой версии/команде. При rollback восстановить прежний skill и оставить старую reference.
- Откат CLI — прежний пакет/commit; task data migration отсутствует. Receipt v1 и пользовательские задачи не чистить.
- Глобальная установка, NuGet publish, release и PR требуют отдельного поручения после проверки реализации.

## 11. Тестирование и критерии приёмки

Изменяется публичный контракт: обязателен полный регрессионный TUnit suite из `.github/workflows/nuget-cli.yml` после targeted tests и сборки. UI-тесты не требуются локальным override, потому что desktop UI/state не меняются. Для скилла — before/after behavioral smoke в одинаковой конфигурации с реалистичными, но временными task fixtures. Не запускать write-команды против личного task space. На синтетическом графе порядка 3000 задач измерить время `search` относительно существующих `candidates` и `status` в том же процессе/окружении; рост сверх разумной цены полного чтения/сортировки разобрать до PASS, не назначая произвольный абсолютный лимит.

- **AC1.** Установленный из локального пакета CLI сообщает совпадающую с package metadata версию; help по каждой команде отличается от общего и точно описывает required/default/side effect. Help/version работают без settings.
- **AC2.** `apply schema` и три примера поставляются в package; каждый пример парсится, проходит schema validation и проверяется на temporary graph через dry-run после замены placeholders. Schema охватывает все десять `TaskApplicationOperationKind`, в том числе отрицательные варианты полей/relations.
- **AC3.** `context` показывает resolved absolute path, provenance и версию; на актуальном мейне `--tasks` выигрывает у `UNLIMOTION_TASKS`, а переменная — у desktop settings. Missing/invalid/server settings, когда выбран settings, и несуществующий явный или environment-каталог не переключают каталог и дают структурированный отказ. Если settings или environment переключаются после `context`, все связанные команды с явным `--tasks` остаются в первоначальном пространстве.
- **AC4.** `search` на fixture >100 задач находит Prepared, NotReady, Completed и Archived по title/ID, выдаёт все страницы без дублей в неизменном графе, корректно сообщает empty/missing-root/malformed-or-mismatched-cursor, включая cursor из другого task space.
- **AC5.** Expanded JSON `task` показывает null/полный repeater, включая extension fields; text `--include` показывает запрошенные секции; legacy JSON/text без include сохраняют contract.
- **AC6.** `apply inspect` различает matching receipt, desired state без receipt, ready for preview, mixed/conflicting state и later drift; task/receipt hashes не меняются от inspection. После `createTask(title=A)` + `setField(title=B)` и удаления receipt итоговый B распознаётся как `desiredStatePresent`, а повтор того же `apply` возвращает `alreadyApplied` без новой task mutation; близкое смешанное состояние не принимается. Request с одной уже совпадающей no-op операцией и другой новой операцией при совпадающих preconditions получает `readyForPreview`, проходит dry-run и применяет только требуемое изменение. Hash inspect/apply совпадает для одинаково декодированного request с BOM/whitespace fixture и различается при смене текста. Journal recovery отдельно маркируется в evidence.
- **AC7.** Обновлённый skill на одинаковых representative запросах выбирает новую help/search/inspect ветку только с новым CLI и сохраняет 1.31.1 fallback; не делает claim при чтении, закрепляет явный `--tasks` на всю цепочку и не повторяет мутацию после неизвестного исхода без read-back.
- **AC8.** Существующие JSON-контракты `status`, `candidates`, `task` без include, `apply`, `create`, errors и exit codes проходят characterization tests; source/test изменения не касаются посторонних UI/Feed файлов.
- **AC9.** README CLI содержит полный список команд, примеры безопасного `apply createTask` и поиска, предупреждение о пагинации и recovery; пакетный smoke подтверждает доступность справки/схемы из установленного в temporary tool-path бинарника.

### Acceptance-to-Test Matrix

| AC | Automated test | Manual / log check | Evidence artifact | If not tested |
| --- | --- | --- | --- | --- |
| AC1 | CLI integration + package metadata contract | `--version`, `help apply` из temporary install | test log + package smoke | обязательное |
| AC2 | schema/examples validation + real dry-run fixtures | JSON output inspection | test log | обязательное |
| AC3 | resolver + CLI integration с environment/explicit приоритетом, settings switch и missing path | три sourceKind и explicit pinning | test log | обязательное |
| AC4 | search pagination/Unicode/filter/cross-space-cursor fixture | representative output | test log | обязательное |
| AC5 | snapshot/text + legacy golden tests | inspect one fixture | test log | обязательное |
| AC6 | receipt-gap, composed create+field/relation, preexisting no-op, BOM/hash, later-drift, mixed-state tests | file hashes before/after | test log | обязательное |
| AC7 | same-runtime before/after skill smoke + `quick_validate.py` | проверить фактический skill diff | smoke transcript | обязательное |
| AC8 | existing CLI tests + full suite | `git diff` scope review | test/build log | обязательное |
| AC9 | package/install smoke + docs contract check | README review | package smoke log | обязательное |

Команды EXEC: сначала `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/UnlimotionCliIntegrationTests/*" --maximum-parallel-tests 1 --output Detailed`; затем `dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj -c Release` и полный `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --maximum-parallel-tests 1 --output Detailed` — тот же runner и полный suite, что в CLI release workflow. По результату preflight проверить, не добавлены ли к EXEC другие затронутые test projects или обязательные CI gates. Для package smoke — `dotnet pack` с явным локальным `PackageVersion`, установка через `--tool-path` во временный каталог, запуск help/version/schema/context на временном task space. Для skill — `quick_validate.py` из `skill-creator`, затем before/after smoke на одинаковом runtime. До долгого full suite сообщить expected duration и путь progress/log evidence по фактическому preflight. После green остановить дополнительные проверки, если нет конкретного остаточного риска.

## 12. Риски и edge cases

- **Schema drift:** статический JSON Schema отстанет от parser/service. Снизить контрактными тестами на все operation kinds, поля, примеры и отрицательные случаи; бизнес-правила явно остаются в help.
- **Пагинация при конкурентной записи:** keyset не обеспечивает единый снимок между вызовами. Вывод не обещает этого; тестирует стабильный граф, skill дедуплицирует ID и повторяет поиск при важных решениях.
- **Межпространственный cursor:** совпадающие фильтры в разных task spaces не означают общую страницу. Fingerprint нормализованного абсолютного пути входит в binding; A→B отклоняется.
- **Смена desktop settings между процессами:** `context` не является precondition для записи. Skill и примеры закрепляют resolved path явным `--tasks` на read/preview/write/read-back; тест имитирует переключение.
- **Hash BOM/whitespace:** inspect и apply используют один путь декодирования/хеширования; иначе receipt ошибочно конфликтует. Проверить file/stdin и форматирующие различия.
- **Ложное подтверждение применения:** desired state может быть достигнуто независимо, receipt может существовать после последующего drift. Assessment различает эти факты; inspect не запускает повтор.
- **Композиция операций:** сравнение каждой операции с промежуточным состоянием даёт ложный отказ после успешного commit. Сверять конечные postconditions всего request; добавить fixture с созданием и последующими изменениями той же задачи, включая отрицательное смешанное состояние.
- **Путь task space:** path может раскрывать локальное имя пользователя. Он выводится только в запрошенном `context`, не добавляется в каждый JSON-ответ и не копируется в публичные артефакты тестов.
- **Repeater extension data:** неверный bridge Newtonsoft→System.Text.Json исказит типы. Нужен fixture с неизвестными полями и сравнением семантического JSON.
- **Global skill drift:** перед записью сравнить active version/hash с candidate baseline; при drift объединить изменения до активации, сохранить rollback.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation | Status |
| --- | --- | --- | --- |
| «Зачем новый `search`, если есть `candidates`?» | Existing list already exists | `candidates` охватывает только статус/доступность и максимум 100 без cursor; AC4 доказывает произвольный поиск | mitigated |
| «Почему не добавить path в каждый ответ?» | Агенту нужно знать источник | Отдельный `context` сохраняет legacy JSON и выдаёт path по запросу | mitigated |
| «Не запишет ли CLI в другой каталог после проверки `context`?» | Desktop settings могут измениться между запусками | Явный `--tasks` на всей цепочке и settings-switch fixture AC3/AC7 | mitigated |
| «`apply inspect` ошибочно назовёт задачу созданной» | Receipt gap и independent changes | `desiredStatePresent` не равен historical `applied`; матрица исходов и negative tests | mitigated |
| «Почему меняется retry существующего `apply`?» | Составной request уже допустим, но сверка промежуточного title ошибается | Исправляется только ложный отказ по итоговому состоянию; AC6 включает create+setField/no-op и mixed-state negatives | mitigated |
| «Скилл станет несовместим с установленной 1.31.1» | Новый CLI не устанавливается этой задачей | conditional detection и сохранённый fallback; before/after smoke | mitigated |
| «Не потеряются ли старые ответы?» | Публичный CLI используют скрипты | additive commands/expanded snapshot, golden/legacy tests | mitigated |

### Rework Prevention Checklist

- Видимые команды и ответы описаны в §6.2–6.3; все сценарии связаны с AC.
- Решения и границы полномочий есть в §5 и §6.5; открытых user-owned решений нет.
- Публичный контракт, совместимость, recovery, тесты и package smoke указаны.
- Role-based review и adversarial pass выполняются до запроса approval в §19.

## 13. План выполнения

1. Зафиксировать legacy contract tests и failing characterization для help/search/context/repeater/inspect; не переписывать правила `apply`.
2. Реализовать help/version/schema/examples/context и проверить пакетный доступ к ресурсам.
3. Реализовать `search` и expanded/text `task`, затем негативные и pagination tests.
4. Вынести общий evaluator итоговых postconditions для retry и read-only `apply inspect`, проверить составной create, receipt-gap, mixed-state, drift и отсутствие новых записей.
5. Обновить README и conditional skill candidate; провести same-runtime behavioral smoke, drift/rollback проверку и только затем активировать skill.
6. Выполнить targeted tests, сборку, полный suite, package smoke и post-EXEC review. Релиз/установка не входят в этап.

## 14. Открытые вопросы

Нет блокирующих вопросов. Выбор синтаксиса команд и имён DTO зафиксирован в §6.2; редакционная корректировка без изменения наблюдаемого контракта допустима в EXEC, существенная — через обновление SPEC.

## 15. Соответствие профилю

- `product-system-design`: цели/non-goals §4–5, архитектурные границы §6.1, публичный API §6.2, совместимость §6.6/10, безопасность и конфигурация §7/12.
- `testing-baseline` / `testing-dotnet`: набор и команды §11, full suite из-за публичного контракта, TUnit syntax.
- `skill-creator`: короткий conditional entrypoint, сохранение актуального fallback, progressive disclosure, validator и behavioral smoke.
- Локальный `AGENTS.override.md` о UI tests не применяется: нет UI-facing изменения; это отдельно проверяется на post-EXEC.

## 16. Таблица изменений файлов

| Файл/группа | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Cli/Program.cs`, `CliIntrospection.cs`, `apply-request-v1.schema.json`, `TaskApplicationJson.cs` | команды, help/schema/version, ответы и проверка null arrays | публичная интроспекция/read API |
| `src/Unlimotion.Cli/TaskDirectoryResolver.cs` | provenance выбранного path | `context` без второго resolver |
| `src/Unlimotion.Cli/TaskApplicationReceiptStore.cs`, `TaskApplicationCommandService.cs` | read-only assessment и исправление составной reconciliation | inspect и корректный retry без повторной записи |
| `src/Unlimotion.Cli/Unlimotion.Cli.csproj` | pack schema/help, build version metadata при необходимости | совпадение бинарника и контракта |
| `src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs`, `TaskDirectoryResolverTests.cs` | contract/negative/recovery и settings-switch tests | доказательство поведения |
| `.github/workflows/nuget-cli.yml` | smoke версии, help, schema/examples/context до публикации | package contract gate |
| `src/Unlimotion.Cli/README.md` | полный CLI workflow | человекочитаемая документация |
| `C:\Users\Kibnet\.codex\skills\unlimotion-cli\SKILL.md`, `references/apply-v1.31.1.md` | conditional routing + старый relation contract | навык соответствует установленной версии |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Help/version | общий список, нет `--version` | точная командная text/JSON помощь и версия пакета |
| `apply` | схема в коде и частично в skill | схема/примеры внутри установленного бинарника |
| Поиск | только `candidates`/`unlocked` | bounded `search` по всем статусам с cursor |
| Task source | не виден в status | явный `context` |
| Snapshot | repeater скрыт, text include игнорируется | repeater и text sections доступны |
| Recovery | receipt скрыт, manual read-back; составной request иногда ложно не проходит retry | read-only evidence через `apply inspect` и сверка итогового состояния |
| Skill | привязка к 1.31.1 reference | capability-aware с сохранением 1.31.1 fallback |

## 18. Альтернативы и компромиссы

| Вариант | Плюсы | Минусы | Решение |
| --- | --- | --- | --- |
| Расширять только skill/README | быстрый старт | версия расходится с бинарником, поиск/repeater остаются недоступными | отклонён |
| Полностью заменить CLI parser на генеративный framework | один metadata source | большой регрессионный diff вне нужного результата | отклонён; узкий metadata layer + tests |
| Добавить поля source в каждый старый JSON | меньше вызовов | меняет стабильные ответы и раскрывает path без запроса | выбран отдельный `context` |
| Индексировать все task descriptions | богатый поиск | persisted index/migration, стоимость и приватность | выбран поиск title/ID |
| Автоматически повторять `apply` при сбое | меньше шагов | смешанное состояние и потерянный stdout не доказывают безопасность | выбран read-only inspection |

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Блок | Статус | Evidence / причина |
| --- | --- | --- | --- |
| 1 | A: outcome | PASS | §1 и §6.3 |
| 2 | A: AS-IS | PASS | §2, installed CLI и source/test inspection |
| 3 | A: проблема | PASS | §3 |
| 4 | A: цели | PASS | §4 |
| 5 | A: границы | PASS | §5 |
| 6 | B: ответственности | PASS | §6.1 |
| 7 | B: интеграция | PASS | §8 |
| 8 | B: алгоритмы | PASS | §6.2, §7 |
| 9 | B: ошибки/recovery | PASS | §6.2, §6.4, §12 |
| 10 | B: performance | PASS | bounded output, one graph read, representative measurement §11 |
| 11 | C: данные/состояние | PASS | §9 |
| 12 | C: совместимость | PASS | §6.6, §10 |
| 13 | C: rollback | PASS | §10 |
| 14 | D: AC | PASS | AC1–AC9 |
| 15 | D: AC→evidence | PASS | матрица §11, negative fixtures |
| 16 | D: commands/stop | PASS | §11 и §1 |
| 17 | E: план | PASS | §13 |
| 18 | E: решения/вопросы | PASS | §6.5, §14 |
| 19 | E: масштаб/форма | PASS | §0 |
| 20 | F: профиль | PASS | §15 |

Итог: ГОТОВО по linter; review-loop ниже остаётся самостоятельным gate.

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель/границы | 5 | Проверяемый агентский outcome, исключены release/UI/server |
| AS-IS | 5 | Установленная 1.31.1, checkout, тесты и реальные формы ответа |
| Дизайн | 5 | Команды, DTO, source, pagination и recovery определены |
| Безопасность/rollback | 5 | Additive API, нет task migration/живых записей, skill backup |
| Проверяемость | 5 | AC→tests, package/skill smoke и негативные случаи |
| Автономность | 5 | Decision Ledger и отсутствие блокирующих вопросов |

Итого: 30/30, готово к автономному выполнению **после exact approval**; числовой результат не заменяет review.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Не смешаны ли поиск, запись задачи и её выполнение? | PASS | §1/7 разделяют |
| UX / designer | applicable to CLI copy/output | Понятны ли агенту help, source, pagination и неопределённый исход? | PASS | §6.2/6.3 |
| Tester / validation | applicable | Каждому AC назначен тест, включая ошибки и совместимость? | PASS | §11 |
| Developer / architect | applicable | Сохранены ли ownership, storage и public compatibility? | PASS | §6.1/6.6/10 |
| Delivery / operations / security | applicable to package/global skill | Не выдана ли локальная сборка за релиз и нет ли записи в живое пространство? | PASS | §0/10/11/12 |

### Post-SPEC Review

- Статус / stop decision: **PASS** для фазы SPEC; можно запросить точное подтверждение. Это не разрешение EXEC.
- Scope reviewed: эта SPEC, central `routing-matrix`, `quest-governance`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `product-system-design`, `skill-creator`, локальный override; CLI source/README/tests, установленная 1.31.1 и старый skill. Планируемые файлы перечислены в §16; блокирующих открытых вопросов нет.
- Review passes:
  - Scope/Evidence: требования сведены к agent workflow из §6.3; ранее существовавший UI/Feed diff не входит в план и не изменялся этим SPEC.
  - Contract: новые ответы не меняют legacy JSON; `context` pinning, cursor source binding, конечные postconditions и hash согласованы с resolver/apply; исправление ложного retry отказа названо явно.
  - Adversarial risk: проверены >100 hits, settings switch, missing explicit directory, cross-space cursor, no receipt, composed/no-op request, BOM/hash, later drift, mixed state, старый CLI, unknown repeater fields и package mismatch.
  - Role-Based: таблица выше; UX относится к CLI text/JSON, не к desktop UI.
  - Fix and re-review: исправления HIGH/MEDIUM внесены в §2, §6.2–6.6, §7, §11–13 и таблицы ниже; повторно сверены сценарии, AC3/AC4/AC6/AC7, матрица тестов и границы совместимости.
  - Stop decision: PASS после исправлений; обязательные проверки поведения отложены до EXEC и не выдаются за выполненные.
- Independent review applicability: запрошен child `independent-reviewer`, но его effective sandbox оказался `danger-full-access`, не read-only. Он применял только чтение; по `review-loops` результат считается **adversarial fallback**, не технически независимым read-only review. Остаточный риск независимого ревью сохраняется.
- Evidence inspected: read-only вывод установленного CLI help/status/task; `Program.cs` (parser/source/hash/DTO), `TaskApplicationJson.cs`, `TaskApplicationReceiptStore.cs`, `TaskDirectoryResolver.cs`, `TaskApplicationCommandService.cs` (ValidateRequest/Reconcile/IsOperationAlreadyApplied), `TaskItem.cs`, `RepeaterPattern.cs`, `UnlimotionCliIntegrationTests.cs`, `nuget-cli.yml`, README и skill. После правок — текст §6.2/AC3–AC7 и отсутствие незаполненных пунктов/концевых пробелов.
- Depth checklist: scope/unrelated changes — отделены; AC/evidence — §11; user scenarios/decisions/objections — §6.3/6.5/12; validation evidence — план без ложного claim о green; unsupported claims — `desiredStatePresent` не назван историческим применением, `context` не назван проверкой доступа; regression — §10/11; comments/docs/package — AC9; hidden behavior — retry correction явно в scope; manual-review challenge — составной request, settings drift, cross-space cursor и text hash разобраны.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | recovery | Per-operation reconciliation ошибается на `createTask(title=A)` + `setField(title=B)` | Общий evaluator конечного состояния и AC6 с retry без receipt | fixed in SPEC |
| HIGH | source scope | `context` без pinning не защищает от смены desktop settings | Явный `--tasks` на всю цепочку и settings-switch test AC3/AC7 | fixed in SPEC |
| MEDIUM | pagination | Cursor A можно применить к пространству B | Bind normalized path fingerprint, negative AC4 | fixed in SPEC |
| MEDIUM | request hash | Raw-byte hash inspect мог бы разойтись с существующим decoded-text hash | Переиспользовать чтение/`RequestHash`, BOM/whitespace AC6 | fixed in SPEC |
| MEDIUM | source availability | `context` мог успешно вернуть несуществующий explicit каталог | Проверка existence, честная граница preflight и negative AC3 | fixed in SPEC |
| MEDIUM | recovery | Частичный match может быть законным no-op при совпадающем исходном ETag | Планирование до решения о reconciliation, no-op fixture AC6 | fixed in SPEC |

- No-findings justification: неприменимо — findings были и закрыты изменениями SPEC; фактический EXEC ещё не проверен.
- Needs human: только точное подтверждение SPEC перед изменением кода или скилла.

### Post-EXEC Review

- Scope/evidence: проверены изменённые CLI, task application service, контрактные тесты, README, package workflow и активный skill; посторонние Feed/UI изменения в основном worktree исключены из проверочного worktree. `git diff --check` по файлам EXEC прошёл. Чистый worktree создан от `HEAD` и содержит только файлы этой SPEC; после общего прогона SHA-256 всех 12 scoped файлов совпали с основным worktree. Раздел SPEC и глобальный skill не копировались в этот проверочный worktree, поскольку не компилируются в test assembly.
- Contract: общий и командный text/JSON help, версия из package metadata, встроенная схема и три примера проверены через локально упакованный и установленный во временный `--tool-path` CLI. `context` использует тот же resolver; `search` связывает курсор с фильтрами и абсолютным task path; legacy JSON без `--include` сохранён. `apply inspect` использует общий путь декодирования/hash с `apply`, receipt и итоговые postconditions; в тестах различены receipt, отсутствие receipt, no-op, смешанное состояние и поздний drift.
- Adversarial: отрицательные schema fixtures отвергают недопустимое поле и relation; тесты охватывают неправильный/cross-space cursor, missing explicit path, BOM/whitespace, битый и неполный receipt, составной create+field/relation/status и retry. Read-only inspect отдельно сверялся по хешам task/receipt; journal recovery считается отдельным прежним побочным эффектом открытия storage. Семантические ограничения графа остаются за dry-run, о чём говорит help.
- Role review: domain workflow — PASS для разделения чтения/claim/write; CLI UX — PASS для help/source/pagination/recovery; developer/architecture — PASS для additive API и receipt v1; tester — targeted и package smoke пройдены, полный suite FAILED по трём Feed/UI тестам; delivery/security — PASS для временного package и отсутствия записи в личные задачи, релиз и публикация не выполнялись.
- Independent review: технически read-only reviewer в этой среде недоступен (проверка на фазе SPEC дала `danger-full-access`); выполнен adversarial self-review. Остаточный риск — отсутствие независимого review с принудительным read-only sandbox.
- Validation: сборка CLI Release прошла; в чистом worktree контрактные CLI **41/41** и resolver **4/4** прошли. Локальный package `99.0.0-local.5` установлен только во временный `--tool-path`: его `version --format json` сообщает тот же `packageVersion` и `buildKind=package`; доступны командная справка и embedded schema. До последней текстовой правки на пакете `99.0.0-local.4` проверены 24 командные справки, три примера, три dry-run fixture, `context` и потерянный receipt; финальная правка коснулась только одной заметки `help create`, которая проверена из пакета `.5`. После неё повтор 41/41 CLI прошёл и SHA-256 всех 12 scoped файлов основного/чистого worktree совпали. Активный skill прошёл `quick_validate.py` под Python UTF-8; старый глобальный CLI остаётся 1.31.1 и идёт по fallback. Единственный последовательный замер на графе примерно 3000 задач: `status` 1757 ms, `candidates` 1498 ms, `search` 1547 ms; это не SLA и не сравнительный benchmark.
- Full suite: первый чистый полный прогон — **1787/1789**, два Feed/UI сбоя; оба прошли изолированные повторы. Второй полный прогон — **1787/1790**, три сбоя: `NoteLink_LateReadDoesNotOverrideNewerOpenOrClose(True)` (временный `B.md` занят при удалении), `Feed_ReviewStartsTodayTopToBottomThenMovesToPreviousDay` (ожидание review state), `FormatImpact_CountsAllFilesWithoutChangingNamesOrSettings` (временный `2026.09.03.md` занят при удалении). Изолированные повторы после второго прогона прошли соответственно **2/2**, **1/1**, **1/1**. Эти файлы/сценарии не входят в EXEC diff; точный общий suite остался красным. После полного прогона изменена только фраза в `help create`; для неё заново выполнены build/pack/install/help и targeted 41/41, но полный suite повторно не запускался. Логи: `%TEMP%\unlimotion-cli-clean-full-20260927.log`, `%TEMP%\unlimotion-cli-clean-full-final-20260927.log`, `%TEMP%\unlimotion-cli-clean-targeted-final5-20260927.log` и три `%TEMP%\unlimotion-cli-ui-retry-*.log`.
- Fix/re-review: после исходных targeted находок исправлены общий no-op/composed evaluator, структурированный отказ на повреждённый receipt и неполные JSON arrays; поздний review нашёл вводящую в заблуждение фразу `apply createTask` в `help create`, заменённую на корректное описание операции внутри request. Повторены контрактные тесты, package/skill smoke и diff check. По доступному evidence открытых дефектов в scoped CLI-контракте не выявлено. Негативный исход полного suite не исправлялся в рамках этой CLI SPEC, чтобы не смешивать её с чужими Feed/UI изменениями.
- Stop decision: **NEEDS-FIX для общего quality gate**. Реализация AC1–AC7/AC9 подтверждена targeted/package/skill evidence; AC8 в части полного suite не принят. Перед релизом/публикацией потребуется отдельный green full suite или явно оформленное решение о принятии этого CI риска. Независимое read-only review также остаётся не проведённым.

### Fresh-main delivery review

- Scope/ancestry: по поручению «Оформи PR от актуального мейна» создана отдельная ветка `feat/cli-agent-discovery` от полученного `origin/main@da9af236`. В неё перенесены только 13 файлов этой SPEC; незакоммиченный Feed/UI diff исходной ветки не перенесён. Diff staged проверен через `git diff --cached --check` и список файлов.
- Compatibility: актуальный main уже поддерживает `UNLIMOTION_TASKS`. Resolver и справка сохранены с порядком `--tasks` → environment → desktop settings; `context` различает `explicitTasks`, `environmentTasks`, `desktopSettings`. При слиянии с main восстановлен отказ для явно переданного пустого `--tasks`, чтобы не происходил скрытый переход к environment. Отсутствующие в main поля `TaskItem.IsGoal`/`AreaIds` исключены из сверки созданной задачи; остальные postconditions и unknown extension data сохранены.
- Validation: на свежем main CLI integration **48/48**, resolver **6/6**. CLI Release упакован как временный `99.0.0-local.pr1` и установлен в отдельный `--tool-path`; версия, `help apply`, embedded schema, три примера и `context` с explicit/environment источниками прошли package smoke. Первый полный последовательный TUnit suite: **1129/1130**, один сбой `TaskPlanningWantedImportanceScenario_ExecutesFeatureSteps` в Avalonia (`Collection was modified`); изолированный повтор **1/1**. Второй полный последовательный suite на той же assembly: **1130/1130**, 0 failed, 0 skipped. Логи: `%TEMP%\unlimotion-cli-pr-targeted-final-20260927.log`, `%TEMP%\unlimotion-cli-pr-resolver-20260927.log`, `%TEMP%\unlimotion-cli-pr-full-20260927.log`, `%TEMP%\unlimotion-cli-pr-ui-retry-20260927.log`, `%TEMP%\unlimotion-cli-pr-full-final-20260927.log`.
- Adversarial/role review: проверены конфликт source provenance, пустой explicit path, общая справка с environment precedence, package metadata/schema и 13-файловый scope. Domain/CLI UX/developer/tester/delivery роли не выявили открытых дефектов в изменённом контракте; нестабильный UI-тест отмечен как риск общего suite, а не результат изменения CLI. Независимое технически read-only review по-прежнему недоступно; остаётся adversarial self-review.
- Stop decision для PR-ветки: **PASS** по локальному full gate и scoped review; исторический `NEEDS-FIX` выше относится к первому EXEC worktree с дополнительными Feed/UI изменениями, не к этой fresh-main ветке. Перед push требуется ещё раз сверить `origin/main` и при его продвижении обновить ветку и релевантную проверку.

## Approval

Получено точное подтверждение пользователя: `Спеку подтверждаю`. Оно разрешило EXEC этого документа; публикация, release, push/PR и глобальная установка нового CLI не поручались.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Фактическое решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC / старт | Пользователь попросил оформить SPEC после аудита CLI; expanded из-за публичного контракта | Установленная 1.31.1, checkout и skill прочитаны; UI/Feed diff уже существовал | Подготовить и проверить SPEC | Реализация не разрешена | этот файл |
| SPEC / дизайн | Выбраны additive команды и conditional skill; `apply createTask` признан существующим идемпотентным путём | §6.2, §6.5, §11; нужен post-SPEC review | Review и исправления | Не требовалось | этот файл |
| SPEC / adversarial fallback | Child sandbox не read-only; его HIGH/MEDIUM findings исправлены в SPEC. Дополнительно закрыт no-op retry counterexample | §19 findings table, AC3/AC4/AC6/AC7; код и skill не менялись | Запросить exact approval | Ожидается `Спеку подтверждаю` | этот файл |
| EXEC / разрешение | Пользователь дословно подтвердил `Спеку подтверждаю`; начата реализация утверждённого контракта | User turn 2026-09-27; сохранены посторонние UI/Feed изменения | Код, контрактные тесты, package и skill smoke, post-EXEC review | EXEC разрешён; release/push/global install не разрешены | этот файл |
| EXEC / контракт и локальные проверки | Реализованы metadata/help/schema/example, context/search, expanded task/repeater, inspect и конечная reconciliation; skill активирован только после candidate/backup/validator | Чистый worktree: CLI 41/41 после финальной текстовой правки, resolver 4/4; локальный пакет `99.0.0-local.5`, предыдущий полный package smoke `.4`; 3000 задач: status 1757 ms, candidates 1498 ms, search 1547 ms (один прогон, не benchmark SLA) | Закрыть review с оговоркой полного suite | Дополнительное решение не требовалось | CLI, tests, README, workflow, skill; `%TEMP%` logs и backup |
| EXEC / чужой UI diff и общий suite | Обычная сборка основного worktree блокировалась `MainScreen.axaml` Click; чистый worktree содержит только CLI diff и имеет совпадающие хеши всех 12 scoped файлов | Первый full suite 1787/1789, второй 1787/1790; три финальных Feed/UI сбоя прошли изолированно, но AC8 full gate остаётся красным | Не выдавать scoped green за общий green; до релиза нужен green gate или явное принятие риска | Не требовалось | `%TEMP%\unlimotion-cli-validation-20260927`, full/targeted/UI logs |
| DELIVERY / fresh main | Перенести только CLI scope на `origin/main`, сохранив появившийся в нём `UNLIMOTION_TASKS` и пустой explicit-path отказ | Ветка `feat/cli-agent-discovery`; CLI 48/48, resolver 6/6, package smoke, второй full suite 1130/1130; первый full 1129/1130 из-за нестабильного UI-теста | Повторить fetch/ancestry, commit, push и открыть PR | Пользователь поручил «Оформи PR от актуального мейна» | fresh-main worktree и `%TEMP%\unlimotion-cli-pr-*.log` |
