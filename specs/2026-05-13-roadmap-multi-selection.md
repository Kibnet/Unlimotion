# Roadmap multi-selection

## 0. Метаданные
- Тип (профиль): delivery-task; stack profile `dotnet-desktop-client`; overlay profile `ui-automation-testing`.
- Владелец: Codex / Unlimotion desktop UI.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка.
- Ограничения: central `QUEST` требует SPEC-first и подтверждение фразой `Спеку подтверждаю` до изменений кода; локальный override требует UI tests для UI-facing изменений.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `C:\Users\Kibnet\.codex\agents\instructions\governance\routing-matrix.md`, `C:\Users\Kibnet\.codex\agents\instructions\core\quest-mode.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\dotnet-desktop-client.md`, `C:\Users\Kibnet\.codex\agents\instructions\profiles\ui-automation-testing.md`.

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Добавить на вкладку Roadmap множественный выбор задач с двумя визуальными состояниями:
- текущая задача, открытая/выбранная в правой карточке через `MainWindowViewModel.CurrentTaskItem`;
- набор roadmap-задач, выделенных пользователем кликами или прямоугольником выделения.

Outcome contract:
- Success means: пользователь видит текущую открытую задачу и выделенные задачи на roadmap, а клики и прямоугольник выделения меняют набор выделения по правилам `None/Ctrl/Shift/Alt`.
- Amendment 2026-05-14: выделенные roadmap-задачи участвуют в drag/drop массово, как selection в остальных вкладках.
- Итоговый артефакт / output: scoped changes в `GraphControl`/roadmap node state плюс Avalonia.Headless regression UI tests.
- Stop rules: не менять persisted task data, не менять roadmap layout algorithm, не завершать EXEC без релевантных UI tests или явного blocker report.

## 2. Текущее состояние (AS-IS)
- Roadmap живёт в `src/Unlimotion/Views/GraphControl.axaml` и рендерится через `nodify:NodifyEditor` с `RoadmapNodes` и `RoadmapConnections`.
- Roadmap node представлен `RoadmapNode` в `src/Unlimotion/Views/Graph/RoadmapNode.cs`; сейчас он хранит `TaskItem`, `Location`, `Width`, anchors и не имеет state выбора.
- Визуальная карточка node в `GraphControl.axaml` сейчас имеет прозрачный `Border`, `CheckBox`, emoji/marker и title. Есть только `Classes.highlighted` для поиска и task-state classes `IsWanted` / `IsCanBeCompleted`.
- `GraphControl.InputElement_OnPointerPressed` уже выбирает clicked task через `SelectRoadmapTask`, а `InputElement_OnPointerMoved` запускает drag roadmap task после threshold.
- Right-button drag поверх task уже используется для pan viewport; node drag source и drop behavior не должны регрессировать.
- `MainWindowViewModel.CurrentTaskItem` и `SelectCurrentTask()` остаются текущим механизмом opened/current task; `CurrentGraphItem` синхронизируется через существующий VM path.
- Обычные tree tabs уже поддерживают массовый drag/drop через batch drag payload из выбранных `TaskWrapperViewModel` и общий `MainControl.Drop`.
- Roadmap single drag сейчас кладёт в `GraphControl.CustomDataFormat` один `TaskItemViewModel`; общий drop умеет принимать этот single graph payload, но не получает roadmap selection batch.
- В `src/Unlimotion.Test/RoadmapGraphUiTests.cs` уже есть headless UI coverage для roadmap rendering, click/double-click, drag threshold, pan, hotkeys и overlay behavior.

## 3. Проблема
Roadmap не имеет отдельного множества выделенных задач и визуально не различает открытую текущую задачу от выделенных задач, поэтому пользователь не может выбирать несколько задач на графе ожидаемыми click/rectangle gestures.

## 4. Цели дизайна
- Разделение ответственности: transient roadmap selection хранить в `GraphControl`/`RoadmapNode`, а opened task оставить в `MainWindowViewModel.CurrentTaskItem`.
- Повторное использование: использовать существующие pointer handlers, viewport adapter и headless UI helpers без нового доменного API.
- Тестируемость: покрыть click selection и rectangle selection через Avalonia.Headless UI tests.
- Консистентность: modifier semantics одинаковы для click и rectangle selection.
- Обратная совместимость: не менять persisted model, task storage, graph builder layout и существующие automation ids.

