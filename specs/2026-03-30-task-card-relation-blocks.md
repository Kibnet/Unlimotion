# Добавление связей через relation-блоки карточки задачи

## 0. Метаданные
- Тип (профиль): `dotnet-desktop-client`
- Overlay profile: `product-system-design`
- Контексты: `testing-dotnet`
- Владелец: Unlimotion desktop task card
- Масштаб: `medium`
- Ограничения:
  - Не менять persisted-модель задач и JSON-формат хранения.
  - Не расширять `ITaskStorage`.
  - Не менять drag-and-drop сценарии и существующее удаление связей.
- Связанные файлы:
  - `src/Unlimotion.ViewModel/MainWindowViewModel.cs`
  - `src/Unlimotion.ViewModel/TaskRelationPickerViewModel.cs`
  - `src/Unlimotion/Views/MainControl.axaml`
  - `src/Unlimotion/Views/MainControl.axaml.cs`
  - `src/Unlimotion.Test/MainWindowViewModelTests.cs`
  - `src/Unlimotion.Test/NotificationManagerWrapperMock.cs`

## 1. Overview / Цель
Сделать relation-блоки в карточке задачи интерактивными: пользователь должен уметь добавлять связи всех 4 типов прямо из карточки, без drag-and-drop.

## 2. Текущее состояние (AS-IS)
- В карточке задачи relation-блоки `Parents`, `Blocking`, `Containing`, `Blocked` существуют только для просмотра и удаления.
- Создание связей доступно через drag-and-drop и отдельные storage-команды.
- В кодовой базе уже есть `AutoCompleteZeroMinimumPrefixLengthDropdownBehaviour`, но в карточке задачи оно не используется.

## 3. Проблема
Пользователь не может добавить связь в месте, где он её видит и редактирует. Это делает карточку задачи односторонней и вынуждает переключаться на drag-and-drop даже для простых операций.

## 4. Цели дизайна
- Добавлять связи из всех 4 relation-блоков карточки.
- Делать выбор существующей задачи через inline autocomplete без отдельного диалога.
- Сохранять симметрию relation-логики и не дублировать storage API.
- Исключать невалидные кандидаты ещё до подтверждения и повторно валидировать перед мутацией.

## 5. Non-Goals
- Не добавлять создание новой задачи из relation picker.
- Не менять persisted schema, миграции или формат snapshot-файлов.
- Не переделывать drag-and-drop и текущую визуальную структуру карточки целиком.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
| Компонент / файл | Ответственность |
| --- | --- |
| `TaskRelationPickerViewModel.cs` | Состояние inline picker, поиск кандидатов, локальная валидация, confirm/cancel/open. |
| `MainWindowViewModel.cs` | Создание 4 picker-инстансов, mapping relation-kind -> storage-flow, повторная валидация и toast при отказе. |
| `MainControl.axaml` | Header row relation-блока, inline autocomplete, confirm/cancel UI, automation ids. |
| `MainControl.axaml.cs` | Enter/Escape обработчики для relation picker input. |
| `MainWindowViewModelTests.cs` | Happy-path и regression tests для add-flow и фильтрации кандидатов. |

### 6.2 Детальный дизайн
- У каждого relation-блока появляется header row с названием, `+` и `Cancel`.
- По `+` раскрывается inline row с `AutoCompleteBox`, confirm-button и zero-prefix dropdown.
- Search source совпадает с глобальным поиском: `OnlyTextTitle`, `Description`, `GetAllEmoji`, `Id`.
- `Settings.IsFuzzySearch` переиспользуется для picker-поиска.
- При пустом запросе показываются первые 30 валидных кандидатов по `Title`, затем по `Id`.
- `Parents` и `Containing` используют существующий flow через `CopyInto`.
- `Blocking` и `Blocked` используют существующий flow через `Block`.

