# Roadmap task actions parity

## 0. Метаданные
- Тип (профиль): delivery-task; stack profile `dotnet-desktop-client`; overlay profile `ui-automation-testing`.
- Владелец: Codex / Unlimotion desktop UI.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка.
- Ограничения: центральный `QUEST` требует SPEC-first и подтверждение фразой `Спеку подтверждаю` до изменений кода; локальный override требует UI tests для UI-facing исправлений.
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Projects\My\Agents\AGENTS.md`, `C:\Projects\My\Agents\instructions\governance\routing-matrix.md`, `C:\Projects\My\Agents\instructions\core\model-behavior-baseline.md`, `C:\Projects\My\Agents\instructions\core\quest-governance.md`, `C:\Projects\My\Agents\instructions\core\quest-mode.md`, `C:\Projects\My\Agents\instructions\core\collaboration-baseline.md`, `C:\Projects\My\Agents\instructions\core\testing-baseline.md`, `C:\Projects\My\Agents\instructions\contexts\testing-dotnet.md`, `C:\Projects\My\Agents\instructions\profiles\dotnet-desktop-client.md`, `C:\Projects\My\Agents\instructions\profiles\ui-automation-testing.md`.

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Сделать карту задач поведенчески совместимой с обычными вкладками задач для базовых действий над выбранной задачей:
- создание связей drag-and-drop между задачами на карте;
- создание sibling / blocked sibling / inner task через глобальные клавиши;
- создание задач кнопками в карточке текущей задачи после выбора задачи на карте.
- открытие карточки задачи двойным кликом по задаче на карте.

Outcome contract:
- Success means: пользователь выбирает задачу на карте, после чего те же команды создания и связи работают с этой задачей как с выбранной задачей в task-tree вкладках.
- Итоговый артефакт / output: минимальный кодовый фикс в `GraphControl`/`MainControl` плюс regression UI tests.
- Stop rules: остановиться после прохождения targeted UI tests и full build/test либо явно зафиксировать невыполнимую проверку и next-best evidence.

## 2. Текущее состояние (AS-IS)
- Вкладка Roadmap в `src/Unlimotion/Views/MainControl.axaml` рендерит `<views:GraphControl DataContext="{Binding Graph}"/>`, то есть сам control работает с `GraphViewModel`, а не напрямую с `MainWindowViewModel`.
- `MainControl` содержит глобальные `UserControl.KeyBindings`: `Ctrl+Enter -> CreateSibling`, `Shift+Enter -> CreateBlockedSibling`, `Ctrl+Tab -> CreateInner`.
- Кнопки в карточке задачи находятся в pane `MainControl` и вызывают команды `Create`, `CreateSibling`, `CreateBlockedSibling`, `CreateInner` из `MainWindowViewModel`.
- Обычный tree drag-and-drop реализован в `MainControl.axaml.cs`: данные имеют форматы `application/xxx-unlimotion-task` / batch, drop-target извлекается из `TaskWrapperViewModel` или `TaskItemViewModel`.
- `GraphControl` имеет свой drag format `application/xxx-unlimotion-task-item` и делегирует `Drop`/`DragOver` в static методы `MainControl`.
- `MainControl.TryGetDropTargetTask` сейчас не распознаёт `RoadmapNode` как target task. При drop на roadmap node event source часто является `Border` с `DataContext = RoadmapNode`, поэтому операция получает `None`.
- `GraphControl.InputElement_OnPointerPressed` выбирает задачу через `TaskItemViewModel.MainWindowInstance`, а не через visual tree owner `MainWindowViewModel`. Это хрупко для headless UI tests и для embedded/control scenarios.
- `GraphControl.TaskTree_OnDoubleTapped` сейчас меняет `DetailsAreOpen` через `TaskItemViewModel.MainWindowInstance` и делает toggle; если карточка уже открыта, двойной клик может закрыть её, а не открыть карточку выбранной roadmap task.
- `GraphControl.RoadmapEditor_KeyDown` обрабатывает только viewport keys (`F/U/T/R`, `R`) и не маршрутизирует команды создания задач из фокуса карты.
- В UI tests уже есть `RoadmapGraphUiTests` для rendering/graph state и `MainControlTreeCommandsUiTests` / `MainControlRelationPickerUiTests` для обычных вкладок, но нет regression coverage на создание задач и drag-drop прямо из roadmap.

## 3. Проблема
Roadmap graph визуально показывает те же задачи, но не участвует в общем контракте task actions как task-tree вкладки: выбор задачи, drop-target resolution и hotkey routing на карте неполны.

## 4. Цели дизайна
- Разделение ответственности: roadmap остаётся graph-view, а task mutations продолжают выполняться через существующие `MainWindowViewModel` commands и `TaskItemViewModel` relation methods.
- Повторное использование: переиспользовать `MainControl` drag-drop pipeline и существующие VM commands вместо нового relation/action API.
- Тестируемость: добавить Avalonia.Headless regression tests, которые воспроизводят действия через UI controls/events.
- Консистентность: modifiers drag-drop на карте должны соответствовать вкладкам: default copy/contains, `Shift` move, `Ctrl` source blocks target, `Alt` target blocks source, `Ctrl+Shift` clone.
- Обратная совместимость: не менять persisted model, storage format, public command names и automation ids.

## 5. Non-Goals (чего НЕ делаем)
- Не менять алгоритм layout/rendering карты и `RoadmapGraphBuilder`.
- Не добавлять новый тип связей или новый persisted state.
- Не перерабатывать весь drag threshold UX roadmap node dragging, если regression закрывается target resolution и selection routing.
- Не менять тексты локализации и visual design карточки задачи.
- Не расширять public storage API.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion/Views/GraphControl.axaml.cs` -> надёжно резолвит `MainWindowViewModel`, выбирает roadmap task без зависимости только от static singleton, маршрутизирует create hotkeys из фокуса карты.
- `src/Unlimotion/Views/MainControl.axaml.cs` -> распознаёт `RoadmapNode` как валидный drop target и сохраняет существующий drag-drop pipeline.
- `src/Unlimotion.Test/RoadmapGraphUiTests.cs` или отдельный UI test class -> regression tests для roadmap selection/create/hotkeys/drop.

