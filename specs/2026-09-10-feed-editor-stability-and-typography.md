# Лента: стабильность редактора, прокрутки и типографики

## 0. Метаданные

- Тип: UI bugfix, medium; ветка `feat/daily-feed`.
- Поверхность: Avalonia desktop; .NET 10 / Avalonia 12.0.3.
- Ограничения: до approval — только эта спека. Commit/push/release не входят в approval. Данные пользовательского vault не изменяются тестами.
- Evidence: headless и FlaUI UI-тесты, нативный осмотр с синтетическим vault; видео/screenshot при доступности.

## 1. Цель

Сделать работу с ежедневной лентой предсказуемой: одно контекстное меню, отсутствие дезориентирующих прыжков при подгрузке, один щелчок для перехода между блоками и визуально стабильные списки, задачи, заголовки и области.

Success means: все девять наблюдаемых сценариев ниже работают без потери Markdown, selection или позиции чтения.

## 2. AS-IS и корневая проблема

- `MarkdownBlockLivePreviewEditor` одновременно обрабатывает `PointerPressed` для TextBox и `ContextRequested` строки: штатное TextBox menu остаётся доступным рядом с меню ленты.
- `FeedControl.TryLoadOlderDaysFromCurrentPositionAsync` вызывает `LoadOlderDaysAsync` у нижней границы без сохранения visual anchor; collapse и добавление новых элементов меняют extent/offset.
- `BeginEdit` отменяет активный edit, но click по preview второго блока сначала завершает первый, не повторяя intent для второго.
- Preview и TextBox используют разные list/task marker geometry; заголовок дня находится вне `ReadingColumn`.
- Area management использует явную кнопку сохранения без пользовательской надписи; карточки приложения в других местах сохраняют draft автоматически.
- Toggle task меняет Markdown, но карточка связанной задачи не получает немедленный refresh.

## 3. TO-BE дизайн

### 3.1 Взаимодействия

| № | Trigger | Итог |
| --- | --- | --- |
| 1 | ПКМ на preview или TextBox | Открыто ровно одно меню ленты; стандартное TextBox menu подавлено. В меню редактируемого текста есть Cut/Copy/Paste/Select all. |
| 2 | Collapse дня или достижение низа при чтении | До structural change запоминается верхний видимый день/смещение; после загрузки или collapse он остаётся на том же экранном месте. Автоподгрузка не запускается из programmatic offset/layout restore. |
| 3 | Один ЛКМ по другому editable блоку | Текущий block commit/cancel проходит как сейчас, а второй сразу становится editor/focus target. Ошибка save оставляет первый edit активным и не открывает второй. |
| 4 | Enter edit numbered item | Номер и текст сохраняют x-позицию и расстояние между marker/text; editor получает content inset, равный preview marker column. |
| 5 | Enter edit task item | Checkbox/status marker имеет высоту строки текста и baseline/alignment не меняют высоту строки. |
| 6 | Изменить name/parent/folder/root task области | Draft дебаунсно сохраняется автоматически; явная безымянная submit-кнопка удаляется. Ошибка остаётся видимой и не затирает draft. |
| 7 | Нажать checkbox task в заметке | Markdown сохраняется и связанная задача/карточка получает refresh сразу после успешной записи; при ошибке status UI откатывается/показывает error. |
| 8 | Wide reading column | Collapse affordance, date/file title и editor имеют один reading-column left edge/max width; tooltip полного path сохраняется. |
| 9 | `#`…`######` | Heading font-size строго убывает с level; H6 больше body text. Preview и edit используют согласованную иерархию. |

### 3.2 Технические границы

- Контекстное меню: один route, `Handled=true` до default handler; не создавать persistent menu на каждом pointer event после закрытия.
- Scroll anchoring: anchor — первый реально видимый day header + local Y. Восстанавливать лишь после `LoadOlderDaysAsync`/collapse, не после user scroll; guard предотвращает повторную загрузку и re-entrant events.
- Автосохранение области: debounce + serial generation; close/dispose отменяет pending write; reload не перетирает более новый local draft.
- Task refresh ограничен текущим source/vault и только успешной Markdown write; не менять task, если item не имеет связанной Unlimotion task.
- Не менять формат Markdown и существующие операции drag/drop/selection.

### 3.3 Non-goals

- Не менять модель областей, task roots, Eremex licensing, формат файлов, поведение других редакторов или общую типографику задач.
- Не устранять отдельно известные баги multi-block native drag и task-storage watcher: они не являются частью этих девяти пунктов.

## 4. Acceptance-to-Test Matrix

| AC | Автоматическая проверка | Evidence |
| --- | --- | --- |
| Один menu | Headless context menu + FlaUI text/preview ПКМ | automation tree/screenshot |
| Stable anchor | Headless ScrollViewer offset + FlaUI scroll/collapse fixture | before/after coordinates |
| One-click switch | Headless pointer click between two dirty editors | focus + saved text |
| Number/task geometry | Headless bounds preview/editor | x/y delta <= 1 DIP |
| Area autosave | AreaManagement UI test with debounce/error | vault JSON readback |
| Task refresh | conversion/toggle UI test | immediate status assertion |
| Reading alignment/headings | Headless bounds and heading levels 1–6 | screenshot on wide window |

Commands: targeted `dotnet run --project src/Unlimotion.Test -- --treenode-filter ... --maximum-parallel-tests 1`; affected FlaUI serial run; normal desktop build. Stop on save/data-loss regression, duplicate menu, or unstable anchor.

## 5. Риски и решения

| Risk | Mitigation |
| --- | --- |
| Anchor restore loops back into load-more | scoped restoration guard and test that load count is one |
| Autosave floods vault on typing | debounce/latest-generation write |
| Default TextBox menu is platform dependent | assert only one process menu and explicitly mark native event handled |
| Font metrics vary by OS | test relative geometry, not exact font pixels; native Windows visual check |

## 6. Файлы

`MarkdownBlockLivePreviewEditor*.cs/.axaml`, `MarkdownBlockPreviewControl.cs`, `FeedControl*.cs/.axaml`, `AreaManagement*.cs/.axaml`, feed/task refresh path, `Strings*.resx`, existing/new UI tests.

## 7. Review

- UX: PASS — one action route, anchor-preserving reading, no hidden confirmation.
- Architecture: PASS — scroll, editor, area persistence and task refresh remain separately owned.
- Tester: PASS — each visible behavior has headless/native evidence.
- Open questions: нет; autosave debounce принимается агентом как 400 ms, без дополнительного user choice.

## Approval

Ожидается точная фраза: `Спеку подтверждаю`.

## Журнал

| Фаза | Результат |
| --- | --- |
| SPEC | Прочитаны существующие handlers context menu, chronology scroll и предыдущая approved feed spec; сформирован контракт девяти правок. |
| EXEC | После подтверждения реализованы единый route контекстного меню, защита scroll-anchor, переход между блоками одним кликом, компактные маркеры задач, автосохранение области, выравнивание reading column и иерархия заголовков. После rebase на `origin/main` выполнены целевые headless-проверки: `MarkdownLivePreviewEditorUiTests` 34/34 и `FeedAreaRootTaskUiTests` 6/6; `git diff --check` чист. |
