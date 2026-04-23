# Пометка повторяемых задач в списках и на карте

## 0. Метаданные
- Тип (профиль): delivery-task; profile `dotnet-desktop-client`; context `testing-dotnet`
- Владелец: Codex
- Масштаб: small
- Целевой релиз / ветка: `feature/repeater-list-badge`
- Ограничения: реализация после подтверждения фразой `Спеку подтверждаю`; не менять persisted model и публичные API без необходимости
- Связанные ссылки: запрос пользователя от 2026-04-22

## 1. Overview / Цель
Добавить в списках задач и на карте видимую компактную пометку для задач, у которых задан шаблон повторения. Пользователь должен видеть повторяемость прямо в списке или узле карты, без открытия карточки задачи.

## 2. Текущее состояние (AS-IS)
- Повторение хранится в `TaskItemViewModel.Repeater` как `RepeaterPatternViewModel`.
- Признак активного повторения уже вычисляется через `TaskItemViewModel.IsHaveRepeater`: `Repeater != null && Repeater.Type != RepeaterType.None`.
- В `MainControl.axaml` есть общий `DataTemplate` для `TaskItemViewModel`; он используется в основном дереве `AllTasksTree` и relation-деревьях в карточке.
- Вкладки `LastCreated`, `LastUpdated`, `Unlocked`, `Completed`, `Archived`, `LastOpened` имеют inline-шаблоны строк с `TaskItem.GetAllEmoji` и `TaskItem.Title`.
- В `GraphControl.axaml` узлы карты задач показывают checkbox, emoji и `TitleWithoutEmoji`, но не показывают повторяемость.
- В деталях задачи уже есть блок выбора `RepeaterTemplates`, но списки задач не показывают, что у задачи есть повторение.

## 3. Проблема
Повторяемые задачи визуально не отличаются от обычных задач в списках и на карте, поэтому пользователь не видит важный атрибут задачи при сканировании списка или roadmap-графа.

## 4. Цели дизайна
- Разделение ответственности: вычисление признака и текста подсказки держать во ViewModel, разметку - в `MainControl.axaml`.
- Повторное использование: использовать один и тот же VM-контракт во всех списочных шаблонах.
- Тестируемость: покрыть вычисляемые свойства VM и headless UI-проверкой хотя бы один общий/основной список.
- Консистентность: пометка должна быть одинаковой в основном дереве, relation-деревьях, табличных списках вкладок и узлах карты.
- Обратная совместимость: не менять формат хранения задач, повторений и существующие команды.

## 5. Non-Goals (чего НЕ делаем)
- Не менять алгоритм расчёта повторений и клонирования задач.
- Не добавлять фильтр/сортировку по повторяемым задачам.
- Не менять persisted DTO/domain-модели `TaskItem` и `RepeaterPattern`.
- Не переразрабатывать строки списков и структуру вкладок.
- Не менять UX выбора шаблона повторения в деталях задачи.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `TaskItemViewModel.cs` -> добавить вычисляемый display-контракт для списков: наличие пометки, символ пометки и tooltip/accessible text.
- `MainControl.axaml` -> добавить компактный marker control рядом с заголовком задачи во всех списочных шаблонах.
- `GraphControl.axaml` -> добавить тот же marker control рядом с заголовком задачи в узле карты.
- `MainControlTreeCommandsUiTests.cs` или отдельный UI test file -> проверить, что marker появляется для задачи с активным repeater и отсутствует для обычной.
- `TaskItemViewModel` tests -> проверить вычисляемые свойства и уведомления при смене `Repeater`/`Repeater.Type`.

### 6.2 Детальный дизайн
- Добавить во ViewModel вычисляемые свойства:
  - использовать существующий `IsHaveRepeater` как единственный source of truth для видимости marker.
  - `RepeaterListMarker` со значением `↻` для активного повторения и пустой строкой для отсутствия повторения.
  - `RepeaterListMarkerToolTip` со значением `Repeater.Title` при активном повторении и `null`/пустым значением иначе.
