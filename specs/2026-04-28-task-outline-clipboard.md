# Копирование задач outline через буфер обмена

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`, `ui-automation-testing`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: соблюдать существующие Avalonia/ViewModel паттерны; не менять формат хранения задач; UI-поведение покрыть Avalonia.Headless тестами
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`

## 1. Overview / Цель
Добавить копирование выбранной задачи вместе с подзадачами в текстовый outline в буфер обмена и вставку outline обратно как дерево задач.

Outcome contract:
- Success means: пользователь может скопировать текущую задачу как табулированный outline и вставить такой outline как новые задачи с сохранением иерархии.
- Итоговый артефакт / output: команды ViewModel/UI, outline serializer/parser, тесты ViewModel и Avalonia.Headless UI.
- Stop rules: остановиться после реализации, целевых тестов, build и доступного полного test-run; если full-run невозможен, зафиксировать причину.

## 2. Текущее состояние (AS-IS)
- `MainWindowViewModel` содержит команды создания/удаления/раскрытия и текущую выбранную задачу.
- `TaskItemViewModel.ContainsTasks` уже содержит загруженные дочерние задачи.
- `ITaskStorage` умеет добавлять корневую задачу и дочернюю задачу через `Add`/`AddChild`.
- `MainControl.axaml` и `MainControl.axaml.cs` уже обрабатывают TreeView hotkeys/context menu.
- Буфер обмена в текущем UI для задач не используется.

## 3. Проблема
Нет пользовательского пути для обмена задачами и подзадачами с внешними редакторами через простой текстовый outline.

## 4. Цели дизайна
- Разделение ответственности: сериализация/парсинг не зависят от Avalonia clipboard.
- Повторное использование: ViewModel команды используют общий service/helper.
- Тестируемость: serializer/parser проверяются unit-тестами, UI hotkeys/context menu проверяются headless тестом.
- Консистентность: команды не создают задачи при пустом буфере, пустой текущей задаче или невалидном outline.
- Обратная совместимость: существующее хранилище и relations model не меняются.

## 5. Non-Goals (чего НЕ делаем)
- Не копируем даты, статусы, важность, repeaters, блокировки и произвольные связи.
- Не меняем JSON-схему задач.
- Не добавляем drag/drop или import/export файлов.
- Не реализуем rich clipboard formats.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `TaskOutlineClipboardService` -> построение outline из `TaskItemViewModel` или текущего `TaskWrapperViewModel`, парсинг текста в дерево заголовков.
- `MainWindowViewModel` -> команды copy/paste outline и создание задач через `ITaskStorage`.
- `MainControl.axaml` -> keybindings/context menu items для `Ctrl+Shift+C`/`Ctrl+Shift+V`.
- `MainControl.axaml.cs` -> адаптер к `TopLevel.Clipboard`.
- Тесты -> unit + Avalonia.Headless сценарии.

### 6.2 Детальный дизайн
- Outline format: одна задача на строку; уровень задаётся ведущими табами или группами из 4 пробелов; заголовок берётся после опциональных маркеров `- `, `* `, `+ `.
- Copy: текущая задача становится корнем outline, дети выводятся рекурсивно по `ContainsTasks`.
- Paste: если есть текущая задача, верхний уровень outline создаётся как дочерние задачи текущей; если текущей нет, верхний уровень создаётся в корне.
- Ошибки: пустой/пробельный буфер или строки без текста игнорируются; jump indentation нормализуется к ближайшему родителю.
- Производительность: рекурсивный обход по уже загруженным relations; объём clipboard считается пользовательским и малым/средним.

## 7. Бизнес-правила / Алгоритмы
- Copy outline:
  - root: `CurrentTaskItem.Title`
  - child: `\t` * depth + `Title`
  - пустой title копируется как пустая строка только для root/child, но команда недоступна без текущей задачи.
- Paste outline:
  - строки whitespace-only пропускаются;
  - leading tab = 1 level;
  - каждые 4 leading spaces = 1 level;
  - `- `, `* `, `+ ` удаляются после indentation;
  - верхние элементы вставляются под текущую задачу или в корень.

## 8. Точки интеграции и триггеры
- `Ctrl+Shift+C` вызывает copy outline.
- `Ctrl+Shift+V` вызывает paste outline.
- Context menu TreeView получает пункты copy/paste outline.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Новые команды и clipboard delegates являются runtime state.

## 10. Миграция / Rollout / Rollback
- Миграция не нужна.
- Откат: удалить команды, UI bindings и service/helper без влияния на сохранённые задачи.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - копирование выбранной задачи с потомками пишет expected outline в clipboard;
  - вставка outline создаёт дерево задач под текущей задачей;
  - вставка без текущей задачи создаёт корневые задачи;
  - hotkeys не срабатывают в `TextBox`;
  - UI menu/hotkeys покрыты Avalonia.Headless.
- Какие тесты добавить/изменить:
  - unit tests для outline service/parser;
  - ViewModel tests для команд;
  - `MainControlTreeCommandsUiTests` или отдельный UI test class для `Ctrl+Shift+C`/`Ctrl+Shift+V`.