## 5. Non-Goals (чего НЕ делаем)
- Не добавлять batch delete/copy commands для roadmap selection вне drag/drop flow.
- Не менять drag-drop relation behavior, node drag threshold, right-button pan и double-click details toggle.
- Не внедрять range-selection между anchor/current items; по запросу `Shift+click` именно добавляет clicked task.
- Не сохранять roadmap selection между перезапусками.
- Не менять алгоритм `RoadmapGraphBuilder` и порядок/координаты node layout.
- Не менять tree tab multiple selection.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/Graph/RoadmapNode.cs` -> добавить observable flags для visual state: `IsSelected`, `IsCurrent`.
- `src/Unlimotion/Views/GraphControl.axaml.cs` -> хранить selected task ids, применять selection operations, синхронизировать `IsCurrent` с owner VM, обрабатывать marquee selection lifecycle.
- `src/Unlimotion/Views/GraphControl.axaml.cs` -> при drag start по выбранной roadmap node отдавать batch payload выбранных visible tasks.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> расширить общий drop parser, чтобы он принимал roadmap batch payload рядом с tree batch payload.
- `src/Unlimotion/Views/GraphControl.axaml` -> добавить visual styling node border для `selected/current`, pointer handler на editor background, overlay rectangle selection.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` -> добавить/обновить Avalonia.Headless UI tests для click modifiers, rectangle modifiers и visual state bindings.
- `specs/2026-05-13-roadmap-multi-selection.md` -> рабочая спецификация и журнал.

### 6.2 Детальный дизайн
- Roadmap selection state:
  - `GraphControl` хранит `HashSet<string>` selected roadmap task ids.
  - `RoadmapNode.IsSelected` отражает membership id в selected set.
  - `RoadmapNode.IsCurrent` отражает `owner.CurrentTaskItem?.Id == node.Id`.
  - После `ApplyProjection` state применяется к reused/new nodes; selection ids, которых больше нет среди visible `RoadmapNodes`, prune-ятся до visible set.
  - Выделение касается только видимых roadmap-задач: если задача скрыта фильтрами roadmap, она теряет выделение.
  - Поиск не меняет состав roadmap nodes и не должен менять выделение; он только обновляет `TaskItemViewModel.IsHighlighted` / visual highlight.
- Current task synchronization:
  - При кликах без удаления task из selection clicked task становится `CurrentTaskItem` через существующий `SelectRoadmapTask`.
  - При external change `CurrentTaskItem` нужно обновить node `IsCurrent`; допустимо подписаться на owner VM через `WhenAnyValue(m => m.CurrentTaskItem)` в roadmap control scope.
  - Rectangle selection меняет только selected set и не переключает открытую карточку, чтобы не открывать произвольную задачу при массовом выделении.
- Click selection semantics:
  - Plain click: selected set = только clicked task; clicked task становится current/opened.
  - `Ctrl+click`: если clicked task selected, убрать её; иначе добавить. При добавлении clicked task становится current/opened; при удалении current/opened task не меняется.
  - `Shift+click`: добавить clicked task; clicked task становится current/opened.
  - `Alt+click`: убрать clicked task; current/opened task не меняется.
  - Если нажато несколько selection modifiers одновременно, precedence: `Alt` remove, иначе `Ctrl` toggle, иначе `Shift` add, иначе replace.
- Double-click selection semantics:
  - Double-click без modifiers применяет plain click selection один раз за жест и затем переключает details pane.
  - Double-click с `Ctrl`/`Shift`/`Alt` применяет соответствующую modifier selection operation один раз за жест и затем переключает details pane.
  - Второй press/click внутри double-click не должен повторно менять selected set, чтобы `Ctrl+double-click` не превращался в toggle туда-обратно.
- Rectangle selection semantics:
  - Rectangle gesture начинается левым drag по пустой области `RoadmapEditor`, а не по node, чтобы не конфликтовать с roadmap node drag.
  - Plain rectangle: selected set = все visible nodes, whose rendered bounds intersect rectangle.
  - `Ctrl+rectangle`: invert selection state for all intersecting nodes.
  - `Shift+rectangle`: add all intersecting nodes.
  - `Alt+rectangle`: remove all intersecting nodes.
  - Rectangle with no hits applies the same operation to empty hit set; for plain rectangle this clears selection.
  - Click on empty editor without drag threshold does not change current task; selection clearing is done by plain rectangle result, not by accidental background click.
- Rectangle hit testing:
  - Track pointer start/current points in the same visual coordinate space that draws the selection rectangle.
  - Collect rendered node `Border` controls by `DataContext is RoadmapNode`.
  - Use `TranslatePoint` + `Bounds` to compare rendered node bounds with selection rectangle in that same coordinate space. This avoids depending on raw layout coordinates and keeps overlay drawing and hit testing aligned after viewport transforms or non-zero editor offset.
