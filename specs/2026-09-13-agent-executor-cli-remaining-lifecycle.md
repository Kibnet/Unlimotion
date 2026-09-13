# Полный жизненный цикл агента-исполнителя через CLI

## 0. Метаданные

- Тип (профиль): `product-system-design` + `testing-dotnet`; расширенная QUEST SPEC.
- Владелец: Codex `/root`.
- Масштаб: large — публичный CLI-контракт, сохраняемая модель, конкурентные записи и многофайловая мутация графа.
- Целевое семейство / базовое поведение: Не применимо — изменение не зависит от модели ИИ.
- Поверхность: локальный `unlimotion-cli` для файлового пространства задач.
- Среда выполнения: .NET 10; ветка `feat/agent-executor-cli`; исходный реализованный срез — коммит `10fccde8`.
- Базовая оценка / доказательства: команды `candidates` и `claim`, конкурентный запуск двух CLI-процессов, старый JSON без `AgentExecution`, задача с критериями, вопросами, результатом, освобождением, повторителем и связанной дочерней задачей.
- Целевой релиз / ветка: продолжение в отдельном worktree `C:\Users\Kibnet\.codex\worktrees\agent-executor-cli\Unlimotion`; публикация пакета, push, PR и merge не входят в SPEC.
- Ограничения:
  - до точной фразы `Спеку подтверждаю` меняется только эта SPEC;
  - все записи выполняются через общий движок задач, блокировку каталога и проверку повторным чтением;
  - серверное пространство задач, desktop UI и внешний обмен сообщениями не расширяются;
  - содержимое задач, вопросы, ответы и ссылки являются данными, а не командами агенту или разрешением внешних действий.
- Связанные ссылки:
  - завершённый срез: `specs/2026-09-13-agent-executor-cli.md`, коммит `10fccde8`;
  - CLI: `src/Unlimotion.Cli/Program.cs`, `src/Unlimotion.Cli/README.md`;
  - доменная модель: `src/Unlimotion.Domain/TaskItem.cs`, `AgentExecutionRecord.cs`;
  - общая граница записи: `src/Unlimotion.TaskTreeManager/TaskGraphCommandService.cs`;
  - контрактные тесты: `src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs`, `TaskGraphCommandServiceTests.cs`.

## 1. Обзор / Цель

Довести начальный `candidates + claim` до полного безопасного протокола агента-исполнителя. После реализации агент сможет получить достаточный контекст задачи, задать и закрыть уточняющие вопросы, записать проверяемый результат, завершить или освободить задачу, повторно взять освобождённую задачу и создать связанную дочернюю задачу без прямого редактирования JSON.

Контракт результата:

- Успех означает:
  - каждое изменение исполнения требует точного совпадения `AgentId + LeaseId`;
  - Description, критерии, связи, история и execution доступны одним ограниченным CLI-снимком;
  - вопросы, ответы, результат, ссылки, освобождение и завершение сохраняются атомарно и видны пользователю в одном защищённом блоке Description;
  - завершение повторяющейся задачи не переносит lease и результат в следующий occurrence;
  - создание follow-up с родителями оставляет либо целиком согласованный граф, либо исходный граф;
  - существующие команды и старые JSON-файлы сохраняют совместимость.
- Итоговый артефакт: расширенные команды CLI, модель полного жизненного цикла исполнения, безопасный формирователь служебного блока, восстановимая операция создания, README и автоматизированные доказательства.
- Правила остановки:
  - при конфликте lease/служебного блока, невалидном графе или недоказанном результате записи не повторять мутацию автоматически;
  - при `outcomeUnknown` вернуть доступный авторитетный снимок и остановиться;
  - не изменять сетевые DTO/серверный execution без отдельной SPEC;
  - EXEC считается завершённым только после целевых тестов, сборки и последовательного полного TUnit либо честно зафиксированного внешнего/флейкового остатка с изолированной перепроверкой.

## 2. Текущее состояние (AS-IS)

- Коммит `10fccde8` добавил `candidates --limit`, фиксированную выборку Prepared/startable, детерминированную сортировку и `claim --id --agent --expected-status Prepared`.
- Claim выполняется внутри существующей блокировки графа, переводит задачу в InProgress, создаёт UUID lease, сохраняет минимальную `AgentExecution` и добавляет marker v1 в Description.
- Два конкурентных CLI-процесса дают одного победителя, один `claimConflict` и один переход истории статуса.
- `AgentExecutionRecord` пока содержит только `AgentId`, `LeaseId`, `State=Active`, `ClaimedAt`, `UpdatedAt`.
- Любой уже существующий marker сейчас блокирует запись: безопасной замены блока, release и re-claim нет.
- `task --id` возвращает только анализ доступности. Вопросы, ответы, итог, ссылки и аудит попыток через CLI недоступны.
- Старая `complete` не знает об активном lease. Ограниченного create с родителями и очистки execution при создании повторного occurrence нет.
- Сетевые DTO намеренно не включают локальное `AgentExecution`; это сохраняется, потому что серверный протокол вне области.

