# Иерархическое отображение эмодзи в фильтрах

## 0. Метаданные

- Статус: SPEC, ожидает согласования.
- Тип: delivery-task; профили `dotnet-desktop-client`, `ui-automation-testing`; context `testing-dotnet`.
- Владелец: пользователь; подготовка и реализация — Codex.
- Масштаб: medium. Expanded SPEC: есть взаимодействие графа задач, реактивных фильтров и UI.
- Canonical template: `C:/Users/Kibnet/.codex/agents/templates/specs/_template.md`.
- Instruction stack: central AGENTS → routing-matrix; creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance, quest-mode, testing-baseline; указанные context/profiles; spec-linter, spec-rubric, review-loops; локальный AGENTS.override.md.
- Использован creator-vibe: отделить раскрытие ветки от выбора фильтра, сохранить быстрый поиск и понятные подписи.
- Behavior baseline: GPT-6 Astra; поверхность Codex, текущая сессия GPT-6; точные tier/effort не проверялись и не влияют на контракт продукта.
- Model eval: не применимо — изменение Avalonia UI.
- Проверенная база: `main`, HEAD `24d63f4f`, исходный worktree чистый; локальный `origin/main` впереди на 9 коммитов. Remote не обновлялся. Перед EXEC повторить preflight; актуальные изменения TaskItemViewModel учесть при интеграции.
- Задача: `ab683f5b-aef5-4a60-8eb7-9114c62072ea`, «В фильтрах эмодзи сделать иерархическое отображение эмодзи»; Description отсутствует.
- Выпуск, commit/push/PR и установка не входят в текущий запрос.

## 1. Цель

Пользователь находит эмодзи через знакомую вложенность своих задач и выбирает её в фильтре включения или исключения.

Success means: дерево отражает containment-связи, поиск находит скрытые пункты, выбор сохраняет текущий смысл фильтрации, обновления графа отражаются без повторного запуска приложения.

Output: изменённый общий контрол, вычисляемая модель иерархии, regression/UI tests, просмотренные визуальные доказательства. На SPEC output — этот документ со схемой интерфейса.

Stop rules: до «Спеку подтверждаю» изменяется только эта SPEC; после реализации выполнить обязательные проверки и review, не повторять успешные прогоны без новой причины. Непройденную проверку явно считать ограничением результата.

## 2. Текущее состояние (AS-IS)

- `MainWindowViewModel.cs`, регион Emoji: задачи группируются по `TaskItemViewModel.Emoji`. Один `EmojiFilter` на уникальную строку эмодзи; подпись/Source берутся из первого элемента группы. Отдельные коллекции для include/exclude.
- `EmojiTextHelper.ExtractEmoji` объединяет все найденные эмодзи заголовка в строку. Например, `🏠🔧` сейчас является одним ключом; разбиение ключа на отдельные символы изменило бы контракт.
- Include допускает задачу при совпадении хотя бы одного выбранного ключа; exclude исключает при любом совпадении. Проверяются `GetAllEmoji` и исходный Title. Наследование родителей уже участвует в фильтрации.
- `EmojiFilterMultiSelectSearchBox.axaml(.cs)` отображает плоский ListBox, поддерживает All, summary, поиск, предупреждение при отсутствии совпадений, переключение Space, вход в поиск Enter/F2, Escape и открытие только одного popup.
- Общий контрол используется в семи местах `MainControl.axaml`, в том числе в Roadmap.
- Иерархию предоставляют `Parents`/`ParentsTasks`; `GetAllParents()` теряет структуру путей, поэтому его плоский результат недостаточен для построения дерева.
- `MainControlFilterToolbarResponsiveUiTests`, `EmojiFilterUiContract`, `StormEmojiFilterExecutableSpecTests` проверяют текущие пользовательские сценарии. Часть assertions привязана к плоскому списку и требует осмысленного обновления.
- Для видео уже есть FlaUI и пример сценарной записи `scripts/record-status-contract-evidence.ps1`; готового сценария иерархии эмодзи пока нет.

## 3. Проблема

Плоский список скрывает контекст эмодзи в графе задач: пользователь вынужден искать знакомые категории без их родительских связей.

## 4. Цели дизайна

- Вычисление дерева независимо от Avalonia; выбор фильтров остаётся единым по ключу Emoji.
- Отдельные состояния «раскрыт» и «выбран».
- Один общий механизм для include/exclude и всех панелей.
- Поиск доступен независимо от раскрытия; данные задач и формат настроек совместимы.

## 5. Non-Goals