- Visual design:
  - Add classes/bindings on node `Border`: `roadmapSelected`, `roadmapCurrent`.
  - Selected nodes receive visible fill/border; current node receives a distinct stronger border/accent. If both states apply, combined state must be readable.
  - Search highlight (`IsHighlighted`) on text remains independent.
  - Add selection rectangle overlay with semi-transparent fill and border, `IsHitTestVisible=false`, stable automation id such as `RoadmapSelectionRectangle`.
- Output contract / evidence rules:
  - UI tests must assert both state (`RoadmapNode.IsSelected`/`IsCurrent`) and at least one visual binding/class or visible selection rectangle behavior.
  - Existing tests for node click, node drag threshold, right-drag pan, double-click and hotkeys must continue to pass.
- Performance:
  - Selection operations are O(visible nodes) only during pointer actions.
  - No rebuild is triggered by selection state changes.
  - No background task or storage mutation is introduced.

### 6.3 Amendment 2026-05-14: batch drag/drop выбранных roadmap-задач
- Roadmap drag source:
  - Если drag начинается plain left-drag с выбранной roadmap node и selected set содержит больше одной visible task, drag payload должен содержать все выбранные visible roadmap tasks.
  - Если drag начинается с невыбранной node, поведение остаётся single: task становится единственной selected/current и drag payload содержит только её.
  - Если plain click по уже выбранной node не переходит threshold drag, selection после release схлопывается до clicked task, как и текущий plain click contract.
  - Чтобы не конфликтовать с selection modifiers, drag candidate стартует только без `Ctrl`/`Shift`/`Alt` на исходном pointer press; operation modifiers можно нажимать уже во время drag/drop, как в остальных вкладках.
- Drop behavior:
  - Roadmap batch payload должен идти через существующий `MainControl.Drop` и использовать те же operation modifiers:
    - none -> copy into target;
    - `Shift` -> move into target;
    - `Ctrl` -> dragged sources block target;
    - `Alt` -> target blocks dragged sources;
    - `Ctrl+Shift` -> clone into target.
  - Для batch operations сохраняются существующие проверки `CanMoveInto` / `CanCreateBlockingRelation`, batch confirmation и error toast.
  - Roadmap не имеет wrapper source context. Поэтому `Shift` move для roadmap batch разрешён только для source tasks, где source parent можно однозначно вывести так же, как для single `TaskItemViewModel` graph drag: `Parents.Count <= 1`; иначе операция отклоняется существующим `MoveMissingParent` / batch error.
  - Drop на одну из dragged selected tasks должен отклоняться существующими validation rules.
- Selection/current interaction:
  - Начало drag по уже выбранной node не должно визуально сбрасывать multi-selection до пересечения drag threshold.
  - После успешного или отменённого batch drag selected set остаётся выделенным.
  - Rectangle selection и click modifier selection остаются transient visible-only state; hidden-by-filter tasks не попадают в batch payload.

## 7. Бизнес-правила / Алгоритмы
Selection operation table:

| Gesture | Modifier | Operation on hit tasks | Current/opened task |
| --- | --- | --- | --- |
| Click task | none | Replace selection with clicked task | clicked task |
| Click task | Ctrl | Toggle clicked task | clicked task only when added |
| Click task | Shift | Add clicked task | clicked task |
| Click task | Alt | Remove clicked task | unchanged |
| Double-click task | none | Replace selection with clicked task once | clicked task, then toggle details |
| Double-click task | Ctrl | Toggle clicked task once | clicked task only when added, then toggle details |
| Double-click task | Shift | Add clicked task once | clicked task, then toggle details |
| Double-click task | Alt | Remove clicked task once | unchanged, then toggle details |
| Rectangle | none | Replace selection with intersecting tasks | unchanged |
| Rectangle | Ctrl | Toggle every intersecting task | unchanged |
| Rectangle | Shift | Add every intersecting task | unchanged |
| Rectangle | Alt | Remove every intersecting task | unchanged |
| Drag selected roadmap node | none on press | Drag all selected visible tasks if more than one selected; otherwise drag clicked task | clicked task/current unchanged except existing click sync |
| Drop roadmap batch | none / Ctrl / Shift / Alt / Ctrl+Shift | Same batch operation semantics as tree tabs | unchanged by drop target selection |

Invariants:
- `IsCurrent` is not the same as `IsSelected`; a node may be current without being selected and selected without being current.
- Selection is local to visible roadmap nodes and transient.
- Фильтры roadmap могут убирать задачи из visible set, и тогда выделение скрытых задач сбрасывается.
- Поиск не является фильтром состава roadmap nodes в рамках этой задачи и не меняет selected set.
- Double-click применяет click selection semantics только один раз за gesture, включая modifier variants.
- Node drag starts only from node left-drag after threshold; rectangle selection starts only from editor empty-space left-drag after threshold.
- Batch roadmap drag includes only currently visible selected roadmap nodes.
- Selection modifiers on pointer press are selection gestures; drag operation modifiers are evaluated by `DragOver`/`Drop`.

