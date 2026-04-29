# Настройки Markdown и описаний для аутлайн-clipboard

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка после подтверждения спеки
- Ограничения: соблюдать существующие Avalonia/ViewModel паттерны; на фазе SPEC не менять код; UI-поведение настроек и clipboard flow покрыть Avalonia.Headless/TUnit тестами; не менять persisted task schema
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `specs/2026-04-28-task-outline-clipboard.md`

Если секция не применима, это указано явно внутри секции.

## 1. Overview / Цель
Расширить копирование и вставку задач аутлайном так, чтобы пользователь мог настроить формат экспорта через настройки приложения:

- включать Markdown checklist-формат для задач;
- включать копирование описания задачи отдельными строками под строкой задачи;
- вставлять обратно обычный outline, Markdown checklist-outline и outline с описаниями независимо от текущих пользовательских настроек.
- перед фактической вставкой показывать подтверждение с предварительным просмотром распознанного дерева и целевого места вставки.

Outcome contract:
- Success means: настройки управляют только форматом копирования, а вставка автоматически распознаёт Markdown-чекбоксы и строки описания, показывает пользователю что и куда будет вставлено, и только после подтверждения создаёт заголовки, описания и статус выполненности задач.
- Итоговый артефакт / output: обновлённая рабочая спека, после подтверждения - ViewModel/settings/UI/service/tests changes.
- Stop rules: до фразы `Спеку подтверждаю` не выполнять EXEC-изменения; после реализации завершать только после targeted unit/ViewModel/UI tests, `dotnet build` и доступного `dotnet test`.

## 2. Текущее состояние (AS-IS)
- `TaskOutlineClipboardService` строит plain outline только из заголовков задач.
- Текущий plain copy формат: одна задача на строку, вложенность через ведущие табы; при копировании из дерева уже учитываются текущие фильтры и сортировка через `TaskWrapperViewModel`.
- `TaskOutlineNode` хранит только `Title` и `Children`.
- `ParseOutline` умеет читать plain outline, leading tabs / groups of 4 spaces и простые bullet-маркеры `- `, `* `, `+ `, но не читает Markdown checklist state и description.
- `MainWindowViewModel.CopyTaskOutline(...)` вызывает `TaskOutlineClipboardService.BuildOutline(...)` без параметров форматирования.
- `MainWindowViewModel.PasteTaskOutline(...)` создаёт задачи только с `Title`; `Description` и `IsCompleted` не заполняются из текста.
- `MainWindowViewModel.PasteTaskOutline(...)` создаёт задачи сразу после чтения clipboard, без пользовательского подтверждения и без preview целевого места вставки.
- В проекте уже есть `INotificationManagerWrapper.Ask(header, message, yesAction, noAction)` и `NotificationManagerWrapper` на базе `DialogHost`, которые используются для подтверждений удаления, массовых операций и maintenance actions.
- Текущий `Ask` dialog отображает `Message` простым `TextBlock` без прокрутки, поэтому он не подходит как есть для большого preview вставки.
- `SettingsViewModel` уже persist-ит пользовательские настройки через `IConfiguration`, а `SettingsControl.axaml` содержит секции настроек, но настроек outline clipboard нет.
- `TaskItemViewModel` содержит `Description` и nullable `IsCompleted`; `IsCompleted == true` означает выполненную задачу, `false` - невыполненную, `null` - архивную.

## 3. Проблема
Аутлайн-clipboard сейчас переносит только заголовки в одном формате и вставляет задачи без предварительного подтверждения, поэтому пользователь не может экспортировать задачи в Markdown checklist-вид, переносить описания и безопасно проверить результат распознавания перед созданием задач.

## 4. Цели дизайна
- Разделение ответственности: настройки хранятся в `SettingsViewModel`, форматирование/парсинг - в `TaskOutlineClipboardService`, clipboard adapter остаётся в UI/ViewModel.
- Повторное использование: единый parser должен принимать все поддерживаемые форматы без зависимости от текущих настроек.
- Тестируемость: форматирование/парсинг проверяются unit tests; ViewModel проверяет применение настроек и создание задач; Settings UI и copy/paste flow проверяются UI tests.
- Консистентность: старый plain outline без описаний продолжает копироваться и вставляться как раньше, если новые настройки выключены.
- Обратная совместимость: persisted task schema не меняется; новые пользовательские настройки получают безопасные default values.
- Безопасность пользовательского действия: вставка clipboard считается потенциально массовой операцией и требует explicit confirmation после preview.

