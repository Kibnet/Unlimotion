# Доработка редактора Ленты, фильтра областей и пространств

## 0. Метаданные

- Дата: 2026-08-28.
- Статус: `IMPLEMENTED / VALIDATED`.
- Режим: QUEST, фаза `EXEC` завершена.
- Ветка: `feat/daily-feed`.
- Базовая спецификация: `specs/2026-08-27-daily-feed-ux-redesign.md`.
- Scope: 13 исходных пользовательских замечаний и дополнительный keyboard-контракт `Enter` / `Ctrl+Enter`.
- Профиль: `.NET desktop client + Avalonia UI + UI automation`.
- Product language: русский; технические идентификаторы остаются на английском.
- Approval gate: реализация начинается только после точной фразы пользователя `Спеку подтверждаю`.
- Git delivery: commit/push/PR не входят в это подтверждение и требуют отдельного указания.

## 1. Overview / Цель

Исправить текущую реализацию Ленты так, чтобы ежедневная заметка ощущалась как цельный документ в стиле Obsidian, хотя внутри продолжала храниться и обрабатываться безопасными Markdown-блоками. Пользователь должен читать, ставить курсор, отмечать чекбоксы, выделять и перемещать блоки без ощущения набора отдельных карточек и технических контейнеров.

Одновременно требуется:

- сделать фильтр областей корректным, живым и иерархическим;
- убрать из Ленты лишние элементы и эффекты выделения дня;
- дать выбранным блокам компактную контекстную панель преобразований;
- исправить адаптивность общей верхней панели;
- перенести переключатель пространств в общий app bar;
- сделать базу заметок частью пространства наравне с базой задач;
- вернуть кнопке создания простой квадратный вид `+` без стрелки комбобокса.

Главный продуктовый результат: **Лента выглядит как непрерывная хронологическая запись, а структура блоков проявляется только тогда, когда пользователь с ней взаимодействует.**

## 2. Текущее состояние (AS-IS)

Проверка текущей ветки и кода показала:

1. Дни выводятся через `ListBox`. Фильтрация не исключает день из коллекции, а скрывает внутренний `Border` через `IsVisibleByAreaFilter`. `ListBoxItem` остаётся в layout и образует пустую строку.
2. `Все области` меняет внутреннее состояние элементов, но открытый flyout не получает достаточного набора `PropertyChanged`; галочки визуально синхронизируются только после повторного открытия.
3. Редактор дня состоит из отдельных preview/editor-контролов. Переход каретки между блоками не реализован, а начало редактирования и позиционирование каретки воспринимаются как работа с разрозненными полями.
4. Ручной drag-and-drop использует pointer capture и hit testing; фактическое перемещение блоков в пользовательском сценарии не срабатывает надёжно.
5. Визуальное выделение относится главным образом к зоне handle, а не ко всему блоку.
6. Markdown-чекбокс в preview намеренно имеет `IsHitTestVisible=false`, поэтому его нельзя переключить напрямую.
7. У обычных заголовков нет действия создания области; area heading отличается от обычного heading внутренним marker-комментарием.
8. Handle перемещения постоянно занимает визуальное внимание.
9. Общая верхняя панель имеет одну строку и фиксированный набор колонок; при малой ширине поиск пересекается с переключателем режима и `Разбор`.
10. Переключатель task spaces находится только внутри task UI. Настройки note vault сейчас глобальные (`NoteVault`), поэтому активная база заметок не меняется вместе с task space.
11. В Ленте виден технический status-блок с количеством проиндексированных файлов и незавершённых пунктов.
12. Фильтр областей строится плоским списком, хотя каталог областей уже содержит `ParentId`.
13. Глобальное создание реализовано `DropDownButton`, из-за чего тема рисует лишнюю стрелку раскрытия.

Текущие безопасные контракты, которые требуется сохранить:

- Markdown-файлы остаются источником истины;
- запись выполняется с revision/precondition проверкой и atomic replace;
- служебные sidecar/area/task-reference markers не теряются;
- существующие journal/recovery-механизмы применяются к многошаговым операциям;
- фоновые изменения vault не затирают активное редактирование;
- задачи, области и родительские связи не получают параллельные дублирующие модели.

## 3. Проблема

Текущая Лента технически разбита на блоки и визуально постоянно это подчёркивает. Из-за этого базовый capture-first сценарий — быстро писать и перечитывать единый дневной текст — требует лишних переключений внимания и прямого знания внутренней структуры редактора.

Дополнительные дефекты фильтра и shell создают недоверие к состоянию приложения: выбранные галочки не отражаются сразу, после фильтра остаются пустоты, элементы верхней панели перекрывают друг друга, а переключение пространства меняет только половину рабочего контекста.

## 4. Цели дизайна

1. Один клик по тексту сразу ставит каретку в ожидаемую позицию и включает редактирование.
2. Стрелки переводят каретку через границы соседних редактируемых блоков как в одном документе.
3. В состоянии чтения блоки не выглядят отдельными полями или карточками.
4. Структурные возможности появляются по hover, focus или явному выделению и не мешают записи.
5. Прямое действие над Markdown-чекбоксом не требует входа в режим редактирования.
6. Выделение, toolbar и drag-and-drop имеют видимый, предсказуемый и доступный результат.
7. Фильтр мгновенно отражает состояние и не оставляет layout-пустот.
8. Иерархия областей читается и управляется теми же parent/child правилами, что каталог.
9. Общий app bar корректен в широком и компактном размере и содержит действительно глобальные действия.
10. Переключение пространства атомарно меняет рабочий контекст задач и заметок.
11. Все пользовательские строки локализованы RU/EN; диагностические детали не выступают главным UI.
12. Поведение покрыто unit, Avalonia.Headless и desktop UI automation в соответствии с риском.

## 5. Non-Goals (чего НЕ делаем)

- Не строим новый WYSIWYG/HTML-документ и не меняем Markdown как источник истины.
- Не реализуем произвольное посимвольное выделение текста сразу через несколько блоков.
- Не добавляем cross-day drag-and-drop; перенос выполняется внутри одного дневного документа.
- Не меняем семантику существующего режима `Разбор` и не переносим в toolbar его session-only действия `Оставить/Пропустить`.
- Не создаём новую сущность вместо существующей `AreaDefinition`.
- Не делаем окно управления областями частью контекстного toolbar; для создания области используется компактный focused flow.
- Не превращаем app bar в постоянно двухстрочный на широком экране.
- Не удаляем внутренние счётчики индексатора, если они нужны для логики bootstrap/review; удаляется только статусная панель Ленты.
- Не выполняем commit, push, PR, merge, release или публикацию в рамках approval этой SPEC.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент | Ответственность |
| --- | --- |
| `MainScreen` / `ShellAppBar` | адаптивная верхняя панель, `+`, space selector, режим, поиск, разбор и глобальные меню |
| `MainWindowViewModel` / app coordinator | единая команда смены пространства, блокировки, commit активного редактора, rollback UI selection |
| `TaskSourceSettingsAdapter` / task-space settings | per-space профиль базы заметок и обратимо-безопасная миграция legacy `NoteVault` |
| `FeedViewModel` | проекция видимых дней, состояние иерархического фильтра, переключение активного vault |
| `FeedControl` | неселектируемая виртуализируемая хронология без пустых day containers |
| `MarkdownBlockLivePreviewEditor` | seamless caret handoff, selection, contextual toolbar, pointer/keyboard block movement |
| `MarkdownBlockPreviewControl` | интерактивный checkbox и корректная preview-семантика |
| Markdown services | revision-safe toggle/transform/move/convert-to-area operations с raw preservation |
| Area catalog service | поиск/создание области, parent validation, canonical area marker |
| AppAutomation / Headless / FlaUI | стабильные selectors и end-to-end user journeys для исправленных сценариев |

### 6.2 Детальный дизайн

#### 6.2.1 Визуальная схема: широкий shell

```text
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ [+] [Личное пространство ▾]  [● Лента  ○ Задачи]  [ Поиск…                ]          │
│                                                   [Разбор 7] [⚙] [⋯]                 │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

- `+` — обычная квадратная `Button` с `MenuFlyout`; размер визуальной площадки 40×40, без chevron/dropdown arrow.
- Space selector всегда доступен в обоих режимах и показывает полное имя в tooltip при обрезании.
- Переключатель `Лента / Задачи` сохраняет текущую модель режима.
- Поиск занимает только свободное место и имеет заданный минимум; он не может рисоваться поверх соседних controls.
- `Разбор` показывает badge только при наличии элементов; Settings и overflow остаются глобальными.

#### 6.2.2 Визуальная схема: компактный shell

```text
┌──────────────────────────────────────────────────────────┐
│ [+] [Личное ▾] [● Лента ○ Задачи] [Разбор 7] [⋯]        │
│ [ Поиск по заметкам / задачам…                         ] │
└──────────────────────────────────────────────────────────┘
```

- При достижении breakpoint app bar переходит в две строки: действия — первая, поиск на всю доступную ширину — вторая.
- Settings может перейти в overflow раньше, чем будет сжат основной переключатель режима.
- При ещё меньшей ширине space selector показывает сокращённое имя с ellipsis, но остаётся доступным.
- Breakpoint определяется доступной шириной и фактическим desired size, а не фиксированным разрешением экрана.

#### 6.2.3 Визуальная схема: день и структурные состояния

```text
Обычное состояние                         Hover / selection