## 8. Точки интеграции и триггеры
- `GraphControl.DataContextChanged` / owner VM discovery -> subscribe/unsubscribe current task changes.
- `ApplyProjection` -> reuse nodes, then apply selection/current flags.
- Roadmap node `PointerPressed` -> click selection operation plus existing current selection and double-click behavior.
- Roadmap node `PointerMoved` -> existing pending drag behavior unchanged.
- Roadmap editor/background `PointerPressed` -> start pending rectangle selection candidate when left button starts outside task node.
- `GraphControl` existing tunnel/bubble `PointerMoved`/`PointerReleased` handlers -> update/commit rectangle selection or keep existing node drag/pan paths.
- `GraphControl.StartRoadmapDragAsync` -> build single or batch roadmap drag payload.
- `MainControl.TryBuildOperationItems` / drag data recognition -> accept roadmap batch task payload in addition to tree batch wrapper payload and single task payload.
- XAML overlay -> visualize active rectangle.

## 9. Изменения модели данных / состояния
- Новые runtime-only properties:
  - `RoadmapNode.IsSelected`.
  - `RoadmapNode.IsCurrent`.
  - `GraphControl` private selected id set and pending rectangle context.
  - `GraphControl` runtime-only roadmap batch drag data object.
  - `GraphControl` styled/direct properties for rectangle overlay visibility/coordinates/sizes if needed for binding.
- Persisted state: не применимо, хранилище задач не меняется.
- Public API: не планируется. Если tests требуют inspection, использовать public `RoadmapNode` properties because `RoadmapNodes` is already public on `GraphControl`.

## 10. Миграция / Rollout / Rollback
- Миграция данных: не применимо.
- Первый запуск: selection пустой; current node подсвечивается только если `CurrentTaskItem` уже задан и roadmap открыт.
- Rollout: обычный desktop build.
- Rollback: откатить изменения в `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapNode.cs`, UI tests и эту спецификацию; пользовательские данные не затрагиваются.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. При открытой Roadmap текущая `CurrentTaskItem` visually marked as current node.
  2. Plain click по task node оставляет selected set из одной task и делает clicked task current/opened.
  3. `Ctrl+click` добавляет unselected task и убирает selected task без сброса остальных selected tasks.
  4. `Shift+click` добавляет clicked task к selection.
  5. `Alt+click` убирает clicked task из selection.
  6. Plain rectangle по пустой области editor выбирает все visible nodes, пересекающиеся с rectangle, и заменяет прежнюю selection.
  7. `Ctrl+rectangle` инвертирует selection для всех hit nodes.
  8. `Shift+rectangle` добавляет все hit nodes к selection.
  9. `Alt+rectangle` убирает все hit nodes из selection.
  10. Rectangle overlay виден во время drag и скрывается после release/cancel.
  11. Rectangle selection не меняет `CurrentTaskItem`.
  12. Node left-drag после threshold всё ещё запускает roadmap drag source, а right-drag поверх node всё ещё двигает viewport и не выбирает задачу.
  13. Double-click roadmap node продолжает переключать details pane и применяет click/modifier selection semantics ровно один раз за gesture.
  14. Если roadmap-фильтр скрывает selected task, эта task исчезает из selected set; при возврате фильтра она не становится selected автоматически.
  15. Изменение поискового текста и search highlight не меняют selected set.
  16. Rectangle overlay и rectangle hit testing используют одну visual coordinate space; после pan/zoom или non-zero editor offset hit nodes соответствуют видимому прямоугольнику.
  17. Plain drag с выбранной roadmap node при multi-selection создаёт batch drop operation для всех selected visible tasks.
  18. Plain drag с невыбранной roadmap node остаётся single drag и схлопывает selection до dragged task.
  19. Plain press/release по выбранной roadmap node без drag threshold всё ещё выполняет обычный plain click и оставляет выбранной только clicked task.
  20. Roadmap batch drop использует те же operation modifiers и validation, что tree batch drop; как минимум UI test должен проверить массовое создание blocking relation или copy/move effect для двух выбранных roadmap tasks.
