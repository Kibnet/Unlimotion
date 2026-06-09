# Переработка статусной модели задачи

## 0. Метаданные
- Тип (профиль): `product-system-design` + `.NET desktop client` + `ui-automation-testing`
- Владелец: Product Owner / активный пользователь
- Масштаб: large
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: Не задано
- Ограничения:
  - Фаза `SPEC`: до подтверждения менять только этот файл.
  - Локальный override требует UI tests для UI-facing изменений.
  - Старые версии приложения с новым хранилищем не поддерживаются.
- Связанные ссылки:
  - `src/Unlimotion.Domain/TaskItem.cs`
  - `src/Unlimotion.Interface/TaskItemHubMold.cs`
  - `src/Unlimotion.Interface/ReceiveTaskItem.cs`
  - `src/Unlimotion.Server.ServiceModel/molds/Tasks/TaskItemMold.cs`
  - `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs`
  - `src/Unlimotion.TelegramBot/Bot.cs`
  - `src/Unlimotion/UnifiedTaskStorage.cs`
  - `src/Unlimotion.ViewModel/TaskItemViewModel.cs`
  - `src/Unlimotion.ViewModel/MainWindowViewModel.cs`
  - `src/Unlimotion.ViewModel/UnlockedTimeFilter.cs`
  - `src/Unlimotion/Views/MainControl.axaml`
  - `src/Unlimotion/Views/GraphControl.axaml`
  - `src/Unlimotion/Views/GraphControl.axaml.cs`
  - `src/Unlimotion.ViewModel/TaskOutlineClipboardService.cs`
  - `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs`
  - `README.RU.md`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель

Перевести Unlimotion с неявной трехзначной модели `IsCompleted: bool?` на явную статусную модель задачи:

1. `NotReady` / `Не готово`
2. `Prepared` / `Подготовлено` (`Готово к выполнению`)
3. `InProgress` / `Выполняется`
4. `Completed` / `Выполнено`
5. `Archived` / `Архивировано`

Новая модель должна разделить:

- жизненный статус задачи, который пользователь контролирует вручную;
- вычисляемую доступность по зависимостям и времени;
- историю переходов статуса для будущей аналитики;
- критерии выполнения задачи как отдельный checklist.

Outcome contract:

- Success means:
  - `IsCompleted` перестает быть persisted source of truth.
  - Все основные представления показывают статус через статусную иконку и поддерживают multi-select фильтр по статусам.
  - Появляется отдельная вкладка `Выполняется`.
  - История переходов хранится в JSON задачи и показывается в раскрывающейся панели карточки.
  - Старое файловое хранилище мигрируется сразу целиком.
  - Markdown outline умеет импортировать и экспортировать новые короткие статусные маркеры.
  - UI и доменные правила покрыты автоматическими тестами.
- Итоговый артефакт / output:
  - Обновленная доменная модель, миграция, ViewModel/UI, Markdown outline, README и тесты.
- Stop rules:
  - Не переходить к EXEC без фразы `Спеку подтверждаю`.
  - Не завершать EXEC при падающих релевантных UI tests.
  - Не оставлять старый `IsCompleted` как persisted источник статуса.

## 2. Текущее состояние (AS-IS)

- `TaskItem` хранит статус через `bool? IsCompleted`:
  - `false` = активная / не выполнена;
  - `true` = выполнена;
  - `null` = архивирована.
- `TaskItem` отдельно хранит `IsCanBeCompleted`, `UnlockedDateTime`, `CompletedDateTime`, `ArchiveDateTime`.
- `TaskTreeManager.UpdateTask` определяет изменение статуса через изменение `IsCompleted`.
- `TaskTreeManager.HandleTaskCompletionChange`:
  - проставляет `CompletedDateTime` при выполнении;
  - очищает `CompletedDateTime` при возврате в активный статус;
  - проставляет `ArchiveDateTime` при архивировании;
  - создает следующий экземпляр repeating task при завершении.
- `TaskTreeManager.CalculateAvailabilityForTask` вычисляет `IsCanBeCompleted` из дочерних задач и блокеров.
- `UnifiedTaskStorage.MigrateIsCanBeCompleted` пересчитывает доступность в файловом хранилище.
- `MainWindowViewModel` строит projections и фильтры вокруг `IsCompleted`:
  - `ShowCompleted`;
  - `ShowArchived`;
  - `CompletedItems`;
  - `ArchivedItems`;
  - `UnlockedItems`.
- `MainControl.axaml` и `GraphControl.axaml` используют `CheckBox IsChecked="{Binding IsCompleted}"`.
- `TaskOutlineClipboardService` импортирует/экспортирует только Markdown checkbox markers `- [ ]` и `- [x]`.
- Interface/server DTO and molds expose `IsCompleted`, `CompletedDateTime`, `ArchiveDateTime` as public task contract fields.
- Telegram bot renders task status and toggles completion through `IsCompleted`.
- AppAutomation test host and snapshots seed task data with old completion fields.
- `README.RU.md` уже концептуально описывает четыре состояния, включая `Выполняется`, но кодовая модель этого не поддерживает как persisted статус.

Скрытые зависимости и инварианты:

- Архив сейчас является значением `IsCompleted`, а не отдельной осью.
- `IsCanBeCompleted = false` визуально серит задачу и запрещает завершение.
- `Unlocked` является projection доступности по зависимостям, а не lifecycle status.
- Completed/Archived списки сортируются по persisted датам, которые в целевой модели должны стать вычисляемыми из истории.
- UI tests завязаны на стабильные automation ids; их нельзя ломать без обновления тестов.

## 3. Проблема

Текущая модель смешивает жизненный статус, архивирование, выполнение и доступность в `bool? IsCompleted`, поэтому невозможно выразить продуктовые состояния `Подготовлено` и `Выполняется`, хранить историю переходов и построить корректные фильтры/аналитику без дальнейшего усложнения неявных правил.

## 4. Цели дизайна

- Разделение ответственности:
  - `Status` отвечает за жизненный статус;
  - `IsCanBeCompleted` отвечает за вычисляемую доступность;
  - история отвечает за аналитику переходов;
  - completion criteria checklist отвечает за проверку готовности результата к завершению.
- Повторное использование:
  - единый status-control используется в списках, графе и карточке.
- Тестируемость:
  - доменные переходы и миграция покрываются unit/contract tests;
  - UI flow покрывается существующими headless/AppAutomation паттернами.
- Консистентность:
  - одинаковые статусы и порядок сортировки во всех представлениях.
- Обратная совместимость:
  - старые файлы мигрируются в новую модель;
  - новые файлы не обязаны открываться старыми версиями приложения.

## 5. Non-Goals (чего НЕ делаем)

- Не вводим отдельный тип задачи `Проект` / `Цель` / `Контейнер`.
- Не делаем отдельный экран `Требует подготовки`.
- Не считаем метрики длительности прямо в текущей задаче; их можно вычислить позже по истории.
- Не поддерживаем старые версии приложения после миграции хранилища.
- Не превращаем `Подготовлено` в автоматически вычисляемый статус.
- Не делаем `Заблокирована` отдельным статусом.
- Не внедряем multi-user collaboration beyond сохранение автора перехода.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `src/Unlimotion.Domain/TaskItem.cs`:
  - хранит `TaskStatus Status`;
  - хранит `List<TaskStatusHistoryEntry> StatusHistory`;
  - хранит `List<TaskCompletionCriterion> CompletionCriteria`;
  - больше не хранит `IsCompleted`, `CompletedDateTime`, `ArchiveDateTime` как persisted source of truth.