## 5. Non-Goals (чего НЕ делаем)
- Не импортируем и не экспортируем даты, planned fields, importance, wanted, repeaters, relations, blockers и archive state.
- Не парсим дату/emoji из заголовка Markdown checklist; например `✅ 2026-04-21` остаётся частью `Title`.
- Не добавляем rich clipboard formats, HTML clipboard, OPML или файловый import/export.
- Не меняем hotkeys `Ctrl+Shift+C` / `Ctrl+Shift+V`.
- Не меняем правила фильтрации и сортировки дерева, которые уже используются при copy из `TaskWrapperViewModel`.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `TaskOutlineClipboardSettings` или константы секции настроек -> имена persisted keys и default values.
- `SettingsViewModel` -> свойства `CopyTaskOutlineAsMarkdown` и `CopyTaskOutlineDescription`, чтение/запись в `IConfiguration`.
- `SettingsControl.axaml` -> отдельная группа настроек outline clipboard с двумя checkbox:
  - `Копирование в Markdown формате`;
  - `Копировать описание`.
- `Resources/Strings*.resx` -> локализованные строки для новой секции, checkbox и hints.
- `TaskOutlineClipboardService` -> options-aware build, auto-detect parse, модель node с title/description/completion state.
- `TaskOutlinePastePreview` или helper в `TaskOutlineClipboardService` -> построение человекочитаемого preview из parse-result и destination.
- `MainWindowViewModel` -> передача текущих settings в build, preflight parse, захват destination, запрос подтверждения и заполнение `Description`/`IsCompleted` только после positive confirmation.
- `TaskOutlinePastePreviewViewModel` / `TaskOutlinePastePreviewDialogViewModel` -> данные dedicated preview dialog: destination label, task count, preview text, confirm/cancel commands.
- `INotificationManagerWrapper` / `NotificationManagerWrapper` -> dedicated async confirmation API для paste preview; существующий `Ask(string message)` нельзя использовать без добавления bounded scrollable preview template.
- `MainScreen.axaml` или отдельный dialog view -> bounded scrollable preview UI с AutomationId для preview container, confirm и cancel.
- `MainControlTreeCommandsUiTests` / settings UI tests -> пользовательские сценарии checkbox + copy/paste + confirmation.

### 6.2 Детальный дизайн
Новые настройки:

```text
TaskOutlineClipboard:CopyAsMarkdown = false
TaskOutlineClipboard:CopyDescription = false
```

Имена ключей могут быть вынесены в статический helper, чтобы не размазывать string literals по коду.

Новая option-модель для сервиса:

```csharp
public sealed record TaskOutlineClipboardOptions(
    bool CopyAsMarkdown,
    bool CopyDescription);
```

Расширение parse/build model:

```csharp
public sealed class TaskOutlineNode
{
    public string Title { get; }
    public string Description { get; }
    public bool? IsCompleted { get; }
    public List<TaskOutlineNode> Children { get; }
}
```

`IsCompleted == null` в parse-result означает, что входной outline не задавал статус. При paste для такого node используется default state новой задачи, а не принудительное значение.

Markdown copy format:

```text
- [x] Бекап настроек и ботстейта ✅ 2026-04-21
- [ ] Показывать почту в аудите заказа и в самом заказе в бипиуме
```

Вложенность в Markdown copy должна использовать 4 пробела на уровень, чтобы результат корректно читался обычными Markdown-редакторами:

```text
- [ ] Родитель
    - [x] Выполненная подзадача
    - [ ] Невыполненная подзадача
```

Description copy format:

```text
- [ ] Родитель
    Описание родителя
    - [ ] Подзадача
        Описание подзадачи
```

Описание пишется сразу после строки задачи, с дополнительным уровнем indentation относительно этой задачи. Для многострочного описания каждая строка описания получает тот же дополнительный отступ. Leading/trailing empty lines описания можно нормализовать, но внутренние непустые строки должны сохраняться.

