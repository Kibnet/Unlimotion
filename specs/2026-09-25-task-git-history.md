# История изменений задачи из Git в карточке

## 0. Метаданные

- Фаза: EXEC, пользователь подтвердил SPEC фразой «Спеку подтверждаю» 2026-09-25.
- Форма: Expanded по центральному `templates/specs/_template.md`: новый UI и межмодульный read-only контракт чтения Git; short неприменим.
- Тип: delivery-task; основной профиль `dotnet-desktop-client`, overlay `ui-automation-testing`; context `testing-dotnet`.
- Владелец: Павел; authoring и будущая интеграция — основной агент.
- Масштаб: large по числу взаимодействующих компонентов, умеренный риск для данных благодаря read-only реализации.
- Baseline: центральный `model-behavior-baseline`; поверхность Codex desktop / Windows / PowerShell. Точный model ID и reasoning не проверялись, настройки модели не меняются. Model eval: не применимо, продуктовая .NET-фича.
- Checkout: `63e5/Unlimotion`, исходный HEAD `0a94426d`, detached HEAD, исходное дерево чистое. До EXEC ветка не создаётся; планируемая рабочая ветка `feat/task-git-history` после Git preflight.
- Проверен SDK `10.0.401`; `global.json` требует `10.0.400` с `latestPatch`; тесты TUnit/Microsoft.Testing.Platform.
- Instruction stack: центральные `AGENTS.md`, `routing-matrix`, `creator-vibe-lens`, `model-behavior-baseline`, `tool-execution-baseline`, `collaboration-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `ui-automation-testing`, `spec-linter`, `spec-rubric`, `review-loops`; локальный `AGENTS.override.md`. Применён skill `creator-vibe` для интерпретации визуального референса.
- Ограничения: на SPEC изменяется только этот документ. После approval — локальная реализация и проверки; публикация, push, PR, установка и релиз этим документом не разрешаются.

## 1. Overview / Цель

Исходное поручение: «Давай реализуем в карточке задачи полноценную историю изменений задачи на основе гит коммитов. Как это будет выглядеть можно позаимствовать в Arm, там есть хорошая реализация которая мне нравится визуально».

Пользователь открывает задачу, раскрывает историю и видит, кто и когда зафиксировал изменения, какие поля изменились и их значения до/после. Визуальная основа — раскрываемая таблица истории из Arm, адаптированная к ширине карточки Unlimotion.

Success means: все доступные в локальном Git коммиты выбранной задачи можно последовательно просмотреть; изменения полей читаются без разбора JSON; неизвестность, локальные незакоммиченные изменения и границы доступной истории обозначены явно.

Итоговый артефакт EXEC: работающая панель в обычной карточке, read-only Git provider, семантический diff, локализация, unit/integration/UI tests и просмотренное визуальное evidence. SPEC-артефакт — этот документ с wireframe.

Stop rules: SPEC останавливается после review и запроса exact approval. EXEC завершается только после обязательных проверок и post-EXEC review; блокеры/непроведённые проверки сообщаются как незавершённость. Дополнительные исследования прекращаются после закрытия конкретного риска.

## 2. Текущее состояние (AS-IS)

Проверено по локальному исходному коду:

- `src/Unlimotion/Views/MainControl.axaml`: внизу карточки есть `CurrentTaskStatusHistorySection`, `StatusHistoryExpander`, `StatusHistoryItems`. Источник — `TaskItemViewModel.StatusHistory`; полной истории полей нет.
- `src/Unlimotion.Domain/TaskItem.cs`: задача содержит название, описание, статус, критерии завершения, плановые даты/длительность, четыре вида связей, повторение, важность, желание, служебные даты, версию, `AgentExecution`, extension data. `StatusHistory` хранится в самой задаче.
- `src/Unlimotion/Services/BackupViaGitService.cs`: уже используется LibGit2Sharp; `Push` может stage/commit сразу все изменения. Автор такого коммита — настроенная Git-подпись, а время — время создания коммита, не момент каждой пользовательской правки.
- Путь Git backup берётся прежде всего из активного `FileStorage.Path`. `TaskSourceManager` переключает локальные/серверные пространства. `TaskItemViewModelContext.SourceId` определяет пространство задачи.
- `src/Unlimotion.FileStorage/FileTaskStorage.cs`: задачи — файлы непосредственно в каталоге хранилища; имена могут отличаться от `Id`. Есть отображение `_taskFilePaths` и `ReadDirectoryAsync().FilesByTaskId`; разрешение пути сейчас приватное. Нельзя просто предполагать `<Id>.json`.
- В `TaskItem`/вложенных моделях есть defaults и нормализация. Десериализация старого blob в live-модель не годится как единственный источник diff: может создать значения, которых в коммите не было.
- Arm: `src/Arm.Client/MiniControls/StatusBar.axaml` и `.axaml.cs`, `src/Arm.Client.ViewModel/Models/ChangeLog.cs`, `FieldChange.cs`. Раскрываемая панель содержит таблицу «Кто менял / Когда менял / Источник / Изменения», значки типа изменения, selectable-текст, красное старое и зелёное новое значение, reload и переключатель `@` для метаданных.
- Arm использует серверные revisions и Eremex. Здесь заимствуются композиция и способ чтения, зависимость на Arm/Eremex не добавляется. Референс исследован по XAML/моделям; живое окно Arm и screenshot не проверялись, pixel-perfect сходство не заявляется.
- Проверены существующие `MainControlTaskCardLayoutUiTests`, `ReadmeDemoHeadlessTests`, AppAutomation TestHost, Headless/FlaUI authoring, `.github/workflows/tests.yml`, scripts записи UI evidence. Полные тесты/сборки в SPEC не запускались.

## 3. Проблема

Карточка показывает только переходы статуса. История остальных изменений уже может находиться в Git, но из карточки недоступна.

## 4. Цели дизайна

- Сохранить узнаваемую структуру истории Arm и читаемость узкой карточки.
- Разделить чтение Git, сравнение JSON, форматирование и состояние UI.
- Не блокировать UI, не выполнять сетевые или записывающие Git-операции при просмотре.
- Не менять формат задач, семантику статусов и расписание backup.
- Сделать воспроизводимыми history, error, paging и task/source-switch сценарии на синтетических репозиториях.

## 5. Non-Goals

- Восстановление/откат задачи или отдельных полей, checkout старой версии.
- Новый журнал событий, коммит на каждое нажатие, изменение автокоммита или push/pull.
- Серверная история без локального Git, remote fetch, поиск в других пространствах/репозиториях, недостижимых commits, reflog или удалённых ветках.
- Отдельный экран глобальной истории/удалённых задач, поиск и фильтрация всего журнала, Git-граф веток.
- Изменения Arm, зависимость на его UI-библиотеки, редизайн всей карточки.
- Git-история не выдаётся за каждое промежуточное редактирование: несколько правок между коммитами восстановить из Git нельзя.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `ITaskHistoryProvider` и immutable DTO в `Unlimotion.ViewModel`: запрос страницы по захваченному source/task context, результат/diagnostics/cursor, CancellationToken; без типов LibGit2Sharp. DTO списка хранят preview и ссылки на immutable blob/path для раскрытия полного значения; загрузка полных значений отдельным запросом, чтобы paging не удерживал все большие JSON в памяти.
- `GitTaskHistoryProvider` в `Unlimotion/Services`: определение локального источника, чтение commits/blobs в фоне, immutable HEAD snapshot, paging, обнаружение неполноты и изоляция источников.
- `TaskHistoryDiffBuilder`: сравнение сырых JSON-токенов; стабильные field paths и типы изменений без UI и мутаций domain-моделей.
- `TaskHistoryViewModel`: состояние одной открытой карточки, refresh/load-more, cancellation/version gate, выбор служебных полей, форматирование и безопасное представление длинного текста.
- `TaskHistoryView.axaml`: раскрываемый блок и элементы строк, адаптивная раскладка, стабильные automation IDs.
- `MainWindowViewModel` / `MainControl` / `App`: lifecycle открытой карточки, DI provider, реакция на выбор задачи и источника.

### 6.2 Детальный дизайн и visual planning artifact

Внизу карточки вместо одиночного блока истории статусов — общий раздел «История изменений». По умолчанию свёрнут. Два режима внутри: «Изменения Git» (по умолчанию) и «Статусы». Второй показывает прежний `StatusHistory` без потери записей и без требования Git. Действующие automation IDs статусов сохраняются у вложенного блока; тест ожидания прежнего положения обновляется по новому контракту.

Wireframe (часть этой SPEC, не screenshot готового приложения):

```text
▾ История изменений                                  [↻]
  [Изменения Git] [Статусы]        [ ] Служебные поля
  Git хранит состояния на момент коммита.

  Ещё не в Git
  Описание: «Подготовить макет» → «Подготовить и согласовать макет»
  ─────────────────────────────────────────────────────────────
  Кто менял   Когда              Источник      Изменения
  Павел       25.09.2026 12:40   Git a12bc34   Статус: Подготовлено → Выполняется
                               Изменения      Срок: 26.09 → 27.09
  ─────────────────────────────────────────────────────────────
  Павел       24.09.2026 18:20   Git b23cd45   + Критерий: «Макет согласован»
                                              − Блокирует: «Подготовить текст»
  ─────────────────────────────────────────────────────────────
  Показано 50 коммитов                         [Показать ещё]