28 августа, пятница                       28 августа, пятница

  ## Проект                               [◈] ## Проект
  Обсудили новую схему API.               [⠿] Обсудили новую схему API.  ┌──────────────┐
  - [ ] Проверить миграцию                [⠿] - [ ] Проверить миграцию  │ ↑ ↓  •  1. ☑ │
                                                                            │ Задача… ⋯    │
                                                                            └──────────────┘
```

- В обычном состоянии gutter не рисует handle и не резервирует заметную «колонку инструментов».
- Hover/focus проявляет handle плавно, без сдвига текста; место под узкий gutter зарезервировано постоянно.
- Выбранный блок подсвечивается целиком: фон + тонкий focus/selection accent, включая текстовую область, но не меняя высоту.
- Для нескольких выбранных блоков подсвечивается каждый выбранный блок, а одна contextual toolbar привязана к границе selection.
- У canonical area heading вместо обычного handle всегда виден спокойный значок области `◈`; при hover/selection он становится drag handle с тем же hit target.
- Toolbar не перекрывает строку, по которой пользователь ставит каретку; если сверху нет места, он открывается снизу.

#### 6.2.4 Хронология без `ListBox`-выделения

- `ListBox` удаляется из роли контейнера дней.
- Используется один `ScrollViewer` и неселектируемый виртуализируемый repeater/items presenter, источник которого — `VisibleDays`, а не полная `Days` с внутренне скрытыми элементами.
- Выбор дня как UI-state отсутствует. `SelectedDay` сохраняется только там, где он действительно нужен команде, либо заменяется explicit current/command target.
- Подгрузка старых страниц остаётся append-only: существующие `FeedDayViewModel` и активные editor instances не пересоздаются.
- После фильтрации день либо присутствует целиком с подходящими блоками, либо отсутствует в visual tree. Нулевой высоты/пустой `ContentPresenter` не допускается.
- Если совпадений нет, показывается один локализованный empty state: `Нет записей в выбранных областях` с действием `Сбросить фильтр`.
- Из Ленты удаляется panel `Проиндексировано файлов / Незавершённых пунктов`; эти данные могут остаться внутренней диагностикой и входом для `Разбор`.

#### 6.2.5 Иерархический фильтр областей

```text
☑ Все области
  ▾ ☑ Работа
      ☑ ИЗП
      ◩ Unlimotion
        ☑ Desktop
        ☐ Android
  ▸ ☐ Личное
  ☑ Без области
```

- Меню строится из `AreaDefinition.ParentId` в стабильном catalog order.
- Каждая область имеет disclosure affordance при наличии детей и tri-state checkbox:
  - `checked`: выбрана область и все её потомки;
  - `unchecked`: не выбрана ни область, ни потомки;
  - `indeterminate`: выбрана только часть subtree.
- Нажатие checkbox родителя переключает всю subtree. Disclosure раскрывает/сворачивает и не меняет выбор.
- `Все области` включает/выключает все реальные области и `Без области` и немедленно вызывает `PropertyChanged` у каждого затронутого видимого node и summary.
- Выбор нескольких областей имеет OR-семантику.
- Выбор родителя включает контент самой области и всех выбранных потомков.
- `Без области` — отдельный root-level leaf; он также участвует в `Все области`.
- Flyout имеет max height и собственный scroll, сохраняет состояние раскрытия в текущей app session.
- Фильтр применяется live без закрытия flyout; summary обновляется в тот же UI tick.
- При изменении area catalog дерево перестраивается с сохранением выбранных существующих `AreaId`; удалённые ID исключаются, новые области по умолчанию включены только если до обновления было выбрано `Все области`.

#### 6.2.6 Цельный caret UX поверх блочной модели

Редактор остаётся блочным на уровне хранения, но ввод ощущается цельным:

1. Single click по preview сразу активирует блок и ставит каретку ближе всего к нажатому символу. Двойной click не требуется.
2. Переход к другому блоку сначала коммитит текущий блок. При конфликте/ошибке переход отменяется, каретка остаётся в текущем блоке и показывается существующий recovery/conflict UI.
3. `Left` в позиции 0 переводит каретку в конец предыдущего редактируемого блока.
4. `Right` в конце текста переводит каретку в начало следующего редактируемого блока.
5. `Up` на первой visual line переводит каретку на последнюю visual line предыдущего блока с максимально близкой X-координатой.
6. `Down` на последней visual line переводит каретку на первую visual line следующего блока с максимально близкой X-координатой.
7. Переход пропускает не редактируемые служебные блоки (frontmatter/technical marker), но обычные headings и list items редактируемы.
8. После перехода scroll минимально доводит каретку в viewport, без прыжка дня к началу.
9. Вход/выход из edit не меняет измеренную высоту блока, фон, border thickness или отступы. Видимый cue фокуса — узкий accent в gutter/selection style, не TextBox-рамка.
10. Pointer selection блоков и текстовая каретка разведены: клик по тексту редактирует, клик/drag по handle выбирает/перемещает блок.

Граница scope: `Backspace/Delete` не объединяют соседние Markdown-блоки в этой итерации; cross-block native text selection также не добавляется. Это явно не должно мешать стрелочной навигации.

##### 6.2.6.1 `Enter` и `Ctrl+Enter`: два разных переноса

Редактор различает границу блоков и перенос строки внутри одного текстового блока так же явно, как пользователь привык в Obsidian:

| Ввод | Пользовательский результат | Markdown-представление |
| --- | --- | --- |
| `Enter` | текущий текстовый блок делится в позиции каретки; правая часть становится новым блоком на следующей строке | между двумя блоками записывается структурная граница: две последовательности `document.NewLine` |
| `Ctrl+Enter` | внутри текущего блока появляется новая визуальная строка; активный блок, selection и режим редактирования не меняются | в содержимое блока вставляется одна последовательность `document.NewLine` |

Дополнительные правила:

1. После `Enter` каретка переходит в начало правой части нового блока. Если Enter нажат в конце, создаётся следующий пустой session-block с кареткой в позиции 0.
2. После `Ctrl+Enter` каретка остаётся в том же блоке сразу после вставленного внутреннего переноса.
3. Preview обязан показывать один внутренний перенос как реальную новую строку, а не схлопывать его в пробел; при этом обе строки остаются единым выбираемым/перемещаемым блоком.
4. Используется фактический newline style документа (`CRLF` или `LF`); смешанные line endings не создаются.
5. Split выполняется как одна revision-checked document operation. При конфликте исходный блок и позиция каретки сохраняются, пустой phantom-block не остаётся.
6. Пустой session-block материализуется при первом вводе. Если пользователь уходит из него, ничего не введя, лишняя последовательность пустых строк в файл не добавляется.
7. Для paragraph `Enter` создаёт второй paragraph. Для list/task item создаётся следующий sibling с тем же indent/marker, а новый task item всегда начинается как `[ ]`. Для heading `Enter` завершает heading и создаёт обычный paragraph после него.
8. `Ctrl+Enter` применяется к многострочным текстовым блокам: paragraph, list/task continuation и blockquote с сохранением требуемого Markdown continuation prefix. Однострочные структурные блоки (`heading`, `area heading`, horizontal rule) не получают невалидную многострочную форму; для них команда недоступна и не изменяет документ.
9. В fenced code обычный `Enter` сохраняет стандартное для кода значение внутреннего перевода строки; разделение fenced block выполняется только за его пределами. Это единственное осознанное исключение, необходимое для валидного Markdown.
10. Прежний shortcut `Ctrl+Enter = сохранить и выйти` удаляется. Явное сохранение без смены блока доступно через `Ctrl+S`; обычные focus/caret transitions по-прежнему автоматически коммитят изменения.
11. Help text, tooltip/shortcut hints и RU/EN accessibility strings отражают новую раскладку клавиш.

#### 6.2.7 Выделение блоков

- Обычный клик по handle выбирает один блок.
- `Ctrl+click` добавляет/убирает блок из selection.
- `Shift+click` выбирает непрерывный диапазон от anchor.
- Selection ограничен одним дневным документом.
- Выделение хранит стабильные block locators, а не только текущие индексы; после разрешённой операции индексы пересчитываются.
- Клик по пустому месту дня снимает selection; клик внутри TextBox не снимает его неожиданно.
- Focus cue и selection cue различимы темой и доступны в light/dark.

#### 6.2.8 Contextual toolbar

Toolbar появляется для selection при hover/focus/keyboard invocation (`Shift+F10`) и содержит:

| Группа | Действия | Правило доступности |
| --- | --- | --- |
| Перемещение | `Выше`, `Ниже` | selection можно переставить внутри дня |
| Формат списка | `Маркированный`, `Нумерованный`, `Чекбоксы` | все выбранные блоки совместимы с line/list transform |
| Семантика | `Создать задачу`, `Вынести в заметку`, `Изменить область` | переиспользуются существующие review operations и их safety contracts |
| Время | `Перенести на сегодня` | исходный день не сегодняшний |
| Область | `Сделать областью` | выбран ровно один обычный heading H1–H6 |
| Дополнительно | overflow при нехватке места | порядок действий не меняется из-за ширины |

- Session-only действия review (`Оставить`, `Пропустить`, next/previous) в toolbar не показываются.
- Для multi-selection операция выполняется над ordered set одним атомарным документным изменением, а не серией независимых сохранений.
- Во время операции toolbar disabled и показывает progress только если операция дольше 300 ms.
- Успешное действие сохраняет разумную selection: transform оставляет блоки выбранными, move оставляет перенесённые блоки выбранными, semantic extraction выбирает результирующий link block.
- Ошибка не очищает selection и не показывает ложный success.

#### 6.2.9 Преобразование списков

- `Маркированный`: каждый выбранный совместимый блок получает `- `; существующий bullet/number/task prefix заменяется, основной текст и вложенные continuation lines сохраняются.
- `Нумерованный`: блоки получают последовательные `1.`, `2.`, ... в текущем визуальном порядке.
- `Чекбоксы`: блоки получают `- [ ]`; у уже выполненного task item сохраняется `[x]`.
- Отступ существующего списка сохраняется; mixed indentation не выравнивается разрушительно.
- Пустые/служебные/frontmatter/area-heading блоки не трансформируются; если selection смешанный, действие disabled с tooltip, а не выполняется частично.
- Сохраняются newline style, BOM, trailing newline и неизвестные inline Markdown constructs.

#### 6.2.10 Интерактивные Markdown-чекбоксы

- Checkbox в preview становится hit-testable и доступным UIA control.
- Click/Space переключает только marker текущего task list item: `[ ]` ↔ `[x]` (также нормализует `[X]` в `[ ]` при снятии).
- Переключение не включает TextBox и не меняет scroll/selection.
- Запись выполняется revision-checked atomic patch.
- Optimistic visual state допустим только с rollback при ошибке; предпочтительно применять состояние после подтверждённой записи, если latency визуально приемлема.
- При внешнем конфликте UI возвращает фактическое состояние и показывает локализованную ошибку/recovery affordance.

#### 6.2.11 Надёжное перемещение блоков

- Текущее pointer-capture/hit-test поведение сначала фиксируется failing UI test.
- Реализация использует либо Avalonia drag-and-drop, либо явный registry bounds элементов; определение target не должно зависеть от `InputHitTest` по захваченному handle.
- Drag начинается только после системного drag threshold, поэтому обычный click остаётся selection.
- Во время drag показываются ghost выбранного набора и insertion indicator между блоками.
- Drop выше/ниже selection считается no-op и не выполняет запись.
- Несмежные выбранные блоки удаляются в descending index order, target пересчитывается после удаления, затем ordered set вставляется один раз.
- Normal headings перемещаются как обычные блоки.
- Area heading перемещается только вместе со всей своей area section до следующего area heading; это сохраняет семантическую принадлежность содержимого. Если одновременно выбрана часть этой же секции, набор нормализуется без дублей.
- Frontmatter/date technical header не draggable.
- Keyboard `Alt+Up/Alt+Down` и toolbar `Выше/Ниже` используют ту же domain operation.
- Любой move сохраняет raw bytes вне переставляемого диапазона и проверяет revision.

#### 6.2.12 Заголовок → область

- Для любого обычного Markdown heading H1–H6 при single selection доступно `Сделать областью`.
- Открывается небольшой popover:
  - `Название` предзаполнено plain text заголовка;
  - `Родитель` использует существующий иерархический area picker и допускает `Без родителя`;
  - если подходящая область уже существует, пользователь может связать заголовок с ней вместо создания дубля.
- Подтверждение создаёт/выбирает `AreaDefinition`, затем заменяет heading на canonical area heading `## Name <!-- unlimotion-area:{id} -->`.
- Операция каталог + Markdown журналируется и идемпотентна. Recovery не оставляет незаметный orphan и не дублирует область при повторе.
- Невалидный parent/cycle блокируется до записи.
- После успеха canonical area heading получает постоянный `◈` в gutter, toolbar закрывается, heading остаётся выбранным.
- Уже canonical area heading не предлагает `Сделать областью`; через area action можно изменить связанную область существующим безопасным flow.

