# Полировка взаимодействия с редактором и плотности Ленты

## 0. Метаданные

- Дата: 2026-08-29.
- Статус: `APPROVED / EXEC IN PROGRESS`.
- Тип (профиль): QUEST, `.NET desktop client + Avalonia UI + UI automation`.
- Владелец: продукт Unlimotion.
- Масштаб: medium.
- Целевое семейство / behavior baseline: текущая ветка `feat/daily-feed` после реализации `specs/2026-08-28-feed-editor-and-space-ux-corrections.md`.
- Поверхность: desktop-приложение Unlimotion, режимы `Лента` и `Задачи`.
- Effective runtime: не применимо — изменение не зависит от model runtime.
- Eval baseline / evidence: восемь пользовательских сценариев из замечаний 2026-08-29; до EXEC дефекты подтверждены инспекцией XAML/C#; после EXEC требуются Headless/FlaUI-проверки и визуальные артефакты.
- Целевой релиз / ветка: `feat/daily-feed`; commit/push/PR не входят в подтверждение спеки.
- Ограничения: Markdown остаётся источником истины; пользовательские незакоммиченные изменения сохраняются; реализация начинается только после точной фразы `Спеку подтверждаю`.
- Связанные ссылки: `specs/2026-08-27-daily-feed-ux-redesign.md`, `specs/2026-08-28-feed-editor-and-space-ux-corrections.md`.

## 1. Overview / Цель

Довести Ленту до ощущения единого компактного Markdown-документа: клавиатурная навигация сохраняет ожидаемое положение каретки, блоки можно обнаружить и переместить мышью, контекстные действия появляются рядом с объектом работы, а вход в редактирование ничего не обводит и не сдвигает.

Одновременно общая верхняя панель должна адаптироваться к фактической доступной ширине, а кнопка создания — вернуть прежний узнаваемый вид без стрелки `DropDownButton`.

Outcome contract:

- Success means: все восемь замечаний воспроизводимо исправлены в широкой, компактной и тёмной раскладках; клавиатурные операции не повреждают Markdown; скрытые действия остаются доступны через overflow.
- Итоговый артефакт / output: изменения приложения, unit/UI-тесты и до/после visual evidence.
- Stop rules: не выполнять EXEC без approval; остановить реализацию при неоднозначном внешнем изменении редактируемого файла, невозможности безопасно объединить блоки или обнаружении конфликта с пользовательскими изменениями.

## 2. Текущее состояние (AS-IS)

Инспекция текущей ветки показала:

1. `TryMoveCaretAcrossBlockAsync` переводит каретку в `0` или `int.MaxValue`, поэтому вертикальная колонка теряется.
2. Видимость handle основана на XAML-селекторе предка `Grid.BlockRow:pointerover`, который не даёт надёжного пользовательского результата внутри шаблона.
3. Drag-and-drop вручную совмещает pointer capture, собственный hit-test и отслеживание кнопки мыши; жест не доходит до устойчивого drop.
4. `ContextualToolbar.IsVisible` связан только с `HasMoveSelection`, поэтому обычный hover не показывает меню.
5. Редактор имеет жёсткий `StableEditorMinHeight >= 42`, отдельный focus accent и стандартное focus-оформление `TextBox`; это создаёт рамку и меняет вертикальный layout.
6. Лента использует крупные интервалы: внешний margin `16`, day padding `16`, gap дня `12`, внутренние spacing `10–14`.
7. Старая кнопка создания была `42×42`, `CornerRadius=12`, с accent outline и визуально центрированным `➕`. После переноса в `MainScreen` классы старого локального стиля к ней не применяются; текущий `+` выглядит бесцветным и смещённым.
8. `UpdateShellLayout` знает только порог `820 px` и перенос поиска. Остальные элементы не участвуют в расчёте доступной ширины, хотя в task tabs уже существует измеряемый overflow-паттерн.
9. `Backspace` не обрабатывается редактором на границе блока.

Сохраняемые инварианты:

- дневной `.md` — единственный источник истины;
- изменения применяются с revision/precondition-проверкой и atomic replace;
- sidecar, area и task-reference markers нельзя потерять или превратить в пользовательский текст;
- активное редактирование, autosave, recovery и внешнее изменение файла не должны взаимно перезаписывать данные;
- множественное выделение перемещается как одна упорядоченная группа.

## 3. Проблема

Лента всё ещё ведёт себя как набор отдельных технических контролов: клавиатура теряет пространственный контекст, hover не раскрывает возможности, drag не работает, а режим edit меняет геометрию. Избыточные отступы и нестабильная app bar усиливают ощущение незавершённости.

## 4. Цели дизайна

1. Сохранить непрерывную модель чтения и навигации, несмотря на внутренние Markdown-блоки.
2. Сделать структуру блока невидимой в покое и очевидной при hover, selection и drag.
3. Не допускать layout shift при входе в edit, hover или показе toolbar.
4. Максимально уплотнить интерфейс без уменьшения кликабельной зоны ниже разумного минимума и без ухудшения читабельности.
5. Повторно использовать проверенный overflow-паттерн task tabs и прежний визуальный язык создания.
6. Любую операцию над двумя блоками выполнять атомарно и с защитой от внешней ревизии.

## 5. Non-Goals (чего НЕ делаем)

