# Unlimotion CLI

Консольный клиент для чтения и изменения локального каталога задач Unlimotion без запуска UI. Он предназначен в том числе для агентов, которым нужны те же правила доступности и завершения, что использует desktop-приложение.

## Установка с NuGet.org

```powershell
dotnet tool install --global Unlimotion.Cli
unlimotion-cli status --format json
unlimotion-cli version --format json
unlimotion-cli help search --format json
```

Обновление установленного инструмента:

```powershell
dotnet tool update --global Unlimotion.Cli
```

## Скилл для Codex

Операционный [скилл `unlimotion-cli`](../../.agents/skills/unlimotion-cli/SKILL.md) помогает агенту читать и изменять задачи через **уже установленный актуальный** CLI; проверенный контракт — 1.32.0. Перед работой агент проверяет версию и возможности фактически запущенного бинарника. При несовместимом CLI он останавливается до изменения задач и предлагает отдельно обновить инструмент командой выше. Установка скилла не устанавливает и не обновляет `Unlimotion.Cli`.

Команды `snapshot`, поиск с `--fields`/`--snapshot` и полный preview с `--diff full`/`--expect-preview` ниже описывают **новые, ещё не выпущенные возможности этой локальной разработки**. Номер версии репозитория не подтверждает их наличие в установленном инструменте. У того же закреплённого executable проверьте `version --format json`, `help snapshot --format json`, `help search --format json`, `help apply --format json` и соответствующие schemas. Если требуемого флага нет, не подменяйте полный preview обычным dry-run и не применяйте согласованный пакет без нужного guard; обновление или установка CLI — отдельная операция.

При запуске Codex из клона Unlimotion (в том числе из его подпапок) скилл обнаруживается автоматически из `.agents/skills/unlimotion-cli/` в корне репозитория. Для другого проекта установите его из GitHub в личный каталог навыков Codex. После слияния изменений в `main` команда PowerShell выглядит так:

```powershell
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$installer = Join-Path $codexHome 'skills\.system\skill-installer\scripts\install-skill-from-github.py'
$personalSkills = Join-Path $env:USERPROFILE '.agents\skills'
python $installer --repo Kibnet/Unlimotion --path .agents/skills/unlimotion-cli --dest $personalSkills
```

Команда использует штатный `skill-installer`, но явно задаёт `--dest`: без него этот helper устанавливает в `$CODEX_HOME/skills`, а документированный личный путь Codex — `$HOME/.agents/skills`. Для версии из ещё не слитой ветки добавьте `--ref <имя-ветки-или-коммит>`. Установщик откажется перезаписать существующий каталог `unlimotion-cli`; сначала проверьте действующую копию и не удаляйте её автоматически. Если одноимённый скилл уже есть в личном каталоге, при работе в клоне могут отображаться обе копии. Новые скиллы Codex обычно обнаруживает автоматически; если копия не появилась, перезапустите Codex. Вне клона проверьте наличие скилла в новой сессии. Эти шаги устанавливают только инструкции агента: для выполнения команд CLI по-прежнему нужен совместимый `Unlimotion.Cli`.

CLI требует совместимый .NET 10 SDK/runtime. Каталог задач выбирается в порядке: явный `--tasks`, непустая переменная окружения `UNLIMOTION_TASKS`, затем путь активного локального пространства из `Settings.json` установленного desktop-приложения. Если выбранный каталог недоступен, CLI выдаёт ошибку и не переключается на другой источник.

Для постоянного пути без `--tasks` задай переменную в окружении процесса. В PowerShell:

```powershell
$env:UNLIMOTION_TASKS = 'C:\Tasks'
unlimotion-cli status
Remove-Item Env:UNLIMOTION_TASKS
```

В Bash, включая Termux при установленном совместимом .NET 10 и доступной общей папке задач:

```bash
export UNLIMOTION_TASKS="$HOME/tasks"
unlimotion-cli status
unset UNLIMOTION_TASKS
```