- Для `Repeater` добавить `AlsoNotifyFor(nameof(IsHaveRepeater), nameof(RepeaterListMarker), nameof(RepeaterListMarkerToolTip))` или эквивалентные явные `RaisePropertyChanged`.
- Для вложенного `RepeaterPatternViewModel.PropertyChanged` завести отдельную заменяемую подписку (`SerialDisposable` или `Switch`-паттерн): при смене `Repeater` старая подписка должна быть disposed и не должна больше поднимать marker-уведомления.
- При изменениях внутри текущего `RepeaterPatternViewModel` поднимать marker-уведомления сразу, без существующего throttling для сохранения, минимум для `Type`, `Period`, weekday-флагов и `AfterComplete`, чтобы tooltip и видимость обновлялись без пересоздания строки.
- В XAML добавить компактный `TextBlock`/`Label` с `Text="{Binding ...RepeaterListMarker}"`, `IsVisible="{Binding ...IsHaveRepeater}"`, `ToolTip.Tip="{Binding ...RepeaterListMarkerToolTip}"` и отдельным automation id для UI-теста.
- В inline-шаблонах вставить marker между emoji/date-блоком и `Title`, расширив `ColumnDefinitions` на один `Auto`.
- В общем `DataTemplate` для `TaskItemViewModel` добавить marker перед `Title`; это покроет `AllTasksTree` и relation-деревья.
- В `GraphControl.axaml` вставить marker между emoji и `TitleWithoutEmoji`, расширив `ColumnDefinitions` узла на один `Auto`.
- Ошибки обработки не добавляются: marker полностью вычисляемый и не должен влиять на команды/drag-drop/selection.
- Производительность: вычисление O(1), без обхода дерева и без синхронных операций на UI-потоке.

## 7. Бизнес-правила / Алгоритмы
| Условие | Пометка в списке/на карте | Tooltip |
| --- | --- | --- |
| `Repeater == null` | нет | нет |
| `Repeater.Type == RepeaterType.None` | нет | нет |
| `Repeater.Type != RepeaterType.None` | `↻` | локализованный `Repeater.Title` |

## 8. Точки интеграции и триггеры
- Загрузка/обновление задачи через `TaskItemViewModel.Update(TaskItem)` обязана обновлять marker после установки/сброса `Repeater`.
- Изменение шаблона в деталях задачи через binding к `Repeater`/`RepeaterPatternViewModel` должно обновлять marker в списках.
- Замена `Repeater` должна отписывать `TaskItemViewModel` от старого `RepeaterPatternViewModel`; изменения старого объекта после замены не являются триггером для текущей строки.
- Все `TreeView` с `TaskWrapperViewModel` в `MainControl.axaml` должны использовать один и тот же признак `TaskItem.IsHaveRepeater`.
- Узел задачи в `GraphControl.axaml` должен использовать тот же VM-контракт `IsHaveRepeater`/`RepeaterListMarker`/`RepeaterListMarkerToolTip`.

## 9. Изменения модели данных / состояния
- Новых persisted полей нет.
- Новое состояние только вычисляемое во ViewModel.
- Влияния на storage, миграции и remote model нет.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- Первый запуск после обновления покажет marker для уже существующих задач с `Repeater.Type != None`.
- Rollback: удалить VM display-свойства/уведомления и XAML marker controls; данные задач не меняются.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - В списках задач повторяемая задача получает компактную пометку рядом с заголовком.
  - На карте задач повторяемая задача получает такую же компактную пометку рядом с заголовком узла.
  - Задача без `Repeater` и задача с `RepeaterType.None` пометку не получает.
  - Tooltip marker отражает текущий локализованный заголовок шаблона повторения.
  - При изменении шаблона повторения в текущей задаче marker обновляется без перезапуска приложения.
  - Существующие selection, drag/drop, remove button и tree commands не ломаются.