#### 6.2.13 Пространство включает задачи и базу заметок

Добавляется scoped model:

```csharp
public sealed class TaskSourceNoteSettings
{
    public string SourceId { get; set; } = TaskSourceDescriptor.DefaultSourceId;
    public string? RootPath { get; set; }
    public bool IsFeedEnabled { get; set; } = true;
    public int DayBoundaryMinutes { get; set; }
}
```

- `TaskSourcesSettings` получает коллекцию `NoteSettings`, keyed по `SourceId`.
- Daily filename format не дублируется в app config: он по-прежнему принадлежит самому vault sidecar и поэтому автоматически следует за `RootPath`.
- Settings редактирует note profile выбранного/активного task space и явно показывает его имя над настройками vault.
- Новое пространство получает пустой `RootPath`, `IsFeedEnabled=true`, текущую глобальную границу дня как friendly default.
- Пространства могут явно ссылаться на один физический vault; приложение это допускает, но не создаёт связь автоматически для новых пространств.

Атомарный switch flow:

1. Shell selector получает target space и переходит в busy/disabled state.
2. Активный Markdown editor пытается commit; при ошибке switch отменяется.
3. Текущие per-space task/note settings flush-ятся в persistence queue.
4. Task-space coordinator переключает task source.
5. Feed инициализирует note vault target space и проверяет sidecar/index.
6. Только после обоих успешных этапов shell публикует новый active space и снимает busy.
7. При ошибке note-vault init task source возвращается к прежнему space; если полный rollback невозможен, включается уже существующий recovery-required surface, а UI не показывает смешанный контекст.

Во время switch:

- create/search/review и повторный space switch disabled;
- режим `Лента/Задачи` остаётся визуально выбранным, но содержимое имеет busy overlay;
- stale async results от старого vault отбрасываются по generation/source id;
- при новом пространстве без vault Лента показывает connect empty state, а Задачи остаются доступны.

#### 6.2.14 Миграция legacy NoteVault

- При первом чтении новой схемы, если `NoteSettings` отсутствует, текущие глобальные `NoteVault.RootPath`, `IsFeedEnabled`, `DayBoundaryMinutes` копируются в note profile **каждого уже существующего** task space. Так ни одно пространство внезапно не теряет доступ к ранее общей Ленте.
- Миграция имеет version/prepared/committed marker по образцу task-space legacy projection.
- До committed marker legacy section не очищается и остаётся rollback source.
- После успешной миграции новые записи идут в scoped settings; legacy значения можно сохранять как projection активного space только для обратной совместимости текущей версии конфигурации.
- Повторный запуск после crash не создаёт дубли note profiles.
- Orphan note settings, duplicate/empty SourceId и отсутствующий active profile валидируются тем же fail-closed подходом, что task-space catalog.

#### 6.2.15 Кнопка `+`

- `DropDownButton` заменяется на `Button` с attached `MenuFlyout` или эквивалентом без theme chevron.
- Видимое содержимое — один аккуратный символ `+`; доступное имя — `Создать` / `Create`.
- Кнопка квадратная, имеет tooltip с shortcut и сохраняет существующие пункты меню/команды quick capture.
- Automation id `GlobalCreateMenuButton` сохраняется, если это не противоречит platform control type; page object проверяет новый тип и отсутствие отдельного arrow presenter.

### 6.3 User-Observable Scenarios

#### S1. Фильтрация без пустых дней

Given в ленте есть три дня, а выбранная область присутствует только во втором
When пользователь включает только эту область
Then виден только второй день, между соседними элементами нет пустых строк, scroll extent соответствует одному дню.

#### S2. Живое `Все области`

Given flyout фильтра открыт и часть nested областей выключена
When пользователь нажимает `Все области`
Then все видимые checkbox в открытом flyout сразу отмечаются, parent nodes выходят из indeterminate, summary и Лента обновляются без закрытия flyout.

#### S3. Курсор как в одном документе

Given пользователь редактирует конец первого абзаца
When он нажимает `Right`
Then первый блок безопасно сохраняется, следующий блок становится активным, каретка находится в позиции 0 и viewport не прыгает.

#### S4. Single-click caret placement

Given блок находится в preview
When пользователь один раз нажимает между словами
Then блок сразу редактируется и каретка поставлена у нажатого текста без второго click и без рамки TextBox.

#### S5. Multi-block drag

Given `Ctrl`-выбором отмечены два несмежных абзаца
When пользователь тянет handle и отпускает их ниже другого блока
Then виден insertion indicator, оба абзаца перемещаются одним ordered set, остальные raw Markdown-блоки не меняются.