Редактирование графа из фильтра; новая пользовательская таксономия эмодзи; фильтрация по конкретной задаче/пути; каскадная установка галочек у потомков; изменение OR/exclude-семантики, Unicode-парсера, статусов задач, хранилища, синхронизации и CLI. Сохранение раскрытия между запусками приложения не добавляется.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Ответственности

- Новый `EmojiFilterHierarchy.cs` в ViewModel: снимок задач с ID, Title, Emoji и ParentIds, индекс ближайших родителей с эмодзи, упорядоченные корни/дети, безопасный обход.
- Новая модель строки `EmojiFilterTreeItem`: конкретное вхождение задачи в дереве, стабильный путь ID, глубина, собственная подпись, ссылка на существующий `EmojiFilter`, признак детей/повторного циклического входа. Это presentation state.
- `MainWindowViewModel`: жизненный цикл снимка и реакция на обновления репозитория. Коллекции плоских фильтров остаются источником состояния выбора для остальных потребителей, включая Graph.
- Общий контрол: раскрытие, плоская проекция видимых строк для виртуализированного ListBox, поиск, клавиатура, popup, summary.
- `MainControl.axaml`: передать один снимок иерархии во все include/exclude контролы.

### 6.2 Детальный дизайн и visual planning artifact

Ниже схема целевого состояния, а не скриншот работающего приложения. Синтетические примеры не связаны с личными задачами.

```text
Фильтр включения [ 🏠 🔧 ]        Фильтр исключения [ − 🙂 ]

┌──────────────────────────────────┐
│    ☐ Все                         │
│ ▾  ☐ 🏠 Дом                      │
│      ☐ 🔧 Ремонт                 │
│   ▸  ☐ 🌱 Сад                    │
│ ▸  ☐ 💼 Работа                   │
│    ☐ 📚 Книги                    │
└──────────────────────────────────┘

Поиск «ремонт»:
┌──────────────────────────────────┐
│    ☐ 🔧 Ремонт                   │
│         🏠 Дом                   │
└──────────────────────────────────┘
```

- Строка: отдельная стрелка раскрытия, checkbox, эмодзи, название конкретной задачи без эмодзи. У листа сохраняется место стрелки. Длинное название обрезается с полным текстом в tooltip; высота следует размеру шрифта.
- Строки соответствуют задачам с непустым Emoji. Задачи без эмодзи пропускаются, их потомки поднимаются к ближайшим предкам с эмодзи. Корень — задача, у которой нет такого предка.
- Несколько родителей дают несколько вхождений одной задачи. Разные задачи с одинаковым Emoji имеют собственные подписи и положения; их checkbox ссылается на общий ключ фильтра. Tooltip объясняет, что выбор применяется ко всем задачам с этой эмодзи, включая унаследовавшие её.
- Галочка меняет только текущий ключ Emoji. Выбор 🏠 уже охватывает наследующие его задачи через текущий predicate, но не устанавливает отдельные галочки 🔧/🌱. Клик по стрелке/Left/Right меняет только раскрытие.
- «Все» находится первым при пустом запросе и переключает все уникальные ключи соответствующего фильтра, включая скрытые. Это специальный элемент, он не зависит от наличия задачи без эмодзи. Его индикатор вычисляется: выбран, только когда выбраны все непустые ключи; иначе снят. Клик по снятому выбирает все, по выбранному снимает все. Синхронизация индикатора не должна вызывать массовое изменение ключей через существующую подписку AllEmojiFilter: отделить пользовательское bulk-действие от вычисляемого индикатора и защитить от reentrancy. Частичный выбор остаётся обычным двухпозиционным состоянием.
- На первом открытии корневые ветки раскрыты на один уровень, вложенные свёрнуты. Состояние путей хранится в памяти конкретного контрола; закрытие, поиск и обычное обновление его сохраняют. При смене источника сбрасывается.
- При непустом запросе показан плоский список совпавших задач с эмодзи, каждая один раз по ID. Поиск использует эмодзи и все собственные текстовые названия, а не только первый Source группы. Под результатом указан контекст ближайшего родителя; при нескольких родителях перечислить их названия, обрезая строку и показывая полный список в tooltip. Стрелок в результатах поиска нет.
- Очистка запроса возвращает дерево с прежним раскрытием. При отсутствии совпадений — существующее предупреждение и дерево с прежним раскрытием; выбранные ключи не меняются.
- Summary строится из уникальных выбранных ключей, поэтому повторения в дереве не увеличивают счётчик. Include/exclude независимы.
- Сохранить размеры/позиционирование popup, темы, существующие automation ID, правила focus/light-dismiss. Глубина отступа ограничивается доступной шириной: checkbox и эмодзи всегда видны; полная цепочка доступна в tooltip. Не добавлять горизонтальное прокручивание для выбора.
- Up/Down перемещают фокус между видимыми строками, Space меняет галочку. В дереве Right раскрывает или переходит к первому ребёнку, Left сворачивает или переходит к родителю. В плоских результатах поиска Left/Right ничего не раскрывают и не меняют выбранную строку; в текстовом поле сохраняют обычное движение каретки. Enter/F2 и Escape сохраняют текущий контракт поиска. При сворачивании родителя фокус скрытого потомка переходит на родителя.