```

Узкая карточка (ширина содержимого менее 700 DIP):

```text
▾ История изменений                          [↻]
  [Изменения Git] [Статусы]
  [ ] Служебные поля

  Павел · 25.09.2026 12:40
  Git a12bc34 · Изменения
  Статус: Подготовлено → Выполняется
  Срок: 26.09 → 27.09

  Павел · 24.09.2026 18:20
  Git b23cd45 · Добавлен критерий
  + Критерий: «Макет согласован»
                                      [Показать ещё]
```

- Признаки Arm, сохраняемые в обоих layouts: раскрытие внутри карточки, группировка полей по revision/commit, кто/когда/источник, «было → стало», знаки добавления/удаления, разные цвета старого/нового, обновление и служебные поля. Светлая/тёмная темы используют semantic resources. Цвет дополняет текст и символы.
- Внутренний viewport истории ограничен примерно 420 DIP; строки виртуализированы. Размер шрифта наследуется от Unlimotion, не копируется мелкий FontSize=10 из Arm. Никакого обязательного горизонтального скролла на узкой карточке.
- Длинные description/JSON значения имеют короткий preview и «Развернуть»; полный текст доступен для выделения/копирования, строки переносятся. Пустая строка, `null` и отсутствующее поле различимы («пустая строка», «не задано», «поле отсутствовало»).
- Источник показывает Git + короткий SHA, message первой строкой. Полные SHA/message, Git author/committer и исходное время с offset доступны в раскрываемых деталях. Основной автор — `Author.Name`, время — `Author.When` в локальной зоне; подпись поясняет, что это данные Git. Не выводить автора правки из текущей учётной записи или message.
- Совпадение авторов или имён не доказывает конкретное физическое лицо; поле называется «Автор коммита» в accessibility/подсказке.
- Показ первых 50 подходящих commits; «Показать ещё» без искусственного общего лимита. Порядок — обратный топологический с временным приоритетом, чтобы потомки предшествовали родителям даже при неверных часах автора.
- Отдельная первая строка «Ещё не в Git» сравнивает сохранённый файл на диске с HEAD. Без фиктивного SHA, автора и времени коммита. Она не включается в число commits. Несохранённый ввод редактора не объявляется сохранённой Git-историей; после фактического сохранения обновляется snapshot.
- Только раскрытая панель загружает данные. Смена задачи/пространства отменяет запрос и немедленно убирает прежние строки; запоздалый ответ не применяется. Состояние `@`, выбранный режим и раскрытие можно сохранить на время сессии, но строки/cursor привязаны к source/task/HEAD.
- Refresh заново захватывает текущий HEAD и файл. При повторном открытии проверяется актуальность snapshot. Изменение задачи помечает открытый snapshot устаревшим и обновляет local row с debounce; смена HEAD обнаруживается при refresh/reopen, без нового постоянного polling. Показывается время чтения и доступна явная кнопка обновления.
- До/после video: на EXEC снять meaningful baseline существующего блока статусов и passing flow новой панели из автоматизированного UI test run на синтетических данных. Конкретные пути и invocation записать в §11/журнал. Если recorder недоступен, допустим только документированный объективный fallback со screenshots/logs и командой UI tests. Артефакты local-only, не коммитятся по умолчанию.

### 6.3 User-Observable Scenarios

| Scenario | Действие | Видимый результат | Evidence required | AC |
| --- | --- | --- | --- | --- |
| S1 | Раскрыть историю существующей задачи | Кто/когда/Git/изменённые поля, новые commits в начале | Реальный временный Git repo + UI screenshot/video | AC1, AC2 |
| S2 | Изменить описание, сроки, связи и критерии; сохранить, затем закоммитить через fixture | Сначала «Ещё не в Git», после refresh — commit с точным diff | Integration + UI flow | AC2, AC3 |
| S3 | Переключить задачу/пространство во время медленной загрузки | Ни одного чужого результата | Controlled delayed provider + UI test | AC4 |
| S4 | Просмотреть >50 изменений, merge и rename | Последовательные страницы до конца доступной истории, корректные merge/rename подписи | Git fixtures + paging UI | AC5 |
| S5 | Открыть без Git/в server mode/при ошибке | Понятная причина; статусы доступны; retry при ошибке | Negative UI tests | AC6 |
| S6 | Читать длинное описание в узкой карточке/тёмной теме | Текст доступен полностью, поля не обрезаны, layout следует wireframe | 360/480/900 DIP, обе темы, просмотренные кадры | AC7 |
| S7 | Включить служебные поля, открыть режим «Статусы» | Метаданные видны по запросу; прежние переходы статусов сохранены | Diff + headless UI + regression tests | AC8 |
| S8 | Открыть неполную/повреждённую историю | Граница доступности/проблемный commit явно помечены; нет ложного «создания» | Shallow/missing/corrupt fixtures | AC5, AC6 |

### 6.4 State / Interaction Matrix

| Состояние | Триггер | Результат | Конкурентность/ошибка |
| --- | --- | --- | --- |
| Collapsed | Expand | Loading → Ready/Empty/Unavailable/Error | До раскрытия provider не вызывается |
| Loading | Другая задача/источник, закрытие карточки | Cancel + clear | Проверка generation и source/task перед публикацией |
| Ready | Load more | Append той же версии HEAD | Кнопка защищена от двойного запроса; ошибка сохраняет загруженные строки |
| Ready | Refresh | Новый session/cursor и чтение текущего HEAD | Не смешивать страницы двух HEAD; прошлые строки обозначены как обновляемые |
| Ready | Сохранение задачи | Обновление local row | Читать disk snapshot после завершения сохранения; частичный JSON → retry/error |
| Empty | Истории commits нет | «Задача ещё не попадала в Git» | Отличается от отсутствующего Git и полностью отфильтрованных metadata |
| Metadata-only | Служебные поля выключены | «Есть только служебные изменения» + включить | Не заявлять отсутствие истории; paging остаётся доступным |
| Unavailable | Нет repo/сервер/неподдерживаемая native library | Причина + режим «Статусы» | Не выполнять init/clone/pull |
| Error | Повторить | Новый запрос | Ошибка только истории, карточка и редактирование работают |
| Partial | Нет родителя/нечитаемый blob | Пометка неполноты + доступные записи | Не сравнивать неизвестное состояние с пустой задачей |

### 6.5 Decision Ledger

| Решение | Owner | Выбор | Confidence | Риск предположения | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Визуальная основа | user | Таблица Arm, адаптивные строки при узкой карточке | 0.95 | Вкус уточняется по wireframe | Нет, включено в approval SPEC |
| Сохранение истории статусов | agent | Второй режим в общем разделе | 0.9 | Лишний шаг до статусов; не теряем события между commits | Нет |
| Полнота | agent | Все локальные commits, достижимые из зафиксированного HEAD, в пределах активного пространства | 0.95 | Недоступные/другие refs не показаны; граница явно описана | Нет |
| Незакоммиченные изменения | agent | Отдельный read-only diff сохранённого файла к HEAD | 0.9 | Не охватывает несохранённый ввод | Нет |
| Merge | agent | Один entry/commit; merge diff относительно первого родителя, явно подписан | 0.85 | Часть изменений также видна в commits ветки | Нет |
| Restore/commit действия | agent | Вне просмотра истории | 0.95 | Это отдельные изменяющие данные функции | Нет |
| Paging и VM lifetime | agent | 50 commits, snapshot cursor, VM на карточку | 0.9 | Требует полноты и cancellation tests | Нет |
| Публикация | user | Не запрошена | 1.0 | Отдельный authorization scope | Нет, вне EXEC |

### 6.6 Runtime / Config / Data Contract Matrix

| Область | Source of truth | Изменение | Совместимость | Проверка |
| --- | --- | --- | --- | --- |
| Task/source identity | Выбранная задача + SourceId + её фактический FileStorage | Захват неизменяемого history context | Ни fallback на чужой активный source, ни общий cache по Id | Два пространства с одинаковым TaskId |
| Git history | Repository текущего каталога/родительского repo и его HEAD | Только локальное чтение | .git directory/worktree file; задачи могут быть в subdirectory | Repo/worktree/subdirectory fixtures |
| Task path | Реальное соответствие file↔JSON Id | Узкий read-only resolver на FileTaskStorage при необходимости | Не менять naming/writes; validation пути сохраняется | Имя без extension, отличающееся имя, traversal/ambiguity |
| Исторические значения | Сырые blobs commit/parent | Diff calculated DTO | Не вызывать миграции, setters или генерацию defaults | Legacy + unknown field fixture |
| Backup | Существующий BackupViaGitService | Просмотр не вызывает его mutation methods | Работа без remote и при BackupEnabled=false | HEAD/index/files/config read-back |
| Persisted config/model | Текущая схема | Не меняется | Миграция отсутствует | Existing storage/serialization tests |

## 7. Бизнес-правила / Алгоритмы

1. По выбранной задаче захватить source ID, storage kind и canonical local path. Разрешить repository discovery, сохранив границу task-space directory: не искать совпадающий Id в соседних пространствах. Task path/Id проверяются до файлового доступа.
2. Найти текущий файл через mapping storage, подтвердить JSON Id. Историческую identity определяет Id, а не расширение/текущее имя. При дубликатах — явная ошибка неоднозначности, не случайный выбор файла. Provider не вызывает `ReadDirectoryAsync`/`ReadGraphAsync`: они изменяют cache и могут создавать lock-файл. Нужен side-effect-free resolver/снимок mapping; при обнаружении stale mapping допустим read-only scan непосредственно task files с проверкой Id и уникальности. Недоступный файл и ещё не сохранённая новая задача различаются.
3. Открыть собственный Repository на worker thread и зафиксировать HEAD SHA. Чтение objects не использует общий mutable Repository с backup. Не держать UI/shared write lock на время обхода истории. Не писать config/safe.directory, index, refs или рабочие файлы.
4. Обойти DAG всех доступных ancestors HEAD в обратном топологическом порядке, каждый SHA один раз. В обычном commit сравнить blob задачи с его родителем; в merge — с первым родителем. Содержательные изменения в веточных commits сохраняются; merge с diff явно помечается «Слияние: относительно первого родителя». Совпадение с первым родителем не создаёт пустую строку merge.
5. Rename отслеживать в пределах каталога пространства: mapping по Id через изменённые tree entries и blobs, а не только heuristic similarity. Поддержать смену имени/расширения, delete/recreate с тем же Id и разные пути в merge parents. Blob cache ограниченный на session; дерево целиком не десериализовать для каждого commit. Перенос между пространствами не объединяет их историю.
6. Commit относится к задаче при изменении содержимого, создании/удалении либо rename её файла. Чистое форматирование JSON не создаёт изменения поля. Rename без изменения JSON — служебное изменение пути. Не связанный с задачей commit не показывается.
7. Root с доказанно отсутствующим parent означает «Первое сохранение в Git», не дату создания задачи. Пустой repository с unborn HEAD означает отсутствие commits, при сохранённом файле доступна только строка «Ещё не в Git». Если parent отсутствует из-за shallow/missing object — «Предыдущее состояние недоступно», без выдуманного diff от пустого объекта. Повреждённые старые blobs дают diagnostic на соответствующем commit, история продолжается где возможно, без сравнения через разрыв.
8. Сравнивать raw JSON рекурсивно по union свойств. Known поля получают локализованные названия/значения; unknown поля сохраняются с точным JSON path. Legacy `IsCompleted`, `CompletedDateTime`, `ArchiveDateTime` не превращать в выдуманные события современного StatusHistory. Реальные legacy-переходы показываются по умолчанию: `false` — «Не готово», `true` — «Выполнено», явный `null` — «Архивировано»; отсутствие свойства не равно `null`. Исходные значения и имена полей доступны в деталях. Даты завершения/архивации legacy тоже основные поля. При переходе legacy→современная схема только доказанно эквивалентная замена статуса/соответствующей даты считается служебной миграцией, без ложного бизнес-события; неэквивалентность и конфликт старых/новых полей остаются видимыми. Нормализация исключительно calculated, без записи в задачу.
9. Основные поля: Title, Description, Status и legacy-статус/даты из п.8, CompletionCriteria, PlannedBeginDateTime, PlannedEndDateTime, PlannedDuration, ContainsTasks, ParentTasks, BlocksTasks, BlockedByTasks, Repeater, Importance, Wanted. Служебные по умолчанию скрыты: Id/UserId, Created/Updated/UnlockedDateTime, IsCanBeCompleted, Version, raw StatusHistory, AgentExecution, путь файла и только доказанно эквивалентные изменения схемы. Неизвестные поля не прятать автоматически.
10. Связи сравнивать как множества Id: добавлено/удалено; перестановка не создаёт ложного diff. Для подписи использовать текущее название из того же пространства с доступным Id и пометкой «текущее название»; отсутствующая задача отображается по Id. Исторические названия не обещаются и не выдаются за восстановленные.
11. Критерии сопоставлять по Id: добавление/удаление, текст и отметка; изменение порядка показывать отдельной строкой, если порядок изменён. При отсутствующих/повторных Id — достоверный fallback old/new JSON. Repeater сравнивать по полям; остальные массивы — предсказуемый структурный diff без выдуманных ключей.
12. JSON reader допускает глубину до 64; structured diff — до 16, затем показывает изменённую ветку целиком с lazy old/new value. Preview — до 300 символов; полное значение раскрывается отдельно. Blob до 8 MiB читается в памяти; выше — diagnostic с размером/SHA и пометкой неполноты, без молчаливого пропуска. Невалидный/слишком глубокий JSON также получает diagnostic. Cache decoded blobs ограничен 32 MiB на открытый history session с вытеснением; DTO всех страниц хранят только bounded preview + SHA/JSON path, без ссылок на полные строки/JToken или вытесненные cache entries. Полные before/after значения открываются в одной общей области деталей выбранного поля; выбор другого поля/закрытие освобождает прежние значения. Кеш полных деталей также ограничен 32 MiB и не удерживается через DTO/closures; смена карточки освобождает session и детали. Размеры — внутренние начальные лимиты, уточняются по замерам без уменьшения заявленного coverage и без добавления пользовательских настроек.
13. Cursor хранит source/path/task/HEAD и frontier обхода, не offset на изменяющемся HEAD. На страницу максимум 50 относящихся к задаче commits, на один background batch максимум 1000 посещённых commits. Если batch не нашёл 50, возвращается continuation и честное «Продолжить поиск», не «История закончилась». Cancellation проверяется между commits и чтениями blobs. UI virtualization ограничивает количество controls, DTO cache/объекты имеют lifecycle карточки.

## 8. Точки интеграции и триггеры

- Открытие истории при `CurrentTaskItem` и `DetailsAreOpen`; переключение режима Git/статусы.
- Смена `CurrentTaskItem`, SourceId, активного storage/пространства и закрытие карточки: cancellation/dispose, сброс данных.
- Refresh/load-more из панели. Событие завершённого сохранения/внешнего обновления выбранной задачи: актуализация local row, без вызова save.
- DI `App` создаёт provider, `MainWindowViewModel` держит один coordinator истории; каждый `TaskItemViewModel` не получает тяжёлый Git cache.
- В server mode provider возвращает unsupported context; lookup локального repo по запасному пути запрещён.

## 9. Изменения модели данных / состояния

Новых persisted полей и миграции нет. Новые calculated DTO: history context, entry, field change, page/cursor, diagnostics. Entry содержит SHA/parents, author/committer/time/message, change kind и JSON changes. Local row — отдельный вид без commit metadata. UI state: expanded/mode/include-metadata/loading/error/partial/has-more, generation и session snapshot.

## 10. Миграция / Rollout / Rollback

Первое раскрытие читает существующий Git; история появляется без переиндексации/импорта. Репозитории без remote и с выключенным backup работают. Откат — revert feature commits/возврат старой сборки; task files/Git data не требуют отката, поскольку просмотр их не меняет. Перед/после integration tests сравнивают task bytes, HEAD, index, refs и config.

## 11. Тестирование и критерии приёмки

- AC1: история открывается в карточке выбранной задачи; сохраняет перечисленные признаки Arm и доступ к StatusHistory.
- AC2: для каждого поддерживаемого поля и unknown/legacy JSON показаны точные before/after; чужие commits и форматирование JSON не превращаются в изменения задачи.
- AC3: local row отделён от commits; после создания commit и refresh нет двойной локальной записи. Просмотр не вызывает Git writes/network/save.
- AC4: deferred results не попадают в другую карточку/пространство; ошибки не ломают редактор; UI thread не выполняет Git traversal.
- AC5: доступна вся достижимая история порциями; merge, rename, root, delete/recreate, empty/shallow/missing/corrupt data обрабатываются согласно §7; paging не пропускает и не дублирует commits.
- AC6: состояния loading/empty/metadata-only/unavailable/error/partial имеют различимые сообщения и recovery; история статусов работает без Git.
- AC7: при ширине панели 360/480/900 DIP в светлой/тёмной теме читается полный раскрытый текст, нет наложения/обрезания controls; keyboard/copy доступны; ru/en локализованы.
- AC8: переключатель служебных полей показывает raw metadata без потери исходных данных; прежние status tests и карточка сохраняют поведение с обновлённой композицией.
- AC9: обычная desktop сборка, полный main test suite, полный Headless suite и targeted FlaUI flow проходят. Обязательное визуальное evidence просмотрено. Отсутствующий green не объявляется выполненным AC.

### Acceptance-to-Test Matrix

| AC | Automated test (план) | Visual/manual check | Evidence artifact (план) | Если не проверено |
| --- | --- | --- | --- | --- |
| AC1 | MainControlTaskHistoryUiTests + AppAutomation flow | Сопоставить открытые состояния с wireframes/признаками Arm | Screenshot + UI run video | SPEC: код ещё не написан |
| AC2 | TaskHistoryDiffBuilderTests: все поля, unknown, legacy, null/missing, критерии, связи; UI legacy `false→true→null→false` при скрытых metadata и эквивалентная миграция в Status без ложного события | Проверить понятность русских названий и видимость старых переходов | Main suite report + legacy UI кадр | SPEC |
| AC3 | GitTaskHistoryProviderTests: working-tree/HEAD и отсутствие writes; Headless refresh | «Ещё не в Git» до/после fixture commit | Test log + кадры | SPEC |
| AC4 | TaskHistoryViewModelTests с delayed provider; Headless task/source switch | UI responsive во время traversal | Test log + video | SPEC |
| AC5 | Временные repo: >120 task commits, >1000 unrelated, branches/merge/rename/delete/recreate/shallow/bad blob/worktree | End/continue/paging captions | Main suite report | SPEC |
| AC6 | MainControlTaskHistoryUiTests: каждое состояние, retry/load-more failure | Открытые error/empty/server states | Кадры + report | SPEC |
| AC7 | Layout tests 360/480/900, long values, ru/en; targeted FlaUI keyboard/copy | Светлая/тёмная темы, inspected screenshots | Кадры и passing flow video | SPEC |
| AC8 | Diff metadata + existing status/card regression tests | Статусы доступны и Git отсутствует | Main/headless reports | SPEC |
| AC9 | Build + full main/headless + targeted FlaUI | Обычный desktop запуск на временном source | Build/run logs и UI evidence | SPEC |

Проверки выполняются последовательно, поскольку UI tests имеют общий mutable state. На EXEC применить skill `run-tunit-tests` до первого запуска. Targeted: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/GitTaskHistoryProviderTests/*"` и соответствующие классы diff/VM/UI. Команды discovery уточнить по установленному MTP без VSTest `--filter`.