- Не заменяем Markdown-движок и не превращаем Ленту в rich-text-хранилище.
- Не добавляем перенос блоков между разными днями или файлами.
- Не меняем согласованные контракты `Enter` и `Ctrl+Enter`.
- Не вводим новую систему тем, дизайн-токенов или глобальный редизайн экранов задач.
- Не реализуем произвольное column selection, совместное редактирование или новый undo-стек.
- Не объединяем через `Backspace` служебные raw-блоки, fenced code/frontmatter и техническую границу area marker.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `MarkdownBlockLivePreviewEditor` — pointer/keyboard orchestration, visual caret navigation, hover/selection/drag states, focus без декорации.
- `MarkdownLivePreviewEditorViewModel` — выбранные/hovered блоки, допустимость команд, запрос атомарного merge и стабильные automation contracts.
- `FeedMarkdownBlockMoveService` — существующая семантика перестановки блока/группы; UI передаёт ему корректную insertion position.
- Новый merge operation в `Unlimotion.Notes` — одна revision-aware операция объединения двух соседних содержательных блоков.
- `FeedControl` — компактные интервалы Ленты.
- `MainScreen` — общий стиль `+`, измеряемое скрытие app-bar actions и построение overflow.
- Headless/FlaUI tests — поведенческая и реальная pointer/keyboard проверка.

### 6.2 Детальный дизайн

#### A. Каретка между блоками

- `↑` на первой визуальной строке активного блока переходит на последнюю визуальную строку предыдущего редактируемого блока.
- `↓` на последней визуальной строке переходит на первую визуальную строку следующего редактируемого блока.
- Для вертикального перехода хранится preferred visual X — горизонтальная координата каретки относительно текстовой области, а не абсолютный индекс символа.
- Если целевая строка короче, каретка ставится в ближайшую допустимую позицию, но preferred X сохраняется для следующего `↑/↓`, как в обычном многострочном редакторе.
- Preferred X сбрасывается после `←/→`, клика мышью, изменения selection или ввода текста.
- `←` в позиции `0` и `→` в конце блока сохраняют последовательную семантику: конец предыдущего ↔ начало следующего.
- Визуальные переносы (`TextWrapping`) участвуют в расчёте; при недоступности text-layout hit-test используется детерминированный fallback по логической колонке строки.

#### B. Hover, handle и contextual toolbar

- Строка блока получает явное состояние `IsPointerOverBlock`; видимость не зависит от хрупкого descendant selector.
- Handle всегда резервирует узкий gutter, чтобы текст не прыгал, но имеет opacity `0` в покое и становится видимым при hover, keyboard focus, selection и drag.
- Для area heading символ области остаётся постоянно видимым по ранее согласованному контракту.
- Toolbar рисуется как overlay над слоем документа и не участвует в измерении высоты блока/дня.
- При hover одного блока toolbar относится к нему; само наведение не меняет selection.
- При вызове команды hovered блок становится единственным target. Если уже есть множественное выделение и hovered блок входит в него, команда применяется ко всей selection.
- Меню остаётся открытым при переводе указателя с блока на него и закрывается после ухода с обоих, Escape или начала редактирования; небольшая задержка закрытия устраняет мерцание.
- Доступные операции: выше, ниже, маркированный список, нумерованный список, чекбоксы, создать задачу, вынести в заметку, сменить область, превратить heading в область. Недоступные операции видимы disabled либо скрываются по действующему контракту конкретной команды.

#### C. Рабочий drag-and-drop

- Жест начинается только с handle после системного drag threshold; клик без движения продолжает управлять selection.
- Используется стандартный Avalonia drag lifecycle (`DragEnter/DragOver/Drop`) либо эквивалентный единый routed lifecycle без ручного удержания pointer capture на протяжении всего перемещения.
- Payload содержит стабильные идентификаторы выбранных блоков текущего документа, а не ссылки на устаревающие визуальные controls.
- Каждая строка принимает drop before/after; insertion indicator виден на всю ширину текстового блока.
- Drop внутрь собственной выбранной группы и перенос через неперемещаемые технические границы отклоняются без изменения файла.
- Порядок множественного selection сохраняется. После успешного drop selection остаётся на перемещённых блоках, а scroll position не сбрасывается.

#### D. Edit без рамки и layout shift

- У edit `TextBox` отключаются border, focus adorner/outline, лишний background и theme padding во всех состояниях, включая `:focus` и `:focus-visible`.
- Убирается голубой вертикальный focus accent: каретка является достаточным индикатором edit.
- Preview и editor используют один типографический baseline и одинаковую фактическую высоту текста.
- Жёсткий минимум `42 px` удаляется. Однострочный блок имеет высоту строки плюс не более `4 px` суммарного вертикального breathing room.
- Hover/selection/toolbar рисуются background/overlay и не меняют border thickness, padding или measured height.
- Явное review highlight остаётся отдельным review-состоянием и не смешивается с обычным edit.

#### E. Плотность Ленты

Целевые density-токены:

| Участок | Целевое значение |
| --- | --- |
| Внешний padding локальной панели | `12×8 px` |
| Margin основного содержимого | `12 px` по горизонтали, `8 px` по вертикали |
| Gap между служебным баннером и документом | `6–8 px` |
| Padding дня | `10–12 px` |
| Gap между днями | `8 px` |
| Gap заголовок дня → текст | `6 px` |
| Gap между обычными блоками | `0–2 px` |
| Gutter handle | `20 px` с hit target не менее `24×24 px` за счёт overlay |
| Однострочный edit block | без фиксированных `42 px`, та же высота, что preview |

