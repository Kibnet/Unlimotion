# Наследование Wanted для вложенных задач

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевой релиз / ветка: текущий worktree
- Ограничения: до подтверждения спеки менять только этот файл; после подтверждения обязательно добавить UI-покрытие и прогнать релевантные UI/.NET тесты
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `TaskItemViewModel`, `TaskTreeManager`, `MainControl.axaml`

## 1. Overview / Цель
Сделать поведение `Wanted` консистентным для вложенных задач:

- при создании вложенной задачи она наследует `Wanted` родительской задачи;
- при изменении `Wanted` у задачи с вложенными задачами показывается подтверждение по аналогии с архивацией: изменить только текущую задачу или также применить новое значение ко всем вложенным.

## 2. Текущее состояние (AS-IS)
- `UnifiedTaskStorage.AddChild` создает новый `TaskItem` и передает его в `TaskTreeManager.AddChildTask`.
- `TaskTreeManager.AddChildTask` сохраняет новый `TaskItem` с дефолтным `Wanted == false`, затем создает parent-child связь.
- В `TaskItemViewModel` свойство `Wanted` входит в общий список persisted-свойств и сохраняется через throttled `SaveItemCommand`.
- Для архивации уже есть похожая UX-логика: `ArchiveCommand` собирает вложенные задачи через `GetChildrenTasks`, затем вызывает `INotificationManagerWrapper.Ask`.
- В `MainControl.axaml` чекбокс `Wanted` находится в details pane и не имеет отдельного стабильного `AutomationId`.
- В проекте есть Avalonia Headless UI-тесты в `src/Unlimotion.Test`, поэтому UI-facing изменение должно сопровождаться UI-тестом.

## 3. Проблема
Вложенные задачи сейчас теряют контекст важности родителя при создании, а последующее изменение `Wanted` у родителя не предлагает синхронизировать уже существующие вложенные задачи.