Обязательные команды после targeted checks:

```powershell
$taskHistoryResults = Join-Path 'artifacts/task-git-history/tests' ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage restore -Project main -ResultsRoot $taskHistoryResults
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage build -Project main -ResultsRoot $taskHistoryResults
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project main -ResultsRoot $taskHistoryResults
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage restore -Project headless -ResultsRoot $taskHistoryResults
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage build -Project headless -ResultsRoot $taskHistoryResults
pwsh -File scripts/ci/Invoke-TestStage.ps1 -Stage test -Project headless -ResultsRoot $taskHistoryResults
dotnet run --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -- --treenode-filter "/*/*/TaskHistoryFlaUiTests/*"
git diff --check
```

Полный main/headless обязателен из-за integration в общие VM/storage read contract и существующего CI gate. CI имеет 30-minute job timeout; локальную длительность не предполагаем. Перед long run проверить restore/runner, объявить команду и путь логов; при отсутствии прогресса исследовать evidence, не повторять timeout вслепую. Изменения вне desktop build не заявляются проверенными на Android/iOS без отдельного evidence.

Performance: фиксировать elapsed и allocations/peak memory на deterministic fixture с 10 000 commits, из них 120 task changes. Отдельно — 160 task commits с различающимися описаниями по 512 KiB: измерения после 50/100/150 записей, последовательного открытия деталей и закрытия карточки. Проверить ограничение cache и отсутствие удержания полного текста/JToken DTO предыдущих страниц; ожидается рост только previews/metadata, не всех исходных descriptions. Проверяемые инварианты — zero provider calls для закрытой панели, отсутствие чтения полных деталей по UI запросу до раскрытия, ограниченный batch, cancellation и отсутствие Git work на UI thread. Не обещать миллисекундный SLA без измерения; записать фактические числа на EXEC.