## 7. Бизнес-правила / Алгоритмы
- Всегда исключать self-link и уже существующие прямые связи.
- Для parent-child использовать ту же проверку, что `CanMoveInto`.
- Для blocking исключать обратное блокирование, чтобы не допустить взаимо-блокировку.
- Перед confirm повторно валидировать кандидата на текущем состоянии репозитория.
- Если кандидат стал невалидным, связь не добавляется, storage не вызывается и показывается `ErrorToast`.

## 8. Точки интеграции и триггеры
- `CurrentTaskItem` change -> пересоздание 4 relation picker view model.
- `Open`/`Query` change -> перестроение candidate list.
- `Confirm` -> `ITaskStorage.CopyInto` или `ITaskStorage.Block`.

## 9. Изменения модели данных / состояния
- Persisted модель и storage schema не меняются.
- Добавляется только runtime UI state relation picker внутри `MainWindowViewModel`.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - добавить picker VM/types
  - подключить их в `MainWindowViewModel`
  - обновить XAML relation-блоков
  - добавить regression tests
- Rollback:
  - удалить picker VM/UI
  - вернуть relation-блоки в read-only режим

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
1. Все 4 relation-блока умеют добавлять существующую задачу через inline autocomplete.
2. При пустом запросе dropdown показывает валидных кандидатов без self/direct/mutual-invalid связей.
3. При попытке форсированного confirm невалидного кандидата связь не создаётся и показывается toast.
4. Существующие remove-flow и drag-and-drop сценарии остаются рабочими.

### Команды для проверки
```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --filter "FullyQualifiedName~Relation|FullyQualifiedName~MainWindowViewModel"
dotnet build
dotnet test
```

## 12. Риски и edge cases
- `AutoCompleteBox` может вести себя по-разному при keyboard-selection, поэтому Enter/Escape обрабатываются явно.
- Текущий storage-слой не валидирует mutual-blocking сам по себе, поэтому повторная валидация в VM обязательна.

## 13. План выполнения
1. Добавить spec и relation picker types.
2. Подключить pickers в `MainWindowViewModel`.
3. Обновить `MainControl` для inline add-flow.
4. Добавить happy-path и regression tests.
5. Прогнать targeted tests, build и full test run.

## 14. Открытые вопросы
Блокирующих вопросов нет. Пользователь подтвердил реализацию по этой спецификации 30 марта 2026.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - UI flow меняется только в desktop-клиенте.
  - существующий `CurrentTaskTitleTextBox` не меняется.
  - будут запущены `dotnet build` и `dotnet test`.

## 16. Альтернативы и компромиссы
- Вариант: отдельный диалог выбора задачи.
- Плюсы:
  - проще изолировать keyboard flow.
- Минусы:
  - медленнее пользовательский сценарий;
  - больше визуального шума и навигации.
- Почему выбранное решение лучше:
  - соответствует карточке как месту редактирования связей;
  - использует уже существующий autocomplete behavior;
  - не требует расширения storage API.

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и non-goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, rollback и ограничения описаны. |
| C. Безопасность изменений | 11-13 | PASS | Persisted schema не меняется, риски и edge cases выделены. |
| D. Проверяемость | 14-16 | PASS | Есть acceptance criteria и команды проверки. |
| E. Готовность к автономной реализации | 17-19 | PASS | Все ключевые решения зафиксированы. |
| F. Соответствие профилю | 20 | PASS | Спека соответствует `dotnet-desktop-client` и `product-system-design`. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Изменение ограничено relation-блоками карточки и не затрагивает persisted model. |
| 2. Понимание текущего состояния | 5 | Зафиксированы существующие remove-flow, drag-and-drop и storage команды. |
| 3. Конкретность целевого дизайна | 5 | Описаны UI-flow, mapping relation-kind и validation rules. |
| 4. Безопасность (миграция, откат) | 5 | Rollback прямой, миграций данных нет. |
| 5. Тестируемость | 5 | Есть happy path, invalid path и команды проверки. |
| 6. Готовность к автономной реализации | 5 | Все решения, влияющие на реализацию, зафиксированы. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

## Approval
Пользователь подтвердил реализацию 30 марта 2026.