### 6.3 User-Observable Scenarios

| Сценарий | Действие | Видимый результат | Evidence | AC |
|---|---|---|---|---|
| Просмотр | Открыть любой emoji popup | Дерево, названия, стрелки и All | UI test + screenshot | 1, 6 |
| Выбор | Выбрать родителя/повторяющуюся эмодзи | Корректный список задач и единое состояние копий | UI/integration | 2 |
| Поиск | Найти скрытого потомка и очистить запрос | Совпадение с контекстом, затем прежнее дерево | UI + видео | 3 |
| Обновление | Перенести/переименовать задачу при открытом popup | Новая структура/подпись без потери существующего выбора | UI/integration | 4 |
| Клавиатура | Раскрыть, выбрать, свернуть, закрыть | Предсказуемый focus, popup остаётся при выборе | UI + видео | 5 |

### 6.4 State / Interaction Matrix

| Состояние | Триггер | Результат | Граничный случай |
|---|---|---|---|
| Дерево | Раскрыть ветку | Добавлены непосредственные дети | Циклический повтор — терминальная строка с пояснением |
| Любое | Checkbox/Space | Изменён общий ключ, копии синхронны | Include не меняет exclude |
| Дерево | Ввод запроса | Поиск по всем emoji-задачам | Нет совпадений — предупреждение + дерево |
| Поиск | Очистка/Escape | Восстановление дерева | Следующий Escape закрывает popup |
| Открытый popup | Обновление graph snapshot | Согласованная замена проекции | Удалённый фокус → ближайший видимый предок, иначе первая строка |
| Источник сменился | Новая taskRepository | Новая иерархия, очищено локальное раскрытие | Отписки старого источника обязательны |
| Нет эмодзи | Открытие | All disabled, пустое состояние «Нет эмодзи» | Новая emoji-задача появляется реактивно |

### 6.5 Decision Ledger

| Решение | Owner | Выбранный default | Confidence | Риск | Needs user before EXEC |
|---|---|---|---:|---|---|
| Основа дерева | agent | Containment задач, пропуск узлов без эмодзи | 0.95 | Нужно сверить пример с ожиданием пользователя при approval | Нет |
| Значение галочки | agent | Существующий глобальный ключ Emoji, без каскада | 0.97 | Повторения требуют пояснения | Нет |
| Несколько родителей | agent | Вхождение под каждым ближайшим emoji-родителем | 0.92 | Число раскрытых путей может расти | Нет |
| Поиск | agent | Плоские совпадения с контекстом | 0.90 | Во время поиска дерево заменяется результатами | Нет |
| Подписи | agent | Название каждой задачи, а не first Source группы | 0.94 | Больше строк при повторяющихся эмодзи | Нет |
| Раскрытие | agent | Один уровень при первом открытии, память контрола | 0.90 | Между вкладками раскрытие различается | Нет |

Отдельных обязательных продуктовых вопросов нет; defaults представлены для согласования всей SPEC.

### 6.6 Runtime / Config / Data Contract

| Область | Source of truth | Изменение | Совместимость | Проверка |
|---|---|---|---|---|
| Граф | Текущая taskRepository, ID и Parents | Только вычисляемая проекция | JSON/Settings без изменений | Snapshot/lifecycle tests |
| Выбор | EmojiFilter.ShowTasks по строке Emoji | Общие ссылки у вхождений | Predicates/Graph сохраняются | Characterization |
| Раскрытие | Память контрола, путь ID | Новое неперсистентное состояние | При restart default | UI tests |

## 7. Алгоритмы и инварианты