UI evidence план: `artifacts/task-git-history/ui/before-status.*`, `after-history.*`, `narrow-dark.png`, `error.png`, логи run. Запись должна быть привязана к automation run, не к ручному показу unrelated приложения. Использовать существующие patterns `record-status-contract-evidence.ps1`/FlaUI harness; новый небольшой wrapper допустим только если нужен этому flow. Screenshots/video на синтетических данных; реальное личное task storage не использовать. Baseline старых статусов meaningful; полного Git flow до реализации не существует.

## 12. Риски и edge cases

- Git metadata может содержать автоматическую подпись; не приписывать правки человеку без данных.
- Старый/переписанный/неполный Git не является полным аудитом всех действий; подписи границ обязательны.
- Rename и дубликаты Id: не объединять разные источники и неоднозначные файлы.
- Raw JSON diff сохраняет факты, но legacy migration может быть многословной; служебный фильтр убирает шум без потери доступа.
- Deep/large JSON, binary blobs, некорректные даты и невалидные enum не падают в UI; raw fallback/diagnostic с сохранением истины.
- Concurrent commit/pull/GC: snapshot SHA/cursor, isolated repo reader, явный retry при исчезновении objects.
- Цвета и truncation могут скрыть смысл; semantic contrast, знаки, wrapping и reveal/copy проверяются UI.

### Expected User Review Objections

| Замечание | Почему вероятно | Решение | Статус |
| --- | --- | --- | --- |
| «Покажи как в Arm, а не список SHA» | Явный референс | Группы field changes с автором/временем/источником, цвета и @, wireframe | mitigated |
| «Куда делись статусы?» | Это уже существующая полезная история | Режим «Статусы» без Git, сохранённые записи | mitigated |
| «Я только что изменил, а в истории пусто» | Коммиты периодические | Отдельный local row и пояснение гранулярности | mitigated |
| «Показывает GUID вместо понятной задачи» | Связи — Id | Текущее название + идентификатор, честный fallback | mitigated |
| «Старые записи обрезали после первых 50» | Pagination может стать скрытым лимитом | Продолжение до конца + bounded search batch без ложного end | mitigated |
| «Слишком тесно в карточке» | Четыре колонки Arm широкие | Адаптивный layout, полные значения по раскрытию | mitigated |

Rework Prevention Checklist: исходный сценарий, наблюдаемые состояния, допущения и evidence связаны в §6/11; вероятные замечания закрыты в границах scope; role review и результаты — §19. Ни тестов, ни visual evidence готовой функции пока нет.

## 13. План выполнения

1. После exact approval: branch/toolchain preflight; characterization текущих статусов и baseline UI evidence на synthetic source.
2. Immutable history DTO/context + read-only file mapping; raw diff и integration Git fixtures, включая историю с branches/rename.
3. Карточный coordinator/VM с cancellation, paging и states; новый control, localization и встраивание старой истории статусов.
4. UI coverage в существующем Headless/FlaUI harness; targeted проверки → desktop build → полный обязательный набор.
5. Просмотр кадров/video, замеры, independent/adversarial post-EXEC review, исправление findings и повтор только затронутых проверок. Заполнение фактической AC matrix.