Для постоянной настройки используй абсолютный путь. Относительный путь в переменной отсчитывается от текущего каталога CLI; значение не обрезается и `~` внутри него не раскрывается. Пустая переменная возвращает обычный поиск desktop-настроек. На Windows конфиг находится в системной папке документов пользователя, обычно `%USERPROFILE%\Documents\Unlimotion\Settings.json`; на Linux используется папка документов XDG, на macOS — `~/Documents/Unlimotion/Settings.json`. На Android каталог приложения закрыт для Termux: выбери папку задач, доступную обоим приложениям, и укажи её в переменной. Переменная фиксирует путь и не меняется автоматически при переключении пространства в приложении.

Начните с `unlimotion-cli --version`, `unlimotion-cli help <command>` или `<command> --help`. Справка и `apply schema/example` доступны даже без настроенного пространства. `version --format json` сообщает `packageVersion`, `applicationVersion` и `buildKind`; у локальной сборки без заданного `PackageVersion` значение `buildKind=local`.

Перед связанной серией чтений и записей запросите `unlimotion-cli context --format json`. Ответ содержит абсолютный `tasksPath`, `sourceKind` (`explicitTasks`, `environmentTasks` или `desktopSettings`) и `storageKind=file`. Передавайте полученный путь как явный `--tasks` **в каждый** `search`, `task`, `apply --dry-run`, `apply`, `apply inspect` и read-back: переменная окружения и desktop settings могут смениться между отдельными процессами. `context` проверяет существование каталога, но не валидность графа и не будущую возможность записи.

Для локальной сборки рекомендуется отдельный каталог инструмента, чтобы агент не изменял глобальную установку пользователя:

```powershell
$packageVersion = "1.32.0-local.1"
dotnet pack src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -p:PackageVersion=$packageVersion -o artifacts\tools
dotnet tool install --tool-path C:\tmp\unlimotion-cli-tool --add-source artifacts\tools Unlimotion.Cli --version $packageVersion
```

Версию опубликованного пакета определяет stable GitHub release tag (`vMAJOR.MINOR.PATCH`), а не файл проекта. Для локальной упаковки всегда передавайте собственный допустимый NuGet `PackageVersion`.

## Команды

```powershell
unlimotion-cli status [--tasks <task-dir>] [--format text|json]
unlimotion-cli context [--tasks <task-dir>] [--format text|json]
unlimotion-cli search [--query <text>] [--status <status>] [--root <task-id>]... [--limit <1..100>] [--cursor <token>] [--tasks <task-dir>] [--format text|json]
unlimotion-cli unlocked [--tasks <task-dir>] [--format text|json]
unlimotion-cli candidates --limit <1..100> [--status Prepared] [--startable true] [--sort default] [--tasks <task-dir>] [--format text|json]
unlimotion-cli task --id <task-id> [--include details,relations,criteria,history,execution] [--tasks <task-dir>] [--format text|json]
unlimotion-cli claim --id <task-id> --agent <agent-id> --expected-status Prepared [--tasks <task-dir>] [--format text|json]
unlimotion-cli execution question --id <task-id> --agent <agent-id> --lease <lease-id> --text <text> [--tasks <task-dir>] [--format text|json]
unlimotion-cli execution answer --id <task-id> --agent <agent-id> --lease <lease-id> --question-id <question-id> --text <text> [--tasks <task-dir>] [--format text|json]
unlimotion-cli execution result --id <task-id> --agent <agent-id> --lease <lease-id> --summary <text> [--link <uri>] [--tasks <task-dir>] [--format text|json]
unlimotion-cli execution complete --id <task-id> --agent <agent-id> --lease <lease-id> --summary <text> [--link <uri>] [--tasks <task-dir>] [--format text|json]
unlimotion-cli release --id <task-id> --agent <agent-id> --lease <lease-id> --reason <text> [--tasks <task-dir>] [--format text|json]
unlimotion-cli create --title <text> [--description <text>] [--parent <task-id>] [--author <name>] [--tasks <task-dir>] [--format text|json]
unlimotion-cli validate [--tasks <task-dir>] [--format text|json]
unlimotion-cli set-status --id <task-id> --status <status> [--author <name>] [--tasks <task-dir>] [--format text|json]
unlimotion-cli complete --id <task-id> [--author <name>] [--tasks <task-dir>] [--format text|json]
unlimotion-cli set-criterion --id <task-id> --criterion <criterion-id> --satisfied true|false [--tasks <task-dir>] [--format text|json]
unlimotion-cli satisfy-criterion --id <task-id> --criterion <criterion-id> [--tasks <task-dir>] [--format text|json]
unlimotion-cli apply --request <path|-> [--dry-run] [--tasks <task-dir>] [--format text|json]
unlimotion-cli apply inspect --request <path|-> [--tasks <task-dir>] [--format text|json]
unlimotion-cli apply schema --format json
unlimotion-cli apply example set-field|add-relation|create-task --format json
unlimotion-cli snapshot capture --selection <json-file> --output <new-file> --tasks <task-dir> --format json
unlimotion-cli snapshot read --snapshot <file> --view targets|context|manifest [--page-size <1..500>] [--cursor <token>] --format text|json
unlimotion-cli snapshot diff --before <file> --after <file> [--page-size <1..500>] [--cursor <token>] --format text|json
unlimotion-cli snapshot schema [--kind selection|artifact|page|delta] --format json
unlimotion-cli search --snapshot <file> --query <text> --fields id,title,description,criteria [--limit <1..100>] [--cursor <token>] --format text|json
unlimotion-cli search --tasks <task-dir> --query <text> --fields title,description,criteria [--limit <1..100>] [--cursor <token>] --format text|json
unlimotion-cli apply --request <file> --dry-run --diff full --tasks <task-dir> --format text|json
unlimotion-cli apply --request <file> --expect-preview <full-preview.json> --tasks <task-dir> --format text|json
```