#### S6. Contextual formatting

Given выбраны три обычных абзаца
When пользователь выбирает `Чекбоксы` в toolbar
Then три блока становятся `- [ ]`, остаются выделенными и не меняют порядок.

#### S7. Прямой checkbox

Given в preview виден `- [ ] Проверить миграцию`
When пользователь нажимает checkbox
Then marker сохраняется как `[x]`, TextBox не появляется, позиция scroll не меняется.

#### S8. Heading в область

Given выбран обычный `### Unlimotion`
When пользователь выбирает `Сделать областью`, задаёт parent и подтверждает
Then каталог получает/переиспользует область, строка становится canonical area heading и в gutter постоянно виден `◈`.

#### S9. Компактный shell

Given окно сужено до compact breakpoint
When app bar перестраивается
Then поиск переходит на вторую строку, не перекрывает mode switch/review, все действия остаются keyboard reachable.

#### S10. Смена пространства из Ленты

Given открыта Лента пространства A с vault A
When пользователь выбирает пространство B
Then после commit активного блока одновременно показываются задачи B и vault B; ни одного кадра с задачами B и заметками A после завершения switch нет.

#### S11. Ошибка смены пространства

Given vault B недоступен
When пользователь переключается A → B
Then приложение возвращает A либо показывает recovery-required state, но не подтверждает B как полностью активное и не теряет текст активного блока.

#### S12. Чистая Лента

Given индексирование завершено
When пользователь читает Ленту
Then технической панели с количеством файлов/пунктов нет; `Разбор` по-прежнему показывает актуальную очередь.

#### S13. Простая кнопка создания

Given app bar открыт в любой теме
When пользователь видит/нажимает `+`
Then кнопка квадратная, стрелки комбобокса нет, существующее create menu открывается.

#### S14. `Enter` создаёт новый блок

Given каретка находится между словами `Первая|вторая` в paragraph
When пользователь нажимает `Enter`
Then слева остаётся отдельный блок `Первая`, справа создаётся новый блок `вторая`, каретка переходит в начало правого блока, а файл содержит структурную границу из двух document newlines.

#### S15. `Ctrl+Enter` создаёт строку внутри блока

Given каретка находится между словами `Первая|вторая` в paragraph
When пользователь нажимает `Ctrl+Enter`
Then preview/editor показывает `Первая` и `вторая` на двух строках одного блока, каретка остаётся в нём, а файл содержит между частями ровно один document newline.

### 6.4 State / Interaction Matrix

| State | Text click | Handle | Toolbar | Checkbox | Filter | Space selector |
| --- | --- | --- | --- | --- | --- | --- |
| Read/idle | single-click edit + caret | hidden, hover reveal | hidden | clickable | enabled | enabled |
| Block hover | edit | visible | показывается для selection | clickable | enabled | enabled |
| Block selected | edit без потери selection | visible/selected | visible/focusable | clickable | enabled | enabled |
| Inline edit clean | caret/edit | visible on focus | keyboard invocation | marker edit через текст или preview после commit | enabled | commit then switch |
| Inline edit dirty | edit | drag после commit | action after commit | n/a | filter after commit | commit required |
| Dragging | disabled | ghost + pointer capture/native DnD | hidden/disabled | disabled | disabled | disabled |
| Document operation | disabled | disabled | progress/disabled | disabled | disabled | disabled |
| Area filter open | normal | normal | normal | normal | live tri-state | enabled unless modal |
| Space switching | disabled/busy | disabled | disabled | disabled | disabled | busy/disabled |
| Conflict/recovery | current block retained | disabled for affected doc | recovery-only | rollback | last committed result | switch blocked/rollback |
| No note vault | connect state | n/a | n/a | n/a | n/a | enabled |

### 6.5 Decision Ledger

| Решение | Выбор | Причина | Отклонённая альтернатива |
| --- | --- | --- | --- |
| Контейнер дней | non-selectable virtualized projection `VisibleDays` | убирает ListBox selection и layout-пустоты | скрывать content внутри `ListBoxItem` |
| Редактор | seamless composite behavior поверх блочной модели | сохраняет Markdown/safety и даёт нужное ощущение | полный rich-text rewrite |
| Caret transition | commit-before-transfer | не теряет изменения и не допускает mixed revisions | переключать focus до сохранения |
| Переносы | `Enter` = новый блок, `Ctrl+Enter` = строка внутри блока | соответствует подтверждённой пользовательской модели Obsidian и сохраняет различие в Markdown | оставить `Ctrl+Enter` командой commit |
| Area filter | hierarchical tri-state tree | отражает реальный каталог и массовый выбор | плоский checklist |
| Parent selection | parent checkbox управляет subtree | ожидаемая семантика иерархического фильтра | parent только как папка без собственного фильтра |
| Selection UI | whole-block highlight + contextual toolbar | ясно, над чем выполнится действие | подсветка только handle |
| Area heading move | move всей section | не меняет область содержимого случайно | двигать только marker-heading |
| Checklist click | revision-safe direct toggle | базовое действие без edit mode | неинтерактивный preview |
| Convert to area | journaled catalog + Markdown operation | два источника истины не расходятся | сначала создать область, затем best-effort rewrite |
| Space ownership | note profile keyed by task SourceId | одно пространство = полный рабочий контекст | глобальный NoteVault при per-space tasks |
| Legacy migration | копировать прежний profile во все существующие spaces | сохраняет поведение каждого старого space | назначить только active space и скрыть Ленту в остальных |
| Daily filename format | остаётся в vault sidecar | это свойство физической базы заметок | дублировать format в каждом space config |
| Narrow shell | responsive second row for search | устраняет overlap без потери действий | горизонтальный scroll app bar |
| `+` | plain Button + MenuFlyout | нет лишнего chevron | style hack внутри DropDownButton template |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract | AS-IS | TO-BE | Совместимость / guard |
| --- | --- | --- | --- |
| Feed days source | `Days` + inner visibility | `VisibleDays` projection | Days остаётся source cache; instances не пересоздаются |
| Area filter selection | flat options | tree nodes + selected IDs + include-unassigned | stable AreaId; live notifications |
| Block selection | visual handle state | stable locators + whole block style | same-day only |
| Keyboard newline | `Ctrl+Enter` commit, обычный Enter внутри TextBox | `Enter` split + `Ctrl+Enter` soft line break + `Ctrl+S` explicit save | document newline style и revision guard |
| Markdown checkbox | preview-only | atomic direct toggle | revision/BOM/newline preservation |
| List transform | отсутствует | atomic selected-block transform | disabled for incompatible selection |
| Area conversion | отсутствует | journaled area + heading marker | idempotent recovery |
| Task source settings | per SourceId | без изменения существующих полей | existing task migration preserved |
| Note vault settings | global `NoteVault` | `TaskSources.NoteSettings[SourceId]` | prepared/committed legacy migration |
| Filename format | vault sidecar | без изменения | follows selected RootPath |
| App bar | single row / task-space local | responsive + global space selector | selector/page objects migrated atomically |
| Create control | `DropDownButton` | `Button` + menu flyout | existing commands and AutomationId |

## 7. Бизнес-правила / Алгоритмы

### 7.1 Построение `VisibleDays`

1. На основе selected area IDs формируется immutable filter snapshot/version.
2. Каждый day вычисляет matching content blocks без мутации исходного Markdown.
3. Day входит в `VisibleDays`, только если после правил area scope остаётся хотя бы один пользовательский block.
4. Обновление коллекции выполняется diff-ом с сохранением ссылок на существующие day/editor VMs.
5. При более новом filter version старый async result отбрасывается.
6. Paging добавляет days в source cache, затем применяет текущий snapshot только к новой странице.

### 7.2 Tri-state

- Node `checked`, если selected содержит node и все descendant IDs.
- Node `unchecked`, если не содержит ни node, ни descendant IDs.
- Иначе `indeterminate`.
- Toggle checked/indeterminate node → удалить всю subtree; toggle unchecked → добавить всю subtree.
- `Все области` checked только при выборе всех current IDs и `Без области`; при частичном выборе — indeterminate.

### 7.3 Caret handoff

1. Control проверяет boundary через caret index + text layout visual line.
2. Captures desired X для вертикального перехода.
3. Выполняет `CommitActiveBlockAsync(expectedRevision)`.
4. После успешного commit активирует adjacent editable block.
5. Восстанавливает caret по start/end или nearest character hit на desired X.
6. Возвращает focus и вызывает minimal `BringIntoView` только при необходимости.

### 7.4 Split / internal line break serialization

1. Определить kind активного блока и caret selection range.
2. Для `Ctrl+Enter` построить валидный continuation fragment и вставить один `document.NewLine` внутри active editor buffer без смены block identity.
3. Для `Enter` построить left/right raw fragments и kind-aware правый блок; пустой right fragment держать session-only до первого символа.
4. Сериализовать непустой split через `document.NewLine + document.NewLine`, сохраняя BOM, surrounding raw и trailing newline.
5. Выполнить одну expected-revision запись и перепарсить только затронутый day с locator remap left/right blocks.
6. При успешном split активировать right locator; при ошибке восстановить исходный editor buffer/caret и не публиковать временный блок.