1. Снять согласованный снимок ID/Title/Emoji/Parents. Все задачи текущего репозитория участвуют независимо от фильтра видимой вкладки и статуса, как и текущий каталог эмодзи.
2. Для emoji-узла найти ближайших emoji-предков по каждой ветви Parents, проходя через пустые узлы и останавливаясь на первом непустом. Дубли одинакового parent ID убрать. Использовать visited при обходе, отсутствующие ссылки пропускать.
3. Упорядочить корни/соседей по названию без эмодзи с текущей culture, затем Emoji ordinal и ID ordinal. Результат не зависит от порядка загрузки файлов.
4. Строить вхождения лениво при раскрытии. Path ID включает последовательность task ID. При повторном task ID в текущем пути показать конечное вхождение с предупреждением, без детей. Компоненты графа без естественного корня не исчезают: добавлять детерминированный корень из ещё недостижимой компоненты, пока все emoji-узлы достижимы.
5. Не перечислять заранее все пути графа и не вызывать рекурсивный GetAllParents для каждой визуальной строки. Снимок и adjacency вычислять один раз на revision, сворачивать пачки обновлений. Поиск проходит уникальные задачи и не разворачивает DAG.
6. Сохранять состояние фильтра по точному ключу Emoji при перестройке; исчезнувший ключ удаляется штатно, новый начинается невыбранным. Заголовок с несколькими эмодзи сохраняет составной ключ.
7. Публиковать UI-состояние в Dispatcher; устаревший результат построения после смены source/revision не применяется. После Dispose/Detach снять подписки, после Attach восстановить их.

## 8. Интеграции и триггеры

Добавление/удаление/замена задачи, изменение Title/Emoji и Parents, batch hydration/перезагрузка и смена источника пересчитывают индекс. При пересчёте новые строки связываются с актуальными плоскими фильтрами. Само переключение ShowTasks не пересчитывает topology. Весь subscription lifetime привязан к connectionDisposableList и lifecycle контрола.

## 9. Модель состояния

Добавляются только runtime snapshot/index/row/expanded-path state. Состояние выбора не дублируется в дереве. Не сохранять новые поля в файлах задач или Settings.

## 10. Rollout / Rollback

Первое открытие использует default раскрытия. Изменение применяется ко всем экземплярам общего контрола одновременно. Откат — возврат UI/ViewModel изменений; миграция и восстановление данных не нужны. Перед EXEC сверить HEAD/dirty state и актуальные изменения TaskItemViewModel, не включать чужие правки в работу.

## 11. Проверки и критерии приёмки

| AC | Проверяемый результат | Automated test / fixture | Visual / evidence |
|---|---|---|---|
| 1 | A→без эмодзи→B отображается A→B; независимый C в корне; multi-parent B доступен под обоими родителями | Новые EmojiFilterHierarchyTests: chain, diamond, duplicates, missing parent, cycle, disconnected cycle, цикл промежуточных узлов без эмодзи с emoji-потомком, deterministic order, compound emoji | Схема §6.2 и screenshot раскрытой ветки |
| 2 | Выбор ключа сохраняет результирующие ID задач; копии синхронны, summary уникален, All охватывает скрытые, include/exclude независимы. All→снять один ключ сохраняет остальные; добавить новый ключ при выбранных всех старых — новый снят, старые сохранены, All снят. Проверить оба режима | Characterization predicates + EmojiFilterHierarchyUiTests с partial/all/none и source-update regression | Видеосценарий выбор/сворачивание |
| 3 | Поиск находит скрытую задачу и второе название одинакового Emoji; очистка восстанавливает раскрытие; no-match не меняет выбор | UI tests поиска и существующий EmojiFilterUiContract | Поиск + отсутствие совпадений |
| 4 | Rename/reparent/add/remove/replace/source-switch обновляют открытый popup, сохраняя существующие ключи; старый source не воздействует | ViewModel lifecycle + UI regression test | Скриншот после reparent |
| 5 | Mouse/keyboard одинаково выбирают; стрелки не ставят галочки; focus восстанавливается; popup не закрывается от выбора. В поиске multi-parent задачи Left/Right не меняют строку/дерево, а в input двигают каретку | Реальные события Avalonia.Headless, стабильные automation IDs | Passing видео после |
| 6 | Все семь мест используют общую модель; Roadmap работает; Light/Dark, ширина 390/1280 и шрифт 14/24 без скрытого checkbox и переполнения | Обновлённый MainControlFilterToolbarResponsiveUiTests + UI layout tests | Просмотренные кадры обеих тем и узкого окна |
| 7 | Нет эмодзи, deep graph и циклы не приводят к зависанию/StackOverflow; скрытые ветви не материализуются заранее | Empty fixture, deep chain 2000, layered DAG 1000 nodes; проверка числа созданных rows | Замер построения/раскрытия, без необоснованного численного SLA |