`--include`, `--link` и `--parent` можно указывать несколько раз. В `--include` также принимается список через запятую. `task` без `--include` сохраняет прежний JSON-контракт анализа доступности.

`search` ищет подстроку в title или ID по всем статусам, включая недоступные и архивные задачи. По умолчанию возвращает 20 элементов, максимум 100 за вызов. JSON содержит `items`, `totalCount`, `nextCursor`; передавайте `nextCursor` с теми же фильтрами и тем же `--tasks`, пока он не станет `null`. Порядок: title без учёта регистра, затем точный title и ID. Между страницами граф может меняться: для важной работы дедуплицируйте ID и при необходимости начните поиск заново. `--root` повторяем и включает корни и их потомков.

`task --include details,relations,criteria,history,execution` возвращает ETag и выбранные секции. В `details.repeater` видны тип, период, флаг `afterComplete`, pattern и неизвестные расширенные поля, если они есть; без повторителя значение `null`. В текстовом режиме запрошенные секции и ETag также печатаются. Старый ответ без `--include` сохранён.

## Снимок полного контекста и изменения между проходами

`snapshot capture` один раз загружает весь штатный каталог, выбирает targets и включает требуемое окружение. Отбор и контекст — разные части selection. Пример `selection.json` с синтетическими ID:

```json
{
  "schemaVersion": 1,
  "select": { "mode": "unlocked", "rootIds": ["goal-a", "goal-b"], "statuses": [] },
  "context": "night-v1",
  "missingSelection": "report",
  "include": ["details", "criteria", "history", "execution"]
}
```

Замените ID реальными. `unlocked` означает доступность в графе, а не достаточность контекста или разрешение на выполнение. `all` включает все статусы; `ids` выбирает явные `taskIds` без roots/statuses. `missingSelection=report` сохраняет отсутствующие IDs в диагностике и полезен для постоянного ночного scope; default `error` отклоняет отсутствующий выбранный ID. Отсутствующая графовая ссылка в обоих случаях остаётся ошибкой.

`night-v1` включает все родительские ветви, в том числе Completed/Archived, соседей исходных целей и нужных upstream blockers. Узел хранится один раз, но каждое родительское ребро сохраняется. Отображайте DAG целиком по связям: нельзя выбирать первый путь/родителя или обрезать глубину с заявлением о полном контексте. История означает текущую `StatusHistory`, не Git history или историю всех правок; execution audit может иметь собственный признак truncation. Отсутствующая необязательная секция имеет состояние `notRequested`.