### 7.5 Move ordered set

1. Resolve stable locators against current revision.
2. Normalize area headings into section ranges.
3. Merge overlapping ranges.
4. Copy ranges in document order.
5. Remove ranges from bottom to top.
6. Re-resolve and adjust target against removed count.
7. Insert copied ranges once.
8. Validate invariant: every original raw block occurs exactly once, except explicitly transformed metadata.
9. Atomic write with expected revision; on conflict perform no partial UI reorder.

### 7.6 Space switch invariant

At every published stable state:

```text
ActiveTaskSourceId == ActiveNoteProfile.SourceId == ShellSelectedSpaceId
```

Если invariant нельзя восстановить, приложение переходит в explicit recovery state и запрещает изменяющие операции до восстановления.

## 8. Точки интеграции и триггеры

- `MainScreen.axaml`: layout states / responsive classes / global selector / create button.
- `MainWindowViewModel`: global space option, switch command, busy/error state.
- `MainControl.axaml`: удалить локальный task-space selector, сохранив task breadcrumb.
- `SettingsControl.axaml`: показать active space для note vault section.
- `SettingsViewModel`: load/save active scoped note profile и property refresh после switch.
- `App.axaml.cs`: связать task-space switch с Feed rebind в единой orchestrated операции.
- `TaskStorageSettings.cs`, `TaskSourceSettingsAdapter.cs`, `ActiveTaskSpaceConfiguration`: note profile schema, validation, migration, persistence/rollback.
- `FeedControl.axaml`, `FeedViewModel.cs`: visible projection, hierarchical filter, empty state, status removal.
- `MarkdownBlockLivePreviewEditor.*`: selection, caret navigation, handles, toolbar, DnD.
- `MarkdownBlockPreviewControl.cs`: checkbox input.
- Notes/Feed domain services: transform, toggle, move section, convert area + journal.
- RU/EN resources: все новые labels, tooltips, errors, automation names where applicable.
- AppAutomation/README media page objects: selectors после переноса space control и control type `+`.

## 9. Изменения модели данных / состояния

### Persistent

- Новая коллекция `TaskSources.NoteSettings` из `TaskSourceNoteSettings`.
- Новый migration/projection state или версия существующего catalog schema для legacy `NoteVault`.
- Новая journal operation kind для `CreateOrBindAreaFromHeading`, если существующий recovery store нельзя безопасно расширить без неё.
- Формат daily note и Markdown-файлы не мигрируются.

### Session-only

- `VisibleDays` / filter generation.
- `FeedAreaFilterNodeViewModel`: `AreaId`, `ParentId`, `Depth`, `Children`, `IsExpanded`, tri-state.
- stable selected block locators, anchor, active toolbar placement.
- desired caret X during vertical navigation.
- shell compact state and `IsTaskSpaceSwitching`/generation.

### Derived

- `AllAreasState`, filter summary, `HasVisibleDays`.
- `CanMoveUp/Down`, transform compatibility, `CanConvertHeadingToArea`.
- `IsAreaHeading`, persistent area icon visibility.

## 10. Миграция / Rollout / Rollback

### Migration

1. Сделать characterization tests legacy config: один и несколько task spaces + global NoteVault.
2. При отсутствии scoped note settings подготовить profiles для всех sources.
3. Записать prepared marker и profiles.
4. Перечитать/проверить catalog + fingerprints.
5. Записать committed marker.
6. Оставить legacy projection совместимым до отдельной schema-cleanup задачи.

### Rollout

- Один feature branch / один связный PR после приёмки.
- Сначала schema/services + tests, затем shell switch wiring, затем editor/filter UI.
- Любое перемещение selectors выполняется в том же commit scope, что control relocation.
- UI-facing PR должен содержать before/after video из автоматизированных прогонов либо явный fallback и next-best evidence.

### Rollback

- UI rollback возможен возвратом коммита; legacy `NoteVault` projection позволяет предыдущей версии прочитать active profile.
- Новая версия игнорирует незавершённый prepared migration и восстанавливает/дописывает её идемпотентно.
- Markdown transformations не требуют data migration; каждая пользовательская операция является обычным сохранённым Markdown изменением.
- Area conversion journal восстанавливается до согласованного состояния, а не откатывается удалением потенциально уже используемой области.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- AC-01: area filter не оставляет пустых day rows/containers.
- AC-02: `Все области`, parent и child checkbox визуально и логически обновляются в открытом flyout в тот же UI tick.
- AC-03: filter отображает parent/child hierarchy, disclosure и tri-state.
- AC-04: `ListBox`-selection эффект дня отсутствует; day chronology не является selectable control.
- AC-05: single click включает edit и ставит caret по click point.
- AC-06: Left/Right/Up/Down переводят caret через границы блоков по правилам 6.2.6.
- AC-07: ошибка commit блокирует переход и сохраняет активный текст/focus.
- AC-08: TextBox border/layout jump не появляется при edit/focus/commit.
- AC-09: whole selected blocks визуально выделены; handle скрыт в idle и виден hover/selection.
- AC-10: multi-block pointer drag и keyboard move реально меняют порядок и сохраняют raw invariants.
- AC-11: area heading переносится вместе с section; insertion indicator соответствует результату.
- AC-12: contextual toolbar доступен мышью и клавиатурой, имеет заданные действия и корректные disabled states.
- AC-13: bullet/number/checklist transform атомарен и raw-safe.
- AC-14: preview checkbox кликабелен/доступен, меняет marker без edit mode.
- AC-15: любой ordinary H1–H6 можно journal-safe преобразовать/связать с областью; area heading имеет постоянный icon.
- AC-16: status panel с indexed/pending counts отсутствует, review queue продолжает работать.
- AC-17: narrow shell не имеет overlap; search переходит в отдельную строку.
- AC-18: space selector находится в общем app bar и отсутствует в task-only breadcrumb.
- AC-19: successful space switch атомарно меняет task source и note vault.
- AC-20: failed switch не публикует mixed space state и не теряет dirty editor content.
- AC-21: legacy global NoteVault мигрируется во все existing task spaces идемпотентно.
- AC-22: новый task space получает новый note profile без автоматической привязки к чужому vault.
- AC-23: Settings ясно редактирует note profile активного пространства.
- AC-24: `+` квадратный, без dropdown arrow, открывает прежнее меню и доступен в обоих режимах.
- AC-25: все новые/изменённые пользовательские строки локализованы RU/EN.
- AC-26: targeted и обязательные UI suites проходят; visual artifacts просмотрены человеком/агентом.
- AC-27: `Enter` делит paragraph в позиции каретки на два Markdown-блока, переносит каретку в правый блок и не оставляет phantom/duplicate blank lines.
- AC-28: `Ctrl+Enter` вставляет ровно один document newline внутри совместимого блока, сохраняет один block identity и визуально показывает новую строку.
- AC-29: list/task/heading/fenced-code edge cases следуют таблице 6.2.6.1; старый `Ctrl+Enter = commit` отсутствует, `Ctrl+S` сохраняет без выхода.

### Characterization / TDD

До product code должны появиться красные regression tests для:

1. скрытого day content внутри ListBox, оставляющего container extent;
2. stale `Все области` checkbox state в открытом flyout;
3. одинарного click и caret index;
4. boundary arrow navigation;
5. текущего неработающего pointer drag;
6. неинтерактивного checkbox preview;
7. app bar overlap при compact width;
8. глобального NoteVault при смене task source;
9. theme arrow presenter у текущего `DropDownButton`;
10. текущего обратного keyboard contract: обычный `Enter` остаётся внутри блока, а `Ctrl+Enter` коммитит его.

### Planned automated coverage

- Unit/domain (`Unlimotion.Test`):
  - visible-days projection and filter generations;
  - hierarchy/tri-state selection;
  - list transforms with BOM/newline/metadata/property tests;
  - split/internal-line-break serialization для `LF`/`CRLF`, caret start/middle/end, empty right block и Markdown block kinds;
  - checkbox toggle conflict;
  - multi-range move and area-section normalization;
  - area conversion journal/recovery/idempotency;
  - note profile migration/catalog validation/projection rollback;
  - atomic space-switch orchestration and stale generation rejection.
- Avalonia.Headless:
  - no selectable day container;
  - open-flyout live checkbox notification;
  - click-to-caret and boundary focus transfer;
  - `Enter` split, `Ctrl+Enter` internal line, `Ctrl+S` save и focus/caret assertions;
  - hover/selection/handle/toolbar states;
  - checkbox pointer + Space;
  - pointer drag threshold/indicator/drop;
  - responsive app bar bounds at wide/compact/minimum widths;
  - square `+` and no arrow visual child;
  - global space selector and task-local selector absence.