Чтобы не ломать legacy plain paste, description lines распознаются только в marked-outline mode:

- если вход содержит Markdown checklist task markers `- [x]`, `- [X]`, `- [ ]`;
- или если вход содержит plain bullet task markers `- `, `* `, `+ `.

Для app-generated copy при `CopyDescription=true` и `CopyAsMarkdown=false` task lines должны выводиться с plain bullet marker `- `, чтобы строки описания были отличимы от дочерних задач:

```text
- Родитель
    Описание родителя
    - Подзадача
        Описание подзадачи
```

При `CopyDescription=false` и `CopyAsMarkdown=false` нужно сохранить текущий legacy plain format без bullet markers, чтобы не менять привычный clipboard output:

```text
Родитель
    Подзадача
```

Обработка ошибок:

- пустой clipboard и whitespace-only строки игнорируются как сейчас;
- невалидные checklist-маркеры не должны падать, строка обрабатывается как обычный title после снятия поддерживаемого bullet marker;
- parser не должен зависеть от текущих settings, потому что пользователь может вставить текст из внешнего редактора или из старого clipboard.

Paste confirmation:

- `PasteTaskOutline(...)` сначала читает clipboard и строит `IReadOnlyList<TaskOutlineNode>`.
- Если nodes пустые, диалог не показывается и задачи не создаются.
- Destination вычисляется и захватывается до показа диалога:
  - если есть `destination ?? CurrentTaskItem`, preview сообщает, что задачи будут вставлены внутрь этой задачи;
  - если destination отсутствует, preview сообщает, что задачи будут вставлены в корень списка задач.
- После подтверждения вставка обязана использовать тот же captured destination, который был показан в preview, а не текущий selection на момент нажатия `Да`.
- Если captured destination был удалён или стал недоступен до подтверждения, paste отменяется без создания задач, показывает ошибку и не меняет selection.
- Preview строится из parse-result, а не из raw clipboard, чтобы пользователь видел именно нормализованную структуру, которую создаст paste.
- Preview должен показывать:
  - количество задач, которые будут созданы;
  - целевое место вставки;
  - дерево заголовков с отступами;
  - Markdown status marker, если status был распознан;
  - наличие/текст описания под задачей, если description был распознан.
- Preview dialog должен иметь ограниченную высоту и вертикальную прокрутку, чтобы пользователь мог просмотреть все распознанные задачи перед подтверждением.
- Preview не должен обрезать дерево задач: все создаваемые задачи должны быть доступны через прокрутку.
- Для больших clipboard preview должен рендериться как текстовый/виртуализируемый блок внутри scroll container, а не как неограниченно растущий набор вложенных контролов.
- Минимальный UI-контракт preview dialog:
  - `AutomationId="TaskOutlinePastePreviewDialog"`;
  - `AutomationId="TaskOutlinePasteDestinationText"`;
  - `AutomationId="TaskOutlinePastePreviewScrollViewer"`;
  - `AutomationId="TaskOutlinePastePreviewText"`;
  - `AutomationId="TaskOutlinePasteConfirmButton"`;
  - `AutomationId="TaskOutlinePasteCancelButton"`.
- Нажатие `Да` запускает создание задач из тех же parsed nodes; нажатие `Нет` не меняет хранилище и текущий selection.

Производительность:

- форматирование остаётся линейным по количеству видимых/copied задач;
- parser остаётся линейным по количеству строк clipboard;
- дополнительных обращений к storage при copy не требуется.
- paste confirmation добавляет только построение preview из уже распарсенного дерева; дополнительных обращений к storage до подтверждения быть не должно.

## 7. Бизнес-правила / Алгоритмы
### 7.1 Copy
- `CopyAsMarkdown=false`, `CopyDescription=false`: сохранить текущий plain output.
- `CopyAsMarkdown=true`, `CopyDescription=false`: каждая task line получает marker `- [x] ` если `task.IsCompleted == true`, иначе `- [ ] `.
- `CopyAsMarkdown=true`, `CopyDescription=true`: после каждой task line с непустым `Description` выводятся строки описания с одним дополнительным уровнем отступа.
- `CopyAsMarkdown=false`, `CopyDescription=true`: task lines выводятся как bullet outline `- Title`, description lines - с дополнительным уровнем отступа без bullet marker.
- Для `IsCompleted == false` и `IsCompleted == null` при Markdown copy используется unchecked marker `- [ ]`, потому что archive state не входит в scope экспорта.
- Copy из дерева продолжает использовать текущий `TaskWrapperViewModel.SubTasks`, чтобы сохранять применённые фильтры и сортировку.