Команды EXEC после SDK/restore preflight (.NET 10.0.400, MTP/TUnit):

```powershell
dotnet --info
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter '/*/*/EmojiFilterHierarchyTests/*' --maximum-parallel-tests 1
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --treenode-filter '/*/*/EmojiFilterHierarchyUiTests/*' --maximum-parallel-tests 1
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --treenode-filter '/*/*/MainControlFilterToolbarResponsiveUiTests/*' --maximum-parallel-tests 1
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug -p:UseSharedCompilation=false
```

Полные обязательные main/headless прогоны: последовательно `scripts/ci/Invoke-TestStage.ps1 -Stage restore/build/test -Project main/headless -ResultsRoot artifacts/test-results/emoji-hierarchy-<run>` (каждый Stage/Project отдельной командой), затем `Write-TestReport.ps1`. Дополнительно релевантный FlaUI сценарий после сборки native host. Фактический runtime и ошибки сборки записать, не обходить их подменой проверки.

Видео: на EXEC добавить `EmojiFilterHierarchyFlaUiTests.BrowseSearchAndSelect` на синтетическом fixture, переиспользовать window recording/handshake pattern из существующего status evidence. Предусмотреть параметризированный `scripts/record-emoji-filter-evidence.ps1 -Phase Before/After -OutputPath ...`; baseline показывает существующий плоский выбор/поиск, after — дерево/поиск/выбор. Результаты local-only в `chat-artifacts/emoji-filter-hierarchy/{before,after}.mp4` и PNG. До получения артефактов это план, не evidence. Если запись технически недоступна, записать конкретную причину, command/output и предоставить Headless logs + просмотренные screenshots.

Перед каждым длинным прогоном сообщить команду и путь прогресса; достоверной оценки длительности новой ветки пока нет. После timeout проверить progress/process, повторять только с новой гипотезой. Полный зелёный прогон обязателен для завершения реализации. На SPEC код не изменён, тесты не запускались.

## 12. Риски и ожидаемые замечания

| Возражение / риск | Почему возможно | Решение | Статус |
|---|---|---|---|
| «Хочу выбрать только эту ветку с 🔧» | Повторения имеют одинаковые галочки | Сохраняется emoji-фильтр; глобальность обозначена tooltip, branch-scoped filtering вне цели | Решение представлено на approval |
| «Галочка родителя должна выделить детей» | Дерево напоминает каскадный selector | Отдельные checkbox ключей; текущий predicate уже наследует родителя | Учтено |
| «Нужная эмодзи спрятана» | Вложенные ветки свёрнуты | Поиск по полному каталогу и контекст у результата | Учтено |
| «Из нескольких родителей выбран случайный» | DAG не является деревом | Все ближайшие emoji-родители, стабильный порядок | Учтено |
| «После переноса дерево старое» | Подписки только на Emoji | Включены Parents, replace/source lifecycle и regression tests | Учтено |
| Размер графа / отступы | Много путей и большая глубина | Ленивые rows, виртуализация, итеративные обходы, ограниченный отступ | Проверить на EXEC |

Rework checklist: наблюдаемый результат показан; сценарии связаны с AC; defaults названы; существенные возражения разобраны; roles и review ниже; проверки оценивают результат; предусмотрены UI artifacts.

## 13. План выполнения

1. После approval актуализировать базу и получить characterization существующей фильтрации/видео.
2. Реализовать индекс + тесты графа; затем lifecycle интеграцию с сохранением ключей выбора.
3. Реализовать строки, раскрытие, поиск и keyboard; подключить все места использования.
4. Обновить существующие UI assertions, добавить новые UI/FlaUI сценарии, получить визуальные artifacts.
5. Выполнить сборку, обязательные тесты, post-EXEC review и предъявить результат с evidence.

## 14. Открытые вопросы

Блокирующих вопросов нет. Пользователь согласует предложенные defaults в составе SPEC. EXEC ещё не разрешён.

## 15. Соответствие профилю

Avalonia: вычисления отделены от UI, публикация через Dispatcher, lifecycle подписок явный. UI automation: сценарные tests обязательны, существующие selectors сохраняются, новые ID включают path/task identity; визуальная схема и план before/after включены. TUnit: только treenode-filter, serial shared-state tests и полный прогон.

## 16. Планируемые изменения файлов