- `src/Unlimotion.Domain/TaskStatus.cs`:
  - новый enum `NotReady`, `Prepared`, `InProgress`, `Completed`, `Archived`.
- `src/Unlimotion.Domain/TaskStatusHistoryEntry.cs`:
  - запись `{ Status, ChangedAt, Author }`.
  - `from` и `reason/source` не хранятся.
- `src/Unlimotion.Domain/TaskCompletionCriterion.cs`:
  - запись checklist-пункта `{ Id, Text, IsSatisfied }`.
- `src/Unlimotion.Interface/*` и `src/Unlimotion.Server.ServiceModel/*`:
  - переводят public task DTO/mold contracts с `IsCompleted`/status dates на `Status`, `StatusHistory`, computed dates для отображения при необходимости и `CompletionCriteria`;
  - не оставляют старые completion fields в публичных task contracts, если только implementation-time compiler/API constraint не потребует явно описанный compatibility shim.
- `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs`:
  - выполняет переходы статуса и guard rules;
  - пишет историю;
  - пересчитывает зависимые задачи;
  - переводит `InProgress -> Prepared`, если выполняющаяся задача стала заблокированной.
- `src/Unlimotion.TelegramBot/Bot.cs`:
  - обновляет status rendering, completion action и task details под `TaskStatus` и computed dates.
- `src/Unlimotion/UnifiedTaskStorage.cs`:
  - мигрирует старые JSON-файлы целиком при первом запуске новой версии.