```powershell
# Закреплённый executable и $tasksPath получены через version/help/context.
# Родительский каталог C:\NightRuns\example уже существует и находится вне task root.
& $cliExe snapshot schema --kind selection --format json
& $cliExe snapshot capture --tasks $tasksPath --selection selection.json --output C:\NightRuns\example\run.snapshot.json --format json
& $cliExe snapshot read --snapshot C:\NightRuns\example\run.snapshot.json --view targets --page-size 100 --format json
& $cliExe snapshot read --snapshot C:\NightRuns\example\run.snapshot.json --view context --page-size 100 --format json
& $cliExe snapshot diff --before C:\NightRuns\example\previous.snapshot.json --after C:\NightRuns\example\run.snapshot.json --format json
```

Capture требует явного `--tasks` и нового output-файла вне каталога задач; существующий destination не перезаписывается. `read`, `diff`, `schema` и поиск с `--snapshot` работают offline, не разрешают live `--tasks` и не обращаются к desktop settings. Снимок неизменяемый, связан с `snapshotId`/`artifactHash`, не имеет TTL и читается после перезапуска. Его ETag и availability относятся к `evaluatedAt`, а не к текущему состоянию. Для каждой страницы передавайте полученный cursor без изменений с тем же artifact/view/фильтрами до `nextCursor=null`; ошибка integrity не должна запускать скрытый live fallback.

Согласованность — общий lock штатных writers и подтверждение имён/исходных bytes вторым проходом, `atomic=false`. Внешняя программа, обходящая lock, может сделать незаметный промежуточный переход. Capture не восстанавливает pending journals: `recoveryRequired` означает отказ без изменений задач/receipt/journal; lock metadata допустимы. Не удаляйте lock или journal. Лимиты: 50 000 задач, 500 000 canonical edges, 8 MiB на task file, 256 MiB исходных bytes, 128 MiB artifact, 5 000 000 entries contextIds; страница — до 500 элементов и 16 MiB. Сужение roots уменьшает payload, но не полный source scan. Превышение или нестабильное чтение не даёт частичного успешного снимка.

Delta сравнивает конечные состояния двух совместимых снимков: создание/удаление, собственные поля, связи, участие в выборке, вычисляемую доступность и invalidation targets из-за всех предков/блокеров. Это **state difference, не event feed**: задача, созданная и удалённая между снимками, не обнаруживается. Смена времени может изменить доступность даже при прежних ETag; изменения Obsidian и внешних фактов проверяются отдельно. Отсутствие в namespace отличается от terminal status и выхода из selection.

При missing/corrupt baseline или `deltaIncompatible` не считайте изменений ноль. Сохраните внешние решения, материалы и очереди; сделайте полный новый capture, отметьте `baselineReset` во внешнем run record и проверьте все targets на необходимость актуализации. Старые материалы остаются кандидатами на повторное использование. Baseline обновляется после сохранения queue checkpoint, связанного с after artifact hash.

### Поиск существующих решений

Новые формы `search` возвращают `searchVersion=2`, совпадения и source metadata. `--fields` — непустой набор `id,title,description,criteria`; description ищется только в пользовательском тексте без служебного execution-сегмента, criteria — в тексте всех критериев. Запрос — одна буквальная подстрока по любому выбранному полю: NFC, OrdinalIgnoreCase, без regex, wildcard, морфологии и semantic search; `ё`/`е` различаются. Пустой запрос перечисляет задачи. Текст shell передавайте обычным quoting, например `--query 'проверка решения'`.

Поиск по snapshot покрывает только payload (`coverage`, `searchedNodeCount`, `catalogCount`), а не весь catalog. Для полного поиска захватите `mode=all`, `context=none`, `include=["details","criteria"]`. Незапрошенная секция даёт `sectionNotCaptured`, неполная root-ветвь — `scopeNotCaptured`; это не пустой результат. Повреждённые execution markers дают `descriptionUnavailable` и `searchComplete=false`, поэтому отсутствие совпадений не доказывает отсутствия решения. Snippets могут быть усечены с явными флагами; диапазоны относятся к UTF-16 позициям NFC snippet. Snapshot pagination стабильна, live v2 сообщает `liveUnpinned`. Старые вызовы без новых флагов сохраняют прежний ID/title контракт.