## 3. Проблема

Claim уже доказывает владение, но после него агент снова вынужден обходить CLI: полный контекст нельзя получить одной командой, а вопросы, ответы, результаты, release и связанное создание требуют прямой работы с файлами. Получается безопасный вход в процесс без безопасного продолжения и выхода.

## 4. Цели дизайна

- Один lease-bound командный протокол для всех изменений execution.
- Структурированные данные как источник истины и читаемая проекция в Description.
- Ограниченные ответы и детерминированные ошибки для автоматизации.
- Переиспользование общей блокировки, валидации графа и проверки повторным чтением.
- Совместимость старых задач, текущих CLI-команд и файлового desktop-хранилища.
- Тестируемая атомарность однофайловых и многофайловых мутаций.

## 5. Вне цели (чего НЕ делаем)

- Не добавляем серверные lease, удалённую синхронизацию execution или сетевую авторизацию агента.
- Не меняем Avalonia UI и не добавляем отдельный статус задачи `AwaitingInput`.
- Не создаём универсальный редактор всех полей и связей графа.
- Не вводим автоматическое истечение lease или фоновый watchdog.
- Не отправляем вопросы в почту/мессенджеры и не открываем ссылки.
- Не публикуем NuGet, release, push, PR или merge.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент/файл | Ответственность |
| --- | --- |
| `Unlimotion.Domain/AgentExecutionRecord.cs` | Полные состояния execution, вопросы, результат и журнал попыток. |
| `TaskItemSnapshot.cs` | Глубокая копия всех новых коллекций и значений. |
| Новый `AgentExecutionDescriptionRenderer` в TaskTreeManager | Разбор, проверка и точная замена единственного marker-блока. |
| `TaskGraphCommandService.cs` | Lease-bound question/answer/result/complete/release/re-claim и create под блокировкой. |
| `TaskTreeManager.cs` / FileTaskStorage | Очистка повтора и восстановимая многофайловая мутация create. |
| `Unlimotion.Cli/Program.cs` | Опции, стабильные DTO, типизированные ошибки и ограничение ответов. |
| `Unlimotion.Cli/README.md` | Публичный lifecycle, примеры, trust boundary и восстановление ошибок. |
| `Unlimotion.Test` | Контрактные, конкурентные, persistence, recovery и regression-тесты. |

### 6.2 Детальный дизайн

Сохраняемая модель расширяется без обязательной миграции:

```text
AgentExecutionRecord
  AgentId, LeaseId
  State: Active | AwaitingInput | Released | Completed
  ClaimedAt, UpdatedAt
  Questions: [{ Id, Text, AskedAt, Answer?, AnsweredAt? }]
  Result?: { Summary, Links[], RecordedAt }
  ReleasedAt?, ReleaseReason?
  PreviousAttempts: [{ AgentId, LeaseId, ClaimedAt, ReleasedAt?, ReleaseReason?, CompletedAt? }]
```

CLI-контракт:

```powershell
unlimotion-cli candidates --limit 20 [--status Prepared] [--startable true] [--sort default] --format json
unlimotion-cli task --id <id> --include details,relations,criteria,history,execution --format json
unlimotion-cli execution question --id <id> --agent <agent> --lease <lease> --text <text> --format json
unlimotion-cli execution answer --id <id> --agent <agent> --lease <lease> --question-id <id> --text <text> --format json
unlimotion-cli execution result --id <id> --agent <agent> --lease <lease> --summary <text> [--link <uri>] --format json
unlimotion-cli execution complete --id <id> --agent <agent> --lease <lease> --summary <text> [--link <uri>] --format json
unlimotion-cli release --id <id> --agent <agent> --lease <lease> --reason <text> --format json
unlimotion-cli create --title <text> [--description <text>] [--parent <id>] --format json
```