- Какие тесты добавить/изменить:
  - Добавить tests в `RoadmapGraphUiTests`:
    - `RoadmapGraph_NodeClickSelection_AppliesModifierSemanticsAndVisualState`.
    - `RoadmapGraph_RectangleSelection_AppliesModifierSemanticsAndDoesNotChangeCurrentTask`.
    - `RoadmapGraph_SelectedNodesDragDrop_AppliesBatchOperation`.
    - `RoadmapGraph_SelectedNodePlainClickWithoutDrag_CollapsesSelection`.
    - При необходимости обновить existing node drag/pan tests только для новых assertions, не ослабляя их.
  - UI tests должны использовать headless `Window.MouseDown/MouseMove/MouseUp` и `RawInputModifiers.Control/Shift/Alt`.
  - Для visual evidence проверить `RoadmapNode.IsSelected`/`IsCurrent` и наличие соответствующих classes на node `Border` либо effective visual property after binding.
  - Для rectangle выбрать стабильные visible nodes через existing `WaitForTaskNode` helper и построить drag rectangle по translated bounds.
- Characterization tests:
  - Existing roadmap tests for drag threshold, right pan, double click, hotkeys, build/update должны остаться зелёными.
- Команды для проверки:
  - `dotnet build src\Unlimotion\Unlimotion.csproj`
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj`
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted roadmap UI tests падают, не завершать EXEC.
  - Если full test run падает на unrelated existing issue, зафиксировать конкретную ошибку и предоставить targeted UI + build evidence.

## 12. Риски и edge cases
- `NodifyEditor` может обрабатывать pointer events before/after `GraphControl`; для rectangle нужно использовать уже существующий `handledEventsToo` path или explicit editor handler так, чтобы не ломать node drag.
- Pointer capture для rectangle не должен перехватывать node drag/pan gestures.
- Rectangle start over child controls внутри node должен считаться node gesture, не background rectangle.
- Selection state может пережить graph rebuild; prune to visible nodes предотвращает stale selection.
- Фильтры и поиск имеют разный контракт: фильтры меняют visible set и могут сбрасывать selection скрытых задач, поиск только подсвечивает совпадения и не должен сбрасывать selection.
- Current task может быть filtered out; тогда ни один visible node не имеет `IsCurrent`, но `CurrentTaskItem` не меняется.
- Roadmap batch drag не имеет tree wrapper parent context; move для tasks с несколькими parent должен быть явно отклонён, а не выбирать случайный parent.
- Plain drag по already-selected node требует deferred click collapse: нельзя сбрасывать selection на pointer press до того, как понятно, click это или drag.
- `Alt` modifier в headless tests и на разных ОС может иметь platform nuances; tests должны использовать Avalonia `RawInputModifiers.Alt`.
- Visual states selected/current/search-highlight must remain readable together.

## 13. План выполнения
1. Добавить failing Avalonia.Headless tests для click modifier semantics и rectangle selection semantics.
2. Добавить `IsSelected`/`IsCurrent` в `RoadmapNode`.
3. Добавить selection state и click modifier handling в `GraphControl`.
4. Добавить rectangle selection lifecycle, hit testing и overlay properties в `GraphControl`.
5. Обновить `GraphControl.axaml` styles, node classes и overlay rectangle.
6. Amendment 2026-05-14: добавить roadmap batch drag payload и поддержку этого payload в `MainControl.Drop`.
7. Amendment 2026-05-14: обновить click-vs-drag lifecycle для plain drag по already-selected roadmap node.
8. Прогнать targeted UI tests, build, затем full test run или documented blocker.
9. Выполнить post-EXEC review: scope, regressions, UI test evidence, stale comments, unintended storage/layout changes.

## 14. Открытые вопросы
Нет блокирующих вопросов.

Принято:
- Rectangle selection стартует только с пустой области editor, потому что drag от node уже занят существующим переносом task node.
- Выделение касается только видимых задач; задачи, скрытые roadmap-фильтрами, теряют выделение.
- Поиск не меняет состав задач и не меняет выделение, он только подкрашивает найденные задачи.
- Double-click с зажатыми `Ctrl`/`Shift`/`Alt` меняет выделение по modifier semantics, но только один раз за double-click gesture.

Amendment 2026-05-14 подтверждён пользователем после изменения прежнего Non-Goal про batch drag.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`; local UI testing override.
- Выполненные требования профиля:
  - UI behavior покрывается Avalonia.Headless UI tests.
  - Изменения не блокируют UI thread длительными синхронными операциями.
  - Stable automation ids сохраняются; новый automation id добавляется только для rectangle overlay.
  - Проверки используют TUnit/Microsoft.Testing.Platform style через `dotnet run --project ... -- --treenode-filter`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/Graph/RoadmapNode.cs` | Добавить observable visual state `IsSelected`/`IsCurrent` | Node template должен отображать selection/current state |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Selection set, click modifiers, rectangle selection lifecycle, current sync | Основное UI behavior roadmap multi-selection |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Roadmap batch drag payload parsing in common drop flow | Массовый drop выбранных roadmap-задач должен использовать существующий batch drop engine |
| `src/Unlimotion/Views/GraphControl.axaml` | Node visual classes/styles и rectangle overlay | Пользователь должен видеть current/selected/drag rectangle |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` | Regression UI tests для click/rectangle selection | Локальный override и central profile требуют UI coverage |
| `specs/2026-05-13-roadmap-multi-selection.md` | Рабочая спецификация и журнал | QUEST gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Current roadmap task visual | Нет отдельной подсветки opened/current task | `RoadmapNode.IsCurrent` driving distinct visual state |
| Roadmap selected tasks | Нет multi-selection state | Local selected id set + `RoadmapNode.IsSelected` |
| Plain click | Выбирает current task | Выбирает current task и заменяет selection одним node |
| Ctrl/Shift/Alt click | Нет roadmap selection semantics | Toggle/add/remove по clicked node |
| Rectangle selection | Нет | Replace/toggle/add/remove по rendered node bounds |
| Roadmap drag source | Всегда single `TaskItemViewModel` | Single для невыбранной dragged task, batch для selected visible roadmap tasks |
| Roadmap batch drop | Не поддержан | Общий `MainControl.Drop` применяет existing batch operation semantics к roadmap batch payload |
| Storage/model | Не меняется | Не меняется |

