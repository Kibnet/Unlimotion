# Лента: документы во вкладках, предсказуемый редактор и настройки областей

## 0. Метаданные

- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; контекст `testing-dotnet` на EXEC.
- Форма: expanded, canonical `_template.md`; large: редактор, междокументное состояние, persisted settings, task graph и внешнее лицензирование.
- Владелец продукта и approval: пользователь; спецификация, интеграция и проверки: агент.
- Baseline: `feat/daily-feed`, HEAD `77b253f6`, worktree `eff3`; Avalonia12.0.3, .NET10. Дерево до SPEC чистое.
- Поверхность: Codex / Windows / PowerShell; model baseline не является доказательством UI-качества. Eval модели не применим: меняется desktop-приложение.
- Ограничения: на SPEC изменяется только этот файл; код после «Спеку подтверждаю». Commit/push/merge/release не входят в approval реализации. Лицензия Eremex запрошена пользователем, но ещё не получена.
- Источники: предыдущая SPEC `2026-09-05-feed-navigation-and-reading-polish.md`; исходники ниже; `License.txt` (MIT); официальные Eremex источники в6.2.

## 1. Overview / Цель

Закрыть все13 замечаний пользователя одним согласованным UX-контрактом: лента остаётся удобным текстовым пространством, документы не прячутся за краем окна, действия находятся в обычном контекстном меню, выбор блоков не оставляет невидимых остатков, область помогает создавать задачи в нужном месте.

- Success: все13 пунктов имеют проверяемое поведение, сохраняются Markdown и уже введённые правки, переключение не вызывает заметного зависания/скачка.
- Output: реализация, regression/UI coverage, измерения и осмотренные before/after evidence; отдельно статус лицензии и платформенной совместимости.
- Stop: ошибка сохранения не закрывает документ/не меняет пространство; неизвестная лицензия не считается полученной; внешняя публикация требует отдельного разрешения. Новые product-выборы вне описанного контракта возвращаются пользователю.

## 2. Текущее состояние (AS-IS)

Подтверждено чтением кода, не новым native воспроизведением:

- `FeedControl.axaml`: collapse toggle находится справа от даты. Дата `FeedDayViewModel.DisplayDate` форматируется через `Date.ToString("D", CurrentCulture)`; tooltip полного пути отсутствует.
- Там же тематическая заметка — overlay `FeedThematicFileRoot`, в строке `*` лежит редактор без внешнего document ScrollViewer. В VM один `OpenedThematicFile`; открытие заменяет предыдущую VM.
- `MarkdownBlockLivePreviewEditor.axaml`: колонка ручки26DIP, ToggleButton24DIP, отдельная hover toolbar с кнопками и вложенным More flyout.
- `MarkdownBlockLivePreviewEditor.axaml.cs`: нажатие на уже выбранную ручку пропускает SelectMoveBlock; выбор принадлежит отдельному `MarkdownLivePreviewEditorViewModel`.
- `MarkdownBlockPreviewControl.CreateInlinePanel`: WrapPanel из отдельных TextBlock/Button, а не единый поток текста. Это кандидат причины потери позиции/переносов вокруг ссылок; точную регрессию зафиксировать fixture до исправления.
- `BeginEdit` вызывает уведомления, смену ActiveBlock, отмену autosave и обновление move state. Причина задержки/сдвига пока не измерена; не утверждаем, что виноват диск или конкретный обработчик.
- `AreaDefinition`: Id, Name, ParentId, IsArchived, SortOrder, DefaultNoteFolder и extension data; корневой задачи нет. `AreaManagementViewModel` уже поддерживает выбор области, dirty draft и reload.
- Формат имени daily-файла уже настраивается отдельно. Его контракт не должен измениться от нового формата отображения.
- Текущая версия тестировалась до этой SPEC: это не evidence новых13 исправлений. Рабочую базу пользователя не изменяли.

## 3. Проблема

Локальные компоненты ведут себя как независимые карточки, хотя пользователь воспринимает ленту как единое рабочее пространство. Это проявляется в разорванном выборе, плавающем layout, одноразовом overlay и недостатке контекста области/файла.

## 4. Цели дизайна

- Один владелец состояния каждого документа и один координатор выбора ленты.
- Нативные привычки: ПКМ, Menu/Shift+F10, вкладки, прокрутка, отмена и предсказуемые модификаторы.
- Компактность без микроскопических click targets; отсутствие постоянно появляющихся панелей над текстом.
- Повторное использование существующих редактора родителей, task conversion journal, safe-save/conflict recovery, area picker и системных тем.
- Новые UI-настройки не меняют именование/содержимое Markdown.

## 5. Non-Goals

- Не переписываем приложение под тему Eremex и не переносим существующие вкладки задач в новый dock.
- Не включаем floating windows, произвольные splits/auto-hide и дизайнер раскладки; нужен DocumentGroup с вкладками.
- Не добавляем редактор PDF/Office/произвольных внешних файлов: вкладки для поддерживаемых Markdown-заметок; остальные ссылки сохраняют текущий безопасный внешний маршрут.
- Не меняем формат Enter/Ctrl+Enter, модель задачи/цели, существующие multi-parent связи и назначения областей заметкам.
- Не делаем скрытых массовых переносов ранее созданных задач при изменении корневой задачи области.
- Не меняем лицензию Unlimotion; не устанавливаем системные службы лицензирования и не публикуем ключи/активационные данные без отдельного согласования.
- Не удаляем/переписываем реальные заметки ради тестирования; synthetic vault only.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент | Ответственность |
| --- | --- |
| `FeedControl` + presenter дня | Заголовок, tooltip, constrained viewport, открытие области |
| Новый `FeedDocumentWorkspaceViewModel` | Вкладки, active document, lifecycle/close guards, связь с пространством |
| Document session/editor store | Единственный editing/save owner на `(space,vault,canonical document)` |
| Новый `FeedBlockSelectionCoordinator` | Выбор, anchor, Ctrl/Shift, глобальный clear и invalidation |
| Editor/Preview controls | Ровный inline text layout, курсор, DnD, стандартный ContextMenu |
| `SettingsViewModel` и application UI settings | DisplayDateFormat, validation, preview и обновление культуры |
| Area management + space settings | Выбор root task и переход к конкретной области |
| Conversion coordinator/journal | Вычисленные родители, explicit override, валидация и recovery |
| Desktop document-host adapter | Eremex DocumentGroup, без Eremex types в domain/notes/common VM |

### 6.2 Детальный дизайн

#### 1–3. Заголовок дня и дата

- Слева направо: компактный chevron → дата → существующий disclosure метаданных. Collapse target остаётся клавиатурно доступным, имя/tooltip зависят от состояния. Stable IDs сохраняются.
- Формат отображения в общих настройках: «Лента → Формат даты», строка .NET custom date format, default `d MMMM yyyy, dddd`. На ru-RU, дате2026-09-09 результат строго `9 сентября 2026, среда`.
- Рядом live preview и «По умолчанию»; пустое/ошибочное/многострочное значение не применяется, последняя валидная настройка сохраняется. Ограничение128 символов; формат проверяется DateOnly formatter, без исполнения выражений. Неаварийный fallback для повреждённого конфига.
- Настройка app-wide, общая для пространств; язык названий месяца/дня — текущая культура приложения. Смена формата/языка обновляет уже загруженные дни и дату в разборе. Идентификаторы, сортировка, поиск daily-файлов и имя файла неизменны.
- Hover/focus даты: tooltip полного абсолютного пути, полученного через текущий vault resolver. Не конкатенация непроверенного пользовательского текста. Tooltip допускает переносы/копирование через «Копировать путь» в контекстном меню даты. По tooltip файл не открывается.
- Узкое окно: дата переносится, chevron не исчезает, текст не перекрывает соседние controls; формат не меняется молча на сокращённый.

#### 4,8. Заметки во вкладках и доступный низ документа

Visual planning artifact (структурный wireframe, не screenshot и не доказательство готового UI):