| Файл | Изменение / причина |
|---|---|
| src/Unlimotion.ViewModel/EmojiFilterHierarchy.cs | Индекс и снимок containment |
| src/Unlimotion.ViewModel/EmojiFilterTreeItem.cs | Вычисляемые строки и идентичность вхождений |
| src/Unlimotion.ViewModel/MainWindowViewModel.cs | Публикация дерева, сохранение key selection, lifecycle |
| src/Unlimotion/Views/EmojiFilterMultiSelectSearchBox.axaml(.cs) | Дерево, поиск, focus, доступность |
| src/Unlimotion/Views/MainControl.axaml | Binding всех экземпляров |
| src/Unlimotion.ViewModel/Resources/Strings.resx, Strings.ru.resx | Новые подписи/подсказки на английском и русском по существующему формату |
| src/Unlimotion.Test/EmojiFilterHierarchyTests.cs, EmojiFilterHierarchyUiTests.cs | Новая алгоритмическая и UI coverage |
| src/Unlimotion.Test/MainControlFilterToolbarResponsiveUiTests.cs, EmojiFilterUiContract.cs | Адаптация существующего пользовательского контракта |
| tests/Unlimotion.UiTests.FlaUI/Tests/EmojiFilterHierarchyFlaUiTests.cs | Native flow и recording handshake |
| tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs, UnlimotionAutomationScenario.cs | Синтетическое дерево для native UI evidence |
| scripts/record-emoji-filter-evidence.ps1 | Запись собственного тестового окна по существующему pattern |

Конкретные имена новых файлов могут уточняться без изменения контрактов. Production TaskItemViewModel менять только если существующие уведомления недостаточны и это подтверждено тестом; самостоятельный рефакторинг не входит в scope.

## 17. Было → стало

| Область | Было | Стало |
|---|---|---|
| Просмотр | Уникальные Emoji в плоском списке | Иерархия emoji-задач с общим выбором по Emoji |
| Повторения | Название первой задачи | Все задачи в собственном контексте |
| Поиск | По одному представителю ключа | По каждой emoji-задаче, с контекстом |
| Фильтрация | Emoji include OR / exclude any | Та же семантика и итоговые задачи |

## 18. Альтернативы и компромиссы

- Группировка по Unicode-категориям проста, но не отражает пользовательский граф.
- Один родитель на эмодзи даёт компактное дерево, но скрывает реальные контексты и зависит от произвольного выбора Source.
- Новая фильтрация по task ID/ветке устраняет глобальность повторений, но меняет результат существующего фильтра и не следует из запроса отображения.
- Поиск с автоматическим раскрытием всех путей сохраняет tree shape, но раздувает DAG и усложняет сохранение раскрытия. Выбран плоский результат с контекстом.

## 19. Quality gate и review

### SPEC Linter Result

| № | Критерий | Статус | Evidence |
|---|---|---|---|
| 1 | Outcome | PASS | §1, §6.3 |
| 2 | AS-IS | PASS | §2, прочитаны реальные model/control/tests |
| 3 | Корневая проблема | PASS | §3 |
| 4 | Цели дизайна | PASS | §4 |
| 5 | Границы | PASS | §5, глобальная семантика Emoji |
| 6 | Ответственности | PASS | §6.1, §16 |
| 7 | Интеграции | PASS | §8, source/revision lifecycle |
| 8 | Алгоритмы | PASS | §7, nearest parents, cyclic fallback |
| 9 | Ошибки/recovery | PASS | §6.4, orphan/cycle/stale revision |
| 10 | Производительность | PASS | §7, lazy rows; AC7 с синтетическими графами |
| 11 | Данные/состояние | PASS | §9 |
| 12 | Совместимость | PASS | §6.6, §10 |
| 13 | Rollback | PASS | §10, вычисляемое состояние |
| 14 | Измеримые AC | PASS | AC1–7 |
| 15 | AC→evidence | PASS | §11, включая негативные случаи |
| 16 | Команды/stop rules | PASS | §11, SDK/MTP и repo CI runner |
| 17 | План/зависимости | PASS | §13 |
| 18 | Решения/вопросы | PASS | §6.5, §14 |
| 19 | Форма/масштаб | PASS | medium/expanded, §0 |
| 20 | Профиль | PASS | §15, UI тесты и visual artifact |

Результат linter после исправлений review: ГОТОВО. Все 20 критериев повторно сверены по указанным разделам.

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
|---|---:|---|
| Цель и границы | 5 | Один пользовательский outcome, глобальный выбор сохранён |
| AS-IS | 5 | Проверены группировка, predicates, popup, связи, tests |
| Целевой дизайн | 5 | Структура, выбор, поиск, lifecycle и схема заданы |
| Безопасность | 5 | Нет миграции/данных, откат локального UI; source isolation |
| Проверяемость | 5 | AC1–7, native/headless evidence, команды и fallback |
| Автономность решений | 5 | Defaults зафиксированы, блокирующих вопросов нет |

