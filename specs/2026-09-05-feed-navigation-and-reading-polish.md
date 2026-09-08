# Лента: навигация к сегодняшнему дню и спокойное чтение

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`.
- Владелец: пользователь — продуктовые решения и approval; агент — проектирование, интерактивный макет, реализация и доказательства.
- Масштаб: medium; локальные UI/state изменения без изменения формата Markdown или модели задач.
- Целевое семейство / behavior baseline: central `model-behavior-baseline`; качество результата проверяется по UI и тестам, а не по модели агента.
- Поверхность: Codex, локальная Windows/PowerShell.
- Effective runtime: текущая локальная Codex-сессия; точный model ID не влияет на контракт Avalonia-приложения.
- Eval baseline / evidence: `feat/daily-feed` на `dfb0377d`; предыдущий post-EXEC подтвердил 750–800 daily-файлов по 1–200 строк и зафиксировал оставшиеся UX-наблюдения в local-only `output/feed-reliability/author-report.md`.
- Целевой релиз / ветка: текущая `feat/daily-feed`; commit/push/PR/release не входят в эту SPEC без отдельной просьбы.
- Ограничения: до фразы «Спеку подтверждаю» изменяется только эта SPEC. Интерактивный макет создаётся первым EXEC-артефактом и отдельно показывается пользователю до production-кода.
- Instruction stack: central AGENTS → creator-vibe-lens + `creator-vibe`; model/tool/collaboration/testing baselines; QUEST governance/mode; `testing-dotnet`; `dotnet-desktop-client`; `ui-automation-testing`; spec linter/rubric/review loops; локальный AGENTS override.
- Связанные ссылки: `specs/2026-09-04-feed-reliability-and-polish.md`; local-only `output/feed-reliability/author-report.md`.

## 1. Overview / Цель
Сделать ленту спокойнее при ежедневном использовании: после глубокой прокрутки быстро вернуться к сегодняшнему дню, видеть только относящийся к текущему режиму фильтр, не читать служебный YAML как основной контент и не сталкиваться с повторяющимися заголовками приложения и заметки.

Outcome contract:
- Success means: пять улучшений ниже уменьшают визуальный шум и число действий, не скрывая пользовательские данные и не меняя Markdown на диске.
- Итоговый артефакт / output: сначала local-only интерактивный HTML-макет с ключевыми состояниями, затем — только после его согласования — код, UI-регрессии, before/after evidence и post-EXEC review.
- Stop rules: SPEC заканчивается запросом точной approval-фразы. EXEC сначала останавливается на согласовании интерактивного макета; production-код не начинается, пока пользователь не подтвердит макет. Любой новый persisted формат, настройка ширины, группировка поиска или AI-функция требуют отдельного scope.

## 2. Текущее состояние (AS-IS)
- `FeedControl.axaml` всегда показывает локальный фильтр областей, хотя в режиме поиска ниже появляется отдельный `FeedSearchFiltersControl`; пользователь может видеть два похожих выбора области.
- После глубокой прокрутки нет явной команды вернуться к сегодняшнему дню. `Ctrl+Home` принадлежит текстовой навигации и не является безопасным app-level shortcut.
- `MarkdownBlockKind.FrontMatter` рендерится как raw fallback. Служебные `unlimotion-id` и `areas` выглядят как обычный код и занимают место.
- В тематическом файле оболочка показывает `DisplayName` и `RelativePath`, после чего Markdown снова может начинаться тем же H1. Автоматически скрывать пользовательский H1 небезопасно.
- На широком окне строка редактора растягивается почти на всю ширину, из-за чего длинные записи тяжелее читать.
- Уже реализованные виртуализация, scroll pagination, поиск, разбор, DnD, editor focus и recovery являются инвариантами; эта работа не должна их перестраивать.

## 3. Проблема
После устранения функциональных дефектов лента остаётся визуально и навигационно тяжелее, чем должна быть: контекстные действия дублируются, служебные данные конкурируют с содержанием, а глубокая хронология не даёт быстрого и безопасного возврата в настоящее.

## 4. Цели дизайна
- Основное содержимое — пользовательский Markdown; служебная оболочка должна быть тише.
- Безопасный путь должен быть коротким: возврат к сегодняшнему дню — одно действие без потери активной правки.
- В каждый момент виден ровно один относящийся к текущему контексту фильтр областей.
- Никакое визуальное упрощение не удаляет и не переписывает пользовательский Markdown.
- На узком окне действия остаются доступны без перекрытий; на широком — текст остаётся читаемым.
- Изменения используют существующие VM, parser, editor и automation-id patterns.

## 5. Non-Goals (чего НЕ делаем)
- Не группируем результаты поиска по файлам и не добавляем «Вернуться к результатам» — это отдельный следующий этап.
- Не вводим bounded cache для search/review index: текущий масштаб уже принят, оптимизация возможна только после измерений.
- Не добавляем настраиваемую пользователем ширину строки, новые settings, новый persisted state или изменения Markdown/frontmatter.
- Не скрываем произвольный пользовательский YAML и не удаляем/перемещаем H1 из документа.
- Не вводим command palette, новую навигационную панель, отдельное окно, AI-классификацию или inbox.
- Не меняем capture/review/task conversion, модель областей, формат имени daily-файла или task storage.
- Не выполняем commit/push/PR/release в рамках подтверждения этой SPEC.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `FeedControl.axaml/.cs`: floating-команда «Сегодня», вычисляемая видимость по viewport, app-local hotkey, контекстная видимость локального фильтра.
- `FeedViewModel`: безопасная подготовка навигации — commit активного редактора, доступ к сегодняшнему дню, понятная ошибка без смены viewport.
- `MarkdownLivePreviewEditorViewModel` и editor view: классификация и presentation-state служебного frontmatter; raw editing остаётся существующим механизмом.
- `FeedControl` thematic-file surface: устранение дублирования только в chrome, без изменения H1.
- Ресурсы: RU/EN подписи, tooltip и accessibility names.
- UI tests/AppAutomation/FlaUI: пользовательские сценарии, narrow/wide layout и video evidence.

### 6.2 Детальный дизайн

#### A. Возврат к сегодняшнему дню
1. В правом нижнем углу viewport хронологии появляется компактная непрозрачная кнопка `Сегодня ↑`, когда заголовок сегодняшнего дня ушёл выше viewport. Видимость любой части длинной карточки не считается видимостью её начала. При нахождении заголовка в viewport кнопка скрыта.
2. Кнопка скрыта в поиске, тематическом файле, управлении областями/файлами, onboarding и review-dialog.
3. Нажатие сначала пытается сохранить активную правку существующим `CommitActiveEditorsAsync`. Только после успеха выполняется `ScrollIntoView` сегодняшнего дня и возврат к верхней части карточки.
4. При ошибке сохранения viewport и focus остаются на редактируемом блоке; показывается существующее локализованное объяснение. Кнопка не изображает успех.
5. App-local shortcut: `Alt+Home`. Он не является стандартной командой редактирования текста, работает только в основном окне и маршрутизируется в то же действие. При открытом modal dialog не срабатывает.
6. Цель выбирается только из видимых дней текущего фильтра: сегодняшняя заметка по `EffectiveToday`, иначе самый новый видимый день. Во втором случае подпись — `К последним записям ↑`, подсказка содержит дату назначения. Фильтры не сбрасываются, файл не создаётся. При отсутствии видимых дней, неинициализированном vault или conflicting operation команда disabled/hidden. Пустая хронология с фильтром предлагает явно сбросить фильтр.

#### B. Один фильтр в текущем контексте
1. `FeedAreaFilterButton` виден только когда доступна хронология: vault загружен, режим приложения — лента, нет поиска, тематического файла, drawers областей/файлов, onboarding или modal/recovery. Существующее `IsChronologyVisible == !IsSearchActive` для этого недостаточно; вводится отдельное вычисляемое условие для доступности навигации и фильтра, не меняющее видимость фоновой хронологии.
2. При `IsSearchActive` виден только `FeedSearchFiltersControl`; chronology-фильтр не остаётся над ним.
3. При открытом тематическом файле, списке файлов или управлении областями фильтр хронологии скрыт.
4. Feed area state и search area state остаются независимыми и запоминаются при переключении режима. Мы меняем visibility, а не объединяем разные семантики в одну коллекцию.
5. На wide layout search filters остаются компактными полями типа/области/периода. На narrow layout они используют существующий перенос без второго summary/chip ряда: дополнительная строка чипов отклонена как новый визуальный шум.
6. Непоместившиеся действия переносятся в меню `Ещё`, а не исчезают. На узком окне поиск доступен отдельной кнопкой; меню сохраняет переключение Лента/Задачи, области, файлы, обновление, разбор и настройки. В макете вне пяти polish-сценариев допускается явно обозначенный ограниченный preview существующего экрана. Переключение ширины не меняет текущий экран или фильтры.

#### C. Служебный frontmatter
1. Frontmatter сворачивается по умолчанию только если parser видит один корректный верхний `FrontMatter` block и все его top-level keys входят в allowlist Unlimotion: `unlimotion-id`, `areas`, `unlimotion-areas`. Последний ключ — фактическое имя, записываемое `FeedNoteExtractionService.BuildDestination`; добавлен при EXEC-сверке с реальным storage contract. Одновременные два ключа областей неоднозначны и остаются raw.
2. Компактная неброская кнопка `Служебные данные` рядом с датой показывает количество областей при наличии и chevron состояния. Широкая залитая полоса на каждый день исключена. Это presentation-only состояние текущего editor VM; на диск не записывается.
3. Нажатие раскрывает существующее raw-представление целиком. Повторное нажатие сворачивает его; редактирование raw остаётся доступным после раскрытия.
4. Пустой, malformed или содержащий хотя бы один неизвестный top-level key frontmatter показывается существующим raw fallback без автоматического сворачивания. Так пользовательский YAML никогда не скрывается по догадке.
5. Поиск продолжает индексировать данные как сейчас; визуальное сворачивание не меняет source identity, offsets или review locators.
6. В раскрытом состоянии raw редактируется. При появлении неизвестного ключа сворачивание становится недоступно; raw остаётся видимым. Раскрытие/сворачивание сами по себе не меняют Markdown. После изменения raw статус правки виден; сценарий ошибки не теряет текст, фокус и выделение.

#### D. Дублирующий заголовок
1. Пользовательский H1 всегда остаётся в editor и доступен для редактирования.
2. В thematic-file chrome большой `DisplayName` скрывается только когда нормализованный первый содержательный H1 точно совпадает с нормализованным display name. `RelativePath`/breadcrumb и кнопка закрытия остаются.
3. Если H1 отсутствует, отличается, содержит неоднозначный Markdown или документ ещё не загружен, chrome title показывается как сейчас.
4. Для daily-карточек дата остаётся стабильным заголовком карточки; пользовательский H1 не скрывается. В этой итерации мы не угадываем, является ли повтор даты намеренным содержанием.

#### E. Комфортная ширина текста
1. Внутреннее содержимое editor получает `MaxWidth=960` и центрируется только при доступной ширине больше 1000 px. Карточка дня, дата, collapse control, scroll bar и floating button используют полную доступную ширину.
2. Hover toolbar и drag handles выравниваются относительно той же текстовой колонки, а не остаются у края окна.
3. При ширине до 1000 px editor остаётся `Stretch`; горизонтального scroll и дополнительной рамки нет.
4. Interactive mockup предлагает сравнить 880/960/full-width через design controls; production default этой SPEC — 960. Изменение default после просмотра макета допустимо без расширения scope.

#### Visual planning artifact

Хронология после глубокой прокрутки:

```text
┌────────────────────────────────────────────────────────────────────┐
│ +  [Пространство]   Лента  Задачи     Поиск…      Разбор  Настройки│
├────────────────────────────────────────────────────────────────────┤
│ [Все области ▾]                           Области  Файлы  Обновить │
│                                                                    │
│  четверг, 3 сентября                                               │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ Служебные данные · 2 области                           ▸     │  │
│  │ ## Работа                                                  ⋮⋮ │  │
│  │ Подготовил схему и записал результаты…                       │  │
│  │ ☐ Проверить обратную связь                                   │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                       [Сегодня ↑] │
└────────────────────────────────────────────────────────────────────┘
```

Поиск — chronology filter исчезает, остаётся один набор search filters:

```text
┌────────────────────────────────────────────────────────────────────┐
│ Поиск: обратная связь                                              │
│ [Заметки и задачи ▾] [Все области ▾] [За всё время ▾]             │
│                                                                    │
│ 3 сентября · Ежедневные/2026.09.03.md                              │
│ …подготовил схему и записал обратную связь…                        │
└────────────────────────────────────────────────────────────────────┘
```

Тематическая заметка с совпадающим H1 — chrome не повторяет название:

```text
┌────────────────────────────────────────────────────────────────────┐
│ Заметки / Проекты / Лента.md                                  [×] │
│                                                                    │
│ # Лента                                                            │
│ Основные решения и наблюдения…                                     │
└────────────────────────────────────────────────────────────────────┘
```

Interactive mockup на EXEC: local-only `output/feed-navigation-polish/feed-navigation-polish.html`, wide-mode. Хронология имеет собственный scroll viewport, различимые документы и заметку из 200 строк; Today реально прокручивает к существующему заголовку. Размер окна независим от экрана. Поиск и фильтры работают на локальных демонстрационных документах; тематическая заметка открывается из результатов/файлов и закрывается обратно. Есть known-only, unknown и malformed YAML, совпадающий/отличающийся/отсутствующий H1. Ошибка сохранения моделируется однократно и не изображает настоящий disk write. Ширина 880/960/full и fault-сценарии доступны через Tweak; полный backend и весь редактор вне границ макета. Макет работает без `ResizeObserver`, сетевых запросов и периодических таймеров. Подтверждение макета требуется по уже согласованному порядку до production-кода.

UI video evidence: до изменения записать current behavior deep-scroll/filter/frontmatter/thematic title; после — тот же маршрут и narrow state. Плановые local-only пути: `output/feed-navigation-polish/before.mp4`, `after.mp4`. Если безопасная window-only запись технически невозможна, fallback: парные screenshots + FlaUI trace + deterministic assertions с явной причиной в отчёте.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| S1 Глубокая история | Прокрутить несколько месяцев вниз | Появляется `Сегодня ↑`; click/Alt+Home сохраняет правку и возвращает к сегодняшней карточке | Headless + FlaUI + video | AC01–02 |
| S2 Ошибка сохранения | Изменить блок, заставить commit завершиться ошибкой, нажать `Сегодня` | Лента не прыгает, focus остаётся, ошибка видима | Headless fault-path | AC02 |
| S3 Обычная лента | Открыть chronology | Видно ровно один feed-area filter | Headless + screenshot | AC03 |
| S4 Поиск | Ввести запрос и выбрать область | Chronology filter скрыт; search filter один, состояние поиска сохраняется | Headless/FlaUI | AC03–04 |
| S5 Служебные данные | Открыть заметку с known-only frontmatter | Одна компактная строка; expand показывает полный raw без изменения файла | Editor UI + disk check | AC05 |
| S6 Пользовательский YAML | Открыть frontmatter с неизвестным key | YAML сразу виден raw, ничего не скрыто | Unit/editor UI | AC05 |
| S7 Тематическая заметка | Открыть файл, где display name равен H1 | Chrome не дублирует название; H1 остаётся и редактируется | Headless/FlaUI | AC06 |
| S8 Широкое/узкое окно | Resize 1400 → 520 px | На wide текст ограничен и центрирован; на narrow заполняет ширину без overlap | Layout UI + screenshots | AC07 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Target heading visible | Заголовок ушёл выше viewport, включая длинный сегодняшний день | Floating action appears | No matching days → hidden; missing today → latest label | View-local calculated state |
| Dirty editor | Today action | Commit → scroll to first day | Commit error → no scroll/focus loss | One async operation |
| Chronology | Start search / open overlay | Hide feed filter; show relevant controls | Close → restore chronology/filter/scroll | States remain independent |
| Known-only frontmatter | Open note | Compact collapsed row | Malformed/unknown key → raw | No disk mutation |
| Collapsed service data | Activate row | Full raw block appears | Editor busy → action disabled | Accessible toggle semantics |
| Thematic H1 equals title | Open file | Hide duplicate chrome title | No/different H1 → show title | H1 never hidden |
| Wide editor | Resize below threshold | Stretch content | No horizontal scrolling | Toolbar follows column |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope next iteration | agent | Только пять high-ROI polish improvements; search grouping позже | 0.98 | Размытый большой redesign | Нет |
| Today placement | agent | Floating bottom-right, only when needed | 0.94 | Постоянная toolbar-кнопка занимает место | Нет |
| Shortcut | agent | App-local `Alt+Home` | 0.86 | Возможный platform conflict; проверяется native | Нет |
| Frontmatter safety | agent | Collapse only allowlisted known-only block | 0.99 | Скрытие пользовательского YAML | Нет |
| Heading dedup | agent | Убирать только повторяющий chrome title, не Markdown H1 | 0.99 | Потеря/недоступность контента | Нет |
| Text width | agent | 960 px; сравнить 880/960/full в mockup | 0.88 | Субъективная плотность | Нет; mockup approval до кода |
| Filter architecture | agent | Независимые filter states, context-only visibility | 0.97 | Неожиданная смена фильтра при переходе | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Markdown/frontmatter | FileNoteVault + Markdown parser | Presentation only | Нет миграции; bytes не меняются от collapse/expand | Hash/file assertions |
| Filter state | FeedViewModel collections | Только visibility по mode | Существующие значения сохраняются | Mode-switch tests |
| Today navigation | Days + editor commit + ItemsControl | Новая UI-команда/route | Persisted state отсутствует | UI/fault tests |
| Layout | Avalonia XAML/code-behind | Adaptive max width/overlay | Старые window sizes поддержаны | 520/1000/1400 layout tests |

## 7. Бизнес-правила / Алгоритмы
1. `CanCollapseFrontMatter = recognizedUnambiguousSubset && first block && keys ⊆ {unlimotion-id, areas, unlimotion-areas} && keys.Count > 0`. Неподдерживаемый сложный YAML остаётся видимым; активный raw-блок никогда не сворачивается автоматически.
2. Любой неизвестный top-level key делает блок пользовательским и оставляет raw visible.
3. `Today` navigation считается успешной только после успешного commit активного editor.
4. Контекст выбирает видимый filter surface, но не копирует selections между feed и search.
5. Нормализация title для сравнения убирает только Markdown H1 prefix, surrounding whitespace и расширение файла; сравнение ordinal ignore-case. Содержимое не изменяется.

## 8. Точки интеграции и триггеры
- `ChronologyScroller.ScrollChanged` и virtualization materialization обновляют visibility `Сегодня`.
- Click и `Alt+Home` вызывают одну async navigation routine.
- `IsChronologyVisible` / `IsSearchActive` управляют filter visibility.
- Editor refresh повторно классифицирует frontmatter без изменения snapshot identity.
- Open thematic result пересчитывает chrome-title visibility после загрузки editor snapshot.
- `SizeChanged`/layout classes переключают bounded/stretch editor column.

## 9. Изменения модели данных / состояния
- Новых persisted полей нет.
- Session-only: `IsServiceFrontMatterExpanded` для editor instance и view-local видимость/цель/подпись команды возврата. `EffectiveToday` учитывает существующую настройку границы дня; после её изменения и обновления документов цель пересчитывается.
- Допустимы calculated properties для frontmatter summary и thematic title duplication.
- Markdown, area catalog, settings и task storage не меняются.

## 10. Миграция / Rollout / Rollback
- Миграция отсутствует: изменения presentation-only и session-only.
- Старые vault читаются без преобразования.
- Откат кода возвращает прежнее отображение, не требует data rollback.
- Если frontmatter classifier сомневается, fail-open: показать raw.

## 11. Тестирование и критерии приёмки

- AC01: команда возврата скрыта при видимом заголовке назначения, появляется даже внутри сегодняшней заметки на 200 строк, отсутствует вне доступной хронологии. Если сегодня отсутствует/скрыто фильтром, подпись `К последним записям ↑`, цель — самый новый видимый день. Пустой результат скрывает команду, фильтры не сбрасываются.
- AC02: click и `Alt+Home` возвращают к сегодняшнему дню после успешного сохранения; commit failure сохраняет viewport/focus и показывает ошибку.
- AC03: в chronology/search/thematic/areas/files режимах виден не более чем один семантически применимый area filter.
- AC04: feed/search filter selections независимы и восстанавливаются после переключения режимов; на 520/320 px все действия доступны напрямую либо через `Ещё`, поиск открывается и принимает запрос. Изменение ширины не меняет экран.
- AC05: known-only frontmatter collapsed/expanded без изменения файла; unknown/malformed YAML raw visible; клавиатура и screen-reader получают toggle name/state.
- AC06: совпадающий thematic chrome title скрыт, но H1 остаётся видимым/редактируемым; отличающийся title не скрывается.
- AC07: при 1400 px внутренний editor не шире 960 px, а карточка/дата остаются полной ширины; toolbar выровнен с editor. При доступной ширине до 1000 px editor stretch. Проверять длинный абзац, списки и 200 строк; выбор 880/960/full не теряет focus/scroll/правки.
- AC08: макет демонстрирует реальную прокрутку, переход/Alt+Home и однократный fault без потери правки/фокуса, смену независимых фильтров, переход результат → заметка → закрыть, редактирование и безопасное раскрытие метаданных. Визуальные режимы/ширины проверяются отдельно от сценариев. Пользователь подтверждает макет до production-кода.
- AC09: existing capture/editor/review/search/filter/virtualization scenarios не регрессируют; обычная сборка, полный main, Headless и FlaUI green.
- AC10: before/after video либо объективно обоснованный fallback сохранены local-only и осмотрены.

План автоматизации:
- `FeedControlUiTests`: today visibility/navigation, dirty commit, save failure, context filter uniqueness, 800-day virtualization regression.
- `MarkdownLivePreviewEditorTests/UiTests`: known-only/unknown/malformed frontmatter, expand/edit/disk identity, accessibility.
- `FeedShellUiTests`: chronology/search/thematic state matrix и 520/1000/1400 px layout.
- Headless integration: opened thematic title match/difference и active filter preservation.
- FlaUI: real scroll → today action, service metadata expand, search mode filter uniqueness, narrow window.

Команды EXEC используют repository-proven TUnit `--treenode-filter`, `--minimum-expected-tests` и `--maximum-parallel-tests 1`; shared UI suites запускаются последовательно. До длинных прогонов фиксируются log path и исторически достаточный timeout. После targeted checks: обычный solution build, full main, full Headless, full FlaUI.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC01–02 | FeedControl UI + fault path + FlaUI | Mockup and native window | today-navigation.log, after.mp4 | — |
| AC03–04 | Shell/filter state matrix | Wide/narrow screenshots | filter-context.log | — |
| AC05 | Parser/editor VM/UI + file hash | Expand/collapse native | frontmatter-ui.log, after.mp4 | — |
| AC06 | Feed thematic Headless/FlaUI | Same/different title screenshots | thematic-title.log | — |
| AC07 | Layout bounds tests 520/1000/1400 | Mockup width alternatives + screenshots | layout-width.log | — |
| AC08 | Interactive local mockup inspection | Explicit user mockup approval | feed-navigation-polish.html | Production code waits for approval |
| AC09 | Targeted + normal build + full suites | Author route | final-*.log | — |
| AC10 | Recorder artifact validation | Human inspection | before.mp4, after.mp4 | Fallback only if recorder objectively unavailable |

## 12. Риски и edge cases
- Scroll event может показать/скрыть floating action во время virtualization; вычисление должно быть throttled/coalesced и не запускать load само по себе.
- Отсутствие сегодняшнего файла — обычный случай в базе с записями по будним дням. Навигация выбирает самый новый видимый день и честно меняет подпись; учитываются фильтр, пустой результат и `EffectiveToday`, файл не создаётся.
- Commit при возврате может пересечься с watcher refresh; используется существующий busy/session/revision contract.
- YAML parser классифицирует block, но allowlist key extraction должна учитывать списки `areas` и не принимать nested unknown key за top-level.
- Title normalization не должна скрывать chrome при частичном совпадении или локализованном различии.
- Max width может отдалить hover controls; поэтому toolbar/handles входят в ту же колонку и проверяются pointer tests.
- Overlay `Сегодня` не перекрывает последний checkbox или scrollbar; нужны hit-test и 520 px проверки.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Кнопка опять занимает место» | Ранее верхняя панель считалась перегруженной | Floating action появляется только вне today viewport | mitigated |
| «Вы скрыли мой YAML» | Markdown — самостоятельная база пользователя | Collapse только known-only allowlist; unknown/malformed raw visible | mitigated |
| «Пропал мой заголовок» | H1 является содержанием | H1 не скрывается; убирается только дублирующий chrome title | mitigated |
| «Теперь фильтры меняют друг друга» | Feed/search имеют разные семантики | Только context visibility; state collections не объединяются | mitigated |
| «На широком экране стало слишком узко» | Плотность субъективна | Mockup 880/960/full; default 960, approval до кода | mitigated |
| «Возврат наверх потеряет несохранённый текст» | Virtualization может убрать editor | Commit-first; failure leaves viewport/focus | mitigated |

### Rework Prevention Checklist
- Видимые элементы и состояния названы: да.
- Каждый пользовательский сценарий имеет evidence: да.
- Все assumed decisions записаны: да.
- Вероятные замечания закрыты безопасными defaults: да.
- Role-based review: ниже.
- AC описывают результат, а не подготовительные действия: да.
- EXEC имеет путь mockup → approval → code → UI evidence → full green: да.

## 13. План выполнения
1. После SPEC approval создать interactive mockup в local-only output, проверить 1024/736/520/320 px и light/dark, показать пользователю; остановиться до подтверждения макета.
2. Записать before-video текущего build либо документировать допустимый fallback.
3. Реализовать today navigation и context filter visibility с UI tests.
4. Реализовать conservative frontmatter folding и thematic chrome dedup с negative fixtures.
5. Реализовать adaptive text width и pointer/narrow layout tests.
6. Запустить targeted tests, обычную сборку и последовательные full main/Headless/FlaUI.
7. Запустить fresh author build, пройти S1–S8, записать/осмотреть after-video.
8. Выполнить full post-EXEC review, исправить findings и повторить затронутые проверки.

## 14. Открытые вопросы
Блокирующих вопросов нет. Производственный default ширины — 960 px; пользователь сможет изменить выбор при согласовании mockup без расширения scope.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`.
- Выполненные требования профиля: visual plan в SPEC; interactive mockup до code; stable automation ids; UI/Headless/FlaUI coverage; before/after video или объективный fallback; normal build и full tests; UI thread не блокируется I/O.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/FeedControl.axaml(.cs)` | Today overlay/hotkey, context visibility, thematic chrome/layout | Основная UI-поверхность |
| `src/Unlimotion.ViewModel/Feed/FeedViewModel.cs` | Commit-first today preparation, calculated title/frontmatter state if needed | Безопасная state coordination |
| `src/Unlimotion.ViewModel/Feed/MarkdownLivePreviewEditorViewModel.cs` | Conservative service-frontmatter presentation | Скрыть только системный шум |
| `src/Unlimotion/Views/MarkdownBlockLivePreviewEditor.axaml(.cs)` | Toggle/raw state and bounded column alignment | Editor rendering |
| `src/Unlimotion.ViewModel/Resources/Strings*.resx` | RU/EN labels and errors | Localization/accessibility |
| `src/Unlimotion.Test/*Feed*`, `*MarkdownLivePreview*` | Unit/Avalonia UI regressions | AC01–09 |
| `tests/Unlimotion.UiTests.*` | Headless/FlaUI user routes | Native behavior evidence |
| `output/feed-navigation-polish/*` | Local-only mockup/video/logs | Не коммитить по умолчанию |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Возврат в настоящее | Только ручная обратная прокрутка | Contextual `Сегодня ↑` + `Alt+Home`, commit-first |
| Фильтры | Feed и search area filters могут быть видны вместе | Виден один относящийся к режиму filter surface |
| Frontmatter | Служебный YAML как raw code | Known-only compact toggle; user YAML raw |
| Заголовок thematic file | Chrome title + path + такой же H1 | Path + H1; chrome title только если отличается |
| Широкий editor | Строка растягивается | Читаемая колонка max 960; narrow stretch |

## 18. Альтернативы и компромиссы
- Постоянная кнопка «Сегодня» в toolbar: проще, но занимает место в 99% времени; отклонено.
- Перехват `Ctrl+Home`: привычно звучит, но ломает стандартную текстовую навигацию; выбран `Alt+Home`.
- Скрывать весь frontmatter: визуально чище, но опасно для самостоятельной Markdown-базы; выбран allowlist/fail-open.
- Скрывать совпадающий H1: убирает дубль, но делает контент менее управляемым; скрывается только chrome.
- Объединить feed/search filter state: одна модель, но неожиданная семантика; сохранены независимые states.
- Полностью запретить wide text: лучше читается, но может казаться тесным; chosen 960 после mockup comparison.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1–5 | PASS | Цель, AS-IS, проблема, дизайн и Non-Goals конкретны |
| B. Качество дизайна | 6–10 | PASS | State ownership, error, compatibility и perf boundaries описаны |
| C. Безопасность изменений | 11–13 | PASS | Нет data migration; fail-open YAML и commit-first navigation |
| D. Проверяемость | 14–16 | PASS | AC01–10, UI matrix, artifacts и команды определены |
| E. Готовность к автономной реализации | 17–19 | PASS | Этапы и defaults заданы; mockup human gate встроен |
| F. Соответствие профилю | 20 | PASS | Visual plan, UI tests, video/fallback и full runs обязательны |

Историческая оценка v1. Отозвана после авторского ревью: обнаружены недоопределённые сценарии Today и расхождения макета. Актуальный результат приведён ниже.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Пять изменений и отдельные будущие этапы разделены |
| 2. Понимание текущего состояния | 5 | Названы реальные controls, VM и UX symptoms |
| 3. Конкретность целевого дизайна | 5 | Определены placement, states, safety rules и wireframes |
| 4. Безопасность | 5 | Presentation-only, no migration, commit-first, fail-open |
| 5. Тестируемость | 5 | AC-to-test mapping, fault/narrow/native/full evidence |
| 6. Готовность к автономной реализации | 5 | Default decisions и stop gates не оставляют скрытого выбора |

Исторический балл v1: 30 / 30 был завышен. Не использовать как доказательство готовности текущего макета или реализации.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Сохраняет ли polish маршрут записать → найти → вернуться сегодня? | PASS | Commit-first и independent filter states зафиксированы |
| UX / designer | applicable | Станет ли интерфейс тише без скрытия agency? | PASS | Floating action, fail-open YAML, chrome-only dedup, bounded text |
| Tester / validation | applicable | Все ли состояния, ошибки и размеры имеют evidence? | PASS | AC01–10 и matrix покрывают happy/fault/narrow/native |
| Developer / architect | applicable | Нет ли нового persisted coupling и риска virtualization? | PASS | View/session-only state, existing contracts, explicit scroll risk |
| Delivery / operations / security | applicable | Изолированы ли artifacts и external effects? | PASS | Mockup/video local-only; Git/release отдельно |

### Post-SPEC Review (исторический v1, заменён ревью ниже)
- Статус: NEEDS-FIX по последующему авторскому ревью; прежний PASS отозван.
- Scope reviewed: эта SPEC, related accepted SPEC/report, current FeedControl/MainScreen/editor/FeedViewModel integration points, central owner documents, selected profiles, planned changed files и local-only artifacts.
- Decision: можно запрашивать подтверждение SPEC; после него сделать и отдельно согласовать interactive mockup до code.
- Review passes:
  - Scope/Evidence pass: каждое наблюдение связано с текущим control/VM или предыдущим inspected author route; search grouping/perf cache явно вынесены.
  - Contract pass: Non-Goals, S1–S8, AC01–10, UI automation/video и no-Git boundaries согласованы.
  - Adversarial risk pass: проверены потеря dirty text, конфликт `Ctrl+Home`, скрытие user YAML/H1, filter coupling, overlay hit-test и субъективная width.
  - Role-Based pass: все пять применимых ролей выше дали PASS после внесённых ограничений.
  - Fix and re-review: исходные идеи «скрыть YAML/H1» сужены до allowlist и chrome-only; chips исключены как лишняя строка; затронутые scenarios/AC повторно сверены.
  - Stop decision: PASS; блокирующих user-owned решений до SPEC approval нет.
- Evidence inspected: `FeedControl.axaml/.cs`, `MainScreen.axaml/.cs`, `FeedSearchFiltersControl.axaml`, `FeedViewModel`, Markdown parser/editor VM, RU/EN resources, previous acceptance spec/report, branch/status.
- Depth checklist:
  - Scope drift / unrelated changes: только stage-1 polish; search grouping/memory optimization/new settings не включены.
  - Acceptance criteria: каждый visible/fault/layout outcome имеет test/evidence.
  - User-observable scenarios / Decision ledger / Expected objections: заполнены; mockup approval — planned EXEC gate.
  - Validation evidence: запланированы targeted, normal build, full main/Headless/FlaUI, native and video.
  - Unsupported claims: не обещаны memory/perf gain, Obsidian parity или скрытие любого YAML.
  - Regression / edge case: dirty commit, modal, unknown YAML, title mismatch, narrow overlay, virtualization предусмотрены.
  - Comments/docs/changelog: новые комментарии только для неочевидных safety invariants; changelog/release вне scope.
  - Hidden contract change: app-local `Alt+Home` и presentation rules названы явно; persisted contract unchanged.
  - Manual-review challenge: наиболее вероятные замечания — «кнопка мешает», «YAML/H1 пропал», «фильтры связались», «текст слишком узкий»; все имеют visible negative checks и mockup comparison.
- No-findings justification: после ограничений allowlist/chrome-only и удаления chip-row открытых findings нет; PASS опирается на state matrix и acceptance mapping, а не на вкусовую оценку.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | data agency | Идея скрывать frontmatter могла скрыть пользовательский YAML | Allowlist known-only, любой unknown key → raw | fixed |
| HIGH | content agency | Dedup мог скрыть редактируемый H1 | Убирать только chrome title | fixed |
| MEDIUM | keyboard | `Ctrl+Home` конфликтует с editor semantics | App-local `Alt+Home` и modal guard | fixed |
| MEDIUM | filter state | «Единый фильтр» мог связать разные состояния | Менять только context visibility | fixed |
| MEDIUM | visual density | Active chips создавали бы ещё одну строку | Оставить компактные existing search controls | fixed |
| LOW | text width | 960 px — вкусовой default | Compare 880/960/full in mockup before code | follow-up gate |

- Fixed before continuing: safety boundaries YAML/H1, shortcut, filter ownership, no-chip decision и mockup width comparison.
- Checks rerun: структурная сверка canonical sections; AC ↔ scenarios ↔ matrix; no open user-owned decision; planned file scope reviewed.
- Needs human: точная SPEC approval; затем отдельное визуальное подтверждение interactive mockup до production code.
- Residual risks / follow-ups: app-local shortcut и 960 px требуют проверки в настоящем окне; search grouping и measured memory optimization остаются отдельными будущими SPEC.

### Post-EXEC Review
- Статус: ASK-HUMAN для завершения native acceptance. Реализация и code review завершены; финальные build, 59 профильных тестов и полный Headless49 прошли. Native3/3 остановились на SendInput AccessDenied; общего PASS нет.
- Scope reviewed: approved SPEC и макет v2; tracked diff FeedControl/MainScreen/editor/resources; новые `FeedControl.Reading.cs`, `MarkdownReadingPresentation.cs`, `FeedReadingPolishUiTests.cs`, `MarkdownReadingPresentationTests.cs`, `FeedReadingPolishFlaUiTests.cs`; фактический `FeedNoteExtractionService.BuildDestination`; build/test logs и Headless PNG.
- Scope/Evidence pass: только пять согласованных polish-сценариев; source и tests прочитаны, unrelated untracked output/obj-* не включались в изменения. Native evidence worker работал только с синтетическим vault и отдельным process.
- Contract pass: возврат commit-first, отдельные filter states, unknown-YAML fail-open, chrome-only H1 dedup и bounded inner column сохранены. Фактическое имя `unlimotion-areas` уточнено по storage source без миграции.
- Adversarial pass: найдены и устранены unknown→known скрытие editor, несовпадение metadata key, отсутствие unrealized-target проверки и восстановление фокуса после смены контекста. Потеря данных проверяется отдельно от представления.
- Role-Based pass: UX — осмотрены рендеры и state matrix, native author route заблокирован средой; tester — expected red → targeted green и full suites; developer — presentation-only, existing commit/session APIs, bounded virtualization; domain — capture/review/task contracts не меняются; delivery/security — без Git/publish и пользовательских данных.
- Fix and re-review: аналитический reviewer повторно прочитал исправления и закрыл четыре code finding. Его sandbox danger-full-access; это read-only-by-behavior review, а не технически изолированное read-only исполнение. Он не запускал builds/tests и не заявлял verification PASS.
- Depth checklist: scope/AC/negative paths/owner docs сверены; нет новых persisted настроек, deps или migration; формулировки evidence отличают HTML mockup, Headless и native. Пропущенная native проверка не маскируется прошлым green run.
- Manual-review challenge: можно ли скрыть активную правку, изменить фильтры переходом, потерять доступ к H1 или вернуть фокус за новую панель — конкретные counterexamples теперь имеют unit/UI coverage. Native pointer/keyboard и визуальная приёмка новой сборки остаются незакрытым evidence, пока не выполнены в доступной сессии.
- No-findings justification: не применимо — находки были, исправлены и повторно проверены по коду; итоговый stop decision зависит от validation ниже.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | metadata editing | Unknown→known мог скрыть активную правку | Раскрывать active metadata до visibility notification, сохранять expansion после commit | fixed; unit/UI regression |
| MEDIUM | storage contract | Собственное поле unlimotion-areas не распознавалось | Узкий allowlist actual key; dual-alias fail-open; SPEC clarification | fixed; unit regression |
| MEDIUM | virtualization evidence | Возврат проверялся только внутри одного дня | 60-day UI test с выгруженным заголовком и bounded realized editors | fixed; targeted PASS |
| MEDIUM | async navigation | Поздняя ошибка возвращала focus за overlay | Context guard до focus/error и внутри queued offset restore | fixed; full-run regression |
| HIGH | UI thread | Background IsBusy notification менял Avalonia visibility не из UI thread | Dispatch PropertyChanged с проверкой актуального VM и самостоятельный guard presentation update | fixed; regression PASS, full Headless49 PASS |
| MEDIUM | native evidence | Текущая RDP-сессия не даёт захват окна | Сохранить failure evidence, выполнить available UI suites, повторить native после восстановления доступа | environment limitation; не product PASS |

### User-Observable Completion Gate до восстановления desktop-сессии (история)

- S1–S8 / AC01–07: production paths реализованы и подтверждены 59 unit/UI tests (`final-reading.log`), включая 14 UI cases, ошибку сохранения, позднее завершение, фоновую команду, выгруженный target, unknown YAML, toggle, filter contexts и ширину520/1000/1400. Три осмотренных Headless PNG подтверждают компонентный layout, но не вид настоящего desktop-окна.
- AC08: ранее согласованный макет v2; нового продуктового выбора не потребовалось.
- AC09: первоначальный full main1499/1499 PASS до узкого dispatcher-fix; после fix —59/59 targeted и полный Headless49/49 PASS. Обычная финальная solution build:0 ошибок,73 предупреждения,27.47s (`final-build.log`). Full main целиком после dispatcher-fix не повторялся; full FlaUI не green. Критерий полной финальной приёмки не закрыт.
- AC10: native video недоступно — Windows Graphics Capture и ffmpeg отказали; fallback: три Headless PNG, детерминированные UI assertions и native trace. Native на520/1000/1400 подтвердил переключение в ленту, наличие фильтра, отсутствие return у заголовка и expand/collapse raw YAML через UIA. Далее3/3 завершились `Mouse.Scroll → SendInput → AccessDenied` (`native-raw-peer.log`,1m35s). Pointer/keyboard return, native search и итоговая проверка файла после полного маршрута в этих тестах не достигнуты.
- Expected objections: лишняя постоянная кнопка, скрытие пользовательского YAML/H1, изменение filter state и потеря draft покрыты контрактом/тестами. Предпочтение ширины принято в макете. Нативная визуальная приёмка остаётся follow-up, не approved residual PASS.
- Fix/re-review: пять production находок закрыты; дополнительно исправлены ошибки native harness — DPI preflight, сравнение outer HWND с client UIA и id Grid вместо raw TextBlock peer. Последующие native traces подтверждают, что тест прошёл эти места и остановился именно на системном вводе.
- Stop decision: ASK-HUMAN. Нужна активная разблокированная Windows/RDP-сессия для завершения авторского маршрута; после её восстановления повторить native/full acceptance. Внесистемных обходов desktop security, изменения разрешения, пользовательских данных и Git delivery не было. Все тестовые desktop-процессы завершены; установленный Unlimotion PID39512 оставлен работающим.

### Продолжение приёмки в активной сессии, 2026-09-06

- Пользователь сообщил «Есть активная сессия». Computer Use подтвердил реальный capture, scroll и Alt+Home на изолированной синтетической базе; прежний AccessDenied снят без изменения разрешения/защит Windows.
- Финальный production-код: полный main **1500/1500 PASS**, без пропусков,25m47s (`active-full-main.log`). Включён dispatcher-fix; прежние1499/1499 больше не являются последним full-main evidence.
- Новые native сценарии520/1000/1400 повторно PASS (`active-full-native-verified.log`): metadata, pointer scroll, return button, Alt+Home, search/filter isolation и неизменность Markdown. Размеры здесь — физические внешние HWND;960DIP и logical layout подтверждены отдельными Headless tests.
- В первом полном native прогоне было13 PASS и один реальный сбой `Major_tabs_can_be_opened_from_main_window`; оставшиеся cases отменены fail-fast. Computer Use подтвердил, что скрытая вкладка доступна через existing overflow. Test-only hook открывает это меню перед теми же восемью assertions; остальные платформы получают no-op. Узкий повтор1/1 PASS (`tabs-native-targeted.log`); code-only independent re-review — без находок, без технической read-only изоляции reviewer.
- Полный Headless после первого hook: **49/49 PASS**, без пропусков,3m27s (`active-full-headless.log`). Полный native: **26/28 PASS,2 FAIL,0 skipped**,12m26s (`active-full-native-verified.log`). Один FAIL — тот же hidden-tab assumption в `Feed_shell_switch_preserves_task_context`, hook добавлен только до начального выбора, assertions контекста после возврата не менялись; independent code-only re-review без находок. Второй FAIL — UIA Invoke exception в `Daily_note_filename_format_settings` на явном reload; предыдущий прогон PASS, причина не установлена. Узкие повторы и Headless после последней строки hook выполняются. Production-код в этом продолжении не менялся.
- Обычная solution build после финального test hook:0 ошибок,30 предупреждений,20.51s (`active-final-solution-build.log`). AC09 пока не закрыт: полный native не green; отдельные ретесты не заменяют полного результата.
- Узкие native ретесты: `Feed_shell_switch_preserves_task_context`1/1 PASS,59.53s (`final-shell-native.log`); `Daily_note_filename_format_settings`1/1 PASS,1m27s (`final-settings-native.log`). Последний прошёл без изменения сценария/production и без внутренних retries; первопричина единичного UIA Invoke failure остаётся неустановленной. Результат26/28 полного прогона не переписывается в28/28.
- AC10: fallback обоснован первоначальным отказом baseline capture; осмотрены реальные новые PNG520/1000/1400, а не только Headless. Видео `active-reading-after.mp4`520×850,15fps,19.98s подтверждает expanded/collapsed metadata (кадры03s/16s), не полный маршрут. Сравнительного baseline video нет; старые видео не подменяют его.
- UX residual: на экстремально узкой логической области при текущем крупном масштабе обрезаются date/metadata/reminder labels, панели оставляют мало высоты для текста. Предлагается отдельный компактный вариант reminder и адаптивные краткие подписи с полным tooltip. Не реализовано; общая оценка всей фичи10/10 не заявляется.
- Данные пользователя, установленное приложение и Git delivery не затронуты. Отчёт: `output/feed-navigation-polish/implementation-report.md`.

### Фиксация результатов перед локальными коммитами, 2026-09-08

- Завершившийся финальный Headless после последней строки test hook: **49/49 PASS**, без пропусков,3m42.657s (`final-hook-headless.log`). Отложенных прогонов этого этапа больше нет. Проверки выполнены6сентября;8сентября повторно прочитаны итоговые логи, новых запусков в commit-only этапе не было.
- Итоговый набор доказательств: обычная сборка0 ошибок/30 предупреждений; main1500/1500; Headless49/49; native reading3/3. Полный FlaUI26/28 не переименовывается в green, хотя оба упавших сценария затем прошли отдельно. Причина нестабильного UIA Invoke в настройках остаётся открытой; AC09 полного native-green не закрыт.
- Пользователь разрешил «Закоммить что нужно». Включаются production, профильные regression tests, текущая SPEC и отдельная адаптация test navigation к existing overflow. Commit не означает полную приёмку10/10, публикацию или разрешение push.
- Local-only output, видео/PNG, диагностика, промежуточные build folders и посторонняя SPEC про emoji не включаются.

## Approval
SPEC и шесть правок авторского ревью подтверждены пользователем фразой «Спеку подтверждаю». Макет v2 подтверждён 2026-09-06 ответом «Да, выглядит хорошо». Production EXEC разрешён; commit/push не запрошены.

### Ревью v2: согласованные исправления

| Severity | Finding | Required action | Status |
| --- | --- | --- | --- |
| HIGH | На узком окне теряется доступ к действиям | Меню overflow и отдельная кнопка поиска, проверить кликами | Исправлено; сценарии v2 PASS |
| HIGH | Today меняет дату вместо перехода | Реальный scroll viewport, документы, keyboard/fault route | Исправлено; сценарии v2 PASS |
| HIGH | Today не определён для отсутствующего/скрытого дня | Новый контракт цели/подписи и длинного дня в AC01 | Исправлено; missing/filtered/empty/long-day проверки PASS |
| MEDIUM | IsChronologyVisible не учитывает overlays | Отдельное условие доступности, priority matrix | Исправлено в SPEC; production ещё не начат |
| MEDIUM | Ограничена карточка вместо текста | Full-width shell, bounded editor, длинные fixtures | Исправлено; сценарии v2 PASS |
| MEDIUM | Фильтры/YAML/thematic paths декоративные | Рабочие переходы и отрицательные примеры | Исправлено; сценарии v2 PASS |

Проверки v1 light/dark и отсутствие console errors не подтверждали новые сценарии.

### Результат проверки макета v2 (2026-09-06)

- Scope/evidence: обновлённая SPEC, `output/feed-navigation-polish/feed-navigation-polish.html`, `review-v2-checks.js`, `final-smoke.js` и осмотренные screenshots `v2-1400-light.png`, `v2-736.png`, `v2-320-menu.png`, `v2-save-failure.png`, `v2-final-dark.png`.
- Contract: 62 браузерные проверки PASS — 16 документов, 200 строк сегодня, scroll/click/Alt+Home, ошибка без потери текста/focus/позиции, отсутствие/фильтрация today, пустой результат, metadata known/unknown/malformed/edit, независимые фильтры, тематические переходы, доступ к узким меню, размеры 1400/1024/1000/736/520/320. JavaScript runtime errors: 0.
- Layout measurement: при viewport 1400 px карточка 1331 px, editor 960 px; варианты 880/960/full действительно меняют только editor. На доступной ширине до 1000 px editor stretch.
- Adversarial/fix/re-review: error notice сначала перекрывал активную строку, исправлено адаптивным overlay; новый hit-test подтверждает видимость строки. Динамический счётчик областей проверен после изменения metadata. Финальная проверка опубликованного preview после снятия editor outline: PASS, реальное колесо → кнопка возврата → scrollTop=0.
- Role-based: UX — пройдено для шести согласованных правок; tester — сценарии и реальные UI interactions пройдены; developer — production integration ещё не выполнялась; domain — данные демонстрационные, дата «сегодня» фиксирована на 2026-09-05 для воспроизводимости; delivery — публикаций и Git mutations нет.
- Границы доказательств: test fixture добавляет адаптер host Tweak и затем точный исходник макета. Проверены обработчики design controls, а не оболочка панели Tweak в Codex. В финальном preview отдельно проверен основной pointer route. Полный Markdown editor, task/settings/review/capture screens и disk persistence не реализованы: эти экраны явно отмечены как previews существующего поведения. Нет доказательства native Avalonia поведения или performance на 800 днях в этой итерации макета.
- Stop decision: PASS для исправлений SPEC + макета, готов к визуальному согласованию. Production-код ещё не начат по согласованному mockup gate. Предыдущие generic оценки 30/30 не используются.
- Данные пользователя и установленное приложение не использовались. Local-only artifacts по умолчанию не коммитятся.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего этапа | 0.97 | Точная width preference | Сформировать UX contract | Нет | Пользователь согласился с рекомендованным stage 1 | Выбраны повторяемые daily-use улучшения вместо новых сущностей | Только текущая SPEC |
| SPEC | Current-code inspection | 0.99 | Native shortcut behavior | Закрыть state/edge matrix | Нет | Пользователь не отвлекался дополнительными вопросами | Подтверждены два filter surfaces, raw FrontMatter, thematic chrome и scroll integration | Только текущая SPEC; source read-only |
| SPEC | Visual/safety design | 0.96 | Реакция на interactive mockup | Approval SPEC → mockup | Да, два фазовых gate | Сейчас запрашивается только SPEC approval | Floating Today, allowlist frontmatter, chrome-only dedup и 960 default минимизируют риск | Только текущая SPEC |
| SPEC | Full post-SPEC review | 0.99 | Фраза approval | Запросить «Спеку подтверждаю» | Да | Ещё не получена | Linter PASS, rubric30/30, roles/review PASS; production files не менялись | Только текущая SPEC |
| EXEC | Interactive mockup gate | 0.98 | Визуальная оценка пользователя и выбор ширины | Показать макет и остановиться до approval | Да | SPEC подтверждена точной фразой | Четыре состояния, Today/frontmatter interactions и 880/960/full Tweak проверены при 1024/736/520/320 px, light/dark; console errors 0 | `output/feed-navigation-polish/*`; production files не менялись |
| EXEC | Исправление по авторскому ревью v2 | 0.95 | Результаты повторных сценариев | Исправить макет и проверить | Визуальное согласование после проверок | Пользователь подтвердил предложенные правки | Отозван завышенный PASS v1; конкретизированы шесть замечаний и проверяемые сценарии | Текущая SPEC, local-only mockup |
| EXEC | Повторная проверка v2 | 0.97 | Визуальное решение пользователя | Показать исправленный макет | Да, до production по согласованному gate | SPEC и замечания подтверждены; исправленный визуальный результат ещё не подтверждён | 62 scenario assertions PASS + финальный mouse-wheel/outline smoke PASS; inspected screenshots | SPEC, local-only mockup/test evidence |
| EXEC | Согласование макета и реализация | 0.98 | Native evidence нового поведения | Реализовать пять polish-сценариев и регрессии | Нет | «Да, выглядит хорошо» — макет согласован | Сохраняется approved scope, данные и Git delivery не затрагиваются | SPEC; FeedControl/editor; UI tests |
| EXEC | Проверки и контрпримеры | 0.98 | Повторные UI/full suite результаты | Исправить unknown→known collapse и проверить pointer toggle | Нет | Пользователю сообщён фактический ключ unlimotion-areas | Исходные 2 UI проверки воспроизвели проблемы, затем PASS; расширенный прогон 46/48 выявил test invocation и viewport edge | Reading VM/helper, FeedControl, новые тесты |
| EXEC | Native baseline | 0.99 | Доступный интерактивный desktop | Использовать headless-render/screenshots и сохранить native failure evidence | Нет | Сообщено ограничение RDP session | Захват окна отклонён независимо Windows Graphics Capture и ffmpeg gdigrab; synthetic process закрыт | local-only native-evidence-blocker.md, headless PNG |
| EXEC | Обычная сборка и fix/re-review | 0.99 | Итоги full suites | Дождаться полных прогонов | Нет | Новых решений не требуется | `dotnet build src/Unlimotion.sln --nologo`: 0 ошибок, 105 предупреждений, 2m26s. Четыре code findings reviewer закрыты; поздний отказ покрыт UI-регрессией | Production diff; новые unit/UI/FlaUI tests; verified-build.log |
| EXEC | Полный прогон и потоковая регрессия | 0.99 | Повторный Headless после исправления | Пересобрать и повторить затронутые наборы | Нет | Пользователю сообщён собственный дефект реализации | Main 1499/1499 PASS (25m40s), затем Headless обнаружил cross-thread UpdateNavigationState в unified review flow. После poisoning UI thread прогон остановлен, не PASS. Dispatcher guards и Task.Run regression добавлены, независимый re-review закрыл находку по коду | FeedControl; FeedReadingPolishUiTests; full-main.log; full-headless.log |
| EXEC | Первый FlaUI attempt | 0.97 | Результат DPI-correct повторного теста | Исправить harness и повторить узко | Нет | Пользователю сообщено отличие тестового запуска | Новый тест520 не достиг requested width; остальные отменены fail-fast, runner завершился исключением отмены. Добавлен стандартный DPI preflight и actual-bounds diagnostic. Это не подтверждение product defect или общего native PASS | FeedReadingPolishFlaUiTests; full-flaui.log |
| EXEC | Финальные доступные проверки | 0.99 | Доступный interactive desktop для полного native маршрута | Передать результаты и запросить разблокированную сессию | Да | Асинхронный запрос уже отправлен, ответа не получено; ограничение повторено в финале | Build0 errors; targeted59/59; Headless49/49. Native3/3 проходит metadata и упирается в SendInput AccessDenied. Общая приёмка не объявляется10/10 | final-build.log; final-reading.log; final-headless.log; native-raw-peer.log; implementation-report.md |
| EXEC | Возобновление в активной сессии | 0.99 | Итоговые native/main результаты | Повторить оконный маршрут и full regression | Нет | Пользователь сообщил «Есть активная сессия» | Computer Use capture/scroll/Alt+Home работают. Native1000/1400 PASS. Для520 на новом DPI тест обновлён: existing overflow → real menu click; поиск popup ограничен окнами PID теста. Production не менялся | FeedReadingPolishFlaUiTests; active-native*.log; active-full-main.log; active-full-native.log |
