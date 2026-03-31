# Команды раскрытия и сворачивания TreeView на всём экране MainControl

## 0. Метаданные
- Core: `quest-governance`, `collaboration-baseline`, `testing-baseline`
- Тип (профиль): `dotnet-desktop-client`
- Overlay profile: `ui-automation-testing`
- Контексты: `testing-dotnet`
- Владелец: Unlimotion desktop tree navigation
- Масштаб: `large`
- Ограничения:
  - Не менять persisted-модель задач, JSON-формат хранения и storage API.
  - Не ломать текущие сценарии выбора узла, drag-and-drop, double click и существующие hotkeys (`Shift+Delete`, `Ctrl+Enter`, `Shift+Enter`, `Ctrl+Tab`).
  - Не делать отдельную реализацию на каждую вкладку, если общий механизм для `TreeView` в `MainControl` покрывает тот же сценарий.
  - Не расширять Roadmap/Graph и Settings-вкладки новыми командами, если они не используют `TaskWrapperViewModel`-based `TreeView`.
- Связанные файлы:
  - `src/Unlimotion.ViewModel/MainWindowViewModel.cs`
  - `src/Unlimotion.ViewModel/TaskWrapperViewModel.cs`
  - `src/Unlimotion/Views/MainControl.axaml`
  - `src/Unlimotion/Views/MainControl.axaml.cs`
  - `src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `src/Unlimotion.Test/MainWindowViewModelTests.cs`
  - `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`

## 1. Overview / Цель
Добавить в desktop UI четыре команды управления иерархией `TreeView`, доступные из горячих клавиш и контекстного меню во всех task-tree на экране `MainControl`: и во вкладках, и в relation-деревьях карточки задачи:
1. `Развернуть вложенные текущего`
2. `Свернуть вложенные текущего`
3. `Развернуть все узлы`
4. `Свернуть все узлы`

## 2. Текущее состояние (AS-IS)
- Во вкладках `All Tasks`, `Last Created`, `Unlocked`, `Completed`, `Archived`, `Last Opened` уже используются `TreeView` с `TaskWrapperViewModel`.
- В правой карточке задачи relation-блоки `Parents`, `Blocking`, `Containing`, `Blocked` тоже используют `TreeView` с тем же wrapper-типом.
- Управление раскрытием сейчас несогласованное:
  - в `All Tasks` есть явный binding `TreeViewItem.IsExpanded <-> TaskWrapperViewModel.IsExpanded`;
  - в остальных tab trees и relation trees такого общего binding нет, поэтому программно управлять раскрытием одинаково на всём экране нельзя.
- Горячие клавиши сейчас покрывают создание и удаление задач, но не recursive expand/collapse.
- Контекстного меню для операций над деревом нет.
- В `MainControl.axaml.cs` уже есть code-behind для view-specific поведения (`MoveToPath`, drag-and-drop, focus), то есть UI-специфичная маршрутизация команд уже допустима архитектурно.

## 3. Проблема
При работе с глубокими иерархиями пользователь вынужден вручную раскрывать и сворачивать ветки по одному узлу. Это медленно как в проекциях вроде `Completed` и `Last Created`, так и в relation-деревьях карточки, где нужно быстро просматривать вложенные связи без перехода в другую часть экрана.

## 4. Цели дизайна
- Дать единый набор из 4 команд во всех вкладках с `TreeView`.
- Распространить тот же набор команд на relation-деревья карточки задачи.
- Сделать поведение команд одинаковым независимо от текущей проекции дерева.
- Обеспечить доступ и через keyboard, и через context menu.
- Ограничить действие команд активным деревом, не затрагивая другие вкладки и другие UI-области.
- Сохранить текущие пользовательские сценарии без побочных эффектов.

## 5. Non-Goals
- Не добавлять новые фильтры, сортировки или tree navigation вне expand/collapse.
- Не менять модель `TaskWrapperViewModel` за пределами runtime-state раскрытия.
- Не проектировать отдельный toolbar или ribbon для tree-команд.
- Не делать общий рефакторинг всех input/pointer обработчиков `MainControl`, кроме минимально нужного для корректной маршрутизации команд.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
| Компонент / файл | Ответственность |
| --- | --- |
| `MainWindowViewModel.cs` | Экспорт 4 команд для XAML и testable helper-логика раскрытия/сворачивания дерева по `TaskWrapperViewModel`. |
| `TaskWrapperViewModel.cs` | Переиспользование текущего `IsExpanded` как единого runtime-state для всех проекций дерева. |
| `MainControl.axaml` | Общие `KeyBinding`, общий `ContextMenu` ресурс и единый стиль/шаблон, привязывающий `TreeViewItem.IsExpanded` к wrapper-state во всех целевых деревьях экрана. |
| `MainControl.axaml.cs` | Sticky active-tree routing, локальный current wrapper для context menu, валидация контекста вызова и защита от текстового ввода. |
| `MainWindowViewModelTests.cs` | Unit/regression tests на recursive expand/collapse semantics и no-op случаи. |
| `MainControlTreeCommandsUiTests.cs` | Headless/UI tests на hotkeys, context menu source routing и relation-tree coverage. |

### 6.2 Детальный дизайн
- В `MainControl` вводится общий механизм для всех task-tree на экране:
  - единый стиль `TreeViewItem`, который биндует `IsExpanded` к `TaskWrapperViewModel.IsExpanded`;
  - единый `ContextMenu` с четырьмя командами;
  - единые `KeyBinding`, привязанные к тем же командам.
- Предлагаемые жесты:
  - `Ctrl+Shift+Right` -> `Развернуть вложенные текущего`
  - `Ctrl+Shift+Left` -> `Свернуть вложенные текущего`
  - `Ctrl+Alt+Right` -> `Развернуть все узлы`
  - `Ctrl+Alt+Left` -> `Свернуть все узлы`
- Команда `Развернуть вложенные текущего`:
  - использует текущий selected/clicked wrapper активного дерева;
  - гарантирует раскрытие самого текущего узла;
  - рекурсивно выставляет `IsExpanded=true` всем потомкам.
- Команда `Свернуть вложенные текущего`:
  - использует текущий selected/clicked wrapper активного дерева;
  - рекурсивно выставляет `IsExpanded=false` потомкам текущего узла;
  - не снимает выделение и не обязана принудительно сворачивать сам текущий узел.
- Команды `Развернуть все узлы` и `Свернуть все узлы`:
  - работают по root-коллекции активного дерева;
  - затрагивают только то дерево, из которого вызваны, и не изменяют состояние других вкладок.
- Источник текущего дерева:
  - для hotkeys используется sticky active tree: последний `TreeView`, получивший pointer/focus внутри `MainControl` и остающийся `IsAttachedToVisualTree == true`, `IsVisible == true`, `IsEnabled == true` в момент вызова команды;
  - hotkeys продолжают работать по sticky active tree, даже если focus ушёл с дерева на другой нетекстовый control внутри `MainControl`;
  - если текущий focused control является текстовым редактором или control'ом ввода (`TextBox`, `AutoCompleteBox`, `NumericUpDown` и аналоги), команды не выполняются;
  - для context menu источник берётся из дерева или item, по которому открыто меню;
  - если активного дерева нет, команды становятся no-op.
- При right click по item menu-команды должны опираться на этот item, а не на старое выделение.
- Для relation trees локальный current wrapper для menu-команд хранится в view-state и не должен переключать глобальный `CurrentTaskItem`.
- Объём дублирования в XAML должен быть минимальным:
  - предпочтителен общий ресурс/style/menu, применяемый ко всем нужным `TreeView`;
  - scope общего механизма включает все `TreeView` в `MainControl`, основанные на `TaskWrapperViewModel`: tab trees и 4 relation trees карточки;
  - допустимы точечные исключения только если конкретный tree-template не позволяет reuse без побочных эффектов.

## 7. Бизнес-правила / Алгоритмы
- Все recursive-операции должны работать по wrapper-дереву текущей проекции, а не только по `CurrentAllTasksItems`.
- Если команда требует `current node`, а в активном дереве он не определён, операция не выполняется и не бросает исключение.
- Для recursive expand/collapse разрешается материализовывать `SubTasks`, если это нужно для обхода.
- Перед выполнением hotkey-команды sticky active tree должен повторно валидироваться на attached/visible/enabled и принадлежность текущему `MainControl`.
- Команды не должны срабатывать, пока keyboard focus находится в текстовом редакторе или relation picker, даже если sticky active tree ранее был установлен.
- Новые команды не должны менять `CurrentTaskItem`; для relation trees current wrapper нормализуется только в локальном view-state.

## 8. Точки интеграции и триггеры
- `TreeView` pointer/focus -> обновление ссылки на active tree.
- `TreeViewItem` right click -> обновление current wrapper для menu-команд.
- `KeyBinding` -> вызов соответствующей команды для active tree.
- Изменение `TaskWrapperViewModel.IsExpanded` -> немедленное обновление UI всех деревьев, где применён общий стиль.
- Headless/UI tests -> проверка routing между tab tree и relation tree.

## 9. Изменения модели данных / состояния
- Persisted data и storage schema не меняются.
- Добавляется только runtime-state маршрутизации:
  - ссылка на активный `TreeView` в view/code-behind;
  - локальный current wrapper для context menu в деревьях без двухстороннего `SelectedItem`;
  - новые UI-команды и helper-логика раскрытия/сворачивания.
- `TaskWrapperViewModel.IsExpanded` становится единым источником истины для всех целевых `TreeView` в `MainControl`.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - добавить spec;
  - унифицировать binding `IsExpanded` для всех целевых деревьев;
  - подключить 4 команды, context menu и hotkeys;
  - добавить unit tests и обязательные headless/UI regression tests;
  - прогнать targeted tests, `dotnet build`, полный `dotnet test`.
- Rollback:
  - удалить новые команды, menu и keybindings;
  - вернуть локальное поведение отдельных деревьев;
  - удалить regression tests для новой функциональности.

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
1. Во всех `TaskWrapperViewModel`-based `TreeView` внутри `MainControl`, включая `All Tasks`, `Last Created`, `Unlocked`, `Completed`, `Archived`, `Last Opened` и 4 relation trees карточки, доступен один и тот же набор из 4 tree-команд через context menu.
2. Команда `Развернуть вложенные текущего` рекурсивно раскрывает выбранный узел и его потомков только в активном дереве.
3. Команда `Свернуть вложенные текущего` рекурсивно сворачивает потомков выбранного узла, не теряя текущее выделение.
4. Команды `Развернуть все узлы` и `Свернуть все узлы` одинаково работают во всех целевых деревьях, включая relation trees и те деревья, где раньше не было общего `IsExpanded` binding.
5. Горячие клавиши работают по sticky active tree, даже если focus ушёл с дерева, но не срабатывают во время текстового ввода в карточке задачи или relation picker.
6. Context menu в relation tree использует item под right click без переключения глобального `CurrentTaskItem`.
7. Существующие сценарии drag-and-drop, double click и текущие hotkeys для create/remove продолжают работать без регрессии.
8. Headless/UI regression tests покрывают минимум один tab tree и один relation tree для hotkeys и context menu flow.

### Команды для проверки
```powershell
dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug
src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.exe --disable-logo --no-progress --output Detailed
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug
```

## 12. Риски и edge cases
- Если оставить `IsExpanded` binding только у `All Tasks`, команды будут вести себя по-разному между вкладками.
- Правый клик по item в Avalonia может не синхронизировать selection автоматически, поэтому контекстное меню рискует примениться к устаревшему current node.
- Sticky active tree повышает риск ложных hotkey-срабатываний; его нужно компенсировать жёсткой проверкой focused text-input controls перед выполнением команды.
- Для relation trees недопустимо нормализовать current node через `CurrentTaskItem`, иначе карточка задачи может перестроиться до завершения команды.
- В проекциях с lazy `SubTasks` рекурсивный обход может временно материализовать дополнительные wrapper-объекты; это допустимо, но должно оставаться in-memory и быстрым.
- В репозитории сейчас нет готового headless/UI harness для этого сценария, поэтому объём работы включает добавление тестовой инфраструктуры или её минимального расширения.

## 13. План выполнения
1. Подготовить единый tree-style/menu contract в `MainControl`.
2. Добавить 4 команды и helper-обходы для wrapper-дерева.
3. Подключить sticky active-tree tracking и локальную normalization для relation trees без переключения `CurrentTaskItem`.
4. Добавить unit tests на recursive semantics и no-op случаи.
5. Добавить headless/UI tests на hotkeys и context menu routing.
6. Прогнать targeted tests, build и полный test run; затем вручную проверить keyboard/context menu flow в desktop app.

## 14. Открытые вопросы
Решения зафиксированы пользователем 30 марта 2026:
- hotkeys работают по sticky active tree;
- scope включает все task-tree на экране `MainControl`, включая relation trees;
- headless/UI tests обязательны и блокируют завершение задачи при падении.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - изменение локализовано в desktop UI и tree navigation;
  - не требует изменения persisted-модели;
  - не должно вносить длительные синхронные операции в UI thread;
  - требует `dotnet build` и `dotnet test` перед завершением реализации.
- Overlay profile: `ui-automation-testing`
- Выполненные требования overlay profile:
  - UI-контракт и keyboard/context menu flow сопровождаются headless/UI regression tests;
  - новые automation selectors и menu routing должны оставаться стабильными;
  - падающие UI tests блокируют завершение задачи.

## 16. Альтернативы и компромиссы
- Вариант: реализовать команды отдельно в каждой вкладке через локальный code-behind.
- Плюсы:
  - проще быстро протащить первый рабочий вариант.
- Минусы:
  - дублирование XAML и логики;
  - высокий риск расхождения поведения между вкладками;
  - сложнее сопровождать новые деревья в будущем.
- Почему выбранное решение лучше:
  - даёт единый UI-контракт для всех task-tree;
  - минимизирует расхождения по `IsExpanded` и selection behavior;
  - снижает объём повторяющегося кода.

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Распределение ответственности, алгоритмы, интеграция и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Persisted data не меняется, риски UI-маршрутизации выделены отдельно. |
| D. Проверяемость | 14-16 | PASS | Есть acceptance criteria, команды проверки и план regression tests. |
| E. Готовность к автономной реализации | 17-19 | PASS | Блокирующих вопросов нет, жесты и scope зафиксированы. |
| F. Соответствие профилю | 20 | PASS | Спека соответствует `dotnet-desktop-client`, `ui-automation-testing` и .NET testing flow. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Объём изменения ограничен 4 tree-командами, menu/hotkeys и общим expand-state. |
| 2. Понимание текущего состояния | 5 | Зафиксированы текущие деревья, существующие hotkeys и различие в `IsExpanded` binding между вкладками. |
| 3. Конкретность целевого дизайна | 5 | Описаны команды, жесты, active-tree routing и поведение current/all операций. |
| 4. Безопасность (миграция, откат) | 5 | Миграций данных нет, rollback прямой и локальный. |
| 5. Тестируемость | 5 | Есть acceptance criteria, unit tests, headless/UI tests и обязательные `build/test` проверки. |
| 6. Готовность к автономной реализации | 5 | Решения по sticky routing, relation-tree scope и UI test bar зафиксированы. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

## Approval
Спека подтверждена пользователем 30 марта 2026. Реализация может выполняться в `EXEC`.