## 14. Открытые вопросы

Блокирующих продуктовых вопросов не выявлено. Визуальная адаптация и границы чтения предложены явно в §6 и принимаются вместе со SPEC. Переход в EXEC ещё не разрешён.

## 15. Соответствие профилю

- .NET desktop: Git I/O вне UI, изолирован от VM, lifecycle/cancellation; build/test обязательны.
- UI automation/local override: новые scenario tests и сохранение selectors, visual planning artifact, проверка доступности и обеих тем.
- Testing: TUnit, staged validation, full main/headless из-за межмодульного влияния, targeted native flow и evidence; отсутствие проверок не называется PASS реализации.
- QUEST: единственное изменение на SPEC — этот документ. ARM-PR правила неприменимы к реализации: Arm только read-only референс, его поведение не меняется.

## 16. Таблица изменений файлов

| Файл/область (план) | Изменение | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/ITaskHistoryProvider.cs`, history DTO/VM | Read contract и состояние панели | UI без Git dependency |
| `src/Unlimotion/Services/GitTaskHistoryProvider.cs`, `TaskHistoryDiffBuilder.cs` | Локальная история и diff | Реальные commits/blobs |
| `src/Unlimotion.FileStorage/FileTaskStorage.cs` | Узкий read-only resolver текущего task path, если mapping нельзя получить без полного scan | Совпадение пути с текущим storage |
| `src/Unlimotion/Views/TaskHistoryView.axaml` и code-behind | История и adaptive layout | Визуальные признаки Arm |
| `src/Unlimotion/Views/MainControl.axaml`, `MainControl.axaml.cs` | Интеграция/старые статусы/события | Карточка задачи |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion/App.axaml.cs` | Provider wiring и lifecycle | Защита смены source/task |
| `src/Unlimotion.ViewModel/Resources/Strings*.resx` | ru/en строки | Локализация |
| `src/Unlimotion.Test/*History*Tests.cs`, `MainControlTaskCardLayoutUiTests.cs` | Diff/Git/VM/UI/regression | AC1–AC8 |
| `tests/Unlimotion.AppAutomation.TestHost`, `tests/Unlimotion.UiTests.Authoring`, `tests/Unlimotion.UiTests.Headless`, `tests/Unlimotion.UiTests.FlaUI` | Один synthetic Git scenario и page/flow tests | Обычный пользовательский путь |
| `scripts/record-task-history-evidence.ps1` (при необходимости) | Wrapper записи automation flow | Доказательство AC7/9 |
| Текущая SPEC | План/фактические результаты и review | Traceability |

Точный набор новых вспомогательных файлов уточняется в рамках того же outcome; общие файлы принадлежат основному агенту. Остальной код и Arm не меняются.

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Карточка | Только статусы | Общий блок Git-изменений и доступные статусы |
| Before/after | Нет | Читаемые изменения всех полей |
| Git backup | Источник сохранённых версий | Тот же backup, появляется read-only просмотр |
| Без Git | Статусы | Статусы + честное объяснение недоступности Git |
| Формат данных | Текущий JSON | Без изменения |

## 18. Альтернативы и компромиссы

- Копировать Arm control целиком: максимальная похожесть, но привносит Eremex/серверные assumptions и мелкий шрифт. Выбрана адаптация композиции средствами существующей Avalonia.
- Список `git log`/сырой patch: проще, но плохо читается и не соответствует референсу. Выбран semantic diff с raw fallback для неизвестных данных.
- Новый persisted audit log/коммит на каждую правку: подробнее, но меняет storage/write/backup contract и не восстанавливает прошлое. Не входит в запрос просмотра существующего Git.
- Только first-parent history: проще и линейнее, но теряет содержательные коммиты слитых веток. Выбран DAG с явно подписанными merge относительно первого родителя.
- Смешать StatusHistory и commits в общую временную ленту: события имеют разную гранулярность и могут дублироваться. Выбраны два режима в одном разделе.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Блок/критерий | Статус | Evidence |
| --- | --- | --- | --- |
| 1 | A: Outcome | PASS | §1 и S1: история в реальной карточке |
| 2 | A: AS-IS | PASS | §2: текущие task/storage/backup/Arm/UI files прочитаны |
| 3 | A: Проблема | PASS | §3: недоступность Git-истории полей |
| 4 | A: Цели дизайна | PASS | §4: reference fidelity, read-only, UI responsiveness |
| 5 | A: Non-Goals | PASS | §5: restore, новый audit, другие sources/refs исключены |
| 6 | B: Ответственности | PASS | §6.1: provider/diff/VM/view/DI |
| 7 | B: Интеграция | PASS | §8: expand, selection, source switch, save, refresh |
| 8 | B: Алгоритмы | PASS | §7: identity/DAG/merge/rename/raw diff/paging |
| 9 | B: Ошибки/recovery | PASS | State matrix и S5/S8, retry/partial/empty |
| 10 | B: Performance | PASS | Batch, cancellation, preview/blob cache, fixture с 10 000 commits |
| 11 | C: Данные | PASS | §9: calculated DTO, persisted schema не меняется |
| 12 | C: Совместимость | PASS | §6.6, §7: raw legacy JSON, старые статусы |
| 13 | C: Rollback | PASS | §10: revert feature, данные не требуют миграции |
| 14 | D: AC | PASS | AC1–AC9 проверяют наблюдаемые результаты |
| 15 | D: Evidence mapping | PASS | S1–S8 → AC → positive/negative unit/integration/UI |
| 16 | D: Команды/stop | PASS | §11: SDK/MTP/CI wrapper проверены, fresh logs, no-evidence/no-pass |
| 17 | E: План/dependencies | PASS | §13: approved scope, characterization → feature → validation |
| 18 | E: Решения | PASS | Decision Ledger; не требуется отдельный выбор помимо approval |
| 19 | E: Форма/масштаб | PASS | §0: expanded, несколько модулей и новый read contract |
| 20 | F: Профиль | PASS | §15: desktop/UI automation/local override |

Итог: ГОТОВО. Все 20 критериев проверены; замечания отдельного reviewer устранены и прошли targeted re-review. Это gate SPEC, не готовность реализации.

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель/границы | 5 | Исходное поручение и недопустимое расширение явно сохранены |
| AS-IS | 5 | Прочитаны фактические controls/models/storage/backup и визуальный reference source |
| Конкретность дизайна | 5 | Wireframes, state matrix, identity/DAG/paging/raw diff, limits |
| Безопасность/rollback | 5 | Read-only snapshot, отсутствие network/writes, revert без миграции |
| Проверяемость | 5 | Полевая diff matrix, временные repo, delayed VM, UI/full-suite evidence |
| Автономность | 5 | Все implementation decisions определены; нужен только exact approval |

Итог: 30/30, готово к автономной реализации после approval. Оценка не подменяет review, exact approval или фактическую проверку реализации.

### Role-Based Review Result

| Role | Applicability | Проверка | Verdict self-review | Изменения |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Не теряются статусы; commits не называются каждым действием | PASS | Local row, отдельный режим статусов, подпись гранулярности |
| UX / designer | applicable | Признаки Arm и читаемость narrow/wide | PASS | Два wireframes, semantic colors, full reveal/copy, checked status labels |
| Tester / validation | applicable | Каждый AC имеет verifier и negative fixtures | PASS | Fresh results path, full main/headless, native flow/evidence |
| Developer / architect | applicable | Side-effect-free read, DAG/identity, async lifecycle, memory | PASS | Не использовать storage reads с lock side effects, preview DTO + bounded cache |
| Delivery / operations / security | applicable | Чужой source, path escape, Git writes/network, rollback | PASS | Source identity, path checks, unchanged HEAD/index/config evidence |

Отдельный reviewer `history_spec_review` работал только чтением, ownership SPEC сохранился у основного агента. Фактический child sandbox — `danger-full-access`, approval `never`: технически read-only independent review недоступен из-за effective runtime. Выполнен отдельный adversarial reviewer fallback, а не заявленный sandbox-enforced independent review. Его initial pass обнаружил два MEDIUM (legacy/memory); после исправлений targeted re-review вернул PASS. Дополнительных существенных нарушений source isolation, DAG/merge/rename, paging и UI-контракта не выявлено. Остаточное ограничение: независимость не обеспечена запретом записи на уровне sandbox; фактических записей reviewer не делал.