## 18. Альтернативы и компромиссы
- Вариант: хранить roadmap selection в `MainWindowViewModel`.
- Плюсы: selection могла бы стать общей для будущих batch commands.
- Минусы: расширяет VM public surface и создаёт продуктовый контракт для batch operations, которые не входят в запрос.
- Почему выбранное решение лучше в контексте этой задачи: пользователю нужна UI selection на вкладке roadmap; local transient state минимален и не меняет доменную модель.

- Вариант: использовать built-in selection `NodifyEditor`.
- Плюсы: потенциально меньше code-behind для selected nodes.
- Минусы: `ItemContainer` сейчас `IsSelectable=False`, roadmap уже имеет custom pointer drag/drop behavior; built-in selection может конфликтовать с task drag, pan и existing tests.
- Почему выбранное решение лучше в контексте этой задачи: custom local state точнее контролирует modifier semantics и не ломает существующие roadmap gestures.

- Вариант: rectangle selection может стартовать с node.
- Плюсы: пользователь мог бы начинать рамку из любой точки.
- Минусы: конфликтует с существующим node drag source после threshold.
- Почему выбранное решение лучше в контексте этой задачи: empty-space rectangle сохраняет текущий task drag contract.

- Вариант: конвертировать selected roadmap tasks в synthetic `TaskWrapperViewModel`.
- Плюсы: можно переиспользовать tree batch payload без изменений общего drop parser.
- Минусы: roadmap не имеет wrapper path/source parent context; synthetic wrappers могут создать ложный источник для move и скрыть ambiguous multi-parent case.
- Почему выбранное решение лучше в контексте этой задачи: отдельный roadmap batch payload честно передаёт только task identities, а общий drop parser явно применяет те же ограничения, что single graph drag.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, алгоритмы, интеграции, state и rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Persisted data не меняется, rollback code-only, edge cases перечислены. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, UI tests и команды TUnit/MTP указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Этапы и компромиссы определены, блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | .NET desktop + UI automation + local UI test requirement учтены. |