### 6.2 Детальный дизайн
- Добавить helper для выбора roadmap task:
  - получить `MainWindowViewModel` через `FindParentDataContext<MainWindowViewModel>()`;
  - использовать `TaskItemViewModel.MainWindowInstance` только как fallback для production compatibility, если visual owner недоступен;
  - установить `CurrentTaskItem`;
  - вызвать `SelectCurrentTask()` при `GraphMode`, чтобы `CurrentGraphItem` синхронизировался с тем же source wrapper, где это возможно.
  - не добавлять новый public owner API в `GraphViewModel`, если реализация возможна через visual owner resolver.
- Для hotkeys в `GraphControl.RoadmapEditor_KeyDown`:
  - при `Ctrl+Enter` выполнить `vm.CreateSibling`;
  - при `Shift+Enter` выполнить `vm.CreateBlockedSibling`;
  - при `Ctrl+Tab` выполнить `vm.CreateInner`;
  - перед выполнением проверять `e.Handled`, после выполнения выставлять `e.Handled = true`;
  - команда должна выполняться ровно один раз на одно нажатие, несмотря на подписки `GraphControl` и `RoadmapEditor`, а также существующие `MainControl.UserControl.KeyBindings`;
  - не ломать существующие viewport keys и не перехватывать ввод из текстовых controls.
- Для double click по roadmap node:
  - переиспользовать тот же helper выбора roadmap task;
  - установить `DetailsAreOpen = true`, а не переключать значение;
  - после double click карточка должна показывать именно clicked task.
- Для drag-drop target:
  - расширить `TryGetDropTargetTask` так, чтобы `Control.DataContext is RoadmapNode` или ancestor с `RoadmapNode` возвращал `node.TaskItem`;
  - оставить `TaskWrapperViewModel` / `TaskItemViewModel` пути без изменений.
- Output contract / evidence rules:
  - тест должен проверять именно изменение storage/model после UI action, а не только вызов обработчика;
  - для drag-drop проверить хотя бы `Ctrl` roadmap node -> roadmap node создаёт blocking relation и вызывает rebuild/update evidence.