- FlaUI/AppAutomation:
  - filter hierarchy and no blank day gaps;
  - real mouse multi-block drag;
  - direct task checkbox toggle persisted after reload;
  - heading → area flow including parent;
  - A → B space switch from Feed and Tasks;
  - failed/missing vault switch recovery;
  - compact resize no overlap and keyboard reachability.
  - persisted `Enter`/`Ctrl+Enter` distinction after day reload.

### Visual acceptance

Capture and inspect at minimum:

1. wide Feed idle;
2. compact Feed with second-row search;
3. area filter opened with nested tri-state selection;
4. block hover;
5. multi-selection + toolbar;
6. pointer drag + insertion indicator;
7. area heading persistent icon;
8. both light and dark themes for focus/selection contrast;
9. space switch busy and missing-vault empty states.

For UI automation delivery evidence record repeatable after video. Because the current dirty branch is itself the baseline for these corrections, record a before video **before product edits** during EXEC; keep generated media untracked unless the user asks otherwise.

### Команды для проверки

Exact project paths/filters are finalized from discoverable tests during EXEC. Minimum gate:

```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore -- --treenode-filter '/*/*/Feed*|/*/*/Markdown*|/*/*/DailyMarkdown*|/*/*/TaskSpace*'
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj --no-restore -- --treenode-filter '/*/*/Feed*|/*/*/TaskSpace*'
dotnet test tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj --no-restore -- --treenode-filter '/*/*/Feed*|/*/*/TaskSpace*'
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj --no-restore
git diff --check
```

Если TUnit discovery требует другой `--treenode-filter`, использовать фактически выведенные fully-qualified tree nodes, а не VSTest `--filter`.

### Stop rules для validation

- Не маскировать failing UI test бесконечными retries.
- Один доказанный infrastructure/pre-existing flake можно повторить один раз, отдельно зафиксировав первый failure и evidence.
- Любой failure в изменённом сценарии возвращает работу в fix/re-review.
- Заблокированный desktop runner не заменяется утверждением о visual correctness; нужен Headless + manual/computer-use fallback с честным ограничением.
- Нельзя считать green targeted suite доказательством полного suite, build, merge или delivery.

### Acceptance-to-Test Matrix

| AC | Primary evidence | Secondary evidence |
| --- | --- | --- |
| AC-01–03 | filter unit + Headless flyout/layout tests | FlaUI filter journey + screenshot |
| AC-04 | Headless control-tree assertion | visual no-selection walkthrough |
| AC-05–08 | editor Headless tests | FlaUI click/caret + video |
| AC-09 | state/style Headless assertions | light/dark screenshots |
| AC-10–11 | move service property tests | real pointer FlaUI drag video |
| AC-12–13 | toolbar Headless + transform unit | keyboard/mouse walkthrough |
| AC-14 | toggle service + Headless input | persisted FlaUI reload journey |
| AC-15 | area journal/recovery unit | FlaUI heading conversion |
| AC-16 | XAML/Headless absence + review queue unit | visual Feed screenshot |
| AC-17 | bounds assertions at widths | resize video |
| AC-18 | shell/task XAML Headless | both-mode FlaUI selector |
| AC-19–23 | migration/coordinator unit | cross-space Headless/FlaUI flows |
| AC-24 | visual tree/style Headless | light/dark screenshot + menu click |
| AC-25 | RU/EN resource key tests | two-culture smoke |
| AC-26 | recorded commands/results | inspected video/screenshots |
| AC-27–29 | split/newline unit + Headless keyboard tests | FlaUI typing, reload and raw-file assertion |

## 12. Риски и edge cases

| Риск / edge case | Защита |
| --- | --- |
| Caret handoff сохраняет блок асинхронно и ощущается медленно | no-op commit fast path; focus transfer только после safety check; measure latency |
| `Enter` в конце создаёт пустые blocks/blank-line growth | session-only empty block; materialize on first input; blur cleanup test |
| Внутренний newline меняет Markdown kind при special prefix | kind-aware continuation; structured single-line blocks disabled; parser round-trip tests |
| Shortcut `Ctrl+Enter` конфликтует с прежним commit | удалить старый handler/help text, добавить `Ctrl+S`, negative regression test |
| Click-to-caret preview glyph metrics отличаются от TextBox | использовать text layout hit testing/transform и regression coordinates на proportional font |
| Filter rebuild теряет активный dirty editor | commit or block filter action; preserve VM instances in diff projection |
| Parent toggle вызывает десятки notify/reloads | batch selection mutation + один filter generation, individual UI notify |
| Area catalog содержит orphan/cycle | fail-closed node bucket + diagnostic/recovery; не бесконечная рекурсия |
| Drag selection содержит area heading и её child blocks | normalize/merge section ranges без дублей |
| Drop target находится внутри переносимой section | deterministic no-op |
| Checkbox изменился внешне во время click | revision conflict + reload/rollback, no lost update |
| Heading conversion создаёт area, но Markdown write падает | journal recovery/reuse same ID; no duplicate name on retry |
| Две области имеют одинаковое имя | picker показывает hierarchy/path и ID-bound choice |
| H1 date title accidentally converted | действие доступно любому H1–H6 по требованию, но technical generated date header классифицируется отдельно и не является ordinary heading |
| Space switch while quick capture/review modal open | selector disabled во время blocking modal/operation; не меняет target silently |
| Space A/B указывают один vault | разрешено; watcher/session generation не дублирует обработку после switch |
| Legacy settings partial migration crash | prepared/committed idempotent recovery |
| Task source switch succeeded, vault switch failed | rollback обоих; explicit recovery if rollback fails |
| Compact translations длиннее русских | desired-size breakpoint + EN/RU bounds tests |
| Invisible handles remain clickable unexpectedly | hit testing включён только при hover/focus/selection opacity state |
| Toolbar covers selected text or exits viewport | placement anchor with flip/clamp and keyboard focus restoration |
| Status counters used elsewhere | remove only Feed presentation, keep ViewModel/domain values until proven unused |

### Expected User Review Objections

| Возможное возражение | Ответ в дизайне |
| --- | --- |
| «Это всё ещё похоже на набор полей» | idle surface без border/handle, single click и стрелочная boundary navigation создают один поток |
| «Toolbar перегружен» | одна contextual panel, действия сгруппированы, overflow появляется только при нехватке места |
| «Область нельзя понять без открытия фильтра» | canonical area heading всегда имеет спокойный `◈` |
| «После фильтра всё прыгает» | diff `VisibleDays` сохраняет VM и anchor scroll; пустые containers отсутствуют |
| «Переключение пространства опасно для незаписанного текста» | commit-before-switch + rollback/recovery invariant |
| «Зачем копировать один vault во все старые spaces?» | это единственный backward-compatible default: до изменения он был общим и доступным из каждого space; пользователь затем может развести пути |
| «Кнопка + опять выглядит как dropdown» | plain square Button + MenuFlyout, отсутствие arrow проверяется visual-tree test |
| «Area heading переместил только заголовок и сломал раздел» | area heading всегда движется со своей section |

### Rework Prevention Checklist

- [x] Все 13 исходных пунктов и дополнительный keyboard-контракт сопоставлены с AC и тестами.
- [x] Зафиксирован felt outcome «один документ, структура по требованию».
- [x] Решена семантика hierarchy, `Все области`, parent subtree и `Без области`.
- [x] Решены single/multi selection, toolbar availability и incompatible selections.
- [x] Различены `Enter` block split и `Ctrl+Enter` internal line, включая Markdown serialization и edge cases.
- [x] Решена семантика перемещения area section.
- [x] Решена атомарность task source + note vault.
- [x] Зафиксирована backward-compatible миграция legacy NoteVault.
- [x] Зафиксированы raw preservation, revision conflict и journal recovery.
- [x] Визуальные wide/compact/editor/filter артефакты включены в SPEC.
- [x] UI automation и visual evidence обязательны.
- [x] Неутверждённые product code changes не выполнялись.

## 13. План выполнения

### Этап 1 — Baseline и красные regression tests

- Зафиксировать before video текущих 13 дефектов и текущего обратного поведения `Enter` / `Ctrl+Enter`, насколько они воспроизводятся.
- Добавить characterization/TDD tests для filter gaps, live checks, caret, DnD, checkbox, responsive shell и per-space vault.
- Не менять текущие safety contracts ради упрощения UI test.

### Этап 2 — Scoped note profiles и atomic space switch

- Добавить schema/migration/validation/persistence.
- Связать task-space coordinator и Feed rebind.
- Перенести selector в shell и обновить Settings context.
- Прогнать task-space unit/Headless/FlaUI до редакторских изменений.

### Этап 3 — Shell cleanup

- Responsive app bar, square `+`, поиск второй строкой.
- Удалить локальный task-space selector и Feed status panel.
- Синхронно обновить selectors/page objects.

### Этап 4 — Filter and chronology

- Hierarchical tri-state model.
- `VisibleDays` projection и non-selectable virtualized day presenter.
- Paging/filter/scroll regression tests.

### Этап 5 — Editor flow