## Декларативное изменение и проверка исхода

`apply` проверяет плановые даты в конечном состоянии пакета: конец не может быть раньше начала. Старая некорректная пара дат не блокирует создание других задач или изменение названия, описания и статуса, в том числе у самой старой задачи, если обе даты остались точно прежними (включая offset). Автоматического исправления старых дат нет. Новая задача или изменённая пара дат с концом раньше начала отклоняется атомарно: `error.taskId` указывает проблемную задачу, сообщение содержит обе даты; `authoritativeTasks` содержит исходную задачу, если она существовала. При нескольких ошибках первой возвращается задача с минимальным ID по ordinal-порядку. Исправление диапазона, очистка даты и равные моменты начала/конца разрешены; учитывается итог пакета, а не промежуточный диапазон между операциями над разными полями.

`validate` проверяет ошибки загрузки, дубликаты, ссылки и доступность задач, но не согласованность плановых дат. Его `isValid=true` не заменяет `apply --dry-run` для конкретного запроса.

Точное сравнение дат относится к загруженным `original/staged` снимкам. Существующее файловое хранилище при чтении нормализует offset в часовой пояс хоста; сохранение исходного текстового offset файла при последующей записи этим правилом не гарантируется.

`apply schema --format json` отдаёт JSON Schema request v1 из установленного пакета. `apply example set-field|set-importance|add-relation|create-task --format json` печатает полноценные шаблоны; все `example-*` ID и ETag замените реальными значениями перед preview. Схема проверяет синтаксис; графовые правила, ETag, статусные ограничения и защищённые маркеры описаны в `help apply` и проверяются `--dry-run`.

Для создания с заранее выбранным ID используйте шаблон `create-task`, задайте уникальные `applicationId`, `newTaskId`, ссылку `proposalRefs` на согласованное поручение и точный `--tasks`:

```powershell
unlimotion-cli apply example create-task --format json > request.json
# Отредактируйте примерные ID, текст, author и reason.
unlimotion-cli apply --tasks $tasksPath --request request.json --dry-run --format json
unlimotion-cli apply --tasks $tasksPath --request request.json --format json
unlimotion-cli task --tasks $tasksPath --id $newTaskId --include details,relations,criteria --format json
```

Отправляйте запись только после успешного preview с тем же файлом запроса. При потере ответа или `outcomeUnknown` сначала выполните `apply inspect --request request.json --tasks $tasksPath --format json`, затем read-back затронутых ID. `receiptMatched` подтверждает receipt для точного `applicationId + requestHash`, но текущее состояние может уже отличаться. `desiredStatePresent` без receipt подтверждает только наблюдаемое конечное состояние, а не историю применения. `readyForPreview` означает, что исходные preconditions совпали и обычный preview проходит. `needsReconciliation` требует ручной сверки; не повторяйте запись автоматически. Хешируется декодированный текст JSON, включая пробелы и переносы, так что не форматируйте исходный request между отправкой и inspection. В новом observation-контракте `inspect` и оба вида dry-run при pending journal возвращают `recoveryRequired`, без replay. Обычный разрешённый write path сохраняет существующее recovery предыдущей транзакции; это отдельное событие от записи нового запроса.

### Изменение важности

Для записи `importance` используйте существующий `setField`. В request `value` — строка, в сохранённой задаче и preview важность — число:

```json
{"operationId":"set-importance","kind":"setField","taskId":"task-id","field":"importance","value":"42"}
```

Допустим канонический десятичный `Int32`: `0`, положительное число без ведущих нулей или отрицательное число с `-`, в пределах `-2147483648..2147483647`. Пробелы, `+42`, `042`, `-0`, дроби, экспонента, `null` и JSON-число вместо строки отклоняются. Диапазон persisted поля отличается от ограничений числового UI-контрола. Для сброса передайте строку `"0"`; `clearField importance` не поддерживается. Схема проверяет форму строки, runtime также проверяет диапазон.

Пример для PowerShell 7; `$cliExe`, `$tasksPath` и `$taskId` получены через обычные discovery-команды:

```powershell
$task = & $cliExe task --tasks $tasksPath --id $taskId --include details --format json | ConvertFrom-Json
$request = & $cliExe apply example set-importance --format json | ConvertFrom-Json
$request.applicationId = 'importance-' + [Guid]::NewGuid().ToString('N')
$request.author = 'agent-name'
$request.reason = 'Согласованное изменение важности'
$request.proposalRefs[0].id = 'P-approved-priority-change'
$request.proposalRefs[0].revision = 1
$request.preconditions[0].taskId = $taskId
$request.preconditions[0].etag = $task.etag
$request.operations[0].taskId = $taskId
$request.operations[0].value = '42'
$request | ConvertTo-Json -Depth 20 | Set-Content request.json -Encoding utf8NoBOM
& $cliExe apply --tasks $tasksPath --request request.json --dry-run --diff full --format json > preview.json
# Проверить success=true, complete=true и числовое изменение /details/importance.
& $cliExe apply --tasks $tasksPath --request request.json --expect-preview preview.json --format json
& $cliExe task --tasks $tasksPath --id $taskId --include details --format json
```

Замените author, reason и proposalRefs на согласованное поручение. При создании с выбранным `newTaskId` добавьте после `createTask` операцию `setField importance` с тем же ID: full preview и проверка применения учитывают конечную важность новой задачи. Две записи одного поля одной задачи в одном request по-прежнему дают `conflictingOperations`.

При повторе используйте прежние request и applicationId. Matching receipt подтверждает прошлое применение; для текущего значения выполните `apply inspect` и read-back. Без receipt совпавшая конечная важность участвует в обычной reconciliation и не требует повторной записи задачи. Если установленная `apply schema` не перечисляет importance, эта версия CLI setter не поддерживает.

### Полный before → after и guard согласованного пакета

`apply --dry-run --diff full` показывает явные, производные и системные изменения: поля, критерии, связи, доступность зависимых задач и protected execution state. Для машинного согласования сохраните полный JSON, а text view используйте для чтения пользователем:

```powershell
& $cliExe apply --tasks $tasksPath --request request.json --dry-run --diff full --format json > preview.json
# Проверить success=true, mode=preview, didMutate=false, preview.complete=true и полный diff.
# Выполнить только после однозначного разрешения пользователя на этот application/proposal revision.
& $cliExe apply --tasks $tasksPath --request request.json --expect-preview preview.json --format json
& $cliExe apply inspect --tasks $tasksPath --request request.json --format json
```

Preview не является согласием. Guard связывает exact request hash, source identity, полный manifest имён/исходных bytes и semantic effect. Изменение даже независимой задачи может дать `previewStale`; заново прочитайте состояние, создайте preview и проверьте необходимость новой revision/согласования. Не удаляйте guard и не откатывайтесь к plain apply ради прохождения отказа. Timestamps, генерируемые при apply, обозначены typed placeholders с правилом генерации; business values, статусы и связи показываются точно. Старые задачи без `CreatedDateTime` могут требовать отдельного решения: `unstablePrecondition`/`unstableSource` не разрешают автоматический ремонт. Полный diff ограничен 32 MiB; неполный diff не имеет executable guard.

В текущей попытке guarded apply дополнительно проверяет исходные bytes перед заменой файла и manifest перед commit; при обнаруженном обходе lock откат сохраняет более поздние внешние изменения и требует read-back. Это не общая атомарность относительно произвольных writers. Формат persisted journal и его recovery остаются прежними: после аварии процесса последующее восстановление может записать сохранённые образы поверх внешних изменений, сделанных в обход lock. Ошибка безопасного отката может оставить journal; она означает неизвестный исход, а не право удалить журнал или повторить apply.

После записи выполните `task --include details,relations,criteria,history,execution` для всех changed/created/affected IDs, сверив и производные изменения, затем сохраните внешний application result и checkpoint очереди. При прерывании между task commit и записью в Obsidian используйте сохранённые exact request/application ID → inspect → read-back → reconciliation, не создавайте новый request по памяти. Отказ пользователя закрывает текущую proposal revision без apply; новый замысел получает новую revision/application. CLI не хранит approval lifecycle и не обеспечивает транзакцию между каталогом задач и Obsidian.

