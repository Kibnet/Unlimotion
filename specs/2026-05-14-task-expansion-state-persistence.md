# Сохранение состояния разворачивания деревьев задач

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`.
- Владелец: Codex / пользователь.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка репозитория.
- Ограничения: central QUEST SPEC-first; на фазе SPEC изменяется только этот файл; UI-изменение требует UI test coverage; состояние разворачивания должно храниться в отдельном json рядом с основным config-файлом.
- Связанные ссылки: `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `AGENTS.override.md`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.ViewModel/TaskWrapperViewModel.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, `src/Unlimotion.Test/MainControlTreeCommandsUiTests.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs`.

## 1. Overview / Цель
Добавить пользовательскую настройку, которая включает сохранение статусов разворачивания деревьев задач между перезапусками приложения. При включении настройки список развёрнутых задач должен записываться в отдельный json-файл рядом с основным конфигом, а при следующем запуске восстанавливаться.

Outcome contract:
- Success means: в Settings есть чекбокс; настройка сохраняется в основном конфиге; при включении списки развёрнутых task id по отдельным деревьям сохраняются в отдельный json рядом с config; после пересоздания ViewModel/перезапуска развёрнутые задачи восстанавливаются; существующее session-only восстановление при поиске не ломается.
- Итоговый артефакт / output: изменения ViewModel, app wiring, XAML/resources и автоматические тесты.
- Stop rules: остановиться перед реализацией до фразы пользователя `Спеку подтверждаю`; после EXEC остановиться только после targeted UI/ViewModel tests, `dotnet build`, полного `dotnet test` или явного отчёта о невозможности запуска.

## 2. Текущее состояние (AS-IS)
- `TaskWrapperViewModel` получает начальное `IsExpanded` через `TaskWrapperActions.GetExpansionState` и сообщает изменения через `SetExpansionState`.
- `MainWindowViewModel.Connect()` создаёт отдельные in-memory словари `allTasksExpansionState`, `unlockedExpansionState`, `completedExpansionState`, `archivedExpansionState`, `lastCreatedExpansionState`, `lastUpdatedExpansionState`, `lastOpenedExpansionState`.
- Локальная функция `TrackExpansionState(...)` привязывает эти словари к wrapper-ам. Это уже восстанавливает состояние после фильтрации/поиска внутри текущего `Connect()`, но не переживает новый запуск.
- После выбора persisted-формата как списка развёрнутых задач эти in-memory словари тоже избыточны: для runtime cache достаточно `HashSet<string>` expanded ids на дерево.
- `SettingsViewModel` пишет настройки через `WritableJsonConfiguration` в основной конфиг. В Settings UI уже есть чекбоксы для похожих bool-настроек (`IsFuzzySearch`, clipboard, auto update).
- `App.Init(string configPath)` знает путь к конфигу, но `MainWindowViewModel` сейчас получает только `IConfiguration`, без явного пути к config.
- UI-тесты есть в `src/Unlimotion.Test`: headless Avalonia тесты для Settings и tree commands.

## 3. Проблема
Пользователь вручную разворачивает нужные ветки дерева, но после перезапуска приложения состояние разворачивания сбрасывается, потому что текущая логика хранит его только в памяти.

## 4. Цели дизайна
- Разделение ответственности: настройка включения в `SettingsViewModel`; чтение/запись файла состояния в отдельном небольшом сервисе/классе; применение состояния в `MainWindowViewModel`.
- Повторное использование: использовать существующие hooks `GetExpansionState`/`SetExpansionState`, не переписывать биндинги дерева.
- Тестируемость: file-backed storage должен быть проверяем без UI; checkbox должен проверяться existing Avalonia.Headless pattern.
- Консистентность: новая настройка должна выглядеть как существующие bool-настройки в Settings и иметь stable `AutomationId`.
- Обратная совместимость: по умолчанию persistence выключен; без файла состояния приложение ведёт себя как сейчас.