- Текущий `candidates --limit` остаётся валидным. Новые опции имеют указанные значения по умолчанию; `--sort` пока принимает только `default`, чтобы не закреплять неподдерживаемые режимы.
- `task --include` возвращает только запрошенные секции. Relations содержат идентификатор, title, status и тип связи для одношаговых соседей; рекурсивного обхода нет. Коллекции сортируются по ID, чтобы JSON был стабильным.
- Любая write-команда execution требует непустые `AgentId`, `LeaseId`, точное совпадение обоих значений и допустимое текущее состояние. Ошибка возвращает код 1 и один из `leaseMismatch`, `executionStateDenied`, `questionNotFound`, `claimConflict`, `descriptionMarkerConflict`, `businessRuleDenied`, `outcomeUnknown`.
- `question` добавляет вопрос с GUID и переводит execution в `AwaitingInput`; задача остаётся InProgress.
- `answer` меняет только указанный неотвеченный вопрос. Когда неотвеченных вопросов больше нет, состояние становится `Active`.
- `result` создаёт или заменяет черновой результат только при `Active`; статус задачи не меняется.
- `execution complete` в одной командной границе валидирует lease, отсутствие неотвеченных вопросов и существующие критерии завершения, записывает результат, обновляет marker, переводит execution и задачу в Completed и проверяет запись повторным чтением.
- Старая `complete` сохраняет прежнюю семантику для задач без Active/AwaitingInput execution. При активном execution она отказывает без записи: завершение владельца возможно только через `execution complete`.
- `release` разрешён для Active/AwaitingInput: сохраняет аудит, переводит execution в Released, задачу в Prepared и обновляет marker. Новый claim разрешён только из Prepared+Released, переносит завершённую попытку в `PreviousAttempts` и создаёт новый lease.
- История `PreviousAttempts` ограничена 20 последними элементами; усечение удаляет самые старые записи детерминированно и документируется в JSON-ответе `auditTruncated:true`.
- Ввод ограничивается: agent 200 символов; question/answer/reason 4000; summary 16000; до 20 links; только абсолютные `http`, `https`, `file`. Управляющие символы и буквальные marker-строки запрещены в выводимых полях.
- Marker parser принимает только ноль либо один корректно спаренный блок v1. При одном блоке заменяется только диапазон от start до end; prefix и suffix сохраняются посимвольно. Непарные, вложенные или повторные markers дают `descriptionMarkerConflict` до записи.
- `create --parent` создаёт Prepared-задачу и симметричные containment-связи. Все родители должны существовать, быть уникальны и принадлежать тому же графу. Операция использует журнал исходных образов и recovery под блокировкой: после сбоя доступен либо старый, либо целиком новый валидный граф.
- При создании следующего occurrence повторителя `AgentExecution` и единственный корректный marker удаляются из клона. Повреждённый marker прерывает completion до создания частичного occurrence.
- JSON write-ответы содержат `success`, `task`, `execution`, `changedTaskIds`, `storageRevision`, `didMutate`; ошибки содержат `success:false`, `error.kind/message` и доступный `authoritativeTask`, но не раскрывают чужой lease в сообщении.
- Производительность: `candidates <=100`; relations — один шаг; history и attempts имеют документированные лимиты; не добавляется полный обход графа поверх уже выполняемого анализа доступности.
- Visual planning artifact: Не применимо — desktop UI не меняется.
- UI test video evidence: Не применимо — изменения относятся к CLI и файловому контракту.

### 6.3 Наблюдаемые пользователем сценарии

| Сценарий | Действие пользователя / триггер | Ожидаемый видимый результат | Необходимое доказательство | Покрыт AC |
| --- | --- | --- | --- | --- |
| Получение контекста | `task --include ...` | Один ограниченный JSON со всеми запрошенными секциями | CLI integration snapshot | AC-1 |
| Уточнение | `execution question`, затем `answer` | AwaitingInput → Active, вопрос и ответ видны в execution/Description | Интеграционный тест | AC-2 |
| Запись итога | `execution result` | Результат и валидные ссылки сохранены без смены статуса | Тест сохранения | AC-3 |
| Завершение | `execution complete` | Результат + Completed одной подтверждённой мутацией | Command-service + CLI test | AC-4 |
| Освобождение и повторный claim | `release`, затем новый `claim` | Новый lease, старая попытка остаётся в аудите | Конкурентный тест жизненного цикла | AC-5 |
| Дочерняя задача | `create --parent` | Новая Prepared-задача и симметричные связи | Тест восстановления при ошибках | AC-6 |
| Повторитель | завершение occurrence с execution | Новый occurrence не содержит execution/marker | Регрессионный тест повторителя | AC-7 |
| Старый клиент/файл | чтение и обычное сохранение старой задачи | Нет миграции и потери неизвестных данных | Round-trip/mapping tests | AC-8 |

### 6.4 Матрица состояний и взаимодействий

| Текущее состояние | Триггер | Ожидаемый переход / результат | Пустой ввод / ошибка / конкуренция | Примечания |
| --- | --- | --- | --- | --- |
| InProgress + Active | question | AwaitingInput, вопрос добавлен | пустой/marker input — invalidArguments | Статус задачи не меняется |
| AwaitingInput | answer последнего вопроса | Active | wrong lease/question — без записи | Повторный answer запрещён |
| Active | result | Active + Result | invalid URI — без записи | Ссылки не открываются |
| Active, criteria satisfied | execution complete | Completed + execution Completed | criteria/questions/lease fail — без записи | Одна блокировка и проверка |
| Active/AwaitingInput | old complete | executionStateDenied | concurrent release/complete: один победитель | Старый контракт защищён |
| Active/AwaitingInput | release | Prepared + Released | wrong lease — без записи | Нет TTL |
| Prepared + Released | claim | InProgress + новый Active lease | два claim: один конфликт | Аудит переносится |
| Completed repeater | execution complete | новый чистый occurrence | marker conflict — вся операция отклонена | Нет наследования результата |

### 6.5 Журнал решений