```text
┌ +  [Пространство ▾]  Лента / Задачи       Поиск  Разбор  ⚙ ┐
├ [Лента] [Архитектура ×] [Идеи ×]                 [список ▾] ┤
│ ▾ 9 сентября 2026, среда       Служебные данные ▸            │
│   ⠿ Работа                         ПКМ → Настройки области… │
│   ⠿ Текст со ссылкой внутри строки                          │
│   ⠿ Следующий блок…                               scroll    │
└─────────────────────────────────────────────────────────────┘

Активна «Архитектура»: те же панели приложения,
под вкладками один прокручиваемый документ без overlay.
«Лента» закреплена, не закрывается. Вкладки задач не вложены сюда.
```

- Клик по внутренней Markdown-ссылке открывает/активирует постоянную вкладку. Повторный клик на тот же resolved document активирует её, не создаёт второй editor. Якорь не входит в identity; открытие по якорю прокручивает к нему.
- Одна session на документ, даже если daily-файл одновременно есть в хронологии: второй view использует того же владельца текста/undo/save, нет двух конкурирующих редакторов. Focus transfer сохраняет/фиксирует текущую редакторскую сессию; ошибка оставляет исходный контекст.
- Каждая вкладка имеет свой scroll/caret/undo. Переключение сохраняет позицию и draft; возврат к «Ленте» восстанавливает её фильтр/scroll. Нет перезагрузки всего vault при переключении.
- Close: commit draft → успешное сохранение → dispose view/subscriptions. При ошибке/конфликте вкладка остаётся, сообщение локально к документу; нет безусловного cancel/edit discard. Ctrl+W закрывает активную документную вкладку, но не приложение и не закреплённую ленту; Ctrl+Tab/Shift+Ctrl+Tab переключают вкладки в этом host.
- При переключении пространств все открытые docs старого пространства проходят существующий save/recovery gate. Вкладки и позиции сохраняются в памяти отдельно для пространства; при выходе документы восстанавливаются из существующего draft recovery, автоматического открытия всей прошлой tab layout после рестарта пока нет.
- Viewport документа ограничен оставшейся высотой окна, vertical scroll Auto. Нижняя строка достижима wheel/scrollbar/PageDown/End и кареткой. Длинные code lines имеют локальную горизонтальную прокрутку, не расширяют окно.
- Все вкладки доступны из overflow списка на узком окне; заголовки обрезаются с tooltip имени+полного пути. Одинаковые basenames различаются родительской папкой.
- Открытие из link/search/files использует общий document-open route. Task links сохраняют текущую навигацию к задаче. Missing/ambiguous/blocked links дают существующие safe errors, не создают пустой файл автоматически.
- Rename/delete/conflict: ключ по canonical identity/revision; не открывать вторую копию после rename; stale внешняя запись не затирает draft. Исчезнувший документ сохраняет recovery и явно показывает отсутствие файла.

**Eremex: отдельная зависимость и лицензионный этап.**