Плотность не достигается уменьшением основного шрифта ниже текущего app font. Длинные строки переносятся, checkbox и команды сохраняют доступную hit area.

#### F. Кнопка `+`

- Остаётся обычным `Button` с `Flyout`, поэтому chevron отсутствует.
- Возвращается прежняя геометрия и визуальный язык: `42×42`, `CornerRadius=12`, accent foreground/border, `ThemeControlLowBrush` background, hover `ThemeControlMidBrush`, bold glyph `➕` размером около `22`.
- Стиль становится локально доступным общей app bar и не зависит от style scope `MainControl`.
- Контент центрируется через `HorizontalContentAlignment/VerticalContentAlignment=Center`; системный padding обнуляется только после явного центрирования.

#### G. Измеряемый overflow общей панели

- App bar измеряет фактические `DesiredSize`, spacing и доступную ширину, как существующий task tabs overflow; один жёсткий breakpoint не считается источником истины.
- Поиск по-прежнему первым переносится во вторую полноширинную строку, когда в одной строке не помещается.
- Затем элементы скрываются с конца приоритетов и появляются в `⋯` в том же порядке и с теми же командами:
  1. `Настройки`;
  2. `Разбор` с текущим счётчиком;
  3. выбор пространства;
  4. переключатель `Лента / Задачи` как два checkable menu item.
- Кнопка `+` и overflow `⋯` не скрываются. Контекст пространства и режима скрывается только при действительно экстремальной ширине и полностью доступен в overflow.
- При расширении окна элементы возвращаются без смены активного пространства/режима и без пересоздания пользовательского состояния.
- Overflow содержит только фактически скрытые команды; disabled/checked состояния синхронизированы с основными controls.

#### H. `Backspace` в начале блока

- Триггер: `Backspace`, collapsed selection, caret index `0`, активный блок не первый и существует непосредственно предыдущий merge-compatible содержательный блок.
- Результат: текущий содержательный текст присоединяется к концу предыдущего; каретка ставится точно в join offset.
- Склейка не вставляет пробел автоматически: она ведёт себя как удаление границы в текстовом редакторе. Пользователь сам определяет, нужен ли пробел.
- Текущий структурный префикс paragraph/heading/bullet/number/task/quote удаляется, потому что объединённый текст наследует тип предыдущего блока; inline Markdown сохраняется.
- Если текущий блок является area heading, сначала требуется явное снятие семантики области через предусмотренное действие: `Backspace` не удаляет area marker молча.
- Fenced code, frontmatter, raw/marker/task-reference и другие технические блоки не объединяются. Для первого или несовместимого блока событие не перехватывается и документ не меняется.
- Замена диапазона двух блоков и разделителя выполняется одной revision-aware atomic operation. При revision conflict сохраняется текущий draft и показывается существующий conflict/recovery UI.
- Успешный merge не создаёт пустой session block и не запускает вторую autosave-запись.

Visual planning artifact:

```text
┌ app bar ───────────────────────────────────────────────────────────────┐
│ [ ➕ ] [Пространство] [● Лента ○ Задачи] [поиск........] [Разбор] [⚙] [⋯] │
│ narrow: [ ➕ ] [● Лента ○ Задачи]                               [⋯] │
│         [поиск......................................................] │
└──────────────────────────────────────────────────────────────────────┘

  29 августа                                              заголовок дня
    ·  Текст обычного блока…                         [↑][↓][•][1.][☑][…]
       Следующий блок; handle и toolbar видны только при взаимодействии
       | каретка без рамки; высота строки не меняется
  ───────────────────── insertion indicator при drag ──────────────────
```