### 7.2 Paste auto-detection
- `- [x] Title` и `- [X] Title` -> `Title`, `IsCompleted=true`.
- `- [ ] Title` -> `Title`, `IsCompleted=false`.
- `- Title`, `* Title`, `+ Title` -> `Title`, `IsCompleted=null`.
- Legacy plain line without marker -> task title, `IsCompleted=null`, description detection disabled unless input also contains supported task markers.
- In marked-outline mode, indented non-empty line without supported task marker directly after a task belongs to nearest preceding task as description, not as child task.
- Несколько description lines для одной задачи объединяются через `Environment.NewLine` или `\n` с нормализацией при сохранении.
- Child task line всегда имеет supported task marker в marked-outline mode; это делает generated outline однозначным.
- Jump indentation нормализуется как сейчас: слишком глубокий уровень привязывается к ближайшему доступному parent.

### 7.3 Paste persistence
- При создании task из node:
  - всегда заполнить `Title`;
  - если `Description` непустой, заполнить `Description`;
  - если `IsCompleted.HasValue`, заполнить `IsCompleted`;
  - вызвать `taskRepository.Update(created)` после заполнения полей.
- Для `IsCompleted=true` запрещено напрямую обходить `TaskTreeManager.UpdateTask`; нужно задать `created.IsCompleted = true` перед `taskRepository.Update(created)`, чтобы существующий механизм выставил `CompletedDateTime` и пересчитал availability.
- Acceptance для completed paste: после вставки `- [x]` созданная задача имеет `IsCompleted == true` и `CompletedDateTime != null`.
- Если при реализации выяснится, что текущий `Update` не выставляет `CompletedDateTime` в этом path, это blocker EXEC: нужно исправить path через существующий completion update flow, а не сохранять completed-задачу с пустой датой.

### 7.4 Paste confirmation
- Любая непустая вставка требует подтверждения пользователя.
- До подтверждения запрещено вызывать `taskRepository.Add`, `AddChild` или `Update`.
- Confirmation message должен быть построен после parse и до storage mutations.
- Positive path:
  1. прочитать clipboard;
  2. parse;
  3. захватить destination и построить destination label + preview;
  4. показать confirmation dialog;
  5. после `Да` revalidate captured destination;
  6. создать задачи под тем же captured destination;
  7. выбрать первую созданную задачу и раскрыть parent nodes как сейчас.
- Negative path:
  1. пользователь нажимает `Нет` или закрывает dialog;
  2. задачи не создаются;
  3. `CurrentTaskItem` и tree selection не меняются;
  4. toast не нужен, потому что cancel является штатным действием.
- Destination label:
  - при вставке внутрь задачи: `Внутрь: <TaskTitle>`; если title пустой, использовать id или локализованный fallback;
  - при вставке в root: `В корень списка задач`.
- Deleted destination path:
  - если captured destination не найден при подтверждении, показать локализованную ошибку `PasteTaskOutlineDestinationUnavailable`;
  - не выполнять fallback в root, потому что это не то место, которое пользователь подтвердил.

## 8. Точки интеграции и триггеры
- Settings tab отображает новую секцию/группу outline clipboard.
- Изменение checkbox сразу persist-ится через `IConfiguration`, как остальные настройки.
- `CopyTaskOutline(TaskItemViewModel?)` и `CopyTaskOutline(TaskWrapperViewModel?)` читают `Settings.CopyTaskOutlineAsMarkdown` и `Settings.CopyTaskOutlineDescription`.
- `PasteTaskOutline(...)` всегда вызывает parser без передачи settings, затем показывает confirmation dialog с preview и destination.
- `Ctrl+Shift+C`, `Ctrl+Shift+V` и context menu остаются текущими trigger points.