- Single-click caret positioning and boundary handoff.
- `Enter` split, `Ctrl+Enter` internal newline, `Ctrl+S` explicit commit и kind-aware keyboard behavior.
- Whole-block selection, hidden handles, persistent area icon.
- Clickable task checkbox.
- Focus/layout stability tests.

### Этап 6 — Toolbar, transforms, move and area conversion

- Atomic list transforms.
- Contextual toolbar and accessible keyboard flow.
- Reliable pointer/keyboard multi-block move including area sections.
- Journaled heading → area flow.

### Этап 7 — Validation и review

- Targeted then full affected suites.
- Real desktop scenarios in RU, light/dark, wide/compact.
- After video/screenshots and artifact inspection.
- Post-EXEC adversarial self-review; independent reviewer only if user separately requests agents under current policy.

## 14. Открытые вопросы

Blocking product questions отсутствуют. В SPEC выбраны следующие defaults как наиболее безопасные и согласованные с текущим продуктом:

- parent checkbox включает subtree;
- area heading переносится вместе с section;
- legacy global vault копируется во все existing spaces;
- technical generated date header не считается ordinary user heading;
- cross-block text selection и Backspace merge остаются вне scope.
- `Enter` создаёт новый блок, `Ctrl+Enter` — перенос внутри текущего совместимого текстового блока; fenced code сохраняет обычное code-editor поведение.

Если пользователь не согласен с одним из defaults, его нужно изменить в SPEC до approval.

## 15. Соответствие профилю

- Avalonia desktop state/ViewModel boundaries сохранены.
- UI change имеет обязательные Headless/FlaUI tests по локальному `AGENTS.override.md`.
- UIA names, focus order, keyboard alternatives и theme contrast включены в acceptance.
- Persistent schema имеет migration/rollback/idempotency.
- Долгие/двухресурсные операции используют progress, revision и recovery.
- Visual planning выполнен inline wireframes, не отдельным artifact file, чтобы на SPEC-фазе менять только текущую spec.
- Product strings RU/EN; коды/Markdown identifiers не переводятся.

## 16. Таблица изменений файлов

Планируемые группы; точный список уточняется по фактическому diff, без unrelated files.

| Файл / группа | Изменение |
| --- | --- |
| `src/Unlimotion/MainScreen.axaml*` | responsive shell, global space selector, square `+` |
| `src/Unlimotion/MainControl.axaml*` | удалить task-only selector |
| `src/Unlimotion/Controls/FeedControl.axaml*` | non-selectable chronology, tree filter, status removal |
| `src/Unlimotion/Controls/MarkdownBlockLivePreviewEditor.axaml*` | caret, selection, toolbar, DnD, handle states |
| `src/Unlimotion/Controls/MarkdownBlockPreviewControl.cs` | clickable checkbox |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | global shell/space state |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | active note profile |
| `src/Unlimotion.ViewModel/TaskStorageSettings.cs` | `TaskSourceNoteSettings` schema |
| `src/Unlimotion.ViewModel/Feed/FeedViewModel.cs` | visible days/filter/vault rebind |
| `src/Unlimotion.ViewModel/Feed/*` | filter nodes, transformations/selection if separated |
| `src/Unlimotion/Services/TaskSourceSettingsAdapter.cs` | note profile persistence/migration |
| `src/Unlimotion/Services/TaskSpaceCoordinator.cs` / `ActiveTaskSpaceConfiguration.cs` | atomic task+note switch orchestration |
| `src/Unlimotion/Services/Notes/*` | checkbox/list/move/area conversion operations and journal |
| `src/Unlimotion/App.axaml.cs` | wiring Feed and active task space |
| `src/Unlimotion/Assets/Strings*.resx` | RU/EN strings |
| `src/Unlimotion.Test/*` | domain/ViewModel/Avalonia UI regressions |
| `tests/Unlimotion.UiTests.Headless/*` | shell/filter/editor/space flows |
| `tests/Unlimotion.UiTests.FlaUI/*` | real pointer/desktop journeys |
| `tests/Unlimotion.AppAutomation/*` and README-media automation | selectors/page objects after relocation |
| `specs/2026-08-28-feed-editor-and-space-ux-corrections.md` | эта SPEC + EXEC evidence/journal |

## 17. Таблица соответствий (было → стало)

| Было | Стало |
| --- | --- |
| скрытый content внутри пустого ListBoxItem | день отсутствует в `VisibleDays`/visual tree |
| stale checks до reopen | live tri-state notifications |
| плоские области | parent/child tree |
| selectable day ListBox | non-selectable chronology |
| отдельные click-to-edit blocks | single click + cross-block caret handoff |
| обычный Enter внутри TextBox, Ctrl+Enter сохраняет | Enter делит блок, Ctrl+Enter добавляет внутреннюю строку, Ctrl+S сохраняет |
| неработающий manual drag | tested pointer drag + indicator + atomic move |
| подсвечен handle | подсвечен весь selected block |
| постоянные handles | idle hidden, hover/selection visible |
| checkbox декоративный | checkbox directly toggles Markdown |
| heading только текст | ordinary heading можно сделать областью |
| area marker виден только по содержимому | постоянный `◈` в gutter |
| toolbar только в review | contextual document toolbar переиспользует safe operations |
| поиск перекрывает controls | responsive second row |
| task-space selector только в Tasks | global shell selector |
| NoteVault глобальный | note profile per space |
| технический Feed status | чистая Лента; review badge/queue отдельно |
| dropdown `+▾` | квадратный plain `+` |

## 18. Альтернативы и компромиссы

### Полный rich-text editor

Отклонено: даст естественную каретку, но потребует нового Markdown round-trip pipeline, усложнит raw preservation и резко увеличит риск потери данных. Composite behavior покрывает заявленный UX без смены источника истины.

### Оставить `ListBox`, выключить selection style

Отклонено: уберёт часть visual effect, но сохранит неверную семантику selectable list и пустые containers при inner visibility.

### Flat filter с indent-текстом

Отклонено: визуально имитирует hierarchy, но не даёт disclosure, tri-state и subtree semantics.

### Note vault остаётся глобальным

Отклонено: противоречит требованию, что база заметок относится к пространству, и создаёт смешанный контекст при переключении.

### Мигрировать legacy vault только в active space

Отклонено: ранее vault был виден из любого task space; остальные пространства неожиданно потеряли бы Ленту.

### Перемещать area heading отдельно

Отклонено: следующий текст сменил бы область без явного выбора пользователя.

### Оставить `DropDownButton` и скрыть chevron style-ом

Отклонено: семантика/UIA control остаётся dropdown, а theme template может вернуть arrow. Plain Button соответствует действию и проще тестируется.

## 19. Результат quality gate и review

### SPEC Linter Result

- Статус: `PASS` после ручной проверки по canonical SPEC linter.
- Присутствуют все обязательные разделы template.
- Scope ограничен 13 исходными пользовательскими пунктами, новым keyboard-newline требованием и необходимыми migration/test изменениями.
- User-observable scenarios, state matrix, decision ledger, runtime/data matrix и acceptance-to-test mapping заполнены.
- Открытых blocking вопросов нет; defaults явно перечислены.
- UI visual planning artifact включён в разделы 6.2.1–6.2.5.
- Approval и Git delivery gates разделены.

### SPEC Rubric Result

| Критерий | Оценка |
| --- | --- |
| Problem / outcome clarity | 5/5 |
| Scope / non-goals | 5/5 |
| UX scenarios / states | 5/5 |
| Architecture / data contracts | 5/5 |
| Safety / migration / rollback | 5/5 |
| Acceptance / test evidence | 5/5 |
| Итого | 30/30 |

### Role-Based Review Result

- Product: сохраняется capture-first flow; техническая структура проявляется только по намерению пользователя.
- UX/UI: wide/compact layouts, selection, toolbar, handles и hierarchy имеют явные состояния.
- Accessibility: keyboard moves, `Shift+F10`, UIA checkbox, focus cues и tooltips включены.
- Architecture: tasks+notes имеют единый SourceId invariant; Markdown и area catalog остаются источниками истины.
- Data safety: revision checks, atomic writes, migration markers и recovery journals обязательны.
- QA: каждый пользовательский пункт покрыт AC и минимум одним UI evidence path.
- Delivery: selectors/media evidence и untracked artifacts учтены; commit/push не подразумеваются.

### Post-SPEC Review

- Статус: `PASS` после adversarial self-review.
- Independent reviewer не запускался: текущая multi-agent policy запрещает proactive subagents без явного запроса пользователя.
- Scope reviewed: эта SPEC, текущие Feed/editor/shell/task-space/note-vault реализации, существующие test suites и предыдущая approved Feed SPEC.
- Review passes:
  - Scope/evidence: каждый из 13 исходных пунктов и дополнительный Enter/Ctrl+Enter contract имеют конкретное решение и AC.
  - Adversarial UX: проверены hidden handles, toolbar overload, compact overflow, caret commit failure и selection ambiguity.
  - Data/contract: проверены multi-range index shift, area section semantics, dual-resource conversion, stale vault results и partial migration.
  - Testability: real pointer drag и caret placement не оставлены только unit tests; нужны Headless/FlaUI.