### Post-SPEC Review

Статус: PASS, можно запрашивать exact approval SPEC. Реализация не начата.

- Scope reviewed: текущая SPEC, весь instruction stack §0, desktop/UI профили, AS-IS §2, open questions §14 и planned files §16.
- Scope/Evidence pass: inspected `MainControl.axaml`, `TaskItem`, `TaskCompletionCriterion`, `RepeaterPattern`, `AgentExecutionRecord`, `TaskItemViewModelContext`, `BackupViaGitService.Push`/active path resolution, `FileTaskStorage` enumeration/JSON/mapping, `TaskSourceManager`, Arm StatusBar/FieldChange/RevisionJsonDiffCalculator, `MainControlTaskCardLayoutUiTests`, Headless ReadmeDemo, CI workflow/wrapper, recording wrappers, `global.json`, packages и SDK version.
- Contract pass: сравнение исходного запроса с S1–S8/AC1–AC9, сохранены Arm-композиция, полная доступная Git-история и прежние статусы; не добавлены Git mutation/новый audit/публикация.
- Adversarial risk pass: проверены контрпримеры filename≠Id, одинаковый Id в двух пространствах, частично сохранённый JSON, unborn/shallow HEAD, merge/rename, >1000 unrelated commits между task revisions, defaults старых моделей, late response, большие значения и повторный CI run.
- Role-Based pass: таблица выше; отдельный adversarial reviewer проверил AS-IS и контракт, два MEDIUM закрыты targeted re-review.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Read-only contract | Существующий ReadDirectoryAsync может мутировать cache и lock file | Запретить этот путь в history; side-effect-free resolver | fixed, §7.2 |
| MEDIUM | Performance | Ограничение blob cache не ограничивает большие значения, удержанные всеми DTO | Preview DTO + lazy full value, session disposal | fixed, §6.1/7.12 |
| MEDIUM | Legacy/domain, reviewer | Legacy-поля могли скрыть реальные завершения/возвраты/архивации | Показывать legacy status/даты по умолчанию; скрывать только доказанную эквивалентную миграцию; diff/UI coverage | fixed, §7.8–9/11 |
| MEDIUM | Performance, reviewer | Cache budget не исключал удержание больших раскрытых значений всеми строками | Запрет ссылок DTO на full values/JToken, одна область деталей, bounded detail cache, fixture с несколькими страницами больших описаний | fixed, §7.12/11 |
| MEDIUM | Evidence | Повторный запуск CI wrapper с тем же ResultsRoot отклоняется | Новый timestamped ResultsRoot для каждой попытки | fixed, §11 |
| LOW | UX/copy | Wireframe использовал несуществующие подписи статуса | Выровнять по Strings.ru.resx | fixed, §6.2 |

- Fix and re-review: после правок повторно сопоставлены read path с `FileTaskStorage`, evidence commands с guard `invocation-test.json` в wrapper, примеры статусов с ресурсами, memory constraints с DTO lifetime. Unborn HEAD и лимиты чтения сделаны явными. Reviewer подтвердил исправления legacy visibility/эквивалентной миграции и ограничений full-value retention/тестового плана. Его дополнительное evidence: `UnifiedTaskStorage.cs` legacy migration / `ReadLegacyStatus`, migration tests, Arm StatusBar. Структура SPEC, парность fenced blocks, отсутствие trailing whitespace и единственность изменённого файла проверены отдельно.
- Depth checklist: scope drift — только SPEC; AC/scenarios/decisions/objections связаны; validation заявлена планом, не выполненными тестами; unsupported live-Arm/performance claims отсутствуют; edge cases рассмотрены; docs/changelog вне SPEC не менялись; новый UI/read contract явный; manual-review challenge — доказать не только список commits, но и полноту branch/rename history, отсутствие чужих данных и доступность полного значения.
- No-findings justification: initial findings перечислены и исправлены. В targeted re-review новых находок нет: legacy semantic changes включены в основной показ и AC2, отсутствие retained full values закреплено в DTO/detail contract и многопагинном performance fixture.
- Residual risks: фактическое визуальное сходство/скорость/совместимость native Git требуют EXEC evidence; legacy/large/corrupt данные могут давать честную partial history. Эти риски не считаются закрытыми тестами на SPEC.
- Needs human: только exact approval «Спеку подтверждаю»; дополнительных блокирующих продуктовых решений нет.
- Stop decision: PASS на фазе SPEC; остановиться до approval, код/тесты/инфраструктуру не менять.

### Согласованные доработки после UX/code review (26.09.2026)

Пользователь согласовал все предложения review. В рамках прежнего outcome уточняем EXEC: история должна быть читаемой на 360/480/900 DIP в светлой и тёмной теме; старые/новые значения оформляются semantic brushes и перестраиваются в вертикальный вид на узкой панели. Выбор полного значения показывает его рядом с изменением или переводит фокус к видимой области. Строка рабочей копии называется «Изменения без коммита» и поясняет, что это сохранённый файл относительно HEAD. Toolbar объединяет режимы, а обновление остается доступным без лишнего визуального веса.

Обновление после локальной записи или внешнего изменения запускается после завершённого сохранения, не на каждом PropertyChanged; при refresh уже показанные строки остаются до нового результата, режим «Статусы» не запускает Git traversal. Для деталей рабочей копии проверяется идентичность снимка перед показом полного значения, иначе предлагается обновить историю. Длинные списки используют виртуализацию; переход к следующей странице продолжает сохранённый обход без повторного пропуска уже прочитанных commits, ресурсы обхода освобождаются при смене задачи/сворачивании/refresh. Проверить эти контракты целевыми unit, Headless и FlaUI тестами, включая узкую ширину, тёмную тему, длинные значения, сохранение и пагинацию. Визуальное evidence должно быть достоверным; неудачный захват таковым не считать.
### Post-EXEC Review

Статус: реализация завершена, targeted gate PASS. Полный AC9 остаётся PARTIAL из-за независимой ошибки конфигурации полного Headless stage и отсутствия пригодного визуального кадра/видео.

- Реализованы read-only Git provider, raw JSON diff, отдельная строка рабочей копии, frozen-HEAD cursor и paging 50/1000, lazy details, metadata filter, partial/unavailable states и source/task generation gate.
- В карточке появился сворачиваемый раздел с режимами «Изменения Git» и «Статусы»; старый StatusHistory сохранён. Компоновка ограничена по высоте, адаптирует controls на узкой ширине, позволяет выделять старые/новые значения и показывает полные SHA/message в tooltip.
- Review-loop исправил обход всего DAG в память, гонки detail/page responses, object/null/scalar diff, порядок критериев, legacy raw dates/status, lifecycle карточки, late errors, обновление после save, переход пути через rename с нечитаемой промежуточной ревизией, corrupt HEAD с исправленной рабочей копией и безопасную отмену delayed details.
- `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release --no-restore`: PASS.
- `GitTaskHistoryProviderTests`: 13/13 PASS, включая paging, corrupt data, corrupt HEAD + repaired working tree, rename → corrupt → repair и отмену delayed details.
- `CurrentTaskCard_TaskHistory_ExposesGitChangesAndStatusModes`: 1/1 PASS (Avalonia.Headless).
- `Task_history_expands_and_shows_git_commit_changes`: 1/1 PASS (FlaUI desktop, последовательный запуск).
- Полный main suite через CI wrapper: 1121/1121 PASS до последних reviewer-исправлений; после них повторены затронутые provider/ViewModel, Headless, FlaUI и desktop build. Evidence: `artifacts/task-git-history/tests/20260925-123411/main/`.
- Полный Headless CI wrapper не дошёл до тестов: существующая сборочная конфигурация `Unlimotion.Desktop/Program.cs` не видит `AppBuilder.WithDeveloperTools` (`CS1061`). Targeted Headless test проходит в Release.
- Попытка снять визуальный кадр не дала достоверного состояния истории: кадр показывал список задач, хотя UI Automation подтверждал раскрытую историю. Поэтому screenshot/video не предъявляются как evidence.
- Не измерялась производительность на отдельном fixture в 10 000 commits; changed-path traversal и лимит 1000 commits/page ограничивают работу, но численное performance evidence отсутствует.
- Финальный узкий adversarial re-review: BLOCKER/HIGH не найдено; reviewer не изменял файлы. Технически read-only sandbox reviewer недоступен, поэтому это process-level fallback.