## 9. Изменения модели данных / состояния
- Persisted task data не меняется.
- Добавляются persisted user settings в конфигурацию приложения:
  - `TaskOutlineClipboard:CopyAsMarkdown`;
  - `TaskOutlineClipboard:CopyDescription`.
- Default values для отсутствующих ключей: `false`.
- Runtime parse model расширяется description/status fields.

## 10. Миграция / Rollout / Rollback
- Миграция не нужна: отсутствующие настройки читаются как `false`.
- При первом запуске после обновления UI показывает оба checkbox выключенными, если пользователь их не менял.
- Rollback: удалить новые settings properties/UI/resource keys и вернуть вызовы `BuildOutline` без options; persisted unknown keys можно оставить, они не влияют на старую версию.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:

- При выключенных новых настройках copy output совпадает с текущим plain outline.
- При включённом `Копирование в Markdown формате` выполненная задача копируется как `- [x] Title`, невыполненная и архивная - как `- [ ] Title`.
- При включённом `Копировать описание` описание каждой задачи копируется следующими строками с дополнительным отступом.
- При включённых обеих настройках Markdown checklist, description и nested tasks roundtrip-ятся через copy/paste.
- Paste распознаёт Markdown checklist и создаёт выполненные/невыполненные задачи независимо от текущих settings.
- Paste для `- [x]` создаёт задачу с `IsCompleted == true` и непустым `CompletedDateTime`.
- Paste распознаёт description lines в marked-outline input и заполняет `TaskItemViewModel.Description` независимо от текущих settings.
- Paste показывает confirmation preview до создания задач.
- Confirmation preview показывает destination: выбранная задача или root.
- Большой confirmation preview прокручивается и позволяет просмотреть все вставляемые задачи, включая последние элементы дерева.
- При отказе в confirmation dialog задачи не создаются, selection не меняется.
- При подтверждении создаётся именно дерево, показанное в preview.
- Если destination, показанный в preview, удалён до подтверждения, paste не создаёт задачи в другом месте.
- Legacy paste обычного outline без markers не начинает ошибочно превращать первую дочернюю задачу в описание.
- Settings UI содержит два checkbox с локализацией и стабильными selectors/AutomationId.

Какие тесты добавить/изменить:

- `TaskOutlineClipboardServiceTests`:
  - legacy plain output unchanged;
  - markdown checklist output for completed/uncompleted/archive;
  - description output for Markdown and non-Markdown generated formats;
  - parse Markdown checklist into title/status;
  - parse descriptions into node descriptions;
  - parse legacy plain nested outline without description regression.
- `MainWindowViewModelTests`:
  - copy passes settings into service;
  - paste fills `Title`, `Description`, `IsCompleted`;
  - paste completed checklist sets `CompletedDateTime`;
  - paste remains independent of settings values.
  - paste asks for confirmation before storage mutations;
  - paste cancel path creates no tasks and keeps selection;
  - paste confirmation message contains destination label and preview tree.
  - paste uses captured destination after confirmation, not current selection changed while dialog is open;
  - paste refuses to create tasks when captured destination is deleted before confirmation.
- UI tests:
  - Settings tab toggles both checkbox and persists values;
  - tree copy hotkey/context menu respects settings;
  - tree paste opens confirmation dialog with destination and preview;
  - large paste preview is scrollable and exposes the last parsed task before confirmation;
  - confirming dialog creates title/description/status from Markdown clipboard;
  - cancelling dialog does not create tasks.

Команды для проверки:

```powershell
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskOutlineClipboardServiceTests/*"
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*"
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/Settings*UiTests/*"
dotnet build src\Unlimotion.sln --no-restore
dotnet test src\Unlimotion.sln --no-build
```

Stop rules для test/retrieval/tool/validation loops:

- если targeted UI tests падают, завершение EXEC блокируется;
- если full test run невозможен из-за внешнего таймаута/окружения, нужно зафиксировать команду, длительность, последний вывод и результаты targeted tests;
- если parser ambiguity обнаружит формат, который нельзя однозначно распознать без изменения output contract, остановиться и обновить spec до продолжения.
- если existing `Ask` dialog не заменён/не расширен bounded scrollable template, завершение EXEC блокируется: paste preview нельзя реализовать простым `Message` `TextBlock`.
- если completed paste создаёт `IsCompleted == true` без `CompletedDateTime`, завершение EXEC блокируется.