- No-findings justification после fixes: наиболее рискованные места получили explicit invariant, recovery и automated evidence; blocking user decision отсутствует.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | task/note context | Первоначальная формулировка «перенести selector» не гарантировала атомарное переключение двух источников. | Ввести SourceId invariant, commit/flush, rollback/recovery flow. | fixed in SPEC |
| HIGH | area move | Перемещение только area heading могло незаметно изменить область последующего текста. | Перемещать heading вместе с section. | fixed in SPEC |
| MEDIUM | migration | Назначение legacy vault только active space ломало прежнее поведение остальных spaces. | Копировать legacy profile во все existing spaces идемпотентно. | fixed in SPEC |
| MEDIUM | drag | Pointer behavior мог снова пройти unit tests, но не работать мышью. | Обязать failing Headless/FlaUI pointer test и insertion indicator evidence. | fixed in SPEC |
| MEDIUM | caret | Перевод focus до сохранения мог потерять/смешать revision. | Commit-before-transfer; ошибка оставляет caret/focus. | fixed in SPEC |
| LOW | invisible handle | Невидимый hit target мог перехватывать click по тексту. | Hit testing handle активен только в видимых interaction states. | fixed in SPEC |
| LOW | toolbar | Review actions могли перегрузить контекстный toolbar session-only командами. | Исключить `Оставить/Пропустить`, сгруппировать document actions. | fixed in SPEC |
| MEDIUM | keyboard/newlines | Простая смена hotkey могла либо оставить два блока одним paragraph, либо создать лишние blank lines при пустой правой части. | Зафиксировать два Markdown-представления, session-only empty block, kind-aware split и reload tests. | fixed in SPEC |

### Post-EXEC Review

- Статус: `PASS` после реализации, полного affected validation и adversarial self-review.
- Scope reviewed: фактический diff Feed/editor/shell/task-space/note-vault, новые operation services, RU/EN resources, AppAutomation selectors, Headless и FlaUI flows.
- Review passes:
  - Scope/evidence: все 13 исходных замечаний и `Enter` / `Ctrl+Enter` contract сопоставлены с production code и UI coverage.
  - Data safety: проверены interrupted first migration, атомарное переключение task+note sources, revision-aware writes и durable heading-to-area recovery.
  - UX/UI: проверены light/dark wide Feed, compact layout, live area filter, empty state, clickable task status и отсутствие горизонтального overflow.
  - Regression: полный `Unlimotion.Test` — 1275/1275, Headless — 49/49, FlaUI — 22/22.
  - Repository hygiene: `git diff --check` прошёл; untracked diagnostics/media/build directories не удалялись и не добавлялись в delivery.
- Role-based result:
  - Product: capture-first поток сохраняется; отдельный composer не возвращён в Ленту.
  - UX/UI: shell и локальный Feed toolbar адаптивны; фильтрация и edit interactions дают немедленную обратную связь.
  - Accessibility: keyboard contract, focus/caret transfer, checkbox/status actions и automation IDs покрыты тестами.
  - Architecture: task space остаётся единицей контекста tasks+notes; Markdown и area catalog остаются источниками истины.
  - Data safety: partial migration и dual-resource heading conversion имеют идемпотентное восстановление.
  - QA: full affected suites и реальный desktop walkthrough завершены без открытых blocking findings.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | migration | Interrupted first migration могла оставить частично заполненный `NoteSettings` catalog. | Очищать partial catalog перед детерминированным восстановлением. | fixed |
| HIGH | heading → area | Area catalog и Markdown могли разойтись при сбое между двумя записями. | Добавить durable journal, deterministic AreaId и идемпотентный resume. | fixed |
| MEDIUM | shell tests | Старый responsive test создавал только task-local control после переноса selector в global shell. | Тестировать реальный `MainScreen`. | fixed |
| MEDIUM | watcher test | `Task.Delay(60)` не являлся надёжным barrier для file watcher. | Использовать наблюдаемое Markdown-событие как barrier. | fixed |
| MEDIUM | FlaUI selector | Структурный поиск archived filter захватывал новый global task-space ComboBox. | Ввести явный `ArchivedDateFilterComboBox` automation ID. | fixed |
| MEDIUM | compact Feed toolbar | Визуальный проход выявил пересечение area filter и локальных action buttons. | Переводить toolbar в две строки и проверять bounds/overlap в Headless и FlaUI. | fixed |

- Visual evidence:
  - `chat-artifacts/feed-editor-light-wide-final.png` — light, wide, real desktop.
  - `chat-artifacts/feed-editor-dark-wide.png` — dark, wide, real desktop.
  - `chat-artifacts/feed-editor-light-compact.png` — compact layout после исправления overlap.
  - Computer-use walkthrough: live `Все области` checked/unchecked, filtered empty state без пустых дней, clickable task status menu.
- Fallback для video evidence: before-video не было снято до начала product edits; `ffmpeg` недоступен в текущем окружении. Использованы repeatable FlaUI сценарии, inspected after-screenshots и ручной computer-use walkthrough. Это ограничение evidence, а не открытый product defect.
- Unrelated changes audit: checkout уже содержал незакоммиченный Feed baseline, diagnostics/media/output и `obj-codex-area-localization`; они сохранены, не очищались и не выдаются за отдельную delivery.
- Остаточный риск: визуальная приёмка hover/multi-selection/drag опирается главным образом на Headless/FlaUI state contracts; отдельного before/after MP4 нет. Блокирующих code findings после fixes нет.

## Approval

Подтверждено пользователем точной фразой `Спеку подтверждаю` 2026-08-28. Фаза переведена в `EXEC`.

Подтверждение разрешает реализацию утверждённого scope, но не разрешает автоматически commit, push, PR, merge, release или публикацию.

## 20. Журнал действий агента

| Фаза | Тип намерения/сценария | Уверенность | Каких данных не хватает | Следующее действие | Нужен человек | Фактическое обращение / решение | Объяснение | Артефакты |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Принять 13 UX/bug замечаний | 0.99 | Нет | Проследить текущие root causes | Нет | Пользователь перечислил правки | Scope задаётся наблюдаемыми дефектами | Current branch/source |
| SPEC | Repo/current-flow inspection | 0.99 | Нет | Спроектировать единый editor/filter/shell flow | Нет | Нет | Root causes подтверждены в XAML/ViewModel/control code | Feed/editor/shell/settings/task-space files |
| SPEC | Product/UX design | 0.97 | Нет | Зафиксировать visual states и defaults | Нет | Нет | Creator-vibe lens удерживает главный результат: цельный документ, структура по требованию | Sections 4, 6, scenarios |
| SPEC | Architecture/data design | 0.96 | Нет | Добавить migration/invariants/tests | Нет | Нет | Перенос selector без per-space vault создавал бы mixed context | Sections 6.2.13–14, 9–10 |
| SPEC | Adversarial self-review | 0.99 | Independent reviewer запрещён policy без user request | Запросить exact approval | Да | Предстоит | Исправлены high/medium risks caret, area section, migration, pointer DnD | Section 19 |
| SPEC | Добавить Enter/Ctrl+Enter contract | 0.99 | Нет | Повторить lint/review и запросить approval | Нет | Пользователь уточнил keyboard UX | Различие block boundary и internal newline сохранено в UI и raw Markdown | Sections 6.2.6.1, S14–S15, AC-27–29 |
| EXEC | Approval получен | 0.99 | Нет | Снять baseline и начать TDD implementation | Нет | Да: пользователь подтвердил SPEC точной фразой | QUEST gate открыт только для утверждённого scope | Эта SPEC, product/test files из раздела 16 |
| EXEC | Реализовать Feed/editor/filter/shell/task-space scope | 0.99 | Нет | Выполнить targeted и full validation | Нет | Выполнено | Изменения внесены без commit/push | Production и test diff |
| EXEC | Закрыть data-safety deviations | 0.99 | Нет | Проверить recovery tests | Нет | Исправлены partial migration и heading-to-area journal | Двухресурсные операции не должны оставаться в неоднозначном состоянии | `FeedHeadingAreaConversionService`, transaction/recovery tests |
| EXEC | Visual desktop walkthrough | 0.98 | Before-video отсутствует | Проверить light/dark/compact и реальные клики | Нет | Выполнено через FlaUI capture и computer-use | Найден и исправлен compact Feed toolbar overlap | `chat-artifacts/feed-editor-*.png` |
| EXEC | Full affected validation | 0.99 | Нет | Провести post-EXEC review | Нет | 1275/1275 unit, 49/49 Headless, 22/22 FlaUI | Последняя responsive правка повторно проверена полными UI suites | HTML test reports |
| EXEC | Post-EXEC adversarial review | 0.99 | Independent reviewer не запрашивался | Завершить EXEC без Git delivery | Нет | PASS | Blocking findings отсутствуют; evidence limitation описано явно | Section 19, `git diff --check`, `git status --short` |
