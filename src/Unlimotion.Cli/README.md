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

Операционный [скилл `unlimotion-cli`](../../.agents/skills/unlimotion-cli/SKILL.md) помогает агенту читать и изменять задачи через **уже установленный** CLI. Скилл не устанавливает `Unlimotion.Cli` и не заменяет команды `dotnet tool` выше. Перед работой агент проверяет версию и возможности фактически запущенного бинарника; инструкция сохраняет отдельный маршрут для 1.31.1 и проверенный контракт 1.32.0.

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
$packageVersion = "1.31.0-local.1"
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
```

`--include`, `--link` и `--parent` можно указывать несколько раз. В `--include` также принимается список через запятую. `task` без `--include` сохраняет прежний JSON-контракт анализа доступности.

`search` ищет подстроку в title или ID по всем статусам, включая недоступные и архивные задачи. По умолчанию возвращает 20 элементов, максимум 100 за вызов. JSON содержит `items`, `totalCount`, `nextCursor`; передавайте `nextCursor` с теми же фильтрами и тем же `--tasks`, пока он не станет `null`. Порядок: title без учёта регистра, затем точный title и ID. Между страницами граф может меняться: для важной работы дедуплицируйте ID и при необходимости начните поиск заново. `--root` повторяем и включает корни и их потомков.

`task --include details,relations,criteria,history,execution` возвращает ETag и выбранные секции. В `details.repeater` видны тип, период, флаг `afterComplete`, pattern и неизвестные расширенные поля, если они есть; без повторителя значение `null`. В текстовом режиме запрошенные секции и ETag также печатаются. Старый ответ без `--include` сохранён.

## Декларативное изменение и проверка исхода

`apply schema --format json` отдаёт JSON Schema request v1 из установленного пакета. `apply example set-field|add-relation|create-task --format json` печатает полноценные шаблоны; все `example-*` ID и ETag замените реальными значениями перед preview. Схема проверяет синтаксис; графовые правила, ETag, статусные ограничения и защищённые маркеры описаны в `help apply` и проверяются `--dry-run`.

Для создания с заранее выбранным ID используйте шаблон `create-task`, задайте уникальные `applicationId`, `newTaskId`, ссылку `proposalRefs` на согласованное поручение и точный `--tasks`:

```powershell
unlimotion-cli apply example create-task --format json > request.json
# Отредактируйте примерные ID, текст, author и reason.
unlimotion-cli apply --tasks $tasksPath --request request.json --dry-run --format json
unlimotion-cli apply --tasks $tasksPath --request request.json --format json
unlimotion-cli task --tasks $tasksPath --id $newTaskId --include details,relations,criteria --format json
```

Отправляйте запись только после успешного preview с тем же файлом запроса. При потере ответа или `outcomeUnknown` сначала выполните `apply inspect --request request.json --tasks $tasksPath --format json`, затем read-back затронутых ID. `receiptMatched` подтверждает receipt для точного `applicationId + requestHash`, но текущее состояние может уже отличаться. `desiredStatePresent` без receipt подтверждает только наблюдаемое конечное состояние, а не историю применения. `readyForPreview` означает, что исходные preconditions совпали и обычный preview проходит. `needsReconciliation` требует ручной сверки; не повторяйте запись автоматически. Хешируется декодированный текст JSON, включая пробелы и переносы, так что не форматируйте исходный request между отправкой и inspection. `inspect` не выполняет новую application mutation; при доступе storage может восстановить ранее незавершённый журнал.

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