- Обработка ошибок: использовать существующие validation guards `CanApplyDrop`, `CanMoveInto`, `CanCreateBlockingRelation`, toast errors.
- Производительность: изменения выполняются на пользовательских events, без дополнительных subscriptions и без влияния на background roadmap build.

## 7. Бизнес-правила / Алгоритмы
- Roadmap node represents `RoadmapNode.TaskItem`; для UI actions он равен task entity в tree вкладках.
- Create commands не должны создавать задачу, если `CurrentTaskItem` отсутствует или title текущей задачи пустой, как сейчас в `MainWindowViewModel`.
- Drag modifiers сохраняют текущую таблицу `MainControl.GetBatchDropOperationKind`:
  - no modifiers: source copied/contained into target;
  - `Shift`: move into target;
  - `Ctrl`: source blocks target;
  - `Alt`: target blocks source;
  - `Ctrl+Shift`: clone into target.

## 8. Точки интеграции и триггеры
- Roadmap node `PointerPressed` -> выбрать текущую задачу через owner VM.
- Roadmap node `DoubleTapped` -> выбрать текущую задачу и открыть details/card pane.
- Roadmap node drag source -> existing `GraphControl.CustomFormat`.
- Roadmap node drop target -> `MainControl.Drop` / `DragOver` через `RoadmapNode` DataContext.
- Roadmap editor `KeyDown` -> existing create commands.
- Storage/relation mutation -> existing roadmap subscriptions trigger rebuild.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Новых public/runtime fields в `GraphViewModel` не планируется; если EXEC докажет техническую необходимость, нужен локальный private/internal helper без расширения публичного command surface.
- `CurrentTaskItem` и `CurrentGraphItem` остаются существующими runtime state.

## 10. Миграция / Rollout / Rollback
- Миграция данных: не применимо, storage schema не меняется.
- Rollout: обычный desktop build.
- Rollback: откатить кодовые изменения в `GraphControl`/`MainControl`/tests; данные пользователей не затрагиваются.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. Клик по roadmap node выбирает задачу в `MainWindowViewModel.CurrentTaskItem` без зависимости от актуального `TaskItemViewModel.MainWindowInstance`.
  2. На Roadmap `Ctrl+Enter` создаёт ровно один sibling для выбранной roadmap task, и новая задача появляется в storage / VM.
  3. На Roadmap `Shift+Enter` создаёт ровно один blocked sibling с blocking relation как на других вкладках.
  4. На Roadmap `Ctrl+Tab` создаёт ровно один inner task под выбранной roadmap task.
  5. Кнопки `NewTask`, `NewSibling`, `NewBlockedSibling`, `NewInner` в карточке задачи после выбора roadmap task создают задачи через существующие commands; relative-кнопки создают задачи относительно выбранной roadmap task, `NewTask` создаёт root task.
  6. Двойной клик по roadmap node выбирает clicked task и открывает карточку задачи; если карточка уже открыта, она остаётся открытой и переключается на clicked task.
  7. Drag-drop roadmap node -> roadmap node с `Ctrl` создаёт blocking relation source blocks target.
  8. Существующие tree command и roadmap rendering tests не регрессируют.
- Какие тесты добавить/изменить:
  - Добавить Avalonia.Headless tests в `RoadmapGraphUiTests` или новый `RoadmapTaskActionsUiTests`.
  - Reproducing tests должны сначала падать до фикса: selection without static singleton, double click opens card without closing, create hotkey/button и drag-drop target на `RoadmapNode`.
  - Selection regression test должен временно сбросить или подменить `TaskItemViewModel.MainWindowInstance`, чтобы старый static path не мог замаскировать баг.
  - Double-click regression test должен проверить два состояния: `DetailsAreOpen = false` перед double click открывается; `DetailsAreOpen = true` перед double click не закрывается и current task становится clicked task.
  - Hotkey regression tests должны проверять прирост числа задач строго на `+1` для каждого нажатия.
  - При необходимости использовать reflection только для private static drop helpers нельзя; предпочтительно UI-level event/controls. Если headless drag API не позволяет полный `DoDragDrop`, допустим targeted handler-level test через реальные `MainControl.Drop`/`MainControl.DragOver`, `DataObject` с `GraphControl.CustomFormat` и assert по `Blocks`/`BlockedBy` в VM/storage. Такой fallback не считается достаточным, если он не проходит через existing drop pipeline и не проверяет mutation результата.