### Проверка согласованных доработок (26.09.2026)

- Готово: адаптивные строки до/после, семантические цвета для двух тем, единый блок режимов, компактное расположение подробностей над списком, читаемое имя рабочей копии и ограничение длинного текста по ширине.
- Готово: обновление после завершённого SaveItemCommand и внешних raw-событий; в режиме статусов и скрытой карточке обход Git не запускается. Старые строки остаются видны до результата обновления.
- Готово: хеш снимка рабочего файла проверяется перед открытием полного значения; состояние обхода Git и репозиторий сохраняются между страницами и освобождаются при сбросе; длинный список виртуализован.
- Validation PASS: `GitTaskHistoryProviderTests` 16/16; два целевых Avalonia.Headless теста 2/2, включая 360/480/900 DIP в светлой/тёмной темах; `TaskHistoryFlaUiTests` 1/1, включая открытие полного 460-символьного значения; Desktop Release rebuild PASS.
- Полный `Unlimotion.Test` дважды не дал полного зелёного результата: при общем параллельном прогоне один watcher-тест завершился по таймауту, а при ограничении 4 потоками другой Headless-тест упал при `DisposeAsync`; оба проходят изолированно 1/1. Оба общих прогона были остановлены после длительного отсутствия дальнейшего вывода. Не считать полный suite пройденным.
- Визуальные кадры, снятые из реального desktop окна теста и просмотренные: `artifacts/ui-evidence/task-history/history-list.png`, `full-value.png`. Они локальные, исключены `.gitignore`; снимки показывают реальную компоновку, но не заменяют проверку на всех размерах и темах.

### Коррекция плотности после пользовательского ревью (27.09.2026)

Пользователь сообщил, что история стала хуже: слишком много пустого места и отступов, визуальная логика нарушена. Подтверждённый дефект: `OpenPaneLength` ограничен 600 DIP, а `IsWide` требовал 650 DIP, поэтому заявленная широкая строка фактически не показывалась. В обычном экране каждое поле занимало пять и более строк; отдельный блок полного значения появлялся над списком и терял связь с выбранным изменением. Скриншот `artifacts/ui-evidence/task-history/history-list.png` и текущая разметка — воспроизведение до правки. Это редакционная коррекция утверждённого UX-outcome, а не новая функция.

Вместо двух веток шаблона оставить одну плотную строку `поле: старое → новое` со знаками изменения и переносом длинных предпросмотров. Группировка по коммитам/автору/времени сохраняется. Полное значение открыть в привязанной к выбранному полю панели/flyout без перемещения списка. Скрыть по умолчанию семантически пустой переход `отсутствует ↔ null` для необязательных дат завершения/архивации, сохранив его при включении служебных полей. Убрать постоянное пояснение и дублирование текста в строке рабочей копии; пояснения оставить в tooltip. Уплотнить toolbar и разделители. Общую ширину карточки не менять без доказательства, что она является причиной пустоты: справа максимум 600 DIP, слева пространство существующего списка задач.

Acceptance: на 360/480/900 DIP обеих тем реальные Git-строки с несколькими полями доступны без горизонтальной прокрутки, короткая правка занимает одну строку или разумный перенос, полное значение открывается рядом с тем же полем и не сдвигает весь список, режим статусов и `@` работают; переход missing/null виден только с `@`. Проверить provider regression, Headless с Git-строками, FlaUI click/full text, Release build и реальный кадр до/после. Для scoped UI правки затронутые тесты и визуальная проверка обязательны; полный suite не повторять без новой области влияния (предыдущие два общих прогона не завершились, оба сторонних сбоя прошли изолированно).

### Согласованная UX/UI коррекция (02.10.2026)

Основание: пользователь принял визуальное ревью кадров `ux-review-20261002` словами «Отлично, сделай это всё». Это продолжение утверждённого outcome; Git/storage/schema остаются прежними. Правка заменяет неустойчивый WrapPanel и overlay деталей из предыдущего addendum.

Целевая компоновка:

```text
▾ История изменений
[ Изменения | Статусы ]                         ↻  ⋯
Уточнить результат и критерии отчёта
Пользователь · 02.10.2026 10:20          f31d9b2 (копировать)
Статус: Не готова → В работе
Описание                              Полный текст
  широкая панель: Было       | Стало
                   старое   | новое (до 2 строк)
  узкая панель:  Было  старое (до 2 строк)
                 Стало новое (до 2 строк)
  раскрыто: те же подписи и полные значения · Свернуть
```

- Короткая пара остаётся одной строкой, если помещается; длинная пара сохраняет выравнивание. Preview ограничен двумя отрисованными строками; modified не получает дублирующую стрелку перед полем. Цвет дополняет подписи.
- Компактные режимы «Изменения / Статусы», refresh с tooltip, «Служебные поля» в меню дополнительных действий. Заголовок с chevron без синей заливки всей секции; единственный цветовой акцент — выбранный режим.
- Сообщение коммита — основной semibold текст, автор/дата вторичны; тихий SHA имеет явное действие копирования полного хеша.
- Полный текст раскрывается внутри выбранного поля. Одновременно раскрыто одно поле, закрытие явно доступно; выбор другой задачи, режима, refresh, сворачивание секции и переиспользование строки удаляют полные значения.
- Связи над историей: компактная строка «название + количество + добавить», пустое дерево не занимает высоту. Заполненные деревья и редакторы связей сохраняют прежние действия и AutomationId.

Проверки: обновить Headless regression на 360/480/900 DIP и обе темы (нет горизонтального overflow, две строки preview, стабильные пары); inline full text/одна открытая строка/сворачивание/смена режима; metadata menu; существующие flow связей. Обновить и запустить FlaUI сценарий полного значения, Desktop Release build и provider tests. Скриншоты с теми же русскими данными, ширинами и темами сохранить отдельно и открыть для проверки. Область ограничена карточкой: полный suite не обязателен по scoped UI gate. Desktop capture в закрытой сессии ранее чёрный; next-best evidence — пиксельные Skia Headless PNG + UI assertions, видео в этом harness недоступно.

Post-SPEC pass: просмотрены baseline PNG, шаблон MainControl, lazy detail/cancellation контракт и UI tests. Контрпример узкой панели закрыт stacked before/after, длинного поля — rendered line clamp и inline expansion; прежний недостижимый wide threshold не возвращается (440 DIP при pane max600). Остаток: визуальная приёмка новых кадров и проверки до завершения EXEC. Scope не включает публикацию.

### Post-EXEC review: UX/UI коррекция 02.10.2026