Итог: ГОТОВО.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | --- | --- |
| 1. Ясность цели и границ | 5 | Описаны две визуальные роли и точные Non-Goals. |
| 2. Понимание текущего состояния | 5 | Зафиксированы `GraphControl`, `RoadmapNode`, VM current task и existing tests. |
| 3. Конкретность целевого дизайна | 5 | Есть state model, click/rectangle tables, hit-testing и visual contract. |
| 4. Безопасность (миграция, откат) | 5 | Нет persisted изменений; rollback локальный. |
| 5. Тестируемость | 5 | Есть targeted UI tests и конкретные commands. |
| 6. Готовность к автономной реализации | 5 | План линейный, открытых блокеров нет. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению.

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлены явные правила различения `IsCurrent` и `IsSelected`; уточнено, что rectangle selection не меняет `CurrentTaskItem`; зафиксирован modifier precedence для конфликтующих modifiers; добавлен prune selected ids после rebuild; добавлены regression constraints для existing node drag и right pan; после review уточнено, что roadmap-фильтры сбрасывают выделение скрытых задач, а поиск выделение не меняет; double-click с modifiers применяет selection semantics один раз за gesture; hit testing и overlay rectangle привязаны к одной visual coordinate space.
- Что осталось на решение пользователя: требуется утверждение спеки фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: после первичного падения targeted roadmap UI tests исправлена setup-логика preselected task в filter/search сценариях; повторный targeted roadmap прогон зелёный.
- Что исправлено до завершения amendment 2026-05-14: добавлен отдельный roadmap batch drag payload вместо synthetic wrappers; plain click по already-selected roadmap node перенесён на release, чтобы drag threshold не сбрасывал multi-selection; common drop parser расширен без изменения tree batch payload.
- Что исправлено по review comments: marquee hit-testing ограничен main `RoadmapEditor`, чтобы не учитывать minimap item borders; double-click details toggle больше не принудительно меняет `CurrentTaskItem`, если modifier selection operation должна оставить current unchanged; duplicate-selection suppression для double-click теперь опирается на `ClickCount > 1` без локального `500ms` окна; после rebase на актуальный `origin/main` сохранена совместимость с inline-редактированием roadmap title.
- Что проверено дополнительно для refactor / comments: просмотрен diff на выход за scope, скрытые storage/layout изменения, stale comments и regression risk в pointer gesture lifecycle; после rebase `git diff --check` не нашёл whitespace errors; `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -v:minimal`, `dotnet build src\Unlimotion\Unlimotion.csproj -v:minimal`, Roadmap UI tests 43/43, новый targeted double-click interval regression 1/1 и MainControlTreeCommands UI tests 34/34 прошли.
- Остаточные риски / follow-ups: полный прогон `dotnet run --no-build --project src\Unlimotion.Test\Unlimotion.Test.csproj` до rebase дважды был остановлен по timeout 10 минут без диагностического вывода; после rebase повторно остановлен по timeout 15 минут без диагностического вывода и оставшийся `dotnet` process был завершён.

## Approval
Получено подтверждение пользователя для исходной версии: "Спеку подтверждаю".