## 5. Non-Goals (чего НЕ делаем)
- Не сохраняем selected item, scroll position, активную вкладку, search/filter/sort сверх уже существующих настроек.
- Не переносим состояние разворачивания в файлы задач и не меняем формат задач.
- Не меняем поведение команд expand/collapse, поиска и lazy projections кроме добавления optional persistence.
- Не добавляем синхронизацию состояния разворачивания через Git backup/server storage.
- Не создаём миграцию старых in-memory состояний, потому что они не persisted.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `SettingsViewModel.cs` -> новое свойство `PersistTaskTreeExpansionState`, чтение/запись bool в основной конфиг.
- `SettingsControl.axaml` -> новый `CheckBox` с `AutomationId="PersistTaskTreeExpansionStateCheckBox"` в секции `Appearance`, рядом с `FuzzySearch`, потому что обе настройки управляют локальным UI-facing поведением.
- `Strings.resx` / `Strings.ru.resx` -> тексты для чекбокса и при необходимости label/hint.
- Новый класс во `Unlimotion.ViewModel` (например, `TaskTreeExpansionStateStore`) -> загрузка отдельного json-файла, накопление dirty changes и пакетная throttled-запись.
- `App.axaml.cs` и test host/fixture -> передают путь к json-файлу, рассчитанный от config path.
- `MainWindowViewModel.cs` -> заменяет per-tree expansion dictionaries на `HashSet<string>` expanded ids и использует store при создании sets и при `SetExpansionState`.

### 6.2 Детальный дизайн
- Config key: `TaskTreeExpansionState:Enabled`.
- Файл состояния: `TaskTreeExpansionState.json` в той же директории, что и основной config. Пример: если config `C:\...\Settings.json`, state file `C:\...\TaskTreeExpansionState.json`.
- Формат файла:
  ```json
  {
    "Version": 1,
    "Trees": {
      "AllTasksTree": [ "<expanded-task-id>" ],
      "UnlockedTree": [ "<expanded-task-id>" ]
    }
  }
  ```
- Сохраняются только развёрнутые task id. Свернутое состояние является default; при `IsExpanded=false` id удаляется из набора.
- При включённой настройке `MainWindowViewModel.Connect()` загружает файл один раз в модель состояния и отдаёт каждому дереву свой набор expanded ids.
- При изменении `TaskWrapperViewModel.IsExpanded` `SetExpansionState` добавляет id в набор при `true`, удаляет при `false`, и помечает состояние dirty, если `Settings.PersistTaskTreeExpansionState == true`.
- При выключенной настройке in-memory наборы продолжают работать внутри текущего `Connect()`, но файл не читается и не пишется.
- Runtime `GetExpansionState` для set-only cache возвращает `true`, если id есть в наборе, и `null`, если id отсутствует. Это сохраняет существующий fallback на `TaskWrapperViewModel.DefaultIsExpanded`: в обычном режиме default остаётся collapsed, а automation-сценарии с `DefaultIsExpanded=true` не ломаются. Точное хранение explicit collapsed в default-expanded automation режиме не входит в эту задачу.
- Если json отсутствует, пустой или повреждён, состояние считается пустым; приложение не должно падать. Ошибку можно игнорировать либо показать toast только при write failure, если это уже соответствует pattern. Для минимального изменения предпочтительно не блокировать UI и не падать.
- Запись MUST быть пакетной и throttled: серия изменений, включая `ExpandAll/CollapseAll`, не должна писать файл на каждую задачу отдельно. Store должен планировать запись после короткого quiet period (ориентир 500 ms) и сериализовать один snapshot всех sets.
- Store должен поддерживать принудительный flush pending write при disposal/reset connection, чтобы не терять последнее изменение при закрытии окна или переподключении.
- Фактическая запись должна быть атомарной overwrite-записью небольшого json с `WriteIndented`. Ошибки записи не должны падать через UI-поток; допустимо сохранить их как no-op/loggable failure без блокировки работы приложения.
- Путь к файлу не должен попадать в основной конфиг как persisted value, чтобы не ломать portable config.