- Автотесты:
  - Unit: свойства marker в `TaskItemViewModel` для `null`, `None`, `Daily/Weekly`.
  - Unit: `PropertyChanged` для marker-свойств при присваивании `Repeater` и изменении `Repeater.Type`.
  - Unit: после замены `Repeater` изменение старого `RepeaterPatternViewModel` не поднимает marker-уведомления для текущей задачи.
  - UI/headless: общий `DataTemplate` показывает marker в `AllTasksTree` для задачи с active repeater и не показывает для обычной задачи.
  - UI/headless или структурный тест XAML: inline-шаблоны `LastCreatedTree`, `LastUpdatedTree`, `UnlockedTree`, `CompletedTree`, `ArchivedTree`, `LastOpenedTree` содержат marker рядом с `TaskItem.Title`.
  - UI/headless или структурный тест XAML: шаблон узла `GraphControl.axaml` содержит marker рядом с `TitleWithoutEmoji`.
- Команды для проверки:
  - `rg -n "TaskItem.Title|TaskItem.GetAllEmoji|RepeaterListMarker|TitleWithoutEmoji" src/Unlimotion/Views/MainControl.axaml src/Unlimotion/Views/GraphControl.axaml`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskItemRepeaterListMarkerTests/*" --no-progress`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/TaskListRepeaterMarkerUiTests/*" --no-progress`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `dotnet test src/Unlimotion.sln --no-build`

## 12. Риски и edge cases
- Риск: existing computed property `IsHaveRepeater` может не уведомлять UI при изменении вложенного `Repeater.Type`. Смягчение: добавить явные уведомления marker-свойств из подписки на `RepeaterPatternViewModel.PropertyChanged`.
- Риск: при замене `Repeater` старая подписка останется активной и будет поднимать ложные уведомления/сохранять задачу. Смягчение: отдельная заменяемая подписка с disposal старого repeater и regression-тестом.
- Риск: inline-шаблонов несколько, можно обновить не все вкладки. Смягчение: после правки проверить поиском все `TaskItem.Title` в `MainControl.axaml`.
- Риск: карта использует отдельный XAML-шаблон и может остаться без marker. Смягчение: добавить structural-тест на `GraphControl.axaml`.
- Риск: marker сдвинет строки в плотных списках. Смягчение: использовать короткий `Auto`-столбец, небольшой margin и не менять размеры checkbox/date/title.
- Риск: tooltip локализации не обновится при смене языка. Смягчение: использовать существующий `Repeater.Title`; при необходимости в EXEC добавить `RaisePropertyChanged` для tooltip в том же месте, где уже обновляются localized display definitions.

## 13. План выполнения
1. Добавить display-свойства marker в `TaskItemViewModel`, используя `IsHaveRepeater` как единственный признак видимости.
2. Добавить безопасную заменяемую подписку на текущий `RepeaterPatternViewModel.PropertyChanged` и marker-уведомления.
3. Добавить/обновить unit-тесты для marker-свойств, `PropertyChanged` и отписки от старого `Repeater`.
4. Обновить общий `DataTemplate` и inline-шаблоны списков в `MainControl.axaml`.
5. Обновить шаблон узла карты в `GraphControl.axaml`.
6. Добавить headless UI-тест на наличие/отсутствие marker и structural/checklist-проверку всех inline-шаблонов и карты.
7. Запустить targeted tests, затем build и полный тестовый прогон.
8. Выполнить post-EXEC review и исправить найденные критичные проблемы.

## 14. Открытые вопросы
Нет блокирующих вопросов. Визуальную форму выбираю как компактный символ `↻` с tooltip, потому что это минимальное изменение, не требует новых ассетов и не расширяет строки текстовой меткой.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - Изменение остаётся в Avalonia UI/ViewModel-слое.
  - Долгих синхронных операций на UI-потоке нет.
  - Стабильные selectors/automation-id будут сохранены; при добавлении marker для UI-теста использовать новый локальный automation id без изменения существующих.
  - Перед завершением EXEC будут запущены `dotnet build` и `dotnet test`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Вычисляемые marker-свойства и уведомления | Единый source of truth для списков и карты |
| `src/Unlimotion/Views/MainControl.axaml` | Marker controls в общем и inline-шаблонах задач | Видимая пометка в списках |
| `src/Unlimotion/Views/GraphControl.axaml` | Marker control в шаблоне узла задачи | Видимая пометка на карте |
| `src/Unlimotion.Test/*.cs` | Unit/UI regression tests | Проверить поведение и не допустить регрессии |
| `specs/2026-04-22-repeater-list-badge.md` | Рабочая спецификация | QUEST gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Обычная задача в списке | Checkbox, emoji/date, title | Без изменений, marker скрыт |
| Задача с active repeater | Визуально как обычная | Появляется `↻` рядом с title |
| Задача с active repeater на карте | Визуально как обычная | Появляется `↻` рядом с `TitleWithoutEmoji` |
| Данные задачи | Не меняются | Не меняются |
| Детали задачи | Есть выбор repeater | Без изменения UX |

## 18. Альтернативы и компромиссы
- Вариант: добавлять текстовую метку `Repeat`.
- Плюсы: очевидно при первом взгляде.
- Минусы: расширяет строки, требует локализации видимого текста, хуже для плотных списков.
- Почему выбранное решение лучше в контексте этой задачи: компактный символ с tooltip решает сканирование списка с минимальным визуальным шумом и без новых ресурсов.

- Вариант: показывать полный `Repeater.Title` прямо в строке.
- Плюсы: сразу видно тип повторения.
- Минусы: длинные строки, риск переполнения, дублирование информации из деталей.
- Почему выбранное решение лучше в контексте этой задачи: запрос просит дополнительную пометку, а не полное описание.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграции, правила, данные и rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Persisted model не меняется, rollback простой, риски перечислены |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, тесты и команды проверки указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | План пошаговый, блокирующих вопросов нет, масштаб small |
| F. Соответствие профилю | 20 | PASS | Требования `dotnet-desktop-client` отражены |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Требуется только marker для повторяемых задач в списках |
| 2. Понимание текущего состояния | 5 | Указаны VM, XAML-шаблоны и текущий repeater-контракт |
| 3. Конкретность целевого дизайна | 5 | Описаны свойства, binding-и, шаблоны и уведомления |
| 4. Безопасность (миграция, откат) | 5 | Нет изменения данных; rollback локальный |
| 5. Тестируемость | 5 | Есть unit и headless UI checks плюс команды |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов, план малый и последовательный |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлены edge cases для `RepeaterType.None`, вложенного `Repeater.Type`, отписки от старого `Repeater` и риска неполного обновления inline-шаблонов.
- Что осталось на решение пользователя: только подтверждение перехода в EXEC фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS с caveat по полному тестовому проекту.
- Реализовано по спеки: VM display-контракт добавлен, старая подписка на repeater заменяется через `SerialDisposable`, общий `DataTemplate`, 6 inline-шаблонов и узел карты получили marker рядом с заголовком.
- Проверки: `git diff --check` без ошибок; targeted unit/UI tests проходят, включая structural-проверку карты; `dotnet build src/Unlimotion.sln --no-restore` проходит.
- Caveat: полный `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --no-build -- --no-progress` падает на 11 существующих/окруженческих проверках: 10 `BackupViaGitServiceTests` с `LibGit2SharpException: path too long` из-за длинного пути worktree и 1 `SettingsViewModelTests.BackupConnection_BecomesReadyForClone_WhenSshKeyIsSelected` с английской строкой при полном параллельном прогоне. Тот же settings-тест отдельно проходит.

## Approval
Получена фраза: `Спеку подтверждаю`

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Создание worktree и выбор instruction stack | 0.95 | Нет | Подготовить рабочую спецификацию | Нет | Нет | Worktree нужен по запросу пользователя; stack выбран как delivery-task для .NET desktop | `C:\Projects\Education\Unlimotion Space\Unlimotion-repeater-list-badge`, `feature/repeater-list-badge` |
| SPEC | Анализ текущей реализации списков и repeater | 0.9 | Нет | Зафиксировать TO-BE и тест-план | Нет | Нет | Найдены общий `TaskItemViewModel` template, inline-шаблоны вкладок и `IsHaveRepeater` | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.ViewModel/TaskItemViewModel.cs`, `src/Unlimotion.ViewModel/RepeaterPatternViewModel.cs` |
| SPEC | Quality gate и post-SPEC review | 0.92 | Нет | Запросить подтверждение спеки | Да | Нет | По `quest-mode` переход к EXEC возможен только после фразы пользователя `Спеку подтверждаю` | `specs/2026-04-22-repeater-list-badge.md` |
| SPEC | Внесение исправлений по review спеки | 0.94 | Нет | Запросить подтверждение спеки | Да | Да, пользователь попросил внести исправления | Уточнён VM-контракт, lifecycle подписки и покрытие всех списочных шаблонов | `specs/2026-04-22-repeater-list-badge.md` |
| EXEC | Подтверждение спеки и старт реализации | 0.95 | Нет | Добавить regression-тесты ViewModel | Нет | Да, пользователь написал `Спеку подтверждаю` | EXEC разрешён по `quest-mode`; начинаю с тестов для marker-контракта | `specs/2026-04-22-repeater-list-badge.md` |
| EXEC | Реализация VM-контракта marker | 0.9 | Нет | Обновить XAML-шаблоны списков | Нет | Нет | Добавлены `RepeaterListMarker`, tooltip и заменяемая подписка на текущий `RepeaterPatternViewModel`; старый repeater больше не поднимает marker-уведомления | `src/Unlimotion.ViewModel/TaskItemViewModel.cs`, `src/Unlimotion.Test/TaskItemRepeaterListMarkerTests.cs` |
| EXEC | Обновление XAML и UI/structural tests | 0.88 | Нет | Запустить targeted tests и build | Нет | Нет | Marker добавлен в общий `DataTemplate` и 6 inline-шаблонов; тест проверяет AllTasksTree и покрытие inline-шаблонов | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/TaskListRepeaterMarkerUiTests.cs` |
| EXEC | Verification | 0.86 | Полный тестовый проект имеет существующие окруженческие падения | Выполнить post-EXEC review | Нет | Нет | Targeted tests проходят, solution build проходит; полный `Unlimotion.Test` падает на `BackupViaGitServiceTests` path too long и нестабильную локаль в существующем settings-тесте | `src/Unlimotion.Test/Unlimotion.Test.csproj`, `src/Unlimotion.sln` |
| EXEC | Post-EXEC review | 0.9 | Нет | Передать итог пользователю | Нет | Нет | Дифф проверен, XAML-покрытие всех inline-шаблонов подтверждено поиском, новые targeted tests и build проходят | `git diff --check`, `src/Unlimotion.ViewModel/TaskItemViewModel.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/*Repeater*Tests.cs` |
| EXEC | Расширение marker на карту | 0.9 | Нет | Передать итог пользователю | Нет | Да, пользователь попросил: `Ещё на карте тоже должно отображаться` | `GraphControl.axaml` использует отдельный шаблон узла, поэтому marker добавлен туда тем же VM-контрактом; targeted UI/structural tests, `git diff --check` и build проходят | `src/Unlimotion/Views/GraphControl.axaml`, `src/Unlimotion.Test/TaskListRepeaterMarkerUiTests.cs`, `specs/2026-04-22-repeater-list-badge.md` |
