# Исправление копирования срока при создании задачи

## 0. Метаданные
- Тип (профиль): delivery-task; .NET desktop UI bugfix; overlay `ui-automation-testing`
- Владелец: Codex
- Масштаб: small
- Целевой релиз / ветка: текущая рабочая ветка
- Ограничения: до подтверждения спеки не менять код вне этого файла; сохранить существующие UX-команды создания задач; использовать UI-тесты
- Связанные ссылки: `AGENTS.md`, `AGENTS.override.md`, `C:\Projects\My\Agents\AGENTS.md`

## 1. Overview / Цель
Выяснить, почему при создании новой задачи в UI в нее переносится срок из последней выбранной задачи, и исправить это так, чтобы новые задачи создавались без плановых дат, если пользователь явно не задавал их для новой задачи.

## 2. Текущее состояние (AS-IS)
- `MainWindowViewModel.Create`, `CreateSibling`, `CreateInner` создают новую задачу через `ITaskStorage.Add()` / `AddChild()`, затем присваивают `CurrentTaskItem = newTask`.
- `UnifiedTaskStorage.Add()` и `AddChild()` передают в `TaskTreeManager` новый `TaskItem()`; в доменной модели `PlannedBeginDateTime` и `PlannedEndDateTime` по умолчанию `null`.
- В `MainControl.axaml` детали задачи используют один живущий экземпляр блока редактирования с `DataContext="{Binding CurrentTaskItem}"`.
- В этом блоке два `CalendarDatePicker` имеют `SelectedDate="{Binding PlannedBeginDateTime}"` и `SelectedDate="{Binding PlannedEndDateTime}"`.
- Существуют Avalonia.Headless UI-тесты в `src/Unlimotion.Test/*UiTests.cs`; локальный override требует добавлять и запускать UI-тесты для UI-facing багфиксов.

## 3. Проблема
При переключении с выбранной задачи со сроком на только что созданную задачу UI-состояние плановых дат переносится в новую задачу, хотя storage создает ее с пустыми датами.

Предварительная гипотеза: источник не в `TaskItem()` и не в `UnifiedTaskStorage`, а в UI-binding жизненном цикле `CalendarDatePicker` при смене `DataContext` деталей задачи. Один и тот же контрол может сохранить старое `SelectedDate` и записать его в новый `CurrentTaskItem` до того, как target обновится из нового source.