- Команды для проверки:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj`
  - `dotnet build src/Unlimotion.sln`
  - `dotnet test src/Unlimotion.sln`
- Stop rules для validation loops: после passing targeted UI/unit tests и build запустить full test; при внешнем blocker зафиксировать.

## 12. Риски и edge cases
- Outline hotkeys не должны перехватывать стандартные `Ctrl+C/V` в текстовых полях; для outline используется `Ctrl+Shift+C/V`.
- Clipboard недоступен в headless: использовать injectable delegates для ViewModel и простой fake в тесте.
- Множественный выбор: scope ограничен текущей задачей, не batch-selection.

## 13. План выполнения
1. Добавить outline service/helper и unit tests.
2. Добавить команды ViewModel и injectable clipboard delegates.
3. Подключить UI keybindings/context menu.
4. Добавить/обновить Avalonia.Headless UI tests.
5. Запустить targeted tests, build, full test.

## 14. Открытые вопросы
Нет блокирующих. Формат outline выбран как tab/4-space indentation с поддержкой Markdown bullet input.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`
- Выполненные требования профиля: изменения UI flow покрываются UI tests; команды не блокируют UI thread длительной синхронной работой; selectors остаются стабильными через AutomationId.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/*` | outline logic и команды | Поведение copy/paste |
| `src/Unlimotion/Views/MainControl.axaml*` | UI bindings/menu/clipboard adapter | Доступ пользователя |
| `src/Unlimotion.Test/*` | unit и UI tests | Acceptance criteria |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Clipboard | Для задач не используется | Copy/paste outline |
| Импорт дерева | Только ручное создание | Paste text outline создаёт дерево |
| Экспорт дерева | Нет | Copy current subtree as text |

## 18. Альтернативы и компромиссы
- Вариант: хранить JSON в clipboard.
- Плюсы: можно перенести больше полей.
- Минусы: хуже редактируется вручную, больше coupling к модели хранения.
- Почему выбранное решение лучше: пользователь попросил outline; простой текст совместим с заметками, markdown и внешними редакторами.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals указаны |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, данные и rollback описаны |
| C. Безопасность изменений | 11-13 | PASS | Есть acceptance criteria, риски и план |
| D. Проверяемость | 14-16 | PASS | Открытых blocker нет, file plan указан |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, tradeoff и quality gate заполнены |
| F. Соответствие профилю | 20 | PASS | UI tests обязательны и запланированы |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Copy/paste outline и non-goals зафиксированы |
| 2. Понимание текущего состояния | 5 | Указаны ViewModel, storage и UI точки |
| 3. Конкретность целевого дизайна | 5 | Формат outline, команды и обработка ошибок определены |
| 4. Безопасность (миграция, откат) | 5 | Persisted model не меняется, rollback простой |
| 5. Тестируемость | 5 | Unit, ViewModel и UI tests перечислены |
| 6. Готовность к автономной реализации | 5 | Блокирующих вопросов нет |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: уточнены hotkey guards для text input и clipboard injection для headless tests.
- Что осталось на решение пользователя: нет блокирующих решений.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: добавлен guard для fire-and-forget UI clipboard команд, чтобы ошибки clipboard/storage попадали в toast, а не в unobserved task.
- Что проверено дополнительно для refactor / comments: новых устаревших комментариев и изменений persisted schema нет.
- Остаточные риски / follow-ups: повторный полный `dotnet test src\Unlimotion.sln` после guard-правки не уложился в 20 минут без вывода; релевантный UI-класс после guard-правки прошёл.

## Approval
Пользователь уже запросил реализацию: "Реализуй Копирование задачи и подзадач в виде аутлайна в буфер обмена и обратно".

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | delivery-task | 0.90 | Нет блокирующих данных | Реализовать outline service и тесты | Нет | Пользователь запросил реализацию | Формат outline выбран простой и совместимый с текстовыми редакторами | `specs/2026-04-28-task-outline-clipboard.md` |
| EXEC | implementation | 0.88 | Нет | Запустить целевые проверки | Нет | Нет | Добавлены outline service, ViewModel commands, UI hotkeys/context menu и локализация | `src/Unlimotion.ViewModel/TaskOutlineClipboardService.cs`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.ViewModel/Resources/Strings*.resx` |
| EXEC | testing | 0.92 | Нет | Провести финальный sanity-pass | Нет | Нет | Добавлены unit/ViewModel/UI тесты; целевые проверки и первый full-run прошли | `src/Unlimotion.Test/TaskOutlineClipboardServiceTests.cs`, `src/Unlimotion.Test/MainWindowViewModelTests.cs`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` |
| EXEC | validation | 0.86 | Повторный full-run после guard-правки завис до таймаута | Завершить с указанием проверки и ограничения | Нет | Нет | Повторный UI-класс после guard-правки прошёл; зависшие dotnet-процессы от таймаута завершены | `specs/2026-04-28-task-outline-clipboard.md` |
| EXEC | hotkey-fix | 0.94 | Нет | Завершить с результатом проверки | Нет | Пользователь сообщил конфликт `Ctrl+C/V` с текстовыми полями | Outline hotkeys перенесены на `Ctrl+Shift+C/V`; `MainControlTreeCommandsUiTests` прошёл 26/26 | `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `specs/2026-04-28-task-outline-clipboard.md` |
| EXEC | filtered-copy | 0.91 | Нет | Завершить с результатом проверки | Нет | Пользователь уточнил, что copy должен учитывать фильтры и сортировку | Tree-copy теперь строит outline из текущего `TaskWrapperViewModel.SubTasks`; добавлен UI regression test; `dotnet build` и полный `dotnet test` прошли | `src/Unlimotion.ViewModel/TaskOutlineClipboardService.cs`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion/Views/MainControl.axaml.cs`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` |