UI test video evidence: после EXEC записать короткий wide/compact desktop-run с hover toolbar, multi-block drag, keyboard column navigation, merge по `Backspace` и app-bar overflow. Если детерминированная запись окна технически невозможна, приложить inspected screenshots плюс логи Headless/FlaUI и явно указать fallback.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Вертикальная навигация | Несколько раз `↑/↓` через строки разной длины и соседние блоки | Каретка сохраняет visual X и возвращается в исходную колонку после короткой строки | Headless keyboard test + desktop walkthrough | AC-1 |
| Горизонтальная навигация | `←` в начале / `→` в конце | Переход в конец/начало соседнего блока | Headless test | AC-2 |
| Hover блока | Навести мышь на обычный блок | Появляются handle и overlay toolbar, текст не сдвигается | Headless pointer test + screenshot | AC-3 |
| Drag одного/нескольких блоков | Потянуть handle на insertion position | Блоки перемещаются в исходном порядке, indicator и selection корректны | Unit + FlaUI real pointer run/video | AC-4 |
| Вход в edit | Клик/Enter по preview | Нет голубой рамки и изменения координат нижних блоков | Geometry assertion + before/after screenshot | AC-5 |
| Плотная Лента | Открыть день с 10+ короткими блоками | Больше содержимого помещается без потери читабельности | Geometry assertions + wide/dark screenshot | AC-6 |
| Кнопка создания | Открыть app bar | Старая accent-кнопка `➕`, центрирована, квадратная, без chevron | Headless style check + screenshot | AC-7 |
| Узкая app bar | Последовательно уменьшать окно | Непомещающиеся действия уходят в `⋯`, ничего не перекрывается | Width matrix Headless + compact screenshot | AC-8 |
| Merge | `Backspace` в начале второго paragraph/list/task блока | Текст атомарно присоединён к предыдущему, caret в join offset | Unit operation + Headless keyboard test | AC-9 |
| Защищённый merge | `Backspace` у area heading/technical boundary или при внешнем конфликте | Структура не повреждается; conflict идёт в recovery flow | Unit negative tests + Headless conflict check | AC-10 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Rest | pointer enters block | Hover, handle + toolbar overlay | Немovable block: handle disabled/toolbar только допустимых действий | Layout неизменен |
| Hover | pointer enters toolbar | Hover удерживается | Уход с обоих закрывает после delay | Без мерцания |
| Selected | hover selected member | Toolbar действует на selection | Hover вне selection не меняет selection | Команда определяет target в момент click |
| Drag armed | movement > threshold | Dragging + insertion indicator | Drop в selection/technical boundary rejected | Файл не меняется до Drop |
| Editing | `↑/↓` на boundary | Commit current revision, edit target at preferred X | Commit conflict: остаёмся с draft/conflict UI | Нет потери текста |
| Editing | `Backspace` at 0 | Atomic merge + caret at join | First/incompatible: no-op/native; conflict: recovery | Selection must be collapsed |
| Wide shell | width shrinks | Search row 2, затем actions → overflow | Extreme narrow: space/mode доступны в menu | Без overlap |
| Compact shell | width grows | Controls возвращаются | Active state сохраняется | Без flicker |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Семантика вертикальной колонки | agent | Preferred visual X, sticky через короткие строки | 0.95 | Логическая колонка хуже работает с wrapping | Нет |
| Семантика `←/→` | agent | Последовательный конец↔начало, без sticky X | 0.98 | Иначе нарушается стандартный text editing | Нет |
| Hover toolbar | agent | Overlay, не занимает layout; hover не меняет selection | 0.95 | Автовыделение было бы неожиданным | Нет |
| Технология drag | agent | Routed/system drag lifecycle вместо долгого manual capture | 0.90 | Реализация зависит от Avalonia platform details | Нет |
| Порядок overflow | agent | settings → review → space → mode | 0.88 | Другой приоритет мог бы быть предпочтительнее | Нет |
| Внешний вид `+` | user + history | Старый `➕`/accent/42 px, но обычный Button без chevron | 0.98 | Emoji rendering различается по платформам | Нет |
| Merge area heading | agent | Не удалять area marker неявно | 0.94 | Один редкий сценарий потребует явного действия | Нет |
| Автопробел при merge | agent | Не вставлять | 0.96 | Пользователь должен сам добавить пробел | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Markdown | daily `.md` + parser offsets/revision | Новый atomic merge двух соседних blocks | Формат файла не меняется | Golden/unit tests |
| Selection/move | VM selection + move service | Надёжный UI drag payload/target | Service contract сохраняется | UI + service tests |
| Shell layout | фактические Avalonia bounds | Measurement-based overflow state | Persisted settings не меняются | Width matrix UI tests |
| Styles | локальные XAML styles | Dedicated app-bar create style | Data migration не нужна | Visual/style assertions |

## 7. Бизнес-правила / Алгоритмы

### 7.1 Sticky visual X

1. Перед вертикальным boundary transition получить X текущей caret из rendered text layout.
2. Если sticky X ещё не установлен — сохранить его.
3. В target visual line выбрать ближайший caret hit по X.
4. Не заменять sticky X координатой короткой target line.
5. Сбросить sticky X при любой не-вертикальной пользовательской установке каретки.

### 7.2 Merge

```text
canMerge = selectionCollapsed
           && caret == 0
           && previous exists
           && current/previous are editable content blocks
           && neither boundary contains protected technical/area semantics
```

При `canMerge=true` операция рассчитывает один replace range от начала предыдущего блока до конца текущего, сохраняет префикс предыдущего блока, удаляет структурный префикс текущего и склеивает semantic payload без автоматически добавленного пробела. Join offset считается до записи и используется для восстановления caret после reparse.

### 7.3 Overflow

Layout пересчитывается при attach, size change, локализации/смене текста, изменении pending review count, space list/selection и visibility фич. Пересчёт идемпотентен и не должен создавать measure loop.

## 8. Точки интеграции и триггеры

- `OnEditorKeyDown`: arrows и `Backspace`.
- Pointer enter/exit блока и toolbar: hover state.
- Move handle pointer pressed + routed drag events строк: DnD.
- `SizeChanged`/bounds and relevant VM property changes: app-bar overflow.
- Existing selection action commands: hover/selection toolbar.
- Existing revision, journal/recovery hooks: merge operation.

## 9. Изменения модели данных / состояния

- Persisted data schema не меняется.
- Calculated/transient: hovered block id, sticky visual X, drag payload/target, shell hidden-actions set.
- Возможно добавление operation request/result для merge; в него входят relative path, expected revision, stable locators блоков и resulting caret join offset.
- Transient state не сериализуется в settings или Markdown.

## 10. Миграция / Rollout / Rollback