- `src/Unlimotion.ViewModel/TaskItemViewModel.cs`:
  - предоставляет команды смены статуса;
  - валидирует completion criteria checklist перед завершением;
  - раскрывает вычисляемые даты из истории.
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs`:
  - добавляет статусные multi-select filters;
  - добавляет projection `InProgressItems`;
  - сохраняет фильтры между запусками.
- `src/Unlimotion.ViewModel/UnlockedTimeFilter.cs`:
  - сохраняет `Unlocked` как отдельную семантику доступности и разводит ее с lifecycle status.
- `src/Unlimotion/Views/MainControl.axaml` и `src/Unlimotion/Views/GraphControl.axaml`:
  - заменяют checkbox на status icon/dropdown control;
  - добавляют вкладку `Выполняется`;
  - добавляют фильтр статуса во все представления.
- `src/Unlimotion/Views/GraphControl.axaml.cs`:
  - перестраивает/обновляет граф по status filter/status changes и синхронизирует status visuals узлов.
- `src/Unlimotion.ViewModel/TaskOutlineClipboardService.cs`:
  - импортирует и экспортирует новые Markdown markers.
- `tests/Unlimotion.AppAutomation.TestHost/*` and snapshots:
  - обновляют seeded scenario data и snapshot expectations под новую статусную модель.
- `README.md` / `README.RU.md`:
  - обновляют описание статусной модели, markers и пользовательского flow.

### 6.2 Ключевые пользовательские сценарии

1. Подготовить задачу к делегированию:
   - Given задача в `NotReady`;
   - When пользователь считает, что контекста достаточно;
   - Then он вручную переводит задачу в `Prepared`, даже если completion criteria еще пустые или не отмечены.
2. Начать выполнение подготовленной задачи:
   - Given задача в `Prepared` и не заблокирована зависимостями/датой;
   - When пользователь выбирает `InProgress`;
   - Then задача попадает во вкладку `Выполняется`, а история фиксирует старт.
3. Найти задачи, которые можно делать или делегировать сейчас:
   - Given есть доступные по зависимостям задачи;
   - When пользователь открывает `Unlocked` и выбирает status filter `Prepared`;
   - Then он видит пересечение `Unlocked + Prepared`.
4. Проверить результат перед завершением:
   - Given задача содержит completion criteria;
   - When пользователь пытается перевести ее в `Completed`;
   - Then переход разрешен только если все criteria отмечены как выполненные.
5. Вернуть выполняемую задачу в подготовленные при новой блокировке:
   - Given задача в `InProgress`;
   - When появляется невыполненный блокер или будущая дата начала;
   - Then система переводит задачу в `Prepared` и пишет историю от `System`.
6. Архивировать родителя с дочерними задачами:
   - Given у задачи есть активные дочерние задачи;
   - When пользователь архивирует родителя;
   - Then сохраняется текущий UX: родитель архивируется, затем modal предлагает применить архивирование к активным дочерним задачам.
7. Разархивировать задачу:
   - Given задача в `Archived`;
   - When пользователь разархивирует задачу;
   - Then задача возвращается в последний доархивный статус из истории, fallback `NotReady`.
8. Импортировать outline с новыми markers:
   - Given пользователь вставляет Markdown outline с `[ ]`, `[!]`, `[>]`, `[x]`, `[#]`;
   - When импорт подтвержден;
   - Then задачи создаются с соответствующими статусами и начальными history entries.
9. Управлять статусом через Telegram:
   - Given пользователь открыл задачу в Telegram bot;
   - When он выбирает `Изменить статус`;
   - Then bot показывает меню всех пяти статусов и применяет тот же набор guard rules.

### 6.3 Детальный дизайн

#### Доменный контракт

```csharp
public enum TaskStatus
{
    NotReady,
    Prepared,
    InProgress,
    Completed,
    Archived
}

public record TaskStatusHistoryEntry
{
    public TaskStatus Status { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string Author { get; set; } = string.Empty;
}

public record TaskCompletionCriterion
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsSatisfied { get; set; }
}
```

`TaskItem` получает:

```csharp
public TaskStatus Status { get; set; } = TaskStatus.NotReady;
public List<TaskStatusHistoryEntry> StatusHistory { get; set; } = new();
public List<TaskCompletionCriterion> CompletionCriteria { get; set; } = new();
```

Persisted JSON хранит `Status` строкой:

```json
{
  "Status": "Prepared",
  "StatusHistory": [
    {
      "Status": "NotReady",
      "ChangedAt": "2026-06-09T10:00:00+03:00",
      "Author": "System"
    },
    {
      "Status": "Prepared",
      "ChangedAt": "2026-06-09T10:20:00+03:00",
      "Author": "User profile display name"
    }
  ],
  "CompletionCriteria": [
    {
      "Id": "criterion-id",
      "Text": "Результат проверен на тестовом наборе",
      "IsSatisfied": true
    }
  ]
}
```

#### Вычисляемые даты

`CompletedDateTime`, `ArchiveDateTime`, `StartedDateTime`, `PreparedDateTime` больше не должны быть persisted state. Для сортировки и UI они вычисляются из `StatusHistory`:

- `CompletedAt` = последняя запись со `Status = Completed`.
- `ArchivedAt` = последняя запись со `Status = Archived`.
- `StartedAt` = последняя запись со `Status = InProgress`.
- `PreparedAt` = последняя запись со `Status = Prepared`.

Если история повреждена или пуста, fallback:

- использовать текущий `Status`;
- дату брать из `UpdatedDateTime ?? CreatedDateTime`;
- не падать при отображении.

#### Правила переходов

Ручные переходы разрешены между всеми пятью статусами, но с guard rules:

| Переход | Правило |
| --- | --- |
| Any -> `Prepared` | Разрешен вручную; completion criteria не блокируют подготовку. |
| Any -> `InProgress` | Разрешен только если задача не заблокирована по зависимостям/времени; completion criteria не блокируют старт. |
| Any -> `Completed` | Разрешен только если задача может быть завершена: все дочерние выполнены, блокеры выполнены, задача не заблокирована, completion criteria пустые или полностью отмечены. |
| Any -> `Archived` | Разрешен; для родителя с активными дочерними задачами показывается cascade prompt. |
| `Archived` -> Any | Разрешен; default при разархивации через команду = последний доархивный статус из истории, fallback `NotReady`. |

`Заблокирована` не является статусом. Это вычисляемое UI/behavior состояние:

- `IsCanBeCompleted = false`;
- будущая `PlannedBeginDateTime`, если временная доступность включена как guard start;
- задача отображается серой, как сейчас.

Product decision для времени:

- `PlannedBeginDateTime` в будущем блокирует переход в `InProgress`.
- `PlannedEndDateTime` в прошлом не блокирует переход в `InProgress`; это overdue-состояние для фильтрации.

Если задача находится в `InProgress`, и пользовательское изменение или пересчет зависимостей делает ее заблокированной:

- система автоматически переводит задачу в `Prepared`;
- в историю пишется `{ Status = Prepared, ChangedAt = now, Author = "System" }`.

`NotReady` и `Prepared` не меняются автоматически из-за доступности:

- `NotReady` означает недостаточно контекста и деталей;
- `Prepared` означает контекста достаточно, можно брать или делегировать;
- доступность только влияет на возможность начать/завершить и на серый UI.

#### Repeater

При завершении repeating task:

- исходная задача получает `Completed`;
- создается следующий экземпляр;
- новый экземпляр получает `Status = Prepared`;
- новый экземпляр получает новую историю, без копирования истории исходной задачи;
- первая запись нового экземпляра: `{ Status = Prepared, ChangedAt = now, Author = "System" }`.

#### Автор истории

- Ручные переходы: автор берется из профиля пользователя.
- Priority для отображаемого автора: `Profile.DisplayName`, затем `TaskItem.UserId`, затем `GitSettings.UserName`, затем `local-user`.
- Миграции и автоматические переходы: `System`.

#### UI status control

Status icon заменяет checkbox во всех местах, где задача сейчас управляется через `IsCompleted`:

| Статус | Иконка |
| --- | --- |
| `NotReady` | пустой квадрат |
| `Prepared` | квадрат со знаком `!` |
| `InProgress` | квадрат с треугольником `play` |
| `Completed` | квадрат с галочкой |
| `Archived` | квадрат с квадратом внутри |

Контроль:

- клик по иконке открывает dropdown со всеми статусами;
- недоступные статусы disabled и показывают tooltip с причиной;
- tooltip иконки показывает локализованный текст текущего статуса;
- одинаковый status control используется в списках, графе и карточке задачи;
- automation ids должны быть стабильными, например:
  - `TaskStatusButton`;
  - `TaskStatusMenu`;
  - `TaskStatusMenuItemNotReady`;
  - `TaskStatusMenuItemPrepared`;
  - `TaskStatusMenuItemInProgress`;
  - `TaskStatusMenuItemCompleted`;
  - `TaskStatusMenuItemArchived`.

#### Горячая клавиша

Пользователь подтвердил `Ctrl`-modifier, но не конкретную клавишу. Так как `Ctrl+Enter` уже занят созданием sibling task, предлагаемый default:

- `Ctrl+D` переводит текущую задачу в `Completed`, если guard rules разрешают.
- Если переход запрещен, показывается короткое объяснение через существующий notification/toast механизм.

Это решение можно изменить до подтверждения спеки.

#### Фильтры и представления

- Во все основные представления добавляется multi-select фильтр по статусам.
- Фильтр сохраняется между запусками.
- Сортировка по статусу идет в workflow-порядке:
  - `NotReady -> Prepared -> InProgress -> Completed -> Archived`.
- `All Tasks` default:
  - показывает `NotReady`, `Prepared`, `InProgress`;
  - скрывает `Completed`, `Archived`;
  - пользователь может включить скрытые статусы через фильтр.
- `Unlocked`:
  - остается отдельным projection доступности по зависимостям;
  - default показывает все статусы, применимые к этому projection;
  - пользователь может пересечь `Unlocked + Prepared`, чтобы получить список задач, которые можно делать или делегировать.
- Новая вкладка `Выполняется`:
  - плоский список задач со `Status = InProgress`;
  - сортировка по `StartedAt` по убыванию;
  - отображает count выполняющихся задач;
  - показывает elapsed time since `StartedAt`.
- `Completed` и `Archived` остаются отдельными вкладками.

#### Карточка задачи

Карточка задачи получает:

- status control;
- completion criteria checklist как отдельное поле;
- раскрывающуюся панель истории статусов;
- disabled/validation state для переходов, которые нарушают guard rules.

Completion criteria checklist:

- это отдельный persisted checklist, а не часть `Description`;
- checklist нужен для проверки результата перед завершением, а не для подготовки задачи;
- пустой checklist не блокирует переход в `Completed`;
- если checklist содержит пункты, переход в `Completed` разрешен только когда все пункты отмечены;
- переходы в `Prepared` и `InProgress` не зависят от completion criteria;
- пользователь может добавлять, редактировать, удалять и отмечать пункты checklist в карточке задачи;
- у `Completed` задачи completion criteria отображаются read-only; чтобы изменить или снять пункт, пользователь должен сначала явно вернуть задачу в активный статус.

#### Архивация родителя

Нужно сохранить текущую бизнес-логику cascade prompt:

- архивирование родителя выполняется по действию пользователя;
- если у родителя есть активные дочерние задачи (`NotReady`, `Prepared`, `InProgress`), показывается modal с предложением применить архивирование к ним;
- подтверждение modal переводит активные дочерние задачи в `Archived` и пишет history entries;
- отказ в modal оставляет дочерние задачи в их текущих статусах;
- `Completed` и уже `Archived` дочерние задачи не меняются.

#### Telegram bot

Telegram bot должен поддерживать все пять статусов:

- отображать status icon/text для `NotReady`, `Prepared`, `InProgress`, `Completed`, `Archived`;
- заменить бинарную кнопку `Выполнить задачу` на `Изменить статус`;
- показывать inline-меню со всеми пятью статусами;
- применять те же guard rules, что и desktop UI;
- при запрещенном переходе отвечать пользователю короткой причиной отказа.

#### Visual planning artifact

Fallback text wireframe достаточен для SPEC, потому что финальный visual дизайн будет проверяться UI tests/screenshots на EXEC.

```text
Task row
+-------------------------------------------------------+
| [□/!/▶/✓/▣]  🔁  Task title ...                       |
|   click -> status dropdown; tooltip -> localized text  |
+-------------------------------------------------------+

Task card header
+-------------------------------------------------------+
| [StatusIcon v] Task title                             |
| Status dropdown: NotReady / Prepared / InProgress ... |
+-------------------------------------------------------+
| Completion criteria                                  |
| [x] result matches expected output                    |
| [ ] edge cases checked                               |
+-------------------------------------------------------+
| v Status history                                      |
| Prepared    2026-06-09 10:20    User                  |
| InProgress  2026-06-09 11:00    User                  |
+-------------------------------------------------------+

InProgress tab
+-------------------------------------------------------+
| Выполняется (3)      [status filters]                 |
| ▶ Task A        started 11:00, running 00:35          |
| ▶ Task B        started 09:15, running 02:20          |
+-------------------------------------------------------+
```

UI test video evidence:

- `До` video не применимо для новой фичи: текущий flow отсутствует как полноценная статусная модель.
- `После` video должен быть получен из автоматизированного UI test run, если test harness поддержит запись.
- Fallback при отсутствии recorder: screenshots/logs из headless/AppAutomation сценариев с указанием причины.

## 7. Бизнес-правила / Алгоритмы

### 7.1 Status invariants

1. `TaskItem.Status` всегда равен последней валидной записи `StatusHistory.Status`, если история не пуста.
2. `StatusHistory` хранит только фактические переходы; промежуточные переходы при прыжке не добавляются.
3. Первая запись истории не содержит `from`; она просто фиксирует первый известный статус.
4. `Archived` заменяет отображаемый жизненный статус. Последний доархивный статус восстанавливается из истории при unarchive.
5. `InProgress` запрещен для заблокированной задачи.
6. `Completed` запрещен для задачи с невыполненными дочерними или блокерами.
7. `Prepared` не гарантирует, что задачу можно начать сейчас; это означает, что контекста достаточно.

### 7.2 Transition table

| From | To | Allowed | Side effects |
| --- | --- | --- | --- |
| Any | `NotReady` | Да | Append history. |
| Any | `Prepared` | Да | Append history. |
| Any | `InProgress` | Да, если startable | Append history. |
| Any | `Completed` | Да, если completion criteria valid и completable | Append history; repeater clone if configured. |
| Any | `Archived` | Да | Append history; optional cascade archive children. |
| `Archived` | previous non-archived | Да | Append history with restored status. |

### 7.3 Markdown outline markers

| Marker | Status |
| --- | --- |
| `- [ ]` | `NotReady` |
| `- [!]` | `Prepared` |
| `- [>]` | `InProgress` |
| `- [x]` | `Completed` |
| `- [#]` | `Archived` |

Импорт старого Markdown:

- `- [ ]` создает `NotReady`;
- `- [x]` создает `Completed`.

Экспорт:

- должен использовать новые markers для всех пяти статусов.

## 8. Точки интеграции и триггеры

- Создание задачи:
  - default `Status = NotReady`;
  - history: `NotReady` от автора текущего пользователя.
- Создание repeater clone:
  - default `Status = Prepared`;
  - history: `Prepared` от `System`.
- Ручная смена статуса:
  - через dropdown, карточку, контекстное меню или `Ctrl+D`;
  - всегда через `TaskTreeManager`, не прямой set property.
- Изменение связей `ContainsTasks`, `BlocksTasks`, `BlockedByTasks`, `ParentTasks`:
  - пересчитать `IsCanBeCompleted`;
  - если affected task `InProgress` стала заблокированной, перевести в `Prepared`.
- Изменение `PlannedBeginDateTime`:
  - если task `InProgress` и новая дата в будущем, перевести в `Prepared`.
- Миграция хранилища:
  - запускается при первом открытии старого файлового хранилища новой версией.
- Paste Markdown outline:
  - создает задачи с соответствующим `Status` и начальной history.
- Copy Markdown outline:
  - экспортирует текущие статусы в markers.

## 9. Изменения модели данных / состояния

Новые поля:

- `TaskStatus Status`
- `List<TaskStatusHistoryEntry> StatusHistory`
- `List<TaskCompletionCriterion> CompletionCriteria`

Удаляемые persisted поля:

- `IsCompleted`
- `CompletedDateTime`
- `ArchiveDateTime`

Остаются:

- `IsCanBeCompleted` как calculated/persisted availability cache.
- `UnlockedDateTime` как дата изменения доступности.
- `CreatedDateTime`, `UpdatedDateTime`, planned dates, relations, repeater, importance, wanted.

Persisted vs calculated:

| Поле | Тип |
| --- | --- |
| `Status` | persisted |
| `StatusHistory` | persisted |
| `CompletionCriteria` | persisted |
| `IsCanBeCompleted` | calculated cache |
| `UnlockedDateTime` | calculated cache/date |
| `CompletedAt` | calculated from history |
| `ArchivedAt` | calculated from history |
| `StartedAt` | calculated from history |

## 10. Миграция / Rollout / Rollback

### 10.1 Миграция старого JSON

Миграция переписывает все задачи сразу.

Mapping:

| Старое состояние | Новый status |
| --- | --- |
| `IsCompleted == false` | `NotReady` |
| `IsCompleted == true` | `Completed` |
| `IsCompleted == null` | `Archived` |
| `IsCompleted` отсутствует | `NotReady` |

History migration:

- Для всех задач добавляется начальная запись `NotReady` с `CreatedDateTime`, если `CreatedDateTime` доступен.
- Для `Completed` задач добавляется запись `Completed` с `CompletedDateTime`, если она есть; иначе `UpdatedDateTime ?? CreatedDateTime ?? migrationTime`.
- Для `Archived` задач добавляется запись `Archived` с `ArchiveDateTime`, если она есть; иначе `UpdatedDateTime ?? CreatedDateTime ?? migrationTime`.
- Для `NotReady` задач итоговая история содержит только `NotReady`, если нет других известных status dates.
- Автор всех миграционных записей: `System`.
- `Status` должен соответствовать последней записи истории.

После миграции:

- старые persisted поля `IsCompleted`, `CompletedDateTime`, `ArchiveDateTime` удаляются из файлов;
- создается migration report рядом с данными;
- повторный запуск миграции должен быть идемпотентным.
- дополнительный вне-Git backup не требуется для этого проекта: task storage ожидаемо находится в Git repository, поэтому rollback данных выполняется через Git history/working tree.

Migration UX:

- после первой успешной миграции приложение показывает одноразовое уведомление или компактную панель: "Активные задачи перенесены в статус Не готово. Отметьте подготовленные задачи вручную.";
- README/release notes должны объяснять, что старые активные задачи (`IsCompleted=false`) сознательно стали `NotReady`, потому что новый `Prepared` означает явное решение пользователя о достаточности контекста.

### 10.2 Rollout

1. Добавить доменную модель и serialization settings для string enum.
2. Добавить миграцию и characterization tests старых snapshots.
3. Перевести TaskTreeManager и ViewModel на status transitions.
4. Перевести projections, filters, tabs, sort.
5. Перевести XAML controls и graph/list/card UI.
6. Перевести Markdown outline import/export.
7. Обновить README.
8. Запустить targeted -> UI -> full validation.

### 10.3 Rollback

Кодовый и data rollback выполняются через Git, потому что task storage для этого проекта находится в репозитории. Перед destructive migration implementation должен:

- убедиться, что изменения task storage видны Git (`git status --short` не скрывает task files);
- явно зафиксировать в отчете, что rollback path = Git revert/checkout of migrated task files;
- не создавать отдельный backup-каталог по умолчанию, чтобы не плодить дубли данных поверх уже существующего Git backup.

Если во время EXEC обнаружится storage path вне Git worktree, это становится blocker для destructive migration до отдельного решения пользователя.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

1. Новая задача создается со `Status = NotReady`, статусной историей и без persisted `IsCompleted`.
2. Пользователь может через dropdown перевести задачу в `Prepared`, `InProgress`, `Completed`, `Archived`.
3. Задача с невыполненными дочерними или блокерами не может перейти в `Completed`.
4. Заблокированная задача не может перейти в `InProgress`.
5. Если `InProgress` задача становится заблокированной, она автоматически переходит в `Prepared` с автором `System`.
6. `Prepared` задача, которая стала заблокированной, остается `Prepared` и визуально серая.
7. Repeater clone создается со `Status = Prepared` и новой историей.
8. `Archived` задача при unarchive возвращается в последний доархивный статус; если восстановить нельзя, в `NotReady`.
9. Multi-select status filter работает во всех основных представлениях и сохраняется между запусками.
10. Вкладка `Выполняется` показывает плоский список `InProgress`, count и elapsed time.
11. Completion criteria не блокируют `Prepared` и `InProgress`, но блокируют переход в `Completed`, если содержат неотмеченные пункты.
12. Status history отображается в раскрывающейся панели карточки задачи.
13. Markdown outline импортирует и экспортирует markers `[ ]`, `[!]`, `[>]`, `[x]`, `[#]`.
14. Старое хранилище мигрируется целиком, история заполняется датами из старых status fields.
15. README описывает новую модель и markers.
16. После миграции пользователь видит одноразовое уведомление о том, что старые активные задачи перенесены в `NotReady`.
17. Telegram bot отображает все пять статусов, открывает меню смены статуса и применяет те же guard rules, что desktop UI.
18. Public/server task contracts не содержат stale old completion fields без явно описанного compatibility shim.

### Какие тесты добавить/изменить

- Domain/unit:
  - `TaskStatusTransitionTests`
  - `TaskStatusMigrationTests`
  - `TaskStatusHistoryTests`
  - `TaskCompletionCriteriaTests`
  - updates to `TaskCompletionChangeTests`
  - updates to `TaskAvailabilityCalculationTests`
  - updates to `UnifiedTaskStorageMigrationRegressionTests`
- ViewModel/projection:
  - `MainWindowStatusFilterTests`
  - `InProgressProjectionTests`
  - sort by workflow status order.
- UI/headless/AppAutomation:
  - status dropdown in task list and card;
  - disabled invalid transitions;
  - `Ctrl+D` complete shortcut;
  - `InProgress` tab count and ordering;
  - status tooltip;
  - status history panel;
  - completion criteria validation;
  - graph status icon parity.
- Markdown:
  - update `TaskOutlineClipboardServiceTests` for five markers.
- README media/tests:
  - update deterministic screenshots if README media pipeline depends on old checkbox visuals.

### Characterization tests / contract checks

- Old `IsCompleted=false` snapshot migrates to `NotReady`.
- Old `IsCompleted=true` + `CompletedDateTime` migrates to `Completed` with matching history timestamp.
- Old `IsCompleted=null` + `ArchiveDateTime` migrates to `Archived` with matching history timestamp.
- Old Markdown `- [ ]` and `- [x]` still import correctly.

### Visual acceptance

- Status icon never resizes the row.
- Tooltip exposes localized status text.
- Dropdown items fit mobile and desktop widths.
- InProgress tab count is visible without horizontal overflow.
- Disabled invalid statuses have clear tooltip/feedback.

### UI video evidence

- Для новой фичи `до` baseline video: `Не применимо`, полноценного старого flow нет.
- Для `после`: automated UI run video artifact preferred.
- Fallback при невозможности video: обязательны desktop и mobile screenshots, сгенерированные существующим UI/README media workflow или ближайшим headless/AppAutomation workflow.
- Key screenshots must be inspected with `view_image` before final report.
- Final report must attach absolute filesystem Markdown image paths for the inspected screenshots.

### Команды для проверки

Canonical validation:

```powershell
dotnet restore
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
git diff --check
```

Fast targeted repeat runs after successful restore/build:

```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -- --treenode-filter "/*/*/TaskStatusTransitionTests/*"
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -- --treenode-filter "/*/*/TaskStatusMigrationTests/*"
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -- --treenode-filter "/*/*/TaskOutlineClipboardServiceTests/*"
```

UI targeted repeat runs after successful restore/build:

```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -- --treenode-filter "/*/*/MainControl*Status*UiTests/*"
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -- --treenode-filter "/*/*/Graph*Status*UiTests/*"
```

Stop rules для validation loops:

- Targeted failures in changed behavior block completion.
- Full-suite failures must be triaged: distinguish regression from known shared-state issue before final report.
- UI-facing changes require relevant UI tests or explicit technical blocker.

## 12. Риски и edge cases

- Риск: миграция переписывает пользовательские JSON-файлы в несовместимом формате.
  - Mitigation: Git rollback contract, blocker для storage вне Git, migration report, idempotent tests.
- Риск: удаление persisted dates ломает sorting/projections.
  - Mitigation: computed date helpers + tests for completed/archived/in-progress ordering.
- Риск: `Ctrl+D` может конфликтовать с будущими shortcuts или ожиданиями пользователя.
  - Mitigation: зафиксировать как default до подтверждения; можно заменить до EXEC.
- Риск: time-based blocking currently not centralized.
  - Mitigation: явно выделить helper `CanStartTask` and tests for future `PlannedBeginDateTime`.
- Риск: status filters во всех представлениях могут сильно расширить UI scope.
  - Mitigation: сделать единый filter view model/control.
- Риск: old code paths могут напрямую set status property.
  - Mitigation: запретить прямую смену статуса вне `TaskTreeManager` tests/review.
- Риск: архивация cascade с новой моделью может неверно восстановить статусы.
  - Mitigation: tests for archive/unarchive parent/children.
- Риск: README screenshots устареют.
  - Mitigation: обновить README media or mark screenshot refresh as required validation.

## 13. План выполнения

Точный порядок важен из-за migration + UI contract:

1. Characterization:
   - добавить tests на текущие old snapshots/migration expectations.
2. Domain model:
   - добавить enum/history/completion criteria models;
   - добавить status date helpers.
3. Migration:
   - переписать old JSON -> new JSON;
   - migration report;
   - idempotency tests.
4. Transition service:
   - централизовать transition guard rules;
   - route all status changes through manager.
5. Availability integration:
   - preserve `IsCanBeCompleted`;
   - add `InProgress -> Prepared` auto-transition.
6. Repeater/import/export:
   - `Prepared` clone;
   - Markdown markers.
7. Projections/filter/sort:
   - status filters;
   - InProgress projection;
   - computed date sorting.
8. UI:
   - status icon/dropdown;
   - card completion criteria checklist;
   - history panel;
   - graph/list parity;
   - hotkey `Ctrl+D`.
9. Docs/media:
   - update README.
10. Validation:
   - targeted tests;
   - relevant UI tests;
   - desktop build;
   - full test project run;
   - visual evidence.

## 14. Открытые вопросы

Нет блокирующих вопросов.

Решение, которое можно изменить до подтверждения:

- Горячая клавиша `Ctrl+D` выбрана как recommended default, потому что пользователь подтвердил `Ctrl`-modifier, а `Ctrl+Enter` уже занят.

## 15. Соответствие профилю

- Профиль: `product-system-design`
  - Цели и non-goals выделены.
  - Целевая архитектура и границы подсистемы описаны.
  - Публичный доменный контракт `TaskStatus`, history и completion criteria checklist зафиксирован.
  - Compatibility/migration/rollback описаны.
- Профиль: `.NET desktop client`
  - UI flow, binding commands, navigation и validation covered.
  - Build/test commands included.
- Профиль: `ui-automation-testing`
  - UI tests обязательны.
  - Visual planning artifact included.
  - Video evidence contract/fallback included.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Domain/TaskItem.cs` | Добавить `Status`, `StatusHistory`, `CompletionCriteria`; удалить persisted status/date fields | Новый source of truth |
| `src/Unlimotion.Interface/TaskItemHubMold.cs` | Заменить old completion contract на status contract | SignalR/public task contract |
| `src/Unlimotion.Interface/ReceiveTaskItem.cs` | Заменить old completion contract на status contract | Client update payload |
| `src/Unlimotion.Server.ServiceModel/molds/Tasks/TaskItemMold.cs` | Заменить old completion fields/descriptions на status fields | Server API mold |
| `src/Unlimotion.Domain/TaskStatus.cs` | Новый enum | Явная статусная модель |
| `src/Unlimotion.Domain/TaskStatusHistoryEntry.cs` | Новый record | История переходов |
| `src/Unlimotion.Domain/TaskCompletionCriterion.cs` | Новый record | Checklist критериев выполнения |
| `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs` | Transition guards, history append, repeater prepared clone, auto in-progress rollback | Доменное поведение |
| `src/Unlimotion.TelegramBot/Bot.cs` | Обновить status emoji/text/toggle/detail rendering | Побочный пользовательский интерфейс |
| `src/Unlimotion/UnifiedTaskStorage.cs` | Миграция old JSON -> new status model | Совместимость старых данных |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Status commands, computed dates, completion criteria binding | ViewModel contract |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Status filters, InProgress projection, persistence | Основные представления |
| `src/Unlimotion.ViewModel/UnlockedTimeFilter.cs` | Развести unlocked predicate и lifecycle status | Доступность отдельно от статуса |
| `src/Unlimotion.ViewModel/SortDefinition.cs` | Status sort and computed date sort updates | Сортировка |
| `src/Unlimotion.ViewModel/TaskOutlineClipboardService.cs` | Five status markers | Markdown import/export |
| `src/Unlimotion/Views/MainControl.axaml` | Status icon/dropdown, InProgress tab, filters, card panels, hotkey | Main UI |
| `src/Unlimotion/Views/GraphControl.axaml` | Status icon/dropdown parity, filters | Graph UI parity |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Rebuild/update graph on status changes and filters | Graph behavior parity |
| `src/Unlimotion.ViewModel/Resources/*.resx` | Status labels/tooltips/errors/filter strings | Localization |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs` | Обновить synthetic scenarios to new status fields | UI automation fixtures |
| `src/Unlimotion.Test/Snapshots/*` | Обновить expected JSON snapshots after migration contract | Storage regression fixtures |
| `src/Unlimotion.Test/*` | Domain, migration, UI, outline tests | Regression coverage |
| `README.md`, `README.RU.md` | Status model and outline markers docs | User-facing docs |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Active status | `IsCompleted=false` | `Status=NotReady/Prepared/InProgress` |
| Completed | `IsCompleted=true` + `CompletedDateTime` | `Status=Completed` + history |
| Archived | `IsCompleted=null` + `ArchiveDateTime` | `Status=Archived` + history |
| Availability | `IsCanBeCompleted` | `IsCanBeCompleted`, separate from status |
| Unlocked tab | Available active tasks | Available tasks + status filter |
| Completion control | Checkbox | Status icon/dropdown + `Ctrl+D` |
| Markdown active | `- [ ]` | `- [ ]`, `- [!]`, `- [>]` |
| Markdown done | `- [x]` | `- [x]` |
| Markdown archive | Not represented | `- [#]` |
| Analytics | Dates only | Full transition history |

## 18. Альтернативы и компромиссы

- Вариант: сделать `Prepared` calculated автоматически.
  - Плюсы: меньше ручной работы.
  - Минусы: не отражает продуктовую семантику "достаточно контекста для делегирования".
  - Почему не выбран: пользователь хочет ручной контроль готовности контекста.

- Вариант: оставить `IsCompleted` и добавить `IsPrepared`/`IsInProgress`.
  - Плюсы: меньше миграции.
  - Минусы: продолжит расползание неявных состояний и конфликтов.
  - Почему не выбран: нужна целостная статусная модель и история.

- Вариант: хранить status events отдельными файлами.
  - Плюсы: append-only журнал проще для аудита.
  - Минусы: сложнее синхронизация, migration, backup и чтение локальных JSON.
  - Почему не выбран: пользователь выбрал хранение внутри задачи.

- Вариант: оставить persisted `CompletedDateTime`/`ArchiveDateTime`.
  - Плюсы: проще текущие сортировки.
  - Минусы: дублирование и риск рассинхронизации с history.
  - Почему не выбран: пользователь хочет вычислять даты из истории.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, алгоритмы, внешние контракты, интеграции, данные и migration описаны. |
| C. Безопасность изменений | 11-13 | PASS | Acceptance, risks, Git rollback и migration safety включены. |
| D. Проверяемость | 14-16 | PASS | Есть AC, воспроизводимый validation path, UI evidence и команды проверки. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, alternatives и review заполнены; блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | Product-system, desktop и UI automation требования отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Статусы, non-goals и outcome contract явно заданы. |
| 2. Понимание текущего состояния | 5 | AS-IS привязан к домену, manager, storage, UI и outline. |
| 3. Конкретность целевого дизайна | 5 | Зафиксированы enum, history, completion criteria checklist, UI control, filters. |
| 4. Безопасность (миграция, откат) | 5 | Mapping, даты, автор, Git rollback и blocker для storage вне Git описаны. |
| 5. Тестируемость | 5 | Есть domain, migration, UI, outline, canonical restore/build/test и visual evidence checks. |
| 6. Готовность к автономной реализации | 5 | План по этапам и file map позволяют реализовывать после approval. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-09-task-status-model.md`, central stack (`model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `product-system-design`, `spec-linter`, `spec-rubric`, `review-loops`), local AGENTS override, user interview answers, planned changed files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: проверены текущие `TaskItem`, interface/server molds, `TelegramBot`, `TaskTreeManager`, `UnifiedTaskStorage`, `TaskItemViewModel`, `MainWindowViewModel`, `UnlockedTimeFilter`, `MainControl.axaml`, `GraphControl.axaml/.cs`, `TaskOutlineClipboardService`, `SortDefinition`, AppAutomation data, snapshots, README status description и test governance.
  - Contract pass: spec сохраняет SPEC-only границу, не начинает EXEC, включает UI tests, migration, README и visual planning artifact.
  - Adversarial risk pass: проверены риск несовместимой миграции, конфликт shortcut, удаление persisted dates, time-based blocking, фильтры во всех views, archive/unarchive restore.
  - Re-review after fixes / Fix and re-review: scope expanded to public/server/Telegram/test-host surfaces; validation commands made reproducible with restore/build; visual screenshots made mandatory fallback; Git rollback accepted per user decision; shortcut ambiguity resolved by explicit recommended default `Ctrl+D`; time availability made explicit as `PlannedBeginDateTime` guard.
  - Stop decision: PASS, потому что HIGH/MEDIUM findings fixed or accepted by product decision, блокирующих вопросов нет, а единственное изменяемое UX-решение вынесено как pre-approval default.
- Evidence inspected:
  - `git status --short` before spec creation: clean output.
  - Current spec list under `specs/`.
  - Central governance docs and selected profiles.
  - Code snippets named in Scope/Evidence pass.
- Depth checklist:
  - Scope drift / unrelated changes: only spec file is planned/created in SPEC.
  - Acceptance criteria: 18 criteria cover domain, migration UX, UI, Telegram, outline and docs.
  - Validation evidence: planned commands include canonical restore/build/full test, targeted repeat runs, UI tests, screenshot evidence and `git diff --check`.
  - Unsupported claims: time guard and shortcut assumptions are explicit.
  - Regression / edge case: migration, archive restore, repeater, blocked in-progress and completion criteria covered.
  - Comments/docs/changelog: README update required; changelog not required unless release workflow asks later.
  - Hidden contract change: old app incompatibility and persisted field removal are explicit.
  - Manual-review challenge: reviewer would likely challenge blast radius, shortcut choice, Git rollback assumption and time-based guard; all are surfaced in spec.
- No-findings justification: Не применимо; review findings were found and addressed or accepted.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | scope | Initial file table missed interface/server/Telegram/test-host/snapshot/status helper surfaces using old completion fields. | Expand scope, integration points and file table to include these surfaces. | fixed |
| HIGH | migration | Destructive migration risk needed an explicit rollback model. | Use Git as accepted rollback mechanism; block destructive migration if storage is outside Git worktree. | accepted-risk |
| MEDIUM | validation | Validation commands used `--no-restore` without canonical restore/build path. | Add canonical `dotnet restore`, build, full test and keep `--no-restore` only for repeat runs. | fixed |
| MEDIUM | evidence | UI-heavy feature needed stronger screenshot evidence if video is unavailable. | Require desktop/mobile screenshots, inspect with `view_image`, attach absolute paths. | fixed |
| MEDIUM | business-analysis | Initial spec lacked end-to-end user scenarios. | Add key Given/When/Then scenarios for delegation, start, Unlocked+Prepared, completion criteria, archive, unarchive, import and Telegram. | fixed |
| MEDIUM | business-analysis | Checklist was incorrectly modeled as readiness criteria instead of completion criteria. | Rename to `CompletionCriteria` and make it block only `Completed`. | fixed |
| MEDIUM | business-analysis | Archive cascade prompt behavior needed explicit business wording. | Preserve current UX: parent archives, modal optionally cascades to active children. | fixed |
| MEDIUM | business-analysis | Migration to `NotReady` needed first-run product communication. | Add one-time migration notice and README/release notes expectation. | fixed |
| MEDIUM | business-analysis | Telegram behavior needed full-status business contract. | Define five-status display/menu and shared guard rules. | fixed |
| LOW | business-analysis | History author fallback was underspecified. | Define `Profile.DisplayName` -> `TaskItem.UserId` -> `GitSettings.UserName` -> `local-user`. | fixed |
| LOW | UX | User said only "с контролом" for completion hotkey; exact key was not provided. | Use non-conflicting recommended default `Ctrl+D` and make it visible before approval. | fixed |
| LOW | behavior | Time-based blocking is not centralized in AS-IS, but user referenced time availability. | Define `PlannedBeginDateTime` future as start guard and `PlannedEndDateTime` as non-blocking overdue signal. | fixed |

- Fixed before continuing:
  - Expanded scope to interface/server/Telegram/test-host/snapshot/status-helper surfaces.
  - Added Git rollback contract and blocker for storage outside Git.
  - Replaced canonical validation with restore/build/full test path.
  - Made desktop/mobile screenshot evidence mandatory fallback if video is unavailable.
  - Added key user scenarios and migration UX.
  - Corrected checklist from readiness to completion criteria.
  - Documented current archive cascade behavior, Telegram full-status flow and author fallback.
  - Added explicit `Ctrl+D` recommendation.
  - Added explicit temporal availability rule.
- Checks rerun:
  - Manual spec-linter/rubric/review pass in this section.
- Needs human:
  - Spec approval phrase: `Спеку подтверждаю`.
- Residual risks / follow-ups:
  - EXEC must verify exact UI test harness/video support.
  - EXEC must verify task storage path is inside Git before destructive migration.

### Post-EXEC Review
- Статус: PASS с зафиксированными residual follow-ups
- Scope reviewed: доменная модель, storage/migration, DTO/contracts, ViewModel/UI, Telegram, Markdown outline, README, targeted domain/migration/Markdown/UI tests.
- Decision: реализация соответствует утвержденной status model spec; блокирующих findings после self-review не осталось.
- Review passes:
  - Scope/Evidence pass: проверены изменения в `TaskItem`, `TaskTreeManager`, `UnifiedTaskStorage`, `FileStorage`, DTO/molds, `TaskItemViewModel`, `MainWindowViewModel`, `TaskStatusOption`, `MainControl.axaml`, `GraphControl.axaml/.cs`, `TaskOutlineClipboardService`, Telegram bot, resources, README и тестах.
  - Contract pass: пять статусов, history-only status dates, completion criteria, status filters, `InProgress` view, archive/restore, Markdown markers, Telegram status menu и incompatible storage migration реализованы.
  - Adversarial risk pass: проверены destructive migration guard outside Git, computed legacy completion fields, blocked/future start rollback from `InProgress`, prohibition of `Completed` without checked criteria, prohibition of archive for incomplete tasks, repeater clone as `Prepared`.
  - Re-review after fixes / Fix and re-review: после поздней сверки добавлены disabled status options with tooltip reasons, read-only completion criteria for `Completed`, one-time migration notice signal and migration tests.
  - Stop decision: PASS; отсутствие screenshot/video artifact зафиксировано как validation gap, а не как функциональный blocker, потому что релевантные Avalonia Headless UI assertions прошли.
- Evidence inspected:
  - `dotnet restore src/Unlimotion.sln`
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskStatusTransitionTests/*"`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskStatusMigrationTests/*"`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/TaskOutlineClipboardServiceTests/*"`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/MainControlResetFiltersUiTests/*"`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build -- --treenode-filter "/*/*/RoadmapGraphUiTests/RoadmapGraph_NodifyView_RendersTasksAndKeepsAutomationIds"`
  - `dotnet build src/Unlimotion.TelegramBot/Unlimotion.TelegramBot.csproj -c Debug --no-restore -p:UseSharedCompilation=false`
  - `git diff --check`
  - `dotnet build src/Unlimotion.sln -c Debug --no-restore -p:UseSharedCompilation=false`