## 4. Цели дизайна
- Разделение ответственности: наследование нового дочернего `Wanted` держать в доменной операции создания связи; UX-вопрос о каскаде держать во ViewModel рядом с текущей modal-логикой.
- Повторное использование: использовать существующие `GetChildrenTasks` и `INotificationManagerWrapper.Ask`.
- Тестируемость: добавить focused model/VM-проверки и headless UI-сценарий с реальным details checkbox.
- Консистентность: текст и поведение подтверждения должны быть аналогичны архивации вложенных задач.
- Обратная совместимость: не менять формат хранения и публичные интерфейсы без необходимости.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем модель данных `TaskItem` и формат persisted JSON.
- Не добавляем новые режимы фильтрации или сортировки.
- Не меняем поведение sibling/clone/copy/move, кроме прямого создания вложенной задачи.
- Не делаем массовую миграцию существующих задач.
- Не меняем глобальный UX notification manager.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs` -> при `AddChildTask` копирует `currentTask.Wanted` в новый `change.Wanted` до первого `Storage.Save(change)`.
- `src/Unlimotion.ViewModel/TaskItemViewModel.cs` -> предоставляет UI-only команду/метод изменения `Wanted`, спрашивает, применять ли новое значение к вложенным задачам, и при согласии каскадно выставляет `Wanted` потомкам.
- `src/Unlimotion.ViewModel/Resources/Strings.resx` и `Strings.ru.resx` -> добавляют локализованные header/message для подтверждения каскада.
- `src/Unlimotion/Views/MainControl.axaml` -> добавляет стабильный `AutomationId` чекбоксу `Wanted`.
- `src/Unlimotion.Test/*` -> добавляет/обновляет автоматические тесты, включая Avalonia Headless UI-покрытие.

### 6.2 Детальный дизайн
- В `AddChildTask` перед сохранением нового `TaskItem` выполнить `change.Wanted = currentTask.Wanted;`. Это сохраняет правило даже при вызове не из UI.
- Не запускать каскадный вопрос из общей подписки `WhenAnyValue(m => m.Wanted)`: требование строго UI-only.
- Добавить в `TaskItemViewModel` отдельную UI-only точку входа, например command/method `SetWantedFromUi(bool wanted)`, которую вызывает details checkbox. Прямое присваивание `Wanted` из кода, storage update, тестового setup или cascade не показывает modal.
- UI-only точка входа сначала меняет текущую задачу (`Wanted = wanted`), затем собирает `GetChildrenTasks(child => child.Wanted != wanted).ToList()`.
- Если список пуст или нет `NotificationManager`, ничего не каскадировать: текущая задача уже изменилась, сохранение остается существующим.
- Если список не пуст, вызвать `Ask(header, message, yesAction)`.
- `yesAction` выставляет всем найденным вложенным задачам `task.Wanted = wanted`; их сохранение остается через существующий throttled механизм.
- `noAction` не нужен: отказ означает "только текущей".
- Guard/suppress-флаг, если понадобится при реализации, должен подавлять только modal/cascade-логику, но не существующее сохранение `Wanted`.

## 7. Бизнес-правила / Алгоритмы
| Сценарий | Правило |
| --- | --- |
| Создание вложенной задачи у `Wanted == true` родителя | Новая задача получает `Wanted == true` |
| Создание вложенной задачи у `Wanted == false` родителя | Новая задача получает `Wanted == false` |
| Изменение `Wanted` у задачи без вложенных | Modal не показывается |
| UI-изменение `Wanted` у задачи с вложенными, где есть отличающиеся значения | Показать вопрос: только текущая или также все вложенные |
| Программное изменение `Wanted` вне UI-only точки входа | Modal не показывается, меняется только эта задача |
| Пользователь подтверждает каскад | Все рекурсивные descendant-задачи получают новое значение родителя |
| Пользователь отклоняет каскад | Меняется только текущая задача |
| Вложенные уже имеют целевое значение | Modal не показывается, потому что менять нечего |

## 8. Точки интеграции и триггеры
- `TaskTreeManager.AddChildTask` перед первым сохранением новой задачи.
- `TaskItemViewModel.Init` создает UI-only command/method для изменения `Wanted`; persisted property subscription остается только для сохранения.
- `MainControl.axaml` details pane checkbox вызывает UI-only trigger, а не полагается на side effect общей property subscription.
- Локализация через `L10n.Get` / `L10n.Format`.

## 9. Изменения модели данных / состояния
- Новых полей нет.
- `Wanted` остается persisted bool.
- Изменение влияет только на значения существующего поля при новых операциях и подтвержденном каскаде.

## 10. Миграция / Rollout / Rollback
- Миграция не нужна: существующие данные остаются как есть.
- Rollout: кодовая логика применяется к новым операциям.
- Rollback: вернуть изменения в перечисленных файлах; persisted schema не меняется.

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
- Новая вложенная задача наследует `Wanted` родителя.
- При UI-изменении `Wanted` родителя с вложенными задачами и подтверждении вопроса все рекурсивные descendant-задачи получают новое значение.
- При UI-отказе от вопроса меняется только родитель.
- При программном изменении `Wanted` вне UI-only точки входа вопрос не показывается.
- Вопрос не появляется при изменении `Wanted` задачи без вложенных или когда вложенным нечего менять.
- UI-тест использует стабильный `AutomationId`, а не текстовую/позиционную привязку.

### Какие тесты добавить/изменить
- Model/VM test в `MainWindowViewModelTests` или близком тестовом классе: `AddChild` наследует `Wanted`.
- VM-level test с `NotificationManagerWrapperMock`: подтверждение и отказ для UI-only каскада `Wanted`, а также прямое программное присваивание без modal.
- Avalonia Headless UI test в существующем стиле: открыть `MainControl`, выбрать родителя, переключить `CurrentTaskWantedCheckBox`, проверить каскад при `AskResult = true`.
- Avalonia Headless UI test или обоснованно VM-level contract test для отказа: переключить `CurrentTaskWantedCheckBox` при `AskResult = false` и проверить, что потомки не изменились.

### Команды для проверки
```powershell
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainWindowViewModelTests/*Wanted*"
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*Wanted*"
dotnet build src\Unlimotion.sln
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj
```

## 12. Риски и edge cases
- Programmatic `Update(TaskItem)` и прямое присваивание `Wanted` не должны вызывать modal, потому что modal запускается только через UI-only точку входа.
- Каскад у дочерних задач не должен вызывать повторные вопросы; прямое `task.Wanted = wanted` не проходит через UI-only trigger.
- Throttled save требует ожидания в тестах через существующий `TestHelpers.WaitThrottleTime`.
- Многородительские задачи: каскад от выбранного родителя меняет саму задачу как shared entity, что соответствует модели общего `TaskItem`, а не отдельного отображения.

## 13. План выполнения
1. Добавить failing tests для наследования и каскада `Wanted`.
2. Добавить `AutomationId` на details checkbox `Wanted` и headless UI test.
3. Реализовать наследование в `TaskTreeManager.AddChildTask`.
4. Реализовать modal/cascade логику в `TaskItemViewModel`.
5. Добавить локализацию EN/RU.
6. Запустить targeted tests, затем `dotnet build` и полный test run проекта.
7. Выполнить post-EXEC review, исправить найденное и обновить журнал.

## 14. Открытые вопросы
Нет блокирующих вопросов. Решение пользователя от 2026-04-28: вопрос о каскаде запускается строго только из UI; "всем вложенным" означает все рекурсивные descendant-задачи, а не только непосредственные дети.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, `ui-automation-testing`, `testing-dotnet`.
- Выполненные требования профиля:
  - UI-facing изменение будет покрыто Avalonia Headless UI-тестом.
  - Стабильный selector будет добавлен через `AutomationId`.
  - После реализации будут запущены targeted UI/.NET tests, `dotnet build` и полный тестовый прогон.
  - Изменения не должны блокировать UI-поток: modal уже асинхронно делегируется notification wrapper; каскад только выставляет in-memory bool на загруженных VM.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.TaskTreeManager/TaskTreeManager.cs` | Наследовать `Wanted` при `AddChildTask` | Доменное правило создания вложенной задачи |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | UI-only command/method изменения `Wanted`, modal и cascade | UI-facing правило изменения важности без modal на programmatic update |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | EN строки подтверждения | Локализация |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | RU строки подтверждения | Локализация |
| `src/Unlimotion/Views/MainControl.axaml` | `AutomationId` для Wanted checkbox | Стабильный UI test selector |
| `src/Unlimotion.Test/MainWindowViewModelTests.cs` | VM/domain regression tests | Проверка правил без лишней UI хрупкости |
| `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs` или близкий UI test файл | Headless UI test | Выполнение локального UI testing MUST |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Создание вложенной задачи | `Wanted` всегда default `false` | `Wanted` наследуется от родителя |
| UI-изменение родительской `Wanted` | Меняется только родитель без вопроса | При наличии отличающихся descendants показывается вопрос о каскаде |
| Программное изменение `Wanted` | Меняется задача без отдельного вопроса | Поведение сохраняется: вопрос не показывается |
| UI test selector | У details Wanted checkbox нет `AutomationId` | Есть стабильный `CurrentTaskWantedCheckBox` |

## 18. Альтернативы и компромиссы
- Вариант: делать наследование в `UnifiedTaskStorage.AddChild`.
  - Плюсы: меньше изменение в доменном менеджере.
  - Минусы: не покрывает прямые вызовы `TaskTreeManager.AddChildTask`.
  - Почему выбранное решение лучше: доменное правило должно жить там, где создается parent-child операция и сохраняется новая задача.
- Вариант: всегда каскадировать `Wanted` без вопроса.
  - Плюсы: проще.
  - Минусы: противоречит требованию "выводить запрос".
  - Почему выбранное решение лучше: сохраняет контроль пользователя и повторяет UX архивации.
- Вариант: спрашивать даже если все descendants уже имеют нужное значение.
  - Плюсы: буквальное "при изменении родительской".
  - Минусы: пустой вопрос без эффекта.
  - Почему выбранное решение лучше: как и архивация, спрашиваем только когда есть реально изменяемые вложенные задачи.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals зафиксированы |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, правила, состояние и rollout описаны |
| C. Безопасность изменений | 11-13 | PASS | Schema не меняется, rollback прямой, риски guard-флагов зафиксированы |
| D. Проверяемость | 14-16 | PASS | Есть acceptance criteria, UI/VM tests и команды |
| E. Готовность к автономной реализации | 17-19 | PASS | План пошаговый, блокирующих вопросов нет, масштаб small |
| F. Соответствие профилю | 20 | PASS | UI automation и .NET desktop требования явно включены |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Требование разложено на два конкретных поведения и Non-Goals |
| 2. Понимание текущего состояния | 5 | Указаны классы, текущие подписки, storage flow и UI selector gap |
| 3. Конкретность целевого дизайна | 5 | Определены методы, guard-подход, тексты и тестовые точки |
| 4. Безопасность (миграция, откат) | 5 | Нет schema changes, откат ограничен файлами изменения |
| 5. Тестируемость | 5 | Есть targeted VM/domain и headless UI тесты с командами |
| 6. Готовность к автономной реализации | 5 | План без блокирующих вопросов, допущение про рекурсивность явно указано |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: добавлено решение пользователя про строгий UI-only trigger; рекурсивность descendants переведена из предположения в бизнес-правило; уточнено, что suppress не должен отключать сохранение; добавлена проверка programmatic update без modal и отказа от UI-каскада.
- Что осталось на решение пользователя: требуется утверждение фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения: после первого targeted-прогона VM-тест отказа от каскада был уточнен на ожидание persisted-состояния родителя, потому что сохранение `Wanted` остается throttled.
- Что проверено дополнительно для refactor / comments: изменения остались в границах спеки, новых комментариев в production-код не добавлено, прямое программное изменение `Wanted` не вызывает modal, UI-binding использует отдельный `WantedFromUi`.
- Остаточные риски / follow-ups: `dotnet build src\Unlimotion.sln` не завершился за 5 минут в текущем окружении; основной `Unlimotion.Test` build и полный TUnit-прогон прошли.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Сбор instruction stack | 1.0 | Нет | Создать spec | Да, перед EXEC | Нет | Локальный `AGENTS.md` требует central stack и SPEC-first workflow | `AGENTS.md`, `AGENTS.override.md`, central instructions |
| SPEC | Анализ кода | 0.9 | Нет | Зафиксировать дизайн и тест-план | Да, перед EXEC | Нет | Найдены существующие точки `AddChildTask`, `TaskItemViewModel` и Avalonia Headless tests | `TaskTreeManager.cs`, `TaskItemViewModel.cs`, `MainControl.axaml`, тесты |
| SPEC | Спецификация и quality gate | 0.9 | Нет | Запросить подтверждение спеки | Да | Да, ожидается фраза `Спеку подтверждаю` | Спека готова, блокирующих вопросов нет | `specs/2026-04-27-inherit-wanted-nested-tasks.md` |
| SPEC | Обработка review-решений пользователя | 0.95 | Нет | Запросить подтверждение обновленной спеки | Да | Да, пользователь выбрал строгий UI-only trigger и рекурсивных descendants | Обновлены правила, acceptance criteria и тест-план под принятые решения | `specs/2026-04-27-inherit-wanted-nested-tasks.md` |
| EXEC | Реализация и тесты | 0.9 | Нет | Запустить targeted проверки | Нет | Нет | Добавлены наследование `Wanted`, UI-only binding/cascade, локализация и regression/UI tests | `TaskTreeManager.cs`, `TaskItemViewModel.cs`, `MainControl.axaml`, resources, tests |
| EXEC | Targeted verification | 0.9 | Нет | Запустить build/full tests | Нет | Нет | VM/domain Wanted tests и headless UI Wanted test прошли после уточнения ожидания persisted save | `MainWindowViewModelTests.cs`, `MainControlWantedUiTests.cs` |
| EXEC | Full verification и review | 0.9 | Нет | Финальный отчет | Нет | Нет | `Unlimotion.Test` build и полный TUnit-прогон прошли; solution build timed out, это зафиксировано | `specs/2026-04-27-inherit-wanted-nested-tasks.md` |