## 7. Бизнес-правила / Алгоритмы
- `PersistTaskTreeExpansionState == false`: поведение как сейчас, файл состояния не создаётся новыми expand/collapse действиями.
- `PersistTaskTreeExpansionState == true`: каждое изменение `IsExpanded` для task id обновляет список развёрнутых задач конкретного дерева и планирует одну пакетную запись после throttle window.
- State scope per tree: один и тот же task id может иметь разные состояния в `AllTasksTree`, `LastCreatedTree`, `UnlockedTree` и других projections.
- Если id отсутствует в set, wrapper использует прежний fallback `DefaultIsExpanded`; в пользовательском runtime это даёт свернутое состояние по умолчанию.
- Unknown tree names in json are ignored by текущие projections, но сохраняются при round-trip только если это не усложняет реализацию; допустимо перезаписать только known trees.
- Пустые/whitespace task id не сохраняются. Дубликаты task id при чтении нормализуются в один id.
- `ExpandAll/CollapseAll` над веткой или деревом должен приводить максимум к одной фактической записи после завершения burst, а не к записи на каждый wrapper.

## 8. Точки интеграции и триггеры
- Settings checkbox -> `SettingsViewModel.PersistTaskTreeExpansionState` -> основной config.
- `MainWindowViewModel.Connect()` -> load state when enabled, wire per-tree expanded id sets.
- `TaskWrapperViewModel.IsExpanded` setter -> existing `SetExpansionState` hook -> update set and schedule throttled batch write.
- App startup/test host/fixture -> calculate state json path from known config path and provide it to ViewModel/store.

## 9. Изменения модели данных / состояния
- Новый config bool: настройка включения persistence.
- Новый sidecar json-файл рядом с config: persisted lists of expanded task ids.
- Данные задач и server model не меняются.
- In-memory `HashSet<string>` остаются как runtime cache и источник для current session restore.

## 10. Миграция / Rollout / Rollback
- Первый запуск: config key отсутствует -> default `false`; sidecar файл не нужен.
- Включение настройки: файл создаётся при первом изменении expand/collapse.
- Отключение настройки: файл не удаляется автоматически; повторное включение может восстановить прежние состояния. Это безопаснее, чем silently deleting user state.
- Повреждённый файл: игнорировать и продолжить с пустым состоянием; следующая успешная запись перезапишет корректным форматом.
- Rollback: удалить новый config key и sidecar json; приложение вернётся к session-only состоянию.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- В Settings отображается чекбокс сохранения состояния разворачивания с устойчивым `AutomationId`.
- Чекбокс меняет `SettingsViewModel.PersistTaskTreeExpansionState` и пишет bool в основной config.
- При выключенной настройке разворачивание не создаёт sidecar json.
- При включенной настройке разворачивание узла добавляет task id в sidecar json, а сворачивание удаляет task id из списка.
- Массовое разворачивание/сворачивание пишет sidecar json пакетно после throttle window, а не на каждую задачу отдельно.
- После пересоздания `MainWindowViewModel` с тем же config path и включённой настройкой состояние соответствующего дерева восстанавливается.
- Существующий тест `TreeSearch_ClearSearch_RestoresExpansionState` остаётся зелёным.

Какие тесты добавить/изменить:
- `SettingsViewModelTests`: persist default/true для новой настройки.
- `SettingsControlResponsiveUiTests`: headless UI test для нового checkbox и binding.
- `MainWindowViewModelTests` или `MainControlTreeCommandsUiTests`: persistence через sidecar json для at least `AllTasksTree`; при низкой стоимости добавить parametrized coverage для нескольких tree names.
- Тест на disabled mode: файл не создаётся при expand/collapse.
- Тест store batching/throttling: несколько изменений подряд приводят к одной записи snapshot после throttle/flush, а не к записи на каждое изменение.
- При необходимости unit-тест store на повреждённый json.