Получено подтверждение пользователя для amendment 2026-05-14: "спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор instruction stack и UI-контекста | 0.95 | Нет | Создать рабочую спецификацию | Да, перед EXEC | Нет | Central QUEST требует SPEC-first; локальный override требует UI tests для UI-facing roadmap behavior | `AGENTS.md`, `AGENTS.override.md`, central instructions |
| SPEC | Анализ AS-IS roadmap selection | 0.90 | Нужен EXEC test-first для точного поведения hit testing | Зафиксировать TO-BE и tests | Да, перед EXEC | Нет | Roadmap node click/drag/pan уже живут в `GraphControl`, поэтому selection нужно встроить без изменения storage/layout | `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapNode.cs`, `RoadmapGraphUiTests.cs` |
| SPEC | SPEC quality gate и post-SPEC review | 0.94 | Нет | Дождаться утверждения спеки | Да | Да, ожидается фраза `Спеку подтверждаю` | Spec прошёл linter/rubric; до approval central QUEST запрещает менять код | `specs/2026-05-13-roadmap-multi-selection.md` |
| SPEC | Уточнение filter/search semantics | 0.95 | Нужно уточнить double-click modifiers | Задать вопрос пользователю | Да | Да, пользователь уточнил filter/search behavior | Зафиксировано: выделение только visible tasks; фильтры сбрасывают selection скрытых задач; поиск только подсвечивает и не меняет selection | `specs/2026-05-13-roadmap-multi-selection.md` |
| SPEC | Уточнение double-click modifiers | 0.95 | Нет | Дождаться утверждения спеки | Да | Да, пользователь уточнил double-click behavior | Зафиксировано: double-click с `Ctrl`/`Shift`/`Alt` меняет selection по modifier semantics один раз за gesture и затем переключает details pane | `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Подтверждение спеки | 0.95 | Нет | Добавить UI tests и реализацию | Нет | Да, пользователь подтвердил фразой `Спеку подтверждаю` | Переход в EXEC разрешён central QUEST contract | `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Реализация roadmap multi-selection | 0.90 | Нет | Запустить targeted UI tests и сборки | Нет | Нет | Добавлены runtime-only state flags, click/rectangle selection lifecycle, visual classes и overlay без изменений storage/layout builder | `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapNode.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Проверка targeted UI behavior | 0.92 | Нет | Запустить broader checks | Нет | Нет | Targeted roadmap UI tests сначала выявили setup issue, после исправления прошли; related tree command UI tests также прошли | `RoadmapGraphUiTests.cs` |
| EXEC | Финальная validation и post-EXEC review | 0.88 | Полный test suite не завершился за 10 минут | Подготовить итоговый отчёт | Нет | Нет | Сборки приложения и тестового проекта прошли; `git diff --check` чистый по whitespace errors; полный прогон зафиксирован как остаточный риск из-за timeout | `specs/2026-05-13-roadmap-multi-selection.md` |
| SPEC | Amendment: roadmap batch drag/drop | 0.88 | Нужно подтверждение изменения scope | Дождаться повторного утверждения amendment | Да | Нет | Пользователь указал недоделку: selected roadmap tasks должны drag/drop-аться массово как в остальных вкладках; это отменяет прежний Non-Goal про batch drag | `specs/2026-05-13-roadmap-multi-selection.md`, `GraphControl.axaml.cs`, `MainControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Подтверждение amendment 2026-05-14 | 0.95 | Нет | Реализовать batch drag/drop | Нет | Да, пользователь подтвердил фразой `спеку подтверждаю` | Переход в EXEC для amendment разрешён QUEST contract | `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Реализация roadmap batch drag/drop | 0.90 | Нет | Прогнать targeted UI tests и сборки | Нет | Нет | Roadmap drag теперь отдаёт batch payload для selected visible tasks; common drop parser применяет existing batch operation semantics; click collapse deferred до release | `GraphControl.axaml.cs`, `MainControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Проверка amendment и post-EXEC review | 0.86 | Полный test suite повторно не завершился за 10 минут | Обновить PR branch | Нет | Нет | Roadmap UI tests 39/39, MainControlTreeCommands UI tests 34/34, build app/test зелёные; full suite timeout без вывода зафиксирован как остаточный риск | `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Исправление PR review comments | 0.92 | Нет | Обновить PR branch | Нет | Да, пользователь передал 2 review findings | Исправлены minimap false-positive hit-testing и double-click modifier current semantics; добавлены UI regression tests; Roadmap UI tests 41/41 и сборки зелёные | `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs`, `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Rebase на актуальный main | 0.90 | Нет | Обновить PR branch | Нет | Нет | Разрешены конфликты с inline-редактированием roadmap title из `main`; сохранены roadmap selection/drag semantics; после rebase прошли build app/test, Roadmap UI 42/42, MainControlTreeCommands UI 34/34 и `git diff --check`; full suite timeout 15m без вывода зафиксирован как остаточный риск | `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs`, `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Исправление PR review comment про double-click interval | 0.94 | Нет | Обновить PR branch | Нет | Да, пользователь попросил исправить | Убран локальный `500ms` guard из duplicate-selection suppression: `ClickCount > 1` уже отражает platform double-click interval; добавлен regression-тест на delayed second click with `ClickCount=2`; прошли build app/test, targeted regression 1/1, Roadmap UI 43/43 и `git diff --check` | `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs`, `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Исправление roadmap pan и layout shift | 0.90 | Full `Unlimotion.Test` run не завершился за 20m; full solution build требует отсутствующий workload `wasm-tools` для Android/iOS | Обновить PR branch | Нет | Да, пользователь сообщил 2 бага | Добавлены regression-тесты для right-drag pan по пустому canvas при выделении и для отсутствия resize/shift от selection frame; код переводит right pan в общий helper и держит border thickness постоянным; прошли desktop/test builds через temp output, targeted UI 2/2, Roadmap UI 45/45 и `git diff --check` | `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs`, `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Исправление zoom-aware rectangle selection | 0.88 | Headless regression не воспроизвёл падение до фикса, но закрывает zoom-flow после правки | Обновить PR branch | Нет | Да, пользователь сообщил баг масштаба | Hit-test roadmap rectangle теперь строит bounds узла через трансформацию обоих углов в координаты selection overlay, а не смешивает transformed origin с unscaled size; добавлены zoom regression-тесты; прошли targeted UI 3/3, Roadmap UI 47/47, desktop/test builds через temp output и `git diff --check` | `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs`, `specs/2026-05-13-roadmap-multi-selection.md` |
| EXEC | Rebase на актуальный main после #240 | 0.93 | Full solution build по-прежнему требует отсутствующий workload `wasm-tools`; full `Unlimotion.Test` ранее timeout 20m | Обновить PR branch | Нет | Да, пользователь попросил rebase | Ветка перебазирована на `origin/main` `8e5d3c0` без конфликтов; после rebase прошли Roadmap UI 47/47, desktop/test builds через temp output и `git diff --check`; повторный test build запускался последовательно из-за локальной блокировки общего `obj` при параллельной сборке | `specs/2026-05-13-roadmap-multi-selection.md` |