| Решение | Владелец | Выбранный вариант | Уверенность | Риск предположения | Нужно решение пользователя до EXEC |
| --- | --- | --- | ---: | --- | --- |
| Ожидание ответа | агент | `AwaitingInput` — состояние execution, TaskStatus остаётся InProgress | 0.96 | UI отдельно не показывает ожидание | Нет |
| Источник истины | агент | `AgentExecution`; Description — производная проекция | 0.97 | Требуется строгая синхронизация | Нет |
| Восстановление владения | агент | Только явный release, без TTL | 0.93 | Брошенную задачу нужно освобождать явно | Нет |
| Аудит повторного claim | агент | 20 последних попыток + флаг усечения | 0.88 | Старейшие детали ограниченно теряются | Нет |
| Создание с родителем | агент | Только containment, одна восстанавливаемая транзакция | 0.90 | Не покрывает произвольные связи | Нет |
| Сетевое исполнение | агент | Вне области; DTO продолжают игнорировать поле | 0.97 | Серверный клиент не видит lease | Нет |

### 6.6 Матрица среды выполнения, конфигурации и данных

| Область контракта | Текущий источник истины | Ожидаемое изменение | Совместимость / миграция | Проверка |
| --- | --- | --- | --- | --- |
| Каталог задач | `TaskDirectoryResolver` | Без изменений | `--tasks` сохраняет приоритет | Resolver tests |
| JSON задачи | `TaskItem` + FileTaskStorage | Полная nullable execution-модель | Старые файлы без миграции | Round-trip tests |
| Description | Пользовательский Markdown | Один управляемый marker v1 | Текст вне блока сохраняется | Byte/ordinal assertions |
| CLI JSON | DTO в `Program.cs` | Новые секции/команды/ошибки | Старые ответы по умолчанию стабильны | Contract snapshots |
| Однофайловая запись | TaskGraphCommandService | Lease-bound mutations | Та же directory lock | Concurrency tests |
| Многoфайловый create | FileTaskStorage/TaskTreeManager | Journal + recovery | Нет полусвязей | Fault matrix |
| Сетевые DTO | AppModelMapping | Без поля execution | Явное Ignore сохраняется | Mapping tests |

## 7. Бизнес-правила / Алгоритмы

1. Владение подтверждается только точной парой `AgentId + LeaseId`; знание одного AgentId недостаточно.
2. Любая execution-мутация повторно читает граф после получения write lock и проверяет авторитетное состояние.
3. `AwaitingInput` запрещает result/complete, пока все вопросы не отвечены.
4. `execution complete` сначала валидирует все предусловия и только затем меняет result, marker, execution и TaskStatus.
5. Release не завершает задачу и не удаляет аудит.
6. Re-claim создаёт новый lease; старый lease никогда не становится валидным снова.
7. Пользовательский текст вне marker сохраняется посимвольно.
8. Следующий occurrence повторителя — новый execution-контекст.
9. Create с parent является одной мутацией графа, а не серией независимых Save.
10. Любой `outcomeUnknown` запрещает автоматический повтор команды агентом.

## 8. Точки интеграции и триггеры

- `CliOptions.Parse` — подкоманды `execution`, повторяемые `--include`, `--link`, `--parent`, строгая применимость опций.
- `TaskGraphCommandService` — все lease-bound операции и создание follow-up.
- `TaskItemSnapshot.Clone` — полная глубокая копия execution.
- `TaskTreeManager.CreateOccurrenceClone` — очистка execution и marker.
- FileTaskStorage init/read under lock — завершение или откат незакрытого журнала create.
- `Program.WriteJson*` — единая типизированная ошибка и авторитетный снимок.

## 9. Изменения модели данных / состояния

- Добавляются состояния `AwaitingInput`, `Released`, `Completed`, коллекции Questions/PreviousAttempts и Result.
- Все поля execution persisted; `CanStart`/`CanComplete` остаются вычисляемыми.
- Отсутствие `AgentExecution` сохраняет старую семантику.
- Никакое новое поле не добавляется в сетевые DTO в этой области.
- Journal create является служебным recoverable-артефактом хранилища и удаляется только после подтверждённой фиксации/отката.

## 10. Миграция / Rollout / Rollback

- Старые задачи читаются с `execution:null`; фоновая миграция не выполняется.
- Первый вопрос/result/release расширяет уже созданную claim-запись.
- Старые CLI-команды продолжают работать, кроме защитного отказа old `complete` при активном execution.
- Откат к старому CLI допустим только для задач без активного lease; активные задачи сначала освобождаются новым CLI.
- При crash create следующий read под lock восстанавливает целый старый или новый граф и затем выполняет validate.
- Публикация/установка новой версии — отдельная задача после реализации и проверки.

## 11. Тестирование и критерии приёмки

Критерии приёмки:

- AC-1: `task --include` возвращает только запрошенные details/relations/criteria/history/execution, стабильный порядок и ограниченные одношаговые связи; ответ без include не меняется.
- AC-2: question/answer требуют совпадающий lease, корректно меняют AwaitingInput/Active и не меняют TaskStatus.
- AC-3: result сохраняет summary/до 20 валидных links, обновляет единственный marker и не меняет статус; невалидный ввод не меняет файл.
- AC-4: execution complete атомарно сохраняет результат и оба состояния Completed; old complete при активном/ожидающем execution отказывает без изменений.
- AC-5: release сохраняет причину/время, возвращает задачу в Prepared; re-claim создаёт новый lease, хранит ограниченный аудит и отклоняет старый lease.
- AC-6: create с уникальными существующими parent создаёт симметричный валидный граф; fault на каждой стадии восстановления не оставляет полусвязей.
- AC-7: следующий occurrence не наследует AgentExecution/marker; конфликт marker отменяет всю completion-мутацию.
- AC-8: старый JSON, ExtensionData, resolver, mapping и существующие CLI-ответы сохраняют совместимость.
- AC-9: parser отклоняет неизвестные/чужие опции, управляющие символы, marker injection, превышение лимитов и неверные URI до записи.
- AC-10: README документирует полный lifecycle, ошибки, локальную область, отсутствие TTL/авторетрая и безопасное восстановление outcomeUnknown.

Команды проверки:

```powershell
dotnet run -p:UseSharedCompilation=false --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/UnlimotionCliIntegrationTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet run --no-build -p:UseSharedCompilation=false --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskGraphCommandServiceTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet run --no-build -p:UseSharedCompilation=false --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskStatusMappingTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet build src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Debug -p:UseSharedCompilation=false --no-restore
dotnet run --no-build -p:UseSharedCompilation=false --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --maximum-parallel-tests 1 --output Normal
```

Правила остановки: тесты запускать последовательно; не дублировать зависший процесс до проверки PID/вывода; на первой воспроизводимой регрессии остановить расширение области, исправить причину и повторить целевой набор; нестабильный сбой полного набора перепроверить изолированно и зафиксировать отдельно от доказательств изменённого поведения.

### Матрица критериев приёмки и тестов

| Критерий приёмки | Автоматизированный тест | Ручная / журнальная проверка | Артефакт доказательства | Причина отсутствия теста |
| --- | --- | --- | --- | --- |
| AC-1 | CLI snapshot/include tests | Проверка ограничений JSON | TUnit report | — |
| AC-2 | question/answer + wrong lease tests | Проверка сохранённого marker | TUnit report | — |
| AC-3 | result/input-validation tests | URI/marker read-back | TUnit report | — |
| AC-4 | complete/criteria/concurrency tests | Авторитетный read-back | TUnit report | — |
| AC-5 | release/re-claim/audit tests | Проверка старого lease | TUnit report | — |
| AC-6 | create fault matrix | `validate --format json` после recovery | TUnit report/log | — |
| AC-7 | repeater regression tests | JSON/Description нового occurrence | TUnit report | — |
| AC-8 | round-trip/resolver/mapping/regression | Сравнение старых JSON-оболочек | TUnit report | — |
| AC-9 | parser/negative tests | До/после hash файла | TUnit report | — |
| AC-10 | README/help contract test | Ручная сверка примеров | README + help output | — |

## 12. Риски и граничные случаи

- Частично записанный marker или ручной дубликат — fail closed без изменения структуры.
- Одновременные answer/release/complete — один победитель под lock, остальные получают авторитетный отказ.
- Падение после Save, но до ответа — `outcomeUnknown`, без автоматического ретрая.
- Старый desktop-клиент может сохранить задачу без понимания execution; файловый путь обязан сохранить неизвестные/структурированные данные, сетевой режим вне области.
- Многофайловый create увеличивает сложность хранилища; матрица инъекций ошибок обязательна до финала.
- Полный TUnit содержит тяжёлый roadmap UI-тест с наблюдавшимся таймаут-флейком; он не заменяет целевые доказательства и перепроверяется изолированно при сбое.

### Ожидаемые замечания пользователя

| Вероятное замечание | Почему вероятно | Как учтено в SPEC / плане | Статус |
| --- | --- | --- | --- |
| «Агент всё ещё не видит контекст задачи» | Это был главный разрыв после claim | AC-1 требует единый include-снимок | учтено |
| «Почему старая complete может обойти lease?» | Совместимость не должна ломать владение | Active/AwaitingInput явно блокируют old complete | учтено |
| «Release затрёт историю работы» | Re-claim заменяет текущую запись | PreviousAttempts + лимит + auditTruncated | учтено |
| «Create оставит поломанный граф» | Связи хранятся в нескольких файлах | Журнал восстановления + матрица инъекций ошибок | учтено |
| «Следующий повтор унаследует чужой результат» | Repeater клонирует Description | AC-7 очищает поле и marker атомарно | учтено |

### Контрольный список предотвращения переделок

- [x] Названы команды и видимые JSON-результаты.
- [x] Каждый пользовательский сценарий связан с AC и доказательством.
- [x] Самостоятельные решения перечислены в журнале решений.
- [x] Вероятные возражения закрыты контрактом.
- [x] Ролевое ревью выполнено ниже.
- [x] AC описывают проверяемый результат, а не подготовительные действия.
- [x] EXEC имеет последовательный путь доказательства.

## 13. План выполнения