## 4. Цели дизайна
- Разделение ответственности: storage продолжает создавать пустую задачу; UI не должен записывать старое состояние в новую модель.
- Повторное использование: сохранить текущие команды и существующий details pane.
- Тестируемость: воспроизвести регрессию Avalonia.Headless UI-тестом до фикса.
- Консистентность: одинаковое поведение для `Create`, `CreateSibling`, `CreateInner`, если они показывают новую задачу в details pane.
- Обратная совместимость: не менять модель данных, формат storage и публичные интерфейсы.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем формат хранения задач и миграции.
- Не меняем бизнес-правила плановых дат, длительности и quick date commands.
- Не меняем UX создания задач за пределами сброса ошибочно перенесенного срока.
- Не исправляем unrelated layout/visual issues.
- Не рефакторим весь details pane.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/...UiTests.cs` -> regression UI-тест на создание задачи после выбора задачи со сроком.
- `src/Unlimotion/Views/MainControl.axaml` и/или `src/Unlimotion/Views/MainControl.axaml.cs` -> исправление UI-binding поведения, если подтвердится причина в reused `CalendarDatePicker`.
- `src/Unlimotion.ViewModel/MainWindowViewModel.cs` -> только если расследование покажет, что проблему безопаснее гасить на уровне команды создания; без изменения публичного API.

### 6.2 Детальный дизайн
- Сначала добавить failing Avalonia.Headless тест:
  - открыть `MainControl` с `DetailsAreOpen = true`;
  - выбрать существующую задачу;
  - задать ей `PlannedBeginDateTime` и `PlannedEndDateTime`;
  - выполнить создание новой задачи через UI-facing command;
  - дождаться UI jobs;
  - проверить, что новая `CurrentTaskItem` имеет `PlannedBeginDateTime == null` и `PlannedEndDateTime == null`.
- Затем локализовать точный момент записи старой даты:
  - сравнить состояние сразу после `taskRepository.Add()` и после `CurrentTaskItem`/binding update;
  - проверить, воспроизводится ли перенос только при созданном `MainControl`.
- Исправить минимально:
  - предпочтительно на UI-слое, чтобы `CalendarDatePicker` не пушил старое target value при смене `DataContext`;
  - если UI-layer fix окажется нестабильным, рассмотреть явный post-create reset плановых дат на newly-created task после установки `CurrentTaskItem`, но только если тест подтвердит отсутствие гонки и не затронет clone/repeater сценарии.
- Ошибки: новых пользовательских ошибок не добавляется.
- Производительность: изменение локальное, без длительных операций на UI-потоке.

## 7. Бизнес-правила / Алгоритмы
- Новая задача без явного пользовательского выбора срока должна иметь:
  - `PlannedBeginDateTime == null`;
  - `PlannedEndDateTime == null`.
- Срок выбранной ранее задачи не является default value для новых задач.
- Существующее правило `TaskItemViewModel`: при ручной установке begin может автоматически выставляться end, остается без изменений.

## 8. Точки интеграции и триггеры
- Триггеры создания: `Create`, `CreateSibling`, `CreateInner`, `CreateBlockedSibling` через `CreateSibling(true)`.
- UI-триггер: смена `CurrentTaskItem` в details pane.
- Триггер сохранения: property changed throttle в `TaskItemViewModel`; тест должен учитывать, что ошибочная запись может попасть и в storage.

## 9. Изменения модели данных / состояния
- Новых persisted-полей нет.
- Изменений storage-схемы нет.
- Изменение касается только transient UI state и/или присваиваний newly-created `TaskItemViewModel`.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- Rollout: обычный desktop build/test.
- Rollback: откатить локальный UI/ViewModel фикс и regression-тест.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - UI-тест воспроизводит сценарий: после выбора задачи со сроком новая задача создается без `PlannedBeginDateTime` и `PlannedEndDateTime`.
  - Исправление проходит targeted UI-тест.
  - `dotnet build` проходит.
  - Полный тестовый прогон проходит или явно зафиксирована внешняя причина невозможности запуска.
- Какие тесты добавить/изменить:
  - добавить Avalonia.Headless regression test рядом с существующими `MainControl*UiTests`.
  - при необходимости добавить ViewModel/unit test, если причина окажется не UI-specific.
- Characterization tests:
  - перед фиксом targeted UI-тест должен падать на текущем поведении.
- Команды для проверки:
  - определить runner: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --list-tests`
  - targeted TUnit run: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/MainControlNewTaskDeadlineUiTests/*"`
  - `dotnet build src/Unlimotion.sln`
  - full test run: `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj`

## 12. Риски и edge cases
- Риск: Avalonia binding может быть timing-sensitive; тест должен выполнять `Dispatcher.UIThread.RunJobs()` и проверять после UI update.
- Риск: прямой reset дат в ViewModel может скрыть UI-причину, но не решить другие future controls. Поэтому предпочтителен UI-level fix после подтверждения причины.
- Edge cases: создание root, sibling, child; blocked sibling использует sibling path.
- Edge case: выбранная задача имеет только begin или только end.

## 13. План выполнения
1. После подтверждения спеки добавить failing Avalonia.Headless regression test.
2. Запустить targeted test и зафиксировать падение.
3. Найти точный источник записи даты через код и минимальную диагностическую проверку.
4. Внести минимальный fix в UI/ViewModel.
5. Запустить targeted UI-тест.
6. Запустить `dotnet build src/Unlimotion.sln`.
7. Запустить полный тестовый прогон проекта.
8. Выполнить post-EXEC review и при необходимости поправить выявленные проблемы.

## 14. Открытые вопросы
Нет блокирующих вопросов. Требуется только формальное подтверждение перехода в EXEC-фазу фразой `Спеку подтверждаю`.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`; overlay `ui-automation-testing`; context `testing-dotnet`.
- Выполненные требования профиля:
  - запланирован UI regression test в существующем Avalonia.Headless стиле;
  - запланированы targeted и full .NET проверки;
  - публичный API и storage-схема не меняются;
  - selectors/automation-id без необходимости не меняются.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-04-27-new-task-deadline-reset.md` | Рабочая спецификация | QUEST SPEC gate |
| `src/Unlimotion.Test/MainControlNewTaskDeadlineUiTests.cs` или существующий UI test file | Добавить regression UI-тест | Воспроизвести и закрепить баг |
| `src/Unlimotion/Views/MainControl.axaml` / `.axaml.cs` | Вероятный UI fix | Предотвратить перенос `CalendarDatePicker.SelectedDate` |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Возможный fallback fix | Только если причина окажется в command flow |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Создание задачи после выбора задачи со сроком | Новая задача может получить старый срок | Новая задача остается без срока |
| UI-тесты | Нет regression coverage на перенос срока | Есть UI-тест на сценарий |
| Storage schema | Без изменений | Без изменений |

## 18. Альтернативы и компромиссы
- Вариант: сбрасывать даты в `Create*` после создания.
- Плюсы: простой и локальный.
- Минусы: может маскировать UI-binding баг и быть чувствительным к порядку UI updates.
- Почему выбранное решение лучше в контексте этой задачи: сначала подтверждаем источник UI-тестом и расследованием; если причина в `CalendarDatePicker` binding, исправляем там, где возникает перенос состояния.

- Вариант: пересоздавать весь details editor при смене задачи.
- Плюсы: убирает retained target state.
- Минусы: больше UI churn, риск фокуса/layout side effects.
- Почему не выбран первым: может быть избыточно для двух date picker bindings.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, дизайн-цели и Non-Goals описаны |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, правила, состояние и rollback зафиксированы |
| C. Безопасность изменений | 11-13 | PASS | Нет миграций/API-изменений; риски и план ограничены |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, UI-тест и команды указаны |
| E. Готовность к автономной реализации | 17-19 | PASS | План есть, блокирующих вопросов нет, масштаб small |
| F. Соответствие профилю | 20 | PASS | .NET desktop + UI automation требования учтены |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Один баг и четкие Non-Goals |
| 2. Понимание текущего состояния | 5 | Зафиксированы команды, storage, details binding и UI tests |
| 3. Конкретность целевого дизайна | 5 | Указан порядок TDD, расследования и fix surface |
| 4. Безопасность (миграция, откат) | 5 | Нет схемных изменений; rollback простой |
| 5. Тестируемость | 5 | Есть targeted UI test и full verification commands |
| 6. Готовность к автономной реализации | 5 | Нет открытых вопросов, реализация ограничена |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: уточнено, что preferred fix должен устранять UI-binding перенос, а ViewModel reset допустим только как подтвержденный fallback.
- Что осталось на решение пользователя: подтвердить переход в EXEC-фазу.

### Post-EXEC Review
- Статус: PASS с оговоркой по full suite
- Что проверено: targeted UI tests прошли 8/8; create-тесты `MainWindowViewModelTests` прошли 17/17; `dotnet build src/Unlimotion.sln --no-restore -m:1 -nr:false` прошел.
- Оговорка: полный `dotnet run --no-build --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --timeout 15m --output Detailed --no-progress` был отменен TUnit по таймауту 15 минут; видимая часть тестов, включая новые UI-тесты, прошла, затем cleanup выдал фоновые `FileSystemWatcher` / temp config exceptions после cancellation.
- Риск после review: низкий для затронутого сценария; остается общий риск долгого полного suite в текущем окружении.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Перечитать инструкции и собрать stack | 0.95 | Нет | Создать SPEC | Нет | Нет | Локальный `AGENTS.md` указывает central stack и local override | `AGENTS.md`, `AGENTS.override.md`, `C:\Projects\My\Agents\AGENTS.md` |
| SPEC | Осмотреть AS-IS без изменения кода | 0.85 | Точный runtime-момент записи даты будет проверен тестом в EXEC | Запросить подтверждение спеки | Да | Да, требуется фраза `Спеку подтверждаю` | Для UI bugfix нужен сначала failing UI test, затем fix | `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion/UnifiedTaskStorage.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion.Test/*UiTests.cs` |
| EXEC | Добавить regression UI-тест | 0.85 | Нужно подтвердить фактическое падение до фикса | Запустить targeted UI-тест | Нет | Пользователь подтвердил спеку фразой `Спеку подтверждаю` | Тест воспроизводит UI-сценарий через Avalonia.Headless и кнопку создания | `src/Unlimotion.Test/MainControlNewTaskDeadlineUiTests.cs`, `specs/2026-04-27-new-task-deadline-reset.md` |
| EXEC | Локализовать источник переноса срока | 0.75 | Headless не воспроизвел прямую запись старых дат в модель | Исправить storage selection и UI stale-source guards | Нет | Нет | Storage выбирал созданную задачу по `CreatedDateTime`, а UI-поля срока/длительности не должны писать retained display state при смене задачи | `src/Unlimotion/UnifiedTaskStorage.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Behavior/PlannedDurationBehavior.cs` |
| EXEC | Внести fix и проверить targeted UI | 0.9 | Нет для целевого сценария | Запустить build и полный тестовый прогон | Нет | Нет | Targeted Avalonia.Headless набор `MainControlNewTaskDeadlineUiTests` прошел: 8/8 | `src/Unlimotion.Test/MainControlNewTaskDeadlineUiTests.cs`, `src/Unlimotion/UnifiedTaskStorage.cs`, `src/Unlimotion/Views/MainControl.axaml`, `src/Unlimotion/Behavior/PlannedDurationBehavior.cs` |
| EXEC | Проверить сборку и дополнительные тесты | 0.9 | Полный suite не успел завершиться за 15 минут | Выполнить post-EXEC review | Нет | Нет | `dotnet build src/Unlimotion.sln --no-restore -m:1 -nr:false` прошел; старые create-тесты ViewModel прошли 17/17; полный TUnit был отменен по `--timeout 15m` после прохождения видимой части, включая новые UI-тесты | `src/Unlimotion.sln`, `src/Unlimotion.Test/Unlimotion.Test.csproj` |