## 12. Риски и edge cases
- Plain outline с описаниями без task markers неоднозначен: строка с дополнительным отступом может быть и описанием, и дочерней задачей. Поэтому generated non-Markdown output при `CopyDescription=true` использует bullet task markers.
- External Markdown может содержать обычные paragraphs под list item; parser должен трактовать их как description только в marked-outline mode.
- `IsCompleted == null` при copy теряет archive state и становится unchecked; это осознанный non-goal, чтобы не придумывать несуществующий Markdown marker для архива.
- Multiline descriptions могут содержать строки, похожие на task markers. В marked-outline mode такая строка будет распознана как child task, если стоит на task indentation level и начинается с marker; это допустимый tradeoff для текстового формата.
- Settings UI не должен ломать адаптивность Settings tab; проверить существующие responsive UI tests, если они покрывают настройки.
- Confirmation preview для большого clipboard может быть длинным; нужен bounded scroll container, а не рост dialog за пределы viewport.
- Очень большой preview может быть тяжёлым для UI, если рендерить каждую строку отдельным контролом; предпочтителен один readonly text block/textbox или виртуализируемое представление.
- Async yes-action в existing `Ask` не должен превращаться в unobserved task; создание задач после подтверждения должно запускаться через обработанный async path с toast/error handling.
- Selection может измениться, пока confirmation dialog открыт; paste должен использовать captured destination из preview.
- Captured destination может быть удалён до подтверждения; безопасное поведение - отмена с ошибкой, без fallback в root.

## 13. План выполнения
1. Расширить `TaskOutlineNode` и добавить `TaskOutlineClipboardOptions`.
2. Обновить `TaskOutlineClipboardService` build/parse и добавить unit tests.
3. Добавить persisted settings properties в `SettingsViewModel`.
4. Добавить UI checkbox в `SettingsControl.axaml` и resource keys.
5. Добавить paste preview builder, captured destination model и confirmation flow без storage mutations до подтверждения.
6. Добавить dedicated scrollable preview dialog/template и async confirmation API.
7. Обновить `MainWindowViewModel` copy/paste flow для options, confirmation, destination revalidation и новых node fields.
8. Добавить/обновить ViewModel и UI tests.
9. Запустить targeted tests, build и доступный full test run.
10. Выполнить post-EXEC review и исправить найденные blocking issues.

## 14. Открытые вопросы
Нет блокирующих.

Принятое решение для неоднозначности plain description: при `CopyDescription=true` и `CopyAsMarkdown=false` generated output использует plain bullet task lines `- Title`; это сохраняет возможность автоопределения описаний при paste и не меняет legacy output, пока описание не включено.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля:
  - UI-facing настройки и clipboard flow требуют UI tests.
  - Стабильные selectors/AutomationId должны быть добавлены для новых checkbox.
  - Длительных синхронных операций на UI thread не требуется.
  - Перед завершением EXEC нужны `dotnet build`, targeted UI tests и `dotnet test` либо явный отчёт о невозможности full run.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/TaskOutlineClipboardService.cs` | Options-aware build, parser Markdown checklist/description, расширенный node | Форматирование и импорт outline |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Два persisted checkbox свойства | Управление copy settings |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Передача options в copy, captured destination, confirmation preview, заполнение description/status при confirmed paste | Интеграция clipboard flow |
| `src/Unlimotion.ViewModel/INotificationManagerWrapper.cs` | Dedicated async confirmation API для paste preview | Читаемый и тестируемый preview перед вставкой |
| `src/Unlimotion/NotificationManagerWrapper.cs` | Реализация paste preview confirmation через DialogHost | Desktop confirmation UI |
| `src/Unlimotion/Views/MainScreen.axaml` или новый view | Scrollable template для `TaskOutlinePastePreviewViewModel` | Просмотр всех задач перед подтверждением |
| `src/Unlimotion/Views/SettingsControl.axaml` | Новая группа настроек и checkbox | UI для настроек |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | Английские строки | Локализация UI |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Русские строки | Локализация UI |
| `src/Unlimotion.Test/TaskOutlineClipboardServiceTests.cs` | Unit tests форматов и parser regression | Контракт сервиса |
| `src/Unlimotion.Test/MainWindowViewModelTests.cs` | ViewModel tests copy/paste fields/settings | Интеграция ViewModel |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` и/или settings UI tests | UI regression для settings + copy/paste | Требования AGENTS.override.md |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Copy format | Только plain title outline | Plain title outline, Markdown checklist, outline с описаниями |
| Copy settings | Нет | Два checkbox в Settings |
| Paste parser | Title-only | Title + optional description + optional completion state |
| Paste dependency on settings | Не применимо | Parser не зависит от settings |
| Paste confirmation | Нет | Scrollable preview + captured destination + confirm/cancel до создания задач |
| Paste destination | Текущий selection на момент вызова | Destination из preview фиксируется до dialog и revalidate-ится после confirm |
| Legacy outline | Работает как task tree | Продолжает работать как task tree |