- Characterization tests:
  - Существующие `MainControlTreeCommandsUiTests` должны продолжить подтверждать обычные вкладки.
- Базовые замеры performance: не применимо, изменение event routing, не layout/perf.
- Команды для проверки:
  - `dotnet build src\Unlimotion\Unlimotion.csproj`
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/RoadmapGraphUiTests/*"`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*"`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj`
- Stop rules для test/retrieval/tool/validation loops:
  - Если targeted roadmap tests падают, не завершать EXEC.
  - Если full test run не запускается из-за внешней known repo issue, выполнить targeted UI tests + relevant project builds и зафиксировать точную ошибку.

## 12. Риски и edge cases
- `Ctrl+Tab` может конфликтовать с tab navigation. Нужно подтверждать headless test и при необходимости обрабатывать на `GraphControl` до стандартной навигации.
- Roadmap hotkeys могут выполниться дважды из-за bubbling/handler duplication. EXEC должен выставлять `e.Handled` и проверять `+1` mutation tests.
- Double click сейчас похож на toggle. EXEC должен заменить его на idempotent open, чтобы пользователь не закрывал карточку случайно.
- `GraphControl.RoadmapEditor_KeyDown` сейчас реагирует на `F/U/T/R` без проверки modifiers; при правке нельзя сломать viewport shortcuts.
- Static `TaskItemViewModel.MainWindowInstance` может быть stale в tests; новый resolver должен предпочитать owner/visual context.
- Drop на child text внутри node может уже работать через `TaskItemViewModel`; drop на border с `RoadmapNode` должен работать одинаково.
- После mutation roadmap rebuild asynchronous; UI tests должны ждать через existing `WaitFor`/throttle helpers.

## 13. План выполнения
1. Добавить failing Avalonia.Headless roadmap UI tests для selection, double click opens card, create hotkeys/buttons и drag-drop relation.
2. Реализовать owner VM resolution / selection helper в `GraphControl`.
3. Реализовать roadmap create hotkey routing в `GraphControl` с сохранением viewport shortcuts.
4. Расширить `MainControl.TryGetDropTargetTask` для `RoadmapNode`.
5. Прогнать targeted UI tests, затем build/full test согласно командам.
6. Выполнить post-EXEC review: отклонения от спеки, regressions, selector stability, stale comments, validation evidence.

## 14. Открытые вопросы
Нет блокирующих вопросов. Если headless API не позволит достоверно симулировать полный OS drag-drop, в EXEC будет использован next-best event-level regression test для drop target plus UI-level create/selection tests.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`; local UI testing override.
- Выполненные требования профиля:
  - План не блокирует UI thread длительными операциями.
  - Изменения пользовательского потока покрываются Avalonia.Headless UI tests.
  - Automation ids сохраняются; новые selectors добавляются только если тестам нужен стабильный roadmap node selector и без переименования существующих ids.
  - Проверки используют TUnit/Microsoft.Testing.Platform style через `dotnet run --project ... -- --treenode-filter`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/Views/GraphControl.axaml.cs` | Owner VM resolution, task selection sync, double-click open-card behavior, create hotkey routing | Roadmap должен участвовать в общем task action contract |
| `src/Unlimotion/Views/MainControl.axaml.cs` | Drop target resolution для `RoadmapNode` | Drag-drop на graph node должен создавать связи как в trees |
| `src/Unlimotion.Test/RoadmapGraphUiTests.cs` или новый test file | Regression UI tests | Локальный override и центральный profile требуют UI coverage |
| `specs/2026-05-07-roadmap-task-actions-parity.md` | Рабочая спецификация и журнал | Центральный QUEST contract |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Выбор roadmap task | Через static `TaskItemViewModel.MainWindowInstance` | Через visual owner VM resolver с static fallback |
| Double click roadmap task | Toggle details pane через static singleton | Выбирает clicked task и открывает карточку idempotently |
| Roadmap create hotkeys | Нет явного routing из `GraphControl` | `Ctrl+Enter`, `Shift+Enter`, `Ctrl+Tab` вызывают существующие commands ровно один раз |
| Roadmap drag-drop target | `RoadmapNode` не распознаётся как target task | `RoadmapNode.TaskItem` используется как target |
| Card create buttons после выбора roadmap node | Зависят от хрупкого selection path | Работают после надёжного выбора текущей задачи |

## 18. Альтернативы и компромиссы
- Вариант: перенести все команды в `GraphViewModel`.
- Плюсы: bindings внутри `GraphControl` стали бы прямыми.
- Минусы: дублирование command surface, риск рассинхронизации с `MainWindowViewModel`, расширение VM API.
- Почему выбранное решение лучше в контексте этой задачи: существующий source of truth для mutations уже в `MainWindowViewModel`/`TaskItemViewModel`; достаточно исправить routing и target resolution.

- Вариант: заставить `GraphControl` DataContext быть `MainWindowViewModel`.
- Плюсы: общие bindings стали бы проще.
- Минусы: ломает текущий graph-specific binding contract (`GraphViewModel.Tasks`, filters, search, reset command), большой blast radius.
- Почему выбранное решение лучше в контексте этой задачи: сохраняет текущий graph binding surface.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals описаны. |
| B. Качество дизайна | 6-10 | PASS | Распределение ответственности, интеграции, state, rollout и perf границы описаны. |
| C. Безопасность изменений | 11-13 | PASS | Persisted data не меняется, rollback простой, план ограничен event routing. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, UI tests и команды TUnit/MTP указаны. |
| E. Готовность к автономной реализации | 17-19 | PASS | Этапы и риски определены, блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | Выбран .NET desktop + UI automation stack, local UI tests requirement учтён. |

Итог: ГОТОВО.

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | --- | --- |
| 1. Ясность цели и границ | 5 | Одна корневая проблема и явные Non-Goals. |
| 2. Понимание текущего состояния | 5 | Зафиксированы `GraphControl`, `MainControl`, VM commands и drag/drop formats. |
| 3. Конкретность целевого дизайна | 5 | Описаны helper, hotkey routing и drop target resolution. |
| 4. Безопасность (миграция, откат) | 5 | Нет persisted изменений; rollback code-only. |
| 5. Тестируемость | 5 | Есть AC и конкретные Avalonia.Headless/TUnit команды. |
| 6. Готовность к автономной реализации | 5 | План действий и риски достаточны, пользовательского выбора не требуется. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению.

### Post-SPEC Review
- Статус: PASS
- Что исправлено: в дизайн добавлены отдельные checks для card buttons и `Ctrl+Tab`, потому что только drop-target fix не покрывал бы всю пользовательскую проблему; добавлен owner/visual VM resolver вместо зависимости от static singleton; после review-находок убрано плановое расширение `GraphViewModel`, добавлен guard против двойного выполнения hotkeys, усилены evidence rules для drag-drop fallback, selection test обязан нейтрализовать `TaskItemViewModel.MainWindowInstance`, кнопка `NewTask` явно включена в card-button contract; double click по roadmap task добавлен как idempotent open-card scenario.
- Что осталось на решение пользователя: требуется только утверждение спеки фразой `Спеку подтверждаю`.

## Approval
Получено подтверждение пользователя: "Спеку подтверждаю".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор central instruction stack | 0.95 | Нет | Сформировать рабочую спецификацию | Да, перед EXEC | Нет | Локальный `AGENTS.md` требует центральный каталог; задача UI bugfix проходит через QUEST и UI tests | `AGENTS.md`, `AGENTS.override.md`, central instructions |
| SPEC | Анализ AS-IS roadmap actions | 0.86 | Нужен EXEC test-first для точного воспроизведения | Зафиксировать TO-BE и tests | Да, перед EXEC | Нет | Обнаружены хрупкий static selection path, отсутствие `RoadmapNode` drop-target resolution и отсутствие явного create hotkey routing в GraphControl | `GraphControl.axaml.cs`, `MainControl.axaml.cs`, `MainWindowViewModel.cs`, UI tests |
| SPEC | SPEC quality gate и post-SPEC review | 0.92 | Нет | Дождаться утверждения спеки | Да | Да, ожидается фраза `Спеку подтверждаю` | Spec прошёл linter/rubric; центральный QUEST запрещает кодовые изменения до утверждения | `specs/2026-05-07-roadmap-task-actions-parity.md` |
| SPEC | Исправление review-находок спеки | 0.94 | Нет | Дождаться утверждения спеки | Да | Да, пользователь попросил исправить spec review findings | Убрана неоднозначность с `GraphViewModel` API, добавлены guards против double hotkey execution, усилены UI evidence requirements и покрытие `NewTask` | `specs/2026-05-07-roadmap-task-actions-parity.md` |
| SPEC | Расширение scope double-click open card | 0.94 | Нет | Дождаться утверждения спеки | Да | Да, пользователь попросил добавить сценарий | Double click на graph task относится к тому же roadmap task action parity и должен открывать карточку clicked task без toggle-close поведения | `specs/2026-05-07-roadmap-task-actions-parity.md` |
| EXEC | Реализация roadmap action routing | 0.91 | Нет | Проверить build и UI tests | Нет | Да, пользователь подтвердил spec | `GraphControl` теперь резолвит owner VM через visual tree, выбирает roadmap task, открывает карточку idempotently и маршрутизирует create hotkeys; `MainControl` распознаёт `RoadmapNode` как drop target | `src/Unlimotion/Views/GraphControl.axaml.cs`, `src/Unlimotion/Views/MainControl.axaml.cs` |
| EXEC | Regression UI coverage | 0.90 | Нет | Запустить targeted suites | Нет | Нет | Добавлены Avalonia.Headless tests для double click, roadmap hotkeys, card buttons и roadmap node drop с `Ctrl` relation | `src/Unlimotion.Test/RoadmapGraphUiTests.cs` |
| EXEC | Верификация targeted checks | 0.94 | Full run упал на существующем storage concurrency exception | Выполнить post-EXEC review | Нет | Нет | `dotnet build` для app/test projects прошёл; `RoadmapGraphUiTests` 31/31 и `MainControlTreeCommandsUiTests` 34/34 прошли; full `Unlimotion.Test` без rebuild упал с `Collection was modified` в `UnifiedTaskStorage.RefreshRelations/Delete` | build/test commands |
| EXEC | Уточнение click/rebuild поведения | 0.93 | Нет | Финальный статус пользователю | Нет | Да, пользователь уточнил требования | Изменение текста задачи больше не запускает rebuild карты; binding обновляет текст сам, а `SizeChanged` обновляет только локальную ширину node/anchors. Roadmap drag теперь стартует только после pointer-move threshold, поэтому первый клик сразу выбирает задачу, а double click открывает карточку | `GraphControl.axaml`, `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Исправление double-click и drag source | 0.94 | Нет | Финальный статус пользователю | Нет | Да, пользователь сообщил о регрессии | Double click теперь дополнительно обрабатывается через `PointerPressed.ClickCount`, если routed `DoubleTapped` не приходит до шаблона node; drag source захватывает pointer до threshold и освобождает его перед `DoDragDrop`, чтобы move events не терялись при выходе курсора за границы node | `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
| EXEC | Верификация follow-up исправления | 0.95 | Нет | Финальный статус пользователю | Нет | Нет | Добавлен source-side UI test для pointer drag на roadmap node; `RoadmapGraphUiTests` прошли 32/32 после пересборки, ранее после production-code изменений прошли `MainControlTreeCommandsUiTests` 34/34 | `RoadmapGraphUiTests.cs`, build/test commands |
| EXEC | Повторное исправление roadmap drag source | 0.93 | Нет | Финальный статус пользователю | Нет | Да, пользователь сообщил, что drag всё ещё не стартует | `GraphControl` теперь слушает `PointerMoved/Released` на своём уровне с `handledEventsToo`, чтобы `NodifyEditor`/контейнеры не теряли pending drag; pointer capture больше не сбрасывается перед `DoDragDrop`, а освобождается после завершения операции. UI test теперь проверяет инкремент `RoadmapDragStartCount` после движения за threshold | `GraphControl.axaml.cs`, `RoadmapGraphUiTests.cs` |