1. Добавить ожидаемо падающие контрактные тесты полного `task --include` и input-validation; расширить модель и deep clone.
2. Выделить marker parser/renderer и покрыть сохранение prefix/suffix, конфликт и injection.
3. Реализовать question/answer/result с единым lease guard и повторным чтением.
4. Реализовать execution complete, защиту old complete, release и re-claim/audit.
5. Реализовать очистку repeating occurrence и её rollback вместе с completion.
6. Реализовать create с parent, журнал восстановления и fault matrix.
7. Обновить README/help, выполнить целевые наборы, сборку и полный последовательный TUnit.
8. Провести post-EXEC review; не push/PR без отдельного указания.

## 14. Открытые вопросы

Нет блокирующих вопросов. Значения лимитов, отсутствие TTL, containment-семантика parent и локальная область зафиксированы как проектные решения; их можно изменить до подтверждения SPEC.

## 15. Соответствие профилю

- Профиль: `product-system-design`, `testing-dotnet`, расширенный QUEST.
- Выполненные требования профиля: определены пользовательский workflow, публичный API, состояние/переходы, данные, миграция, обратная совместимость, concurrency/recovery, негативные сценарии и сопоставление AC с тестами. UI override не применим, потому что UI-поведение не меняется.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Domain/AgentExecutionRecord.cs` | Полная модель lifecycle | Вопросы, результат, release и аудит |
| `src/Unlimotion.TaskTreeManager/TaskItemSnapshot.cs` | Deep clone | Без потери/общих ссылок |
| Новый renderer в TaskTreeManager | Marker parse/render | Сохранение пользовательского текста |
| `src/Unlimotion.TaskTreeManager/TaskGraphCommandService.cs` | Lease-bound команды и create | Атомарные правила |
| `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs` | Очистка occurrence | Новый чистый execution-контекст |
| `src/Unlimotion.FileTaskStorage/FileTaskStorage.cs` и новые journal-типы | Recovery многофайловой мутации | Целостный граф после crash |
| `src/Unlimotion.Cli/Program.cs` | Parser/DTO/errors/help | Машинный публичный контракт |
| `src/Unlimotion.Cli/README.md` | Документация lifecycle | Самодостаточное применение агентом |
| `src/Unlimotion.Test/*` | Контрактные/fault/regression тесты | Доказательства AC |
| `specs/2026-09-13-agent-executor-cli-remaining-lifecycle.md` | Эта SPEC | Источник истины остатка |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Контекст | Короткий availability-анализ | Подключаемый полный ограниченный снимок |
| Владение после claim | Только Active lease | Полный question/result/complete/release lifecycle |
| Description | Marker можно только впервые добавить | Единственный проверяемый и безопасно заменяемый блок |
| Завершение | Old complete не знает lease | Lease-bound completion; old complete защищён |
| Повторный claim | Невозможен | Новый lease с сохранением ограниченного аудита |
| Follow-up | Нет create через CLI | Prepared child + parents одной recoverable-мутацией |
| Повторитель | Может скопировать marker | Чистый новый occurrence |
| Документация | Новые команды ещё не описаны | Полный lifecycle и recovery rules |

## 18. Альтернативы и компромиссы

- Вариант: хранить execution только в Description.
  - Плюсы: нет новой модели.
  - Минусы: хрупкий парсинг и слабые concurrency-инварианты.
- Вариант: отдельные sidecar-файлы.
  - Плюсы: изоляция данных агента.
  - Минусы: сложнее синхронизация, backup и атомарность.
- Вариант: добавить серверный протокол сразу.
  - Плюсы: единая модель для всех источников.
  - Минусы: резко расширяет scope, авторизацию и распределённые блокировки.
- Выбранное решение: nullable execution внутри локального TaskItem + производный marker + существующая directory lock; оно минимально продолжает уже закоммиченный срез и сохраняет понятную границу сервера.

## 19. Результат контроля качества и ревью

### Результат проверки SPEC

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема и границы конкретны. |
| B. Качество дизайна | 6-10 | PASS | Команды, состояния, marker, create/recovery и миграция заданы. |
| C. Безопасность изменений | 11-13 | PASS | Lease, fail-closed, outcomeUnknown, rollback и ограничения ввода учтены. |
| D. Проверяемость | 14-16 | PASS | 10 AC связаны с тестами и командами. |
| E. Готовность к автономной реализации | 17-19 | PASS | План и решения однозначны; блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | Контракт продукта и .NET-валидация отражены. |

Итог: ГОТОВО к запросу подтверждения.

### Результат оценки SPEC

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Описан только остаток после `10fccde8`; server/UI/delivery исключены. |
| 2. Понимание текущего состояния | 5 | Зафиксированы фактические команды, модель и ограничения marker. |
| 3. Конкретность целевого дизайна | 5 | Синтаксис, DTO, состояния, алгоритмы и лимиты определены. |
| 4. Безопасность | 5 | Lease, marker conflict, recovery и rollback имеют fail-closed контракт. |
| 5. Тестируемость | 5 | Есть матрица AC, concurrency/fault/compatibility проверки. |
| 6. Готовность к автономной реализации | 5 | Нет скрытого продуктового выбора до EXEC. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению после явного подтверждения.

### Результат ролевого ревью

| Роль | Применимость | Вопрос ревью | Вердикт | Необходимые изменения SPEC |
| --- | --- | --- | --- | --- |
| Бизнес-аналитик / предметный процесс | применимо | Полон ли путь claim → контекст → вопросы → результат → завершение/освобождение? | PASS | Нет. |
| UX / дизайнер | не применимо | Desktop UI не меняется. | PASS | Текстовая проекция проверяется контрактно. |
| Тестировщик / валидация | применимо | Есть ли доказательство всех состояний, ошибок и конкурентных путей? | PASS | Матрица инъекций ошибок и изолированная проверка нестабильных сбоев обязательны. |
| Разработчик / архитектор | применимо | Согласованы ли модель, блокировка, marker и восстановление? | PASS | Формирователь marker отделён; серверная граница сохранена. |
| Поставка / эксплуатация / безопасность | применимо | Ограничены ли ввод, область и внешние эффекты? | PASS | Нет публикации, URL не открываются, outcomeUnknown не повторяется. |

### Ревью после SPEC

- Статус / решение об остановке: PASS; EXEC только после точной фразы подтверждения пользователя.
- Проверка области и доказательств: проверены исходная SPEC, коммит `10fccde8`, фактические контракты CLI, домена, командного сервиса и маппинга, а также канонический шаблон.
- Проверка контракта: оставшиеся AC отделены от уже работающих `candidates + claim`; старый CLI и сетевой DTO-контур имеют явные границы совместимости.
- Проверка рисков и ролей: проверены неверный lease, конкурентные release/complete, повреждение и инъекция marker, авария многофайлового create, повторитель и outcomeUnknown.
- Находки: HIGH — риск обхода lease через old complete; закрыт явным `executionStateDenied`. HIGH — полусвязи create; закрыты журналом восстановления и матрицей инъекций ошибок. MEDIUM — потеря release-аудита; закрыта PreviousAttempts с лимитом и флагом усечения. MEDIUM — наследование marker повторителем; закрыто AC-7. LOW — неоднозначность опций candidates; закреплены значения по умолчанию и единственный sort.
- Исправления и повторное ревью: требования добавлены в дизайн, AC, матрицу состояний и тестовую матрицу; повторная самопроверка не выявила незакрытых блокирующих вопросов.
- Ручная проверка и остаточные риски: независимый reviewer с технически read-only средой в текущей поверхности недоступен; выполнена самостоятельная проверка контрпримеров. Самая инвазивная часть — восстановимое создание нескольких файлов — не считается готовой без матрицы инъекций ошибок в EXEC.

### Ревью после EXEC

- Статус / решение об остановке: PASS. Все BLOCKER/HIGH/MEDIUM находки закрыты кодом и проверками; публикация, commit, push, PR и merge не выполнялись.
- Проверка области и доказательств: изменены только доменная модель execution, CLI-контракты, командный сервис, файловое восстановление, документация и соответствующие тесты. Desktop UI не менялся.
- Проверка контракта: подтверждены legacy JSON без `--include`, одношаговые отсортированные relations, lease-bound lifecycle, ограниченный аудит, симметричный create, очистка следующего occurrence, fail-closed marker/input и `outcomeUnknown` без автоматического retry.
- Проверка восстановления: `ReadDirectoryAsync` и `ReadGraphAsync` выполняют recovery под directory lock; target/journal deletion fail-fast; authoritative read-back выполняется только после rollback/rollforward; primary task многофайловой операции сохраняется независимо от порядка attempted writes.
- Независимое ревью: reviewer работал фактически read-only, но среда имела `danger-full-access`, поэтому результат классифицирован как adversarial fallback, а не технически изолированный read-only review. Первый verdict `NEEDS-FIX` нашёл recovery bypass, best-effort delete, неполную fault matrix и разрывы JSON/input/state/help; повторное ревью подтвердило закрытие реализации и запросило недостающие контрактные тесты. Все запрошенные тесты добавлены.

| Находка post-EXEC | Исправление | Повторная проверка | Статус |
| --- | --- | --- | --- |
| CLI read мог увидеть частичный граф до recovery | `ReadDirectoryAsync` обёрнут directory lock + recovery | CLI `validate` после abandoned journal | закрыто |
| Ошибка удаления могла оставить partial state без журнала | target и journal удаляются fail-fast, журнал сохраняется до полного применения | delete failure + idempotent retry tests | закрыто |
| `outcomeUnknown` мог содержать pre-rollback или чужой authoritative task | read-back перенесён после finalization; primary task берётся из operation result | partial rollback и multi-file commit-failure tests | закрыто |
| Не хватало audit/input/state/repeater/help контрактов | Добавлены JSON-флаг, лимиты, Active-only question, subtree preflight и полный help | CLI integration 28/28 | закрыто |
| Не хватало fault matrix | Добавлены before/after journal, before task write, child/parent/availability, commit, target delete и journal delete | recovery 14/14 | закрыто |

- Ролевой sanity-pass: бизнес-процесс claim → context → questions → result → complete/release полон; тестировщик подтвердил отрицательные, конкурентные и аварийные пути; архитектор подтвердил единый lock/WAL/marker boundary; эксплуатационная граница честно ограничена process-crash recovery без заявления power-loss durability; UX не применим, так как UI не менялся.
- User-Observable Completion Gate: PASS. Агент может получить ограниченный контекст, задать/закрыть вопрос, сохранить результат, завершить или освободить задачу, повторно взять её с аудитом и создать связанную задачу только через CLI. После аварии следующий CLI-read восстанавливает старый или новый целый граф.
- Финальная валидация:
  - `UnlimotionCliIntegrationTests`: 28/28;
  - `FileTaskStorageRecoverableMutationTests`: 14/14;
  - `AgentExecutionCommandServiceTests`: 3/3;
  - `AgentExecutionDescriptionRendererTests`: 2/2;
  - `TaskGraphCommandServiceTests`: 39/39;
  - `TaskStatusMappingTests`: 2/2;
  - `dotnet build src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Debug -p:UseSharedCompilation=false --no-restore`: успешно, 0 warnings, 0 errors;
  - первый полный последовательный TUnit: 1009/1010, один headless UI-сбой `CurrentTaskCard_PhoneWidthLayout_DoesNotOverflowAndKeepsRelationEditorUsable(390)`; изолированный класс затем прошёл 23/23;
  - повторный полный последовательный TUnit на неизменном коде: 1010/1010, 0 failed, 0 skipped, 18m55s.
- Остаточные риски: power-loss durability не заявляется; сетевой режим и публикация новой CLI-версии вне области; Git предупреждает о потенциальной LF→CRLF нормализации, но `git diff --check` не показывает whitespace errors.

## Approval

Ожидается фраза: `Спеку подтверждаю`.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выделить остаток полного lifecycle после готового `candidates + claim` | 0.96 | Нет блокирующих данных | Запросить подтверждение SPEC | Да: подтверждение перед EXEC | Пользователь попросил отдельную SPEC | Сохраняется честная граница: готовый срез закоммичен, будущие команды не считаются реализованными | `specs/2026-09-13-agent-executor-cli-remaining-lifecycle.md` |
| SPEC | Adversarial и ролевое ревью | 0.94 | Независимый read-only reviewer недоступен | Ожидать решения пользователя | Да | Нет | Проверены обход lease, marker corruption, crash create, аудит release, repeat clone и совместимость; находки встроены в AC | `specs/2026-09-13-agent-executor-cli-remaining-lifecycle.md` |
| EXEC | Переход к реализации подтверждённой SPEC | 0.99 | Нет | Исследовать точки расширения и начать с ожидаемо падающих контрактных тестов | Нет | Пользователь подтвердил SPEC точной фразой `Спеку подтверждаю` | Область реализации зафиксирована разделами 5, 6 и критериями AC-1—AC-10 | `specs/2026-09-13-agent-executor-cli-remaining-lifecycle.md` |
| EXEC | Реализовать полный lease-bound lifecycle, ограниченный снимок и безопасный marker | 0.96 | Нет | Проверить аварийное восстановление и регрессии | Нет | Нет | Добавлены question/answer/result/complete/release/re-claim, защита старого complete, лимит аудита, task include и очистка execution у следующего occurrence | `AgentExecutionRecord.cs`, `AgentExecutionDescriptionRenderer.cs`, `TaskGraphCommandService.cs`, `TaskTreeManager.cs`, `Program.cs` |
| EXEC | Сделать многофайловые записи восстановимыми | 0.94 | Нет | Выполнить полную тестовую матрицу и post-EXEC review | Нет | Нет | Журнал хранит исходные и новые байтовые образы; до commit recovery откатывает, после commit докатывает; fault-тесты проверяют обе ветви и частичный create | `TaskGraphDiagnostics.cs`, `FileTaskStorage.cs`, `FileTaskStorageRecoverableMutationTests.cs` |
| EXEC | Закрыть находки независимого post-EXEC review | 0.98 | Нет | Повторить целевые и полные тесты | Нет | Нет | Recovery добавлен в CLI read path, delete стал fail-fast, authoritative read-back перенесён после finalization, primary task сохранён отдельно, контракты JSON/input/help/repeater усилены | `FileTaskStorage.cs`, `TaskGraphCommandService.cs`, `Program.cs`, `README.md` |
| EXEC | Доказать acceptance matrix и полную регрессию | 0.99 | Нет | Зафиксировать итог и передать пользователю | Нет | Нет | Целевые наборы зелёные; первый полный прогон выявил изолированный UI-флейк, isolated retry прошёл, повторный полный TUnit прошёл 1010/1010 | `UnlimotionCliIntegrationTests.cs`, `FileTaskStorageRecoverableMutationTests.cs`, `FileStorageTaskStatusTests.cs`, этот SPEC |