## 18. Альтернативы и компромиссы
- Вариант: для non-Markdown descriptions выводить description line без markers и без изменения task lines.
- Плюсы: ближе к формулировке "описание на следующей строчке с дополнительным отступом".
- Минусы: невозможно надёжно отличить описание от первой дочерней задачи в plain outline, что ломает legacy paste.
- Почему выбранное решение лучше: bullet task markers включаются только когда description copy включён, дают однозначный roundtrip и сохраняют legacy output при default settings.

- Вариант: сохранять archive state отдельным marker.
- Плюсы: точнее переносит состояние.
- Минусы: выходит за пользовательский запрос и не имеет очевидного Markdown checklist эквивалента.
- Почему выбранное решение лучше: spec сохраняет только выполнено/не выполнено, как в requested examples.

- Вариант: вставлять без подтверждения, как сейчас.
- Плюсы: быстрее для power-user flow.
- Минусы: пользователь не видит результат auto-detect parser и destination до создания задач; ошибка clipboard может массово создать неверное дерево.
- Почему выбранное решение лучше: пользователь явно запросил подтверждение с preview; это снижает риск массовой ошибочной вставки.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, алгоритмы, confirmation flow, данные, rollback и ошибки описаны |
| C. Безопасность изменений | 11-13 | PASS | Есть acceptance criteria, тест-план, риски и план выполнения |
| D. Проверяемость | 14-16 | PASS | Открытых blocker нет, команды проверки и file plan указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, alternatives и quality gate заполнены |
| F. Соответствие профилю | 20 | PASS | UI tests и desktop constraints явно учтены |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Настройки copy и auto-detect paste заданы измеримо |
| 2. Понимание текущего состояния | 5 | Указаны текущие service/ViewModel/settings/UI ограничения |
| 3. Конкретность целевого дизайна | 5 | Форматы, settings keys, parse rules и confirmation preview contract определены |
| 4. Безопасность (миграция, откат) | 5 | Task schema не меняется, defaults и rollback описаны |
| 5. Тестируемость | 5 | Unit, ViewModel и UI tests перечислены с командами |
| 6. Готовность к автономной реализации | 5 | Блокирующих вопросов нет, ambiguity решена в spec |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: зафиксировано правило marked-outline mode, чтобы description lines не ломали legacy plain paste; добавлен explicit non-goal для archive state; добавлен обязательный paste confirmation preview с captured destination, cancel path и completed-state persistence checks.
- Дополнение по preview: требование лимита заменено на bounded scrollable preview без обрезания дерева задач, чтобы пользователь мог просмотреть все вставляемые задачи.
- Дополнение после review: existing `Ask` больше не считается достаточным UI; spec требует dedicated scrollable preview/template, revalidation captured destination и проверку `CompletedDateTime` для `- [x]`.
- Что осталось на решение пользователя: нет блокирующих решений; пользователь может подтвердить или скорректировать выбранный non-Markdown формат с bullet task lines.