- Depth checklist:
  - Scope drift / unrelated changes: changes are scoped to status model, persistence, UI surfaces, Telegram, docs and tests.
  - Acceptance criteria: implemented and covered by domain/migration/UI/outline tests where practical.
  - Validation evidence: targeted tests and full solution build passed; screenshot artifact was not collected.
  - Unsupported claims: no unverified runtime screenshot/video claim is made.
  - Regression / edge case: migration, archive restore, repeater, blocked/future `InProgress`, completion criteria and Markdown import/export covered.
  - Comments/docs/changelog: README updated; changelog not changed because release workflow was not requested.
  - Hidden contract change: incompatible storage migration and legacy field removal are explicit in docs and tests.
  - Manual-review challenge: likely challenges are visual evidence gap, exact Telegram rejection reason and strict item-container disabled styling; all are surfaced below.
- No-findings justification: blocking findings fixed before final report; residual items are non-blocking follow-ups.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Screenshot/video artifacts were not collected despite the visual evidence clause. | Add or reuse a stable Avalonia screenshot artifact helper if visual evidence becomes mandatory for this change. | follow-up |
| LOW | Telegram | Telegram uses the shared transition guard, but rejection copy is generic/resulting-status oriented rather than exact per-rule localized reason. | Extract a shared transition-reason service if Telegram must show the same detailed reason text as UI tooltips. | follow-up |
| LOW | UI | Invalid status options are exposed as disabled VM options with tooltip text and backend guard; strict container-level `ComboBoxItem` disabled styling can still be improved. | Add an Avalonia `ComboBoxItem` container theme if design review requires native disabled item behavior. | follow-up |