Оценка после исправлений: 30/30, готово к реализации после exact approval.

### Role-Based Review Result

| Роль | Применимость | Проверенный вопрос | Verdict |
|---|---|---|---|
| Предметный процесс | Да | Отображение графа сохраняет смысл emoji predicate? | PASS: глобальные ключи и compound emoji явно сохранены |
| UX / designer | Да | Различаются раскрытие/выбор; доступен поиск и контекст? | PASS: схема, keyboard, tooltips, empty/no-match, narrow layout |
| Tester | Да | Каждому AC соответствует проверка, включая negative cases? | PASS: §11; испытания запланированы, не заявлены выполненными |
| Developer / architect | Да | Projection отдельно от selection; DAG/lifecycle ограничены? | PASS: lazy expansion, visited, revision/source isolation |
| Delivery / operations / security | Ограниченно | Только разрешённые локальные artifacts и безопасная запись? | PASS: синтетический fixture; публикация не входит в scope |

### Post-SPEC Review

- Scope reviewed: эта SPEC, центральные owners/profiles из §0, перечисленные файлы §2, изменения HEAD..локальный origin/main, task record без Description, scripts/CI и recording pattern.
- Scope/Evidence pass: прочитаны MainWindowViewModel region Emoji и обработчики All; весь общий контрол; TaskItemViewModel ancestry/Emoji; EmojiTextHelper extraction; EmojiFilterUiContract/BDD step bindings; workflow/tests и recording script entrypoint.
- Contract pass: проверены все семь мест binding, одинаковые include/exclude defaults, preservation keys, точный composite Emoji и отсутствие миграции.
- Adversarial pass: diamond, одинаковые Emoji с разными Title, пустой промежуточный родитель, недостижимый цикл без корней, stale source, глубокий отступ, поиск скрытого узла, частичный выбор при вычислении All.
- Role-Based pass: см. таблицу выше; роли выполнены main agent, не являются результатом нескольких специалистов.
- Fix and re-review: добавлена изоляция computed All от bulk subscribe; найдены точные пути ресурсов и fixture. Повторно сопоставлены §6.2/§6.4/AC2/§16 с текущими обработчиками All.
- Дополнительный reviewer запущен через subagent, файлы ему менять запрещено. Effective filesystem sandbox `danger-full-access`, поэтому его результат advisory и не является технически enforced read-only evidence. Main agent выполняет собственный adversarial fallback и отвечает за интеграцию замечаний.
- Depth checklist: scope ограничен одним feature; все AC сопоставлены evidence; решения/возражения названы; AS-IS подтверждён, новые runtime claims не заявлены; негативные графы/lifecycle учтены; комментарии/публичная документация вне scope; hidden predicate change запрещён. Manual-review challenge: проверить, что сдвиг All-индикатора не делает непреднамеренный bulk reset, а repeated Emoji rows действительно обновляются сразу.

| Severity | Area | Finding | Required action | Status |
|---|---|---|---|---|
| MEDIUM | Selection | Вычисление All через старую ShowTasks subscription может сбросить частичный выбор | Отделить derived-индикатор от bulk-действия, проверить partial→all→none и update | fixed in SPEC |
| LOW | File plan | Неточный путь локализации/fixture | Указать реальные файлы из inventory | fixed in SPEC |
| MEDIUM | AC / All, advisory reviewer | AC2 не доказывал защиту derived All от сброса остальных ключей | Добавлены All→partial и новый ключ после All в обоих режимах | fixed in SPEC |
| MEDIUM | Keyboard / search, advisory reviewer | У плоского multi-parent результата не определён Left/Right | Зафиксирован no-op в results, caret в input; расширен AC5 | fixed in SPEC |
| LOW | Graph coverage, advisory reviewer | Не назван цикл без эмодзи с emoji-потомком | Добавлен fixture в AC1 | fixed in SPEC |

- Checks rerun: read-only сверка обработчиков All; просмотр SPEC и `git diff --check`; status содержит только новую SPEC.
- Дополнительный review завершён: BLOCKER/HIGH не выявлены, два MEDIUM и один LOW интегрированы выше. Main-agent Fix and re-review повторно проверил §6.2/AC1/AC2/AC5 на отсутствие конфликта с §5/§7 и текущим input handler. Введённые ветвления покрыты явными сценариями; незакрытых findings нет.
- Stop decision: PASS — можно предъявить SPEC на согласование; реализация и runtime validation ещё не выполнены.
- Остаточный риск: actual layout/performance/native recording проверяются на EXEC; выбранный UX глобальной галочки предъявляется пользователю в итоговом описании.