### Post-EXEC Review
- Статус: PASS
- Реализовано: настройки copy Markdown/description, auto-detect paste Markdown checklist/plain bullet/legacy outline, заполнение `Description`, `IsCompleted` и `CompletedDateTime`, confirmation dialog с destination/task count/full scrollable preview, cancel path без storage mutations.
- Исправлено по ходу EXEC: после `ITaskStorage.Update(...)` созданная задача повторно берётся из cache по `Id`, потому что storage может вернуть `null` при in-place update; иначе вложенные задачи теряли родителя после обновления заголовка/описания/статуса.
- Исправлено по full-test feedback: `CompletedDateTime` для импортируемого `- [x]` выставляется в paste path детерминированно, потому что auto-save по `IsCompleted` может опередить явный update в параллельном test run.
- Проверки: targeted unit/ViewModel/UI tests зелёные; `dotnet test src\Unlimotion.sln --no-build` прошёл 290/290; `dotnet build src\Unlimotion.sln --no-restore` и `git diff --check` прошли без ошибок.
- Остаточные предупреждения: существующие NuGet vulnerability warnings, nullable warnings, preview .NET SDK message и LF->CRLF warnings.

## Approval
Подтверждено пользователем фразой `спеку подтверждаю`; EXEC выполнен.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | delivery-task | 0.90 | Нет блокирующих данных; формат non-Markdown descriptions выбран как app-generated bullet outline | Ожидать подтверждение спеки | Да | Нет | Нужно согласовать output contract до EXEC, потому что parser/output меняют пользовательский clipboard формат | `specs/2026-04-29-task-outline-markdown-description-settings.md` |
| SPEC | requirement-update | 0.91 | Нет блокирующих данных; при реализации нужно проверить, достаточно ли existing `Ask` dialog для multiline preview | Ожидать подтверждение спеки | Да | Пользователь добавил требование подтверждения paste с preview | Confirmation включён в paste contract до любых storage mutations, чтобы пользователь видел нормализованное дерево и destination | `specs/2026-04-29-task-outline-markdown-description-settings.md` |
| SPEC | requirement-update | 0.93 | Нет блокирующих данных; при реализации нужно проверить scroll support в confirmation dialog | Ожидать подтверждение спеки | Да | Пользователь уточнил, что большое preview должно прокручиваться и показывать все задачи | Preview contract обновлён: bounded scroll container без обрезания дерева, с UI test на доступность последней задачи | `specs/2026-04-29-task-outline-markdown-description-settings.md` |
| SPEC | review-fix | 0.94 | Нет блокирующих данных | Ожидать подтверждение спеки | Да | Пользователь попросил исправить review findings | Устранены недоопределённости: dedicated scrollable UI вместо plain Ask, captured destination + revalidation, completed paste must set CompletedDateTime | `specs/2026-04-29-task-outline-markdown-description-settings.md` |
| EXEC | implementation | 0.92 | Нет | Запустить targeted проверки | Нет | Пользователь подтвердил спеку | Реализованы settings, parser/build options, confirmation preview dialog и ViewModel paste flow без создания задач до подтверждения | `TaskOutlineClipboardService.cs`, `SettingsViewModel.cs`, `MainWindowViewModel.cs`, `NotificationManagerWrapper.cs`, `SettingsControl.axaml`, `TaskOutlinePastePreviewControl.axaml`, resources, tests |
| EXEC | test-fix | 0.95 | Нет | Повторить проверки и финализировать | Нет | Нет | Исправлена потеря родителя при рекурсивной вставке после `Update`, когда storage обновляет VM in-place и возвращает `null` | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.Test/MainWindowViewModelTests.cs` |
| EXEC | full-test-fix | 0.96 | Нет | Повторить полный test run | Нет | Нет | Full `dotnet test` выявил гонку `CompletedDateTime` для `- [x]`; paste теперь проставляет дату завершения сразу при импорте completed state | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.Test/MainWindowViewModelTests.cs` |
| EXEC | verification | 0.97 | Нет | Передать результат пользователю | Нет | Нет | Пройдены unit/ViewModel/UI тесты, полный `dotnet test` 290/290, build и diff check; warnings не блокируют задачу и не появились как ошибки | `src/Unlimotion.Test/*`, `tests/Unlimotion.UiTests.*`, `src/Unlimotion.sln`, `specs/2026-04-29-task-outline-markdown-description-settings.md` |