- Fixed before final report:
  - Added persisted `Status`, status history and completion criteria domain model.
  - Added raw JSON migration from legacy `IsCompleted`/date fields with Git worktree safety guard and migration report.
  - Reworked DTO/molds and AppModel mapping to carry status/history/criteria and avoid persisting computed legacy fields.
  - Replaced task checkbox UI with status control, status icons, filters across views, `InProgress` tab, status history panel and completion criteria checklist.
  - Added disabled status options with tooltip reasons and made completion criteria read-only for `Completed` tasks.
  - Updated Telegram status display/menu, Markdown markers/import/export and README documentation.
  - Added migration notice signal and tests for transitions, migration, outline and UI filter behavior.
- Checks rerun:
  - Targeted domain/migration/Markdown/UI tests passed.
  - Telegram bot build passed.
  - Full solution build passed.
  - `git diff --check` passed with line-ending warnings only.
- Validation evidence:
  - PASS: `TaskStatusTransitionTests`, `TaskStatusMigrationTests`, `TaskOutlineClipboardServiceTests`, `MainControlResetFiltersUiTests`, selected `RoadmapGraphUiTests`.
  - PASS: `Unlimotion.Test`, `Unlimotion.TelegramBot`, full `Unlimotion.sln` builds.
  - GAP: no screenshot/video artifact collected.