Команды для проверки:
- Targeted: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj --filter "FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~SettingsControlResponsiveUiTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~MainControlTreeCommandsUiTests"`
- Build: `dotnet build src/Unlimotion.sln`
- Full: `dotnet test src/Unlimotion.sln`

Stop rules для validation loops:
- Повторять targeted tests после каждой правки persistence logic или UI binding.
- Full test запускать после targeted green.
- Если full test невозможен из-за времени/окружения, зафиксировать targeted/build результат и точную причину.

## 12. Риски и edge cases
- Нет config path в некоторых тестовых/ручных конструкторах: использовать no-op store или optional path, чтобы не ломать существующие вызовы.
- Throttled-запись может потерять последнее изменение при резком закрытии процесса без штатного dispose; mitigation: flush при disposal/reset connection и тест на принудительный flush.
- Повреждённый json не должен ломать startup.
- Lazy projections: set состояния для вкладки должен применяться при её первой активации, а не только во время первого `Connect()`.
- Поиск временно фильтрует wrapper-ы; сохранённое состояние не должно перетирать пользовательский current-session state при очистке поиска.

## 13. План выполнения
1. Добавить model/store для sidecar expansion state с безопасной загрузкой, dirty tracking, throttled batch save и forced flush.
2. Добавить настройку в `SettingsViewModel` и локализованные UI strings.
3. Добавить checkbox в `SettingsControl.axaml` со stable `AutomationId`.
4. Прокинуть путь sidecar json из app/test host/fixture, сохранив существующие call sites через optional/no-op path.
5. Заменить локальные per-tree dictionaries в `MainWindowViewModel.Connect()` на runtime `HashSet<string>` expanded ids, инициализированные из store при включенной настройке.
6. Добавить/обновить tests.
7. Запустить targeted tests, build, full test.
8. Выполнить post-EXEC review и исправить критичные находки.

## 14. Открытые вопросы
Нет блокирующих вопросов. Product choice принят в рамках запроса: sidecar filename `TaskTreeExpansionState.json`, setting default `false`, файл не удаляется при выключении.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`.
- Выполненные требования профиля: spec предусматривает сохранение UI-поведения без блокировки UI-потока, stable automation id, UI test coverage, targeted/full .NET checks.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Добавить bool-свойство настройки | Persist setting in config |
| `src/Unlimotion.ViewModel/TaskTreeExpansionStateStore.cs` | Новый store sidecar json с throttled batch write и forced flush | Изолировать файловую persistence-логику и не писать на диск на каждую задачу |
| `src/Unlimotion.ViewModel/MainWindowViewModel.cs` | Заменить expansion dictionaries на sets и подключить store | Восстановление/сохранение развёрнутых задач |
| `src/Unlimotion/Views/SettingsControl.axaml` | Добавить checkbox | Пользовательское управление |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | EN strings | Локализация UI |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | RU strings | Локализация UI |
| `src/Unlimotion/App.axaml.cs` | Рассчитать sidecar path рядом с config | Runtime wiring |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAppLaunchHost.cs` | Передать sidecar path в headless host | UI/integration тесты |
| `src/Unlimotion.Test/MainWindowViewModelFixture.cs` | Передать sidecar path, cleanup | Repository test pattern |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | Unit coverage setting | Config persistence |
| `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` | UI checkbox coverage | UI requirement |
| `src/Unlimotion.Test/MainWindowViewModelTests.cs` или `MainControlTreeCommandsUiTests.cs` | Persistence behavior coverage | Regression |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Expansion state | In-memory словари bool в текущем `Connect()` | In-memory sets + optional persisted sidecar json со списками expanded task ids per tree |
| Settings UI | Нет настройки | Checkbox включает/выключает persistence |
| Startup | Нет восстановления после перезапуска | При enabled загружает sidecar json |
| Disabled mode | Session-only | Session-only сохранён |

## 18. Альтернативы и компромиссы
- Вариант: хранить состояния в основном config.
- Плюсы: меньше файлов.
- Минусы: пользователь явно попросил специальный json рядом с config; config будет часто меняться и разрастаться.
- Почему выбранное решение лучше в контексте этой задачи: sidecar json отделяет runtime UI-state от пользовательских настроек и соответствует запросу.

- Вариант: хранить состояние в файлах задач.
- Плюсы: состояние рядом с task data.
- Минусы: меняет domain data, может попасть в Git sync/server sync и загрязнить историю задач UI-state.
- Почему выбранное решение лучше в контексте этой задачи: состояние разворачивания является локальным UI-state.

- Вариант: хранить словарь task id -> bool.
- Плюсы: напрямую соответствует текущему runtime-состоянию `IsExpanded`.
- Минусы: `false` избыточен, потому что свернутое состояние является default; json становится шумнее.
- Почему выбранное решение лучше в контексте этой задачи: список развёрнутых задач компактнее, проще для чтения и достаточно точно описывает persisted contract. При сворачивании task id удаляется из списка.

- Вариант: писать json сразу на каждое изменение `IsExpanded`.
- Плюсы: проще реализация, меньше риска потерять последнее изменение при аварийном завершении процесса.
- Минусы: `ExpandAll/CollapseAll` создаёт запись на каждую задачу и может заметно грузить диск/UI.
- Почему выбранное решение лучше в контексте этой задачи: throttled batch write сохраняет тот же итоговый snapshot, но резко снижает количество файловых операций; forced flush покрывает штатное закрытие/переподключение.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, формат json, интеграции, ошибки, rollout описаны. |
| C. Безопасность изменений | 11-13 | PASS | Есть тест-план, риски, phased implementation. |
| D. Проверяемость | 14-16 | PASS | Acceptance Criteria, команды и таблица файлов есть. |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет блокирующих вопросов, альтернативы и quality gate заполнены. |
| F. Соответствие профилю | 20 | PASS | UI automation и .NET desktop требования учтены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---|---|
| 1. Ясность цели и границ | 5 | Запрос сведен к optional persistence expansion state. |
| 2. Понимание текущего состояния | 5 | Зафиксированы текущие hooks и per-tree dictionaries, которые будут заменены на sets. |
| 3. Конкретность целевого дизайна | 5 | Указаны config key, sidecar file, schema, wiring. |
| 4. Безопасность (миграция, откат) | 5 | Default off, no task data migration, corrupt json handling. |
| 5. Тестируемость | 5 | Указаны unit/UI/behavior tests и команды. |
| 6. Готовность к автономной реализации | 5 | План не требует открытых решений пользователя. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено: в spec явно добавлены disabled-mode acceptance, corrupt json behavior, lazy projection риск, stable `AutomationId`, запрет писать путь sidecar в основной config; после замечаний пользователя формат persistence упрощён со словаря bool до списков expanded task ids, runtime cache также переведён со словарей на sets; после ревью выбран конкретный config key/UI placement и исправлен контракт absent-id с `false` на `null`, чтобы сохранить `DefaultIsExpanded`; добавлено обязательное throttled batch сохранение с forced flush.
- Что осталось на решение пользователя: только подтверждение перехода в EXEC.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Контекст и проектирование | 0.86 | Нет блокирующих данных | Запросить подтверждение спеки | Да | Нет | Central QUEST требует SPEC-first и фразу подтверждения перед изменением кода | `specs/2026-05-14-task-expansion-state-persistence.md` |
| SPEC | Уточнение дизайна | 0.9 | Нет блокирующих данных | Запросить подтверждение обновлённой спеки | Да | Да: пользователь предложил хранить список развёрнутых задач вместо словаря | Список expanded ids проще и достаточен, потому что collapsed является default | `specs/2026-05-14-task-expansion-state-persistence.md` |
| SPEC | Уточнение runtime-модели | 0.88 | Нет блокирующих данных | Запросить подтверждение обновлённой спеки | Да | Да: пользователь предложил заменить in-memory словари на sets | Runtime sets совпадают с persisted контрактом и уменьшают лишнее состояние | `specs/2026-05-14-task-expansion-state-persistence.md` |
| SPEC | Ревью спеки | 0.9 | Нет блокирующих данных | Запросить подтверждение обновлённой спеки | Да | Да: пользователь запросил ревью спеки | Убраны недоопределённые решения и сохранён fallback `DefaultIsExpanded` | `specs/2026-05-14-task-expansion-state-persistence.md` |
| SPEC | Уточнение записи на диск | 0.9 | Нет блокирующих данных | Запросить подтверждение обновлённой спеки | Да | Да: пользователь потребовал throttling и batch save | Batch save предотвращает запись sidecar json на каждую задачу при массовых командах | `specs/2026-05-14-task-expansion-state-persistence.md` |
| EXEC | Старт реализации | 0.88 | Нет блокирующих данных | Внести store, wiring, UI и тесты | Нет | Да: пользователь подтвердил спеки фразой `Спеку подтверждаю` | Переход в EXEC разрешён central QUEST-гейтом | `specs/2026-05-14-task-expansion-state-persistence.md` |
| EXEC | Реализация store и UI wiring | 0.82 | Нужна компиляционная проверка | Добавить тесты и запустить targeted suite | Нет | Нет | Добавлен sidecar store с throttled batch write, подключён к деревьям, добавлена настройка Settings UI | `src/Unlimotion.ViewModel/TaskTreeExpansionStateStore.cs`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.ViewModel/SettingsViewModel.cs`, `src/Unlimotion/Views/SettingsControl.axaml`, resources, app/test host/fixture |
| EXEC | Добавление тестов | 0.82 | Нужен запуск тестов | Запустить targeted tests | Нет | Нет | Добавлены проверки config setting, UI checkbox, throttled batch write, disabled sidecar и restore из sidecar | `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs`, `src/Unlimotion.Test/MainWindowViewModelTests.cs` |
| EXEC | Валидация | 0.78 | Полный solution build/full test заблокированы окружением/таймаутом | Выполнить post-EXEC review | Нет | Нет | Targeted тесты и build test project прошли; solution build упал на missing `wasm-tools`, полный `Unlimotion.Test` не завершился за 15 минут | test commands, `src/Unlimotion.Test/Unlimotion.Test.csproj` |
| EXEC | Post-EXEC fix | 0.84 | Нужен повторный build после тестовой правки | Повторить build test project и завершить review | Нет | Нет | Добавлен недостающий тест forced flush при dispose для pending batch write; `SettingsViewModelTests` повторно прошёл 56/56 | `src/Unlimotion.Test/SettingsViewModelTests.cs` |
| EXEC | Финальная проверка | 0.82 | Нет блокирующих данных | Завершить задачу | Нет | Нет | Повторный `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj` прошёл; зависшие процессы полного прогона закрыты | `src/Unlimotion.Test/Unlimotion.Test.csproj` |
| EXEC | Post-EXEC review | 0.84 | Нет блокирующих данных | Финальный отчёт пользователю | Нет | Нет | Проверены отклонения от спеки, UI coverage, batch/flush coverage и остаточные validation ограничения | изменённые файлы и результаты проверок |
| EXEC | Исправления по review | 0.86 | Нет блокирующих данных | Финальный отчёт пользователю | Нет | Да: пользователь попросил внести изменения после review | Pending write теперь отменяется при выключении настройки, failed write не очищает dirty state, test fixture изолирует sidecar рядом с уникальным config | `src/Unlimotion.ViewModel/TaskTreeExpansionStateStore.cs`, `src/Unlimotion.ViewModel/MainWindowViewModel.cs`, `src/Unlimotion.Test/MainWindowViewModelFixture.cs`, tests |
