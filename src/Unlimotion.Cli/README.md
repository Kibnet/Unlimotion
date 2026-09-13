# Unlimotion CLI

Консольный клиент для чтения и изменения локального каталога задач Unlimotion без запуска UI. Он предназначен в том числе для агентов, которым нужны те же правила доступности и завершения, что использует desktop-приложение.

## Установка с NuGet.org

```powershell
dotnet tool install --global Unlimotion.Cli
unlimotion-cli status --format json
```

Обновление установленного инструмента:

```powershell
dotnet tool update --global Unlimotion.Cli
```

CLI требует совместимый .NET 10 SDK/runtime. Явный `--tasks` всегда имеет приоритет. Без него CLI читает путь активного локального пространства задач из `%USERPROFILE%\Documents\Unlimotion\Settings.json`.

Для локальной сборки рекомендуется отдельный каталог инструмента, чтобы агент не изменял глобальную установку пользователя:

```powershell
dotnet pack src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -o artifacts\tools
dotnet tool install --tool-path C:\tmp\unlimotion-cli-tool --add-source artifacts\tools Unlimotion.Cli --version 1.30.1
```

## Команды

```powershell
unlimotion-cli status [--tasks <task-dir>] [--format text|json]
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
```

`--include`, `--link` и `--parent` можно указывать несколько раз. В `--include` также принимается список через запятую. `task` без `--include` сохраняет прежний JSON-контракт анализа доступности.

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