- Миграция пользовательских файлов не требуется.
- Rollback — возврат UI/operation diff; созданные ранее Markdown-файлы остаются валидными.
- При выключенной Ленте shell остаётся работоспособным; Feed-specific actions не попадают в видимые controls/overflow.
- Никакая recovery запись не удаляется автоматически в рамках этой работы.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **AC-1:** `↑/↓` между блоками сохраняют sticky visual X, включая короткую промежуточную и wrapped line.
- **AC-2:** `←/→` корректно переходят конец↔начало соседнего editable блока.
- **AC-3:** handle и toolbar появляются на hover/focus/selection без layout shift; area icon остаётся видимым.
- **AC-4:** pointer drag реально перемещает один и несколько selected blocks; invalid drop не меняет файл.
- **AC-5:** edit не показывает голубую рамку/accent и не меняет Y/height соседних blocks.
- **AC-6:** интервалы соответствуют density contract; основной шрифт и hit areas остаются читаемыми/доступными.
- **AC-7:** глобальная `➕` визуально соответствует прежней accent-кнопке, центрирована и не имеет chevron.
- **AC-8:** при матрице ширин app bar не перекрывается; скрытые элементы доступны в overflow и возвращаются с сохранением state.
- **AC-9:** `Backspace` объединяет совместимые blocks одной atomic записью, сохраняет inline Markdown и ставит caret в join offset.
- **AC-10:** protected boundary, first block и revision conflict не повреждают документ; recovery/conflict contract сохранён.
- **AC-11:** существующие `Enter`, `Ctrl+Enter`, checkbox, selection transform, review и space-switching тесты остаются зелёными.
- **AC-12:** wide/light, wide/dark и compact визуальный walkthrough не содержит overlap, clipping, hover flicker или layout jump.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1, AC-2 | `MarkdownLivePreviewEditorUiTests`: boundary navigation + sticky X | Keyboard walkthrough | Headless log + video | — |
| AC-3 | Headless pointer enter/exit and geometry test | Hover screenshot | PNG/video | — |
| AC-4 | Move service tests + Headless selection/drop contract + FlaUI physical drag | Inspect resulting order | Video + test log | — |
| AC-5 | Before/edit/after bounds assertions | Wide/dark screenshot | PNG | — |
| AC-6 | XAML/control geometry assertions | Product visual review | wide/dark/compact PNG | — |
| AC-7 | Style/size/centering Headless assertions | Screenshot | PNG | — |
| AC-8 | `FeedShellUiTests` width matrix and menu state | Compact walkthrough | PNG/video | — |
| AC-9 | New merge operation golden tests + keyboard UI test | Inspect saved Markdown/caret | Test log | — |
| AC-10 | protected/revision-conflict negative tests | Recovery UI check | Test log/screenshot | — |
| AC-11 | Existing targeted suites | Smoke walkthrough | Logs | — |
| AC-12 | — | Required visual pass | inspected media paths | Visual criterion |

Минимальные команды после реализации (точные project paths уточняются по solution; generated `obj-*` исключаются):

```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore '-p:DefaultItemExcludesInProjectFolder=**/obj-*/**'
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj --no-restore '-p:DefaultItemExcludesInProjectFolder=**/obj-*/**'
dotnet test tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj --no-restore '-p:DefaultItemExcludesInProjectFolder=**/obj-*/**'
git diff --check
```

Сначала запускаются новые targeted tests; затем затронутые Headless/FlaUI scenarios; после исправлений — полные релевантные suites. Три повторения одной и той же инфраструктурной ошибки без прогресса являются stop condition: сохранить диагностику и не выдавать частичный результат за validation pass.

## 12. Риски и edge cases

- Avalonia text layout может по-разному отдавать caret hit для emoji, combining characters и proportional font; тесты включают короткую строку, кириллицу и wrapping.
- Hover overlay может перекрывать конец короткой строки; размещать его в свободном верхнем правом слое с резервом и горизонтальным scroll/overflow при недостатке ширины.
- Системный DnD в Headless может быть ограничен; обязательная desktop FlaUI-проверка закрывает реальный gesture.
- Reparse после merge меняет индексы blocks; восстановление идёт через returned stable locator/join offset, а не старый VM reference.
- Частые shell measurement events могут зациклить layout; обновлять visibility только при фактическом изменении hidden set и не вызывать синхронный Measure из SizeChanged.
- Emoji `➕` зависит от шрифта ОС; центрирование проверяется по control layout и реальному Windows screenshot.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Toolbar опять сдвигает текст или мигает | Уже наблюдались layout jump и отсутствие hover | Toolbar — overlay, состояние удерживается между block/menu, geometry test обязателен | mitigated |
| «Сохраняется индекс, но не место на экране» | Proportional font и wrapping делают индекс недостаточным | Контракт задан через visual X и text-layout hit-test | mitigated |
| На совсем узком окне исчезнет выбор пространства/режима | Пользователю нужны глобальные действия в любом режиме | Они скрываются только последними и дублируются checkable overflow items | mitigated |
| `Backspace` повредит область | Area heading имеет технический marker | Неявный merge через такую границу запрещён | mitigated |
| «Плотнее» снова окажется субъективным | Предыдущее оформление имело крупные интервалы | Зафиксированы численные density targets и geometry evidence | mitigated |

### Rework Prevention Checklist

- [x] Названы все пользовательские действия и видимые результаты.
- [x] Каждый пользовательский сценарий имеет evidence route.
- [x] Агентские решения внесены в Decision Ledger.
- [x] Вероятные возражения закрыты до approval.
- [x] Выполнен role-based review UX/tester/developer.
- [x] Acceptance criteria сформулированы как проверяемый результат.
- [x] EXEC имеет маршрут до реального desktop evidence.