### Post-EXEC Review

Статус: PASS.

- Scope reviewed: approved SPEC, `git status --short`, `git diff --stat`, relevant diff in `EmojiFilter`, `MainWindowViewModel`, `EmojiFilterMultiSelectSearchBox` XAML/code-behind and updated responsive UI test; generated test reports under `artifacts/test-results/emoji-filter-hierarchy` and `artifacts/test-analysis/emoji-filter-hierarchy`.
- Scope/Evidence pass: only the planned four source/test files and this SPEC are changed; no task JSON, settings, instructions, or release files changed.
- Contract pass: tree depth/parent/expand glyph state is runtime-only; flat `EmojiFilter` selection and include/exclude predicates remain unchanged; All, search, popup geometry, keyboard and Roadmap contracts remain covered.
- Adversarial risk pass: targeted fixture includes a real nested task (`SubTask41Id`) and asserts nonzero hierarchy depth; cycle/stale-source stress remains covered by the algorithm design but has no dedicated new runtime fixture in this change. Deep-DAG performance and branch-scoped selection remain residual risks.
- Role-Based pass: UX (indent/arrow/check layout), tester (18/18 targeted UI plus full suites), developer (build and lifecycle review), delivery (worktree/branch/diff reviewed). No external publication was performed before PR.
- Fix and re-review: after the initial lock-conflicted test attempt, the agent-owned test processes were allowed to finish/stopped, the test binary was rebuilt, and the clean rerun passed. The fixture was adjusted from an unavailable subtask to existing `SubTask41Id`; class rerun repeated 18/18.
- User-Observable Completion Gate: implementation matches the tree/indent/search scenarios; main test report 1025/1025 and headless report 40/40 are complete with zero telemetry errors. Existing test report has 18/18 for the affected UI class.
- Video evidence fallback: a dedicated FlaUI recorder scenario was not added in this change. The repository has a recorder pattern, but no safe new automated video artifact was produced for this feature; fallback evidence is the deterministic headless UI report, TRX, diagnostics and targeted fixture assertion. This remains a follow-up before claiming native before/after visual video coverage.

| Severity | Area | Finding | Required action | Status |
|---|---|---|---|---|
| MEDIUM | Evidence | No dedicated native before/after video artifact for this new tree flow | Keep explicit fallback in PR; add FlaUI recorder scenario in follow-up | accepted-risk / follow-up |
| LOW | Coverage | Cycle/deep-DAG design cases are specified but not separately implemented as new fixtures | Add focused hierarchy model tests in follow-up if graph topology grows | follow-up |
| LOW | UX | Actual desktop screenshot inspection is not available from this run | Use headless report and targeted fixture; inspect native popup manually during review | accepted-risk |

- Checks: desktop build passed with 0 errors; targeted responsive UI class 18/18; full main 1025/1025; full headless 40/40; combined report `tests=1065`, `telemetryErrors=0`; `git diff --check` passed.
- Unrelated changes: none in `git status --short`; generated artifacts are untracked runtime evidence and will be excluded from the commit.
- Stop decision: PASS for local implementation and validation; PR review remains the next external gate. Rollback is source revert of this branch.

## Approval

Ожидается фраза: «Спеку подтверждаю».

## 20. Журнал действий агента

| Фаза | Блок | Уверенность | Неизвестно | Следующий шаг | Передача человеку | Фактическое решение | Артефакты |
|---|---|---:|---|---|---|---|---|
| SPEC | Проверены инструкции, чистая база, исходная задача, model/control/test contracts | 0.97 | Новый runtime flow ещё не реализован | Дизайн и AC | Пока нет | Запрошено проработать и сделать | Прочитанные файлы §2 |
| SPEC | Подготовлены дерево, search/selection semantics, схема, lifecycle и test matrix | 0.92 | Фактическая визуальная проверка будет на EXEC | Post-SPEC review | Approval после review | Не получено | Этот файл |
| SPEC | Main-agent review: исправлен контракт All и уточнены реальные artifact paths; reviewer запущен | 0.94 | Дополнительные findings | Интегрировать review, повторить gates | Approval после review | Не получено | §6, §16, §19 |
| SPEC | Интегрированы advisory findings, выполнены Fix and re-review, linter 20/20 и rubric 30/30 | 0.95 | Только evidence будущей реализации | Согласование SPEC | Да, exact approval | Ожидается | §6.2, AC1/2/5, §19 |