### Многострочное описание

`--description` принимает многострочный текст и Markdown. Переносы строк, табуляция, Unicode-символы и остальные переданные символы сохраняются без нормализации:

````powershell
$description = @'
# Контекст

- первый пункт
- [документация](https://example.com/docs)

```csharp
Console.WriteLine("Пример");
```
'@

unlimotion-cli create --title "Задача с контекстом" --description $description --format json
````

Максимальная длина описания — 100 000 символов. Точные строки служебных маркеров `AgentExecution` зарезервированы: они управляют проекцией выполнения агента и не могут входить в пользовательский текст. Для машинных сценариев создания и обновления описания используйте `apply --request <path|->` и поле `descriptionUserText`; JSON позволяет надёжно передать произвольный текст без ограничений командной строки оболочки.

## Жизненный цикл агента

1. Агент получает ограниченный список через `candidates` и полный необходимый контекст через `task --include ...`.
2. `claim` атомарно переводит Prepared-задачу в InProgress и возвращает `leaseId`.
3. Все дальнейшие execution-команды требуют точного совпадения `agentId + leaseId`.
4. `question` переводит execution в `AwaitingInput`; `answer` возвращает его в `Active`, когда отвечены все вопросы.
5. `result` сохраняет черновой итог без завершения задачи. `execution complete` проверяет критерии, сохраняет итог и завершает задачу в одной командной границе.
6. `release` возвращает задачу в Prepared. Следующий `claim` создаёт новый lease, а предыдущая попытка остаётся в ограниченном журнале.

Старая команда `complete` работает как раньше только для задач без активного execution. Активную lease-bound задачу может завершить только `execution complete`.

Структурированное `AgentExecution` является источником истины. CLI поддерживает одну читаемую проекцию между служебными маркерами в Description. Непарный или повторный marker приводит к `descriptionMarkerConflict` без записи. Вводимые тексты считаются данными, а ссылки сохраняются, но не открываются.

## Записи и восстановление

Перед записью CLI проверяет загрузку, дубликаты идентификаторов и связи графа. Записи сериализуются блокировкой каталога и выполняются через атомарную замену файлов.

Командная операция дополнительно ведёт журнал исходных и новых образов. До записи отметки commit следующий доступ под блокировкой откатывает исходные файлы; после отметки докатывает новые. Поэтому `create --parent` не оставляет частичную дочернюю задачу или одностороннюю связь после аварийного завершения процесса. Гарантия относится к восстановлению процесса; устойчивость к внезапной потере питания отдельно не заявляется.

При `outcomeUnknown` нельзя автоматически повторять мутацию: сначала нужно перечитать авторитетный снимок задачи. Ошибки execution содержат доступный `authoritativeTask`, но не раскрывают чужой lease.

Пример JSON-ошибки:

```json
{
  "success": false,
  "error": {
    "kind": "leaseMismatch",
    "message": "Task is not owned by the supplied agent and lease."
  },
  "authoritativeTask": {
    "id": "...",
    "status": "InProgress",
    "executionAgentId": "agent-a",
    "executionState": "Active"
  }
}
```

## Семантика доступности

- незавершённые `ContainsTasks` блокируют содержащую задачу;
- незавершённые прямые `BlockedByTasks` блокируют задачу;
- блокеры предков через `ParentTasks` наследуются дочерними задачами;
- невыполненные `CompletionCriteria` блокируют завершение, но не старт;
- будущий `PlannedBeginDateTime` блокирует старт, но не доступность завершения;
- отсутствующие ссылки являются ошибкой валидации, но не runtime-блокером.

CLI работает только с файловым хранилищем. Отсутствующие, повреждённые или server-mode настройки дают ошибку без чтения учётных данных, соединения с сервером, записи настроек или создания каталога задач.

## Коды завершения

- `0` — команда успешно завершена;
- `1` — ошибка загрузки/валидации, отказ бизнес-правила, неизвестная задача или неподтверждённый результат операции;
- `2` — неверные аргументы.