- Предлагается Eremex1.4.x для desktop через отдельный adapter и version pin после совместимого restore/build spike; текущая документация заявляет поддержку Avalonia12. Версия пакета не угадывается и не требует downgrade Avalonia.
- До первого restore/build с vendor build targets установить `EMX_TELEMETRY_OPTOUT=1` в среде локального spike и CI. Проверить opt-out у дочернего build process; не менять системные переменные пользователя. Build-time telemetry включена поставщиком по умолчанию, поэтому «локальный spike» сам по себе не означает отсутствие отправки данных. Источник: [Build-time telemetry](https://eremexcontrols.net/whats-included/build-time-telemetry.html).
- Android/iOS и F-Droid dependency graph не получают закрытую библиотеку: общий workspace contract с обычным Avalonia tab host на этих платформах. Web — стандартный host до отдельной проверки. Это не fallback на старый overlay: те же вкладки и пользовательские правила.
- Темы Eremex ограничены host; не подключать глобальную тему, меняющую все стандартные controls. Проверить light/dark, localization, accessibility, AOT/trimming и отсутствие runtime trial notice в принимаемой desktop-сборке.
- Официальный free-license сервис проверяет публичный GitHub-проект, README со ссылкой на Eremex, reference библиотеки и допустимую OSS-лицензию. MIT проекта подходит к опубликованному списку, но это не подтверждение уже выданной лицензии.
- После approval: локальное подключение+минимальная attribution в README; затем отдельный gate разрешения публичного commit/push, если сервис не может проверить ещё не опубликованные изменения. Не выдавать изменение README/reference локально за выполнение публичной проверки.
- Запрос лицензии только для `https://github.com/Kibnet/Unlimotion`; без оплаты/смены лицензии. Сервис open-source лицензирования, не аппаратная активация обычной коммерческой лицензии. Если потребует персональные данные, login, согласие или установку службы — остановка и точный запрос пользователю.
- Runtime/license files включаются в Git/CI только после проверки разрешённого способа распространения; activation secrets никогда не публикуются. При отказе не обходить проверку, не убирать watermark патчем; показать ответ сервиса и согласовать альтернативу. До успешного результата пункт8 не считается полностью выполненным.
- Sources, проверены2026-09-09: [Docking](https://eremexcontrols.net/controls/docking/index.html), [System requirements](https://eremexcontrols.net/whats-included/system-requirements.html), [License rules](https://eremexcontrols.net/licensing/index.html#free-non-commercial-licenses-for-open-source-projects), [Open-source service](https://eremexcontrols.net/open-source-licensing/). Это описание требований поставщика, не юридическое заключение о всех каналах распространения.

#### 5. Стандартное контекстное меню

- Убирается hover toolbar целиком; наведение показывает только компактную ручку, не меню.
- ContextRequested/ПКМ/Menu/Shift+F10 на блоке или ручке открывает стандартный вертикальный ContextMenu с текстом, необязательной иконкой, shortcut и separator. Esc закрывает и возвращает focus; нет сетки кнопок/hover flyout.
- На выбранном блоке ПКМ сохраняет набор. На невыбранном выбирает только его, глобально сбрасывая прежний набор. Открытие меню не выполняет преобразование, не сбрасывает draft.
- Текстовый TextBox сохраняет Cut/Copy/Paste/SelectAll для текстового выделения. Block-команды находятся в том же стандартном меню после separator; они действуют на явно обозначенный блок/набор, не расширяют выделенный текст скрыто до нескольких документов.
- Меню блока: «Создать задачу…», «Создать заметку…», «Назначить области…»; separator; «Переместить выше», «Переместить ниже», «Переместить в область…», существующее «Перенести на сегодня» где применимо; separator; «Обычный текст», «Маркированный список», «Нумерованный список», «Чекбоксы», «Превратить заголовок в область…»; для связанной области «Настройки области…». Точный inventory существующих команд сверяется при EXEC, никакая доступная сейчас операция не теряется.
- Неприменимые действия disabled с объяснением, а не молча меняют target. Существующие safe conversion/review dialogs и возможность уточнить диапазон используются повторно. Read-only, conflict, busy исключают мутации.
- Маршрут действий обобщается до document session: `(TaskSourceIdentity, VaultIdentity, DocumentIdentity, Revision, Selection)`, без обязательного `FeedDay` и поиска editor только в `Days`. В тематической вкладке доступны создание задачи/заметки, назначение областей, форматирование, перемещение внутри документа и настройки связанной области. Только day-specific действия, например «Перенести на сегодня», disabled вне ежедневной заметки с объяснением. После действия сохраняется именно исходный документ; focus возвращается в его вкладку.
- Для несмежного выбора одного документа копирование/очистка, преобразование формата и существующее перемещение/DnD работают по точному набору blocks, сохраняя исходный порядок выбранных и raw text остальных. Команды с contiguous-range contract (создание задачи/заметки, назначение/смена области через range dialog) disabled с «Выберите соседние блоки», пока выбор не непрерывен в исходном документе. Автоматическое расширение min..max запрещено; скрытый фильтром промежуточный block тоже считается разрывом. Проверка selection/revision повторяется на submit, не только при открытии меню. Существующее exact-set перемещение в пределах документа не ограничивается новым range gate.

#### 6,7. Ссылки и стабильный Live Preview

- Превью абзаца использует единый inline text layout с корректной baseline. Ссылки и task-reference status/title остаются кликабельными и доступными, но не раскладываются отдельными крупными прямоугольниками в WrapPanel.
- Фиксируется сохранение порядка tokens, пробелов, пустых строк и existing soft/hard breaks. Обычный Enter создаёт следующий block, Ctrl+Enter — newline внутри block; рендер не объединяет текст по обе стороны ссылки. Не изменять source Markdown при простом просмотре.
- Fixtures: текст перед/после Markdown/wiki/task links, несколько ссылок в строке, отдельная ссылка между абзацами, inline newline до/после ссылки, длинный URL/label, mixed formatting, CRLF/LF, unsafe/unresolved links. Маркеры не превращаются в ссылку внутри code.
- Перед оптимизацией измерить begin-edit до painted caret и layout shift; не заменять диагностирование предположением. Убрать rebuild всех блоков/всего feed и синхронный I/O с критического пути, если это подтверждено trace.
- Одинаковые font metrics, line-height, padding, baseline и anchoring preview/editor. Ручка/фокус не добавляют border/padding. Первая видимая строка остаётся на месте с допуском1DIP; Markdown-маркеры могут менять переносы внутри редактируемого block, но не вызывать произвольный сдвиг viewport.
- Цель: после прогрева p95 click→caret ≤100ms на30 переключениях при fixture800 будних дней по1–200 строк; cold activation учитывается отдельно, цель≤250ms на тестовой машине. Записываются абсолютные значения, оборудование, DPI, сборка; не объявлять PASS по одному unit stopwatch. При недостижении — measured finding, а не скрытый пересмотр порога.

#### 9–11. Аккуратная ручка и общий выбор

- Колонка ручки20DIP вместо26, нейтральная иконка10–12DIP; высота target минимум24DIP. В idle ручка прозрачна, место зарезервировано, поэтому текст не прыгает. Hover/focus/selection делает её видимой. Значок связанной области остаётся видимым в idle, как ранее согласовано.
- Выбран весь block, не только ручка. Указатель над icon/target различается по hover; область target не перекрывает текст. Touch может использовать увеличенную прозрачную hit area без изменения desktop layout.
- Координатор принадлежит активному feed workspace; идентификатор выбора включает документ и устойчивый block locator/revision, а не только индекс/ссылку на реализованный view. Detach виртуализированного дня не оставляет «вечный» выбор и не теряет сохранённый selection state.
- Без модификаторов: невыбранный block заменяет весь набор; клик на уже выбранной ручке очищает ВЕСЬ набор. Решение о deselect принимается на release без DnD: drag ранее выбранного набора не должен сначала очищать выбор.
- Ctrl — toggle block; Shift — диапазон в текущем видимом порядке хронологии, в том числе через соседние дни; Ctrl+Shift добавляет диапазон. Esc/клик по свободному месту ленты/начало обычного текстового редактирования очищают общий block selection. Клик внутри меню/drag не считается фоном.
- Фильтр, смена вкладки/пространства и внешний revision change очищают/валидируют selection централизованно. Скрытые фильтром blocks не должны оставаться неявной целью команд. Collapse дня очищает выбранные внутри него blocks с единым пересчётом общего набора; возврат/раскрытие не «воскрешает» старые flags.
- Набор может охватывать дни для выбора и копирования. Существующие single-document mutation/dialog contracts НЕ расширяются неявно: при выборе нескольких исходных файлов преобразование/перемещение disabled с текстом «Выберите блоки одной заметки». Новые multi-file transactional bulk operations не входят в эти13 пунктов. Clear/Ctrl/Shift при этом сквозные. Drag не теряет выбор при отказе.

#### 12,13. Настройки области и корневая задача

- В контекстном меню связанного area heading и area chip — «Настройки области…». Открывается существующее окно областей, нужная область выделена по Id; не новое окно поверх ещё одного окна. Если в окне есть чужой dirty draft, действует Save/Discard/Cancel gate, не тихое переключение.
- Для заголовка с несколькими назначенными областями submenu со списком областей; без связи — только «Превратить в область…». Исчезнувшая область показывает понятное сообщение с возможностью открыть список.
- Поле «Корневая задача» в properties области: существующий поиск/выбор task и очистка. Можно выбрать одну доступную задачу текущего пространства; показ title+идентификатора, открытие task по ссылке. Это task parent, не parent area.
- Настройка относится к тройке `(TaskSpaceId, VaultIdentity, AreaId)`: внутри настроек пространства хранится `AreaTaskDefaultsByVault[VaultIdentity][AreaId].RootTaskId`, не в переносимом `.unlimotion/areas.json`. Используется существующий проверенный VaultId, не отображаемое имя/необработанный RootPath. Смена RootPath на другой vault выбирает другую mapping; возврат к исходному vault восстанавливает его mapping. Перенос того же vault с сохранённым VaultId сохраняет defaults; коллизия identity разных одновременно подключённых vault блокируется до разрешения. JSON update сохраняет неизвестные поля и использует существующий безопасный storage/lock contract.
- Для новой задачи из заметки вычисляется union корневых задач её выбранных областей, distinct; несколько родителей допустимы существующей моделью. Наследования от родительской области нет: его пользователь не просил. Без заданных roots поведение прежнее.
- В диалоге создания/разбора computed roots видны в уже существующем редакторе родителей ДО подтверждения. Пользователь может удалить/заменить/добавить родителей; explicit выбор приоритетен. После ручной правки области больше не перетирают родителей; доступно явное «Применить родителей областей».
- На submit заново проверяются space/source, существование root, доступность storage, cycle rules и выбранный набор. Missing/archived/unavailable root — предупреждение и явное исправление/удаление связи до создания; не создавать задачу молча в корне. Старые задачи не перемещаются.
- Plain Markdown checkbox остаётся пунктом заметки: root применяется при фактическом создании задачи Unlimotion (review, conversion, соответствующий quick capture), не к каждому чекбоксу при вводе.
- Parent IDs фиксируются в operation intent/journal; task creation+relations должны быть восстановимыми и идемпотентными. Markdown заменяется ссылкой только после подтверждения задачи И связей. Fail after task create не порождает дубль на retry, ошибка не теряет исходный block.
- До первого побочного эффекта journal сохраняет также `TaskSourceIdentity`: устойчивый Id настроенного источника и fingerprint storage binding (тип и canonical local repository / server+database+account, без credentials). Текущий source обязан совпадать с записанным перед каждым create/update/relations/Markdown-replacement/recovery; async смена source также отменяет продолжение. Recovery общего vault в другом task space или после переназначения storage того же SourceId — fail-closed, без записей, с предложением открыть исходный источник. Совпадение TaskId/ParentId не доказывает тождество источника.
- Legacy journal без TaskSourceIdentity автоматически не получает текущий source и не запускает мутации. Pending intent сохраняется, пользователь получает отдельный маршрут проверки/подтверждения исходного источника; после проверки фактической task+operation metadata binding записывается атомарно. Если исходный источник доказать нельзя, автоматический retry недоступен; исходный Markdown/recovery сохраняются, новая задача молча не создаётся. Defaults и effective parents legacy intent не пересчитываются.

### 6.3 User-Observable Scenarios

| Scenario / пункты | Действие | Видимый результат | Evidence / AC |
| --- | --- | --- | --- |
| S1 /1–3 | Collapse, изменить дату, hover | Chevron слева, требуемый формат, полный путь | UI/header screenshot; AC1–3 |
| S2 /4,8 | Открыть3 ссылки, повторить1, прокрутить/вернуться |3 doc tabs без дубликата, доступный низ, сохранённая лента | native route+disk checks; AC4,8 |
| S3 /5 | ПКМ/Menu на single/multi selection и в TextBox | Нативный список действий с правильной целью | UI/menu keyboard; AC5 |
| S4 /6 | Читать ссылки с переносами | Текст в исходном порядке и на правильных строках | fixture/layout screenshots; AC6 |
| S5 /7 |30 раз войти в edit, печатать/перейти | Нет viewport jump и задержки выше budget | trace+UI coords/video; AC7 |
| S6 /9–11 | Выбор в двух днях, Ctrl/Shift, повторный клик, drag | Единый выбор/clear; drag выбранного набора работает | native input+virtualization; AC9–11 |
| S7 /12 | «Настройки области» из heading/chip | Выбрана нужная область, draft protected | UI; AC12 |
| S8 /13 | Задать roots, создать задачу, изменить родителей | Parents предзаполнены, override сохранён, links реальны | UI+graph+recovery; AC13 |
| S9 /safety | Ошибка сохранения, rename, смена space | Не теряется draft, нет чужого документа/родителя | negative UI+disk checks; AC14 |

### 6.4 State / Interaction Matrix

| Состояние | Trigger | Результат | Ошибка / особый случай |
| --- | --- | --- | --- |
| Chronology | Внутренняя ссылка | Activate/create doc tab | Missing/unsafe: сообщение без пустого файла |
| Dirty doc | Close/space switch | Commit then transition | Failure: остаётся активным, recovery сохранён |
| Selection | ПКМ на его member | Набор не меняется | Busy: disabled mutations |
| Selection | Click-release selected handle | Global clear | Drag threshold reached: не clear |
| Filtered/collapsed days | Выбор/диапазон | Только доступные targets | Hidden selection централизованно снимается |
| Draft area properties | Открыть другую область | Save/Discard/Cancel | Cancel сохраняет исходное окно |
| Default parents | Пользователь правит parent picker | Explicit override | Area change больше не перетирает |
| Conversion partial failure | Retry | Resume recorded intent | Не новая задача и не новая случайная root |

### 6.5 Decision Ledger

| Решение | Owner | Предложение | Уверенность | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Display date | user request/agent | App-wide custom format, отдельно от filenames |0.97| Непонятная настройка | Нет, включено в approval |
| Tab scope | agent | Только документы ленты, pinned chronology, no float/split |0.94| Перегруженная оболочка | Нет, включено в approval |
| Root при нескольких областях | agent | Distinct parents всех выбранных областей, explicit override |0.88| Неожиданные лишние родители | Нет, видны до submit; явно сообщить пользователю |
| Shared vault | agent | Mapping в task space, не в portable area catalog |0.97| Чужие task IDs | Нет |
| Cross-day selection | agent | Общий selection; multi-file mutations disabled |0.88| Ожидание bulk conversion across files | Нет, явно сообщить пользователю |
| Eremex | user request/agent | Desktop adapter + native tabs на остальных targets |0.86| Licensing/platform integration | Нет для локального EXEC; gate проверки перед интеграцией |
| Публичный README/reference | user | Отдельное разрешение публикации, если нужно сервису |1.0| Неавторизованный push | До внешнего этапа, не до локального EXEC |
| Получение лицензии | vendor | Только реальный положительный ответ/ключ и runtime check |1.0| Отказ/новые условия | External gate, не обещание выдачи |

### 6.6 Runtime / Config / Data Contract Matrix

| Контракт | Source of truth | Изменение | Совместимость | Проверка |
| --- | --- | --- | --- | --- |
| DisplayDateFormat | Общие app UI settings | Optional string | Missing→default, filenames untouched | restart/culture/invalid |
| AreaTaskDefaultsByVault | Space settings / VaultIdentity / AreaId | Optional nested mapping | Missing→no parent, unknown fields preserved | spaces A/B/shared vault; vaults A→B→A/one space |
| Documents | Existing vault store + unified session | UI tab list, transient state | Markdown format unchanged | two views/one save owner |
| Selection | Active workspace coordinator | Transient locators/revisions | Не хранится в Markdown | virtualize/clear/filter |
| Parent creation | Existing conversion journal/task graph | Record effective parents + TaskSourceIdentity before writes | Legacy без source: explicit verified binding, no automatic replay/default recompute | fail/restart/wrong-space retry/legacy |
| Eremex | Pinned desktop package + разрешённая лицензия | Новый adapter | Mobile graph clean, no trial accepted | restore/build/license smoke |

## 7. Бизнес-правила / Алгоритмы

1. Один document identity → одна save/undo session; разные вкладки не создают competing writers.
2. Plain handle click и drag различаются до выполнения global-clear.
3. Root defaults — предложение родителей, а не автоматический перенос существующих задач.
4. Не подтверждать conversion, пока task+parents не сохранены; retry использует зафиксированный intent.
5. UI operations используют текущие revision/space guards; позднее async completion не выбирает старую вкладку/область.

## 8. Точки интеграции и триггеры

Настройки → refresh date presenters; ContextRequested → existing block actions; link/search/files → document workspace; pointer/keyboard/filter/collapse → selection coordinator; area menu → area-management selected Id; conversion submit → parent resolver→journal→task graph. Vault/source changes dispose/rebind без фоновых подписок старого пространства.

## 9. Изменения модели данных / состояния

Новые persisted settings перечислены6.6; Domain.TaskItem не расширяется. AreaDefinition hierarchy не меняется. Tab order/active/scroll — session state; recovery text хранится только существующим механизмом. Нельзя сериализовать vendor controls/objects в domain configuration.

## 10. Миграция / Rollout / Rollback

Missing settings сохраняют прежние parent defaults и применяют новый display default. Никаких массовых rewrite Markdown. Area mapping добавляется атомарно с сохранением unknown JSON. Для shared vault mappings изолированы поspace/vault. Rollback: откат кода, display setting игнорируется старой версией, mappings остаются неактивными; note/tasks не удаляются. Новый source-bound journal использует версионированный namespace, который старая версия не перечисляет; при rollback незавершённые новые операции остаются в recovery до возврата новой версии, без запуска unsafe старого replay. Legacy journal сохраняется с исходным содержимым до verified binding; новая версия не добавляет roots задним числом. Eremex отделён adapter boundary и не меняет file format.

## 11. Тестирование и критерии приёмки

### Acceptance-to-Test Matrix

| AC / пункт | Автоматическая проверка | Native / visual evidence |
| --- | --- | --- |
| AC1 /1 | Chevron.X < Date.X, keyboard toggle | Header light/dark520/1000/1400DIP |
| AC2 /2 | exact ru date, invalid, culture, restart, filename hash unchanged | Settings preview/apply |
| AC3 /3 | Resolved absolute file path tooltip | Hover/focus реальной даты |
| AC4 /4 | Last line reachable in doc viewport, editing at bottom | Wheel/scrollbar/PageDown/End |
| AC5 /5 | Нет hover toolbar; Menu/Shift+F10/ПКМ; target and CanExecute | Стандартное меню, separators, TextBox actions |
| AC6 /6 | Inline ordering/break geometry, LF/CRLF, source unchanged | Ссылки перед/после newline и на узкой строке |
| AC7 /7 | Bounds/scroll delta≤1DIP, keyboard regression |30 warm latency samples + cold, budgets6.2 |
| AC8 /8 | Dedup/caret/scroll/close-error/rename/source, one session per file |3 tabs, anchored link, overflow; actual license smoke |
| AC9 /9 | Gutter20DIP; idle/hover/focus/area icon; target not over text | Hover and multiselect in both themes |
| AC10 /10 | Ctrl/Shift across loaded/unloaded days; filter/collapse clears hidden | Реальный выбор двух дней и reset |
| AC11 /11 | Selected click release clears all; drag preserves selection | Mouse no modifier vs drag |
| AC12 /12 | AreaId selection, multiple area submenu, dirty draft Cancel | Existing area management window |
| AC13 /13 | roots0/1/N, override, shared vault2 spaces, invalid root, journal retry | Task parents picker и сохранённые графовые связи |
| AC14 /safety | stale writer/late callback/recovery/space switch, no unwanted writes | Failure route сохраняет текст и фокус |

- AC5/10 exact selection: выбрать blocks1 и3 (также при скрытом2), скопировать/отформатировать/переместить кнопкой или DnD — текст block2 неизменен, выбранные перемещаются в исходном порядке; только range-команды преобразования disabled без расширения. Соседний диапазон сохраняет прежнюю доступность. Сохранить regression `Move_NonContiguousBlocksAcrossAreas_PreservesSourceOrderAndRawText`.
- AC8 thematic commands: в200-строчной тематической вкладке создать задачу, выделить заметку и назначить область; проверять путь сохранённого источника, parents, focus и scroll после каждого действия, не наличие `FeedDay`.
- AC13 isolation: один space, vaults A→B→A с одинаковым AreaId и разными roots; два spaces, общий vault и совпадающие TaskId/ParentId. Crash в A→restart в B: journal recovery не меняет task stores/Markdown; возврат в A продолжает ровно один intent. Проверить также смену storage binding с прежним SourceId и legacy intent без source.
- AC14 rollback: новая pending операция не видна старому journal enumerator; возврат новой версии подхватывает её без дубля, исходный Markdown остаётся до commit.
- Vendor preflight: opt-out наследуется процессом локальной/CI-сборки до запуска vendor targets; принимаемый результат не содержит отправки build telemetry по умолчанию.

- Baseline fixtures:800 будних дней,1–200 строк, минимум3 тематических документа по200 строк; смешанные заголовки/ссылки/code/checklists. Никаких абсолютных user paths в публичных evidence.
- До исправлений — воспроизводящие assertions/characterization video для6,7; после — matching scenario video. Native capture через existing FlaUI/record-app-screen skill; при объективном отказе recorder — точный лог команды, screenshots+UI assertions, не «native PASS» по headless.
- Команды после SDK/workload preflight: `dotnet build src/Unlimotion.sln --nologo`; TUnit через `dotnet run --project src/Unlimotion.Test -- --treenode-filter "/*/*/<AffectedClass>/*" --minimum-expected-tests 1 --maximum-parallel-tests 1 --output Detailed`; полныеmain/Headless/FlaUI перед финальной приёмкой. Package/mobile checks отдельно, без неявной установки workloads.
- Performance compare на одинаковых hardware/DPI/build/config и одной synthetic базе; profiler отдельно от latency smoke. Layout glyph delta измеряется вDIP, screenshots учитывают physical pixels.
- Один воспроизводимый failure → исправление/повтор затронутого набора; системный blocker → evidence+next-best path, не бесконечные retries. Частичные повторы не переименовывают неуспешный full run вgreen.

## 12. Риски и edge cases

| Вероятное замечание пользователя | Почему | Предотвращение | Статус |
| --- | --- | --- | --- |
| «Опять слишком много окон/панелей» | Dock может превратиться в IDE | Только один ряд tabs, безfloat/split/hover toolbar | mitigated |
| «Дата изменила имена файлов» | Есть похожая настройка | Явно отдельные labels/store, hash/no rename test | mitigated |
| «Root не тот/лишний» | Несколько областей/shared vault | Space mapping, preview parents, explicit override | mitigated |
| «Выделил через дни, почему нельзя преобразовать всё?» | Selection шире существующих transactions | Явный disabled multi-file mutation, без скрытого partial success | Предложенный предел, показать до approval |
| «После tabs потерял draft» | Несколько editors одного файла | Single owner и close/save/revision gates | mitigated |
| «Eremex подключили, а лицензии нет/сломался Android» | Внешнее решение и неполная platform matrix | Desktop isolation, license stage, no claim until granted | external gate |

Rework checklist: все13 пунктов mapped; UX artifact внутриSPEC; причины6/7 не выдуманы; choices6.5 явные; evidence дляnegative paths предусмотрено. Native100% scaling и highDPI проверяются отдельно. Задержка сохранения не должна превращаться в потерюdraft при оптимизации.

## 13. План выполнения

1. После approval: characterization layout/input и лицензия/compatibility spike локально; минимальный интерактивный native prototype tab/header/context menu показать до широкого внедрения editor изменений.
2. Header/settings, selection coordinator/context menu и inline/layout fixes с регрессиями.
3. Document sessions/tabs/scroll и recovery lifecycle; Eremex desktop adapter, мобильный host без vendor зависимости.
4. Area navigation/root mapping→conversion parent policy→negative graph tests.
5. Full tests, реальный author route, measurements и independent review; внешнее лицензирование после необходимого разрешения публикации. До выдачи лицензии не закрывать весьscope.

## 14. Открытые вопросы

До локального EXEC блокирующих product-вопросов нет: предложенные решения6.5 входят в approval. До внешнего этапа остаются реальная выдача лицензии, разрешение публикации attribution/reference и возможность проверки сервиса на выбранной публичной ревизии. Если требуется изменить ограничения non-commercial или платформенную стратегию — отдельный user decision, не молчаливая замена поставщика.

## 15. Соответствие профилю

UI-thread I/O запрещён, IDs сохраняются/имеют migration map, unit+UI+native evidence запланированы. На SPEC tests не запускались: implementation не менялась. Creator-vibe повлиял на минимальную оболочку tabs, сохранение места ручки и отказ от hover-команд, а не на добавление декоративных функций.

## 16. Таблица изменений файлов

| Путь | Планируемое изменение | Причина |
| --- | --- | --- |
| `FeedControl.axaml/.cs`, `FeedControl.Reading.cs` | Header, date tooltip, docs host |1–4,8,12 |
| `FeedViewModel`, новые workspace/selection coordinators | Общие state owners |8,10,11 |
| `MarkdownBlockLivePreviewEditor*`, `MarkdownBlockPreviewControl` | ContextMenu, inline layout, glyph alignment, handle |5–7,9–11 |
| `SettingsViewModel`, app/space settings, settings view | Date format + per-space roots |2,13 |
| `AreaManagement*`, conversion coordinator/journal adapter | Open-by-Id, root picker, parent policy |12,13 |
| Desktop adapter/project + central package props, README | Eremex reference и attribution послеapproval |8 |
| `src/Unlimotion.Test`, `tests/Unlimotion.UiTests.*` | Characterization/regression/author routes |AC1–14 |

## 17. Таблица соответствий (было → стало)

Правый collapse→левый; culture D→настраиваемыйdisplay; no path→tooltip; overlay→tabs+scroll; hover actions→ContextMenu; WrapPanel tokens→inline flow; независимые selections→координатор; широкий toggle→компактная ручка; area безtaskdefault→space-bound parent setting. Файловый формат заметок не меняется.

## 18. Альтернативы и компромиссы

- Только стандартный TabControl: проще/no license, но пользователь явно просит Eremex; не подменять без согласования. Оставлен для платформ вне заявленной поддержки поставщика.
- Полный dock workspace: лишние UI-состояния, не нужен для доступа к заметкам; исключён.
- RootTaskId в portable area catalog: проще, но неверен для одного vault с разными task spaces; выбранspace mapping.
- Незаметно выполнять команду только на одном дне при cross-day selection: небезопасно; лучше явный disabled unsupported operation.

## 19. Результат quality gate и review

### SPEC linter

| № | Статус | Проверяемое основание / остаток |
| --- | --- | --- |
| 1 | PASS | Все 13 замечаний связаны с outcome и S1–S9 |
| 2 | PARTIAL | AS-IS подтверждён исходниками; native причины лагов/переносов требуют characterization до исправления |
| 3 | PASS | Указаны раздельные владельцы состояния и ошибочный layout, причины задержки не выдуманы |
| 4 | PASS | Минимальная оболочка, предсказуемый выбор и сохранность текста |
| 5 | PASS | Non-Goals исключают полный dock, массовые переносы и изменения формата заметок |
| 6 | PASS | Таблица владельцев session/selection/settings/conversion/host |
| 7 | PASS | Общий document route, settings refresh, context commands и source guards |
| 8 | PASS | Click-release/drag, exact selection, parent override и source-bound recovery |
| 9 | PASS | Close failure, missing file/root, stale callback, legacy и wrong-source recovery |
| 10 | PASS | Fixture800, warm/cold budgets, одинаковая машина/DPI, trace+native evidence |
| 11 | PASS | Отдельные display settings, nested vault defaults, transient tabs, versioned journal |
| 12 | PASS | Без Markdown rewrite; legacy не получает текущий source; mobile dependency isolation |
| 13 | PASS | Новые pending intents изолированы от старого replay; возврат новой версии восстанавливает их |
| 14 | PASS | AC1–14 с точным ru-format, geometry, input, isolation и no-write assertions |
| 15 | PASS | AC→UI/unit/native; negative cases для несмежного выбора и чужого source |
| 16 | PARTIAL | Build/TUnit команды и stop rules заданы; реальные affected test names и доступность native recorder уточняются на EXEC, не считаются PASS сейчас |
| 17 | PASS | Поэтапный local prototype→implementation→full evidence, отдельный license gate |
| 18 | PASS | Ledger: tab scope, union roots, ограничения bulk, отдельная публикация |
| 19 | PASS | Expanded выбран из-за multi-module/state/config/vendor рисков |
| 20 | PASS | Desktop+UI profiles: UI coverage, before/after evidence, synthetic vault, no UI-thread I/O |

### Rubric

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель / границы | 5 | Полный mapping запроса и исключение лишних dock-функций |
| AS-IS | 2 | Код проверен, новые native baseline/trace ещё не выполнены |
| Конкретность дизайна | 5 | Wireframe, modifiers, commands, tabs lifecycle, source/vault contracts |
| Безопасность / миграция / rollback | 5 | Защита drafts, legacy/source mismatch, namespace rollback, opt-out |
| Проверяемость | 5 | Матрица AC с негативными fixtures и числовыми budgets |
| Автономность решений | 2 | Локальный этап определён; лицензия и возможная публикация требуют внешнего gate |

Итого 24/30. Оценка описывает готовность плана, не подтверждает реализацию/производительность/лицензию.

### Full post-SPEC review

- Scope reviewed: текущая SPEC целиком, 13 пунктов запроса, central QUEST/linter/rubric/review, desktop/UI profiles, локальный UI-test override; планируемые файлы перечислены в16.
- Scope/Evidence pass: проверены `FeedControl`, `FeedViewModel`, block editor VM/view/code-behind, preview inline builder, area catalog/management, task conversion journal/target, settings и central package props, `License.txt`; read-only vendor licensing/system requirements/telemetry. `git status --short` показывает только новую SPEC. Runtime-тесты этого изменения отсутствуют, потому что код не менялся.
- Contract pass: S1–S9 и AC1–14 сопоставлены со всеми замечаниями; оформление даты отделено от filename; клавиатурные правила и сохранение Markdown не меняются; UI coverage обязательно на EXEC.
- Adversarial risk pass: проверены counterexamples blocks1+3/скрытый2; crash в space A→retry в B; одинаковые AreaId разных vault; commands thematic без FeedDay; downgrade с pending journal; build telemetry до license stage. Эти случаи включены в контракты и AC, без утверждения runtime PASS.
- Reviewer: `/root/feed_workspace_spec_review`, отдельный role `independent-reviewer`, без правок файлов. Фактический sandbox `danger-full-access`, filesystem unrestricted, approval never — технически НЕ read-only. Это отдельная оценка, но не sandbox-isolated независимый аудит. Дополнительно основной агент выполнил отдельный adversarial fallback выше; residual risk: no-write обеспечено поведением reviewer, не техническим запретом.
- Fix and re-review: пять исходных findings внесены в6.2,6.6,10,11 и закрыты повторной проверкой reviewer. Повторный проход выявил регрессию ограничения несмежного DnD; она исправлена сохранением exact-set поведения. Финальный targeted pass reviewer по двум изменённым абзацам: PASS, остаточных HIGH/MEDIUM нет. Основной агент повторно сверил связанные AC и Non-Goals; проверка whitespace новой SPEC не нашла ошибок (Git сообщил только стандартную LF→CRLF нормализацию).

#### Role-Based Review Result

| Роль | Проверено | Результат |
| --- | --- | --- |
| Business analyst / domain | Multi-area roots, manual override, shared vault/task sources | Union виден до submit; старые задачи не меняются; source mismatch закрыт контрактом |
| UX / designer | Wireframe, narrow tabs, native menu, selection/drag, focus | Один ряд вкладок без полного dock; ограничения range-команд нужно явно сообщить до approval |
| Tester / validation | Все 13 пунктов, AC1–14,800-note fixture, negative recovery | План достаточен; native/perf PASS не заявлены, полные наборы остаются EXEC |
| Developer / architect | Single session, centralized selection, settings namespace, journal | Устранены min..max и implicit current-source assumptions |
| Delivery / operations / security | MIT/vendor requirements, mobile graph, telemetry, secrets/publication | Opt-out до vendor targets; license/publication отдельный этап, не скрытый push |

#### Findings и исправления

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | selection | min..max захватывает невыбранные blocks | Exact-set либо contiguous-only; regression1+3/hidden2 | fixed, re-reviewed |
| HIGH | recovery | Vault-only journal может повторить операцию в чужом task source | Persist source binding до writes; mismatch/legacy fail-closed; rollback namespace | fixed, re-reviewed |
| MEDIUM | documents | Existing commands требуют editor из Days | Generalized document action context и thematic AC | fixed, re-reviewed |
| MEDIUM | settings | Пример map терял VaultIdentity | Explicit nested map и vault A→B→A test | fixed, re-reviewed |
| MEDIUM | vendor/privacy | Локальная сборка отправляет telemetry | Opt-out до restore/build и CI preflight | fixed, re-reviewed |
| MEDIUM | DnD regression | Первый fix ошибочно запрещал уже поддержанное несмежное перемещение | Сохранить exact-set DnD одного документа, ограничить только range conversions | fixed, re-reviewed |

#### Depth checklist / Stop decision

- Scope drift/unrelated changes: только текущая SPEC; новых product-окон и task-model fields нет.
- AC/scenarios/ledger/objections: заполнены6.3–6.5,11,12; cross-day/range limitation и union parents вынести в сообщение пользователю.
- Validation evidence: source inspection + vendor docs достаточны для SPEC; для EXEC нужны tests/native/measurements, старые green runs не засчитываются.
- Unsupported claims: license ещё не получена, jitter cause не измерена, mobile vendor support не обещана.
- Regression/edge cases: selection exactness, save/recovery, source/vault isolation, legacy/downgrade перечислены и тестируемы.
- Docs/changelog: SPEC сейчас; README attribution после approval; publication отдельно. Changelog итогового UX после реализации по профилю.
- Hidden contract changes: новая политика parent defaults, табы и ограничения bulk перечислены явно.
- Manual-review challenge: атаковали совпадающими IDs разных источников и несмежным выбором; дополнительно на EXEC проверить real TextBox native context menu, focus after close failure и фактические package transitive dependencies.
- No-findings justification: неприменимо — пять findings обнаружены и исправлены в плане.
- Stop decision: PASS / ГОТОВО к запросу approval локального EXEC. Открытых BLOCKER/HIGH/MEDIUM нет. Остаточные PARTIAL: native baseline и конкретные новые test names проверяются на EXEC; внешняя выдача лицензии/публикация остаются отдельным gate. Post-EXEC не выполнен; реализации и выданной лицензии нет.

## Approval

Получено «Всё ок. Спеку подтверждаю» 2026-09-09. Фаза EXEC. Это не разрешение push/release или системной установки лицензирования.

## 20. Журнал действий агента

| Фаза | Намерение | Уверенность | Недостающие данные | Следующее действие | Нужен человек | Обращение/решение | Объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Разобрать13 замечаний |0.97| Native baseline6/7 | Code inspection и контракт | Нет | Получен список | UI/данные не меняются доapproval | ТолькоSPEC |
| SPEC | Проверить Eremex |0.95| Реальный ответ сервиса послеreference/attribution | Зафиксировать staged license gate | На внешнем этапе | Сообщены требования без claim выдачи | MIT+Avalonia12 подтверждены, mobile не обещан | SPEC, read-only vendor sources |
| SPEC | Сформировать дизайн иnegative paths |0.92| Independent review | Review/rework | Послеreview approval | Ещё не запрошено | Общие owners, минимальныеtabs, no data loss | ТекущаяSPEC |
| SPEC | Закрыть пять review findings |0.96| Повторный reviewer pass | Проверить исправленные контракты | Нет | Исправлены exact selection, source recovery, thematic route, vault map, telemetry | Только SPEC, без code/vendor writes | Разделы6,10,11,19 |
| SPEC | Завершить quality gate |0.96| Approval и будущие native measurements | Запросить «Спеку подтверждаю» | Да | Re-review PASS, включая сохранение несмежного DnD | Linter без critical FAIL, rubric24/30; реализация не заявлена | Раздел19; только новая SPEC |
| EXEC | Начать утверждённую реализацию |0.96| Новые regression results/native evidence | TDD, settings/render/documents/selection/roots | Нет | Approval получен | Main owns Feed VM/view/editor integration; workers own date settings, inline preview, area/journal; builds serial | Normal test build работает, первый date UI red подтверждён |
| EXEC | Реализовать date/roots/inline/menu/tabs |0.91| Полные suites и final native evidence | Последовательная валидация | Нет | Изменения в рабочем дереве, без commit/push | Eremex1.4.38 только desktop; portable host общий | Targeted результаты ниже |
| EXEC | Проверить все виды блоков |0.98| Native latency | Исправлены typography и hit-testing | Нет | H1 −16.633DIP и quote −2DIP воспроизведены, после исправления все6 baseline0DIP | Измерено в Headless, не в native | FeedEditorInteractionPerformanceUiTests7/7 |
| EXEC | Проверить безопасность вкладок |0.95| Полный regression suite | Исправлены coalesced save и caret restore | Нет | Mouse tab switch ждёт единый delayed save; A→B→A сохраняет текстовое выделение | Save error удерживает tab/vault | FeedDocumentWorkspaceUiTests3/3 |
| EXEC | Проверить native чтение |0.93| Final rebuild/video и расширенные сценарии | Живое окно с синтетической базой | Нет | Проверены ПКМ, настоящая document tab, scroll до пункта200 | Первичная capture показала другую поверхность; после activation и свежей capture app/тестовый путь проверены, ввод в чужие приложения не выполнялся | Computer Use, изображения в чате |
| EXEC | Проверить лицензию |0.99| Ответ внешнего сервиса/возможная публикация reference | Не заявлять выдачу | Внешний gate | In-app браузер: ERR_TIMED_OUT страницы open-source-licensing; формы не отправлялись | Автогенерация emxLicense.cs не означает выданную OSS-лицензию; файл только local exclude | README local attribution, opt-out CI env |

### Промежуточные результаты EXEC (не окончательный PASS)

- Обычная сборка desktop и отдельного Debian desktop-проекта: PASS. Mobile не получает Eremex dependency; mobile/runtime проверки ещё не заявлены.
- Targeted: display-date14; settingsUI1; loaded-dateUI1; root settings10; parent contracts5; legacy binding3; area UI5; task conversion/quick capture/storage target25; inline8; Eremex lifecycle1; workspace3; context menu5 — соответствующие последние запуски PASS. Полные suites ещё в работе, эти числа их не заменяют.
- Eremex lifecycle объединяет сценарии в одном dispatcher: vendor static SVG cache не поддерживает повторные независимые Headless UI sessions в одном процессе. Проверены mouse activation, сохранение viewport, failed-save close/switch, no-float, список8 вкладок при360px, light/dark. Это тестовый host, не полная фотография приложения.
- Headless800 weekday notes1–200lines: init9207.89ms, first401.45ms, warm p50=66.23/p95=95.30/max101.71ms; 30 samples. Cold budget250ms НЕ подтверждён, native latency ещё не измерена. Не называть производительность10/10 по этому запуску.
- Физическое размещение настроек root task уточнено: отдельный JSON section `TaskSourceAreaDefaults`, keyed source/vault/area. Это сохраняет логический scope SPEC и не допускает перезаписи mapping старым cached `TaskSourceManager` settings snapshot. В переносимый areas.json настройки задач не добавляются.
- Локальные артефакты `output/` и автогенерируемый desktop `emxLicense.cs` исключены через существующий repo-local `.git/info/exclude`; пользовательские файлы не удалялись.
- Независимый post-EXEC reviewer создать не удалось: `agent thread limit reached`. Другой исполнитель выполнил read-only adversarial cross-review не своих файлов; effective sandbox writable, это НЕ технически изолированный аудит. Main отдельно проверяет оставшуюся поверхность и фиксирует residual risk. Full post-EXEC PASS пока не поставлен.
- Cross-review corrections: active TextBox move-to-area commit wrapper; возвращение каретки/выделения; coalesced LostFocus+tab save; background selection clear; portable duplicate filename disambiguation; visible-only daily-anchor target. Finding о selection после полного refresh снят reviewer: `ApplySnapshot → ApplyFeedAreaFilter → Clear` уже защищает путь.

### Дополнительный post-EXEC проход (валидация продолжается)

- Scope/evidence: основной агент проверил новые date/area settings stores, parent-draft adapter, source identity factory, v3 journal/recovery и graph-parent writes; общие tab/session/selection contracts, native context-menu routing и документацию README/README.RU. Проверка XML ресурсов: дубликатов нет, новые ключи имеют русский перевод.
- Contract/adversarial: найден незавершённый rename/delete lifecycle; добавлен явный маршрут существующих watcher signals к открытым вкладкам, dirty conflict не меняет commit path до решения. Удалённый файл сохраняется в recovery; изменения имени обновляют заголовки без создания новой вкладки. Проверки `FeedDocumentExternalChangesUiTests` ещё должны пройти на новой сборке.
- Дополнительные исправления: позднее чтение ссылки отменяется при закрытии документа; старые UI expectations обновлены под18DIP и daily document tabs. Первый полный прогон идёт по предыдущей сборке; его три обнаруженных падения не объявляются зелёными на основании исправления исходников.
- Новое coverage: background-click очищает выбор разных дней; portable tab names различаются полным относительным путём; caret/selection сохраняется при vault A→B→A с проверкой revision; отдельный200-line thematic route выполняет Task/Note/Area без строки в `Days`. Очистка review highlight распространена на тематические editors.
- Native evidence: `output/feed-documents-2026-09-09/native-after.mp4`,45s,20fps,2880×1498. Просмотрены кадры35s и44s: подтверждают переход в ленту и стандартное контекстное меню. Открытие вкладки и прокрутка до200 проверены отдельными действиями Computer Use, но в этот45s ролик не вошли. Видео «до» всей фичи не записано; fallback — исходные baseline screenshots и числовой layout test до/после, это не эквивалент паре native videos.
- Машина для локальных проверок: AMD Ryzen5 3500X,6 logical processors, .NET10.0.400; Headless scale1 и viewport1200×800 указаны в timing log. Первый click в прогретом полном suite69.84ms, warm p50=34.17/p95=53.59/max66.50ms. Это не заменяет изолированное401.45ms измерение и не доказывает native cold budget.
- Внешняя лицензия: read-only web fetch официальной страницы успешен; интерактивная вкладка остаётся страницей ERR_TIMED_OUT. Форма/заявка не отправлялась, ключ не получен, attribution/reference существуют только локально. Нужен отдельный публичный delivery gate, если сервис проверяет только опубликованное состояние.
- Stop decision пока **NEEDS-VALIDATION**, не PASS: завершить свежие targeted/full suites, проверить новые rename hooks; отдельно остаются actual license smoke и native latency. Не считать эту запись завершённым post-EXEC quality gate.

### Исправления по полному прогону и повторному adversarial review

- Первый полный прогон обнаружил 11 падений: три уже описанных, семь устаревших проверок hover-toolbar и реальную разницу высоты блока 2DIP при редактировании. Семь проверок адаптированы под context menu с сохранением atomic checklist/exact selection, запрета технических операций, narrow-window и modal-overlay контрактов; проверки не удалены. Причина высоты — пустой status Grid с постоянным верхним отступом2. Grid теперь видим только при ошибке/сохранении; допуск теста≤1DIP не ослаблен. Новые исходники ещё должны пройти свежий прогон.
- Rename-overwrite: при открытых A/B вытесненная чистая версия B сохраняется в recovery до объединения вкладок. Dirty target удерживает alias A в blocked состоянии до явного разрешения; два dirty редактора используют существующее окно конфликтов последовательно с обновлением disk revision второго. Повторные проверки после awaited persistence защищают появившийся draft. Всего11 lifecycle cases подготовлены, в том числе три overwrite collision; активный target отдельно runtime ещё не проверен.
- Cross-review другим исполнителем подтвердил по коду закрытие collision и stale conflict callback findings; новых подтверждённых HIGH/MEDIUM в этой поверхности не найдено. Это не выполнение тестов и не sandbox-isolated audit. Pending Eremex metadata observer остаётся отдельной незавершённой частью до red/green.
- Подготовлен `FeedEditorPerformanceFlaUiTests`: fresh native process,800 generated notes1–200lines, first+30warm samples input injection→observed UIA keyboard focus. Включены overhead injection/polling; это не timestamp первого нарисованного кадра/каретки. Budget booleans печатаются отдельно от успешности функционального теста. Запуск ещё не выполнен.
- Проверено исполнением: external lifecycle11/11, workspace4/4, thematic actions1/1, inline8/8, пять случаев трёх исправленных FeedControl методов — PASS; точная высота редактора тоже PASS. Eremex rename header получил настоящий red (старый заголовок) и green1/1 после observer, hook повторно просмотрен другим исполнителем.
- Background-clear: первоначальная дополнительная проверка кликала за пределами окна. Координата исправлена и проверяется `ClientSize.Contains`. Повторный прогон с исправленной координатой проходит и без дополнительного root Background; лишний Background удалён. Не представлять этот случай как воспроизведённый production bug.
- Проверка delivery graph выявила пропущенные Eremex references в реальном macOS packaging target `Unlimotion.Desktop.ForMacBuild.csproj`, используемом `ci/osx/generate-osx-publish.sh`. Добавлены обе desktop-only references; отдельная компиляция включена в финальную валидацию. Runtime macOS не заявлен. Текущие desktop CI publish scripts не включают NativeAOT/PublishTrimmed; прежний TrimMode=copyused сам по себе не доказывает trimmed publish. Проверка нового AOT/trimmed режима не выполнялась.
- Обновлённый context suite7/7 PASS. Editor suite33/33 PASS после исправления fixtures. Последний modal failure в полном классе диагностирован до/после `CaptureRenderedFrame`: при одинаковой геометрии hit target менялся с TextBlock подложки (ancestor MarkdownBlockPreviewControl) на Border диалога. В изолированном запуске уже был Border. Сохранено явное обновление headless frame и строгая проверка cover hit перед ПКМ; временные Console diagnostics убраны. Это исправление тестового rendered-hit-state, не доказанный дефект native overlay. Лог `feed-editor-modal-diagnostic-all.log` подтверждает33/33, исходники без диагностического вывода ещё попадут в финальный полный прогон.
- Обычные desktop/Debian/macOS project builds выполнены последовательно (обычный Desktop последним из-за общей папки bin/obj): все exit0,37warnings/0errors. Для Desktop все37 предупреждений — вывод Git о будущей нормализации LF→CRLF в рабочем дереве; других `warning:` в его журнале не найдено. macOS runtime и package installation на Linux не выполнялись.
- Первый полный отдельный Headless run:51total/50PASS/1FAIL/0skip,5m12.510s, exit2. `CliLiveRefreshHeadlessTests.ExternalWriterCreateRelationAndDelete_RefreshOpenDesktopProjection` успел подтвердить storage graph и contains-tree, но не parents-tree. Найдена введённая UI automation regression: новые draft `TaskRelationsControl` имели custom button prefix, но скрытые trees сохраняли `CurrentItemParentsTree`, перекрывая lookup реальной task card. Prefix-scoped tree IDs и два UI regression tests подготовлены; стандартный task-card ID и явные overrides сохраняются. Старый failed full run не считается зелёным до свежего полного повтора.

### Проверка baseline и масштаба native стенда

- Scoped automation IDs: новые2/2 и прежние TaskRelationsControl2/2 PASS. Однако CLI timeout сохранился. В отдельном чистом detached worktree на исходном `77b253f65cbecc46cbd921a0d3e75bedbe9da2e0` тот же неизменённый тест также FAIL на line162,24.816s. Это подтверждает старый сбой, но не устанавливает его причину и не делает текущий full suite зелёным. Лог `C:/Users/Kibnet/AppData/Local/Temp/feed-headless-baseline-77b253f6-f0b7674e.log`.
- Последующая диагностика показала корректные current task/parent IDs, но detached parents tree. Headless fixture не вызывал `Window.Show`, а построение parents root зависит от attachment. Исправление fixture проверяется отдельно; production storage/graph ради этого теста не меняется. Временный diagnostic helper сам вызвал nested-dispatch зависание; тот synthetic run остановлен, результатом тестов не считается.
- Первые native Reading3/3 функционально прошли, но ручной просмотр выявил ошибку единиц стенда: `MoveWindow` получил физические пиксели вместо DIP. Исправлен только fixture; системные настройки экрана не менялись. Фактически `GetDpiForWindow=216`, масштаб225%. Повторный Reading3/3 PASS1m01.499s для520/1000/1400DIP, evidence `output/feed-documents-2026-09-09/final-native-dip/`; основной агент просмотрел top520 и1400. На узком окне дата/служебные данные переносятся без обрезания, поиск доступен. Числа ширины задают decorated HWND, client немного уже.
- Native timing: первоначальный запуск завершился timeout поиска preview без единого замера. Следующий obsolete fixture с требованием полной видимости200-line paragraph отменён после Reading по решению main; исправлен на видимое пересечение. Ещё один запуск обнаружил недоступное UIA AutomationId у чужих элементов; чтение заменено на `ValueOrDefault`. Эти ошибки стенда не являются измерениями задержки редактора. До успешного fresh run native budget остаётся НЕ подтверждённым.

### Заключительный review: контракт и границы приёмки

- Scope pass основного агента: date presentation/copy path, portable/vendor tab lifecycle, document session save/close/restore, global selection, context-menu mutation guards, typography/status layout, area/source isolation. Дополнительно сверены desktop packaging references и telemetry opt-out, diff не публиковался. Артефакты остаются локальными.
- Contract pass: обнаружены недостающие строгие утверждения AC4/8/10 (последняя строка, scroll/focus после thematic actions, selection при скрытии дней); дополнение coverage поручено date reviewer. Найден малый дефект error path: clipboard exception заменял date tooltip, скрывая полный путь; исправление не должно менять саму привязку пути.
- Adversarial pass: проверены stale session после await, rename/overwrite двух dirty tabs, source mismatch/legacy journal, выбор несмежных блоков, menu над активным TextBox, нативный capture при225%DPI. Ограничение остаётся: уже начавшийся graph call может завершить запись в ранее захваченный OLD repository при смене пространства, но не перенаправляется в NEW; последующие guards останавливают продолжение и оставляют recovery.
- Role review: BA — область задаёт видимые parent defaults, ручной выбор сохраняется; UX — один document viewport и обычное меню без дополнительного докинга; developer — единые session/selection owners и fail-closed source identity; tester — отдельные targeted/full/native evidence и неослабленные assertions; delivery/security — лицензия не выдана, публикация и системная установка не выполнялись. Новых подтверждённых HIGH/MEDIUM в просмотренном roots/source code не найдено; это bounded read-only cross-review исполнителей, не sandbox-isolated аудит.
- Fix/re-review: duplicate tree IDs закрыты2новыми+2прежними UI tests; CLI fixture Show исправлен с сохранением исходных assertions,2/2 PASS17.335s (`feed-headless-cli-shown.log`). Временный observer code удалён, понадобится короткий rerun после rebuild; full51 запущен с тем же Show и ещё присутствующими failure-only diagnostics. Нельзя выдавать его за сборку после удаления диагностики.
- Stop decision остаётся **NEEDS-VALIDATION**: full suites и новые coverage ещё идут, native painted-caret budget не доказан, фактическая OSS license требует внешнего этапа. Финальный PASS до закрытия этих границ не разрешён.

### Native long-block regression, обнаруженная при измерении

- Обновлённый Headless full:51/51 PASS5m19.927s на бинарнике с failure-only диагностикой; после её удаления отдельный rebuild CLI2/2 PASS17.404s. Новые date UI2, workspace4 и global selection regression3 PASS. Thematic focus assertion сначала выбрал пустой блок: fixture исправлен на реально видимый редактируемый paragraph, contract не ослаблен.
- Реальные первые native input→UIA focus измерения:323.12ms,290.42ms,361.38ms; в последующем full-native baseline329.73ms. Все выше250ms; не скрывать их за более быстрым будущим запуском. Полной warm-серии пока нет.
- После Escape из200-line paragraph повторный click timeout оказался связан с уходом viewport к более старым дням. На просмотренном screenshot после DIP6 видны20/19августа вместо9сентября. В full-native повторе исходный text peer имеетY−1470 иH6329physicalpx; его точка клика больше не соответствует исходному месту. Нельзя обходить этот сбой принудительным scroll-to-top внутри benchmark.
- В локальной Avalonia12.0.3 проверено `ScrollViewer.OnGotFocus`: он вызывает `control.BringIntoView()` для всего focused control. Кандидат причины — фокус oversized preview после Escape; проверка усиливается реальным Escape, ограничением смещения≤1DIP и кликом только внутри client viewport. Это отличается от прежнего Headless benchmark, который вызывал VM.CancelActiveEdit напрямую, не проходя preview-focus маршрут.