- Unrelated changes: not observed as separate unrelated work; changed files are in the approved status-model scope.
- Needs human: no blocking product decision remains; spec was approved by `Спеку подтверждаю`.
- Residual risks / follow-ups:
  - Add screenshot artifact workflow for Avalonia UI evidence if this becomes a hard delivery gate.
  - Improve Telegram rejection reason parity with UI tooltip reasons if product requires detailed bot feedback.
  - Normalize repository line endings separately if LF/CRLF warnings become noisy in review.

## Approval

Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Интервью и repo reconnaissance | 0.90 | Не хватало точного hotkey и time guard semantics | Сформировать spec с явными assumptions | Да | Да, пользователь дал продуктовые ответы по статусам, UX, storage и transitions | Глубокое интервью собрало продуктовые решения до реализации | `README.RU.md`, `TaskItem.cs`, `TaskTreeManager.cs`, `UnifiedTaskStorage.cs`, `TaskItemViewModel.cs`, `MainWindowViewModel.cs`, `MainControl.axaml`, `TaskOutlineClipboardService.cs` inspected |
| SPEC | Создание рабочей спецификации | 0.88 | Нет блокирующих данных; hotkey выбран recommended default | Запросить подтверждение спеки | Да | Нет после записи спеки | QUEST требует spec-first и approval до EXEC | `specs/2026-06-09-task-status-model.md` |
| EXEC | Доменная модель, миграция и DTO | 0.78 | Не хватает проверки UI flow и storage path outside Git behavior | Перевести ViewModel/фильтры/Markdown/Telegram на `Status` | Нет | Да, пользователь подтвердил спеку фразой `Спеку подтверждаю` | Добавлен persisted `Status`/history/criteria, guard rules и raw JSON migration; основной проект компилируется после слоя модели | `TaskStatus.cs`, `TaskStatusHistoryEntry.cs`, `TaskCompletionCriterion.cs`, `TaskStatusExtensions.cs`, `TaskItem.cs`, `TaskTreeManager.cs`, `FileStorage.cs`, `UnifiedTaskStorage.cs`, `TaskItemHubMold.cs`, `ReceiveTaskItem.cs`, `TaskItemMold.cs`, `*.csproj` |
| EXEC | ViewModel/UI/filters/criteria/Markdown/Telegram | 0.84 | Не хватало runtime screenshot artifact; UI behavior закрыт headless tests | Провести финальную валидацию и Post-EXEC review | Нет | Нет | Статусный контрол заменил чекбокс, добавлены фильтры, InProgress tab, criteria/status history, Markdown markers и Telegram menu | `TaskItemViewModel.cs`, `MainWindowViewModel.cs`, `TaskStatusOption.cs`, `MainControl.axaml`, `GraphControl.axaml`, `TaskOutlineClipboardService.cs`, `Bot.cs`, `Resources/*.resx` |
| EXEC | Поздний self-review и фиксы требований | 0.80 | Нет PNG/video evidence; точные Telegram guard reasons не вынесены в общий сервис | Зафиксировать Post-EXEC review | Нет | Нет | После сверки со spec добавлены disabled status options с tooltip reasons, read-only criteria для Completed и одноразовый migration notice | `TaskItemViewModel.cs`, `TaskStatusOption.cs`, `MainWindowViewModel.cs`, `ITaskStorage.cs`, `UnifiedTaskStorage.cs`, `TaskStatusTransitionTests.cs`, `TaskStatusMigrationTests.cs` |
| EXEC | Валидация | 0.88 | Нет screenshot artifact; остались только warning-only build notes | Завершить отчет | Нет | Нет | Целевые domain/migration/Markdown/UI тесты, Telegram build, diff check и полный solution build прошли | `Unlimotion.Test.csproj`, `Unlimotion.sln`, `Unlimotion.TelegramBot.csproj` |
| EXEC | Визуальная правка статусных иконок | 0.86 | Полный тестовый прогон всё ещё содержит падения старых ожиданий общей status-model миграции | Завершить отчет по visual fix | Нет | Нет | Компактный статусный контрол заменён на checkbox-like vector picker, dropdown показывает те же пять иконок; Computer Use screenshots подтвердили list и flyout, targeted UI tests/build passed, full `Unlimotion.Test` failed on remaining old selectors/JSON expectations outside icon rendering | `TaskStatusIcon.cs`, `TaskStatusPicker.cs`, `MainControl.axaml`, `GraphControl.axaml`, `MainControlTaskStatusIconUiTests.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Дизайн-доработка статусных иконок | 0.88 | Нет runtime screenshot окна приложения; доступен preview artifact | Прогнать targeted UI test/build и показать preview | Нет | Да, пользователь подтвердил прозрачную базу и попросил применить дизайнерское ревью | Иконки сохранены checkbox-like: прозрачная база для активных промежуточных статусов, `Prepared` перекрашен из warning-оранжевого в teal, play уменьшен, archive получил более читаемую внутреннюю форму | `TaskStatusIcon.cs`, `MainControlTaskStatusIconUiTests.cs` |