- **Scope/Evidence:** просмотрены diff/status, `TaskHistoryFieldChangeView`, MainControl template/lifecycle, detail cancellation в модели, ресурсы двух языков, обновлённые Headless/FlaUI tests. Git provider и storage формат не изменены. Остальной dirty diff — исходная согласованная реализация этой же истории; временный capture test вынесен обратно в ignored artifacts.
- **Contract:** все пять согласованных UX пунктов выполнены, пустые связи имеют count и доступное добавление. Подтверждены обычные пользовательские сценарии: короткая правка одной строкой; длинная пара с подписями и двумя строками preview; inline full text, одно открытое поле, сворачивание, смена режима; metadata через меню; копирование полного SHA. Полные значения очищаются при закрытии и отмене pending read.
- **Adversarial:** повторное использование строки/late response закрыты owner guard и отменой; wide branch проверен при фактической ширине строки ≥440, ниже — stacked. Нет горизонтального выхода значений, duplicated modified arrow или flyout, перекрывающего задачу. Проверка клавиатуры выявила потерю фокуса на новом контроле до layout: focus теперь переносится после layout только при открытии с фокусом на кнопке, закрытие возвращает его к действию. Полный suite не объявляется пройденным: изменена локальная UI поверхность и отмена деталей, storage/provider traversal не затронуты.
- **Роли:** UX — открыты и сопоставлены baseline/final PNG на тех же русских данных, 360/900 DIP и двух темах; developer — view хранит только одну раскрытую пару, DTO не удерживает full text, обработчики освобождают её; tester — relevant UI/provider tests, build и визуальные состояния проверены; domain/operations — Git остаётся read-only, новых schema/publication действий нет.
- **Fix and re-review:** preview regression сначала RED (`MaxLines: expected 2, found 0`); исправлен responsive renderer. По первому новому кадру исправлен белый заголовок на светлом фоне и смягчены разделители. По keyboard regression исправлен deferred focus; после этого три history Headless tests и FlaUI повторно PASS. Оставшиеся 23 проверки layout не затронуты focus fix.
- **Validation:** `MainControlTaskCardLayoutUiTests` 26/26 PASS (`ux-layout-20261002.log`); финальный history scope 3/3 PASS (`ux-headless-final-20261002.log`); `MainControlRelationPickerUiTests` 5/5 PASS, включая открытие четырёх редакторов добавления и сохранение родительской связи; `GitTaskHistoryProviderTests` 18/18 PASS, включая закрытие pending detail read; `TaskHistoryFlaUiTests` 1/1 PASS в финальном прогоне; Desktop Release build PASS, `git diff --check` PASS. Логи: `artifacts/ui-evidence/task-history/ux-*-20261002.log`.
- **Visual evidence:** открыты все восемь `ux-final-20261002/history-{Light|Dark}-{360|900}.png` и `details-{Light|Dark}-{360|900}.png`, сопоставлены с `ux-review-20261002`. Это пиксели области Avalonia Window, не системной рамки. Capture flow `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/TaskHistoryUxReviewCaptureTests/*"` с `UNLIMOTION_UX_REVIEW_OUTPUT`; harness сохранён рядом с final PNG. Последующий focus fix не меняет layout/цвета этих состояний и проверен отдельно с активным окном. Desktop PNG/video в закрытой сессии не используются как визуальное доказательство; fallback — просмотренные Skia PNG + зелёный FlaUI сценарий.
- **Expected objections / depth:** неустойчивая пара и лишние отступы закрыты; режимы и служебные поля доступны; header читается в Light/Dark; long text не перекрывает соседние области; четыре relationship действия сохранены. Нет unrelated scope, неподтверждённых perf/delivery claims, открытого обязательного AC или требуемого решения человека. Changelog/release не входят в запрос.
- **Stop decision: PASS** для этой UX/UI коррекции. Независимый agent не привлекался: medium scoped UI addendum, Git/provider/storage контракт прежний. Остаток — прежние ограничения полной suite/video evidence из основного feature delivery; они не подменяются новым локальным PASS.

### Полировка значка и рамки (по следующему запросу 02.10.2026)

Пользователь принял результат и запросил commit, затем две точечные правки. Принятый вариант сохранён как `cd78e3d4`. Это short-sized редакционное продолжение EXEC в той же UI поверхности; новый feature/storage контракт не вводится.

Цель: `[chevron 14×14 по центру] История изменений`, без прямоугольной рамки вокруг toolbar/list. Chevron — vector, одинаковый размер в двух состояниях, регулярный зазор 8 DIP; заголовок и icon по вертикали выровнены. Тонкие разделители между коммитами сохраняются, данные/действия не меняются.

Проверки: regression на actual rendered content border и выравнивание/состояния icon в Light/Dark на 360/900 DIP; существующие три history UI tests, Desktop build, новый Skia кадр и просмотр. Provider/relationship behavior прежние, повторный полный suite не нужен. Video fallback прежний: закрытая desktop-сессия и Headless capture harness без recorder; next-best evidence — PNG + targeted UI tests. Пост-SPEC pass: границы/решение/AC→evidence/риск/rollback заданы; основной риск — theme trigger поверх template setter, проверяется actual border. Push/PR не входят в поручение.

Пост-EXEC pass: meaningful RED обнаружил фактическую рамку `1,0,1,1`, которую Fluent theme накладывала на `#ExpanderContent`. Локальное имя `TaskHistoryContent` устранило коллизию; векторный chevron 14×14 выровнен с заголовком и имеет зазор 8 DIP в обоих состояниях. Новый regression проверяет фактическую компоновку в Light/Dark на 360/900 DIP. В existing inline-details test дата начала подготовлена в fixture storage, чтобы autosave не удалял синтетические строки истории; deferred focus ожидается явно. Проверка повёрнутого icon учитывает RenderTransform и допуск вычисления координат.

Validation: четыре `CurrentTaskCard_TaskHistory_*` UI tests PASS (`chrome-headless-20261002.log`), Desktop Release build PASS (`chrome-build-20261002.log`), capture scenario 1/1 PASS (`chrome-capture-20261002.log`). Снимки в `chrome-after-20261002`: просмотрены четыре `history-{Light|Dark}-{360|900}.png` и `details-Light-900.png`; значок и заголовок выровнены, рамка отсутствует, детали сохраняют структуру. Capture выполнен последовательно через `--no-build`, harness сохранён рядом с PNG вне source. Изменены только template, UI tests и эта SPEC; provider/storage/relationship поведение прежнее. Scope/self-review/visual pass: PASS; обязательных незакрытых требований нет. Rollback — отдельный fix commit поверх принятой реализации `cd78e3d4`.

## Approval

Получено: «Спеку подтверждаю».

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC: исследование | Источник Git, визуальная композиция Arm, отдельный режим статусов | Прочитаны MainControl, TaskItem, storage/backup, Arm StatusBar/FieldChange, UI tests; код не менялся | Подготовить конкретный контракт | Исходный запрос реализации | Эта SPEC |
| SPEC: проектирование | Raw diff, source isolation, DAG paging, local row, wireframes | Контракты/AC/test plan заданы; review ещё не пройден | Reviewer + self-review, исправить найденное | Exact approval отсутствует | Эта SPEC |
| SPEC: review/rework | Уточнены legacy transitions, side-effect-free path, ограничение full-value retention и свежие test-result paths | Self-review + отдельный adversarial fallback; 2 reviewer MEDIUM закрыты targeted re-review PASS; только SPEC изменена | Запросить exact approval | «Спеку подтверждаю» ещё не получено | Эта SPEC |
| EXEC: переход | Утверждённый outcome и границы переходят в реализацию | Пользователь прислал точную фразу «Спеку подтверждаю»; публикация/push/PR по-прежнему не разрешены | Создать рабочую ветку, реализовать и проверить AC | «Спеку подтверждаю» | Эта SPEC и будущие in-scope файлы |
| EXEC: реализация | Добавлена история Git с raw diff, paging, working-tree row, metadata/details и сохранённым режимом статусов | Изменены provider/models/MainControl/resources; schema и Git state не мутируются | Выполнить targeted/full validation и review | «Спеку подтверждаю» | In-scope source/test files |
| EXEC: validation/review | Targeted provider/headless/FlaUI и desktop build прошли; main suite 1121/1121 | Full Headless stage блокирован `WithDeveloperTools` CS1061; достоверного screenshot/video нет; performance 10k не измерен | Зафиксировать ограничения без объявления полного AC9 | Публикация не запрошена | Test reports и эта SPEC |
| EXEC: коррекция плотности 27.09 | Устранён недостижимый «широкий» режим при панели максимум 600 DIP; единая компактная строка diff, детали во flyout выбранного поля, пустые даты скрыты до включения `@` | Release desktop build, 17 provider tests, 2 Headless UI tests на 360/480/900 DIP в Light/Dark и 1 FlaUI test прошли; в закрытой desktop-сессии FlaUI screenshots получаются полностью чёрными, семантический Headless renderer вернул `null` | Настроить пиксельный Skia Headless для визуального кадра | «Исправь» после UX review | Source/tests, test reports; baseline `artifacts/ui-evidence/task-history/before-history-list.png` |
| EXEC: снимки AppAutomation 28.09 | Пиксельный Skia Headless builder, предпросмотр длинных значений 60 символов по результату просмотра первого кадра | Пройдены Headless layout test на 360/480/900 DIP в обеих темах, 17 provider tests и FlaUI test. Открыты и проверены PNG на 480/900 DIP и flyout с демонстрационными данными; это кадры Avalonia Window без системной рамки. FlaUI screenshot остаётся чёрным в закрытой desktop-сессии | Приложить просмотренные файлы пользователю | «Используя скилл appautomation сделай скриншоты» | `artifacts/ui-evidence/task-history/after-headless-history-480.png`, `after-headless-history-900.png`, `after-headless-full-value.png` |
| EXEC: UX/UI коррекция 02.10 | Устойчивая пара before/after, compact toolbar, commit hierarchy, inline details, спокойный header, плотные связи/count | 26 layout + 5 relation UI tests, финальные 3 history UI tests, 18 provider tests, FlaUI 1/1, Desktop build и diff-check PASS; 8 final Skia PNG открыты и сопоставлены с baseline | Передать результат и кадры | «Отлично, сделай это всё» | `ux-final-20261002`, `ux-*-20261002.log`, source/tests |