## 13. План выполнения

1. Добавить characterization tests текущих boundary/hover/geometry/shell состояний.
2. Реализовать sticky visual X и atomic `Backspace` merge с unit tests.
3. Перевести hover/toolbar/DnD на явные состояния и routed drag lifecycle.
4. Удалить edit decoration/layout shift и применить density tokens.
5. Восстановить отдельный app-bar create style и measurement-based overflow.
6. Обновить Headless/FlaUI page objects/scenarios и automation ids без ломки стабильных существующих selectors.
7. Запустить targeted, затем полные релевантные suites; выполнить wide/dark/compact visual walkthrough и post-EXEC review.

## 14. Открытые вопросы

Нет блокирующих вопросов. Спека готова к пользовательскому approval.

## 15. Соответствие профилю

- Профиль: QUEST UI behavior change.
- Выполнено до EXEC: AS-IS inspection, explicit user scenarios, design/interaction/state matrices, decision ledger, test/evidence mapping, rollback and risks, Post-SPEC review.
- Обязательство EXEC: изменить/добавить UI coverage и запустить релевантные UI tests согласно `AGENTS.override.md`.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/MarkdownBlockLivePreviewEditor.axaml` | hover/toolbar overlay, handle states, borderless compact edit | AC-3–AC-6 |
| `src/Unlimotion/Views/MarkdownBlockLivePreviewEditor.axaml.cs` | sticky caret, Backspace, pointer/drag orchestration | AC-1–AC-5, AC-9 |
| `src/Unlimotion.ViewModel/Feed/MarkdownLivePreviewEditorViewModel.cs` | transient hover/merge/drag contracts | Testable separation |
| `src/Unlimotion.Notes/Operations/*Merge*.cs` | atomic revision-aware merge | Data safety |
| `src/Unlimotion/Views/FeedControl.axaml` | density tokens/layout | AC-6 |
| `src/Unlimotion/Views/MainScreen.axaml(.cs)` | restored `+`, measurement overflow | AC-7, AC-8 |
| `src/Unlimotion.Test/MarkdownLivePreviewEditorUiTests.cs` | keyboard/hover/geometry/merge UI tests | UI regression coverage |
| `src/Unlimotion.Test/FeedShellUiTests.cs` | width matrix, overflow and `+` tests | Shell regression coverage |
| `src/Unlimotion.Test/DailyMarkdownBlockMergeTests.cs` (new) | merge golden/conflict/protected cases | Operation correctness |
| `tests/Unlimotion.UiTests.*` relevant page/scenario files | real hover/drag/compact evidence | Project UI policy |
| RU/EN resources if a missing accessible label is found | localized tooltip/menu text only | Accessibility/localization |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| `↑/↓` | начало/конец target | Sticky visual X |
| Handle | hover selector фактически не раскрывает | Явный hover/focus/selection state |
| DnD | manual capture, нестабильный drop | Routed/system drag lifecycle |
| Edit | голубой декор + min 42 px | Чистый текст без layout shift |
| Toolbar | только после selection, занимает строку | Hover/selection overlay |
| Density | крупные карточные интервалы | Численно заданная compact rhythm |
| `+` | бесцветный смещённый glyph | Прежняя accent `➕`, без chevron |
| App bar | один breakpoint, controls обрезаются | Measurement-based overflow |
| `Backspace` | no-op на boundary | Atomic merge совместимых blocks |

## 18. Альтернативы и компромиссы

- Оставить logical character column: проще, но визуально неверно при proportional font/wrapping; отклонено.
- Показывать toolbar отдельной строкой над документом: проще для layout, но разрывает связь с hovered block и съедает место; отклонено.
- Починить текущий manual pointer capture: меньше diff, но сохраняет хрупкую архитектуру gesture; выбран routed drag lifecycle.
- Всегда показывать все app-bar actions во второй строке: ничего не скрывается, но панель растёт и противоречит запросу; выбран overflow.
- Автоматически вставлять пробел при merge: удобнее для некоторых paragraphs, но не соответствует удалению границы и ломает Markdown/пунктуационные случаи; отклонено.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1–5 | PASS | Цель, AS-IS, scope/non-goals и восемь замечаний отражены |
| B. Качество дизайна | 6–10 | PASS | Есть interaction contracts, visual artifact, matrices и ownership |
| C. Безопасность изменений | 11–13 | PASS | Revision/atomic/recovery/protected boundaries сохранены |
| D. Проверяемость | 14–16 | PASS | AC сопоставлены unit/Headless/FlaUI/visual evidence |
| E. Готовность к автономной реализации | 17–19 | PASS | Нет открытых блокирующих решений |
| F. Соответствие профилю | 20 | PASS | QUEST approval gate и UI testing policy указаны |

Итог: **ГОТОВО**.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Восемь замечаний и non-goals однозначны |
| 2. Понимание текущего состояния | 5 | Причины привязаны к текущим controls/methods |
| 3. Конкретность целевого дизайна | 5 | Заданы алгоритмы, states, density и overflow priority |
| 4. Безопасность | 5 | Merge atomic/revision-aware, protected markers не теряются |
| 5. Тестируемость | 5 | Полная AC-to-test matrix и UI evidence |
| 6. Готовность к автономной реализации | 5 | Решения приняты, блокирующих вопросов нет |

Итоговый балл: **30 / 30**. Зона: **готово к автономному выполнению после approval**.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Соответствует ли редактор реальному ежедневному workflow? | PASS | Зафиксировать бесшовную навигацию и безопасный merge — выполнено |
| UX / designer | applicable | Стабилен ли layout и понятны ли affordances? | PASS | Overlay toolbar, явный hover, density targets, старый `+` — выполнено |
| Tester / validation | applicable | Каждый ли AC проверяем, включая pointer и narrow layout? | PASS | Добавлены Headless/FlaUI/visual routes |
| Developer / architect | applicable | Безопасны ли Markdown и state boundaries? | PASS | Atomic merge, stable locators, routed drag, idempotent overflow |
| Delivery / operations / security | not applicable | Есть ли deploy/config/security impact? | PASS | Нет deploy; Git delivery отдельно |

### Post-SPEC Review

- Статус: `PASS`.
- Scope reviewed: эта спека, восемь замечаний, предыдущая approved spec, текущие XAML/C# contracts и локальный UI testing policy.
- Decision: можно запрашивать подтверждение.
- Scope/Evidence pass: каждый пункт 1–8 сопоставлен AS-IS причине, TO-BE и AC.
- Contract pass: `Enter`/`Ctrl+Enter`, Markdown revision/recovery, multi-selection и global shell availability сохранены.
- Adversarial risk pass: проверены area marker merge, short/wrapped line, invalid drop, extreme narrow width, layout-loop и external conflict.
- Role-Based pass: PASS для BA/UX/tester/developer.
- Re-review after fixes: не требовался после финальной внутренней сверки.
- Stop decision: ожидать approval до product code.
- No-findings justification: блокирующих пользовательских решений нет; наиболее спорные решения явно записаны в Decision Ledger и соответствуют безопасному/стандартному поведению редактора.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | risk | Реальный platform DnD нельзя доказать только Headless | Обязательный FlaUI desktop gesture/video | fixed in validation plan |
| LOW | design | Area heading merge мог бы удалить служебную связь | Запретить неявный merge, оставить явное преобразование | fixed |

### Post-EXEC Review

- Статус: `PASS`; исправимые findings закрыты, post-fix desktop FlaUI подтверждён.
- Reviewed scope: sticky visual caret, atomic Backspace merge, hover/selection chrome,
  routed pointer drag, compact Feed layout, global create style, measurement-based
  shell overflow и соответствующие unit/Headless/FlaUI contracts.
- Scope/Evidence pass: unit и Headless-покрытие обновлено для всех исправленных
  взаимодействий. После восстановления console-сессии до `1707×960` полный post-fix
  FlaUI suite прошёл 23/23, включая physical pointer drag и desktop shell flows.
- Contract pass: `Enter`, `Ctrl+Enter`, checkbox, multi-selection, area marker,
  revision/recovery и task-space contracts сохранены; `Shift+Arrow` остаётся обычным
  выделением внутри TextBox и сбрасывает sticky visual X.
- Adversarial pass: first/protected block, revision conflict, invalid/self drop,
  duplicate blocks, proportional/wrapped caret, technical blocks, flyout pointer exit
  и narrow overflow проверены.
- Role-Based pass: PASS для BA/UX/tester/developer; delivery не выполнялся.
- Code review: первый adversarial child review в writable sandbox выявил boundary commit, UI-context, toolbar,
  caret, overflow, hit-target и test-integrity findings; они исправлены. Повторный review
  выявил reset для `Shift+Arrow`, capabilities technical blocks и flyout lifecycle;
  они также исправлены и покрыты регрессиями. После единого зелёного полного baseline
  финальный adversarial child re-review завершён `PASS` без новых actionable findings.
- Validation evidence:
  - `MarkdownLivePreviewEditorUiTests`: 28/28 PASS;
  - `MarkdownLivePreviewEditorTests`: 14/14 PASS;
  - `FeedShellUiTests`: 7/7 PASS;
  - реальные global-create menu routes: deadline 9/9 и roadmap 1/1 PASS;
  - полный `Unlimotion.Test` до финальных interaction fixes: 1296/1296 PASS;
  - финальный полный `Unlimotion.Test` после всех product/test-lifecycle fixes:
    1299/1299 PASS одним запуском на четырёх workers за 27m46s;
  - три промежуточных post-review запуска завершались 1298/1299 из-за разных
    timing-флейков `FeedControlUiTests`. Исправлены fire-and-forget ожидание
    `ReactiveCommand`, блокирующий post-navigation polling и слишком короткий deadline
    файловой lazy-load операции; после этого класс прошёл 25/25 два раза подряд;
  - полный Avalonia.Headless на финальном product build: 49/49 PASS;
  - полный post-fix FlaUI на активной console-сессии `1707×960`: 23/23 PASS;
  - первый повтор FlaUI после восстановления desktop был невалиден из-за отсутствующего
    `DefaultItemExcludesInProjectFolder` во внутренней build-среде и остановлен; корректный
    повтор с environment property прошёл полностью.
- Visual evidence:
  - `chat-artifacts/feed-polish/feed-editor-drag-after.mp4` — 20 s, 2880×1512, 30 fps;
  - `chat-artifacts/feed-polish/feed-editor-drag-after-frame.png` — inspected frame;
  - оба артефакта относятся к предыдущему product snapshot, новый post-fix desktop
    артефакт не создан: standalone app не предоставил targetable window для
    computer-use; фактическое post-fix desktop evidence — полный FlaUI 23/23.
- Re-review after fixes: два adversarial child review в writable sandbox со статусом
  `NEEDS-FIX` выполнены; финальный adversarial child re-review в writable sandbox
  завершён `PASS` без новых actionable findings и без изменений файлов reviewer'ом.
  `git diff --check` — PASS.
- Residual validation action: отсутствует для согласованного scope; Git delivery не выполнялась.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | delivery evidence | До-видео не создавалось до изменения dirty checkpoint | Использовать after-video + automated regression evidence; не заявлять paired before/after | accepted limitation |

## Approval

Получено: **«Спеку подтверждаю»**. EXEC разрешён; commit/push отдельно не запрашивались.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность | Каких данных не хватает | Следующее действие | Нужна передача человеку | Фактическое обращение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | AS-IS inspection | 0.98 | Нет | Зафиксировать design contracts | Нет | Нет | Причины восьми дефектов найдены в текущем коде | Editor/Feed/MainScreen/tests |
| SPEC | UX/interaction design | 0.94 | Нет | Выполнить quality gate | Нет | Нет | Выбраны visual X, overlay toolbar, density tokens, overflow priority | Эта спека |
| SPEC | Post-SPEC review | 0.97 | Только approval | Передать пользователю | Да | Да | Quality gate PASS, product code не изменён | Эта спека |
| EXEC | Approval получен | 1.00 | Нет | Начать TDD implementation | Нет | Да: пользователь написал `Спеку подтверждаю` | QUEST перешёл в EXEC | Эта спека |
| EXEC | Regression tests до fix | 0.96 | Результат expected-red запуска | Запустить targeted TUnit tests | Нет | Нет | Добавлены проверки sticky caret, Backspace merge, hover chrome, edit geometry, `+` и shell overflow | `MarkdownLivePreviewEditorUiTests.cs`, `FeedShellUiTests.cs` |
| EXEC | Editor interaction implementation | 0.97 | Desktop/FlaUI evidence ещё не выполнены | Перейти к density и shell | Нет | Нет | Реализованы sticky visual caret, atomic Backspace merge, borderless edit, overlay toolbar, hover state и tunnel-based pointer drag | Editor XAML/code-behind, ViewModel, merge service/tests |
| EXEC | Pointer drag root-cause analysis | 0.99 | Нет | Сохранить tunnel lifecycle в UI coverage | Нет | Нет | Theme template направлял hit-test во внутренний ContentPresenter и помечал press handled; root tunnel handler с handledEventsToo восстановил selection/drop | `MoveHandlePointerDrag_MovesSelectedBlockToDropTarget` PASS |
| EXEC | Shell/density implementation | 0.97 | Нет | Выполнить полную validation matrix | Нет | Нет | Feed уплотнён, edit chrome не сдвигает layout, `➕` возвращена в accent style, app-bar использует measurement overflow | Feed/MainScreen XAML, `FeedShellUiTests` |
| EXEC | Visual self-review | 0.99 | Нет | Исправить скрытие всего handle и повторить targeted tests/video | Нет | Нет | На первом кадре оставался серый фон кнопки; opacity перенесена с glyph на ToggleButton | Editor XAML, after-video предыдущего snapshot |
| EXEC | Adversarial child review cycle 1 | 0.99 | Нет | Исправить findings и повторить tests | Нет | Нет | Writable sandbox; reviewer фактически не менял файлы. Исправлены boundary arrows, UI-context, compact toolbar, sticky caret reset, dynamic overflow, 24 px handle, merge recovery и честные menu UI routes | Editor/ViewModel/MainScreen/tests |
| EXEC | Full validation before final review | 0.99 | Нужен обычный interactive desktop | Запросить повторный review | Нет | Нет | `Unlimotion.Test` 1296/1296; Headless 49/49; editor 25/25 + 14/14; shell 7/7 | Test reports |
| EXEC | Adversarial child review cycle 2 | 0.99 | Нет | Закрыть три interaction findings | Нет | Нет | Writable sandbox; reviewer фактически не менял файлы. Исправлены `Shift+Arrow` reset, capabilities technical blocks и удержание toolbar при открытом flyout | Editor/ViewModel, новые regression tests |
| EXEC | Test lifecycle stabilization | 0.99 | Нет | Повторить полный baseline | Нет | Нет | Три разных timing-флейка `FeedControlUiTests` устранены ожиданием фактического `ReactiveCommand`, async navigation polling и 30 s lazy-load deadline; класс 25/25 дважды | `FeedControlUiTests.cs`, test reports |
| EXEC | Final automated validation | 1.00 | Нет | Обновить Post-EXEC и выполнить final re-review | Нет | Нет | Editor 28/28 + 14/14; Headless 49/49; полный `Unlimotion.Test` 1299/1299 одним запуском на 4 workers; post-fix FlaUI 23/23 | Test reports, эта спека |
| EXEC | Post-EXEC review | 1.00 | Нет | Передать результат пользователю без Git delivery | Да | Да | Финальный adversarial child re-review в writable sandbox: PASS, новых actionable findings нет, reviewer файлы не менял | Эта спека, `git diff --check` |
